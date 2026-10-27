using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Show;

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
        Network(Check);
        NetworkProof(Check);
        NetworkWords(Check);
        DeskRows(Check);
        Readers(Check);
        Persist(Check);
        Rebinds(Check);
        BindingWords(Check);
        Choices(Check);
    }

    // ------------------------------------------------------------------ the bindings kept and moved (Settings, the Show network)

    static ApiKey ScopedKey(string id, ApiKeyScope scope, string name)
        => new() { Id = id, Name = name, Scope = scope, Hash = new string('b', 64), Hint = id[^2..].PadLeft(4, 'x') };

    /// <summary>
    /// The saved pairs follow the bindings: a first use, a kind first named, the operator's move and the key it took the id
    /// from are all written onto the keys, and a restart (the settings file read back) holds exactly the same pairs.
    /// </summary>
    static void Persist(Action<bool, string> Check)
    {
        var saved = new List<ApiKey> { NodeKey("kP", null, null), NodeKey("kA", null, null), NodeKey("kN", null, null), ScopedKey("kR", ApiKeyScope.Read, "Dashboard") };
        var nodes = new NodeBindings(saved);
        Check(!nodes.Unsaved(saved) && !nodes.SaveTo(saved), "persist: nothing bound, nothing to save");

        nodes.Speak(NodeCredential.Key("kP"), "patterns", "pat1", T0);
        nodes.Speak(NodeCredential.Key("kA"), "", "ag1", T0);   // a collect names the id alone
        Check(nodes.Unsaved(saved) && saved[0].Node is null, "persist: a first use leaves the saved keys behind the bindings until saved");
        Check(nodes.SaveTo(saved) && saved[0] is { Node: "pat1", NodeKind: "patterns" } && saved[1] is { Node: "ag1", NodeKind: null }
              && saved[2] is { Node: null, NodeKind: null } && saved[3] is { Node: null } && !nodes.Unsaved(saved),
              "persist: each node key gets the node it speaks for (the kind only once named); a key bound to nothing and a read key stay bare");
        nodes.Speak(NodeCredential.Key("kA"), "messenger", "ag1", T0);
        Check(nodes.SaveTo(saved) && saved[1] is { Node: "ag1", NodeKind: "messenger" }, "persist: the kind first named is saved");

        // The operator moves pat1 onto kN: kN is saved with it, and kP, which it was taken from, is saved with nothing.
        nodes.Rebind("kN", "patterns", "pat1");
        Check(nodes.SaveTo(saved) && saved[2] is { Node: "pat1", NodeKind: "patterns" } && saved[0] is { Node: null, NodeKind: null },
              "persist: the operator's move is saved on both keys, so the old pair can't come back");

        // A restart reads the settings file back: the same pairs, and kP can't reclaim pat1 even once kN is revoked.
        var opts = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var reread = ApiKeys.Sanitise(JsonSerializer.Deserialize<List<ApiKey>>(JsonSerializer.Serialize(saved, opts), opts));
        var restarted = new NodeBindings(reread);
        Check(restarted.KeyOf("pat1") == "kN" && restarted.NodeOf("kA") == ("messenger", "ag1") && restarted.NodeOf("kP") is null && !restarted.Unsaved(reread),
              "persist: after a restart the bindings are what they were");
        restarted.Keep(reread.Where(k => k.Id != "kN"));
        Check(restarted.KeyOf("pat1") is null && restarted.Speak(NodeCredential.Key("kP"), "patterns", "pat1", T0).Ok,
              "persist: once the new holder is revoked, the id is free (the old key binds only by a new first use)");

        // Saving touches only keys the bindings were given.
        var stranger = NodeKey("kZ", "patterns", "pat9");
        Check(!restarted.SaveTo(new[] { stranger }) && stranger is { Node: "pat9", NodeKind: "patterns" },
              "persist: a key the bindings were never given is left as saved");

        // A key saved for an id another key holds (a hand-edited file) is cleared on the next save.
        var twice = new List<ApiKey> { NodeKey("k1", "patterns", "pat1"), NodeKey("k2", "patterns", "pat1") };
        var dup = new NodeBindings(twice);
        Check(dup.KeyOf("pat1") == "k1" && dup.Unsaved(twice) && dup.SaveTo(twice) && twice[1].Node is null && twice[0].Node == "pat1",
              "persist: a second key saved for a held id is cleared, never bound");
    }

    /// <summary>Only the operator moves a binding, and only onto a node key in force; an empty kind is left to the node's own words.</summary>
    static void Rebinds(Action<bool, string> Check)
    {
        var keys = new List<ApiKey> { NodeKey("kP", null, null), NodeKey("kQ", null, null), ScopedKey("kC", ApiKeyScope.Control, "Stream Deck"), ScopedKey("kR", ApiKeyScope.Read, "Dashboard") };
        var nodes = new NodeBindings(keys);
        Check(nodes.Rebind("kC", "patterns", "pat1").Error == NodeBindings.NotANodeKey && nodes.Rebind("kR", "patterns", "pat1").Error == NodeBindings.NotANodeKey
              && nodes.KeyOf("pat1") is null, "rebind: a control or read key never speaks for a node");
        Check(nodes.Rebind("kGone", "patterns", "pat1").Error == "no such key" && nodes.KeyOf("pat1") is null, "rebind: a key not in force can't be bound");
        Check(nodes.Rebind("kP", "patterns", "desk01", "desk01").Error == "desk01 is this Stagewatch's own id" && nodes.KeyOf("desk01") is null && nodes.NodeOf("kP") is null
              && nodes.Rebind("kP", "patterns", "desk01x", "desk01").Bound is { Id: "desk01x" } && nodes.Rebind("kP", "", "desk01x").Error is not null,
              "rebind: this Stagewatch's own id is refused (no node route or link can use it); any other id is not");
        Check(nodes.Rebind("kP", "Bad Kind!", "pat1").Error is not null && nodes.Rebind("kP", "patterns", "bad id").Error is not null, "rebind: a malformed kind or id is refused");

        var (b, error) = nodes.Rebind("kP", "", "pm1");
        Check(error is null && b is { Kind: "", Id: "pm1", ByOperator: true } && nodes.NodeOf("kP") == ("", "pm1"), "rebind: an empty kind binds the id alone");
        Check(nodes.Rebind("kP", "", "pm1").Error == "it already speaks for node pm1" && nodes.Rebind("kP", null, "pm1").Error is not null,
              "rebind: the same id again changes nothing and says so");
        Check(nodes.Speak(NodeCredential.Key("kP"), "playmap", "pm1", T0).Ok && nodes.NodeOf("kP") == ("playmap", "pm1")
              && nodes.Rebind("kP", "", "pm1").Error == "it already speaks for playmap pm1",
              "rebind: the node's own first words name the kind, and an empty kind never erases it");

        // A key refused for a node, then let speak for it: its refusal is answered and forgotten.
        nodes.Speak(NodeCredential.Key("kQ"), "patterns", "pat2", T0);
        nodes.Speak(NodeCredential.Key("kQ"), "messenger", "ag3", T0.AddSeconds(5));
        Check(nodes.LastRefused("kQ") is { Id: "ag3" } && nodes.Rebind("kQ", "messenger", "ag3").Bound is { PreviousId: "pat2", TakenFromKeyId: null }
              && nodes.LastRefused("kQ") is null && nodes.KeyOf("pat2") is null && nodes.Speak(NodeCredential.Key("kQ"), "messenger", "ag3", T0).Ok,
              "rebind: the refused node is now the key's, its old one is free, and the refusal is gone");
    }

    /// <summary>The journal rows and Settings' words: names and ids, never a secret.</summary>
    static void BindingWords(Action<bool, string> Check)
    {
        string? Names(string id) => id switch { "kP" => "Patterns FOH", "kO" => "Old Patterns", _ => null };
        Check(KeyBindingWords.JournalLine(new NodeBound("kP", "patterns", "9f8e7d6c", null, null, null, false), Names)
              == "API key 'Patterns FOH' now speaks for patterns 9f8e7d6c (first use)", "words: a first use");
        Check(KeyBindingWords.JournalLine(new NodeBound("kP", "playmap", "pm1", "", "pm1", null, false), Names)
              == "API key 'Patterns FOH' speaks for playmap pm1 (its kind, first named)", "words: a kind first named");
        Check(KeyBindingWords.JournalLine(new NodeBound("kP", "patterns", "9f8e7d6c", "patterns", "1a2b3c4d", "kO", true), Names)
              == "API key 'Patterns FOH' now speaks for patterns 9f8e7d6c (the operator's choice; it spoke for patterns 1a2b3c4d; key 'Old Patterns' speaks for no node now)",
              "words: the operator's choice names what the key spoke for and the key the id moved off");
        Check(KeyBindingWords.JournalLine(new NodeBound("kX", "", "ag1", null, null, null, true), Names)
              == "API key (a revoked key) now speaks for node ag1 (the operator's choice; it spoke for no node)", "words: a key gone by the time the row is written");

        var node = NodeKey("kP", null, null);
        var refused = new NodeRefusal("kP", "messenger", "ag2", T0, "this key speaks for patterns pat1 — make a key for messenger ag2");
        Check(KeyBindingWords.KeyLine(node, ("patterns", "pat1"), null, T0) == "speaks for patterns pat1"
              && KeyBindingWords.KeyLine(node, null, null, T0) == "no node yet"
              && KeyBindingWords.KeyLine(node, ("patterns", "pat1"), refused, T0.AddSeconds(12)) == "speaks for patterns pat1 · refused for messenger ag2 12 s ago"
              && KeyBindingWords.KeyLine(node, ("messenger", "ag2"), refused, T0) == "speaks for messenger ag2"
              && KeyBindingWords.KeyLine(ScopedKey("kC", ApiKeyScope.Control, "Deck"), null, refused, T0) == "",
              "words: a node key says what it speaks for, or no node yet, and what it was refused for; other keys say nothing");
        Check(KeyBindingWords.Ago(T0, T0.AddSeconds(59)) == "59 s ago" && KeyBindingWords.Ago(T0, T0.AddSeconds(125)) == "2 min ago"
              && KeyBindingWords.Ago(T0, T0.AddHours(3)) == "3 h ago" && KeyBindingWords.Ago(T0.AddSeconds(5), T0) == "0 s ago",
              "words: how long ago, in whole units");
    }

    /// <summary>What "Let it speak for…" offers: in Settings the nodes for a key, on the Show network the keys for a node.</summary>
    static void Choices(Action<bool, string> Check)
    {
        var keys = new List<ApiKey>
        {
            NodeKey("kP", null, null), NodeKey("kA", null, null), NodeKey("kF", null, null), NodeKey("kB", null, null),
            ScopedKey("kC", ApiKeyScope.Control, "Stream Deck"),
        };
        keys[0].Name = "Patterns FOH";
        keys[1].Name = "AGMessage";
        keys[2].Name = "Spare";
        keys[3].Name = "Booth";
        foreach (var k in keys) k.Hint = k.Id[^2..].PadLeft(4, 'x');
        var nodes = new NodeBindings(keys);
        nodes.Speak(NodeCredential.Key("kP"), "patterns", "pat1", T0);
        nodes.Speak(NodeCredential.Key("kB"), "playmap", "pm1", T0);
        nodes.Speak(NodeCredential.Key("kP"), "patterns", "pat2", T0.AddSeconds(10));   // a second install pasted Patterns' key
        nodes.Speak(NodeCredential.Key("kB"), "messenger", "ag9", T0.AddSeconds(4));    // and the booth's key went into an AGMessage
        var view = ShowNetwork.Summaries(
            new[] { new HeardNode("patterns", "pat1", "FOH desk", "10.0.0.5", 1), new HeardNode("messenger", "ag1", "AGM", "10.0.0.7", 1), new HeardNode("playmap", "pm1", "Stage", "10.0.0.8", 1) },
            Array.Empty<HeldContext>(),
            new[] { Fact("patterns", "pat2", NodeProof.None, "kP", "Patterns FOH", "this key speaks for patterns pat1 — make a key for patterns pat2", address: "10.0.0.6") },
            "desk01", T0.AddSeconds(12));
        string? Names(string id) => keys.FirstOrDefault(k => k.Id == id)?.Name;

        var forP = KeyBindingWords.NodeChoices("kP", nodes, view, nodes.RefusedSince(T0, "desk01"), "desk01", Names, T0.AddSeconds(12));
        Check(forP.Select(c => c.Id).SequenceEqual(new[] { "pat2", "ag1", "pm1", "ag9" }) && forP[0].Kind == "patterns"
              && forP[0].Label == "patterns pat2 — this key was refused for it 2 s ago · no key of its own yet"
              && forP[2].Label == "Stage — playmap pm1 · speaks with key 'Booth', which would speak for no node"
              && forP[3].Label == "messenger ag9 — a key was refused for it 8 s ago · no key of its own yet",
              $"choices: the node this key was refused for first, then the rows and the refused nodes, never its own node ({string.Join(" | ", forP.Select(c => c.Label))})");
        var forF = KeyBindingWords.NodeChoices("kF", nodes, view, Array.Empty<NodeRefusal>(), "desk01", Names, T0.AddSeconds(12));
        Check(forF.Select(c => c.Id).SequenceEqual(new[] { "ag1", "pat1", "pm1", "pat2" }) && forF[1].Label == "FOH desk — patterns pat1 · speaks with key 'Patterns FOH', which would speak for no node"
              && forF[3].Label == "patterns pat2 — on /ws, unverified · no key of its own yet",
              $"choices: a key that was never refused is offered every node heard, the held ones saying whose they are ({string.Join(" | ", forF.Select(c => c.Id))})");

        var forPat2 = KeyBindingWords.KeyChoices("pat2", keys, nodes, T0.AddSeconds(12));
        Check(forPat2.Select(c => c.KeyId).SequenceEqual(new[] { "kP", "kA", "kF", "kB" }) && forPat2[0].Label == "Patterns FOH (…xxkP) · refused for this node 2 s ago · speaks for patterns pat1 now, and would stop"
              && forPat2[1].Label == "AGMessage (…xxkA) · speaks for no node yet" && forPat2[3].Label == "Booth (…xxkB) · speaks for playmap pm1 now, and would stop",
              $"choices: the keys refused for the node first, then free keys, then keys that would move, each saying what it speaks for now; never a control key ({string.Join(" | ", forPat2.Select(c => c.Label))})");
        Check(KeyBindingWords.KeyChoices("pat1", keys, nodes, T0).All(c => c.KeyId != "kP"), "choices: never the key that already speaks for the node");

        Check(forF[0] is { Id: "ag1", Kind: "" } && forF[0].Label == "AGM — node ag1 · no key of its own yet" && forF[1].Kind == "patterns" && forF[2].Kind == "playmap",
              "choices: a node heard only by its hello is offered with no kind (a hello is anyone's word); one a key speaks for, with that key's kind");

        // The kind saved on a key is one a credential named: the node's own keyed words, then the key that holds it, then its
        // row's credentialed facts; never a hello's word nor an unverified context's, whatever the row shows.
        var peers = new PeerContexts(nodes);
        Check(peers.Accept(Context("messenger", "ag5", "Claimed"), "10.0.0.20", T0, "desk01", NodeCredential.None).Status == 200
              && peers.Accept(Context("playmap", "pm7", "Booth PlayMap"), "10.0.0.21", T0, "desk01", NodeCredential.Operator).Status == 200,
              "choices: an open context and one the operator's credential proves are held");
        var told = ShowNetwork.Summaries(
            new[] { new HeardNode("messenger", "pl3", "Spoofed", "10.0.0.22", 1), new HeardNode("messenger", "pat1", "Spoofed FOH", "10.0.0.23", 1) },
            peers.Held(T0), new[] { Fact("playmap", "pl3", NodeProof.Operator, null, "", address: "10.0.0.24") }, "desk01", T0);
        NetworkRow Told(string id) => told.Rows.Single(r => r.Peer.Id == id);
        Check(KeyBindingWords.CredentialKind(Told("ag5")) == "" && Told("ag5").Peer.Kind == "messenger"
              && KeyBindingWords.CredentialKind(Told("pm7")) == "playmap"
              && KeyBindingWords.CredentialKind(Told("pl3")) == "playmap" && Told("pl3").Peer.Kind == "messenger"
              && KeyBindingWords.CredentialKind(Told("pat1")) == "" && Told("pat1").Peer.Kind == "messenger"
              && KeyBindingWords.CredentialKind(view.Rows.Single(r => r.Peer.Id == "ag1")) == "",
              "choices: a row's credentialed kind is its proven context's or its proven link's, never an unverified context's nor a hello's");
        Check(KeyBindingWords.KindFor(KeyBindingWords.CredentialKind(Told("ag5")), "ag5", nodes) == ""
              && KeyBindingWords.KindFor(KeyBindingWords.CredentialKind(Told("pl3")), "pl3", nodes) == "playmap"
              && KeyBindingWords.KindFor(KeyBindingWords.CredentialKind(Told("pat1")), "pat1", nodes) == "patterns"
              && KeyBindingWords.KindFor("messenger", "pat2", nodes) == "patterns" && KeyBindingWords.KindFor("", "ag9", nodes) == "messenger"
              && KeyBindingWords.KindFor("", "nobody", nodes) == "",
              "choices: a key is let speak with the node's own keyed words, else the holding key's kind, else its row's credential, else none (its first keyed words name it)");
        var forA = KeyBindingWords.NodeChoices("kA", nodes, told, Array.Empty<NodeRefusal>(), "desk01", Names, T0);
        Check(forA.SingleOrDefault(c => c.Id == "ag5") is { Kind: "", Label: "Claimed — node ag5 · no key of its own yet" }
              && forA.SingleOrDefault(c => c.Id == "pat1") is { Kind: "patterns" } && forA.SingleOrDefault(c => c.Id == "pl3") is { Kind: "playmap" },
              $"choices: Settings offers a node an unverified context names with no kind, and a spoofed hello never names a held node's kind ({string.Join(" | ", forA.Select(c => c.Kind + ":" + c.Id))})");
    }

    static NodeLinkFact Fact(string kind, string? id, NodeProof verified, string? keyId, string keyName, string? words = null, int links = 1, string address = "10.0.0.5", int at = 0)
        => new(kind, id, verified, keyId, keyName, address, T0.AddSeconds(at), links, words);

    /// <summary>
    /// One join for every reader (ShowNetwork.Summaries): hellos, contexts and the node links on /ws by node id; "ws" and
    /// the verified word from what proves the id now; conflicts and keys that name no id apart; the cap.
    /// </summary>
    static void Network(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        nodes.Keep(new[] { NodeKey("kP", null, null), NodeKey("kA", null, null) });
        Check(peers.Accept(Context("patterns", "pat1", "Patterns FOH"), "10.0.0.5", T0, "desk01", NodeCredential.Key("kP")).Status == 200
              && peers.Accept(Context("messenger", "ag1", ""), "10.0.0.7", T0, "desk01", NodeCredential.None).Status == 200,
              "network: a keyed context and an open one are held");
        var hellos = new[]
        {
            new HeardNode("patterns", "pat1", "FOH desk", "10.0.0.5", 1),
            new HeardNode("playmap", "pm1", "PlayMap stage", "10.0.0.8", 2),
            new HeardNode("agent", "lectern1", "Lectern PC", "10.0.0.20", 1),
            new HeardNode("main", "desk01", "This Stagewatch", "10.0.0.1", 1),
            new HeardNode("messenger", "ag1", "AG room", "10.0.0.7", 30),
        };
        var links = new[]
        {
            Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH", links: 2),
            Fact("playmap", "pm2", NodeProof.Operator, null, "", address: "10.0.0.9"),
            Fact("playmap", "pm3", NodeProof.Operator, "kC", "Desk control", address: "10.0.0.10"),
            Fact("patterns", "pat2", NodeProof.None, "kP", "Patterns FOH", "this key speaks for patterns pat1 — make a key for patterns pat2", address: "10.0.0.6"),
            Fact("messenger", "ag1", NodeProof.None, "kA", "AGMessage", "messenger ag1 speaks with its own key", address: "10.0.0.11", at: 1),
            Fact("messenger", null, NodeProof.None, "kOld", "Old AGMessage", NodeLinks.NamesNoId, address: "10.0.0.12"),
            Fact("main", "desk01", NodeProof.Key, "kX", "X"),
        };
        var now = T0.AddSeconds(2);
        var view = ShowNetwork.Summaries(hellos, peers.Held(now), links, "desk01", now);
        var ids = view.Rows.Select(r => r.Peer.Id).ToList();
        Check(ids.SequenceEqual(new[] { "ag1", "pat1", "pm1", "pm2", "pm3" }) && view.More == 0,
              $"network: told rows (hello or context) by kind and name, then rows only a proven link gives; never a presenter PC or this Stagewatch ({string.Join(", ", ids)})");
        var pat1 = view.Rows.Single(r => r.Peer.Id == "pat1");
        Check(pat1.Peer is { Link: "ws", Verified: "key", Name: "Patterns FOH", HasContext: true, Liveness: "live" } && pat1.Named && pat1.ContextProof == NodeProof.Key
              && pat1.Link is { KeyName: "Patterns FOH", Links: 2 } && pat1.Unproven.Count == 0,
              "network: a hello, a keyed context and a key's link on /ws are one row: link ws, verified key, named by its context");
        var pm1 = view.Rows.Single(r => r.Peer.Id == "pm1");
        Check(pm1.Peer is { Link: "", Verified: "", Name: "PlayMap stage", Light: Light.Grey, Headline: ShowNetwork.HelloOnly, HasContext: false } && pm1.Named,
              "network: a hello alone is a grey row that nothing spoke for: no link, verified \"\"");
        var pm2 = view.Rows.Single(r => r.Peer.Id == "pm2");
        Check(pm2.Peer is { Link: "ws", Verified: "operator", Name: "PlayMap pm2", Headline: ShowNetwork.LinkOnly, Liveness: "live", Address: "10.0.0.9" } && !pm2.Named,
              "network: a link the operator's credential proves is a row of its own, named after its kind and id, live while open");
        var ag1 = view.Rows.Single(r => r.Peer.Id == "ag1");
        Check(ag1.Peer is { Link: "", Verified: "none", Name: "AG room", HasContext: true } && ag1.ContextProof == NodeProof.None && ag1.Unproven is [{ KeyName: "AGMessage" }],
              "network: an open context is verified none (only claimed), and a link that proves nothing for the id rides on its row, not as a link");
        Check(view.Apart.Select(l => l.Id ?? "key:" + l.KeyId).SequenceEqual(new[] { "key:kOld", "pat2" }),
              $"network: a key that names no id and a conflict no hello, context or proven link lists are apart, never rows ({string.Join(", ", view.Apart.Select(l => l.Id ?? l.KeyId))})");

        // Liveness: a gone hello is live again while a proven link is open; a stale context with no link stays stale.
        var gone = ShowNetwork.Summaries(new[] { new HeardNode("patterns", "pat1", "FOH", "10.0.0.5", 40) }, Array.Empty<HeldContext>(),
            new[] { Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH") }, "desk01", now);
        var stale = ShowNetwork.Summaries(Array.Empty<HeardNode>(), peers.Held(T0.AddSeconds(9)), Array.Empty<NodeLinkFact>(), "desk01", T0.AddSeconds(9));
        Check(gone.Rows is [{ Peer.Liveness: "live", Peer.Link: "ws" }] && stale.Rows.All(r => r.Peer.Liveness == "stale" && r.Peer.Link == ""),
              "network: an open proven link is heard now; without one, a context ages as it always has");

        // Trust order: a pulled context counts for its own id while no key speaks for it; the first that proves something wins.
        var linkedJson = Context("patterns", "pat9", "Linked");
        var pulled = PeerContexts.Parse(linkedJson, "10.0.0.30", now)!;
        Check(peers.LinkedProof("pat9") == NodeProof.Key && peers.LinkedProof("pat1") == NodeProof.None,
              "network: a context pulled over this Stagewatch's own link proves its id only while no key speaks for that id");
        peers.Accept(Context("patterns", "pat9", "Open post"), "10.0.0.31", now, "desk01", NodeCredential.None);
        var trust = ShowNetwork.Summaries(Array.Empty<HeardNode>(), peers.Held(now).Append(new HeldContext(pulled, peers.LinkedProof("pat9"))), Array.Empty<NodeLinkFact>(), "desk01", now);
        Check(trust.Rows.Single(r => r.Peer.Id == "pat9") is { Peer: { Name: "Linked", Verified: "key" }, ContextProof: NodeProof.Key },
              "network: an open post never outranks the pulled context that proves the id");
        peers.Accept(Context("patterns", "pat8", "Desk post"), "10.0.0.32", now, "desk01", NodeCredential.Operator);
        var pulled8 = PeerContexts.Parse(Context("patterns", "pat8", "Pulled"), "10.0.0.30", now)!;
        var first = ShowNetwork.Summaries(Array.Empty<HeardNode>(), peers.Held(now).Append(new HeldContext(pulled8, peers.LinkedProof("pat8"))), Array.Empty<NodeLinkFact>(), "desk01", now);
        Check(first.Rows.Single(r => r.Peer.Id == "pat8") is { Peer: { Name: "Desk post", Verified: "operator" }, ContextProof: NodeProof.Operator },
              "network: a held context that proves something comes first, as the risk reads it");

        // The cap: told rows first, then link-only rows; the rest are counted, never listed.
        var many = Enumerable.Range(1, 3).Select(i => new HeardNode("patterns", "p" + i, "P" + i, "10.0.1." + i, 1));
        var only = Enumerable.Range(1, 3).Select(i => Fact("playmap", "m" + i, NodeProof.Key, "km" + i, "M" + i)).ToList();
        var capped = ShowNetwork.Summaries(many, Array.Empty<HeldContext>(), only, "desk01", now, max: 4);
        Check(capped.Rows.Count == 4 && capped.More == 2 && capped.Rows.Count(r => r.Peer.Kind == "patterns") == 3 && capped.Apart.Count == 0,
              $"network: capped, hello and context rows are kept first and the rest counted ({capped.Rows.Count} + {capped.More})");
        Check(ShowNetwork.Summaries(many, Array.Empty<HeldContext>(), only, "desk01", now).Rows.Count == 6 && PeerContexts.MaxPeers == 32,
              "network: the cap is the peers' own (32) unless a reader asks for fewer");
    }

    /// <summary>A context with a light and items of its own (what advice and the desk's problems read).</summary>
    static string Lit(string kind, string id, string name, string light, params (string Id, string Light, string State)[] items)
        => $"{{\"v\":1,\"node\":{{\"t\":\"{kind}\",\"id\":\"{id}\",\"name\":\"{name}\"}},\"light\":\"{light}\",\"headline\":\"{name} says {light}\",\"items\":["
           + string.Join(",", items.Select(i => $"{{\"id\":\"{i.Id}\",\"kind\":\"health\",\"label\":\"{i.Id}\",\"light\":\"{i.Light}\",\"state\":\"{i.State}\"}}")) + "]}";

    /// <summary>
    /// A row's verified is what proves the facts it shows: a context that proves nothing never rides under an id a link
    /// proves (set aside, so its light, headline, items and name reach no reader), and a context this Stagewatch pulled
    /// over one of its own playout links says so rather than claiming a key of its own.
    /// </summary>
    static void NetworkProof(Action<bool, string> Check)
    {
        var now = T0.AddSeconds(2);
        string? Names(string id) => id == "kP" ? "Patterns FOH" : null;

        // An operator's link (the PIN) and an open post for that free id: the post is held, but nothing of it shows under the id.
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        Check(peers.Accept(Lit("playmap", "pm7", "Forged stage", "red", ("projector", "red", "LAMP FAILED")), "10.0.0.66", T0, "desk01", NodeCredential.None).Proof == NodeProof.None,
              "proof: an open post for a free id is held, unverified");
        var pin = ShowNetwork.Summaries(Array.Empty<HeardNode>(), peers.Held(now), new[] { Fact("playmap", "pm7", NodeProof.Operator, null, "", address: "10.0.0.9") }, "desk01", now);
        var pm7 = pin.Rows.Single();
        Check(pm7.Peer is { Link: "ws", Verified: "operator", Light: Light.Grey, Headline: ShowNetwork.LinkOnly, Name: "PlayMap pm7", HasContext: false, Address: "10.0.0.9" }
              && pm7.Context is null && pm7.ContextProof == NodeProof.None && pm7.ClaimSetAside && !pm7.Named && !pm7.Pulled,
              $"proof: under an id the operator's link proves, an open post sets nothing: grey, the link's headline, verified operator ({pm7.Peer.Light}, {pm7.Peer.Headline}, {pm7.Peer.Verified})");
        Check(ShowNetwork.LinkLine(pm7, Names) == "follows over /ws · the PIN (the operator's, not a key of its own) · 10.0.0.9 · " + ShowNetwork.SetAside
              && ShowNetwork.MarkTip(pm7) == ShowNetwork.MarkWords("operator") && ShowNetwork.PeerAdvice(pin).Count == 0 && peers.RiskFor("pm7", "go", now) == "high",
              $"proof: the link line says the claim was set aside; no advice and no risk come from it ({ShowNetwork.LinkLine(pm7, Names)})");

        // A key's link and an open post stored before the key bound: the same, verified key.
        nodes.Keep(new[] { NodeKey("kP", null, null) });
        Check(peers.Accept(Lit("patterns", "pat1", "Early post", "red", ("screen:1", "red", "MISMATCH")), "10.0.0.67", T0, "desk01", NodeCredential.None).Proof == NodeProof.None
              && nodes.Speak(NodeCredential.Key("kP"), "patterns", "pat1", now).Ok,
              "proof: an open post, then the node's key binds its id on /ws");
        var keyed = ShowNetwork.Summaries(new[] { new HeardNode("patterns", "pat1", "FOH desk", "10.0.0.5", 1) }, peers.Held(now),
            new[] { Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH") }, "desk01", now);
        var pat1 = keyed.Rows.Single(r => r.Peer.Id == "pat1");
        Check(pat1.Peer is { Link: "ws", Verified: "key", Light: Light.Grey, Headline: ShowNetwork.LinkOnly, Name: "FOH desk", HasContext: false }
              && pat1.Context is null && pat1.ClaimSetAside && pat1.Named && ShowNetwork.PeerAdvice(keyed).All(a => !a.Key.StartsWith("peer:pat1:", StringComparison.Ordinal))
              && ShowNetwork.LinkLine(pat1, Names) == "follows over /ws · key 'Patterns FOH' · 10.0.0.5 · " + ShowNetwork.SetAside,
              $"proof: under a key-verified id, an earlier open post shows nothing; the row is named by its hello ({pat1.Peer.Name}, {pat1.Peer.Headline})");
        var facts = keyed.Rows.Where(r => r.Peer.Id == "pat1").Select(r => new PeerFact(r.Peer.Kind, r.Peer.Name, r.Peer.Light, r.Peer.Headline,
            r.Context?.Items.Select(i => i.State).ToList() ?? new List<string>(), Array.Empty<string>(), r.Peer.Link, r.Peer.Verified)).ToList();
        Check(facts is [{ Light: Light.Grey, Verified: "key", Parts.Count: 0 }] && !facts[0].Headline.Contains("Early post", StringComparison.Ordinal),
              "proof: the AI's facts carry nothing of a claim under a proven id");

        // With no link, an open context is shown, verified none, and its advice is labelled and a level lower.
        var open = ShowNetwork.Summaries(Array.Empty<HeardNode>(), peers.Held(now), Array.Empty<NodeLinkFact>(), "desk01", now);
        var advice = ShowNetwork.PeerAdvice(open);
        Check(open.Rows.Single(r => r.Peer.Id == "pm7").Peer is { Verified: "none", Light: Light.Red, Link: "" }
              && advice is [{ Level: AdviceLevel.Warn, Key: "peer:pat1:screen:1" }, { Level: AdviceLevel.Warn, Key: "peer:pm7:projector" }]
              && advice.All(a => a.Text.StartsWith("unverified: ", StringComparison.Ordinal))
              && advice[1].Text == "unverified: Forged stage — projector: LAMP FAILED",
              $"proof: with no link, a claim is shown as one: verified none, its advice 'unverified:' and never urgent ({string.Join(" | ", advice.Select(a => $"{a.Level} {a.Text}"))})");

        // A context posted with the node's own key: its advice keeps its level, under the name the row shows.
        peers.Accept(Lit("patterns", "pat1", "Patterns FOH", "red", ("screen:1", "red", "MISMATCH"), ("ndi", "amber", "late"), ("audio", "red", "no signal")), "10.0.0.5", now, "desk01", NodeCredential.Key("kP"));
        var proven = ShowNetwork.PeerAdvice(ShowNetwork.Summaries(Array.Empty<HeardNode>(), peers.Held(now), Array.Empty<NodeLinkFact>(), "desk01", now));
        Check(proven.Where(a => a.Key.StartsWith("peer:pat1:", StringComparison.Ordinal)).Select(a => (a.Level, a.Text)).SequenceEqual(new[]
              {
                  (AdviceLevel.Urgent, "Patterns FOH — screen:1: MISMATCH"), (AdviceLevel.Urgent, "Patterns FOH — audio: no signal"),
              }),
              $"proof: a proven context's red parts are urgent, two at most, worst first ({string.Join(" | ", proven.Select(a => a.Text))})");

        // Pulled over one of this Stagewatch's own playout links: key on the wire (decision 10), and the desk says what proves it — the link by name.
        var pulled = new HeldContext(PeerContexts.Parse(Context("patterns", "pat9", "Linked"), "10.0.0.30", now)!, peers.LinkedProof("pat9"), Pulled: true) { Via = "patterns" };
        var linked = ShowNetwork.Summaries(Array.Empty<HeardNode>(), new[] { pulled }, Array.Empty<NodeLinkFact>(), "desk01", now).Rows.Single();
        Check(linked.Peer is { Verified: "key", Link: "", HasContext: true } && linked.Pulled && linked.Via == "patterns" && linked.ContextProof == NodeProof.Key
              && ShowNetwork.LinkLine(linked, Names) == "its context comes over this Stagewatch's playout link patterns (10.0.0.30) · not linked on /ws"
              && ShowNetwork.Mark(linked.Peer.Verified) == "✓"
              && ShowNetwork.MarkTip(linked) == "verified: its context comes over this Stagewatch's own playout link patterns, to the address the operator set — not a key of its own",
              $"proof: a pulled context is verified key, and its words say this Stagewatch's own link proves it, never a key it posted with ({ShowNetwork.LinkLine(linked, Names)})");
        var pinLinked = ShowNetwork.Summaries(Array.Empty<HeardNode>(), new[] { pulled }, new[] { Fact("patterns", "pat9", NodeProof.Operator, null, "", address: "10.0.0.30") }, "desk01", now).Rows.Single();
        Check(pinLinked.Peer is { Verified: "key", Link: "ws", HasContext: true }
              && ShowNetwork.LinkLine(pinLinked, Names) == "follows over /ws · the PIN (the operator's, not a key of its own) · 10.0.0.30 · its context comes over this Stagewatch's playout link patterns (10.0.0.30)",
              $"proof: following over /ws with the PIN, its pulled context still says where it came from ({ShowNetwork.LinkLine(pinLinked, Names)})");
        var bound = new HeldContext(PeerContexts.Parse(Context("patterns", "pat1", "Pulled"), "10.0.0.30", now)!, peers.LinkedProof("pat1"), Pulled: true) { Via = "stage-left" };
        var boundRow = ShowNetwork.Summaries(Array.Empty<HeardNode>(), new[] { bound }, Array.Empty<NodeLinkFact>(), "desk01", now).Rows.Single();
        Check(boundRow.Peer.Verified == "none" && ShowNetwork.Mark(boundRow.Peer.Verified) == "○" && ShowNetwork.MarkTip(boundRow) == ShowNetwork.MarkWords("none")
              && ShowNetwork.LinkLine(boundRow, Names) == "its context comes over this Stagewatch's playout link stage-left (10.0.0.30), but a key speaks for its id: it proves nothing, so it sets no risk · not linked on /ws",
              $"proof: once a key speaks for the id, a pulled context proves nothing and says so ({ShowNetwork.LinkLine(boundRow, Names)})");
    }

    /// <summary>The desk's words for a row: the mark beside the light, and how the app reaches this Stagewatch.</summary>
    static void NetworkWords(Action<bool, string> Check)
    {
        Check(ShowNetwork.Mark("key") == "✓" && ShowNetwork.Mark("operator") == "△" && ShowNetwork.Mark("none") == "○" && ShowNetwork.Mark("") == "○"
              && ShowNetwork.MarkWords("key").StartsWith("verified: it speaks with a node key of its own", StringComparison.Ordinal)
              && ShowNetwork.MarkWords("none").StartsWith("unverified", StringComparison.Ordinal) && ShowNetwork.MarkWords("").StartsWith("unverified", StringComparison.Ordinal),
              "words: a shape for each proof (✓ key, △ operator, ○ unverified), with words beside it");
        static NetworkRow Row(NodeLinkFact? link, PeerContexts.Entry? context = null, NodeProof proof = NodeProof.None, params NodeLinkFact[] unproven)
            => new(new PeerSummary("patterns", "pat1", "FOH", Light.Green, "", "", 0, context is not null), context, proof, link, unproven, true);
        string? Names(string id) => id == "kP" ? "Patterns FOH" : null;
        var keyed = Row(Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH", links: 2));
        var ctl = Row(Fact("patterns", "pat1", NodeProof.Operator, "kC", "Desk control"));
        var pin = Row(Fact("patterns", "pat1", NodeProof.Operator, null, ""));
        var entry = PeerContexts.Parse(Context("patterns", "pat1", "FOH"), "10.0.0.5", T0)! with { KeyId = "kP", Proof = NodeProof.Key };
        Check(ShowNetwork.LinkLine(keyed, Names) == "follows over /ws · key 'Patterns FOH' · 10.0.0.5 · 2 links"
              && ShowNetwork.LinkLine(ctl, Names) == "follows over /ws · control key 'Desk control' (the operator's, not its own) · 10.0.0.5"
              && ShowNetwork.LinkLine(pin, Names) == "follows over /ws · the PIN (the operator's, not a key of its own) · 10.0.0.5",
              $"words: a link names the credential that proves it and where it comes from ({ShowNetwork.LinkLine(keyed, Names)})");
        Check(ShowNetwork.LinkLine(Row(null, entry, NodeProof.Key), Names) == "posts its context with key 'Patterns FOH' · not linked on /ws"
              && ShowNetwork.LinkLine(Row(null, entry, NodeProof.Operator), Names) == "posts its context with the operator's credential · not linked on /ws"
              && ShowNetwork.LinkLine(Row(null, entry), Names) == "posts its context unverified, so it sets no risk · not linked on /ws"
              && ShowNetwork.LinkLine(Row(null), Names) == "heard on the network · not linked on /ws",
              "words: with no link, how its context came, or that it was only heard");
        var twin = Fact("patterns", "pat1", NodeProof.None, "kQ", "Spare", "patterns pat1 speaks with its own key", address: "10.0.0.6");
        Check(ShowNetwork.LinkLine(Row(keyed.Link, null, NodeProof.None, twin), Names)
                  == "follows over /ws · key 'Patterns FOH' · 10.0.0.5 · 2 links · on /ws from 10.0.0.6, unverified: patterns pat1 speaks with its own key"
              && ShowNetwork.ApartLine(Fact("messenger", null, NodeProof.None, "kOld", "Old AGMessage", NodeLinks.NamesNoId, address: ""))
                  == "on /ws with key 'Old AGMessage', which names no node id: it follows the timer and speaks for no node",
              "words: a link that proves nothing says why, and a key that names no id says so; names and addresses only");
        Check(ShowNetwork.LinkLine(Row(null, entry, NodeProof.None, twin), Names)
                  == "posts its context unverified, so it sets no risk · on /ws from 10.0.0.6, unverified: patterns pat1 speaks with its own key"
              && ShowNetwork.LinkLine(Row(null, null, NodeProof.None, twin), Names) == "heard on the network · on /ws from 10.0.0.6, unverified: patterns pat1 speaks with its own key",
              "words: a row whose only link proves nothing never also says it is not linked");
        Check(ShowNetwork.AppName("patterns") == "Patterns" && ShowNetwork.AppName("messenger") == "AGMessage" && ShowNetwork.AppName("playmap") == "PlayMap"
              && ShowNetwork.AppName("main") == "Stagewatch" && ShowNetwork.AppName("") == "show app" && ShowNetwork.AppName("eye") == "eye",
              "words: the app a kind names");

        // Screens: who is a screen, who only claims to be an app, and the apps that follow too.
        var screens = new[]
        {
            new ScreenInfo("screen01", "Lobby", "/viewer", "10.0.0.40", "iPad", T0, false, 1),
            new ScreenInfo("ctl00001", "", "/control", "10.0.0.41", "Windows", T0, true, 1),
            new ScreenInfo(NodeLinks.ClaimId("patterns", "pat9"), "", "/", "10.0.0.42", "Browser", T0, false, 1, "patterns pat9"),
        };
        var apps = new[]
        {
            Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH", links: 2),
            Fact("patterns", "pat2", NodeProof.None, "kP", "Patterns FOH", "w"),
            Fact("messenger", null, NodeProof.None, "kOld", "Old", NodeLinks.NamesNoId),
        };
        Check(Screens.ShowApps(apps) == 3 && Screens.ShowApps(Array.Empty<NodeLinkFact>()) == 0, "screens: one show app per install (its id), or per key that names none");
        Check(Screens.Subtitle(screens, apps, 9924)
                == "1 screen and 1 control page, 1 unproven claim (never in 'all') · 3 show apps follow this Stagewatch too (Producer → Show network); screen commands never reach them. Double-click a name to change it; select screens to find them or send them somewhere else."
              && Screens.Subtitle(screens[..2], Array.Empty<NodeLinkFact>(), 9924)
                == "1 screen and 1 control page. Double-click a name to change it; select screens to find them or send them somewhere else."
              && Screens.Subtitle(Array.Empty<ScreenInfo>(), apps[..1], 9930)
                == "No browser screens connected yet. Open http://<this PC>:9930/viewer on a TV or tablet. 1 show app follows this Stagewatch too (Producer → Show network); screen commands never reach it.",
              $"screens: the subtitle counts browsers, control pages and claims apart, and names the show apps it never reaches ({Screens.Subtitle(screens, apps, 9924)})");
        Check(Screens.ClaimWords("patterns pat9") == "claims to be Patterns pat9 — unproven; never in 'all'"
              && Screens.ClaimWords("messenger ag1") == "claims to be AGMessage ag1 — unproven; never in 'all'",
              "screens: a claim's row says what it claims, unproven, never in 'all'");
        Check(new WsCensus(3, 1, 2, 1).Describe() == "3 browsers, 1 window link, 2 show app links, 1 unproven claim"
              && new WsCensus(1, 0, 1, 0).Describe() == "1 browser, 0 window links, 1 show app link, 0 unproven claims",
              "screens: diagnostics count browsers, windows' links, show apps' links and claims apart");
    }

    /// <summary>The desk's own rows and headline from the join: links apart, grouped by the id they named or by key, and the counts.</summary>
    static void DeskRows(Action<bool, string> Check)
    {
        var now = T0.AddSeconds(2);
        var links = new[]
        {
            Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH"),
            Fact("patterns", "pat2", NodeProof.None, "kP", "Patterns FOH", "this key speaks for patterns pat1 — make a key for patterns pat2", address: "10.0.0.6"),
            Fact("patterns", "pat2", NodeProof.None, "kQ", "Spare", "patterns pat2 speaks with its own key", address: "10.0.0.7", at: 1),
            Fact("messenger", null, NodeProof.None, "kOld", "Old AGMessage", NodeLinks.NamesNoId, address: "10.0.0.12"),
            Fact("messenger", null, NodeProof.None, "kGone", "", NodeLinks.NamesNoId, address: ""),
        };
        var view = ShowNetwork.Summaries(Array.Empty<HeardNode>(), Array.Empty<HeldContext>(), links, "desk01", now);
        var apart = ShowNetwork.ApartRows(view.Apart);
        Check(view.Rows.Select(r => r.Peer.Id).SequenceEqual(new[] { "pat1" }) && apart.Select(a => a.Key).SequenceEqual(new[] { "key:kOld", "key:kGone", "pat2" }),
              $"desk: one row a key proves; apart, one row per key that names no id and one per id a conflict named ({string.Join(", ", apart.Select(a => a.Key))})");
        Check(apart[0] is { Name: "Key 'Old AGMessage'", NodeId: null, Kind: "messenger", Headline: ShowNetwork.ApartNoId }
              && apart[0].LinkLine == "on /ws from 10.0.0.12 with key 'Old AGMessage', which names no node id: it follows the timer and speaks for no node"
              && apart[1] is { Name: "A revoked key", NodeId: null } && !NodeBindings.ValidId(apart[0].Key),
              "desk: a key that names no id is its own row, named by the key, with no node id to copy");
        Check(apart[2] is { Name: "Patterns pat2", NodeId: "pat2", Kind: "patterns", Headline: ShowNetwork.ApartUnproven }
              && apart[2].LinkLine == "on /ws from 10.0.0.6, unverified: this key speaks for patterns pat1 — make a key for patterns pat2 · on /ws from 10.0.0.7, unverified: patterns pat2 speaks with its own key",
              $"desk: a conflict is one row for the id it named, each link saying why it proves nothing ({apart[2].LinkLine})");
        Check(ShowNetwork.NetworkLine(view, refused: 1, refusalOnly: 0) == "Show network: 1 app · 1 key refused · 3 unverified on /ws"
              && ShowNetwork.NetworkLine(NetworkView.Empty, 0, 0) == "" && ShowNetwork.NetworkLine(NetworkView.Empty, 1, 1) == "Show network: 0 apps · 1 key refused",
              $"desk: the headline counts the apps, the keys refused and the rows apart ({ShowNetwork.NetworkLine(view, 1, 0)})");
        var red = new NetworkRow(new PeerSummary("patterns", "pat1", "FOH", Light.Red, "", "", 0, true), null, NodeProof.None, null, Array.Empty<NodeLinkFact>(), true);
        var capped = ShowNetwork.Summaries(Enumerable.Range(1, 3).Select(i => new HeardNode("patterns", "p" + i, "P" + i, "10.0.1." + i, 1)), Array.Empty<HeldContext>(),
            Array.Empty<NodeLinkFact>(), "desk01", now, max: 2);
        Check(ShowNetwork.NetworkLine(new NetworkView(new[] { red }, 0, Array.Empty<NodeLinkFact>()), 0, 0) == "Show network: 1 app · 1 red"
              && ShowNetwork.NetworkLine(capped, 0, 0) == "Show network: 2 apps · +1 more",
              "desk: red apps and the rows past the cap are counted");
    }

    /// <summary>A string field of a JSON row, or null when the row has none (a missing field fails a check, never the run).</summary>
    static string? Field(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>The readers of the join: the context's peers rows and the AI's facts carry link and verified, last in each row.</summary>
    static void Readers(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        nodes.Keep(new[] { NodeKey("kP", null, null) });
        peers.Accept(Context("patterns", "pat1", "Patterns FOH"), "10.0.0.5", T0, "desk01", NodeCredential.Key("kP"));
        var view = ShowNetwork.Summaries(new[] { new HeardNode("playmap", "pm1", "PlayMap stage", "10.0.0.8", 1) }, peers.Held(T0),
            new[] { Fact("patterns", "pat1", NodeProof.Key, "kP", "Patterns FOH"), Fact("patterns", "pat2", NodeProof.None, "kP", "Patterns FOH", "w") }, "desk01", T0);
        var input = new ContextInput("desk01", "Hall A", T0, new ShowSnapshot { Title = "Keynote" }, false, Array.Empty<ContextItem>(),
            view.Rows.Select(r => r.Peer).ToList(), Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 0);
        var json = ShowContext.Build(input);
        using (var doc = JsonDocument.Parse(json))
        {
            var rows = doc.RootElement.GetProperty("peers").EnumerateArray().ToList();
            Check(rows.Count == 2 && Field(rows[0], "id") == "pat1" && Field(rows[0], "link") == "ws" && Field(rows[0], "verified") == "key"
                  && Field(rows[1], "id") == "pm1" && Field(rows[1], "link") == "" && Field(rows[1], "verified") == ""
                  && !json.Contains("pat2", StringComparison.Ordinal),
                  "readers: the context's peers rows say which apps follow over /ws and what proves them; a conflict adds no row");
        }
        Check(json.Contains("\"heard\":\"live\",\"link\":\"ws\",\"verified\":\"key\"}", StringComparison.Ordinal),
              "readers: link and verified come last in a peers row, after the fields readers already match");

        var facts = view.Rows.Select(r => new PeerFact(r.Peer.Kind, r.Peer.Name, r.Peer.Light, r.Peer.Headline, Array.Empty<string>(), Array.Empty<string>(), r.Peer.Link, r.Peer.Verified)).ToList();
        var copilot = AiContext.Copilot(new CopilotFacts(T0, ShowClock.For("UTC"), "{}"u8.ToArray(), "{}"u8.ToArray(),
            new[] { new ScreenFact("screen01", "Lobby", "/viewer", "iPad", false), new ScreenFact(NodeLinks.ClaimId("patterns", "pat9"), "", "/", "Browser", false, "patterns pat9") },
            Array.Empty<ScreenPreset>(), Array.Empty<IntegrationFact>(), 0, OverrunModel.Learn(Array.Empty<HistoryRecord>()), Array.Empty<RundownItem>(), Array.Empty<JournalEntry>(), facts));
        using (var doc = JsonDocument.Parse(copilot))
        {
            var screens = doc.RootElement.GetProperty("screens");
            var apps = doc.RootElement.GetProperty("other_apps");
            Check(Field(screens[0], "claim") == "" && Field(screens[1], "claim") == "patterns pat9"
                  && Field(apps[0], "link") == "ws" && Field(apps[0], "verified") == "key" && Field(apps[1], "verified") == "",
                  "readers: the copilot sees which screen only claims to be an app, and which apps follow the timer and what proves them");
        }
        var schedule = ScheduleEngine.Project(new ScheduleEngine.Input(Array.Empty<RundownItem>(), T0, -1, false, TimeSpan.Zero, new Dictionary<int, ItemActual>(), T0, ShowClock.For("UTC"), TimeSpan.Zero));
        using (var doc = JsonDocument.Parse(AiContext.Assistant(Array.Empty<RundownItem>(), schedule, ShowClock.For("UTC"), T0, -1, TimeSpan.Zero, TimeSpan.Zero, 0, facts)))
        {
            var apps = doc.RootElement.GetProperty("other_apps");
            Check(apps.GetArrayLength() == 2 && Field(apps[0], "link") == "ws" && Field(apps[0], "verified") == "key",
                  "readers: the assistant's compact view carries link and verified too");
        }
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

        // The store's size is UTF-8 bytes, as the door's: a context of two-byte characters within MaxBytes characters but one byte
        // over MaxBytes is refused, and binds nothing; the same context a byte shorter is taken.
        static string Sized(int bytes)
        {
            const string head = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\"},\"headline\":\"", tail = "\"}";
            var room = bytes - Encoding.UTF8.GetByteCount(head + tail);
            return head + new string('\u00e9', room / 2) + new string('a', room % 2) + tail;
        }
        var fits = Sized(PeerContexts.MaxBytes);
        var over = Sized(PeerContexts.MaxBytes + 1);
        Check(Encoding.UTF8.GetByteCount(fits) == PeerContexts.MaxBytes && Encoding.UTF8.GetByteCount(over) == PeerContexts.MaxBytes + 1 && over.Length < PeerContexts.MaxBytes
              && new PeerContexts(nodes).Accept(over, "", T0, null, NodeCredential.Key("kS")) == (400, "a context is 1 byte to 64 KB", NodeProof.None) && nodes.NodeOf("kS") is null
              && new PeerContexts(nodes).Accept(fits, "", T0, null, NodeCredential.Operator).Status == 200,
              $"gate: the store counts a context's UTF-8 bytes: {PeerContexts.MaxBytes} taken, one more refused though its characters are fewer ({over.Length})");
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
