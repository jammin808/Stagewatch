using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

/// <summary>
/// Running-order cues and timed GO (SW20.7): the timing of a GO asked together, pure (an AT only on a clock fit for it and measured
/// over TLS or a local link, members held half their round trip before the instant, at the instant itself by this PC's clock alone
/// when measured over a plain link off this PC, nothing together while a member up has no clock, a member down taking no timing
/// from the others), one GO per sync group, the late-start words, a receipt that says "scheduled" only from the node's own answer,
/// what a peer lists (INDEX, CUE LIST) and how a cue is held to it, the copilot's facts; then the planner over real links — standby
/// off by default, a CUE only on an idle playlist and only at the end of a turn the item did not start in, a Patterns cue's CUE GO
/// alone, timed and held GOs on a manual clock, followers left to a leader whose GO went (and said when it did not), a held GO
/// never sent after a drop, a stopped link or a lapsed licence, late starts, a refused timed GO never called scheduled, and a peer
/// answering as another node feeding nothing of its list. Fields first, rows second.
/// </summary>
static class PlayoutCueTests
{
    const string Token = PlayoutDirectorTests.Token;
    const int Lead = 300;

    public static async Task Run(Action<bool, string> Check)
    {
        Timing(Check);
        Leaders(Check);
        Late(Check);
        Lists(Check);
        AiFacts(Check);
        await Receipts(Check);
        await LinkLists(Check);
        await Planner(Check);
        await DeskPresses(Check);
        await TogetherOverLinks(Check);
        await SyncGroups(Check);
        await LateStart(Check);
        await WrongNode(Check);
    }

    // ------------------------------------------------------------------ helpers

    static string Ok(string tag, string words = "") => "OK" + (tag.Length > 0 ? " " + tag : "") + (words.Length > 0 ? " " + words : "");

    static string Err(string tag, string words) => "ERR" + (tag.Length > 0 ? " " + tag : "") + " " + words;

    static string Ms(long ms) => ms.ToString(CultureInfo.InvariantCulture);

    static PlayoutCue Cue(string link, string list, string item, bool standby = false, bool together = false, string label = "")
        => new() { Link = link, List = list, Item = item, Standby = standby, Together = together, Label = label };

    static RundownItem Item(string id, params PlayoutCue[] cues) => new() { Id = id, Title = "Item " + id, DurationSeconds = 60, Playout = [.. cues] };

    static PlayoutLinkSettings L(string name, string kind, int port) => new() { Name = name, Kind = kind, Port = port, Enabled = true };

    /// <summary>The commands a fake desk heard (lines with a command's request id), without their ids.</summary>
    static List<string> Commands(PlayoutLinkTests.FakeDesk desk)
        => desk.Heard.Where(l => l.StartsWith("@s", StringComparison.Ordinal) && l.Length > 2 && char.IsAsciiDigit(l[2]) && l.IndexOf(' ') > 0)
            .Select(l => l[(l.IndexOf(' ') + 1)..]).ToList();

    static string PmState(string state, string id = "pm5e6f7a", string group = "", string role = "solo", string leader = "", string index = "sig1", long? at = null, long position = 0, string item = "it1")
        => "{\"v\":1,\"seq\":4,\"id\":\"" + id + "\",\"t\":\"playmap\"" + (at is { } a ? ",\"at\":" + Ms(a) : "")
           + ",\"video\":{\"file\":\"intro.mov\",\"role\":\"playlist\",\"positionMs\":1000,\"lengthMs\":90000,\"playing\":true,\"seq\":2}"
           + ",\"playlists\":[{\"id\":\"pl1\",\"name\":\"Main show\",\"state\":\"" + state + "\",\"index\":0,\"item\":{\"id\":\"" + item + "\",\"label\":\"Intro\"},\"positionMs\":" + Ms(position)
           + ",\"syncGroup\":\"" + group + "\",\"seq\":2,\"next\":{\"id\":\"it2\",\"label\":\"Walk-in\"}}]"
           + ",\"sync\":{\"role\":\"" + role + "\",\"leader\":\"" + leader + "\"},\"project\":{\"index\":\"" + index + "\"}}";

    static string IndexJson(string sig = "sig1", string extra = "")
        => "{\"v\":1,\"revision\":9,\"sig\":\"" + sig + "\",\"playlists\":[{\"id\":\"pl1\",\"name\":\"Main show\",\"loop\":false,\"syncGroup\":null,\"items\":["
           + "{\"id\":\"it1\",\"label\":\"Intro\",\"kind\":\"generator\"},{\"id\":\"it2\",\"label\":\"Walk-in\",\"kind\":\"color\"}" + extra + "]}],\"surfaces\":[]}";

    const string PatState = """{"video":null,"cuestack":{"armed":true,"hold":false,"listRev":7,"standby":{"id":"c1","number":"12","name":"Walk-in","requireConfirm":false}}}""";

    const string CueList = """{"name":"Main","listRev":7,"cues":[{"id":"c1","number":"12","name":"Walk-in","enabled":true},{"id":"c2","number":"13","name":"Dark","enabled":false}]}""";

    const string PmContext = """{"v":1,"node":{"t":"playmap","id":"pm5e6f7a","name":"Stage left"},"light":"green","headline":"ok","items":[],"commands":[]}""";

    const string PatContext = """{"v":1,"node":{"t":"patterns","id":"9f8e7d6c","name":"Patterns FOH"},"light":"green","headline":"ok","items":[],"commands":[]}""";

    const string Whoami = """{"paired":true,"role":"remote","armed":false,"may":"*"}""";

    /// <summary>
    /// A fake PlayMap node: its STATE as <paramref name="state"/> says it now, its INDEX, and OK to each playlist command — or
    /// <paramref name="answer"/>'s lines where it gives some, and no answer at all where it gives "".
    /// </summary>
    static void PlayMap(PlayoutLinkTests.FakeDesk desk, Func<string> state, Func<string> index, long offsetMs = 0, Func<string, string?>? answer = null, string context = PmContext)
        => PlayoutDirectorTests.Peer(desk, state, context, Whoami, body => body == "INDEX" ? Ok("@@", index())
            : answer?.Invoke(body) is { } a ? (a.Length > 0 ? a : null) : body.StartsWith("PLAYLIST ", StringComparison.Ordinal) ? Ok("@@") : null, offsetMs);

    /// <summary>A fake Patterns desk: its cue stack, its CUE LIST (or a refusal of it), and OK to each cue command.</summary>
    static void Patterns(PlayoutLinkTests.FakeDesk desk, bool lists = true)
        => PlayoutDirectorTests.Peer(desk, () => PatState, PatContext, null, body => body == "CUE LIST" ? (lists ? Ok("@@", CueList) : Err("@@", "unknown command 'CUE LIST'"))
            : body.StartsWith("CUE ", StringComparison.Ordinal) ? Ok("@@") : null);

    // ------------------------------------------------------------------ timing, pure

