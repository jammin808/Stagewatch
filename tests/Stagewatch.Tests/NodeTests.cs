using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;

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
}
