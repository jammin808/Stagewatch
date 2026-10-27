using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Stagewatch.Core;

/// <summary>
/// The scripted peer, tools/showbus/peerlink-peer.mjs (Show Bus 13.20), as a process the suite drives: one JSON command a line on its
/// stdin, one event a line on its stdout, every event kept in order with its number <c>n</c>. Node runs the file; the peer takes no
/// arguments, and every key, code and certificate it uses comes from a file of <see cref="KitFiles"/>.
/// </summary>
sealed class ScriptedPeer : IAsyncDisposable
{
    readonly Process _process;
    readonly List<JsonElement> _events = [];
    readonly List<string> _unread = [];
    readonly StringBuilder _stderr = new();
    readonly Lock _gate = new();
    readonly Task _reader;
    int _tags;

    ScriptedPeer(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line) lock (_gate) _stderr.AppendLine(line);
        };
        _process.BeginErrorReadLine();
        _reader = Task.Run(async () =>
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                try
                {
                    var e = JsonDocument.Parse(line).RootElement.Clone();
                    lock (_gate) _events.Add(e);
                }
                catch (JsonException)
                {
                    lock (_gate) _unread.Add(line);
                }
            }
        });
    }

    /// <summary>The peer's file in this tree: tools/showbus/peerlink-peer.mjs beside docs/SHOWBUS.md's tree; null when not found.</summary>
    public static string? Script()
        => ShowBusSpecTests.Master() is { } master && Path.GetDirectoryName(Path.GetDirectoryName(master)) is { } root
            && Path.Combine(root, "tools", "showbus", "peerlink-peer.mjs") is var path && File.Exists(path) ? path : null;

    /// <summary>Node, started on <paramref name="script"/> with <paramref name="arguments"/> after it (the peer takes none).</summary>
    public static Process Node(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false),
        };
        start.ArgumentList.Add(script);
        foreach (var a in arguments) start.ArgumentList.Add(a);
        return Process.Start(start) ?? throw new InvalidOperationException("node did not start");
    }

    public static ScriptedPeer Start(string script) => new(Node(script));

    /// <summary>Every event so far, in order.</summary>
    public IReadOnlyList<JsonElement> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    /// <summary>What the peer wrote to stderr, and any stdout line that was not an event.</summary>
    public string Faults
    {
        get { lock (_gate) return _stderr + string.Join("\n", _unread); }
    }

    /// <summary>Sends <paramref name="command"/> with a tag of its own and waits for the one event that answers it (its <c>re</c>).</summary>
    public async Task<JsonElement> Ask(object command, int ms = 4000)
    {
        var node = JsonSerializer.SerializeToNode(command)!.AsObject();
        var tag = "t" + Interlocked.Increment(ref _tags).ToString(System.Globalization.CultureInfo.InvariantCulture);
        node["tag"] = tag;
        await _process.StandardInput.WriteLineAsync(node.ToJsonString());
        await _process.StandardInput.FlushAsync();
        return await Wait(e => Str(e, "re") == tag, ms) ?? JsonDocument.Parse("{\"ev\":\"no answer\"}").RootElement.Clone();
    }

    /// <summary>The first event after <paramref name="after"/> that <paramref name="match"/> takes, waiting at most <paramref name="ms"/>.</summary>
    public async Task<JsonElement?> Wait(Func<JsonElement, bool> match, int ms = 2000, int after = 0)
    {
        var clock = Stopwatch.StartNew();
        for (;;)
        {
            foreach (var e in Events)
                if ((Int(e, "n") ?? 0) > after && match(e)) return e;
            if (clock.ElapsedMilliseconds > ms || _process.HasExited) return null;
            await Task.Delay(10);
        }
    }

    /// <summary>The events after <paramref name="after"/> of one kind on one connection.</summary>
    public List<JsonElement> On(string ev, int conn, int after = 0)
        => Events.Where(e => (Int(e, "n") ?? 0) > after && Str(e, "ev") == ev && Int(e, "conn") == conn).ToList();

    /// <summary>The lines or messages the peer read on <paramref name="conn"/>.</summary>
    public List<string> ReadOn(int conn) => On("read", conn).Select(e => Str(e, "line") ?? Str(e, "message") ?? "").ToList();

    /// <summary>Every line and message the peer read, on any connection: what crossed the wire toward it.</summary>
    public List<string> AllRead() => Events.Where(e => Str(e, "ev") == "read").Select(e => Str(e, "line") ?? Str(e, "message") ?? "").ToList();

    public static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int? Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("{\"do\":\"quit\"}");
                await _process.StandardInput.FlushAsync();
            }
            using var cts = new CancellationTokenSource(3000);
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidOperationException)
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        await _reader.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { }, TaskScheduler.Default);
        _process.Dispose();
    }
}

/// <summary>A certificate the kit made: its PEM file and its key's (each 0600), its fingerprint (13.8.1) and the form a .NET server shows.</summary>
sealed record KitCert(string CertFile, string KeyFile, string Fp, X509Certificate2 Server);

/// <summary>
/// The kit's files: keys, codes and certificates the scripted peer reads, each written owner-only (0600) in a directory of its own, as
/// 13.20 asks — never an argument. It owns the certificates it makes for the kit's own TLS ends, so the key container each one holds
/// on Windows goes with them. Gone when disposed (<see cref="Scratch"/>).
/// </summary>
sealed class KitFiles : IDisposable
{
    readonly List<X509Certificate2> _made = [];
    readonly ScratchFolder _folder = Scratch.Folder("peerlink");

    public string Dir => _folder.Path;

    public KitFiles()
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>A file of <paramref name="text"/>, owner-only; <paramref name="open"/> leaves it readable by others, as 13.20 forbids.</summary>
    public string Write(string name, string text, bool open = false)
    {
        var path = Path.Combine(Dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | (open ? UnixFileMode.GroupRead | UnixFileMode.OtherRead : 0));
        return path;
    }

    /// <summary>A self-signed P-256 certificate for a TLS end of the peer's or the kit's.</summary>
    public KitCert Cert(string name)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, ec, HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var server = X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12), null);
        _made.Add(server);
        return new KitCert(Write(name + ".crt", cert.ExportCertificatePem()), Write(name + ".key", ec.ExportPkcs8PrivateKeyPem()),
            Convert.ToHexStringLower(SHA256.HashData(cert.RawData)), server);
    }

    public void Dispose()
    {
        foreach (var cert in _made) cert.Dispose();
        _made.Clear();
        _folder.Dispose();
    }
}

