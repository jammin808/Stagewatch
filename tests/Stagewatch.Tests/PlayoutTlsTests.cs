using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// A playout link to a PlayMap node over TLS (<c>playmap.lines.v1</c> on its <c>https</c> door, a line a message), against a node played
/// in this process on loopback with self-signed certificates, the link playing a node on another PC (its local-address seam says no).
/// First use: the door its hello names is dialled, never the line wire; the pairing is the proof alone; the certificate is pinned
/// <c>first</c> for the node its STATE names, compared and trusted by one press. Once a person pinned it the link is never dialled plain,
/// whatever the hello says or does not; a changed certificate is CERTIFICATE CHANGED at the handshake — no HTTP request, line or proof —
/// until a person types at least 8 hex of it. A pin pinned first decides the transport too: with no hello naming a door the link dials
/// the door kept beside its pin, never the line wire. A press is bound to the certificate it showed. A hello under §2's conflict mark
/// holds the link; a node whose STATE names another id than the one set is sent nothing. A 429 at the door and a close 4401 (or PlayMap's
/// ERR then 4401) hold the token; an answer without the subprotocol is no line channel. A plain link moves to TLS when its node's hello
/// names the door. A pin is kept for its node (<see cref="PlayoutKeptStore"/>), and the hello is chosen as the desk chooses it
/// (<see cref="PlayoutHellos"/>). Fields first, words beside them; the waits are real (the redial's first wait is 0.4 s with j = 0).
/// </summary>
static class PlayoutTlsTests
{
    const string Token = "AB12CD34";
    const string Nonce = "00112233445566778899aabbccddeeff";
    const string NodeId = "pm5e6f7a";
    const string PlayMapState = """{"v":1,"seq":4,"id":"pm5e6f7a","t":"playmap","video":{"file":"intro.mov","role":"playlist","positionMs":1000,"lengthMs":90000,"playing":true,"seq":2},"playlists":[],"sync":{"role":"solo","leader":""}}""";
    const string Context = """{"v":1,"node":{"t":"playmap","id":"pm5e6f7a","name":"Stage left"},"light":"green","headline":"Out 3 · 60 fps","items":[]}""";

    public static async Task Run(Action<bool, string> Check)
    {
        Heard(Check);
        KeptForItsNode(Check);
        KeptHoldFollowsItsToken(Check);
        Hellos(Check);
        await HelloSource(Check);
        using var files = new KitFiles();
        var first = files.Cert("node-a");
        var second = files.Cert("node-b");
        await FirstUseAndChange(Check, first, second);
        await PressBoundToWhatItShowed(Check, first, second);
        await PinnedNeverPlain(Check, first);
        await FirstPinNeverPlain(Check, first);
        await Conflicted(Check, first);
        await OtherNode(Check, first);
        await WssOnly(Check);
        await Locked(Check, first);
        await WrongToken(Check, first);
        await ErrThenClosed(Check, first);
        await NoSubprotocol(Check, first);
        await MovesToTls(Check, first);
    }

    // ------------------------------------------------------------------ helpers

    static string NewInst() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>A line as a node reads it: its request id (with its @) and its body.</summary>
    static (string Tag, string Body) Split(string line)
    {
        if (!line.StartsWith('@')) return ("", line);
        var sp = line.IndexOf(' ');
        return sp < 0 ? (line, "") : (line[..sp], line[(sp + 1)..]);
    }

    static string Ok(string tag, string words) => "OK" + (tag.Length > 0 ? " " + tag : "") + " " + words;

    /// <summary>A Show Bus 1.1 PlayMap node's answers at level 0: its proof's, the probe's, pings', STATUS, CONTEXT and WHOAMI.</summary>
    static IEnumerable<string> Answer(string line) => Answer(line, PlayMapState);

    /// <summary>As <see cref="Answer(string)"/>, its STATUS answering <paramref name="state"/>.</summary>
    static IEnumerable<string> Answer(string line, string state)
    {
        var (tag, body) = Split(line);
        if (body.StartsWith("AUTH-PROOF ", StringComparison.Ordinal))
            return [body == "AUTH-PROOF " + PlayoutWire.Proof(Token, Nonce) ? Ok(tag, "paired") : "ERR " + tag + " wrong token"];
        if (body.StartsWith("PING ", StringComparison.Ordinal)) return [Ok(tag, "PONG " + body[5..] + " " + body[5..] + " " + body[5..])];
        return body switch
        {
            "STATUS" => [Ok(tag, state)],
            "CONTEXT" => [Ok(tag, Context)],
            "WHOAMI" => [Ok(tag, """{"paired":true,"role":"operator","armed":false,"may":"*"}""")],
            _ => [],
        };
    }

    /// <summary>
    /// A PlayMap node's <c>https</c> door on loopback: TLS with <see cref="Cert"/> (swapped when a test changes it), then the upgrade
    /// to <c>/ws</c> — answered <see cref="Status"/> (101, or a refusal with its <c>Retry-After</c>), taking <c>playmap.lines.v1</c>
    /// while <see cref="TakesProtocol"/> — then NONCE and <see cref="Answer(string, string)"/>'s lines (its STATUS <see cref="State"/>), a
    /// message each, or for any proof a close 4401 while <see cref="Close4401"/>, or PlayMap's own <c>ERR … — closed</c> line and then
    /// the close while <see cref="ErrThen4401"/>. It counts what came: TLS handshakes completed and HTTP requests read; it keeps every
    /// line heard.
    /// </summary>
    sealed class TlsNode : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _stop = new();
        readonly Task _accepting;
        readonly Lock _gate = new();
        readonly List<WebSocket> _sockets = [];
        int _handshakes, _requests;

        public int Port { get; }
        public volatile KitCert Cert;
        public volatile int Status = 101;
        public volatile int RetryAfter;
        public volatile bool TakesProtocol = true;
        public volatile bool Close4401;
        public volatile bool ErrThen4401;
        public volatile string State = PlayMapState;
        public ConcurrentQueue<string> Heard { get; } = new();
        public int Handshakes => Volatile.Read(ref _handshakes);
        public int Requests => Volatile.Read(ref _requests);