    static void Timing(Action<bool, string> Check)
    {
        const long now = 1_761_000_000_000;
        const long t = now + Lead;
        static string W(long ms) => Ms(ms);
        PeerClock.Belief B(double offset, long rtt) => new(offset, rtt, PeerClock.ErrMs(rtt), now - 500);
        var fit = new PlayoutClockFacts("PlayMap Stage left", PlayoutRules.PlayMap, B(5000.4, 4), Fit: true, MayTime: true);
        var local = new PlayoutClockFacts("Patterns", PlayoutRules.Patterns, B(-20, 6), Fit: false, MayTime: true);
        var plain = new PlayoutClockFacts("Patterns FOH", PlayoutRules.Patterns, B(3, 8), Fit: false, MayTime: false);
        var unfit = new PlayoutClockFacts("PlayMap Stage right", PlayoutRules.PlayMap, B(1, 9), Fit: false, MayTime: true);
        var pmPlain = new PlayoutClockFacts("PlayMap Booth", PlayoutRules.PlayMap, B(1, 10), Fit: false, MayTime: false);
        var none = new PlayoutClockFacts("PlayMap Gallery", PlayoutRules.PlayMap, null, Fit: false, MayTime: true);

        var set = PlayoutDirector.Together([fit, local, plain, unfit, pmPlain], now, Lead, W);
        Check(set[0] is { When: PlayoutWhen.At, InstantMs: t, AtPeerMs: t + 5000, RttMs: 4 } && set[0].Words == $"sent with AT for {W(t)} (± 3 ms)"
              && set[0].Taken == $"scheduled for {W(t)} (± 3 ms)" && set.Skip(1).All(m => m.Taken.Length == 0),
              $"playout timing: a PlayMap node whose clock is fit gets AT at the instant on its own clock, now — sent with it as it goes, scheduled only once taken ({set[0].Words})");
        Check(set[1] is { When: PlayoutWhen.Hold, SendAtMs: t - 3, InstantMs: t } && set[1].Words == $"held here, to be sent at {W(t - 3)}, half its round trip (6 ms) before {W(t)}: Patterns takes no AT",
              $"playout timing: a Patterns desk on a channel that may time is held here half its round trip before the instant ({set[1].Words})");
        Check(set[2] is { When: PlayoutWhen.Hold, SendAtMs: t } && set[2].Words.StartsWith($"held here, to be sent at {W(t)} by this PC's clock alone: Patterns FOH takes no AT", StringComparison.Ordinal)
              && set[2].Words.Contains("plain TCP off this PC", StringComparison.Ordinal) && set[2].Words.Contains("13.11.6", StringComparison.Ordinal)
              && set[2].Words.Contains("Round 97", StringComparison.Ordinal),
              $"playout timing: a clock measured over plain TCP off this PC schedules nothing, its round trip included — held to the instant by this PC's clock, and the row says why and until when ({set[2].Words})");
        Check(set[3] is { When: PlayoutWhen.Hold, SendAtMs: t - 5 }
              && set[3].Words.EndsWith($"PlayMap Stage right's clock is not fit for a GO 300 ms ahead (a sample at most 10 s old, off by at most {Ms(PlayoutDirector.ToleranceMs(Lead))} ms)", StringComparison.Ordinal),
              $"playout timing: a PlayMap node whose clock is not fit is held, not given an AT, and the row says what fit is ({set[3].Words})");
        Check(set[4] is { When: PlayoutWhen.Hold, SendAtMs: t } && set[4].Words.Contains("measured over plain TCP off this PC and schedules nothing", StringComparison.Ordinal),
              $"playout timing: a PlayMap node reached over plain TCP off this PC gets no AT and no round-trip hold ({set[4].Words})");

        var missing = PlayoutDirector.Together([fit, local, none], now, Lead, W);
        Check(missing.All(m => m.When == PlayoutWhen.Now && m.Words == "not together: PlayMap Gallery has no clock yet — every GO sent at once, untimed"),
              "playout timing: a member with no clock at all makes nothing together — every GO at once, untimed, its clock named");
        Check(PlayoutDirector.Together([fit], now, Lead, W) is [{ When: PlayoutWhen.At, AtPeerMs: t + 5000 }]
              && PlayoutDirector.Together([unfit], now, Lead, W) is [{ When: PlayoutWhen.Now } u] && u.Words.StartsWith("not timed: PlayMap Stage right's clock is not fit", StringComparison.Ordinal) && u.Words.EndsWith(" — sent at once", StringComparison.Ordinal)
              && PlayoutDirector.Together([local], now, Lead, W) is [{ When: PlayoutWhen.Now, Words: "not timed: Patterns takes no AT — sent at once" }]
              && PlayoutDirector.Together([none], now, Lead, W) is [{ When: PlayoutWhen.Now, Words: "not timed: PlayMap Gallery has no clock yet — sent at once" }]
              && PlayoutDirector.Together([], now, Lead, W).Count == 0,
              "playout timing: a lone GO is never held — at its instant on a fit clock, else at once, said either way");
        // A link that is not up and paired is sent nothing: its missing clock takes no timing from the others, and its words say why.
        var down = new PlayoutClockFacts("PlayMap Gallery", PlayoutRules.PlayMap, null, Fit: false, MayTime: true, Up: false);
        var withDown = PlayoutDirector.Together([fit, local, down], now, Lead, W);
        Check(withDown[0] is { When: PlayoutWhen.At } && withDown[1] is { When: PlayoutWhen.Hold, SendAtMs: t - 3 }
              && withDown[2] is { When: PlayoutWhen.Now, Words: "not timed: the link to PlayMap Gallery wasn't up as the start was timed — sent at once" },
              "playout timing: a member whose link is down takes no timing from the others — they stay together, it goes at once and its link refuses it");
        Check(PlayoutDirector.Together([local, down], now, Lead, W) is [{ When: PlayoutWhen.Now, Words: "not timed: Patterns takes no AT — sent at once" }, { When: PlayoutWhen.Now }]
              && PlayoutDirector.Together([fit, down, down with { Peer = "PlayMap Booth" }], now, Lead, W) is [{ When: PlayoutWhen.At }, { When: PlayoutWhen.Now }, { When: PlayoutWhen.Now }],
              "playout timing: the one member up is a lone GO — never held, timed where its clock is fit");
        var behind = new PlayoutClockFacts("PlayMap Stage left", PlayoutRules.PlayMap, B(-20.6, 2), Fit: true, MayTime: true);
        Check(PlayoutDirector.Together([behind], now, Lead, W) is [{ AtPeerMs: t - 21 }], "playout timing: a node behind this PC gets an AT behind it, its offset rounded");

        // 13.11.4: fit for a GO a lead ahead is a sample at most 10 s old whose error a round trip as long as the lead gives.
        var tol = PlayoutDirector.ToleranceMs(Lead);
        Check(tol == PeerClock.ErrMs(Lead) && tol == 151 && PeerClock.IsFitFor(0, PeerClock.ErrMs(300), tol) && !PeerClock.IsFitFor(0, PeerClock.ErrMs(302), tol)
              && PeerClock.IsFitFor(PeerLink.ClockFreshMs, 3, tol) && !PeerClock.IsFitFor(PeerLink.ClockFreshMs + 1, 3, tol),
              "playout timing: fit for a GO 300 ms ahead is a round trip of at most the lead and a sample at most 10 s old");
    }

    static void Leaders(Action<bool, string> Check)
    {
        Check(PlayoutDirector.Leaders([("main", "follower"), ("main", "leader"), ("", ""), ("other", "follower"), ("main", "solo"), ("main", "leader")]) is [1, 1, 2, 3, 4, 5],
              "playout sync: a follower's GO is its group's leader's among them; a follower whose leader is not among them stands alone (its plan refuses it)");
        Check(PlayoutDirector.Leaders([]).Count == 0, "playout sync: no members, none moved");
    }

    static void Late(Action<bool, string> Check)
    {
        var s = new PlayoutPeerState
        {
            Whole = true, Kind = PlayoutRules.PlayMap, At = 10_000, HasPlaylists = true,
            Playlists = [new PlayoutPlaylist("pl1", "Main show", "playing", "it1", "Intro", 2_000, "", "", "", 1)],
        };
        Check(PlayoutDirector.Late(s, "pl1", 7_860, 10) == "Main show started late by 140 ms (AT arrived after its instant)"
              && PlayoutDirector.Late(s, "Main show", 7_860, 10) is not null,
              "playout late: at − positionMs past the AT by more than half the round trip and 20 ms is a late start, in words");
        Check(PlayoutDirector.Late(s, "pl1", 7_975, 10) is null && PlayoutDirector.Late(s, "pl1", 7_974, 10) is not null && PlayoutDirector.Late(s, "pl1", 8_100, 10) is null,
              "playout late: within half the round trip and 20 ms (or early) it is on time — the AT's own instant");
        Check(PlayoutDirector.Late(s with { At = null }, "pl1", 7_000, 10) is null && PlayoutDirector.Late(s, "pl9", 7_000, 10) is null
              && PlayoutDirector.Late(s with { Playlists = [s.Playlists[0] with { PositionMs = null }] }, "pl1", 7_000, 10) is null,
              "playout late: a STATE with no at, no such playlist or no position says nothing");
    }

