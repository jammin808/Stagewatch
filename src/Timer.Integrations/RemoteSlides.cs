using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>A presenter PC as Stagewatch sees it.</summary>
public sealed record PresenterPc(string Agent, string Name, string Machine, string Address, bool Presenting, string Presentation,
    int Slide, int Count, bool Connected, double AgeSeconds);

/// <summary>
/// Stagewatch's side of remote slides: the latest feed per presenter PC, their scripts (by hash), and which deck the
/// show follows — this PC's PowerPoint, a chosen presenter PC, or automatically whichever one is presenting.
/// Feeds arrive on web-server threads; reads come from the show thread. Latest-wins, lock-free reads.
/// </summary>
public sealed class RemoteDecks
{
    /// <summary>A presenter PC counts as gone when it hasn't reported for this long.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(4);

    private sealed record Entry(SlideFeed Feed, string Address, long AtTicks);

    private readonly ConcurrentDictionary<string, Entry> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _now;

    public RemoteDecks(Func<long>? now = null) => _now = now ?? Stopwatch.GetTimestamp;

    /// <summary>Take a feed. Returns true when Stagewatch wants this deck's script (it hasn't seen that hash).</summary>
    public bool Accept(SlideFeed feed, string address)
    {
        if (_feeds.Count > 64 && !_feeds.ContainsKey(feed.Agent)) return false;   // bounded
        var entry = new Entry(feed, address, _now());
        _feeds.AddOrUpdate(feed.Agent, entry, (_, old) => feed.Seq != 0 && feed.Seq < old.Feed.Seq && feed.Seq > old.Feed.Seq - 1_000_000 ? old : entry);
        return feed.ScriptHash.Length > 0 && !_scripts.ContainsKey(feed.ScriptHash);
    }

    /// <summary>Drop every stored script (what a restart does) — presenter PCs send them again when asked.</summary>
    public void ForgetScripts() => _scripts.Clear();

    public void AcceptScript(string hash, string script)
    {
        if (hash.Length == 0 || SlideFeed.HashScript(script) != hash) return;   // only the script that was announced
        if (_scripts.Count > 32) _scripts.Clear();
        _scripts[hash] = script;
    }

    public IReadOnlyList<PresenterPc> List()
    {
        var now = _now();
        return _feeds.Values
            .Select(e =>
            {
                var age = Stopwatch.GetElapsedTime(e.AtTicks, now);
                var f = e.Feed;
                return new PresenterPc(f.Agent, f.Name.Length > 0 ? f.Name : f.Machine, f.Machine, e.Address, f.Presenting, f.Presentation,
                    f.Slide, f.Count, age < StaleAfter, age.TotalSeconds);
            })
            .Where(p => p.AgeSeconds < 600)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The deck to follow. <paramref name="source"/>: "local" (or empty) = this PC; "auto" = a presenter PC that is
    /// presenting, the most recent one if several (else this PC); otherwise a presenter PC's agent id.
    /// </summary>
    public DeckState? Pick(string source, DeckState? local, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(source) || source == "local") return local;
        var now = _now();
        Entry? chosen = null;
        if (source == "auto")
        {
            chosen = _feeds.Values
                .Where(e => e.Feed.Presenting && Stopwatch.GetElapsedTime(e.AtTicks, now) < StaleAfter)
                .OrderBy(e => e.Feed.ShowMs)                      // the one that started presenting most recently
                .FirstOrDefault();
            if (chosen is null) return local;
        }
        else if (_feeds.TryGetValue(source, out var e) && Stopwatch.GetElapsedTime(e.AtTicks, now) < StaleAfter && e.Feed.Presenting)
            chosen = e;
        if (chosen is null) return null;

        var f = chosen.Feed;
        var since = Stopwatch.GetElapsedTime(chosen.AtTicks, now);   // time since the packet arrived
        _scripts.TryGetValue(f.ScriptHash, out var script);
        return new DeckState(f.Presentation, f.Slide, f.Count, f.NextTitle, f.Notes, f.Budgets,
            nowUtc - since - TimeSpan.FromMilliseconds(f.SlideMs), nowUtc - since - TimeSpan.FromMilliseconds(f.ShowMs), script ?? "");
    }
}

