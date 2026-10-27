using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// Where the link's pairing stands on its current connection. Facts — STATE, receipts, clock samples, context — are taken
/// only once it is <see cref="Paired"/> (the desk said <c>OK paired</c> to this link's proof, or to its token on this PC) or
/// <see cref="Open"/> (the desk asks this link for no key); before that they are set aside and counted.
/// </summary>
public enum PatternsPairing { None, Paired, Open }

/// <summary>
/// Patterns (the user's show-control desk) as a playout source, over its TCP line protocol (port 9697):
/// <c>HELLO Stagewatch</c>, then the pairing (Show Bus §7), then Patterns pushes <c>STATE &lt;json&gt;</c> on every change —
/// latest-wins, so this reader only ever keeps the newest, read by <see cref="PlayoutStateReader"/>: a STATE longer than
/// <see cref="PlayoutWire.LineBytes"/> is read for the clip on air when it lies within that part, and the link turns amber with
/// the STATE's size (13.14). The token never leaves this PC in clear: a desk's <c>NONCE</c> of exactly 32 lowercase hex is
/// answered with <c>AUTH-PROOF</c> (13.6.8, <see cref="PlayoutWire.Proof"/>); plain <c>AUTH &lt;token&gt;</c> goes only to
/// a socket whose remote address is loopback (the desk on this PC), judged by a local-address seam, never a host name;
/// any other nonce gets no proof. Nothing the desk says before it pairs is taken. The clip's position counts on locally
/// between frames from a monotonic clock; a playing clip with no fresh frame for <see cref="StaleAfter"/> is reported
/// stale (no countdown) rather than trusted. Commands are a fixed whitelist (<c>VIDEO END n</c>, <c>VIDEO RESTART</c>,
/// and on desks that support them <c>VIDEO PLAY</c>, <c>VIDEO PAUSE</c>, <c>VIDEO STOP</c>, <c>VIDEO SEEK n</c>) — never a
/// raw line, and never a bare <c>STOP</c>, which on Patterns closes every output. Nothing is queued while disconnected or
/// unpaired.
/// </summary>
public sealed class PatternsLink : IPlayoutSource
{
    public const int DefaultPort = 9697;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);
    // Show Bus 1.1 liveness: a ping every 2 s; a link silent for 12 s is dropped and redialled.
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan DeadAfter = TimeSpan.FromSeconds(12);
    /// <summary>The wait before a redial: <see cref="RedialFirstMs"/> at first and after a connection that came up, doubled after each wait to at most <see cref="RedialMaxMs"/>.</summary>
    public const int RedialFirstMs = 250, RedialMaxMs = 5000;
    private static readonly TimeSpan ContextEvery = TimeSpan.FromSeconds(5);
    /// <summary>How long to wait for a desk's NONCE before judging it asks for none (desks older than Show Bus 1.1 never send one).</summary>
    private static readonly TimeSpan NonceWait = TimeSpan.FromMilliseconds(1500);

    private readonly string _host;
    private readonly int _port;
    private readonly string _token;
    private readonly Func<IPAddress, bool> _isLoopback;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeLock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private sealed record Reading(PlayoutVideo? Video, long AtMs);
    private readonly Latest<Reading> _latest = new();
    private NetworkStream? _stream;
    /// <summary>The current connection's stream once that connection has paired (or is open), else null. Commands are written
    /// only to the stream read from here, so a reconnect between a check and a write never puts a line on an unpaired
    /// connection.</summary>
    private volatile NetworkStream? _pairedStream;
    private volatile bool _connected;
    private volatile LinkHealth _health = LinkHealth.Starting;
    private volatile string _status = "connecting";
    private long _lastLineMs;
    private int _malformed;
    private int _oversize;
    private int _setAside;
    private int _nextId;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Action<bool, string>> _pending = new();
    private readonly ClockOffsetEstimator _offset = new();
    private volatile string? _context;
    /// <summary>A proof, or the token to a desk on this PC, went out on this connection: one per connection, never a second.</summary>
    private volatile bool _proofSent;
    /// <summary>This connection heard a NONCE (of any shape): the desk asks for a proof, so plain AUTH never goes on it.</summary>
    private volatile bool _nonceHeard;
    /// <summary>This connection's remote address is loopback, as the seam judged it when the socket connected.</summary>
    private volatile bool _loopback;
    private int _pairing;
    /// <summary>The desk speaks Show Bus 1.1 (it sent NONCE or answered the probe): request ids, timed pings and CONTEXT.
    /// Older desks refuse words they don't know, so they get plain lines.</summary>
    private volatile bool _bus11;
    /// <summary>The "@hi PING" probe is out: a 1.1 desk answers "OK @hi PONG …", an older one an ERR we swallow.</summary>
    private volatile bool _probing;

    /// <param name="loopback">Whether a connected socket's remote address is this machine — the only place plain AUTH may
    /// go. Defaults to <see cref="IsLoopbackAddress"/>; a test plays a desk on another machine from loopback with it.</param>
    public PatternsLink(string host, int port, string token, Func<IPAddress, bool>? loopback = null)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        _port = port is > 0 and < 65536 ? port : DefaultPort;
        _token = (token ?? "").Trim();
        _isLoopback = loopback ?? IsLoopbackAddress;
        _thread = new Thread(Run) { IsBackground = true, Name = "Stagewatch Patterns", Priority = ThreadPriority.BelowNormal };
    }

    public string Name => "Patterns";
    public LinkHealth Health => _health;
    public string Status => _status;
    public bool Connected => _connected;
    /// <summary>STATE frames that couldn't be read (ignored, never fatal).</summary>
    public int Malformed => Volatile.Read(ref _malformed);
    /// <summary>Lines longer than <see cref="PlayoutWire.LineBytes"/> (13.14's <c>oversize</c>): a STATE read in part, anything else not read.</summary>
    public int Oversize => Volatile.Read(ref _oversize);
    /// <summary>Where the pairing stands on the current connection (<see cref="PatternsPairing.None"/> while disconnected).</summary>
    public PatternsPairing Pairing => (PatternsPairing)Volatile.Read(ref _pairing);
    /// <summary>STATE frames, receipts, clock samples and contexts the desk sent before it paired: set aside, never taken.</summary>
    public int SetAsideBeforePairing => Volatile.Read(ref _setAside);

    public void Start() => _thread.Start();

    /// <summary>The desk's clock minus ours (Show Bus §5), or null before a good sample or while the link is not paired.</summary>
    public TimeSpan? ClockOffset => _connected && Pairing != PatternsPairing.None ? _offset.Offset : null;

    /// <summary>Patterns' context (Show Bus §6 JSON: the Eye's headline, lights, problems…), refreshed every 5 s, or null.</summary>
    public string? Context => _connected ? _context : null;

    /// <summary>A command's receipt arrived: (ok, words). Raised on the link's thread.</summary>
    public event Action<string, bool, string>? Receipt;

    /// <summary>The clip on air with its position counted on to now, or null (none, or stale).</summary>
    public PlayoutVideo? Video
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
        if (NotReady() is { } why) return why;   // an unpaired link knows no clip, so it says why it sends nothing
        if (_latest.Value?.Video is { Transport: false })
            return "this Patterns desk is older than Round 86 — it only takes Last 10 s and restart";
        if (_latest.Value?.Video is null && _connected) return "Patterns has no clip on air";
        return Send(line);
    }

    /// <summary>Null when a command may go out; else why not — no connection, or a desk that has not paired with this link.</summary>
    private string? NotReady()
    {
        if (!_connected || _stream is null) return "Patterns isn't connected — nothing sent";
        return Pairing == PatternsPairing.None ? "Patterns hasn't paired with this link — nothing sent (" + _status + ")" : null;
    }

    // Only lines built in this class reach the wire, and only on a paired link. Each carries a request id (Show Bus §3):
    // the desk's "OK @id" / "ERR @id why" is matched back to it and raised as Receipt, so "sent" is never mistaken for "done".
    private string? Send(string line)
    {
        if (line.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)) return "refused";   // never: it closes every Patterns output
        // One read: the stream held here is one whose own connection paired, so the line goes there or nowhere.
        var s = _pairedStream;
        if (s is null) return NotReady() ?? "Patterns hasn't paired with this link — nothing sent";
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
        var backoff = RedialFirstMs;
        while (!ct.IsCancellationRequested)
        {
            using var tcp = new TcpClient { NoDelay = true };
            try
            {
                var connect = tcp.ConnectAsync(_host, _port, ct).AsTask();
                if (!connect.Wait(2000, ct) || !tcp.Connected) throw new SocketException((int)SocketError.TimedOut);
                var stream = tcp.GetStream();
                // Pairing (Show Bus §7) starts afresh on every connection, judged by the socket's own remote address.
                _loopback = tcp.Client.RemoteEndPoint is IPEndPoint remote && _isLoopback(remote.Address);
                _proofSent = false;
                _nonceHeard = false;
                _bus11 = false;
                Volatile.Write(ref _pairing, (int)PatternsPairing.None);
                _pairedStream = null;
                _stream = stream;
                Write(stream, "HELLO Stagewatch");
                // The desk sends NONCE on connect and we prove we hold the token without sending it. A desk that trusts
                // this connection (on this PC, or open) sends none: ask it whether it speaks Show Bus 1.1.
                _probing = true;
                Write(stream, "@hi PING " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                Interlocked.Exchange(ref _lastLineMs, _clock.ElapsedMilliseconds);
                _connected = true;
                backoff = RedialFirstMs;
                Report(LinkHealth.Starting, $"connected to {_host}:{_port} — pairing");
                ReadLoop(stream, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is SocketException or IOException or AggregateException or ObjectDisposedException or InvalidOperationException)
            {
                Report(LinkHealth.NotFound, $"Patterns not reachable at {_host}:{_port} — retrying");
            }
            finally
            {
                _pairedStream = null;
                _connected = false;
                _stream = null;
                Volatile.Write(ref _pairing, (int)PatternsPairing.None);
                _latest.Set(null);
                _context = null;
                // Nothing waits for an answer from a dead link: every pending command is told it's lost.
                foreach (var id in _pending.Keys.ToList())
                    if (_pending.TryRemove(id, out var lost)) lost(false, "Patterns connection lost before it answered");
            }
            if (ct.IsCancellationRequested) break;
            ct.WaitHandle.WaitOne(backoff);
            backoff = Math.Min(RedialMaxMs, backoff * 2);
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
        var lines = new PlayoutLineReader();
        PlayoutLineReader.LineRead onLine = HandleLine;
        var lastPing = _clock.ElapsedMilliseconds;
        var connectedAt = lastPing;
        var lastContext = long.MinValue / 2;
        var waited = false;
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
                lines.Feed(buf.AsSpan(0, n), onLine);
            }
            if (_probing && now - connectedAt > 3000)
            {
                _probing = false;   // no answer to the probe: judged as an older desk
                NoNonce(stream);
            }
            if (!waited && now - connectedAt > NonceWait.TotalMilliseconds)
            {
                waited = true;
                NoNonce(stream);   // no NONCE in time: the desk asks this connection for none
            }
            // A link that says nothing at all is checked, then dropped: no silent half-open sockets. The ping carries
            // our time, so the desk's reply gives the clock offset (Show Bus §5).
            if (now - lastPing > PingEvery.TotalMilliseconds)
            {
                Write(stream, _bus11 ? "PING " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) : "PING");
                lastPing = now;
            }
            if (_bus11 && Pairing != PatternsPairing.None && now - lastContext > ContextEvery.TotalMilliseconds)
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

    /// <summary>
    /// A line off the wire. A STATE goes to <see cref="TakeState(ReadOnlySpan{byte}, long)"/> as it came, whole or in part; a line
    /// longer than this link reads that is not a STATE is counted and not read, so no receipt, clock sample or context is ever taken
    /// from part of one.
    /// </summary>
    private void HandleLine(ReadOnlySpan<byte> line, long size, bool whole)
    {
        if (!whole) Interlocked.Increment(ref _oversize);
        var json = line.StartsWith("STATE "u8) ? 6 : !whole && line.StartsWith("OK @st "u8) ? 7 : 0;
        if (json > 0)
        {
            // Judged by the pairing as it stood when the line arrived: the greeting STATE comes before the NONCE, never taken.
            if (Pairing == PatternsPairing.None) { SetAside(); return; }
            TakeState(line[json..], size - json);
            return;
        }
        if (whole) Handle(Encoding.UTF8.GetString(line));
    }

    private void Handle(string line)
    {
        // A line is judged by the pairing as it stood when the line arrived: what came before OK paired is set aside.
        var paired = Pairing != PatternsPairing.None;
        if (line.StartsWith("NONCE ", StringComparison.Ordinal))
        {
            OnNonce(line[6..]);
            return;
        }
        if (line.StartsWith("OK PONG ", StringComparison.Ordinal))
        {
            if (!paired) { SetAside(); return; }
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
            // The desk greets with its NONCE before it reads a line, so a 1.1 desk answering the probe without one asks
            // this connection for none.
            if (_stream is { } st) NoNonce(st);
            if (paired) Handle("OK PONG " + line[12..]);
            else SetAside();
            return;
        }
        // An older desk doesn't know "@hi" and answers with an untagged ERR. Once this link's proof or token went out, an
        // untagged ERR is the desk's answer to that (a refused proof is red), never taken for the probe's.
        if (_probing && !_proofSent && line.StartsWith("ERR", StringComparison.Ordinal) && !line.StartsWith("ERR @", StringComparison.Ordinal))
        {
            _probing = false;   // plain lines from here on
            if (_stream is { } st) NoNonce(st);
            return;
        }
        // The desk's answer to this link's proof (or, on this PC, its token): only once one went out, and only to settle.
        if (line == "OK paired" || line.StartsWith("OK paired ", StringComparison.Ordinal))
        {
            if (_proofSent && Pairing != PatternsPairing.Paired) Settle(PatternsPairing.Paired);
            return;
        }
        if (line.StartsWith("OK open", StringComparison.Ordinal))
        {
            if (_proofSent && !paired) Settle(PatternsPairing.Open);
            return;
        }
        // Receipts: "OK @id …" / "ERR @id …".
        if ((line.StartsWith("OK @", StringComparison.Ordinal) || line.StartsWith("ERR @", StringComparison.Ordinal)))
        {
            if (!paired) { SetAside(); return; }
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
            if (id == "st")
            {
                // The STATE this link asked for once it paired; a refusal leaves the link waiting for the desk's next push.
                if (ok && words.StartsWith('{')) TakeState(Encoding.UTF8.GetBytes(words), Encoding.UTF8.GetByteCount(words));
                return;
            }
            if (_pending.TryRemove(id, out var done)) done(ok, words.Length > 200 ? words[..200] : words);
            if (ok) return;
            line = "ERR " + words;
        }
        if (line.StartsWith("ERR", StringComparison.Ordinal))
        {
            var why = line.Length > 4 ? line[4..] : "refused";
            Report(why.Contains("not paired", StringComparison.OrdinalIgnoreCase) || why.Contains("token", StringComparison.OrdinalIgnoreCase)
                ? LinkHealth.Error : _health, "Patterns: " + (why.Length > 160 ? why[..160] : why));
        }
    }

    private void SetAside() => Interlocked.Increment(ref _setAside);

    /// <summary>
    /// A STATE from the paired desk, pushed or asked for, whole or the first <see cref="PlayoutWire.LineBytes"/> of one
    /// (<paramref name="size"/> its whole size): the newest is the reading. One read in part is amber with its size; its clip is read
    /// when its <c>video</c> lies within that part, and otherwise there is none, never the last one kept. A <c>video</c> of a shape
    /// the reader does not take is amber and gives no clip; a STATE the reader does not read at all is counted, and the reading and the
    /// link stay as they were.
    /// </summary>
    private void TakeState(ReadOnlySpan<byte> json, long size)
    {
        if (PlayoutStateReader.Read(json, size) is not { } state)
        {
            Interlocked.Increment(ref _malformed);
            return;
        }
        var video = state.Video;
        _latest.Set(new Reading(video, _clock.ElapsedMilliseconds));
        var said = (video is null ? $"connected to {_host}:{_port} — no clip on air"
            : $"{video.Title}" + (video.CountsDown ? $": {TimeSpan.FromSeconds(Math.Max(0, video.LengthSeconds - video.PositionSeconds)):mm\\:ss} left" : video.Loops ? " (looping)" : "")) + OpenNote;
        var part = state.Whole ? "" : " · " + PlayoutWire.OversizeWords(state.Bytes) + ", read in part";
        if (state.Unreadable.Contains("video", StringComparer.Ordinal))
            Report(LinkHealth.Starting, $"connected to {_host}:{_port} — its STATE gives the clip on air in a shape this link does not read, so no countdown" + OpenNote + part);
        else if (state.Whole) Report(PairedHealth, said);
        else if (state.HasVideo) Report(LinkHealth.Starting, said + part);
        else Report(LinkHealth.Starting, PlayoutWire.OversizeWords(state.Bytes) + ": its clip on air lies past the part read — no countdown" + OpenNote);
    }

    /// <summary>
    /// The desk asks for a proof. Only a nonce of exactly 32 lowercase hex is proved, once per connection; plain AUTH never
    /// goes on a connection that heard a NONCE, whatever its shape. A NONCE after the link paired changes nothing.
    /// </summary>
    private void OnNonce(string nonce)
    {
        _nonceHeard = true;
        _bus11 = true;
        if (_proofSent || Pairing != PatternsPairing.None) return;
        if (!PlayoutWire.IsProvableNonce(nonce))
        {
            Report(LinkHealth.Starting, $"{_host}:{_port} sent a NONCE that is not 32 lowercase hex — no proof sent; nothing is taken from it until it pairs");
            return;
        }
        if (_token.Length == 0)
        {
            Report(LinkHealth.Starting, $"not paired — {_host}:{_port} asks for its pairing token (Settings → Integrations → Patterns); nothing is taken from it until it pairs");
            return;
        }
        if (PlayoutWire.Proof(_token, nonce) is not { } proof)
        {
            Report(LinkHealth.Starting, $"not paired — {PlayoutWire.TokenProblem(_token)} (Settings → Integrations → Patterns); no proof sent to {_host}:{_port}");
            return;
        }
        if (_stream is not { } st) return;
        _proofSent = true;
        Write(st, "AUTH-PROOF " + proof);
    }

    /// <summary>
    /// The desk sent no NONCE (by its answer to the probe, within <see cref="NonceWait"/>, or when the probe went unanswered):
    /// it asks this connection for no proof. The token goes in clear only to a desk on this PC (a loopback socket), which
    /// answers OK paired or OK open; off this PC it stays here. A desk that asks for nothing is open once it is known to speak
    /// Show Bus 1.1 (it would have asked for a proof), or at once on this PC when this link holds no token. Off this PC a desk
    /// is called older only once it refused the probe or let it go unanswered; an older desk off this PC never pairs.
    /// </summary>
    private void NoNonce(NetworkStream stream)
    {
        if (_nonceHeard || _proofSent || Pairing != PatternsPairing.None) return;
        if (_token.Length > 0 && _loopback)
        {
            _proofSent = true;
            Write(stream, "AUTH " + _token);
            return;
        }
        if (_bus11 || (_loopback && _token.Length == 0))
        {
            Settle(PatternsPairing.Open);
            return;
        }
        if (_probing) return;   // not known yet: the status stays "pairing" until the probe is answered or times out
        Report(LinkHealth.Starting, _token.Length > 0
            ? $"{_host}:{_port} is older than Show Bus 1.1 and pairs only by sending its token in clear — update Patterns; the token stays on this PC"
            : $"{_host}:{_port} is older than Show Bus 1.1 and pairs only on its own PC — update Patterns; nothing is taken from it until it pairs");
    }

    /// <summary>
    /// The pairing settles on this connection: its stream becomes the one commands go to. The greeting STATE came before
    /// the pairing and was set aside, so a Show Bus 1.1 desk is asked for its STATE now (its answer carries this link's id);
    /// an older desk answers untagged, so it is not asked and its next push is the first reading.
    /// </summary>
    private void Settle(PatternsPairing how)
    {
        Volatile.Write(ref _pairing, (int)how);
        _pairedStream = _stream;
        Report(PairedHealth, (how == PatternsPairing.Paired ? $"paired with {_host}:{_port}" : $"connected to {_host}:{_port}") + OpenNote + " — waiting for its STATE");
        if (_bus11 && _stream is { } st) Write(st, "@st STATUS");
    }

    /// <summary>Amber while the link holds a token the desk never asked for (it asks for no key); otherwise green.</summary>
    private LinkHealth PairedHealth => Pairing == PatternsPairing.Open && _token.Length > 0 ? LinkHealth.Starting : LinkHealth.Ok;

    private string OpenNote => Pairing != PatternsPairing.Open ? ""
        : _token.Length > 0 ? " · asks for no key — the token stays on this PC" : " · asks this link for no key";

    /// <summary>Pure: the default local-address seam — the socket's remote address is loopback (IPv4-mapped included).</summary>
    public static bool IsLoopbackAddress(IPAddress address)
        => IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

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