        public TlsNode(KitCert cert)
        {
            Cert = cert;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = Task.Run(Accept);
        }

        /// <summary>The hello this node sends: its id, its <c>https</c> door, and the certificate it names there.</summary>
        public PlayoutHeard Hello() => new(NodeId, PlayoutRules.PlayMap, Port, "sha256:" + Cert.Fp, "1a2b3c4d", "127.0.0.1");

        async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        async Task Serve(TcpClient client)
        {
            using var _ = client;
            try
            {
                await using var ssl = new SslStream(client.GetStream(), false);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = Cert.Server }, cts.Token);
                Interlocked.Increment(ref _handshakes);
                if (await Kit.Head(ssl) is not { } head) return;
                Interlocked.Increment(ref _requests);
                if (Status != 101)
                {
                    var retry = RetryAfter > 0 ? $"Retry-After: {RetryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n" : "";
                    await ssl.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {Status.ToString(System.Globalization.CultureInfo.InvariantCulture)} Refused\r\n{retry}Content-Length: 0\r\nConnection: close\r\n\r\n"), cts.Token);
                    return;
                }
                var key = head.Headers.GetValueOrDefault("Sec-WebSocket-Key") ?? "";
                var asked = (head.Headers.GetValueOrDefault("Sec-WebSocket-Protocol") ?? "").Split(',').Select(p => p.Trim());
                var take = TakesProtocol && asked.Contains(PlayoutWire.LinesProtocol);
                var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                await ssl.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept
                    + "\r\n" + (take ? "Sec-WebSocket-Protocol: " + PlayoutWire.LinesProtocol + "\r\n" : "") + "\r\n"), cts.Token);
                using var ws = WebSocket.CreateFromStream(ssl, new WebSocketCreationOptions
                {
                    IsServer = true, SubProtocol = take ? PlayoutWire.LinesProtocol : null, KeepAliveInterval = TimeSpan.Zero,
                });
                lock (_gate) _sockets.Add(ws);
                await Send(ws, "NONCE " + Nonce, _stop.Token);
                var buf = new byte[65536];
                var message = new List<byte>();
                while (ws.State == WebSocketState.Open)
                {
                    var r = await ws.ReceiveAsync(buf, _stop.Token);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    message.AddRange(buf.AsSpan(0, r.Count).ToArray());
                    if (!r.EndOfMessage) continue;
                    var line = Encoding.UTF8.GetString(message.ToArray());
                    message.Clear();
                    Heard.Enqueue(line);
                    var (tag, body) = Split(line);
                    if ((Close4401 || ErrThen4401) && body.StartsWith("AUTH-PROOF ", StringComparison.Ordinal))
                    {
                        // PlayMap says why before it closes: ERR, its words and "— closed", then 4401 (WireSession.Authenticate).
                        if (ErrThen4401) await Send(ws, "ERR " + tag + " too many wrong tokens from this address — wait a minute — closed", _stop.Token);
                        await ws.CloseOutputAsync((WebSocketCloseStatus)PlayoutWire.WrongProofsClose, "wrong token", _stop.Token);
                        return;
                    }
                    foreach (var a in Answer(line, State)) await Send(ws, a, _stop.Token);
                }
            }
            catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException or ObjectDisposedException or WebSocketException
                                          or SocketException or InvalidOperationException)
            {
                // The connection ended: the link refused the certificate, closed it, or the test did.
            }
        }

        static Task Send(WebSocket ws, string line, CancellationToken ct) => ws.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, true, ct);

        /// <summary>The door shuts: no new connection is taken, and every line channel up now is cut.</summary>
        public void Shut()
        {
            _listener.Stop();
            DropAll();
        }

        /// <summary>Every line channel up now, cut by the node: the link redials.</summary>
        public void DropAll()
        {
            lock (_gate)
            {
                foreach (var ws in _sockets) ws.Abort();
                _sockets.Clear();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            DropAll();
            await _accepting.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { }, TaskScheduler.Default);
            _stop.Dispose();
        }
    }

    /// <summary>
    /// A link to node <see cref="NodeId"/> as the desk makes one, its line wire at <paramref name="plainPort"/> (where a plain dial would
    /// go), the node's hello as <paramref name="hello"/> gives it, and what it keeps handed to <paramref name="kept"/>.
    /// </summary>
    static PlayoutLink Link(int plainPort, Func<PlayoutHeard?> hello, ConcurrentQueue<PeerKept>? kept = null, PeerKept? from = null, string transport = PlayoutRules.Auto,
        ConcurrentQueue<int>? doors = null, int door = 0)
        => new(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = plainPort, Token = Token, Inst = NewInst(), Name = "stage-left", Kind = PlayoutRules.PlayMap,
            IsLoopback = PlayoutLinkTests.Remote, Jitter = () => 0, Transport = transport, NodeId = NodeId, Hello = hello, Kept = from,
            Keep = kept is null ? null : k =>
            {
                kept.Enqueue(k);
                return Task.FromResult(true);
            },
            Door = door,
            KeepDoor = doors is null ? null : doors.Enqueue,
        });

    static Task<bool> Until(Func<bool> cond, int ms = 10_000) => PlayoutLinkTests.Until(cond, ms);

    static Task<bool> PairedOverTls(PlayoutLink link) => Until(() => link.Pairing == PlayoutPairing.Paired && link.OverTls && link.Video is { File: "intro.mov" });

    // ------------------------------------------------------------------ pure

    /// <summary>A hello names a TLS door only for a PlayMap node, on a port, beside a certificate it names (13.4.2: a hint, never a pin).</summary>
    static void Heard(Action<bool, string> Check)
    {
        var fp = "sha256:" + new string('a', 64);
        Check(new PlayoutHeard(NodeId, PlayoutRules.PlayMap, 9443, fp, null, "10.0.0.12").TlsDoor == 9443
              && new PlayoutHeard(NodeId, PlayoutRules.Patterns, 9443, fp, null, "10.0.0.12").TlsDoor is null
              && new PlayoutHeard(NodeId, PlayoutRules.PlayMap, 0, fp, null, "10.0.0.12").TlsDoor is null
              && new PlayoutHeard(NodeId, PlayoutRules.PlayMap, 9443, new string('a', 64), null, "10.0.0.12").TlsDoor is null
              && new PlayoutHeard(NodeId, PlayoutRules.PlayMap, 9443, null, null, "10.0.0.12").TlsDoor is null
              && new PlayoutHeard(NodeId, PlayoutRules.PlayMap, 9443, fp, null, "10.0.0.12", ConflictLeftMs: 1).TlsDoor is null,
              "playout tls: a hello names a door only for a PlayMap node, on a port, beside the certificate it names as sha256:<64 hex> — never one under a conflict mark");
        Check(PlayoutWire.Short("03e9b152ca42518c" + new string('0', 48)) == "03e9 b152 ca42 518c"
              && PlayoutWire.Fingerprint([1, 2, 3]) == Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])),
              "playout tls: a fingerprint is SHA-256 of the DER in lowercase hex, shown by its first 16 hex in fours");
    }

    /// <summary>
    /// 13.15.3: a hold holds the token it was for. A link renamed in Settings, its token kept, starts from the hold its old name kept at
    /// the same address or node — so it never proves at once the token its peer refused — capped as any restored hold is; never a hold
    /// for another token, at another address or node, or one that has ended; and a link's own hold comes before another's.
    /// </summary>
    static void KeptHoldFollowsItsToken(Action<bool, string> Check)
    {
        var now = 1_700_000_000_000L;
        string tag1 = PlayoutKeptStore.Tag("token one"), tag2 = PlayoutKeptStore.Tag("token two");
        const string Here = "10.0.0.12:9941", There = "10.0.0.13:9941";
        var store = new PlayoutKeptStore(null);
        store.Keep("stage-left", tag1, new PeerKept(null, null, now + 30_000, "key"), now, Here);
        Check(store.For("renamed", tag1, now, null, Here) is { HeldUntilWall: var until, HeldRefusal: "key", Pin: null } && until == now + 30_000,
              "playout kept: a link renamed, its token kept, starts held as its old name was at the same address — it never proves the refused token again at once");
        Check(store.For("renamed", tag2, now, null, Here) is null && store.For("renamed", tag1, now, null, There) is null && store.For("renamed", tag1, now) is null
              && store.For("renamed", tag1, now + 30_000, null, Here) is null,
              "playout kept: never another token's hold, one kept at another address, one with no address to match, or one that has ended");

        // With its node set, a hold kept for that node (its binding, or its pin's node) follows the token to another address.
        store.Keep("by-node", tag1, new PeerKept(null, null, now + 20_000, "locked", NodeId, "playmap"), now + 1, "10.0.0.20:9941");
        Check(store.For("renamed-2", tag1, now, NodeId, "10.0.0.99:9941") is { HeldRefusal: "locked", NodeId: null } && store.For("renamed-2", tag1, now, "pm000000", "10.0.0.99:9941") is null,
              "playout kept: with a node set, the hold kept for that node follows its token — its binding does not — and none kept for another node");

        // A restored hold that would end too late is capped from now, carried or its own.
        store.Keep("far", tag2, new PeerKept(null, null, now + 10 * PlayoutKeptStore.HoldMaxMs, "busy"), now, There);
        Check(store.For("far-renamed", tag2, now, null, There)?.HeldUntilWall == now + PlayoutKeptStore.HoldMaxMs,
              "playout kept: a carried hold ends within the longest a hold may be held, as a restored one does");

        // A link's own hold for its token comes first; its own entry with no hold takes one kept for its token elsewhere.
        store.Keep("own", tag1, new PeerKept(null, null, now + 5_000, "scope"), now + 2, Here);
        store.Keep("plain", tag1, new PeerKept(new PeerPin(null, new string('d', 64), PinTrust.First), null, null, null), now, Here);
        Check(store.For("own", tag1, now, null, Here) is { HeldRefusal: "scope", HeldUntilWall: var ownUntil } && ownUntil == now + 5_000
              && store.For("stage-left", tag1, now, null, Here) is { HeldRefusal: "key" }
              && store.For("plain", tag1, now, null, Here) is { HeldRefusal: "scope", Pin: not null },
              "playout kept: a link's own hold comes first; a link holding none starts from the newest hold its token has at its address, keeping its own pin");
    }

    /// <summary>
    /// 13.8: a pin is for its node. A link set to another node since starts with none of the old one's pin, floor or binding; a link new
    /// under its name for a node another entry pinned starts from that pin and floor, never its hold, which is the token's.
    /// </summary>
    static void KeptForItsNode(Action<bool, string> Check)
    {
        var store = new PlayoutKeptStore(null);
        var now = 1_700_000_000_000L;
        string tag1 = PlayoutKeptStore.Tag("token one"), tag2 = PlayoutKeptStore.Tag("token two");
        var pin = new PeerPin(NodeId, new string('c', 64), PinTrust.Person);
        var floor = new PeerFloor(true, 0, "legacy");
        store.Keep("stage-left", tag1, new PeerKept(pin, floor, now + 30_000, "refused", NodeId, "playmap"), now);
        Check(store.For("stage-left", tag1, now, NodeId) is { Pin: { } same, Floor.Tls: true, HeldRefusal: "refused" } && same == pin
              && store.For("stage-left", tag1, now) is { Pin: not null } && store.For("stage-left", tag1, now, "") is { Pin: not null },
              "playout kept: the link set to the pin's node, or to none, starts from its pin, floor and hold");
        Check(store.For("stage-left", tag1, now, "pm999999") is { Pin: null, Floor: null, NodeId: null, HeldRefusal: "refused" },
              "playout kept: a link set to another node since starts with none of the old node's pin, floor or binding — its token's hold stays");
        Check(store.For("stage-right", tag2, now, NodeId) is { Pin: { } moved, Floor.Tls: true, HeldUntilWall: null, HeldRefusal: null } && moved == pin
              && store.For("stage-right", tag2, now) is null && store.For("stage-right", tag2, now, "pm999999") is null,
              "playout kept: a link new under its name for a pinned node starts from that node's pin and floor, never another token's hold");

        // With no node set, a pin is the one kept at the address the link dials: another address may be another node.
        var at = new PlayoutKeptStore(null);
        const string Here = "10.0.0.12:9941", There = "10.0.0.13:9941";
        at.Keep("stage-left", tag1, new PeerKept(pin, floor, null, null), now, Here);
        _ = at.KeepDoor("stage-left", 9443, now);
        Check(at.For("stage-left", tag1, now, null, Here) is { Pin: { } p1 } && p1 == pin && at.DoorFor("stage-left", null, Here) == 9443
              && at.For("stage-left", tag1, now, null, There) is null && at.DoorFor("stage-left", null, There) == 0
              && at.For("stage-left", tag1, now, NodeId, There) is { Pin: not null } && at.DoorFor("stage-left", NodeId, There) == 9443,
              "playout kept: with no node set, a pin and the TLS door beside it are the ones kept at the address the link dials — moved to another, it starts with neither; with its node set, the node's");
        Check(at.For("renamed", tag2, now, null, Here) is { Pin: { } p2, HeldUntilWall: null } && p2 == pin && at.DoorFor("renamed", null, Here) == 9443
              && at.For("renamed", tag2, now, null, There) is null,
              "playout kept: a link renamed, its node not set, finds the pin and door kept at its address — never another address's");
        at.Keep("held-only", tag2, new PeerKept(null, null, now + 30_000, "key"), now, There);
        at.Keep("pinned-x", tag1, new PeerKept(pin, null, null, null), now + 1, "10.0.0.99:9941");
        Check(at.For("held-only", tag2, now, NodeId, There) is { Pin: { } p3, HeldRefusal: "key" } && p3 == pin,
              "playout kept: a link whose own entry keeps no pin finds the one kept for the node it is set to, keeping its own hold");
        _ = at.KeepDoor("stage-left", 0, now);
        at.Keep("stage-left", tag1, new PeerKept(null, null, now + 30_000, "key"), now, Here);
        _ = at.KeepDoor("stage-left", 9443, now);
        Check(at.Links["stage-left"].Door == 0 && at.Links["pinned-x"].Door == 0,
              "playout kept: a link that keeps no pin keeps no door beside it");
        at.Keep("stage-left", tag1, new PeerKept(pin, floor, null, null), now, Here);
        _ = at.KeepDoor("stage-left", 9443, now);
        var json = PlayoutKeptStore.ToJson(at.Links);
        var back = PlayoutKeptStore.Parse(json);
        Check(back["stage-left"] is { Door: 9443, Address: Here } && json.Contains("\"door\":9443", StringComparison.Ordinal)
              && PlayoutKeptStore.Parse(json.Replace("\"door\":9443", "\"door\":70000", StringComparison.Ordinal))["stage-left"].Door == 0,
              "playout kept: the door and the address come back as they were written; a door that is no port is dropped, never guessed");
    }

    /// <summary>
    /// The hello a link takes for its node, as the desk chooses it (Show Bus §2, 13.4.2): by the node id Settings set, else a PlayMap
    /// node heard at its host — by an address a hello came from, by the hello's own host name, or by an address the host name resolves
    /// to — heard within 6 s; one under a conflict mark comes with its mark and nothing else of its hello; the certificate the hello at
    /// the link's address named, else its last one's.
    /// </summary>
    static void Hellos(Action<bool, string> Check)
    {
        var fpA = "sha256:" + new string('a', 64);
        var fpB = "sha256:" + new string('b', 64);
        SlidesBeacon.Peer Peer(string id, string kind, string address, double age = 1, HelloConflict? conflict = null, string? tlsAtAddress = null, string? host = null)
            => new(new SlidesBeacon.Hello(kind, 1, id, "Stage left", "STAGE-PC", 0, false, Proto: 3, Ports: new() { ["https"] = 9443, ["wire"] = 9941 },
                    Host: host, Tls: fpA, Inst: "1a2b3c4d"), address, age)
            {
                Addresses = [address], Conflict = conflict, ConflictLeftMs = conflict is null ? 0 : 40_000,
                HeardTls = tlsAtAddress is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [address] = tlsAtAddress },
            };
        var playmap = Peer(NodeId, PlayoutRules.PlayMap, "10.0.0.12", tlsAtAddress: fpB, host: "stage-left");
        var patterns = Peer("pat1", PlayoutRules.Patterns, "10.0.0.20");
        Check(PlayoutHellos.Choose([patterns, playmap], NodeId, "anything") is { Id: NodeId, TlsDoor: 9443, Tls: var t1, Inst: "1a2b3c4d" } && t1 == fpA
              && PlayoutHellos.Choose([patterns, playmap], "pm000000", "10.0.0.12") is null,
              "playout hellos: with a node set, its hello is chosen by id wherever it was heard, and no other");
        Check(PlayoutHellos.Choose([patterns, playmap], "", "10.0.0.12") is { Id: NodeId, Tls: var t2 } && t2 == fpB
              && PlayoutHellos.Choose([patterns, playmap], "", "stage-left") is { Id: NodeId }
              && PlayoutHellos.Choose([patterns, playmap], "", "stage-left.lan", ["10.0.0.12"]) is { Id: NodeId }
              && PlayoutHellos.Choose([patterns, playmap], "", "stage-left.lan") is null
              && PlayoutHellos.Choose([patterns, playmap], "", "10.0.0.20") is null,
              "playout hellos: with none set, a PlayMap node heard at the link's host — its address, its own host name, or an address the name resolves to — never a Patterns desk; the certificate the hello at that address named");
        Check(PlayoutHellos.Choose([Peer(NodeId, PlayoutRules.PlayMap, "10.0.0.12", age: 7)], NodeId, "10.0.0.12") is null,
              "playout hellos: a hello not heard for more than 6 s is none");
        var conflict = new HelloConflict(NodeId, "STAGE-PC", "10.0.0.12", "COPY-PC", "10.0.0.66");
        Check(PlayoutHellos.Choose([Peer(NodeId, PlayoutRules.PlayMap, "10.0.0.12", conflict: conflict, tlsAtAddress: fpB)], NodeId, "10.0.0.12")
                  is { ConflictLeftMs: 40_000, TlsDoor: null, Tls: null, Inst: null, HttpsPort: 0 },
              "playout hellos: a node two machines say comes with its mark and nothing else of its hello — no door, no certificate");
    }

    /// <summary>
    /// The desk's hello source never waits: a host name is resolved off the asker's thread, its addresses matched once the answer
    /// came, and asked again only <see cref="PlayoutHelloSource.ResolveEvery"/> after it; an address or a node id is never resolved.
    /// </summary>
    static async Task HelloSource(Action<bool, string> Check)
    {
        var clock = new PlayoutLinkTests.LinkClock(1_700_000_000_000);
        var asked = 0;
        var answer = new TaskCompletionSource<IPAddress[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<IPAddress[]> Resolve(string host, CancellationToken ct)
        {
            Interlocked.Increment(ref asked);
            return host == "stage-left.lan" ? answer.Task : Task.FromResult(Array.Empty<IPAddress>());
        }
        var peer = new SlidesBeacon.Peer(new SlidesBeacon.Hello(PlayoutRules.PlayMap, 1, NodeId, "Stage left", "STAGE-PC", 0, false, Proto: 3,
            Ports: new() { ["https"] = 9443 }, Tls: "sha256:" + new string('a', 64)), "10.0.0.12", 1) { Addresses = ["10.0.0.12"] };
        var source = new PlayoutHelloSource(() => [peer], "", "stage-left.lan", Resolve, clock);
        var before = source.Heard();
        var once = await Until(() => Volatile.Read(ref asked) == 1, 2000);
        var still = source.Heard();
        answer.SetResult([IPAddress.Parse("10.0.0.12")]);
        var found = await Until(() => source.Heard() is { Id: NodeId, TlsDoor: 9443 }, 2000);
        var askedThen = Volatile.Read(ref asked);
        clock.Advance((long)PlayoutHelloSource.ResolveEvery.TotalMilliseconds);
        _ = source.Heard();
        var again = await Until(() => Volatile.Read(ref asked) == 2, 2000);
        Check(before is null && once && still is null && found && askedThen == 1 && again,
              $"playout hellos: a host name is resolved off the asker's thread — no hello until its addresses come, then the node's — and again only after {PlayoutHelloSource.ResolveEvery.TotalSeconds} s ({Volatile.Read(ref asked)} asks)");
        var byAddress = new PlayoutHelloSource(() => [peer], "", "10.0.0.12", Resolve, clock);
        var byId = new PlayoutHelloSource(() => [peer], NodeId, "stage-left.lan", Resolve, clock);
        var none = new PlayoutHelloSource(() => null, "", "10.0.0.12", Resolve, clock);
        Check(byAddress.Heard() is { Id: NodeId } && byId.Heard() is { Id: NodeId } && none.Heard() is null && Volatile.Read(ref asked) == 2,
              "playout hellos: an address, or a node id, is never resolved; no show network hears no hello");
    }

    // ------------------------------------------------------------------ against a node

    static async Task FirstUseAndChange(Action<bool, string> Check, KitCert first, KitCert second)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(first);
        var kept = new ConcurrentQueue<PeerKept>();
        using var link = Link(plain.Port, node.Hello, kept);
        link.Start();
        var paired = await PairedOverTls(link) && await Until(() => link.Kept.Pin is not null);
        var heard = node.Heard.ToArray();
        Check(paired && plain.Taken == 0 && node.Requests == 1 && heard.Contains("HELLO Stagewatch")
              && heard.Any(l => l.StartsWith("@pa AUTH-PROOF ", StringComparison.Ordinal)) && !heard.Any(l => l.StartsWith("AUTH ", StringComparison.Ordinal) || l.Contains(Token, StringComparison.Ordinal)),
              $"playout tls: first use dials the TLS door its hello names, never the line wire, and pairs by the proof alone ({link.Status}; {plain.Taken} plain)");
        Check(link.Kept.Pin is { Trust: PinTrust.First, NodeId: NodeId } pin && pin.Fingerprint == first.Fp && link.RecordNow().Tls == "first"
              && link.TrustWaiting is { Changed: false, Pinned: "" } wait && wait.Fingerprint == first.Fp && wait.NodeId == NodeId
              && link.Status.Contains("pinned first only — compare it on its own screen", StringComparison.Ordinal)
              && link.Status.Contains(PlayoutWire.Short(first.Fp), StringComparison.Ordinal) && kept.Any(k => k.Pin?.Fingerprint == first.Fp),
              $"playout tls: its certificate is pinned first for the node its STATE names — kept to compare, its short form in the words ({link.Status})");

        var (otherTrusted, notIt) = link.Trust(second.Fp, typed: false);
        Check(!otherTrusted && notIt.Contains("is not how the certificate", StringComparison.Ordinal) && link.Kept.Pin is { Trust: PinTrust.First },
              $"playout tls: one press bound to another fingerprint trusts nothing ({notIt})");
        var (pressedFirst, trusted) = link.Trust(first.Fp, typed: false);
        Check(pressedFirst && trusted.Contains("trusted by a person on this desk", StringComparison.Ordinal)
              && link.Kept.Pin is { Trust: PinTrust.Person, NodeId: NodeId } && link.TrustWaiting is null && link.RecordNow().Tls == "pinned"
              && kept.LastOrDefault()?.Pin is { Trust: PinTrust.Person } && link.Status.Contains("over TLS, its certificate pinned", StringComparison.Ordinal),
              $"playout tls: one press beside the short form makes the pin a person's, kept before the link dials again ({trusted})");
        var (again, none) = link.Trust(first.Fp, typed: false);
        Check(!again && none.Contains("no certificate", StringComparison.Ordinal), "playout tls: with nothing waiting, a press trusts nothing");

        // The node now shows another certificate: the handshake ends there — no HTTP request, no line, no proof.
        var requests = node.Requests;
        var lines = node.Heard.Count;
        node.Cert = second;
        node.DropAll();
        var refused = await Until(() => link.State == PeerLinkState.Refused);
        await Task.Delay(600);
        Check(refused && link.RecordNow() is { State: "refused", Refusal: "certificate" } && link.Health == LinkHealth.Error
              && node.Requests == requests && node.Heard.Count == lines && plain.Taken == 0,
              $"playout tls: a changed certificate is CERTIFICATE CHANGED at the handshake — red, and no HTTP request, line or proof reaches it ({link.Status})");
        Check(link.Status.StartsWith($"CERTIFICATE CHANGED: {link.Peer} now shows {PlayoutWire.Short(second.Fp)} …, not the certificate pinned for it ({PlayoutWire.Short(first.Fp)} …) — nothing sent", StringComparison.Ordinal)
              && link.Status.EndsWith($"type at least 8 hex of it on this desk ({PlayoutRules.TrustWhere})", StringComparison.Ordinal)
              && link.TrustWaiting is { Changed: true } changed && changed.Fingerprint == second.Fp && changed.Pinned == first.Fp && changed.NodeId == NodeId,
              $"playout tls: its words name both short forms and where a person types the new one ({link.Status})");

        var (byPress, pressed) = link.Trust(second.Fp, typed: false);
        var (byShort, shortHex) = link.Trust(second.Fp[..7], typed: true);
        var (byLetters, _) = link.Trust("zzzzzzzz", typed: true);
        var (byOther, wrong) = link.Trust(first.Fp[..8] == second.Fp[..8] ? "00000000" : first.Fp[..8], typed: true);
        Check(!byPress && pressed.Contains("type at least 8 hex", StringComparison.Ordinal) && !byShort && shortHex.Contains("type 8 to 64 hex", StringComparison.Ordinal)
              && !byLetters && !byOther && wrong.Contains("is not how", StringComparison.Ordinal)
              && link.State == PeerLinkState.Refused && link.Kept.Pin is { Trust: PinTrust.Person } p1 && p1.Fingerprint == first.Fp,
              $"playout tls: a press that typed nothing, fewer than 8 hex, no hex, or another certificate's trusts nothing ({pressed} | {shortHex} | {wrong})");
        await Task.Delay(600);
        Check(node.Requests == requests && node.Heard.Count == lines, "playout tls: while it waits for a person, nothing goes past the handshake");

        var (typedIn, accepted) = link.Trust(PlayoutWire.Short(second.Fp)[..9].ToUpperInvariant(), typed: true);
        Check(typedIn && accepted.Contains("in place of the one pinned before", StringComparison.Ordinal)
              && link.Kept.Pin is { Trust: PinTrust.Person, NodeId: NodeId } p2 && p2.Fingerprint == second.Fp && kept.LastOrDefault()?.Pin?.Fingerprint == second.Fp,
              $"playout tls: at least 8 hex typed as the short form reads (spaces, either case) pin the new certificate for its node, kept ({accepted})");
        Check(await PairedOverTls(link) && node.Requests == requests + 1 && link.RecordNow().Tls == "pinned" && plain.Taken == 0,
              $"playout tls: the link dials it again over TLS and pairs ({link.Status})");
    }

    /// <summary>
    /// A press is bound to the certificate it showed (13.8.2): drawn while A is pinned first, then the node shows B — pinned first in A's
    /// place on its next pairing — a press bound to A trusts nothing, nor does part of B's fingerprint; B's own whole one does.
    /// </summary>
    static async Task PressBoundToWhatItShowed(Action<bool, string> Check, KitCert first, KitCert second)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(first);
        using var link = Link(plain.Port, node.Hello);
        link.Start();
        var pinnedA = await PairedOverTls(link) && await Until(() => link.Kept.Pin is { Trust: PinTrust.First } p && p.Fingerprint == first.Fp);
        var drawn = link.TrustWaiting;
        node.Cert = second;
        node.DropAll();
        var pinnedB = await Until(() => link.Kept.Pin is { Trust: PinTrust.First } p && p.Fingerprint == second.Fp) && await PairedOverTls(link);
        var (byA, wordsA) = link.Trust(drawn!.Fingerprint, typed: false);
        var (byPart, wordsPart) = link.Trust(second.Fp[..16], typed: false);
        Check(pinnedA && drawn is { Changed: false } && drawn.Fingerprint == first.Fp && pinnedB && !byA && !byPart
              && link.Kept.Pin is { Trust: PinTrust.First } still && still.Fingerprint == second.Fp && plain.Taken == 0
              && wordsA.Contains("is not how the certificate", StringComparison.Ordinal) && wordsPart.Contains("bound to its whole fingerprint", StringComparison.Ordinal),
              $"playout tls: a press drawn for A trusts nothing once B is pinned first in its place, nor does part of B's fingerprint ({wordsA} | {wordsPart})");
        var (byB, _) = link.Trust(second.Fp, typed: false);
        Check(byB && link.Kept.Pin is { Trust: PinTrust.Person } mine && mine.Fingerprint == second.Fp,
              "playout tls: the press drawn for B trusts B");
    }

    /// <summary>
    /// 13.4.2: for an id with a pin the pin alone decides — a pin made first too. The door its certificate answered at is kept beside it;
    /// with its hello gone or naming no door, the link dials that door, never the line wire; a link started again from the kept pin and
    /// door does the same; and with that door shut it dials nothing plain.
    /// </summary>
    static async Task FirstPinNeverPlain(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk { Greeting = () => ["NONCE " + Nonce], Answer = Answer };
        await using var node = new TlsNode(cert);
        PlayoutHeard? heard = node.Hello();
        var doors = new ConcurrentQueue<int>();
        PeerKept kept;
        using (var link = Link(plain.Port, () => Volatile.Read(ref heard), doors: doors))
        {
            link.Start();
            var first = await PairedOverTls(link) && await Until(() => link.Kept.Pin is { Trust: PinTrust.First } && doors.Contains(node.Port));
            Check(first && plain.Taken == 0, $"playout tls: pinned first over TLS, the door its certificate answered at is kept beside the pin ({string.Join(",", doors)})");
            // A hello that names no door now — stale, forged or a node gone back to plain — lowers nothing: the kept door is dialled.
            Volatile.Write(ref heard, new PlayoutHeard(NodeId, PlayoutRules.PlayMap, 0, null, "1a2b3c4d", "127.0.0.1"));
            var handshakes = node.Handshakes;
            node.DropAll();
            var again = await Until(() => node.Handshakes > handshakes) && await PairedOverTls(link);
            Check(again && plain.Taken == 0 && link.Kept.Pin is { Trust: PinTrust.First },
                  $"playout tls: a pin seen first is never dialled plain — with no hello naming a door it dials the door kept beside its pin ({link.Status})");
            kept = link.Kept;
        }
        Volatile.Write(ref heard, null);
        using (var restarted = Link(plain.Port, () => Volatile.Read(ref heard), from: kept, door: node.Port))
        {
            restarted.Start();
            Check(await PairedOverTls(restarted) && plain.Taken == 0,
                  $"playout tls: started again from its kept pin and door with no hello heard, it dials TLS there and pairs ({restarted.Status})");
            var port = node.Port;
            node.Shut();
            var down = await Until(() => restarted.Status.Contains($"not reachable over TLS at 127.0.0.1:{port}", StringComparison.Ordinal), 15_000);
            await Task.Delay(1000);
            Check(down && plain.Taken == 0 && !restarted.Connected,
                  $"playout tls: with its door shut it dials that door again, and never the line wire ({restarted.Status})");
        }
    }

    /// <summary>
    /// 13.4.4: a hello under §2's conflict mark — two running nodes say the id, one may be forged — holds the link while the mark stands:
    /// nothing dialled, TLS or plain, amber with the words; once the mark lapses the link dials the door a clean hello names.
    /// </summary>
    static async Task Conflicted(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(cert);
        PlayoutHeard? heard = node.Hello() with { ConflictLeftMs = 2_500 };
        using var link = Link(plain.Port, () => Volatile.Read(ref heard));
        link.Start();
        var held = await Until(() => link.State == PeerLinkState.Held && link.RecordNow().Refusal == "conflict");
        await Task.Delay(1500);
        Check(held && link.State == PeerLinkState.Held && plain.Taken == 0 && node.Handshakes == 0 && link.Health == LinkHealth.Starting
              && link.Status.Contains("two machines say they are " + NodeId, StringComparison.Ordinal),
              $"playout tls: a hello under a conflict mark holds the link — nothing dialled, amber, said ({link.Health}: {link.Status})");
        Volatile.Write(ref heard, node.Hello());
        Check(await PairedOverTls(link) && plain.Taken == 0, $"playout tls: once the mark lapses, the door a clean hello names is dialled ({link.Status})");
    }

    /// <summary>
    /// 13.4.3: an address that answers as another node than the one Settings set — its STATE names another id — is amber, both ids named,
    /// sent nothing, feeds no reader, and its certificate is pinned nothing.
    /// </summary>
    static async Task OtherNode(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(cert) { State = PlayMapState.Replace(NodeId, "pm999999", StringComparison.Ordinal) };
        using var link = Link(plain.Port, node.Hello);
        link.Start();
        var said = await Until(() => link.WrongKind is { } w && w.Contains("answers as node pm999999, not node " + NodeId, StringComparison.Ordinal));
        await Task.Delay(500);
        var sent = link.Clip("RESTART", 0);
        Check(said && link.Pairing == PlayoutPairing.Paired && link.Health == LinkHealth.Starting && link.Video is null && link.PulledContext is null
              && link.Kept.Pin is null && link.TrustWaiting is null && sent.Ticket is null && sent.Error is { } e && e.Contains("answers as node pm999999", StringComparison.Ordinal)
              && !node.Heard.Any(l => l.Contains("VIDEO", StringComparison.Ordinal)) && link.Status.Contains("answers as node pm999999", StringComparison.Ordinal),
              $"playout tls: another node at the address is amber with both ids, sent nothing, read for nothing and pinned nothing ({link.Status})");
    }

    static async Task PinnedNeverPlain(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(cert);
        PlayoutHeard? heard = null;
        var pinned = new PeerKept(new PeerPin(NodeId, cert.Fp, PinTrust.Person), null, null, null);
        using var link = Link(plain.Port, () => Volatile.Read(ref heard), from: pinned);
        link.Start();
        var said = await Until(() => link.Status.Contains("never dialled plain", StringComparison.Ordinal) && link.Status.Contains("looking again in", StringComparison.Ordinal));
        await Task.Delay(1500);
        Check(said && plain.Taken == 0 && node.Handshakes == 0 && link.State != PeerLinkState.Refused && link.Health == LinkHealth.Starting
              && link.Status.Contains("no hello names its TLS door now", StringComparison.Ordinal),
              $"playout tls: a node a person pinned is never dialled plain — with no TLS door heard it dials nothing, amber, and says so ({link.Status})");
        Volatile.Write(ref heard, node.Hello());
        Check(await PairedOverTls(link) && plain.Taken == 0 && link.Kept.Pin is { Trust: PinTrust.Person },
              $"playout tls: once its hello names the door, it dials TLS there and pairs under its pin ({link.Status})");
    }

    static async Task WssOnly(Action<bool, string> Check)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        using var link = Link(plain.Port, () => null, transport: PlayoutRules.Wss);
        link.Start();
        var said = await Until(() => link.Status.Contains("is set to dial wss only, so it is never dialled plain", StringComparison.Ordinal));
        await Task.Delay(1000);
        Check(said && plain.Taken == 0 && link.State != PeerLinkState.Refused,
              $"playout tls: a link set to wss with no TLS door heard dials nothing plain, and says so ({link.Status})");
    }

    static async Task Locked(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        // Retry-After longer than the floor's minute: the hold is the node's, from the moment the 429 came.
        await using var node = new TlsNode(cert) { Status = 429, RetryAfter = 120 };
        var kept = new ConcurrentQueue<PeerKept>();
        using var link = Link(plain.Port, node.Hello, kept);
        var started = TimeProvider.System.GetUtcNow().ToUnixTimeMilliseconds();
        link.Start();
        var held = await Until(() => link.State == PeerLinkState.Held);
        await Task.Delay(1500);
        var until = link.Kept.HeldUntilWall ?? 0;
        Check(held && node.Requests == 1 && node.Heard.IsEmpty && plain.Taken == 0 && link.Health == LinkHealth.Error
              && link.RecordNow().Refusal == "locked" && link.Kept.HeldRefusal == "locked" && kept.Any(k => k.HeldRefusal == "locked")
              && link.Status.Contains("locked this PC out after wrong tokens (429 at 127.0.0.1:", StringComparison.Ordinal)
              && link.Status.Contains("the token is held", StringComparison.Ordinal),
              $"playout tls: a 429 at the door is a lockout — red, the token held and kept, nothing sent and no dial while it holds ({link.Status})");
        Check(until - started is >= 119_000 and <= 125_000,
              $"playout tls: a 429's Retry-After longer than the floor's minute holds the token that long ({(until - started) / 1000.0:0.#} s)");
    }

    static async Task WrongToken(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(cert) { Close4401 = true };
        using var link = Link(plain.Port, node.Hello);
        link.Start();
        var held = await Until(() => link.State == PeerLinkState.Held);
        await Task.Delay(1500);
        Check(held && node.Requests == 1 && node.Heard.Count(l => l.Contains("AUTH-PROOF", StringComparison.Ordinal)) == 1 && link.Health == LinkHealth.Error
              && link.RecordNow().Refusal == "key" && link.Kept.HeldRefusal == "key" && link.Pairing == PlayoutPairing.None
              && link.Status.Contains("closed the link: wrong token (4401: wrong token)", StringComparison.Ordinal),
              $"playout tls: a close 4401 is a wrong token — red, held, one proof and no second ({link.Status})");
    }

    /// <summary>
    /// PlayMap as it is: a wrong proof is answered <c>ERR … — closed</c> (for an address its wrong tokens locked out, or the fifth
    /// wrong one) and the channel closes 4401 after it. The ERR holds the token: one proof, red, held once — the close that follows
    /// is not a second refusal.
    /// </summary>
    static async Task ErrThenClosed(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(cert) { ErrThen4401 = true };
        var kept = new ConcurrentQueue<PeerKept>();
        using var link = Link(plain.Port, node.Hello, kept);
        link.Start();
        var held = await Until(() => link.State == PeerLinkState.Held);
        await Task.Delay(1500);
        Check(held && node.Requests == 1 && node.Heard.Count(l => l.Contains("AUTH-PROOF", StringComparison.Ordinal)) == 1 && link.Health == LinkHealth.Error
              && link.RecordNow().Refusal == "key" && link.Kept.HeldRefusal == "key" && kept.Count(k => k.HeldUntilWall is not null) == 1
              && link.Pairing == PlayoutPairing.None && plain.Taken == 0
              && link.Status.Contains("too many wrong tokens from this address", StringComparison.Ordinal) && link.Status.Contains("the token is held", StringComparison.Ordinal),
              $"playout tls: PlayMap's ERR then its close 4401 hold the token once — one proof, red, no second dial while it holds ({link.Status})");
    }

    static async Task NoSubprotocol(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk();
        await using var node = new TlsNode(cert) { TakesProtocol = false };
        using var link = Link(plain.Port, node.Hello);
        link.Start();
        var said = await Until(() => link.Status.Contains("did not take playmap.lines.v1", StringComparison.Ordinal));
        await Task.Delay(300);
        Check(said && node.Requests >= 1 && node.Heard.IsEmpty && link.Pairing == PlayoutPairing.None && link.Health != LinkHealth.Ok && plain.Taken == 0,
              $"playout tls: an upgrade that does not take playmap.lines.v1 is no line channel — nothing is sent on it ({link.Status})");
    }

    static async Task MovesToTls(Action<bool, string> Check, KitCert cert)
    {
        await using var plain = new PlayoutLinkTests.FakeDesk { Greeting = () => ["NONCE " + Nonce], Answer = Answer };
        await using var node = new TlsNode(cert);
        PlayoutHeard? heard = null;
        using var link = Link(plain.Port, () => Volatile.Read(ref heard));
        link.Start();
        var plainPaired = await Until(() => link.Pairing == PlayoutPairing.Paired && !link.OverTls && link.Kind == PlayoutRules.PlayMap);
        Check(plainPaired && plain.Taken == 1 && node.Handshakes == 0 && link.Kept.Pin is null,
              $"playout tls: with no hello naming a door, an auto link to a PlayMap node pairs on its line wire, and pins nothing ({link.Status})");
        Volatile.Write(ref heard, node.Hello());
        Check(await PairedOverTls(link) && plain.Taken == 1 && link.Kept.Pin is { Trust: PinTrust.First } pin && pin.Fingerprint == cert.Fp,
              $"playout tls: once its hello names the TLS door, the link moves there between commands and pins the certificate first ({link.Status})");
    }
}
