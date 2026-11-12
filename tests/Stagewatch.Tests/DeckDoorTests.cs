using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Show;

/// <summary>
/// The decks' doors (release B): Companion TCP and OSC take a person's lines — <c>peer</c> and <c>desk</c> — from this PC or a listed
/// address, named a deck's; a window grant from them is asked of the operator on the desk (Show Bus §12.4), never made — pending, its app
/// by its Show Bus id alone, accepted at once and finished by the decision; and Companion tells each connection what its address may do
/// (<c>ACCESS</c>), again whenever the operator's list changes. Fields first, words beside them.
/// </summary>
static class DeckDoorTests
{
    static readonly IPAddress Lan = IPAddress.Parse("10.0.0.40");

    public static async Task Run(Action<bool, string> Check)
    {
        Asked(Check);
        await AskedReceipt(Check);
        Approves(Check);
        PeerLine(Check);
        Access(Check);
        await AccessLine(Check);
    }

    static WindowArgs Grant(string window, string app) => new("grant", window, app);

    static readonly WindowChange Would = new(true, "", [], [], WindowEffect.Changed);

    /// <summary>A deck's grant waits on the desk for a person; nothing else asks, and nothing the windows would refuse is queued.</summary>
    static void Asked(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var deck = PlayoutGate.DeckSender(Line("window grant lobby pat1"), CommandDoor.Companion, IPAddress.Loopback)!;
        var tried = new List<WindowArgs>();
        var asked = b.Desk.AskGrant(Grant("lobby", "pat1"), deck, a => { tried.Add(a); return Would; });
        Check(asked is { Ok: true, Words: "asked as p1 — window grant lobby pat1 waits for the operator's approval at the desk" }
              && asked.Proposal is { State: SharedProposalState.Pending, TargetIsDesk: true, Risk: "high", Line: "window grant lobby pat1", DecidedBy: "" } p
              && p.From.Name == "Companion TCP" && p.From.Kind == "main" && p.Target.Kind == "main" && tried is [{ Who: "lobby", Argument: "pat1" }],
            $"deck ask: a grant from Companion TCP is tried on the windows, then waits on the desk — pending, high, aimed at this desk, from the door by name ({asked.Words})");
        Check(b.Rows.Any(r => r.Words == "Proposed p1: Companion TCP → this Stagewatch: window grant lobby pat1" && r.Who == "Companion TCP, 127.0.0.1")
              && b.Rows.All(r => !r.Words.Contains("127.0.0.1", StringComparison.Ordinal)),
            $"deck ask: the journal's row names the door and the line; the address is kept apart for the journal file alone ({b.Last} | {b.LastWho})");
        Check(b.Desk.Summary() is { Pending: 1, Next: { Id: "p1-k3m9", From: "Companion TCP", Target: "this Stagewatch", Risk: "high" } },
            "deck ask: the desk's readers (the frame's desk block, a deck's desk_next_*) show it waiting");
        var rows = b.Rows.Count;
        var again = b.Desk.AskGrant(Grant("lobby", "pat1"), deck, _ => Would);
        var louder = b.Desk.AskGrant(Grant("LOBBY", "pat1"), DeskOrigin.Deck("OSC, 10.0.0.40", "OSC"), _ => Would);
        Check(again is { Ok: true, Words: "p1 already asks for it — window grant lobby pat1 waits for the operator's approval at the desk", Proposal.Id: "p1-k3m9" }
              && louder is { Ok: true, Proposal.Id: "p1-k3m9" } && b.Desk.Queue.All().Count == 1 && b.Rows.Count == rows,
            $"deck ask: the same grant asked again while it waits — held down, pressed twice, from either deck — is that ask, nothing new queued or journalled ({again.Words})");

        // Approved by a person (its id and the word high): the line runs here as a proposal's, its app by its Show Bus id alone.
        ShowCommand? ran = null;
        b.RunHere = (cmd, _) => { ran = cmd; return new DeskRun(true, "lobby granted to Patterns FOH (pat1)"); };
        var decided = b.Desk.Decide("p1", approve: true, DeskOrigin.Operator, high: true);
        Check(decided.Ok && ran is { Kind: ShowCommandKind.Window } c && c.By == DeskOrigin.Proposal && c.Get<WindowArgs>() is { Action: "grant", Who: "lobby", Argument: "pat1" }
              && b.Desk.ResultOf("p1") is { Stage: "done", Ok: true },
            "deck ask: approved at the desk, the grant runs here as a proposal's line — its app found by id alone — and the proposal is done");

        // Refused, nothing queued: what the windows would refuse, an app named by its name, every window, and a sender that is no deck.
        var before = b.Desk.Queue.All().Count;
        Check(b.Desk.AskGrant(Grant("lobby", "pat1"), deck, _ => WindowChange.Refused([], "lobby is already granted to Patterns FOH (pat1)")) is { Ok: false, Words: "lobby is already granted to Patterns FOH (pat1)", Proposal: null },
            "deck ask: a grant the windows refuse now is refused in their words — the operator could only approve a refusal");
        Check(b.Desk.AskGrant(Grant("lobby", "Patterns FOH"), deck, _ => Would) is { Ok: false, Words: ProposalDesk.AsksGrantsOnly }
              && b.Desk.AskGrant(Grant("all", "pat1"), deck, _ => Would) is { Ok: false, Words: ProposalDesk.AsksGrantsOnly }
              && b.Desk.AskGrant(new WindowArgs("withdraw", "lobby", "pat1"), deck, _ => Would) is { Ok: false, Words: ProposalDesk.AsksGrantsOnly },
            "deck ask: only a grant of one window to an app named by its Show Bus id is asked");
        foreach (var notDeck in new[] { DeskOrigin.Operator, DeskOrigin.Wire("key 'FOH'"), DeskOrigin.Mcp, DeskOrigin.Proposal, DeskOrigin.Deck("Companion TCP, 127.0.0.1") })
            Check(b.Desk.AskGrant(Grant("lobby", "pat1"), notDeck, _ => Would) is { Ok: false, Words: DeskService.AsksFromDeckOnly },
                $"deck ask: {notDeck.Role}{(notDeck.Door.Length == 0 && notDeck.Role == "deck" ? " with no door" : "")} asks nothing — only a deck's door asks the operator");
        Check(b.Desk.Queue.All().Count == before, "deck ask: no refusal queued anything");
        Check(b.Desk.Queue.ProposeAsked("", "window grant lobby pat1", "", "high", DeskTests.T0).Error is { } noDoor && noDoor.Contains("door", StringComparison.Ordinal)
              && b.Desk.Queue.ProposeAsked("OSC", "window grant lobby pat1\u0007", "", "high", DeskTests.T0).Error is { Length: > 0 }
              && b.Desk.Queue.ProposeAsked("OSC", "next", "", "low", DeskTests.T0).Error == ProposalDesk.AsksGrantsOnly,
            "deck ask: the queue itself takes only a grant, from a door it names, within §11's line rules");
        Check(ScreenWindows.GrantLine(Grant(" lobby ", " pm-1a2b ")) == "window grant lobby pm-1a2b" && ScreenWindows.GrantLine(Grant("Lobby TV", "pat1")) is null
              && ScreenWindows.GrantLine(Grant("lobby", "a b")) is null,
            "deck ask: a grant is written as one line of bare words, or not at all");
    }