/// <summary>
/// One connection's traffic as the kit's carriers see it: lines on a stream, or text messages on a WebSocket, each read in turn with a
/// real-time bound; a null read is the other end closing.
/// </summary>
abstract class KitIo : IAsyncDisposable
{
    /// <summary>What a read hands on for a line or message longer than the carrier reads whole: no line can hold a NUL.</summary>
    public const string Oversize = "\0oversize";

    /// <summary>What a connector reads whole (§14's <c>link.read.min.bytes</c>): a longer line or message is <see cref="Oversize"/> (13.14).</summary>
    public const int ReadMax = 262144;

    protected readonly Channel<string?> Incoming = Channel.CreateUnbounded<string?>();

    public abstract Task Send(string text);

    /// <summary>The next line or message within <paramref name="ms"/>: (false, null) when none came in time, (true, null) when the other end closed.</summary>
    public async Task<(bool Got, string? Text)> Next(int ms)
    {
        using var cts = new CancellationTokenSource(Math.Max(1, ms));
        try
        {
            return (true, await Incoming.Reader.ReadAsync(cts.Token));
        }
        catch (OperationCanceledException)
        {
            return (false, null);
        }
        catch (ChannelClosedException)
        {
            return (true, null);
        }
    }

    public abstract ValueTask DisposeAsync();
}

/// <summary>
/// Lines over a stream: a TCP socket, or TLS over one. A line past <see cref="KitIo.ReadMax"/> bytes is handed on as
/// <see cref="KitIo.Oversize"/> once and skipped to its LF, so its tail is never read as a line of its own (13.14).
/// </summary>
sealed class StreamIo : KitIo
{
    readonly TcpClient _tcp;
    readonly Stream _stream;
    readonly StreamWriter _writer;

    public StreamIo(TcpClient tcp, Stream stream)
    {
        _tcp = tcp;
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[16384];
                using var line = new MemoryStream();
                var skipping = false;
                int n;
                while ((n = await stream.ReadAsync(buffer)) > 0)
                {
                    for (var at = 0; at < n;)
                    {
                        var lf = Array.IndexOf(buffer, (byte)'\n', at, n - at);
                        var end = lf < 0 ? n : lf;
                        if (!skipping && line.Length + (end - at) > ReadMax)
                        {
                            skipping = true;
                            line.SetLength(0);
                            await Incoming.Writer.WriteAsync(Oversize);
                        }
                        else if (!skipping) line.Write(buffer, at, end - at);
                        if (lf < 0) break;
                        if (!skipping)
                        {
                            var text = Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length);
                            await Incoming.Writer.WriteAsync(text.EndsWith('\r') ? text[..^1] : text);
                        }
                        line.SetLength(0);
                        skipping = false;
                        at = lf + 1;
                    }
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or AuthenticationException)
            {
                // The socket ended under the reader: the null below says so.
            }
            Incoming.Writer.TryWrite(null);
            Incoming.Writer.TryComplete();
        });
    }

    public override async Task Send(string text)
    {
        try { await _writer.WriteLineAsync(text); }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { Incoming.Writer.TryWrite(null); }
    }

    public override ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _tcp.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Text messages over a WebSocket, a client's or a server's. A message past <see cref="KitIo.ReadMax"/> bytes is read to its end and
/// handed on as <see cref="KitIo.Oversize"/> (13.14).
/// </summary>
sealed class WsIo : KitIo
{
    readonly WebSocket _ws;
    readonly IDisposable? _owner;

    public WsIo(WebSocket ws, IDisposable? owner = null)
    {
        _ws = ws;
        _owner = owner;
        _ = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    var over = false;
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(buffer, CancellationToken.None);
                        over |= message.Length + r.Count > ReadMax;
                        if (!over) message.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage && r.MessageType != WebSocketMessageType.Close);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    await Incoming.Writer.WriteAsync(over ? Oversize : Encoding.UTF8.GetString(message.ToArray()));
                }
            }
            catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The socket ended under the reader: the null below says so.
            }
            Incoming.Writer.TryWrite(null);
            Incoming.Writer.TryComplete();
        });
    }

    public override async Task Send(string text)
    {
        try { await _ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException) { Incoming.Writer.TryWrite(null); }
    }

    public override ValueTask DisposeAsync()
    {
        _ws.Abort();
        _ws.Dispose();
        _owner?.Dispose();
        return ValueTask.CompletedTask;
    }
}

static class Kit
{
    public static string Fp(X509Certificate cert) => Convert.ToHexStringLower(SHA256.HashData(cert.GetRawCertData()));

    public static string Nonce() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(PeerLink.NonceBytes));

    public static async Task<TcpClient?> Connect(int port)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var cts = new CancellationTokenSource(2000);
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            return tcp;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            tcp.Dispose();
            return null;
        }
    }

    /// <summary>An HTTP head read byte by byte off <paramref name="stream"/>, so nothing after it is taken: its start line and headers.</summary>
    public static async Task<(string Start, Dictionary<string, string> Headers)?> Head(Stream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 16384)
        {
            if (await stream.ReadAsync(one) == 0) return null;
            bytes.Add(one[0]);
            if (bytes.Count >= 4 && bytes[^1] == '\n' && bytes[^2] == '\r' && bytes[^3] == '\n' && bytes[^4] == '\r') break;
        }
        var lines = Encoding.Latin1.GetString(bytes.ToArray()).Split("\r\n");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines.Skip(1))
            if (l.IndexOf(':') is var i and > 0) headers[l[..i].Trim()] = l[(i + 1)..].Trim();
        return (lines[0], headers);
    }
}

/// <summary>
/// Stagewatch's core as a connector over a real socket: <see cref="PeerLinkConnector"/> decides, and this carrier only dials, reads,
/// writes and proves with K — as PlayoutLink will once its playout links run on the core (SW20.4a/b, SW23.1). On <c>line</c> it speaks
/// the line grammar over TCP, over TLS, or over <c>playmap.lines.v1</c>; on <c>ws</c> it is a node link with its side calls. Each
/// method runs at the injected time it is given; the socket's own waits are bounded in real time.
/// </summary>
sealed class CoreConnector : IAsyncDisposable
{
    const string Id = "3f9c2a1b7d0e";
    const string Name = "Stagewatch";

    readonly PeerCarrier _carrier;
    readonly bool _linesOverWs;
    readonly byte[] _k;
    readonly string _kid;
    readonly string? _code;
    KitIo? _io;
    PeerOffer? _offer;
    string _cnonce = Kit.Nonce();
    string? _nonce;
    int _n, _rq;
    bool _helloAnswered, _first, _tlsUp;
    readonly HashSet<string> _linkIds = new(StringComparer.Ordinal);
    readonly HashSet<string> _rqIds = new(StringComparer.Ordinal);
    readonly HashSet<string> _commandIds = new(StringComparer.Ordinal);

