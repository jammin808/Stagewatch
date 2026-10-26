using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;

/// <summary>
/// The PeerLink kit's scenarios S8–S22 (Show Bus 13.20): the scripted peer as an attacker as well as honest, against Stagewatch's core —
/// its connector (<see cref="PeerLinkConnector"/>, carried by <see cref="CoreConnector"/>) on <c>line</c>, <c>playmap.lines.v1</c> and
/// <c>ws</c>, and its acceptor's parsers, proofs, session check and receipt store behind the kit's stand-in (<see cref="CoreAcceptor"/>).
/// Each check names what it holds and, where a row's other half is an acceptor's or a reader's Stagewatch does not have yet, the unit
/// that brings it.
/// </summary>
static partial class PeerLinkScenarios
{
    /// <summary>A peer command: <paramref name="basis"/> with <paramref name="more"/>'s fields set over it.</summary>
    static JsonObject With(object basis, object more)
    {
        var o = JsonSerializer.SerializeToNode(basis)!.AsObject();
        foreach (var (k, v) in JsonSerializer.SerializeToNode(more)!.AsObject().ToList()) o[k] = v?.DeepClone();
        return o;
    }

    /// <summary>
    /// The core linked to the peer: dialled at <paramref name="t"/>, both proofs verified, live — and on <c>ws</c> its show live, the
    /// verdict's frame read.
    /// </summary>
    static async Task<bool> Link(CoreConnector c, Leg leg, long t)
    {
        await c.Dial(t);
        return await c.Pump(t, () => c.M.State(t) == PeerLinkState.Live && c.M.Record(t).Proof == "mutual" && (leg.Carrier != PeerCarrier.Ws || c.M.Show == PeerShow.Live));
    }

    /// <summary>The connection on which the peer's acceptor verified a link, the <paramref name="nth"/> one from 1.</summary>
    static async Task<int> Accepted(ScriptedPeer peer, int nth = 1)
    {
        for (var i = 0; i < 50; i++)
        {
            var links = peer.Events.Where(e => Ev(e, "link") && Str(e, "role") == "acceptor").ToList();
            if (links.Count >= nth) return Int(links[nth - 1], "conn") ?? -1;
            await Task.Delay(20);
        }
        return -1;
    }

    /// <summary>A <c>set</c> on the peer's end "a"; whether the peer took it.</summary>
    static async Task<bool> Set(ScriptedPeer peer, object fields) => Ev(await peer.Ask(With(new { @do = "set", end = "a" }, fields)), "set");

    /// <summary>A command the core sends at <paramref name="t"/>, written and pumped until its first outcome (or <paramref name="until"/>).</summary>
    static async Task<string> Command(CoreConnector c, long t, Func<string, bool>? until = null)
    {
        var id = c.M.NextId();
        c.M.Send(t, id, "NOTE");
        await c.Flush(t);
        await c.Pump(t, () => until?.Invoke(id) ?? c.M.OutcomeOf(id) is not null);
        return id;
    }

    /// <summary>
    /// What the peer wrote on <paramref name="conn"/> to link, as it went: its LINK and LINK PROOF lines with their <c>@l&lt;n&gt;</c>
    /// ids, or its link message — the upgrade and anything written once the link was answered left out.
    /// </summary>
    static List<string> LinkSent(ScriptedPeer peer, int conn)
        => peer.On("wrote", conn).Select(e => Str(e, "line") ?? Str(e, "message") ?? "")
            .Where(x => x.Contains(" LINK ", StringComparison.Ordinal) || x.Contains("\"type\":\"link\"", StringComparison.Ordinal)).ToList();

