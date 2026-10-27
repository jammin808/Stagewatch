using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

/// <summary>
/// The link set, PlayoutDirector and receipts (SW20.5): what a playout command becomes from the peer's own facts (Patterns' clip
/// on a clock of its own refused in its words until it says pausable, PlayMap never refused by role, a sync group's follower, a CUE
/// only on an idle playlist, go and cue only once the kind is heard), the receipt completed in the peer's words with its one final
/// follow-up, the clips' ends moving the running order once an execution, the VT's pick, every reader's fields (context items from
/// the record, frame link and node, config playoutLinks, AI facts), and the link itself: a PlayMap node heard by its STATE, its
/// playlist lines quoted by its rule, WHOAMI, CONTEXT at once, tickets, and a peer of another kind sent nothing. Fields first.
/// </summary>
static class PlayoutDirectorTests
{
    internal const string Token = "AB12CD34";
    const string Nonce = "00112233445566778899aabbccddeeff";

    public static async Task Run(Action<bool, string> Check)
    {
        Plans(Check);
        await Receipts(Check);
        ClipEnds(Check);
        Vt(Check);
        Readers(Check);
        Whoami(Check);
        await PlayMapLink(Check);
        await KindMismatch(Check);
        await ContextOfAnother(Check);
        await ContextOfAnotherKind(Check);
        await Tickets(Check);
        await Director(Check);
    }

    // ------------------------------------------------------------------ helpers

    static PlayoutVideo Clip(string role, bool? pausable = null, bool? stoppable = null, string key = "")
        => new("opener.mp4", "", 60, 10, true, false, false, Transport: true, Role: role, Key: key, Pausable: pausable, Stoppable: stoppable);

    static PlayoutPeerState PatternsState(PlayoutVideo? v) => new() { Whole = true, Kind = PlayoutRules.Patterns, HasVideo = true, Video = v };

    static PlayoutPeerState PlayMapState(string plState = "idle", string role = "solo", string group = "", string next = "it2")
        => new()
        {
            Whole = true, Kind = PlayoutRules.PlayMap, Id = "pm5e6f7a", HasVideo = true, Video = Clip("playlist"), HasPlaylists = true,
            Playlists = [new PlayoutPlaylist("pl1", "Show", plState, "it1", "Intro", 0, group, next, next.Length > 0 ? "Walk-in" : "", 1)],
            HasSync = true, Sync = new PlayoutSync(role, role == "follower" ? "Stage left" : ""),
        };

    static PlayoutLinkSettings L(string name, string kind = "auto") => new() { Name = name, Kind = kind, Enabled = true };

    static (string Tag, string Body) Split(string line)
    {
        if (!line.StartsWith('@')) return ("", line);
        var sp = line.IndexOf(' ');
        return sp < 0 ? (line, "") : (line[..sp], line[(sp + 1)..]);
    }

    static string Ok(string tag, string words = "") => "OK" + (tag.Length > 0 ? " " + tag : "") + (words.Length > 0 ? " " + words : "");

    static string Err(string tag, string words) => "ERR" + (tag.Length > 0 ? " " + tag : "") + " " + words;

    static string Proof(string token) => PlayoutWire.Proof(token, Nonce, PlayoutWire.HelloName)!;

    static string Now(long offsetMs = 0) => (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + offsetMs).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A 1.1 peer as a fake desk plays it: it greets with a NONCE, takes the proof of <see cref="Token"/>, answers the probe, pings
    /// (its clock <paramref name="offsetMs"/> ahead of this PC's), STATUS (<paramref name="state"/>), CONTEXT (<paramref name="context"/>),
    /// WHOAMI (<paramref name="whoami"/>, none for a Patterns desk) and each command <paramref name="commands"/> knows, by its line
    /// without its id ("@@" in the answer is the id).
    /// </summary>
    internal static void Peer(PlayoutLinkTests.FakeDesk desk, Func<string> state, string context, string? whoami, Func<string, string?> commands, long offsetMs = 0)
    {
        desk.Greeting = () => ["NONCE " + Nonce];
        desk.Answer = l =>
        {
            var (tag, body) = Split(l);
            if (body.StartsWith("HELLO", StringComparison.Ordinal)) return [Ok(tag), "STATE " + state()];
            if (body.StartsWith("AUTH-PROOF ", StringComparison.Ordinal)) return body == "AUTH-PROOF " + Proof(Token) ? [Ok(tag, "paired")] : [Err(tag, "wrong token")];
            if (body.StartsWith("PING", StringComparison.Ordinal))
            {
                var t1 = body.Length > 5 ? body[5..] : Now();
                return [Ok(tag, "PONG " + t1 + " " + Now(offsetMs) + " " + Now(offsetMs))];
            }
            if (body == "STATUS") return [Ok(tag, state())];
            if (body == "CONTEXT") return [Ok(tag, context)];
            if (body == "WHOAMI") return [whoami is { } w ? Ok(tag, w) : Err(tag, "unknown verb WHOAMI")];
            if (commands(body) is { } answer) return answer.Replace("@@", tag, StringComparison.Ordinal).Split('\n');
            return [];
        };
    }

    const string PlayMapStateJson = """{"v":1,"seq":4,"id":"pm5e6f7a","t":"playmap","video":{"file":"intro.mov","role":"playlist","positionMs":1000,"lengthMs":90000,"playing":true,"seq":2},"playlists":[{"id":"pl1","name":"Main show","state":"playing","index":0,"item":{"id":"it1","label":"Intro"},"positionMs":1000,"syncGroup":"","seq":2,"next":{"id":"it2","label":"Walk-in"}}],"sync":{"role":"solo","leader":""}}""";

    const string PlayMapContext = """{"v":1,"node":{"t":"playmap","id":"pm5e6f7a","name":"Stage left"},"light":"green","headline":"Out 3 · 60 fps","items":[],"commands":[{"verb":"PLAYLIST GO <playlist> [<item>]","risk":"high"}]}""";

    const string PatternsContext = """{"v":1,"node":{"t":"patterns","id":"9f8e7d6c","name":"Patterns FOH"},"light":"green","headline":"3 screens green","items":[],"commands":[{"verb":"VIDEO PAUSE","risk":"high"}]}""";

    // ------------------------------------------------------------------ the plan, pure

