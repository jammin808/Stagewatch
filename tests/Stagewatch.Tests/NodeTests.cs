using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>
/// One node key speaks for one node id (Show Bus §11): the bindings, the contexts and the approval desk on them. Pure, on
/// injected time.
/// </summary>
static class NodeTests
{
    static readonly DateTime T0 = new(2026, 10, 25, 19, 0, 0, DateTimeKind.Utc);

    static string Context(string kind, string id, string name = "", string commands = "")
        => $"{{\"v\":1,\"node\":{{\"t\":\"{kind}\",\"id\":\"{id}\",\"name\":\"{name}\"}},\"light\":\"green\",\"headline\":\"{id} here\"" +
           (commands.Length > 0 ? $",\"commands\":[{commands}]" : "") + "}";

    public static void Run(Action<bool, string> Check)
    {
        Bindings(Check);
        Contexts(Check);
        Desk(Check);
        Kinds(Check);
        Proofs(Check);
        Room(Check);
        Refusals(Check);
        Keys(Check);
        Gate(Check);
        BadRequests(Check);
        Links(Check);
        Rechecks(Check);
        Targets(Check);
    }

    /// <summary>
    /// The server's gate (Check): Speak's answer on what a request shows, binding nothing, so the store that takes the
    /// request is the one that binds, and a request it refuses binds no key.
    /// </summary>
    static void Gate(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var bound = 0;
        nodes.Bound += _ => bound++;
        var kP = NodeCredential.Key("kP");
        Check(nodes.Check(kP, "patterns", "pat1", T0) == NodeVerdict.Allowed(NodeProof.Key) && nodes.NodeOf("kP") is null && nodes.KeyOf("pat1") is null && bound == 0,
              "gate: an unbound key for a free id is let through with its key's proof, and stays unbound");
        Check(nodes.Check(kP, "messenger", "ag1", T0).Ok && nodes.NodeOf("kP") is null,
              "gate: checking never binds, so the same unbound key passes for another id until a store takes one");
        Check(nodes.Check(NodeCredential.Operator, "patterns", "pat1", T0) == NodeVerdict.Allowed(NodeProof.Operator)
              && nodes.Check(NodeCredential.None, "patterns", "pat1", T0) == NodeVerdict.Allowed(NodeProof.None),
              "gate: the operator and no credential pass for a free id with their own proof");

        nodes.Speak(kP, "patterns", "pat1", T0);
        Check(bound == 1 && nodes.Check(kP, "patterns", "pat1", T0) == NodeVerdict.Allowed(NodeProof.Key) && nodes.Check(kP, "", "pat1", T0).Ok,
              "gate: once bound, the key passes for its own node (by kind and id, or the id alone)");
        var other = nodes.Check(kP, "messenger", "ag1", T0.AddSeconds(1));
        Check(!other.Ok && other.Error == "this key speaks for patterns pat1 — make a key for messenger ag1" && nodes.LastRefused("kP") is { Id: "ag1", Kind: "messenger" }
              && nodes.NodeOf("kP") == ("patterns", "pat1"),
              $"gate: a bound key is refused for another node with Speak's words, remembered for the operator ({other.Error})");
        Check(nodes.Check(NodeCredential.Key("kQ"), "patterns", "pat1", T0) is { Ok: false, Error: "patterns pat1 speaks with its own key" } && nodes.NodeOf("kQ") is null
              && nodes.Check(NodeCredential.Operator, "patterns", "pat1", T0) is { Ok: false, Error: "patterns pat1 speaks with its own key" }
              && nodes.Check(NodeCredential.None, "", "pat1", T0) is { Ok: false, Error: "patterns pat1 speaks with its own key" },
              "gate: another key, the operator and no credential are refused for a key-bound id");
        Check(nodes.Check(kP, "patterns", "bad id", T0) is { Ok: false, Error: "a node id is 1-40 letters, digits, - or _" } && bound == 1,
              "gate: a malformed id is refused, nothing bound");

        // A key bound by a collect (the id alone) learns its kind only from a store that takes its words.
        nodes.Speak(NodeCredential.Key("kC"), "", "pm1", T0);
        Check(nodes.Check(NodeCredential.Key("kC"), "playmap", "pm1", T0).Ok && nodes.NodeOf("kC") == ("", "pm1") && bound == 2,
              "gate: checking a kind for a key bound by the id alone names no kind");
        nodes.Keep(new[] { NodeKey("kP", "patterns", "pat1") });
        nodes.Keep(Array.Empty<ApiKey>());
        Check(nodes.Check(kP, "patterns", "pat1", T0) is { Ok: false, Error: "unknown or revoked API key" }, "gate: a revoked key is refused");

        Check(NodeBindings.Word(NodeProof.Key) == "key" && NodeBindings.Word(NodeProof.Operator) == "operator" && NodeBindings.Word(NodeProof.None) == "none",
              "gate: a proof's word on the wire");

        // The server reads a proposal's from exactly as the desk does, with the desk's words when it names none.
        Check(ProposalDesk.FromOf("{\"from\":{\"t\":\"patterns\",\"id\":\" pat1 \",\"name\":\"FOH\"},\"line\":\"x\"}") == (new NodeRef("patterns", "pat1", "FOH"), null)
              && ProposalDesk.FromOf("{\"from\":{\"t\":\"patterns\"}}") == (null, ProposalDesk.NoFrom) && ProposalDesk.FromOf("{\"from\":{\"t\":\"bad kind\",\"id\":\"p1\"}}") == (null, ProposalDesk.NoFrom)
              && ProposalDesk.FromOf("{\"from\":{\"t\":\"patterns\",\"id\":7}}") == (null, ProposalDesk.NoFrom)
              && ProposalDesk.FromOf("not json") == (null, ProposalDesk.NotAProposal) && ProposalDesk.FromOf("[1]") == (null, ProposalDesk.NotAProposal),
              "gate: a proposal's from is read as the desk reads it (trimmed), and the desk's words when it names no node");

        // A repeated key, at any depth, makes a body unreadable: never a lookup that throws, never two readings of one key.
        var twoFroms = "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"from\":{\"t\":\"patterns\",\"id\":\"pat2\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}";
        var twoIds = "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\",\"id\":\"pat2\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}";
        var desk = new ProposalDesk(() => "desk01", nodes);
        var boundBefore = bound;
        Check(ProposalDesk.FromOf(twoFroms) == (null, ProposalDesk.NotAProposal) && ProposalDesk.FromOf(twoIds) == (null, ProposalDesk.NotAProposal)
              && desk.Submit(twoFroms, NodeCredential.Key("kD"), LowRisk, T0) == (null, ProposalDesk.NotAProposal, false)
              && desk.Submit(twoIds, NodeCredential.Key("kD"), LowRisk, T0) == (null, ProposalDesk.NotAProposal, false)
              && desk.All().Count == 0 && nodes.NodeOf("kD") is null && bound == boundBefore,
              "gate: a proposal that repeats a key (its from, or an id inside it) is not a proposal: refused with words, nothing queued or bound");
        Check(PeerContexts.Parse("{\"v\":1,\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\"}}", "", T0) is null
              && PeerContexts.Parse("{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\",\"id\":\"pat8\"}}", "", T0) is null
              && PeerContexts.Parse("{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\"},\"items\":[{\"id\":\"a\",\"id\":\"b\"}]}", "", T0) is null
              && PeerContexts.Parse("{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\"},\"items\":[{\"id\":\"a\"}]}", "", T0) is { Id: "pat9" },
              "gate: a context that repeats a key at any depth is not a context; the same context without the repeat is");
        Check(new PeerContexts(nodes).Accept("{\"v\":1,\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\"}}", "", T0, null, NodeCredential.Key("kD"))
                  == (400, PeerContexts.NotAContext, NodeProof.None) && nodes.NodeOf("kD") is null,
              "gate: the store refuses a repeated key with the same words, and binds nothing");
    }

    static string LowRisk(NodeRef target, string line) => "low";

