using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Show;

/// <summary>
/// One report for the room (Show Bus §6, §11): what verified peers' own journals told this Stagewatch — only a context that proves
/// its node, posted or read over a link, each line once with its node, its own time and its road; never a forged id, never a
/// context that proves nothing, never a line that holds a key or a window's address — and every desk decision, a rejection
/// included, one row a decision. The report's AI section holds the desk's rows and the copilot's notes; its show network section
/// and network.csv hold the peer rows and say they are a sample; no peer row reaches Stagewatch's own context or a model's
/// summary input. Every time is written the same under any culture, and a journal from before peer rows reports byte for byte as
/// it did.
/// </summary>
static class ReportTests
{
    static readonly DateTime T0 = DeskTests.T0;
    const long At = 1792898411000;   // 2026-10-25T03:20:11Z, a line's time on its node's clock
    static readonly ShowClock Utc = ShowClock.For("UTC");
    static readonly JsonSerializerOptions File = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static void Run(Action<bool, string> Check)
    {
        Hearing(Check);
        DeskRows(Check);
        Sections(Check);
        OwnReaders(Check);
        Culture(Check);
        OldJournal(Check);
    }

    static string Context(string kind, string id, string name, params (long At, string Text)[] recent) => JsonSerializer.Serialize(new
    {
        v = 1,
        node = new { t = kind, id, name },
        recent = recent.Select(r => r.At > 0 ? (object)new { at = r.At, text = r.Text } : new { text = r.Text }).ToArray(),
    });

    static (int Status, NodeProof Proof) Post(DeskTests.Bench b, string kind, string id, string name, NodeCredential credential, params (long At, string Text)[] recent)
    {
        var (status, _, proof) = b.Peers.Accept(Context(kind, id, name, recent), "10.0.0.9", b.Now, DeskTests.DeskId, credential);
        return (status, proof);
    }

    static string Said(IEnumerable<HeardLine> rows) => string.Join(" | ", rows.Select(r => $"{r.Node.Id}{(r.Note ? " note" : "")}: {r.Text} [{r.Via}]"));

    /// <summary>What the desk hears of peers' journals, once a second: verified contexts alone, both roads, each line once.</summary>
    static void Hearing(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        b.KeyNames["kM"] = "Stage left";
        b.Desk.Expire();
        Check(b.Lines.Count == 3 && b.Lines.All(l => l is { Note: true, AtMs: null } && l.HeardUtc == T0 && l.Text.EndsWith(" joins the report; its earlier lines are in its own journal", StringComparison.Ordinal))
              && b.Lines.Select(l => l.Node.Id).Order(StringComparer.Ordinal).SequenceEqual(["ag1", "pat1", "pm1"])
              && b.Lines.First(l => l.Node.Id == "pm1") is { Via: "node key 'Stage left'", Node: { Kind: "playmap", Name: "PlayMap Hall" } }
              && b.Lines.First(l => l.Node.Id == "ag1").Via == "its node key" && b.Rows.Count == 0,
            $"report hearing: each verified node joins once, with the road its context came by — its node key, by the key's name when it has one ({Said(b.Lines)})");

        b.Now = T0.AddSeconds(2);
        var posted = Post(b, "playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"), (At, "PLAYLIST GO show — Intro"), (At + 3000, "refused: PANIC"));
        b.Desk.Expire();
        var fresh = b.Lines.Skip(3).ToList();
        Check(posted is (200, NodeProof.Key) && fresh.Count == 2
              && fresh[0] is { Note: false, Text: "PLAYLIST GO show — Intro", AtMs: At, Via: "node key 'Stage left'", Node.Id: "pm1" } && fresh[0].HeardUtc == b.Now
              && fresh[1] is { Text: "refused: PANIC", AtMs: At + 3000 },
            $"report hearing: a keyed post's new lines are heard, each with its node, its own time and its road ({Said(fresh)})");
        var entry = fresh[0].Entry(3);
        var line = JsonSerializer.Serialize(entry, File);
        var back = JsonSerializer.Deserialize<JournalEntry>(line, File);
        Check(entry is { Kind: JournalKind.Peer, Item: 3, Value: 0, IsPeerNote: false } && line.Contains("\"k\":\"Peer\"", StringComparison.Ordinal) && line.Contains("\"n\":\"pm1\"", StringComparison.Ordinal)
              && line.Contains("\"pt\":1792898411000", StringComparison.Ordinal) && line.EndsWith("\"nn\":\"PlayMap Hall\",\"nk\":\"playmap\"}", StringComparison.Ordinal)
              && back is { Kind: JournalKind.Peer, Node: "pm1", PeerAtMs: At, Via: "node key 'Stage left'", NodeName: "PlayMap Hall", NodeKind: "playmap", Text: "PLAYLIST GO show — Intro" }
              && b.Lines[0].Entry() is { IsPeerNote: true, PeerAtMs: null },
            $"report hearing: the journal file keeps the peer kind, the node's id, name and kind, the line's own time and the road, after every older field, and reads them back ({line})");
        var plain = JsonSerializer.Serialize(new JournalEntry(T0, JournalKind.Note, -1, "a note"), File);
        Check(!plain.Contains("\"n\"", StringComparison.Ordinal) && !plain.Contains("\"pt\"", StringComparison.Ordinal) && !plain.Contains("\"nn\"", StringComparison.Ordinal) && !plain.Contains("\"nk\"", StringComparison.Ordinal),
            "report hearing: a row about no node writes none of the node fields");