    static void Lists(Action<bool, string> Check)
    {
        var index = PlayoutIndex.ReadIndex(IndexJson("9f1c2ab07d3e4410"));
        Check(index is { Kind: PlayoutRules.PlayMap, Revision: "9f1c2ab07d3e4410", Cut: false, Cues.Count: 0 } i && i.Playlists is [{ Id: "pl1", Name: "Main show", SyncGroup: "", Items: [{ Id: "it1", Label: "Intro" }, { Id: "it2" }] }],
              "playout lists: a PlayMap INDEX is read for its playlists and items, its sig the revision its STATE's project.index repeats");
        var cues = PlayoutIndex.ReadCues(CueList);
        Check(cues is { Kind: PlayoutRules.Patterns, Revision: "7", Playlists.Count: 0 } c && c.Cues is [{ Id: "c1", Number: "12", Name: "Walk-in", Enabled: true }, { Id: "c2", Enabled: false }],
              "playout lists: a Patterns CUE LIST is read for its cues, its listRev (a number) the revision");
        Check(index!.Lacks(Cue("stage-left", "pl1", "it2"), "PlayMap Stage left") is null && index.Lacks(Cue("stage-left", "Main show", "#2"), "P") is null
              && index.Lacks(Cue("stage-left", "pl1", "it9"), "PlayMap Stage left") == "PlayMap Stage left's Main show lists no item it9"
              && index.Lacks(Cue("stage-left", "pl9", "it1"), "PlayMap Stage left") == "PlayMap Stage left lists no playlist pl9"
              && index.Lacks(Cue("stage-left", "pl1", "#3"), "PlayMap Stage left") == "PlayMap Stage left's Main show has no item #3"
              && index.Lacks(Cue("stage-left", "", "c1"), "P") is null,
              "playout lists: a cue naming what the node lists passes; a missing playlist or item is said, a cue of the other shape left to the plan");
        Check(cues!.Lacks(Cue("patterns", "", "c1"), "Patterns") is null && cues.Lacks(Cue("patterns", "", "c9"), "Patterns") == "Patterns lists no cue c9"
              && cues.Lacks(Cue("patterns", "", "c2"), "Patterns") == "Patterns's cue 13 Dark is switched off",
              "playout lists: a Patterns cue the desk lists passes; one it lacks, or switched off, is said");

        var many = string.Join(",", Enumerable.Range(0, PlayoutIndex.MaxItems + 5).Select(n => "{\"id\":\"x" + Ms(n) + "\",\"label\":\"X\"}"));
        var cut = PlayoutIndex.ReadIndex("{\"sig\":\"s\",\"playlists\":[{\"id\":\"big\",\"name\":\"Big\",\"items\":[" + many + "]}]}");
        Check(cut is { Cut: true } && cut.Playlists[0].Items.Count == PlayoutIndex.MaxItems && cut.Lacks(Cue("l", "big", "nowhere"), "P") is null && cut.Lacks(Cue("l", "elsewhere", "x"), "P") is null,
              "playout lists: a list past its bound is cut, and then what it does not hold is not known — never called missing");
        var odd = PlayoutIndex.ReadIndex("""{"sig":"s","playlists":[{"id":"","items":[]},{"id":"ok","items":[{"id":7},{"label":"x"},{"id":"good","label":"G\u0007ood"}]},"x"]}""");
        Check(odd is { Playlists: [{ Id: "ok", Items: [{ Id: "good", Label: "Good" }] }] },
              "playout lists: an entry with no id a line can carry is left out, a label's control characters dropped");
        Check(PlayoutIndex.ReadIndex("nope") is null && PlayoutIndex.ReadIndex("[1]") is null && PlayoutIndex.ReadIndex("""{"playlists":7}""") is null
              && PlayoutIndex.ReadCues("{}") is null && PlayoutIndex.ReadCues("""{"cues":[1,{"id":"a"}]}""") is { Cues: [{ Id: "a", Enabled: true }], Revision: "" },
              "playout lists: an answer it cannot read is none, never an exception");
        Check(index.Lines(1) is ["pl1 it1 · Main show · Intro"] && cues.Lines(5) is ["c1 · 12 Walk-in", "c2 · 13 Dark (switched off)"],
              "playout lists: what the copilot sees of them, bounded, ids a playout line names");
    }

    static void AiFacts(Action<bool, string> Check)
    {
        var items = Enumerable.Range(0, 30).Select(n => Item("i" + Ms(n), Cue("stage-left", "pl1", "it1", standby: n == 0, together: n == 0, label: "Intro"), Cue("patterns", "", "c1"))).ToList();
        var lines = AiContext.PlayoutCues(items, "stage-left");
        Check(lines.Count == 30 && lines[0] == "item 1 (Item i0): pl1 it1 · Intro, stands by at load, together" && lines[1] == "item 2 (Item i1): pl1 it1 · Intro"
              && AiContext.PlayoutCues(items, "patterns") is { Count: 30 } p && p[0] == "item 1 (Item i0): c1"
              && AiContext.PlayoutCues(items.Concat(items).ToList(), "stage-left").Count == AiContext.PlayoutListedMax && AiContext.PlayoutListedMax == 50,
              "playout facts: the copilot sees each link's running-order cues, by item, at most 50");
        var facts = new CopilotFacts(DateTime.UtcNow, ShowClock.For("UTC"), "{}"u8.ToArray(), "{}"u8.ToArray(), [], [], [], 0, OverrunModel.Learn([]), [], [],
            Playout: [new PlayoutFact("stage-left", "playmap", "PlayMap Stage left", true, true, "green", "ok", "", [], "", ["pl1 it1 · Main show · Intro"], lines)]);
        using var doc = JsonDocument.Parse(AiContext.Copilot(facts));
        var link = doc.RootElement.GetProperty("playout_links")[0];
        var names = link.EnumerateObject().Select(x => x.Name).ToList();
        Check(names.TakeLast(2).SequenceEqual(["listed", "cues"]) && link.GetProperty("listed").GetArrayLength() == 1 && link.GetProperty("cues").GetArrayLength() == 30 && link.GetProperty("timed").GetBoolean(),
              "playout facts: what the peer lists and the running order's cues on the link come last in its facts");
    }

    /// <summary>
    /// A GO sent with AT is "scheduled" in its receipt only from its node's own answer, applied or accepted: waiting for that answer,
    /// or unconfirmed, the receipt says what is true whatever comes — it was sent with AT — and a refusal keeps the peer's words alone.
    /// </summary>
    static async Task Receipts(Action<bool, string> Check)
    {
        const string Peer = "PlayMap Stage left";
        static PlayoutAnswer A(PeerOutcome o, string words = "") => new("s9-5e6f7a8b", "PLAYLIST GO pl1 it1 AT 1761000000300", o, words);
        static PlayoutStep Step(Task<PlayoutAnswer> first) => new PlayoutStep(null, "stage-left", Peer, "play Main show · it1", new PlayoutTicket("s9-5e6f7a8b", "PLAYLIST GO pl1 it1 AT 1761000000300", first, new TaskCompletionSource<PlayoutAnswer>().Task))
        {
            Note = "sent with AT for 20:00:01.250 (± 3 ms)",
            Taken = "scheduled for 20:00:01.250 (± 3 ms)",
        };
        var clock = new PlayoutLinkTests.LinkClock(1_800_000_000_000);
        var waiting = await PlayoutReceipts.Within("r1", Step(new TaskCompletionSource<PlayoutAnswer>().Task), TimeSpan.Zero, clock, clock.GetTimestamp());
        var applied = await PlayoutReceipts.Within("r2", Step(Task.FromResult(A(PeerOutcome.Applied))), TimeSpan.Zero, clock, clock.GetTimestamp());
        var accepted = await PlayoutReceipts.Within("r3", Step(Task.FromResult(A(PeerOutcome.Accepted))), TimeSpan.Zero, clock, clock.GetTimestamp());
        var unconfirmed = await PlayoutReceipts.Within("r4", Step(Task.FromResult(A(PeerOutcome.Unconfirmed, "the link dropped before Stage left answered"))), TimeSpan.Zero, clock, clock.GetTimestamp());
        var refused = await PlayoutReceipts.Within("r5", Step(Task.FromResult(A(PeerOutcome.Refused, "double GO protection: 0.40 s since the last GO on Main show (1.00 s armed)"))), TimeSpan.Zero, clock, clock.GetTimestamp());
        Check(waiting is { Ok: true, Stage: "accepted" } && waiting.Effect.EndsWith(" · sent with AT for 20:00:01.250 (± 3 ms)", StringComparison.Ordinal)
              && unconfirmed is { Ok: true } && unconfirmed.Effect.EndsWith(" · sent with AT for 20:00:01.250 (± 3 ms)", StringComparison.Ordinal)
              && !waiting.Effect.Contains("scheduled", StringComparison.Ordinal) && !unconfirmed.Effect.Contains("scheduled", StringComparison.Ordinal),
              $"playout receipts: a timed GO waiting for its node's answer, or unconfirmed, is sent with AT — never scheduled ({waiting.Effect} | {unconfirmed.Effect})");
        Check(applied is { Ok: true, Stage: "applied" } && applied.Effect.EndsWith(" · scheduled for 20:00:01.250 (± 3 ms)", StringComparison.Ordinal)
              && accepted is { Ok: true, Stage: "accepted" } && accepted.Effect.EndsWith(" · scheduled for 20:00:01.250 (± 3 ms)", StringComparison.Ordinal)
              && !applied.Effect.Contains("sent with AT", StringComparison.Ordinal),
              $"playout receipts: once its node took it, applied or accepted, it is scheduled ({applied.Effect})");
        Check(refused is { Ok: false, Effect: "" } && refused.Error!.StartsWith("PlayMap Stage left refused PLAYLIST GO pl1 it1 AT 1761000000300: double GO protection", StringComparison.Ordinal)
              && !refused.Error.Contains("scheduled", StringComparison.Ordinal) && !refused.Error.Contains("sent with AT", StringComparison.Ordinal),
              "playout receipts: a timed GO its node refused keeps the node's words alone");
    }