    public PeerLinkConnector M { get; }
    public int Port { get; set; }
    /// <summary>The certificate the last TLS handshake showed, as 64 hex.</summary>
    public string? Shown { get; private set; }
    /// <summary>The core ended the last dial at the certificate: nothing of the connector's went past the TLS handshake.</summary>
    public bool EndedAtCertificate { get; private set; }
    public List<string> Wrote { get; } = [];
    /// <summary>The credentials the carrier put on the wire, by kind (13.20's <c>credentials</c>).</summary>
    public HashSet<string> Credentials { get; } = new(StringComparer.Ordinal);
    /// <summary>Writes the core asked for that this carrier cannot make in the kit.</summary>
    public List<string> Unmade { get; } = [];
    public int Sockets { get; private set; }
    /// <summary>
    /// What the core published after each step, as every reader would take it (13.19's capture): its 13.16.3 record, and the words of
    /// each command this carrier wrote.
    /// </summary>
    public List<string> Published { get; } = [];
    /// <summary>
    /// The secrets and the handshake's values this carrier held or made — K, the nonces, both proofs, S, and a legacy NONCE and its
    /// AUTH-PROOF — none of which the core may publish (13.19).
    /// </summary>
    public HashSet<string> Made { get; } = new(StringComparer.Ordinal);
    /// <summary>Every session credential S this carrier made: of <see cref="Made"/>, the ones no wire may carry in clear either (13.7, 13.19).</summary>
    public HashSet<string> Sessions { get; } = new(StringComparer.Ordinal);
    /// <summary>The last verdict the acceptor sent this carrier, as it came: <c>OK @l paired</c>'s JSON, or the <c>auth</c> message.</summary>
    public JsonElement? LastVerdict { get; private set; }

    public CoreConnector(PeerLinkConnector connector, PeerCarrier carrier, byte[] k, string kid, string? code = null, bool linesOverWs = false)
    {
        M = connector;
        _carrier = carrier;
        _k = k;
        _kid = kid;
        _code = code;
        _linesOverWs = linesOverWs;
    }

    /// <summary>
    /// T of this connection, once the acceptor's offer came: its <c>bind</c> the certificate this connection was shown, never the one
    /// the offer names (13.6.1). The proofs, lid and S are made from it with K.
    /// </summary>
    public string? T => _offer is { } o
        ? PeerLink.Transcript(_carrier == PeerCarrier.Line ? "line" : "ws", PeerLink.Version, o.Speaks, o.T + ":" + o.Id, "main:" + Id, _kid, o.SNonce, _cnonce,
            _tlsUp && Shown is { } shown ? "tls:sha256:" + shown : "plain")
        : null;

    bool VerifyAcceptor(string proof) => T is { } t && PeerLink.Verify(PeerLink.ServerProof(_k, t), proof);

    /// <summary>How a socket the carrier opened came out.</summary>
    enum Opened
    {
        Up,
        NoConnection,
        NoTls,
        EndedAtCertificate,
        Failed,
    }

    /// <summary>
    /// The core dials when its schedule lets it; the carrier opens the socket its scheme names. A connection that never came is a failed
    /// dial; TLS that fails for want of TLS on offer is the core's NoTls; a certificate the core refuses ends the handshake before
    /// anything is written. Returns the scheme, or null when the core does not dial.
    /// </summary>
    public async Task<string?> Dial(long t)
    {
        var scheme = M.Dial(t);
        if (scheme is null) return null;
        _offer = null;
        _nonce = null;
        _n = _rq = 0;
        _rqIds.Clear();
        _helloAnswered = false;
        _first = true;
        _cnonce = Kit.Nonce();
        _linkIds.Clear();
        Shown = null;
        EndedAtCertificate = false;
        switch (await Open(t, scheme == "tls"))
        {
            case Opened.Up:
                if (scheme != "tls") M.Connected(t);
                break;
            case Opened.NoTls:
                M.NoTls(t);
                if (M.KeepsConnection(t) && await Open(t, tls: false) != Opened.Up) M.Closed(t);
                break;
            case Opened.EndedAtCertificate:
                EndedAtCertificate = true;
                break;
            default:
                M.Closed(t);
                break;
        }
        await Flush(t);
        return scheme;
    }

