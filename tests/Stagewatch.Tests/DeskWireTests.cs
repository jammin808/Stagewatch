using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// The two verbs only a person sends (Show Bus §11): <c>peer &lt;app&gt; &lt;line&gt;</c> and <c>desk approve|reject</c>. The grammar —
/// one line, whole, never altered, and no model handed either verb; the sender gate — the PIN or a control key, with no PIN this PC or
/// a listed address, never an agent, and not Companion TCP or OSC in this release; the desk's one executor, by whoever the door named;
/// the receipt of a sent line — what its app made of it, with its one follow-up; and both verbs over a loopback web server, HTTP and
/// WebSocket, a line reaching its app byte for byte through each door.
/// </summary>
static class DeskWireTests
{
    public static async Task Run(Action<bool, string> Check)
    {
        Grammar(Check);
        Gate(Check);
        Verbs(Check);
        await Receipts(Check);
        await Transports(Check);
    }

    static ShowCommand Parse(string verb, params string[] args)
        => ShowCommandParser.TryParse(verb, args, out var cmd, out var error) ? cmd : throw new InvalidOperationException(error);

    static string Refusal(string verb, params string[] args) => ShowCommandParser.TryParse(verb, args, out _, out var error) ? "" : error;

    static string LineRefusal(string line) => ShowCommandParser.TryParseLine(line, out _, out var error) ? "" : error;

    static void Grammar(Action<bool, string> Check)
    {
        const string Quoted = "SET out1 name \"Left wall\"";
        var peer = Parse("peer", "pm-a1b2", Quoted);
        Check(peer.Kind == ShowCommandKind.Peer && peer.Get<PeerArgs>() == new PeerArgs("pm-a1b2", Quoted) && peer.By is null,
            "peer grammar: an app and one line, the line whole with its own quote marks — and no parser names a sender");
        Check(Parse("peer", "PlayMap Hall", "ADD media {\"path\": \"a b.mp4\"}").Get<PeerArgs>().Line == "ADD media {\"path\": \"a b.mp4\"}"
              && Parse("PEER", "playmap", " UNDO ").Get<PeerArgs>() == new PeerArgs("playmap", " UNDO "),
            "peer grammar: no word of the line is split, joined, trimmed or unescaped");
        Check(Refusal("peer", "playmap", "SET", "out1") == ShowCommandParser.PeerTakes && Refusal("peer", "playmap") == ShowCommandParser.PeerTakes
              && Refusal("peer") == ShowCommandParser.PeerTakes && Refusal("peer", "playmap", " ") == ShowCommandParser.PeerTakes
              && ShowCommandParser.PeerTakes == "peer takes an app and one line — quote the line as one argument",
            "peer grammar: more than one token after the app, or none, is refused — quote the line as one argument");
        Check(Refusal("peer", "playmap", "NOTE a\nPANIC") == LineFence.Control && Refusal("peer", "play\u0001map", "UNDO") == LineFence.Control,
            "peer grammar: a control character in the app or the line is refused, never cut at");

        // A typed line: its quote marks are dropped as it is split, so a line that holds quote marks of its own is refused, never altered.
        Check(ShowCommandParser.TryParseLine("peer agmessage \"notice Q&A in 2 minutes\"", out var typed, out _)
              && typed.Get<PeerArgs>() == new PeerArgs("agmessage", "notice Q&A in 2 minutes")
              && ShowCommandParser.TryParseLine("peer \"PlayMap Hall\" UNDO", out var named, out _) && named.Get<PeerArgs>() == new PeerArgs("PlayMap Hall", "UNDO"),
            "peer grammar: a typed line takes the line as one quoted argument");
        Check(LineRefusal("peer pm \"SET out1 name \\\"Left wall\\\"\"") == ShowCommandParser.PeerQuotes
              && LineRefusal("peer pm \"SET name \\\"Left\\\"\"") == ShowCommandParser.PeerQuotes
              && LineRefusal("peer pm SET\"x y\"") == ShowCommandParser.PeerQuotes && LineRefusal("peer pm \"UNDO") == ShowCommandParser.PeerQuotes,
            "peer grammar: a typed line whose own line holds quote marks is refused — it would reach its app altered");
        Check(LineRefusal("peer pm SET out1 name Left") == ShowCommandParser.PeerTakes && LineRefusal("note \"a \\\"b\\\" c\"") == "",
            "peer grammar: an unquoted line of several words is refused; other verbs' typed lines read as they always have");

        var approve = Parse("desk", "approve", "p17");
        var high = Parse("desk", "APPROVE", "p18-k3m9", "HIGH");
        var next = Parse("desk", "approve", "NEXT");
        var reject = Parse("desk", "reject", "next", "not", "now");
        Check(approve.Kind == ShowCommandKind.Desk && approve.Get<DeskArgs>() == new DeskArgs("approve", "p17", false, "")
              && high.Get<DeskArgs>() == new DeskArgs("approve", "p18-k3m9", true, "") && next.Get<DeskArgs>() == new DeskArgs("approve", "next", false, "")
              && reject.Get<DeskArgs>() == new DeskArgs("reject", "next", false, "not now") && Parse("desk", "reject", "p3").Get<DeskArgs>() == new DeskArgs("reject", "p3", false, ""),
            "desk grammar: approve <id> [high], approve next, reject <id>|next [why]");
        Check(Refusal("desk") == "desk takes approve or reject" && Refusal("desk", "run", "p1") == "desk takes approve or reject"
              && Refusal("desk", "approve") == "desk approve takes a proposal id (p17) or next" && Refusal("desk", "approve", "17") == "desk approve takes a proposal id (p17) or next"
              && Refusal("desk", "reject", "p1-zz") == "desk reject takes a proposal id (p17) or next" && Refusal("desk", "approve", "p1", "now").Length > 0
              && Refusal("desk", "approve", "p1", "high", "please").Length > 0 && Refusal("desk", "reject", "p1", new string('x', ShowCommandParser.DeskWhyChars + 1)).Length > 0,
            "desk grammar: anything else is refused in words");
        Check(ProposalDesk.LooksLikeId("p1") && ProposalDesk.LooksLikeId("p17-k3m9") && !ProposalDesk.LooksLikeId("p") && !ProposalDesk.LooksLikeId("next")
              && !ProposalDesk.LooksLikeId("p1-") && !ProposalDesk.LooksLikeId(null),
            "desk grammar: an id is p<n>, or whole, p<n>-<run>");

        // No model is handed either verb, and neither is anything but a person's.
        Check(!ShowCommandParser.Help.Contains("peer", StringComparison.Ordinal) && !ShowCommandParser.Help.Contains("| desk ", StringComparison.Ordinal)
              && !ShowCommandParser.Help.Contains("desk approve", StringComparison.Ordinal) && !ShowCommandParser.Help.Contains("desk reject", StringComparison.Ordinal)
              && ShowCommandParser.PersonHelp.StartsWith(ShowCommandParser.Help, StringComparison.Ordinal)
              && ShowCommandParser.PersonHelp.Contains("peer <app> <line>", StringComparison.Ordinal) && ShowCommandParser.PersonHelp.Contains("desk approve <id> [high]", StringComparison.Ordinal)
              && ShowCommandParser.PersonHelp.Contains("desk reject <id|next> [why]", StringComparison.Ordinal),
            "person verbs: the grammar the AIs read leaves peer and desk out; the one people read lists both");
        var rows = ShowContext.CommandsFor([new PlayoutRow("stage-left", "PlayMap Stage left", "playmap", Starts: true)]);
        Check(rows.Count > 0 && !rows.Any(r => r.Verb.StartsWith("peer", StringComparison.OrdinalIgnoreCase) || r.Verb.StartsWith("desk", StringComparison.OrdinalIgnoreCase)),
            "person verbs: neither is a row of this Stagewatch's own context, so no peer or AI is told it may propose one");
        Check(CommandRisk.Of(peer) == LineRisk.High && CommandRisk.Of(approve) == LineRisk.High && Copilot.Classify(peer) == CopilotRisk.High && Copilot.Classify(reject) == CopilotRisk.High,
            "person verbs: both rate high by Core's table, and the copilot's reading follows");
        Check(CloudAdvisor.Validate("peer playmap UNDO") is null && CloudAdvisor.Validate("desk approve next") is null,
            "person verbs: neither is on the advisor's allow-list");
        var plan = Copilot.ParsePlan("""{"reply":"ok","question":"","steps":[{"command":"peer playmap \"LINK STAGEWATCH host swk_abc\"","why":"x"},{"command":"desk approve next","why":"y"},{"command":"next","why":"go on"}]}""");
        Check(plan.Steps is [{ Command: "next" }] && plan.Dropped.Count == 2 && plan.Dropped[0] == "peer (" + DeskOrigin.PersonSends + ")"
              && plan.Dropped[1] == "desk (" + DeskOrigin.PersonDecides + ")" && !plan.Dropped.Any(d => d.Contains("swk_", StringComparison.Ordinal)),
            $"person verbs: a copilot plan's peer and desk steps are dropped in words that never quote the line ({string.Join(" | ", plan.Dropped)})");
        // A step that opens with either verb and does not parse is dropped the same way: by its verb, its words never quoted — and
        // so is one past the plan's last step, which is otherwise quoted as one too many.
        var filler = string.Concat(Enumerable.Repeat("""{"command":"next","why":"go on"},""", 12));
        var misShaped = Copilot.ParsePlan("{\"reply\":\"ok\",\"question\":\"\",\"steps\":[{\"command\":\"peer playmap LINK swk_abcdefghijkmnpqrstuvwxyz234567\",\"why\":\"x\"},"
                                          + "{\"command\":\"desk\",\"why\":\"y\"},{\"command\":\"PEER\",\"why\":\"z\"}," + filler
                                          + "{\"command\":\"peer playmap \\\"LINK host swk_abcdefghijkmnpqrstuvwxyz234567\\\"\",\"why\":\"late\"}]}");
        Check(misShaped.Steps.Count == 12 && misShaped.Dropped is [var d1, var d2, var d3, var d4] && d1 == "peer (" + DeskOrigin.PersonSends + ")"
              && d2 == "desk (" + DeskOrigin.PersonDecides + ")" && d3 == d1 && d4 == d1 && !misShaped.Dropped.Any(d => d.Contains("swk_", StringComparison.Ordinal)),
            $"person verbs: a mis-shaped peer or desk step, and one past the plan's last, are dropped by the verb alone ({string.Join(" | ", misShaped.Dropped)})");
        Check(PlayoutGate.PersonKind("peer") == ShowCommandKind.Peer && PlayoutGate.PersonKind(" DESK ") == ShowCommandKind.Desk && PlayoutGate.PersonKind("playout") is null
              && PlayoutGate.PersonKind("peers") is null && PlayoutGate.PersonKind(null) is null
              && ProposalDesk.PersonVerbs.All(v => PlayoutGate.PersonKind(v) is { } kind && PlayoutGate.Person(kind)),
            "person verbs: a line's first word names a person's kind whatever follows it — each of the desk's person verbs, and no other word");
        Check(PlayoutGate.NeverProposed(["playout", "vt", "trust", "03e9b152"]) is not null
              && new ProposalDesk(() => "desk01", new NodeBindings(), "k3m9").Submit(
                  JsonSerializer.Serialize(new { from = new { t = "messenger", id = "ag1", name = "AGMessage" }, target = new { t = "main", id = "", name = "" }, line = "desk approve next" }),
                  NodeCredential.Operator, (_, _) => RiskVerdict.Unlisted, DeskTests.T0).Proposal is null,
            "person verbs: a desk line aimed at the desk is never a proposal");
    }