    static void Plans(Action<bool, string> Check)
    {
        var patterns = L("patterns");
        PlayoutPlan P(PlayoutLinkSettings link, PlayoutFacts f, string action, string list = "", string item = "", bool together = false, int s = 0)
            => PlayoutDirector.Plan(link, f, action, list, item, together, s, 300);
        PlayoutFacts Pat(PlayoutVideo? v, string? context = null) => new(PlayoutRules.Patterns, PatternsState(v), 100, context);

        // G48: Patterns' clip on a clock of its own, refused in its own words until its pausable / stoppable say otherwise.
        Check(P(patterns, Pat(Clip("playlist")), "pause") is { Error: { } p1 } && p1 == "Patterns: 'opener.mp4' is a playlist item — its own clock keeps running, so it is not paused (end or seek move it)",
              "playout plan: Patterns' playlist item is not paused, in its words, while it publishes no pausable");
        Check(P(patterns, Pat(Clip("stinger")), "stop") is { Error: { } p2 } && p2.Contains("is a stinger's clip — its own clock keeps running, so it is not stopped (restart puts it back to the top)", StringComparison.Ordinal),
              "playout plan: a stinger's clip is not stopped, in Patterns' words");
        Check(P(patterns, Pat(Clip("playlist", pausable: true)), "pause") is { Error: null, Call: PlayoutCall.Clip, What: "PAUSE" }
              && P(patterns, Pat(Clip("stinger", stoppable: true)), "stop") is { Error: null, What: "STOP" },
              "playout plan: once Patterns says pausable or stoppable, its word decides and the line goes");
        Check(P(patterns, Pat(Clip("program", pausable: false)), "pause") is { Error: { } p3 } && p3.Contains("says 'opener.mp4' can't be paused now", StringComparison.Ordinal)
              && P(patterns, Pat(Clip("program")), "pause") is { Error: null },
              "playout plan: a pausable false refuses whatever the role; a program clip with no word goes");
        Check(P(patterns, Pat(Clip("playlist")), "end", s: 10) is { Error: null, What: "END", Seconds: 10 } && P(patterns, Pat(Clip("playlist")), "seek", s: 30) is { Error: null },
              "playout plan: end and seek move a playlist item, as Patterns' own words point at");
        Check(P(L("stage-left", "playmap"), new(PlayoutRules.PlayMap, PlayMapState(), 100, null), "pause") is { Error: null, What: "PAUSE" },
              "playout plan: PlayMap is never refused by role (its playlist lane holds on PAUSE)");
        // ON <key>: only when Patterns' context lists that form, and only a key that reads back as itself bare.
        const string OnKey = """{"v":1,"node":{"t":"patterns","id":"9f8e7d6c","name":"FOH"},"commands":[{"verb":"VIDEO PLAY|PAUSE|STOP ON <key>","risk":"high"}]}""";
        Check(P(patterns, Pat(Clip("layer", key: "layer:2"), OnKey), "pause") is { OnKey: "layer:2" }
              && P(patterns, Pat(Clip("layer", key: "layer:2"), PatternsContext), "pause") is { Error: null, OnKey: null }
              && P(patterns, Pat(Clip("layer", key: "layer 2"), OnKey), "pause") is { Error: null, OnKey: null }
              && P(patterns, Pat(Clip("layer", key: "layer:2"), OnKey), "end", s: 10) is { OnKey: null },
              "playout plan: VIDEO … ON <key> only for a verb Patterns' context lists with that form, and a key it reads back bare; else the clip on air");
        // go and cue: the kind heard first, together not carried, a kind that contradicts the settings refused.
        Check(P(patterns, new(null, null, long.MaxValue, null), "go", item: "c1") is { Error: { } k } && k == "Patterns's kind is not known yet — no STATE heard; nothing sent",
              "playout plan: go and cue wait for the peer's kind (its STATE)");
        Check(P(patterns, new(null, null, long.MaxValue, null), "pause") is { Error: null, What: "PAUSE" },
              "playout plan: the clip's transport needs no kind (the link refuses with no clip on air)");
        Check(P(L("stage-left", "playmap"), new(PlayoutRules.PlayMap, PlayMapState(), 100, null), "go", "show", "it1", together: true) is { Error: null, Together: true, At: null, What: "GO" }
              && P(patterns, Pat(null, PatternsContext), "go", item: "c1", together: true) is { Error: null, Together: true, What: "GO" }
              && P(L("stage-left", "playmap"), new(PlayoutRules.PlayMap, PlayMapState(), 100, null), "go", "show", "it1") is { Together: false },
              "playout plan: a GO asked together is marked so, never timed by the plan — when it goes is the timing's, from each link's clock");
        Check(P(L("stage-left", "playmap"), new(PlayoutRules.Patterns, PatternsState(null), 100, null), "pause") is { Error: { } m } && m.Contains("answers as Patterns, but its settings say PlayMap", StringComparison.Ordinal),
              "playout plan: a peer that answers as another kind than its settings is sent nothing");
        // PlayMap: a playlist's item, never on a follower, a CUE only on a playlist seen idle a moment ago, a bare GO only with its EXPECT.
        var pm = L("stage-left", "playmap");
        PlayoutFacts Pm(PlayoutPeerState st, long age = 100) => new(PlayoutRules.PlayMap, st, age, null);
        Check(P(pm, Pm(PlayMapState()), "cue", "", "show") is { Error: { } c1 } && c1.Contains("cue <playlist> <item>", StringComparison.Ordinal),
              "playout plan: PlayMap cues a playlist's item, two words");
        Check(P(pm, Pm(PlayMapState(role: "follower", group: "main")), "go", "Show", "it1") is { Error: { } f } && f == "PlayMap Stage left's Show follows sync group main (leader Stage left) — GO its leader; nothing sent"
              && P(pm, Pm(PlayMapState(role: "follower", group: "main")), "cue", "pl1", "it1") is { Error: not null },
              "playout plan: a GO or CUE on a sync group's follower is refused naming its leader");
        Check(P(pm, Pm(PlayMapState("idle")), "cue", "show", "it3") is { Error: null, Call: PlayoutCall.Playlist, What: "CUE", List: "show", Item: "it3", Watch: not null }
              && P(pm, Pm(PlayMapState("ended")), "cue", "pl1", "it3") is { Error: null },
              "playout plan: a CUE goes to a playlist seen idle or ended (by its id or its name, any case)");
        Check(P(pm, Pm(PlayMapState("playing")), "cue", "show", "it3") is { Error: { } busy } && busy == "not cued — Show is playing on PlayMap Stage left; GO will play it"
              && P(pm, Pm(PlayMapState("idle"), age: 5000), "cue", "show", "it3") is { Error: { } old } && old.Contains("STATE is 5 s old", StringComparison.Ordinal)
              && P(pm, Pm(PlayMapState("idle")), "cue", "nope", "it3") is { Error: { } missing } && missing.Contains("isn't in PlayMap Stage left's STATE", StringComparison.Ordinal),
              "playout plan: no CUE on a playlist that plays, one seen too long ago, or one its STATE does not show (a CUE shows its first frame on air)");
        Check(P(pm, Pm(PlayMapState("playing")), "go", "show", "it3") is { Error: null, What: "GO", List: "show", Item: "it3", Expect: null }
              && P(pm, Pm(PlayMapState("playing")), "go", "", "show") is { Error: null, What: "GO", Item: null, Expect: "it2" }
              && P(pm, Pm(PlayMapState("playing", next: "")), "go", "", "show") is { Error: { } noNext } && noNext.Contains("next item isn't in", StringComparison.Ordinal),
              "playout plan: GO with an item, or with none its next by EXPECT, refused when its STATE names no next");
        // Patterns: one cue, bare, GO or STANDBY — never both.
        Check(P(patterns, Pat(null), "go", "", "c0ffee12") is { Error: null, Call: PlayoutCall.Cue, What: "GO", Item: "c0ffee12" }
              && P(patterns, Pat(null), "cue", "", "c0ffee12") is { Error: null, What: "STANDBY" },
              "playout plan: a Patterns cue's GO is CUE GO alone, its cue CUE STANDBY");
        Check(P(patterns, Pat(null), "go", "show", "c1") is { Error: { } two } && two.Contains("one cue, no playlist", StringComparison.Ordinal)
              && P(patterns, Pat(null), "go", "", "Cue 12") is { Error: { } bare } && bare.Contains("bare", StringComparison.Ordinal),
              "playout plan: Patterns takes one cue, by its id, bare");
    }