/// <summary>
/// "Who's here?" for Stagewatch and its presenter PCs (Show Bus §2): each says hello on UDP 9928 every two seconds where its doors
/// are bound to the network, and listens for the others. No configuration; nothing but names and ports goes on the wire (no PINs,
/// no slides). Each send is on its own, so a broadcast that fails never stops the copy to this machine, and what each beat did is
/// published whole (<see cref="Status"/>). What it hears is kept by <see cref="HelloHearing"/>.
/// </summary>
public sealed class SlidesBeacon : IDisposable
{
    public const int Port = 9928;

    /// <summary>How often a hello goes out.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(2);

    /// <summary>The largest datagram read as a hello; a longer one is dropped unread.</summary>
    public const int ReadBytes = 2048;

    /// <summary>The longest kind and id a heard hello may carry; a longer one drops the hello.</summary>
    public const int KindChars = 16, IdChars = 40;

    /// <summary>
    /// A heard hello keeps its first <see cref="KeptCaps"/> caps of §2's shape and <see cref="KeptPorts"/> doors; its name, machine
    /// and host are kept whole up to <see cref="NameChars"/> characters and cut there.
    /// </summary>
    public const int KeptCaps = 32, KeptPorts = 8, NameChars = ShowBusText.NameChars;

    /// <summary>The most ids held: a hello from a new id is dropped while this many are (<see cref="HasRoom"/>).</summary>
    public const int MaxPeers = 128;

    /// <summary>The most addresses kept for one id.</summary>
    public const int KeptAddresses = 4;

    /// <summary>An address, or a sender's inst, unheard this long is stale: the dialled address moves, and a switch is no longer close.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(6);

    /// <summary>A peer not heard for this long is forgotten.</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromSeconds(60);

    /// <summary>An id's conflict mark clears this long after the last alternation that set it.</summary>
    public static readonly TimeSpan ConflictClearsAfter = TimeSpan.FromSeconds(60);

    /// <summary>Whether a hello from a new id is taken while <paramref name="held"/> ids are held: the table holds at most <see cref="MaxPeers"/>.</summary>
    public static bool HasRoom(int held) => held < MaxPeers;

