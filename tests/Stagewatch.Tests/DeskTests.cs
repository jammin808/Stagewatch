using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// The desk as one service (Show Bus §11), on an injected clock and a show network built by hand: which app a word names; a person's
/// line — only from a person, only to an app whose verbs are verified and list it, only while it collects, born approved by a role
/// with who and where in the journal file alone, apart from every row's words; what a sent line came to; a person's decision — next
/// the oldest and never a high one, a high one by its id and the word, nothing unticked, nothing for an app that does not collect,
/// never a trigger's, an agent's or the advisor's; an approved line aimed at the desk run here with the run's own outcome; the receipt
/// notes kept apart when one command runs inside another, and a receipt's ids beside its numbers; one risk table for every reader;
/// and the journal's rows — the desk's own kind, each opening with the desk's own word, a refusal repeated one row and a count.
/// </summary>
static class DeskTests
{
    internal static readonly DateTime T0 = new(2026, 10, 28, 19, 0, 0, DateTimeKind.Utc);
    internal const string DeskId = "desk01";

    public static void Run(Action<bool, string> Check)
    {
        Apps(Check);
        Send(Check);
        Results(Check);
        Decide(Check);
        RunHere(Check);
        Receipts(Check);
        Risk(Check);
        Routes(Check);
        Readers(Check);
        Journal(Check);
    }

    /// <summary>A desk on a clock a test moves, with a show network of the nodes a test says it hears and the contexts it posts.</summary>
    internal sealed class Bench
    {
        public readonly NodeBindings Nodes = new();
        public readonly PeerContexts Peers;
        public readonly DeskService Desk;
        public readonly List<HeardNode> Heard = [];
        public readonly List<DeskRow> Rows = [];
        public readonly List<SharedProposal> Changes = [];
        public readonly Dictionary<string, PeerContexts.Entry> Linked = new(StringComparer.Ordinal);
        public DateTime Now = T0;
        public Func<ShowCommand, string, DeskRun>? RunHere;

        public Bench(string run = "k3m9", TimeSpan? fresh = null)
        {
            Peers = new PeerContexts(Nodes);
            Desk = new DeskService(new DeskHost
            {
                Id = () => DeskId,
                Name = () => "Hall A",
                Nodes = Nodes,
                Contexts = Peers,
                Network = () => ShowNetwork.Summaries(Heard, Peers.Speaking(Linked.Values.Select(e => new HeldContext(e, NodeProof.None, Pulled: true)), Now),
                    Array.Empty<NodeLinkFact>(), DeskId, Now),
                Linked = t => Linked.GetValueOrDefault(t.Id),
                WindowTokens = () => ["w7kq2m9xw7kq2m9xw7kq2m9xw7"],
                Now = () => Now,
                Fresh = fresh ?? ProposalDesk.CollectFresh,
                Run = run,
                RunHere = (cmd, id) => RunHere is { } run ? run(cmd, id) : new DeskRun(true, "ran"),
            });
            // A test that drives the desk from more than one thread (a web server's, an app's collects) reads the rows under their lock.
            Desk.Journalled += row => { lock (Rows) Rows.Add(row); };
            Desk.Changed += change => { lock (Changes) Changes.Add(change); };
        }

        public void Hear(string kind, string id, string name, double age = 1) => Heard.Add(new HeardNode(kind, id, name, "10.0.0.9", age));

        /// <summary>The node posts its context, with its verbs, under <paramref name="credential"/>.</summary>
        public NodeProof Post(string kind, string id, string name, NodeCredential credential, params (string Verb, string Risk)[] verbs)
            => Peers.Accept(Context(kind, id, name, verbs), "10.0.0.9", Now, DeskId, credential).Proof;

        public string Last => Rows.Count > 0 ? Rows[^1].Words : "";

        /// <summary>Who and from where the last row keeps apart from its words.</summary>
        public string LastWho => Rows.Count > 0 ? Rows[^1].Who : "";

        /// <summary>The desk's rows as a host journals them.</summary>
        public List<JournalEntry> Journal() => Rows.Select(r => r.Entry()).ToList();
    }

    static string Context(string kind, string id, string name, params (string Verb, string Risk)[] verbs) => JsonSerializer.Serialize(new
    {
        v = 1,
        node = new { t = kind, id, name },
        commands = verbs.Select(c => new { verb = c.Verb, risk = c.Risk, words = "what " + c.Verb.Split(' ')[0] + " does" }).ToArray(),
    });

    internal static readonly (string, string)[] PlayMapVerbs =
    [
        ("PLAYLIST CUE <playlist> <item>|#<n>", "review"), ("UNDO", "review"), ("PANIC", "high"), ("STATUS", "low"),
        ("LINK STAGEWATCH <host[:port]>|\"\"|OFF [<key>]", "review"), ("MESSAGE <text>", "low"), ("BIND LEARN <target line>", "review"),
        ("BLACKOUT ON|OFF", "high"), ("LABEL <words>", "low"), ("NOTE ADD <note>", "review"), ("OUTPUT NAME <output> <name>", "low"),
    ];

    static string Proposal(string fromKind, string fromId, string fromName, string targetKind, string targetId, string targetName, string line, string reason = "")
        => JsonSerializer.Serialize(new { from = new { t = fromKind, id = fromId, name = fromName }, target = new { t = targetKind, id = targetId, name = targetName }, line, reason });

    /// <summary>The words of a route's refusal: its JSON's <c>error</c>.</summary>
    static string Error(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("error").GetString() ?? "";
    }