    // ------------------------------------------------------------------ receipts

    static async Task Receipts(Action<bool, string> Check)
    {
        static PlayoutTicket Ticket(Task<PlayoutAnswer> first, Task<PlayoutAnswer> final) => new("s17-5e6f7a8b", "PLAYLIST GO show intro", first, final);
        static PlayoutAnswer A(PeerOutcome o, string words = "") => new("s17-5e6f7a8b", "PLAYLIST GO show intro", o, words);
        var t = Ticket(Task.FromResult(A(PeerOutcome.Applied)), Task.FromResult(A(PeerOutcome.Applied)));
        const string Peer = "PlayMap Stage left";
        Check(PlayoutReceipts.Of("a1", A(PeerOutcome.Applied), Peer, t) is { Ok: true, Stage: "applied", Effect: "PlayMap Stage left took PLAYLIST GO show intro", Error: null } r1
              && r1.Refs!["peerRequest"] == 17 && t.Number == 17,
              "playout receipts: applied in Stagewatch's words where the peer gave none, the peer's request named");
        Check(PlayoutReceipts.Of("a1", A(PeerOutcome.Refused, "Show has no item intro"), Peer, t) is { Ok: false, Stage: "applied", Error: "PlayMap Stage left refused PLAYLIST GO show intro: Show has no item intro" },
              "playout receipts: refused in the peer's own words");
        Check(PlayoutReceipts.Of("a1", A(PeerOutcome.Accepted, "asks for a second GO — a person's"), Peer, t) is { Ok: true, Stage: "accepted", Effect: "PlayMap Stage left: asks for a second GO — a person's" }
              && PlayoutReceipts.Of("a1", A(PeerOutcome.Unconfirmed, "the link dropped before Stage left answered"), Peer, t) is { Ok: true, Stage: "accepted", Effect: "unconfirmed — the link dropped before Stage left answered" }
              && PlayoutReceipts.Of("a1", A(PeerOutcome.NotSent, "Stage left is down — not sent; nothing is queued"), Peer, t) is { Ok: false, Error: "not sent — Stage left is down — not sent; nothing is queued" },
              "playout receipts: accepted (taken, not run yet), unconfirmed (it may have run) and not sent, never a done the peer did not say");
        var plain = Encoding.UTF8.GetString(new CommandReceipt("a1", true, "applied", "", null).ToJson());
        var final = Encoding.UTF8.GetString((PlayoutReceipts.Of("a1", A(PeerOutcome.Applied), Peer, t) with { IsFinal = true }).ToJson());
        Check(!plain.Contains("final", StringComparison.Ordinal) && final.EndsWith(",\"final\":true}", StringComparison.Ordinal) && final.StartsWith("{\"type\":\"receipt\",\"id\":\"a1\",\"ok\":true,\"stage\":\"applied\"", StringComparison.Ordinal),
              $"playout receipts: final is a new last field, only on the follow-up ({final})");

        var step = new PlayoutStep(null, "stage-left", Peer, "play show · intro", t);
        var clock = new PlayoutLinkTests.LinkClock(1_800_000_000_000);
        Check(await PlayoutReceipts.Within("a1", step, TimeSpan.FromSeconds(3), clock, clock.GetTimestamp()) is { Stage: "applied", Final: null },
              "playout receipts: an answer within the deadline is the receipt, and nothing follows an applied one");
        Check(await PlayoutReceipts.Within("a1", PlayoutStep.Refused("no playout link called x"), TimeSpan.FromSeconds(3), clock, 0) is { Ok: false, Error: "no playout link called x" },
              "playout receipts: a command refused before it was sent is refused, nothing sent");

        static TaskCompletionSource<PlayoutAnswer> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        // No answer within the deadline: accepted, sent and waiting; its follow-up is the peer's final answer, marked final.
        var first = Pending();
        var last = Pending();
        var slow = step with { Ticket = Ticket(first.Task, last.Task) };
        var waiting = await PlayoutReceipts.Within("a2", slow, TimeSpan.Zero, clock, clock.GetTimestamp());
        Check(waiting is { Ok: true, Stage: "accepted", Effect: "sent to PlayMap Stage left — waiting for its answer", Final: not null, IsFinal: false },
              "playout receipts: no answer within the deadline — accepted, sent and waiting, a follow-up to come");
        first.SetResult(A(PeerOutcome.Applied));
        last.SetResult(A(PeerOutcome.Applied));
        Check(await waiting.Final! is { IsFinal: true, Stage: "applied", Id: "a2" } f2 && Encoding.UTF8.GetString(f2.ToJson()).EndsWith("\"final\":true}", StringComparison.Ordinal),
              "playout receipts: the follow-up is the peer's final answer, marked final");

        // No answer within the deadline, then a late accepted, then applied: the follow-up waits for the final one, never the accepted.
        var lateFirst = Pending();
        var lateFinal = Pending();
        var late = await PlayoutReceipts.Within("a5", step with { Ticket = Ticket(lateFirst.Task, lateFinal.Task) }, TimeSpan.Zero, clock, clock.GetTimestamp());
        lateFirst.SetResult(A(PeerOutcome.Accepted, "starting Intro"));
        await Task.Yield();
        var stillWaiting = !late.Final!.IsCompleted;
        lateFinal.SetResult(A(PeerOutcome.Applied, "playing Intro"));
        Check(stillWaiting && await late.Final! is { IsFinal: true, Ok: true, Stage: "applied", Effect: "PlayMap Stage left took PLAYLIST GO show intro (playing Intro)" },
              "playout receipts: an accepted that comes after the deadline is not the follow-up — the final applied is, once");

        // A peer that answered accepted: its final applied or refused follows, within receipt.final.ms of the send.
        var acc = Pending();
        var accepted = step with { Ticket = Ticket(Task.FromResult(A(PeerOutcome.Accepted, "at 20:00:01.250")), acc.Task) };
        var sent = clock.GetTimestamp();
        var r3 = await PlayoutReceipts.Within("a3", accepted, TimeSpan.FromSeconds(3), clock, sent);
        Check(r3 is { Stage: "accepted", Effect: "PlayMap Stage left: at 20:00:01.250", Final: not null }, "playout receipts: the peer's accepted, its words, a final to follow");
        acc.SetResult(A(PeerOutcome.Refused, "Show has no item intro"));
        Check(await r3.Final! is { IsFinal: true, Ok: false, Error: { } e3 } && e3.EndsWith("Show has no item intro", StringComparison.Ordinal),
              "playout receipts: the accepted command's final refusal follows, in the peer's words");

        // No final within receipt.final.ms of the send: the follow-up is Stagewatch's own unconfirmed — it may have run — never nothing.
        var quietClock = new PlayoutLinkTests.LinkClock(1_800_000_000_000);
        var quiet = await PlayoutReceipts.Within("a4", step with { Ticket = Ticket(Task.FromResult(A(PeerOutcome.Accepted)), Pending().Task) }, TimeSpan.FromSeconds(3), quietClock, quietClock.GetTimestamp());
        await PlayoutLinkTests.Until(() => quietClock.OneShotWaiting, 2000);
        quietClock.Advance(29_999);
        await Task.Yield();
        var before30 = !quiet.Final!.IsCompleted;
        quietClock.Advance(1);
        var unconfirmed = await quiet.Final!;
        Check(before30 && unconfirmed is { IsFinal: true, Ok: true, Stage: "accepted", Effect: "unconfirmed — PlayMap Stage left took it and gave no final answer in 30 s — it may have run; look at its state" }
              && unconfirmed.Refs!["peerRequest"] == 17 && Encoding.UTF8.GetString(unconfirmed.ToJson()).EndsWith(",\"final\":true}", StringComparison.Ordinal),
              $"playout receipts: no final within receipt.final.ms — the follow-up says unconfirmed in Stagewatch's own words, marked final ({unconfirmed.Effect})");
        var silentClock = new PlayoutLinkTests.LinkClock(1_800_000_000_000);
        var silent = await PlayoutReceipts.Within("a6", step with { Ticket = Ticket(Pending().Task, Pending().Task) }, TimeSpan.Zero, silentClock, silentClock.GetTimestamp());
        await PlayoutLinkTests.Until(() => silentClock.OneShotWaiting, 2000);
        silentClock.Advance(30_000);
        Check(await silent.Final! is { IsFinal: true, Stage: "accepted", Effect: "unconfirmed — no answer from PlayMap Stage left in 30 s — it may have run; look at its state" },
              "playout receipts: a peer that never answered at all — the follow-up says so, unconfirmed");

        await ProposalOutcomes(Check, Ticket, A);
    }

