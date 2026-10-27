using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// What a playout link is set to: the peer's line wire, the pairing token it proves (never sent in clear off this PC), the inst its
/// request ids carry, and its seams — whether a socket's remote address is this machine, the clock and 13.15.1's j.
/// </summary>
public sealed record PlayoutLinkOptions
{
    /// <summary>The peer's host, as Settings names it ("" is this PC).</summary>
    public required string Host { get; init; }

    /// <summary>The peer's line wire; anything outside 1–65535 is Patterns' own port.</summary>
    public required int Port { get; init; }

    /// <summary>The pairing token ("" = none). Proved against a desk's NONCE, and sent as plain AUTH only to a socket on this PC.</summary>
    public string Token { get; init; } = "";

    /// <summary>This process's hello <c>inst</c> (§2, 8 lowercase hex): every request id the link makes ends with it (13.12.1).</summary>
    public required string Inst { get; init; }

    /// <summary>The link's name (Settings → playout links): its words say its label, "Patterns" for link "patterns".</summary>
    public string Name { get; init; } = PlayoutRules.PatternsName;

    /// <summary>
    /// Whether a connected socket's remote address is this machine — the only place plain AUTH may go, and what makes the link local
    /// (13.1). Asked of an address, never of a host name; a test plays a desk on another machine from loopback by answering false.
    /// </summary>
    public Func<IPAddress, bool> IsLoopback { get; init; } = PlayoutLink.IsLoopbackAddress;

    /// <summary>The clock every wait, deadline and stamp of the link is counted on.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>13.15.1's j, in [0, 1); null takes the process's, seeded once from the CSPRNG.</summary>
    public Func<double>? Jitter { get; init; }

    /// <summary>
    /// What the link starts from (13.15.3): a pin, a floor, and a hold with its end in wall time; null for none. A pin or a floor that
    /// asks for TLS is never dialled plain: this carrier has no TLS, so such a link is refused with nothing sent.
    /// </summary>
    public PeerKept? Kept { get; init; }
}

/// <summary>
/// Where the pairing stands on the link's connection up now: facts — STATE, receipts, clock samples, context — are taken only once
/// it is <see cref="Paired"/> (the peer said <c>OK paired</c> to this link's proof, or to its token on this PC) or <see cref="Open"/>
/// (the peer asks this link for no proof); before that they are set aside and counted.
/// </summary>
public enum PlayoutPairing { None, Paired, Open }

/// <summary>
/// A command's outcome as the link knows it now (13.12.2): its request id, the line it was (without its id), its outcome by 13.12.3's
/// table and the words that go with it. An outcome only moves forward; each move is said once.
/// </summary>
public readonly record struct PlayoutAnswer(string Id, string Line, PeerOutcome Outcome, string Words)
{
    /// <summary>Pure: the desk's status line for this outcome, the peer named by <paramref name="label"/>.</summary>
    public string StatusLine(string label)
    {
        var words = Words.Length > 0 ? $" ({Words})" : "";
        return Outcome switch
        {
            PeerOutcome.Applied => $"{label} did it: {Line}{words}",
            PeerOutcome.Accepted => $"{label} took {Line} and has not run it yet{words}",
            PeerOutcome.Refused => $"{label} refused {Line}: {Words}",
            PeerOutcome.Unconfirmed => $"{label}: {Line} unconfirmed — {Words}",
            _ => $"{label}: {Line} not sent — {Words}",
        };
    }
}

/// <summary>
/// A playout peer — a Patterns desk, or a PlayMap node's line wire — over its TCP line protocol, as a level-0 connector of Show Bus
/// 1.3 §13 on Stagewatch's PeerLink core: <see cref="PeerLinkConnector"/> decides — the dial and its jittered redial, liveness
/// (live, stale past <c>link.stale.ms</c>, down past <c>link.drop.ms</c>) with its own self-gap, each command's outcome by 13.12.3,
/// the matched pongs its clock takes, holds — and this class carries it over a socket. It offers no LINK yet: it greets with
/// <c>HELLO Stagewatch</c> and pairs by §7's level-0 rules (13.6.8): a NONCE of exactly 32 lowercase hex is answered with
/// <c>AUTH-PROOF</c>, once a connection; plain <c>AUTH &lt;token&gt;</c> goes only to a socket whose remote address is this PC and
/// that heard no NONCE; a desk off this PC older than Show Bus 1.1 never pairs. Nothing the peer says before it pairs is taken, and a
/// refused proof holds the token <c>peerlink.hold.refused.ms</c> (13.15.3). STATE is latest-wins, read by
/// <see cref="PlayoutStateReader"/>, in part past <see cref="PlayoutWire.LineBytes"/> (13.14). Each command carries a request id
/// <c>s&lt;n&gt;-&lt;inst&gt;</c>, unique for the process; at most <c>link.inflight.max</c> wait for answers and none is evicted;
/// nothing is queued while the link is not up, and a command written before the link dropped is unconfirmed, never failed. Commands
/// are a fixed set — <c>VIDEO END n</c>, <c>VIDEO RESTART</c>, and on desks that have them <c>VIDEO PLAY</c>, <c>PAUSE</c>,
/// <c>STOP</c>, <c>SEEK n</c> — never a raw line, and never a bare <c>STOP</c>, which on Patterns closes every output. One writer:
/// every step of the core runs under one lock, on its time read inside that lock, and what readers take — the light and words, the
/// record, the clock, the reading and the context — is one snapshot published after each step; a link that is not live is never green.
/// </summary>
public sealed class PlayoutLink : IPlayoutSource
{
    public const int DefaultPort = PlayoutRules.PatternsPort;
    /// <summary>A playing clip with no fresh frame for this long shows no countdown: an old countdown is worse than none.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);
    /// <summary>Show Bus §5: a ping every 2 s once the link is paired (<c>ping.min.ms</c>–<c>ping.max.ms</c>).</summary>
    public static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(2);
    /// <summary>How often the evaluator runs (13.10.3: at least every <c>link.check.max.ms</c>), on a timer of its own.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(250);
    /// <summary>How often a Show Bus 1.1 desk is asked for its context.</summary>
    public static readonly TimeSpan ContextEvery = TimeSpan.FromSeconds(5);
    /// <summary>How long to wait for a desk's NONCE before judging it asks for none (desks older than Show Bus 1.1 never send one).</summary>
    public static readonly TimeSpan NonceWait = TimeSpan.FromMilliseconds(1500);
    /// <summary>How long the <c>@hi PING</c> probe waits for its answer before the desk is judged older than Show Bus 1.1.</summary>
    public static readonly TimeSpan ProbeWait = TimeSpan.FromSeconds(3);
    /// <summary>One write's deadline (§14 <c>link.write.deadline.max.ms</c>): a write stuck longer ends the connection.</summary>
    public static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(10);
    /// <summary>The prefix of the request ids a playout link makes (13.12.1).</summary>
    public const string IdPrefix = "s";
    /// <summary>
    /// 13.11.7: how far this PC's wall clock may move against its monotonic one between two reads before it has stepped, and the clock's
    /// window is emptied. Two reads a pause did not split agree to within <see cref="WallReadMs"/>, and a slewing clock moves far less
    /// between the evaluator's runs.
    /// </summary>
    public const long WallStepMs = 100;
    /// <summary>A read of the two clocks the thread was paused in the middle of (its monotonic reads further apart) says nothing of a step.</summary>
    public const long WallReadMs = 5;
    /// <summary>
    /// 13.10.4: one line names the evaluator's own pause, and at most one such line goes out in this span; the next names how many
    /// pauses it folds in.
    /// </summary>
    public static readonly TimeSpan PauseLineEvery = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The request-id counters of this process, one per prefix and inst (13.12.1: an id is unique for the process), shared by every
    /// playout link: a link made again, or two side by side, never repeats an id.
    /// </summary>
    private static readonly ConcurrentDictionary<string, StrongBox<long>> Counters = new(StringComparer.Ordinal);