    public sealed record Hello(
        [property: JsonPropertyName("t")] string Kind,      // "main" (a Stagewatch), "agent" (a presenter PC), "patterns", "playmap", "messenger"…
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("machine")] string Machine,
        [property: JsonPropertyName("web")] int WebPort,
        // A Slides agent's one show fact (§2): said only while true, so no other node's hello carries it.
        [property: JsonPropertyName("presenting"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Presenting,
        /// <summary>What this node offers, for anything listening (Patterns, other tools): e.g. "slides", "timer", "screens".</summary>
        [property: JsonPropertyName("caps")] string[]? Caps = null,
        /// <summary>The Show Bus revision whose shapes this node speaks (§2): 3 for 1.3; 1.1 and 1.2 nodes sent 1.</summary>
        [property: JsonPropertyName("proto")] int Proto = 1,
        /// <summary>Show Bus 1.1: where to connect ("http", "ws", "wire", "osc", "companion").</summary>
        [property: JsonPropertyName("ports")] Dictionary<string, int>? Ports = null,
        /// <summary>Show Bus 1.1: the sender's Unix time in ms (a first, rough clock offset).</summary>
        [property: JsonPropertyName("clock")] long Clock = 0,
        [property: JsonPropertyName("host")] string? Host = null,
        /// <summary>Show Bus 1.2: "sha256:&lt;hex&gt;" of the node's TLS certificate (pinned by peers on first authenticated use).</summary>
        [property: JsonPropertyName("tls")] string? Tls = null,
        /// <summary>Show Bus 1.3: 8 lowercase hex, new at every start of the sender's process (one id from two of them is a conflict).</summary>
        [property: JsonPropertyName("inst")] string? Inst = null)
    {
        /// <summary>The node's web port: "ports.http", or the older "web". A reader's, never a field of the hello on the wire (§2 names its doors in "ports").</summary>
        [JsonIgnore]
        public int HttpPort => Ports is not null && Ports.TryGetValue("http", out var p) && p is > 0 and < 65536 ? p : WebPort;
    }

    /// <summary>A node kind or id as the Show Bus allows it: short, letters, digits, '-' and '_'.</summary>
    public static bool ValidToken(string? s, int max) => s is { Length: > 0 } && s.Length <= max && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// A node heard: its last hello; <see cref="Address"/>, the one to dial — kept while it is still heard, so it never flips between
    /// a node's addresses; how long since any hello; every address heard within <see cref="StaleAfter"/>, newest first; the
    /// certificate the last hello heard at each of them named; and the conflict mark while two running nodes say its id (null
    /// otherwise), under which no hello of it is taken to say whose certificate an address presents.
    /// </summary>
    public sealed record Peer(Hello Hello, string Address, double AgeSeconds)
    {
        public IReadOnlyList<string> Addresses { get; init; } = Array.Empty<string>();
        public HelloConflict? Conflict { get; init; }

        /// <summary>By address heard within <see cref="StaleAfter"/>: the <c>tls</c> its own last hello named (addresses whose hello named none are absent).</summary>
        public IReadOnlyDictionary<string, string> HeardTls { get; init; } = new Dictionary<string, string>();

        /// <summary>The certificate the hello heard at <paramref name="address"/> named, or null: never another address's.</summary>
        public string? TlsAt(string address) => HeardTls.TryGetValue(address, out var tls) ? tls : null;

        /// <summary>Whether <paramref name="address"/> is one this peer was heard from, or its host (a typed address matching a heard peer).</summary>
        public bool HeardAt(string address)
            => Addresses.Contains(address, StringComparer.OrdinalIgnoreCase) || string.Equals(Address, address, StringComparison.OrdinalIgnoreCase)
               || string.Equals(Hello.Host, address, StringComparison.OrdinalIgnoreCase) || string.Equals(Hello.Machine, address, StringComparison.OrdinalIgnoreCase);
    }

    private readonly Func<(Hello? Hello, HelloScope Scope)> _hello;
    private readonly int _port;
    private readonly IReadOnlyList<IPEndPoint>? _targets;
    private readonly UdpClient? _rx;
    private readonly UdpClient _tx = new() { EnableBroadcast = true };
    private readonly Timer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly HelloHearing _hearing;
    private readonly Dictionary<string, HelloTarget> _counts = new(StringComparer.Ordinal);   // the send's own, under _sending
    private volatile string? _ownId, _ownInst;
    private volatile HelloStatus _status = HelloStatus.NotYet;
    private int _sending;

    /// <summary>A hello that always goes to the network (a Slides agent's, which has no doors of its own to bind).</summary>
    public SlidesBeacon(Func<Hello?> hello, int port = Port) : this(() => (hello(), HelloScope.Network), port) { }

    /// <param name="hello">The hello and where it goes, asked for at every beat (on the beacon's timer thread).</param>
    /// <param name="port">The port it listens on, and sends to when <paramref name="targets"/> is not given.</param>
    /// <param name="targets">Where a hello of scope <see cref="HelloScope.Network"/> goes in place of §2's targets (a test, the rig's
    /// <c>--bus-to</c>), each an IPv4 address, as the socket that sends is; a hello of scope <see cref="HelloScope.None"/> goes nowhere
    /// whatever this says.</param>
    /// <param name="nowMs">The hearing's monotonic clock in milliseconds (tests inject one).</param>
    public SlidesBeacon(Func<(Hello? Hello, HelloScope Scope)> hello, int port = Port, IReadOnlyList<IPEndPoint>? targets = null, Func<long>? nowMs = null)
    {
        if (targets?.Any(t => t.AddressFamily != AddressFamily.InterNetwork) == true)
            throw new ArgumentException("a hello goes to IPv4 addresses only, as §2's targets are", nameof(targets));
        _hello = hello;
        _port = port;
        _targets = targets;
        _hearing = new HelloHearing(nowMs);
        try
        {
            var rx = new UdpClient(AddressFamily.InterNetwork);
            rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            rx.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _rx = rx;
            _ = Task.Run(() => ListenAsync(rx, _cts.Token));
            Listening = true;
        }
        catch (SocketException) { Listening = false; }
        _timer = new Timer(_ => Send(), null, TimeSpan.FromMilliseconds(200), Every);
    }

    public bool Listening { get; }

    /// <summary>What the last beat did with the hello, published whole; any thread reads it.</summary>
    public HelloStatus Status => _status;

    /// <summary>Another machine saying this node's own id (§2), while its mark stands; null otherwise (<see cref="HelloHearing.OwnConflict"/>).</summary>
    public HelloConflict? OwnConflict => _hearing.OwnConflict();

    /// <summary>
    /// One beat: the hello to each target, each send on its own, so a target that fails never stops the next; then what the beat
    /// did. A beat still running when the next is due is not overlapped.
    /// </summary>
    private void Send()
    {
        if (Interlocked.Exchange(ref _sending, 1) == 1) return;
        try
        {
            var (hello, scope) = _hello();
            if (hello is not null)
            {
                _ownInst = hello.Inst;
                _ownId = hello.Id;
            }
            if (hello is null || scope != HelloScope.Network)
            {
                Beat(HelloSaid.Silent, "", scope, hello?.HttpPort ?? 0);
                return;
            }
            var bytes = ShowBusHello.Bytes(hello);
            var sends = new List<(IPEndPoint, bool)>();
            string? failure = null;
            foreach (var target in _targets ?? ShowBusHello.Targets(scope, _port))
            {
                var key = target.ToString();
                var count = _counts.TryGetValue(key, out var c) ? c : new HelloTarget(key, IPAddress.IsLoopback(target.Address), 0, 0, "");
                try
                {
                    _tx.Send(bytes, bytes.Length, target);
                    _counts[key] = count with { Sent = count.Sent + 1 };
                    sends.Add((target, true));
                }
                catch (SocketException ex)
                {
                    _counts[key] = count with { Failed = count.Failed + 1, LastFailure = ex.Message };
                    failure ??= ex.Message;
                    sends.Add((target, false));
                }
            }
            Beat(ShowBusHello.Said(sends), failure ?? "", scope, hello.HttpPort);
        }
        catch (ObjectDisposedException) { /* stopping: the beacon is being disposed */ }
        finally { Volatile.Write(ref _sending, 0); }
    }

    private void Beat(HelloSaid said, string words, HelloScope scope, int webPort)
    {
        var last = _status;
        _status = new HelloStatus(said, last.Said == said ? last.Beats + 1 : 1, words, _counts.Values.ToList()) { Scope = scope, WebPort = webPort };
    }

    private async Task ListenAsync(UdpClient rx, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await rx.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            try
            {
                // Every kind of show node is heard (Show Bus §2); presenter PCs still need a valid feed id. A hello of our own id is
                // never a peer: with our inst it is our own echo; once we say an inst, one with another (or none) is another process
                // saying our id — a conflict.
                if (ShowBusHello.Read(r.Buffer) is not { } h) continue;
                var from = r.RemoteEndPoint.Address.ToString();
                if (h.Id != _ownId) _hearing.Hear(h, from);
                else if (_ownInst is { } mine && h.Inst != mine) _hearing.HearOwn(h, from);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Anything on the network can send us anything: a bad packet is dropped, and we keep listening.
            }
        }
    }

    /// <summary>Everyone heard in the last minute (<see cref="HelloHearing.Peers"/>).</summary>
    public IReadOnlyList<Peer> Peers() => _hearing.Peers();

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _rx?.Dispose();
        _tx.Dispose();
    }
}