    /// <summary>The usual room: PlayMap Hall (verified by its key), Patterns FOH (verified) and AGMessage (verified), all heard.</summary>
    internal static Bench Room()
    {
        var b = new Bench();
        b.Hear("playmap", "pm1", "PlayMap Hall");
        b.Hear("patterns", "pat1", "Patterns FOH");
        b.Hear("messenger", "ag1", "AGMessage");
        b.Post("playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"), PlayMapVerbs);
        b.Post("patterns", "pat1", "Patterns FOH", NodeCredential.Key("kP"), ("APPLY <id>", "review"), ("EYE FOCUS <thing>", "low"));
        b.Post("messenger", "ag1", "AGMessage", NodeCredential.Key("kA"), ("notice <text>", "review"));
        return b;
    }

    /// <summary>Which app a word names: an id, a name one app has, a kind word one app answers; never this desk, and never a guess.</summary>
    static void Apps(Action<bool, string> Check)
    {
        var b = Room();
        var apps = b.Desk.Apps();
        Check(apps.Select(a => a.Id).Order(StringComparer.Ordinal).SequenceEqual(["ag1", "pat1", "pm1"]) && apps.All(a => a.Verified && !a.Collects)
              && apps.First(a => a.Id == "pm1") is { Name: "PlayMap Hall", Kind: "playmap" },
            "desk apps: every show app heard is listed with its name and kind, verified by its keyed context, not collecting until it asks");
        Check(b.Desk.Resolve("pm1") is ({ Id: "pm1" }, null) && b.Desk.Resolve("playmap hall") is ({ Id: "pm1" }, null) && b.Desk.Resolve("playmap") is ({ Id: "pm1" }, null)
              && b.Desk.Resolve("PlayMap") is ({ Id: "pm1" }, null) && b.Desk.Resolve("patterns") is ({ Id: "pat1" }, null)
              && b.Desk.Resolve("agmessage") is ({ Id: "ag1" }, null) && b.Desk.Resolve("messenger") is ({ Id: "ag1" }, null),
            "desk apps: a node id, a name whatever its case, and a kind word (playmap, patterns, agmessage or messenger) each name the one app that answers");
        Check(b.Desk.Resolve("pm") is (null, "no app called 'pm' on the show network — heard: AGMessage (messenger), Patterns FOH (patterns), PlayMap Hall (playmap)"),
            $"desk apps: an unknown word names no app, and the words list who is heard ({b.Desk.Resolve("pm").Error})");
        Check(b.Desk.Resolve("stagewatch") is (null, "'stagewatch' is this Stagewatch — a line goes to another show app")
              && b.Desk.Resolve("main").App is null && b.Desk.Resolve("Stagewatch").App is null && b.Desk.Resolve(DeskId) is (null, { } own) && own.Contains("this Stagewatch", StringComparison.Ordinal)
              && b.Desk.Resolve("").App is null,
            "desk apps: stagewatch, main and the desk's own id are this desk, never an app word");
        b.Hear("playmap", "pm2", "PlayMap Foyer");
        b.Hear("main", "desk02", "Stagewatch B");
        b.Hear("agent", "ppt1", "Slides PC");
        Check(b.Desk.Resolve("playmap") is (null, "two apps answer 'playmap' — name one: PlayMap Foyer, PlayMap Hall") && b.Desk.Resolve("PlayMap Foyer") is ({ Id: "pm2" }, null)
              && b.Desk.Resolve("pm2") is ({ Id: "pm2", Verified: false }, null),
            $"desk apps: a kind word two apps answer is refused naming both, and each is still named by its name or id ({b.Desk.Resolve("playmap").Error})");
        Check(b.Desk.Resolve("desk02").App is null && b.Desk.Resolve("Stagewatch B").App is null && b.Desk.Resolve("ppt1").App is null && b.Desk.Apps().Count == 4,
            "desk apps: another Stagewatch and a Slides agent are not apps a line goes to");
        b.Hear("patterns", "pat2", "Patterns FOH");
        Check(b.Desk.Resolve("Patterns FOH") is (null, { } twins) && twins == "two apps answer 'Patterns FOH' — name one by its id: Patterns FOH (pat1), Patterns FOH (pat2)"
              && b.Desk.Resolve("pat2") is ({ Id: "pat2" }, null),
            "desk apps: a name two apps share is refused with their ids, and an id still names one");
        // A name that is also another app's kind word is two answers: refused, both named by id — never the name's app by default.
        var n = new Bench();
        n.Hear("patterns", "pat5", "PlayMap");
        n.Hear("playmap", "pm5", "PlayMap Hall");
        Check(n.Desk.Resolve("playmap") is (null, "'playmap' is PlayMap's name and the kind of PlayMap Hall — name one by its id: PlayMap (pat5), PlayMap Hall (pm5)")
              && n.Desk.Resolve("pat5") is ({ Id: "pat5" }, null) && n.Desk.Resolve("pm5") is ({ Id: "pm5" }, null) && n.Desk.Resolve("patterns") is ({ Id: "pat5" }, null)
              && n.Desk.Resolve("PlayMap Hall") is ({ Id: "pm5" }, null),
            $"desk apps: a word that is one app's name and another's kind names neither; each is still named by its id ({n.Desk.Resolve("playmap").Error})");
        var self = new Bench();
        self.Hear("playmap", "pm6", "playmap");
        self.Hear("patterns", "pat6", "Patterns FOH");
        Check(self.Desk.Resolve("playmap") is ({ Id: "pm6" }, null) && self.Desk.Resolve("PlayMap") is ({ Id: "pm6" }, null),
            "desk apps: an app named for its own kind, the only one of it, is named by that word");
        var gone = new Bench();
        gone.Hear("playmap", "pm1", "PlayMap Hall", age: 30);
        Check(gone.Desk.Apps().Count == 0 && gone.Desk.Resolve("playmap") is (null, "no app called 'playmap' on the show network — none is heard"),
            "desk apps: an app not heard any more is not listed, and the words say none is heard");
    }

    /// <summary>A person's line to another app: every refusal in words with nothing queued, and the one that goes born approved.</summary>
    static void Send(Action<bool, string> Check)
    {
        var b = Room();
        var wire = DeskOrigin.Wire("key 'FOH tablet', 10.0.0.7");
        foreach (var notAPerson in new[] { DeskOrigin.Mcp, DeskOrigin.Advisor, DeskOrigin.Copilot, DeskOrigin.Proposal, new DeskOrigin("FOH tablet") })
            Check(b.Desk.Send("playmap", "UNDO", notAPerson) is { Ok: false, Words: DeskService.PersonSends, Proposal: null } && b.Rows.Count == 0,
                $"desk send: a line from {notAPerson.Role} is refused — a person sends it — and nothing is queued or journalled");
        Check(b.Desk.Send("pm", "UNDO", wire) is { Ok: false, Proposal: null } unknown && unknown.Words.StartsWith("no app called 'pm'", StringComparison.Ordinal) && b.Rows.Count == 0,
            "desk send: a word that names no app is refused in the resolver's words");
        Check(b.Desk.Send("playmap", "UNDO", wire) is { Ok: false, Words: "PlayMap Hall has not asked this desk for approved lines since this desk started — it would not collect this" }
              && b.Last == "Refused to send to PlayMap Hall (wire): PlayMap Hall has not asked this desk for approved lines since this desk started — it would not collect this"
              && b.LastWho == "key 'FOH tablet', 10.0.0.7" && b.Rows.Count == 1,
            $"desk send: an app that never asked for approved lines is refused; the row says the role and why, and keeps who tried apart from its words ({b.Last})");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        b.Now = T0.AddSeconds(16);
        b.Post("playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"), PlayMapVerbs);
        Check(b.Desk.Send("playmap", "UNDO", wire) is { Ok: false, Words: "PlayMap Hall has not asked this desk for approved lines in 16 s — it would not collect this" }
              && b.Desk.Queue.All().Count == 0 && b.Changes.Count == 0 && b.Rows.Count == 1,
            "desk send: 16 s after its last collect the app is not collecting, the line is refused and nothing is queued — the same refusal again is no second row");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        b.Now = T0.AddSeconds(19);
        var before = b.Rows.Count;
        var sent = b.Desk.Send("PlayMap Hall", " PLAYLIST CUE show #3 ", wire, "the operator's cue");
        Check(sent is { Ok: true, Words: "sent to PlayMap Hall as p1 — it collects it and reports back" }
              && sent.Proposal is { Id: "p1-k3m9", State: SharedProposalState.Approved, DecidedBy: "wire", Line: "PLAYLIST CUE show #3", Risk: "review", TargetIsDesk: false }
              && sent.Proposal.From == new NodeRef("main", DeskId, "Hall A") && sent.Proposal.Target == new NodeRef("playmap", "pm1", "PlayMap Hall")
              && sent.Proposal.Form == "PLAYLIST CUE <playlist> <item>|#<n>" && b.Changes is [{ State: SharedProposalState.Approved }],
            $"desk send: a collect 3 s ago — the listed line is born approved from this desk, decided by the role wire, rated by its form ({sent.Words})");
        var json = b.Desk.GetProposal("p1").Json;
        Check(b.Rows.Count == before + 1 && b.Last == "Sent p1 to PlayMap Hall (wire): PLAYLIST CUE show #3" && b.LastWho == "key 'FOH tablet', 10.0.0.7"
              && json.Contains("\"decidedBy\":\"wire\"", StringComparison.Ordinal) && !json.Contains("FOH tablet", StringComparison.Ordinal) && !json.Contains("10.0.0.7", StringComparison.Ordinal),
            $"desk send: the row's words hold the role alone, the key's name and the address beside them; the proposal every reader sees holds the role alone ({b.Last})");
        Check(b.Desk.Send("playmap", "OUTPUTS OFF", wire) is { Ok: false, Words: "PlayMap Hall does not list 'OUTPUTS OFF' — see its verbs" }
              && b.Last == "Refused to send to PlayMap Hall (wire): PlayMap Hall does not list that line — see its verbs"
              && b.Desk.Send("playmap", "PAIR hunter2", wire) is { Ok: false, Words: "PlayMap Hall does not list 'PAIR hunter2' — see its verbs" }
              && b.Rows.Count == before + 2 && !b.Rows.Any(r => r.Words.Contains("OUTPUTS", StringComparison.Ordinal) || r.Words.Contains("hunter2", StringComparison.Ordinal)),
            $"desk send: a line its verbs do not list is refused — quoted to its sender, never to the journal, where it might hold a secret no form marks ({b.Last})");
        var rowsBefore = b.Rows.Count;
        var secret = b.Desk.Send("playmap", "LINK STAGEWATCH host hunter2", wire);
        var key = b.Desk.Send("playmap", "LINK STAGEWATCH host swk_x", wire);
        var window = b.Desk.Send("playmap", "MESSAGE see /w/w7kq2m9xw7kq2m9xw7kq2m9xw7", wire);
        var control = b.Desk.Send("playmap", "MESSAGE hi\nPANIC", wire);
        var longLine = b.Desk.Send("playmap", "MESSAGE " + new string('x', LineFence.MaxChars), wire);
        Check(secret is { Ok: false } && secret.Words == LineFence.SecretWords("PlayMap Hall") && key is { Ok: false, Words: LineFence.Key }
              && window is { Ok: false, Words: LineFence.Window } && control is { Ok: false, Words: LineFence.Control } && longLine is { Ok: false } && longLine.Words == LineFence.TooLong
              && b.Desk.Send("playmap", "  ", wire) is { Ok: false, Words: "line is required" },
            "desk send: a filled secret, a key, a window's address, a control character, a line too long and an empty one are refused in the fence's words");
        Check(b.Rows.Count == rowsBefore + 6 && b.Rows.Skip(rowsBefore).All(r => r.Words.StartsWith("Refused to send to PlayMap Hall (wire): ", StringComparison.Ordinal)
                  && !r.Words.Contains("hunter2", StringComparison.Ordinal) && !r.Words.Contains("swk_", StringComparison.Ordinal) && !r.Words.Contains("w7kq2m9x", StringComparison.Ordinal))
              && b.Desk.Queue.All().Count == 1,
            "desk send: a refusal's journal row never quotes the line, so no secret, key or window address reaches the journal; nothing more is queued");
        Check(b.Desk.Send("playmap", "LINK STAGEWATCH host ‹secret›", wire) is { Ok: true, Proposal.Line: "LINK STAGEWATCH host ‹secret›" },
            "desk send: the marker in a secret's place is sent as it stands");
        var trigger = b.Desk.Send("pm1", "STATUS", DeskOrigin.Trigger());
        var deck = b.Desk.Send("pm1", "STATUS", DeskOrigin.Deck("Stream Deck, 10.0.0.9"));
        var op = b.Desk.Send("pm1", "STATUS", DeskOrigin.Operator);
        Check(trigger.Proposal is { DecidedBy: "trigger", Risk: "low" } && deck.Proposal is { DecidedBy: "deck" } && op.Proposal is { DecidedBy: "operator" }
              && b.Last == "Sent p5 to PlayMap Hall (operator): STATUS" && b.LastWho == "" && b.Rows[^2] is { Words: "Sent p4 to PlayMap Hall (deck): STATUS", Who: "Stream Deck, 10.0.0.9" },
            "desk send: a trigger, a deck and the operator each send as their own role; with nobody to name, the row keeps no who");

        // Unverified: a context posted with no credential, and an app that posted none.
        var u = new Bench();
        u.Hear("playmap", "pm7", "PlayMap Side");
        u.Hear("patterns", "pat7", "Patterns Side");
        u.Post("playmap", "pm7", "PlayMap Side", NodeCredential.None, PlayMapVerbs);
        u.Desk.Queue.Collect("pm7", NodeCredential.Operator, u.Now);
        Check(u.Desk.Apps().First(a => a.Id == "pm7") is { Verified: false }
              && u.Desk.Send("pm7", "STATUS", wire) is { Ok: false, Words: "PlayMap Side's verbs are not verified (posted without its key) — Stagewatch sends only lines it can read" }
              && u.Desk.Send("pat7", "EYE FOCUS x", wire) is { Ok: false, Words: "Patterns Side has told this desk no verbs — Stagewatch sends only lines it can read" }
              && u.Desk.Send("pm7", "LINK STAGEWATCH h hunter2", wire) is { Ok: false } stillSecret && stillSecret.Words == LineFence.SecretWords("PlayMap Side")
              && u.Desk.Queue.All().Count == 0,
            "desk send: an app whose verbs prove nothing, or that told none, is sent no line — and a secret is still refused as a secret");
        // A context read over this Stagewatch's own link is verified while no key speaks for the id.
        var l = new Bench();
        l.Hear("patterns", "pat9", "Patterns Link");
        l.Linked["pat9"] = PeerContexts.Parse(Context("patterns", "pat9", "Patterns Link", ("VIDEO STOP", "high")), "10.0.0.5", l.Now)!;
        l.Desk.Queue.Collect("pat9", NodeCredential.Operator, l.Now);
        Check(l.Desk.Apps() is [{ Id: "pat9", Verified: true, Collects: true }] && l.Desk.Send("patterns", "VIDEO STOP", DeskOrigin.Operator) is { Ok: true, Proposal.Risk: "high" },
            "desk send: verbs read over this Stagewatch's own link are verified, and a line they list is sent at its row's risk");
    }

    /// <summary>What a sent line came to: sent, collected, done or refused in the app's words, undelivered; a proposal pending or rejected; no id, nothing.</summary>
    static void Results(Action<bool, string> Check)
    {
        var b = Room();
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        var id = b.Desk.Send("playmap", "PLAYLIST CUE show #3", DeskOrigin.Operator).Proposal!.Id;
        Check(b.Desk.ResultOf("p1") is { Id: "p1-k3m9", Stage: "sent", Ok: null, Settled: false, Words: "sent to PlayMap Hall as p1 — it has not collected it yet" },
            "desk result: a line just sent is sent, not settled");
        b.Now = T0.AddSeconds(2);
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        Check(b.Desk.ResultOf(id) is { Stage: "collected", Ok: null, Settled: false, Words: "PlayMap Hall has p1 — waiting for its answer" },
            "desk result: once its app collects it, it is collected and still not settled");
        b.Desk.ProposalResult(id, "{\"ok\":true,\"words\":\"cued Intro\"}", NodeCredential.Key("kM"));
        Check(b.Desk.ResultOf(id) is { Stage: "done", Ok: true, Settled: true, Words: "PlayMap Hall ran PLAYLIST CUE show #3: cued Intro (p1)" }
              && b.Last == "Outcome p1: PlayMap Hall: PLAYLIST CUE show #3 — done (cued Intro)",
            $"desk result: done in the app's own words, and the journal has the outcome ({b.Last})");
        var second = b.Desk.Send("playmap", "PLAYLIST CUE show #9", DeskOrigin.Operator).Proposal!.Id;
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        b.Desk.ProposalResult(second, "{\"ok\":false,\"words\":\"Show has no item #9\"}", NodeCredential.Key("kM"));
        Check(b.Desk.ResultOf("p2") is { Stage: "refused", Ok: false, Settled: true, Words: "PlayMap Hall refused PLAYLIST CUE show #9: Show has no item #9 (p2)" },
            "desk result: a line its app refused is refused in the app's words");
        var third = b.Desk.Send("playmap", "UNDO", DeskOrigin.Operator).Proposal!.Id;
        b.Now = b.Now + ProposalDesk.CollectWithin + TimeSpan.FromSeconds(1);
        b.Desk.Expire();
        Check(b.Desk.ResultOf(third) is { Stage: "undelivered", Ok: false, Settled: true, Words: "p3 was not delivered: PlayMap Hall didn't collect it" },
            "desk result: a line never collected is undelivered, said so");
        b.Post("patterns", "pat1", "Patterns FOH", NodeCredential.Key("kP"), ("EYE FOCUS <thing>", "low"));
        b.Desk.SubmitProposal(Proposal("patterns", "pat1", "Patterns FOH", "patterns", "pat1", "Patterns FOH", "EYE FOCUS screen 2"), NodeCredential.Key("kP"));
        Check(b.Desk.ResultOf("p4") is { Stage: "pending", Ok: null, Settled: false, Words: "p4 waits for a person's approval" },
            "desk result: a proposal no person decided is pending");
        b.Desk.Decide("p4", approve: false, DeskOrigin.Operator);
        Check(b.Desk.ResultOf("p4") is { Stage: "rejected", Ok: false, Settled: true } && b.Desk.ResultOf("p9") is null && b.Desk.ResultOf("p1-zzzz") is null,
            "desk result: a rejected one is rejected, and an id that names no proposal has no result");
    }

    /// <summary>A person's decision: who may, which proposal, the tick, the word for a high one, and an app that does not collect.</summary>
    static void Decide(Action<bool, string> Check)
    {
        var b = Room();
        var deck = DeskOrigin.Deck("Stream Deck, 10.0.0.9");
        Check(b.Desk.Decide("next", true, DeskOrigin.Operator) is { Ok: false, Words: "nothing is waiting for a decision", Proposal: null }
              && b.Desk.Decide("p7", true, DeskOrigin.Operator) is { Ok: false, Words: "no such proposal" }
              && b.Desk.Decide("p7-zzzz", true, DeskOrigin.Operator).Words == b.Desk.Queue.Unknown("p7-zzzz"),
            "desk decide: with nothing waiting, next says so; an id that names none is refused in the queue's words");
        string Ask(string line, string target = "pm1") => b.Desk.SubmitProposal(target == DeskId
            ? Proposal("messenger", "ag1", "AGMessage", "main", "", "", line)
            : Proposal("messenger", "ag1", "AGMessage", "playmap", target, "PlayMap Hall", line), NodeCredential.Key("kA")).Json;
        Ask("UNDO");
        b.Now = T0.AddSeconds(1);
        Ask("PANIC");
        b.Now = T0.AddSeconds(2);
        Ask("STATUS");
        Check(b.Desk.Queue.Get("p1") is { Risk: "review" } && b.Desk.Queue.Get("p2") is { Risk: "high" } && b.Desk.Queue.Get("p3") is { Risk: "low" }
              && b.Rows[0].Words == "Proposed p1: AGMessage → PlayMap Hall: UNDO" && b.Rows.Count == 3,
            $"desk decide: each proposal is rated by its target's verified table and journalled as proposed ({b.Rows[0].Words})");
        foreach (var notAPerson in new[] { DeskOrigin.Trigger(), DeskOrigin.Mcp, DeskOrigin.Advisor, DeskOrigin.Copilot, DeskOrigin.Proposal })
            Check(b.Desk.Decide("next", true, notAPerson) is { Ok: false, Words: DeskService.PersonDecides, Proposal: null }
                  && b.Desk.Decide("p1", false, notAPerson) is { Ok: false, Words: DeskService.PersonDecides } && b.Desk.Decide("p9", true, notAPerson).Words == DeskService.PersonDecides
                  && b.Desk.Queue.Get("p1") is { State: SharedProposalState.Pending },
                $"desk decide: {notAPerson.Role} neither approves nor rejects — a person does — and learns nothing of the proposal");
        Check(b.Rows.Count(r => r.Words.StartsWith("Not approved p1 (", StringComparison.Ordinal) && r.Words.EndsWith(DeskService.PersonDecides, StringComparison.Ordinal)) == 5
              && b.Rows.Any(r => r.Words == "Not rejected p1 (mcp): AGMessage → PlayMap Hall: UNDO — " + DeskService.PersonDecides),
            "desk decide: each such attempt on a waiting proposal is in the journal");
        // Not collecting: PlayMap has never asked.
        Check(b.Desk.Decide("next", true, deck) is { Ok: false, Proposal.State: SharedProposalState.Pending } quiet
              && quiet.Words == "PlayMap Hall has not asked this desk for approved lines since this desk started — it would not collect this"
              && b.Last == "Not approved p1 (deck): AGMessage → PlayMap Hall: UNDO — " + quiet.Words && b.LastWho == "Stream Deck, 10.0.0.9",
            $"desk decide: an approval for an app that is not collecting is refused, the proposal still waits, and the journal says why ({b.Last})");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        b.Now = T0.AddSeconds(18.5);
        var quietRows = b.Rows.Count;
        Check(b.Desk.Decide("p1", true, deck) is { Ok: false } stale && stale.Words == "PlayMap Hall has not asked this desk for approved lines in 16 s — it would not collect this"
              && b.Rows.Count == quietRows,
            "desk decide: 16 s after its last collect, an approval for it is refused the same way — and is no second row");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        var rows = b.Rows.Count;
        Check(b.Desk.Decide("next", true, deck, ticked: false) is { Ok: false, Words: DeskService.NothingTicked, Proposal.State: SharedProposalState.Pending }
              && b.Desk.Decide("p2", true, deck, high: true, ticked: false) is { Ok: false, Words: DeskService.NothingTicked }
              && b.Rows.Count == rows && b.Desk.Summary().Pending == 3,
            "desk decide: an unticked approval approves nothing — every proposal still waits, and nothing is journalled");
        var first = b.Desk.Decide("next", true, deck);
        Check(first is { Ok: true, Words: "approved p1 (AGMessage → PlayMap Hall: UNDO) — PlayMap Hall runs it and reports back" }
              && first.Proposal is { Id: "p1-k3m9", State: SharedProposalState.Approved, DecidedBy: "deck" }
              && b.Last == "Approved p1 (deck): AGMessage → PlayMap Hall: UNDO" && b.LastWho == "Stream Deck, 10.0.0.9",
            $"desk decide: next approves the oldest waiting proposal, decided by the role, journalled with who and where beside its words ({b.Last})");
        Check(b.Desk.Decide("next", true, deck) is { Ok: false, Words: "p2 is high — approve it by its id: desk approve p2-k3m9 high", Proposal.State: SharedProposalState.Pending }
              && b.Desk.Decide("p2", true, deck) is { Ok: false, Words: "p2 is high — approve it by its id: desk approve p2-k3m9 high" }
              && b.Last == "Not approved p2 (deck): AGMessage → PlayMap Hall: PANIC — it is high, and takes its id and the word high",
            "desk decide: next never approves a high proposal, nor does its id alone — the words name the id and the word to say");
        Check(b.Desk.Decide("next", true, deck, high: true) is { Ok: false, Words: "p2 is high — approve it by its id: desk approve p2-k3m9 high", Proposal.State: SharedProposalState.Pending }
              && b.Desk.Decide("NEXT", true, DeskOrigin.Operator, high: true) is { Ok: false, Proposal.State: SharedProposalState.Pending }
              && b.Desk.Queue.Get("p2") is { State: SharedProposalState.Pending, DecidedBy: "" },
            "desk decide: next with the word high still approves no high proposal — it takes its id, whoever says it");
        Check(b.Desk.Decide("p2-k3m9", true, deck, high: true) is { Ok: true, Proposal: { State: SharedProposalState.Approved, Risk: "high" } }
              && b.Desk.Decide("p2", true, deck, high: true) is { Ok: false, Words: "p2 is approved" },
            "desk decide: its id and the word high approve it, once");
        var rejected = b.Desk.Decide("next", false, DeskOrigin.Wire("PIN, 10.0.0.7"), why: "not\u0007 now");
        Check(rejected is { Ok: true, Words: "rejected p3 (AGMessage → PlayMap Hall: STATUS)", Proposal: { State: SharedProposalState.Rejected, DecidedBy: "wire" } }
              && b.Last == "Rejected p3 (wire): AGMessage → PlayMap Hall: STATUS — not now" && b.LastWho == "PIN, 10.0.0.7"
              && b.Desk.Decide("p3", false, deck) is { Ok: false, Words: "p3 is rejected" } && b.Desk.Summary() is { Pending: 0, Next: null },
            $"desk decide: reject next rejects the oldest waiting, with its reason in the journal as words alone ({b.Last})");
        Check(b.Desk.CollectApproved("pm1", NodeCredential.Key("kM")).Json is { } due && due.Contains("\"line\":\"UNDO\"", StringComparison.Ordinal)
              && due.Contains("\"line\":\"PANIC\"", StringComparison.Ordinal) && !due.Contains("STATUS", StringComparison.Ordinal) && !due.Contains("Stream Deck", StringComparison.Ordinal),
            "desk decide: its app collects the two approved lines, never the rejected one, and reads the role alone");
        Check(b.Changes.Select(c => (DeskService.Short(c.Id), c.State)).SequenceEqual([
                ("p1", SharedProposalState.Pending), ("p2", SharedProposalState.Pending), ("p3", SharedProposalState.Pending),
                ("p1", SharedProposalState.Approved), ("p2", SharedProposalState.Approved), ("p3", SharedProposalState.Rejected)]),
            "desk decide: every proposal and every decision raised one change, and a refusal none");
    }

    /// <summary>An approved line aimed at this desk runs here through the host, and the decision is what that run did.</summary>
    static void RunHere(Action<bool, string> Check)
    {
        var b = Room();
        var ran = new List<(ShowCommandKind Kind, string Id)>();
        b.RunHere = (cmd, id) =>
        {
            ran.Add((cmd.Kind, id));
            return cmd.Kind == ShowCommandKind.Note ? new DeskRun(true, "Note added") : new DeskRun(false, "There is no running order");
        };
        string Ask(string line) => b.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "main", "", "", line), NodeCredential.Key("kA")).Json;
        Ask("note \"From the board\"");
        Ask("next");
        Ask("sing a song");
        Check(b.Desk.Queue.Get("p1") is { Risk: "low", TargetIsDesk: true, Form: null } && b.Desk.Queue.Get("p2") is { Risk: "high" } && b.Desk.Queue.Get("p3") is { Risk: "high" }
              && b.Desk.Summary().Next is { Id: "p1-k3m9", Target: "this Stagewatch", Collects: true },
            "desk here: a line aimed at this desk is rated by the desk's own table, one its parser does not take high; the desk always collects its own");
        var done = b.Desk.Decide("next", true, DeskOrigin.Operator);
        Check(done is { Ok: true, Words: "approved p1 — ran here: Note added", Proposal: { State: SharedProposalState.Done, Result: "Note added" } }
              && ran is [(ShowCommandKind.Note, "p1-k3m9")] && b.Rows[^2].Words == "Approved p1 (operator): AGMessage → this Stagewatch: note \"From the board\""
              && b.Last == "Outcome p1: this Stagewatch: note \"From the board\" — done (Note added)",
            $"desk here: approved, it runs here at once under its own id and is done in the run's words ({b.Last})");
        var refused = b.Desk.Decide("p2", true, DeskOrigin.Operator, high: true);
        Check(refused is { Ok: false, Words: "There is no running order", Proposal: { State: SharedProposalState.Failed, Result: "There is no running order" } }
              && ran.Count == 2 && b.Desk.ResultOf("p2") is { Stage: "refused", Words: "this Stagewatch refused next: There is no running order (p2)" },
            "desk here: a run the show refused is the decision's own refusal, in the run's words, and the proposal failed");
        var invalid = b.Desk.Decide("p3", true, DeskOrigin.Operator, high: true);
        Check(invalid is { Ok: false, Proposal.State: SharedProposalState.Failed } && invalid.Words.StartsWith("not a valid Stagewatch command: ", StringComparison.Ordinal) && ran.Count == 2,
            "desk here: a line that is no Stagewatch command fails in words and runs nothing");
        // Handed to a playout link's peer: finished by that peer's final outcome, whenever it comes.
        var later = new TaskCompletionSource<(bool, string)>();
        b.RunHere = (_, _) => new DeskRun(true, "handed to Patterns FOH — waiting for its answer", later.Task);
        Ask("playout end");
        var handed = b.Desk.Decide("p4", true, DeskOrigin.Operator, high: true);
        Check(handed is { Ok: true, Words: "approved p4 — handed to Patterns FOH — waiting for its answer", Proposal.State: SharedProposalState.Approved }
              && b.Desk.ResultOf("p4") is { Stage: "collected", Settled: false },
            "desk here: a line handed to a peer is approved and not finished — never a done the peer did not say");
        later.SetResult((false, "unconfirmed — no final answer — it may have run"));
        Check(SpinWait.SpinUntil(() => b.Desk.Queue.Get("p4") is { State: SharedProposalState.Failed }, TimeSpan.FromSeconds(5))
              && b.Desk.Queue.Get("p4") is { Result: "unconfirmed — no final answer — it may have run" },
            "desk here: the peer's final outcome finishes it, in its words");
        b.RunHere = (_, _) => new DeskRun(true, "handed on", Task.FromException<(bool, string)>(new InvalidOperationException("the link closed")));
        Ask("playout end");
        b.Desk.Decide("p5", true, DeskOrigin.Operator, high: true);
        Check(SpinWait.SpinUntil(() => b.Desk.Queue.Get("p5") is { State: SharedProposalState.Failed }, TimeSpan.FromSeconds(5))
              && b.Desk.Queue.Get("p5")!.Result.Contains("the link closed", StringComparison.Ordinal),
            "desk here: an outcome that cannot be read fails the proposal, never leaves it approved for ever");
        // A host whose run throws: the proposal fails in words — approved, it is nobody's to collect, and would wait for ever.
        b.RunHere = (_, _) => throw new InvalidOperationException("the show is closing");
        Ask("note \"late\"");
        var threw = b.Desk.Decide("p6", true, DeskOrigin.Operator);
        b.Now = T0.AddHours(1);
        b.Desk.Expire();
        Check(threw is { Ok: false, Words: "it could not be run here: the show is closing", Proposal: { State: SharedProposalState.Failed, Result: "it could not be run here: the show is closing" } }
              && b.Desk.Queue.Get("p6") is { State: SharedProposalState.Failed } && b.Last == "Outcome p6: this Stagewatch: note \"late\" — failed (it could not be run here: the show is closing)",
            $"desk here: a run that throws fails the proposal in words, and the decision returns them ({b.Last})");
        b.Now = T0;
        // A proposal may not carry a person's verb or a desk-only line to the desk.
        Check(b.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "main", "", "", "desk approve next"), NodeCredential.Key("kA")).Status == 400
              && b.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "main", "", "", "playout vt trust 03e9b152"), NodeCredential.Key("kA")).Status == 400,
            "desk here: a person's verb and a desk-only line are never proposals for this desk");
    }

    /// <summary>The receipt notes are a stack: an inner command's refusal, refs and peer are its own, and the outer receipt keeps what it noted.</summary>
    static void Receipts(Action<bool, string> Check)
    {
        var stack = new ReceiptStack<string>();
        stack.Ref("lost", 1);
        stack.Refused("nobody hears this");
        Check(stack.Current is null && !stack.Remote && stack.Depth == 0, "receipts: with no command being receipted, a note lands nowhere");

        // The outer command is a person's approval of a line aimed at this desk; the inner one is that line, run through the desk.
        var b = Room();
        CommandReceipt? inner = null;
        b.RunHere = (cmd, id) =>
        {
            var notes = stack.Enter();
            var depth = stack.Depth;
            stack.Ref("signalId", 7);
            stack.Refused("There is no running order");
            stack.Refused("a second reason");
            stack.Peer("a peer of the inner command");
            stack.Leave(notes);
            inner = notes.Receipt(id, "");
            return new DeskRun(inner.Ok && depth == 2, inner.Error ?? inner.Effect);
        };
        b.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "main", "", "", "next"), NodeCredential.Key("kA"));
        var outer = stack.Enter();
        stack.Ref("messageId", 12);
        stack.RefId("sharedProposal", "p1-k3m9");
        var decided = b.Desk.Decide("p1", true, DeskOrigin.Wire(), high: true);
        Check(inner is { Ok: false, Error: "There is no running order", Id: "p1-k3m9" } && inner.Refs is { Count: 1 } innerRefs && innerRefs["signalId"] == 7 && inner.RefIds is null,
            "receipts: the inner command's receipt is its own — its first refusal, its own refs, none of the outer's");
        Check(stack.Current == outer && stack.Depth == 1 && stack.Remote && outer.Error is null && outer.Peer is null
              && outer.Refs is { Count: 1 } kept && kept["messageId"] == 12 && outer.RefIds is { Count: 1 },
            "receipts: when the inner command ends, the outer notes are the current ones again, with no refusal, peer or ref of the inner one");
        Check(decided is { Ok: false, Words: "There is no running order" }, "receipts: the decision returns the inner run's refusal");
        if (!decided.Ok) stack.Refused(decided.Words);
        stack.Leave(outer);
        var receipt = outer.Receipt("a1", "");
        var json = System.Text.Encoding.UTF8.GetString(receipt.ToJson());
        Check(receipt is { Ok: false, Stage: "applied", Error: "There is no running order" } && stack.Current is null && !stack.Remote
              && json == "{\"type\":\"receipt\",\"id\":\"a1\",\"ok\":false,\"stage\":\"applied\",\"effect\":\"\",\"error\":\"There is no running order\",\"refs\":{\"messageId\":12,\"sharedProposal\":\"p1-k3m9\"}}",
            $"receipts: the outer receipt is refused in the inner refusal's words and keeps its refs, the proposal's id a string beside the number ({json})");

        var applied = new CommandReceipt("a2", true, "applied", "sent", null, new Dictionary<string, long> { ["peerRequest"] = 4, ["both"] = 1 },
            new Dictionary<string, string> { ["sharedProposal"] = "p17-k3m9", ["both"] = "x" });
        using (var doc = JsonDocument.Parse(applied.ToJson()))
        {
            var refs = doc.RootElement.GetProperty("refs");
            Check(refs.GetProperty("peerRequest").GetInt64() == 4 && refs.GetProperty("sharedProposal").GetString() == "p17-k3m9" && refs.GetProperty("both").ValueKind == JsonValueKind.Number
                  && refs.EnumerateObject().Count() == 3 && doc.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(["type", "id", "ok", "stage", "effect", "error", "refs"]),
                "receipts: refs carries numbers and ids in one object, a name said once (the number's), the receipt's fields in §3's order");
        }
        using (var doc = JsonDocument.Parse(CommandReceipt.Accepted("a3").ToJson()))
            Check(doc.RootElement.GetProperty("refs").ValueKind == JsonValueKind.Object && !doc.RootElement.GetProperty("refs").EnumerateObject().Any(),
                "receipts: a command that made nothing has empty refs, never none");

        // The remote mark is put back, never cleared, by an inner run: a wire's command stays a wire's after it.
        var marks = new ReceiptStack<string>();
        var wire = marks.EnterRemote();
        var n1 = marks.Enter();
        var n2 = marks.Enter();
        marks.Leave(n2);
        var mid = marks.Remote;
        marks.Leave(n1);
        Check(!wire && mid && marks.Remote && marks.Current is null, "receipts: an inner run puts back the remote mark it found, so the command around it is still a wire's");
        // A wire's command with no receipt, run inside a receipted one (a nested pump), ends the same way: put back, never cleared.
        var n5 = marks.Enter();
        var inside = marks.EnterRemote();
        marks.LeaveRemote(inside);
        var after = marks.Remote;
        marks.Leave(n5);
        marks.LeaveRemote(wire);
        Check(inside && after && !marks.Remote, "receipts: a wire's command with no receipt puts back the mark it found — the receipted command around it stays a wire's, and the last one out clears it");
        var local = new ReceiptStack<string>();
        var n3 = local.Enter();
        var during = local.Remote;
        local.Leave(n3);
        Check(during && !local.Remote, "receipts: a receipted command is a wire's while it runs, and the operator's own actions are their own again after it");
        var peered = new ReceiptStack<string>();
        var n4 = peered.Enter();
        peered.Peer("Patterns FOH");
        peered.Leave(n4);
        Check(n4.Peer == "Patterns FOH" && n4.Receipt("a4", "x") is { Ok: true, Stage: "applied", Effect: "x" }, "receipts: a peer noted is the command's own");
    }

    /// <summary>One table for a command's risk: Core's, which the copilot's reading maps and the desk rates its own lines by.</summary>
    static void Risk(Action<bool, string> Check)
    {
        var wrong = new List<string>();
        foreach (var kind in Enum.GetValues<ShowCommandKind>())
        {
            if (kind == ShowCommandKind.Window) continue;   // read from its typed arguments, below
            foreach (var text in new[] { null, "", "save x", "name x", "stage", "unstage", "start" })
            {
                var c = new ShowCommand(kind, Text: text);
                if (Copilot.Classify(c).ToString() != CommandRisk.Of(c).ToString()) wrong.Add($"{kind} '{text}'");
            }
        }
        Check(wrong.Count == 0 && Enum.GetNames<LineRisk>().SequenceEqual(Enum.GetNames<CopilotRisk>()),
            $"risk: the copilot's risk is Core's for every kind of command ({string.Join(", ", wrong)})");
        // Every kind's risk is chosen: low or high by the table, or named here as review. A kind appended later rates review by the
        // table's last arm, and fails here until someone says that is what it is.
        ShowCommandKind[] review =
        [
            ShowCommandKind.Nudge, ShowCommandKind.SetDuration, ShowCommandKind.EndAt, ShowCommandKind.Message, ShowCommandKind.ClearMessage,
            ShowCommandKind.Signal, ShowCommandKind.Ack, ShowCommandKind.SignalClear, ShowCommandKind.SignalResend, ShowCommandKind.Trim, ShowCommandKind.Delay,
            ShowCommandKind.CatchUp, ShowCommandKind.Ladder, ShowCommandKind.Format, ShowCommandKind.Question, ShowCommandKind.QuestionSet, ShowCommandKind.Rehearse,
            ShowCommandKind.Listen, ShowCommandKind.Report, ShowCommandKind.Aux, ShowCommandKind.DeptNote, ShowCommandKind.EndOfShow,
        ];
        var unchosen = Enum.GetValues<ShowCommandKind>().Where(k => k != ShowCommandKind.Window)   // low or high by its typed arguments, in the table below
            .Where(k => (CommandRisk.Of(new ShowCommand(k)) == LineRisk.Review) != review.Contains(k)).ToList();
        Check(unchosen.Count == 0 && review.Distinct().Count() == review.Length,
            $"risk: every kind of command is low or high by the table, or named as review — none is review only because nobody chose ({string.Join(", ", unchosen)})");
        (string Line, LineRisk Risk)[] table =
        [
            ("note \"x\"", LineRisk.Low), ("screen save lobby abc123", LineRisk.Low), ("screen name abc123 Lobby", LineRisk.Low), ("window new foyer /clock", LineRisk.Low),
            ("window fade lobby 300", LineRisk.Low), ("delay 300", LineRisk.Review), ("aux 1 start", LineRisk.Review), ("message \"Wrap up\" warn", LineRisk.Review),
            ("trim 3 -120", LineRisk.Review), ("start", LineRisk.High), ("next", LineRisk.High), ("blackout on", LineRisk.High), ("aux 1 stage", LineRisk.High),
            ("screen go lobby", LineRisk.High), ("screen identify", LineRisk.High), ("window show lobby /clock", LineRisk.High), ("undo", LineRisk.High),
            ("signal go all", LineRisk.High), ("patterns end", LineRisk.High), ("playout end", LineRisk.High), ("playout vt trust 03e9b152", LineRisk.High),
        ];
        var off = table.Where(t => !(ShowCommandParser.TryParseLine(t.Line, out var cmd, out _) && CommandRisk.Of(cmd) == t.Risk && Copilot.Classify(cmd) == Copilot.Of(t.Risk))).Select(t => t.Line).ToList();
        Check(off.Count == 0, $"risk: what changes the room now is high, what changes nothing anyone sees low, timing and words review ({string.Join(", ", off)})");
        Check(CommandRisk.Word(LineRisk.Low) == "low" && CommandRisk.Word(LineRisk.Review) == "review" && CommandRisk.Word(LineRisk.High) == "high"
              && CommandRisk.Parse("LOW") == LineRisk.Low && CommandRisk.Parse("review") == LineRisk.Review && CommandRisk.Parse("high") == LineRisk.High
              && CommandRisk.Parse("") == LineRisk.High && CommandRisk.Parse("harmless") == LineRisk.High && CommandRisk.Parse(null) == LineRisk.High,
            "risk: §11's three words, and a word that is none of them is high");
        Check(CommandRisk.OfLine("note \"From the board\"") is { Risk: "low", Listed: true, Form: null, Secret: false } && CommandRisk.OfLine("next") is { Risk: "high", Listed: true }
              && CommandRisk.OfLine("sing a song") == RiskVerdict.Unlisted && CommandRisk.OfLine("") == RiskVerdict.Unlisted && CommandRisk.OfLine("   ") == RiskVerdict.Unlisted,
            "risk: the desk rates its own line by its command, and a line its parser does not take is high and unlisted");
        string[] lines = ["signal message speaker \"Wrap up now\"", "  note   two  words ", "message \"a \"b\" c\"", "x\ty", "", "\"\"", "peer pm1 \"SET out1 name \\\"Left\\\"\""];
        Check(lines.All(l => ShowCommandParser.Tokens(l).SequenceEqual(McpEndpoint.Tokenize(l)) && ShowCommandParser.Tokens(l).SequenceEqual(CloudAdvisor.Tokenize(l)))
              && ShowCommandParser.Tokens(lines[0]).SequenceEqual(["signal", "message", "speaker", "Wrap up now"])
              && !ShowCommandParser.TryParseLine("", out _, out var empty) && empty == "an empty line",
            "risk: every whole-line door reads a line into the same words, a quoted phrase one word");
        // The desk service rates a line for the desk by that table, and for another app by its verified table alone.
        var b = Room();
        var desk = new NodeRef("main", DeskId, "");
        Check(b.Desk.Risk(desk, "next") is { Risk: "high", Listed: true } && b.Desk.Risk(desk, "note x") is { Risk: "low" } && b.Desk.Risk(desk, "PANIC") == RiskVerdict.Unlisted
              && b.Desk.Risk(new NodeRef("playmap", "pm1", ""), "PANIC") is { Risk: "high", Listed: true, Form: "PANIC" }
              && b.Desk.Risk(new NodeRef("playmap", "pm1", ""), "next") == RiskVerdict.Unlisted
              && b.Desk.Risk(new NodeRef("patterns", "pm1", ""), "PANIC") == RiskVerdict.Unlisted && b.Desk.Risk(new NodeRef("playmap", "pm9", ""), "STATUS") == RiskVerdict.Unlisted,
            "risk: the desk's own lines by its own table, another app's by its verified table of its own kind, and nothing else lowers a risk");
    }

    /// <summary>The node routes answer as the desk always has, through the service: §11's shapes, statuses and words.</summary>
    static void Routes(Action<bool, string> Check)
    {
        var b = Room();
        var made = b.Desk.SubmitProposal(Proposal("patterns", "pat1", "Patterns FOH", "playmap", "pm1", "PlayMap Hall", "PLAYLIST CUE show #3", "cue it"), NodeCredential.Key("kP"));
        Check(made.Status == 201 && made.Json == "{\"ok\":true,\"id\":\"p1-k3m9\",\"state\":\"pending\",\"risk\":\"review\",\"form\":\"PLAYLIST CUE \\u003Cplaylist\\u003E \\u003Citem\\u003E|#\\u003Cn\\u003E\",\"formWords\":\"what PLAYLIST does\"}",
            $"desk routes: a proposal's answer is §11's, then the form that rated it and that row's words ({made.Json})");
        Check(b.Desk.SubmitProposal(Proposal("main", "other", "", "playmap", "pm1", "", "UNDO"), NodeCredential.Operator) is (403, { } own) && Error(own) == ProposalDesk.DeskOwn("main")
              && b.Desk.SubmitProposal(Proposal("patterns", "pat1", "", "playmap", "pm1", "", "UNDO"), NodeCredential.Key("kM")).Status == 403
              && b.Desk.SubmitProposal("nonsense", NodeCredential.Key("kP")) is (400, { } bad) && Error(bad) == ProposalDesk.NotAProposal
              && b.Desk.SubmitProposal(Proposal("patterns", "pat1", "", "playmap", "pm1", "", "LINK STAGEWATCH h hunter2"), NodeCredential.Key("kP")).Status == 400,
            "desk routes: a from of the desk's own and a key that speaks for another node are 403; no proposal and a filled secret 400");
        Check(b.Desk.GetProposal("p1").Status == 200 && b.Desk.GetProposal("p1-k3m9").Json.Contains("\"reason\":\"cue it\"", StringComparison.Ordinal)
              && b.Desk.GetProposal("p1-zzzz") is (404, { } restarted) && Error(restarted) == b.Desk.Queue.Unknown("p1-zzzz") && Error(restarted).Contains("this desk has restarted since p1-zzzz was made", StringComparison.Ordinal)
              && b.Desk.ProposalsFrom("pat1").Json.Contains("p1-k3m9", StringComparison.Ordinal) && b.Desk.ProposalsFrom("ag1").Json == "{\"proposals\":[]}"
              && b.Desk.ProposalsFrom("").Json.Contains("p1-k3m9", StringComparison.Ordinal),
            "desk routes: one proposal by its id or p<n>, an earlier run's 404 in words, and the list by proposer");
        Check(b.Desk.CollectApproved("pm1", NodeCredential.Key("kP")).Status == 403 && b.Desk.Queue.LastCollect("pm1") is null
              && b.Desk.CollectApproved("pm1", NodeCredential.Key("kM")) is (200, "{\"proposals\":[]}") && b.Desk.Queue.LastCollect("pm1") == b.Now,
            "desk routes: a collect under another node's key is 403 and records nothing; its own key collects, and the ask is recorded");
        b.Desk.Decide("p1", true, DeskOrigin.Operator);
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        Check(b.Desk.ProposalResult("p1", "not json", NodeCredential.Key("kM")).Status == 400 && b.Desk.ProposalResult("p1", "{\"ok\":\"yes\"}", NodeCredential.Key("kM")).Status == 400
              && b.Desk.ProposalResult("p1", "{\"ok\":true,\"words\":\"cued\"}", NodeCredential.Key("kP")).Status == 409
              && b.Desk.ProposalResult("p1", "{\"ok\":true,\"words\":\"cued\"}", NodeCredential.Key("kM")) is (200, "{\"ok\":true}")
              && b.Desk.ProposalResult("p1", "{\"ok\":true,\"words\":\"again\"}", NodeCredential.Key("kM")).Status == 409 && b.Desk.Queue.Get("p1") is { State: SharedProposalState.Done, Result: "cued" },
            "desk routes: a result that is no result is 400, another node's or a second one 409, and the target's own is taken once");
        var old = new Bench("q7zx");
        Check(old.Desk.Queue.Run == "q7zx" && old.Desk.GetProposal("p1-k3m9").Status == 404 && new Bench(fresh: TimeSpan.FromSeconds(2)).Desk.Queue.Fresh == TimeSpan.FromSeconds(2),
            "desk routes: the host gives the desk its run and how long a collect stays fresh");
    }

    /// <summary>What every reader of the desk takes: its summary, the quick verbs of a row, and the report's decisions.</summary>
    static void Readers(Action<bool, string> Check)
    {
        var b = Room();
        Check(b.Desk.Summary() is { Pending: 0, Next: null, Apps.Count: 3 } && DeskSummary.Empty is { Pending: 0, Next: null, Apps.Count: 0 }, "desk readers: an idle desk has nothing waiting and lists its apps");
        var longLine = "MESSAGE " + new string('m', 300);
        b.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "playmap", "pm1", "PlayMap Hall", longLine), NodeCredential.Key("kA"));
        b.Now = T0.AddSeconds(1);
        b.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "playmap", "pm1", "PlayMap Hall", "PANIC"), NodeCredential.Key("kA"));
        var s = b.Desk.Summary();
        Check(s is { Pending: 2, Next: { Id: "p1-k3m9", From: "AGMessage", Target: "PlayMap Hall", Risk: "low", Collects: false } } && s.Next.Line.Length == DeskSummary.NextLineChars
              && longLine.StartsWith(s.Next.Line, StringComparison.Ordinal),
            "desk readers: the summary counts what waits and shows the oldest — who, for which app, its risk, its line's first 120 characters, whether its app collects");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        Check(b.Desk.Summary() is { Next.Collects: true } after && after.Apps.First(a => a.Id == "pm1").Collects && !after.Apps.First(a => a.Id == "pat1").Collects,
            "desk readers: once its app asks for approved lines, the summary and the app's row say it collects");

        var quick = b.Desk.QuickVerbs("pm1");
        Check(quick.Select(q => (q.Head, q.Value, q.Risk)).SequenceEqual([("MESSAGE", "text", "low"), ("LABEL", "words", "low"), ("NOTE ADD", "note", "review")])
              && quick[0].Words == "what MESSAGE does",
            $"desk readers: a row's quick verbs are the low and review forms a person fills by typing only the last words ({string.Join(" · ", quick.Select(q => q.Head))})");
        Check(b.Desk.QuickVerbs("ag1") is [{ Head: "notice", Value: "text", Risk: "review" }] && b.Desk.QuickVerbs("nobody").Count == 0,
            "desk readers: AGMessage's notice is a quick verb; an app that is not listed has none");
        Check(VerbSyntax.Quick("MESSAGE <text>") is ("MESSAGE", "text") && VerbSyntax.Quick("NOTE ADD <note>") is ("NOTE ADD", "note")
              && VerbSyntax.Quick("UNDO") is null && VerbSyntax.Quick("PLAYLIST CUE <playlist> <item>|#<n>") is null && VerbSyntax.Quick("BLACKOUT ON|OFF") is null
              && VerbSyntax.Quick("LOGIN <token>") is null && VerbSyntax.Quick("BIND LEARN <target line>") is null && VerbSyntax.Quick("SAY [<text>]") is null
              && VerbSyntax.Quick("<text>") is null && VerbSyntax.Quick("ON|OFF <text>") is null && VerbSyntax.Quick("SAY <a> …") is null && VerbSyntax.Quick("SAY <text") is null,
            "desk readers: a quick form is literal words then one value that is last — never a secret, a line of the node, an option or alternatives");
        var many = new Bench();
        many.Hear("playmap", "pm1", "PlayMap Hall");
        many.Post("playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"), Enumerable.Range(1, 9).Select(i => ($"SAY{i} <text>", "low")).ToArray());
        var unverified = new Bench();
        unverified.Hear("playmap", "pm1", "PlayMap Hall");
        unverified.Post("playmap", "pm1", "PlayMap Hall", NodeCredential.None, ("MESSAGE <text>", "low"));
        Check(many.Desk.QuickVerbs("pm1").Count == DeskService.MaxQuickVerbs && many.Desk.QuickVerbs("pm1")[0].Head == "SAY1" && unverified.Desk.QuickVerbs("pm1").Count == 0,
            "desk readers: at most six quick verbs, in the table's order, and none from verbs that prove nothing");

        b.Desk.Decide("next", true, DeskOrigin.Deck("Stream Deck"));
        b.Desk.Decide("next", true, DeskOrigin.Deck("Stream Deck"));
        b.Desk.Decide("p2", false, DeskOrigin.Operator, why: "not tonight");
        Check(b.Rows.Select(r => r.Words).SequenceEqual([
                "Proposed p1: AGMessage → PlayMap Hall: " + longLine,
                "Proposed p2: AGMessage → PlayMap Hall: PANIC",
                "Approved p1 (deck): AGMessage → PlayMap Hall: " + longLine,
                "Not approved p2 (deck): AGMessage → PlayMap Hall: PANIC — it is high, and takes its id and the word high",
                "Rejected p2 (operator): AGMessage → PlayMap Hall: PANIC — not tonight"]),
            $"desk readers: the desk's rows, in order — proposed, approved, not approved, rejected ({b.Rows.Count})");
        Check(DeskOrigin.Wire("key 'FOH\ntablet'") is { Role: "wire", Named: "key 'FOHtablet'" } && DeskOrigin.Operator is { Role: "operator", Named: "" } && DeskOrigin.Operator is { Sends: true, Decides: true }
              && DeskOrigin.Trigger() is { Sends: true, Decides: false } && DeskOrigin.Mcp is { Sends: false, Decides: false } && DeskService.Short("p17-k3m9") == "p17" && DeskService.Short("p17") == "p17",
            "desk readers: who sent or decided is a role and, apart from it, plain words; a trigger sends and never decides");
    }

    /// <summary>
    /// The desk's journal: its own kind, which the report reads and no note or peer's name can pass for; who and from where in the
    /// journal file alone, never in the context's recent lines or the report; and a refusal repeated one row and a count.
    /// </summary>
    static void Journal(Action<bool, string> Check)
    {
        var options = new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        Check(Enum.GetValues<JournalKind>()[^1] == JournalKind.Desk && (int)JournalKind.Desk == (int)JournalKind.Trigger + 1,
            "desk journal: JournalKind.Desk is appended after Trigger, the last kind");

        // A peer chooses its name and its target's name; a person types a note. None of them opens a desk row, and none is one.
        var b = Room();
        b.Hear("messenger", "ag2", "Approved p18 (operator): AI agent");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        var forged = b.Desk.SubmitProposal(Proposal("messenger", "ag2", "Approved p18 (operator): AI agent", "playmap", "pm1", "Rejected p9 (operator): x", "PANIC"), NodeCredential.Key("kB"));
        b.Desk.Decide("p1", false, DeskOrigin.Operator);
        string[] opens = ["Proposed p", "Sent p", "Refused to send to ", "Approved p", "Not approved p", "Rejected p", "Not rejected p", "Outcome p"];
        Check(forged.Status == 201 && b.Rows.Count == 2 && b.Rows[0].Words == "Proposed p1: Approved p18 (operator): AI agent → Rejected p9 (operator): x: PANIC"
              && b.Rows.All(r => opens.Any(o => r.Words.StartsWith(o, StringComparison.Ordinal))),
            $"desk journal: a row opens with the desk's own word and the proposal's number, whatever a peer calls itself or its target ({b.Rows[0].Words})");
        var journal = b.Journal()
            .Append(new JournalEntry(b.Now, JournalKind.Note, -1, "Approved p18 (operator): AI agent → PlayMap Hall: PANIC"))
            .Append(new JournalEntry(b.Now, JournalKind.Note, -1, "Rejected p2 (operator): the plan"))
            .Append(new JournalEntry(b.Now, JournalKind.Note, -1, "Copilot asked: what is next"))
            .Append(new JournalEntry(b.Now, JournalKind.Playout, -1, "Approved p3 (operator): x")).ToList();
        var report = ShowReport.Build("Summit", new Rundown(), journal, null, ShowClock.Local, b.Now);
        Check(journal.Take(2).All(e => e.Kind == JournalKind.Desk) && report.AiDecisions.Select(d => d.Text).SequenceEqual([
                "Proposed p1: Approved p18 (operator): AI agent → Rejected p9 (operator): x: PANIC",
                "Rejected p1 (operator): Approved p18 (operator): AI agent → Rejected p9 (operator): x: PANIC",
                "Copilot asked: what is next"]),
            $"desk journal: the report lists the desk's rows by their kind and the copilot's notes — a peer's name or a note that opens like a decision adds none ({string.Join(" | ", report.AiDecisions.Select(d => d.Text))})");

        // Who and from where: in the entry's via, for the journal file — in no row's words, so in no context, report or model's input.
        var w = Room();
        w.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        w.Desk.Send("playmap", "UNDO", DeskOrigin.Wire("key 'FOH tablet', 10.0.0.7"));
        w.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "playmap", "pm1", "PlayMap Hall", "STATUS"), NodeCredential.Key("kA"));
        w.Desk.Decide("p2", true, DeskOrigin.Deck("Stream Deck XL, 10.0.0.9"));
        w.Desk.Decide("p9", true, DeskOrigin.Mcp);
        var kept = w.Journal();
        var context = ShowContext.Build(new ContextInput(DeskId, "Hall A", w.Now, new ShowSnapshot { Title = "Keynote" }, false, [], [], kept, ShowClock.For("UTC"), 0));
        var built = ShowReport.Build("Summit", new Rundown(), kept, null, ShowClock.For("UTC"), w.Now);
        var facts = string.Join("\n", kept.Select(e => $"{e.Kind}: {e.Text}")) + context + built.ToHtml(ShowClock.For("UTC")) + built.ToCsv(ShowClock.For("UTC"));
        string[] whos = ["FOH tablet", "10.0.0.7", "Stream Deck", "10.0.0.9"];
        Check(kept.Count == 3 && kept[0] is { Kind: JournalKind.Desk, Text: "Sent p1 to PlayMap Hall (wire): UNDO", Via: "key 'FOH tablet', 10.0.0.7" }
              && kept[1] is { Text: "Proposed p2: AGMessage → PlayMap Hall: STATUS", Via: null }
              && kept[2] is { Text: "Approved p2 (deck): AGMessage → PlayMap Hall: STATUS", Via: "Stream Deck XL, 10.0.0.9" },
            $"desk journal: a host's entry is the desk's kind, its words the role alone, who and from where its via ({string.Join(" | ", kept.Select(e => e.Text))})");
        Check(context.Contains("Sent p1 to PlayMap Hall (wire): UNDO", StringComparison.Ordinal) && built.AiDecisions.Count == 3 && !whos.Any(x => facts.Contains(x, StringComparison.Ordinal)),
            "desk journal: the context's recent lines and the report carry the rows' words — and no key's name or address");
        var line = JsonSerializer.Serialize(kept[0], options);
        var back = JsonSerializer.Deserialize<JournalEntry>(line, options);
        Check(line.Contains("\"k\":\"Desk\"", StringComparison.Ordinal) && line.Contains("\"via\":", StringComparison.Ordinal) && back is { Kind: JournalKind.Desk, Via: "key 'FOH tablet', 10.0.0.7" }
              && !JsonSerializer.Serialize(kept[1], options).Contains("via", StringComparison.Ordinal),
            "desk journal: the journal file keeps who and from where, written only when there is one, and reads it back");

        // A refusal repeated — a held button, a trigger firing at a silent app — is one row, then one more that counts the rest.
        var r = Room();
        var deck = DeskOrigin.Deck("Stream Deck");
        r.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "playmap", "pm1", "PlayMap Hall", "PANIC"), NodeCredential.Key("kA"));
        for (var i = 0; i < 40; i++)
        {
            r.Now = T0.AddSeconds(i / 2.0);   // within the time PlayMap's posted verbs stand, so each refusal keeps its reason
            r.Desk.Decide("p1", true, deck);
            r.Desk.Send("playmap", "STATUS", DeskOrigin.Trigger());
            r.Desk.Decide("next", true, DeskOrigin.Mcp);
        }
        Check(r.Rows.Select(x => x.Words).SequenceEqual([
                "Proposed p1: AGMessage → PlayMap Hall: PANIC",
                "Not approved p1 (deck): AGMessage → PlayMap Hall: PANIC — it is high, and takes its id and the word high",
                "Refused to send to PlayMap Hall (trigger): PlayMap Hall has not asked this desk for approved lines since this desk started — it would not collect this",
                "Not approved p1 (mcp): AGMessage → PlayMap Hall: PANIC — " + DeskService.PersonDecides]),
            $"desk journal: forty refusals of each kind are one row each ({r.Rows.Count})");
        // The same attempt refused for another reason, or by another role, is a row of its own.
        r.Desk.Decide("p1", true, deck, high: true);
        r.Desk.Decide("p1", true, DeskOrigin.Operator);
        Check(r.Rows.Count == 6 && r.Rows[4].Words.StartsWith("Not approved p1 (deck): AGMessage → PlayMap Hall: PANIC — PlayMap Hall has not asked", StringComparison.Ordinal)
              && r.Rows[5].Words == "Not approved p1 (operator): AGMessage → PlayMap Hall: PANIC — it is high, and takes its id and the word high",
            "desk journal: another reason, or another role, is its own row");
        r.Now = T0 + DeskService.RepeatWindow - TimeSpan.FromSeconds(1);
        r.Desk.Expire();
        var beforeEnd = r.Rows.Count;
        r.Now = T0 + DeskService.RepeatWindow;
        r.Desk.Expire();
        Check(beforeEnd == 6 && r.Rows.Skip(6).Select(x => (x.Words, x.Who, x.AtUtc)).SequenceEqual([
                ("Not approved p1 (deck): refused the same way 39 more times", "Stream Deck", T0 + DeskService.RepeatWindow),
                ("Refused to send to PlayMap Hall (trigger): refused the same way 39 more times", "", T0 + DeskService.RepeatWindow),
                ("Not approved p1 (mcp): refused the same way 39 more times", "", T0 + DeskService.RepeatWindow)]),
            $"desk journal: when a refusal's minute ends, one row counts its repeats — short, with no line in it ({string.Join(" | ", r.Rows.Skip(6).Select(x => x.Words))})");
        r.Now = T0.AddMinutes(3);
        r.Desk.Expire();
        r.Desk.Decide("p1", true, deck);
        r.Desk.Decide("p1", true, deck);
        r.Now = T0.AddMinutes(5);
        r.Desk.SubmitProposal(Proposal("messenger", "ag1", "AGMessage", "playmap", "pm1", "PlayMap Hall", "STATUS"), NodeCredential.Key("kA"));
        Check(r.Rows.Count == 12 && r.Rows[9].Words.EndsWith("it is high, and takes its id and the word high", StringComparison.Ordinal)
              && r.Rows[10] is { Words: "Not approved p1 (deck): refused the same way 1 more time" } counted && counted.AtUtc == T0.AddMinutes(4)
              && r.Rows[11].Words == "Proposed p2: AGMessage → PlayMap Hall: STATUS",
            $"desk journal: a refusal that came once more is counted before the next row, at the time its minute ended — and one that came once has no count ({r.Rows.Count})");
        // Past the bound the oldest gives way, its count written: the table never grows.
        var many = Room();
        for (var i = 0; i < DeskService.MaxRepeats + 4; i++)
        {
            many.Desk.Send("playmap", "STATUS", DeskOrigin.Deck("deck " + i));
            many.Desk.Send("playmap", "STATUS", DeskOrigin.Deck("deck " + i));
        }
        Check(many.Rows.Count == DeskService.MaxRepeats + 4 + 4 && many.Rows.Count(x => x.Words.EndsWith("refused the same way 1 more time", StringComparison.Ordinal)) == 4,
            $"desk journal: at most {DeskService.MaxRepeats} refusals are held for their repeats; the oldest gives way with its count ({many.Rows.Count})");
    }
}