        // Heard once: the same context a second later, and the same lines posted again after the context was dropped (31 s).
        b.Now = T0.AddSeconds(3);
        b.Desk.Expire();
        b.Now = T0.AddSeconds(34);
        b.Desk.Expire();
        Post(b, "playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"), (At, "PLAYLIST GO show — Intro"), (At + 3000, "refused: PANIC"));
        b.Desk.Expire();
        Check(b.Lines.Count == 5, $"report hearing: a line is journalled once — the same context each second, and the same lines posted again 31 s later, add nothing ({b.Lines.Count})");

        // A forged id: AGMessage's key posting as PlayMap is refused, and nothing it said is heard.
        var forged = Post(b, "playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kA"), (At + 9000, "FORGED by another node's key"));
        var claimed = Post(b, "playmap", "pm1", "PlayMap Hall", NodeCredential.None, (At + 9500, "FORGED with no credential"));
        // A context that proves nothing (posted with no credential, for an id no key speaks for): held and shown, never heard.
        var unverified = Post(b, "messenger", "ag9", "Stranger", NodeCredential.None, (At, "words nobody proved"));
        b.Desk.Expire();
        Check(forged.Status == 403 && claimed.Status == 403 && unverified is (200, NodeProof.None) && b.Lines.Count == 5
              && b.Desk.Apps().Any(a => a.Id == "ag9") && !b.Lines.Any(l => l.Node.Id == "ag9" || l.Text.Contains("FORGED", StringComparison.Ordinal)),
            $"report hearing: a context posted under another node's key or under none for a keyed id is refused, and one that proves nothing writes no row ({forged.Status}, {claimed.Status}, {unverified.Status})");

        // The operator's credential proves a node no key speaks for; a context read over this Stagewatch's own link proves by the link.
        var byOperator = Post(b, "patterns", "pat9", "Patterns B", NodeCredential.Operator, (At, "take wall — done"));
        b.Linked["pat7"] = PeerContexts.Parse(Context("patterns", "pat7", "Patterns Wall", (At, "cut wall — done")), "10.0.0.7", b.Now)!;
        b.Desk.Expire();
        b.Now = T0.AddSeconds(36);
        b.Linked["pat7"] = PeerContexts.Parse(Context("patterns", "pat7", "Patterns Wall", (At, "cut wall — done"), (At + 500, "take wall — done")), "10.0.0.7", b.Now)!;
        b.Desk.Expire();
        var roads = b.Lines.Skip(5).ToList();
        Check(byOperator is (200, NodeProof.Operator) && roads.Count == 3
              && roads.Single(l => l.Node.Id == "pat9") is { Note: true, Via: "the operator's credential", From: "10.0.0.9" }
              && roads.Where(l => l.Node.Id == "pat7").All(l => l.From.Length == 0) && b.Lines.Where(l => l.Node.Id == "pm1").All(l => l.From.Length == 0)
              && roads.Where(l => l.Node.Id == "pat7").Select(l => (l.Note, l.Via, l.Text)).SequenceEqual([
                  (true, "the link", "Patterns Wall joins the report; its earlier lines are in its own journal"), (false, "the link", "take wall — done")]),
            $"report hearing: the operator's credential and this Stagewatch's own link are roads too — the link-read line is heard as a posted one is ({Said(roads)})");
        // Whoever holds the operator's credential may speak for an id no key is bound to: the journal file keeps where such a context
        // was posted from, in a field of its own, after every other — never in the road, which the report prints.
        var operatorRow = JsonSerializer.Serialize(roads.Single(l => l.Node.Id == "pat9").Entry(), File);
        Check(operatorRow.EndsWith("\"via\":\"the operator\\u0027s credential\",\"n\":\"pat9\",\"nn\":\"Patterns B\",\"nk\":\"patterns\",\"fr\":\"10.0.0.9\"}", StringComparison.Ordinal)
              && JsonSerializer.Deserialize<JournalEntry>(operatorRow, File) is { From: "10.0.0.9", Via: "the operator's credential" }
              && !JsonSerializer.Serialize(fresh[0].Entry(), File).Contains("\"fr\"", StringComparison.Ordinal),
            $"report hearing: a row whose node only the operator's credential proved keeps the address its context was posted from, for the journal file alone; a keyed row keeps none ({operatorRow})");
        var entryOf = PeerContexts.Parse(Context("playmap", "pm5", "Stage left"), "10.0.0.5", T0)!;
        var row = new NetworkRow(new PeerSummary("playmap", "pm5", "Stage left", Light.Green, "", "10.0.0.5", 1, true), entryOf, NodeProof.Key, null, [], true);
        Check(ShowNetwork.Road(row with { Pulled = true, Via = "stage-left" }, _ => "x") == "the link 'stage-left'"
              && ShowNetwork.Road(row with { Context = entryOf with { KeyId = "k1" } }, id => id == "k1" ? "Stage left" : null) == "node key 'Stage left'"
              && ShowNetwork.Road(row with { Context = entryOf with { KeyId = "k1" } }, _ => null) == "its node key"
              && ShowNetwork.Road(row with { ContextProof = NodeProof.Operator }, _ => "x") == "the operator's credential"
              && ShowNetwork.Road(row with { ContextProof = NodeProof.None }, _ => "x") == "" && ShowNetwork.Road(row with { Context = null }, _ => "x") == "",
            "report hearing: a road names the link or the key, never an address; a context that proves nothing has none");

        // A line that holds a window's address or a key is left out, and its row says so without it.
        const string token = "w7kq2m9xw7kq2m9xw7kq2m9xw7";
        b.Now = T0.AddSeconds(38);
        Post(b, "playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"), (At + 3000, "refused: PANIC"), (At + 20000, "OUTPUT 1 shows http://10.0.0.2:8080/w/" + token),
            (At + 21000, "LINK STAGEWATCH 10.0.0.2 " + token), (At + 22000, "UNDO"));
        b.Desk.Expire();
        var kept = b.Lines.Skip(8).ToList();
        Check(kept.Select(l => (l.Note, l.Text, l.AtMs)).SequenceEqual([
                  (true, "a line from PlayMap Hall is left out of the report: it holds a window's address", null),
                  (true, "a line from PlayMap Hall is left out of the report: it holds a window's address", null),
                  (false, "UNDO", (long?)(At + 22000))])
              && !b.Lines.Any(l => l.Text.Contains(token, StringComparison.Ordinal)),
            $"report hearing: a line that holds a window's address — by its path or by a token of this desk's — is left out, said, and never journalled ({Said(kept)})");
    }

    static string Propose(DeskTests.Bench d, string fromId, string targetKind, string targetId, string line, string key)
    {
        var (status, json) = d.Desk.SubmitProposal(DeskTests.Proposal("messenger", fromId, "AGMessage", targetKind, targetId, "x", line), NodeCredential.Key(key));
        using var doc = JsonDocument.Parse(json);
        return status == 201 ? doc.RootElement.GetProperty("id").GetString()! : "refused: " + json;
    }

    /// <summary>Every desk decision is one desk row, a rejection included, each with the other node's id and who decided, apart from its words.</summary>
    static void DeskRows(Action<bool, string> Check)
    {
        var d = DeskTests.Room();
        d.KeyNames["kA"] = "AGM";
        d.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        d.Desk.Send("playmap", "UNDO", DeskOrigin.Wire("key 'FOH tablet', 10.0.0.7"));
        var p2 = Propose(d, "ag1", "playmap", "pm1", "STATUS", "kA");
        var rejected = d.Desk.Decide(p2, false, DeskOrigin.Deck("Stream Deck XL, 10.0.0.9"), why: "not now");
        var rows = d.Journal();
        Check(rejected.Ok && rows.Count == 3 && rows.All(e => e.Kind == JournalKind.Desk)
              && rows[0] is { Text: "Sent p1 to PlayMap Hall (wire): UNDO", Node: "pm1", Via: "key 'FOH tablet', 10.0.0.7" }
              && rows[1] is { Text: "Proposed p2: AGMessage → PlayMap Hall: STATUS", Node: "ag1", Via: "node key 'AGM'" }
              && rows[2] is { Text: "Rejected p2 (deck): AGMessage → PlayMap Hall: STATUS — not now", Node: "pm1", Via: "Stream Deck XL, 10.0.0.9" },
            $"report desk rows: sent, proposed and rejected are desk rows with the other node's id — who proposed, then who was to run it — and who and how apart from the words ({string.Join(" | ", rows.Select(e => $"{e.Text} [{e.Node}; {e.Via}]"))})");

        // Every way a person rejects — a deck, the wire, the desk — is exactly one row that says who (by role) and why.
        foreach (var by in new[] { DeskOrigin.Deck("Stream Deck"), DeskOrigin.Wire("the PIN, 10.0.0.7"), DeskOrigin.Operator })
        {
            var id = Propose(d, "ag1", "playmap", "pm1", "UNDO", "kA");
            var before = d.Rows.Count;
            var outcome = d.Desk.Decide(id, false, by, why: "wrong cue");
            Check(outcome.Ok && d.Rows.Count == before + 1 && d.Last == $"Rejected {DeskService.Short(id)} ({by.Role}): AGMessage → PlayMap Hall: UNDO — wrong cue"
                  && d.Rows[^1].Node == "pm1" && d.Desk.Queue.Get(id) is { State: SharedProposalState.Rejected },
                $"report desk rows: a rejection by {by.Role} writes exactly one desk row, with the role and the reason ({d.Last})");
        }
        // What is not a person rejects nothing: that is one row too, and the proposal still waits.
        var kept = Propose(d, "ag1", "playmap", "pm1", "UNDO", "kA");
        var beforeMcp = d.Rows.Count;
        var byAgent = d.Desk.Decide(kept, false, DeskOrigin.Mcp, why: "an agent's reason");
        Check(!byAgent.Ok && d.Rows.Count == beforeMcp + 1 && d.Last == $"Not rejected {DeskService.Short(kept)} (mcp): AGMessage → PlayMap Hall: UNDO — {DeskService.PersonDecides}"
              && d.Desk.Queue.Get(kept) is { State: SharedProposalState.Pending },
            $"report desk rows: an agent's rejection rejects nothing and writes exactly one row saying so ({d.Last})");
        // An approval, and an approval refused, are one row each; an approved line aimed at this desk adds what its run came to.
        var beforeApprove = d.Rows.Count;
        var approved = d.Desk.Decide(kept, true, DeskOrigin.Deck("Stream Deck"));
        var high = Propose(d, "ag1", "playmap", "pm1", "PANIC", "kA");
        var beforeHigh = d.Rows.Count;
        var refused = d.Desk.Decide(high, true, DeskOrigin.Wire("the PIN, 10.0.0.7"));
        Check(approved.Ok && beforeHigh == beforeApprove + 2 && d.Rows[beforeApprove].Words.StartsWith("Approved ", StringComparison.Ordinal) && d.Rows[beforeApprove].Node == "pm1"
              && !refused.Ok && d.Rows.Count == beforeHigh + 1 && d.Last.StartsWith("Not approved ", StringComparison.Ordinal) && d.Rows[^1].Node == "pm1",
            $"report desk rows: an approval is one row, and so is an approval refused ({d.Last})");
        var own = Propose(d, "ag1", "main", DeskTests.DeskId, "note hello", "kA");
        var beforeOwn = d.Rows.Count;
        d.Desk.Decide(own, true, DeskOrigin.Operator);
        var ownRows = d.Rows.Skip(beforeOwn).ToList();
        Check(ownRows.Count == 2 && ownRows[0].Words.StartsWith("Approved ", StringComparison.Ordinal) && ownRows[1].Words.StartsWith("Outcome ", StringComparison.Ordinal)
              && ownRows.All(r => r.Node == "ag1") && d.Rows[beforeOwn - 1] is { Node: "ag1", Who: "node key 'AGM'" },
            $"report desk rows: a line aimed at this desk names its proposer as the row's node — the decision, then what its run came to ({string.Join(" | ", ownRows.Select(r => r.Words))})");
    }

    static readonly NodeRef PlayMap = new("playmap", "9f8e7d6c", "Stage left");
    static readonly NodeRef Agm = new("messenger", "a1b2c3d4", "AGM, \"hall\" desk");
    static readonly DateTime Heard = new(2026, 10, 25, 3, 20, 15, DateTimeKind.Utc);

    /// <summary>A journal of every sort of row: the show's own, the copilot's notes, the desk's rows and what peers' journals told.</summary>
    static List<JournalEntry> Mixed() =>
    [
        new JournalEntry(Heard.AddSeconds(-20), JournalKind.ShowOpened, -1, "Summit"),
        new JournalEntry(Heard.AddSeconds(-15), JournalKind.Note, -1, "Doors at seven"),
        new JournalEntry(Heard.AddSeconds(-12), JournalKind.Note, -1, "Copilot asked: are we late?"),
        new JournalEntry(Heard.AddSeconds(-10), JournalKind.Desk, 3, "Proposed p17: AGM → this Stagewatch: message \"Two minutes\" warn off ack") { Node = "a1b2c3d4", Via = "node key 'AGM'" },
        new JournalEntry(Heard.AddSeconds(-5), JournalKind.Desk, 3, "Rejected p18 (deck): AI agent → Stage left: UNDO — not now") { Node = "9f8e7d6c", Via = "Stream Deck XL, 10.0.0.9" },
        new HeardLine(Heard, PlayMap, PeerLines.Joins("Stage left"), null, "node key 'Stage left'", Note: true).Entry(3),
        new HeardLine(Heard.AddSeconds(1), PlayMap, "PLAYLIST GO show — Intro", At, "node key 'Stage left'", Note: false).Entry(3),
        new HeardLine(Heard.AddSeconds(2), Agm, "=HYPERLINK(\"x\") <b>Rejected p99 (operator)</b> & co", null, "node key 'AGM'", Note: false).Entry(3),
        new HeardLine(Heard.AddSeconds(3), new NodeRef("patterns", "pat7", ""), "take wall — done, \"Wall A\"", At + 5000, "the link 'foh'", Note: false).Entry(3),
        new JournalEntry(Heard.AddSeconds(4), JournalKind.Note, 3, "AI agent proposed: nudge 60 — waits for approval"),
    ];

    /// <summary>The report's sections: the AI section, the show network section and network.csv.</summary>
    static void Sections(Action<bool, string> Check)
    {
        var report = ShowReport.Build("Summit", new Rundown(), Mixed(), null, Utc, Heard.AddMinutes(1));
        Check(report.AiDecisions.Select(d => d.Text).SequenceEqual([
                "Copilot asked: are we late?",
                "Proposed p17: AGM → this Stagewatch: message \"Two minutes\" warn off ack",
                "Rejected p18 (deck): AI agent → Stage left: UNDO — not now",
                "AI agent proposed: nudge 60 — waits for approval"]),
            $"report: the AI section holds every desk row — the rejection included — and the copilot's notes, in order; no plain note and no peer's line, whatever it opens with ({string.Join(" | ", report.AiDecisions.Select(d => d.Text))})");
        Check(report.Network.Select(l => (l.Node, l.Kind, l.NodeId, l.Note)).SequenceEqual([
                ("Stage left", "playmap", "9f8e7d6c", true), ("Stage left", "playmap", "9f8e7d6c", false), ("AGM, \"hall\" desk", "messenger", "a1b2c3d4", false), ("patterns pat7", "patterns", "pat7", false)])
              && report.Network.Select(l => l.Utc).SequenceEqual(report.Network.Select(l => l.Utc).Order()) && report.Network[1] is { PeerAtMs: At, Via: "node key 'Stage left'", Text: "PLAYLIST GO show — Intro" },
            "report: the show network holds the peer rows alone, in the order heard, each under the name the desk gave its node");

        var html = report.ToHtml(Utc);
        var ai = html.IndexOf("<h2>AI suggestions and decisions</h2>", StringComparison.Ordinal);
        var network = html.IndexOf("<h2>The show network</h2>", StringComparison.Ordinal);
        var aiSection = ai >= 0 && network > ai ? html[ai..network] : "";
        var networkSection = network >= 0 ? html[network..] : "";
        Check(aiSection.Contains("Rejected p18 (deck): AI agent → Stage left: UNDO — not now", StringComparison.Ordinal) && !aiSection.Contains("PLAYLIST GO", StringComparison.Ordinal)
              && !aiSection.Contains("Rejected p99", StringComparison.Ordinal) && !html.Contains("Stream Deck", StringComparison.Ordinal) && !html.Contains("10.0.0.9", StringComparison.Ordinal),
            "report: the AI section's page shows the rejection, no peer's words, and never who decided from where");
        Check(networkSection.Contains("Peer lines are a sample; each node's own journal is the record.", StringComparison.Ordinal)
              && networkSection.Contains("<td class=\"n\">03:20:16</td><td>Stage left<br><small>playmap</small></td><td class=\"n\">its clock 03:20:11</td><td>node key &#39;Stage left&#39;</td><td>PLAYLIST GO show — Intro</td>", StringComparison.Ordinal)
              && networkSection.Contains("<td class=\"n\">—</td><td>node key &#39;Stage left&#39;</td><td><i>Stage left joins the report; its earlier lines are in its own journal</i></td>", StringComparison.Ordinal)
              && networkSection.Contains("its clock 03:20:16</td><td>the link &#39;foh&#39;</td>", StringComparison.Ordinal)
              && networkSection.Contains("=HYPERLINK(&quot;x&quot;) &lt;b&gt;Rejected p99 (operator)&lt;/b&gt; &amp; co", StringComparison.Ordinal) && !networkSection.Contains("<b>", StringComparison.Ordinal),
            "report: the show network section says it is a sample and lists each line with this Stagewatch's time, the node, its clock and the road — a peer's words as text, the desk's own in italics");
        Check(!ShowReport.Build("Summit", new Rundown(), Mixed().Where(e => e.Kind != JournalKind.Peer).ToList(), null, Utc, Heard).ToHtml(Utc).Contains("The show network", StringComparison.Ordinal),
            "report: with no peer row there is no show network section");

        var csv = report.NetworkCsv(Utc);
        const string expected =
            "# peer lines are a sample; each node's own journal is the record\r\n"
            + "Time,Node,Kind,Node id,Node time,Via,Text\r\n"
            + "2026-10-25 03:20:15,Stage left,playmap,9f8e7d6c,,node key 'Stage left',Stage left joins the report; its earlier lines are in its own journal\r\n"
            + "2026-10-25 03:20:16,Stage left,playmap,9f8e7d6c,2026-10-25 03:20:11,node key 'Stage left',PLAYLIST GO show — Intro\r\n"
            + "2026-10-25 03:20:17,\"AGM, \"\"hall\"\" desk\",messenger,a1b2c3d4,,node key 'AGM',\"'=HYPERLINK(\"\"x\"\") <b>Rejected p99 (operator)</b> & co\"\r\n"
            + "2026-10-25 03:20:18,patterns pat7,patterns,pat7,2026-10-25 03:20:16,the link 'foh',\"take wall — done, \"\"Wall A\"\"\"\r\n";
        Check(csv == expected, $"report: network.csv says it is a sample, names its columns, and gives each line with its Via — quoted where it must be, and no peer's cell a formula\n{csv}");
        Check(ShowReports.Format("network") == ShowReports.Network && ShowReports.Format("csv") == "csv" && ShowReports.Format("anything") == "html" && ShowReports.Format(null) == "html"
              && ShowReports.Render(report, ShowReports.Network, Utc) == csv && ShowReports.Render(report, "csv", Utc) == report.ToCsv(Utc) && ShowReports.Render(report, "html", Utc) == html
              && Enum.GetValues<FeatureTier>().All(t => ShowReports.Allowed(ShowReports.Network, t) == ShowReports.Allowed("csv", t)),
            "report: network is a format of its own, rendered as network.csv and licensed as the report's CSV is");
        // The show network's lines straight from the journal are the report's own, and ask for no report to be built.
        Check(ShowReports.NetworkCsv(Mixed(), Utc) == csv && ShowReport.NetworkOf(Mixed()).SequenceEqual(report.Network)
              && ShowReports.Text(ShowReports.Network, Mixed(), Utc, () => throw new InvalidOperationException("no report is built for the show network's lines")) == csv
              && ShowReports.Text("csv", Mixed(), Utc, () => report) == report.ToCsv(Utc) && ShowReports.Text(null, Mixed(), Utc, () => report) == html,
            "report: network.csv is read from the journal's peer rows in one pass, with no report built; the other formats render the report");
        // Where an operator-proved context was posted from is the journal file's alone: no page, CSV, context or model input prints it.
        var traced = Mixed();
        traced.Add((new HeardLine(Heard.AddSeconds(5), new NodeRef("patterns", "pat9", "Patterns B"), "take wall — done", At + 6000, "the operator's credential", Note: false) { From = "10.0.0.77" }).Entry(3));
        var tracedReport = ShowReport.Build("Summit", new Rundown(), traced, null, Utc, Heard.AddMinutes(1));
        var tracedCsv = tracedReport.NetworkCsv(Utc);
        Check(traced[^1].From == "10.0.0.77" && tracedCsv.EndsWith("2026-10-25 03:20:20,Patterns B,patterns,pat9,2026-10-25 03:20:17,the operator's credential,take wall — done\r\n", StringComparison.Ordinal)
              && !tracedCsv.Contains("10.0.0.77", StringComparison.Ordinal) && !tracedReport.ToHtml(Utc).Contains("10.0.0.77", StringComparison.Ordinal)
              && !tracedReport.ToCsv(Utc).Contains("10.0.0.77", StringComparison.Ordinal)
              && !ShowContext.Build(new ContextInput(DeskTests.DeskId, "Hall A", Heard.AddMinutes(1), new ShowSnapshot { Title = "Keynote" }, false, [], [], traced, Utc, 0)).Contains("10.0.0.77", StringComparison.Ordinal)
              && !AiContext.SummaryInput(new AppSettings(), traced, Utc).Contains("10.0.0.77", StringComparison.Ordinal),
            "report: the address an operator-proved context was posted from is in the journal row and in no page, CSV, context or summary input");
        // A line whose own time no calendar holds (a journal file edited by hand) shows no time, and the page still renders.
        var odd = ShowReport.Build("Summit", new Rundown(), [new JournalEntry(Heard, JournalKind.Peer, -1, "x") { Node = "pm1", PeerAtMs = long.MaxValue }], null, Utc, Heard);
        Check(odd.NetworkCsv(Utc).EndsWith("2026-10-25 03:20:15,node pm1,,pm1,,,x\r\n", StringComparison.Ordinal) && odd.ToHtml(Utc).Contains("<td class=\"n\">—</td>", StringComparison.Ordinal),
            "report: a peer row with a time no calendar holds, and no name or kind, is listed with no node time, by its id");
    }

    /// <summary>Stagewatch's own readers: no peer row is said again as this show's own, or crowds the show's own rows out.</summary>
    static void OwnReaders(Action<bool, string> Check)
    {
        var journal = Mixed();
        for (var i = 0; i < 40; i++)
            journal.Add(new HeardLine(Heard.AddSeconds(10 + i), PlayMap, "PEER LINE " + i, At + 10000 + i, "node key 'Stage left'", Note: false).Entry(3));
        var context = ShowContext.Build(new ContextInput(DeskTests.DeskId, "Hall A", Heard.AddMinutes(1), new ShowSnapshot { Title = "Keynote" }, false, [], [], journal, Utc, 0));
        using var doc = JsonDocument.Parse(context);
        var recent = doc.RootElement.GetProperty("recent").EnumerateArray().Select(r => r.GetProperty("text").GetString()!).ToList();
        Check(recent.SequenceEqual([
                "03:19:55 ShowOpened: Summit", "03:20:00 Note: Doors at seven", "03:20:03 Note: Copilot asked: are we late?",
                "03:20:05 Desk: Proposed p17: AGM → this Stagewatch: message \"Two minutes\" warn off ack", "03:20:10 Desk: Rejected p18 (deck): AI agent → Stage left: UNDO — not now",
                "03:20:19 Note: AI agent proposed: nudge 60 — waits for approval"])
              && !context.Contains("PEER LINE", StringComparison.Ordinal) && !context.Contains("PLAYLIST GO", StringComparison.Ordinal) && !context.Contains("joins the report", StringComparison.Ordinal),
            $"report: Stagewatch's own recent holds its own rows — forty peer lines after them push none out, and no peer row is in it ({string.Join(" | ", recent)})");
        Check(ShowJournal.LastOwn(journal, 3).Select(e => e.Text).SequenceEqual([
                  "Proposed p17: AGM → this Stagewatch: message \"Two minutes\" warn off ack", "Rejected p18 (deck): AI agent → Stage left: UNDO — not now", "AI agent proposed: nudge 60 — waits for approval"])
              && ShowJournal.LastOwn(journal, 0).Count == 0 && ShowJournal.LastOwn([], 5).Count == 0 && ShowJournal.LastOwn(journal, 500).Count == 6,
            "report: the journal's last own rows are read from its end, oldest first, peer rows left out");
        var copilot = AiContext.Copilot(new CopilotFacts(Heard.AddMinutes(1), Utc, "{}"u8.ToArray(), "{}"u8.ToArray(), [], [], [], 0, OverrunModel.Learn([]), [], journal));
        using var facts = JsonDocument.Parse(copilot);
        var said = facts.RootElement.GetProperty("recent").EnumerateArray().Select(r => r.GetString()!).ToList();
        Check(said.SequenceEqual(recent) && facts.RootElement.GetProperty("now").GetString() == "2026-10-25 03:21:15"
              && !copilot.Contains("PEER LINE", StringComparison.Ordinal) && !copilot.Contains("Peer:", StringComparison.Ordinal) && !copilot.Contains("PLAYLIST GO", StringComparison.Ordinal)
              && !copilot.Contains("joins the report", StringComparison.Ordinal) && !copilot.Contains("HYPERLINK", StringComparison.Ordinal),
            $"report: the copilot's recent lines are this show's own rows too — forty peer lines after them push none out, and no peer row is among its facts' journal ({string.Join(" | ", said)})");
        var summary = AiContext.SummaryInput(new AppSettings(), journal, Utc);
        Check(summary.Contains("Rejected p18 (deck)", StringComparison.Ordinal) && summary.Contains("\"t\":\"03:20:10\"", StringComparison.Ordinal)
              && !summary.Contains("PEER LINE", StringComparison.Ordinal) && !summary.Contains("HYPERLINK", StringComparison.Ordinal) && !summary.Contains("\"k\":\"Peer\"", StringComparison.Ordinal),
            "report: a summary is drafted from this show's own rows — another node's words are no part of a model's input through it");
    }

    /// <summary>Every time the report, network.csv, the context and a summary's input write is the same under any culture.</summary>
    static void Culture(Action<bool, string> Check)
    {
        var journal = Mixed();
        var rundown = new Rundown { Id = "ro-1" };
        rundown.Items.Add(new RundownItem { Id = "it-1", Title = "Welcome", DurationSeconds = 300 });
        journal.Add(new JournalEntry(Heard.AddSeconds(30), JournalKind.ItemStart, 0, "Welcome") { RundownId = "ro-1", ItemId = "it-1", Execution = "e1" });
        journal.Add(new JournalEntry(Heard.AddSeconds(40), JournalKind.Ack, 0, "Two minutes", 4.5) { RundownId = "ro-1", ItemId = "it-1", Execution = "e1" });
        journal.Add(new JournalEntry(Heard.AddSeconds(400), JournalKind.ItemEnd, 0, "Welcome") { RundownId = "ro-1", ItemId = "it-1", Execution = "e1" });
        var items = rundown.Items.ToList();
        (string Html, string Csv, string Network, string Context, string Summary, string Path, string Copilot, string Assistant) Written()
        {
            var built = ShowReport.Build("Summit", rundown, journal, Heard, Utc, Heard.AddMinutes(10));
            // The zone's own name is the machine's words for it, in the machine's language: the report is held to its own words.
            var report = new ShowReport { ShowName = built.ShowName, GeneratedUtc = built.GeneratedUtc, Rows = built.Rows, TimeZoneName = "UTC", Summary = built.Summary, AiDecisions = built.AiDecisions, Network = built.Network };
            return (report.ToHtml(Utc), report.ToCsv(Utc), report.NetworkCsv(Utc),
                ShowContext.Build(new ContextInput(DeskTests.DeskId, "Hall A", Heard.AddMinutes(10), new ShowSnapshot { Title = "Keynote" }, false, [], [], journal, Utc, 0)),
                AiContext.SummaryInput(new AppSettings(), journal, Utc),
                ShowReports.PathFor("shows", "Summit", new DateTime(2026, 10, 25, 14, 32, 0), ShowReports.Network),
                AiContext.Copilot(new CopilotFacts(Heard.AddMinutes(10), Utc, "{}"u8.ToArray(), "{}"u8.ToArray(), [], [], [], 0, OverrunModel.Learn([]), items, journal)),
                AiContext.Assistant(items, ScheduleEngine.Project(new ScheduleEngine.Input(items, Heard, -1, false, TimeSpan.Zero, new Dictionary<int, ItemActual>(), Heard.AddMinutes(10), Utc, TimeSpan.Zero)),
                    Utc, Heard.AddMinutes(10), -1, TimeSpan.Zero, TimeSpan.Zero, 0));
        }
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var invariant = Written();
            Check(invariant.Csv.EndsWith("\r\n1,Welcome,,Talk,2026-10-25 03:20:15,00:05:00,,2026-10-25 03:20:45,2026-10-25 03:26:55,00:06:10,00:01:10,,0,1,4.5\r\n", StringComparison.Ordinal)
                  && invariant.Html.Contains("generated Sunday 25 October 2026 03:30 · UTC", StringComparison.Ordinal) && invariant.Html.Contains("<td class=\"n\">03:20:45</td>", StringComparison.Ordinal)
                  && invariant.Network.Contains("\r\n2026-10-25 03:20:16,Stage left,playmap,9f8e7d6c,2026-10-25 03:20:11,", StringComparison.Ordinal)
                  && invariant.Context.Contains("\"text\":\"03:20:45 ItemStart: Welcome\"", StringComparison.Ordinal)
                  && invariant.Path.Replace('\\', '/') == "shows/Reports/Summit 2026-10-25 1432.network.csv",
                $"report culture: the times as written are Gregorian yyyy-MM-dd HH:mm:ss and HH:mm:ss ({invariant.Path})");
            Check(invariant.Copilot.Contains("\"now\":\"2026-10-25 03:30:15\"", StringComparison.Ordinal) && invariant.Copilot.Contains("\"03:20:45 ItemStart: Welcome\"", StringComparison.Ordinal)
                  && invariant.Assistant.Contains("\"now\":\"03:30:15\"", StringComparison.Ordinal) && invariant.Assistant.Contains("\"planned_start\":\"03:20\"", StringComparison.Ordinal),
                $"report culture: what a model is given — the copilot's now and recent lines, the assistant's now and planned starts — is timed the same way ({invariant.Assistant})");

            var thai = new CultureInfo("th-TH");
            var dotted = (CultureInfo)new CultureInfo("fi-FI").Clone();
            dotted.DateTimeFormat.TimeSeparator = ".";
            dotted.DateTimeFormat.DateSeparator = ".";
            dotted.NumberFormat.NumberDecimalSeparator = ",";
            Check(Heard.ToString("yyyy", thai) == "2569" && Heard.ToString("HH:mm", dotted) == "03.20" && 4.5.ToString(dotted) == "4,5",
                $"report culture: th-TH counts Buddhist years and the dotted culture writes times with '.' and decimals with ',' on this machine ({Heard.ToString("yyyy", thai)}, {Heard.ToString("HH:mm", dotted)})");
            foreach (var (culture, name) in new[] { (thai, "th-TH"), (dotted, "a culture with '.' as its time separator") })
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
                var written = Written();
                Check(written.Csv == invariant.Csv, $"report culture: under {name}, the report's CSV is byte for byte the invariant one");
                Check(written.Network == invariant.Network, $"report culture: under {name}, network.csv is byte for byte the invariant one");
                Check(written.Html == invariant.Html, $"report culture: under {name}, the report's page is byte for byte the invariant one");
                Check(written.Context == invariant.Context, $"report culture: under {name}, the context's recent lines are the invariant ones");
                Check(written.Summary == invariant.Summary && written.Path == invariant.Path, $"report culture: under {name}, a summary's input and a saved report's name are the invariant ones");
                Check(written.Copilot == invariant.Copilot && written.Assistant == invariant.Assistant, $"report culture: under {name}, the copilot's facts and the assistant's view are the invariant ones");
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }

    /// <summary>A journal written before peer rows existed loads, and its report is byte for byte what it was (the fixtures hold both).</summary>
    static void OldJournal(Action<bool, string> Check)
    {
        var fixtures = ShowBusSpecTests.Master() is { } master ? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(master)!)!, "tests", "Stagewatch.Tests", "fixtures") : null;
        string? Fixture(string name) => fixtures is not null && System.IO.File.Exists(Path.Combine(fixtures, name)) ? Path.Combine(fixtures, name) : null;
        var journalPath = Fixture("journal-before-peers.jsonl");
        var htmlPath = Fixture("report-before-peers.html");
        var csvPath = Fixture("report-before-peers.csv");
        Check(journalPath is not null && htmlPath is not null && csvPath is not null, "report: the journal from before peer rows and its report are in the suite's fixtures");
        if (journalPath is null || htmlPath is null || csvPath is null) return;
        var journal = ShowJournal.Load(journalPath);
        var rundown = new Rundown { Id = "ro-1" };
        rundown.Items.Add(new RundownItem { Id = "it-1", Title = "Welcome", Speaker = "Sam \"MC\" Lee", DurationSeconds = 300 });
        rundown.Items.Add(new RundownItem { Id = "it-2", Title = "Keynote, part 1", DurationSeconds = 1800, AdjustSeconds = 60 });
        rundown.Items.Add(new RundownItem { Id = "it-3", Title = "Close", DurationSeconds = 120 });
        var clock = new ShowClock(TimeZoneInfo.CreateCustomTimeZone("show", TimeSpan.FromHours(7), "show", "show"));
        var built = ShowReport.Build("Autumn gala", rundown, journal, new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc), clock, new DateTime(2026, 10, 25, 19, 0, 0, DateTimeKind.Utc));
        var report = new ShowReport { ShowName = built.ShowName, GeneratedUtc = built.GeneratedUtc, Rows = built.Rows, TimeZoneName = "show time", Summary = built.Summary, AiDecisions = built.AiDecisions, Network = built.Network };
        var html = report.ToHtml(clock);
        var csv = report.ToCsv(clock);
        Check(journal.Count == 15 && journal.Count(e => e.Kind == JournalKind.Desk) == 2 && journal.All(e => e.Node is null && e.PeerAtMs is null) && built.Network.Count == 0,
            $"report: a journal from before peer rows loads whole — {journal.Count} rows, none about a node");
        Check(html == System.IO.File.ReadAllText(htmlPath), "report: its page is byte for byte what it was before peer rows");
        Check(csv == System.IO.File.ReadAllText(csvPath), "report: its CSV is byte for byte what it was before peer rows");
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("th-TH");
            Check(report.ToHtml(clock) == html && report.ToCsv(clock) == csv, "report: and the same bytes on a PC set to Thai");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }
}