    /// <summary>One socket, TLS or plain. In a TLS handshake the core reads the certificate before anything is written (13.8).</summary>
    async Task<Opened> Open(long t, bool tls)
    {
        Sockets++;
        var consulted = false;
        var refused = false;
        bool Consult(X509Certificate? cert)
        {
            consulted = true;
            Shown = cert is null ? null : Kit.Fp(cert);
            if (Shown is null) return false;
            M.Certificate(t, Shown);
            refused = !M.KeepsConnection(t);
            return !refused;
        }
        Opened Failed(Exception? e)
        {
            if (refused) return Opened.EndedAtCertificate;
            if (consulted) return Opened.Failed;
            for (var x = e; x is not null; x = x.InnerException)
                if (x is SocketException) return Opened.NoConnection;
            return tls ? Opened.NoTls : Opened.NoConnection;
        }
        if (_carrier == PeerCarrier.Ws || _linesOverWs)
        {
            var ws = new ClientWebSocket();
            ws.Options.Proxy = null;
            if (tls) ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => Consult(cert);
            if (_linesOverWs) ws.Options.AddSubProtocol("playmap.lines.v1");
            var query = _carrier == PeerCarrier.Ws ? "?node=" + Id + "&t=main&link=" + PeerLink.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            try
            {
                using var cts = new CancellationTokenSource(2000);
                await ws.ConnectAsync(new Uri($"{(tls ? "wss" : "ws")}://127.0.0.1:{Port}/ws{query}"), cts.Token);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or AuthenticationException or IOException or HttpRequestException)
            {
                ws.Dispose();
                return Failed(e);
            }
            _io = new WsIo(ws);
            _tlsUp = tls;
            return Opened.Up;
        }
        if (await Kit.Connect(Port) is not { } tcp) return Opened.NoConnection;
        if (!tls)
        {
            _io = new StreamIo(tcp, tcp.GetStream());
            _tlsUp = false;
            return Opened.Up;
        }
        var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => Consult(cert));
        try
        {
            using var cts = new CancellationTokenSource(2000);
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "peer" }, cts.Token);
        }
        catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException)
        {
            await ssl.DisposeAsync();
            tcp.Dispose();
            return refused ? Opened.EndedAtCertificate : consulted ? Opened.Failed : Opened.NoTls;
        }
        _io = new StreamIo(tcp, ssl);
        _tlsUp = true;
        return Opened.Up;
    }

    /// <summary>Writes what the core asked for since the last drain, in order; closes the socket once the core ended the connection.</summary>
    public async Task Flush(long t)
    {
        var line = _carrier == PeerCarrier.Line;
        foreach (var w in M.Drain())
        {
            if (w.Credential is { } c) Credentials.Add(c);
            var text = (w.Verb, w.Credential) switch
            {
                ("HELLO", null) => "HELLO " + Name,
                ("LINK", null) => LinkId() + " LINK " + PeerLink.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) + " main " + Id + " " + _cnonce + " " + _kid,
                ("LINK", "proof") when T is { } tt => LinkId() + " LINK PROOF " + PeerLink.ClientProof(_k, tt),
                ("link", "proof") when T is { } tt => JsonSerializer.Serialize(new { type = "link", client = new { t = "main", id = Id }, cnonce = _cnonce, kid = _kid, proof = PeerLink.ClientProof(_k, tt) }),
                ("AUTH-PROOF", "auth-proof") when _code is not null && _nonce is not null => LinkId("pa") + " AUTH-PROOF " + PeerLink.LegacyProof(_code, _nonce, Name),
                ("RECEIPTS", null) => "@" + RqId() + " RECEIPTS " + w.Detail,
                ("receipts", null) when w.Detail is { } ids => JsonSerializer.Serialize(new { cmd = "receipts", args = ids.Split(' '), id = RqId() }),
                ({ } verb, null) when w.Detail is { } id => line ? "@" + id + " " + verb : JsonSerializer.Serialize(new { cmd = verb, args = Array.Empty<string>(), id }),
                _ => null,
            };
            if (text is null)
            {
                Unmade.Add(w.Verb + " " + w.Credential);
                continue;
            }
            if (_io is null) continue;
            await _io.Send(text);
            Wrote.Add(text);
            if (w.Credential == "auth-proof")
            {
                Made.Add(_nonce!);
                Made.Add(text.Split(' ')[^1]);
            }
            else if (w.Credential is null && w.Detail is { } command && w.Verb is { } verb && !verb.Equals("RECEIPTS", StringComparison.OrdinalIgnoreCase)) _commandIds.Add(command);
        }
        if (_io is not null && !M.KeepsConnection(t)) await Drop();
        Published.Add(M.Record(t).ToJson());
        foreach (var id in _commandIds) Published.Add(M.WordsOf(id));
        Made.Add(Convert.ToHexStringLower(_k));
        Made.Add(_cnonce);
        if (T is { } made && _offer is { } offer)
        {
            Made.Add(offer.SNonce);
            Made.Add(PeerLink.ClientProof(_k, made));
            Made.Add(PeerLink.ServerProof(_k, made));
            Made.Add(PeerLink.Session(_k, made));
            Sessions.Add(PeerLink.Session(_k, made));
        }
    }

    /// <summary>A RECEIPTS ask's own id, <c>rq&lt;n&gt;</c>, counted from 1 on its connection (13.12.1).</summary>
    string RqId()
    {
        var id = "rq" + (++_rq).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _rqIds.Add(id);
        return id;
    }

    string LinkId(string prefix = "l")
    {
        var id = prefix + (++_n).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _linkIds.Add(id);
        return "@" + id;
    }

    /// <summary>A ping with this t1 (the connector's wall clock): the core matches the pong it answers (13.10.2).</summary>
    public async Task Ping(long t, long t1)
    {
        M.Ping(t, t1);
        if (_io is null) return;
        var text = _carrier == PeerCarrier.Line
            ? "@p" + t1.ToString(System.Globalization.CultureInfo.InvariantCulture) + " PING " + t1.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "{\"cmd\":\"ping\",\"t\":" + t1.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
        await _io.Send(text);
        Wrote.Add(text);
    }

    /// <summary>
    /// Reads what comes and hands it to the core at injected time <paramref name="t"/>, writing what the core asks for, until
    /// <paramref name="until"/> holds, the socket ends, or <paramref name="ms"/> of real time pass; whether <paramref name="until"/> holds.
    /// </summary>
    public async Task<bool> Pump(long t, Func<bool> until, int ms = 2000)
    {
        var clock = Stopwatch.StartNew();
        while (!until())
        {
            if (_io is null) return until();
            var left = ms - (int)clock.ElapsedMilliseconds;
            if (left <= 0) return false;
            var (got, text) = await _io.Next(left);
            if (!got) return until();
            if (text is null)
            {
                await Drop();
                M.Closed(t);
                continue;
            }
            if (text == KitIo.Oversize) M.Oversize(t);
            else if (_carrier == PeerCarrier.Line) FeedLine(t, text);
            else FeedMessage(t, text);
            await Flush(t);
        }
        return true;
    }

    /// <summary>A line read on a <c>line</c> link, handed to the core as 13.6.4, 13.6.8 and 13.12.3 read it.</summary>
    void FeedLine(long t, string text)
    {
        if (text.StartsWith("NONCE ", StringComparison.Ordinal))
        {
            _nonce = text[6..];
            M.Nonce(t, _nonce);
            return;
        }
        if (text == "OK" && !_helloAnswered)
        {
            _helloAnswered = true;
            return;
        }
        var m = Regex.Match(text, @"^(?:OK|ERR) @(\S+)");
        if (m.Success && _linkIds.Contains(m.Groups[1].Value))
        {
            if (PeerLink.ParseLinkAnswer(text) is { } offer) _offer = offer;
            if (text.IndexOf(" paired {", StringComparison.Ordinal) is var at and > 0) LastVerdict = Json(text[(at + 8)..]);
            M.LineAnswer(t, text, VerifyAcceptor);
            return;
        }
        if (m.Success && _rqIds.Contains(m.Groups[1].Value))
        {
            M.Heard(t);
            if (text.StartsWith("OK ", StringComparison.Ordinal) && text.IndexOf('{') is var brace and > 0 && Json(text[brace..]) is { } answer) Receipts(t, answer);
            return;
        }
        // 13.5.5: a live link refused - its key revoked, its code replaced - with no id, as the acceptor ends it.
        if (text.StartsWith("ERR LINK ", StringComparison.Ordinal))
        {
            M.LineAnswer(t, text, VerifyAcceptor);
            return;
        }
        var pong = Regex.Match(text, @"^OK(?: @\S+)? PONG (\d+) (\d+) (\d+)( STUCK)?$");
        if (pong.Success)
        {
            M.Pong(t, long.Parse(pong.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                long.Parse(pong.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                long.Parse(pong.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), pong.Groups[4].Success);
            return;
        }
        if (m.Success && M.Knows(m.Groups[1].Value) && PeerLink.ReadLine(text) is { Id: { } id } read)
        {
            M.Heard(t);
            M.Answer(t, id, read.Reading);
            return;
        }
        M.Frame(t);
    }

    /// <summary>A message read on a <c>ws</c> link (13.6.5): the offer, the verdict, beats, pongs, receipts and frames.</summary>
    void FeedMessage(long t, string text)
    {
        JsonElement msg;
        try
        {
            msg = JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (JsonException)
        {
            M.Malformed(t);
            return;
        }
        var type = msg.ValueKind == JsonValueKind.Object && msg.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String ? ty.GetString() : null;
        if (_first)
        {
            _first = false;
            if (type == "link")
            {
                _offer = PeerLink.ParseLinkOffer(msg);
                M.LinkOffered(t, _offer);
            }
            else M.FirstFrame(t);
            return;
        }
        switch (type)
        {
            case "link": M.LinkOffered(t, PeerLink.ParseLinkOffer(msg)); break;
            case "auth":
                LastVerdict = msg;
                M.Verdict(t, msg, VerifyAcceptor);
                break;
            case "beat": M.Beat(t, !(msg.TryGetProperty("live", out var live) && live.ValueKind == JsonValueKind.False)); break;
            case "pong":
                M.Pong(t, msg.TryGetProperty("t", out var t1) ? t1.GetInt64() : null, msg.TryGetProperty("t2", out var t2) ? t2.GetInt64() : null,
                    msg.TryGetProperty("t3", out var t3) ? t3.GetInt64() : null, false);
                break;
            case "receipt" when msg.TryGetProperty("id", out var rq) && rq.ValueKind == JsonValueKind.String && _rqIds.Contains(rq.GetString()!):
                M.Heard(t);
                if (msg.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True) Receipts(t, msg);
                break;
            case "receipt" when msg.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String:
                M.Heard(t);
                M.Answer(t, id.GetString()!, PeerLink.ReadReceipt(msg));
                break;
            default: M.Frame(t); break;
        }
    }

    /// <summary>13.12.6: a RECEIPTS answer's entries, each read by 13.12.3, or null for <c>unknown</c>, handed to the core.</summary>
    void Receipts(long t, JsonElement answer)
    {
        if (!answer.TryGetProperty("receipts", out var rs) || rs.ValueKind != JsonValueKind.Object) return;
        M.Receipts(t, rs.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ValueKind == JsonValueKind.Object ? PeerLink.ReadReceipt(x.Value) : (PeerReading?)null));
    }

    static JsonElement? Json(string text)
    {
        try { return JsonDocument.Parse(text).RootElement.Clone(); }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// A side call on this verified <c>ws</c> link (13.7): its S in <c>Authorization: ShowBus</c>, only while the core holds the link
    /// verified and only over TLS to the certificate the link pinned; its answer read by 13.12.3 and handed to the core. The status, or
    /// null when no call went out or no answer came.
    /// </summary>
    public async Task<int?> SideCall(long t, string path)
    {
        if (T is not { } tt || Shown is not { } pinned || M.Record(t).Proof != "mutual") return null;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && Kit.Fp(cert) == pinned },
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{Port}{path}");
        request.Headers.TryAddWithoutValidation("Authorization", $"ShowBus lid={PeerLink.Lid(tt)}, s={PeerLink.Session(_k, tt)}");
        Credentials.Add("session");
        try
        {
            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            M.SideCall(t, PeerLink.ReadHttp((int)response.StatusCode, body));
            return (int)response.StatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            M.SideCall(t, PeerLink.ReadHttp(null, null));
            return null;
        }
    }

    async Task Drop()
    {
        if (_io is { } io)
        {
            _io = null;
            await io.DisposeAsync();
        }
    }

    public ValueTask DisposeAsync() => new(Drop());
}

/// <summary>
/// The kit's stand-in acceptor, on <c>/ws</c> with its side calls or on a <c>line</c>, where Stagewatch's /ws acceptor will stand once
/// it speaks LINK (SW22.1a–c). What it reads goes through the core's parsers (PeerLink.ParseLinkMessage, ParseLinkLine), every proof,
/// lid and S it checks or makes is the core's, made with the K it holds for the kid, a side call's session is the core's CheckSession
/// (an S in clear from another machine included), and the commands it runs go through the core's receipt store, PeerReceipts — so
/// what runs against it is the scripted peer's connector end against the core's parsers, proofs, session check and store. Its
/// decisions are the kit's, not Stagewatch's: the K it takes for a kid it does not know, a fresh snonce for each connection, the
/// verdict and its words, one proof a connection, a refused socket kept as a public viewer, what it answers a command (applied, or
/// refused for the verb <c>REFUSE</c>) and how it carries a receipt on each carrier. It answers no ping with a pong and keeps no
/// wrong-proof table, no unpaired bound and no reply bound: those are the /ws acceptor's (SW22.1a; the reply bound SW22.1c).
/// </summary>
sealed class CoreAcceptor : IAsyncDisposable
{
    public const string Id = "a1b2c3d4e5f6";
    public const string Inst = "5a6b7c8d";

    readonly TcpListener _listener;
    readonly KitCert? _cert;
    readonly PeerCarrier _carrier;
    readonly Dictionary<string, (byte[] K, string Kind)> _keys;
    readonly CancellationTokenSource _stop = new();
    readonly Task _accepting;
    readonly Lock _gate = new();
    readonly Dictionary<string, string> _sessions = new(StringComparer.Ordinal);
    readonly Dictionary<string, Action> _enders = new(StringComparer.Ordinal);
    readonly PeerReceipts _receipts = new();

    public int Port { get; }
    /// <summary>
    /// What it decided, in order: <c>link &lt;lid&gt;</c>, <c>refused &lt;words&gt;</c>, <c>served &lt;status&gt; &lt;authorization&gt;</c>,
    /// <c>ended &lt;lid&gt;</c>, <c>ran &lt;id&gt;</c>, <c>repeat &lt;id&gt;</c>.
    /// </summary>
    public List<string> Log { get; } = [];

    /// <summary>The stand-in, listening on loopback or on the address <paramref name="on"/> names.</summary>
    public CoreAcceptor(PeerCarrier carrier, KitCert? cert, IEnumerable<(byte[] K, string Kid, string Kind)> keys, IPAddress? on = null)
    {
        _carrier = carrier;
        _cert = cert;
        _keys = keys.ToDictionary(k => k.Kid, k => (k.K, k.Kind), StringComparer.Ordinal);
        _listener = new TcpListener(on ?? IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(Accept);
    }

    public List<string> Entries
    {
        get { lock (_gate) return Log.ToList(); }
    }

    readonly HashSet<string> _made = new(StringComparer.Ordinal);

    /// <summary>Every session credential S this stand-in made for a link, kept once the link ends: a canary for the peer's stdout (13.20).</summary>
    public List<string> Sessions
    {
        get { lock (_gate) return [.. _made]; }
    }

    void Note(string entry)
    {
        lock (_gate) Log.Add(entry);
    }

    async Task Accept()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
            _ = Task.Run(() => Serve(tcp));
        }
    }

    /// <summary>
    /// What the stand-in holds for a kid: its K, or for a kid it does not know a fresh K, so an unknown kid is treated exactly like a
    /// known one until the proof (13.6.3).
    /// </summary>
    (byte[] K, string Kind, bool Known) KeyOf(string kid)
        => _keys.TryGetValue(kid, out var k) ? (k.K, k.Kind, true) : (RandomNumberGenerator.GetBytes(32), kid == "code" ? "code" : "key", false);

    /// <summary>What the stand-in answers a command whose verb is <c>REFUSE</c>: it refuses it, and its record keeps the refusal.</summary>
    public const string RefuseWords = "the stand-in refuses it";

    /// <summary>
    /// A command from a verified connector (<c>t:id</c>), through the core's store (13.12.6): run once — applied, or refused when its
    /// verb is <c>REFUSE</c> — and its receipt's fields kept, or, for an id it holds, answered from the record with
    /// <c>"repeat":true</c> and not run again. The receipt's fields, which <see cref="OnLine"/> and <see cref="OnWs"/> carry.
    /// </summary>
    string Run(string connector, string id, string verb)
    {
        lock (_gate)
        {
            if (_receipts.Begin(connector, id, 0) is { } repeat)
            {
                Log.Add("repeat " + id);
                return repeat;
            }
            Log.Add("ran " + id);
            var receipt = verb.Equals("REFUSE", StringComparison.OrdinalIgnoreCase)
                ? "{\"ok\":false,\"error\":\"" + RefuseWords + "\"}"
                : "{\"ok\":true,\"stage\":\"applied\",\"effect\":\"\"}";
            _receipts.Keep(connector, id, receipt, 0);
            return receipt;
        }
    }

    /// <summary>
    /// A receipt's fields on <c>line</c> (13.12.3, 13.12.6): a repeat as <c>OK @id {…,"repeat":true}</c> whatever it says, a refusal as
    /// <c>ERR @id &lt;error&gt;</c>, an applied one with no effect as <c>OK @id</c>, anything else as <c>OK @id {…}</c>.
    /// </summary>
    public static string OnLine(string id, string fields)
    {
        using var doc = JsonDocument.Parse(fields);
        var o = doc.RootElement;
        if (o.TryGetProperty("repeat", out var r) && r.ValueKind == JsonValueKind.True) return "OK @" + id + " " + fields;
        if (o.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False) return "ERR @" + id + " " + Text(o, "error");
        return fields == "{\"ok\":true,\"stage\":\"applied\",\"effect\":\"\"}" ? "OK @" + id : "OK @" + id + " " + fields;
    }

    /// <summary>A receipt's fields on <c>ws</c>: <c>{"type":"receipt","id":…}</c> and then the fields.</summary>
    public static string OnWs(string id, string fields)
        => "{\"type\":\"receipt\",\"id\":\"" + JsonEncodedText.Encode(id) + "\"" + (fields.Length > 2 ? "," + fields[1..] : "}");

    /// <summary>13.12.6: the core's RECEIPTS answer for the connector's own ids, with this stand-in's inst.</summary>
    string Answer(string connector, IEnumerable<string> ids)
    {
        lock (_gate) return _receipts.Answer(connector, ids, Inst, 0);
    }

    async Task Serve(TcpClient tcp)
    {
        using var _ = tcp;
        try
        {
            Stream stream = tcp.GetStream();
            var first = new byte[1];
            using var cts = new CancellationTokenSource(5000);
            if (await tcp.Client.ReceiveAsync(first, SocketFlags.Peek, cts.Token) == 0) return;
            var tls = first[0] == 0x16 && _cert is not null;
            if (tls)
            {
                var ssl = new SslStream(stream, false);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _cert!.Server }, cts.Token);
                stream = ssl;
            }
            var bind = tls ? "tls:sha256:" + _cert!.Fp : "plain";
            // 13.1's local is the socket's remote address; a call with neither TLS nor that is in clear from another machine (13.7.1).
            var clear = !tls && tcp.Client.RemoteEndPoint is IPEndPoint remote && !IPAddress.IsLoopback(remote.Address);
            if (_carrier == PeerCarrier.Line) await ServeLine(stream, bind);
            else await ServeHttp(stream, bind, clear);
        }
        catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException or SocketException or WebSocketException or ObjectDisposedException)
        {
            // A connection that ends mid-way ends here; what the acceptor decided before is in its log.
        }
    }

    async Task ServeLine(Stream stream, string bind)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        await writer.WriteLineAsync("NONCE " + Kit.Nonce());
        PeerLink.LinkAsk? ask = null;
        string? snonce = null;
        bool proved = false, paired = false;
        while (await reader.ReadLineAsync() is { } line)
        {
            var at = Regex.Match(line, "^@([A-Za-z0-9_-]{1,32}) ");
            var id = at.Success ? at.Groups[1].Value : null;
            var body = at.Success ? line[at.Length..] : line;
            var verb = body.Split(' ')[0];
            string Reply(bool ok, string text) => (ok ? "OK" : "ERR") + (id is null ? "" : " @" + id) + (text.Length > 0 ? " " + text : "");
            if (verb == "HELLO")
            {
                await writer.WriteLineAsync(Reply(true, ""));
                continue;
            }
            if (verb == "LINK")
            {
                var p = PeerLink.ParseLinkLine(line);
                if (p.Ask is { } a && ask is null && !proved)
                {
                    ask = a;
                    snonce = Kit.Nonce();
                    await writer.WriteLineAsync(Reply(true, $"LINK {PeerLink.Speaks(a.Offer)} main {Id} {snonce} {bind}"));
                    continue;
                }
                if ((p.Proof is not null || p.Refused == "refused") && ask is not null && !proved)
                {
                    proved = true;
                    var (k, kind, known) = KeyOf(ask.Kid);
                    var t = PeerLink.Transcript("line", ask.Offer, PeerLink.Speaks(ask.Offer)!.Value, "main:" + Id, ask.T + ":" + ask.Id, ask.Kid, snonce!, ask.CNonce, bind)!;
                    if (known && PeerLink.Verify(PeerLink.ClientProof(k, t), p.Proof))
                    {
                        paired = true;
                        var lid = PeerLink.Lid(t);
                        Note("link " + lid);
                        await writer.WriteLineAsync(Reply(true, "paired " + JsonSerializer.Serialize(new
                        {
                            v = 1, verified = kind, scope = kind == "code" ? "operate" : "post", proof = PeerLink.ServerProof(k, t), lid, level = 1,
                            can = new[] { "receipts" }, inst = Inst, beatMs = 2000,
                        })));
                        await writer.WriteLineAsync("STATE {\"rev\":1}");
                        continue;
                    }
                    Note("refused " + PeerLink.ProofWords);
                    await writer.WriteLineAsync(Reply(false, "LINK refused — " + PeerLink.ProofWords));
                    return;
                }
                await writer.WriteLineAsync(Reply(false, "LINK " + (p.Refused is "version" ? "version" : "malformed") + " — a LINK this node cannot take"));
                continue;
            }
            if (!paired || ask is null)
            {
                await writer.WriteLineAsync(Reply(false, "not paired — this node shows its state only to a paired connection"));
                continue;
            }
            var connector = ask.T + ":" + ask.Id;
            if (verb == "RECEIPTS") await writer.WriteLineAsync(Reply(true, Answer(connector, body.Split(' ').Skip(1).Where(x => x.Length > 0))));
            else if (id is null) await writer.WriteLineAsync(Reply(true, ""));
            else await writer.WriteLineAsync(OnLine(id, Run(connector, id, verb)));
        }
    }

    async Task ServeHttp(Stream stream, string bind, bool clear)
    {
        if (await Kit.Head(stream) is not { } head) return;
        var parts = head.Start.Split(' ');
        var target = parts.Length > 1 ? parts[1] : "/";
        var path = target.Split('?')[0];
        var query = target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "";
        if (head.Headers.TryGetValue("Upgrade", out var up) && up.Equals("websocket", StringComparison.OrdinalIgnoreCase) && path == "/ws"
            && head.Headers.TryGetValue("Sec-WebSocket-Key", out var key))
        {
            var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
            using var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.Zero });
            await ServeWs(ws, query, bind);
            return;
        }
        // A side call (13.7): the session of a live, verified link its lid names, checked by the core, which ends a link whose S came
        // in clear from another machine.
        head.Headers.TryGetValue("Authorization", out var authorization);
        string? Of(string lid)
        {
            lock (_gate) return _sessions.TryGetValue(lid, out var s) ? s : null;
        }
        var check = PeerLink.CheckSession(authorization, Of, clear);
        if (check.Ends is { } gone)
        {
            Action? end;
            lock (_gate)
            {
                _sessions.Remove(gone);
                _enders.Remove(gone, out end);
            }
            Note("ended " + gone);
            end?.Invoke();
        }
        var body = check.Status == 200 ? "{\"ok\":true}" : JsonSerializer.Serialize(new { ok = false, error = check.Words, relink = check.Relink });
        Note("served " + check.Status.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (authorization is null ? "none" : authorization.Split(' ')[0]));
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"HTTP/1.1 {check.Status} {(check.Status == 200 ? "OK" : "Unauthorized")}\r\nContent-Type: application/json\r\n"
            + $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}"));
    }

    async Task ServeWs(WebSocket ws, string query, string bind)
    {
        Task Send(string text) => ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
        var io = new WsIo(ws);
        string? lid = null;
        try
        {
            if (!query.Split('&').Any(p => p.StartsWith("link=", StringComparison.Ordinal)))
            {
                await Send("{\"type\":\"state\",\"v\":1,\"seq\":1,\"public\":true}");
                while ((await io.Next(10000)) is (true, not null)) { }
                return;
            }
            var snonce = Kit.Nonce();
            await Send(JsonSerializer.Serialize(new { type = "link", v = 1, speaks = PeerLink.Version, server = new { t = "main", id = Id }, snonce, bind }));
            var (got, text) = await io.Next(3000);
            if (!got || text is null) return;
            JsonElement message;
            try { message = JsonDocument.Parse(text).RootElement.Clone(); }
            catch (JsonException) { message = default; }
            var p = message.ValueKind == JsonValueKind.Object ? PeerLink.ParseLinkMessage(query, message) : PeerLink.LinkParse.Malformed;
            string? connector = null;
            if (p.Ask is { } ask && p.Proof is { } proof)
            {
                var (k, kind, known) = KeyOf(ask.Kid);
                var t = PeerLink.Transcript("ws", ask.Offer, PeerLink.Speaks(ask.Offer)!.Value, "main:" + Id, ask.T + ":" + ask.Id, ask.Kid, snonce, ask.CNonce, bind)!;
                if (known && PeerLink.Verify(PeerLink.ClientProof(k, t), proof))
                {
                    connector = ask.T + ":" + ask.Id;
                    lid = PeerLink.Lid(t);
                    lock (_gate)
                    {
                        _sessions[lid] = PeerLink.Session(k, t);
                        _made.Add(_sessions[lid]);
                        _enders[lid] = ws.Abort;
                    }
                    Note("link " + lid);
                    await Send(JsonSerializer.Serialize(new
                    {
                        type = "auth", ok = true, scope = kind == "code" ? "operate" : "post", node = new { t = ask.T, id = ask.Id, verified = kind },
                        proof = PeerLink.ServerProof(k, t), lid, level = 1, can = new[] { "receipts" }, inst = Inst, beatMs = 2000,
                    }));
                    await Send("{\"type\":\"state\",\"v\":1,\"seq\":1,\"public\":false}");
                }
            }
            if (connector is null)
            {
                var cls = p.Refused is "malformed" or "version" ? p.Refused : "refused";
                var words = cls == "refused" ? PeerLink.ProofWords : "a link message this node cannot take";
                Note("refused " + words);
                await Send(JsonSerializer.Serialize(new { type = "auth", ok = false, error = words, @class = cls, retry = 60 }));
                await Send("{\"type\":\"state\",\"v\":1,\"seq\":1,\"public\":true}");
            }
            while ((await io.Next(10000)) is (true, { } read))
            {
                if (connector is null) continue;
                JsonElement m;
                try { m = JsonDocument.Parse(read).RootElement.Clone(); }
                catch (JsonException) { continue; }
                if (Text(m, "cmd") is not { } cmd || Text(m, "id") is not { } id) continue;
                if (cmd == "receipts")
                {
                    var ids = m.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array
                        ? args.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!) : [];
                    await Send(OnWs(id, "{\"ok\":true," + Answer(connector, ids)[1..]));
                }
                else await Send(OnWs(id, Run(connector, id, cmd)));
            }
        }
        finally
        {
            if (lid is not null)
                lock (_gate)
                {
                    _sessions.Remove(lid);
                    _enders.Remove(lid);
                }
        }
    }

    static string? Text(JsonElement o, string name)
        => o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }
}