    /// <summary>A deck's grant is answered at once, waiting for the operator; its one follow-up is the decision.</summary>
    static async Task AskedReceipt(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var time = new ShowWorkflowTests.ManualTime(DeskTests.T0);
        var deck = DeskOrigin.Deck("OSC, 10.0.0.40", "OSC");
        var p = b.Desk.AskGrant(Grant("lobby", "pat1"), deck, _ => Would).Proposal!;
        var first = await DeskReceipts.Within("g1", b.Desk, p.Id, TimeSpan.FromSeconds(3), time, time.GetTimestamp()).WaitAsync(TimeSpan.FromSeconds(5));
        Check(first is { Ok: true, Stage: "accepted", Effect: "p1 waits for a person's approval", IsFinal: false, Final: not null } && first.RefIds![DeskReceipts.Ref] == p.Id,
            $"deck receipt: answered at once — accepted, waiting for a person, refs.sharedProposal the proposal ({first.Effect})");
        b.Desk.Decide("p1", approve: false, DeskOrigin.Operator, why: "not this show");
        var final = await first.Final!.WaitAsync(TimeSpan.FromSeconds(5));
        Check(final is { Ok: false, Stage: "applied", Error: "p1 was rejected", IsFinal: true },
            $"deck receipt: its follow-up is the operator's decision, marked final ({final.Error})");

        var q = b.Desk.AskGrant(Grant("stage", "pm1"), deck, _ => Would).Proposal!;
        var at = time.GetTimestamp();
        var waiting = await DeskReceipts.Within("g2", b.Desk, q.Id, TimeSpan.FromSeconds(3), time, at).WaitAsync(TimeSpan.FromSeconds(5));
        for (var i = 0; i < 200 && time.Timers < 1; i++) await Task.Delay(10);
        time.Advance(DeskReceipts.FinalWithin);
        var still = await waiting.Final!.WaitAsync(TimeSpan.FromSeconds(5));
        Check(still is { Ok: true, Stage: "accepted", IsFinal: true } && still.Effect.StartsWith("p2 waits for a person's approval (30 s on); it runs only once a person approves it", StringComparison.Ordinal),
            $"deck receipt: undecided within receipt.final.ms, the follow-up says it still waits and where its outcome will be ({still.Effect})");

        // Approved by a person: the follow-up is what the grant did here, applied and final.
        b.RunHere = (_, _) => new DeskRun(true, "lobby granted to Patterns FOH (pat1)");
        var r = b.Desk.AskGrant(Grant("lobby", "pat1"), deck, _ => Would).Proposal!;
        var asked = await DeskReceipts.Within("g3", b.Desk, r.Id, TimeSpan.FromSeconds(3), time, time.GetTimestamp()).WaitAsync(TimeSpan.FromSeconds(5));
        var approved = b.Desk.Decide(DeskService.Short(r.Id), approve: true, DeskOrigin.Operator, high: true);
        var done = await asked.Final!.WaitAsync(TimeSpan.FromSeconds(5));
        Check(approved.Ok && done is { Ok: true, Stage: "applied", IsFinal: true, Error: null }
              && done.Effect == "this Stagewatch ran window grant lobby pat1: lobby granted to Patterns FOH (pat1) (p3)" && done.RefIds![DeskReceipts.Ref] == r.Id,
            $"deck receipt: approved at the desk, the follow-up is the grant's run here, applied and final ({done.Effect})");
    }