    /// <summary>A proposer's word for a target's kind never refuses the target's own key, nor names the kind it binds.</summary>
    static void Kinds(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = new ProposalDesk(() => "desk01", nodes);
        var peers = new PeerContexts(nodes);
        var keyP = NodeCredential.Key("kP");
        var keyA = NodeCredential.Key("kA");
        Check(peers.Accept(Context("patterns", "pat1"), "10.0.0.5", T0, "desk01", keyP).Status == 200 && nodes.NodeOf("kP") == ("patterns", "pat1"),
              "kinds: Patterns' key speaks for its own node");
        var typo = desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"Patterns\",\"id\":\"pat1\"},\"line\":\"EYE FOCUS screen 2\"}", keyA, LowRisk, T0).Proposal!;
        var asMain = desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"main\",\"id\":\"pat1\"},\"line\":\"EYE RESET\"}", keyA, LowRisk, T0).Proposal!;
        desk.Approve(typo.Id, "operator", T0);
        desk.Approve(asMain.Id, "operator", T0);
        var got = desk.Collect("pat1", keyP, T0.AddSeconds(1));
        Check(got.Error is null && got.Items is { Count: 2 } && nodes.NodeOf("kP") == ("patterns", "pat1") && nodes.LastRefused("kP") is null,
              $"kinds: another node's proposals naming pat1 'Patterns' or 'main' neither block pat1's key from collecting nor change its kind ({got.Error})");
        Check(desk.Result(typo.Id, true, "focused", keyP, T0.AddSeconds(2)) is null && desk.Result(asMain.Id, false, "refused", keyP, T0.AddSeconds(2)) is null
              && nodes.LastRefused("kP") is null && nodes.Refusals().Count == 0,
              "kinds: its key reports both results, and nothing is recorded as refused");

        // A key whose first use is a collect binds the id alone; its own context then names the kind.
        var keyQ = NodeCredential.Key("kQ");
        var poison = desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"ag9\"},\"target\":{\"t\":\"main\",\"id\":\"pm1\"},\"line\":\"note x\"}", NodeCredential.Operator, LowRisk, T0).Proposal!;
        desk.Approve(poison.Id, "operator", T0);
        Check(desk.Collect("pm1", keyQ, T0).Items is [{ Line: "note x" }] && nodes.NodeOf("kQ") == ("", "pm1"),
              "kinds: a key that first collects binds the id alone, not the kind the operator's proposal wrote");
        Check(peers.Accept(Context("playmap", "pm1"), "10.0.0.9", T0.AddSeconds(1), "desk01", keyQ) is (200, null, NodeProof.Key) && nodes.NodeOf("kQ") == ("playmap", "pm1"),
              "kinds: its own context is taken and fills the kind");
    }

    /// <summary>A held context proves only what its poster could still prove: a revoked or moved key nothing, at once.</summary>
    static void Proofs(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        var line = "EYE FOCUS screen 2";
        var low = "{\"verb\":\"EYE FOCUS <thing>\",\"risk\":\"low\"}";
        var keys = new[] { NodeKey("kK", null, null), NodeKey("kL", null, null), NodeKey("kM", null, null), NodeKey("kN", null, null) };
        nodes.Keep(keys);
        Check(peers.Accept(Context("patterns", "pat1", "FOH", low), "10.0.0.5", T0, "desk01", NodeCredential.Key("kK")) is (200, null, NodeProof.Key)
              && peers.Find("pat1", T0) is { KeyId: "kK" } && peers.RiskFor("pat1", line, T0) == "low",
              "proofs: a node key's context keeps the key it came with and sets the risk");
        nodes.Keep(keys.Where(k => k.Id != "kK"));
        var held = peers.Find("pat1", T0.AddSeconds(5));
        Check(held is { Proof: NodeProof.Key, KeyId: "kK" } && peers.ProofOf(held) == NodeProof.None && peers.RiskFor("pat1", line, T0.AddSeconds(5)) == "high",
              "proofs: once its key is revoked its context proves nothing at once, and the risk is high");
        Check(peers.Accept(Context("patterns", "pat1", "Operator's", low), "10.0.0.2", T0.AddSeconds(5), "desk01", NodeCredential.Operator) is (200, null, NodeProof.Operator)
              && peers.Find("pat1", T0.AddSeconds(5)) is { Name: "Operator's", Proof: NodeProof.Operator, KeyId: null } && peers.RiskFor("pat1", line, T0.AddSeconds(5)) == "low",
              "proofs: an operator post then replaces it (a revoked key's proof is no longer the stronger)");

        Check(peers.Accept(Context("messenger", "ag1", "AGM", "{\"verb\":\"note <text>\",\"risk\":\"low\"}"), "10.0.0.7", T0, "desk01", NodeCredential.Key("kL")).Status == 200
              && peers.RiskFor("ag1", "note hi", T0) == "low", "proofs: another node's key posts its own");
        nodes.Rebind("kM", "messenger", "ag1");
        Check(peers.ProofOf(peers.Find("ag1", T0)!) == NodeProof.None && peers.RiskFor("ag1", "note hi", T0) == "high",
              "proofs: when the operator moves the id to another key, the old key's context proves nothing");
        Check(nodes.Speak(NodeCredential.Key("kL"), "patterns", "pat1", T0.AddSeconds(6)).Ok
              && peers.ProofOf(peers.Find("pat1", T0.AddSeconds(6))!) == NodeProof.None && peers.RiskFor("pat1", line, T0.AddSeconds(6)) == "high",
              "proofs: the operator's context proves nothing once a key speaks for that node (it speaks with its key alone)");

        // A context pulled over this desk's own link: it counts for its own id while no key speaks for that id.
        var linked = Context("patterns", "pat9", "Linked", low);
        Check(peers.RiskFor("pat9", line, T0, linked) == "low" && peers.RiskFor("pat8", line, T0, linked) == "high",
              "proofs: the linked context sets the risk for its own id only");
        nodes.Speak(NodeCredential.Key("kN"), "patterns", "pat9", T0);
        Check(peers.RiskFor("pat9", line, T0, linked) == "high", "proofs: the linked context counts for nothing once a key speaks for that id");
        peers.Accept(Context("patterns", "pat9", "Own", "{\"verb\":\"EYE FOCUS <thing>\",\"risk\":\"review\"}"), "10.0.0.9", T0, "desk01", NodeCredential.Key("kN"));
        Check(peers.RiskFor("pat9", line, T0, linked) == "review", "proofs: the node's own key's context is what sets the risk");
    }

    /// <summary>A request refused for want of room binds no key.</summary>
    static void Room(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = new ProposalDesk(() => "desk01", nodes);
        for (var i = 0; i < ProposalDesk.MaxOpen; i++)
        {
            var n = i.ToString(CultureInfo.InvariantCulture);
            desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"op" + n + "\"},\"target\":{\"t\":\"main\"},\"line\":\"note " + n + "\"}", NodeCredential.Operator, LowRisk, T0);
        }
        var late = desk.Submit("{\"from\":{\"t\":\"patterns\",\"id\":\"late1\"},\"target\":{\"t\":\"main\"},\"line\":\"note late\"}", NodeCredential.Key("kNew"), LowRisk, T0);
        Check(desk.All().Count == ProposalDesk.MaxOpen && late is (null, "too many proposals waiting", false) && nodes.NodeOf("kNew") is null && nodes.KeyOf("late1") is null,
              $"room: a full desk refuses a new key's proposal, and the key stays unbound ({late.Error})");
    }

    /// <summary>What the desk's Show network reads: the newest refusal per node, and the kind a key made for it is saved with.</summary>
    static void Refusals(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        foreach (var (key, kind, id) in new[] { ("kS", "patterns", "pat1"), ("kU", "playmap", "pm7"), ("kV", "messenger", "ag5"), ("kW", "messenger", "ag6"), ("kX", "playmap", "pm8") })
            nodes.Speak(NodeCredential.Key(key), kind, id, T0);
        nodes.Speak(NodeCredential.Key("kW"), "messenger", "ag3", T0);                    // long ago
        nodes.Speak(NodeCredential.Key("kS"), "messenger", "ag2", T0.AddSeconds(11));     // AGMessage pasted Patterns' key
        nodes.Speak(NodeCredential.Key("kV"), "main", "desk01", T0.AddSeconds(12));       // this Stagewatch's own id
        nodes.Speak(NodeCredential.Key("kU"), "", "ag2", T0.AddSeconds(13));              // a collect names the id alone
        nodes.Speak(NodeCredential.Key("kX"), "", "pm9", T0.AddSeconds(14));
        var shown = nodes.RefusedSince(T0.AddSeconds(10), "desk01");
        Check(shown is [{ Id: "pm9", KeyId: "kX" }, { Id: "ag2", KeyId: "kU", Kind: "" }],
              $"refusals: the newest per node since then, newest first, never this Stagewatch's own id ({string.Join(",", shown.Select(r => r.KeyId + ":" + r.Id))})");
        Check(nodes.KindToMakeFor("ag2") == "messenger" && nodes.KindToMakeFor("pm9") is null && nodes.KindToMakeFor("nobody") is null,
              "refusals: a key made for a node takes the kind its own refused request named, or none until it names one");
        nodes.Keep(new[] { NodeKey("kS", "patterns", "pat1"), NodeKey("kU", "playmap", "pm7"), NodeKey("kV", "messenger", "ag5"), NodeKey("kW", "messenger", "ag6"),
                           NodeKey("kX", "playmap", "pm8"), NodeKey("kY", "messenger", "pat1") });
        Check(nodes.KeyOf("pat1") == "kS" && nodes.NodeOf("kY") is null,
              "refusals: a key saved for an id another key already speaks for is not bound (the desk then removes it unseen)");
    }

    static void Bindings(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var bound = new List<NodeBound>();
        nodes.Bound += b => bound.Add(b);
        var k1 = NodeCredential.Key("k1");
        var k2 = NodeCredential.Key("k2");

        // First proven use binds.
        var v = nodes.Speak(k1, "patterns", "pat1", T0);
        Check(v is { Ok: true, Proof: NodeProof.Key, Error: null } && nodes.NodeOf("k1") == ("patterns", "pat1") && nodes.KeyOf("pat1") == "k1"
              && bound is [{ KeyId: "k1", Kind: "patterns", Id: "pat1", PreviousId: null, TakenFromKeyId: null, ByOperator: false }],
              "nodes: a key bound to nothing is bound by its first proven use");
        Check(nodes.Speak(k1, "patterns", "pat1", T0).Ok && nodes.Speak(k1, "", "pat1", T0).Ok && bound.Count == 1, "nodes: Bound is raised once per pair; a use naming only the id matches");

        // The same key for another id, and another key for a bound id: both refused, with words.
        v = nodes.Speak(k1, "messenger", "ag2", T0.AddSeconds(1));
        Check(v is { Ok: false, Proof: NodeProof.None } && v.Error == "this key speaks for patterns pat1 — make a key for messenger ag2" && nodes.KeyOf("ag2") is null,
              $"nodes: a bound key is refused for another node, with words ({v.Error})");
        Check(nodes.LastRefused("k1") is { Kind: "messenger", Id: "ag2" } r && r.AtUtc == T0.AddSeconds(1) && nodes.Refusals() is [{ KeyId: "k1" }],
              "nodes: the refusal is remembered for the operator (which node, when)");
        Check(nodes.Speak(k1, "messenger", "pat1", T0).Error == "this key speaks for patterns pat1 — make a key for messenger pat1",
              "nodes: the same id under another kind is another node");
        v = nodes.Speak(k2, "patterns", "pat1", T0);
        Check(!v.Ok && v.Error == "patterns pat1 speaks with its own key" && nodes.NodeOf("k2") is null, "nodes: another key is refused for a bound id and stays unbound");

        // The operator's credential and none: refused for a key-bound id, allowed for a free one with their own proof.
        Check(nodes.Speak(NodeCredential.Operator, "patterns", "pat1", T0).Error == "patterns pat1 speaks with its own key"
              && nodes.Speak(NodeCredential.None, "patterns", "pat1", T0).Error == "patterns pat1 speaks with its own key",
              "nodes: the PIN, a control key and no credential are refused for a key-bound id");
        Check(nodes.Speak(NodeCredential.Operator, "messenger", "ag9", T0) is { Ok: true, Proof: NodeProof.Operator }
              && nodes.Speak(default, "messenger", "ag9", T0) is { Ok: true, Proof: NodeProof.None } && nodes.KeyOf("ag9") is null,
              "nodes: a free id may be spoken for by the operator or by nobody, and nothing binds");
        Check(NodeCredential.Key("") == NodeCredential.None && default(NodeCredential).Proof == NodeProof.None, "nodes: a credential nobody set proves nothing");
        Check(!nodes.Speak(k2, "patterns", "bad id", T0).Ok && !nodes.Speak(k2, "bad kind!", "ok1", T0).Ok && nodes.NodeOf("k2") is null,
              "nodes: a malformed id or kind binds nothing");

        // Only the operator moves a binding.
        bound.Clear();
        var (moved, error) = nodes.Rebind("k2", "patterns", "pat1");
        Check(error is null && moved is { KeyId: "k2", Id: "pat1", TakenFromKeyId: "k1", PreviousId: null, ByOperator: true } && bound.Count == 1
              && nodes.KeyOf("pat1") == "k2" && nodes.NodeOf("k1") is null,
              "nodes: the operator lets a key speak for a node; the id moves off the key that had it, and says which");
        Check(nodes.Speak(k2, "patterns", "pat1", T0).Ok && nodes.Speak(k1, "patterns", "pat1", T0).Error == "patterns pat1 speaks with its own key",
              "nodes: after the move the old key is refused for that id");
        Check(nodes.Speak(k1, "messenger", "ag2", T0).Ok && nodes.NodeOf("k1") == ("messenger", "ag2"), "nodes: the freed key binds again on its next first use");
        (moved, _) = nodes.Rebind("k2", "patterns", "pat2");
        Check(moved is { PreviousKind: "patterns", PreviousId: "pat1", TakenFromKeyId: null } && nodes.KeyOf("pat1") is null && nodes.KeyOf("pat2") == "k2",
              "nodes: rebinding a key frees the id it spoke for before");
        Check(nodes.Rebind("k2", "patterns", "pat2").Error is not null && nodes.Rebind("k2", "patterns", "no way").Error is not null,
              "nodes: a rebind that changes nothing, or names no valid node, is refused");

        // A use that names only the id binds it; the first kind named later fills it in.
        bound.Clear();
        Check(nodes.Speak(NodeCredential.Key("k3"), "", "pm1", T0).Ok && nodes.NodeOf("k3") == ("", "pm1"), "nodes: a collect binds the id before any kind is named");
        Check(nodes.Speak(NodeCredential.Key("k3"), "playmap", "pm1", T0).Ok && nodes.NodeOf("k3") == ("playmap", "pm1")
              && bound is [{ KeyId: "k3", Kind: "", Id: "pm1", PreviousId: null }, { KeyId: "k3", Kind: "playmap", PreviousKind: "", PreviousId: "pm1" }]
              && nodes.Speak(NodeCredential.Key("k3"), "messenger", "pm1", T0).Error!.StartsWith("this key speaks for playmap pm1", StringComparison.Ordinal),
              "nodes: the first kind named fills the binding, and then it holds");

        // Keep: the keys in force. A revoked key is forgotten with its binding, and refused from then on.
        var keys = new[] { NodeKey("k1", "messenger", "ag2"), NodeKey("k2", "patterns", "pat2"), NodeKey("k3", "playmap", "pm1") };
        nodes.Keep(keys);
        nodes.Keep(keys.Where(k => k.Id != "k2"));
        Check(nodes.NodeOf("k2") is null && nodes.KeyOf("pat2") is null && nodes.Speak(k2, "patterns", "pat2", T0).Error == "unknown or revoked API key"
              && nodes.KeyOf("pat2") is null && nodes.Rebind("k2", "patterns", "pat2").Error is not null,
              "nodes: a revoked key speaks for nothing and can't bind again (a request already past the key check)");
        Check(nodes.Speak(NodeCredential.Operator, "patterns", "pat2", T0).Ok, "nodes: its id is free again");

        // Pairs saved on keys survive a restart; a key not yet bound binds on its first use as before.
        var restarted = new NodeBindings(new[] { NodeKey("k1", "messenger", "ag2"), NodeKey("k3", "playmap", "pm1"), NodeKey("k4", null, null), NodeKey("k5", "patterns", "ag2") });
        Check(restarted.NodeOf("k1") == ("messenger", "ag2") && restarted.NodeOf("k3") == ("playmap", "pm1") && restarted.NodeOf("k5") is null && restarted.NodeOf("k4") is null,
              "nodes: pairs saved on the keys are put back after a restart (one key per id)");
        Check(restarted.Speak(NodeCredential.Key("k1"), "patterns", "pat1", T0).Error!.StartsWith("this key speaks for messenger ag2", StringComparison.Ordinal)
              && restarted.Speak(NodeCredential.Key("k4"), "patterns", "pat1", T0).Ok,
              "nodes: after a restart a saved pair still refuses another node, and an unbound key binds on first use");

        // The fail-closed default: one instance per owner, first-bind-wins.
        var owner1 = new object();
        var owner2 = new object();
        Check(ReferenceEquals(NodeBindings.For(owner1), NodeBindings.For(owner1)) && !ReferenceEquals(NodeBindings.For(owner1), NodeBindings.For(owner2)),
              "nodes: a surface with no bindings of its own gets one set, its own");
        NodeBindings.For(owner1).Speak(k1, "patterns", "pat1", T0);
        Check(!NodeBindings.For(owner1).Speak(k1, "messenger", "ag1", T0).Ok && NodeBindings.For(owner2).Speak(k1, "messenger", "ag1", T0).Ok,
              "nodes: the default still lets one key speak for one node only");
        IShowAccess bare = new BareAccess();
        Check(ReferenceEquals(bare.Nodes, bare.Nodes) && bare.Nodes.Speak(k1, "patterns", "pat1", T0).Ok && !bare.Nodes.Speak(k1, "patterns", "pat9", T0).Ok,
              "nodes: a surface's default bindings are kept between requests");
    }

    sealed class BareAccess : IShowAccess { }

    static ApiKey NodeKey(string id, string? kind, string? node)
        => new() { Id = id, Name = "key " + id, Scope = ApiKeyScope.Node, Hash = new string('a', 64), Node = node, NodeKind = kind };

    static void Contexts(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        var keyA = NodeCredential.Key("kA");
        var keyP = NodeCredential.Key("kP");
        var pat = Context("patterns", "pat1", "Patterns FOH", "{\"verb\":\"EYE FOCUS <thing>\",\"risk\":\"low\"}");

        // G15: a node key speaks only for its own node, checked before anything is stored.
        Check(peers.Accept(Context("messenger", "ag1"), "10.0.0.7", T0, "own1", keyA) is (200, null, NodeProof.Key), "contexts: a node key posts its own context");
        Check(peers.Accept(pat, "10.0.0.5", T0, "own1", keyP) is (200, null, NodeProof.Key), "contexts: another node posts its own with its own key");
        var forged = peers.Accept(Context("patterns", "pat1", "Patterns FOH", "{\"verb\":\"VIDEO END <n>\",\"risk\":\"low\"}"), "10.0.0.7", T0.AddSeconds(1), "own1", keyA);
        Check(forged is (403, "this key speaks for messenger ag1 — make a key for patterns pat1", NodeProof.None)
              && peers.Find("pat1", T0.AddSeconds(1)) is { Address: "10.0.0.5", AtUtc: var at } && at == T0
              && peers.RiskFor("pat1", "VIDEO END 10", T0.AddSeconds(1)) == "high",
              "contexts: key A posting for Patterns is refused (403) and Patterns' entry is unchanged");
        Check(peers.Accept(pat, "10.0.0.9", T0, "own1", NodeCredential.None).Status == 403 && peers.Accept(pat, "10.0.0.9", T0, "own1", NodeCredential.Operator).Status == 403,
              "contexts: an open post or the PIN for a key-bound id is refused");
        Check(peers.Accept(Context("bad kind!", "x1"), "x", T0, "own1", NodeCredential.None).Status == 400 && nodes.KeyOf("x1") is null,
              "contexts: a context whose kind Show Bus can't name is no context");
        var crowd = new NodeBindings();
        var crowded = new PeerContexts(crowd);
        for (var i = 0; i < PeerContexts.MaxPeers; i++) crowded.Accept(Context("messenger", "n" + i.ToString(CultureInfo.InvariantCulture)), "x", T0, "own1", NodeCredential.Operator);
        Check(crowded.Accept(Context("patterns", "late1"), "x", T0, "own1", NodeCredential.Key("kLate")) is (400, "too many peers", _) && crowd.NodeOf("kLate") is null
              && crowded.All(T0).Count == PeerContexts.MaxPeers, "contexts: a context there is no room for is refused and binds no key");

        // Unverified contexts are held but never set risk; the node's own key's context does.
        var line = "EYE FOCUS screen 2";
        var open = Context("patterns", "pat7", "Open Patterns", "{\"verb\":\"EYE FOCUS <thing>\",\"risk\":\"low\"}");
        Check(peers.Accept(open, "10.0.0.8", T0, "own1", NodeCredential.None) is (200, null, NodeProof.None)
              && peers.Find("pat7", T0) is { Proof: NodeProof.None } e7 && peers.ProofOf(e7) == NodeProof.None && peers.RiskFor("pat7", line, T0) == "high",
              "contexts: an open post for a free id is held unverified, and its 'low' never lowers a risk");
        Check(peers.RiskFor("pat1", line, T0) == "low" && peers.RiskFor("nobody1", line, T0) == "high",
              "contexts: the node's own context sets the risk; no context is high");

        // A weaker proof never replaces a stronger one within the time a context is kept.
        var asOperator = Context("messenger", "ag5", "AGM", "{\"verb\":\"note <text>\",\"risk\":\"low\"}");
        Check(peers.Accept(asOperator, "10.0.0.3", T0, "own1", NodeCredential.Operator) is (200, null, NodeProof.Operator), "contexts: the operator's credential posts for a free id");
        var weaker = peers.Accept(Context("messenger", "ag5", "Not AGM"), "10.0.0.66", T0.AddSeconds(12), "own1", NodeCredential.None);
        Check(weaker is (403, "messenger ag5 spoke with a stronger proof 12 s ago", NodeProof.None) && peers.Find("ag5", T0.AddSeconds(12)) is { Name: "AGM", Proof: NodeProof.Operator },
              $"contexts: an open post can't replace an operator-proof one within 30 s ({weaker.Error})");
        Check(peers.Accept(Context("messenger", "ag5", "AGM again"), "10.0.0.3", T0.AddSeconds(13), "own1", NodeCredential.Operator).Status == 200,
              "contexts: an equal proof replaces it");
        Check(peers.Accept(Context("messenger", "ag5", "Later"), "10.0.0.66", T0.AddSeconds(44), "own1", NodeCredential.None) is (200, null, NodeProof.None),
              "contexts: after 30 s unheard, the id is open again (and the new context is unverified)");
        Check(peers.Accept(Context("messenger", "ag5", "Its key"), "10.0.0.3", T0.AddSeconds(45), "own1", NodeCredential.Key("k5")) is (200, null, NodeProof.Key)
              && nodes.KeyOf("ag5") == "k5", "contexts: a node key replaces a weaker proof and binds the id");
    }

    static void Desk(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = new ProposalDesk(() => "desk01", nodes);
        var keyP = NodeCredential.Key("kP");
        var keyA = NodeCredential.Key("kA");
        static string Risk(NodeRef t, string l) => "low";
        var proposal = "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"EYE FOCUS screen 2\"}";

        Check(desk.Submit(proposal, keyP, Risk, T0) is ({ State: SharedProposalState.Pending }, null, false) && nodes.KeyOf("pat1") == "kP",
              "desk: a proposal's from binds its key on first use");
        Check(desk.Submit(proposal, NodeCredential.Operator, Risk, T0) is (null, "patterns pat1 speaks with its own key", true)
              && desk.Submit(proposal, NodeCredential.None, Risk, T0).Forbidden && desk.All().Count == 1,
              "desk: the PIN, a control key or nothing can't propose from a key-bound node (forbidden), and nothing is queued");
        Check(desk.Submit("{\"from\":{\"t\":\"main\",\"id\":\"desk01\"},\"target\":{\"t\":\"main\"},\"line\":\"next\"}", keyA, Risk, T0) is (null, not null, true) && nodes.NodeOf("kA") is null,
              "desk: nobody on the network proposes as this Stagewatch, and no key binds to its id");
        Check(desk.Submit("{\"from\":{\"t\":\"patterns\"},\"target\":{\"t\":\"main\"},\"line\":\"next\"}", keyA, Risk, T0) is (null, not null, false)
              && desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"patterns\"},\"line\":\"next\"}", keyA, Risk, T0) is (null, not null, false)
              && nodes.NodeOf("kA") is null,
              "desk: a malformed proposal is a bad request, not a forbidden one, and binds no key");

        var id = desk.All()[0].Id;
        Check(desk.Result(id, true, "early", NodeCredential.Key("kZ"), T0) == $"proposal {id} is pending" && nodes.NodeOf("kZ") is null,
              "desk: a result that can't be taken yet binds no key");
        desk.Approve(id, "operator", T0);
        Check(desk.Collect("pat1", NodeCredential.Operator, T0).Error == "patterns pat1 speaks with its own key"
              && desk.Collect("pat1", keyA, T0).Error == "patterns pat1 speaks with its own key" && nodes.NodeOf("kA") is null,
              "desk: a control key, or another node's key, can't collect a key-bound node's approvals");
        Check(desk.Collect("desk01", keyA, T0).Error is not null && nodes.NodeOf("kA") is null, "desk: nobody collects for this Stagewatch");
        Check(desk.Collect("pat1", keyP, T0).Items is [{ Line: "EYE FOCUS screen 2" }], "desk: the node's own key collects");
        Check(desk.Result(id, true, "done", NodeCredential.Operator, T0) == "patterns pat1 speaks with its own key"
              && desk.Result(id, true, "done", keyA, T0) == "patterns pat1 speaks with its own key" && desk.Get(id) is { State: SharedProposalState.Approved },
              "desk: only the target's own key reports its result");
        Check(desk.Result(id, true, "focused", keyP, T0) is null && desk.Get(id) is { State: SharedProposalState.Done, Result: "focused" }, "desk: the target's key reports back");

        // A free node may still be spoken for by the operator; a collect binds an unbound key to the id alone.
        var free = "{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"line\":\"note hi\"}";
        Check(desk.Submit(free, NodeCredential.Operator, Risk, T0) is ({ }, null, false), "desk: the operator proposes for a node that has no key");
        Check(desk.Collect("ag1", keyA, T0).Items is not null && nodes.NodeOf("kA") == ("", "ag1"),
              "desk: a key's first collect binds it to the id alone, never to the kind a proposer wrote for it");
        var toDesk = desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"main\"},\"line\":\"note hi\"}", keyA, Risk, T0).Proposal!;
        desk.Approve(toDesk.Id, "operator", T0);
        Check(desk.Result(toDesk.Id, true, "done", keyA, T0) is not null && desk.Get(toDesk.Id) is { State: SharedProposalState.Approved },
              "desk: what this Stagewatch runs, it records itself — no node reports for it");
    }

    static void Keys(Action<bool, string> Check)
    {
        // node/nodeKind ride on node keys only, appended, and only with an id Show Bus can name.
        var (key, _) = ApiKeys.Create("Patterns FOH", ApiKeyScope.Node, T0);
        key.Node = "9f8e7d6c";
        key.NodeKind = "patterns";
        var (read, _) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, T0);
        read.Node = "9f8e7d6c";
        read.NodeKind = "patterns";
        var (bad, _) = ApiKeys.Create("Odd", ApiKeyScope.Node, T0);
        bad.Node = "has space";
        bad.NodeKind = "patterns";
        var (kindless, _) = ApiKeys.Create("Kindless", ApiKeyScope.Node, T0);
        kindless.Node = "pm1";
        kindless.NodeKind = "Bad Kind!";
        var clean = ApiKeys.Sanitise(new[] { key, read, bad, kindless });
        Check(clean[0] is { Node: "9f8e7d6c", NodeKind: "patterns" } && clean[1] is { Node: null, NodeKind: null } && clean[2] is { Node: null, NodeKind: null }
              && clean[3] is { Node: "pm1", NodeKind: null },
              "keys: a node binding is kept only on a node key with a valid id (the kind only when valid)");

        var opts = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(clean[0], opts);
        var plain = JsonSerializer.Serialize(clean[1], opts);
        Check(json.EndsWith(",\"node\":\"9f8e7d6c\",\"nodeKind\":\"patterns\"}", StringComparison.Ordinal) && !plain.Contains("\"node", StringComparison.Ordinal)
              && JsonSerializer.Deserialize<ApiKey>(json) is { Node: "9f8e7d6c", NodeKind: "patterns" },
              $"keys: node and nodeKind are appended to a key in settings, and absent on other keys ({json})");
        Check(!json.Contains(ApiKeys.Prefix, StringComparison.Ordinal), "keys: the settings entry holds no secret");
    }

    // ------------------------------------------------------------------ node links on /ws (Show Bus §7.1)

    /// <summary>A /ws query as the server reads it (keys ignore case, a repeated key keeps the last value).</summary>
    static IReadOnlyDictionary<string, string> Query(string query)
    {
        var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            q[eq >= 0 ? pair[..eq] : pair] = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..]) : "";
        }
        return q;
    }

    static string Text(byte[]? bytes) => bytes is null ? "" : Encoding.UTF8.GetString(bytes);

    /// <summary>
    /// Admit a socket with the PIN 4321 asked for (or none), its viewing-code answer <paramref name="view"/>; the count
    /// says how often the viewing code was asked.
    /// </summary>
    static WsAdmission Admit(NodeBindings nodes, string query, WsCredential credential, bool pinAsked = true, string? view = null, int[]? asked = null)
        => NodeLinks.Admit(Query(query), credential, pinAsked, NodeLinks.PinHash(pinAsked ? "4321" : null),
            () => { if (asked is not null) asked[0]++; return view; }, "browser01", nodes, T0);

    /// <summary>What names a node and can't be a node link is refused before the upgrade, before anything is checked or counted.</summary>
    static void BadRequests(Action<bool, string> Check)
    {
        string? Bad(string query, string? authorization = null) => NodeLinks.BadRequest(Query(query), authorization, "abc123abc123");
        Check(Bad("node=pat1&t=patterns&win=abc") == NodeLinks.WindowNotNode && Bad("node=pat1&t=patterns", "Window k7f3") == NodeLinks.WindowNotNode
              && Bad("node=pat1&t=patterns", "window k7f3") == NodeLinks.WindowNotNode,
              "links: a window's address with a node, in the URL or the Authorization header, is a 400");
        Check(Bad("node=pat1&t=patterns&pin=swk_abc") == NodeLinks.PinInUrl && Bad("node=pat1&t=patterns&pin=4321") == NodeLinks.PinInUrl,
              "links: a node link's key or PIN in the URL is a 400 (the Authorization header carries it)");
        Check(Bad("node=pat1&t=patterns&role=control") == NodeLinks.NodeDoesNotControl, "links: a node link that asks to control is a 400");
        Check(Bad("node=&t=patterns") == NodeLinks.BadId && Bad("node=has%20space&t=patterns") == NodeLinks.BadId && Bad("node=" + new string('a', 41) + "&t=patterns") == NodeLinks.BadId,
              "links: a bad node id is a 400");
        Check(Bad("node=pat1") == NodeLinks.BadKind && Bad("node=pat1&t=") == NodeLinks.BadKind && Bad("node=pat1&t=Bad%20Kind!") == NodeLinks.BadKind
              && Bad("node=pat1&t=" + new string('p', 17)) == NodeLinks.BadKind,
              "links: a missing or bad kind is a 400");
        Check(Bad("node=abc123abc123&t=main") == NodeLinks.OwnId && NodeLinks.BadRequest(Query("node=abc123abc123&t=main"), null, null) is null,
              "links: this Stagewatch's own id is a 400 (when it has one)");
        Check(Bad("node=pat1&t=patterns&cid=pat1&page=/patterns&detail=1", "Bearer swk_x") is null && Bad("cid=lobby01&win=abc&pin=1&role=control", "Window x") is null,
              "links: a good node link passes, and a socket that names no node is never refused here");
    }

    /// <summary>The admission matrix: what each credential makes of a socket that names a node, and of one that names none.</summary>
    static void Links(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var k1 = WsCredential.Key("k1", ApiKeyScope.Node);
        var k2 = WsCredential.Key("k2", ApiKeyScope.Node);

        // A node key: the verdict before any frame; its first proven use binds it.
        var a = Admit(nodes, "node=pat1&t=patterns&cid=pat1&page=/patterns&detail=1", k1);
        Check(a is { Close: false, Link: { Class: WsClass.Node, Node: "pat1", Kind: "patterns", Verified: NodeProof.Key, Words: null, KeyId: "k1", Scope: "node", OperatorRead: true, CanControl: false, PinHash: null } }
              && Text(a.First) == "{\"type\":\"auth\",\"ok\":true,\"scope\":\"node\",\"node\":{\"t\":\"patterns\",\"id\":\"pat1\",\"verified\":\"key\"}}"
              && nodes.NodeOf("k1") == ("patterns", "pat1"),
              $"links: a node key makes a node link, verified by key, bound on its first proven use; it hears the verdict first ({Text(a.First)})");
        Check(a.Link.ScreenId == "node:pat1" && !Screens.ValidId(a.Link.ScreenId) && a.Link.Claim == "",
              "links: a node link's screen id is one no browser can have");

        // The shared key keeps the frames: a node link with no verified id, and the binding's words.
        var shared = Admit(nodes, "node=pat2&t=patterns", k1);
        using (var d = JsonDocument.Parse(shared.First!))
            Check(shared is { Close: false, Link: { Class: WsClass.Node, Node: "pat2", Verified: NodeProof.None, OperatorRead: true } }
                  && d.RootElement.GetProperty("ok").GetBoolean() && d.RootElement.GetProperty("node").GetProperty("verified").GetString() == "none"
                  && d.RootElement.GetProperty("words").GetString() == "this key speaks for patterns pat1 — make a key for patterns pat2"
                  && shared.Link.Words == d.RootElement.GetProperty("words").GetString()
                  && nodes.NodeOf("k1") == ("patterns", "pat1") && nodes.LastRefused("k1") is { Id: "pat2" },
                  $"links: a key bound to another node keeps its frames, verified none with the binding's words, and the refusal is kept for the operator ({Text(shared.First)})");
        var other = Admit(nodes, "node=pat1&t=patterns", k2);
        Check(other.Link is { Class: WsClass.Node, Verified: NodeProof.None, Words: "patterns pat1 speaks with its own key" } && nodes.NodeOf("k2") is null,
              "links: another key for a bound id is a node link verified none, and binds nothing");

        // The operator's credential: a control key or the PIN.
        var ctl = Admit(nodes, "node=ag1&t=messenger", WsCredential.Key("c1", ApiKeyScope.Control));
        var ctlBound = Admit(nodes, "node=pat1&t=patterns", WsCredential.Key("c1", ApiKeyScope.Control));
        Check(ctl.Link is { Class: WsClass.Node, Verified: NodeProof.Operator, Scope: "control", Words: null, CanControl: false } && nodes.KeyOf("ag1") is null
              && Text(ctl.First).Contains("\"scope\":\"control\"", StringComparison.Ordinal) && Text(ctl.First).Contains("\"verified\":\"operator\"", StringComparison.Ordinal)
              && ctlBound.Link is { Class: WsClass.Node, Verified: NodeProof.None, Words: "patterns pat1 speaks with its own key" },
              "links: a control key speaks for a free id as the operator (it reads, it never controls there), and not for a key-bound one");
        var pin = Admit(nodes, "node=ag2&t=messenger", WsCredential.Pin);
        var bareHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("4321"))).ToLowerInvariant();
        Check(pin.Link is { Class: WsClass.Node, Verified: NodeProof.Operator, Scope: "pin", OperatorRead: true, KeyId: null } && pin.Link.PinHash == NodeLinks.PinHash("4321")
              && pin.Link.PinHash != "4321" && Admit(nodes, "node=pat1&t=patterns", WsCredential.Pin).Link is { Class: WsClass.Node, Verified: NodeProof.None },
              "links: the PIN speaks for a free id as the operator; the link keeps a mark of it, never the PIN");
        Check(NodeLinks.PinHash("4321") != bareHash && NodeLinks.PinHash("4321") != NodeLinks.PinHash("4322") && NodeLinks.PinHash("4321").Length == 64 && NodeLinks.PinHash("") == "",
              "links: a PIN's mark is keyed (not a bare hash a 4-digit PIN could be read back from), and tells PINs apart");
        var wrong = Admit(nodes, "node=ag3&t=messenger", WsCredential.PinRefused("wrong PIN"));
        Check(wrong is { Close: false, Link: { Class: WsClass.Claim, OperatorRead: false, PinHash: null } }
              && Text(wrong.First) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"wrong PIN\",\"retry\":60}",
              $"links: a wrong PIN is one refusal with a retry, and the socket stays a claim with the public frame ({Text(wrong.First)})");

        // What proves nothing about a node is a claim: in Screens, flagged, out of 'all'.
        var read = Admit(nodes, "node=pat9&t=patterns", WsCredential.Key("r1", ApiKeyScope.Read));
        var mcp = Admit(nodes, "node=pat9&t=patterns", WsCredential.Key("m1", ApiKeyScope.Mcp));
        var none = Admit(nodes, "node=pat9&t=patterns&cid=browser99", WsCredential.None);
        Check(read.Link is { Class: WsClass.Claim, Verified: NodeProof.None, Words: NodeLinks.ReadKey, Scope: "read", OperatorRead: true }
              && mcp.Link is { Class: WsClass.Claim, Scope: "mcp" } && none.Link is { Class: WsClass.Claim, Words: NodeLinks.NoKey, Scope: "none", OperatorRead: false },
              "links: a read or MCP key, or nothing, only claims the id");
        Check(none.Link.ScreenId == NodeLinks.ClaimId("patterns", "pat9") && read.Link.ScreenId == none.Link.ScreenId && Screens.ValidId(none.Link.ScreenId)
              && none.Link.ScreenId.StartsWith("claim-", StringComparison.Ordinal) && none.Link.ScreenId.Length == 18 && none.Link.Claim == "patterns pat9",
              $"links: a claim is listed under one id for what it claims (reachable by that id), flagged with it ({none.Link.ScreenId})");
        Check(!Screens.ValidBrowserId(none.Link.ScreenId) && !Screens.ValidBrowserId(none.Link.ScreenId.ToUpperInvariant()) && !Screens.ValidBrowserId("win-abc123")
              && Screens.ValidBrowserId("lobbytv01") && Screens.ValidBrowserId("claimant01"),
              "links: no browser may choose a claim's id or a window's (case aside), so none is grouped with one or pushed to as one");
        using (var d = JsonDocument.Parse(none.First!))
            Check(d.RootElement.GetProperty("ok").GetBoolean() && d.RootElement.GetProperty("scope").GetString() == "none"
                  && d.RootElement.GetProperty("node").GetProperty("verified").GetString() == "none" && d.RootElement.GetProperty("words").GetString() == NodeLinks.NoKey,
                  $"links: a claim hears the verdict too: verified none, and why ({Text(none.First)})");
        Check(nodes.KeyOf("pat9") is null && nodes.LastRefused("r1") is null, "links: a claim binds nothing and is no refusal");
        var unknown = Admit(nodes, "node=ag4&t=messenger", WsCredential.KeyRefused("unknown or revoked API key"));
        var rate = Admit(nodes, "node=ag4&t=messenger", WsCredential.KeyRefused("rate limit: too many requests with this key", 37));
        Check(unknown is { Close: false, Link: { Class: WsClass.Claim, KeyId: null, OperatorRead: false } }
              && Text(unknown.First) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"unknown or revoked API key\",\"retry\":60}"
              && Text(rate.First).EndsWith(",\"retry\":37}", StringComparison.Ordinal),
              $"links: a refused key hears one refusal with when to try again, and stays open with the public frame ({Text(unknown.First)})");

        // The viewing code: only it closes, after one message.
        var asked = new int[1];
        var viewKey = Admit(nodes, "node=ag5&t=messenger", WsCredential.KeyRefused("unknown or revoked API key"), view: "This screen needs the viewing code", asked: asked);
        using (var d = JsonDocument.Parse(viewKey.First!))
            Check(viewKey.Close && asked[0] == 1
                  && Text(viewKey.First) == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\",\"retry\":60,\"refused\":\"unknown or revoked API key\"}"
                  && d.RootElement.GetProperty("view").GetBoolean() && d.RootElement.GetProperty("error").GetString() == "This screen needs the viewing code"
                  && d.RootElement.GetProperty("refused").GetString() == "unknown or revoked API key" && d.RootElement.GetProperty("retry").GetInt32() == 60,
                  $"links: a refused key where a viewing code is needed: one message saying both (the viewing code's words as its error, the key's after), then close ({Text(viewKey.First)})");
        var viewNone = Admit(nodes, "node=ag5&t=messenger", WsCredential.None, view: "This screen needs the viewing code");
        Check(viewNone.Close && Text(viewNone.First) == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\"}",
              "links: a keyless node link where a viewing code is needed gets the one message screens get, then close");
        asked[0] = 0;
        var viewed = Admit(nodes, "node=ag6&t=messenger", WsCredential.Key("k6", ApiKeyScope.Node), view: "This screen needs the viewing code", asked: asked);
        Check(!viewed.Close && asked[0] == 0 && viewed.Link.Verified == NodeProof.Key && Admit(nodes, "node=ag7&t=messenger", WsCredential.Pin, view: "x", asked: asked) is { Close: false } && asked[0] == 0,
              "links: a node key or the PIN opens the viewing code; it is never asked");

        // A socket that names no node: today's rules, and a node key still leaves the screens.
        var legacyNode = Admit(nodes, "cid=patterns&page=/patterns", WsCredential.Key("k3", ApiKeyScope.Node));
        Check(legacyNode is { First: null, Close: false, Link: { Class: WsClass.Node, Node: null, Verified: NodeProof.None, Words: NodeLinks.NamesNoId } }
              && legacyNode.Link.ScreenId == "key:k3" && nodes.NodeOf("k3") is null,
              "links: a node key that names no node is a show app listed under its key: no message, no id asserted, nothing bound");
        var legacyRead = Admit(nodes, "detail=1&cid=dashboard1", WsCredential.Key("r1", ApiKeyScope.Read));
        Check(legacyRead is { First: null, Link: { Class: WsClass.Screen, OperatorRead: true, ScreenId: "browser01" } },
              "links: an accepted key that names no node hears nothing new (its first message is a frame)");
        var legacyBad = Admit(nodes, "cid=dashboard1", WsCredential.KeyRefused("unknown or revoked API key"));
        Check(legacyBad is { Close: false, Link: { Class: WsClass.Screen, OperatorRead: false } }
              && Text(legacyBad.First) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"unknown or revoked API key\",\"retry\":60}",
              "links: a refused key that names no node hears the one refusal and stays a viewer");
        Check(Admit(nodes, "cid=lobbytv01&page=/viewer", WsCredential.None) is { First: null, Close: false, Link: { Class: WsClass.Screen, CanControl: false } },
              "links: a browser hears nothing new");

        // Control pages keep their auth messages.
        var page = Admit(nodes, "role=control&pin=4321", WsCredential.Pin);
        Check(page is { Link: { CanControl: true, Class: WsClass.Screen } } && Text(page.First) == "{\"type\":\"auth\",\"ok\":true}" && page.Link.PinHash == NodeLinks.PinHash("4321"),
              "links: a control page with the PIN controls, and hears ok");
        var pageWrong = Admit(nodes, "role=control&pin=0000", WsCredential.PinRefused("wrong PIN"));
        var pageNone = Admit(nodes, "role=control", WsCredential.None);
        var pageRead = Admit(nodes, "role=control", WsCredential.Key("r1", ApiKeyScope.Read));
        var pageOpen = Admit(nodes, "role=control", WsCredential.None, pinAsked: false);
        Check(Text(pageWrong.First) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"wrong PIN\"}" && !pageWrong.Link.CanControl
              && Text(pageNone.First) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"PIN required\"}"
              && Text(pageRead.First).Contains("this API key is read only", StringComparison.Ordinal) && pageRead.Link is { CanControl: false, OperatorRead: true }
              && pageOpen.Link is { CanControl: true, PinHash: "" },
              "links: a control page's refusals are as before (no retry: a person reads them); with no PIN asked for, it controls");
        var pageView = Admit(nodes, "role=control&pin=0000", WsCredential.PinRefused("wrong PIN"), view: "This screen needs the viewing code");
        Check(pageView.Close && Text(pageView.First) == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\"}",
              "links: a control page refused where a viewing code is needed hears what screens hear, then closes");
    }

    /// <summary>Keys or the PIN changed: what each open socket becomes, and the one message it hears (or none).</summary>
    static void Rechecks(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var dash = new ApiKey { Id = "r1", Name = "Dashboard", Scope = ApiKeyScope.Read, Hash = new string('a', 64) };
        var keys = new[] { NodeKey("k1", null, null), NodeKey("k2", null, null), NodeKey("k3", null, null), dash };
        nodes.Keep(keys);
        var hash = NodeLinks.PinHash("4321");
        var link = Admit(nodes, "node=pat1&t=patterns", WsCredential.Key("k1", ApiKeyScope.Node)).Link;

        const string needsView = "This screen needs the viewing code";
        var same = NodeLinks.Recheck(link, keys, nodes, hash, viewRefusal: null);
        Check(ReferenceEquals(same.Link, link) && same.Message is null && !same.Close, "recheck: nothing changed, nothing said");

        // The operator moves the binding: the link hears a fresh verdict, latest wins; a re-read records no refusal.
        nodes.Rebind("k2", "patterns", "pat1");
        var moved = NodeLinks.Recheck(link, keys, nodes, hash, null);
        Check(moved is { Close: false, Link: { Class: WsClass.Node, Verified: NodeProof.None, Words: "patterns pat1 speaks with its own key" } }
              && Text(moved.Message).Contains("\"verified\":\"none\"", StringComparison.Ordinal) && Text(moved.Message).Contains("speaks with its own key", StringComparison.Ordinal)
              && nodes.LastRefused("k1") is null,
              $"recheck: a binding moved off the link's key: a fresh verdict, verified none, and no refusal recorded ({Text(moved.Message)})");
        nodes.Rebind("k1", "patterns", "pat1");
        var back = NodeLinks.Recheck(moved.Link, keys, nodes, hash, null);
        Check(back.Link is { Verified: NodeProof.Key, Words: null } && Text(back.Message).Contains("\"verified\":\"key\"", StringComparison.Ordinal)
              && !Text(back.Message).Contains("words", StringComparison.Ordinal),
              "recheck: moved back by the operator, the link hears it is verified by key again");

        // A key revoked: one refusal, the public frame; a node link becomes a claim. Only the viewing code closes.
        var noK1 = keys.Where(k => k.Id != "k1").ToArray();
        var gone = NodeLinks.Recheck(back.Link, noK1, nodes, hash, viewRefusal: null);
        Check(gone is { Close: false, Link: { Class: WsClass.Claim, KeyId: null, OperatorRead: false, Verified: NodeProof.None, Scope: "none" } }
              && Text(gone.Message) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"this key was revoked\",\"retry\":60}"
              && gone.Link.ScreenId == NodeLinks.ClaimId("patterns", "pat1"),
              $"recheck: a revoked key's node link hears so once, keeps the public frame, and is a claim from now on ({Text(gone.Message)})");
        var goneView = NodeLinks.Recheck(back.Link, noK1, nodes, hash, viewRefusal: needsView);
        Check(goneView.Close && Text(goneView.Message) == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\",\"retry\":60,\"refused\":\"this key was revoked\"}",
              $"recheck: where a viewing code is set and it has no other pass, the one message carries view, the code's words and the key's, and it closes ({Text(goneView.Message)})");
        Check(NodeLinks.Recheck(gone.Link, noK1, nodes, hash, null) is { Message: null } again && ReferenceEquals(again.Link, gone.Link),
              "recheck: a link already dropped hears nothing more");
        var board = Admit(nodes, "detail=1&cid=dashboard1", WsCredential.Key("r1", ApiKeyScope.Read)).Link;
        var boardGone = NodeLinks.Recheck(board, keys.Where(k => k.Id != "r1").ToArray(), nodes, hash, null);
        Check(boardGone is { Close: false, Link: { Class: WsClass.Screen, OperatorRead: false, ScreenId: "browser01" } }
              && Text(boardGone.Message).Contains("this key was revoked", StringComparison.Ordinal),
              "recheck: a keyed dashboard (no node) hears the same and drops to the public frame");
        var keyed = Admit(nodes, "cid=patterns", WsCredential.Key("k3", ApiKeyScope.Node)).Link;
        nodes.Speak(NodeCredential.Key("k3"), "patterns", "pat3", T0);   // its own context POST binds its key
        Check(NodeLinks.Recheck(keyed, keys, nodes, hash, null) is { Message: null } kept && ReferenceEquals(kept.Link, keyed),
              "recheck: a node key that names no node is untouched when its own context binds its key");
        Check(NodeLinks.Recheck(keyed, keys.Where(k => k.Id != "k3").ToArray(), nodes, hash, null).Link is { Class: WsClass.Screen, ScreenId: "browser01" },
              "recheck: revoked, it is a screen again under its browser's id");

        // The PIN changed: what the PIN admitted drops to a viewer.
        var page = Admit(nodes, "role=control&pin=4321", WsCredential.Pin).Link;
        Check(NodeLinks.Recheck(page, keys, nodes, hash, null) is { Message: null } pageSame && ReferenceEquals(pageSame.Link, page), "recheck: the same PIN, nothing said");
        var pageChanged = NodeLinks.Recheck(page, keys, nodes, NodeLinks.PinHash("9999"), null);
        Check(pageChanged is { Close: false, Link: { CanControl: false, OperatorRead: false, PinHash: null } }
              && Text(pageChanged.Message) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"the PIN changed — enter the new one\"}"
                  .Replace("—", "\\u2014", StringComparison.Ordinal),
              $"recheck: a new PIN drops a control page to a viewer, and its page asks again ({Text(pageChanged.Message)})");
        var open = Admit(nodes, "role=control", WsCredential.None, pinAsked: false).Link;
        Check(NodeLinks.Recheck(open, keys, nodes, hash, null).Link is { CanControl: false },
              "recheck: a PIN set where there was none drops a control page let in without one");
        Check(NodeLinks.Recheck(page, keys, nodes, "", null) is { Message: null, Link: { CanControl: true, PinHash: "" } },
              "recheck: the PIN taken away: a control page keeps on, as any page may now");
        var byPin = Admit(nodes, "node=ag7&t=messenger", WsCredential.Pin).Link;
        var byPinChanged = NodeLinks.Recheck(byPin, keys, nodes, NodeLinks.PinHash("9999"), null);
        var byPinOpen = NodeLinks.Recheck(byPin, keys, nodes, "", null);
        Check(byPinChanged.Link is { Class: WsClass.Claim, OperatorRead: false } && Text(byPinChanged.Message).EndsWith(",\"retry\":60}", StringComparison.Ordinal)
              && byPinOpen.Link is { Class: WsClass.Claim, Words: NodeLinks.NoKey } && Text(byPinOpen.Message).Contains("\"scope\":\"none\"", StringComparison.Ordinal),
              "recheck: a node link the PIN admitted is a claim once the PIN changes (refused, with a retry) or is taken away (verdict: only claimed)");
        var screen = Admit(nodes, "cid=lobbytv01", WsCredential.None).Link;
        Check(ReferenceEquals(NodeLinks.Recheck(screen, Array.Empty<ApiKey>(), nodes, "", needsView).Link, screen), "recheck: a socket nothing admitted is never touched");
        var pageView = NodeLinks.Recheck(page, keys, nodes, NodeLinks.PinHash("9999"), needsView);
        Check(pageView.Close && Text(pageView.Message) == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\",\"refused\":\"the PIN changed — enter the new one\"}"
                  .Replace("—", "\\u2014", StringComparison.Ordinal),
              $"recheck: a new PIN where a viewing code is set: the page asks for the code under its own words, the PIN's after, and closes ({Text(pageView.Message)})");

        // A node link the PIN or a control key admitted speaks as the operator only while no key speaks for its id.
        var pinLink = Admit(nodes, "node=pm1&t=playmap", WsCredential.Pin).Link;
        Check(pinLink is { Class: WsClass.Node, Verified: NodeProof.Operator } && NodeLinks.Recheck(pinLink, keys, nodes, hash, null) is { Message: null } pinSame && ReferenceEquals(pinSame.Link, pinLink),
              "recheck: a PIN's node link, nothing changed: nothing said");
        nodes.Rebind("k2", "playmap", "pm1");
        var pinMoved = NodeLinks.Recheck(pinLink, keys, nodes, hash, null);
        Check(pinMoved is { Close: false, Link: { Class: WsClass.Node, Verified: NodeProof.None, Words: "playmap pm1 speaks with its own key", OperatorRead: true } }
              && Text(pinMoved.Message) == "{\"type\":\"auth\",\"ok\":true,\"scope\":\"pin\",\"node\":{\"t\":\"playmap\",\"id\":\"pm1\",\"verified\":\"none\"},\"words\":\"playmap pm1 speaks with its own key\"}",
              $"recheck: the operator lets a key speak for a PIN link's id: a fresh verdict, verified none, with the binding's words ({Text(pinMoved.Message)})");
        var c1 = new ApiKey { Id = "c1", Name = "Desk", Scope = ApiKeyScope.Control, Hash = new string('c', 64) };
        var withCtl = keys.Append(c1).ToArray();
        var ctlLink = Admit(nodes, "node=ag8&t=messenger", WsCredential.Key("c1", ApiKeyScope.Control)).Link;
        nodes.Speak(NodeCredential.Key("k1"), "messenger", "ag8", T0);   // a key used anywhere fails: k1 already speaks for pat1
        Check(NodeLinks.Recheck(ctlLink, withCtl, nodes, hash, null) is { Message: null } ctlSame && ReferenceEquals(ctlSame.Link, ctlLink),
              "recheck: a control key's node link: a key refused for its id changes nothing");
        nodes.Keep(withCtl.Append(NodeKey("k4", null, null)));
        nodes.Speak(NodeCredential.Key("k4"), "messenger", "ag8", T0);  // another key's first use takes the id
        Check(NodeLinks.Recheck(ctlLink, withCtl, nodes, hash, null) is { Close: false, Link: { Verified: NodeProof.None, Words: "messenger ag8 speaks with its own key", Scope: "control" } } ctlTaken
              && Text(ctlTaken.Message).Contains("\"verified\":\"none\"", StringComparison.Ordinal),
              "recheck: a control key's node link hears verified none once a key's first use takes its id");

        // A key the bindings freed binds to its standing link's id, as that link's admission would have.
        var fresh = new NodeBindings();
        var nodeKeys = new[] { NodeKey("k1", null, null), NodeKey("k2", null, null), NodeKey("k3", null, null) };
        fresh.Keep(nodeKeys);
        var holder = Admit(fresh, "node=pat1&t=patterns", WsCredential.Key("k1", ApiKeyScope.Node)).Link;
        var waiting = Admit(fresh, "node=pat1&t=patterns", WsCredential.Key("k2", ApiKeyScope.Node));
        var third = Admit(fresh, "node=pat1&t=patterns", WsCredential.Key("k3", ApiKeyScope.Node)).Link;
        var refusedAt = fresh.LastRefused("k2")?.AtUtc;
        Check(holder.Verified == NodeProof.Key && waiting.Link is { Verified: NodeProof.None, Words: "patterns pat1 speaks with its own key" } && fresh.NodeOf("k2") is null,
              "recheck: a second key for a bound id waits, verified none, bound to nothing");
        var bound = new List<NodeBound>();
        fresh.Bound += b => bound.Add(b);
        var noHolder = nodeKeys.Where(k => k.Id != "k1").ToArray();
        fresh.Keep(noHolder);
        var taken = NodeLinks.Recheck(waiting.Link, noHolder, fresh, hash, null);
        var stillWaiting = NodeLinks.Recheck(third, noHolder, fresh, hash, null);
        Check(taken is { Close: false, Link: { Class: WsClass.Node, Verified: NodeProof.Key, Words: null } } && Text(taken.Message) == "{\"type\":\"auth\",\"ok\":true,\"scope\":\"node\",\"node\":{\"t\":\"patterns\",\"id\":\"pat1\",\"verified\":\"key\"}}"
              && fresh.NodeOf("k2") == ("patterns", "pat1") && fresh.KeyOf("pat1") == "k2" && bound is [{ KeyId: "k2", Id: "pat1", ByOperator: false }],
              $"recheck: the holder revoked, a waiting link's free key binds the free id: verified key is a binding the bindings hold ({Text(taken.Message)})");
        Check(stillWaiting.Message is null && ReferenceEquals(stillWaiting.Link, third) && fresh.NodeOf("k3") is null && fresh.LastRefused("k2")?.AtUtc == refusedAt
              && fresh.LastRefused("k3")?.AtUtc == T0,
              "recheck: a third key for that id still waits (its words unchanged, nothing said), and no refusal is re-stamped by a recheck");
    }

    /// <summary>Whom a screen command reaches: browsers only, in a fixed order a peer's chosen name can't shadow.</summary>
    static void Targets(Action<bool, string> Check)
    {
        var claimId = NodeLinks.ClaimId("patterns", "pat9");
        var screens = new[]
        {
            new ScreenInfo("lobbytv01", "Lobby TV", "/viewer", "10.0.0.20", "Smart TV", T0, false, 1),
            new ScreenInfo("ctl000001", "", "/control", "10.0.0.21", "iPad", T0, true, 1),
            new ScreenInfo(claimId, "", "/", "10.0.0.22", "Browser", T0, false, 1, "patterns pat9"),
        };
        var links = new[]
        {
            new NodeLinkFact("messenger", "ag1", NodeProof.Key, "k1", "AGMessage", "10.0.0.5", T0, 1, null),
            new NodeLinkFact("patterns", "pat1", NodeProof.Key, "k2", "Patterns FOH", "10.0.0.6", T0, 1, null),
            new NodeLinkFact("patterns", "pat2", NodeProof.None, "k2", "Patterns FOH", "10.0.0.7", T0, 1, "this key speaks for patterns pat1 — make a key for patterns pat2"),
            new NodeLinkFact("playmap", "pm1", NodeProof.Operator, null, "", "10.0.0.8", T0, 1, null),
            new NodeLinkFact("", null, NodeProof.None, "k3", "Show apps", "10.0.0.9", T0, 1, NodeLinks.NamesNoId),
        };
        static string? Names(string id) => id switch { "ag1" => "Lobby TV", "pat1" => "FOH desk", "pat2" => "Spare desk", "pm1" => "all", _ => null };
        ScreenTarget To(string who) => ScreenDirector.Target(screens, links, who, Names);

        Check(To("all") is { Refusal: null, Hits: [{ Id: "lobbytv01" }] } && To("") is { Hits: [{ Id: "lobbytv01" }] } && To(" ALL ") is { Hits: [{ Id: "lobbytv01" }] },
              "screen targets: 'all' and no target are the browsers: never a control page, a claim or a show app (one named 'all' included)");
        Check(To(claimId) is { Refusal: null, Hits: [{ Claim: "patterns pat9" }] }, "screen targets: a claim is reached by its own id");
        Check(To("ag1").Refusal == "Screens: ag1 is a show app (Lobby TV), not a screen — screen commands reach browsers only (Producer → Show network)"
              && To("ag1").Hits.Count == 0 && To("pm1").Refusal!.StartsWith("Screens: pm1 is a show app (all)", StringComparison.Ordinal),
              $"screen targets: a show app's id is refused with its name ({To("ag1").Refusal})");
        Check(To("pat2").Refusal!.StartsWith("Screens: pat2 is a show app (Spare desk)", StringComparison.Ordinal),
              "screen targets: a show app whose id its key doesn't verify is still a show app");
        Check(To("FOH desk").Refusal!.StartsWith("Screens: FOH desk is a show app (patterns pat1)", StringComparison.Ordinal)
              && To("Spare desk").Refusal == "Screens: nothing matches \"Spare desk\"",
              "screen targets: a show app verified by its key is refused by its name too; a name no key verifies means nothing");
        Check(To("Lobby TV") is { Refusal: null, Hits: [{ Id: "lobbytv01" }] } && To("lobby tv") is { Hits: [{ Id: "lobbytv01" }] },
              "screen targets: a browser named Lobby TV is reached, beside a show app that calls itself Lobby TV");
        Check(To("nobody").Refusal == "Screens: nothing matches \"nobody\"" && To("key:k3").Refusal == "Screens: nothing matches \"key:k3\""
              && To("node:ag1").Refusal == "Screens: nothing matches \"node:ag1\"",
              "screen targets: nothing else matches (a node link's own screen id included)");
        Check(ScreenDirector.Target(Array.Empty<ScreenInfo>(), links, "all", Names).Refusal == "Screens: no browser screen is connected"
              && ScreenDirector.Target(screens.Where(s => s.Claim.Length > 0).ToArray(), links, "all", Names).Refusal == "Screens: no browser screen is connected",
              "screen targets: 'all' with no browser (only claims) reaches nothing, and says so");
        Check(Screens.Match(screens, "all").Select(s => s.Id).SequenceEqual(new[] { "lobbytv01" }) && Screens.Match(screens, claimId).Count == 1,
              "screens: Match keeps claims out of 'all' and reaches one by its id");
    }
}
