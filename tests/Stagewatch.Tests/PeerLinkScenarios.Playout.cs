using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

/// <summary>
/// The PeerLink kit's scenarios S1, S11, S13, S14, S19 and S22 (Show Bus 13.20) against PlayoutLink, the desk's link to Patterns, as it
/// is on the core today: a level-0 connector (13.6.8) that offers no LINK. The scripted peer is a level-0 acceptor with Patterns 88.1's grammar —
/// it greets with STATE, then a NONCE of 32 lowercase hex, answers LINK <c>unknown command</c>, takes <c>AUTH-PROOF</c> for the code it
/// holds, and offers no feature — on a real socket, and PlayoutLink runs on the injected clock of
/// <see cref="PlayoutLinkTests.LinkClock"/>, its wall time starting at <see cref="T0"/>. What 13.20 asks of a level-1 link (LINK's
/// proofs, a verdict's features, RECEIPTS, a final receipt) is not this link's until it offers LINK (SW23.1); each scenario says what it
/// holds at level 0 and that the link asks for none of it.
/// </summary>
static partial class PeerLinkScenarios
{
    /// <summary>Every PlayoutLink the scenarios made, for the look at what each one published.</summary>
    static readonly List<PlayoutLink> PlayoutLinks = [];

    /// <summary>Every status line and record PlayoutLink published in these scenarios, and each command's words.</summary>
    static readonly ConcurrentQueue<string> PlayoutSaid = new();

    /// <summary>Every row PlayoutLink gave the show log in these scenarios — its changes of state, its outcomes, its lines — as the desk journals them.</summary>
    static readonly ConcurrentQueue<string> PlayoutRows = new();

    /// <summary>An inst of its own, so each scenario's request ids count from 1 in the process's counter.</summary>
    static string FreshInst() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// PlayoutLink to the peer at <paramref name="port"/>, holding the code typed with spaces, on <paramref name="clock"/>; a link to a
    /// desk on another machine when <paramref name="local"/> is false (the seam answers false for loopback).
    /// </summary>
    static PlayoutLink PlayoutTo(int port, PlayoutLinkTests.LinkClock clock, string inst, bool local = true, Func<double>? jitter = null)
        => Playout(PlayoutLinkTests.Link(port, CodeTyped, local ? null : PlayoutLinkTests.Remote, clock, inst, jitter ?? (() => 0.5)));

    /// <summary><paramref name="link"/>, kept for the canary look, with everything it says to its listeners captured as the desk takes it.</summary>
    static PlayoutLink Playout(PlayoutLink link)
    {
        link.Answered += a =>
        {
            PlayoutSaid.Enqueue(a.StatusLine(link.Name));
            if (a.JournalLine(link.Name) is { } row) PlayoutRows.Enqueue(row);
        };
        link.Changed += c => PlayoutRows.Enqueue(c.Line(link.Name));
        link.Logged += PlayoutRows.Enqueue;
        PlayoutLinks.Add(link);
        return link;
    }

    /// <summary>What a reader of <paramref name="link"/> takes now: its status line and its record, kept for the canary look.</summary>
    static PeerLinkRecord Seen(PlayoutLink link)
    {
        var record = link.RecordNow();
        PlayoutSaid.Enqueue(link.Status);
        PlayoutSaid.Enqueue(record.ToJson());
        return record;
    }