    /// <summary>
    /// A window grant aimed at this desk is approved only by a person who proved it (Show Bus §12.4): never by a deck — its own ask or an
    /// app's self-ask — nor a wire request on its address alone; the proposal still waits, a journal row says why, and a deck may reject.
    /// </summary>
    static void Approves(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var companion = PlayoutGate.DeckSender(Line("desk approve p1 high"), CommandDoor.Companion, IPAddress.Loopback)!;
        var osc = PlayoutGate.DeckSender(Line("desk approve p1 high"), CommandDoor.Osc, Lan)!;
        var bare = DeskOrigin.Wire("this PC, no PIN set");
        Check(DeskOrigin.Operator.Proved && DeskOrigin.Wire("PIN, 10.0.0.7", proved: true).Proved && !bare.Proved && !companion.Proved && !osc.Proved
              && !DeskOrigin.Deck("Stream Deck").Proved && !DeskOrigin.Trigger().Proved,
            "deck approve: the operator and a wire request with the PIN or a key prove a person; a deck and an address alone do not");
        var asked = b.Desk.AskGrant(Grant("lobby", "pat1"), companion, _ => Would).Proposal!;
        b.Desk.SubmitProposal(DeskTests.Proposal("patterns", "pat1", "Patterns FOH", "main", "", "", "window grant stage pat1"), NodeCredential.Key("kP"));
        var self = b.Desk.Queue.All().Single(p => p.Line == "window grant stage pat1");
        var ran = 0;
        b.RunHere = (_, _) => { ran++; return new DeskRun(true, "granted"); };
        foreach (var by in new[] { companion, osc, bare })
            foreach (var p in new[] { asked, self })
                Check(b.Desk.Decide(p.Id, approve: true, by, high: true) is { Ok: false, Words: DeskService.GrantTakesProof } refused
                      && refused.Proposal is { State: SharedProposalState.Pending } && b.Desk.Queue.Get(p.Id) is { State: SharedProposalState.Pending },
                    $"deck approve: {by.Role} ({by.Who}) approves no window grant — {(p == asked ? "a deck's own ask" : "an app's self-ask")} still waits");
        Check(b.Desk.Decide("next", approve: true, companion, high: true) is { Ok: false, Words: DeskService.GrantTakesProof } && ran == 0,
            "deck approve: nor as next, high or not — nothing ran");
        Check(b.Rows.Any(r => r.Words == $"Not approved p1 (deck): Companion TCP → this Stagewatch: window grant lobby pat1 — {DeskService.GrantTakesProof}" && r.Who == "Companion TCP, 127.0.0.1")
              && b.Rows.Count(r => r.Words.StartsWith("Not approved p1 (deck)", StringComparison.Ordinal) && r.Who == "Companion TCP, 127.0.0.1") == 1,
            $"deck approve: the refusal is one journal row per deck naming it, its address kept apart, its repeats (next, the same refusal) counted ({string.Join(" | ", b.Rows.Select(r => r.Words))})");
        Check(b.Desk.Decide(self.Id, approve: false, osc) is { Ok: true } && b.Desk.Queue.Get(self.Id) is { State: SharedProposalState.Rejected, DecidedBy: "deck" },
            "deck approve: a deck may still reject a grant");
        Check(b.Desk.Decide(asked.Id, approve: true, DeskOrigin.Wire("PIN, 10.0.0.7", proved: true), high: true) is { Ok: true } && ran == 1
              && b.Desk.Queue.Get(asked.Id) is { State: SharedProposalState.Done, DecidedBy: "wire" },
            "deck approve: a person with the PIN approves the deck's ask, and the grant runs here once");
    }

