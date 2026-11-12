using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>
/// The scenarios 13.20 holds every acceptor to, run against Stagewatch's own <c>/ws</c> acceptor — a real <see cref="WebServer"/>, the
/// scripted peer connecting to it: S1–S3, S8–S10, S12's emitting half, S15's oversize message, S16, S17 and S18, the session credential
/// speaking for its key on the side calls' routes and nowhere else, and a key seen in clear (13.6.9); S14, S19 and S20 at /ws are in
/// PeerLinkScenarios.Receipts.cs. S15's reply bound is not kept yet (docs/WHATS_NEXT.md) and stays with the kit's stand-in.
/// </summary>
static partial class PeerLinkScenarios
{
    /// <summary>The Stagewatch the acceptor scenarios link to: its node id, its kind, and the key a show app proves.</summary>
    const string AcceptorId = "abc123abc123";

    static readonly object Acceptor = new { t = LinkAcceptor.Kind, id = AcceptorId };

    /// <summary>A key in force as Stagewatch keeps it: its id, its scope, and SHA-256 of its secret, which is its K (13.5.1).</summary>
    static ApiKey KeyInForce(string id, string secret, ApiKeyScope scope = ApiKeyScope.Node)
        => new() { Id = id, Name = "Patterns FOH", Scope = scope, Hash = Convert.ToHexStringLower(PeerLink.KeyK(secret)), Hint = secret[^4..] };

    /// <summary>A Stagewatch on loopback with one node key in force (<see cref="Swk"/>), its certificate the kit's node certificate.</summary>
    sealed class Hall : IAsyncDisposable
    {
        public readonly ShowBusTests.BusControl Control;
        public readonly WebServer Web;
        public readonly ShowWorkflowTests.ManualTime Clock;
        /// <summary>The show thread's clock as the frames' hub reads it, in ms: moved by hand to make the producer stuck (S12).</summary>
        public long ShowMs;
        public int Port { get; }