    // ------------------------------------------------------------------ the link's lists and its AT

    /// <summary>INDEX asked once a PlayMap node's kind is heard and again when its project.index moves; CUE LIST of a Patterns desk, not asked again on a connection that refused it; a GO's AT words.</summary>
    static async Task LinkLists(Action<bool, string> Check)
    {
        var sig = "sig1";
        var extra = "";
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        PlayMap(playmap, () => PmState("idle", index: sig), () => IndexJson(sig, extra));
        await using var patterns = new PlayoutLinkTests.FakeDesk();
        Patterns(patterns, lists: false);
        using var pm = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = playmap.Port, Token = Token, Inst = "1a2b3c4d", Name = "stage-left", Kind = "playmap" });
        using var pat = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = patterns.Port, Token = Token, Inst = "1a2b3c4d", Name = "patterns" });
        pm.Start();
        pat.Start();
        Check(await PlayoutLinkTests.Until(() => pm.Index is { Revision: "sig1" } && pat.Kind == PlayoutRules.Patterns, 10_000)
              && pm.Index!.Playlists[0].Items.Count == 2 && playmap.Heard.Count(l => l.EndsWith(" INDEX", StringComparison.Ordinal)) == 1
              && playmap.Heard.All(l => !l.EndsWith(" CUE LIST", StringComparison.Ordinal)),
              $"playout lists: a PlayMap node is asked its INDEX once its kind is heard, and its list is read ({pm.Status})");

        sig = "sig2";
        extra = ",{\"id\":\"it3\",\"label\":\"Outro\"}";
        playmap.Say("STATE " + PmState("idle", index: sig));
        Check(await PlayoutLinkTests.Until(() => pm.Index is { Revision: "sig2" }, 9_000) && pm.Index!.Playlists[0].Items.Any(i => i.Id == "it3")
              && playmap.Heard.Count(l => l.EndsWith(" INDEX", StringComparison.Ordinal)) == 2,
              "playout lists: a STATE whose project.index moved has the INDEX asked again, within a context's interval");
        Check(pat.Index is null && patterns.Heard.Count(l => l.EndsWith(" CUE LIST", StringComparison.Ordinal)) == 1
              && patterns.Heard.All(l => !l.EndsWith(" INDEX", StringComparison.Ordinal)),
              "playout lists: a Patterns desk is asked its CUE LIST, and one that refuses it is not asked again on that connection");

        var at = 1_761_000_000_123L;
        var go = pm.Playlist("GO", "pl1", "it1", null, -1, at);
        var expect = pm.Playlist("GO", "pl1", null, "it2", -1, at);
        Check(go.Ticket is not null && expect.Ticket is not null
              && await PlayoutLinkTests.Until(() => Commands(playmap).Contains("PLAYLIST GO pl1 it1 AT 1761000000123") && Commands(playmap).Contains("PLAYLIST GO pl1 EXPECT it2 AT 1761000000123")),
              "playout link: a GO at an instant names it last, in the node's clock: PLAYLIST GO <pl> <item> AT <ms>, and with EXPECT");
        Check(pm.Playlist("CUE", "pl1", "it1", null, -1, at) is { Ticket: null } && pm.Playlist("GO", "pl1", "it1", null, -1, 0) is { Ticket: null, Error: { } zero }
              && zero.Contains("Unix ms", StringComparison.Ordinal) && !Commands(playmap).Any(c => c.StartsWith("PLAYLIST CUE", StringComparison.Ordinal)),
              "playout link: only a GO carries AT, and only a Unix ms");
        Check(pm.ClockMayTime && pat.ClockMayTime && pm.ConnectionSerial == 1, "playout link: a link to this PC may time a command; its connection is counted");
    }

    // ------------------------------------------------------------------ the planner over real links

    static async Task Planner(Action<bool, string> Check)
    {
        var pmState = PmState("idle");
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        PlayMap(playmap, () => pmState, () => IndexJson());
        await using var patterns = new PlayoutLinkTests.FakeDesk();
        Patterns(patterns);
        var settings = new List<PlayoutLinkSettings> { L("stage-left", "playmap", playmap.Port), L("patterns", "auto", patterns.Port), L("off", "playmap", 1) };
        settings[2].Enabled = false;
        var rows = new ConcurrentQueue<(int Mark, string Row)>();
        var licensed = true;
        var links = new PlayoutLinks();
        var director = new PlayoutDirector(links, () => settings, () => licensed, () => Lead, (_, mark, row) => rows.Enqueue((mark, row)), _ => { });
        foreach (var s in settings.Take(2)) links.Set(s.Name, PlayoutLinks.Make(s, Token, "2b3c4d5e", TimeProvider.System, null, "", director.Wire));
        try
        {
            Check(await PlayoutLinkTests.Until(() => links["stage-left"]?.Index is not null && links["patterns"]?.Index is not null
                                                    && links["stage-left"]?.Reading?.State.Playlists is [{ State: "idle" }], 10_000),
                  "playout cues: both links paired, their kinds heard and their lists read");
            var both = Item("a", Cue("stage-left", "pl1", "it1", standby: true, label: "Intro"), Cue("patterns", "", "c1", standby: true));

            // Loaded and started in one turn: the GOs alone — never a CUE that shows a frame, never STANDBY beside GO.
            director.ItemLoaded(0, both);
            director.ItemStarted(0, both);
            director.EndOfTurn();
            Check(await PlayoutLinkTests.Until(() => Commands(playmap).Contains("PLAYLIST GO pl1 it1") && Commands(patterns).Contains("CUE GO c1")),
                  "playout cues: an item that starts brings each cue's GO — PlayMap's playlist item, Patterns' CUE GO");
            await Task.Delay(300);
            Check(!Commands(playmap).Any(c => c.StartsWith("PLAYLIST CUE", StringComparison.Ordinal)) && !Commands(patterns).Contains("CUE STANDBY c1"),
                  "playout cues: loaded and started in one turn, nothing stands by — GO only");

            // Loaded with standby off (the default): nothing at all.
            var plain = Item("b", Cue("stage-left", "pl1", "it2"), Cue("patterns", "", "c1"));
            var before = Commands(playmap).Count + Commands(patterns).Count;
            director.ItemLoaded(1, plain);
            director.EndOfTurn();
            await Task.Delay(300);
            Check(Commands(playmap).Count + Commands(patterns).Count == before, "playout cues: standby is off unless set — a load sends nothing");

            // Standby on, the playlist idle: a CUE and a STANDBY, at the end of the turn and not before; then the start's GOs.
            director.ItemLoaded(2, both);
            await Task.Delay(200);
            Check(Commands(playmap).Count + Commands(patterns).Count == before, "playout cues: standing by waits for the end of the turn that loaded the item");
            director.EndOfTurn();
            Check(await PlayoutLinkTests.Until(() => Commands(playmap).Contains("PLAYLIST CUE pl1 it1") && Commands(patterns).Contains("CUE STANDBY c1")),
                  "playout cues: standby on and the playlist idle — PlayMap's CUE (its first frame on air) and Patterns' STANDBY at the end of the turn");
            var gos = Commands(patterns).Count(c => c == "CUE GO c1");
            director.ItemStarted(2, both);
            Check(await PlayoutLinkTests.Until(() => Commands(patterns).Count(c => c == "CUE GO c1") == gos + 1), "playout cues: and when it starts, its GO alone");

            // Standby on, the playlist playing: no CUE, and the row says why.
            pmState = PmState("playing");
            playmap.Say("STATE " + pmState);
            await PlayoutLinkTests.Until(() => links["stage-left"]?.Reading?.State.Playlists is [{ State: "playing" }]);
            var cuesBefore = Commands(playmap).Count(c => c.StartsWith("PLAYLIST CUE", StringComparison.Ordinal));
            director.ItemLoaded(3, both);
            director.EndOfTurn();
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 3 && r.Row == "playout cue Intro on Stage left: not stood by — not cued — Main show is playing on PlayMap Stage left; GO will play it"))
                  && Commands(playmap).Count(c => c.StartsWith("PLAYLIST CUE", StringComparison.Ordinal)) == cuesBefore,
                  "playout cues: a playlist playing is never cued — a CUE would put its item's first frame on air — and the row says so");

            // What a link cannot take is advice before the item and a row at its start, never a command.
            var odd = Item("c", Cue("nope", "pl1", "it1"), Cue("stage-left", "pl1", "it9"), Cue("patterns", "", "c2"), Cue("off", "pl1", "it1"));
            var advice = director.CueAdvice(odd, 4);
            Check(advice.Select(a => a.Text).SequenceEqual([
                      "Item 5 (Item c): playout cue pl1 it1 on Nope will be skipped — no playout link called nope on this PC",
                      "Item 5 (Item c): playout cue pl1 it9 on Stage left will be skipped — PlayMap Stage left's Main show lists no item it9",
                      "Item 5 (Item c): playout cue c2 on Patterns will be skipped — Patterns's cue 13 Dark is switched off",
                      "Item 5 (Item c): playout cue pl1 it1 on Off will be skipped — Off is switched off (Settings → Integrations)"])
                  && advice.All(a => a.Level == AdviceLevel.Warn && a.Command is null) && advice.Select(a => a.Key).Distinct().Count() == 4
                  && director.CueAdvice(both, 0).Count == 0 && director.CueAdvice(Item("d"), 0).Count == 0,
                  $"playout cues: amber advice before the item for each cue a link cannot take, in the row's words ({string.Join(" | ", advice.Select(a => a.Text))})");
            var heard = Commands(playmap).Count + Commands(patterns).Count;
            director.ItemLoaded(4, odd);
            director.ItemStarted(4, odd);
            director.EndOfTurn();
            Check(await PlayoutLinkTests.Until(() => rows.Count(r => r.Mark == 4 && r.Row.Contains(": skipped — ", StringComparison.Ordinal)) == 4)
                  && rows.Any(r => r.Mark == 4 && r.Row == "playout cue pl1 it9 on Stage left: skipped — PlayMap Stage left's Main show lists no item it9"),
                  "playout cues: each is skipped at the item's start with a row, against the item");
            await Task.Delay(200);
            Check(Commands(playmap).Count + Commands(patterns).Count == heard, "playout cues: and nothing of them is sent");

            // Without the licence: one line of advice, one row, nothing sent.
            licensed = false;
            Check(director.CueAdvice(both, 0) is [{ Text: "Item 1 (Item a): its playout cues won't go — playout links need Stagewatch Production" }],
                  "playout cues: without the licence one line says the item's cues won't go");
            director.ItemLoaded(5, both);
            director.ItemStarted(5, both);
            director.EndOfTurn();
            Check(rows.Any(r => r.Mark == 5 && r.Row == "2 playout cues not sent: playout links need Stagewatch Production"), "playout cues: and its start says so once");
            await Task.Delay(200);
            Check(Commands(playmap).Count + Commands(patterns).Count == heard, "playout cues: and sends nothing");
            Check(!playmap.Heard.Concat(patterns.Heard).Any(l => l.Contains(Token, StringComparison.OrdinalIgnoreCase)), "playout cues: neither peer saw the token");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }

    /// <summary>
    /// A running-order row's presses on the desk, over a fake PlayMap node and a fake Patterns desk: "Cue playout now" stands every
    /// cue of the item by — neither set to stand by at load — with PlayMap's CUE on an idle playlist and Patterns' STANDBY, and sends no
    /// GO; "GO playout now" GOes them as a start does and stands nothing by; a playlist playing is never cued, its row saying why while
    /// Patterns' STANDBY still goes. Each link's lane writes in order, so each peer's commands heard after a later press are the whole
    /// of what the presses before it sent — no wait on a quiet moment decides a "sent nothing".
    /// </summary>
    static async Task DeskPresses(Action<bool, string> Check)
    {
        var pmState = PmState("idle");
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        PlayMap(playmap, () => pmState, () => IndexJson());
        await using var patterns = new PlayoutLinkTests.FakeDesk();
        Patterns(patterns);
        var settings = new List<PlayoutLinkSettings> { L("stage-left", "playmap", playmap.Port), L("patterns", "auto", patterns.Port) };
        var rows = new ConcurrentQueue<(int Mark, string Row)>();
        var licensed = true;
        var links = new PlayoutLinks();
        var director = new PlayoutDirector(links, () => settings, () => licensed, () => Lead, (_, mark, row) => rows.Enqueue((mark, row)), _ => { });
        foreach (var s in settings) links.Set(s.Name, PlayoutLinks.Make(s, Token, "2b3c4d5e", TimeProvider.System, null, "", director.Wire));
        List<string> Played() => Commands(playmap).Where(c => c.StartsWith("PLAYLIST ", StringComparison.Ordinal)).ToList();
        List<string> Cued() => Commands(patterns).Where(c => c is not "CUE LIST" && c.StartsWith("CUE ", StringComparison.Ordinal)).ToList();
        bool Fresh() => links["stage-left"] is { } l && PlayoutFacts.Of(l).StateAgeMs < 1_000;
        try
        {
            Check(await PlayoutLinkTests.Until(() => links["stage-left"]?.Index is not null && links["patterns"]?.Index is not null
                                                    && links["stage-left"]?.Reading?.State.Playlists is [{ State: "idle" }], 10_000),
                  "playout desk presses: both links paired, their kinds heard and their lists read");
            var item = Item("a", Cue("stage-left", "pl1", "it1", label: "Intro"), Cue("patterns", "", "c1"));

            playmap.Say("STATE " + pmState);
            await PlayoutLinkTests.Until(Fresh);
            director.CueNow(0, item);
            Check(await PlayoutLinkTests.Until(() => Played().Count == 1 && Cued().Count == 1)
                  && Played() is ["PLAYLIST CUE pl1 it1"] && Cued() is ["CUE STANDBY c1"],
                  $"playout desk presses: Cue playout now stands both cues by though neither is set to stand by at load — PlayMap's CUE, Patterns' STANDBY ({string.Join(" | ", Played().Concat(Cued()))})");

            director.GoNow(0, item);
            Check(await PlayoutLinkTests.Until(() => Played().Count >= 2 && Cued().Count >= 2)
                  && Played() is ["PLAYLIST CUE pl1 it1", "PLAYLIST GO pl1 it1"] && Cued() is ["CUE STANDBY c1", "CUE GO c1"],
                  $"playout desk presses: GO playout now GOes each cue as a start does, and Cue playout now sent no GO before it ({string.Join(" | ", Played().Concat(Cued()))})");

            playmap.Say("STATE " + pmState);
            await PlayoutLinkTests.Until(Fresh);
            director.CueNow(0, item);
            Check(await PlayoutLinkTests.Until(() => Played().Count >= 3 && Cued().Count >= 3)
                  && Played() is ["PLAYLIST CUE pl1 it1", "PLAYLIST GO pl1 it1", "PLAYLIST CUE pl1 it1"] && Cued() is ["CUE STANDBY c1", "CUE GO c1", "CUE STANDBY c1"],
                  $"playout desk presses: GO playout now stood nothing by — each peer heard the next press's standby straight after its GO ({string.Join(" | ", Played().Concat(Cued()))})");

            // A playlist playing is never cued by hand either: its row says why, and Patterns' STANDBY still goes.
            pmState = PmState("playing");
            playmap.Say("STATE " + pmState);
            await PlayoutLinkTests.Until(() => links["stage-left"]?.Reading?.State.Playlists is [{ State: "playing" }]);
            director.CueNow(1, item);
            Check(await PlayoutLinkTests.Until(() => Cued().Count >= 4)
                  && rows.Any(r => r.Mark == 1 && r.Row == "playout cue Intro on Stage left: not stood by — not cued — Main show is playing on PlayMap Stage left; GO will play it")
                  && Cued()[3] == "CUE STANDBY c1" && Played().Count(c => c.StartsWith("PLAYLIST CUE", StringComparison.Ordinal)) == 2,
                  "playout desk presses: Cue playout now never cues a playlist that is playing — the row says why — and the Patterns standby goes");

            // No cues, or no licence: a row each, against the row's item, and nothing planned.
            director.CueNow(2, Item("b"));
            director.GoNow(2, Item("b"));
            licensed = false;
            director.CueNow(3, item);
            director.GoNow(3, item);
            Check(rows.Where(r => r.Mark == 2).Select(r => r.Row).SequenceEqual(["no playout cues to stand by — nothing sent", "no playout cues to GO — nothing sent"])
                  && rows.Where(r => r.Mark == 3).Select(r => r.Row).SequenceEqual(["2 playout cues not stood by: " + PlayoutRules.LicenceWords, "2 playout cues not sent: " + PlayoutRules.LicenceWords]),
                  "playout desk presses: an item with no cues, or no licence, is said in a row each, and nothing is sent");
            Check(item.Playout.All(c => !c.Standby), "playout desk presses: Cue playout now changes no cue's own standby setting");
            Check(!playmap.Heard.Concat(patterns.Heard).Any(l => l.Contains(Token, StringComparison.OrdinalIgnoreCase)), "playout desk presses: neither peer saw the token");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }

    /// <summary>
    /// A start's GOs together on a manual clock: a PlayMap node 5 s ahead gets AT at the instant on its clock, a Patterns desk on this
    /// PC is held here half its round trip before it, one reached as if off this PC at the instant itself; nothing held goes before
    /// its moment, and a drop while it is held leaves it not sent. A lone GO asked together on the wire carries its words in its receipt.
    /// </summary>
    static async Task TogetherOverLinks(Action<bool, string> Check)
    {
        const long ahead = 5000;
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        PlayMap(playmap, () => PmState("idle"), () => IndexJson(), offsetMs: ahead);
        await using var patterns = new PlayoutLinkTests.FakeDesk();
        Patterns(patterns);
        await using var foh = new PlayoutLinkTests.FakeDesk();
        Patterns(foh);
        var settings = new List<PlayoutLinkSettings> { L("stage-left", "playmap", playmap.Port), L("patterns", "patterns", patterns.Port), L("foh", "patterns", foh.Port) };
        var rows = new ConcurrentQueue<(int Mark, string Row)>();
        var status = new ConcurrentQueue<string>();
        var manual = new ShowWorkflowTests.ManualTime(DateTime.UtcNow);
        var links = new PlayoutLinks();
        var licensed = true;
        var director = new PlayoutDirector(links, () => settings, () => licensed, () => Lead, (_, mark, row) => rows.Enqueue((mark, row)), status.Enqueue, manual);
        foreach (var s in settings.Take(2)) links.Set(s.Name, PlayoutLinks.Make(s, Token, "3c4d5e6f", TimeProvider.System, null, "", director.Wire));
        // FOH's desk as if on another PC: its clock is measured over plain TCP off this PC, which never schedules.
        var fohLink = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = foh.Port, Token = Token, Inst = "3c4d5e6f", Name = "foh", Kind = "patterns", IsLoopback = _ => false });
        director.Wire(fohLink);
        fohLink.Start();
        links.Set("foh", fohLink);
        try
        {
            var tol = PlayoutDirector.ToleranceMs(Lead);
            Check(await PlayoutLinkTests.Until(() => links.All.All(l => l.Clock is not null && l.Kind is not null) && links["stage-left"]!.CanSchedule(tol), 10_000)
                  && !fohLink.ClockMayTime && !fohLink.CanSchedule(tol) && links["patterns"]!.ClockMayTime,
                  "playout together: three links paired with clocks; the node's fit, the far desk's measured where it may not time");
            var pm = links["stage-left"]!;
            var t = manual.GetUtcNow().ToUnixTimeMilliseconds() + Lead;
            var item = Item("t", Cue("stage-left", "pl1", "it1", together: true, label: "Intro"), Cue("patterns", "", "c1", together: true), Cue("foh", "", "c1", together: true));
            director.ItemLoaded(0, item);
            director.ItemStarted(0, item);
            director.EndOfTurn();
            Check(await PlayoutLinkTests.Until(() => Commands(playmap).Any(c => c.StartsWith("PLAYLIST GO pl1 it1 AT ", StringComparison.Ordinal))),
                  "playout together: the node's GO goes now, with AT");
            var at = long.Parse(Commands(playmap).First(c => c.StartsWith("PLAYLIST GO pl1 it1 AT ", StringComparison.Ordinal))[23..], CultureInfo.InvariantCulture);
            var offset = pm.Clock!.Value;
            Check(Math.Abs(at - (t + (long)Math.Round(offset.OffsetMs))) <= 1 && Math.Abs(at - (t + ahead)) <= offset.ErrMs + 50,
                  $"playout together: its AT is the instant on the node's own clock — the lead ahead plus its offset (AT {Ms(at)}, T {Ms(t)}, offset {offset.OffsetMs.ToString("0.#", CultureInfo.InvariantCulture)})");
            await Task.Delay(300);
            Check(!Commands(patterns).Contains("CUE GO c1") && !Commands(foh).Contains("CUE GO c1"),
                  "playout together: the desks' GOs are held here — nothing goes before its moment on this PC's clock");
            Check(rows.Any(r => r.Mark == 0 && r.Row.StartsWith("playout cue Intro on Stage left: play Main show · it1 sent with AT for ", StringComparison.Ordinal))
                  && await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 0 && r.Row.StartsWith("playout cue Intro on Stage left: play Main show · it1 scheduled for ", StringComparison.Ordinal)))
                  && rows.Any(r => r.Mark == 0 && r.Row.StartsWith("playout cue c1 on Patterns: GO cue c1 held here, to be sent at ", StringComparison.Ordinal) && r.Row.EndsWith(": Patterns takes no AT", StringComparison.Ordinal))
                  && rows.Any(r => r.Mark == 0 && r.Row.StartsWith("playout cue c1 on Foh: GO cue c1 held here, to be sent at ", StringComparison.Ordinal) && r.Row.Contains("by this PC's clock alone", StringComparison.Ordinal)),
                  "playout together: a row for each timed or held GO, saying when and why — the node's sent with AT, then scheduled once it took it");
            Check(status.Any(l => l.StartsWith("Item 1: playout cue c1 on Foh: GO cue c1 held here, to be sent at ", StringComparison.Ordinal)),
                  "playout together: the planner's rows reach the desk's status line too, with the item's number");
            // The desk on this PC goes half its round trip before the instant (on loopback that may be the instant itself); the far one
            // at the instant, not a millisecond before.
            manual.Advance(TimeSpan.FromMilliseconds(Lead - 1));
            await Task.Delay(300);
            Check(!Commands(foh).Contains("CUE GO c1"), "playout together: the far desk's GO waits for the instant itself");
            manual.Advance(TimeSpan.FromMilliseconds(1));
            Check(await PlayoutLinkTests.Until(() => Commands(foh).Contains("CUE GO c1") && Commands(patterns).Contains("CUE GO c1")),
                  "playout together: at the instant both held GOs have gone — the far desk's by this PC's clock alone");
            Check(Commands(patterns).Count(c => c == "CUE GO c1") == 1 && !Commands(patterns).Contains("CUE STANDBY c1"), "playout together: once each, never with a STANDBY");

            // A GO held while the licence goes is not sent; nor is one whose link Settings stopped while it was held — each said.
            var lapsed = Item("v", Cue("stage-left", "pl1", "it2", together: true), Cue("patterns", "", "c1", together: true));
            director.ItemStarted(2, lapsed);
            await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 2 && r.Row.Contains("held here", StringComparison.Ordinal)));
            licensed = false;
            manual.Advance(TimeSpan.FromMilliseconds(Lead));
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 2 && r.Row == "playout cue c1 on Patterns: not sent — playout links need Stagewatch Production")),
                  "playout together: a GO held while the licence lapsed is not sent, said");
            licensed = true;
            var patLink = links["patterns"]!;
            director.ItemStarted(3, lapsed);
            await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 3 && r.Row.Contains("held here", StringComparison.Ordinal)));
            links.Set("patterns", null);
            manual.Advance(TimeSpan.FromMilliseconds(Lead));
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 3 && r.Row == "playout cue c1 on Patterns: not sent — the link to Patterns was stopped or replaced while it was held")),
                  $"playout together: a GO held on a link Settings stopped is not sent on it, said ({string.Join(" | ", rows.Where(r => r.Mark == 3).Select(r => r.Row))})");
            links.Set("patterns", patLink);
            await Task.Delay(200);
            Check(Commands(patterns).Count(c => c == "CUE GO c1") == 1, "playout together: neither held GO was written");

            // A drop while a GO is held: not sent, said.
            var again = Item("u", Cue("stage-left", "pl1", "it2", together: true), Cue("foh", "", "c1", together: true));
            director.ItemLoaded(1, again);
            director.ItemStarted(1, again);
            director.EndOfTurn();
            await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 1 && r.Row.Contains("held here", StringComparison.Ordinal)));
            var serial = fohLink.ConnectionSerial;
            foh.Closing = true;
            foh.Drop();
            await PlayoutLinkTests.Until(() => fohLink.ConnectionSerial != serial);
            manual.Advance(TimeSpan.FromMilliseconds(Lead));
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 1 && r.Row == "playout cue c1 on Foh: not sent — the link to Patterns Foh dropped while it was held"))
                  && Commands(foh).Count(c => c == "CUE GO c1") == 1,
                  $"playout together: a drop while a GO is held leaves it not sent, never written on the next connection ({string.Join(" | ", rows.Where(r => r.Mark == 1).Select(r => r.Row))})");

            // A lone GO asked together on the wire: AT on a fit clock, its words in the receipt; a desk's lone GO at once, said.
            var step = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it2", true), 0, 7, null);
            Check(step is { Error: null, Ticket: not null } && step.Note.StartsWith("sent with AT for ", StringComparison.Ordinal) && step.Taken.StartsWith("scheduled for ", StringComparison.Ordinal)
                  && await PlayoutLinkTests.Until(() => Commands(playmap).Any(c => c.StartsWith("PLAYLIST GO pl1 it2 AT ", StringComparison.Ordinal)))
                  && rows.Any(r => r.Mark == 7 && r.Row.StartsWith("PlayMap Stage left: play Main show · it2 sent with AT for ", StringComparison.Ordinal))
                  && await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 7 && r.Row.StartsWith("PlayMap Stage left: play Main show · it2 scheduled for ", StringComparison.Ordinal))),
                  $"playout together: playout <link> go … together reaches the node with AT, a row as it goes and one once it took it ({step.Note})");
            var receipt = await PlayoutReceipts.Within("r1", step, TimeSpan.FromSeconds(3), TimeProvider.System, TimeProvider.System.GetTimestamp());
            Check(receipt is { Ok: true } && receipt.Effect.Contains(" · scheduled for ", StringComparison.Ordinal),
                  $"playout together: the receipt says the GO was timed ({receipt.Effect})");
            var lone = director.Playout(new PlayoutArgs("patterns", "go", "", "c1", true), 0, 8, null);
            Check(lone is { Error: null, Note: "not timed: Patterns takes no AT — sent at once", Taken: "" }
                  && await PlayoutLinkTests.Until(() => Commands(patterns).Count(c => c == "CUE GO c1") == 2)
                  && rows.Any(r => r.Mark == 8 && r.Row == "Patterns: GO cue c1 not timed: Patterns takes no AT — sent at once"),
                  $"playout together: a desk's lone GO asked together goes at once and says why, in its row too ({lone.Note})");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }

    /// <summary>
    /// One GO per sync group: a follower's cue is left to its leader's GO among the item's cues whose plans pass, and its row says
    /// whether that GO went; with no such leader among them, its plan refuses it naming its leader.
    /// </summary>
    static async Task SyncGroups(Action<bool, string> Check)
    {
        await using var left = new PlayoutLinkTests.FakeDesk();
        // The leader answers every playlist command but one, which it leaves unanswered: what fills its link's 32 in flight below.
        PlayMap(left, () => PmState("idle", group: "main", role: "leader"), () => IndexJson(),
            answer: body => body == "PLAYLIST GO pl1 it2" ? "" : null);
        await using var right = new PlayoutLinkTests.FakeDesk();
        PlayMap(right, () => PmState("idle", id: "pm9a8b7c", group: "main", role: "follower", leader: "Stage left"), () => IndexJson(),
            context: PmContext.Replace("pm5e6f7a", "pm9a8b7c", StringComparison.Ordinal));
        var settings = new List<PlayoutLinkSettings> { L("stage-left", "playmap", left.Port), L("stage-right", "playmap", right.Port) };
        var rows = new ConcurrentQueue<(int Mark, string Row)>();
        var links = new PlayoutLinks();
        var director = new PlayoutDirector(links, () => settings, () => true, () => Lead, (_, mark, row) => rows.Enqueue((mark, row)), _ => { });
        foreach (var s in settings) links.Set(s.Name, PlayoutLinks.Make(s, Token, "4d5e6f7a", TimeProvider.System, null, "", director.Wire));
        try
        {
            Check(await PlayoutLinkTests.Until(() => links.All.All(l => l.Reading?.State.Sync is not null), 10_000), "playout sync: both nodes' STATEs read");
            var item = Item("s", Cue("stage-right", "pl1", "it1"), Cue("stage-left", "pl1", "it1"));
            director.ItemLoaded(0, item);
            director.ItemStarted(0, item);
            director.EndOfTurn();
            Check(await PlayoutLinkTests.Until(() => Commands(left).Contains("PLAYLIST GO pl1 it1"))
                  && rows.Any(r => r.Row == "playout cue pl1 it1 on Stage right: not sent — PlayMap Stage right follows sync group main, which PlayMap Stage left's GO moves"),
                  "playout sync: the item's cues on one sync group collapse to one GO, its leader's, and the follower's row says so");
            var alone = Item("f", Cue("stage-right", "pl1", "it1"));
            director.ItemStarted(1, alone);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 1 && r.Row.Contains("follows sync group main (leader Stage left) — GO its leader", StringComparison.Ordinal))),
                  "playout sync: a follower's cue with no leader among the item's cues is refused, naming its leader");
            await Task.Delay(200);
            Check(!Commands(right).Any(c => c.StartsWith("PLAYLIST GO", StringComparison.Ordinal)), "playout sync: the follower is never sent a GO of its own");

            // A leader whose GO its plan refuses moves nothing — here Settings now name its link another kind than the node it runs to:
            // the follower stands alone, and its own plan says to GO its leader.
            settings[0].Kind = "patterns";
            director.ItemStarted(2, item);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 2 && r.Row.StartsWith("playout cue pl1 it1 on Stage left: not sent — ", StringComparison.Ordinal) && r.Row.Contains("but its settings say Patterns", StringComparison.Ordinal))
                                                    && rows.Any(r => r.Mark == 2 && r.Row.StartsWith("playout cue pl1 it1 on Stage right: not sent — PlayMap Stage right's Main show follows sync group main (leader Stage left) — GO its leader", StringComparison.Ordinal)))
                  && !rows.Any(r => r.Mark == 2 && r.Row.Contains("which PlayMap Stage left's GO moves", StringComparison.Ordinal)),
                  $"playout sync: a leader's GO its plan refuses moves no follower, and the follower's row never says it does ({string.Join(" | ", rows.Where(r => r.Mark == 2).Select(r => r.Row))})");
            settings[0].Kind = "playmap";

            // A leader's GO its link does not send (32 wait for answers on it): the follower's row says its leader's GO was not sent, and why.
            var leader = links["stage-left"]!;
            for (var n = 0; n < PeerLink.InflightMax; n++) leader.Playlist("GO", "pl1", "it2", null);
            Check(await PlayoutLinkTests.Until(() => Commands(left).Count(c => c == "PLAYLIST GO pl1 it2") == PeerLink.InflightMax), "playout sync: the leader's link has 32 waiting for answers");
            director.ItemStarted(3, item);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 3 && r.Row.StartsWith("playout cue pl1 it1 on Stage left: not sent — ", StringComparison.Ordinal))
                                                    && rows.Any(r => r.Mark == 3 && r.Row.StartsWith("playout cue pl1 it1 on Stage right: not sent — PlayMap Stage right follows sync group main, and its leader's GO on PlayMap Stage left was not sent: ", StringComparison.Ordinal)))
                  && !rows.Any(r => r.Mark == 3 && r.Row.Contains("which PlayMap Stage left's GO moves", StringComparison.Ordinal)),
                  $"playout sync: a leader's GO not sent leaves its followers' rows saying so, never that it moved them ({string.Join(" | ", rows.Where(r => r.Mark == 3).Select(r => r.Row))})");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }

    /// <summary>
    /// 13.4.3: a peer that answers as another node than its link is set to feeds no reader — the list it gave before is dropped, a
    /// cue on it is skipped for that, never for what that list lacked — and once it answers as the node meant its list is asked again.
    /// </summary>
    static async Task WrongNode(Action<bool, string> Check)
    {
        var id = "pm5e6f7a";
        var extra = "";
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        PlayMap(playmap, () => PmState("idle", id: id), () => IndexJson("sig1", extra));
        var set = L("stage-left", "playmap", playmap.Port);
        set.Node = "pm5e6f7a";
        var settings = new List<PlayoutLinkSettings> { set };
        var rows = new ConcurrentQueue<(int Mark, string Row)>();
        var links = new PlayoutLinks();
        var director = new PlayoutDirector(links, () => settings, () => true, () => Lead, (_, mark, row) => rows.Enqueue((mark, row)), _ => { });
        links.Set("stage-left", PlayoutLinks.Make(set, Token, "6f7a8b9c", TimeProvider.System, null, "", director.Wire));
        try
        {
            var link = links["stage-left"]!;
            Check(await PlayoutLinkTests.Until(() => link.Index is { Revision: "sig1" }, 10_000), "playout lists: the node meant lists its playlists");
            var nine = Item("x", Cue("stage-left", "pl1", "it9"));
            id = "pm9a8b7c";
            playmap.Say("STATE " + PmState("idle", id: id));
            Check(await PlayoutLinkTests.Until(() => link.WrongKind is not null) && link.Index is null
                  && director.CueAdvice(nine, 0) is [{ Text: var words }] && words.Contains("answers as node pm9a8b7c, not node pm5e6f7a", StringComparison.Ordinal)
                  && !words.Contains("lists no item", StringComparison.Ordinal),
                  $"playout lists: a peer answering as another node drops the list it gave, and a cue on it is skipped for that ({link.WrongKind})");
            director.ItemStarted(0, nine);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 0 && r.Row.StartsWith("playout cue pl1 it9 on Stage left: skipped — ", StringComparison.Ordinal) && r.Row.Contains("answers as node pm9a8b7c", StringComparison.Ordinal))),
                  "playout lists: and its row at the start says the same");
            var asked = playmap.Heard.Count(l => l.EndsWith(" INDEX", StringComparison.Ordinal));
            id = "pm5e6f7a";
            extra = ",{\"id\":\"it9\",\"label\":\"Encore\"}";
            playmap.Say("STATE " + PmState("idle", id: id));
            Check(await PlayoutLinkTests.Until(() => link.Index is { } i && i.Playlists[0].Items.Any(x => x.Id == "it9"), 9_000)
                  && playmap.Heard.Count(l => l.EndsWith(" INDEX", StringComparison.Ordinal)) == asked + 1 && director.CueAdvice(nine, 0).Count == 0,
                  "playout lists: once it answers as the node meant again, its list is asked again and read");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }

    /// <summary>A timed GO the node started past its instant (its AT clamped) has a row saying how late; one on time has none.</summary>
    static async Task LateStart(Action<bool, string> Check)
    {
        var state = PmState("idle");
        var lateBy = 140L;
        var refuse = false;
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        PlayMap(playmap, () => state, () => IndexJson(), answer: body =>
        {
            if (!body.StartsWith("PLAYLIST GO pl1 ", StringComparison.Ordinal) || body.LastIndexOf(" AT ", StringComparison.Ordinal) is not (> 0 and var i)) return null;
            if (refuse) return Err("@@", "double GO protection: 0.40 s since the last GO on Main show (1.00 s armed)");
            var at = long.Parse(body[(i + 4)..], CultureInfo.InvariantCulture);
            var item = body.Split(' ')[3];
            // The node pushes its new STATE ahead of its answer: playing the item, started lateBy past the AT on its own clock.
            state = PmState("playing", at: at + lateBy + 500, position: 500, item: item);
            return "STATE " + state + "\n" + Ok("@@");
        });
        var settings = new List<PlayoutLinkSettings> { L("stage-left", "playmap", playmap.Port) };
        var rows = new ConcurrentQueue<(int Mark, string Row)>();
        var links = new PlayoutLinks();
        var director = new PlayoutDirector(links, () => settings, () => true, () => Lead, (_, mark, row) => rows.Enqueue((mark, row)), _ => { });
        links.Set("stage-left", PlayoutLinks.Make(settings[0], Token, "5e6f7a8b", TimeProvider.System, null, "", director.Wire));
        try
        {
            Check(await PlayoutLinkTests.Until(() => links["stage-left"]!.CanSchedule(PlayoutDirector.ToleranceMs(Lead)) && links["stage-left"]!.Kind is not null, 10_000),
                  "playout late: the node's clock fit for a timed GO");
            var item = Item("l", Cue("stage-left", "pl1", "it1", together: true));
            director.ItemStarted(0, item);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 0 && r.Row == "PlayMap Stage left: Main show started late by 140 ms (AT arrived after its instant)"), 6000)
                  && rows.Any(r => r.Mark == 0 && r.Row == "seen: Main show plays it1 on PlayMap Stage left")
                  && rows.Any(r => r.Mark == 0 && r.Row.StartsWith("playout cue pl1 it1 on Stage left: play Main show · it1 scheduled for ", StringComparison.Ordinal)),
                  $"playout late: scheduled, seen, and started late by what its STATE says ({string.Join(" | ", rows.Select(r => r.Row))})");
            lateBy = 5;
            var onTime = Item("m", Cue("stage-left", "pl1", "it2", together: true));
            director.ItemStarted(1, onTime);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 1 && r.Row == "seen: Main show plays it2 on PlayMap Stage left"), 6000)
                  && !rows.Any(r => r.Mark == 1 && r.Row.Contains("started late", StringComparison.Ordinal)),
                  "playout late: a GO that started at its instant, within half its round trip and 20 ms, has no late row");

            // A timed GO its node refuses (its double-GO fence) was sent with AT and is never said to be scheduled — by a row or a receipt.
            refuse = true;
            director.ItemStarted(2, Item("n", Cue("stage-left", "pl1", "it1", together: true)));
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 2 && r.Row.Contains("refused PLAYLIST GO pl1 it1 AT ", StringComparison.Ordinal) && r.Row.EndsWith(": double GO protection: 0.40 s since the last GO on Main show (1.00 s armed)", StringComparison.Ordinal)), 6000)
                  && rows.Any(r => r.Mark == 2 && r.Row.StartsWith("playout cue pl1 it1 on Stage left: play Main show · it1 sent with AT for ", StringComparison.Ordinal)),
                  $"playout late: a timed GO the node refuses has its sent row and the node's refusal ({string.Join(" | ", rows.Where(r => r.Mark == 2).Select(r => r.Row))})");
            var step = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it1", true), 0, 3, null);
            var receipt = await PlayoutReceipts.Within("r9", step, TimeSpan.FromSeconds(3), TimeProvider.System, TimeProvider.System.GetTimestamp());
            await Task.Delay(300);
            Check(step is { Error: null } && step.Note.StartsWith("sent with AT for ", StringComparison.Ordinal) && receipt is { Ok: false } && receipt.Error!.Contains("double GO protection", StringComparison.Ordinal)
                  && !receipt.Error.Contains("scheduled", StringComparison.Ordinal)
                  && !rows.Any(r => r.Mark is 2 or 3 && r.Row.Contains("scheduled for", StringComparison.Ordinal)),
                  $"playout late: no row and no receipt says a refused timed GO was scheduled ({receipt.Error})");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }
}