    /// <summary>A JSON body as an object; an empty object when it is not one.</summary>
    static JsonElement Body(JsonElement answer)
    {
        try { return JsonDocument.Parse(Str(answer, "body") ?? "").RootElement.Clone(); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
    }

    static PeerCarrier CarrierOf(string carrier) => carrier == "line" ? PeerCarrier.Line : PeerCarrier.Ws;

    // ------------------------------------------------------------------ S8: a replayed proof, a reused nonce

    static async Task S8(Action<bool, string> Check, string script, Material kit)
    {
        // The acceptor's half, the peer connecting to the kit's stand-in, whose checks are the core's Transcript, ClientProof and
        // Verify. A LINK recorded on one connection — its cnonce, kid and client proof — is sent again as it was on the next; the
        // stand-in offers a fresh snonce there (Kit.Nonce), so T differs and the core's Verify refuses it. The same replay links at an
        // acceptor that offers its snonce again (PeerRules' control), so this refusal is the fresh snonce's. One proof a connection is
        // the stand-in's rule here; S8's row at Stagewatch's /ws acceptor, its snonce, refusals, words and table, comes with SW22.1a.
        foreach (var carrier in new[] { "line", "ws" })
        {
            var name = "S8 " + carrier + ", the peer connecting";
            await using var acceptor = Stand(CarrierOf(carrier), kit.B, [(PeerLink.KeyK(Swk), Kid(Swk), "key")]);
            await using var peer = Peer(script);
            var first = await peer.Ask(new { @do = "dial", carrier, port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main });
            var conn1 = Int(first, "conn") ?? -1;
            var linked = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn1);
            var again = await peer.Ask(new { @do = "dial", carrier, port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main, attack = new { replay = conn1 } });
            var conn2 = Int(again, "conn") ?? -1;
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn2);
            var sent1 = LinkSent(peer, conn1);
            var sent2 = LinkSent(peer, conn2);
            Check(linked is not null && sent1.Count > 0 && sent2.SequenceEqual(sent1) && refused is { } r && Str(r, "class") == "refused"
                  && Str(r, "words") == PeerLink.ProofWords && acceptor.Entries.Count(x => x.StartsWith("link ", StringComparison.Ordinal)) == 1
                  && acceptor.Entries.Contains("refused " + PeerLink.ProofWords) && peer.Events.Any(e => Ev(e, "attack") && Str(e, "mode") == "replay" && Int(e, "conn") == conn2),
                $"peerlink scenarios: {name} — a LINK replayed as it was sent (its cnonce, kid and proof, byte for byte) meets the stand-in's fresh snonce, another T, and the core's Verify refuses it ({string.Join("; ", acceptor.Entries)} | {string.Join(" | ", sent2)}) {peer.Faults}");
            var twice = await peer.Ask(new { @do = "dial", carrier, port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main, attack = new { twice = true } });
            var conn3 = Int(twice, "conn") ?? -1;
            var proved = await peer.Wait(e => Ev(e, "attack") && Str(e, "mode") == "twice" && Int(e, "conn") == conn3);
            var second = carrier == "line" ? await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn3) : null;
            await Settle(peer);
            Check(proved is not null && acceptor.Entries.Count(x => x.StartsWith("link ", StringComparison.Ordinal)) == 2
                  && (carrier != "line" || (second is { } s2 && Str(s2, "class") == "malformed")),
                $"peerlink scenarios: {name} — a second proof under one snonce links nothing more: each nonce answers one proof (the stand-in's rule; SW22.1a's acceptor) ({string.Join("; ", acceptor.Entries)})");
            // A forged kid: the peer names the kid of a key the stand-in holds and proves with another; the stand-in checks the proof
            // with the named kid's K, and the core's Verify refuses it in 13.6.3's words, noting no link.
            var forged = await peer.Ask(new { @do = "dial", carrier, port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Stranger), meant = Main, attack = new { kid = Kid(Swk) } });
            var conn4 = Int(forged, "conn") ?? -1;
            var forgedRefused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn4);
            Check(forgedRefused is { } f && Str(f, "class") == "refused" && Str(f, "words") == PeerLink.ProofWords
                  && LinkSent(peer, conn4) is [var offer, ..] && offer.Contains(Kid(Swk), StringComparison.Ordinal)
                  && acceptor.Entries.Count(x => x.StartsWith("link ", StringComparison.Ordinal)) == 2 && acceptor.Entries.Count(x => x == "refused " + PeerLink.ProofWords) == 2
                  && peer.Events.Any(e => Ev(e, "attack") && Str(e, "mode") == "kid" && Int(e, "conn") == conn4),
                $"peerlink scenarios: {name} — a forged kid (a known key's kid, proved with another key) is refused by the core's Verify in 13.6.3's words, and no link is noted ({string.Join("; ", acceptor.Entries)}) {peer.Faults}");
        }

        // The connector's half: an acceptor that offers the snonce it offered before and replays the verdict a link verified with. The
        // kit's carrier makes a fresh cnonce for each connection (CoreConnector's Kit.Nonce), so T differs; it checks the acceptor's
        // proof with the core's Transcript, ServerProof and Verify, and the core takes that check's verdict: nothing taken, pinned or
        // floored, and the dial fails in 13.6.3's words. PlayoutLink's carrier (SW20.4a, SW23.1) makes its own cnonce, fresh for each
        // connection, and takes this half over.
        foreach (var leg in Legs)
        {
            var name = "S8 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, With(ListenAs(leg, kit.A, [Key(kit, leg.Secret)]),
                new { attack = new { snonce = "00112233445566778899aabbccddeeff", verdict = "replay" } }));
            await using var honest = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            var first = await Link(honest, leg, T0);
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await c.Dial(T0);
            await c.Pump(T0, () => !c.M.KeepsConnection(T0));
            var rec = c.M.Record(T0);
            var replayed = await peer.Wait(e => Ev(e, "attack") && Str(e, "mode") == "replay");
            Check(first && replayed is not null && rec is { State: "down", Proof: "none", Tls: "none" } && rec.Words.Contains(PeerLink.ProofWords, StringComparison.Ordinal)
                  && c.M.Kept is { Pin: null, Floor: null } && !c.M.Follows(T0) && c.Credentials.SetEquals(["proof"]),
                $"peerlink scenarios: {name} — a reused snonce and a replayed verdict: the kit carrier's fresh cnonce makes another T, the acceptor's proof fails the core's Verify, and the core takes nothing, pins nothing and floors nothing ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S9: a server proof offered as a client proof

    static async Task S9(Action<bool, string> Check, string script, Material kit)
    {
        // The acceptor's half: the peer, holding the right key, sends its server proof as its client proof; the core's ClientProof is
        // over "client\n" ‖ T, so it does not verify. S9's row at Stagewatch's /ws acceptor comes with SW22.1a.
        foreach (var carrier in new[] { "line", "ws" })
        {
            var name = "S9 " + carrier + ", the peer connecting";
            await using var acceptor = Stand(CarrierOf(carrier), kit.B, [(PeerLink.KeyK(Swk), Kid(Swk), "key")]);
            await using var peer = Peer(script);
            var dial = await peer.Ask(new { @do = "dial", carrier, port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main, attack = new { role = true } });
            var conn = Int(dial, "conn") ?? -1;
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
            Check(refused is { } r && Str(r, "class") == "refused" && Str(r, "words") == PeerLink.ProofWords && acceptor.Entries.Contains("refused " + PeerLink.ProofWords)
                  && !acceptor.Entries.Any(x => x.StartsWith("link ", StringComparison.Ordinal)) && peer.Events.Any(e => Ev(e, "attack") && Str(e, "mode") == "role"),
                $"peerlink scenarios: {name} — a server proof offered as a client proof is refused by the core's Verify ({string.Join("; ", acceptor.Entries)}) {peer.Faults}");
        }

        // The connector's half: an acceptor that answers with a verdict whose proof is the client proof it was sent, reflected. The kit's
        // carrier checks the acceptor's proof with the core's ServerProof — over "server\n" ‖ T — and Verify, and the core takes that
        // check's verdict: nothing taken, pinned or floored. PlayoutLink's carrier (SW20.4a, SW23.1) takes this half over.
        foreach (var leg in Legs)
        {
            var name = "S9 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, With(ListenAs(leg, kit.A, [Key(kit, leg.Secret)]), new { attack = new { verdict = "reflect" } }));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await c.Dial(T0);
            await c.Pump(T0, () => !c.M.KeepsConnection(T0));
            var rec = c.M.Record(T0);
            var reflected = await peer.Wait(e => Ev(e, "attack") && Str(e, "mode") == "reflect");
            Check(reflected is not null && rec is { State: "down", Proof: "none" } && rec.Words.Contains(PeerLink.ProofWords, StringComparison.Ordinal)
                  && c.M.Kept is { Pin: null, Floor: null } && !c.M.Follows(T0) && c.Credentials.SetEquals(["proof"]),
                $"peerlink scenarios: {name} — a client proof reflected as the acceptor's fails the core's ServerProof and Verify: nothing taken, pinned or floored ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S10: S replayed in clear, after the link, and with another lid

    static async Task S10(Action<bool, string> Check, string script, Material kit)
    {
        // The acceptor's half (Stagewatch takes side calls), the core's CheckSession behind the kit's stand-in, reached at this machine's
        // own LAN address so a plain call is from another machine by 13.1's rule. S10's row at Stagewatch's /ws acceptor comes with
        // SW22.1a, and the side calls' S it checks with SW22.1b.
        var lan = Lan();
        Check(lan is not null, "peerlink scenarios: S10 — this machine has a LAN address that is not loopback, for S in clear from another machine");
        if (lan is null) return;
        await using var acceptor = Stand(PeerCarrier.Ws, kit.B, [(PeerLink.KeyK(Swk), Kid(Swk), "key")], lan);
        await using var peer = Peer(script);
        var dial = await peer.Ask(new { @do = "dial", carrier = "ws", host = lan.ToString(), port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main });
        var conn = Int(dial, "conn") ?? -1;
        var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        var lid = link is { } l ? Str(l, "lid") ?? "" : "";
        var honest = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
        var otherLid = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { lid = "0123456789abcdef" } });
        var stillOk = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
        Check(Int(honest, "status") == 200 && Int(otherLid, "status") == 401 && True(Body(otherLid), "relink") && Int(stillOk, "status") == 200,
            $"peerlink scenarios: S10 — a side call with another lid is 401 relink, and ends nothing: the link's own S is taken after it ({otherLid.GetRawText()}) {peer.Faults}");
        var clear = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { clear = true } });
        var ended = await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == conn);
        Check(Int(clear, "status") == 401 && True(Body(clear), "relink") && Str(Body(clear), "error")?.Contains("in clear from another machine", StringComparison.Ordinal) == true
              && acceptor.Entries.Contains("ended " + lid) && ended is { } y && Str(y, "by") == "other",
            $"peerlink scenarios: S10 — S replayed in clear from another machine is refused by the core's CheckSession, and the link is ended ({string.Join("; ", acceptor.Entries)})");
        var after = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { closed = true } });
        var refusedHonest = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
        Check(Int(after, "status") == 401 && True(Body(after), "relink") && Str(Body(after), "error") == "the link ended"
              && Ev(refusedHonest, "error") && Says(refusedHonest, "call: the link is not live and verified"),
            $"peerlink scenarios: S10 — S after its link closed is 401 relink, the link ended; the honest peer carries no S on a link that is not live ({after.GetRawText()})");
    }

    // ------------------------------------------------------------------ S11: silence, redial, the self-gap

    static async Task S11(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S11 " + leg.Label;
            {
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
                await Set(peer, new { pongs = false });
                var js = new Queue<double>([0, 0.999, 0.5, 0.25]);
                await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs, jitter: () => js.Count > 0 ? js.Dequeue() : 0.5);
                var up = await Link(c, leg, T0);
                var conn = await Accepted(peer);
                // The evaluator runs every second (link.check.max.ms) while the peer says nothing at all.
                var seen = new Dictionary<long, string>();
                for (var t = 1000L; t <= 12_000; t += 1000)
                {
                    c.M.Evaluate(T0 + t);
                    if (t == 6000)
                    {
                        seen[6000] = c.M.Record(T0 + 6000).State;
                        seen[6001] = c.M.Record(T0 + 6001).State + " " + c.M.Record(T0 + 6001).Light;
                    }
                }
                var at12000 = c.M.Record(T0 + 12_000).State;
                var drop = c.M.Evaluate(T0 + 12_001);
                await c.Flush(T0 + 12_001);
                var rec = c.M.Record(T0 + 12_001);
                var closed = await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == conn);
                Check(up && seen[6000] == "live" && seen[6001] == "stale amber" && at12000 == "stale" && drop.Closed && rec is { State: "down", Light: "red", Attempt: 1 }
                      && closed is { } y && Str(y, "by") == "other" && c.M.WaitMs == PeerLink.RedialWait(0, 0),
                    $"peerlink scenarios: {name} — silence: live at link.stale.ms, stale and amber past it and at link.drop.ms, down past it; the core closes the socket and waits 13.15.1's first wait ({rec.ToJson()}) {peer.Faults}");
                // Nothing answers now: each failed dial waits 13.15.1's next wait, j injected — first 1200 (n 1, j 0.999), 2000, 3600.
                await peer.Ask(new { @do = "close", end = "a" });
                var at = T0 + 12_001;
                var waits = new List<string>();
                var ok = true;
                foreach (var (n, j) in new[] { (1, 0.999), (2, 0.5), (3, 0.25) })
                {
                    ok &= c.M.DialDue(at + c.M.WaitMs - 1) is null;
                    at += c.M.WaitMs;
                    ok &= await c.Dial(at) is not null;
                    var want = PeerLink.RedialWait(n, j);
                    var b = Math.Min(PeerLink.RedialMaxMs, PeerLink.RedialFirstMs << n);
                    ok &= c.M.WaitMs == want && want >= Math.Round(b * 0.8) && want <= Math.Round(b * 1.2) && c.M.Attempt == n + 1;
                    waits.Add(c.M.WaitMs.ToString(CultureInfo.InvariantCulture));
                }
                Check(ok && waits.SequenceEqual(["1200", "2000", "3600"]),
                    $"peerlink scenarios: {name} — each redial waits 13.15.1's wait, within ±20 % of its base, and dials no sooner ({string.Join(", ", waits)})");
            }
            {
                // The self-gap: the evaluator itself paused 9 s. It pings every link at once and moves none to down for link.selfgap.ms.
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name + ", the self-gap", ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
                await Set(peer, new { pongs = false });
                await using var g = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
                await Link(g, leg, T0);
                g.M.Evaluate(T0 + 5000);
                var gap = g.M.Evaluate(T0 + 14_000);
                if (gap.PingedAll) await g.Ping(T0 + 14_000, T0 + 14_000);
                var pinged = await peer.Wait(e => Ev(e, "read") && (Str(e, "line") ?? Str(e, "message") ?? "").Contains("ping", StringComparison.OrdinalIgnoreCase));
                var during = g.M.Record(T0 + 14_000).State;
                var still = g.M.Record(T0 + 15_999).State;
                var after = g.M.Evaluate(T0 + 16_000);
                await g.Flush(T0 + 16_000);
                Check(gap is { PingedAll: true, Closed: false, GapMs: 9000 } && pinged is not null && during == "stale" && still == "stale" && after.Closed
                      && g.M.Record(T0 + 16_000).State == "down",
                    $"peerlink scenarios: {name} — the self-gap: after its own 9 s pause the evaluator pings every link, shows stale past link.drop.ms, and closes only link.selfgap.ms later, the peer still silent");
                // The same pause, the peer answering the ping: heard, so live, and never down.
                await Set(peer, new { pongs = true });
                await using var h = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
                await Link(h, leg, T0);
                h.M.Evaluate(T0 + 5000);
                var gap2 = h.M.Evaluate(T0 + 14_000);
                await h.Ping(T0 + 14_000, T0 + 14_000);
                var heard = await h.Pump(T0 + 14_000, () => h.M.State(T0 + 14_000) == PeerLinkState.Live);
                var kept = h.M.Evaluate(T0 + 16_000);
                Check(gap2.PingedAll && heard && !kept.Closed && h.M.Record(T0 + 16_000).State == "live",
                    $"peerlink scenarios: {name} — the self-gap's ping answered: the link is heard, live, and not closed for the node's own pause");
            }
        }
    }

    // ------------------------------------------------------------------ S12: stuck

    static async Task S12(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in new[] { Line, LinesV1 })
        {
            var name = "S12 " + leg.Label;
            {
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name, With(ListenAs(leg, kit.A, [Key(kit, leg.Secret)]), new { can = new[] { "stuck" }, stuckMs = 3000 }));
                await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
                var up = await Link(c, leg, T0);
                var verdict = c.LastVerdict;
                await Set(peer, new { stuck = true });
                await c.Ping(T0 + 10, T0 + 10);
                var stuck = await c.Pump(T0 + 10, () => c.M.Show == PeerShow.Stuck);
                var rec = c.M.Record(T0 + 10);
                var id = c.M.NextId();
                var sent = c.M.Send(T0 + 10, id, "NOTE");
                Check(up && verdict is { } v && v.TryGetProperty("stuckMs", out var ms) && ms.GetInt32() == 3000 && stuck && rec is { State: "live", Show: "stuck", Light: "amber" }
                      && rec.Words.EndsWith("says its show is stuck", StringComparison.Ordinal) && sent == PeerOutcome.NotSent && c.M.WordsOf(id) == "Hall A says its show is stuck — not sent",
                    $"peerlink scenarios: {name} — with feature stuck, PONG … STUCK makes the show stuck: the link live, amber, and no command sent ({rec.ToJson()}) {peer.Faults}");
                await Set(peer, new { stuck = false });
                await c.Ping(T0 + 20, T0 + 20);
                var live = await c.Pump(T0 + 20, () => c.M.Show == PeerShow.Live);
                Check(live && c.M.Record(T0 + 20) is { Show: "live", Light: "green" }, $"peerlink scenarios: {name} — a pong without STUCK makes the show live again");
            }
            {
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name + ", no feature", ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
                await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
                await Link(c, leg, T0);
                await Set(peer, new { stuck = true });
                await c.Ping(T0 + 10, T0 + 10);
                var sampled = await c.Pump(T0 + 10, () => c.M.Clock.Samples == 1);
                Check(sampled && c.M.Show == PeerShow.Unknown && !peer.AllRead().Concat(peer.Events.Where(e => Ev(e, "wrote")).Select(e => Str(e, "line") ?? "")).Any(x => x.EndsWith(" STUCK", StringComparison.Ordinal)),
                    $"peerlink scenarios: {name}, no feature — an acceptor whose verdict lists no stuck never says it, and the show stays unknown: the link claims nothing");
            }
        }
        {
            // ws: the beat. Only a state frame clears a stuck show; neither a pong nor a plain beat does.
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S12 ws", ListenAs(Ws, kit.A, [Key(kit, Swk)]));
            await using var c = Connector(PeerCarrier.Ws, Swk, false, "main", MainId, port);
            var up = await Link(c, Ws, T0);
            var conn = await Accepted(peer);
            await Set(peer, new { stuck = true });
            var stuck = await c.Pump(T0 + 10, () => c.M.Show == PeerShow.Stuck);
            await c.Ping(T0 + 20, T0 + 20);
            var pong = await c.Pump(T0 + 20, () => c.M.Clock.Samples == 1);
            var afterPong = c.M.Show;
            await peer.Ask(new { @do = "send", conn, message = new { type = "beat" } });
            var beat = await c.Pump(T0 + 30, () => c.M.Record(T0 + 30).HeardMs == 0);
            var afterBeat = c.M.Show;
            await Set(peer, new { stuck = false });
            var live = await c.Pump(T0 + 40, () => c.M.Show == PeerShow.Live);
            Check(up && stuck && pong && afterPong == PeerShow.Stuck && beat && afterBeat == PeerShow.Stuck && live && c.M.Record(T0 + 40).Light == "green",
                $"peerlink scenarios: S12 ws — a beat with live false makes the show stuck; a pong and a plain beat leave it stuck; the next state frame makes it live ({c.M.Record(T0 + 40).ToJson()}) {peer.Faults}");
        }
        // S12's emitting half — an acceptor's own stuck beat and STUCK pong — is Stagewatch's /ws acceptor's: its row comes with SW22.1b,
        // and feature stuck itself, its stuckMs Stagewatch's StuckAfter, with SW22.1c. Nothing of it is run here.
    }

    // ------------------------------------------------------------------ S13: a forged or unmatched pong; a plain clock

    static async Task S13(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S13 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await Link(c, leg, T0);
            var conn = await Accepted(peer);
            // A pong that answers a t1 the core never sent; then one sent unasked; then one whose t3 comes before its t2. The first two
            // would be admitted as samples by their stamps alone (13.11.2): only the match refuses them.
            await Set(peer, new { pongs = "unmatched" });
            await c.Ping(T0 + 10, T0 + 10);
            var unmatched = await c.Pump(T0 + 12, () => c.M.Record(T0 + 12).Counts.Unmatched == 1);
            var forgedT1 = (T0 + 15).ToString(CultureInfo.InvariantCulture);
            var forgedT2 = (T0 + 16).ToString(CultureInfo.InvariantCulture);
            if (leg.Carrier == PeerCarrier.Ws) await peer.Ask(new { @do = "send", conn, message = new { type = "pong", t = T0 + 15, t2 = T0 + 16, t3 = T0 + 16 } });
            else await peer.Ask(new { @do = "send", conn, line = $"OK PONG {forgedT1} {forgedT2} {forgedT2}" });
            var forged = await c.Pump(T0 + 20, () => c.M.Record(T0 + 20).Counts.Unmatched == 2);
            await Set(peer, new { pongs = "backwards" });
            await c.Ping(T0 + 30, T0 + 30);
            var backwards = await c.Pump(T0 + 30, () => c.M.Record(T0 + 30).HeardMs == 0);
            Check(unmatched && forged && backwards && c.M.Clock.Samples == 0 && c.M.Record(T0 + 30).Counts.Unmatched == 2 && c.M.State(T0 + 30) == PeerLinkState.Live,
                $"peerlink scenarios: {name} — an unmatched pong, a forged one sent unasked and one with t3 before t2 are no clock sample; the first two counted unmatched, the link live ({c.M.Record(T0 + 30).ToJson()}) {peer.Faults}");
            // A lying clock: matched and admitted, it is believed — and shown CLOCKS APART, amber.
            await Set(peer, new { pongs = true, clock = T0 + 40 + 5_000_000 });
            await c.Ping(T0 + 40, T0 + 40);
            var lied = await c.Pump(T0 + 40, () => c.M.Clock.Samples == 1);
            var rec = c.M.Record(T0 + 40);
            Check(lied && c.M.Clock.Apart && rec.Flags.Contains("apart") && rec.Light == "amber",
                $"peerlink scenarios: {name} — a clock 5000 s off is believed as measured and shown CLOCKS APART, amber ({rec.ToJson()})");
        }
        foreach (var leg in new[] { Line, LinesV1 })
        {
            // A plain line off this machine: its clock is shown, never used to schedule AT (13.11.6); over TLS the same sample schedules (S1).
            var name = "S13 " + leg.Label + ", plain";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)], tls: "plain"));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs, timed: 6);
            var up = await Link(c, leg, T0);
            await c.Ping(T0 + 20, T0 + 20);
            var sampled = await c.Pump(T0 + 30, () => c.M.Clock.Samples == 1);
            var rec = c.M.Record(T0 + 30);
            Check(up && sampled && rec is { Tls: "none", Light: "amber" } && rec.Flags.Contains("plain") && rec.Flags.Contains("unfit") && c.M.Clock.Believed is { ErrMs: <= 6 }
                  && !c.M.CanSchedule(T0 + 30, 6),
                $"peerlink scenarios: {name} — a clock measured over a plain link off this machine, however fit, never schedules a timed command: flags plain and unfit ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S14: receipts

    static async Task S14(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S14 " + leg.Label;
            await using var peer = Peer(script);
            object Ends(string inst, int port) => With(ListenAs(leg, kit.A, [Key(kit, leg.Secret)]), new { can = new[] { "final", "receipts" }, inst, port });
            var port = await Listen(Check, peer, name, Ends("a41f09c2", 0));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            var up = await Link(c, leg, T0);
            var applied = await Command(c, T0 + 10);
            await Set(peer, new { answer = "accepted" });
            var accepted = await Command(c, T0 + 20);
            var waiting = c.M.OutcomeOf(accepted) == PeerOutcome.Accepted && c.M.InFlight == 1;
            await Set(peer, new { final = "applied" });
            var final = await c.Pump(T0 + 30, () => c.M.OutcomeOf(accepted) == PeerOutcome.Applied);
            await Set(peer, new { answer = "refused" });
            var refused = await Command(c, T0 + 40);
            await Set(peer, new { answer = "applied", late = 1500 });
            var late = await Command(c, T0 + 50);
            // The receipt reaches the core on the socket, the peer's events on its stdout through a reader of their own: the count is
            // read once the last command's event is in, and the peer writes its events in order, so the three before it are in too.
            var ranLate = await peer.Wait(e => Ev(e, "command") && Str(e, "id") == late);
            Check(up && c.M.OutcomeOf(applied) == PeerOutcome.Applied && waiting && final && c.M.InFlight == 0 && c.M.OutcomeOf(refused) == PeerOutcome.Refused
                  && c.M.WordsOf(refused) == "the scripted peer refuses it" && c.M.OutcomeOf(late) == PeerOutcome.Applied && c.M.Record(T0 + 50).Counts.Late == 1
                  && ranLate is not null && peer.Events.Count(e => Ev(e, "command")) == 4,
                $"peerlink scenarios: {name} — receipts read by 13.12.3: applied; accepted, waiting in flight, then applied by its final; refused with its words; applied late, counted ({c.M.Record(T0 + 50).ToJson()}) {peer.Faults}");

            // A drop after the write: unconfirmed. Relinked to the same acceptor (its inst), RECEIPTS asks, and its record says applied.
            await Set(peer, new { late = 0, answer = "drop" });
            var dropped = await Command(c, T0 + 60, id => c.M.OutcomeOf(id) is not null && !c.M.KeepsConnection(T0 + 60));
            var droppedWords = c.M.WordsOf(dropped);
            var wasUnconfirmed = c.M.OutcomeOf(dropped) == PeerOutcome.Unconfirmed;
            await Set(peer, new { answer = "applied" });
            var at = T0 + 60 + c.M.WaitMs;
            await c.Dial(at);
            var resolved = await c.Pump(at, () => c.M.OutcomeOf(dropped) == PeerOutcome.Applied);
            var asked = c.Wrote.Any(w => w.Contains("RECEIPTS " + dropped, StringComparison.Ordinal) || (w.Contains("\"receipts\"", StringComparison.Ordinal) && w.Contains(dropped, StringComparison.Ordinal)));
            Check(wasUnconfirmed && droppedWords == "the link dropped before Hall A answered" && resolved && asked
                  && peer.Events.Count(e => Ev(e, "command") && Str(e, "id") == dropped) == 1,
                $"peerlink scenarios: {name} — a drop after the write is unconfirmed; relinked to the same inst, RECEIPTS asks for it and it is applied, run once ({string.Join(" | ", c.Wrote.TakeLast(3))}) {peer.Faults}");

            // Accepted and still running when the link drops: unconfirmed. Relinked to the same inst, RECEIPTS asks for it and the
            // acceptor's record answers it accepted — never unknown — which leaves it unconfirmed: only a final receipt moves it.
            await Set(peer, new { answer = "accepted" });
            var running = await Command(c, at + 10);
            var wasAccepted = c.M.OutcomeOf(running) == PeerOutcome.Accepted;
            await peer.Ask(new { @do = "close", conn = await Accepted(peer, 2) });
            var downAt = at + 20;
            var dropsUnconfirmed = await c.Pump(downAt, () => c.M.OutcomeOf(running) == PeerOutcome.Unconfirmed);
            await Set(peer, new { answer = "applied" });
            var at3 = downAt + c.M.WaitMs;
            await c.Dial(at3);
            var stillRuns = await c.Pump(at3, () => peer.Events.Any(e => Ev(e, "wrote") && (Str(e, "line") ?? Str(e, "message") ?? "") is var w
                && w.Contains("\"" + running + "\":{\"ok\":true,\"stage\":\"accepted\"", StringComparison.Ordinal)), 1000);
            await c.Pump(at3, () => false, 100);
            Check(wasAccepted && dropsUnconfirmed && stillRuns && c.M.OutcomeOf(running) == PeerOutcome.Unconfirmed
                  && peer.Events.Count(e => Ev(e, "command") && Str(e, "id") == running) == 1,
                $"peerlink scenarios: {name} — accepted, then the link dropped: unconfirmed; RECEIPTS is answered accepted for an id that still runs, and it stays unconfirmed, run once ({c.M.WordsOf(running)}) {peer.Faults}");

            // Silent: no answer within receipt.final.ms is unconfirmed (an answer at 30000 ms still counts); the 33rd is not sent.
            await Set(peer, new { answer = "silent" });
            var t1 = at3 + 10;
            var ids = new List<string>();
            for (var i = 0; i < PeerLink.InflightMax + 1; i++)
            {
                var id = c.M.NextId();
                c.M.Send(t1, id, "NOTE");
                ids.Add(id);
            }
            await c.Flush(t1);
            var all = await peer.Wait(e => Ev(e, "command") && Str(e, "id") == ids[PeerLink.InflightMax - 1]);
            var inFlight = c.M.InFlight;
            c.M.State(t1 + PeerLink.ReceiptFinalMs);
            var stillWaits = c.M.OutcomeOf(ids[0]) is null;
            c.M.State(t1 + PeerLink.ReceiptFinalMs + 1);
            Check(all is not null && inFlight == PeerLink.InflightMax && c.M.OutcomeOf(ids[^1]) == PeerOutcome.NotSent
                  && c.M.WordsOf(ids[^1]) == "32 commands are waiting on Hall A's answers" && stillWaits
                  && ids.Take(PeerLink.InflightMax).All(id => c.M.OutcomeOf(id) == PeerOutcome.Unconfirmed && c.M.WordsOf(id) == "no answer in 30 s — it may have run")
                  && !peer.Events.Any(e => Ev(e, "command") && Str(e, "id") == ids[^1]),
                $"peerlink scenarios: {name} — 32 commands wait, the 33rd is not sent; with no answer each waits through receipt.final.ms and is unconfirmed past it");

            // The acceptor restarted (a new inst, its record gone): RECEIPTS is answered unknown, and they stay unconfirmed.
            var closeAt = t1 + PeerLink.ReceiptFinalMs + 1;
            c.M.Evaluate(closeAt);
            await c.Flush(closeAt);
            await peer.Ask(new { @do = "close", end = "a" });
            var again = await Listen(Check, peer, name + ", restarted", Ends("b7c3d9e1", port));
            var at2 = closeAt + c.M.WaitMs;
            var wrote = c.Wrote.Count;
            await c.Dial(at2);
            var relinked = await c.Pump(at2, () => c.M.State(at2) == PeerLinkState.Live && c.M.Record(at2).Proof == "mutual");
            var answered = await c.Pump(at2, () => peer.Events.Any(e => Ev(e, "wrote") && (Str(e, "line") ?? Str(e, "message") ?? "").Contains("\"b7c3d9e1\"", StringComparison.Ordinal)
                && (Str(e, "line") ?? Str(e, "message") ?? "").Contains("unknown", StringComparison.Ordinal)), 1000);
            await c.Pump(at2, () => false, 200);
            Check(again == port && relinked && answered && c.Wrote.Skip(wrote).Any(w => w.Contains("RECEIPTS", StringComparison.Ordinal) || w.Contains("\"receipts\"", StringComparison.Ordinal))
                  && ids.Take(PeerLink.InflightMax).All(id => c.M.OutcomeOf(id) == PeerOutcome.Unconfirmed),
                $"peerlink scenarios: {name} — relinked to a restarted acceptor (another inst), RECEIPTS is answered unknown and the commands stay unconfirmed");
        }

        {
            // Feature expire: a command read later than the acceptor's expireMs is refused and never run (13.12.5).
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S14 line, expire", With(ListenAs(Line, kit.A, [Key(kit, Ptk)]), new { can = new[] { "expire" }, expireMs = 2000 }));
            await using var c = Connector(PeerCarrier.Line, Ptk, false, "patterns", PatternsId, port);
            var up = await Link(c, Line, T0);
            await Set(peer, new { late = 2500 });
            var expired = await Command(c, T0 + 10);
            var said = await peer.Wait(e => Ev(e, "expired") && Str(e, "id") == expired);
            Check(up && c.LastVerdict is { } v && v.TryGetProperty("expireMs", out var ms) && ms.GetInt32() == 2000 && said is not null
                  && c.M.OutcomeOf(expired) == PeerOutcome.Refused && c.M.WordsOf(expired).StartsWith("expired — read 2500 ms late", StringComparison.Ordinal)
                  && !peer.Events.Any(e => Ev(e, "command")),
                $"peerlink scenarios: S14 line, expire — a command read past the verdict's expireMs is refused, expired, and not run ({c.M.WordsOf(expired)}) {peer.Faults}");
        }

        {
            // The peer's own acceptor answers a refused command's repeat in 13.12.6's line form, which the core's 13.12.3 reads as refused.
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S14 line, a refused repeat", With(ListenAs(Line, kit.A, [Key(kit, Ptk)]), new { can = new[] { "receipts" } }));
            await Set(peer, new { answer = "refused" });
            var dial = await peer.Ask(new { @do = "dial", carrier = "line", port, tls = true, node = Main, key = Key(kit, Ptk), meant = Patterns });
            var conn = Int(dial, "conn") ?? -1;
            await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector" && Int(e, "conn") == conn);
            for (var i = 0; i < 2; i++) await peer.Ask(new { @do = "send", conn, line = "@s1-11aa22bb NOTE twice" });
            var repeat = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "line") ?? "").Contains("\"repeat\":true", StringComparison.Ordinal));
            var line = repeat is { } r ? Str(r, "line") ?? "" : "";
            var reading = PeerLink.ReadLine(line);
            Check(line == "OK @s1-11aa22bb {\"ok\":false,\"error\":\"the scripted peer refuses it\",\"repeat\":true}"
                  && reading is { Id: "s1-11aa22bb", Reading: { Outcome: PeerOutcome.Refused, Repeat: true } read }
                  && read.Words == "the scripted peer refuses it — answered from its record — not run again" && peer.Events.Count(e => Ev(e, "command")) == 1,
                $"peerlink scenarios: S14 line — a refused command's repeat is OK @id {{\"ok\":false,…,\"repeat\":true}}, read refused by the core, run once ({line}) {peer.Faults}");
        }

        // The acceptor's half: the peer, as an attacker, sends request ids twice; the core's PeerReceipts behind the stand-in runs each
        // once — one applied, one refused — and answers the second from its record with "repeat":true in 13.12.6's form on each
        // carrier, which the core's 13.12.3 reads back; a RECEIPTS ask is answered from it, in its form on each carrier. S14's row at
        // Stagewatch's /ws acceptor comes with SW22.1b, and feature receipts, the store on /ws, with SW22.1c.
        foreach (var carrier in new[] { "line", "ws" })
        {
            var name = "S14 " + carrier + ", the peer connecting";
            await using var acceptor = Stand(CarrierOf(carrier), kit.B, [(PeerLink.KeyK(Swk), Kid(Swk), "key")]);
            await using var peer = Peer(script);
            var dial = await peer.Ask(new { @do = "dial", carrier, port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main });
            var conn = Int(dial, "conn") ?? -1;
            await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
            const string RanId = "s1-11aa22bb", RefusedId = "s2-11aa22bb", UnknownId = "s9-11aa22bb";
            foreach (var (id, verb) in new[] { (RanId, "NOTE"), (RefusedId, "REFUSE") })
                for (var i = 0; i < 2; i++)
                {
                    if (carrier == "line") await peer.Ask(new { @do = "send", conn, line = "@" + id + " " + verb + " once" });
                    else await peer.Ask(new { @do = "send", conn, message = new { cmd = verb.ToLowerInvariant(), args = Array.Empty<string>(), id } });
                }
            if (carrier == "line") await peer.Ask(new { @do = "send", conn, line = "@rq1 RECEIPTS " + RanId + " " + RefusedId + " " + UnknownId });
            else await peer.Ask(new { @do = "send", conn, message = new { cmd = "receipts", args = new[] { RanId, RefusedId, UnknownId }, id = "rq1" } });
            var receipts = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "line") ?? Str(e, "message") ?? "").Contains("\"receipts\":{", StringComparison.Ordinal));
            var answers = peer.ReadOn(conn).Where(x => x.Contains(RanId, StringComparison.Ordinal) || x.Contains(RefusedId, StringComparison.Ordinal)).ToList();
            string[] want = carrier == "line"
                ?
                [
                    "OK @" + RanId,
                    "OK @" + RanId + " {\"ok\":true,\"stage\":\"applied\",\"effect\":\"\",\"repeat\":true}",
                    "ERR @" + RefusedId + " " + CoreAcceptor.RefuseWords,
                    "OK @" + RefusedId + " {\"ok\":false,\"error\":\"" + CoreAcceptor.RefuseWords + "\",\"repeat\":true}",
                ]
                :
                [
                    "{\"type\":\"receipt\",\"id\":\"" + RanId + "\",\"ok\":true,\"stage\":\"applied\",\"effect\":\"\"}",
                    "{\"type\":\"receipt\",\"id\":\"" + RanId + "\",\"ok\":true,\"stage\":\"applied\",\"effect\":\"\",\"repeat\":true}",
                    "{\"type\":\"receipt\",\"id\":\"" + RefusedId + "\",\"ok\":false,\"error\":\"" + CoreAcceptor.RefuseWords + "\"}",
                    "{\"type\":\"receipt\",\"id\":\"" + RefusedId + "\",\"ok\":false,\"error\":\"" + CoreAcceptor.RefuseWords + "\",\"repeat\":true}",
                ];
            PeerReading? Read(string x) => carrier == "line" ? PeerLink.ReadLine(x)?.Reading : PeerLink.ReadReceipt(JsonDocument.Parse(x).RootElement);
            var read = answers.Take(4).Select(Read).ToList();
            Check(answers.Take(4).SequenceEqual(want) && read is [{ Outcome: PeerOutcome.Applied, Repeat: false }, { Outcome: PeerOutcome.Applied, Repeat: true },
                      { Outcome: PeerOutcome.Refused, Repeat: false }, { Outcome: PeerOutcome.Refused, Repeat: true }]
                  && acceptor.Entries.Count(x => x == "ran " + RanId) == 1 && acceptor.Entries.Count(x => x == "repeat " + RanId) == 1
                  && acceptor.Entries.Count(x => x == "ran " + RefusedId) == 1 && acceptor.Entries.Count(x => x == "repeat " + RefusedId) == 1,
                $"peerlink scenarios: {name} — ids sent twice run once at the core's PeerReceipts, the repeat answered from its record in 13.12.6's {carrier} form and read back by 13.12.3 ({string.Join(" | ", answers)}) {peer.Faults}");
            var receiptsWant = "{\"inst\":\"" + CoreAcceptor.Inst + "\",\"receipts\":{\"" + RanId + "\":{\"ok\":true,\"stage\":\"applied\",\"effect\":\"\"},\""
                + RefusedId + "\":{\"ok\":false,\"error\":\"" + CoreAcceptor.RefuseWords + "\"},\"" + UnknownId + "\":\"unknown\"}}";
            var text = receipts is { } rr ? Str(rr, "line") ?? Str(rr, "message") ?? "" : "";
            Check(text == (carrier == "line" ? "OK @rq1 " + receiptsWant : "{\"type\":\"receipt\",\"id\":\"rq1\",\"ok\":true," + receiptsWant[1..]),
                $"peerlink scenarios: {name} — RECEIPTS is answered from the core's record in 13.12.6's {carrier} form: the inst, each final receipt, unknown for an id it does not hold ({text})");
        }
    }

    // ------------------------------------------------------------------ S15: oversize; a slow reader

    static async Task S15(Action<bool, string> Check, string script, Material kit)
    {
        // The connector's half, the core: a message past link.read.min.bytes is counted and flagged with the link up, and the next is read.
        foreach (var leg in Legs)
        {
            var name = "S15 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await Link(c, leg, T0);
            var conn = await Accepted(peer);
            var pad = new string('x', KitIo.ReadMax + 10);
            if (leg.Carrier == PeerCarrier.Ws)
            {
                await peer.Ask(new { @do = "send", conn, message = new { type = "state", pad } });
                await peer.Ask(new { @do = "send", conn, message = new { type = "state", seq = 9 } });
            }
            else
            {
                await peer.Ask(new { @do = "send", conn, line = "STATE " + pad });
                await peer.Ask(new { @do = "send", conn, line = "STATE {\"rev\":9}" });
            }
            var counted = await c.Pump(T0 + 10, () => c.M.Record(T0 + 10).Counts.Oversize == 1);
            var next = await c.Pump(T0 + 20, () => c.M.Record(T0 + 20).HeardMs == 0);
            var rec = c.M.Record(T0 + 20);
            Check(counted && next && rec is { State: "live", Light: "amber" } && rec.Flags.Contains("oversize") && c.M.KeepsConnection(T0 + 20)
                  && peer.On("closed", conn).Count == 0,
                $"peerlink scenarios: {name} — a message past link.read.min.bytes is counted and flagged amber, the link stays up, and the next is read whole ({rec.ToJson()}) {peer.Faults}");
        }
        {
            // The peer as a slow reader: once linked it reads nothing more, so what the acceptor answers backs up on the wire. Cutting a
            // reader past link.replies.max is the /ws acceptor's: S15's row comes with SW22.1a, the reply bound with SW22.1c. The
            // stand-in keeps no reply bound, so this checks the peer's attack alone.
            await using var acceptor = Stand(PeerCarrier.Line, kit.B, [(PeerLink.KeyK(Swk), Kid(Swk), "key")]);
            await using var peer = Peer(script);
            var dial = await peer.Ask(new { @do = "dial", carrier = "line", port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main, attack = new { reads = false } });
            var conn = Int(dial, "conn") ?? -1;
            var deaf = await peer.Wait(e => Ev(e, "attack") && Str(e, "mode") == "reads" && Int(e, "conn") == conn);
            var mark = deaf is { } d ? Int(d, "n") ?? 0 : 0;
            await peer.Ask(new { @do = "send", conn, line = "@c1 NOTE unread" });
            var ran = false;
            for (var i = 0; i < 50 && !ran; i++)
            {
                ran = acceptor.Entries.Contains("ran c1");
                if (!ran) await Task.Delay(20);
            }
            await Settle(peer);
            Check(deaf is not null && ran && !peer.Events.Any(e => (Int(e, "n") ?? 0) > mark && Ev(e, "read") && Int(e, "conn") == conn),
                $"peerlink scenarios: S15 — the peer as a slow reader: linked, it reads no answer more (the stand-in ran c1 and answered it) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S16: an unpaired flood

    static async Task S16(Action<bool, string> Check, string script)
    {
        // The peer's flood: connections that come up and send nothing. Holding at most peerlink.unpaired.per.address of them per address
        // and closing each at peerlink.handshake.ms is an acceptor's; Stagewatch's /ws acceptor's (SW22.1a). This checks the peer's
        // attack alone, against a sink that accepts and reads.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = new List<TcpClient>();
        long bytes = 0;
        using var stop = new CancellationTokenSource();
        var accepting = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    lock (accepted) accepted.Add(client);
                    _ = Task.Run(async () =>
                    {
                        var buffer = new byte[256];
                        try
                        {
                            int n;
                            while ((n = await client.GetStream().ReadAsync(buffer, stop.Token)) > 0) Interlocked.Add(ref bytes, n);
                        }
                        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
                        {
                            // The flood's connection ended, or the sink stopped.
                        }
                    });
                }
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                // The sink stopped.
            }
        });
        try
        {
            await using var peer = Peer(script);
            const int Count = 6;
            for (var i = 0; i < Count; i++) await peer.Ask(new { @do = "dial", carrier = "line", port, tls = false, node = Patterns, attack = new { unpaired = true } });
            // The sink's accepts can trail the peer's connected events on a loaded machine: wait for them, a bounded while.
            var held = 0;
            for (var i = 0; i < 100 && held < Count; i++)
            {
                lock (accepted) held = accepted.Count;
                if (held < Count) await Task.Delay(20);
            }
            await Settle(peer);
            Check(peer.Events.Count(e => Ev(e, "attack") && Str(e, "mode") == "unpaired") == Count && held == Count && Interlocked.Read(ref bytes) == 0
                  && !peer.Events.Any(e => Ev(e, "wrote")),
                $"peerlink scenarios: S16 — the peer's flood: {Count} connections up, not one byte sent on any (the acceptor's bound and its close at peerlink.handshake.ms are SW22.1a's) {peer.Faults}");
        }
        finally
        {
            await stop.CancelAsync();
            listener.Stop();
            lock (accepted) foreach (var c in accepted) c.Dispose();
            await accepting.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    // ------------------------------------------------------------------ S17: a key revoked while live

    static async Task S17(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S17 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await Link(c, leg, T0);
            var before = leg.Carrier == PeerCarrier.Ws ? await c.SideCall(T0 + 5, "/api/context") : 200;
            var ran = await Command(c, T0 + 10);
            await Set(peer, new { revoke = Kid(leg.Secret) });
            const long At = T0 + 20;
            var held = await c.Pump(At, () => c.M.State(At) == PeerLinkState.Held);
            var rec = c.M.Record(At);
            var revoked = await peer.Wait(e => Ev(e, "revoked") && Str(e, "kid") == Kid(leg.Secret));
            var id = c.M.NextId();
            var sent = c.M.Send(At, id, "NOTE");
            var call = leg.Carrier == PeerCarrier.Ws ? await c.SideCall(At, "/api/context") : null;
            Check(before == 200 && c.M.OutcomeOf(ran) == PeerOutcome.Applied && held && revoked is not null && rec is { State: "held", Refusal: "key", Light: "red", Proof: "none" }
                  && c.M.HeldUntilWall(At) == At + PeerLink.HoldRefusedMs && sent == PeerOutcome.NotSent && call is null
                  && (leg.Carrier == PeerCarrier.Ws ? c.M.KeepsConnection(At) && rec.Flags.Contains("keyless") && c.M.Follows(At) : !c.M.KeepsConnection(At)),
                $"peerlink scenarios: {name} — a key revoked while live is refused at once: held 60 s, red, no command sent, its S no longer carried{(leg.Carrier == PeerCarrier.Ws ? ", the socket a public viewer" : ", the line ended")} ({rec.ToJson()}) {peer.Faults}");
            var due = c.M.DialDue(At + PeerLink.HoldRefusedMs - 1) is null && c.M.DialDue(At + PeerLink.HoldRefusedMs) == "tls";
            Check(due, $"peerlink scenarios: {name} — no dial while the hold runs; the key is proved again only when it ends");
        }
        // S17's acceptor half — a revocation ending its live links and their S at once — is Stagewatch's /ws acceptor's (SW22.1b);
        // nothing of it is run here.
    }

    // ------------------------------------------------------------------ S18: a key bound to another connector

    static async Task S18(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S18 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            // Another connector proves the key first, and so binds it to itself (13.5.2).
            var other = new { t = "main", id = "0f0f0f0f0f0f" };
            var first = await peer.Ask(new { @do = "dial", carrier = leg.Wire, transport = leg.Transport, port, tls = true, node = other, key = Key(kit, leg.Secret), meant = leg.Node });
            var bound = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector" && Int(e, "conn") == Int(first, "conn"));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await c.Dial(T0);
            var held = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Held);
            var rec = c.M.Record(T0);
            var refusal = await peer.Wait(e => Ev(e, "refused") && Str(e, "role") == "acceptor" && Str(e, "class") == "binding");
            Check(bound is not null && held && rec is { State: "held", Refusal: "binding", Light: "red" } && c.M.HeldUntilWall(T0) == T0 + PeerLink.HoldRefusedMs
                  && refusal is not null && c.Credentials.SetEquals(["proof"]) && c.M.Kept.Pin is null,
                $"peerlink scenarios: {name} — a key bound to another connector at its first verified use is a binding refusal: held 60 s, refusal binding ({rec.ToJson()}) {peer.Faults}");
        }
        {
            // Bound by its operator when made: the peer's `bound`.
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S18 line, bound when made", ListenAs(Line, kit.A, [new { file = kit.File(Ptk), kind = "key", bound = "main:0f0f0f0f0f0f" }]));
            await using var c = Connector(PeerCarrier.Line, Ptk, false, "patterns", PatternsId, port);
            await c.Dial(T0);
            var held = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Held);
            var words = await Wrote(peer, await Taken(peer), "ERR @l2 LINK binding");
            Check(held && c.M.Record(T0).Refusal == "binding" && words == "ERR @l2 LINK binding — this key speaks for main 0f0f0f0f0f0f",
                $"peerlink scenarios: S18 line — a key its operator bound to another connector is refused in 13.5.2's words ({words})");
        }
        // S18's acceptor half — a binding refusal never counted toward a lockout — is Stagewatch's /ws acceptor's: its row and the
        // bindings come with SW22.1b, the wrong-proof table it is never counted in with SW22.1a. Nothing of it is run here.
    }

    // ------------------------------------------------------------------ S20: a foreign Origin, a forwarder

    static async Task S20(Action<bool, string> Check, string script, Material kit)
    {
        // The peer carries the Host and Origin an acceptor's loopback trust must check (13.3.4); here they are read at the peer's own
        // acceptor as they came, which checks the peer's attack alone. Checking them before loopback is trusted is Stagewatch's /ws
        // acceptor's: S20's row comes with SW22.1c, the Host and Origin checks with SW22.1a. Stagewatch runs no forwarder of outside
        // traffic through loopback, so S20's forwarder is not its row.
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, "S20", ListenAs(Ws, kit.A, [Key(kit, Swk)]));
        var dial = await peer.Ask(new { @do = "dial", carrier = "ws", port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main,
            attack = new { origin = "http://evil.example", host = "evil.example" } });
        var conn = Int(dial, "conn") ?? -1;
        var upgrade = await peer.Wait(e => Ev(e, "upgrade"));
        await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        var call = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { origin = "http://evil.example" } });
        var served = await peer.Wait(e => Ev(e, "served"));
        Check(upgrade is { } u && Str(u, "origin") == "http://evil.example" && Str(u, "host") == "evil.example" && Int(call, "status") == 200
              && served is { } s && Str(s, "origin") == "http://evil.example",
            $"peerlink scenarios: S20 — the peer sends a foreign Origin and Host on its upgrade and its side call, as an acceptor reads them {peer.Faults}");
    }

    // ------------------------------------------------------------------ S21: enrolment

    static async Task S21(Action<bool, string> Check, string script, Material kit)
    {
        // Over TLS a code proved by the core enrols at an acceptor that offers it: the core's EnrolK and Kid over its own T name the
        // kid the verdict names, and a connector proving with that key links verified, scope follow. A new code revokes it at once.
        // The connector that keeps an enrolled key and proves with it from then on is PlayoutLink's (SW23.1); Stagewatch's acceptor
        // offers no enrolment.
        foreach (var leg in Legs)
        {
            var name = "S21 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, With(ListenAs(leg, kit.A, [CodeOf(kit, Code)]), new { can = new[] { "enrol" } }));
            await using var c = Connector(leg.Carrier, CodeTyped, true, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            var up = await Link(c, leg, T0);
            var named = c.LastVerdict is { } v && v.TryGetProperty("enrolled", out var e) ? e.GetString() : null;
            var kLink = c.T is { } t ? PeerLink.EnrolK(PeerLink.CodeK(PeerLink.Canon(Code)!), t) : [];
            var kid = PeerLink.Kid(kLink);
            Enrolled.Add(Convert.ToHexStringLower(kLink));
            var atAcceptor = await peer.Wait(x => Ev(x, "enrolled") && Str(x, "role") == "acceptor");
            Check(up && named == kid && atAcceptor is { } a && Str(a, "kid") == kid && PeerLink.MayEnrol("code", "tls:sha256:" + kit.A.Fp),
                $"peerlink scenarios: {name} — a code proved over TLS enrols: the core's EnrolK over its T names the kid the verdict names ({named} / {kid}) {peer.Faults}");
            await using var enrolled = ConnectorWith(leg.Carrier, kLink, kid, null, leg.Kind, leg.Id, port, c.M.Kept, linesOverWs: leg.OverWs);
            var proved = await Link(enrolled, leg, T0);
            var link = await peer.Wait(x => Ev(x, "link") && Str(x, "role") == "acceptor" && Str(x, "kid") == kid);
            Check(proved && link is { } l && Str(l, "verified") == "link" && enrolled.M.Record(T0) is { Kid: var k, Scope: "follow", Tls: "pinned" } && k == kid
                  && enrolled.Credentials.SetEquals(["proof"]),
                $"peerlink scenarios: {name} — the enrolled key, never sent, proves the link: verified link, scope follow ({enrolled.M.Record(T0).ToJson()})");
            // Patterns' NEW TOKEN: a new code revokes every key enrolled from the old one, its live link refused at once.
            var replaced = await Set(peer, new { code = CodeOf(kit, WrongCode) });
            var held = await enrolled.Pump(T0 + 10, () => enrolled.M.State(T0 + 10) == PeerLinkState.Held);
            var revoked = await peer.Wait(x => Ev(x, "revoked") && Str(x, "kid") == kid);
            var again = enrolled.M.Record(T0 + 10);
            Check(replaced && held && revoked is not null && again is { State: "held", Refusal: "key" },
                $"peerlink scenarios: {name} — a new code revokes the key enrolled from the old one: its live link refused at once ({again.ToJson()})");
        }
        foreach (var leg in new[] { Line, LinesV1 })
        {
            // A code over plain never enrols (13.5.7), at the acceptor or by the core's MayEnrol.
            var name = "S21 " + leg.Label + ", plain";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, With(ListenAs(leg, kit.A, [CodeOf(kit, Code)], tls: "plain"), new { can = new[] { "enrol" } }));
            await using var c = Connector(leg.Carrier, CodeTyped, true, leg.Kind, leg.Id, port, local: true, linesOverWs: leg.OverWs);
            var up = await Link(c, leg, T0);
            await Settle(peer);
            Check(up && c.LastVerdict is { } v && !v.TryGetProperty("enrolled", out _) && !peer.Events.Any(e => Ev(e, "enrolled")) && !PeerLink.MayEnrol("code", "plain"),
                $"peerlink scenarios: {name} — a code proved over plain enrols nothing ({c.LastVerdict?.GetRawText()})");
        }
        {
            // The peer's own connector end enrols as 13.5.7 says, for the trees whose acceptors offer it: it derives the same key, and
            // proves with it the next time it dials the node it meant.
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S21, the peer connecting", With(ListenAs(Line, kit.A, [CodeOf(kit, Code)]), new { can = new[] { "enrol" } }));
            var first = await peer.Ask(new { @do = "dial", carrier = "line", port, tls = true, node = Main, key = CodeOf(kit, CodeTyped), meant = Patterns });
            var derived = await peer.Wait(e => Ev(e, "enrolled") && Str(e, "role") == "connector");
            var second = await peer.Ask(new { @do = "dial", carrier = "line", port, tls = true, node = Main, key = CodeOf(kit, CodeTyped), meant = Patterns });
            var conn = Int(second, "conn") ?? -1;
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector" && Int(e, "conn") == conn);
            var accepted = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "acceptor" && Str(e, "verified") == "link");
            Check(Ev(first, "connected") && derived is { } d && True(d, "derived") && link is { } l && Str(l, "kid") == Str(d, "kid") && accepted is not null,
                $"peerlink scenarios: S21, the peer connecting — it derives the enrolled key, kid for kid, and proves with it on its next link {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S22: a level-0 acceptor with Patterns 88.1's grammar

    static async Task S22(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in new[] { Line, LinesV1 })
        {
            var name = "S22 " + leg.Label;
            {
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
                await using var c = Connector(PeerCarrier.Line, CodeTyped, true, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
                await c.Dial(T0);
                var live = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Live);
                var rec = c.M.Record(T0);
                var conn = await peer.Wait(e => Ev(e, "paired")) is { } p ? Int(p, "conn") ?? -1 : -1;
                var read = peer.ReadOn(conn);
                Check(live && rec is { State: "live", Proof: "legacy", Level: 0, Light: "amber" } && rec.Flags.Contains("legacy") && rec.Flags.Contains("plain")
                      && rec.Counts.Malformed >= 1 && c.Credentials.SetEquals(["auth-proof"]) && read.Any(l => l.StartsWith("@pa", StringComparison.Ordinal) && l.Contains(" AUTH-PROOF ", StringComparison.Ordinal))
                      && !read.Any(l => l.StartsWith("AUTH ", StringComparison.Ordinal) || l.Contains(" AUTH ", StringComparison.Ordinal)) && c.M.Follows(T0),
                    $"peerlink scenarios: {name} — Patterns 88.1's grammar (STATE, then NONCE): legacy, amber; AUTH-PROOF for its 32-hex NONCE, never plain AUTH off this machine; the STATE before OK paired discarded and counted ({rec.ToJson()}; {string.Join(" | ", read)}) {peer.Faults}");
            }
            foreach (var nonce in new[] { "3F9A0C5E7B2D4F6A8C1E3B5D7F9A0C2E", "3f9a0c5e7b2d4f6a8c1e3b5d7f9a0c2" })
            {
                // A NONCE that is not 32 lowercase hex is never proved: nothing but HELLO and LINK is sent, and nothing is taken.
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name + ", NONCE " + nonce.Length.ToString(CultureInfo.InvariantCulture) + " characters",
                    With(ListenAs(leg, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"), new { attack = new { nonce } }));
                await using var c = Connector(PeerCarrier.Line, CodeTyped, true, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
                await c.Dial(T0);
                var level0 = await c.Pump(T0 + 10, () => c.M.Record(T0 + 10).Proof == "legacy");
                await Settle(peer);
                var rec = c.M.Record(T0 + 10);
                var follows = c.M.Follows(T0 + 10);
                var later = c.M.Record(T0 + PeerLink.HandshakeMs + 1);
                Check(level0 && c.Credentials.Count == 0 && !peer.AllRead().Any(l => l.Contains("AUTH", StringComparison.Ordinal)) && !c.Wrote.Any(w => w.Contains("AUTH", StringComparison.Ordinal))
                      && rec.State == "connecting" && !follows && later.State == "down" && peer.AllRead().Any(l => l.StartsWith("HELLO ", StringComparison.Ordinal)),
                    $"peerlink scenarios: {name} — a NONCE that is not 32 lowercase hex ({nonce}) is never answered: no AUTH-PROOF, no AUTH, nothing taken; no link within peerlink.handshake.ms ({rec.ToJson()}) {peer.Faults}");
            }
        }
    }

    // ------------------------------------------------------------------ S19: canary secrets

    /// <summary>Every key enrolled from a code in S21, as hex: a canary none of the core's records and no peer's stdout may carry.</summary>
    static readonly List<string> Enrolled = [];

    /// <summary>
    /// The words of every command <paramref name="c"/>'s core knows, sent or not: its ids are the connector's own (<c>s&lt;n&gt;-11aa22bb</c>,
    /// 13.12.1), and a person reads each one's words (13.19.5).
    /// </summary>
    static IEnumerable<string> CommandWords(CoreConnector c)
        => Enumerable.Range(1, 512).Select(n => PeerLink.RequestId("s", n, "11aa22bb")).Where(c.M.Knows).Select(c.M.WordsOf);

    static async Task S19(Action<bool, string> Check, string script, Material kit)
    {
        // One more honest link on each carrier with a command, a side call and a revocation, so its every record is captured too.
        foreach (var leg in Legs)
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S19 " + leg.Label, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, linesOverWs: leg.OverWs);
            await Link(c, leg, T0);
            await Command(c, T0 + 10);
            if (leg.Carrier == PeerCarrier.Ws) await c.SideCall(T0 + 20, "/api/context");
            await Set(peer, new { revoke = Kid(leg.Secret) });
            await c.Pump(T0 + 30, () => c.M.State(T0 + 30) == PeerLinkState.Held);
            await c.Flush(T0 + 30);
            await Settle(peer);
            var own = Canaries(leg.Secret).Append(c.T is { } t ? PeerLink.Session(PeerLink.KeyK(leg.Secret), t) : "").Where(x => x.Length > 0).ToList();
            Check(Found(Stdout(peer), own) is null, $"peerlink scenarios: S19 {leg.Label} — no key, K or S of the peer's own on its stdout");
        }
        // Every record and command's words Stagewatch's core published in S1–S22, against every secret the scenarios held and every
        // value their handshakes made: the keys and codes, K, the nonces, both proofs, S, and a legacy NONCE and its AUTH-PROOF (13.19.5).
        var secrets = Canaries(Ptk, Pmk, Swk, Stranger, Code, CodeTyped, WrongCode).Concat(Enrolled).ToList();
        var canaries = secrets.Concat(Every.SelectMany(x => x.Made)).Distinct(StringComparer.Ordinal).ToList();
        var published = Every.SelectMany(x => x.Published.Concat(CommandWords(x))).ToList();
        var found = Found(published, canaries);
        Check(Every.Count >= 50 && published.Count >= 100 && canaries.Count >= 100 && found is null
              && Every.Any(x => x.Credentials.Contains("auth-proof")) && published.Any(x => x.Contains("not sent", StringComparison.Ordinal)),
            $"peerlink scenarios: S19 — none of {canaries.Count} canaries (keys, codes, K, nonces, proofs, S, legacy NONCEs and AUTH-PROOFs) in any of the {published.Count} records and command words the core published across {Every.Count} links ({found})");
        // Every scripted peer's stdout in S1–S22, against what is the peer's own and never crosses a wire: the keys and codes, K, the
        // keys enrolled from a code, and every S the core, the stand-in or the peer made. What crossed a wire it reports as it was.
        var peerOwn = secrets.Concat(Every.SelectMany(x => x.Sessions)).Concat(Stands.SelectMany(x => x.Sessions)).Distinct(StringComparer.Ordinal).ToList();
        var printed = Peers.SelectMany(Stdout).ToList();
        var leaked = Found(printed, peerOwn);
        Check(Peers.Count >= 50 && printed.Count >= 1000 && peerOwn.Count >= 30 && leaked is null,
            $"peerlink scenarios: S19 — none of {peerOwn.Count} secrets of the peer's own (keys, codes, K, enrolled keys, S) on the stdout of any of the {Peers.Count} scripted peers ({printed.Count} events; {leaked})");
        // The journal, STATE, context, report and error captures 13.19.5 names are PlayoutLink's readers' (SW20.4b, SW20.5); the core's
        // record is what each of them takes, and every record it published is held here.
    }
}