    /// <summary>
    /// How an approved proposal whose line went to a peer ends (Show Bus §11: done or failed): done only once the peer said it applied
    /// it — accepted then applied among it; refused, accepted then refused and never sent are failed in their words; Patterns' Confirm,
    /// an unconfirmed answer and a peer that says nothing at all are never done: failed, the words saying it may have run.
    /// </summary>
    static async Task ProposalOutcomes(Action<bool, string> Check, Func<Task<PlayoutAnswer>, Task<PlayoutAnswer>, PlayoutTicket> ticket, Func<PeerOutcome, string, PlayoutAnswer> a)
    {
        const string Peer = "PlayMap Stage left";
        async Task<(bool Done, string Words)> Settle(Task<PlayoutAnswer> first, Task<PlayoutAnswer> final, string peer = Peer, bool silent = false)
        {
            var clock = new PlayoutLinkTests.LinkClock(1_800_000_000_000);
            var step = new PlayoutStep(null, "stage-left", peer, "play show · intro", ticket(first, final));
            var settled = PlayoutReceipts.Settled("p1", step, clock, clock.GetTimestamp());
            if (silent)
            {
                await PlayoutLinkTests.Until(() => clock.OneShotWaiting, 2000);
                clock.Advance(30_000);
            }
            return PlayoutReceipts.ProposalOutcome(await settled);
        }
        static Task<PlayoutAnswer> Never() => new TaskCompletionSource<PlayoutAnswer>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        Check(await Settle(Task.FromResult(a(PeerOutcome.Applied, "")), Task.FromResult(a(PeerOutcome.Applied, ""))) == (true, "PlayMap Stage left took PLAYLIST GO show intro"),
              "playout proposals: applied — done, in the peer's words");
        Check(await Settle(Task.FromResult(a(PeerOutcome.Accepted, "starting Intro")), Task.FromResult(a(PeerOutcome.Applied, "playing Intro")))
              == (true, "PlayMap Stage left took PLAYLIST GO show intro (playing Intro)"),
              "playout proposals: accepted, then applied — done once the peer said it ran");
        Check(await Settle(Task.FromResult(a(PeerOutcome.Accepted, "starting Intro")), Task.FromResult(a(PeerOutcome.Refused, "Show has no item intro")))
              == (false, "PlayMap Stage left refused PLAYLIST GO show intro: Show has no item intro"),
              "playout proposals: accepted, then refused — failed in the peer's words, never done");
        var confirm = await Settle(Task.FromResult(a(PeerOutcome.Accepted, "asks for a second GO — a person's")), Never(), "Patterns", silent: true);
        Check(confirm == (false, "unconfirmed — Patterns took it and gave no final answer in 30 s — it may have run; look at its state"),
              $"playout proposals: Patterns' Confirm (a second GO, a person's) is never done — unconfirmed, it may have run ({confirm.Words})");
        Check(await Settle(Task.FromResult(a(PeerOutcome.Unconfirmed, "the link dropped before Stage left answered")), Task.FromResult(a(PeerOutcome.Unconfirmed, "the link dropped before Stage left answered")))
              == (false, "unconfirmed — the link dropped before Stage left answered"),
              "playout proposals: an unconfirmed answer is never done — failed, its words saying so");
        Check(await Settle(Never(), Never(), silent: true) == (false, "unconfirmed — no answer from PlayMap Stage left in 30 s — it may have run; look at its state"),
              "playout proposals: a peer that says nothing in receipt.final.ms — never done");
        Check(await Settle(Task.FromResult(a(PeerOutcome.NotSent, "Stage left is down — not sent")), Task.FromResult(a(PeerOutcome.NotSent, "Stage left is down — not sent")))
              == (false, "not sent — Stage left is down — not sent"),
              "playout proposals: not sent — failed, nothing of it reached the peer");
        Check(PlayoutReceipts.ProposalOutcome(await PlayoutReceipts.Settled("p2", PlayoutStep.Refused("no playout link called x"), TimeProvider.System, 0)) == (false, "no playout link called x"),
              "playout proposals: refused before anything was sent — failed in Stagewatch's words");
        Check(PlayoutReceipts.ProposalOutcome(new CommandReceipt("p3", true, "accepted", "PlayMap Stage left: starting Intro", null)) == (false, "unconfirmed — PlayMap Stage left: starting Intro — it may have run"),
              "playout proposals: an accepted receipt, however it came, is never done");
    }

    // ------------------------------------------------------------------ the clips' ends and the VT

    static void ClipEnds(Action<bool, string> Check)
    {
        var ends = new PlayoutClipEnds();
        PlayoutVideo At(double position, bool ended = false, string file = "vt.mp4") => new(file, "VT", 60, position, !ended, ended, false, Transport: true);
        Check(!ends.Ended("a", At(30)) && !ends.Ended("a", At(59.5)) && ends.Ended("a", At(60, ended: true)) && !ends.Ended("a", At(60, ended: true)),
              "playout clip ends: a clip's end is said once a clip run");
        Check(!ends.Ended("a", At(10)) && ends.Ended("a", At(60, ended: true)), "playout clip ends: a clip rewound ends again");
        Check(ends.Ended("b", At(60, ended: true, file: "other.mp4")), "playout clip ends: each link's clip on its own");
        Check(ends.MayMoveOn("x1") && !ends.MayMoveOn("x1") && ends.MayMoveOn("x2") && !ends.MayMoveOn(""),
              "playout clip ends: two clip ends in one item execution move the running order once; no execution, no move");
        ends.Keep(["b"]);
        Check(ends.Ended("a", At(60, ended: true)), "playout clip ends: a link no longer running is forgotten");

        // A gap — the link stale or dropped (no clip), a playing clip gone stale, a looping clip — changes nothing it knows: the same
        // ended clip seen again after it is not a second end (no second row, no second "clip ends" trigger).
        var gap = new PlayoutClipEnds();
        var looping = new PlayoutVideo("vt.mp4", "VT", 60, 30, true, false, Loops: true, Transport: true);
        Check(gap.Ended("a", At(60, ended: true)) && !gap.Ended("a", null) && !gap.Ended("a", At(60, ended: true))
              && !gap.Ended("a", looping) && !gap.Ended("a", At(60, ended: true)),
              "playout clip ends: an ended clip seen again after the link had no clip, or none that counts down, is not a second end");
        Check(!gap.Ended("a", null) && gap.Ended("a", At(60, ended: true, file: "next.mp4")),
              "playout clip ends: another clip that ended across a gap is its own end");
        Check(!gap.Ended("a", null) && !gap.Ended("a", At(10, file: "next.mp4")) && gap.Ended("a", At(60, ended: true, file: "next.mp4")),
              "playout clip ends: a clip rewound across a gap ends again");
    }

