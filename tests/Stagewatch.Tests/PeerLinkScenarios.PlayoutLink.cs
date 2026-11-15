using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// The PeerLink kit's connector scenarios (Show Bus 13.20: S1, S2, S4–S7, S11–S14, S17–S19, S21 and S22) against PlayoutLink speaking
/// LINK on <c>line</c>, as it does to a Patterns desk from 2.23: an issued app key proved by LINK over plain or TLS, the pairing token
/// proved as a code inside TLS (where the acceptor may enrol a key), and an acceptor that answers LINK as an older build kept at level 0.
/// The scripted peer is a level-1 line acceptor with Patterns' features (<c>final</c>, <c>receipts</c>, <c>enrol</c> over TLS), its TLS
/// told from plain by the first byte as Patterns' 9697 tells it (98.1); PlayoutLink runs on the injected clock of
/// <see cref="PlayoutLinkTests.LinkClock"/>. Each scenario says what it holds over the real socket, so the tests can be cited by name.
/// </summary>
static partial class PeerLinkScenarios
{
    /// <summary>This Stagewatch's Show Bus id in the scenarios: the connector a peer binds a key to is <c>stagewatch:</c> and it.</summary>
    const string SelfId = "5e1fa11c";

    /// <summary>The peer as Patterns' line acceptor: its node, TLS told by the first byte with <paramref name="cert"/>, and the features it offers.</summary>
    static Dictionary<string, object> PatternsAt(KitCert cert, object[] keys, string[] can, int level = 1, string tls = "sniff", string? scope = null)
    {
        var d = new Dictionary<string, object>
        {
            ["do"] = "listen", ["end"] = "a", ["carrier"] = "line", ["transport"] = "tcp", ["tls"] = tls, ["cert"] = cert.CertFile,
            ["certKey"] = cert.KeyFile, ["node"] = Patterns, ["keys"] = keys, ["level"] = level, ["can"] = can, ["clock"] = T0 + 25,
        };
        if (scope is not null) d["scope"] = scope;
        return d;
    }

    static readonly string[] PatternsCan = ["final", "receipts"];
    static readonly string[] PatternsCanTls = ["final", "receipts", "enrol"];