    /// <summary>The link paired with the peer and read the STATE it pushes once paired: everything since is taken.</summary>
    static Task<bool> PlayoutUp(PlayoutLink link)
        => PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.Status.Contains("no clip on air", StringComparison.Ordinal));

    /// <summary>The request id PlayoutLink's <paramref name="n"/>th command under <paramref name="inst"/> carries (13.12.1).</summary>
    static string PlayoutId(int n, string inst) => PeerLink.RequestId(PlayoutLink.IdPrefix, n, inst);

    /// <summary>The commands the peer ran under PlayoutLink's own request ids (its STATUS and CONTEXT asks carry ids of their own).</summary>
    static List<JsonElement> Ran(ScriptedPeer peer, string inst)
        => peer.Events.Where(e => Ev(e, "command") && (Str(e, "id") ?? "").EndsWith("-" + inst, StringComparison.Ordinal)).ToList();

    /// <summary>The connection the peer paired, the <paramref name="nth"/> one from 1.</summary>
    static async Task<int> PairedOn(ScriptedPeer peer, int nth = 1)
    {
        for (var i = 0; i < 100; i++)
        {
            var paired = peer.Events.Where(e => Ev(e, "paired")).ToList();
            if (paired.Count >= nth) return Int(paired[nth - 1], "conn") ?? -1;
            await Task.Delay(20);
        }
        return -1;
    }

    /// <summary>Moves the clock on by <paramref name="ms"/>, then lets what the link wrote at that moment reach the peer and its answer come back.</summary>
    static async Task Step(PlayoutLinkTests.LinkClock clock, long ms)
    {
        clock.Advance(ms);
        await Task.Delay(20);
    }

    static async Task Playout(Action<bool, string> Check, string script, Material kit)
    {
        await PlayoutS1(Check, script, kit);
        await PlayoutS11(Check, script, kit);
        await PlayoutS13(Check, script, kit);
        await PlayoutS14(Check, script, kit);
        await PlayoutS22(Check, script, kit);
        await PlayoutS19(Check, script, kit);
    }

    // ------------------------------------------------------------------ S1 at level 0

    /// <summary>
    /// S1 on PlayoutLink: the honest link at level 0. It greets with HELLO and offers no LINK; the peer's greeting STATE is set aside and
    /// counted; the 32-hex NONCE is answered with one AUTH-PROOF and never plain AUTH, and the code never crosses the wire; the link is
    /// live and its record says what it is — legacy, level 0, amber, local or plain. A command runs once at the peer under its request
    /// id <c>s1-&lt;inst&gt;</c> and reads applied, and a pong that answers the link's ping is a clock sample that schedules a timed
    /// command on this machine.
    /// </summary>
    static async Task PlayoutS1(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var local in new[] { true, false })
        {
            var name = "S1 PlayoutLink " + (local ? "on this machine" : "off this machine");
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            var inst = FreshInst();
            using var link = PlayoutTo(port, clock, inst, local);
            link.Start();
            var up = await PlayoutUp(link);
            var conn = await PairedOn(peer);
            var paired = peer.Events.FirstOrDefault(e => Ev(e, "paired"));
            var read = peer.ReadOn(conn);
            var rec = Seen(link);
            Check(up && rec is { State: "live", Light: "amber", Proof: "legacy", Level: 0, Kid: "code", PeerName: "Patterns", Carrier: "line" }
                  && rec.Tls == (local ? "local" : "none") && rec.Flags.Contains("legacy") && rec.Flags.Contains("plain") != local && rec.Counts.Malformed >= 1
                  && link.SetAsideBeforePairing >= 1 && Str(paired, "how") == "auth-proof",
                $"peerlink scenarios: {name} — the level-0 link is live: legacy, level 0, amber, {(local ? "local" : "plain")}; the greeting STATE before OK paired set aside and counted ({rec.ToJson()}) {peer.Faults}");
            Check(read.Count >= 2 && read[0] == "HELLO Stagewatch" && read.Count(l => l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal)) == 1
                  && !read.Any(l => l.StartsWith("LINK", StringComparison.Ordinal) || l.StartsWith("AUTH ", StringComparison.Ordinal) || l.Contains(" AUTH ", StringComparison.Ordinal)),
                $"peerlink scenarios: {name} — HELLO, no LINK, one AUTH-PROOF for the 32-hex NONCE and never plain AUTH ({string.Join(" | ", read)})");

            var written = link.Restart();
            var id = PlayoutId(1, inst);
            var applied = await PlayoutLinkTests.Until(() => link.OutcomeOf(id) == PeerOutcome.Applied);
            await peer.Wait(e => Ev(e, "command") && Str(e, "id") == id);
            var ran = Ran(peer, inst);
            Check(written is null && applied && ran.Count == 1 && Str(ran[0], "id") == id && Str(ran[0], "line") == "VIDEO RESTART" && link.InFlight == 0,
                $"peerlink scenarios: {name} — a command with its request id ({id}) runs once at the peer and reads applied ({link.OutcomeOf(id)}) {peer.Faults}");

            // The first evaluator run after the pairing pings, its t1 the link's wall clock (T0 + 250); the peer stamps the same time.
            await Set(peer, new { clock = T0 + 250 });
            clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
            var sampled = await PlayoutLinkTests.Until(() => link.RecordNow().Samples == 1);
            var pinged = peer.AllRead().Contains("PING " + (T0 + 250).ToString(CultureInfo.InvariantCulture));
            rec = Seen(link);
            Check(sampled && pinged && link.Clock is { RttMs: 0, ErrMs: 1 } b && Math.Abs(b.OffsetMs) < 0.001 && rec.Counts.Unmatched == 0
                  && link.CanSchedule(6) == local && !link.ClockApart,
                $"peerlink scenarios: {name} — a pong that answers the link's ping is a clock sample (rtt 0, errMs 1, offset 0){(local ? ", fit for a timed command on this machine" : "; off this machine a plain link's clock never schedules")} ({rec.ToJson()})");
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Code, CodeTyped)) is null, $"peerlink scenarios: {name} — no code or K on the peer's stdout");
        }
    }

    // ------------------------------------------------------------------ S11 at level 0

    /// <summary>
    /// S11 on PlayoutLink: silence. Its evaluator runs on its own timer; the peer answers nothing — no pong, no answer to its asks. Live
    /// at link.stale.ms, stale and amber past it and at link.drop.ms, down past it: the link closes the socket and waits 13.15.1's first
    /// wait; with nothing listening each redial waits 13.15.1's next wait (j injected), never sooner. Its own 9 s pause is the self-gap:
    /// it pings at once and closes only link.selfgap.ms later, and a ping answered in that time keeps the link.
    /// </summary>
    static async Task PlayoutS11(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "S11 PlayoutLink";
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            await Set(peer, new { pongs = false, answer = "silent" });
            var clock = new PlayoutLinkTests.LinkClock(T0);
            var js = new Queue<double>([0, 0.999, 0.5, 0.25]);
            using var link = PlayoutTo(port, clock, FreshInst(), jitter: () => js.Count > 0 ? js.Dequeue() : 0.5);
            link.Start();
            var up = await PlayoutUp(link);
            var conn = await PairedOn(peer);
            // The evaluator's timer runs every 250 ms; each step moves the clock on and lets one run.
            var seen = new Dictionary<long, string>();
            for (var t = 1000L; t <= 6000; t += 1000) await Step(clock, 1000);
            seen[6000] = Seen(link).State;
            await Step(clock, 1);
            var at6001 = Seen(link);
            seen[6001] = at6001.State + " " + at6001.Light;
            await Step(clock, 999);
            var stale = (link.Health, link.Status, link.Video);
            for (var t = 8000L; t <= 11_000; t += 1000) await Step(clock, 1000);
            await Step(clock, 751);   // the evaluator at 11 751 ms, so its next run is at 12 001
            await Step(clock, 249);
            seen[12_000] = Seen(link).State;
            await Step(clock, 1);
            var closed = await PlayoutLinkTests.Until(() => !link.Connected);
            var rec = Seen(link);
            var bye = await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == conn);
            Check(up && seen[6000] == "live" && seen[6001] == "stale amber" && seen[12_000] == "stale" && closed
                  && rec is { State: "down", Light: "red", Attempt: 1 } && rec.NextMs == PeerLink.RedialWait(0, 0) && bye is { } y && Str(y, "by") == "other",
                $"peerlink scenarios: {Name} — silence: live at link.stale.ms, stale and amber past it and at link.drop.ms, down past it; the link closes the socket and waits 13.15.1's first wait ({rec.ToJson()}) {peer.Faults}");
            Check(stale.Health == LinkHealth.Starting && stale.Status.Contains(" quiet for 7 s — no countdown until it is heard again", StringComparison.Ordinal) && stale.Video is null
                  && link.Status.EndsWith("dialling again in 0.4 s", StringComparison.Ordinal),
                $"peerlink scenarios: {Name} — the desk's words follow the record: stale is amber and shows no countdown, down says when it dials again ({stale.Status}; {link.Status})");

            // Nothing answers now: each failed dial waits 13.15.1's next wait, j injected — 1200 (n 1, j 0.999), 2000, 3600.
            await peer.Ask(new { @do = "close", end = "a" });
            var waits = new List<string>();
            var ok = await PlayoutLinkTests.Until(() => clock.OneShotWaiting);
            foreach (var (n, j) in new[] { (1, 0.999), (2, 0.5), (3, 0.25) })
            {
                var wait = link.RecordNow().NextMs ?? -1;
                await Step(clock, wait - 1);
                await Task.Delay(80);
                ok &= link.RecordNow().Attempt == n;
                await Step(clock, 1);
                ok &= await PlayoutLinkTests.Until(() => link.RecordNow().Attempt == n + 1 && clock.OneShotWaiting);
                var want = PeerLink.RedialWait(n, j);
                ok &= link.RecordNow().NextMs == want;
                waits.Add((link.RecordNow().NextMs ?? -1).ToString(CultureInfo.InvariantCulture));
            }
            Check(ok && waits.SequenceEqual(["1200", "2000", "3600"]),
                $"peerlink scenarios: {Name} — each redial waits 13.15.1's wait, within ±20 % of its base, and dials no sooner ({string.Join(", ", waits)})");
        }
        {
            // The self-gap: the evaluator itself paused 9 s. It pings at once and moves the link to down only link.selfgap.ms later.
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name + ", the self-gap", ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            await Set(peer, new { pongs = false, answer = "silent" });
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var g = PlayoutTo(port, clock, FreshInst());
            g.Start();
            await PlayoutUp(g);
            for (var t = 1000L; t <= 5000; t += 1000) await Step(clock, 1000);
            await Step(clock, 9000);
            var ping = "PING " + (T0 + 14_000).ToString(CultureInfo.InvariantCulture);
            var pinged = await peer.Wait(e => Ev(e, "read") && Str(e, "line") == ping);
            var during = Seen(g).State;
            await Step(clock, 1750);   // the evaluator at 15 750 ms, so its next run is at 16 000
            await Step(clock, 249);
            var still = (Seen(g).State, g.Connected);
            await Step(clock, 1);
            var gone = await PlayoutLinkTests.Until(() => !g.Connected);
            Check(pinged is not null && during == "stale" && still == ("stale", true) && gone && Seen(g).State == "down",
                $"peerlink scenarios: {Name} — the self-gap: after its own 9 s pause the evaluator pings at once, shows stale past link.drop.ms, and closes only link.selfgap.ms later, the peer still silent ({still})");
            g.Dispose();   // gone before the next link dials the same peer on the same clock

            // The same pause, the peer answering the ping it sends then: heard, so live, and never down for the link's own pause.
            using var h = PlayoutTo(port, clock, FreshInst());
            h.Start();
            await PlayoutUp(h);
            for (var t = 1000L; t <= 5000; t += 1000) await Step(clock, 1000);
            await Set(peer, new { pongs = true });
            await Step(clock, 9000);
            var heard = await PlayoutLinkTests.Until(() => h.RecordNow().State == "live");
            await Step(clock, 2000);
            var kept = Seen(h);
            Check(heard && h.Connected && kept.State == "live",
                $"peerlink scenarios: {Name} — the self-gap's ping answered: the link is heard, live, and not closed for its own pause ({kept.ToJson()})");
        }
    }

    // ------------------------------------------------------------------ S13 at level 0

    /// <summary>
    /// S13 on PlayoutLink: a pong that answers a t1 the link never sent, one sent unasked and one whose t3 comes before its t2 are no
    /// clock sample, the first two counted unmatched; a lying clock, matched, is believed and shown CLOCKS APART; and a plain link off
    /// this machine shows its clock but never schedules a timed command by it.
    /// </summary>
    static async Task PlayoutS13(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "S13 PlayoutLink";
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = PlayoutTo(port, clock, FreshInst());
            link.Start();
            await PlayoutUp(link);
            var conn = await PairedOn(peer);
            await Set(peer, new { pongs = "unmatched" });
            await Step(clock, 250);
            var unmatched = await PlayoutLinkTests.Until(() => link.RecordNow().Counts.Unmatched == 1);
            var forgedT1 = (T0 + 240).ToString(CultureInfo.InvariantCulture);
            var forgedT2 = (T0 + 245).ToString(CultureInfo.InvariantCulture);
            await peer.Ask(new { @do = "send", conn, line = $"OK PONG {forgedT1} {forgedT2} {forgedT2}" });
            var forged = await PlayoutLinkTests.Until(() => link.RecordNow().Counts.Unmatched == 2);
            await Set(peer, new { pongs = "backwards" });
            await Step(clock, 2000);
            var backwards = await PlayoutLinkTests.Until(() => link.RecordNow().HeardMs == 0);
            var rec = Seen(link);
            Check(unmatched && forged && backwards && rec.Samples == 0 && rec.Counts.Unmatched == 2 && rec.State == "live" && link.Clock is null && link.ClockOffset is null,
                $"peerlink scenarios: {Name} — an unmatched pong, a forged one sent unasked and one with t3 before t2 are no clock sample; the first two counted unmatched, the link live ({rec.ToJson()}) {peer.Faults}");
            // A lying clock: matched and admitted, it is believed — and shown CLOCKS APART.
            await Set(peer, new { pongs = true, clock = T0 + 4250 + 5_000_000 });
            await Step(clock, 2000);
            var lied = await PlayoutLinkTests.Until(() => link.RecordNow().Samples == 1);
            await Step(clock, 250);
            rec = Seen(link);
            Check(lied && rec.Apart && rec.Flags.Contains("apart") && rec.Light == "amber" && link.ClockApart && link.ClockOffset is { } off && Math.Abs(off.TotalMilliseconds - 5_000_000) < 1,
                $"peerlink scenarios: {Name} — a clock 5000 s off is believed as measured and shown CLOCKS APART, amber ({rec.ToJson()})");
        }
        {
            // A plain link off this machine: its clock is shown, never used to schedule a timed command (13.11.6); on this machine the
            // same sample schedules (S1).
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name + ", plain", ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = PlayoutTo(port, clock, FreshInst(), local: false);
            link.Start();
            var up = await PlayoutUp(link);
            await Step(clock, 250);
            var sampled = await PlayoutLinkTests.Until(() => link.RecordNow().Samples == 1);
            var rec = Seen(link);
            Check(up && sampled && rec is { Tls: "none", Light: "amber" } && rec.Flags.Contains("plain") && link.Clock is { ErrMs: <= 6 } && !link.CanSchedule(6),
                $"peerlink scenarios: {Name}, plain — a clock measured over a plain link off this machine, however fit, never schedules a timed command ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S14 at level 0

    /// <summary>
    /// S14 on PlayoutLink: each command's outcome read by 13.12.3 — applied; accepted, which a level-0 peer never follows with a final
    /// receipt, so it is not waited on; refused with the peer's words; applied late, counted; a drop after the write unconfirmed, and
    /// never asked about again (no RECEIPTS at level 0), run once; with no answer, 32 commands wait, the 33rd is not sent, and each is
    /// unconfirmed past receipt.final.ms (an answer at that limit would still count).
    /// </summary>
    static async Task PlayoutS14(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "S14 PlayoutLink";
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, Name, ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
        var clock = new PlayoutLinkTests.LinkClock(T0);
        var inst = FreshInst();
        using var link = PlayoutTo(port, clock, inst);
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        link.Start();
        var up = await PlayoutUp(link);
        PlayoutAnswer? Last(string id) => answers.LastOrDefault(a => a.Id == id) is { Id: not null } a ? a : null;

        link.Restart();
        var applied = PlayoutId(1, inst);
        var a1 = await PlayoutLinkTests.Until(() => link.OutcomeOf(applied) == PeerOutcome.Applied);
        await Set(peer, new { answer = "accepted" });
        link.EndIn(10);
        var accepted = PlayoutId(2, inst);
        var a2 = await PlayoutLinkTests.Until(() => link.OutcomeOf(accepted) == PeerOutcome.Accepted);
        var notWaited = link.InFlight == 0;
        await Set(peer, new { answer = "refused" });
        link.Restart();
        var refused = PlayoutId(3, inst);
        var a3 = await PlayoutLinkTests.Until(() => link.OutcomeOf(refused) == PeerOutcome.Refused && Last(refused) is not null);
        await Set(peer, new { answer = "applied", late = 1500 });
        link.EndIn(9);
        var late = PlayoutId(4, inst);
        var a4 = await PlayoutLinkTests.Until(() => link.OutcomeOf(late) == PeerOutcome.Applied);
        await peer.Wait(e => Ev(e, "command") && Str(e, "id") == late);
        var rec = Seen(link);
        Check(up && a1 && a2 && notWaited && a3 && Last(refused) is { Words: "the scripted peer refuses it", Line: "VIDEO RESTART" } && a4 && rec.Counts.Late == 1
              && Ran(peer, inst).Count == 4,
            $"peerlink scenarios: {Name} — receipts read by 13.12.3: applied; accepted, never followed at level 0 and so not waited on; refused with its words; applied late, counted ({rec.ToJson()}) {peer.Faults}");

        // A drop after the write: unconfirmed. Relinked, the link asks nothing about it (a level-0 peer keeps no receipts), and it ran once.
        await Set(peer, new { late = 0, answer = "drop" });
        link.Restart();
        var dropped = PlayoutId(5, inst);
        var a5 = await PlayoutLinkTests.Until(() => link.OutcomeOf(dropped) == PeerOutcome.Unconfirmed && !link.Connected && clock.OneShotWaiting);
        var droppedWords = Last(dropped)?.Words;
        await Set(peer, new { answer = "applied" });
        await Step(clock, link.RecordNow().NextMs ?? 0);
        var back = await PlayoutUp(link);
        await Settle(peer);
        Check(a5 && droppedWords == "the link dropped before Patterns answered" && back && link.OutcomeOf(dropped) == PeerOutcome.Unconfirmed
              && Ran(peer, inst).Count(e => Str(e, "id") == dropped) == 1 && !peer.AllRead().Any(l => l.Contains("RECEIPTS", StringComparison.Ordinal)),
            $"peerlink scenarios: {Name} — a drop after the write is unconfirmed, and stays so once relinked: a level-0 link asks no RECEIPTS; it ran once ({droppedWords}) {peer.Faults}");

        // Silent: 32 commands wait, the 33rd is not sent; with no answer each waits through receipt.final.ms and is unconfirmed past it.
        // The peer answers the link's pings, so the link stays live the whole time.
        await Set(peer, new { answer = "silent" });
        var ids = new List<string>();
        var allSent = true;
        for (var i = 0; i < PeerLink.InflightMax; i++)
        {
            allSent &= link.EndIn(10) is null;
            ids.Add(PlayoutId(6 + i, inst));
        }
        var waiting = link.InFlight;
        var thirtyThird = link.EndIn(10);
        var all = await peer.Wait(e => Ev(e, "command") && Str(e, "id") == ids[^1]);
        for (var t = 1000L; t <= PeerLink.ReceiptFinalMs; t += 1000)
        {
            await Step(clock, 1000);
            await PlayoutLinkTests.Until(() => link.RecordNow().HeardMs <= 1000, 2000);
        }
        var stillWaits = link.OutcomeOf(ids[0]) is null && link.InFlight == PeerLink.InflightMax && link.Connected;
        await Step(clock, 1);
        var past = ids.All(id => link.OutcomeOf(id) == PeerOutcome.Unconfirmed);
        // The evaluator's next run says each outcome to the desk.
        await Step(clock, PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
        var unconfirmed = await PlayoutLinkTests.Until(() => ids.All(id => Last(id) is { Outcome: PeerOutcome.Unconfirmed, Words: "no answer in 30 s — it may have run" }));
        rec = Seen(link);
        Check(allSent && waiting == PeerLink.InflightMax && thirtyThird == "32 commands are waiting on Patterns's answers" && all is not null && stillWaits && past && unconfirmed
              && link.InFlight == 0 && rec.State == "live" && Ran(peer, inst).Count == 5 + PeerLink.InflightMax
              && link.OutcomeOf(accepted) == PeerOutcome.Accepted && !peer.AllRead().Any(l => l.Contains("RECEIPTS", StringComparison.Ordinal)),
            $"peerlink scenarios: {Name} — 32 commands wait, the 33rd is not sent ({thirtyThird}); with no answer each waits through receipt.final.ms and is unconfirmed past it, the link live ({rec.ToJson()})");
    }

    // ------------------------------------------------------------------ S22 at level 0

    /// <summary>
    /// S22 on PlayoutLink: a level-0 acceptor with Patterns 88.1's real grammar — STATE, then NONCE. Off this machine: legacy and amber,
    /// one AUTH-PROOF for its 32-hex NONCE, never plain AUTH, the greeting STATE set aside and counted and no clip read from it until OK
    /// paired. A NONCE that is not 32 lowercase hex — capitals, or 31 digits — on this machine or off it, is never proved: no AUTH-PROOF,
    /// no AUTH (a desk that sent a NONCE is never sent the token, even on this PC), nothing taken, the link amber saying why; and with no
    /// pairing within peerlink.handshake.ms the link ends the connection.
    /// </summary>
    static async Task PlayoutS22(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "S22 PlayoutLink";
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = PlayoutTo(port, clock, FreshInst(), local: false);
            link.Start();
            var up = await PlayoutUp(link);
            var conn = await PairedOn(peer);
            var read = peer.ReadOn(conn);
            var rec = Seen(link);
            Check(up && rec is { State: "live", Proof: "legacy", Level: 0, Light: "amber", Tls: "none" } && rec.Flags.Contains("legacy") && rec.Flags.Contains("plain")
                  && link.SetAsideBeforePairing >= 1 && rec.Counts.Malformed >= 1
                  && read.Count(l => l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal)) == 1
                  && !read.Any(l => l.StartsWith("AUTH ", StringComparison.Ordinal) || l.Contains(" AUTH ", StringComparison.Ordinal)),
                $"peerlink scenarios: {Name} off this machine — Patterns 88.1's grammar (STATE, then NONCE): legacy, amber; one AUTH-PROOF for its 32-hex NONCE, never plain AUTH; the STATE before OK paired set aside and counted ({rec.ToJson()}; {string.Join(" | ", read)}) {peer.Faults}");
        }
        foreach (var local in new[] { true, false })
        foreach (var nonce in new[] { "3F9A0C5E7B2D4F6A8C1E3B5D7F9A0C2E", "3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2" })
        {
            var name = $"{Name}, a NONCE of {nonce.Length.ToString(CultureInfo.InvariantCulture)} characters{(nonce.Any(char.IsUpper) ? " in capitals" : "")} {(local ? "on this machine" : "off it")}";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, With(ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"), new { attack = new { nonce } }));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = PlayoutTo(port, clock, FreshInst(), local);
            link.Start();
            var said = await PlayoutLinkTests.Until(() => link.Status.Contains("is not 32 lowercase hex", StringComparison.Ordinal));
            // Past the link's wait for the NONCE and its probe, on its own clock: still nothing proved, nothing sent in clear.
            await Step(clock, 4000);
            var rec = Seen(link);
            var waited = (link.Pairing, link.Health, link.Video, rec.State);
            // No pairing within peerlink.handshake.ms: the link ends the connection.
            await Step(clock, PeerLink.HandshakeMs - 4000 + PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
            var ended = await PlayoutLinkTests.Until(() => !link.Connected);
            await Settle(peer);
            var all = peer.AllRead();
            Check(said && waited == (PlayoutPairing.None, LinkHealth.Starting, null, "connecting") && ended && Seen(link).State == "down"
                  && link.SetAsideBeforePairing >= 1 && all.Any(l => l == "HELLO Stagewatch") && !all.Any(l => l.Contains("AUTH", StringComparison.Ordinal))
                  && !peer.Events.Any(e => Ev(e, "paired")),
                $"peerlink scenarios: {name} — never proved: no AUTH-PROOF, no AUTH, nothing taken, amber saying why; no link within peerlink.handshake.ms ({rec.ToJson()}; {string.Join(" | ", all)}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S19 at level 0, and 13.19.5 on what PlayoutLink published

    /// <summary>
    /// S19 on PlayoutLink: canary secrets. One more link pairs, runs commands and drops; one holds a code the peer refuses, so its hold is
    /// kept on disk; and one meets a service on this PC that answers its plain AUTH by echoing the whole line, code and all. Everything the
    /// scenarios' links published or said is captured as the desk takes it — their status lines, records and command words, the show
    /// log's rows (each change of state, each outcome, each line), a show journal written to a file from those rows, the context
    /// Stagewatch would build from the link's light and words and that journal, the report built from it, the holds' file, and every
    /// refusal a command came back with — and none of it carries the code (as typed, canonical, or its K), a NONCE the peer sent or an
    /// AUTH-PROOF the link answered it with: the echoed code is taken out of the peer's words before the link says them. STATE:
    /// Stagewatch's frame carries the clip and its times, not the link's words or record, until the record reaches its readers (SW20.5);
    /// the record is held here.
    /// </summary>
    static async Task PlayoutS19(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "S19 PlayoutLink";
        var refused = new List<string>();
        using var keptFile = Scratch.File("kept", ".json");
        var store = new PlayoutKeptStore(keptFile.Path);
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            var inst = FreshInst();
            using var link = PlayoutTo(port, clock, inst);
            link.Start();
            var up = await PlayoutUp(link);
            link.Restart();
            await Set(peer, new { answer = "refused" });
            link.EndIn(10);
            await PlayoutLinkTests.Until(() => link.OutcomeOf(PlayoutId(2, inst)) == PeerOutcome.Refused);
            await Set(peer, new { answer = "drop" });
            link.Restart();
            await PlayoutLinkTests.Until(() => !link.Connected);
            refused.Add(link.Restart() ?? "");
            refused.Add(link.Seek(30) ?? "");
            Seen(link);
            Check(up && refused.All(r => r.Length > 0), $"peerlink scenarios: {Name} — a link paired, ran commands and dropped; what it refused said why ({string.Join(" | ", refused)}) {peer.Faults}");
        }
        {
            // A code the peer refuses: the proof is wrong, the code held a minute, and the hold kept on disk.
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name + ", refused", ListenAs(Line, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            var tag = PlayoutKeptStore.Tag("a protected form of the wrong code");
            using var link = Playout(new PlayoutLink(new PlayoutLinkOptions
            {
                Host = "127.0.0.1", Port = port, Token = WrongCode, Inst = FreshInst(), Time = clock, Jitter = () => 0.5,
                Kept = store.For(PlayoutRules.PatternsName, tag, clock.WallMs),
                Keep = k => store.Keep(PlayoutRules.PatternsName, tag, k, clock.WallMs),
            }));
            link.Start();
            var held = await PlayoutLinkTests.Until(() => link.State == PeerLinkState.Held);
            refused.Add(link.Restart() ?? "");
            var rec = Seen(link);
            var landed = await store.Flushed;
            Check(held && landed && rec is { State: "held", Refusal: "key" } && store.For(PlayoutRules.PatternsName, tag, clock.WallMs)?.HeldUntilWall == T0 + PeerLink.HoldRefusedMs,
                $"peerlink scenarios: {Name}, refused — a wrong code is held a minute and the hold kept on disk ({rec.ToJson()}) {peer.Faults}");
        }
        {
            // A service on this PC at the link's port that answers a plain AUTH with the whole line: the code comes back in its words.
            await using var echo = new PlayoutLinkTests.FakeDesk();
            echo.Answer = l => l.StartsWith("AUTH ", StringComparison.Ordinal) ? ["ERR unknown command: " + l] : [];
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = PlayoutTo(echo.Port, clock, FreshInst());
            link.Start();
            await PlayoutLinkTests.Until(() => link.Connected);
            await Step(clock, PlayoutLink.NonceWait.Ticks / TimeSpan.TicksPerMillisecond + PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
            var echoed = await PlayoutLinkTests.Until(() => echo.Heard.Any(l => l.StartsWith("AUTH ", StringComparison.Ordinal)) && link.State == PeerLinkState.Held);
            var rec = Seen(link);
            Check(echoed && link.Status.Contains("Patterns: unknown command: AUTH (token)", StringComparison.Ordinal) && rec.State == "held",
                $"peerlink scenarios: {Name}, echoed — a service that echoes the plain AUTH it was sent is refused in its own words, the code taken out of them ({link.Status})");
        }

        var made = Peers.SelectMany(p => p.Events)
            .Select(e => Str(e, "line") ?? "")
            .Select(l => l.StartsWith("NONCE ", StringComparison.Ordinal) ? l[6..] : l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal) ? l[15..] : "")
            .Where(x => x.Length >= 32);
        var canaries = Canaries(Code, CodeTyped, WrongCode).Concat(made).Distinct(StringComparer.Ordinal).ToList();
        foreach (var link in PlayoutLinks) Seen(link);
        var rows = PlayoutRows.ToList();

        // The show journal the desk writes from those rows, the context it builds from the link's light, words and that journal, and the
        // report built from it.
        var now = DateTime.UtcNow;
        using var journalFile = Scratch.File("journal", ".jsonl");
        var journal = new ShowJournal(journalFile.Path);
        foreach (var row in rows) journal.Add(now, JournalKind.Playout, -1, row);
        await journal.DisposeAsync();
        var written = File.Exists(journalFile.Path) ? File.ReadAllText(journalFile.Path) : "";
        var items = PlayoutLinks.Select(l => new ContextItem("link:patterns", "playout", l.Name,
            l.Health switch { LinkHealth.Ok => Light.Green, LinkHealth.Starting => Light.Amber, _ => Light.Red }, l.Status)).ToList();
        var context = ShowContext.Build(new ContextInput("abc123abc123", "Hall A", now, new ShowSnapshot { Title = "Keynote" }, false, items,
            Array.Empty<PeerSummary>(), journal.Entries, ShowClock.For("UTC"), 0));
        var report = ShowReport.Build("Show", new Rundown(), journal.Entries, null, ShowClock.For("UTC"), now).ToHtml(ShowClock.For("UTC"));
        await store.Flushed;
        var kept = File.Exists(keptFile.Path) ? File.ReadAllText(keptFile.Path) : "";

        var captured = PlayoutSaid.Concat(rows).Concat(refused).Append(written).Append(context).Append(report).Append(kept).ToList();
        var found = Found(captured, canaries);
        Check(PlayoutLinks.Count >= 10 && rows.Count >= 20 && canaries.Count >= 12 && written.Length > 0 && context.Contains("link:patterns", StringComparison.Ordinal)
              && kept.Contains("\"heldUntil\"", StringComparison.Ordinal) && found is null,
            $"peerlink scenarios: S19 PlayoutLink — none of {canaries.Count} canaries (the codes, their K, the NONCEs and the AUTH-PROOFs) in any of the {captured.Count} "
            + $"status lines, records, command words, show-log rows, the journal file, the context, the report, the holds' file and the refusals {PlayoutLinks.Count} links published ({found})");
    }
}