    static void Vt(Action<bool, string> Check)
    {
        var links = new[] { L("stage-left"), L("patterns"), new PlayoutLinkSettings { Name = "foh", FollowVt = false } };
        PlayoutState S(string link, bool running) => new("x", link, 60, 1, running) { Link = link };
        var clips = new Dictionary<string, PlayoutState> { ["patterns"] = S("patterns", true), ["stage-left"] = S("stage-left", false), ["foh"] = S("foh", true) };
        var vmix = new PlayoutState("vMix", "v", 60, 1, true);
        Check(PlayoutDirector.PickVt(links, n => clips.GetValueOrDefault(n), vmix)?.Link == "patterns", "playout vt: the first running clip among the links that feed it, in Settings' order");
        clips["patterns"] = S("patterns", false);
        Check(PlayoutDirector.PickVt(links, n => clips.GetValueOrDefault(n), vmix) is { Source: "vMix" }, "playout vt: vMix's running clip before a paused one");
        Check(PlayoutDirector.PickVt(links, n => clips.GetValueOrDefault(n), null)?.Link == "stage-left", "playout vt: else the first with a clip");
        Check(PlayoutDirector.PickVt(links, _ => null, null) is null && PlayoutDirector.PickVt(links, n => n == "foh" ? clips["foh"] : null, null) is null,
              "playout vt: a link that may not feed the VT never does");
    }

    // ------------------------------------------------------------------ every reader

