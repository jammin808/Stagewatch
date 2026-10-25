using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// The clip Patterns has on air, as its STATE frame reports it. Newer desks (Patterns Round 86+) send milliseconds,
/// <c>paused</c>, <c>rate</c> and a <c>seq</c> that changes only on jumps; older ones whole seconds. Length 0 = not known yet.
/// <paramref name="Transport"/>: the desk understands VIDEO PLAY / PAUSE / STOP / SEEK.
/// </summary>
public sealed record PatternsVideo(string File, string Tag, double LengthSeconds, double PositionSeconds, bool Playing, bool Ended, bool Loops,
    bool Paused = false, double Rate = 1, long Seq = -1, bool Transport = false)
{
    /// <summary>A countdown only makes sense for a clip with a known length that doesn't loop.</summary>
    public bool CountsDown => LengthSeconds > 0 && !Loops;
    public string Title => Tag.Length > 0 && !File.StartsWith(Tag, StringComparison.OrdinalIgnoreCase) ? $"{Tag} {File}".Trim() : File;
}

/// <summary>A Patterns machine heard on its UDP beacon (port 9700) or the show network's hello (9928).</summary>
public sealed record PatternsMachine(string Name, string Address, int WirePort, string Kind);

/// <summary>
/// Patterns (the user's show-control desk) as a playout source, over its TCP line protocol (port 9697):
/// <c>HELLO Stagewatch</c>, <c>AUTH &lt;token&gt;</c>, then Patterns pushes <c>STATE &lt;json&gt;</c> on every change —
/// latest-wins, so this reader only ever keeps the newest. The clip's position counts on locally between frames from
/// a monotonic clock; a playing clip with no fresh frame for <see cref="StaleAfter"/> is reported stale (no countdown)
/// rather than trusted. Commands are a fixed whitelist (<c>VIDEO END n</c>, <c>VIDEO RESTART</c>, and on desks that
/// support them <c>VIDEO PLAY</c>, <c>VIDEO PAUSE</c>, <c>VIDEO STOP</c>, <c>VIDEO SEEK n</c>) — never a raw line, and
/// never a bare <c>STOP</c>, which on Patterns closes every output. Nothing is queued while disconnected.
/// </summary>
public sealed class PatternsLink : IPlayoutSource
{
    public const int DefaultPort = 9697;
    public const int BeaconPort = 9700;
    public const int HelloPort = 9928;
    public const int MaxLine = 256 * 1024;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);
    // Show Bus 1.1 liveness: a ping every 2 s; a link silent for 12 s is dropped and redialled.
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DeadAfter = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ContextEvery = TimeSpan.FromSeconds(5);
    /// <summary>How long to wait for a desk's NONCE before pairing the old way (desks older than Show Bus 1.1).</summary>
    private static readonly TimeSpan NonceWait = TimeSpan.FromMilliseconds(1500);

    private readonly string _host;
    private readonly int _port;
    private readonly string _token;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeLock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private sealed record Reading(PatternsVideo? Video, long AtMs);
    private readonly Latest<Reading> _latest = new();
    private NetworkStream? _stream;
    private volatile bool _connected;
    private volatile LinkHealth _health = LinkHealth.Starting;
    private volatile string _status = "connecting";
    private long _lastLineMs;
    private int _malformed;
    private int _nextId;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Action<bool, string>> _pending = new();
    private readonly ClockOffsetEstimator _offset = new();
    private volatile string? _context;
    private volatile bool _proofSent;
    /// <summary>The desk speaks Show Bus 1.1 (it sent NONCE): request ids, timed pings and CONTEXT. Older desks refuse
    /// words they don't know, so they get plain lines.</summary>
    private volatile bool _bus11;
    /// <summary>The "@hi PING" probe is out: a 1.1 desk answers "OK @hi PONG …", an older one an ERR we swallow.</summary>
    private volatile bool _probing;

    public PatternsLink(string host, int port, string token)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port is > 0 and < 65536 ? port : DefaultPort;
        _token = (token ?? "").Trim();
        _thread = new Thread(Run) { IsBackground = true, Name = "Stagewatch Patterns", Priority = ThreadPriority.BelowNormal };
    }

    public string Name => "Patterns";
    public LinkHealth Health => _health;
    public string Status => _status;
    public bool Connected => _connected;
    /// <summary>STATE frames that couldn't be read (ignored, never fatal).</summary>
    public int Malformed => Volatile.Read(ref _malformed);

    public void Start() => _thread.Start();

    /// <summary>The desk's clock minus ours (Show Bus §5), or null before a good sample.</summary>
    public TimeSpan? ClockOffset => _offset.Offset;

    /// <summary>Patterns' context (Show Bus §6 JSON: the Eye's headline, lights, problems…), refreshed every 5 s, or null.</summary>
    public string? Context => _connected ? _context : null;

    /// <summary>A command's receipt arrived: (ok, words). Raised on the link's thread.</summary>
    public event Action<string, bool, string>? Receipt;

    /// <summary>The clip on air with its position counted on to now, or null (none, or stale).</summary>
    public PatternsVideo? Video
    {
        get
        {
            var r = _latest.Value;
            if (r?.Video is not { } v || !_connected) return null;
            var age = (_clock.ElapsedMilliseconds - r.AtMs) / 1000.0;
            if (v.Playing && age > StaleAfter.TotalSeconds) return null;   // fail safe: an old countdown is worse than none
            if (!v.Playing || v.Paused || !v.CountsDown) return v;
            return v with { PositionSeconds = Math.Min(v.LengthSeconds, v.PositionSeconds + age * v.Rate) };
        }
    }

    /// <summary>
    /// For the VT countdown: only a clip with a known length that doesn't loop. At a playback rate other than 1 the
    /// clip's time is scaled to real time, so the countdown says how long it will really take.
    /// </summary>
    public PlayoutState? Value
        => Video is { CountsDown: true } v
            ? new PlayoutState("Patterns", v.Title, v.LengthSeconds / v.Rate, v.PositionSeconds / v.Rate, v.Playing && !v.Paused)
            : null;

    /// <summary>Jump the clip on air to its last <paramref name="seconds"/>. Null = sent, else why not.</summary>
    public string? EndIn(int seconds)
    {
        if (seconds is < 1 or > 3600) return "seconds must be 1-3600";
        if (Video is { } v && !v.CountsDown) return v.Loops ? "the clip on air loops — it has no end to jump to" : "Patterns doesn't know this clip's length yet";
        return Send($"VIDEO END {seconds}");
    }

    /// <summary>Play the clip on air again from its start. Null = sent, else why not.</summary>
    public string? Restart() => Send("VIDEO RESTART");

    /// <summary>Play on / pause / stop (back to the top, held) the clip on air — desks with transport only.</summary>
    public string? Play() => Transport("VIDEO PLAY");
    public string? Pause() => Transport("VIDEO PAUSE");
    public string? StopClip() => Transport("VIDEO STOP");   // the clip only — never the bare STOP that closes outputs

    /// <summary>Move the clip on air to <paramref name="seconds"/> from its start.</summary>
    public string? Seek(int seconds)
    {
        if (seconds is < 0 or > 24 * 3600) return "seek takes 0 to 24 hours";
        if (Video is { LengthSeconds: > 0 } v && seconds > v.LengthSeconds) return $"the clip is only {Stagewatch.Core.TimerTick.FormatSeconds((long)v.LengthSeconds)} long";
        return Transport(FormattableString.Invariant($"VIDEO SEEK {seconds}"));
    }

    private string? Transport(string line)
    {
        if (_latest.Value?.Video is { Transport: false })
            return "this Patterns desk is older than Round 86 — it only takes Last 10 s and restart";
        if (_latest.Value?.Video is null && _connected) return "Patterns has no clip on air";
        return Send(line);
    }

    // Only lines built in this class reach the wire. Each carries a request id (Show Bus §3): the desk's
    // "OK @id" / "ERR @id why" is matched back to it and raised as Receipt, so "sent" is never mistaken for "done".
    private string? Send(string line)
    {
        if (line.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)) return "refused";   // never: it closes every Patterns output
        var s = _stream;
        if (!_connected || s is null) return "Patterns isn't connected — nothing sent";
        var wire = line;
        if (_bus11)
        {
            var id = "s" + Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _pending[id] = (ok, words) => Receipt?.Invoke(line, ok, words);
            if (_pending.Count > 64) foreach (var old in _pending.Keys.Take(_pending.Count - 64)) _pending.TryRemove(old, out _);
            wire = "@" + id + " " + line;
        }
        try
        {
            var bytes = Encoding.UTF8.GetBytes(wire + "\n");
            lock (_writeLock) s.Write(bytes, 0, bytes.Length);
            return null;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            return "Patterns connection lost — nothing sent";
        }
    }

    private void Report(LinkHealth h, string status) { _health = h; _status = status; }

    private void Run()
    {
        var ct = _cts.Token;
        var backoff = 250;
        while (!ct.IsCancellationRequested)
        {
            using var tcp = new TcpClient { NoDelay = true };
            try
            {
                var connect = tcp.ConnectAsync(_host, _port, ct).AsTask();
                if (!connect.Wait(2000, ct) || !tcp.Connected) throw new SocketException((int)SocketError.TimedOut);
                var stream = tcp.GetStream();
                _stream = stream;
                Write(stream, "HELLO Stagewatch");
                // Pairing (Show Bus §7): the desk sends NONCE on connect and we prove we hold the token without sending
                // it. A desk that never sends one is older: it gets the plain AUTH it expects (after a short wait).
                _proofSent = _token.Length == 0;
                _bus11 = false;
                // A desk on this PC trusts loopback and sends no NONCE: ask it whether it speaks Show Bus 1.1.
                _probing = true;
                Write(stream, "@hi PING " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                Interlocked.Exchange(ref _lastLineMs, _clock.ElapsedMilliseconds);
                _connected = true;
                backoff = 250;
                Report(LinkHealth.Ok, $"connected to {_host}:{_port}");
                ReadLoop(stream, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is SocketException or IOException or AggregateException or ObjectDisposedException or InvalidOperationException)
            {
                Report(LinkHealth.NotFound, $"Patterns not reachable at {_host}:{_port} — retrying");
            }
            finally
            {
                _connected = false;
                _stream = null;
                _latest.Set(null);
                _context = null;
                // Nothing waits for an answer from a dead link: every pending command is told it's lost.
                foreach (var id in _pending.Keys.ToList())
                    if (_pending.TryRemove(id, out var lost)) lost(false, "Patterns connection lost before it answered");
            }
            if (ct.IsCancellationRequested) break;
            ct.WaitHandle.WaitOne(backoff);
            backoff = Math.Min(5000, backoff * 2);
        }
        Report(LinkHealth.Off, "off");
    }

    private void Write(NetworkStream s, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_writeLock) s.Write(bytes, 0, bytes.Length);
    }

    private void ReadLoop(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        var line = new MemoryStream();
        var discarding = false;
        var lastPing = _clock.ElapsedMilliseconds;
        var connectedAt = lastPing;
        var lastContext = long.MinValue / 2;
        stream.ReadTimeout = 500;
        while (!ct.IsCancellationRequested)
        {
            int n;
            try { n = stream.Read(buf, 0, buf.Length); }
            catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
            {
                n = -1;
            }
            if (n == 0) { Report(LinkHealth.NotFound, "Patterns closed the connection — reconnecting"); return; }
            var now = _clock.ElapsedMilliseconds;
            if (n > 0)
            {
                Interlocked.Exchange(ref _lastLineMs, now);
                for (var i = 0; i < n; i++)
                {
                    var b = buf[i];
                    if (b == (byte)'\n')
                    {
                        if (!discarding) Handle(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r'));
                        line.SetLength(0);
                        discarding = false;
                    }
                    else if (!discarding)
                    {
                        if (line.Length >= MaxLine) { discarding = true; line.SetLength(0); Interlocked.Increment(ref _malformed); continue; }
                        line.WriteByte(b);
                    }
                }
            }
            if (_probing && now - connectedAt > 3000) _probing = false;   // no answer to the probe: treat as an older desk
            if (!_proofSent && now - connectedAt > NonceWait.TotalMilliseconds)
            {
                _proofSent = true;
                Write(stream, "AUTH " + _token);   // a desk from before Show Bus 1.1: no NONCE, so the old way
            }
            // A link that says nothing at all is checked, then dropped: no silent half-open sockets. The ping carries
            // our time, so the desk's reply gives the clock offset (Show Bus §5).
            if (now - lastPing > PingEvery.TotalMilliseconds)
            {
                Write(stream, _bus11 ? "PING " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) : "PING");
                lastPing = now;
            }
            if (_bus11 && _proofSent && now - lastContext > ContextEvery.TotalMilliseconds)
            {
                Write(stream, "@ctx CONTEXT");
                lastContext = now;
            }
            if (now - Interlocked.Read(ref _lastLineMs) > DeadAfter.TotalMilliseconds)
            {
                Report(LinkHealth.Error, "Patterns stopped answering — reconnecting");
                return;
            }
        }
    }

    private void Handle(string line)
    {
        if (line.StartsWith("NONCE ", StringComparison.Ordinal))
        {
            var nonce = line[6..].Trim();
            _bus11 = true;
            if (!_proofSent && _token.Length > 0 && nonce.Length is > 0 and <= 128 && _stream is { } st)
            {
                _proofSent = true;
                Write(st, "AUTH-PROOF " + Proof(_token, nonce, "Stagewatch"));
            }
            return;
        }
        if (line.StartsWith("OK PONG ", StringComparison.Ordinal))
        {
            var p = line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (p.Length >= 3 && long.TryParse(p[0], System.Globalization.NumberStyles.Integer, inv, out var t1)
                && long.TryParse(p[1], System.Globalization.NumberStyles.Integer, inv, out var t2)
                && long.TryParse(p[2], System.Globalization.NumberStyles.Integer, inv, out var t3))
                _offset.Add(t1, t2, t3, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return;
        }
        if (line.StartsWith("OK @hi PONG ", StringComparison.Ordinal))
        {
            _probing = false;
            _bus11 = true;
            Handle("OK PONG " + line[12..]);
            return;
        }
        if (_probing && line.StartsWith("ERR", StringComparison.Ordinal) && !line.StartsWith("ERR @", StringComparison.Ordinal))
        {
            _probing = false;   // an older desk doesn't know "@hi": plain lines from here on
            return;
        }
        // Receipts: "OK @id …" / "ERR @id …".
        if ((line.StartsWith("OK @", StringComparison.Ordinal) || line.StartsWith("ERR @", StringComparison.Ordinal)))
        {
            var ok = line[0] == 'O';
            var rest = line[(ok ? 4 : 5)..];
            var sp = rest.IndexOf(' ');
            var id = sp < 0 ? rest : rest[..sp];
            var words = sp < 0 ? "" : rest[(sp + 1)..];
            if (id == "ctx")
            {
                if (ok && words.StartsWith('{') && words.Length < 256 * 1024) _context = words;
                return;
            }
            if (_pending.TryRemove(id, out var done)) done(ok, words.Length > 200 ? words[..200] : words);
            if (ok) return;
            line = "ERR " + words;
        }
        if (line.StartsWith("STATE ", StringComparison.Ordinal))
        {
            if (TryParseState(line[6..], out var video))
            {
                _latest.Set(new Reading(video, _clock.ElapsedMilliseconds));
                Report(LinkHealth.Ok, video is null ? $"connected to {_host}:{_port} — no clip on air"
                    : $"{video.Title}" + (video.CountsDown ? $": {TimeSpan.FromSeconds(Math.Max(0, video.LengthSeconds - video.PositionSeconds)):mm\\:ss} left" : video.Loops ? " (looping)" : ""));
            }
            else Interlocked.Increment(ref _malformed);
        }
        else if (line.StartsWith("ERR", StringComparison.Ordinal))
        {
            var why = line.Length > 4 ? line[4..] : "refused";
            Report(why.Contains("not paired", StringComparison.OrdinalIgnoreCase) || why.Contains("token", StringComparison.OrdinalIgnoreCase)
                ? LinkHealth.Error : _health, "Patterns: " + (why.Length > 160 ? why[..160] : why));
        }
    }

    /// <summary>
    /// Pure: the pairing proof — HMAC-SHA256(key, nonce + client name), lowercase hex (Show Bus §7). The key is the token as
    /// Patterns canonicalises it: upper case, without dashes or spaces (so "abcd-efgh" typed either way pairs).
    /// </summary>
    public static string Proof(string token, string nonce, string clientName)
    {
        var key = new string(token.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        using var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes(nonce + clientName))).ToLowerInvariant();
    }

    /// <summary>Pure: a STATE frame's JSON → the clip on air (null when none). False when the frame can't be read.</summary>
    public static bool TryParseState(string json, out PatternsVideo? video)
    {
        video = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty("video", out var v) || v.ValueKind != JsonValueKind.Object) return true;
            string S(string n) => v.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "").Trim() : "";
            double D(string n) => v.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Max(0, d) : 0;
            bool B(string n) => v.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;
            var file = S("file");
            // Round 86+: milliseconds (null length = not known yet), paused, rate, seq. Older desks: whole seconds.
            var precise = v.TryGetProperty("positionMs", out var pm) && pm.ValueKind == JsonValueKind.Number;
            var length = D("length");
            var position = D("position");
            if (precise)
            {
                position = pm.TryGetDouble(out var p0) && double.IsFinite(p0) ? Math.Max(0, p0 / 1000) : position;
                length = v.TryGetProperty("lengthMs", out var lm) && lm.ValueKind == JsonValueKind.Number && lm.TryGetDouble(out var l0) && double.IsFinite(l0) ? Math.Max(0, l0 / 1000) : 0;
            }
            var rate = v.TryGetProperty("rate", out var rt) && rt.ValueKind == JsonValueKind.Number && rt.TryGetDouble(out var r0) && r0 is > 0.05 and < 20 ? r0 : 1;
            var seq = v.TryGetProperty("seq", out var sq) && sq.ValueKind == JsonValueKind.Number && sq.TryGetInt64(out var s0) ? s0 : -1;
            video = new PatternsVideo(file.Length > 120 ? file[..120] : file, S("tag") is var t && t.Length <= 30 ? t : "", length, position, B("playing"), B("ended"), B("loops"),
                Paused: B("paused"), Rate: rate, Seq: seq, Transport: precise);
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Listen for a moment on Patterns' beacon (9700) and the show network's hello (9928): the Patterns machines on this
    /// network with their wire port.
    /// </summary>
    public static async Task<IReadOnlyList<PatternsMachine>> DiscoverAsync(TimeSpan listen, CancellationToken ct = default)
    {
        var found = new System.Collections.Concurrent.ConcurrentDictionary<string, PatternsMachine>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(listen);
        async Task ListenOn(int port)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                while (!timeout.IsCancellationRequested && found.Count < 32)
                {
                    UdpReceiveResult r;
                    try { r = await udp.ReceiveAsync(timeout.Token); }
                    catch (OperationCanceledException) { break; }
                    if (r.Buffer.Length > 8192 || ParseBeacon(r.Buffer, r.RemoteEndPoint.Address.ToString()) is not { } m) continue;
                    found[m.Address + ":" + m.WirePort] = m;
                }
            }
            catch (SocketException) { }
        }
        await Task.WhenAll(ListenOn(BeaconPort), ListenOn(HelloPort));
        return found.Values.ToList();
    }

    /// <summary>Pure: a beacon datagram → the machine, if it's a Patterns beacon with the wire on.</summary>
    public static PatternsMachine? ParseBeacon(byte[] data, string address)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            string S(string n) => root.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            // Patterns' own beacon ({"patterns":1,…}) or the show network's hello ({"t":"patterns","v":1,…}).
            var isBeacon = root.TryGetProperty("patterns", out var proto) && proto.ValueKind == JsonValueKind.Number;
            var isHello = S("t") == "patterns" && root.TryGetProperty("v", out var hv) && hv.ValueKind == JsonValueKind.Number && hv.TryGetInt32(out var hello) && hello == 1;
            if (!isBeacon && !isHello) return null;
            var wire = root.TryGetProperty("wire", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var wp) ? wp : 0;
            if (wire is <= 0 or > 65535) return null;
            var name = S("machine") is { Length: > 0 } mn ? mn : S("name");
            return new PatternsMachine(name.Length > 60 ? name[..60] : name, address, wire, isBeacon ? S("kind") : S("role"));
        }
        catch (JsonException) { return null; }
    }

    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;   // safe to call twice (a retiring link may be stopped from two places)
        _cts.Cancel();
        try { _stream?.Dispose(); } catch { }
        if (_thread.IsAlive) _thread.Join(1500);
        _cts.Dispose();
    }
}