    private readonly PlayoutLinkOptions _o;
    private readonly TimeProvider _time;
    private readonly long _t0;
    private readonly string _host;
    private readonly int _port;
    private readonly string _token;
    private readonly string _label;
    private readonly StrongBox<long> _ids;
    private readonly Lock _gate = new();
    private readonly PeerLinkConnector _m;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, string> _outbound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal);
    private readonly List<PlayoutAnswer> _toSay = [];
    private readonly List<string> _toLog = [];
    private Task? _run;
    private ITimer? _tick;
    private Connection? _conn;
    private int _started, _disposed;

    // What the link says, set by its pairing and its readings (under the lock); the machine's state is laid over it in Publish.
    private LinkHealth _saidHealth = LinkHealth.Starting;
    private string _said;

    // The newest reading of the peer's STATE and its context, on the connection up now (under the lock): published with the rest.
    private sealed record Reading(PlayoutVideo? Video, long AtMs);
    private Reading? _reading;
    private string? _context;

    // This PC's wall clock less its monotonic one at the last read a pause did not split (13.11.7), and the evaluator's own pauses
    // (13.10.4): when the last line about one went out, and how many came since.
    private long? _skew;
    private long _pauseSaidAt = long.MinValue / 2;
    private int _pausesUnsaid;

    private volatile Published _pub;
    private int _malformed, _oversize, _setAside;

    /// <summary>A command the link wrote, its line without its id and the last outcome it said.</summary>
    private sealed class Tracked(string line, long order)
    {
        public readonly string Line = line;
        public readonly long Order = order;
        public PeerOutcome? Said;
    }

    /// <summary>
    /// What every reader takes, published whole after each step of the core: the light and words, the core's state and record, the
    /// clock, and the reading and context of the connection up now — one snapshot, so no reader sees a reading beside an older light.
    /// </summary>
    private sealed record Published(LinkHealth Health, string Status, PeerLinkState State, PlayoutPairing Pairing, bool Connected,
        PeerLinkRecord Record, PeerClock.Belief? Clock, bool ClockApart, Reading? Reading, string? Context);

    /// <summary>One connection: its socket and what its pairing has seen (under the lock).</summary>
    private sealed class Connection(TcpClient tcp, NetworkStream stream, bool loopback, CancellationToken stop) : IDisposable
    {
        public readonly TcpClient Tcp = tcp;
        public readonly NetworkStream Stream = stream;
        /// <summary>The socket's remote address is this PC, as the seam judged it when it connected.</summary>
        public readonly bool Loopback = loopback;
        /// <summary>Cancelled when the connection ends: the core ended it, a write broke, or the link stops.</summary>
        public readonly CancellationTokenSource End = CancellationTokenSource.CreateLinkedTokenSource(stop);
        public long ConnectedAt;
        public PlayoutPairing Pairing;
        /// <summary>A proof, or the token to a desk on this PC, went out on this connection: one per connection, never a second.</summary>
        public bool ProofSent;
        /// <summary>What went out was the token as plain AUTH (untagged, so its answer is too), not the proof under <c>@pa</c>.</summary>
        public bool PlainAuth;
        /// <summary>This connection heard a NONCE (of any shape): the desk asks for a proof, so plain AUTH never goes on it.</summary>
        public bool NonceHeard;
        /// <summary>The last NONCE of 32 lowercase hex this connection heard: what its one proof answers.</summary>
        public string? Nonce;
        /// <summary>The desk speaks Show Bus 1.1 (it sent NONCE or answered the probe): request ids, timed pings and CONTEXT.</summary>
        public bool Bus11;
        /// <summary>The <c>@hi PING</c> probe is out: a 1.1 desk answers <c>OK @hi PONG …</c>, an older one an ERR or nothing.</summary>
        public bool Probing;
        public bool NonceJudged;
        /// <summary>A write on this connection broke: the connection ends, and the link says so.</summary>
        public bool Broke;
        /// <summary>What ended this connection is said already — a refusal, the evaluator's drop, a busy desk, a broken write.</summary>
        public bool EndExplained;
        /// <summary>The link said why this desk does not pair (an older desk, a NONCE it does not prove, no token): kept while it redials.</summary>
        public bool Advised;
        public long LastPing = long.MinValue / 2, LastContext = long.MinValue / 2;

        public void Dispose()
        {
            End.Cancel();
            Stream.Dispose();
            Tcp.Dispose();
            End.Dispose();
        }
    }

    public PlayoutLink(PlayoutLinkOptions options)
    {
        _o = options ?? throw new ArgumentNullException(nameof(options));
        _time = options.Time;
        _t0 = _time.GetTimestamp();
        _host = string.IsNullOrWhiteSpace(options.Host) ? "127.0.0.1" : options.Host.Trim();
        _port = options.Port is > 0 and < 65536 ? options.Port : DefaultPort;
        _token = (options.Token ?? "").Trim();
        _label = PlayoutRules.Label(options.Name);
        // An id this link would make is checked now, so an inst of another shape fails here and not at the first command.
        _ = PeerLink.RequestId(IdPrefix, 1, options.Inst);
        _ids = Counters.GetOrAdd(IdPrefix + "-" + options.Inst, _ => new StrongBox<long>());
        _said = $"connecting to {_host}:{_port}";
        var settings = new PeerLinkSettings
        {
            Carrier = PeerCarrier.Line,
            Name = _label,
            Address = _host + ":" + _port.ToString(CultureInfo.InvariantCulture),
            // The token is held as 13.6.8's code: proved only by the legacy proof, and only one 1.1 desks key alike.
            Key = PlayoutWire.TokenProblem(_token) is null ? "code" : null,
            Local = IsLocal(_host, options.IsLoopback),
            ShowNeeds = true,
            IdPrefix = IdPrefix,
            Inst = options.Inst,
            OffersLink = false,
            TriesTls = false,
        };
        lock (_gate)
        {
            var now = Now();
            _m = new PeerLinkConnector(settings, options.Kept, now, options.Jitter);
            _pub = Publish(now);
        }
    }

    /// <summary>The link's label: "Patterns" for link "patterns".</summary>
    public string Name => _label;
    public LinkHealth Health => _pub.Health;
    public string Status => _pub.Status;
    /// <summary>A socket is up to the peer (paired or not).</summary>
    public bool Connected => _pub.Connected;
    /// <summary>Where the pairing stands on the connection up now (<see cref="PlayoutPairing.None"/> while there is none).</summary>
    public PlayoutPairing Pairing => _pub.Pairing;
    /// <summary>13.16.1's state of the link, as last published.</summary>
    public PeerLinkState State => _pub.State;
    /// <summary>13.16.3's record of the link, as last published: what every reader of the link takes. It carries no token, proof or nonce.</summary>
    public PeerLinkRecord Record => _pub.Record;
    /// <summary>STATE frames that couldn't be read (ignored, never fatal).</summary>
    public int Malformed => Volatile.Read(ref _malformed);
    /// <summary>Lines longer than <see cref="PlayoutWire.LineBytes"/> (13.14's <c>oversize</c>): a STATE read in part, anything else not read.</summary>
    public int Oversize => Volatile.Read(ref _oversize);
    /// <summary>STATE frames, receipts, clock samples and contexts the peer sent before it paired: set aside, never taken.</summary>
    public int SetAsideBeforePairing => Volatile.Read(ref _setAside);

    /// <summary>The peer's clock minus this PC's (Show Bus §5): the believed sample of its matched pongs (13.11), while the link is paired.</summary>
    public TimeSpan? ClockOffset => Clock is { } b ? TimeSpan.FromMilliseconds(b.OffsetMs) : null;

    /// <summary>The believed clock sample (offset, rtt, error, t4), while the link is paired; null before a matched pong.</summary>
    public PeerClock.Belief? Clock => _pub is { Pairing: not PlayoutPairing.None } pub ? pub.Clock : null;

    /// <summary>13.11.5's CLOCKS APART, while the link is paired: |offset| − its error past <c>clock.apart.ms</c>.</summary>
    public bool ClockApart => _pub is { Pairing: not PlayoutPairing.None, ClockApart: true };

    /// <summary>The peer's context (Show Bus §6 JSON), refreshed every 5 s from a 1.1 desk while paired, or null.</summary>
    public string? Context => _pub is { Pairing: not PlayoutPairing.None } pub ? pub.Context : null;

    /// <summary>A command's outcome came, or moved forward (13.12.2). Raised on the link's own threads, outside its lock.</summary>
    public event Action<PlayoutAnswer>? Answered;

    /// <summary>
    /// A line for the show's log, raised on the link's own threads outside its lock: the evaluator's own pause (13.10.4) — at most one
    /// line each <see cref="PauseLineEvery"/>, naming how many pauses it folds in.
    /// </summary>
    public event Action<string>? Logged;

    /// <summary>
    /// 13.16.3's record at this moment, read from the core under the link's lock with every deadline due by now applied — for a reader
    /// that must see the link between the evaluator's runs. The evaluator alone closes a link gone down.
    /// </summary>
    public PeerLinkRecord RecordNow()
    {
        lock (_gate) return _m.Record(Now());
    }

    /// <summary>A command's outcome at this moment (13.12.2), by its request id; null while it waits for its first answer or is not known.</summary>
    public PeerOutcome? OutcomeOf(string id)
    {
        lock (_gate)
        {
            _m.State(Now());
            return _m.OutcomeOf(id);
        }
    }

    /// <summary>How many commands wait for answers at this moment (13.12.8: at most <c>link.inflight.max</c>).</summary>
    public int InFlight
    {
        get
        {
            lock (_gate)
            {
                _m.State(Now());
                return _m.InFlight;
            }
        }
    }

    /// <summary>
    /// 13.11.6: whether a command timed within <paramref name="toleranceMs"/> may use the link's clock now — fit for that tolerance and
    /// measured on a local link (this link has no TLS yet); a clock measured over a plain link off this PC never schedules.
    /// </summary>
    public bool CanSchedule(long toleranceMs)
    {
        lock (_gate) return _m.CanSchedule(Now(), toleranceMs);
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _tick = _time.CreateTimer(_ => Tick(), null, CheckEvery, CheckEvery);
        _run = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>The clip on air with its position counted on to now, or null: none, the link not live, or a playing clip gone stale.</summary>
    public PlayoutVideo? Video
    {
        get
        {
            var pub = _pub;
            if (pub.Reading is not { Video: { } v } r || pub.Pairing == PlayoutPairing.None || pub.State != PeerLinkState.Live) return null;
            var age = (MonoMs() - r.AtMs) / 1000.0;
            if (v.Playing && age > StaleAfter.TotalSeconds) return null;   // fail safe: an old countdown is worse than none
            if (!v.Playing || v.Paused || !v.CountsDown) return v;
            return v with { PositionSeconds = Math.Min(v.LengthSeconds, v.PositionSeconds + age * v.Rate) };
        }
    }

    /// <summary>
    /// For the VT countdown: only a clip with a known length that doesn't loop. At a playback rate other than 1 the clip's time is
    /// scaled to real time, so the countdown says how long it will really take.
    /// </summary>
    public PlayoutState? Value
        => Video is { CountsDown: true } v
            ? new PlayoutState(_label, v.Title, v.LengthSeconds / v.Rate, v.PositionSeconds / v.Rate, v.Playing && !v.Paused)
            : null;

    /// <summary>Jump the clip on air to its last <paramref name="seconds"/>. Null = written, else why not.</summary>
    public string? EndIn(int seconds)
    {
        if (seconds is < 1 or > 3600) return "seconds must be 1-3600";
        if (Video is { } v && !v.CountsDown) return v.Loops ? "the clip on air loops — it has no end to jump to" : $"{_label} doesn't know this clip's length yet";
        return Send("VIDEO", "END", seconds.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Play the clip on air again from its start. Null = written, else why not.</summary>
    public string? Restart() => Send("VIDEO", "RESTART");

    /// <summary>Play on / pause / stop (back to the top, held) the clip on air — desks with transport only.</summary>
    public string? Play() => Transport("PLAY");
    public string? Pause() => Transport("PAUSE");
    public string? StopClip() => Transport("STOP");   // VIDEO STOP: the clip only — never the bare STOP that closes outputs

    /// <summary>Move the clip on air to <paramref name="seconds"/> from its start.</summary>
    public string? Seek(int seconds)
    {
        if (seconds is < 0 or > 24 * 3600) return "seek takes 0 to 24 hours";
        if (Video is { LengthSeconds: > 0 } v && seconds > v.LengthSeconds) return $"the clip is only {TimerTick.FormatSeconds((long)v.LengthSeconds)} long";
        return Transport("SEEK", seconds.ToString(CultureInfo.InvariantCulture));
    }

    private string? Transport(string what, params string[] more)
    {
        if (NotReady() is { } why) return why;   // an unpaired link knows no clip, so it says why it sends nothing
        var video = _pub.Reading?.Video;
        if (video is { Transport: false })
            return $"this {_label} desk is older than Round 86 — it only takes Last 10 s and restart";
        if (video is null) return $"{_label} has no clip on air";
        return Send("VIDEO", [what, .. more]);
    }

    /// <summary>Null when a command may go out; else why not — no connection, or a peer that has not paired with this link.</summary>
    private string? NotReady()
    {
        var pub = _pub;
        if (!pub.Connected) return $"{_label} isn't connected — nothing sent";
        return pub.Pairing == PlayoutPairing.None ? $"{_label} hasn't paired with this link — nothing sent ({pub.Status})" : null;
    }

    /// <summary>
    /// One command, built here and nowhere else (<see cref="PlayoutWire.Line"/>), written only on a connection that paired: the core
    /// decides whether it may go (13.12.3's not-sent rows, nothing queued) and reads its answer by its request id. Null once it was
    /// written, else why it was not. A desk older than Show Bus 1.1 echoes no id, so its command is unconfirmed at once (13.12.3).
    /// </summary>
    private string? Send(string verb, params string[] words)
    {
        if (verb == "STOP" && words.Length == 0) return "refused";   // never: it closes every Patterns output
        if (PlayoutWire.Line(PlayoutRules.Patterns, null, verb, words) is not { } line) return "that command can't be written on this link";
        string? why;
        lock (_gate)
        {
            why = SendLocked(verb, line);
        }
        SayAnswers();
        return why;
    }

    private string? SendLocked(string verb, string line)
    {
        var now = Now();
        var conn = _conn;
        if (conn is null || conn.Pairing == PlayoutPairing.None)
        {
            _pub = Publish(now);
            return NotReady() ?? $"{_label} hasn't paired with this link — nothing sent";
        }
        var id = PeerLink.RequestId(IdPrefix, Interlocked.Increment(ref _ids.Value), _o.Inst);
        _outbound[id] = conn.Bus11 ? "@" + id + " " + line : line;
        if (_m.Send(now, id, verb) == PeerOutcome.NotSent)
        {
            _outbound.Remove(id);
            var why = _m.WordsOf(id);
            _pub = Publish(now);
            return why;
        }
        Track(id, line);
        Flush(conn);
        // 13.12.3: an acceptor older than 1.1 echoes no id — its command is unconfirmed, sent with no receipt to come.
        if (!conn.Bus11 && PeerLink.ReadLine("OK") is { Reading: var noReceipt }) _m.Answer(now, id, noReceipt);
        Ended(conn, now);
        TrackAnswers();
        _pub = Publish(now);
        return null;
    }

    // ------------------------------------------------------------------ the connection

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            long wait;
            string? scheme;
            lock (_gate)
            {
                var now = Now();
                // The core's own wait: 13.15.1's redial, a hold, or none at all (a dial now). A link the core dials nothing for (a
                // refusal a person must act on) looks again each second.
                scheme = _m.Dial(now);
                wait = scheme is not null ? 0 : Math.Max(1, _m.Record(now).NextMs ?? 1000);
                _pub = Publish(now);
            }
            if (scheme is null)
            {
                if (!await Waits(TimeSpan.FromMilliseconds(wait), stop)) break;
                continue;
            }
            await ConnectionAsync(scheme, stop);
            SayAnswers();
        }
    }

    /// <summary>A wait on the link's clock; false once the link stops.</summary>
    private async Task<bool> Waits(TimeSpan wait, CancellationToken stop)
    {
        try
        {
            await Task.Delay(wait, _time, stop);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// One dial on the scheme the core meant, the connection it makes, and its end: whatever ends it, the core hears it once, and the
    /// link says why — never a light that was the live link's.
    /// </summary>
    private async Task ConnectionAsync(string scheme, CancellationToken stop)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(PeerLink.ConnectTimeoutMs), _time);
            using var either = CancellationTokenSource.CreateLinkedTokenSource(stop, deadline.Token);
            await tcp.ConnectAsync(_host, _port, either.Token);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException or ArgumentException)
        {
            tcp.Dispose();
            lock (_gate)
            {
                var now = Now();
                if (_m.KeepsConnection(now)) _m.Closed(now);
                if (!stop.IsCancellationRequested) Say(LinkHealth.NotFound, $"{_label} not reachable at {_host}:{_port}");
                _pub = Publish(now);
            }
            return;
        }
        tcp.Client.SendTimeout = (int)WriteDeadline.TotalMilliseconds;
        var loopback = tcp.Client.RemoteEndPoint is IPEndPoint remote && _o.IsLoopback(remote.Address);
        var conn = new Connection(tcp, tcp.GetStream(), loopback, stop);
        if (!Greet(conn, scheme))
        {
            // The core ended the dial at its own deadline before the socket came, or refused it: nothing is said on it.
            conn.Dispose();
            return;
        }
        await ReadAsync(conn);
        lock (_gate)
        {
            var now = Now();
            if (ReferenceEquals(_conn, conn))
            {
                _conn = null;
                var kept = _m.KeepsConnection(now);
                if (kept) _m.Closed(now);
                // Why it ended, unless that was said as it ended (a refusal, the evaluator's drop, a busy desk, a broken write) or is an
                // error the desk said on it. A desk that does not pair keeps the words that say why while the link redials.
                if (!stop.IsCancellationRequested && !conn.EndExplained && _saidHealth is not LinkHealth.Error)
                {
                    if (kept) Say(LinkHealth.NotFound, $"{_label} closed the connection");
                    else if (conn.Pairing != PlayoutPairing.None) Say(LinkHealth.NotFound, $"the link to {_host}:{_port} ended");
                    else if (!conn.Advised)
                        Say(LinkHealth.NotFound, $"{_host}:{_port} did not pair within {(PeerLink.HandshakeMs / 1000).ToString(CultureInfo.InvariantCulture)} s");
                }
                _reading = null;
                _context = null;
                _outbound.Clear();
                TrackAnswers();
                _pub = Publish(now);
            }
        }
        conn.Dispose();
    }

    /// <summary>
    /// The socket is up: the core greets (<c>HELLO Stagewatch</c>; it offers no LINK yet), and the link probes whether the peer speaks
    /// Show Bus 1.1. This carrier has no TLS, so a dial the core meant as TLS (a pin, a floor, a hello's <c>tls</c>) found none: the
    /// core refuses a node a proof or a person pinned, or a floor holds to TLS, with nothing sent (13.9.3), and otherwise goes on plain
    /// (13.3.3) — never a plain socket taken for TLS. False when the core had ended the dial already or refused it, and nothing is said
    /// on the socket.
    /// </summary>
    private bool Greet(Connection conn, string scheme)
    {
        lock (_gate)
        {
            var now = Now();
            if (!_m.KeepsConnection(now))
            {
                _pub = Publish(now);
                return false;
            }
            if (scheme == "tls") _m.NoTls(now);
            else _m.Connected(now);
            if (!_m.KeepsConnection(now))
            {
                _pub = Publish(now);
                return false;
            }
            _conn = conn;
            conn.ConnectedAt = now.Mono;
            // 13.11.7: the window empties when the peer's inst changes. A level-0 link never learns the inst, so each connection may be
            // another desk at the address, or the same one restarted: its clock starts with no sample.
            _m.Clock.Clear();
            Say(LinkHealth.Starting, $"connected to {_host}:{_port} — pairing");
            Flush(conn);
            // The desk sends NONCE on connect and this link proves it holds the token without sending it. A desk that trusts this
            // connection (on this PC, or open) sends none: ask whether it speaks Show Bus 1.1.
            conn.Probing = true;
            Write(conn, "@hi PING " + now.Wall.ToString(CultureInfo.InvariantCulture));
            Ended(conn, now);
            _pub = Publish(now);
            return true;
        }
    }

    /// <summary>Reads the connection's lines until it ends: the peer closes it, the core ends it, a write breaks, or the link stops.</summary>
    private async Task ReadAsync(Connection conn)
    {
        var buf = new byte[16 * 1024];
        var lines = new PlayoutLineReader();
        PlayoutLineReader.LineRead onLine = (line, size, whole) => OnLine(conn, line, size, whole);
        try
        {
            while (!conn.End.IsCancellationRequested)
            {
                var n = await conn.Stream.ReadAsync(buf, conn.End.Token);
                if (n == 0) return;
                lines.Feed(buf.AsSpan(0, n), onLine);
                SayAnswers();
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
            // The connection ended: what ended it is said by whoever ended it, and the core hears it once, after this.
        }
    }

    /// <summary>
    /// A line off the wire, judged by the pairing as it stood when the line arrived. A STATE goes to <see cref="TakeState"/> as it came,
    /// whole or in part; a line longer than this link reads that is not a STATE is counted and not read, so no receipt, clock sample or
    /// context is ever taken from part of one.
    /// </summary>
    private void OnLine(Connection conn, ReadOnlySpan<byte> line, long size, bool whole)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_conn, conn) || conn.End.IsCancellationRequested) return;
            var now = Now();
            TakeLine(conn, now, line, size, whole);
            Flush(conn);
            Ended(conn, now);
            TrackAnswers();
            _pub = Publish(now);
        }
    }

    private void TakeLine(Connection conn, PeerNow now, ReadOnlySpan<byte> line, long size, bool whole)
    {
        var paired = conn.Pairing != PlayoutPairing.None;
        if (!whole)
        {
            Interlocked.Increment(ref _oversize);
            _m.Oversize(now);
        }
        var json = line.StartsWith("STATE "u8) ? 6 : !whole && line.StartsWith("OK @st "u8) ? 7 : 0;
        if (json > 0)
        {
            // A state frame: before the pairing it is discarded and counted (13.6.3), the greeting STATE among them.
            _m.Frame(now);
            if (!paired) { SetAside(); return; }
            TakeState(line[json..], size - json, now);
            return;
        }
        if (!whole)
        {
            if (paired) _m.Heard(now);
            return;
        }
        Handle(conn, now, Encoding.UTF8.GetString(line));
    }

    private void Handle(Connection conn, PeerNow now, string line)
    {
        var paired = conn.Pairing != PlayoutPairing.None;
        if (line.StartsWith("NONCE ", StringComparison.Ordinal))
        {
            OnNonce(conn, now, line[6..]);
            return;
        }
        if (line.StartsWith("OK PONG ", StringComparison.Ordinal))
        {
            // 13.10.2: a sample only when it answers one of this link's last pings; the core matches it, and before the pairing it is
            // discarded and counted.
            if (!paired) SetAside();
            var p = line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3 && Ms(p[0]) is { } t1 && Ms(p[1]) is { } t2 && Ms(p[2]) is { } t3) _m.Pong(now, t1, t2, t3, stuck: false);
            else _m.Heard(now);
            return;
        }
        if (line.StartsWith("OK @hi ", StringComparison.Ordinal))
        {
            // The probe's answer: a Show Bus 1.1 desk. It greets with its NONCE before it reads a line, so a 1.1 desk answering the
            // probe without one asks this connection for none. The probe's own pong is no clock sample: it answers no ping of the core's.
            conn.Probing = false;
            conn.Bus11 = true;
            if (paired) _m.Heard(now);
            else SetAside();
            NoNonce(conn, now);
            return;
        }
        // A desk at its connection ceiling says so in words before it closes an extra connection (13.14): Patterns' "ERR busy — …". That
        // is no answer to the probe and no sign of an older desk: its words are shown, nothing goes to it, and the redial backs off.
        if (!paired && !conn.ProofSent && (line == "ERR busy" || line.StartsWith("ERR busy ", StringComparison.Ordinal)))
        {
            conn.Probing = false;
            Explain(conn, LinkHealth.NotFound, $"{_label} at {_host}:{_port} is {Clip(line[4..])}");
            End(conn);
            return;
        }
        // An older desk doesn't know "@hi" and answers with an untagged ERR. Once this link's proof or token went out, an untagged ERR
        // is never taken for the probe's answer.
        if (conn.Probing && !conn.ProofSent && line.StartsWith("ERR", StringComparison.Ordinal) && !line.StartsWith("ERR @", StringComparison.Ordinal))
        {
            conn.Probing = false;   // plain lines from here on
            NoNonce(conn, now);
            return;
        }
        // The desk's answer to this link's proof (or, on this PC, its token): only once one went out, and only to settle.
        var answer = line.StartsWith("OK @pa ", StringComparison.Ordinal) || line.StartsWith("ERR @pa ", StringComparison.Ordinal)
            ? (line[0] == 'O' ? "OK " : "ERR ") + line[(line.IndexOf(' ', 4) + 1)..]
            : line;
        if (answer == "OK paired" || answer.StartsWith("OK paired ", StringComparison.Ordinal))
        {
            if (conn.ProofSent && conn.Pairing != PlayoutPairing.Paired) Settle(conn, now, PlayoutPairing.Paired);
            return;
        }
        if (answer.StartsWith("OK open", StringComparison.Ordinal))
        {
            if (conn.ProofSent && !paired) Settle(conn, now, PlayoutPairing.Open);
            return;
        }
        // A refusal: the proof's own ERR @pa, or — plain AUTH carries no id — an untagged ERR once the token went out. An untagged ERR
        // after the proof answers some other line (an older grammar's HELLO, say) and refuses nothing.
        var refusal = line.StartsWith("ERR @pa ", StringComparison.Ordinal)
            || (conn.PlainAuth && line.StartsWith("ERR", StringComparison.Ordinal) && !line.StartsWith("ERR @", StringComparison.Ordinal));
        if (!paired && conn.ProofSent && refusal)
        {
            Refused(conn, now, answer.Length > 4 ? answer[4..] : "refused");
            return;
        }
        // Receipts: "OK @id …" / "ERR @id …".
        if (line.StartsWith("OK @", StringComparison.Ordinal) || line.StartsWith("ERR @", StringComparison.Ordinal))
        {
            if (!paired)
            {
                SetAside();
                _m.Heard(now);
                return;
            }
            _m.Heard(now);
            var ok = line[0] == 'O';
            var rest = line[(ok ? 4 : 5)..];
            var sp = rest.IndexOf(' ');
            var id = sp < 0 ? rest : rest[..sp];
            var words = sp < 0 ? "" : rest[(sp + 1)..];
            if (id == "ctx")
            {
                if (ok && words.StartsWith('{') && words.Length < PlayoutWire.LineBytes) _context = words;
                return;
            }
            if (id == "st")
            {
                // The STATE this link asked for once it paired; a refusal leaves the link waiting for the desk's next push.
                if (ok && words.StartsWith('{')) TakeState(Encoding.UTF8.GetBytes(words), Encoding.UTF8.GetByteCount(words), now);
                return;
            }
            if (_m.Knows(id) && PeerLink.ReadLine(line) is { Id: { } known, Reading: var reading }) _m.Answer(now, known, reading);
            if (ok) return;
            line = "ERR " + words;
        }
        if (!paired) return;
        _m.Heard(now);
        if (line.StartsWith("ERR", StringComparison.Ordinal))
        {
            var why = line.Length > 4 ? line[4..] : "refused";
            Say(why.Contains("not paired", StringComparison.OrdinalIgnoreCase) || why.Contains("token", StringComparison.OrdinalIgnoreCase)
                ? LinkHealth.Error : _saidHealth, _label + ": " + Clip(why));
        }
    }

    private static long? Ms(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string Clip(string words) => words.Length > 160 ? words[..160] : words;

    private void SetAside() => Interlocked.Increment(ref _setAside);

    /// <summary>
    /// A STATE from the paired peer, pushed or asked for, whole or the first <see cref="PlayoutWire.LineBytes"/> of one
    /// (<paramref name="size"/> its whole size): the newest is the reading. One read in part is amber with its size; its clip is read
    /// when its <c>video</c> lies within that part, and otherwise there is none, never the last one kept. A <c>video</c> of a shape the
    /// reader does not take is amber and gives no clip; a STATE the reader does not read at all is counted, and the reading and the
    /// link stay as they were.
    /// </summary>
    private void TakeState(ReadOnlySpan<byte> json, long size, PeerNow now)
    {
        if (PlayoutStateReader.Read(json, size) is not { } state)
        {
            Interlocked.Increment(ref _malformed);
            return;
        }
        var video = state.Video;
        _reading = new Reading(video, now.Mono);
        var said = (video is null ? $"connected to {_host}:{_port} — no clip on air"
            : $"{video.Title}" + (video.CountsDown ? $": {TimeSpan.FromSeconds(Math.Max(0, video.LengthSeconds - video.PositionSeconds)):mm\\:ss} left" : video.Loops ? " (looping)" : "")) + OpenNote;
        var part = state.Whole ? "" : " · " + PlayoutWire.OversizeWords(state.Bytes) + ", read in part";
        if (state.Unreadable.Contains("video", StringComparer.Ordinal))
            Say(LinkHealth.Starting, $"connected to {_host}:{_port} — its STATE gives the clip on air in a shape this link does not read, so no countdown" + OpenNote + part);
        else if (state.Whole) Say(PairedHealth, said);
        else if (state.HasVideo) Say(LinkHealth.Starting, said + part);
        else Say(LinkHealth.Starting, PlayoutWire.OversizeWords(state.Bytes) + ": its clip on air lies past the part read — no countdown" + OpenNote);
    }

    /// <summary>
    /// The desk asks for a proof. Only a nonce of exactly 32 lowercase hex is proved, once per connection, and only when the token is
    /// one 13.6.8 proves; the core decides the proof and this class writes it. Plain AUTH never goes on a connection that heard a
    /// NONCE, whatever its shape. A NONCE after the link paired changes nothing.
    /// </summary>
    private void OnNonce(Connection conn, PeerNow now, string nonce)
    {
        conn.NonceHeard = true;
        conn.Bus11 = true;
        if (conn.ProofSent || conn.Pairing != PlayoutPairing.None) return;
        if (!PlayoutWire.IsProvableNonce(nonce))
        {
            Advise(conn, $"{_host}:{_port} sent a NONCE that is not 32 lowercase hex — no proof sent; nothing is taken from it until it pairs");
            return;
        }
        if (_token.Length == 0)
        {
            Advise(conn, $"not paired — {_host}:{_port} asks for its pairing token (Settings → Integrations → {_label}); nothing is taken from it until it pairs");
            return;
        }
        if (PlayoutWire.TokenProblem(_token) is { } problem)
        {
            Advise(conn, $"not paired — {problem} (Settings → Integrations → {_label}); no proof sent to {_host}:{_port}");
            return;
        }
        conn.Nonce = nonce;
        _m.Nonce(now, nonce);   // the core answers it with the legacy proof, once a connection (13.6.8)
    }

    /// <summary>
    /// The desk sent no NONCE (by its answer to the probe, within <see cref="NonceWait"/>, or when the probe went unanswered): it asks
    /// this connection for no proof. The token goes in clear only to a desk on this PC (a loopback socket), which answers OK paired or
    /// OK open; off this PC it stays here. A desk that asks for nothing is open once it is known to speak Show Bus 1.1 (it would have
    /// asked for a proof), or at once on this PC when this link holds no token. Off this PC a desk is called older only once it refused
    /// the probe or let it go unanswered; an older desk off this PC never pairs.
    /// </summary>
    private void NoNonce(Connection conn, PeerNow now)
    {
        if (conn.NonceHeard || conn.ProofSent || conn.Pairing != PlayoutPairing.None) return;
        if (_token.Length > 0 && conn.Loopback)
        {
            conn.ProofSent = conn.PlainAuth = true;
            Write(conn, "AUTH " + _token);
            return;
        }
        if (conn.Bus11 || (conn.Loopback && _token.Length == 0))
        {
            Settle(conn, now, PlayoutPairing.Open);
            return;
        }
        if (conn.Probing) return;   // not known yet: the status stays "pairing" until the probe is answered or times out
        Advise(conn, _token.Length > 0
            ? $"{_host}:{_port} is older than Show Bus 1.1 and pairs only by sending its token in clear — update {_label}; the token stays on this PC"
            : $"{_host}:{_port} is older than Show Bus 1.1 and pairs only on its own PC — update {_label}; nothing is taken from it until it pairs");
    }

    /// <summary>
    /// The pairing settles on this connection: the core takes the link up (13.6.8's level 0: <c>OK paired</c>, or a desk that asks no
    /// proof), and commands may go to it. The greeting STATE came before the pairing and was set aside, so a Show Bus 1.1 desk is asked
    /// for its STATE now (its answer carries this link's id); an older desk answers untagged, so it is not asked and its next push is
    /// the first reading.
    /// </summary>
    private void Settle(Connection conn, PeerNow now, PlayoutPairing how)
    {
        if (how == PlayoutPairing.Paired) _m.LineAnswer(now, "OK paired");
        else _m.AsksNoProof(now);
        if (!_m.Follows(now)) return;
        conn.Pairing = how;
        Say(PairedHealth, (how == PlayoutPairing.Paired ? $"paired with {_host}:{_port}" : $"connected to {_host}:{_port}") + OpenNote + " — waiting for its STATE");
        if (conn.Bus11) Write(conn, "@st STATUS");
    }

    /// <summary>
    /// The desk refused this link's proof, or its token on this PC: red with its words, and the token is held
    /// <c>peerlink.hold.refused.ms</c> (13.15.3) — the connection ends, and no proof goes to the desk again until the hold ends or the
    /// token changes, so a wrong token never walks the desk's lockout.
    /// </summary>
    private void Refused(Connection conn, PeerNow now, string words)
    {
        Explain(conn, LinkHealth.Error, _label + ": " + Clip(words));
        _m.Refused(now, "refused", 0);
    }

    /// <summary>Amber while the link holds a token the desk never asked for (it asks for no key); otherwise green.</summary>
    private LinkHealth PairedHealth => _conn is { Pairing: PlayoutPairing.Open } && _token.Length > 0 ? LinkHealth.Starting : LinkHealth.Ok;

    private string OpenNote => _conn is not { Pairing: PlayoutPairing.Open } ? ""
        : _token.Length > 0 ? " · asks for no key — the token stays on this PC" : " · asks this link for no key";

    // ------------------------------------------------------------------ the evaluator

    /// <summary>
    /// Every <see cref="CheckEvery"/>, on the link's clock: the core's evaluator (13.10.3–4: stale, down, its own gap), the desk's NONCE
    /// wait and the probe's, the ping that carries the clock exchange and the CONTEXT ask once paired, the commands whose answer is
    /// overdue, and what every reader takes.
    /// </summary>
    private void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_gate)
        {
            var now = Now();
            var evaluation = _m.Evaluate(now);
            if (evaluation is { PingedAll: true, GapMs: { } gap }) Paused(now, gap);
            if (_conn is { } conn && !conn.End.IsCancellationRequested)
            {
                if (evaluation.Closed) Explain(conn, LinkHealth.Error, $"{_label} stopped answering");
                if (conn.Probing && now.Mono - conn.ConnectedAt > ProbeWait.TotalMilliseconds)
                {
                    conn.Probing = false;   // no answer to the probe: judged as an older desk
                    NoNonce(conn, now);
                }
                if (!conn.NonceJudged && now.Mono - conn.ConnectedAt > NonceWait.TotalMilliseconds)
                {
                    conn.NonceJudged = true;
                    NoNonce(conn, now);   // no NONCE in time: the desk asks this connection for none
                }
                if (conn.Pairing != PlayoutPairing.None && _m.Follows(now))
                {
                    if (evaluation.PingedAll || now.Mono - conn.LastPing >= PingEvery.TotalMilliseconds) Ping(conn, now);
                    if (conn.Bus11 && now.Mono - conn.LastContext >= ContextEvery.TotalMilliseconds)
                    {
                        Write(conn, "@ctx CONTEXT");
                        conn.LastContext = now.Mono;
                    }
                }
                Flush(conn);
                Ended(conn, now);
            }
            TrackAnswers();
            _pub = Publish(now);
        }
        SayAnswers();
    }

    /// <summary>
    /// 13.10.4: the evaluator's runs came further apart than <c>link.selfgap.ms</c> — this PC was paused, asleep or starved. The core
    /// pings at once and calls nothing down for its own pause; the show's log gets one line naming it, at most one each
    /// <see cref="PauseLineEvery"/>, the next naming the pauses it folds in.
    /// </summary>
    private void Paused(PeerNow now, long gapMs)
    {
        if (now.Mono - _pauseSaidAt < PauseLineEvery.TotalMilliseconds)
        {
            _pausesUnsaid++;
            return;
        }
        var more = _pausesUnsaid switch
        {
            0 => "",
            1 => " (and 1 more pause since the last line)",
            var n => $" (and {n.ToString(CultureInfo.InvariantCulture)} more pauses since the last line)",
        };
        _toLog.Add($"{_label} link: this PC paused for {(gapMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s — {_label} pinged at once, "
            + $"and not called down for the pause{more}");
        _pauseSaidAt = now.Mono;
        _pausesUnsaid = 0;
    }

    /// <summary>A ping: its t1 (this PC's wall clock) kept by the core among the last it matches, then written; a desk older than 1.1 gets a bare PING.</summary>
    private void Ping(Connection conn, PeerNow now)
    {
        _m.Ping(now, now.Wall);
        conn.LastPing = now.Mono;
        Write(conn, conn.Bus11 ? "PING " + now.Wall.ToString(CultureInfo.InvariantCulture) : "PING");
    }

    // ------------------------------------------------------------------ writes

    /// <summary>What the core asked to write since the last drain, in order, on <paramref name="conn"/>.</summary>
    private void Flush(Connection conn)
    {
        foreach (var w in _m.Drain())
        {
            switch (w.Verb, w.Credential)
            {
                case ("HELLO", null):
                    Write(conn, "HELLO " + PlayoutWire.HelloName);
                    break;
                case ("AUTH-PROOF", "auth-proof") when conn.Nonce is { } nonce && PlayoutWire.Proof(_token, nonce) is { } proof:
                    conn.ProofSent = true;
                    Write(conn, "@pa AUTH-PROOF " + proof);
                    break;
                case ({ }, null) when w.Detail is { } id && _outbound.Remove(id, out var line):
                    Write(conn, line);
                    break;
                default:
                    // Nothing else is this link's to write at level 0: it offers no LINK and asks no RECEIPTS.
                    break;
            }
        }
    }

    /// <summary>One line on the connection, within the write deadline; a write that breaks ends the connection.</summary>
    private static void Write(Connection conn, string line)
    {
        if (conn.End.IsCancellationRequested) return;
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        try
        {
            conn.Stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException)
        {
            conn.Broke = true;
            End(conn);
        }
    }

    /// <summary>
    /// Ends a connection: its read stops and its socket closes. Asked under the lock, so its reader's continuation runs later on a
    /// thread of its own, never inside the step that ended it.
    /// </summary>
    private static void End(Connection conn) => _ = conn.End.CancelAsync();

    /// <summary>
    /// The core ended the connection (a refusal, a deadline, a link gone down) or a write broke it: the socket closes, and the core hears
    /// it once. A broken write is said as the core hears it, so the link is never down under the live link's words.
    /// </summary>
    private void Ended(Connection conn, PeerNow now)
    {
        if (!_m.KeepsConnection(now)) End(conn);
        else if (conn.End.IsCancellationRequested)
        {
            _m.Closed(now);
            if (conn.Broke && !conn.EndExplained) Explain(conn, LinkHealth.NotFound, $"{_label}: the connection to {_host}:{_port} broke");
        }
    }

    // ------------------------------------------------------------------ outcomes and what readers take

    private void Track(string id, string line)
    {
        _tracked[id] = new Tracked(line, Interlocked.Read(ref _ids.Value));
        if (_tracked.Count <= PeerLink.ReceiptsKept) return;
        foreach (var old in _tracked.OrderBy(t => t.Value.Order).Take(_tracked.Count - PeerLink.ReceiptsKept).Select(t => t.Key).ToList())
            _tracked.Remove(old);
    }

    /// <summary>Each written command whose outcome came or moved forward since the last look, to be said once outside the lock.</summary>
    private void TrackAnswers()
    {
        foreach (var (id, t) in _tracked.ToList())
        {
            var outcome = _m.OutcomeOf(id);
            if (outcome is { } o && o != t.Said)
            {
                t.Said = o;
                _toSay.Add(new PlayoutAnswer(id, t.Line, o, _m.WordsOf(id)));
            }
            // Applied and refused are final; accepted and unconfirmed can still move, until the core no longer holds the command.
            if (outcome is PeerOutcome.Applied or PeerOutcome.Refused or PeerOutcome.NotSent || !_m.Knows(id)) _tracked.Remove(id);
        }
    }

    /// <summary>What the link has to tell its listeners — outcomes and lines for the log — told outside its lock, in order.</summary>
    private void SayAnswers()
    {
        List<PlayoutAnswer> said;
        List<string> logged;
        lock (_gate)
        {
            if (_toSay.Count == 0 && _toLog.Count == 0) return;
            said = [.. _toSay];
            logged = [.. _toLog];
            _toSay.Clear();
            _toLog.Clear();
        }
        foreach (var a in said) Answered?.Invoke(a);
        foreach (var line in logged) Logged?.Invoke(line);
    }

    private void Say(LinkHealth health, string words)
    {
        _saidHealth = health;
        _said = words;
    }

    /// <summary>Why <paramref name="conn"/> ended, said as it ends: what the link shows while it is down.</summary>
    private void Explain(Connection conn, LinkHealth health, string words)
    {
        Say(health, words);
        conn.EndExplained = true;
    }

    /// <summary>Why this desk does not pair (amber): kept, should the connection end unpaired, while the link redials.</summary>
    private void Advise(Connection conn, string words)
    {
        Say(LinkHealth.Starting, words);
        conn.Advised = true;
    }

    /// <summary>
    /// What every reader takes now: the core's record and state laid over what the link says — live as its pairing and its readings
    /// say, stale amber with the silence (no countdown), down with what ended it and the redial's wait, held red with why and when it
    /// tries again, refused red in the core's words. A link that is not live is never green (<see cref="HealthOf"/>): should nothing
    /// have said why it is down, the core's words stand in for the live link's.
    /// </summary>
    private Published Publish(PeerNow now)
    {
        var record = _m.Record(now);
        var state = _m.State(now);
        string Seconds(long? ms) => ((ms ?? 0) / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s";
        var live = _saidHealth == LinkHealth.Ok;
        var status = state switch
        {
            PeerLinkState.Live => _said,
            PeerLinkState.Stale => $"{_label} at {_host}:{_port} quiet for {Seconds(record.HeardMs)} — no countdown until it is heard again",
            PeerLinkState.Down when live => record.Words,
            PeerLinkState.Down => _said + " — dialling again in " + Seconds(record.NextMs),
            PeerLinkState.Held when _saidHealth == LinkHealth.Error => _said + " — the token is held; next try in " + Seconds(record.NextMs),
            PeerLinkState.Held or PeerLinkState.Refused => record.Words,
            _ when live => record.Words,
            _ => _said,
        };
        return new Published(HealthOf(state, _saidHealth), status, state, _conn?.Pairing ?? PlayoutPairing.None, _conn is not null, record,
            _m.Clock.Believed, _m.Clock.Apart, _reading, _context);
    }

    /// <summary>
    /// Pure: the light a link in <paramref name="state"/> shows, <paramref name="said"/> being what its pairing and readings last said.
    /// Green only while it is live; stale is amber; held and refused are red; down, connecting or finding keep what was said of why,
    /// and what was said of a live link is never theirs.
    /// </summary>
    public static LinkHealth HealthOf(PeerLinkState state, LinkHealth said) => state switch
    {
        PeerLinkState.Live => said,
        PeerLinkState.Stale => LinkHealth.Starting,
        PeerLinkState.Held or PeerLinkState.Refused => LinkHealth.Error,
        PeerLinkState.Down when said == LinkHealth.Ok => LinkHealth.NotFound,
        _ when said == LinkHealth.Ok => LinkHealth.Starting,
        _ => said,
    };

    // ------------------------------------------------------------------ time

    /// <summary>
    /// Now on the link's clock: its monotonic ms since the link was made and its wall clock's Unix ms, read together. A read whose two
    /// monotonic ends lie within <see cref="WallReadMs"/> gives this PC's wall clock less its monotonic one; when that moved more than
    /// <see cref="WallStepMs"/> since the last such read, the wall clock stepped, and the clock's window is emptied (13.11.7).
    /// </summary>
    private PeerNow Now()
    {
        var before = MonoMs();
        var wall = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var mono = MonoMs();
        var now = new PeerNow(mono, wall);
        if (mono - before <= WallReadMs)
        {
            var skew = wall - mono;
            if (_skew is { } last && Math.Abs(skew - last) > WallStepMs) _m.WallStepped(now);
            _skew = skew;
        }
        return now;
    }

    private long MonoMs() => (long)_time.GetElapsedTime(_t0).TotalMilliseconds;

    /// <summary>Whether a host Settings names is this PC by its address (13.1's local), as the seam judges an address.</summary>
    private static bool IsLocal(string host, Func<IPAddress, bool> isLoopback)
        => IPAddress.TryParse(host, out var ip) ? isLoopback(ip) : host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && isLoopback(IPAddress.Loopback);

    /// <summary>Pure: the default local-address seam — the socket's remote address is loopback (IPv4-mapped included).</summary>
    public static bool IsLoopbackAddress(IPAddress address)
        => IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;   // safe to call twice (a retiring link may be stopped from two places)
        _stop.Cancel();
        _tick?.Dispose();
        lock (_gate)
        {
            if (_conn is { } conn) End(conn);
        }
        try
        {
            _run?.Wait(TimeSpan.FromMilliseconds(1500));
        }
        catch (AggregateException)
        {
            // The loop ended on its own fault: there is nothing left to stop.
        }
        lock (_gate)
        {
            var now = Now();
            if (_m.KeepsConnection(now)) _m.Closed(now);
            TrackAnswers();
            _pub = Publish(now);
        }
        SayAnswers();
        // Its token source goes only with the loop that reads it: a loop still ending keeps it until the process does.
        if (_run is null || _run.IsCompleted) _stop.Dispose();
    }
}