/// <summary>
/// Another machine between a connector and an acceptor at first use (13.8.2): it ends the connector's TLS with a certificate of its own
/// and dials the acceptor's TLS, passing bytes both ways. Given <c>from</c> and <c>to</c> (as long as each other), it rewrites the
/// acceptor's bytes on their way back — the acceptor's certificate in its answer to LINK made to name its own.
/// </summary>
sealed class TlsRelay : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly KitCert _mine;
    readonly int _target;
    readonly byte[]? _from, _to;
    readonly CancellationTokenSource _stop = new();
    readonly Task _accepting;

    public int Port { get; }

    public TlsRelay(KitCert mine, int target, string? from = null, string? to = null)
    {
        if ((from is null) != (to is null) || (from is not null && from.Length != to!.Length)) throw new ArgumentException("a rewrite keeps its length");
        _mine = mine;
        _target = target;
        _from = from is null ? null : Encoding.ASCII.GetBytes(from);
        _to = to is null ? null : Encoding.ASCII.GetBytes(to);
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accepting = Task.Run(Accept);
    }

    async Task Accept()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
            _ = Task.Run(() => Relay(client));
        }
    }

    async Task Relay(TcpClient client)
    {
        using var _ = client;
        try
        {
            await using var near = new SslStream(client.GetStream(), false);
            using var cts = new CancellationTokenSource(5000);
            await near.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = _mine.Server }, cts.Token);
            using var upstream = await Kit.Connect(_target);
            if (upstream is null) return;
            await using var far = new SslStream(upstream.GetStream(), false, (_, _, _, _) => true);
            await far.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "acceptor" }, cts.Token);
            var up = Pump(near, far, rewrite: false);
            var down = Pump(far, near, rewrite: true);
            await Task.WhenAny(up, down);
        }
        catch (Exception e) when (e is IOException or AuthenticationException or OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Either side ended: the relay ends with it.
        }
    }

    async Task Pump(Stream from, Stream to, bool rewrite)
    {
        var buffer = new byte[16384];
        var held = Array.Empty<byte>();
        try
        {
            int n;
            while ((n = await from.ReadAsync(buffer)) > 0)
            {
                if (!rewrite || _from is null)
                {
                    await to.WriteAsync(buffer.AsMemory(0, n));
                    continue;
                }
                var bytes = held.Concat(buffer.Take(n)).ToArray();
                for (var i = 0; i + _from.Length <= bytes.Length; i++)
                    if (bytes.AsSpan(i, _from.Length).SequenceEqual(_from)) _to!.CopyTo(bytes, i);
                // Hold back only a tail that could still start a match the next read completes; send the rest now.
                var keep = 0;
                for (var k = Math.Min(bytes.Length, _from.Length - 1); k > 0; k--)
                    if (bytes.AsSpan(bytes.Length - k).SequenceEqual(_from.AsSpan(0, k)))
                    {
                        keep = k;
                        break;
                    }
                await to.WriteAsync(bytes.AsMemory(0, bytes.Length - keep));
                held = bytes[(bytes.Length - keep)..];
            }
            if (held.Length > 0) await to.WriteAsync(held);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // Either side ended.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accepting.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }
}

/// <summary>
/// One connection, on loopback or the address named, that the suite writes raw bytes to as a peer's other end would: <c>serve</c> runs
/// on it once it comes, and the connection is held open until this is disposed, so what the scripted peer reads is only what
/// <c>serve</c> wrote. <see cref="Received"/> counts what the peer sent it.
/// </summary>
sealed class RawServer : IAsyncDisposable
{
    readonly TcpListener _listener;
    readonly CancellationTokenSource _stop = new();
    readonly Task _serving;

    long _received;

    public int Port { get; }

    /// <summary>The bytes the other end sent, once <c>serve</c> has finished and while the connection is held.</summary>
    public long Received => Interlocked.Read(ref _received);

    public RawServer(Func<Stream, CancellationToken, Task> serve, IPAddress? on = null)
    {
        _listener = new TcpListener(on ?? IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serving = Task.Run(async () =>
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                var stream = client.GetStream();
                await serve(stream, _stop.Token);
                var buffer = new byte[4096];
                int n;
                while ((n = await stream.ReadAsync(buffer, _stop.Token)) > 0) Interlocked.Add(ref _received, n);
                await Task.Delay(Timeout.Infinite, _stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // Disposed, or the peer ended the connection: the server ends with it.
            }
        });
    }

    /// <summary>A WebSocket upgrade answered on <paramref name="stream"/> as a server answers it; false when no upgrade came.</summary>
    public static async Task<bool> Upgrade(Stream stream, CancellationToken ct)
    {
        if (await Kit.Head(stream) is not { } head || !head.Headers.TryGetValue("Sec-WebSocket-Key", out var key)) return false;
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), ct);
        return true;
    }

    /// <summary>A server's unmasked text frame: its declared length is <paramref name="declared"/> (the payload's own when null).</summary>
    public static byte[] TextFrame(ReadOnlySpan<byte> payload, long? declared = null)
    {
        var length = declared ?? payload.Length;
        var head = new List<byte> { 0x81 };
        if (length < 126) head.Add((byte)length);
        else if (length < 65536) head.AddRange([126, (byte)(length >> 8), (byte)length]);
        else
        {
            head.Add(127);
            for (var shift = 56; shift >= 0; shift -= 8) head.Add((byte)(length >> shift));
        }
        return [.. head, .. payload];
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _serving.WaitAsync(TimeSpan.FromSeconds(2)).ContinueWith(_ => { }, TaskScheduler.Default);
        _stop.Dispose();
    }
}