    static void Readers(Action<bool, string> Check)
    {
        var items = ShowContext.PlayoutItems([
            new PlayoutLinkFact("stage-left", "PlayMap Stage left", "amber", "Show: 1:23 left", ["plain", "legacy"], -2400.4, 3, ClockApart: true),
            new PlayoutLinkFact("patterns", "Patterns", "green", "VT1: 0:42 left"),
            new PlayoutLinkFact("foh", "Patterns Foh", "purple", "dialling")]);
        Check(items is [{ Id: "link:stage-left", Kind: "playout", Label: "PlayMap Stage left", Light: "amber" } a, { Id: "clock:stage-left", Kind: "health", Light: "amber" } c,
                        { Id: "link:patterns", Light: "green", State: "VT1: 0:42 left" }, { Id: "link:foh", Light: "grey" }]
              && a.State == "Show: 1:23 left · plain TCP — its wire offers no TLS · proved at level 0 — the peer does not prove itself to this link"
              && c.State == "CLOCKS APART: PlayMap Stage left's clock is 2.4 s behind (± 3 ms)" && c.Label == "Clock vs PlayMap Stage left",
              "playout readers: link:<name> in its record's light with its flags in words, clock:<name> while apart, ids from the name alone, an unknown light grey");

        var cfg = Encoding.UTF8.GetString(SnapshotJson.SerializeConfig(new RemoteConfig([], [], [], Features: ["vmix"],
            PlayoutLinks: [("stage-left", "playmap", "PlayMap Stage left"), ("patterns", "", "Patterns")])));
        Check(cfg.EndsWith("\"features\":[\"vmix\"],\"playoutLinks\":[{\"name\":\"stage-left\",\"kind\":\"playmap\",\"label\":\"PlayMap Stage left\"},{\"name\":\"patterns\",\"kind\":\"\",\"label\":\"Patterns\"}]}", StringComparison.Ordinal)
              && !Encoding.UTF8.GetString(SnapshotJson.SerializeConfig(new RemoteConfig([], [], []))).Contains("playoutLinks", StringComparison.Ordinal),
              $"playout readers: the config's playoutLinks, last, only from a host that runs them ({cfg[^120..]})");
        var frame = Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { Playout = new PlayoutView("Intro", 83, 90, true, "PlayMap", "stage-left", "pm5e6f7a") }, FrameAccess.Operator));
        Check(frame.Contains("\"playout\":{\"title\":\"Intro\",\"remaining\":83,\"duration\":90,\"running\":true,\"source\":\"PlayMap\",\"link\":\"stage-left\",\"node\":\"pm5e6f7a\"}", StringComparison.Ordinal),
              "playout readers: the frame's playout row gains link and node, last");

        var many = Enumerable.Range(1, 60).Select(i => new PlayoutPlaylistFact("pl" + i.ToString(CultureInfo.InvariantCulture), "List " + i.ToString(CultureInfo.InvariantCulture), "idle", "", "")).ToList();
        var facts = new CopilotFacts(DateTime.UtcNow, ShowClock.For("UTC"), "{}"u8.ToArray(), "{}"u8.ToArray(), [], [], [], 0, OverrunModel.Learn([]), [], [],
            Playout: [new PlayoutFact("stage-left", "playmap", "PlayMap Stage left", true, false, "amber", "Show: 1:23 left", "role remote, armed", many, "")]);
        using var doc = JsonDocument.Parse(AiContext.Copilot(facts));
        var link = doc.RootElement.GetProperty("playout_links")[0];
        Check(link.GetProperty("name").GetString() == "stage-left" && link.GetProperty("kind").GetString() == "playmap" && link.GetProperty("paired").GetBoolean()
              && !link.GetProperty("timed").GetBoolean() && link.GetProperty("playlists").GetArrayLength() == AiContext.PlayoutPlaylistsMax && AiContext.PlayoutPlaylistsMax == 50,
              "playout readers: the copilot's facts name each link, its kind, whether it is paired and timed, and at most 50 of its playlists");
    }

    static void Whoami(Action<bool, string> Check)
    {
        Check(PlayoutLink.ReadWhoami("""{"session":"s1","paired":true,"role":"remote","armed":true,"may":"*","minGoMs":0,"proto":2}""") is { Paired: true, Role: "remote", Armed: true, May: null, MayDrive: true },
              "playout whoami: \"*\" lets the link run every verb");
        Check(PlayoutLink.ReadWhoami("""{"paired":true,"role":"remote","armed":true,"may":["WHOAMI","STATE"]}""") is { MayDrive: false }
              && PlayoutLink.ReadWhoami("""{"paired":true,"role":"remote","may":["PLAYLIST GO","WHOAMI"]}""") is { MayDrive: true },
              "playout whoami: a list with no playlist GO and no clip verb lets the link only read");
        Check(PlayoutLink.ReadWhoami("nope") is null && PlayoutLink.ReadWhoami("[1]") is null && PlayoutLink.ReadWhoami("""{"may":7}""") is null && PlayoutLink.ReadWhoami("""{"role":"x"}""") is null,
              "playout whoami: an answer it cannot read is none, never an exception");
    }

    // ------------------------------------------------------------------ the link

    /// <summary>
    /// A PlayMap node on the line wire: its kind and id from its STATE, its context asked at once after the pairing, WHOAMI asked once its
    /// kind is heard, a node that lets the link only read amber with its desk's words; a playlist GO quoted by PlayMap's rule with its
    /// ticket, applied in its words; the clip's state for the VT named PlayMap with the link and the node.
    /// </summary>
    static async Task PlayMapLink(Action<bool, string> Check)
    {
        await using var desk = new PlayoutLinkTests.FakeDesk();
        Peer(desk, () => PlayMapStateJson, PlayMapContext, """{"paired":true,"role":"remote","armed":true,"may":["WHOAMI","STATE"]}""",
            body => body == "PLAYLIST GO \"Main show\" it1" ? Ok("@@") : null);
        using var link = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = "5e6f7a8b", Name = "stage-left", Kind = "playmap" });
        link.Start();
        Check(await PlayoutLinkTests.Until(() => link.Kind == PlayoutRules.PlayMap && link.Whoami is not null && link.Context is not null, 10_000),
              $"playout link: a PlayMap node's kind, context and WHOAMI heard ({link.Status})");
        var heard = desk.Heard.ToList();
        var status = heard.FindIndex(l => l == "@st STATUS");
        Check(status >= 0 && heard.IndexOf("@ctx CONTEXT") == status + 1 && heard.Contains("@wh WHOAMI"),
              "playout link: once paired it asks for its STATE and its context at once, and a PlayMap node's WHOAMI");
        Check(link.NodeId == "pm5e6f7a" && link.Peer == "PlayMap Stage left" && link.LinkName == "stage-left",
              "playout link: the node's id from its STATE, its peer read as PlayMap Stage left");
        Check(await PlayoutLinkTests.Until(() => link.Health == LinkHealth.Starting && link.Status.Contains("SHOW SET REMOTE-VIEW-ONLY OFF", StringComparison.Ordinal)),
              $"playout link: a node that lets this PC only read is amber with what its desk can do ({link.Status})");
        Check(link.Value is { Source: "PlayMap", Link: "stage-left", Node: "pm5e6f7a" }, "playout link: the clip feeding the VT names PlayMap, the link and the node");
        var now = DateTime.UtcNow;
        Check(link.WrongKind is null && link.PulledContext == link.Context
              && PlayoutLinks.Pulled([link], _ => NodeProof.Key, now) is [{ Pulled: true, Via: "stage-left", Proof: NodeProof.Key, Entry: { Id: "pm5e6f7a", Address: "127.0.0.1" } }]
              && PlayoutLinks.PulledFor([link], new NodeRef("playmap", "pm5e6f7a", "")) is { Id: "pm5e6f7a", Kind: "playmap" }
              && PlayoutLinks.PulledFor([link], new NodeRef("playmap", "9f8e7d6c", "")) is null && PlayoutLinks.PulledFor([link], new NodeRef("patterns", "pm5e6f7a", "")) is null,
              "playout link: its context speaks for the id its STATE gives and as its kind, named by the link it came over");
        Check(link.PulledEntry is { } read && ReferenceEquals(read, link.PulledEntry) && ReferenceEquals(read.Verbs, link.PulledEntry!.Verbs),
              "playout link: one context the peer sent is read once, and its table compiled once, for every reader");
        var peers = new PeerContexts(new NodeBindings());
        Check(peers.Accept(PlayMapContext.Replace("Stage left", "Posted", StringComparison.Ordinal), "10.0.0.9", now, "desk01", NodeCredential.Operator).Status == 200
              && peers.Speaking(PlayoutLinks.Pulled([link], peers.LinkedProof, now), now) is [{ Pulled: true, Via: "stage-left", Entry.Name: "Stage left" }],
              "playout link: beside the operator's post for that id, the context its own link read speaks — one per node id");
        var sent = link.Playlist("GO", "Main show", "it1", null);
        Check(sent.Ticket is { } ticket && await ticket.First is { Outcome: PeerOutcome.Applied } && await ticket.Final is { Outcome: PeerOutcome.Applied }
              && desk.Heard.Any(l => l.EndsWith(" PLAYLIST GO \"Main show\" it1", StringComparison.Ordinal) && l.StartsWith("@s", StringComparison.Ordinal)),
              "playout link: a playlist GO goes with its words quoted by PlayMap's rule, and its ticket completes in the node's answer");
        Check(link.Cue("GO", "c1").Error is { } notPatterns && notPatterns.Contains("isn't heard to be a Patterns desk", StringComparison.Ordinal)
              && !desk.Heard.Any(l => l.Contains("CUE GO", StringComparison.Ordinal)),
              "playout link: a cue verb is never sent to a PlayMap node");
        Check(!desk.Heard.Any(l => l.Contains(Token, StringComparison.OrdinalIgnoreCase) || l.StartsWith("AUTH ", StringComparison.Ordinal)),
              "playout link: the token never crosses the wire");
    }

    /// <summary>
    /// A PlayMap node whose context names another node than its STATE: the context speaks for no id — not on the network, not for a
    /// proposal's risk — however the link pairs.
    /// </summary>
    static async Task ContextOfAnother(Action<bool, string> Check)
    {
        await using var desk = new PlayoutLinkTests.FakeDesk();
        Peer(desk, () => PlayMapStateJson, PatternsContext.Replace("\"t\":\"patterns\"", "\"t\":\"playmap\"", StringComparison.Ordinal), null, _ => null);
        using var link = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = "5e6f7a8b", Name = "stage-left", Kind = "playmap" });
        link.Start();
        Check(await PlayoutLinkTests.Until(() => link.Kind == PlayoutRules.PlayMap && link.Context is not null && link.NodeId == "pm5e6f7a", 10_000)
              && link.PulledContext is null && PlayoutLinks.Pulled([link], _ => NodeProof.Key, DateTime.UtcNow).Count == 0
              && PlayoutLinks.PulledFor([link], new NodeRef("playmap", "9f8e7d6c", "")) is null && PlayoutLinks.PulledFor([link], new NodeRef("playmap", "pm5e6f7a", "")) is null,
              $"playout link: a context naming another id than the node's STATE speaks for neither ({link.Status})");
    }

    /// <summary>
    /// A PlayMap node whose context names its STATE's id as another kind (a Patterns context): it speaks for no node — a context speaks
    /// only as the kind its link reads, so a peer at the operator's address never sets the risk of another kind of node.
    /// </summary>
    static async Task ContextOfAnotherKind(Action<bool, string> Check)
    {
        await using var desk = new PlayoutLinkTests.FakeDesk();
        Peer(desk, () => PlayMapStateJson, PatternsContext.Replace("9f8e7d6c", "pm5e6f7a", StringComparison.Ordinal), null, _ => null);
        using var link = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = "5e6f7a8b", Name = "stage-left", Kind = "playmap" });
        link.Start();
        Check(await PlayoutLinkTests.Until(() => link.Kind == PlayoutRules.PlayMap && link.Context is not null && link.NodeId == "pm5e6f7a", 10_000)
              && link.WrongKind is null && link.PulledContext is null && link.PulledEntry is null && PlayoutLinks.Pulled([link], _ => NodeProof.Key, DateTime.UtcNow).Count == 0
              && PlayoutLinks.PulledFor([link], new NodeRef("patterns", "pm5e6f7a", "")) is null && PlayoutLinks.PulledFor([link], new NodeRef("playmap", "pm5e6f7a", "")) is null,
              $"playout link: a context of another kind than the node's STATE speaks for no node ({link.Status})");
    }

    /// <summary>A link set up as a PlayMap node that meets a Patterns desk: amber with both kinds named, and nothing sent to it.</summary>
    static async Task KindMismatch(Action<bool, string> Check)
    {
        await using var desk = new PlayoutLinkTests.FakeDesk();
        Peer(desk, () => """{"video":{"file":"opener.mp4","role":"program","positionMs":1000,"lengthMs":60000,"playing":true,"seq":1}}""", PatternsContext, null,
            _ => Ok("@@"));
        using var link = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = "5e6f7a8b", Name = "stage-left", Kind = "playmap" });
        link.Start();
        Check(await PlayoutLinkTests.Until(() => link.Kind == PlayoutRules.Patterns && link.Status.Contains("answers as Patterns, but its settings say PlayMap", StringComparison.Ordinal), 10_000),
              $"playout link: a peer that answers as another kind than its settings is told apart ({link.Status})");
        Check(link.Clip("PAUSE", 0).Error is { } why && why.Contains("answers as Patterns", StringComparison.Ordinal) && !desk.Heard.Any(l => l.Contains("VIDEO", StringComparison.Ordinal)),
              "playout link: and is sent nothing");
        Check(await PlayoutLinkTests.Until(() => link.Context is not null && link.Reading?.State.Video is not null, 10_000)
              && link.WrongKind is { } wrong && wrong.Contains("answers as Patterns", StringComparison.Ordinal)
              && link.Video is null && link.Value is null && link.PulledContext is null
              && PlayoutLinks.Pulled([link], _ => NodeProof.Key, DateTime.UtcNow).Count == 0 && PlayoutLinks.PulledFor([link], new NodeRef("patterns", "9f8e7d6c", "")) is null,
              "playout link: nor feeds any reader — no clip for the VT, the frame or a clip's end, and no context for the network or a risk");
    }

    /// <summary>
    /// Tickets: the first outcome and the final one. A Patterns CUE GO answered Confirm is accepted (the second GO is a person's, never
    /// sent), its final still to come; a command the desk never answers is unconfirmed once the link drops; a stopped link hands every
    /// waiting ticket an outcome.
    /// </summary>
    static async Task Tickets(Action<bool, string> Check)
    {
        await using var desk = new PlayoutLinkTests.FakeDesk();
        Peer(desk, () => """{"video":null,"cuestack":{"armed":true,"hold":false,"listRev":"7","standby":{"id":"c1","number":"12","name":"Walk-in","requireConfirm":true}}}""",
            PatternsContext, null,
            body => body == "CUE GO c1" ? Ok("@@", """{"outcome":"Confirm","confirm":"press GO again within 4 s"}""") : body == "CUE STANDBY c2" ? null : Err("@@", "no"));
        var link = new PlayoutLink(new PlayoutLinkOptions { Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = "6a7b8c9d", Name = "patterns" });
        PlayoutTicket? go = null;
        try
        {
            link.Start();
            Check(await PlayoutLinkTests.Until(() => link.Kind == PlayoutRules.Patterns && link.Reading is { State.Cuestack.StandbyId: "c1" }, 10_000),
                  $"playout tickets: a Patterns desk heard, its cue stack read ({link.Status})");
            go = link.Cue("GO", "c1").Ticket!;
            var first = await go.First;
            Check(first is { Outcome: PeerOutcome.Accepted } && first.Words.Contains("second GO", StringComparison.Ordinal) && !go.Final.IsCompleted
                  && desk.Heard.Count(l => l.EndsWith(" CUE GO c1", StringComparison.Ordinal)) == 1,
                  $"playout tickets: Confirm is accepted, the second GO a person's — sent once, its final still to come ({first.Words})");
            var standby = link.Cue("STANDBY", "c2").Ticket!;
            await PlayoutLinkTests.Until(() => desk.Heard.Any(l => l.EndsWith(" CUE STANDBY c2", StringComparison.Ordinal)));
            desk.Closing = true;
            desk.Drop();
            Check(await standby.First.WaitAsync(TimeSpan.FromSeconds(20)) is { Outcome: PeerOutcome.Unconfirmed } && standby.Final.IsCompleted,
                  "playout tickets: a written command the desk never answered is unconfirmed once the link drops — it may have run");
        }
        finally
        {
            link.Dispose();
        }
        Check(go is { Final.IsCompleted: true } && go.Final.Result is { Outcome: PeerOutcome.Unconfirmed, Before: PeerOutcome.Accepted } stopped
              && stopped.Words.StartsWith("the link was stopped", StringComparison.Ordinal),
              "playout tickets: a stopped link hands a ticket still waiting on its final answer an unconfirmed one, so no waiter waits on nothing");
    }

    // ------------------------------------------------------------------ the director over real links

    /// <summary>
    /// The director over a PlayMap node and a Patterns desk, as the desk and the rig run it: which link a line reaches, the peer's own
    /// refusals and answers in the receipt, the show log's rows at the link's time against the item asked for, the effect seen in STATE,
    /// and a refusal that sends nothing.
    /// </summary>
    static async Task Director(Action<bool, string> Check)
    {
        await using var playmap = new PlayoutLinkTests.FakeDesk();
        var pmState = PlayMapStateJson.Replace("\"state\":\"playing\"", "\"state\":\"idle\"", StringComparison.Ordinal);
        // A GO on it3 is answered as a node that pushes its new STATE ahead of its answer does: the STATE first, then OK.
        var it3 = PlayMapStateJson.Replace("\"item\":{\"id\":\"it1\",\"label\":\"Intro\"}", "\"item\":{\"id\":\"it3\",\"label\":\"Outro\"}", StringComparison.Ordinal);
        Peer(playmap, () => pmState, PlayMapContext, """{"paired":true,"role":"remote","armed":false,"may":"*"}""", body => body switch
        {
            "PLAYLIST GO pl1 it9" => Err("@@", "Main show has no item it9"),
            "PLAYLIST GO pl1 it1" or "PLAYLIST GO pl1 it2" => Ok("@@"),
            "PLAYLIST GO pl1 it3" => "STATE " + (pmState = it3) + "\n" + Ok("@@"),
            _ => null,
        });
        await using var patterns = new PlayoutLinkTests.FakeDesk();
        Peer(patterns, () => """{"video":{"file":"opener.mp4","role":"playlist","key":"pl:3","positionMs":1000,"lengthMs":60000,"playing":true,"seq":1}}""", PatternsContext, null,
            body => body == "VIDEO END 10" ? Ok("@@", "jumped to the last 10 s") : null);
        var settings = new List<PlayoutLinkSettings> { L("stage-left", "playmap"), L("patterns") };
        settings[0].Port = playmap.Port;
        settings[1].Port = patterns.Port;
        var rows = new ConcurrentQueue<(DateTimeOffset At, int Mark, string Row)>();
        var lines = new ConcurrentQueue<string>();
        var links = new PlayoutLinks();
        var director = new PlayoutDirector(links, () => settings, () => true, () => 300, (at, mark, row) => rows.Enqueue((at, mark, row)), lines.Enqueue);
        foreach (var s in settings)
            links.Set(s.Name, PlayoutLinks.Make(s, Token, "7b8c9d0e", TimeProvider.System, null, "", director.Wire));
        try
        {
            Check(await PlayoutLinkTests.Until(() => links["stage-left"]?.Kind == PlayoutRules.PlayMap && links["patterns"]?.Kind == PlayoutRules.Patterns, 10_000),
                  "playout director: both links paired and their kinds heard");
            var refused = director.Playout(new PlayoutArgs("nope", "go", "show", "it1", false), 0, 3, null);
            Check(refused is { Error: "no playout link called nope — links: stage-left, patterns", Ticket: null }, $"playout director: a link it does not have is refused by name ({refused.Error})");
            var two = director.Playout(new PlayoutArgs("", "go", "show", "it1", false), 0, 3, null);
            Check(two.Error == "two playout links — name one: playout stage-left go …", "playout director: go never guesses between two links");

            var go = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it1", false), 0, 3, null);
            var receipt = await PlayoutReceipts.Within("a1", go, TimeSpan.FromSeconds(3), TimeProvider.System, TimeProvider.System.GetTimestamp());
            Check(receipt is { Ok: true, Stage: "applied", Effect: "PlayMap Stage left took PLAYLIST GO pl1 it1", Final: null } && go is { Link: "stage-left", Asked: "play Main show · it1" },
                  $"playout director: a GO's receipt is the node's applied, in Stagewatch's words where it gave none ({receipt.Effect})");
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 3 && r.Row == "PlayMap Stage left did it: PLAYLIST GO pl1 it1")),
                  "playout director: the show log's row for the outcome, against the item it was asked for");
            pmState = PlayMapStateJson;
            playmap.Say("STATE " + pmState);
            Check(await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 3 && r.Row == "seen: Main show plays it1 on PlayMap Stage left"), 6000),
                  "playout director: the effect seen in the node's STATE gets its row");
            var unseen = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it2", false), 0, 4, null);
            // The node already plays it1 and sends no STATE after this GO: the STATE it read before the answer proves nothing of it.
            var stale = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it1", false), 0, 5, null);
            Check(unseen is { Error: null } && await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 4 && r.Row == "not seen in PlayMap Stage left's STATE within 3.3 s: Main show plays it2"), 8000),
                  "playout director: an effect its STATE never shows is said not seen, the lead and 3 s on — taken is not seen");
            Check(stale is { Error: null, Ticket: not null } && await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 5 && r.Row == "not seen in PlayMap Stage left's STATE within 3.3 s: Main show plays it1"), 8000)
                  && !rows.Any(r => r.Mark == 5 && r.Row.StartsWith("seen:", StringComparison.Ordinal)),
                  "playout director: a STATE read before the peer's answer that already shows the effect is not seen — only one read after it counts");
            await PlayoutLinkTests.Until(() => director.Watching("stage-left") == 0, 4000);
            var ahead = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it3", false), 0, 6, null);
            Check(ahead is { Error: null } && it3.Contains("\"id\":\"it3\"", StringComparison.Ordinal)
                  && await PlayoutLinkTests.Until(() => rows.Any(r => r.Mark == 6 && r.Row == "seen: Main show plays it3 on PlayMap Stage left"), 6000),
                  "playout director: a STATE the node pushed ahead of its answer, read after the command went, shows the effect — seen");
            await PlayoutLinkTests.Until(() => director.Watching("stage-left") == 0, 4000);

            // Bounded: at most WatchesMax effects looked for at once on one link; one past it still goes, its sender told it is not looked for.
            var steps = new List<PlayoutStep>();
            for (var i = 0; i <= PlayoutDirector.WatchesMax; i++)
            {
                var s = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it2", false), 0, 100 + i, null);
                steps.Add(s);
                if (s.Ticket is { } t) await t.First;
            }
            var past = steps[^1];
            Check(steps.All(x => x.Error is null && x.Ticket is not null) && steps.Take(PlayoutDirector.WatchesMax).All(x => x.Note.Length == 0)
                  && past.Note == "its effect is not looked for in PlayMap Stage left's STATE: 32 are looked for on it already"
                  && director.Watching("stage-left") == PlayoutDirector.WatchesMax && PlayoutDirector.WatchesMax == PeerLink.InflightMax,
                  $"playout director: at most link.inflight.max effects are looked for at once on a link; the next command still goes, and says so ({past.Note})");
            Check(await PlayoutLinkTests.Until(() => director.Watching("stage-left") == 0, 8000)
                  && rows.Count(r => r.Mark >= 100 && r.Row.StartsWith("not seen in ", StringComparison.Ordinal)) == PlayoutDirector.WatchesMax
                  && !rows.Any(r => r.Mark == 100 + PlayoutDirector.WatchesMax && r.Row.Contains("seen", StringComparison.Ordinal)),
                  "playout director: each look ends with its one row and gives its place back; the one not looked for has none");
            var nine = director.Playout(new PlayoutArgs("stage-left", "go", "pl1", "it9", false), 0, -1, null);
            var r9 = await PlayoutReceipts.Within("a2", nine, TimeSpan.FromSeconds(3), TimeProvider.System, TimeProvider.System.GetTimestamp());
            Check(r9 is { Ok: false, Error: "PlayMap Stage left refused PLAYLIST GO pl1 it9: Main show has no item it9" }, $"playout director: refused in the node's words ({r9.Error})");

            // G48 through the director: patterns pause on a playlist item is refused in Patterns' words, and the desk hears nothing.
            Check(await PlayoutLinkTests.Until(() => links["patterns"]?.Reading?.State.Video is { Role: "playlist" }), "playout director: the Patterns desk's clip is a playlist item");
            var pause = director.Patterns("pause", 0, -1, null);
            Check(pause.Error is { } p && p.StartsWith("Patterns: 'opener.mp4' is a playlist item", StringComparison.Ordinal) && !patterns.Heard.Any(l => l.Contains("VIDEO PAUSE", StringComparison.Ordinal)),
                  $"playout director: patterns pause on a playlist item is refused in Patterns' words, and nothing is sent ({pause.Error})");
            var end = director.Patterns("end", 10, -1, null);
            Check(await PlayoutReceipts.Within("a3", end, TimeSpan.FromSeconds(3), TimeProvider.System, TimeProvider.System.GetTimestamp()) is { Ok: true, Effect: "Patterns took VIDEO END 10 (jumped to the last 10 s)" },
                  "playout director: patterns end reaches link patterns, its receipt in the desk's words");
            Check(!playmap.Heard.Concat(patterns.Heard).Any(l => l.Contains(Token, StringComparison.OrdinalIgnoreCase)), "playout director: neither peer saw the token");
        }
        finally
        {
            foreach (var l in links.All) l.Dispose();
        }
    }
}
