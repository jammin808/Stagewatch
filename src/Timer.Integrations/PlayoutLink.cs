using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
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
    /// The peer's kind as Settings set it: <see cref="PlayoutRules.Auto"/> (its STATE says), <see cref="PlayoutRules.Patterns"/> or
    /// <see cref="PlayoutRules.PlayMap"/>. A peer whose STATE says another kind than one set here is told apart, and sent nothing.
    /// </summary>
    public string Kind { get; init; } = PlayoutRules.Auto;

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
    /// What the link starts from (13.15.3): a pin, a floor, and a hold with its end in wall time; null for none. A pin a person or a
    /// proof made, or a floor that asks for TLS, is never dialled plain: with no TLS door heard such a link dials nothing and says so.
    /// </summary>
    public PeerKept? Kept { get; init; }

    /// <summary>
    /// Keeps what the link holds across a restart (13.15.3) each time it changes — a hold the peer's refusal starts, and its end — in
    /// the step that changed it, before the link's next write or dial. Asked under the link's lock, so it is cheap and never waits on
    /// a disk: it takes the change at once and writes it on a lane of its own (<see cref="PlayoutKeptStore"/>), its task completing
    /// false should the write fail. The core's own hold blocks every proof and dial until it ends, so the write has the hold's whole
    /// length to land. Null keeps nothing. It is given a <see cref="PeerKept"/>, which carries no token, proof or nonce.
    /// </summary>
    public Func<PeerKept, Task<bool>>? Keep { get; init; }

    /// <summary>The connection's bytes as the link reads and writes them; null for the socket's own. A test holds a write with it.</summary>
    public Func<Stream, Stream>? Wrap { get; init; }

    /// <summary>
    /// How the link reaches its peer, as Settings set it: <see cref="PlayoutRules.Auto"/> dials a PlayMap node over TLS
    /// (<c>playmap.lines.v1</c> on its <c>https</c> door) where the node's hello names that door, and its line wire plain where it
    /// names none, until its certificate is pinned; <see cref="PlayoutRules.Tcp"/> the line wire alone; <see cref="PlayoutRules.Wss"/>
    /// TLS alone. Once a pin or a floor asks for TLS, nothing is dialled plain, whatever this says.
    /// </summary>
    public string Transport { get; init; } = PlayoutRules.Auto;

    /// <summary>The Show Bus id of the node this link is for (Settings' <c>node</c>; null or "" while none is chosen): its pin's id.</summary>
    public string? NodeId { get; init; }

    /// <summary>
    /// The hello the show network heard for this link's node (§2): by its id when one is set, else at the link's address. Asked as
    /// the link dials and on its checks, on the link's own threads, outside its lock; null hears none. A hello only raises the link's
    /// guard (13.4.2): a TLS door to try, never a pin, never plain. One under §2's conflict mark holds the node: no new dial (13.4.4).
    /// </summary>
    public Func<PlayoutHeard?>? Hello { get; init; }

    /// <summary>
    /// The TLS door kept beside the pin from an earlier run (0 for none): where the pinned node's certificate last answered. A pinned
    /// link dials it while no fresh hello names a door (13.4.2: the pin alone decides, so a lost, late or forged hello never makes it
    /// plain).
    /// </summary>
    public int Door { get; init; }

    /// <summary>
    /// Keeps the TLS door beside the pin each time it changes, in the step that changed it, after <see cref="Keep"/> kept the pin. Asked
    /// under the link's lock, so it is cheap and never waits on a disk (<see cref="PlayoutKeptStore.KeepDoor"/>). Null keeps none.
    /// </summary>
    public Action<int>? KeepDoor { get; init; }
}

/// <summary>
/// Where the pairing stands on the link's connection up now: facts — STATE, receipts, clock samples, context — are taken only once
/// it is <see cref="Paired"/> (the peer said <c>OK paired</c> to this link's proof, or to its token on this PC) or <see cref="Open"/>
/// (the peer asks this link for no proof); before that they are set aside and counted.
/// </summary>
public enum PlayoutPairing { None, Paired, Open }