    /// <summary>
    /// PlayoutLink to the peer at <paramref name="port"/> as a Patterns link that names itself <see cref="SelfId"/>: holding
    /// <paramref name="token"/> (an app key or the pairing token), on <paramref name="transport"/>, off this machine unless
    /// <paramref name="local"/>, starting from <paramref name="kept"/> and keeping a key it enrols with <paramref name="keep"/>.
    /// </summary>
    static PlayoutLink LinkTo(int port, string token, PlayoutLinkTests.LinkClock clock, string inst, string transport = PlayoutRules.Auto,
        PeerKept? kept = null, bool local = false, Action<PlayoutEnrolment>? keep = null, PlayoutEnrolment? enrolled = null)
        => Playout(new PlayoutLink(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = port, Token = token, Inst = inst, Name = "patterns", Kind = PlayoutRules.Patterns,
            IsLoopback = local ? PlayoutLink.IsLoopbackAddress : PlayoutLinkTests.Remote, Time = clock, Jitter = () => 0.5, Transport = transport,
            Kept = kept, Self = SelfId, KeepEnrolled = keep, Enrolled = enrolled, Tag = "tag-1",
        }));

    /// <summary>The link verified both ways (LINK, 13.6.3) and read its first STATE: everything since is taken.</summary>
    static Task<bool> Proved(PlayoutLink link)
        => PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.RecordNow().Proof == "mutual" && link.Reading is not null);

    /// <summary>The kid of an issued key, as LINK names it.</summary>
    static string KidOf(string key) => PeerLink.Kid(PeerLink.KeyK(key));

    static async Task PlayoutLinkLevel1(Action<bool, string> Check, string script, Material kit)
    {
        await LinkS1(Check, script, kit);
        await LinkS2S17S18(Check, script, kit);
        await LinkS4(Check, script, kit);
        await LinkS5S6S7(Check, script, kit);
        await LinkS11S12S13(Check, script, kit);
        await LinkS14(Check, script, kit);
        await LinkS14Patterns(Check, script, kit);
        await LinkS21(Check, script, kit);
        await LinkS21Forget(Check, script, kit);
        await LinkS22(Check, script, kit);
        await LinkPlayMapSeam(Check, script, kit);
        LinkRule(Check);
    }

    // ------------------------------------------------------------------ S1 at level 1

    /// <summary>
    /// S1 on PlayoutLink at level 1 (its test name: <c>PlayoutLink S1 — verified both ways</c>). Over the real socket to the scripted
    /// peer: an issued key over plain TCP and the pairing token as a code inside TLS each prove LINK — HELLO, then
    /// <c>@l1 LINK 1 stagewatch &lt;id&gt; &lt;cnonce&gt; &lt;kid&gt;</c>, one <c>LINK PROOF</c>, and never the key, the code, plain AUTH
    /// or AUTH-PROOF on the wire; the acceptor's own proof checked before anything is taken; one lid at both ends; the record live at
    /// level 1, proof mutual, its kid and scope, pinned by the proof and a floor recorded over TLS; a command applied once under its
    /// request id; and a matched pong a clock sample that schedules a timed command over TLS, never off this machine on plain TCP.
    /// </summary>
    static async Task LinkS1(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var tls in new[] { false, true })
        {
            var name = "PlayoutLink S1 — verified both ways " + (tls ? "with the pairing code inside TLS" : "with an issued key over plain TCP");
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, PatternsAt(kit.A, tls ? [CodeOf(kit, Code)] : [Key(kit, Ptk)], tls ? PatternsCan : PatternsCan, scope: "drive"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            var inst = FreshInst();
            using var link = LinkTo(port, tls ? CodeTyped : Ptk, clock, inst, tls ? PlayoutRules.Wss : PlayoutRules.Auto);
            link.Start();
            var up = await Proved(link);
            var conn = await PairedOn(peer);
            var verified = peer.Events.FirstOrDefault(e => Ev(e, "link") && Str(e, "role") == "acceptor");
            var read = peer.ReadOn(conn);
            var rec = Seen(link);
            var kid = tls ? "code" : KidOf(Ptk);
            Check(up && rec is { State: "live", Level: 1, Proof: "mutual", Carrier: "line" } && rec.Kid == kid && rec.Scope == (tls ? "operate" : "drive")
                  && (tls ? rec.Tls == "pinned" && rec.Light == "green" && rec.Floor is { Tls: true, Level: 1, Proof: "mutual" } : rec.Flags.Contains("plain") && rec.Light == "amber")
                  && verified.ValueKind == JsonValueKind.Object && Str(verified, "kid") == kid,
                $"peerlink scenarios: {name} — the record is live at level 1, proof mutual, kid {kid}, {(tls ? "pinned by the proof, a floor over TLS, green" : "plain off this machine, amber")} ({rec.ToJson()}) {peer.Faults}");
            var fact = PlayoutLinkTests.FactOf(link);
            Check(link.PeerNode is (PatternsId, true) && link.Use == "controls"
                  && Stagewatch.Show.ShowContext.PlayoutRows([fact]) is [{ From: "link:patterns", To: "@" + PatternsId, Kind: "controls" } row] && row.Light == rec.Light
                  && Stagewatch.Show.ShowContext.PeerWords(fact) == "node " + PatternsId,
                $"peerlink scenarios: {name} — the context names the node LINK proved, its one row controls it at scope {rec.Scope} in the record's light ({link.PeerNode}, {link.Use})");
            Check(read.Count >= 3 && read[0] == "HELLO Stagewatch" && read[1].StartsWith("@l1 LINK 1 stagewatch " + SelfId + " ", StringComparison.Ordinal)
                  && read[1].EndsWith(" " + kid, StringComparison.Ordinal) && read.Count(l => l.StartsWith("@l2 LINK PROOF ", StringComparison.Ordinal)) == 1
                  && !read.Any(l => l.Contains("AUTH", StringComparison.Ordinal)) && Found(read, Canaries(Ptk, Code, CodeTyped)) is null,
                $"peerlink scenarios: {name} — HELLO, one LINK naming this Stagewatch and the kid, one LINK PROOF, no AUTH and no secret on the wire ({string.Join(" | ", read)})");

            var written = link.Restart();
            var id = PlayoutId(1, inst);
            var applied = await PlayoutLinkTests.Until(() => link.OutcomeOf(id) == PeerOutcome.Applied);
            await peer.Wait(e => Ev(e, "command") && Str(e, "id") == id);
            var ran = Ran(peer, inst);
            Check(written is null && applied && ran.Count == 1 && Str(ran[0], "line") == "VIDEO RESTART",
                $"peerlink scenarios: {name} — a command with its request id ({id}) runs once at the peer and reads applied ({link.OutcomeOf(id)}) {peer.Faults}");

            await Set(peer, new { clock = T0 + 250 });
            clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
            var sampled = await PlayoutLinkTests.Until(() => link.RecordNow().Samples >= 1);
            rec = Seen(link);
            Check(sampled && rec.Counts.Unmatched == 0 && link.CanSchedule(6) == tls && link.ClockMayTime == tls,
                $"peerlink scenarios: {name} — a matched pong is a clock sample{(tls ? ", fit for a timed command over TLS" : "; off this machine a plain link's clock never schedules")} ({rec.ToJson()})");
            Check(link.Standing.StartsWith("PeerLink 1 · proved both ways with " + (tls ? "the pairing code" : "key " + kid[..8]), StringComparison.Ordinal)
                  && link.Value is null && link.ScopeHeld is null,
                $"peerlink scenarios: {name} — the link's standing in a reader's words names its level, proof and key by kid ({link.Standing})");
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Ptk, Code, CodeTyped)) is null, $"peerlink scenarios: {name} — no key, code or K on the peer's stdout");
        }
    }

    // ------------------------------------------------------------------ S2, S17, S18

    /// <summary>
    /// S2, S17 and S18 on PlayoutLink at level 1: a key the acceptor does not take (<c>PlayoutLink S2 — a refused key is held</c>), a key
    /// revoked while its link is live (<c>PlayoutLink S17 — a revoked key</c>) and a key bound to another connector
    /// (<c>PlayoutLink S18 — a key bound elsewhere</c>) are each refused in the acceptor's words, the connection ended and the key held
    /// <c>peerlink.hold.refused.ms</c> (13.15.3), red, with no second LINK until the hold ends.
    /// </summary>
    static async Task LinkS2S17S18(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var (name, keys, token, live) in new (string, object[], string, bool)[]
                 {
                     ("PlayoutLink S2 — a refused key is held", [Key(kit, Ptk)], Stranger, false),
                     ("PlayoutLink S18 — a key bound elsewhere", [new { file = kit.File(Ptk), kind = "key", bound = "stagewatch:0ther0ne" }], Ptk, false),
                     ("PlayoutLink S17 — a revoked key", [Key(kit, Ptk)], Ptk, true),
                 })
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, PatternsAt(kit.A, keys, PatternsCan, scope: "drive"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, token, clock, FreshInst());
            link.Start();
            if (live)
            {
                var up = await Proved(link);
                await Set(peer, new { revoke = KidOf(Ptk) });
                Check(up, $"peerlink scenarios: {name} — the link was live before its key was revoked {peer.Faults}");
            }
            var held = await PlayoutLinkTests.Until(() => link.RecordNow().State == "held");
            var rec = Seen(link);
            var want = name.Contains("bound", StringComparison.Ordinal) ? "binding" : "key";
            var closed = await peer.Wait(e => Ev(e, "closed"));
            var links = peer.AllRead().Count(l => l.Contains(" LINK 1 stagewatch ", StringComparison.Ordinal));
            await Step(clock, PeerLink.HoldRefusedMs - 1000);
            var still = peer.AllRead().Count(l => l.Contains(" LINK 1 stagewatch ", StringComparison.Ordinal));
            Check(held && rec is { State: "held", Light: "red" } && rec.Refusal == want && rec.NextMs is > 50_000 and <= PeerLink.HoldRefusedMs && closed is not null
                  && links == 1 && still == 1 && link.Health == LinkHealth.Error,
                $"peerlink scenarios: {name} — refused ({want}), the connection ended, the key held {PeerLink.HoldRefusedMs / 1000} s, red, and no second LINK in the hold ({rec.ToJson()}; {link.Status}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S4

    /// <summary>
    /// S4 on PlayoutLink at level 1 (<c>PlayoutLink S4 — a machine between</c>): an acceptor whose verdict's proof is this link's own
    /// client proof (what a machine relaying the link would answer) is taken for nothing — not verified, not pinned, no floor, no
    /// command, the connection ended.
    /// </summary>
    static async Task LinkS4(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "PlayoutLink S4 — a machine between";
        await using var peer = Peer(script);
        var listen = PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCanTls);
        listen["attack"] = new { verdict = "reflect" };
        var port = await Listen(Check, peer, Name, listen);
        var clock = new PlayoutLinkTests.LinkClock(T0);
        using var link = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Wss);
        link.Start();
        var attacked = await peer.Wait(e => Ev(e, "attack"));
        var ended = await PlayoutLinkTests.Until(() => !link.Connected);
        var rec = Seen(link);
        Check(attacked is not null && ended && rec.Proof != "mutual" && link.Pairing == PlayoutPairing.None && link.Kept.Pin is null && link.Kept.Floor is null
              && link.EnrolledKid is null && link.Restart() is not null,
            $"peerlink scenarios: {Name} — a verdict whose proof is the link's own is taken for nothing: not verified, pinned or floored, nothing sent ({rec.ToJson()}) {peer.Faults}");
    }

    // ------------------------------------------------------------------ S5, S6, S7

    /// <summary>
    /// S5, S6 and S7 on PlayoutLink at level 1, from what an S1 link over TLS kept: a pinned node is dialled over TLS on the line wire's
    /// own port whatever its transport says (<c>PlayoutLink S5 — pinned, dialled over TLS</c>); a pinned node that shows another
    /// certificate gets nothing of the link's — not a byte past the TLS handshake — and waits for a person
    /// (<c>PlayoutLink S6 — CERTIFICATE CHANGED</c>); and a floored node that answers LINK as an older build is refused for its floor,
    /// no AUTH-PROOF sent, until a person lowers the floor (<c>PlayoutLink S7 — the floor</c>), when it pairs at level 0, amber, legacy.
    /// </summary>
    static async Task LinkS5S6S7(Action<bool, string> Check, string script, Material kit)
    {
        PeerKept kept;
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "PlayoutLink S5–S7's first link", PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCan));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var first = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Wss);
            first.Start();
            Check(await Proved(first), $"peerlink scenarios: PlayoutLink S5–S7's first link proves itself over TLS {peer.Faults}");
            kept = first.Kept;
            Check(kept.Pin is { Trust: PinTrust.Proof } pin && pin.Fingerprint == kit.A.Fp && kept.Floor is { Tls: true, Level: 1 },
                $"peerlink scenarios: PlayoutLink S5–S7's first link keeps a pin made by its proof and a floor over TLS ({kept})");
        }
        {
            const string Name = "PlayoutLink S5 — pinned, dialled over TLS";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCan));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Auto, kept);
            link.Start();
            var up = await Proved(link);
            var connected = peer.Events.Where(e => Ev(e, "connected")).ToList();
            Check(up && connected.Count >= 1 && connected.All(e => e.TryGetProperty("tls", out var t) && t.ValueKind == JsonValueKind.True) && link.OverTls
                  && link.RecordNow().Tls == "pinned",
                $"peerlink scenarios: {Name} — set to auto, the pinned node is dialled over TLS on its wire's own port and never plain ({connected.Count} connections) {peer.Faults}");
        }
        {
            const string Name = "PlayoutLink S6 — CERTIFICATE CHANGED";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, PatternsAt(kit.B, [CodeOf(kit, Code)], PatternsCan));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Auto, kept);
            link.Start();
            var refused = await PlayoutLinkTests.Until(() => link.RecordNow().State == "refused");
            await Settle(peer);
            var rec = Seen(link);
            Check(refused && rec.Refusal == "certificate" && peer.AllRead().Count == 0 && link.TrustWaiting is { Changed: true } w && w.Fingerprint == kit.B.Fp
                  && link.Status.StartsWith("CERTIFICATE CHANGED", StringComparison.Ordinal),
                $"peerlink scenarios: {Name} — another certificate gets nothing of the link's, not a line past the handshake, and waits for a person ({rec.ToJson()}; {link.Status}) {peer.Faults}");
        }
        {
            const string Name = "PlayoutLink S7 — the floor";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [CodeOf(kit, Code)], [], level: 0, tls: "sniff"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Auto, kept);
            link.Start();
            var refused = await PlayoutLinkTests.Until(() => link.RecordNow().State == "refused");
            await Settle(peer);
            var rec = Seen(link);
            Check(refused && rec.Refusal == "floor" && !peer.AllRead().Any(l => l.Contains("AUTH", StringComparison.Ordinal)) && link.Floor is { Tls: true, Level: 1 },
                $"peerlink scenarios: {Name} — a floored node that answers LINK as an older build is refused for its floor, no AUTH-PROOF sent ({rec.ToJson()}) {peer.Faults}");
            var (lowered, words) = link.LowerFloor();
            // A link refused for a person to act on looks again each second: the next look dials, asking for LINK as before.
            await Step(clock, 1000);
            var legacy = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.RecordNow() is { Level: 0, Proof: "legacy" });
            rec = Seen(link);
            var (again, _) = link.LowerFloor();
            Check(lowered && words.Contains("floor", StringComparison.Ordinal) && legacy && rec is { Light: "amber" } && rec.Flags.Contains("legacy") && rec.Floor is null
                  && !again && peer.AllRead().Count(l => l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal)) == 1,
                $"peerlink scenarios: {Name} — a person lowers the floor and the link pairs at level 0, amber, legacy, by one AUTH-PROOF; lowering again changes nothing ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S11, S12, S13

    /// <summary>
    /// S11, S12 and S13 on PlayoutLink at level 1: silence makes the verified link stale past link.stale.ms and down past link.drop.ms,
    /// the socket closed (<c>PlayoutLink S11 — silence</c>); a pong that says STUCK makes the show stuck, amber, and a STATE clears it
    /// (<c>PlayoutLink S12 — the stuck show</c>); an unmatched pong is no clock sample and is counted (<c>PlayoutLink S13 — unmatched
    /// pongs</c>).
    /// </summary>
    static async Task LinkS11S12S13(Action<bool, string> Check, string script, Material kit)
    {
        {
            const string Name = "PlayoutLink S11 — silence";
            await using var peer = Peer(script);
            // No receipts offered: a peer that keeps them answers a repeated ask (the link's CONTEXT) from its record, never silent.
            var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [Key(kit, Ptk)], ["final"], scope: "drive"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, Ptk, clock, FreshInst());
            link.Start();
            var up = await Proved(link);
            await Set(peer, new { pongs = false, answer = "silent" });
            // The evaluator's states second by second, from the last line heard (the answers to the link's first asks) on.
            var states = new List<string>();
            var lights = new List<string>();
            for (var t = 0; t < 16 && link.Connected; t++)
            {
                await Step(clock, 1000);
                var r = Seen(link);
                if (states.Count == 0 || states[^1] != r.State)
                {
                    states.Add(r.State);
                    lights.Add(r.Light);
                }
            }
            var down = await PlayoutLinkTests.Until(() => !link.Connected);
            var rec = Seen(link);
            if (states[^1] != rec.State) { states.Add(rec.State); lights.Add(rec.Light); }
            Check(up && states.SequenceEqual(["live", "stale", "down"]) && lights.SequenceEqual(["amber", "amber", "red"]) && down && rec.Level == 0,
                $"peerlink scenarios: {Name} — the verified link is live at link.stale.ms, stale past it and down past link.drop.ms, its socket closed ({string.Join(" → ", states)}; {rec.ToJson()}) {peer.Faults}");
        }
        {
            const string Name = "PlayoutLink S12 — the stuck show";
            await using var peer = Peer(script);
            var listen = PatternsAt(kit.A, [Key(kit, Ptk)], ["final", "receipts", "stuck"], scope: "drive");
            listen["stuckMs"] = 3000;
            var port = await Listen(Check, peer, Name, listen);
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, Ptk, clock, FreshInst());
            link.Start();
            var up = await Proved(link);
            await Set(peer, new { stuck = true });
            await Step(clock, 2000);
            var stuck = await PlayoutLinkTests.Until(() => link.RecordNow().Show == "stuck");
            var rec = Seen(link);
            Check(up && stuck && rec is { Light: "amber", State: "live" },
                $"peerlink scenarios: {Name} — a pong that says STUCK makes the show stuck, amber, the link live ({rec.ToJson()}) {peer.Faults}");
        }
        {
            const string Name = "PlayoutLink S13 — unmatched pongs";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [Key(kit, Ptk)], PatternsCan, scope: "drive"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, Ptk, clock, FreshInst());
            link.Start();
            var up = await Proved(link);
            await Set(peer, new { pongs = "unmatched" });
            await Step(clock, 250);
            var counted = await PlayoutLinkTests.Until(() => link.RecordNow().Counts.Unmatched >= 1);
            var rec = Seen(link);
            Check(up && counted && rec.Samples == 0 && rec.State == "live" && link.Clock is null,
                $"peerlink scenarios: {Name} — a pong that answers no ping of the link's is no clock sample, counted unmatched ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S14

    /// <summary>
    /// S14 on PlayoutLink at level 1, for the features Patterns offers (<c>final</c>, <c>receipts</c>). Over the real socket, with
    /// the scripted peer's own receipts (a <c>VIDEO RESTART</c>; Patterns' own CUE GO bytes are <see cref="LinkS14Patterns"/>'s): a
    /// command the acceptor answers accepted reads accepted first and its final receipt is the outcome
    /// (<c>PlayoutLink S14 — the final is the outcome</c>); a command refused reads refused in the acceptor's words; and a command the
    /// acceptor ran as the connection dropped (a pulled cable) reads unconfirmed, then — once the link proves itself again — RECEIPTS
    /// reads it applied from the acceptor's record, and it ran once (<c>PlayoutLink S14 — a GO caught by a pulled cable runs once</c>).
    /// </summary>
    static async Task LinkS14(Action<bool, string> Check, string script, Material kit)
    {
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, "PlayoutLink S14", PatternsAt(kit.A, [Key(kit, Ptk)], PatternsCan, scope: "drive"));
        var clock = new PlayoutLinkTests.LinkClock(T0);
        var inst = FreshInst();
        using var link = LinkTo(port, Ptk, clock, inst);
        link.Start();
        Check(await Proved(link), $"peerlink scenarios: PlayoutLink S14's link proves itself {peer.Faults}");

        await Set(peer, new { answer = "accepted" });
        var go = link.Clip("RESTART", 0);
        var id1 = PlayoutId(1, inst);
        var accepted = await PlayoutLinkTests.Until(() => link.OutcomeOf(id1) == PeerOutcome.Accepted);
        var first = go.Ticket is { } t1 ? await t1.First.WaitAsync(TimeSpan.FromSeconds(5)) : default;
        await Set(peer, new { final = "applied" });
        var final = go.Ticket is { } t2 ? await t2.Final.WaitAsync(TimeSpan.FromSeconds(5)) : default;
        Check(go.Error is null && accepted && first.Outcome == PeerOutcome.Accepted && final.Outcome == PeerOutcome.Applied && link.OutcomeOf(id1) == PeerOutcome.Applied,
            $"peerlink scenarios: PlayoutLink S14 — the final is the outcome: accepted first, then applied by the final receipt ({first.Outcome} → {final.Outcome}) {peer.Faults}");

        await Set(peer, new { answer = "refused" });
        link.Restart();
        var id2 = PlayoutId(2, inst);
        var refused = await PlayoutLinkTests.Until(() => link.OutcomeOf(id2) == PeerOutcome.Refused);
        Check(refused, $"peerlink scenarios: PlayoutLink S14 — a refused command reads refused in the acceptor's words ({link.OutcomeOf(id2)}) {peer.Faults}");

        // A pulled cable with a GO in flight: the acceptor runs it and the connection drops before its answer.
        await Set(peer, new { answer = "drop" });
        link.Clip("RESTART", 0);
        var id3 = PlayoutId(3, inst);
        var unconfirmed = await PlayoutLinkTests.Until(() => link.OutcomeOf(id3) == PeerOutcome.Unconfirmed);
        await Set(peer, new { answer = "applied" });
        var redial = await PlayoutLinkTests.Until(() => clock.OneShotWaiting);
        await Step(clock, link.RecordNow().NextMs ?? 1000);
        var relinked = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.RecordNow().Proof == "mutual");
        var asked = await peer.Wait(e => Ev(e, "read") && (Str(e, "line") ?? "").Contains(" RECEIPTS " + id3, StringComparison.Ordinal));
        var applied = await PlayoutLinkTests.Until(() => link.OutcomeOf(id3) == PeerOutcome.Applied);
        await Settle(peer);
        var ran = Ran(peer, inst).Count(e => Str(e, "id") == id3);
        Check(unconfirmed && redial && relinked && asked is not null && applied && ran == 1,
            $"peerlink scenarios: PlayoutLink S14 — a GO caught by a pulled cable runs once: unconfirmed, then RECEIPTS after the link proves itself again, then applied ({link.OutcomeOf(id3)}, ran {ran}) {peer.Faults}");
    }

    /// <summary>
    /// S14 in Patterns' own bytes (<c>PlayoutLink S14 — Patterns' CUE GO: Requested, then the final</c>; Patterns at 2515017,
    /// ControlService.Link's Settled and CueFinal.Line). Over the real socket, the scripted peer runs the GO and answers nothing of its
    /// own, and the lines Patterns' wire writes are sent in its place: <c>OK @id {"outcome":"Requested",…,"execution":…}</c> reads
    /// accepted and settles nothing — the ticket's final still waits — and <c>OK @id final {"ok":true,"stage":"applied","effect":…,
    /// "lateMs":N}</c> is the outcome, applied in its words, its lateness counted. A GO Patterns answers <c>Confirm</c> (its second GO is
    /// a person's) reads accepted, and its at-once <c>ERR @id final not run — …</c> is the outcome: refused.
    /// </summary>
    static async Task LinkS14Patterns(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "PlayoutLink S14 — Patterns' CUE GO: Requested, then the final";
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [Key(kit, Ptk)], PatternsCan, scope: "drive"));
        var clock = new PlayoutLinkTests.LinkClock(T0);
        var inst = FreshInst();
        using var link = LinkTo(port, Ptk, clock, inst);
        link.Start();
        var up = await Proved(link);
        var conn = await PairedOn(peer);
        await Set(peer, new { answer = "silent" });

        var go = link.Cue("GO", "1");
        var id = PlayoutId(1, inst);
        var ran = await peer.Wait(e => Ev(e, "command") && Str(e, "id") == id);
        await peer.Ask(new { @do = "send", conn, line = "OK @" + id + " {\"outcome\":\"Requested\",\"effect\":\"Changed\",\"visibility\":\"OutputsLive\",\"execution\":\"e7a1c2\"}" });
        var accepted = await PlayoutLinkTests.Until(() => link.OutcomeOf(id) == PeerOutcome.Accepted);
        var first = go.Ticket is { } t1 ? await t1.First.WaitAsync(TimeSpan.FromSeconds(5)) : default;
        await Settle(peer);
        var waiting = go.Ticket is { Final.IsCompleted: false } && link.OutcomeOf(id) == PeerOutcome.Accepted;
        await peer.Ask(new { @do = "send", conn, line = "OK @" + id + " final {\"ok\":true,\"stage\":\"applied\",\"effect\":\"01.010 Wall to main — Done\",\"lateMs\":1500}" });
        var final = go.Ticket is { } t2 ? await t2.Final.WaitAsync(TimeSpan.FromSeconds(5)) : default;
        var rec = Seen(link);
        Check(up && go.Error is null && ran is not null && accepted && first.Outcome == PeerOutcome.Accepted && waiting && final.Outcome == PeerOutcome.Applied
              && final.Words.Contains("Wall to main", StringComparison.Ordinal) && link.OutcomeOf(id) == PeerOutcome.Applied && rec.Counts.Late >= 1,
            $"peerlink scenarios: {Name} — Requested reads accepted and settles nothing; OK @id final is the outcome, applied in its words, lateMs counted ({first.Outcome} → {final.Outcome} \"{final.Words}\"; late {rec.Counts.Late}) {peer.Faults}");

        var confirm = link.Cue("GO", "1");
        var id2 = PlayoutId(2, inst);
        await peer.Wait(e => Ev(e, "command") && Str(e, "id") == id2);
        await peer.Ask(new { @do = "send", conn, line = "OK @" + id2 + " {\"outcome\":\"Confirm\",\"effect\":\"Nothing\",\"confirm\":\"GO again within the confirm window\"}" });
        var asked = await PlayoutLinkTests.Until(() => link.OutcomeOf(id2) == PeerOutcome.Accepted);
        await peer.Ask(new { @do = "send", conn, line = "ERR @" + id2 + " final not run — this GO asked for a second GO, a person's, within the confirm window; the cue runs on that GO, never on this one" });
        var notRun = confirm.Ticket is { } t3 ? await t3.Final.WaitAsync(TimeSpan.FromSeconds(5)) : default;
        Check(confirm.Error is null && asked && notRun.Outcome == PeerOutcome.Refused && notRun.Words.StartsWith("not run", StringComparison.Ordinal)
              && link.OutcomeOf(id2) == PeerOutcome.Refused,
            $"peerlink scenarios: {Name} — Confirm reads accepted, and its at-once ERR @id final is the outcome: refused, not run ({notRun.Outcome} \"{notRun.Words}\") {peer.Faults}");
    }

    // ------------------------------------------------------------------ S21

    /// <summary>
    /// S21 on PlayoutLink (<c>PlayoutLink S21 — enrolment</c>): the pairing code proved inside TLS to an acceptor that offers
    /// <c>enrol</c> enrols a key for this link; the link checks it — derived from the code over this connection's T, its kid the one the
    /// verdict named — adopts it and keeps it with the machine, and from its next link proves with that key, never the code, so a second
    /// link enrols no second key. The enrolled key stands at <c>follow</c>: the link reads, and sends no playout command Patterns would
    /// refuse for scope — refused here first, in words that name the scope and who raises it. A new code at the acceptor revokes what
    /// the old one enrolled, and the live link is refused and held. Over plain the code enrols nothing: it is 13.6.8's level 0.
    /// </summary>
    static async Task LinkS21(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "PlayoutLink S21 — enrolment";
        PlayoutEnrolment? keptKey = null;
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCanTls));
        var clock = new PlayoutLinkTests.LinkClock(T0);
        var inst = FreshInst();
        using var link = LinkTo(port, CodeTyped, clock, inst, PlayoutRules.Wss, keep: e => keptKey = e);
        link.Start();
        var up = await Proved(link);
        var enrolled = await peer.Wait(e => Ev(e, "enrolled") && Str(e, "role") == "acceptor");
        var kid = enrolled is { } en ? Str(en, "kid") : null;
        Check(up && kid is not null && link.EnrolledKid == kid && keptKey is { } k && k.Kid == kid && k.Tag == "tag-1" && PeerLink.Kid(k.K) == kid
              && link.RecordNow() is { Kid: "code", Scope: "operate" } && !k.ToString().Contains(Convert.ToHexStringLower(k.K), StringComparison.Ordinal),
            $"peerlink scenarios: {Name} — the code inside TLS enrols a key; the link checks and adopts its kid, keeps it, and this link stands on the code ({kid}) {peer.Faults}");

        // The next link proves with the enrolled key: one LINK naming its kid, verified as an enrolled link, at follow.
        var conn = await PairedOn(peer);
        await peer.Ask(new { @do = "close", conn });
        await PlayoutLinkTests.Until(() => clock.OneShotWaiting);
        // Down, the context names the node the earlier LINK bound, not proved now, and no clock: none is measured with no connection.
        var down = PlayoutLinkTests.FactOf(link);
        Check(!link.Connected && link.PeerNode is (PatternsId, false) && link.Clock is null
              && Stagewatch.Show.ShowContext.PlayoutItems([down]).All(i => !i.Id.StartsWith("clock:", StringComparison.Ordinal))
              && Stagewatch.Show.ShowContext.PeerWords(down) == "node " + PatternsId + ", not proved",
            $"peerlink scenarios: {Name} — with its connection down the link names the node an earlier LINK bound, not proved, and no clock ({link.PeerNode})");
        await Step(clock, link.RecordNow().NextMs ?? 1000);
        var again = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.RecordNow().Kid == kid);
        var second = peer.Events.LastOrDefault(e => Ev(e, "link") && Str(e, "role") == "acceptor");
        var enrolments = peer.Events.Count(e => Ev(e, "enrolled") && Str(e, "role") == "acceptor");
        var rec = Seen(link);
        Check(again && Str(second, "kid") == kid && Str(second, "verified") == "link" && enrolments == 1 && rec.Scope == "follow" && rec.Flags.Contains("scope") && rec.Light == "amber",
            $"peerlink scenarios: {Name} — the next link proves with the enrolled key, never the code; no second key is enrolled; it stands at follow, amber for its scope ({rec.ToJson()}) {peer.Faults}");
        // §6 P2's K at follow: a key that only reads its peer follows it; the context never says it controls what the key does not let it.
        Check(link.PeerNode is (PatternsId, true) && link.Use == "follows"
              && Stagewatch.Show.ShowContext.PlayoutRows([PlayoutLinkTests.FactOf(link)]) is [{ To: "@" + PatternsId, Kind: "follows" }],
            $"peerlink scenarios: {Name} — at follow the link's one row follows the node LINK proved, never controls ({link.PeerNode}, {link.Use})");
        // Its first whole STATE says live; the desk's status and light still say the scope before any press, never green.
        var said = await PlayoutLinkTests.Until(() => link.Reading is not null && link.Status.Contains("scope follow", StringComparison.Ordinal));
        Check(said && link.Health != LinkHealth.Ok && link.Status.Contains("Nodes page", StringComparison.Ordinal)
              && link.Standing.Contains("enrolled key " + kid![..8], StringComparison.Ordinal)
              && PlayoutMenus.Why(new PlayoutLinkSettings { Name = "patterns", Kind = PlayoutRules.Patterns }, PlayoutFacts.Of(link), null, true, true, "pause", 300, link.ScopeHeld)
                  is { } why0 && why0.Contains("scope follow", StringComparison.Ordinal),
            $"peerlink scenarios: {Name} — after its STATE the link at follow is amber and its status says the scope; the VT entries are greyed with the same words ({link.Health}: {link.Status}; {link.Standing})");

        var before = Ran(peer, inst).Count;
        var sent = link.Clip("RESTART", 0);
        await Settle(peer);
        Check(sent.Ticket is null && sent.Error is { } why && why.Contains("scope follow", StringComparison.Ordinal) && why.Contains("nothing sent", StringComparison.Ordinal)
              && link.ScopeHeld is not null && Ran(peer, inst).Count == before,
            $"peerlink scenarios: {Name} — a link at follow sends no playout command; the desk says why before anything goes ({sent.Error}) {peer.Faults}");

        // Patterns' NEW TOKEN: the enrolled key is revoked with the old code, and the live link is refused and held.
        await Set(peer, new { code = new { file = kit.File(WrongCode), kind = "code" } });
        var held = await PlayoutLinkTests.Until(() => link.RecordNow().State == "held");
        rec = Seen(link);
        Check(held && rec.Refusal == "key" && rec.Light == "red",
            $"peerlink scenarios: {Name} — a new code at the acceptor revokes the enrolled key; the live link is refused and held ({rec.ToJson()}) {peer.Faults}");

        // Over plain the code is 13.6.8's level 0: no LINK, no enrolment.
        await using var plain = Peer(script);
        var plainPort = await Listen(Check, plain, Name + " over plain", PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCanTls, tls: "plain"));
        using var legacy = LinkTo(plainPort, CodeTyped, new PlayoutLinkTests.LinkClock(T0), FreshInst());
        legacy.Start();
        var paired = await PlayoutLinkTests.Until(() => legacy.Pairing == PlayoutPairing.Paired && legacy.RecordNow().Proof == "legacy");
        await Settle(plain);
        Check(paired && legacy.EnrolledKid is null && !plain.AllRead().Any(l => l.Contains(" LINK ", StringComparison.Ordinal)) && !plain.Events.Any(e => Ev(e, "enrolled")),
            $"peerlink scenarios: {Name} — over plain the code sends no LINK and enrols nothing: level 0, legacy ({legacy.RecordNow().ToJson()}) {plain.Faults}");
    }

    /// <summary>
    /// S21's way out (<c>PlayoutLink S21 — a forgotten enrolled key</c>): a key the acceptor enrolled for this link and then revoked
    /// refuses the link that proves it, held, in words that say the key is no longer held there and how a person recovers; a person
    /// forgets it on this desk, and once the hold ends the link proves its pairing token inside TLS again and the acceptor enrols a new
    /// key, which the link adopts. Forgetting when none is held changes nothing.
    /// </summary>
    static async Task LinkS21Forget(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "PlayoutLink S21 — a forgotten enrolled key";
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCanTls));
        var clock = new PlayoutLinkTests.LinkClock(T0);
        using var link = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Wss);
        link.Start();
        var up = await Proved(link) && await PlayoutLinkTests.Until(() => link.EnrolledKid is not null);
        var kid1 = link.EnrolledKid ?? "";
        var conn = await PairedOn(peer);
        await peer.Ask(new { @do = "close", conn });
        await PlayoutLinkTests.Until(() => clock.OneShotWaiting);
        await Step(clock, link.RecordNow().NextMs ?? 1000);
        var again = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.RecordNow().Kid == kid1);

        await Set(peer, new { revoke = kid1 });
        var held = await PlayoutLinkTests.Until(() => link.RecordNow().State == "held" && link.Status.Contains("no longer held there", StringComparison.Ordinal));
        var words = link.Status;
        Check(up && kid1.Length == 16 && again && held && words.Contains(kid1[..8], StringComparison.Ordinal) && words.Contains("forget it", StringComparison.Ordinal),
            $"peerlink scenarios: {Name} — the enrolled key revoked, the link is held in words that say the key is gone and how to recover ({words}) {peer.Faults}");

        var (forgot, said) = link.ForgetEnrolled();
        var (twice, _) = link.ForgetEnrolled();
        await PlayoutLinkTests.Until(() => clock.OneShotWaiting);
        await Step(clock, link.RecordNow().NextMs ?? PeerLink.HoldRefusedMs);
        var relinked = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.RecordNow() is { Proof: "mutual", Kid: "code" });
        var renewed = await PlayoutLinkTests.Until(() => link.EnrolledKid is { } k && k != kid1);
        var enrolments = peer.Events.Count(e => Ev(e, "enrolled") && Str(e, "role") == "acceptor");
        Check(forgot && said.Contains(kid1[..8], StringComparison.Ordinal) && !twice && relinked && renewed && enrolments == 2
              && !said.Contains(Convert.ToHexStringLower(PeerLink.CodeK(PeerLink.Canon(Code)!)), StringComparison.Ordinal),
            $"peerlink scenarios: {Name} — forgotten by a person, the link proves the code inside TLS again once the hold ends, and adopts the key enrolled anew ({said}; {link.RecordNow().ToJson()}) {peer.Faults}");
    }

    // ------------------------------------------------------------------ S22

    /// <summary>
    /// S22 on PlayoutLink offering LINK (<c>PlayoutLink S22 — an older Patterns</c>): an acceptor with Patterns 88.1's grammar (STATE, then
    /// NONCE) answers LINK as an unknown command; with the pairing code the link proves the 32-hex NONCE once by AUTH-PROOF and pairs at
    /// level 0, amber, legacy, in 2.22's words, the STATE before the proof set aside; with an app key it sends no proof at all — no
    /// AUTH, no AUTH-PROOF — and says why.
    /// </summary>
    static async Task LinkS22(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "PlayoutLink S22 — an older Patterns";
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name, PatternsAt(kit.A, [CodeOf(kit, Code)], [], level: 0));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, CodeTyped, clock, FreshInst(), PlayoutRules.Wss);
            link.Start();
            var paired = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired && link.Reading is not null);
            var rec = Seen(link);
            var read = peer.AllRead();
            Check(paired && rec is { Level: 0, Proof: "legacy", Light: "amber" } && rec.Flags.Contains("legacy") && link.SetAsideBeforePairing >= 1
                  && read.Count(l => l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal)) == 1 && read.Count(l => l.Contains(" LINK 1 ", StringComparison.Ordinal)) == 1
                  && !read.Any(l => l.StartsWith("AUTH ", StringComparison.Ordinal)),
                $"peerlink scenarios: {Name} — LINK answered as unknown, the code proved by one AUTH-PROOF: level 0, amber, legacy, the STATE before it set aside ({rec.ToJson()}) {peer.Faults}");
        }
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, Name + " with an app key", PatternsAt(kit.A, [CodeOf(kit, Code)], [], level: 0, tls: "plain"));
            var clock = new PlayoutLinkTests.LinkClock(T0);
            using var link = LinkTo(port, Ptk, clock, FreshInst());
            link.Start();
            var advised = await PlayoutLinkTests.Until(() => link.Status.Contains("takes no app key", StringComparison.Ordinal));
            await Settle(peer);
            Check(advised && link.Pairing == PlayoutPairing.None && !peer.AllRead().Any(l => l.Contains("AUTH", StringComparison.Ordinal)) && Found(peer.AllRead(), Canaries(Ptk)) is null,
                $"peerlink scenarios: {Name} — with an app key the link sends no proof and no key, and says the desk takes none ({link.Status}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ PlayMap's seam

    /// <summary>
    /// PlayMap offers no LINK yet (Mapper's PM-9b): a PlayMap link on its <c>playmap.lines.v1</c> channel greets at level 0 whatever
    /// the node offers — against the scripted peer offering LINK on that carrier it sends no LINK, proves the code by AUTH-PROOF, and
    /// reads level 0, amber, legacy (<c>PlayoutLink — PlayMap's seam</c>). <see cref="PlayoutProof.PlayMapSpeaksLink"/> is where that ends.
    /// </summary>
    static async Task LinkPlayMapSeam(Action<bool, string> Check, string script, Material kit)
    {
        const string Name = "PlayoutLink — PlayMap's seam";
        await using var peer = Peer(script);
        var listen = new Dictionary<string, object>
        {
            ["do"] = "listen", ["end"] = "a", ["carrier"] = "line", ["transport"] = "ws", ["tls"] = "only", ["cert"] = kit.A.CertFile,
            ["certKey"] = kit.A.KeyFile, ["node"] = PlayMap, ["keys"] = new[] { CodeOf(kit, Code) }, ["level"] = 1, ["can"] = PatternsCan, ["clock"] = T0 + 25,
        };
        var port = await Listen(Check, peer, Name, listen);
        var heard = new PlayoutHeard(PlayMapId, PlayoutRules.PlayMap, port, "sha256:" + kit.A.Fp, null, "127.0.0.1");
        var clock = new PlayoutLinkTests.LinkClock(T0);
        using var link = Playout(new PlayoutLink(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = port, Token = CodeTyped, Inst = FreshInst(), Name = "stage-left", Kind = PlayoutRules.PlayMap,
            IsLoopback = PlayoutLinkTests.Remote, Time = clock, Jitter = () => 0.5, Transport = PlayoutRules.Wss, Hello = () => heard, Self = SelfId,
        }));
        link.Start();
        var paired = await PlayoutLinkTests.Until(() => link.Pairing == PlayoutPairing.Paired);
        await Settle(peer);
        var rec = Seen(link);
        Check(!PlayoutProof.PlayMapSpeaksLink && paired && link.OverTls && rec is { Level: 0, Proof: "legacy", Light: "amber" }
              && !peer.AllRead().Any(l => l.Contains(" LINK ", StringComparison.Ordinal)) && peer.AllRead().Count(l => l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal)) == 1,
            $"peerlink scenarios: {Name} — offered LINK on playmap.lines.v1, the PlayMap link sends none and pairs at level 0, amber, legacy ({rec.ToJson()}) {peer.Faults}");

        // Its TCP wire alike: a PlayMap link holding a PlayMap app key, to a node offering LINK over plain TCP, sends no LINK.
        await using var tcp = Peer(script);
        var plain = PatternsAt(kit.A, [CodeOf(kit, Code)], PatternsCan, tls: "plain");
        plain["node"] = PlayMap;
        var tcpPort = await Listen(Check, tcp, Name + " on TCP", plain);
        using var keyed = Playout(new PlayoutLink(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = tcpPort, Token = Pmk, Inst = FreshInst(), Name = "stage-left", Kind = PlayoutRules.PlayMap,
            IsLoopback = PlayoutLinkTests.Remote, Time = new PlayoutLinkTests.LinkClock(T0), Jitter = () => 0.5, Transport = PlayoutRules.Tcp, Self = SelfId,
        }));
        keyed.Start();
        var greeted = await tcp.Wait(e => Ev(e, "read") && Str(e, "line") == "HELLO Stagewatch");
        await Settle(tcp);
        Check(greeted is not null && !tcp.AllRead().Any(l => l.Contains(" LINK ", StringComparison.Ordinal)) && keyed.RecordNow().Proof != "mutual"
              && Found(tcp.AllRead(), Canaries(Pmk)) is null,
            $"peerlink scenarios: {Name} — on PlayMap's TCP wire, with a PlayMap app key, the link sends no LINK and no key ({string.Join(" | ", tcp.AllRead())}) {tcp.Faults}");
    }

    // ------------------------------------------------------------------ the rule

    /// <summary>
    /// The rule PlayoutLink decides LINK by (<see cref="PlayoutProof"/>), pure: never by a hello's cap; a key always, the token only
    /// inside TLS, nothing without a Show Bus id of its own, and never on PlayMap's line channel; what is proved with by kid; an
    /// enrolment checked and kept sealed by its kid alone; and the scope Patterns' map gives a playout command.
    /// </summary>
    static void LinkRule(Action<bool, string> Check)
    {
        var codeK = PeerLink.CodeK(PeerLink.Canon(Code)!);
        Check(PlayoutProof.KindOf(Ptk, null) == PlayoutProofKind.Key && PlayoutProof.KindOf(Pmk, null) == PlayoutProofKind.Key
              && PlayoutProof.KindOf(CodeTyped, null) == PlayoutProofKind.Code && PlayoutProof.KindOf("", null) == PlayoutProofKind.None
              && PlayoutProof.Kid(Ptk, null) == KidOf(Ptk) && PlayoutProof.Kid(CodeTyped, null) == "code"
              && PlayoutProof.K(CodeTyped, null) is { } k && k.SequenceEqual(codeK),
            "playout proof: an app key, a code or nothing, by its shape; LINK names a key by its kid and the token as code");
        var rows = new[]
        {
            (PlayoutProofKind.Key, false, false, SelfId, true), (PlayoutProofKind.Key, true, false, SelfId, true),
            (PlayoutProofKind.Code, false, false, SelfId, false), (PlayoutProofKind.Code, true, false, SelfId, true),
            (PlayoutProofKind.Enrolled, false, false, SelfId, true), (PlayoutProofKind.None, true, false, SelfId, false),
            (PlayoutProofKind.Key, true, true, SelfId, false), (PlayoutProofKind.Code, true, true, SelfId, false),
            (PlayoutProofKind.Key, true, false, null, false), (PlayoutProofKind.Key, true, false, "x", false),
        };
        Check(rows.All(r => PlayoutProof.OffersLink(r.Item1, r.Item2, r.Item3, r.Item4) == r.Item5),
            "playout proof: LINK with a key always, with the token only inside TLS, never without an id of its own, never on PlayMap's line channel");
        var t = PeerLink.Transcript("line", 1, 1, "patterns:" + PatternsId, "stagewatch:" + SelfId, "code", new string('a', 32), new string('b', 32), "tls:sha256:" + new string('c', 64))!;
        var enrolK = PeerLink.EnrolK(codeK, t);
        var named = PeerLink.Kid(enrolK);
        var e = PlayoutProof.Enrolled(named, codeK, t, "tls:sha256:" + new string('c', 64), "tag-1");
        Check(e is { } got && got.Kid == named && got.K.SequenceEqual(enrolK)
              && PlayoutProof.Enrolled(named, codeK, t, "plain", "tag-1") is null && PlayoutProof.Enrolled(new string('0', 16), codeK, t, "tls:sha256:" + new string('c', 64), "tag-1") is null
              && PlayoutEnrolment.Read(got.Sealable()) == got && PlayoutEnrolment.Read(got.Sealable().Replace(named, new string('0', 16), StringComparison.Ordinal)) is null
              && PlayoutEnrolment.Read("") is null && !got.ToString().Contains(Convert.ToHexStringLower(enrolK), StringComparison.Ordinal)
              && PlayoutProof.KindOf(CodeTyped, got) == PlayoutProofKind.Enrolled && PlayoutProof.Kid(CodeTyped, got) == named,
            "playout proof: an enrolled key is taken only over TLS and only as the kid the verdict named; kept sealed, read back whole, and named by its kid alone");
        Check(PlayoutProof.ScopeAllows(PlayoutRules.Patterns, "drive") && PlayoutProof.ScopeAllows(PlayoutRules.Patterns, "operate") && PlayoutProof.ScopeAllows(PlayoutRules.Patterns, null)
              && !PlayoutProof.ScopeAllows(PlayoutRules.Patterns, "follow") && !PlayoutProof.ScopeAllows(PlayoutRules.Patterns, "post") && PlayoutProof.ScopeAllows(PlayoutRules.PlayMap, "follow")
              && PlayoutProof.ScopeWords(PlayoutRules.Patterns, "follow", "Patterns").Contains("operator can raise its key on its Nodes page", StringComparison.Ordinal),
            "playout proof: a Patterns link at follow or post sends no playout command — every one is drive's — and says who raises it");
    }
}