        /// <param name="after">
        /// A hall whose server this one rebuilds, as the desk rebuilds its server on a Remote settings change: the same show (its control,
        /// keys and clock) and the receipts record handed over; null for a hall of its own.
        /// </param>
        public Hall(Material kit, Func<IPAddress, bool>? isThisPc = null, bool tls = true, string? pin = null, IReadOnlyList<ApiKey>? keys = null, Hall? after = null)
        {
            if (after is not null)
            {
                Control = after.Control;
                Clock = after.Clock;
            }
            else
            {
                Clock = new(new DateTime(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc));
                Control = new ShowBusTests.BusControl { Snapshots = new SnapshotHub(() => Volatile.Read(ref ShowMs)) };
                Control.ApiKeys = keys ?? [KeyInForce("kS", Swk)];
                Control.Nodes.Keep(Control.ApiKeys);
                Control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300, Title = "Keynote" });
            }
            Web = new WebServer(Control, () => pin)
            {
                LinkClock = Clock, IsThisPc = isThisPc ?? PlayoutGate.IsLoopback, Receipts = after?.Web.Receipts ?? new ReceiptRecord(),
            };
            if (tls) Web.Certificate = kit.A.Server;
            Port = FreeTcpPort();
            Web.Start(Port, bindAll: false);
        }

        public ValueTask DisposeAsync() => Web.DisposeAsync();
    }

    static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>The peer dials the hall as Patterns over <c>/ws?…&amp;link=1</c>, holding <paramref name="key"/>; the connection's number.</summary>
    static async Task<int> DialHall(ScriptedPeer peer, Hall hall, object key, bool tls = true, object? attack = null)
    {
        var dial = attack is null
            ? await peer.Ask(new { @do = "dial", carrier = "ws", port = hall.Port, tls, node = Patterns, key, meant = Acceptor })
            : await peer.Ask(new { @do = "dial", carrier = "ws", port = hall.Port, tls, node = Patterns, key, meant = Acceptor, attack });
        return Int(dial, "conn") ?? -1;
    }

    /// <summary>What the peer read on <paramref name="conn"/>, each as JSON (an unreadable one as an empty object).</summary>
    static List<JsonElement> ReadJson(ScriptedPeer peer, int conn)
        => peer.ReadOn(conn).Select(m =>
        {
            try { return JsonDocument.Parse(m).RootElement.Clone(); }
            catch (JsonException) { return JsonDocument.Parse("{}").RootElement.Clone(); }
        }).ToList();

    static async Task WsAcceptor(Action<bool, string> Check, string script, Material kit)
    {
        await AcceptorS1(Check, script, kit);
        await AcceptorS2AndS3(Check, script, kit);
        await AcceptorS8AndS9(Check, script, kit);
        await AcceptorS10(Check, script, kit);
        await AcceptorS15(Check, script, kit);
        await AcceptorS16(Check, kit);
        await AcceptorHostAndOrigin(Check, script, kit);
        await AcceptorLiveChanges(Check, script, kit);
        await AcceptorOtherRefusals(Check, script, kit);
        await AcceptorSideCalls(Check, script, kit);
        await AcceptorS12(Check, script, kit);
        await AcceptorS18(Check, script, kit);
        await AcceptorSeenInClear(Check, kit);
        await WsAcceptorReceipts(Check, script, kit);
    }

    /// <summary>A control key the scenarios link with as the operator's credential (verified <c>operator</c> while no key speaks for the id).</summary>
    const string ControlSecret = "swk_23456789abcdefghijkmnpqrstuvwxyz";

    /// <summary>A second control key, held by another holder, that names the same unheld id.</summary>
    const string ControlSecret2 = "swk_3456789abcdefghijkmnpqrstuvwxyz2";

    // ------------------------------------------------------------------ S1 at /ws: honest, over TLS and plain on this machine

    static async Task AcceptorS1(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var tls in new[] { true, false })
        {
            var name = "S1 /ws acceptor, " + (tls ? "TLS" : "plain on this machine");
            await using var hall = new Hall(kit);
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, Swk), tls);
            var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
            var frame = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"type\":\"state\"", StringComparison.Ordinal));
            var read = ReadJson(peer, conn);
            var verdictAt = read.FindIndex(m => Str(m, "type") == "auth");
            var firstFrame = read.FindIndex(m => Str(m, "type") == "state");
            var offer = read.FirstOrDefault();
            var verdict = verdictAt >= 0 ? read[verdictAt] : default;
            Check(link is { } l && Str(l, "verified") == "key" && Str(l, "scope") == "node" && Int(l, "level") == 1 && Str(l, "inst") is { Length: 8 }
                  && Str(offer, "type") == "link" && offer.TryGetProperty("server", out var server) && Str(server, "id") == AcceptorId && Str(server, "t") == "main"
                  && Str(offer, "bind") == (tls ? "tls:sha256:" + kit.A.Fp : "plain")
                  && verdictAt == 1 && frame is not null && firstFrame > verdictAt
                  && verdict.TryGetProperty("can", out var can) && can.ValueKind == JsonValueKind.Array && Int(verdict, "beatMs") == (int)WebServer.HeartbeatEvery.TotalMilliseconds,
                $"peerlink scenarios: {name} — the offer first, then the verdict with proof, lid, level, can, inst and beatMs, and only then a frame "
                + $"({string.Join(" | ", peer.ReadOn(conn).Take(3))}) {peer.Faults}");
            var facts = hall.Web.ListNodes();
            Check(facts is [{ Kind: "patterns", Id: "9f8e7d6c", Verified: NodeProof.Key, KeyId: "kS", Linked: 1 }] && hall.Web.ListScreens().Count == 0,
                $"peerlink scenarios: {name} — the socket is a node link verified by LINK, listed once and never a screen ({string.Join("; ", facts)})");
            await peer.Ask(new { @do = "send", conn, message = new { cmd = "ping", t = 1000 } });
            await peer.Ask(new { @do = "send", conn, message = new { cmd = "ask", args = new[] { "the next cue?" }, id = "p1-0a1b2c3d" } });
            var pong = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"type\":\"pong\",\"t\":1000", StringComparison.Ordinal));
            var receipt = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"id\":\"p1-0a1b2c3d\"", StringComparison.Ordinal));
            Check(pong is not null && receipt is { } r && (Str(r, "message") ?? "").Contains("\"ok\":true", StringComparison.Ordinal)
                  && hall.Control.Commands.Count(c => c.Kind == ShowCommandKind.Question) == 1,
                $"peerlink scenarios: {name} — a ping is answered with a pong, and a command with its request id runs once and is answered with its receipt {peer.Faults}");
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Swk)) is null, $"peerlink scenarios: {name} — no key or K on the peer's stdout");
        }
    }

    // ------------------------------------------------------------------ S2 and S3 at /ws: a wrong key, an unknown kid

    static async Task AcceptorS2AndS3(Action<bool, string> Check, string script, Material kit)
    {
        await using var hall = new Hall(kit);
        await using var peer = Peer(script);
        var known = Kid(Swk);

        // S2: the right key's kid, proved with another key — a wrong proof for a key in force, counted per (address, kid).
        var wrong = await DialHall(peer, hall, Key(kit, Stranger), attack: new { kid = known });
        var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == wrong);
        var publicFrame = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == wrong && (Str(e, "message") ?? "").Contains("\"type\":\"state\"", StringComparison.Ordinal));
        var stays = await peer.Ask(new { @do = "silence", match = new { ev = "closed", conn = wrong }, ms = 300 });
        var s2Words = peer.ReadOn(wrong).FirstOrDefault(m => m.Contains("\"type\":\"auth\"", StringComparison.Ordinal)) ?? "";
        Check(refused is { } r && Str(r, "class") == "refused" && Str(r, "words") == PeerLink.ProofWords && Int(r, "retry") == 60 && publicFrame is not null && True(stays)
              && s2Words == "{\"type\":\"auth\",\"ok\":false,\"error\":\"" + PeerLink.ProofWords + "\",\"class\":\"refused\",\"retry\":60}"
              && hall.Web.ProofFailures() is [{ Kid: var k, Count: 1 }] && k == known && hall.Web.ListNodes().Count == 0,
            $"peerlink scenarios: S2 /ws acceptor — a wrong proof for a key in force: 13.6.3's words, class refused, retry 60, the socket kept as a public viewer, "
            + $"counted once for its (address, kid) ({s2Words}) {peer.Faults}");

        // S3: a kid no key in force has — the same words byte for byte, and counted nowhere, however often.
        var s3Words = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var c = await DialHall(peer, hall, Key(kit, Stranger));
            await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == c);
            s3Words.Add(peer.ReadOn(c).FirstOrDefault(m => m.Contains("\"type\":\"auth\"", StringComparison.Ordinal)) ?? "");
        }
        Check(s3Words.All(w => w == s2Words) && hall.Web.ProofFailures().All(f => f.Kid == known) && hall.Web.AuthFailures().Count == 0,
            $"peerlink scenarios: S3 /ws acceptor — an unknown kid is refused in S2's words, byte for byte, six times, and counted nowhere ({string.Join(" | ", s3Words.Distinct())})");

        // Four more wrong proofs lock the pair for the minute: then even the right key is refused, locked, before its proof is checked;
        // the PIN's table counts nothing; the minute's end lets the right key link.
        for (var i = 0; i < WebServer.WrongProofsPerMinute - 1; i++)
        {
            var c = await DialHall(peer, hall, Key(kit, Stranger), attack: new { kid = known });
            await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == c);
        }
        var right = await DialHall(peer, hall, Key(kit, Swk));
        var locked = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == right);
        Check(locked is { } x && Str(x, "class") == "locked" && Str(x, "words") == LinkAcceptor.ProofsLocked && Int(x, "retry") is > 0 and <= 60
              && hall.Web.ProofFailures() is [{ Count: WebServer.WrongProofsPerMinute }] && hall.Web.AuthFailures().Count == 0 && hall.Web.ListNodes().Count == 0,
            $"peerlink scenarios: S2 /ws acceptor — {WebServer.WrongProofsPerMinute} wrong proofs in a minute lock that (address, kid): the right key is refused, "
            + "class locked, before its proof is checked, and no PIN or view-pass count moves");
        hall.Clock.Advance(TimeSpan.FromMilliseconds(60_000));
        var after = await DialHall(peer, hall, Key(kit, Swk));
        var linked = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == after);
        Check(linked is not null && hall.Web.ProofFailures().Count == 0 && hall.Web.ListNodes() is [{ Linked: 1 }],
            "peerlink scenarios: S2 /ws acceptor — at the minute's end the pair is free again, and the right key links (the table's minute is on the injected clock)");
        await Settle(peer);
        Check(Found(Stdout(peer), Canaries(Swk, Stranger)) is null, "peerlink scenarios: S2 and S3 /ws acceptor — no key or K on the peer's stdout");
    }

    // ------------------------------------------------------------------ S8 and S9 at /ws: a replay, a second proof, a reflected proof

    static async Task AcceptorS8AndS9(Action<bool, string> Check, string script, Material kit)
    {
        await using var hall = new Hall(kit);
        await using var peer = Peer(script);
        var first = await DialHall(peer, hall, Key(kit, Swk));
        var linked = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == first);
        var replay = await DialHall(peer, hall, Key(kit, Swk), attack: new { replay = first });
        var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == replay);
        var offers = new[] { first, replay }.Select(c => ReadJson(peer, c).FirstOrDefault()).Select(o => Str(o, "snonce")).ToList();
        Check(linked is not null && refused is { } r && Str(r, "class") == "refused" && Str(r, "words") == PeerLink.ProofWords
              && LinkSent(peer, replay).SequenceEqual(LinkSent(peer, first)) && offers.Distinct().Count() == 2 && offers.All(o => PeerLink.IsHex(o, 32))
              && hall.Web.ListNodes() is [{ Linked: 1 }],
            $"peerlink scenarios: S8 /ws acceptor — a LINK replayed byte for byte meets a fresh snonce, another T, and is refused ({string.Join(", ", offers)}) {peer.Faults}");

        var twice = await DialHall(peer, hall, Key(kit, Swk), attack: new { twice = true });
        var proved = await peer.Wait(e => Ev(e, "attack") && Str(e, "mode") == "twice" && Int(e, "conn") == twice);
        var answer = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == twice && (Str(e, "message") ?? "").Contains("\"type\":\"error\"", StringComparison.Ordinal));
        Check(proved is not null && answer is not null && ReadJson(peer, twice).Count(m => Str(m, "type") == "auth") == 1 && hall.Web.ListNodes() is [{ Links: 2, Linked: 2 }],
            "peerlink scenarios: S8 /ws acceptor — a second proof on one socket links nothing more: each snonce answers one proof, and a link message after the verdict is no command");

        var role = await DialHall(peer, hall, Key(kit, Swk), attack: new { role = true });
        var reflected = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == role);
        Check(reflected is { } f && Str(f, "class") == "refused" && Str(f, "words") == PeerLink.ProofWords && hall.Web.ProofFailures() is [{ Count: 2 }],
            $"peerlink scenarios: S9 /ws acceptor — a server proof offered as a client proof is refused in 13.6.3's words and counted as a wrong proof, beside "
            + "S8's replay; the right proofs between cleared neither "
            + $"({(reflected is { } g ? g.GetRawText() : "no refusal")}; {string.Join(", ", hall.Web.ProofFailures())}) {peer.Faults}");
    }

    // ------------------------------------------------------------------ S10 at /ws: S in clear, after its link, under another lid

    static async Task AcceptorS10(Action<bool, string> Check, string script, Material kit)
    {
        // Loopback plays another machine (the IsThisPc seam): the link runs over TLS, and a plain call is in clear from another machine.
        await using var hall = new Hall(kit, isThisPc: _ => false);
        await using var peer = Peer(script);
        var conn = await DialHall(peer, hall, Key(kit, Swk));
        var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        var honest = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
        var otherLid = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { lid = "0123456789abcdef" } });
        var stillOk = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
        Check(link is not null && Int(honest, "status") == 200 && Int(otherLid, "status") == 401 && True(Body(otherLid), "relink") && Int(stillOk, "status") == 200,
            $"peerlink scenarios: S10 /ws acceptor — a live link's S passes the door; another lid is 401 relink and ends nothing ({otherLid.GetRawText()}) {peer.Faults}");
        var clear = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { clear = true } });
        var ended = await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == conn);
        var gone = await Until(() => hall.Web.ListNodes().Count == 0);
        Check(Int(clear, "status") == 401 && True(Body(clear), "relink") && Str(Body(clear), "error")?.Contains("in clear from another machine", StringComparison.Ordinal) == true
              && ended is { } y && Str(y, "by") == "other" && gone,
            $"peerlink scenarios: S10 /ws acceptor — S in clear from another machine is refused and its link ended ({clear.GetRawText()})");
        var afterClose = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { closed = true } });
        Check(Int(afterClose, "status") == 401 && True(Body(afterClose), "relink") && Str(Body(afterClose), "error") == "the link ended",
            $"peerlink scenarios: S10 /ws acceptor — S after its link closed is 401 relink, the link ended ({afterClose.GetRawText()})");
        // The door alone refuses: the same call with no credential is answered, and with a ShowBus header that names no live link it is not.
        var bare = await RawGet(hall.Port, "/api/context");
        var misshapen = await RawGet(hall.Port, "/api/context", "Authorization: ShowBus lid=zz");
        Check(bare.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && misshapen.StartsWith("HTTP/1.1 401", StringComparison.Ordinal)
              && misshapen.Contains("\"relink\":true", StringComparison.Ordinal),
            $"peerlink scenarios: S10 /ws acceptor — the session door answers before the route: a call with no credential is 200, a misshapen ShowBus one 401 relink "
            + $"({bare.Split("\r\n")[0]}; {misshapen.Split("\r\n")[0]})");
        // A plain link= socket from another machine is refused before its upgrade: no S could ever ride its side calls but in clear.
        var plain = await DialHall(peer, hall, Key(kit, Swk), tls: false);
        var plainRefused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == plain);
        Check(plainRefused is { } p && (Str(p, "words") ?? "").Contains("403", StringComparison.Ordinal) && ReadJson(peer, plain).Count == 0,
            $"peerlink scenarios: S10 /ws acceptor — a link= socket in clear from another machine is refused 403 before its upgrade, never offered LINK ({(plainRefused is { } q ? Str(q, "words") : "no refusal")})");
    }

    // ------------------------------------------------------------------ S15 at /ws: an oversize message

    static async Task AcceptorS15(Action<bool, string> Check, string script, Material kit)
    {
        await using var hall = new Hall(kit);
        await using var peer = Peer(script);
        var conn = await DialHall(peer, hall, Key(kit, Swk));
        await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        await peer.Ask(new { @do = "send", conn, message = new { cmd = "ask", args = new[] { new string('x', WebServer.MaxWsMessageBytes + 10) } } });
        await peer.Ask(new { @do = "send", conn, message = new { cmd = "ping", t = 2000 } });
        var pong = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"type\":\"pong\",\"t\":2000", StringComparison.Ordinal));
        var silent = await peer.Ask(new { @do = "silence", match = new { ev = "closed", conn }, ms = 300 });
        Check(pong is not null && True(silent) && hall.Web.ListNodes() is [{ Oversize: 1, Linked: 1 }] && hall.Control.Commands.Count == 0,
            $"peerlink scenarios: S15 /ws acceptor — a message past link.ws.in.bytes on a verified link is skipped and counted, the link stays up, and the next is read {peer.Faults}");
    }

    // ------------------------------------------------------------------ S16 at /ws: an unpaired flood

    static async Task AcceptorS16(Action<bool, string> Check, Material kit)
    {
        await using var hall = new Hall(kit);
        var held = new List<ClientWebSocket>();
        try
        {
            var query = "/ws?node=9f8e7d6c&t=patterns&link=1";
            for (var i = 0; i < WebServer.UnpairedPerAddress; i++)
            {
                var ws = new ClientWebSocket();
                held.Add(ws);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{hall.Port}{query}"), CancellationToken.None);
            }
            var ends = held.Select(ReadToEnd).ToList();
            var over = await RawStatus(hall.Port, query);
            for (var i = 0; i < 200 && hall.Clock.Timers < WebServer.UnpairedPerAddress; i++) await Task.Delay(10);
            var armed = hall.Clock.Timers;
            hall.Clock.Advance(TimeSpan.FromMilliseconds(PeerLink.HandshakeMs - 1));
            await Task.Delay(200);
            var openBefore = ends.All(t => !t.IsCompleted);
            hall.Clock.Advance(TimeSpan.FromMilliseconds(1));
            var closedAfter = await Task.WhenAll(ends).WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(t => t.IsCompletedSuccessfully, TaskScheduler.Default);
            var again = await Until(async () => (await RawStatus(hall.Port, query)).StartsWith("HTTP/1.1 101", StringComparison.Ordinal));
            Check(over.StartsWith("HTTP/1.1 429", StringComparison.Ordinal) && BodyError(over) == WebServer.LinkBusy && over.Contains("\r\nRetry-After: 10\r\n", StringComparison.Ordinal)
                  && armed == WebServer.UnpairedPerAddress && openBefore && closedAfter && again,
                $"peerlink scenarios: S16 /ws acceptor — at most {WebServer.UnpairedPerAddress} unfinished link= sockets per address (the next answered 429 before its upgrade), "
                + $"each closed at peerlink.handshake.ms on the injected clock, and the bound free again after ({over.Split("\r\n")[0]}; {armed} timers; open before {openBefore}; "
                + $"closed after {closedAfter}; again {again})");
        }
        finally
        {
            foreach (var ws in held) ws.Dispose();
        }

        // Reads until the server ends the socket (a close or the connection's end); the offer is read on the way.
        static async Task ReadToEnd(ClientWebSocket ws)
        {
            var buffer = new byte[4096];
            try
            {
                WebSocketReceiveResult r;
                do r = await ws.ReceiveAsync(buffer, CancellationToken.None);
                while (r.MessageType != WebSocketMessageType.Close);
            }
            catch (Exception e) when (e is WebSocketException or ObjectDisposedException or IOException)
            {
                // The server ended the connection: that is the end this waits for.
            }
        }
    }

    /// <summary>The <c>error</c> of a raw answer's JSON body, decoded; null when it has none.</summary>
    static string? BodyError(string answer)
    {
        var at = answer.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (at < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(answer[(at + 4)..]);
            return Str(doc.RootElement, "error");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The status line and body an upgrade to <paramref name="target"/> on loopback is answered with, read raw.</summary>
    static async Task<string> RawStatus(int port, string target, string? host = null, string? origin = null)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var head = $"GET {target} HTTP/1.1\r\nHost: {host ?? "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture)}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                   + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n" + (origin is null ? "" : "Origin: " + origin + "\r\n") + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(3000);
        var got = new StringBuilder();
        try
        {
            // An upgrade's answer is its head (the socket stays open); a refusal's is all the server writes before it closes.
            while (!(got.ToString().StartsWith("HTTP/1.1 101", StringComparison.Ordinal) && got.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)))
            {
                var n = await stream.ReadAsync(buffer, cts.Token);
                if (n == 0) break;
                got.Append(Encoding.Latin1.GetString(buffer, 0, n));
            }
        }
        catch (OperationCanceledException)
        {
            // What came in time is the answer.
        }
        return got.ToString();
    }

    /// <summary>Whether <paramref name="done"/> holds within 3 s of real time, asked every 20 ms.</summary>
    static async Task<bool> Until(Func<bool> done)
    {
        for (var i = 0; i < 150; i++)
        {
            if (done()) return true;
            await Task.Delay(20);
        }
        return done();
    }

    static async Task<bool> Until(Func<Task<bool>> done)
    {
        for (var i = 0; i < 150; i++)
        {
            if (await done()) return true;
            await Task.Delay(20);
        }
        return false;
    }

    /// <summary>The status line, head and body a plain GET of <paramref name="path"/> on loopback is answered with, read raw.</summary>
    static async Task<string> RawGet(int port, string path, string? header = null)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var head = $"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n"
                   + (header is null ? "" : header + "\r\n") + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var cts = new CancellationTokenSource(3000);
        try { return await reader.ReadToEndAsync(cts.Token); }
        catch (OperationCanceledException) { return ""; }
    }

    // ------------------------------------------------------------------ a verified link's key or binding changes while it is live (13.5.5)

    static async Task AcceptorLiveChanges(Action<bool, string> Check, string script, Material kit)
    {
        // A control key links for 9f8e7d6c as the operator's credential; then a node key comes to speak for 9f8e7d6c. The link hears one
        // verdict only: the change refuses it, class binding, and its S ends — never a second ok:true, which its connector would end on.
        {
            await using var hall = new Hall(kit, keys: [KeyInForce("kS", Swk), KeyInForce("kC", ControlSecret, ApiKeyScope.Control)]);
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, ControlSecret));
            var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
            var bound = hall.Control.Nodes.Rebind("kS", "patterns", PatternsId);
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn && True(e, "live"));
            var after = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { closed = true } });
            var verdicts = ReadJson(peer, conn).Where(m => Str(m, "type") == "auth").ToList();
            var facts = hall.Web.ListNodes();
            Check(link is { } l && Str(l, "verified") == "operator" && bound.Bound is not null
                  && refused is { } r && Str(r, "class") == "binding" && Int(r, "retry") == 60
                  && verdicts.Count(v => v.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True) == 1
                  && Int(after, "status") == 401 && Str(Body(after), "error") == "the link ended"
                  && facts.All(f => f.Linked == 0),
                $"peerlink scenarios: /ws acceptor — a node key binding the id a control key's LINK speaks for refuses that link, class binding, retry 60, "
                + $"its S ended, and never sends it a second verdict ({string.Join(" | ", verdicts.Select(v => v.GetRawText()))}; {after.GetRawText()}) {peer.Faults}");
        }

        // A key revoked while its link is live: refused at once, class refused, the socket a public viewer, its S ended (13.5.5, S17's shape).
        {
            await using var hall = new Hall(kit);
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, Swk));
            await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
            hall.Control.ApiKeys = [];
            hall.Control.Nodes.Keep(hall.Control.ApiKeys);
            hall.Web.CredentialsChanged();
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn && True(e, "live"));
            var stays = await peer.Ask(new { @do = "silence", match = new { ev = "closed", conn }, ms = 300 });
            var after = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack = new { closed = true } });
            Check(refused is { } r && Str(r, "class") == "refused" && Str(r, "words") == NodeLinks.KeyRevoked && True(stays)
                  && Int(after, "status") == 401 && Str(Body(after), "error") == "the link ended" && hall.Web.ListNodes().All(f => f.Linked == 0),
                $"peerlink scenarios: S17 /ws acceptor — a key revoked while its link is live refuses it at once, class refused, keeps the socket a public viewer "
                + $"and ends its S ({after.GetRawText()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ refused before the proof, or by the viewing code

    static async Task AcceptorOtherRefusals(Action<bool, string> Check, string script, Material kit)
    {
        // A device locked out by wrong PINs proves no key: refused, class locked, for the rest of the PIN's minute; no proof is counted.
        {
            await using var hall = new Hall(kit, tls: false, pin: "4321");
            for (var i = 0; i < WebServer.WrongPinsPerMinute; i++) await RawGet(hall.Port, "/api/note?text=x", "X-Stagewatch-Pin: 0000");
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, Swk), tls: false);
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
            Check(hall.Web.AuthFailures() is [{ Failures: WebServer.WrongPinsPerMinute }]
                  && refused is { } r && Str(r, "class") == "locked" && Str(r, "words") == WebServer.PinsLocked && Int(r, "retry") is > 0 and <= 60
                  && hall.Web.ProofFailures().Count == 0 && hall.Web.ListNodes().All(f => f.Linked == 0),
                $"peerlink scenarios: /ws acceptor — a device locked out by wrong PINs is refused, class locked, retry the rest of that minute, its proof never "
                + $"checked or counted ({(refused is { } x ? x.GetRawText() : "no refusal")}) {peer.Faults}");
        }

        // A key over its rate: refused, class busy, with its Retry-After; nothing counted.
        {
            await using var hall = new Hall(kit, tls: false);
            using (var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{hall.Port}") })
            {
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Swk);
                for (var i = 0; i < ApiKeys.PerMinute; i++) (await http.GetAsync("/api/screens")).Dispose();
            }
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, Swk), tls: false);
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
            Check(refused is { } r && Str(r, "class") == "busy" && Int(r, "retry") is > 0 and <= 60 && (Str(r, "words") ?? "").StartsWith("rate limit", StringComparison.Ordinal)
                  && hall.Web.ProofFailures().Count == 0 && hall.Web.AuthFailures().Count == 0,
                $"peerlink scenarios: /ws acceptor — a right proof for a key over its rate is refused, class busy, retry its Retry-After, nothing counted "
                + $"({(refused is { } x ? x.GetRawText() : "no refusal")}) {peer.Faults}");
        }

        // A viewing code set: a right proof opens it and links; a wrong one hears LINK's refusal, its class and view, and then the close.
        {
            await using var hall = new Hall(kit);
            hall.Control.View = "4242";
            await using var peer = Peer(script);
            var right = await DialHall(peer, hall, Key(kit, Swk));
            var linked = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == right);
            var wrong = await DialHall(peer, hall, Key(kit, Stranger), attack: new { kid = Kid(Swk) });
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == wrong);
            var closed = await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == wrong);
            var heard = peer.ReadOn(wrong).FirstOrDefault(m => m.Contains("\"type\":\"auth\"", StringComparison.Ordinal)) ?? "";
            Check(linked is not null && refused is { } r && Str(r, "class") == "refused" && closed is not null
                  && heard == "{\"type\":\"auth\",\"ok\":false,\"error\":\"" + PeerLink.ProofWords + "\",\"class\":\"refused\",\"retry\":60,\"view\":true}"
                  && hall.Web.ProofFailures() is [{ Count: 1 }],
                $"peerlink scenarios: /ws acceptor — with a viewing code set a right proof links, and a wrong one hears LINK's refusal with its class and view "
                + $"before the close ({heard}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ Host and Origin before loopback is trusted (13.3.4)

    static async Task AcceptorHostAndOrigin(Action<bool, string> Check, string script, Material kit)
    {
        await using var hall = new Hall(kit, tls: false);
        var query = "/ws?node=9f8e7d6c&t=patterns&link=1";
        var named = await RawStatus(hall.Port, query);
        var rebound = await RawStatus(hall.Port, query, host: "evil.example");
        var foreign = await RawStatus(hall.Port, query, origin: "http://evil.example");
        var otherPort = await RawStatus(hall.Port, query, host: "127.0.0.1:1");
        Check(named.StartsWith("HTTP/1.1 101", StringComparison.Ordinal)
              && new[] { rebound, foreign, otherPort }.All(a => a.StartsWith("HTTP/1.1 403", StringComparison.Ordinal) && BodyError(a) == WebServer.LinkNeedsTls),
            $"peerlink scenarios: /ws acceptor — a plain link= socket on loopback is local only under a Host that names this server and no foreign Origin; "
            + $"a rebound name, another port or a foreign page is another machine's, refused before its upgrade ({rebound.Split("\r\n")[0]}; {foreign.Split("\r\n")[0]})");

        // A header credential on a LINK socket, and LINK with no node, are refused before the upgrade and counted nowhere.
        var header = await RawStatusWith(hall.Port, query, "Authorization: Bearer " + Swk);
        var noNode = await RawStatus(hall.Port, "/ws?link=1");
        Check(header.StartsWith("HTTP/1.1 400", StringComparison.Ordinal) && BodyError(header) == NodeLinks.LinkWithHeader
              && noNode.StartsWith("HTTP/1.1 400", StringComparison.Ordinal) && BodyError(noNode) == NodeLinks.LinkNamesNode
              && hall.Web.AuthFailures().Count == 0 && hall.Web.ProofFailures().Count == 0,
            "peerlink scenarios: /ws acceptor — a LINK socket with an Authorization header, or naming no node, is refused 400 before its upgrade, counted nowhere");

        // The peer's own foreign Host and Origin (13.20's S20 attack) on a plain link to this server: refused, nothing proved.
        await using var peer = Peer(script);
        var dial = await peer.Ask(new { @do = "dial", carrier = "ws", port = hall.Port, tls = false, node = Patterns, key = Key(kit, Swk), meant = Acceptor,
            attack = new { origin = "http://evil.example", host = "evil.example" } });
        var conn = Int(dial, "conn") ?? -1;
        var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
        Check(refused is { } r && (Str(r, "words") ?? "").Contains("403", StringComparison.Ordinal) && !peer.On("wrote", conn).Any(e => (Str(e, "message") ?? "").Contains("\"proof\"", StringComparison.Ordinal)),
            $"peerlink scenarios: /ws acceptor — the peer's foreign Host and Origin over loopback are refused before the upgrade: no offer, no proof sent {peer.Faults}");

        static async Task<string> RawStatusWith(int port, string target, string header)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var stream = tcp.GetStream();
            var head = $"GET {target} HTTP/1.1\r\nHost: 127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
                       + $"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\n{header}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var cts = new CancellationTokenSource(3000);
            try { return await reader.ReadToEndAsync(cts.Token); }
            catch (OperationCanceledException) { return ""; }
        }
    }

    // ------------------------------------------------------------------ side calls: S speaks for its key on its routes (13.7.1)

    static async Task AcceptorSideCalls(Action<bool, string> Check, string script, Material kit)
    {
        // A PIN set, so a route a call reaches with no credential is shut: only S speaking for its key opens it. Loopback plays another
        // machine (the IsThisPc seam), so S rides only inside the link's TLS.
        await using var hall = new Hall(kit, isThisPc: _ => false, pin: "4321");
        await using var peer = Peer(script);
        var conn = await DialHall(peer, hall, Key(kit, Swk));
        var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        Task<JsonElement> Call(string method, string path, object? body = null)
            => body is null ? peer.Ask(new { @do = "call", conn, method, path }) : peer.Ask(new { @do = "call", conn, method, path, body });

        var context = await Call("GET", "/api/context");
        var own = await Call("POST", "/api/peers/context", new { v = 1, node = new { t = "patterns", id = PatternsId, name = "Patterns FOH" }, light = "green", headline = "cue 12" });
        var ownSeen = hall.Control.LastCredential;
        // G15 through S: the key speaks for the node its LINK named, and posts under no other.
        var forged = await Call("POST", "/api/peers/context", new { v = 1, node = new { t = "playmap", id = "pm1abc", name = "Stage left" }, light = "green", headline = "x" });
        Check(link is not null && Int(context, "status") == 200 && Int(own, "status") == 200 && Str(Body(own), "verified") == "key" && ownSeen == NodeCredential.Key("kS")
              && Int(forged, "status") == 403 && (Str(Body(forged), "error") ?? "").StartsWith("this key speaks for patterns " + PatternsId, StringComparison.Ordinal)
              && hall.Control.LastCredential == NodeCredential.Key("kS") && hall.Control.Credentials.Count == 1,
            $"peerlink scenarios: side calls — S on /api/context and /api/peers/context speaks for its key: its own context is taken as key-verified, another node's "
            + $"is 403 in the binding's words ({Str(forged, "body")}) {peer.Faults}");

        var proposed = await Call("POST", "/api/proposals", new { from = new { t = "patterns", id = PatternsId }, target = new { t = "main" }, line = "note from the link" });
        var proposalSeen = hall.Control.LastCredential;
        var id = Str(Body(proposed), "id") ?? "p0";
        var read = await Call("GET", "/api/proposals/" + id);
        int heardBefore;
        lock (hall.Control.Credentials) heardBefore = hall.Control.Credentials.Count;
        var result = await Call("POST", "/api/proposals/" + id + "/result", new { ok = true, words = "done" });
        // The route reached the store as the key's: one credential more, the key's. A door that let S speak nowhere would answer 401
        // here and add none; the store's own answer is 409, for a proposal aimed at this Stagewatch's desk runs here.
        NodeCredential? resultSeen;
        lock (hall.Control.Credentials) resultSeen = hall.Control.Credentials.Count == heardBefore + 1 ? hall.Control.Credentials[heardBefore] : null;
        var message = await Call("POST", "/api/message/Doors%20in%205");
        Check(Int(proposed, "status") == 201 && proposalSeen == NodeCredential.Key("kS") && Int(read, "status") == 200
              && Int(result, "status") == 409 && (Str(Body(result), "error") ?? "").StartsWith("this Stagewatch runs its own proposals", StringComparison.Ordinal)
              && resultSeen == NodeCredential.Key("kS")
              && Int(message, "status") == 200 && hall.Control.Commands.Any(c => c.Kind == ShowCommandKind.Message && c.Text == "Doors in 5"),
            $"peerlink scenarios: side calls — S on the proposals, result and message routes speaks for its key: a proposal from its node queued, its result "
            + $"read as the key's, a stage message run ({Str(proposed, "body")}; {Str(result, "body")}; {Str(message, "body")}) {peer.Faults}");

        // Elsewhere S is no credential: the PIN's routes stay shut to it and count nothing; the public frame is the public frame.
        var start = await Call("POST", "/api/start");
        var state = await Call("GET", "/api/state?detail=1");
        // The detail asked for is the public agenda view: the operator's frame would carry the approval desk.
        var stateBody = Body(state);
        Check(Int(start, "status") == 401 && Str(Body(start), "error") == "PIN required" && !hall.Control.Commands.Any(c => c.Kind == ShowCommandKind.Start)
              && Int(state, "status") == 200 && stateBody.ValueKind == JsonValueKind.Object && stateBody.TryGetProperty("detail", out var detail)
              && detail.ValueKind == JsonValueKind.True && !stateBody.TryGetProperty("desk", out _)
              && hall.Web.AuthFailures().Count == 0 && hall.Web.ProofFailures().Count == 0,
            $"peerlink scenarios: side calls — S speaks on no other route: /api/start is 401 PIN required and counted nowhere, /api/state?detail=1 "
            + $"is the public agenda view ({Str(start, "body")}) {peer.Faults}");

        // A context too big to read, posted with S, is noted for the Show network by the node its link's key speaks for, not by address.
        var tooBig = await Call("POST", "/api/peers/context", new string('x', 70 * 1024));
        Check(Int(tooBig, "status") == 413 && hall.Web.TooBigContexts(DateTime.UtcNow) is [{ NodeId: PatternsId, Kind: "patterns" }],
            $"peerlink scenarios: side calls — a context too big posted with S is noted by the node its key speaks for "
            + $"({string.Join(", ", hall.Web.TooBigContexts(DateTime.UtcNow))}) {peer.Faults}");
        await Settle(peer);
        Check(Found(Stdout(peer), Canaries(Swk)) is null, "peerlink scenarios: side calls — no key or K on the peer's stdout");
    }

    // ------------------------------------------------------------------ S12's emitting half at /ws: the stuck beat

    static async Task AcceptorS12(Action<bool, string> Check, string script, Material kit)
    {
        await using var hall = new Hall(kit);
        hall.Control.Snapshots.MarkAlive();
        await using var peer = Peer(script);
        var conn = await DialHall(peer, hall, Key(kit, Swk));
        var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        // The show thread falls silent past StuckAfter: the verified link hears the stuck beat, and no frame while it lasts.
        Interlocked.Add(ref hall.ShowMs, (long)SnapshotHub.StuckAfter.TotalMilliseconds + 1);
        var stuck = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && Str(e, "message") == "{\"type\":\"beat\",\"live\":false}");
        var framesWhileStuck = ReadJson(peer, conn).Count(m => Str(m, "type") == "state");
        // It turns over again: a frame follows, and the link is live.
        hall.Control.Snapshots.MarkAlive();
        hall.Control.Snapshots.Publish(new ShowSnapshot { Text = "04:59", Seconds = 299, Title = "Keynote" });
        var live = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"04:59\"", StringComparison.Ordinal));
        Check(link is not null && stuck is not null && live is not null && ReadJson(peer, conn).Count(m => Str(m, "type") == "state") > framesWhileStuck,
            $"peerlink scenarios: S12 /ws acceptor — a verified link hears the stuck beat while the show thread is stuck, and frames again once it turns over {peer.Faults}");
    }

    // ------------------------------------------------------------------ S18 at /ws: a key bound to another connector

    static async Task AcceptorS18(Action<bool, string> Check, string script, Material kit)
    {
        // Its operator bound the key to another node; and, apart, the id is held by another key. Both: refused class binding in the
        // binding's words, retry 60, the socket a public viewer with no lid — counted nowhere, the key's rate one use.
        var other = KeyInForce("kO", ControlSecret);
        var legs = new (string Label, IReadOnlyList<ApiKey> Keys, string Words)[]
        {
            ("its key bound to another node", [WithNode(KeyInForce("kS", Swk), "patterns", "0f0f0f0f0f0f")],
                "this key speaks for patterns 0f0f0f0f0f0f — make a key for patterns " + PatternsId),
            ("its id held by another key", [KeyInForce("kS", Swk), WithNode(other, "patterns", PatternsId)], "patterns " + PatternsId + " speaks with its own key"),
        };
        foreach (var (label, keys, words) in legs)
        {
            await using var hall = new Hall(kit, keys: keys);
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, Swk));
            var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
            var frame = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"type\":\"state\"", StringComparison.Ordinal));
            var verdicts = ReadJson(peer, conn).Where(m => Str(m, "type") == "auth").ToList();
            Check(refused is { } r && Str(r, "class") == "binding" && Int(r, "retry") == 60 && Str(r, "words") == words
                  && verdicts.Count == 1 && verdicts[0].TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False && frame is not null
                  && hall.Web.ProofFailures().Count == 0 && hall.Web.AuthFailures().Count == 0 && hall.Web.ListNodes().All(f => f.Linked == 0)
                  && hall.Control.Nodes.NodeOf("kS") is not { Id: PatternsId },
                $"peerlink scenarios: S18 /ws acceptor, {label} — a binding refusal: class binding, retry 60, one verdict, a public viewer with no session, "
                + $"counted nowhere ({(refused is { } x ? x.GetRawText() : "no refusal")}) {peer.Faults}");
        }

        static ApiKey WithNode(ApiKey k, string kind, string id)
        {
            k.Scope = ApiKeyScope.Node;
            k.Node = id;
            k.NodeKind = kind;
            return k;
        }
    }

    // ------------------------------------------------------------------ 13.6.9: a key seen in clear

    static async Task AcceptorSeenInClear(Action<bool, string> Check, Material kit)
    {
        // Loopback plays another machine. A key in force sent plain is marked — once, from its address, whatever the answer; the PIN or an
        // unknown key marks nothing; over TLS nothing is marked.
        await using var hall = new Hall(kit, isThisPc: _ => false, pin: "4321");
        var sightings = ((IShowAccess)hall.Control).Sightings;
        var heard = new List<KeySighting>();
        sightings.Seen += s => { lock (heard) heard.Add(s); };
        var unknown = await RawGet(hall.Port, "/api/context", "Authorization: Bearer swk_zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz");
        var pin = await RawGet(hall.Port, "/api/context", "X-Stagewatch-Pin: 4321");
        var none = heard.Count;
        // The PIN in ?pin= beside a key in the bearer header: every carrier is read, so the key is marked though a route reads the PIN first.
        var plain = await RawGet(hall.Port, "/api/context?pin=4321", "Authorization: Bearer " + Swk);
        var again = await RawGet(hall.Port, "/api/start?pin=" + Swk);
        Check(unknown.StartsWith("HTTP/1.1 401", StringComparison.Ordinal) && pin.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && none == 0
              && plain.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && again.StartsWith("HTTP/1.1 403", StringComparison.Ordinal)
              && heard is [{ KeyId: "kS", Address: "127.0.0.1" }] && !sightings.MayGrant("kS") && sightings.All().Count == 1,
            $"peerlink scenarios: a key seen in clear — a key in force sent plain from another machine is marked once, from its address, whatever "
            + $"the route answers; an unknown key or the PIN marks nothing; the key gets no grant ({string.Join(", ", heard)})");

        // Over TLS the same key marks nothing; and a body too big to read is answered 413 after the mark, not before it.
        await using var second = new Hall(kit, isThisPc: _ => false);
        var secondSightings = ((IShowAccess)second.Control).Sightings;
        using (var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true })
        using (var https = new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{second.Port}") })
        {
            https.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Swk);
            (await https.GetAsync("/api/context")).Dispose();
        }
        var overTls = secondSightings.All().Count;
        var tooBig = await RawPost(second.Port, "/api/peers/context", "Authorization: Bearer " + Swk, new string('x', 70 * 1024));
        Check(overTls == 0 && tooBig.StartsWith("HTTP/1.1 413", StringComparison.Ordinal) && secondSightings.Of("kS") is { Address: "127.0.0.1" },
            $"peerlink scenarios: a key seen in clear — over TLS nothing is marked; sent plain with a body too big it is marked before the 413 ({tooBig.Split("\r\n")[0]})");

        static async Task<string> RawPost(int port, string path, string header, string body)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, port);
            var stream = tcp.GetStream();
            var head = $"POST {path} HTTP/1.1\r\nHost: 127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n{header}\r\n"
                       + $"Content-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(body).ToString(CultureInfo.InvariantCulture)}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head + body));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var cts = new CancellationTokenSource(15000);
            try { return await reader.ReadToEndAsync(cts.Token); }
            catch (Exception ex) when (ex is OperationCanceledException or IOException) { return ""; }
        }
    }
}