    /// <summary>A deck's peer line is a person's, born approved; its line is fenced as every person's is.</summary>
    static void PeerLine(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        var tokens = Stagewatch.Companion.CompanionProtocol.Tokenize("PEER playmap \"UNDO\\nPANIC\"");
        Check(tokens is [_, _, "UNDO\nPANIC"] && !ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out _, out var why) && why == LineFence.Control,
            "deck peer: a line break Companion's quoting carries into the line is refused as the line is read, nothing sent");
        var undo = Line("peer playmap UNDO");
        var sent = b.Desk.Run(undo with { By = PlayoutGate.DeckSender(undo, CommandDoor.Osc, Lan) });
        Check(sent is { Ok: true } && sent.Proposal is { DecidedBy: "deck", State: SharedProposalState.Approved, Line: "UNDO" }
              && b.Rows.Any(r => r.Words.StartsWith("Sent p1 to PlayMap Hall (deck)", StringComparison.Ordinal) && r.Who == "OSC, 10.0.0.40"),
            $"deck peer: from a deck, born approved and decided by the role word deck; its door and address only in the journal's who ({b.Last})");
    }

    /// <summary>What a deck at an address may do, as Companion's ACCESS line says it.</summary>
    static void Access(Action<bool, string> Check)
    {
        JsonNode? Of(bool thisPc, IReadOnlyList<IPAddress> from) => JsonNode.Parse(PlayoutGate.AccessJson(CommandDoor.Companion, new CommandSender(thisPc ? IPAddress.Loopback : Lan, thisPc, false), from));
        Check(Of(true, []) is { } here && (bool?)here["peer"] == true && (bool?)here["desk"] == true && (string?)here["why"] == "" && (bool?)here["grant"] == true,
            "access: this PC may send peer and desk lines and ask for grants");
        Check(Of(false, [Lan]) is { } listed && (bool?)listed["peer"] == true && (bool?)listed["grant"] == true, "access: so may a listed address");
        Check(Of(false, []) is { } other && (bool?)other["peer"] == false && (bool?)other["desk"] == false && (bool?)other["grant"] == false
              && (string?)other["why"] == PlayoutGate.AccessWords(CommandDoor.Companion) && PlayoutGate.AccessWords(CommandDoor.Companion).Contains(PlayoutGate.SettingWords, StringComparison.Ordinal),
            "access: an unlisted machine may do none of it, in words that name the setting");
        Check(PlayoutGate.AccessJson(CommandDoor.Companion, new CommandSender(Lan, false, false), []).StartsWith("{\"peer\":false,\"desk\":false,\"why\":", StringComparison.Ordinal),
            "access: its fields in the design's order, grant appended last");
    }

    /// <summary>Companion tells each JSON subscriber what it may do as it subscribes, and again when the operator's list changes.</summary>
    static async Task AccessLine(Action<bool, string> Check)
    {
        var bridge = new FakeBridge { Allowed = true };
        var port = FreePort();
        await using var server = new Stagewatch.Companion.CompanionServer(bridge) { IsThisPc = _ => false };
        server.Start(port, bindAllInterfaces: false);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        await stream.WriteAsync("SUBSCRIBE JSON\n"u8.ToArray());
        var first = await Read(reader, "ACCESS ");
        Check(first is not null && JsonNode.Parse(first[7..]) is { } a && (bool?)a["peer"] == false && (bool?)a["grant"] == false
              && (string?)a["why"] == PlayoutGate.AccessWords(CommandDoor.Companion),
            $"access line: a deck on another machine, unlisted, hears so once it subscribes ({first})");
        bridge.From = [IPAddress.Loopback];
        bridge.Snapshots.Publish(new ShowSnapshot { Text = "01:00", Seconds = 60 });
        var second = await Read(reader, "ACCESS ");
        Check(second is not null && JsonNode.Parse(second[7..]) is { } b && (bool?)b["peer"] == true && (bool?)b["desk"] == true && (bool?)b["grant"] == true && (string?)b["why"] == "",
            $"access line: listed, it hears again with the next frame that it may ({second})");
        bridge.Snapshots.Publish(new ShowSnapshot { Text = "00:59", Seconds = 59 });
        var none = await Read(reader, "ACCESS ", lines: 6);
        Check(none is null, "access line: nothing changed, nothing said again");
        await stream.WriteAsync(Encoding.UTF8.GetBytes("@n1 PEER playmap \"UNDO\\nPANIC\"\n"));
        var broken = await Read(reader, "ERROR @n1 ");
        bool anyPeer;
        lock (bridge.Commands) anyPeer = bridge.Commands.Any(c => c.Kind == ShowCommandKind.Peer);
        Check(broken == "ERROR @n1 " + LineFence.Control && !anyPeer, $"access line: a peer line holding a line break is refused at Companion's door, listed or not ({broken})");
    }

    static ShowCommand Line(string line) => ShowCommandParser.TryParseLine(line, out var cmd, out var error) ? cmd : throw new InvalidOperationException(error);

    static async Task<string?> Read(StreamReader r, string prefix, int lines = 20)
    {
        for (var i = 0; i < lines; i++)
        {
            using var cts = new CancellationTokenSource(1500);
            string? line;
            try { line = await r.ReadLineAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }
            if (line is null) return null;
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
        }
        return null;
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }
}