/// <summary>
/// A command's outcome as the link knows it now (13.12.2): its request id, the line it was (without its id), its outcome by 13.12.3's
/// table and the words that go with it, the outcome it moved on from (null for its first), when the link read it, on the link's
/// clock, the mark its asker gave it (the desk: the running-order item it was asked for; -1 for none), and how many STATEs the link had
/// read when it read this answer (<see cref="PlayoutReading.Number"/>): a reading numbered past it was read after it. An outcome only
/// moves forward; each move is said once.
/// </summary>
public readonly record struct PlayoutAnswer(string Id, string Line, PeerOutcome Outcome, string Words, PeerOutcome? Before = null, DateTimeOffset At = default,
    int Mark = -1, long Read = 0)
{
    /// <summary>
    /// Pure: the show log's row for this outcome (13.12.10), the peer named by <paramref name="label"/> — one when the outcome of a
    /// command that reached the socket first settles, and one more only when an unconfirmed one is resolved (answered after all); null
    /// for any other move (accepted → applied or refused, accepted → unconfirmed), which the status line alone says, and for a command
    /// that never reached the socket.
    /// </summary>
    public string? JournalLine(string label) => Outcome == PeerOutcome.NotSent ? null
        : Before is null ? StatusLine(label)
        : Before == PeerOutcome.Unconfirmed && Outcome is PeerOutcome.Applied or PeerOutcome.Refused ? StatusLine(label) + " — it was unconfirmed until then"
        : null;

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
/// A change of a playout link's state (13.16.1), as the show log keeps it (13.16.7, 13.19.4): when, from, to, the link's words for why,
/// 13.15.1's attempt, the peer's address (host:port) and its node id when the link knows one. <see cref="Folded"/> above 0 is the one
/// summary that stands for that many changes, each a repeat of a change said within <see cref="PlayoutLink.RepeatSummaryEvery"/>, over
/// <see cref="FoldedMs"/> — the last of them is the one it names.
/// </summary>
public readonly record struct PlayoutLinkChange(DateTimeOffset At, PeerLinkState From, PeerLinkState To, string Reason, int Attempt, int Folded = 0, long FoldedMs = 0,
    string Address = "", string? NodeId = null)
{
    /// <summary>
    /// Pure: the show log's row for this change, the peer named by <paramref name="label"/> (its kind) with its id when known and its
    /// address; it carries no token, proof or nonce.
    /// </summary>
    public string Line(string label)
    {
        var attempt = Attempt.ToString(CultureInfo.InvariantCulture);
        var peer = label + (NodeId is { Length: > 0 } id ? " " + id : "") + " link" + (Address.Length > 0 ? " (" + Address + ")" : "");
        if (Folded == 0) return $"{peer}: {From.Word()} → {To.Word()} — {Reason} (attempt {attempt})";
        var many = Folded == 1 ? "1 more change" : Folded.ToString(CultureInfo.InvariantCulture) + " more changes";
        return $"{peer}: {many} of state in {(FoldedMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s, each one said before — "
            + $"the last {From.Word()} → {To.Word()} — {Reason} (attempt {attempt})";
    }
}

/// <summary>
/// A command the link took for its peer (13.12.2): its request id, the line it is (without its id), and its outcomes as they come —
/// <see cref="First"/> the first the link reads (applied, accepted, refused, unconfirmed or not-sent), <see cref="Final"/> the first
/// that is not accepted. Both complete outside the link's lock, their continuations never inline: a task completed by the link's own
/// thread never runs its waiter's code there.
/// </summary>
public sealed record PlayoutTicket(string Id, string Line, Task<PlayoutAnswer> First, Task<PlayoutAnswer> Final)
{
    /// <summary>The <c>n</c> of the request id <c>s&lt;n&gt;-&lt;inst&gt;</c>, the number a receipt's <c>refs.peerRequest</c> names.</summary>
    public long Number => Id.Length > 1 && Id.IndexOf('-') is var dash and > 1
        && long.TryParse(Id.AsSpan(1, dash - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
}

/// <summary>What asking the link for a command gave: its ticket, on its way, or why nothing was sent.</summary>
public readonly record struct PlayoutSent(PlayoutTicket? Ticket, string? Error);

/// <summary>
/// A certificate of a playout link's peer that waits for a person on this desk (Show Bus 13.8.2–3): its fingerprint (64 hex), and
/// whether it is a changed one — CERTIFICATE CHANGED, nothing sent until a person types at least
/// <see cref="PlayoutRules.TrustHexMin"/> hex of it, never filled in for them — or the one pinned first, for comparison only, which a
/// person trusts with one press beside its short form. <see cref="Pinned"/> is the pin a changed one would replace (64 hex), else "";
/// <see cref="NodeId"/> the node the pin is for, when known. It carries no secret: a fingerprint is public.
/// </summary>
public sealed record PlayoutTrustWait(string Fingerprint, bool Changed, string Pinned, string? NodeId);

/// <summary>
/// A PlayMap node's answer to <c>WHOAMI</c>: whether it holds this link paired, its role for it and whether its show is armed, and the
/// verbs it may run now (null = every one, PlayMap's <c>"*"</c>). <see cref="MayDrive"/>: a playlist's GO or a clip's transport is
/// among them.
/// </summary>
public sealed record PlayoutWhoami(bool Paired, string Role, bool Armed, IReadOnlyList<string>? May)
{
    public bool MayDrive => May is null || May.Any(v => v.StartsWith("PLAYLIST GO", StringComparison.Ordinal) || v.StartsWith("VIDEO ", StringComparison.Ordinal));
}

/// <summary>
/// The peer's STATE as the link last read it, on the connection up now, with its age on the link's clock and its number: how many
/// STATEs the link has read in its life, this one the last — a STATE read after an answer is numbered past that answer's
/// <see cref="PlayoutAnswer.Read"/>.
/// </summary>
public sealed record PlayoutReading(PlayoutPeerState State, long AgeMs, long Number = 0);

/// <summary>
/// A playout peer — a Patterns desk, or a PlayMap node's line wire — over its TCP line protocol, or a PlayMap node's over TLS
/// (<c>playmap.lines.v1</c> on its <c>https</c> door, a line a message), as a level-0 connector of Show Bus 1.3 §13 on Stagewatch's
/// PeerLink core. TLS is dialled where the node's hello names that door (a hello only raises the guard, 13.4.2), or always when Settings
/// say <c>wss</c>; its certificate is checked by fingerprint alone, pinned only <c>first</c> once the link pairs over it (13.8.2: at
/// level 0 nothing proves the node), and pinned for its node id only when a person on this desk trusts it (<see cref="Trust"/>). Once
/// a person's pin (or a floor) asks for TLS, the link is never dialled plain: with no TLS door heard it says so and dials nothing. A
/// pinned node showing another certificate is CERTIFICATE CHANGED and sent nothing — no HTTP request, line or proof — until a person
/// types at least 8 hex of the new one (13.8.3). A 429 at the door is a lockout and holds the token; a close 4401 is a wrong token and
/// holds it. <see cref="PeerLinkConnector"/> decides — the dial and its jittered redial, liveness
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
/// <c>STOP</c>, <c>SEEK n</c>, each naming its clip <c>ON &lt;key&gt;</c> where the asker gives one; to a peer heard to be PlayMap
/// <c>PLAYLIST GO</c> and <c>PLAYLIST CUE</c>, to one heard to be Patterns <c>CUE GO</c> and <c>CUE STANDBY</c> — each with a ticket
/// its asker waits on (<see cref="PlayoutTicket"/>), never a raw line, and never a bare <c>STOP</c>, which on Patterns closes every
/// output. The peer's kind is what its STATE says (a whole STATE with no <c>t</c> is Patterns, <c>"t":"playmap"</c> PlayMap); a
/// peer that answers as another kind than Settings set is sent nothing. Once paired it is asked for its STATE and its context at once,
/// its context every 5 s, and a PlayMap node its <c>WHOAMI</c> every 5 s: a node that lets this link only read is amber. One writer:
/// every step of the core runs under one lock, on its time read inside that lock, and what readers take — the light and words, the
/// record, the clock, the reading and the context — is one snapshot published after each step; a link that is not live is never green.
/// Nothing touches the socket under that lock or on a caller's thread: each connection writes its lines in order on a lane of its own,
/// so the show thread asking for a command and the evaluator never wait on a write; a command still on the lane when its connection
/// ends is not-sent, one written unconfirmed, one the core settled while it waited there is never written, and a write that has not
/// finished within <see cref="WriteDeadline"/> ends the connection. A hold the peer's refusal starts is handed with its end to its
/// keeper (<see cref="PlayoutLinkOptions.Keep"/>) in the step that started it, before the link writes or dials again — the keeper's
/// own lane puts it on disk, never this link's lock — so a restart keeps it. Each change of the link's state is one row for the show
/// log (<see cref="Changed"/>), a repeat of one said within <see cref="RepeatSummaryEvery"/> folded into one summary row each that
/// long, never a row per beat; peer words reach no row with the token in them. Listeners hear the link in the order it spoke.
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
    /// <summary>How often a running link asks the show network for its node's hello: as often as a hello comes (§2).</summary>
    public static readonly TimeSpan HelloEvery = SlidesBeacon.Every;
    /// <summary>How long to wait for a desk's NONCE before judging it asks for none (desks older than Show Bus 1.1 never send one).</summary>
    public static readonly TimeSpan NonceWait = TimeSpan.FromMilliseconds(1500);
    /// <summary>How long the <c>@hi PING</c> probe waits for its answer before the desk is judged older than Show Bus 1.1.</summary>
    public static readonly TimeSpan ProbeWait = TimeSpan.FromSeconds(3);
    /// <summary>
    /// One write's deadline (§14 <c>link.write.deadline.max.ms</c>), on the link's clock: the evaluator ends a connection whose lane has
    /// been writing one line longer than this.
    /// </summary>
    public static readonly TimeSpan WriteDeadline = TimeSpan.FromSeconds(10);
    /// <summary>
    /// The most lines a connection's lane holds unwritten: at most <c>link.inflight.max</c> commands, the link's own pings and asks
    /// between two writes, and its greeting. A lane past it ends its connection — a line is never dropped, coalesced or left behind.
    /// </summary>
    public const int LaneMax = 64;
    /// <summary>
    /// 13.19.4's <c>log.repeat.summary.ms</c>: a change of state said within this long before is a repeat, and repeats go to the show
    /// log as one summary row this often, never a row each.
    /// </summary>
    public static readonly TimeSpan RepeatSummaryEvery = TimeSpan.FromMinutes(1);
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
    private readonly string _name;
    private readonly string _setKind;
    private readonly string _transport;
    /// <summary>The host Settings name is this PC by its address (13.1's local): its link may stay on the plain line wire.</summary>
    private readonly bool _local;
    private readonly StrongBox<long> _ids;
    private readonly Lock _gate = new();
    /// <summary>
    /// Held while a batch is taken from under <see cref="_gate"/> and raised, so the batches reach listeners in the order they were
    /// made, whichever threads take them. Never taken under <see cref="_gate"/>.
    /// </summary>
    private readonly Lock _telling = new();
    private readonly PeerLinkConnector _m;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, string> _outbound = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal);
    private readonly List<PlayoutAnswer> _toSay = [];
    private readonly List<PlayoutLinkChange> _toChange = [];
    private readonly List<string> _toLog = [];
    // Each command's ticket by its request id, until its final outcome is said; and the outcomes to hand them, outside the lock.
    private readonly Dictionary<string, Pending> _tickets = new(StringComparer.Ordinal);
    private readonly List<(TaskCompletionSource<PlayoutAnswer> Tcs, PlayoutAnswer Answer)> _toComplete = [];

    /// <summary>A ticket's two outcomes still to hand out, and whether its first was handed (under the lock).</summary>
    private sealed class Pending
    {
        public readonly TaskCompletionSource<PlayoutAnswer> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<PlayoutAnswer> Final = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FirstSaid;
    }
    private Task? _run;
    private ITimer? _tick;
    private Connection? _conn;
    private int _started, _disposed;

    // What the link says, set by its pairing and its readings (under the lock); the machine's state is laid over it in Publish.
    private LinkHealth _saidHealth = LinkHealth.Starting;
    private string _said;

    // The newest reading of the peer's STATE and its context, on the connection up now (under the lock): published with the rest.
    private sealed record StateRead(PlayoutPeerState State, long AtMs, long Number)
    {
        public PlayoutVideo? Video => State.Video;
    }
    private StateRead? _reading;
    // How many STATEs the link has read in its life (under the lock): each reading and each answer carries the count it was read at.
    private long _readings;
    private string? _context;
    private string? _contextNode;
    // The kind the peer's STATE says (13.4: its t, Patterns' none) once one was read, kept across a redial to the same address: a
    // peer's kind is what it said last, never guessed from the port. And PlayMap's WHOAMI answer, on the connection up now.
    private string? _kindSeen;
    private PlayoutWhoami? _whoami;
    // What the peer lists of itself for running-order cues (its INDEX or CUE LIST), as last read: kept across a redial and asked again
    // when its STATE's revision moves; dropped when the peer's kind changes. And the connections made, counted.
    private PlayoutIndex? _index;
    private int _serials;

    // This PC's wall clock less its monotonic one at the last read a pause did not split (13.11.7), and the evaluator's own pauses
    // (13.10.4): when the last line about one went out, and how many came since.
    private long? _skew;
    private long _pauseSaidAt = long.MinValue / 2;
    private int _pausesUnsaid;

    // What the link last kept across a restart (13.15.3), and whether keeping it failed and was said.
    private PeerKept _kept;
    private bool _keepFailed;

    // The TLS dial out now, counted (under the lock): a certificate shown to an older one, whose deadline ended it, takes nothing. The
    // TLS door the last hello named for the node, and when the hello was last asked (monotonic ms). Whether the link's last dial
    // made none, for want of a TLS door it may take: its words then say it looks again, not that it dials.
    private int _attempt;
    private long _helloAskedAt = long.MinValue / 2;
    private bool _noDoor;
    // The TLS dial whose certificate the core judged and took (under the lock): a TLS connection from any other is never taken up.
    private int _certFor;
    // The TLS door kept beside the pin — where its certificate last answered once the link paired — and the one last handed to the
    // keeper (under the lock).
    private int _door, _keptDoor;
    /// <summary>The node Settings set this link to (a valid id), or null: a STATE naming another is sent nothing (13.4.3).</summary>
    private readonly string? _meant;

    // The state the show log last heard of (13.16.7), when each change was last said, and the repeats not said apart (13.19.4).
    private PeerLinkState _logged = PeerLinkState.Off;
    private readonly Dictionary<(PeerLinkState From, PeerLinkState To), long> _changeSaid = [];
    private readonly HashSet<(PeerLinkState From, PeerLinkState To)> _repeated = [];
    private PlayoutLinkChange? _repeatLast;
    private long? _repeatSince;
    private int _repeats;

    private volatile Published _pub;
    private int _malformed, _oversize, _setAside;

    /// <summary>A command the link wrote, its line without its id, its asker's mark and the last outcome it said.</summary>
    private sealed class Tracked(string line, long order, int mark)
    {
        public readonly string Line = line;
        public readonly long Order = order;
        public readonly int Mark = mark;
        public PeerOutcome? Said;
        public PlayoutAnswer? Last;
    }

    /// <summary>
    /// What every reader takes, published whole after each step of the core: the light and words, the core's state and record, the
    /// clock, and the reading and context of the connection up now — one snapshot, so no reader sees a reading beside an older light.
    /// </summary>
    private sealed record Published(LinkHealth Health, string Status, PeerLinkState State, PlayoutPairing Pairing, bool Connected,
        PeerLinkRecord Record, PeerClock.Belief? Clock, bool ClockApart, StateRead? Reading, string? Context, string? Kind, string? NodeId,
        PlayoutWhoami? Whoami, string? ContextNode, PlayoutTrustWait? TrustWait, bool Tls, PlayoutIndex? Index, int Connection);

    /// <summary>
    /// A line for a connection's lane: its text, the request id of the command it is, or a ping whose t1 the lane stamps as it writes it.
    /// </summary>
    private sealed record Outgoing(string? Line, string? Id = null, bool Ping = false);

    /// <summary>
    /// One connection: its socket (a TCP client, or the line channel over TLS), its writer lane and what its pairing has seen (under
    /// the lock). <see cref="Lines"/> is the TLS line channel's, whose close code the link reads as it ends; null on TCP.
    /// </summary>
    private sealed class Connection(IDisposable socket, Stream stream, bool loopback, CancellationToken stop, PlayoutLinesStream? lines = null, int door = 0,
        int attempt = 0) : IDisposable
    {
        public readonly IDisposable Socket = socket;
        public readonly Stream Stream = stream;
        /// <summary>The line channel over TLS (<c>playmap.lines.v1</c>), or null for the TCP line wire.</summary>
        public readonly PlayoutLinesStream? Lines = lines;
        /// <summary>The TLS door it was dialled at (0 on TCP).</summary>
        public readonly int Door = door;
        /// <summary>The TLS dial it came from (0 on TCP): its certificate was judged by the core only when that dial's step ran.</summary>
        public readonly int Attempt = attempt;
        public bool Tls => Lines is not null;
        /// <summary>Its certificate was looked at for a <c>first</c> pin, at its first whole STATE once it paired.</summary>
        public bool PinLooked;
        /// <summary>The lines to write, in the order they were asked for; one lane writes them, outside the link's lock.</summary>
        public readonly Channel<Outgoing> Lane = Channel.CreateBounded<Outgoing>(new BoundedChannelOptions(LaneMax) { SingleReader = true });
        /// <summary>The lane writing this connection's lines; null until the connection greets.</summary>
        public Task? Writer;
        /// <summary>When the lane began the write it is in (monotonic ms), or null between writes: the evaluator's write deadline.</summary>
        public long? WritingSince;
        /// <summary>The socket's remote address is this PC, as the seam judged it when it connected.</summary>
        public readonly bool Loopback = loopback;
        /// <summary>Cancelled when the connection ends: the core ended it, a write broke, or the link stops.</summary>
        public readonly CancellationTokenSource End = CancellationTokenSource.CreateLinkedTokenSource(stop);
        public long ConnectedAt;
        /// <summary>Which of the link's connections this is, counted from 1 as each comes up: a send held for one never goes on another.</summary>
        public int Serial;
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
        public long LastPing = long.MinValue / 2, LastContext = long.MinValue / 2, LastWhoami = long.MinValue / 2;
        /// <summary>When the peer's list (INDEX, CUE LIST) was last asked on this connection, and for which revision ("" for none said).</summary>
        public long LastList = long.MinValue / 2;
        public string? ListAskedFor;
        /// <summary>The peer refused the list's verb on this connection (an older peer): it is not asked again until the link dials again.</summary>
        public bool ListRefused;

        public void Dispose()
        {
            End.Cancel();
            Lane.Writer.TryComplete();
            Stream.Dispose();
            Socket.Dispose();
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
        _name = options.Name;
        _label = PlayoutRules.Label(options.Name);
        _setKind = options.Kind is PlayoutRules.Patterns or PlayoutRules.PlayMap ? options.Kind : PlayoutRules.Auto;
        // A transport word this link does not know is the strictest, as Settings reads one: TLS alone, never plain.
        _transport = options.Transport is PlayoutRules.Auto or PlayoutRules.Tcp ? options.Transport : PlayoutRules.Wss;
        // An id this link would make is checked now, so an inst of another shape fails here and not at the first command.
        _ = PeerLink.RequestId(IdPrefix, 1, options.Inst);
        _ids = Counters.GetOrAdd(IdPrefix + "-" + options.Inst, _ => new StrongBox<long>());
        _said = $"connecting to {_host}:{_port}";
        _meant = options.NodeId is { Length: > 0 } meant && ShowBusHello.ValidId(meant) ? meant : null;
        _door = _keptDoor = options.Door is > 0 and < 65536 ? options.Door : 0;
        var settings = new PeerLinkSettings
        {
            Carrier = PeerCarrier.Line,
            Name = _label,
            // The node the operator chose: its pin is kept for that id, and a hello for another id raises nothing here.
            NodeId = _meant,
            Kind = _setKind == PlayoutRules.Auto ? null : _setKind,
            Address = _host + ":" + _port.ToString(CultureInfo.InvariantCulture),
            // The token is held as 13.6.8's code: proved only by the legacy proof, and only one 1.1 desks key alike.
            Key = PlayoutWire.TokenProblem(_token) is null ? "code" : null,
            Local = _local = IsLocal(_host, options.IsLoopback),
            ShowNeeds = true,
            IdPrefix = IdPrefix,
            Inst = options.Inst,
            OffersLink = false,
            // A link set to wss dials TLS every time; otherwise TLS when a hello names the node's door, or a pin or floor asks.
            TriesTls = _transport == PlayoutRules.Wss,
        };
        lock (_gate)
        {
            var now = Now();
            _m = new PeerLinkConnector(settings, options.Kept, now, options.Jitter);
            _kept = _m.Kept;
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

    /// <summary>The peer's context (Show Bus §6 JSON), asked for once the link pairs and every 5 s after from a 1.1 peer, or null.</summary>
    public string? Context => _pub is { Pairing: not PlayoutPairing.None } pub ? pub.Context : null;

    /// <summary>
    /// Why nothing goes to the peer and nothing of it feeds a reader now: its STATE says another kind than the one its settings name,
    /// a kind this Stagewatch does not speak, or another node than the one its settings name (13.4.3) — in words; null while it says
    /// none or the right one.
    /// </summary>
    public string? WrongKind => Problem(_pub.Reading?.State);

    /// <summary>
    /// The peer's context as the show network and a proposal's risk may take it for the node it names (Show Bus §6, §11: a context read
    /// over this Stagewatch's own link to the address the operator set): <see cref="Context"/>, but null while the peer answers as
    /// another kind than its settings name, and null when its STATE names its node (PlayMap's <c>id</c>) and its context names another
    /// — a context speaks only for the id the same link reads from its STATE.
    /// </summary>
    public string? PulledContext
    {
        get
        {
            var pub = _pub;
            if (pub.Pairing == PlayoutPairing.None || pub.Context is null || Problem(pub.Reading?.State) is not null) return null;
            return pub.Reading?.State.Id is { Length: > 0 } stateId && pub.ContextNode != stateId ? null : pub.Context;
        }
    }

    /// <summary>The link's name as Settings keeps it ("patterns", "stage-left"): its context ids and the wire's verbs name it.</summary>
    public string LinkName => _name;

    /// <summary>The peer's host as the link dials it ("127.0.0.1" for a blank one).</summary>
    public string Host => _host;

    /// <summary>
    /// The peer's kind as its STATE said it last — <see cref="PlayoutRules.Patterns"/> (a whole STATE with no <c>t</c>) or
    /// <see cref="PlayoutRules.PlayMap"/> (<c>"t":"playmap"</c>) — or null: no STATE read yet, or one that names another kind.
    /// </summary>
    public string? Kind => _pub.Kind;

    /// <summary>The kind Settings set for the peer: <see cref="PlayoutRules.Auto"/>, <see cref="PlayoutRules.Patterns"/> or <see cref="PlayoutRules.PlayMap"/>.</summary>
    public string SetKind => _setKind;

    /// <summary>How people read the peer (<see cref="PlayoutRules.Peer"/>): "PlayMap Stage left" once its kind is known or set.</summary>
    public string Peer => PlayoutRules.Peer(Kind ?? (_setKind == PlayoutRules.Auto ? null : _setKind), _name);

    /// <summary>The peer's Show Bus id: its STATE's (PlayMap's <c>id</c>), else its context's <c>node.id</c>; null while neither said one.</summary>
    public string? NodeId => _pub.NodeId;

    /// <summary>A PlayMap node's answer to <c>WHOAMI</c> on the connection up now, asked once its kind is known and every 5 s; null otherwise.</summary>
    public PlayoutWhoami? Whoami => _pub is { Pairing: not PlayoutPairing.None } pub ? pub.Whoami : null;

    /// <summary>
    /// The peer's STATE as last read while the link is paired, with its age on the link's clock — every section it read (its clip,
    /// playlists, sync, cue stack), whole or in part (13.14) — or null: not paired, or no STATE read on the connection up now.
    /// </summary>
    public PlayoutReading? Reading
    {
        get
        {
            var pub = _pub;
            return pub.Reading is { } r && pub.Pairing != PlayoutPairing.None ? new PlayoutReading(r.State, MonoMs() - r.AtMs, r.Number) : null;
        }
    }

    /// <summary>
    /// A command's outcome came, or moved forward (13.12.2). Raised outside the link's lock, on its own threads or the asker's, in the
    /// order the link made them — as are <see cref="Changed"/> and <see cref="Logged"/>, one order for the three.
    /// </summary>
    public event Action<PlayoutAnswer>? Answered;

    /// <summary>
    /// A line for the show's log, raised on the link's own threads outside its lock: the evaluator's own pause (13.10.4) — at most one
    /// line each <see cref="PauseLineEvery"/>, naming how many pauses it folds in — and a hold that could not be kept across a restart.
    /// </summary>
    public event Action<string>? Logged;

    /// <summary>
    /// The link's state changed (13.16.7), raised on the link's own threads outside its lock: one for each change, the link's start
    /// among them (from off), and for repeats one summary each <see cref="RepeatSummaryEvery"/> (13.19.4) — never one per beat.
    /// </summary>
    public event Action<PlayoutLinkChange>? Changed;

    /// <summary>What the link keeps across a restart at this moment (13.15.3): its hold and its end, a pin, a floor, its node; no secret.</summary>
    public PeerKept Kept
    {
        get
        {
            lock (_gate)
            {
                _m.State(Now());
                return _m.Kept;
            }
        }
    }

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
    /// measured on a local link or over TLS; a clock measured over a plain link off this PC never schedules.
    /// </summary>
    public bool CanSchedule(long toleranceMs)
    {
        lock (_gate) return _m.CanSchedule(Now(), toleranceMs);
    }

    /// <summary>
    /// 13.11.6's channel: the link's clock is measured where it may time a command — over TLS, or on a local link. A clock measured
    /// over a plain link off this PC may be shown and never schedules anything: not an <c>AT</c>, and not a send held for its round trip.
    /// </summary>
    public bool ClockMayTime => _pub.Tls || _local;

    /// <summary>Which of the link's connections is up now, counted from 1 as each comes up; 0 for none. A send held for one never goes on another.</summary>
    public int ConnectionSerial => _pub.Connection;

    /// <summary>
    /// What the peer lists of itself for running-order cues, as last read — a PlayMap node's playlists and items (INDEX), a Patterns
    /// desk's cues (CUE LIST) — asked once its kind is heard and again when its STATE's revision moves; null until one is read, while
    /// the peer says another kind than the list was read from, and while it answers as another kind or node than its settings name.
    /// </summary>
    public PlayoutIndex? Index => _pub is { } pub && Problem(pub.Reading?.State) is null ? pub.Index : null;

    /// <summary>The connection up now runs over TLS: PlayMap's line channel on its <c>https</c> door.</summary>
    public bool OverTls => _pub.Tls;

    /// <summary>
    /// The certificate that waits for a person on this desk now (Show Bus 13.8.2–3), or null: a changed one (CERTIFICATE CHANGED,
    /// nothing sent until a person types at least <see cref="PlayoutRules.TrustHexMin"/> hex of it), or the one pinned first for
    /// comparison, which one press beside its short form trusts. What the desk offers beside the fingerprint; never a line's to run.
    /// </summary>
    public PlayoutTrustWait? TrustWaiting => _pub.TrustWait;

    /// <summary>
    /// A person on this desk trusts this link's certificate (Show Bus 13.8.2–3), from the desk's own entry beside its fingerprint and
    /// nowhere else — never a line from the wire, a deck, a trigger or an assistant, which every door and executor refuse. After
    /// CERTIFICATE CHANGED, <paramref name="hex"/> is what the person typed (<paramref name="typed"/>: an entry that was never filled
    /// in for them): at least <see cref="PlayoutRules.TrustHexMin"/> hex the certificate shown now starts with, which is then pinned
    /// for the node and the link dials again; a press that typed nothing trusts no changed certificate. For a certificate pinned first,
    /// it is that certificate's whole fingerprint (64 hex) as the press was shown it — the press is bound to it, so one pinned in its
    /// place since the words were drawn is never trusted by it. The new pin is handed to the keeper in this step, before the link dials
    /// again: the keeper holds it at once and writes its file on a lane of its own, and a write that fails is said once in the show
    /// log. Whether it trusted, and the words for the desk; nothing changes when it did not. Any thread.
    /// </summary>
    public (bool Trusted, string Words) Trust(string hex, bool typed)
    {
        var given = PlayoutRules.TrustHex(hex);
        (bool, string) result;
        lock (_gate)
        {
            var now = Now();
            var waiting = TrustWait(now);
            if (waiting is null)
                result = (false, $"no certificate of {Peer} waits for trust now — nothing changed");
            else if (waiting.Changed && !typed)
                result = (false, $"{Peer}'s certificate changed: type at least {PlayoutRules.TrustHexMin.ToString(CultureInfo.InvariantCulture)} hex of the new one, compared on its own screen — nothing trusted");
            else if (given is null)
                result = (false, $"type {PlayoutRules.TrustHexMin.ToString(CultureInfo.InvariantCulture)} to 64 hex of {Peer}'s certificate, as its fingerprint shows it — nothing trusted");
            else if (!waiting.Fingerprint.StartsWith(given, StringComparison.Ordinal))
                result = (false, $"{PlayoutWire.Short(given)} … is not how the certificate {Peer} shows starts ({PlayoutWire.Short(waiting.Fingerprint)} …) — compare it on its own screen; nothing trusted");
            else if (!waiting.Changed && given.Length != 64)
                result = (false, $"a certificate pinned first is trusted by the press beside its short form, bound to its whole fingerprint — nothing trusted");
            else if (waiting.Changed ? _m.AcceptCertificate(given) : _m.TrustFirst(given))
            {
                _noDoor = false;
                result = (true, $"{Peer}'s certificate {PlayoutWire.Short(waiting.Fingerprint)} … trusted by a person on this desk"
                    + (waiting.Changed ? " in place of the one pinned before — the link dials it again" : " — from now on a changed one is refused, and it is never dialled plain"));
            }
            else result = (false, $"{Peer}'s certificate changed as it was trusted — compare it again; nothing trusted");
            _pub = Publish(now);
        }
        SayAnswers();
        return result;
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _tick = _time.CreateTimer(_ => Tick(), null, CheckEvery, CheckEvery);
        _run = Task.Run(() => RunAsync(_stop.Token));
    }

    /// <summary>
    /// The clip on air with its position counted on to now, or null: none, the link not live, a playing clip gone stale, or a peer that
    /// answers as another kind than its settings name — sent nothing, it feeds no reader either (the VT, the frame, a clip's end).
    /// </summary>
    public PlayoutVideo? Video
    {
        get
        {
            var pub = _pub;
            if (pub.Reading is not { Video: { } v } r || pub.Pairing == PlayoutPairing.None || pub.State != PeerLinkState.Live) return null;
            if (Problem(r.State) is not null) return null;
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
            ? new PlayoutState(ShowBusKinds.Label(Kind) ?? _label, v.Title, v.LengthSeconds / v.Rate, v.PositionSeconds / v.Rate, v.Playing && !v.Paused)
            {
                Link = _name, Node = NodeId ?? "",
            }
            : null;

    /// <summary>
    /// Jump the clip on air to its last <paramref name="seconds"/>. Null = on its way (the link's lane writes it), else why not. Each
    /// command takes its asker's <paramref name="mark"/> (the desk: the running-order item it was asked for), handed back with each of
    /// its outcomes (<see cref="PlayoutAnswer.Mark"/>).
    /// </summary>
    public string? EndIn(int seconds, int mark = -1) => Clip("END", seconds, mark).Error;

    /// <summary>Play the clip on air again from its start. Null = on its way (the link's lane writes it), else why not.</summary>
    public string? Restart(int mark = -1) => Clip("RESTART", 0, mark).Error;

    /// <summary>Play on / pause / stop (back to the top, held) the clip on air — desks with transport only.</summary>
    public string? Play(int mark = -1) => Clip("PLAY", 0, mark).Error;
    public string? Pause(int mark = -1) => Clip("PAUSE", 0, mark).Error;
    public string? StopClip(int mark = -1) => Clip("STOP", 0, mark).Error;   // VIDEO STOP: the clip only — never the bare STOP that closes outputs

    /// <summary>Move the clip on air to <paramref name="seconds"/> from its start.</summary>
    public string? Seek(int seconds, int mark = -1) => Clip("SEEK", seconds, mark).Error;

    /// <summary>
    /// The clip on air's transport — <c>VIDEO END n</c>, <c>RESTART</c>, <c>PLAY</c>, <c>PAUSE</c>, <c>STOP</c> or <c>SEEK n</c> — with its
    /// ticket, or why nothing was sent. With <paramref name="onKey"/> the line names the clip by the peer's own key
    /// (<c>VIDEO PAUSE ON &lt;key&gt;</c>), for a peer that lists that form; never a bare <c>STOP</c>.
    /// </summary>
    public PlayoutSent Clip(string what, int seconds, int mark = -1, string? onKey = null)
    {
        string[] On(params string[] words) => onKey is { Length: > 0 } key ? [.. words, "ON", key] : words;
        switch (what)
        {
            case "END":
                if (seconds is < 1 or > 3600) return new(null, "seconds must be 1-3600");
                if (Video is { } v && !v.CountsDown)
                    return new(null, v.Loops ? "the clip on air loops — it has no end to jump to" : $"{_label} doesn't know this clip's length yet");
                return Send(mark, "VIDEO", On("END", seconds.ToString(CultureInfo.InvariantCulture)));
            case "RESTART":
                return Send(mark, "VIDEO", On("RESTART"));
            case "PLAY" or "PAUSE" or "STOP":
                return Transport(mark, On(what));
            case "SEEK":
                if (seconds is < 0 or > 24 * 3600) return new(null, "seek takes 0 to 24 hours");
                if (Video is { LengthSeconds: > 0 } c && seconds > c.LengthSeconds) return new(null, $"the clip is only {TimerTick.FormatSeconds((long)c.LengthSeconds)} long");
                return Transport(mark, On("SEEK", seconds.ToString(CultureInfo.InvariantCulture)));
            default:
                return new(null, $"VIDEO {what} isn't one of the clip's verbs a playout link sends");
        }
    }

    /// <summary>
    /// A PlayMap playlist's <c>PLAYLIST GO &lt;playlist&gt; [&lt;item&gt;] [EXPECT &lt;next&gt;] [AT &lt;unix ms&gt;]</c> or
    /// <c>PLAYLIST CUE &lt;playlist&gt; &lt;item&gt;</c>, each word quoted by PlayMap's line rule where it needs it, with its ticket, or why
    /// nothing was sent. Only to a peer heard to be PlayMap: a Patterns desk has no playlists of this shape. <paramref name="at"/> is a
    /// GO's instant on the node's own wall clock, which only a GO carries.
    /// </summary>
    public PlayoutSent Playlist(string verb, string list, string? item, string? expect, int mark = -1, long? at = null)
    {
        if (Kind != PlayoutRules.PlayMap) return new(null, $"{Peer} isn't heard to be a PlayMap node — no playlist command is sent to it");
        if (at is <= 0) return new(null, "a GO's AT is a Unix ms on the node's clock");
        // A GO at an instant names it last, in the node's own clock (13.11.6: only from a clock fit for it, which the asker judged).
        string[] At(params string[] words) => at is { } ms && verb == "GO" ? [.. words, "AT", ms.ToString(CultureInfo.InvariantCulture)] : words;
        return verb switch
        {
            "GO" when item is { Length: > 0 } => Send(mark, "PLAYLIST", At("GO", list, item)),
            "GO" when expect is { Length: > 0 } => Send(mark, "PLAYLIST", At("GO", list, "EXPECT", expect)),
            "CUE" when at is null && item is { Length: > 0 } => Send(mark, "PLAYLIST", ["CUE", list, item]),
            _ => new(null, $"PLAYLIST {verb} isn't one of the playlist verbs a playout link sends (GO with an item or its EXPECT, CUE with an item)"),
        };
    }

    /// <summary>
    /// A Patterns desk's <c>CUE GO &lt;id&gt;</c> or <c>CUE STANDBY &lt;id&gt;</c>, the id bare (Patterns reads its words raw), with its
    /// ticket, or why nothing was sent. Only to a peer heard to be Patterns. Never STANDBY and GO together: its own standby fence decides.
    /// </summary>
    public PlayoutSent Cue(string verb, string cue, int mark = -1)
    {
        if (Kind != PlayoutRules.Patterns) return new(null, $"{Peer} isn't heard to be a Patterns desk — no cue command is sent to it");
        return verb is "GO" or "STANDBY" ? Send(mark, "CUE", [verb, cue]) : new(null, $"CUE {verb} isn't one of the cue verbs a playout link sends (GO, STANDBY)");
    }

    private PlayoutSent Transport(int mark, string[] words)
    {
        if (NotReady() is { } why) return new(null, why);   // an unpaired link knows no clip, so it says why it sends nothing
        var video = _pub.Reading?.Video;
        if (video is { Transport: false })
            return new(null, $"this {_label} desk is older than Round 86 — it only takes Last 10 s and restart");
        if (video is null) return new(null, $"{_label} has no clip on air");
        return Send(mark, "VIDEO", words);
    }

    /// <summary>Null when a command may go out; else why not — no connection, or a peer that has not paired with this link.</summary>
    private string? NotReady()
    {
        var pub = _pub;
        if (!pub.Connected) return $"{_label} isn't connected — nothing sent";
        return pub.Pairing == PlayoutPairing.None ? $"{_label} hasn't paired with this link — nothing sent ({pub.Status})" : null;
    }

    /// <summary>
    /// One command, built here and nowhere else (<see cref="PlayoutWire.Line"/>, in the words the peer's kind reads back as themselves),
    /// only for a connection that paired: the core decides whether it may go (13.12.3's not-sent rows, nothing queued across a drop) and
    /// reads its answer by its request id. Its ticket once it is on the connection's lane, which writes it in order — the caller's thread
    /// never touches the socket — else why it was not sent. Its outcome comes by <see cref="Answered"/> and its ticket: not-sent should
    /// the connection end before the lane wrote it, and once written, a desk older than Show Bus 1.1 echoes no id, so it is unconfirmed
    /// at once (13.12.3).
    /// </summary>
    private PlayoutSent Send(int mark, string verb, string[] words)
    {
        if (verb == "STOP" && words.Length == 0) return new(null, "refused");   // never: it closes every Patterns output
        if (Problem(_pub.Reading?.State) is { } wrongKind) return new(null, wrongKind);
        var kind = Kind ?? _setKind;
        if (PlayoutWire.Line(kind, null, verb, words) is not { } line) return new(null, "that command can't be written on this link");
        PlayoutSent sent;
        lock (_gate)
        {
            sent = SendLocked(verb, line, mark);
        }
        SayAnswers();
        return sent;
    }

    private PlayoutSent SendLocked(string verb, string line, int mark)
    {
        var now = Now();
        var conn = _conn;
        if (conn is null || conn.Pairing == PlayoutPairing.None)
        {
            _pub = Publish(now);
            return new(null, NotReady() ?? $"{_label} hasn't paired with this link — nothing sent");
        }
        var id = PeerLink.RequestId(IdPrefix, Interlocked.Increment(ref _ids.Value), _o.Inst);
        _outbound[id] = conn.Bus11 ? "@" + id + " " + line : line;
        if (_m.Send(now, id, verb, queued: true) == PeerOutcome.NotSent)
        {
            _outbound.Remove(id);
            var why = _m.WordsOf(id);
            _pub = Publish(now);
            return new(null, why);
        }
        var pending = _tickets[id] = new Pending();
        Track(id, line, mark);
        Flush(conn);
        Ended(conn, now);
        TrackAnswers(now);
        _pub = Publish(now);
        return new(new PlayoutTicket(id, line, pending.First.Task, pending.Final.Task), null);
    }

    // ------------------------------------------------------------------ the connection

    private async Task RunAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            long wait;
            string? scheme;
            // The node's hello as the dial is made: a TLS door it names is tried first (13.4.2).
            var heard = Heard();
            lock (_gate)
            {
                var now = Now();
                Hear(now, heard);
                // The core's own wait: 13.15.1's redial, a hold, or none at all (a dial now). A link the core dials nothing for (a
                // refusal a person must act on) looks again each second.
                scheme = _m.Dial(now);
                wait = scheme is not null ? 0 : Math.Max(1, _m.Record(now).NextMs ?? 1000);
                _pub = Publish(now);
            }
            SayAnswers();
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
    /// link says why — never a light that was the live link's. A TLS dial goes to the node's line channel over TLS where a fresh hello
    /// names its door, else where a pinned node's certificate last answered (the door kept beside its pin: 13.4.2, the pin alone
    /// decides); where neither does, a link that may not go plain (set to wss, or any pin or a floor that asks for TLS) dials nothing
    /// and says so, and any other goes on the line wire, where the core takes it plain (13.3.3).
    /// </summary>
    private async Task ConnectionAsync(string scheme, CancellationToken stop)
    {
        if (scheme == "tls")
        {
            var heard = Heard();
            int? door;
            string? notPlain = null;
            lock (_gate)
            {
                door = DoorOf(heard) ?? KeptDoor();
                if (door is null) notPlain = NotPlain();
            }
            if (door is { } d)
            {
                await WssAsync(d, stop);
                return;
            }
            if (notPlain is not null)
            {
                NoDoor(notPlain);
                return;
            }
        }
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
                _noDoor = false;
                if (!stop.IsCancellationRequested) Say(LinkHealth.NotFound, $"{_label} not reachable at {_host}:{_port}");
                _pub = Publish(now);
            }
            return;
        }
        var loopback = tcp.Client.RemoteEndPoint is IPEndPoint remote && _o.IsLoopback(remote.Address);
        Stream stream = tcp.GetStream();
        await RunConnectionAsync(new Connection(tcp, _o.Wrap is { } wrap ? wrap(stream) : stream, loopback, stop), scheme, stop);
    }

    /// <summary>
    /// The TLS dial: <c>wss://&lt;host&gt;:&lt;door&gt;/ws</c>, asking for <c>playmap.lines.v1</c>, through no proxy and following no redirect.
    /// The certificate is the core's to judge in the handshake (<see cref="CertificateShown"/>): one a person pinned that it does not
    /// match ends the dial there, so no HTTP request, line or proof goes to it. A 429 at the door is a lockout: the token is held. An
    /// answer that does not take the subprotocol is no PlayMap line channel, and nothing is sent on it.
    /// </summary>
    private async Task WssAsync(int door, CancellationToken stop)
    {
        int attempt;
        lock (_gate)
        {
            attempt = ++_attempt;
            _noDoor = false;
        }
        var ws = new ClientWebSocket();
        ws.Options.Proxy = null;
        ws.Options.AddSubProtocol(PlayoutWire.LinesProtocol);
        ws.Options.CollectHttpResponseDetails = true;
        ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => CertificateShown(attempt, cert);
        var host = IPAddress.TryParse(_host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + _host + "]" : _host;
        var where = _host + ":" + door.ToString(CultureInfo.InvariantCulture);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(PeerLink.ConnectTimeoutMs), _time);
            using var either = CancellationTokenSource.CreateLinkedTokenSource(stop, deadline.Token);
            await ws.ConnectAsync(new Uri("wss://" + host + ":" + door.ToString(CultureInfo.InvariantCulture) + "/ws"), either.Token);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException or HttpRequestException
                                       or System.Security.Authentication.AuthenticationException or ArgumentException or UriFormatException)
        {
            var status = (int)ws.HttpStatusCode;
            var retry = RetryAfter(ws.HttpResponseHeaders);
            ws.Dispose();
            lock (_gate)
            {
                var now = Now();
                // What the core asked to write for a connection that never came (its greeting) goes nowhere: the next one greets anew.
                _ = _m.Drain();
                if (_m.State(now) == PeerLinkState.Refused)
                {
                    // CERTIFICATE CHANGED at the handshake: nothing of this link's went past it, and its words are the refusal's.
                }
                else if (status == 429)
                {
                    Say(LinkHealth.Error, $"{Peer} has locked this PC out after wrong tokens (429 at {where}) — nothing sent");
                    _m.Refused(now, "locked", retry);
                }
                else
                {
                    if (_m.KeepsConnection(now)) _m.Closed(now);
                    if (!stop.IsCancellationRequested)
                        Say(LinkHealth.NotFound, status switch
                        {
                            403 => $"{Peer} refused this link's line channel at {where} (403) — its own log says why; nothing sent",
                            > 0 => $"{Peer} answered {status.ToString(CultureInfo.InvariantCulture)} at {where}, not its line channel — nothing sent",
                            _ => $"{_label} not reachable over TLS at {where}",
                        });
                }
                _pub = Publish(now);
            }
            SayAnswers();
            return;
        }
        if (ws.SubProtocol != PlayoutWire.LinesProtocol)
        {
            ws.Abort();
            ws.Dispose();
            lock (_gate)
            {
                var now = Now();
                _ = _m.Drain();
                if (_m.KeepsConnection(now)) _m.Closed(now);
                Say(LinkHealth.Error, $"{where}/ws did not take {PlayoutWire.LinesProtocol} — no PlayMap line channel there; nothing sent");
                _pub = Publish(now);
            }
            SayAnswers();
            return;
        }
        // Plain AUTH never goes on the line channel (the node takes proofs only there), so it is no loopback socket for it.
        var lines = new PlayoutLinesStream(ws);
        await RunConnectionAsync(new Connection(lines, _o.Wrap is { } wrap ? wrap(lines) : lines, false, stop, lines, door, attempt), "tls", stop);
    }

    /// <summary>A 429's <c>Retry-After</c> in seconds (0 for none, or one that is not a number of seconds).</summary>
    private static int RetryAfter(IReadOnlyDictionary<string, IEnumerable<string>>? headers)
        => headers is not null && headers.TryGetValue("Retry-After", out var values) && values.FirstOrDefault() is { } v
           && int.TryParse(v.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;

    /// <summary>
    /// 13.8: the certificate the TLS handshake shows, judged by the core by its fingerprint alone (never a name, chain or authority):
    /// a pin a person or a proof made that it does not match is CERTIFICATE CHANGED, and the handshake ends here with nothing of the
    /// link's sent. Only the dial out now is judged: a handshake its deadline already ended, or one of a stopped link, is refused.
    /// </summary>
    private bool CertificateShown(int attempt, X509Certificate? cert)
    {
        if (cert is null) return false;
        var fingerprint = PlayoutWire.Fingerprint(cert.GetRawCertData());
        bool ok;
        lock (_gate)
        {
            var now = Now();
            if (attempt != _attempt || !_m.KeepsConnection(now)) return false;
            _m.Certificate(now, fingerprint);
            ok = _m.KeepsConnection(now);
            if (ok) _certFor = attempt;
            _pub = Publish(now);
        }
        SayAnswers();
        return ok;
    }

    /// <summary>
    /// The hello the show network heard for this link's node, as a link that may dial TLS takes it: a PlayMap node's, for a link not
    /// set to tcp nor set to (or heard to be) a Patterns desk; else null. Asked outside the link's lock.
    /// </summary>
    private PlayoutHeard? Heard()
    {
        if (_o.Hello is not { } hello || _transport == PlayoutRules.Tcp || _setKind == PlayoutRules.Patterns || Volatile.Read(ref _kindSeen) == PlayoutRules.Patterns)
            return null;
        return hello() is { Kind: PlayoutRules.PlayMap } heard ? heard : null;
    }

    /// <summary>The TLS door a hello names for the node, or null: none heard, or one this link may not take.</summary>
    private static int? DoorOf(PlayoutHeard? heard) => heard?.TlsDoor;

    /// <summary>
    /// The door kept beside the pin (under the lock), or null: none kept, no pin, or a link that dials no TLS (set to tcp, or to — or
    /// heard to be — a Patterns desk). Where a pinned node's certificate last answered, dialled while no fresh hello names a door.
    /// </summary>
    private int? KeptDoor()
        => _door > 0 && _m.Kept.Pin is not null && _transport != PlayoutRules.Tcp && _setKind != PlayoutRules.Patterns && _kindSeen != PlayoutRules.Patterns
            ? _door : null;

    /// <summary>
    /// A PlayMap node's hello for this link, told to the core (under the lock): it may raise the guard — try TLS first — and never
    /// lowers it; a pinned node's hello naming another certificate is a note, never a refusal (13.4.2). One under §2's conflict mark
    /// holds the node while the mark stands — no new dial, a link up keeps running (13.4.4) — and nothing it says is taken.
    /// </summary>
    private void Hear(PeerNow now, PlayoutHeard? heard)
    {
        if (heard is null || !ShowBusHello.ValidId(heard.Id)) return;
        if (heard.ConflictLeftMs > 0)
        {
            _m.Marked(now, heard.Id, heard.ConflictLeftMs);
            return;
        }
        _m.Hello(now, heard.Id, PlayoutWire.IsFingerprint(heard.Tls) ? heard.Tls : null, heard.Inst, heard.Address);
    }

    /// <summary>
    /// Why this link may not go plain where no TLS door is heard or kept (under the lock), or null when it may: it is set to wss, or a
    /// pin — any, a <c>first</c> one among them — or a floor asks for TLS (13.4.2: for an id with a pin the pin alone decides, and a
    /// hello never lowers the guard, so a pinned node is never dialled plain, whatever its hello says or does not say). A <c>first</c>
    /// pin decides only the transport: a changed certificate under it is a note, never a refusal (13.8.2).
    /// </summary>
    private string? NotPlain()
    {
        var kept = _m.Kept;
        var pinned = kept.Pin is not null || kept.Floor is { Tls: true };
        if (!pinned && _transport != PlayoutRules.Wss) return null;
        var why = pinned ? $"{Peer}'s certificate is pinned" : $"{_label} is set to dial wss only";
        var door = _transport == PlayoutRules.Tcp
            ? "its transport is set to tcp — set it to auto or wss (Settings → Integrations)"
            : _setKind == PlayoutRules.Patterns || _kindSeen == PlayoutRules.Patterns
                ? "a Patterns desk offers no TLS on its line wire"
                : "no hello names its TLS door now, and none is kept beside its pin";
        return $"{why}, so it is never dialled plain, and {door} — nothing sent";
    }

    /// <summary>
    /// A TLS dial with no door to take, by a link that may not go plain: nothing is dialled. The core hears a failed dial, so the link
    /// looks again on 13.15.1's wait, and says why it dials nothing (amber, never red: nothing refused it).
    /// </summary>
    private void NoDoor(string why)
    {
        lock (_gate)
        {
            var now = Now();
            if (_m.KeepsConnection(now)) _m.Closed(now);
            _noDoor = true;
            Say(LinkHealth.Starting, why);
            _pub = Publish(now);
        }
        SayAnswers();
    }

    /// <summary>
    /// A connection that came, greeted and run to its end: its lane writes, its lines are read, and whatever ends it the core hears
    /// once. A TLS line channel the node closed with 4401 (wrong proofs, or an address its wrong tokens locked out) holds the token, as
    /// a refused proof does.
    /// </summary>
    private async Task RunConnectionAsync(Connection conn, string scheme, CancellationToken stop)
    {
        if (!Greet(conn, scheme))
        {
            // The core ended the dial at its own deadline before the socket came, or refused it: nothing is said on it.
            conn.Dispose();
            return;
        }
        // The lane reads its connection's token as the connection starts: the token's source is disposed with the connection, which
        // can end before the lane's first line.
        var end = conn.End.Token;
        conn.Writer = Task.Run(() => WriteLaneAsync(conn, end));
        await ReadAsync(conn);
        lock (_gate)
        {
            var now = Now();
            if (ReferenceEquals(_conn, conn))
            {
                _conn = null;
                // The core hears the end once: what the lane wrote is unconfirmed, what it had not is not-sent (13.15.5).
                var kept = _m.KeepsConnection(now);
                var closed = conn.Lines?.CloseCode;
                if (kept && closed == PlayoutWire.WrongProofsClose)
                {
                    var words = conn.Lines!.CloseWords;
                    Explain(conn, LinkHealth.Error, $"{Peer} closed the link: wrong token ({PlayoutWire.WrongProofsClose.ToString(CultureInfo.InvariantCulture)}"
                        + (words.Length > 0 ? ": " + Clip(words) : "") + ")");
                    _m.Refused(now, "refused", 0);
                }
                else if (kept) _m.Closed(now);
                // Why it ended, unless that was said as it ended (a refusal, the evaluator's drop, a busy desk, a broken write) or is an
                // error the desk said on it. A desk that does not pair keeps the words that say why while the link redials.
                if (!stop.IsCancellationRequested && !conn.EndExplained && _saidHealth is not LinkHealth.Error)
                {
                    if (kept && closed is { } code && code != 1000)
                        Say(LinkHealth.NotFound, $"{_label} closed the line channel ({code.ToString(CultureInfo.InvariantCulture)}"
                            + (conn.Lines!.CloseWords.Length > 0 ? ": " + Clip(conn.Lines.CloseWords) : "") + ")");
                    else if (kept) Say(LinkHealth.NotFound, $"{_label} closed the connection");
                    else if (conn.Pairing != PlayoutPairing.None) Say(LinkHealth.NotFound, $"the link to {_host}:{_port} ended");
                    else if (!conn.Advised)
                        Say(LinkHealth.NotFound, $"{_host}:{_port} did not pair within {(PeerLink.HandshakeMs / 1000).ToString(CultureInfo.InvariantCulture)} s");
                }
                _reading = null;
                _context = null;
                _contextNode = null;
                _whoami = null;
                _outbound.Clear();
                TrackAnswers(now);
                _pub = Publish(now);
            }
        }
        // The socket closes under a write the lane is in, so the lane ends with it; nothing it had not written is written after this.
        conn.Dispose();
        if (conn.Writer is { } writer) await writer;
    }

    /// <summary>
    /// The socket is up: the core greets (<c>HELLO Stagewatch</c>; it offers no LINK yet), and the link probes whether the peer speaks
    /// Show Bus 1.1 — both put on the connection's lane, which starts once this returns true. On the TLS line channel the handshake's
    /// certificate already brought the link up — and one whose certificate the core did not judge for this dial is ended with nothing
    /// sent, red, never taken up unchecked. On the line wire, which has no TLS, a dial the core meant as TLS found none: the core
    /// refuses a node a proof or a person pinned, or a floor holds to TLS, with nothing sent (13.9.3), and otherwise goes on plain
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
            if (conn.Tls)
            {
                // The certificate's step brought it up, over TLS. A TLS connection whose certificate the core never judged for this
                // dial (its step did not run, or ran for another) is never taken up unpinned: it ends here, and nothing is sent on it.
                if (conn.Attempt != _certFor || _m.CertificateShown is null)
                {
                    _m.Closed(now);
                    Say(LinkHealth.Error, $"{_label}'s TLS at {_host}:{conn.Door.ToString(CultureInfo.InvariantCulture)} came up with no certificate this link checked — nothing sent");
                    _pub = Publish(now);
                    return false;
                }
            }
            else if (scheme == "tls") _m.NoTls(now);
            else _m.Connected(now);
            if (!_m.KeepsConnection(now))
            {
                _pub = Publish(now);
                return false;
            }
            _conn = conn;
            conn.Serial = ++_serials;
            conn.ConnectedAt = now.Mono;
            _noDoor = false;
            // 13.11.7: the window empties when the peer's inst changes. A level-0 link never learns the inst, so each connection may be
            // another desk at the address, or the same one restarted: its clock starts with no sample.
            _m.Clock.Clear();
            Say(LinkHealth.Starting, $"connected to {(conn.Tls ? _host + ":" + conn.Door.ToString(CultureInfo.InvariantCulture) + " over TLS" : _host + ":" + _port.ToString(CultureInfo.InvariantCulture))} — pairing");
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
            TrackAnswers(now);
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
                if (ok && words.StartsWith('{') && words.Length < PlayoutWire.LineBytes)
                {
                    _context = words;
                    _contextNode = ContextNode(words);
                }
                return;
            }
            if (id == "wh")
            {
                // PlayMap's WHOAMI: who this link is to it now. An older node that does not know the verb leaves it unknown.
                _whoami = ok ? ReadWhoami(words) : null;
                return;
            }
            if (id is ListAskIndex or ListAskCues)
            {
                // What the peer lists for running-order cues: kept when it reads as the list of the kind the peer says it is, and only
                // while the peer answers as the kind and node its settings name (13.4.3: nothing of another node feeds a reader). A
                // peer that refuses the verb (an older one) is not asked again on this connection.
                if (!ok) conn.ListRefused = true;
                else if (Problem(_reading?.State) is null
                         && (id == ListAskIndex ? PlayoutIndex.ReadIndex(words) : PlayoutIndex.ReadCues(words)) is { } list && list.Kind == _kindSeen) _index = list;
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

    /// <summary>The request ids of the link's own asks for the peer's list: a PlayMap node's INDEX, a Patterns desk's CUE LIST.</summary>
    private const string ListAskIndex = "ix", ListAskCues = "cl";

    /// <summary>
    /// The ask for the peer's list now, with the revision its STATE names (under the lock), or null: none for a kind not heard, or a
    /// peer that answers as another kind or node than its settings name; none while the list read is the one the STATE names (or the STATE names none and a list was read); and none for a revision already
    /// asked on this connection, so a peer whose answer names another revision than its STATE is not asked again and again.
    /// </summary>
    private (string Line, string Revision)? ListAsk(Connection conn)
    {
        var state = _reading?.State;
        var (line, revision) = _kindSeen switch
        {
            PlayoutRules.PlayMap => ("@" + ListAskIndex + " INDEX", state is { HasProject: true } ? state.ProjectIndex ?? "" : ""),
            PlayoutRules.Patterns => ("@" + ListAskCues + " CUE LIST", state is { HasCuestack: true } ? state.Cuestack?.ListRev ?? "" : ""),
            _ => ("", ""),
        };
        // A peer that answers as another kind, or as another node, than its settings name is asked nothing more.
        if (line.Length == 0 || Problem(state) is not null) return null;
        var needed = _index is not { } read || read.Kind != _kindSeen || (revision.Length > 0 && revision != read.Revision);
        return needed && conn.ListAskedFor != revision ? (line, revision) : null;
    }

    /// <summary>A context's <c>node.id</c> when it is a Show Bus id, else null: never an exception, whatever the peer sent.</summary>
    private static string? ContextNode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("node", out var node)
                   && node.ValueKind == JsonValueKind.Object && node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                   && id.GetString() is { } said && ShowBusHello.ValidId(said) ? said : null;
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>
    /// PlayMap's WHOAMI answer, read for what a link uses — paired, role, armed, may (<c>"*"</c> or a list of verbs, at most 256 of 64
    /// characters) — or null for one it cannot read. Never an exception, whatever the peer sent.
    /// </summary>
    public static PlayoutWhoami? ReadWhoami(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            var role = r.TryGetProperty("role", out var ro) && ro.ValueKind == JsonValueKind.String ? ShowBusText.Clip(ro.GetString() ?? "", 24) : "";
            IReadOnlyList<string>? may = null;
            if (r.TryGetProperty("may", out var m))
            {
                if (m.ValueKind == JsonValueKind.Array)
                    may = m.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "")
                        .Where(v => v.Length is > 0 and <= 64).Take(256).ToList();
                else if (m.ValueKind != JsonValueKind.String || m.GetString() != "*") return null;
            }
            else return null;
            return new PlayoutWhoami(r.TryGetProperty("paired", out var p) && p.ValueKind == JsonValueKind.True, role,
                r.TryGetProperty("armed", out var a) && a.ValueKind == JsonValueKind.True, may);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Peer words as the link says them: the token taken out first (<see cref="Scrub"/>), so no cut leaves part of it, then at most 160 characters.</summary>
    private string Clip(string words)
    {
        words = Scrub(words);
        return words.Length > 160 ? words[..160] : words;
    }

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
        _reading = new StateRead(state, now.Mono, ++_readings);
        // The kind its STATE says (13.4): what the link's commands are written for, and the one a kind set in Settings must match.
        if (state.Kind is { } kind) _kindSeen = kind.Length > 0 ? kind : null;
        if (_index is { } listed && listed.Kind != _kindSeen) _index = null;
        if (Problem(state) is { } problem)
        {
            // Amber, both named, nothing sent — and over TLS, the certificate of a node not meant is pinned nothing; nor is a list
            // read before kept for it: it is asked again once the peer answers as the node meant.
            _index = null;
            if (_conn is { } asked) asked.ListAskedFor = null;
            Say(LinkHealth.Starting, problem);
            return;
        }
        if (state.Whole) PinFirst(now, state.Id);
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
    /// 13.8.2 on the TLS line channel, at its first whole STATE once the link paired: the certificate it runs over is pinned
    /// <c>first</c> for the node that STATE names — kept for comparison, never trust, since at level 0 nothing proves the node; a node
    /// other than the one Settings chose never gets here (<see cref="NodeProblem"/>). Once pinned, by this step or a person, the door it
    /// answered at is kept beside the pin and dialled while no fresh hello names one, so the link never goes plain for want of a
    /// hello (13.4.2). A person makes the pin theirs (<see cref="Trust"/>); from then on a changed certificate is refused.
    /// </summary>
    private void PinFirst(PeerNow now, string? stateId)
    {
        if (_conn is not { Tls: true, PinLooked: false } conn || conn.Pairing == PlayoutPairing.None) return;
        conn.PinLooked = true;
        var id = stateId is { Length: > 0 } s && ShowBusHello.ValidId(s) ? s : null;
        _m.PinShown(now, id);
        if (_m.Kept.Pin is not null && conn.Door > 0) _door = conn.Door;
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
        // Plain AUTH goes only on the line wire to this PC: the TLS line channel takes proofs alone, and its socket is never loopback here.
        if (_token.Length > 0 && conn.Loopback && !conn.Tls)
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
        if (!conn.Bus11) return;
        // Its STATE, and its context at once: the greeting it sent before the pairing was set aside, and nothing it says is taken
        // from before; the context's next ask comes ContextEvery from now.
        Write(conn, "@st STATUS");
        Write(conn, "@ctx CONTEXT");
        conn.LastContext = now.Mono;
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

    /// <summary>Null when the peer's STATE names a kind and a node this link may command; else why not, in words (the kind's first).</summary>
    private string? Problem(PlayoutPeerState? state) => KindProblem(state?.Kind) ?? NodeProblem(state?.Id);

    /// <summary>
    /// Null when the node the peer's STATE names is the one Settings set this link to, or either names none; else why not, in words,
    /// both ids named — 13.4.3: an acceptor that answers as another id than the one meant is followed in nothing, so nothing is sent
    /// to it, nothing of it feeds a reader, and its certificate is pinned nothing, until Settings name its node again.
    /// </summary>
    private string? NodeProblem(string? stateId)
    {
        if (_meant is null || stateId is not { Length: > 0 } id || id == _meant) return null;
        return $"{_label} at {_host}:{_port} answers as node {Clip(id)}, not node {_meant} this link is set to — nothing is sent to it until its node is set again (Settings → Integrations)";
    }

    /// <summary>
    /// Null when the peer's kind is one this link may command: its STATE named none yet, or the kind Settings set (or any of the two
    /// Stagewatch speaks, when Settings leaves it to the peer); else why not, in words — nothing is sent to a peer that answers as
    /// another kind than the one it was set up as, or as a kind this Stagewatch does not speak.
    /// </summary>
    private string? KindProblem(string? said)
    {
        if (said is null) return null;
        if (said.Length == 0) return $"{_label} at {_host}:{_port} says it is a kind of node this Stagewatch doesn't speak to — nothing is sent to it";
        if (_setKind != PlayoutRules.Auto && said != _setKind)
            return $"{_label} at {_host}:{_port} answers as {ShowBusKinds.Label(said)}, but its settings say {ShowBusKinds.Label(_setKind)} — nothing is sent to it until its kind is set again (Settings → Integrations)";
        return null;
    }

    /// <summary>Amber while the link holds a token the desk never asked for (it asks for no key); otherwise green.</summary>
    private LinkHealth PairedHealth => _conn is { Pairing: PlayoutPairing.Open } && _token.Length > 0 ? LinkHealth.Starting : LinkHealth.Ok;

    private string OpenNote => _conn is not { Pairing: PlayoutPairing.Open } ? ""
        : _token.Length > 0 ? " · asks for no key — the token stays on this PC" : " · asks this link for no key";

    // ------------------------------------------------------------------ the evaluator

    /// <summary>
    /// Every <see cref="CheckEvery"/>, on the link's clock and never behind a write: the core's evaluator (13.10.3–4: stale, down, its
    /// own gap), the write deadline of the connection's lane, the desk's NONCE wait and the probe's, the ping that carries the clock
    /// exchange and the CONTEXT ask once paired, the commands whose answer is overdue, and what every reader takes.
    /// </summary>
    private void Tick()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        // The node's hello, asked as often as hellos come (outside the lock: the show network's table is its own).
        PlayoutHeard? heard = null;
        var asked = _o.Hello is not null && MonoMs() - Interlocked.Read(ref _helloAskedAt) >= HelloEvery.TotalMilliseconds;
        if (asked)
        {
            Interlocked.Exchange(ref _helloAskedAt, MonoMs());
            heard = Heard();
        }
        lock (_gate)
        {
            var now = Now();
            var evaluation = _m.Evaluate(now);
            if (evaluation is { PingedAll: true, GapMs: { } gap }) Paused(now, gap);
            if (asked) Hear(now, heard);
            if (_conn is { } conn && !conn.End.IsCancellationRequested)
            {
                if (evaluation.Closed) Explain(conn, LinkHealth.Error, $"{_label} stopped answering");
                // 13.4.2: a hello that names the node's TLS door raises the guard. A paired link on the line wire, off this PC, moves to
                // TLS at once — between commands, never with one waiting for its answer, so none is left unconfirmed by the move.
                if (asked && !conn.Tls && !_local && conn.Pairing != PlayoutPairing.None && DoorOf(heard) is not null && _m.InFlight == 0)
                {
                    Explain(conn, LinkHealth.Starting, $"{Peer}'s hello names its TLS door now — the link moves to TLS");
                    End(conn);
                }
                if (conn.WritingSince is { } since && now.Mono - since > WriteDeadline.TotalMilliseconds)
                {
                    // A write the socket holds up past its deadline ends the connection: what it was writing may have reached the desk.
                    Explain(conn, LinkHealth.NotFound, $"{_label}: a write to {_host}:{_port} did not finish in "
                        + WriteDeadline.TotalSeconds.ToString(CultureInfo.InvariantCulture) + " s");
                    conn.Broke = true;
                    End(conn);
                }
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
                    // A PlayMap node says who this link is to it — its role, whether its show is armed, what it may run — asked once
                    // its kind is heard and every ContextEvery after: what greys a desk's entries, never a local refusal.
                    if (conn.Bus11 && _kindSeen == PlayoutRules.PlayMap && now.Mono - conn.LastWhoami >= ContextEvery.TotalMilliseconds)
                    {
                        Write(conn, "@wh WHOAMI");
                        conn.LastWhoami = now.Mono;
                    }
                    // What the peer lists for running-order cues, asked once its kind is heard and again when its STATE's revision
                    // moves — at most once each ContextEvery, once a revision on a connection, never again on one that refused it.
                    if (conn.Bus11 && !conn.ListRefused && now.Mono - conn.LastList >= ContextEvery.TotalMilliseconds && ListAsk(conn) is { } ask)
                    {
                        Write(conn, ask.Line);
                        conn.LastList = now.Mono;
                        conn.ListAskedFor = ask.Revision;
                    }
                }
                Flush(conn);
                Ended(conn, now);
            }
            TrackAnswers(now);
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

    /// <summary>
    /// A ping, on the connection's lane: the lane stamps its t1 (this PC's wall clock) as it writes it, and the core keeps that t1 among
    /// the last it matches (<see cref="Stamp"/>); a desk older than 1.1 gets a bare PING.
    /// </summary>
    private void Ping(Connection conn, PeerNow now)
    {
        conn.LastPing = now.Mono;
        Enqueue(conn, new Outgoing(null, Ping: true));
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
                    Enqueue(conn, new Outgoing(line, id));
                    break;
                default:
                    // Nothing else is this link's to write at level 0: it offers no LINK and asks no RECEIPTS.
                    break;
            }
        }
    }

    /// <summary>One line for the connection, on its lane (under the lock): the lane writes it after every line asked for before it.</summary>
    private void Write(Connection conn, string line) => Enqueue(conn, new Outgoing(line));

    /// <summary>
    /// A line onto the connection's lane, never written here. A lane past <see cref="LaneMax"/> lines ends its connection rather than
    /// drop one; a connection that ended takes nothing more.
    /// </summary>
    private void Enqueue(Connection conn, Outgoing line)
    {
        if (conn.End.IsCancellationRequested || conn.Lane.Writer.TryWrite(line)) return;
        Explain(conn, LinkHealth.NotFound, $"{_label}: {LaneMax.ToString(CultureInfo.InvariantCulture)} lines waited to be written to {_host}:{_port}");
        conn.Broke = true;
        End(conn);
    }

    /// <summary>
    /// The connection's writer lane: its lines, in the order they were asked for, written outside the link's lock, so neither a caller
    /// nor the evaluator ever waits on the socket. A command is written once the core has it written (<see cref="Take"/>); a write that
    /// breaks ends the connection, said as the core hears it; the evaluator ends one that has not finished within its deadline.
    /// </summary>
    private async Task WriteLaneAsync(Connection conn, CancellationToken end)
    {
        try
        {
            while (await conn.Lane.Reader.WaitToReadAsync(end))
            {
                while (conn.Lane.Reader.TryRead(out var line))
                {
                    if (Take(conn, line) is not { } bytes) return;
                    if (bytes.Length == 0) continue;   // a command the core settled while it waited here: never written
                    try
                    {
                        await conn.Stream.WriteAsync(bytes, end);
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or InvalidOperationException or OperationCanceledException)
                    {
                        Broke(conn);
                        return;
                    }
                    lock (_gate) conn.WritingSince = null;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The connection ended while the lane waited: what it had not written is not-sent, said as the connection ends.
        }
    }

    /// <summary>
    /// The lane's next line, as bytes to write now; none (empty) for a command the core no longer holds as queued — it settled while it
    /// waited here, not-sent with "it did not run", or was let go — which is never written, so those words stay true; or null when its
    /// connection is no longer the link's (it ended, or the core ended it), and nothing more is written. Under the lock: a command is
    /// written in the core from here, since a byte of it may reach the desk (a desk older than 1.1 echoes no id, so it is unconfirmed
    /// at once, 13.12.3); a ping's t1 is this moment's wall clock, kept by the core among the last it matches.
    /// </summary>
    private byte[]? Take(Connection conn, Outgoing o)
    {
        string? text = null;
        lock (_gate)
        {
            var now = Now();
            if (!ReferenceEquals(_conn, conn) || conn.End.IsCancellationRequested || !_m.KeepsConnection(now)) return null;
            if (o.Id is not { } id || _m.IsQueued(now, id))
            {
                if (o.Ping)
                {
                    _m.Ping(now, now.Wall);
                    text = conn.Bus11 ? "PING " + now.Wall.ToString(CultureInfo.InvariantCulture) : "PING";
                }
                else text = o.Line ?? "";
                if (o.Id is { } written)
                {
                    _m.Written(now, written);
                    if (!conn.Bus11 && PeerLink.ReadLine("OK") is { Reading: var noReceipt }) _m.Answer(now, written, noReceipt);
                }
                conn.WritingSince = now.Mono;
            }
            if (o.Id is not null)
            {
                TrackAnswers(now);
                _pub = Publish(now);
            }
        }
        SayAnswers();
        return text is null ? [] : Encoding.UTF8.GetBytes(text + "\n");
    }

    /// <summary>A write on the lane broke: the connection ends, and — unless something ended it first and said so — the link says why.</summary>
    private void Broke(Connection conn)
    {
        lock (_gate)
        {
            conn.WritingSince = null;
            if (!ReferenceEquals(_conn, conn) || conn.End.IsCancellationRequested) return;
            var now = Now();
            conn.Broke = true;
            End(conn);
            Ended(conn, now);
            TrackAnswers(now);
            _pub = Publish(now);
        }
        SayAnswers();
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

    private void Track(string id, string line, int mark)
    {
        _tracked[id] = new Tracked(line, Interlocked.Read(ref _ids.Value), mark);
        if (_tracked.Count <= PeerLink.ReceiptsKept) return;
        foreach (var old in _tracked.OrderBy(t => t.Value.Order).Take(_tracked.Count - PeerLink.ReceiptsKept).ToList())
        {
            _tracked.Remove(old.Key);
            LetGo(old.Key, old.Value);
        }
    }

    /// <summary>
    /// A command's outcome came or moved forward: its ticket's first outcome is this one if none was handed yet, and its final one when
    /// this one is not accepted (applied, refused, unconfirmed or not-sent). Handed out by <see cref="SayAnswers"/>, outside the lock.
    /// </summary>
    private void Ticket(string id, PlayoutAnswer answer)
    {
        if (!_tickets.TryGetValue(id, out var p)) return;
        if (!p.FirstSaid)
        {
            p.FirstSaid = true;
            _toComplete.Add((p.First, answer));
        }
        if (answer.Outcome == PeerOutcome.Accepted) return;
        _toComplete.Add((p.Final, answer));
        _tickets.Remove(id);
    }

    /// <summary>
    /// The link no longer follows a command (its core let it go, or the link stopped) while its ticket still waits: it hands the last
    /// outcome said, or, with none, unconfirmed — it may have reached the peer — so no waiter waits on a command nobody follows.
    /// </summary>
    private void LetGo(string id, Tracked t, string why = "the link no longer follows it")
    {
        if (!_tickets.ContainsKey(id)) return;
        Ticket(id, t.Last is { Outcome: not PeerOutcome.Accepted } said ? said
            : new PlayoutAnswer(id, t.Line, PeerOutcome.Unconfirmed, why + " — it may have run; look at " + _label + "'s state", t.Last?.Outcome, t.Last?.At ?? default, t.Mark,
                _readings));
    }

    /// <summary>
    /// Each command whose outcome came or moved forward since the last look, to be said once outside the lock, with the outcome it moved
    /// on from and the moment, on the link's clock.
    /// </summary>
    private void TrackAnswers(PeerNow now)
    {
        foreach (var (id, t) in _tracked.ToList())
        {
            var outcome = _m.OutcomeOf(id);
            if (outcome is { } o && o != t.Said)
            {
                var answer = new PlayoutAnswer(id, t.Line, o, Scrub(_m.WordsOf(id)), t.Said, DateTimeOffset.FromUnixTimeMilliseconds(now.Wall), t.Mark, _readings);
                _toSay.Add(answer);
                t.Said = o;
                t.Last = answer;
                Ticket(id, answer);
            }
            // Applied and refused are final; accepted and unconfirmed can still move, until the core no longer holds the command.
            if (outcome is PeerOutcome.Applied or PeerOutcome.Refused or PeerOutcome.NotSent || !_m.Knows(id))
            {
                _tracked.Remove(id);
                LetGo(id, t);
            }
        }
    }

    /// <summary>
    /// What the link has to tell its listeners — changes of its state, outcomes, and lines for the log — told outside its lock, in
    /// order: a change first, so the show log says the link dropped before the commands it left unconfirmed. One batch at a time, taken
    /// and told under <see cref="_telling"/>: a batch the reader made is never told after the timer's later one, so no listener hears
    /// "stale → live" before "live → stale", or an outcome's move before the outcome.
    /// </summary>
    private void SayAnswers()
    {
        lock (_telling)
        {
            List<PlayoutLinkChange> changed;
            List<PlayoutAnswer> said;
            List<string> logged;
            List<(TaskCompletionSource<PlayoutAnswer> Tcs, PlayoutAnswer Answer)> tickets;
            lock (_gate)
            {
                if (_toChange.Count == 0 && _toSay.Count == 0 && _toLog.Count == 0 && _toComplete.Count == 0) return;
                changed = [.. _toChange];
                said = [.. _toSay];
                logged = [.. _toLog];
                tickets = [.. _toComplete];
                _toChange.Clear();
                _toSay.Clear();
                _toLog.Clear();
                _toComplete.Clear();
            }
            foreach (var c in changed) Changed?.Invoke(c);
            foreach (var a in said) Answered?.Invoke(a);
            foreach (var line in logged) Logged?.Invoke(line);
            // A ticket's waiter hears its outcome after the listeners: the show log has its row before a receipt says it.
            foreach (var (tcs, answer) in tickets) tcs.TrySetResult(answer);
        }
    }

    private void Say(LinkHealth health, string words)
    {
        _saidHealth = health;
        _said = Scrub(words);
    }

    /// <summary>
    /// Peer words with this link's token taken out (13.19.1, 13.19.3): whatever the peer echoes — a service on the port that answers a
    /// plain AUTH with the whole line — reaches no status line, row or record with the token in it.
    /// </summary>
    private string Scrub(string words)
        => _token.Length == 0 || !words.Contains(_token, StringComparison.OrdinalIgnoreCase) ? words : words.Replace(_token, "(token)", StringComparison.OrdinalIgnoreCase);

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
    /// have said why it is down, the core's words stand in for the live link's. The step that changed what the link keeps across a
    /// restart keeps it here, before the lane writes or the link dials again (13.15.3), and the step that changed its state gives the
    /// show log its row (13.16.7).
    /// </summary>
    private Published Publish(PeerNow now)
    {
        var record = _m.Record(now);
        var state = _m.State(now);
        string Seconds(long? ms) => ((ms ?? 0) / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " s";
        var live = _saidHealth == LinkHealth.Ok;
        // A PlayMap node that lets this link only read (armed and remote-view-only) is amber with what its desk can do: the link stays
        // up and its commands still go, and the node's own ERR refuses them in its words.
        var readOnly = live && _whoami is { MayDrive: false };
        var waiting = TrustWait(now);
        // §2's conflict mark holds the link — no new dial — and is amber, as the core's light says (13.16.5); a held token is red.
        var conflict = state == PeerLinkState.Held && _m.Refusal(now) == "conflict";
        var status = state switch
        {
            PeerLinkState.Live when readOnly => _said + TlsNote(waiting) + $" · {Peer} is armed and lets this PC only read — its desk can SHOW SET REMOTE-VIEW-ONLY OFF",
            PeerLinkState.Live => _said + TlsNote(waiting),
            PeerLinkState.Stale => $"{_label} at {_host}:{_port} quiet for {Seconds(record.HeardMs)} — no countdown until it is heard again",
            PeerLinkState.Down when live => record.Words,
            PeerLinkState.Down => _said + (_noDoor ? " — looking again in " : " — dialling again in ") + Seconds(record.NextMs),
            PeerLinkState.Held when _saidHealth == LinkHealth.Error && !conflict => _said + " — the token is held; next try in " + Seconds(record.NextMs),
            PeerLinkState.Refused when waiting is { Changed: true } changed => ChangedWords(changed),
            PeerLinkState.Held or PeerLinkState.Refused => record.Words,
            _ when live => record.Words,
            _ => _said,
        };
        KeepIfChanged();
        if (state != _logged)
        {
            Change(now, _logged, state, status, record.Attempt);
            _logged = state;
        }
        if (_repeats > 0 && _repeatSince is { } since && now.Mono - since >= RepeatSummaryEvery.TotalMilliseconds) FoldRepeats(now);
        var node = _reading?.State.Id is { Length: > 0 } stateId ? stateId : _contextNode;
        return new Published(conflict ? LinkHealth.Starting : HealthOf(state, readOnly ? LinkHealth.Starting : _saidHealth), status, state, _conn?.Pairing ?? PlayoutPairing.None,
            _conn is not null, record, _m.Clock.Believed, _m.Clock.Apart, _reading, _context, _kindSeen, node, _whoami, _contextNode, waiting,
            _conn is { Tls: true }, _index, _conn is { } up ? up.Serial : 0);
    }

    /// <summary>
    /// The certificate that waits for a person now (under the lock), or null: after CERTIFICATE CHANGED the one shown, beside the pin
    /// it would replace; else a pin made <c>first</c> (13.8.2), which only a person makes theirs. Never a guess: a changed one is named
    /// only while the core refuses the link for it, and only by what the handshake showed.
    /// </summary>
    private PlayoutTrustWait? TrustWait(PeerNow now)
    {
        var pin = _m.Kept.Pin;
        if (_m.State(now) == PeerLinkState.Refused && _m.Refusal(now) == "certificate" && _m.CertificateShown is { } shown)
            return new PlayoutTrustWait(shown, true, pin?.Fingerprint ?? "", _m.NodeId);
        return pin is { Trust: PinTrust.First } first ? new PlayoutTrustWait(first.Fingerprint, false, "", first.NodeId ?? _m.NodeId) : null;
    }

    /// <summary>
    /// 13.8.3's words for a changed certificate: the one shown now and the one pinned, each by its short form, nothing sent, and where a
    /// person types at least <see cref="PlayoutRules.TrustHexMin"/> hex of the new one — compared on the node's own screen, never here.
    /// </summary>
    private string ChangedWords(PlayoutTrustWait changed)
        => $"CERTIFICATE CHANGED: {Peer} now shows {PlayoutWire.Short(changed.Fingerprint)} …"
           + (changed.Pinned.Length > 0 ? $", not the certificate pinned for it ({PlayoutWire.Short(changed.Pinned)} …)" : "")
           + $" — nothing sent; compare it on {_label}'s own screen, then type at least {PlayoutRules.TrustHexMin.ToString(CultureInfo.InvariantCulture)} hex of it on this desk ({PlayoutRules.TrustWhere})";

    /// <summary>
    /// What a live link over TLS adds to its words: the certificate it runs over, pinned by a person, or pinned first only — a pin kept
    /// for comparison, which proves nothing until a person compares it on the node's own screen and trusts it. Nothing on the line wire.
    /// </summary>
    private string TlsNote(PlayoutTrustWait? waiting)
    {
        if (_conn is not { Tls: true }) return "";
        var shown = _m.CertificateShown;
        if (waiting is { Changed: false } first && first.Fingerprint == shown)
            return $" · over TLS, its certificate {PlayoutWire.Short(first.Fingerprint)} … pinned first only — compare it on its own screen and trust it ({PlayoutRules.TrustWhere})";
        return _m.Kept.Pin is { Trust: not PinTrust.First } pin && pin.Fingerprint == shown ? " · over TLS, its certificate pinned" : " · over TLS";
    }

    /// <summary>
    /// 13.15.3: what the link keeps across a restart, kept when it changed — a hold the peer's refusal started, or one that ended — in
    /// this step, under the lock, so a later change is never kept before an earlier one; the keeper takes it at once and writes it on a
    /// lane of its own, never here. Should the write fail, the hold still holds until this Stagewatch stops, and the show log says so
    /// once.
    /// </summary>
    private void KeepIfChanged()
    {
        var kept = _m.Kept;
        if (kept != _kept)
        {
            _kept = kept;
            if (_o.Keep is { } keep)
            {
                var landed = keep(kept);
                if (!_keepFailed) _ = SayIfNotKeptAsync(landed);
            }
        }
        // The door beside the pin, after the pin it is kept with; none once there is no pin.
        var door = kept.Pin is null ? 0 : _door;
        if (door == _keptDoor) return;
        _keptDoor = door;
        _o.KeepDoor?.Invoke(door);
    }

    /// <summary>The keeper's write for a change, heard outside the lock: one that failed is said once in the show log.</summary>
    private async Task SayIfNotKeptAsync(Task<bool> landed)
    {
        if (await landed.ConfigureAwait(ConfigureAwaitOptions.ForceYielding)) return;
        lock (_gate)
        {
            if (_keepFailed) return;
            _keepFailed = true;
            _toLog.Add($"{_label} link: its hold could not be kept on this PC — it holds until Stagewatch stops, not across a restart");
        }
        SayAnswers();
    }

    /// <summary>
    /// A change of the link's state for the show log (13.16.7): its own row, unless the same change — from and to — was said within
    /// <see cref="RepeatSummaryEvery"/>: a redial going round, a link flapping live and stale or live and down (13.19.4), its coming live
    /// again among them. Such a repeat is folded into one summary row, said that long after the first repeat or just before the next
    /// row, so a link that flaps all show writes a few rows a minute, never one per flap; the summary names the last change it folds.
    /// </summary>
    private void Change(PeerNow now, PeerLinkState from, PeerLinkState to, string reason, int attempt)
    {
        var change = new PlayoutLinkChange(DateTimeOffset.FromUnixTimeMilliseconds(now.Wall), from, to, reason, attempt,
            Address: _host + ":" + _port.ToString(CultureInfo.InvariantCulture), NodeId: _m.NodeId);
        if (_changeSaid.TryGetValue((from, to), out var said) && now.Mono - said < RepeatSummaryEvery.TotalMilliseconds)
        {
            _repeats++;
            _repeatSince ??= now.Mono;
            _repeated.Add((from, to));
            _repeatLast = change;
            return;
        }
        FoldRepeats(now);
        _changeSaid[(from, to)] = now.Mono;
        _toChange.Add(change);
    }

    /// <summary>The repeats not said apart, as one summary row naming the last of them; each folded change counts as said now.</summary>
    private void FoldRepeats(PeerNow now)
    {
        if (_repeats > 0 && _repeatLast is { } last && _repeatSince is { } since)
        {
            _toChange.Add(last with { At = DateTimeOffset.FromUnixTimeMilliseconds(now.Wall), Folded = _repeats, FoldedMs = now.Mono - since });
            foreach (var pair in _repeated) _changeSaid[pair] = now.Mono;
        }
        _repeats = 0;
        _repeatSince = null;
        _repeatLast = null;
        _repeated.Clear();
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
            TrackAnswers(now);
            // A ticket still waiting on an answer the stopped link will never read is handed one now, so no waiter waits on nothing.
            foreach (var (id, t) in _tracked.ToList()) LetGo(id, t, "the link was stopped");
            _pub = Publish(now);
            // The link is stopped (Settings changed or turned it off, or Stagewatch is closing): its last row says so, and what it was.
            FoldRepeats(now);
            if (_logged != PeerLinkState.Off)
                _toChange.Add(new PlayoutLinkChange(DateTimeOffset.FromUnixTimeMilliseconds(now.Wall), _logged, PeerLinkState.Off, "the link was stopped",
                    _pub.Record.Attempt, Address: _host + ":" + _port.ToString(CultureInfo.InvariantCulture), NodeId: _m.NodeId));
            _logged = PeerLinkState.Off;
        }
        SayAnswers();
        // Its token source goes only with the loop that reads it: a loop still ending keeps it until the process does.
        if (_run is null || _run.IsCompleted) _stop.Dispose();
    }
}