    static void Gate(Action<bool, string> Check)
    {
        var lan = IPAddress.Parse("10.0.0.7");
        IReadOnlyList<IPAddress> none = [], listed = [lan];
        CommandSender Sender(bool thisPc = false, bool credentialed = false, bool otherSite = false) => new(lan, thisPc, credentialed, otherSite);
        Check(PlayoutGate.Person(ShowCommandKind.Peer) && PlayoutGate.Person(ShowCommandKind.Desk) && !PlayoutGate.Person(ShowCommandKind.Playout)
              && !PlayoutGate.Drives(ShowCommandKind.Peer) && !PlayoutGate.Drives(ShowCommandKind.Desk) && !PlayoutGate.DeskOnly(ShowCommandKind.Peer),
            "person gate: peer and desk are a person's lines — neither a line that drives a playout link, nor a desk-only one");
        foreach (var kind in new[] { ShowCommandKind.Peer, ShowCommandKind.Desk })
        {
            var verb = kind.ToString().ToLowerInvariant();
            foreach (var door in new[] { CommandDoor.Http, CommandDoor.WebSocket })
            {
                Check(PlayoutGate.Check(kind, door, Sender(credentialed: true), none) is null && PlayoutGate.Check(kind, door, Sender(credentialed: true, otherSite: true), none) is null,
                    $"person gate: {verb} over {door} with the PIN or a control key passes, from any machine");
                Check(PlayoutGate.Check(kind, door, Sender(), none) == PlayoutGate.Words(kind, door) && PlayoutGate.Words(kind, door).StartsWith(verb + " acts on another show app", StringComparison.Ordinal)
                      && PlayoutGate.Words(kind, door).EndsWith(PlayoutGate.SettingWords, StringComparison.Ordinal),
                    $"person gate: {verb} over {door} with no PIN set, from an unlisted machine, is refused in words that name the setting");
                Check(PlayoutGate.Check(kind, door, Sender(), listed) is null && PlayoutGate.Check(kind, door, Sender(thisPc: true), none) is null,
                    $"person gate: {verb} over {door} with no PIN set passes from this PC and from a listed address");
                Check(PlayoutGate.Check(kind, door, Sender(thisPc: true, otherSite: true), listed) == PlayoutGate.OtherSiteWords(kind, door)
                      && PlayoutGate.OtherSiteWords(kind, door).StartsWith(verb + " from another site's web page", StringComparison.Ordinal),
                    $"person gate: {verb} over {door} from another site's page proves nothing, wherever its browser runs");
            }
            var notAgent = kind == ShowCommandKind.Desk ? DeskOrigin.PersonDecides : DeskOrigin.PersonSends;
            Check(PlayoutGate.Check(kind, CommandDoor.Mcp, Sender(thisPc: true, credentialed: true), listed) == notAgent,
                $"person gate: {verb} over MCP is refused with any key, from any machine — an agent is not a person");
            Check(PlayoutGate.Check(kind, CommandDoor.Osc, Sender(thisPc: true), listed) == PlayoutGate.PersonLater(kind, CommandDoor.Osc)
                  && PlayoutGate.Check(kind, CommandDoor.Companion, Sender(thisPc: true, credentialed: true), listed) == PlayoutGate.PersonLater(kind, CommandDoor.Companion)
                  && PlayoutGate.PersonLater(kind, CommandDoor.Companion).Contains("Companion TCP does not take it in this release", StringComparison.Ordinal),
                $"person gate: {verb} over OSC and Companion TCP is refused in this release, this PC and listed addresses included");
        }
        Check(PlayoutGate.SettingWords == "Settings → Remote → May drive other show apps"
              && PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Osc) == "playout from another machine over OSC needs its address in Settings → Remote → May drive other show apps (OSC carries no PIN)",
            "person gate: the one list is named for what it lets a machine do — drive other show apps");
    }

    /// <summary>A person's line as its door stamps it.</summary>
    static ShowCommand By(ShowCommand c, DeskOrigin? by) => c with { By = by };

    static void Verbs(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var pm = NodeCredential.Key("kM");
        b.Desk.CollectApproved("pm1", pm);
        var wire = DeskOrigin.Wire("key 'FOH tablet', 10.0.0.7");
        var undo = Parse("peer", "playmap", "UNDO");
        Check(b.Desk.Run(undo) is { Ok: false, Words: DeskService.PersonSends, Proposal: null } && b.Desk.Queue.All().Count == 0 && b.Rows.Count == 0,
            "desk verbs: a peer line whose door named nobody is nobody's — refused, nothing queued");
        foreach (var notAPerson in new[] { DeskOrigin.Mcp, DeskOrigin.Advisor, DeskOrigin.Copilot, DeskOrigin.Proposal, DeskOrigin.Nobody })
            Check(b.Desk.Run(By(undo, notAPerson)) is { Ok: false, Words: DeskService.PersonSends } && b.Desk.Run(By(Parse("desk", "approve", "next"), notAPerson)) is { Ok: false, Words: DeskService.PersonDecides },
                $"desk verbs: peer and desk from {notAPerson.Role} are refused — a person sends, a person approves");
        var sent = b.Desk.Run(By(undo, wire));
        Check(sent is { Ok: true, Words: "sent to PlayMap Hall as p1 — it collects it and reports back" } && sent.Proposal is { Id: "p1-k3m9", DecidedBy: "wire", State: SharedProposalState.Approved, Line: "UNDO" }
              && b.Last == "Sent p1 to PlayMap Hall (wire): UNDO" && b.LastWho == "key 'FOH tablet', 10.0.0.7" && sent.Shown == sent.Words,
            $"desk verbs: a peer line from the wire is born approved by the role; who and from where stay out of the row's words ({b.Last})");
        var fired = b.Desk.Run(By(Parse("peer", "agmessage", "notice Q&A in 2 minutes"), DeskOrigin.Trigger()));
        Check(fired is { Ok: false } && fired.Words.Contains("has not asked this desk", StringComparison.Ordinal), "desk verbs: a trigger's peer line for an app that does not collect is refused");
        b.Desk.CollectApproved("ag1", NodeCredential.Key("kA"));
        Check(b.Desk.Run(By(Parse("peer", "agmessage", "notice Q&A in 2 minutes"), DeskOrigin.Trigger())) is { Ok: true, Proposal: { DecidedBy: "trigger", Risk: "review" } },
            "desk verbs: a trigger may send a peer line — decided by the role trigger");
        var unlisted = b.Desk.Run(By(Parse("peer", "playmap", "OUTPUTS OFF now"), wire));
        Check(unlisted is { Ok: false, Words: "PlayMap Hall does not list 'OUTPUTS OFF now' — see its verbs", Shown: "PlayMap Hall does not list that line — see its verbs" },
            "desk verbs: an unlisted line is quoted to its sender alone — the words for any other reader never quote it");
        var secret = b.Desk.Run(By(Parse("peer", "playmap", "LINK STAGEWATCH host swk_abcdefghijkmnpqrstuvwxyz234567"), wire));
        Check(secret is { Ok: false, Words: LineFence.Key } && secret.Shown == LineFence.Key && !b.Rows.Any(r => r.Words.Contains("swk_", StringComparison.Ordinal)),
            "desk verbs: a key rides in no line — refused, and no row holds it");

        // The app's word is quoted in a refusal: one that holds a key or a window's address is refused unquoted, however it is hidden.
        const string Key = "swk_abcdefghijkmnpqrstuvwxyz234567";
        var rowsThen = b.Rows.Count;
        var swapped = b.Desk.Run(By(Parse("peer", "LINK STAGEWATCH 10.0.0.5 " + Key, "playmap"), wire));
        var split = b.Desk.Run(By(Parse("peer", "swk_abcdefghijkmnpq\u200Brstuvwxyz234567", "UNDO"), wire));
        var window = b.Desk.Run(By(Parse("peer", "see /w/w7kq2m9xw7kq2m9xw7kq2m9xw7", "UNDO"), wire));
        Check(swapped is { Ok: false, Words: LineFence.Key, Proposal: null } && swapped.Shown == LineFence.Key && split is { Ok: false, Words: LineFence.Key }
              && window is { Ok: false, Words: LineFence.Window } && window.Shown == LineFence.Window && b.Desk.Resolve(Key) is { App: null, Error: LineFence.Key }
              && b.Rows.Count == rowsThen && b.Desk.Resolve("nobody") is { App: null, Error: var none } && none!.StartsWith("no app called 'nobody'", StringComparison.Ordinal),
            $"desk verbs: a key or a window's address where the app is named is refused in words that never quote it ({swapped.Words} | {split.Words} | {window.Words})");

        // Decisions: an AI's proposals for PlayMap, one review and one high.
        string Ask(string line) => b.Desk.SubmitProposal(JsonSerializer.Serialize(new
        {
            from = new { t = "messenger", id = "ag1", name = "AGMessage" }, target = new { t = "playmap", id = "pm1", name = "PlayMap Hall" }, line,
        }), NodeCredential.Key("kA")).Json;
        Ask("UNDO");
        Ask("PANIC");
        var review = b.Desk.Queue.NextPending()!;
        Check(b.Desk.Run(By(Parse("desk", "approve", "next"), DeskOrigin.Trigger())) is { Ok: false, Words: DeskService.PersonDecides } && b.Desk.Queue.Get(review.Id) is { State: SharedProposalState.Pending },
            "desk verbs: a trigger decides nothing — the proposal still waits");
        var approved = b.Desk.Run(By(Parse("desk", "approve", "next"), wire));
        Check(approved is { Ok: true, Proposal: { State: SharedProposalState.Approved, DecidedBy: "wire", Line: "UNDO" } }
              && approved.Words == $"approved {DeskService.Short(review.Id)} (AGMessage → PlayMap Hall: UNDO) — PlayMap Hall runs it and reports back",
            $"desk verbs: desk approve next approves the oldest waiting, a review one ({approved.Words})");
        var panic = b.Desk.Queue.NextPending()!;
        var refusedHigh = b.Desk.Run(By(Parse("desk", "approve", "next"), wire));
        Check(panic.Risk == "high" && refusedHigh is { Ok: false } && refusedHigh.Words == $"{DeskService.Short(panic.Id)} is high — approve it by its id: desk approve {panic.Id} high"
              && b.Desk.Run(By(Parse("desk", "approve", "next", "high"), wire)) is { Ok: false } && b.Desk.Run(By(Parse("desk", "approve", DeskService.Short(panic.Id)), wire)) is { Ok: false }
              && b.Desk.Queue.Get(panic.Id) is { State: SharedProposalState.Pending },
            "desk verbs: next never approves a high proposal, with the word or without; its id without the word does not either");
        Check(b.Desk.Run(By(Parse("desk", "approve", DeskService.Short(panic.Id), "high"), wire)) is { Ok: true, Proposal: { State: SharedProposalState.Approved } },
            "desk verbs: a high proposal is approved by its id and the word high");
        Ask("STATUS");
        var status = b.Desk.Queue.NextPending()!;
        var rejected = b.Desk.Run(By(Parse("desk", "reject", "next", "not", "during", "the", "keynote"), wire));
        Check(rejected is { Ok: true, Proposal: { State: SharedProposalState.Rejected, DecidedBy: "wire" } } && b.Last == $"Rejected {DeskService.Short(status.Id)} (wire): AGMessage → PlayMap Hall: STATUS — not during the keynote",
            $"desk verbs: desk reject carries its reason to the journal ({b.Last})");
        // The reason is journalled: one that holds a key or a window's address rejects nothing, and no row keeps it.
        Ask("STATUS");
        var kept = b.Desk.Queue.NextPending()!;
        var keyed = b.Desk.Run(By(Parse("desk", "reject", "next", "because", Key, "said", "so"), wire));
        var addressed = b.Desk.Run(By(Parse("desk", "reject", kept.Id, "see", "/w/w7kq2m9xw7kq2m9xw7kq2m9xw7"), wire));
        Check(keyed is { Ok: false, Words: LineFence.Key, Proposal: { State: SharedProposalState.Pending } } && keyed.Shown == LineFence.Key
              && addressed is { Ok: false, Words: LineFence.Window } && b.Desk.Queue.Get(kept.Id) is { State: SharedProposalState.Pending }
              && b.Rows.Any(r => r.Words == $"Not rejected {DeskService.Short(kept.Id)} (wire): AGMessage → PlayMap Hall: STATUS — {LineFence.Key}")
              && !b.Rows.Any(r => r.Words.Contains("swk_", StringComparison.Ordinal) || r.Words.Contains("/w/", StringComparison.Ordinal) || r.Words.Contains("said so", StringComparison.Ordinal)),
            $"desk verbs: a rejection whose reason holds a key or a window's address is refused, the proposal still waiting, and no row holds the reason ({b.Last})");
        Check(b.Desk.Run(By(Parse("desk", "reject", kept.Id), wire)) is { Ok: true, Proposal: { State: SharedProposalState.Rejected } },
            "desk verbs: the same proposal is rejected once the reason holds none");
        Check(b.Desk.Run(By(Parse("desk", "approve", "p99"), wire)) is { Ok: false, Words: "no such proposal" } && b.Desk.Run(By(Parse("desk", "reject", "next"), wire)) is { Ok: false, Words: "nothing is waiting for a decision" },
            "desk verbs: an id that names nothing, and next with nothing waiting, are refused in words");

        // What a sent line comes to: one waiter a proposal, completed by its end.
        var waiting = b.Desk.Settled("p1");
        Check(!waiting.IsCompleted && ReferenceEquals(waiting, b.Desk.Settled("p1-k3m9")), "desk settled: an open line's end is waited for, one waiter however it is named");
        b.Desk.CollectApproved("pm1", pm);
        Check(!waiting.IsCompleted && b.Desk.ResultOf("p1") is { Stage: "collected", Settled: false }, "desk settled: collected is not the end");
        b.Desk.ProposalResult("p1-k3m9", """{"ok":true,"words":"undone"}""", pm);
        Check(waiting.Wait(2000) && waiting.Result is { Stage: "done", Ok: true, Settled: true, Words: "PlayMap Hall ran UNDO: undone (p1)" }
              && b.Desk.Settled("p1").IsCompleted && b.Desk.Settled("p77") is { IsCompleted: true, Result: { Stage: "unknown", Ok: false, Words: "no such proposal" } },
            "desk settled: the app's result completes it; a settled line and an unknown id are complete at once");

        // A flood of different unlisted lines from one sender: a refusal never quotes the line, so it is one row and a count.
        var rowsBefore = b.Rows.Count;
        var queuedBefore = b.Desk.Queue.All().Count;
        for (var i = 0; i < 500; i++) b.Desk.Run(By(Parse("peer", "playmap", "OUTPUTS OFF " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), wire));
        Check(b.Rows.Count == rowsBefore && b.Desk.Queue.All().Count == queuedBefore,
            $"desk verbs: 500 different unlisted lines from one sender add no journal row past the first and queue nothing ({b.Rows.Count - rowsBefore} rows)");
        b.Now = b.Now + DeskService.RepeatWindow + TimeSpan.FromSeconds(1);
        b.Desk.Expire();
        var counted = b.Rows.Skip(rowsBefore).Where(r => r.Words.StartsWith("Refused to send to PlayMap Hall", StringComparison.Ordinal)).Select(r => r.Words).ToList();
        Check(counted is ["Refused to send to PlayMap Hall (wire): refused the same way 500 more times"],
            $"desk verbs: when its minute ends the flood is one row that says how many ({string.Join(" | ", counted)})");
    }

    static async Task Receipts(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var pm = NodeCredential.Key("kM");
        var wire = DeskOrigin.Wire();
        var time = new ShowWorkflowTests.ManualTime(DeskTests.T0);
        var wait = TimeSpan.FromSeconds(3);
        string Send(string line)
        {
            b.Desk.CollectApproved("pm1", pm);
            return b.Desk.Run(By(Parse("peer", "playmap", line), wire)).Proposal!.Id;
        }
        async Task Armed(int timers)
        {
            for (var i = 0; i < 200 && time.Timers < timers; i++) await Task.Delay(10);
        }

        // Its app answers within the deadline: applied in the app's words, and nothing follows.
        var first = Send("UNDO");
        var pending = DeskReceipts.Within("a1", b.Desk, first, wait, time, time.GetTimestamp());
        b.Desk.CollectApproved("pm1", pm);
        b.Desk.ProposalResult(first, """{"ok":true,"words":"cued Intro"}""", pm);
        var applied = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Check(applied is { Ok: true, Stage: "applied", Effect: "PlayMap Hall ran UNDO: cued Intro (p1)", Final: null, IsFinal: false }
              && applied.RefIds is { Count: 1 } && applied.RefIds[DeskReceipts.Ref] == "p1-k3m9"
              && Encoding.UTF8.GetString(applied.ToJson()).Contains("\"refs\":{\"sharedProposal\":\"p1-k3m9\"}", StringComparison.Ordinal),
            $"peer receipt: its app ran it within the deadline — applied, in its words, refs.sharedProposal a string ({applied.Effect})");

        // Its app says no: a refused receipt.
        var second = Send("PANIC");
        b.Desk.CollectApproved("pm1", pm);
        b.Desk.ProposalResult(second, """{"ok":false,"words":"Show Mode is on"}""", pm);
        var refused = await DeskReceipts.Within("a2", b.Desk, second, wait, time, time.GetTimestamp()).WaitAsync(TimeSpan.FromSeconds(5));
        Check(refused is { Ok: false, Stage: "applied", Effect: "", Error: "PlayMap Hall refused PANIC: Show Mode is on (p2)", Final: null } && refused.RefIds![DeskReceipts.Ref] == "p2-k3m9",
            $"peer receipt: its app refused it — not ok, in its words ({refused.Error})");

        // Not collected by the deadline: accepted, saying so; its follow-up is the app's result once it comes.
        var third = Send("STATUS");
        var sentAt = time.GetTimestamp();
        var slow = DeskReceipts.Within("a3", b.Desk, third, wait, time, sentAt);
        await Armed(1);
        time.Advance(wait);
        var accepted = await slow.WaitAsync(TimeSpan.FromSeconds(5));
        Check(accepted is { Ok: true, Stage: "accepted", Effect: "sent to PlayMap Hall as p3 — it has not collected it yet", IsFinal: false, Final: not null },
            $"peer receipt: not collected by the deadline — accepted, sent and not collected, with one follow-up to come ({accepted.Effect})");
        b.Desk.CollectApproved("pm1", pm);
        b.Desk.ProposalResult(third, """{"ok":true,"words":""}""", pm);
        var followUp = await accepted.Final!.WaitAsync(TimeSpan.FromSeconds(5));
        Check(followUp is { Ok: true, Stage: "applied", Effect: "PlayMap Hall ran STATUS (p3)", IsFinal: true, Final: null }
              && Encoding.UTF8.GetString(followUp.ToJson()).EndsWith(",\"final\":true}", StringComparison.Ordinal),
            $"peer receipt: the follow-up is the app's result, marked final ({followUp.Effect})");

        // Collected and never answered: the follow-up is unconfirmed — it may have run. Never collected: it has not run.
        var fourth = Send("UNDO");
        var fourthAt = time.GetTimestamp();
        var held = DeskReceipts.Within("a4", b.Desk, fourth, wait, time, fourthAt);
        b.Desk.CollectApproved("pm1", pm);
        var timers = time.Timers;
        await Armed(timers + 1);
        time.Advance(wait);
        var took = await held.WaitAsync(TimeSpan.FromSeconds(5));
        Check(took is { Ok: true, Stage: "accepted", Effect: "PlayMap Hall has p4 — waiting for its answer", Final: not null }, $"peer receipt: collected and not answered by the deadline — accepted, saying its app has it ({took.Effect})");
        timers = time.Timers;
        await Armed(timers + 1);
        time.Advance(DeskReceipts.FinalWithin - wait);
        var unconfirmed = await took.Final!.WaitAsync(TimeSpan.FromSeconds(5));
        Check(unconfirmed is { Ok: true, Stage: "accepted", IsFinal: true } && unconfirmed.Effect.StartsWith("unconfirmed — PlayMap Hall has p4", StringComparison.Ordinal)
              && unconfirmed.Effect.Contains("it may have run", StringComparison.Ordinal) && unconfirmed.RefIds![DeskReceipts.Ref] == "p4-k3m9",
            $"peer receipt: no result within receipt.final.ms of the send — the follow-up is unconfirmed, never applied ({unconfirmed.Effect})");
        var never = DeskReceipts.Unsettled("a5", b.Desk.ResultOf(Send("STATUS"))!);
        Check(never is { Ok: true, Stage: "accepted", IsFinal: true } && never.Effect.StartsWith("sent to PlayMap Hall as p5 — it has not collected it yet", StringComparison.Ordinal)
              && !never.Effect.Contains("may have run", StringComparison.Ordinal),
            $"peer receipt: a line never collected has not run, and its follow-up says so ({never.Effect})");
        Check(DeskReceipts.FinalWithin == TimeSpan.FromMilliseconds(PeerLink.ReceiptFinalMs) && DeskReceipts.Ref == "sharedProposal",
            "peer receipt: the follow-up's bound is §14's receipt.final.ms");
    }

    /// <summary>A surface that hosts the desk as the app and the rig do: a person's line runs through the service, its receipt waits for its app.</summary>
    sealed class DeskControl(DeskTests.Bench b) : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public RemoteConfig GetRemoteConfig() => new([], [], []);
        public volatile IReadOnlyList<ApiKey> Keys = [];
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
        public NodeBindings Nodes => b.Nodes;

        /// <summary>What a host says of each person's line where others read it (a status line, a log): <see cref="DeskOutcome.Shown"/>.</summary>
        public List<string> Shown { get; } = new();

        private DeskOutcome Desk(ShowCommand command)
        {
            var outcome = b.Desk.Run(command);
            lock (Shown) Shown.Add(outcome.Shown);
            return outcome;
        }

        public void Execute(ShowCommand command)
        {
            lock (Commands) Commands.Add(command);
            if (PlayoutGate.Person(command.Kind)) Desk(command);
        }

        public async Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
        {
            lock (Commands) Commands.Add(command);
            if (!PlayoutGate.Person(command.Kind)) return new CommandReceipt(id, true, "applied", "ran", null);
            var time = TimeProvider.System;
            var sentAt = time.GetTimestamp();
            var outcome = Desk(command);
            var ids = outcome.Proposal is { } p ? new Dictionary<string, string> { [DeskReceipts.Ref] = p.Id } : null;
            if (!outcome.Ok) return CommandReceipt.Refused(id, outcome.Words) with { RefIds = ids };
            if (command.Kind == ShowCommandKind.Peer) return await DeskReceipts.Within(id, b.Desk, outcome.Proposal!.Id, timeout, time, sentAt);
            return new CommandReceipt(id, true, "applied", outcome.Words, null, null, ids);
        }
    }

    static async Task Transports(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        b.Post("playmap", "pm1", "PlayMap Hall", NodeCredential.Key("kM"),
            [.. DeskTests.PlayMapVerbs, ("SET <output> name <name>", "low"), ("ADD media <json>", "low")]);
        var control = new DeskControl(b);
        string? pin = "4321";
        var thisPc = true;
        var port = FreeTcpPort();
        var rejected = new List<string>();
        await using var web = new WebServer(control, () => pin) { IsThisPc = a => thisPc && IPAddress.IsLoopback(a) };
        web.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        web.Start(port, bindAll: false);
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        var (mcpKey, mcpSecret) = ApiKeys.Create("Agent", ApiKeyScope.Mcp, DateTime.UtcNow);
        var (nodeKey, nodeSecret) = ApiKeys.Create("Board", ApiKeyScope.Node, DateTime.UtcNow);
        var (readKey, readSecret) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, DateTime.UtcNow);
        control.Keys = [ctlKey, mcpKey, nodeKey, readKey];

        // PlayMap as a show app: it asks for its approved lines, runs each once and reports — PANIC it refuses.
        var ran = new List<string>();
        using var stop = new CancellationTokenSource();
        var app = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var (_, json) = b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
                foreach (var p in JsonNode.Parse(json)!["proposals"]!.AsArray())
                {
                    var line = (string)p!["line"]!;
                    lock (ran) ran.Add(line);
                    b.Desk.ProposalResult((string)p["id"]!, JsonSerializer.Serialize(new { ok = line != "PANIC", words = line == "PANIC" ? "Show Mode is on" : "ok" }), NodeCredential.Key("kM"));
                }
                await Task.Delay(20);
            }
        });
        int Ran(string line) { lock (ran) return ran.Count(l => l == line); }
        // The desk journals on the server's threads and the app's: the rows are read under their lock.
        bool Row(Func<DeskRow, bool> has) { lock (b.Rows) return b.Rows.Any(has); }
        bool Rejected(Func<string, bool> has) { lock (rejected) return rejected.Any(has); }
        // What the host showed of each person's line, and what the door itself said of one it refused.
        bool Shown(Func<string, bool> has) { lock (control.Shown) return control.Shown.Any(has); }

        HttpRequestMessage Ask(HttpMethod method, string path, string? credential = null, string? requestId = null, string? body = null)
        {
            var m = new HttpRequestMessage(method, path);
            if (credential is not null) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            if (requestId is not null) m.Headers.Add("X-Request-Id", requestId);
            if (body is not null) m.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return m;
        }
        async Task<(HttpStatusCode Status, JsonNode Json)> Get(string path, string? credential = null, string? requestId = null)
        {
            var r = await http.SendAsync(Ask(HttpMethod.Get, path, credential, requestId));
            return (r.StatusCode, JsonNode.Parse(await r.Content.ReadAsStringAsync())!);
        }
        async Task<(HttpStatusCode Status, JsonNode Json)> PostPeer(string appWord, string line, string? credential, string? requestId = null)
        {
            var r = await http.SendAsync(Ask(HttpMethod.Post, "/api/peer", credential, requestId, JsonSerializer.Serialize(new { app = appWord, line })));
            return (r.StatusCode, JsonNode.Parse(await r.Content.ReadAsStringAsync())!);
        }
        static string Seg(string s) => Uri.EscapeDataString(s);

        try
        {
            // With the PIN: a receipt on HTTP, in the app's own words, the proposal named by its whole id.
            var (status, receipt) = await Get("/api/peer/playmap/UNDO", pin, "a1");
            Check(status == HttpStatusCode.OK && (string?)receipt["type"] == "receipt" && (string?)receipt["id"] == "a1" && (bool?)receipt["ok"] == true && (string?)receipt["stage"] == "applied"
                  && (string?)receipt["effect"] == "PlayMap Hall ran UNDO: ok (p1)" && (string?)receipt["refs"]!["sharedProposal"] == "p1-k3m9" && Ran("UNDO") == 1,
                $"peer http: with the PIN, the receipt is what PlayMap made of the line, and PlayMap ran it once ({receipt.ToJsonString()})");
            Check(b.Desk.Queue.Get("p1") is { DecidedBy: "wire", From.Kind: "main" } && Row(r => r.Words == "Sent p1 to PlayMap Hall (wire): UNDO" && r.Who == "PIN, 127.0.0.1"),
                "peer http: the line is decided by the role wire; who and from where are in the journal's via alone");
            var (bare, noId) = await Get("/api/peer/playmap/STATUS", pin);
            Check(bare == HttpStatusCode.OK && (string?)noId["type"] == "receipt" && (string?)noId["id"] == "peer" && (string?)noId["stage"] == "applied",
                "peer http: a request that names no id still gets the receipt, never a bare ok");
            var (refusedStatus, refusedBody) = await Get("/api/peer/playmap/PANIC", pin, "a2");
            Check(refusedStatus == HttpStatusCode.Conflict && (bool?)refusedBody["ok"] == false && (string?)refusedBody["error"] == "PlayMap Hall refused PANIC: Show Mode is on (p3)"
                  && (string?)refusedBody["refs"]!["sharedProposal"] == "p3-k3m9",
                $"peer http: a line its app refuses is 409 in the app's words ({refusedBody.ToJsonString()})");
            var before = b.Desk.Queue.All().Count;
            var (unlisted, unlistedBody) = await Get("/api/peer/playmap/" + Seg("OUTPUTS OFF"), pin, "a3");
            var (keyed, keyedBody) = await PostPeer("playmap", "LINK STAGEWATCH host swk_abcdefghijkmnpqrstuvwxyz234567", pin, "a4");
            var (unknown, unknownBody) = await Get("/api/peer/pm/UNDO", pin, "a5");
            // The arguments the wrong way round: the key where the app is named, by a GET segment and by the body.
            var (swapped, swappedBody) = await Get("/api/peer/" + Seg("LINK STAGEWATCH 10.0.0.5 swk_abcdefghijkmnpqrstuvwxyz234567") + "/playmap", pin, "a6");
            var (swappedPost, swappedPostBody) = await PostPeer("LINK STAGEWATCH 10.0.0.5 swk_abcdefghijkmnpqrstuvwxyz234567", "playmap", pin, "a7");
            Check(unlisted == HttpStatusCode.Conflict && (string?)unlistedBody["error"] == "PlayMap Hall does not list 'OUTPUTS OFF' — see its verbs"
                  && keyed == HttpStatusCode.Conflict && (string?)keyedBody["error"] == LineFence.Key
                  && unknown == HttpStatusCode.Conflict && ((string?)unknownBody["error"] ?? "").StartsWith("no app called 'pm'", StringComparison.Ordinal)
                  && swapped == HttpStatusCode.Conflict && (string?)swappedBody["error"] == LineFence.Key
                  && swappedPost == HttpStatusCode.Conflict && (string?)swappedPostBody["error"] == LineFence.Key
                  && b.Desk.Queue.All().Count == before,
                "peer http: an unlisted line, a key in a line or where the app is named, and a word that names no app are refused 409 in words, nothing queued");
            Check(!Row(r => r.Words.Contains("swk_", StringComparison.Ordinal) || r.Words.Contains("OUTPUTS OFF", StringComparison.Ordinal))
                  && Shown(w => w == "PlayMap Hall does not list that line — see its verbs") && Shown(w => w == LineFence.Key)
                  && !Shown(w => w.Contains("swk_", StringComparison.Ordinal) || w.Contains("OUTPUTS OFF", StringComparison.Ordinal))
                  && !Rejected(r => r.Contains("swk_", StringComparison.Ordinal)),
                "peer http: no journal row, nothing the host shows and nothing the door says holds the key, or quotes the unlisted line");
            var (badBody, badWords) = await ((Func<Task<(HttpStatusCode, JsonNode)>>)(async () =>
            {
                var r = await http.SendAsync(Ask(HttpMethod.Post, "/api/peer", pin, body: """{"app":"playmap","line":["UNDO"]}"""));
                return (r.StatusCode, JsonNode.Parse(await r.Content.ReadAsStringAsync())!);
            }))();
            Check(badBody == HttpStatusCode.BadRequest && (string?)badWords["error"] == WebServer.PeerBodyTakes, "peer http: POST /api/peer takes two strings — anything else is 400 in words");

            // Keys: a control key sends; a node, read or MCP key never does.
            var (byKey, _) = await Get("/api/peer/playmap/STATUS", ctlSecret, "k1");
            Check(byKey == HttpStatusCode.OK && Row(r => r.Who == "key 'Deck', 127.0.0.1"), "peer http: a control key sends, and the journal's via names the key, never its secret");
            foreach (var (name, secret) in new[] { ("node", nodeSecret), ("read", readSecret), ("mcp", mcpSecret) })
            {
                var count = b.Desk.Queue.All().Count;
                var (peerStatus, _) = await Get("/api/peer/playmap/UNDO", secret, "k2");
                var (deskStatus, _) = await Get("/api/desk/approve/next", secret, "k3");
                Check(peerStatus == HttpStatusCode.Forbidden && deskStatus == HttpStatusCode.Forbidden && b.Desk.Queue.All().Count == count,
                    $"peer http: a {name} key sends no line and decides nothing (403)");
            }

            // A line reaches its app byte for byte through each door: a GET segment, the POST body, a WebSocket's args.
            string[] lines = ["SET out1 name \"Left wall\"", "ADD media {\"path\": \"a b.mp4\"}", "LABEL a/b"];
            using (var ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control&pin={pin}"), CancellationToken.None);
                var n = 0;
                foreach (var line in lines)
                {
                    n++;
                    var (g, gBody) = await Get("/api/peer/playmap/" + Seg(line), pin, "g" + n);
                    var (p, pBody) = await PostPeer("PlayMap Hall", line, pin, "p" + n);
                    await Send(ws, JsonSerializer.Serialize(new { cmd = "peer", args = new[] { "pm1", line }, id = "w" + n }));
                    var w = await Next(ws, "\"type\":\"receipt\"");
                    Check(g == HttpStatusCode.OK && p == HttpStatusCode.OK && (string?)gBody["stage"] == "applied" && (string?)pBody["stage"] == "applied"
                          && w is not null && JsonNode.Parse(w) is { } wBody && (string?)wBody["id"] == "w" + n && (bool?)wBody["ok"] == true && (string?)wBody["stage"] == "applied"
                          && ((string?)wBody["refs"]!["sharedProposal"] ?? "").EndsWith("-k3m9", StringComparison.Ordinal) && Ran(line) == 3,
                        $"peer round trip: {line} reaches PlayMap exactly, by a GET segment, the POST body and a WebSocket's args ({Ran(line)} of 3)");
                }
                // desk on the WebSocket: an AI's proposal approved by next, with its receipt.
                Propose(b, "UNDO");
                var waiting = b.Desk.Queue.NextPending()!;
                await Send(ws, """{"cmd":"desk","args":["approve","next"],"id":"d1"}""");
                var d = await Next(ws, "\"id\":\"d1\"");
                Check(d is not null && JsonNode.Parse(d) is { } dBody && (bool?)dBody["ok"] == true && (string?)dBody["stage"] == "applied"
                      && ((string?)dBody["effect"] ?? "").StartsWith($"Approved {DeskService.Short(waiting.Id)} (AGMessage → PlayMap Hall: UNDO)", StringComparison.OrdinalIgnoreCase)
                      && (string?)dBody["refs"]!["sharedProposal"] == waiting.Id && b.Desk.Queue.Get(waiting.Id) is { DecidedBy: "wire" },
                    $"desk ws: desk approve next approves the waiting proposal, its receipt naming it ({d})");
            }

            // desk over HTTP: next never approves a high one; its id and the word do; a rejection carries its reason.
            Propose(b, "PANIC");
            var high = b.Desk.Queue.NextPending()!;
            var (nextStatus, nextBody) = await Get("/api/desk/approve/next", pin, "d2");
            Check(nextStatus == HttpStatusCode.Conflict && (string?)nextBody["error"] == $"{DeskService.Short(high.Id)} is high — approve it by its id: desk approve {high.Id} high"
                  && (string?)nextBody["refs"]!["sharedProposal"] == high.Id && b.Desk.Queue.Get(high.Id) is { State: SharedProposalState.Pending },
                "desk http: next refuses a high proposal, naming the line to send — and the receipt still names the proposal");
            var (highStatus, highBody) = await Get($"/api/desk/approve/{DeskService.Short(high.Id)}/high", pin, "d3");
            Check(highStatus == HttpStatusCode.OK && (string?)highBody["stage"] == "applied" && b.Desk.Queue.Get(high.Id) is { State: not SharedProposalState.Pending, DecidedBy: "wire" },
                "desk http: a high proposal is approved by its id and the word high");
            Propose(b, "STATUS");
            var third = b.Desk.Queue.NextPending()!;
            var (rejectStatus, rejectBody) = await Get($"/api/desk/reject/{third.Id}/" + Seg("not during the keynote"), pin);
            Check(rejectStatus == HttpStatusCode.OK && (string?)rejectBody["id"] == "desk" && b.Desk.Queue.Get(third.Id) is { State: SharedProposalState.Rejected }
                  && Row(r => r.Words.EndsWith("— not during the keynote", StringComparison.Ordinal)),
                "desk http: a rejection by id carries its reason to the journal, and is answered with a receipt");

            Propose(b, "STATUS");
            var fourth = b.Desk.Queue.NextPending()!;
            var (keyedReject, keyedRejectBody) = await Get($"/api/desk/reject/{fourth.Id}/" + Seg("because swk_abcdefghijkmnpqrstuvwxyz234567 said so"), pin, "d4");
            Check(keyedReject == HttpStatusCode.Conflict && (string?)keyedRejectBody["error"] == LineFence.Key && (string?)keyedRejectBody["refs"]!["sharedProposal"] == fourth.Id
                  && b.Desk.Queue.Get(fourth.Id) is { State: SharedProposalState.Pending }
                  && !Row(r => r.Words.Contains("swk_", StringComparison.Ordinal)) && !Shown(w => w.Contains("swk_", StringComparison.Ordinal)),
                "desk http: a rejection whose reason holds a key is refused 409, the proposal still waiting, the key in no row and nothing shown");
            var (plainReject, _) = await Get($"/api/desk/reject/{fourth.Id}", pin, "d5");
            Check(plainReject == HttpStatusCode.OK && b.Desk.Queue.Get(fourth.Id) is { State: SharedProposalState.Rejected }, "desk http: and rejected once its reason holds none");

            // MCP: an agent sends neither, with the PIN itself.
            async Task<JsonNode> Mcp(string line)
            {
                var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "run_command", arguments = new { command = line, reason = "test" } } });
                var r = await http.SendAsync(Ask(HttpMethod.Post, "/mcp", pin, body: body));
                return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!;
            }
            var count2 = b.Desk.Queue.All().Count;
            var agentPeer = await Mcp("peer playmap UNDO");
            var agentDesk = await Mcp("desk approve next");
            Check((bool?)agentPeer["isError"] == true && (string?)agentPeer["content"]![0]!["text"] == "Refused: " + DeskOrigin.PersonSends
                  && (bool?)agentDesk["isError"] == true && (string?)agentDesk["content"]![0]!["text"] == "Refused: " + DeskOrigin.PersonDecides && b.Desk.Queue.All().Count == count2,
                "person verbs mcp: run_command refuses peer and desk as errors, and queues nothing");
            // A mis-shaped line is answered the same: an agent is handed neither verb's grammar, and its line is never quoted back.
            var agentWords = await Mcp("peer playmap LINK swk_abcdefghijkmnpqrstuvwxyz234567");
            var agentBare = await Mcp("desk");
            var agentOther = await Mcp("nosuchverb now");
            Check((bool?)agentWords["isError"] == true && (string?)agentWords["content"]![0]!["text"] == "Refused: " + DeskOrigin.PersonSends
                  && (bool?)agentBare["isError"] == true && (string?)agentBare["content"]![0]!["text"] == "Refused: " + DeskOrigin.PersonDecides
                  && ((string?)agentOther["content"]![0]!["text"] ?? "").StartsWith("Rejected: ", StringComparison.Ordinal) && b.Desk.Queue.All().Count == count2,
                $"person verbs mcp: a mis-shaped peer or desk line is refused as a person's too, never with the verbs' grammar ({agentWords["content"]![0]!["text"]})");
            using (var helped = await http.SendAsync(Ask(HttpMethod.Get, "/api/help")))
                Check((await helped.Content.ReadAsStringAsync()).Contains("peer <app> <line>", StringComparison.Ordinal), "person verbs: /api/help, which people read, lists them");

            // No PIN set: only this PC or a listed address, on HTTP and on a control page's WebSocket.
            pin = null;
            thisPc = false;
            // PlayMap asks every 20 ms: what was approved for it above is run before the count is taken.
            for (var i = 0; i < 250 && b.Desk.Queue.All().Any(p => p.State == SharedProposalState.Approved); i++) await Task.Delay(20);
            var sentBefore = Ran("UNDO");
            var (open, openBody) = await Get("/api/peer/playmap/UNDO");
            var (openId, openReceipt) = await Get("/api/desk/approve/next", requestId: "n1");
            Check(open == HttpStatusCode.Forbidden && (bool?)openBody["ok"] == false && (string?)openBody["error"] == PlayoutGate.Words(ShowCommandKind.Peer, CommandDoor.Http)
                  && openId == HttpStatusCode.Forbidden && (string?)openReceipt["type"] == "receipt" && (string?)openReceipt["error"] == PlayoutGate.Words(ShowCommandKind.Desk, CommandDoor.Http),
                "person gate http: no PIN set, another machine's peer and desk are refused 403 in words, with an id as a refused receipt");
            using (var ws = new ClientWebSocket())
            {
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
                await Send(ws, """{"cmd":"peer","args":["playmap","UNDO"]}""");
                var error = await Next(ws, "\"type\":\"error\"");
                Check(error is not null && (string?)JsonNode.Parse(error)!["error"] == PlayoutGate.Words(ShowCommandKind.Peer, CommandDoor.WebSocket),
                    "person gate ws: no PIN set, another machine's control page is refused peer in words");
            }
            await Task.Delay(100);
            Check(Ran("UNDO") == sentBefore, $"person gate: nothing refused at the door reached PlayMap ({Ran("UNDO")} of {sentBefore})");
            control.From = [IPAddress.Loopback];
            var (listedStatus, _) = await Get("/api/peer/playmap/UNDO", requestId: "n2");
            Check(listedStatus == HttpStatusCode.OK && Row(r => r.Who == "listed address 127.0.0.1"), "person gate http: no PIN set, a listed address sends — and the journal says it was listed");
            control.From = [];
            thisPc = true;
            var (localStatus, _) = await Get("/api/peer/playmap/UNDO", requestId: "n3");
            Check(localStatus == HttpStatusCode.OK && Row(r => r.Who == "this PC, no PIN set") && Ran("UNDO") == sentBefore + 2,
                $"person gate http: no PIN set, this PC sends ({localStatus}, {Ran("UNDO")} of {sentBefore + 2})");
        }
        finally
        {
            await stop.CancelAsync();
            await app;
        }
    }

    static void Propose(DeskTests.Bench b, string line)
        => b.Desk.SubmitProposal(JsonSerializer.Serialize(new
        {
            from = new { t = "messenger", id = "ag1", name = "AGMessage" }, target = new { t = "playmap", id = "pm1", name = "PlayMap Hall" }, line,
        }), NodeCredential.Key("kA"));

    static Task Send(ClientWebSocket ws, string json) => ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>The next message holding <paramref name="marker"/>, frames and beats passed over; null when none comes.</summary>
    static async Task<string?> Next(ClientWebSocket ws, string marker)
    {
        var buf = new byte[65536];
        for (var i = 0; i < 60; i++)
        {
            using var cts = new CancellationTokenSource(5000);
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buf, cts.Token); }
            catch (OperationCanceledException) { return null; }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            var text = Encoding.UTF8.GetString(buf, 0, r.Count);
            if (text.Contains(marker, StringComparison.Ordinal)) return text;
        }
        return null;
    }

    static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }
}
