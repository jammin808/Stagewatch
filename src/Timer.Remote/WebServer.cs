using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// Embedded web server: browser viewer, browser remote control, JSON/HTTP API and a
/// WebSocket push channel. Built on a raw socket rather than HttpListener so it needs
/// no admin rights / URL ACLs and no extra runtime.
///
/// Network model (game-server style):
///   * The app is the single authority. Remotes send commands; they never hold state.
///   * State goes out as whole snapshots, latest-wins. Each socket has a one-slot
///     mailbox, so a slow or stalled client just skips frames — it can never queue up
///     memory or slow anyone else down. A client that can't take a frame in 5 s is dropped.
///   * Each snapshot is serialized once, lazily, however many clients are connected.
/// </summary>
public sealed class WebServer : IAsyncDisposable
{
    /// <summary>The connections this server serves at once; past it a connection hears <see cref="Full"/> before it closes.</summary>
    public const int MaxConnections = 64;

    /// <summary>
    /// Connections past <see cref="MaxConnections"/> answered at once with <see cref="Full"/> (13.14: a ceiling said in words before an
    /// extra connection closes); past these too, one is closed unanswered, so a flood never holds more than this many more sockets.
    /// </summary>
    public const int ShedAnswered = 16;

    /// <summary>How long a connection past the ceiling has to send its request's head (and its TLS handshake) before it is closed unanswered.</summary>
    public static readonly TimeSpan ShedWithin = TimeSpan.FromSeconds(2);

    /// <summary>The Retry-After, in seconds, a connection past the ceiling is answered with.</summary>
    public const int FullRetryAfter = 5;

    /// <summary>The words a connection past the ceiling hears, with 429 and <see cref="FullRetryAfter"/>.</summary>
    public static readonly string Full = "this Stagewatch is serving its " + MaxConnections.ToString(CultureInfo.InvariantCulture)
        + " connections — try again in " + FullRetryAfter.ToString(CultureInfo.InvariantCulture) + " s";
    private const int MaxHeaderBytes = 8192;
    /// <summary>The largest message a /ws client may send; a longer one ends the socket.</summary>
    public const int MaxWsMessageBytes = 4096;
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly IShowControl _control;
    private readonly Func<string?> _pin;
    private readonly ConcurrentDictionary<WsClient, byte> _sockets = new();

    /// <summary>Wrong PINs a device may give in a minute; then the PIN and every key are refused it for the rest of that minute.</summary>
    public const int WrongPinsPerMinute = 5;

    /// <summary>
    /// Show Bus §14 <c>view.wrong.per.minute</c>: wrong view passes — window addresses, viewing codes and crew links — a device may
    /// give in a minute; then every view pass is refused it for the rest of that minute (12.2).
    /// </summary>
    public const int WrongViewPassesPerMinute = 5;

    /// <summary>
    /// The words for a device locked out of view passes, wherever it gives one (a page, a fetch, a crew link's confirmation).
    /// </summary>
    public const string PassesLocked = "too many wrong viewing codes, window addresses or crew links from this device — wait a minute";

    /// <summary>The words for a device locked out of the PIN and every key.</summary>
    public const string PinsLocked = "too many wrong PINs — wait a minute";

    /// <summary>
    /// Wrong PINs per device. Only the right PIN or its minute's end clears it: a key in force never does, so a key holder cannot
    /// go on guessing the PIN below the limit from its address.
    /// </summary>
    private readonly WrongGuesses _pinGuesses = new(WrongPinsPerMinute);

    /// <summary>
    /// Wrong view passes per device, a count of their own (Show Bus 12.2): nothing clears it but its minute's end, so a right PIN,
    /// a key or a right view pass never lets a device go on guessing, and wrong view passes never lock out the PIN or a key.
    /// </summary>
    private readonly WrongGuesses _passGuesses = new(WrongViewPassesPerMinute);

    /// <summary>Show Bus §14 <c>peerlink.wrong.proofs.per.minute</c>: wrong LINK proofs a (device, kid) pair may give in a minute (13.5.6).</summary>
    public const int WrongProofsPerMinute = 5;

    /// <summary>
    /// Show Bus §14 <c>peerlink.unpaired.per.address</c>: <c>link=</c> sockets one device may hold that have not finished LINK; one more
    /// is answered 429 before its upgrade (13.6.3).
    /// </summary>
    public const int UnpairedPerAddress = 4;

    /// <summary>The words a device holding <see cref="UnpairedPerAddress"/> unfinished LINK sockets hears for one more.</summary>
    public const string LinkBusy = "too many links from this device have not finished proving themselves — wait";

    /// <summary>
    /// The words for a <c>link=</c> socket in clear that is not local (13.3.2, 13.3.4): another machine links only over TLS, so no
    /// session credential can ride a plain side call.
    /// </summary>
    public const string LinkNeedsTls = "another machine links to this Stagewatch only over TLS (wss://) — turn HTTPS on in Settings → Remote";

    /// <summary>
    /// Wrong LINK proofs per (device, kid), a table of its own (Show Bus 13.5.6): only a key in force is counted, never an unknown kid,
    /// and it locks out no PIN, viewing code, view pass, other key or browser.
    /// </summary>
    private readonly WrongProofs _proofGuesses = new(WrongProofsPerMinute);

    /// <summary>Per device: its <c>link=</c> sockets upgraded or about to be that have not finished LINK (13.6.3).</summary>
    private readonly ConcurrentDictionary<IPAddress, int> _unpaired = new();

    /// <summary>
    /// Every live verified LINK socket by its lid, with its session credential S (13.7.1): made with the verdict, gone with the socket,
    /// and ended at once when its S comes in clear from another machine.
    /// </summary>
    private readonly ConcurrentDictionary<string, LinkSession> _sessions = new(StringComparer.Ordinal);

    private sealed record LinkSession(string Session, WsClient Client)
    {
        /// <summary>Never the session credential (13.19): a log line or an exception that names a session says only its socket.</summary>
        public override string ToString() => "LinkSession { " + Client.Link.Lid + " }";
    }

    /// <summary>This server's own inst, named in a verdict when the node says none (Show Bus §2's shape: 8 lowercase hex).</summary>
    private readonly string _ownInst = RandomNumberGenerator.GetHexString(8, lowercase: true);
    /// <summary>Per API key: requests this minute, when the minute started, and when it was last used.</summary>
    private readonly ConcurrentDictionary<string, (int Count, long WindowStartMs, DateTime LastUtc)> _keyUse = new();
    private readonly SemaphoreSlim _connectionSlots = new(MaxConnections, MaxConnections);
    private readonly SemaphoreSlim _shedSlots = new(ShedAnswered, ShedAnswered);
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Frame? _frame;
    private Frame? _detailFrame;
    private Frame? _operatorFrame;
    private (int Version, byte[] Json)? _config;
    private readonly ConcurrentDictionary<IPAddress, (int Count, long WindowStartMs, long LastMs)> _questionRate = new();
    private readonly McpEndpoint _mcp;

    private sealed record Frame(ShowSnapshot Snapshot, byte[] Json);

    /// <param name="pin">Read on every control request, so a PIN change applies immediately.</param>
    public WebServer(IShowControl control, Func<string?> pin)
    {
        _control = control;
        _pin = pin;
        _mcp = new McpEndpoint(control, () => CurrentFrameJson(FrameAccess.Operator), ServedPreflight);
    }

    /// <summary>
    /// The room's preflight as every door of this server gives it (GET /api/preflight, MCP get_preflight): the host's, or the stuck
    /// answer while the show thread is stuck or the host's verdict has gone stale (<see cref="PreflightWire.Served"/>); null when
    /// the host keeps none. Any thread.
    /// </summary>
    public string? ServedPreflight()
        => PreflightWire.Served(_control.PreflightJson(), _control.Snapshots.ProducerStuck, PreflightClock.GetUtcNow().ToUnixTimeMilliseconds());

    /// <summary>
    /// The clock a verdict's age is read on (<see cref="PreflightWire.MaxAge"/>): the system's, or a manual one that ages a verdict
    /// without waiting. Fixed when the server is made.
    /// </summary>
    public TimeProvider PreflightClock { get; init; } = TimeProvider.System;

    /// <summary>
    /// 13.12.6's record of what each LINK-verified connector's ids came to, read and kept by every socket's reader. It answers for the
    /// node's inst, so it lives as long as that inst: a host that rebuilds its server under one inst (the desk, on a Remote settings
    /// change) hands each server the one record, or a relinked connector would hear "unknown" for ids this inst ran, and a repeat would
    /// run again. Fixed when the server is made; its own by default.
    /// </summary>
    public ReceiptRecord Receipts { get; init; } = new();

    public bool IsRunning => _listener is not null;

    /// <summary>
    /// The clock LINK is timed on: the deadline of a <c>link=</c> socket's one link message (§14 <c>peerlink.handshake.ms</c>) and the
    /// minutes of the wrong-proof table. The system's, or a manual one that reaches them without waiting. Fixed when the server is made.
    /// </summary>
    public TimeProvider LinkClock { get; init; } = TimeProvider.System;

    private long LinkNowMs => (long)LinkClock.GetElapsedTime(0, LinkClock.GetTimestamp()).TotalMilliseconds;

    /// <summary>The inst a PeerLink verdict names (13.6.6): the node's hello inst, or this server's own.</summary>
    private string Inst => _control.OwnInst is { Length: 8 } inst && PeerLink.IsHex(inst, 8) ? inst : _ownInst;

    /// <summary>
    /// Whether a request is from this machine as a PeerLink acceptor may trust it (13.1, 13.3.4): its socket's address is loopback, its
    /// Host names this server there, and no page of another site sent it — a rebound name, a forwarder's port or a foreign Origin is
    /// another machine's.
    /// </summary>
    private bool IsLocal(HttpRequest req, IPAddress remote)
    {
        var host = req.Headers.GetValueOrDefault("host");
        return IsThisPc(remote) && PlayoutGate.NamesThisPc(host, Port)
               && !PlayoutGate.OtherSite(req.Headers.GetValueOrDefault("origin"), host, req.Headers.GetValueOrDefault("sec-fetch-site"));
    }

    /// <summary>
    /// This install's certificate (Show Bus §10). With one, the port also speaks TLS: a connection whose first byte is a
    /// TLS handshake is encrypted, any other is plain HTTP — one port, both kinds of client.
    /// </summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? Certificate { get; set; }

    /// <summary>Refuse plain HTTP and WebSockets from other machines (this machine and the certificate pages still work).</summary>
    public bool RequireHttps { get; set; }

    /// <summary>
    /// The clock an oversize body's drain is timed on (<see cref="HttpRequest.DrainFor"/>): the system's, or a manual one that reaches
    /// the drain's deadline without waiting it out. Fixed when the server is made.
    /// </summary>
    public TimeProvider DrainClock { get; init; } = TimeProvider.System;

    /// <summary>The certificate's fingerprint ("sha256:…"), or null without one.</summary>
    public string? Fingerprint => Certificate is { } c ? TlsPins.Fingerprint(c.RawData) : null;

    /// <summary>
    /// The local-address seam of the playout gate (<see cref="PlayoutGate"/>): whether a sender is this PC (loopback, an IPv4-mapped one
    /// included). Fixed when the server is made; a test plays a sender on another machine from loopback with it. Nothing else here
    /// reads it.
    /// </summary>
    public Func<IPAddress, bool> IsThisPc { get; init; } = PlayoutGate.IsLoopback;

    /// <summary>
    /// The playout gate's sender for a web request (HTTP, the /ws upgrade, MCP): this PC only from a loopback address under a Host that
    /// names this server there (<see cref="PlayoutGate.NamesThisPc"/>: a rebound name or a forwarder's port is another machine's), and a
    /// page of another site said by the browser's own headers (<see cref="PlayoutGate.OtherSite"/>), which proves nothing.
    /// </summary>
    private CommandSender WebSender(HttpRequest req, IPAddress remote, bool proved)
    {
        var host = req.Headers.GetValueOrDefault("host");
        var thisPc = IsThisPc(remote) && PlayoutGate.NamesThisPc(host, Port);
        var otherSite = PlayoutGate.OtherSite(req.Headers.GetValueOrDefault("origin"), host, req.Headers.GetValueOrDefault("sec-fetch-site"));
        return new CommandSender(remote, thisPc, proved, otherSite);
    }
    public int Port { get; private set; }

    /// <summary>The door as <see cref="Start"/> bound it, published whole once the listener runs; null while it does not.</summary>
    public BoundDoor? Door => _door;
    private volatile BoundDoor? _door;
    public int ConnectedClients => _sockets.Count;

    // ------------------------------------------------------------------ screens

    /// <summary>
    /// Every connected browser screen, grouped by its id (tabs of one browser count as one screen), and every claim (a
    /// socket that named a node and proved nothing), flagged. A show app's node link is never a screen. Thread-safe.
    /// </summary>
    public IReadOnlyList<ScreenInfo> ListScreens()
        => _sockets.Keys.Where(c => c.WindowId is null).Select(c => (Client: c, Link: c.Link)).Where(x => x.Link.Class != WsClass.Node)
            .GroupBy(x => x.Link.ScreenId)
            .Select(g =>
            {
                var first = g.OrderBy(x => x.Client.ConnectedUtc).First();
                var name = _control.ScreenName(g.Key) ?? "";
                // A claim's id is one no browser can take (Screens.ValidBrowserId); a group is still flagged if any member claims.
                var claim = g.Select(x => x.Link.Claim).FirstOrDefault(c => c.Length > 0) ?? "";
                return new ScreenInfo(g.Key, name, first.Client.Page, Screens.AddressOf(first.Client.Remote), first.Client.Device, first.Client.ConnectedUtc,
                    g.All(x => x.Link.CanControl), g.Count(), claim);
            })
            .OrderBy(s => s.Control).ThenBy(s => s.Name.Length == 0).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.ConnectedUtc)
            .ToList();

    /// <summary>
    /// Every show app's node link on /ws: grouped by the id it named and what speaks for it (two installs are two rows, and
    /// a key shared by both is one verified row and one that is not), or by its key when it named no id. Thread-safe.
    /// </summary>
    public IReadOnlyList<NodeLinkFact> ListNodes()
    {
        var keys = _control.ApiKeys;
        return _sockets.Keys.Where(c => c.WindowId is null).Select(c => (Client: c, Link: c.Link)).Where(x => x.Link.Class == WsClass.Node)
            .GroupBy(x => (x.Link.Node, x.Link.KeyId, x.Link.Verified))
            .Select(g =>
            {
                var first = g.OrderBy(x => x.Client.ConnectedUtc).First();
                var keyName = first.Link.KeyId is { } id ? keys.FirstOrDefault(k => k.Id == id)?.Name ?? "" : "";
                return new NodeLinkFact(first.Link.Kind ?? "", first.Link.Node, first.Link.Verified, first.Link.KeyId, keyName,
                    Screens.AddressOf(first.Client.Remote), first.Client.ConnectedUtc, g.Count(), first.Link.Words,
                    g.Count(x => x.Link.Lid is not null), g.Sum(x => x.Client.Oversize), g.OrderBy(x => x.Client.ConnectedUtc).Last().Client.Can);
            })
            .OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Id ?? "", StringComparer.Ordinal).ThenBy(n => n.ConnectedUtc)
            .ToList();
    }

    /// <summary>Every socket on /ws by what it is: a browser, a window's link, a show app's node link or a claim. Thread-safe.</summary>
    public WsCensus Census()
    {
        int browsers = 0, windows = 0, apps = 0, claims = 0;
        foreach (var c in _sockets.Keys)
        {
            if (c.WindowId is not null) { windows++; continue; }
            switch (c.Link.Class)
            {
                case WsClass.Node: apps++; break;
                case WsClass.Claim: claims++; break;
                default: browsers++; break;
            }
        }
        return new WsCensus(browsers, windows, apps, claims);
    }

    /// <summary>Send screens to another page on this Stagewatch. Returns how many browser tabs were told. Never blocks.</summary>
    public int Redirect(IEnumerable<string> screenIds, string path)
    {
        if (!ScreenPreset.ValidPath(path)) return 0;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { type = "redirect", to = path });
        return Push(screenIds, payload);
    }

    /// <summary>Make screens reload their page (after a design change, or a browser that's misbehaving).</summary>
    public int Reload(IEnumerable<string> screenIds) => Push(screenIds, "{\"type\":\"reload\"}"u8.ToArray());

    /// <summary>
    /// Disconnect screens: they're told to stay off (no automatic reconnect) and their sockets close. The page says so,
    /// and reloading it reconnects — this is for a stray tablet, not a security wall (that's the viewing code). A show
    /// app's node link is never disconnected here, whatever id is passed (revoking its key is the wall).
    /// </summary>
    public int Disconnect(IEnumerable<string> screenIds)
    {
        var ids = new HashSet<string>(screenIds, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var c in _sockets.Keys)
        {
            if (c.Link.Class == WsClass.Node || !ids.Contains(c.ScreenId)) continue;
            n++;
            _ = Task.Run(async () =>
            {
                try { await c.SendTextAsync("{\"type\":\"disconnect\"}"u8.ToArray(), CancellationToken.None); } catch { }
                await Task.Delay(300);
                c.Abort();
            });
        }
        return n;
    }

    /// <summary>Show a screen's name and address full screen for a few seconds, so you can find it in the room.</summary>
    public int Identify(IEnumerable<string> screenIds)
    {
        var n = 0;
        foreach (var id in screenIds.Distinct())
        {
            var name = _control.ScreenName(id) ?? "";
            n += Push(new[] { id }, JsonSerializer.SerializeToUtf8Bytes(new { type = "identify", name, id }));
        }
        return n;
    }

    /// <summary>A screen message to the sockets with these screen ids. Never to a show app's node link, whatever id is passed.</summary>
    private int Push(IEnumerable<string> screenIds, byte[] payload)
    {
        var ids = new HashSet<string>(screenIds, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var c in _sockets.Keys)
        {
            if (c.Link.Class == WsClass.Node || !ids.Contains(c.ScreenId)) continue;
            n++;
            _ = Task.Run(async () => { try { await c.SendTextAsync(payload, CancellationToken.None); } catch { } });
        }
        return n;
    }

    // ------------------------------------------------------------------ windows (controlled browser windows, /w/<token>)

    /// <summary>What each window last said it shows, kept after its link drops (so a lost window reads "link lost").</summary>
    private readonly ConcurrentDictionary<string, WindowObserved> _windowSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// The most viewers' reports kept (12.10): one per window for its own address and one per grant, <see cref="ScreenWindows.MaxGrants"/>
    /// of them, for each of <see cref="ScreenWindows.Max"/> windows. Reports of a window or grant that is gone are dropped one by one,
    /// never all at once, so the table never holds more than the windows and grants there are.
    /// </summary>
    public const int MaxViewers = (ScreenWindows.MaxGrants + 1) * ScreenWindows.Max;

    /// <summary>What each viewer of a window last said it shows, by (window id, via): kept after its link drops, bounded by <see cref="MaxViewers"/>.</summary>
    private readonly ConcurrentDictionary<(string Window, string Via), ViewerObserved> _viewerSeen = new();

    /// <summary>How many viewers' reports are kept now (never more than <see cref="MaxViewers"/>). Thread-safe.</summary>
    public int ViewersKept => _viewerSeen.Count;

    /// <summary>
    /// The windows changed (owner thread): each window's link and each show app's node link re-reads its own on its next turn, latest
    /// wins. A window whose address was revoked or deleted is told so and let go, a page grant's black; an app's link hears its grants
    /// as they now are. The reports of viewers whose window or grant is gone are dropped. Never blocks.
    /// </summary>
    public void PushWindows()
    {
        DropGoneViewers();
        foreach (var c in _sockets.Keys)
            if (c.WindowId is not null || c.Link.Class == WsClass.Node) c.Signal();
    }

    /// <summary>Whether a viewer's report still has a window and a pass to be about: the window's own address, or a grant of it to that node.</summary>
    private static bool ViewerHolds(IReadOnlyList<ScreenWindow> windows, (string Window, string Via) key)
        => windows.FirstOrDefault(w => w.Id == key.Window) is { } w && (key.Via.Length == 0 || (w.Grants ?? []).Any(g => g?.Node == key.Via));

    private void DropGoneViewers()
    {
        var windows = _control.Windows;
        foreach (var key in _viewerSeen.Keys)
            if (!ViewerHolds(windows, key)) _viewerSeen.TryRemove(key, out _);
    }

    /// <summary>
    /// Keeps one viewer's report when its window and pass are there now. A new viewer past <see cref="MaxViewers"/> first drops those that
    /// are gone, and is kept only when there is then room: the table is bounded, and nobody's report is cleared to make room.
    /// </summary>
    private void KeepViewer(ViewerObserved seen)
    {
        var key = (seen.WindowId, seen.Via);
        if (!ViewerHolds(_control.Windows, key)) return;
        if (!_viewerSeen.ContainsKey(key) && _viewerSeen.Count >= MaxViewers)
        {
            DropGoneViewers();
            if (_viewerSeen.Count >= MaxViewers) return;
        }
        _viewerSeen[key] = seen;
    }

    /// <summary>The node links a key bound to their node proves (12.5's first point), each with that node: the only links grants reach.</summary>
    private IEnumerable<(WsClient Client, string Node)> GrantLinks()
    {
        foreach (var c in _sockets.Keys)
            if (c.WindowId is null && GrantsNode(c.Link) is { } node) yield return (c, node);
    }

    /// <summary>
    /// Show windows' names full screen for a few seconds: on every link on the window's addresses, and on each show app's link that draws
    /// it (12.6's <c>window-identify</c>). Returns how many links were told.
    /// </summary>
    public int IdentifyWindows(IEnumerable<string> windowIds)
    {
        var n = 0;
        foreach (var id in windowIds.Distinct())
        {
            var name = _control.Windows.FirstOrDefault(w => w.Id == id)?.Name ?? "";
            n += PushToWindows(new[] { id }, IdentifyJson(name, id));
            var drawn = WindowGrants.IdentifyJson(id, name);
            foreach (var (c, _) in GrantLinks())
            {
                if (!c.Drawing.Contains(id)) continue;
                n++;
                _ = Task.Run(async () => { try { await c.SendTextAsync(drawn, CancellationToken.None); } catch { } });
            }
        }
        return n;
    }

    /// <summary>What a window hears when its operator asks it to show its name (Show Bus 12.3).</summary>
    public static byte[] IdentifyJson(string name, string id) => JsonSerializer.SerializeToUtf8Bytes(new { type = "identify", name, id });

    /// <summary>
    /// Make windows reload themselves (they come back to what they were showing): every link on their addresses. A show app that draws a
    /// window has no page to reload, and hears nothing (12.6).
    /// </summary>
    public int ReloadWindows(IEnumerable<string> windowIds) => PushToWindows(windowIds, Encoding.UTF8.GetBytes(ReloadJson));

    /// <summary>What a window hears when its operator reloads it (Show Bus 12.3).</summary>
    public const string ReloadJson = "{\"type\":\"reload\"}";

    private int PushToWindows(IEnumerable<string> windowIds, byte[] payload)
    {
        var ids = new HashSet<string>(windowIds, StringComparer.Ordinal);
        var n = 0;
        foreach (var c in _sockets.Keys)
        {
            if (c.WindowId is null || !ids.Contains(c.WindowId)) continue;
            n++;
            _ = Task.Run(async () => { try { await c.SendTextAsync(payload, CancellationToken.None); } catch { } });
        }
        return n;
    }

    /// <summary>How many show apps' links draw each window now (a draw grant assigned on its app's key-verified link), by window id.</summary>
    private Dictionary<string, int> DrawnCounts()
    {
        var drawn = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (c, _) in GrantLinks())
            foreach (var id in c.Drawing)
                drawn[id] = drawn.GetValueOrDefault(id) + 1;
        return drawn;
    }

    /// <summary>
    /// Every window's links and what it last said it shows: one row per window, over every link on any of its addresses, and how many
    /// show apps' links draw it (<see cref="WindowObserved.Drawn"/>). Thread-safe.
    /// </summary>
    public IReadOnlyList<WindowObserved> WindowStatus()
    {
        var live = _sockets.Keys.Where(c => c.WindowId is not null).GroupBy(c => c.WindowId!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var drawn = DrawnCounts();
        var ids = new HashSet<string>(_windowSeen.Keys, StringComparer.Ordinal);
        ids.UnionWith(live.Keys);
        ids.UnionWith(drawn.Keys);
        var list = new List<WindowObserved>(ids.Count);
        foreach (var id in ids)
        {
            var seen = _windowSeen.GetValueOrDefault(id);
            var clients = live.GetValueOrDefault(id);
            var newest = clients?.OrderByDescending(c => c.ConnectedUtc).First();
            list.Add(new WindowObserved(id, clients?.Count ?? 0, seen?.Revision ?? 0, seen?.Content ?? "", seen?.Hold ?? false, seen?.SwapMs ?? 0, seen?.ShownUtc,
                seen?.Error ?? "", newest?.Device ?? seen?.Device ?? "", newest is null ? seen?.Address ?? "" : Screens.AddressOf(newest.Remote),
                seen?.Via ?? "", drawn.GetValueOrDefault(id)));
        }
        return list;
    }

    /// <summary>
    /// Every viewer of every window (12.10): one row per (window, via) — its own address, each page grant's address, each app's link that
    /// draws it — with how many links hold it that way now and what that viewer last said it shows. Thread-safe.
    /// </summary>
    public IReadOnlyList<ViewerObserved> ViewerStatus()
    {
        var windows = _control.Windows;
        var live = _sockets.Keys.Where(c => c.WindowId is not null).GroupBy(c => (Window: c.WindowId!, Via: c.WindowVia ?? ""))
            .ToDictionary(g => g.Key, g => g.ToList());
        var drawing = new Dictionary<(string Window, string Via), int>();
        foreach (var (c, node) in GrantLinks())
            foreach (var id in c.Drawing)
                drawing[(id, node)] = drawing.GetValueOrDefault((id, node)) + 1;
        var keys = new HashSet<(string Window, string Via)>(_viewerSeen.Keys);
        keys.UnionWith(live.Keys);
        keys.UnionWith(drawing.Keys);
        var list = new List<ViewerObserved>(keys.Count);
        foreach (var key in keys.Where(k => ViewerHolds(windows, k)))
        {
            var seen = _viewerSeen.GetValueOrDefault(key);
            var how = key.Via.Length == 0 ? "" : windows.First(w => w.Id == key.Window).Grants.First(g => g?.Node == key.Via).How;
            var clients = live.GetValueOrDefault(key);
            var newest = clients?.OrderByDescending(c => c.ConnectedUtc).First();
            var connected = how == ScreenWindows.HowDraw ? drawing.GetValueOrDefault(key) : clients?.Count ?? 0;
            list.Add(new ViewerObserved(key.Window, key.Via, how, connected, seen?.Revision ?? 0, seen?.Content ?? "", seen?.Hold ?? false, seen?.SwapMs ?? 0,
                seen?.ShownUtc, seen?.Error ?? "", newest?.Device ?? seen?.Device ?? "", newest is null ? seen?.Address ?? "" : Screens.AddressOf(newest.Remote)));
        }
        return list;
    }

    /// <summary>
    /// Every grant of every window as its app's links carry it (12.5–12.6): whether the app is linked by its own key-verified link and by
    /// what, whether that link was given the window, how many of its viewers are on it, and where that leaves it in words. Never a path.
    /// Thread-safe.
    /// </summary>
    public IReadOnlyList<GrantObserved> GrantStatus()
    {
        var windows = _control.Windows;
        var links = GrantLinks().ToList();
        var viewers = ViewerStatus().ToDictionary(v => (v.WindowId, v.Via));
        var list = new List<GrantObserved>();
        foreach (var w in windows)
        {
            foreach (var g in w.Grants ?? [])
            {
                if (g is null) continue;
                var newest = links.Where(l => l.Node == g.Node).Select(l => l.Client).OrderByDescending(c => c.ConnectedUtc).FirstOrDefault();
                var sent = newest?.Grants;
                var row = newest is null ? null
                    : sent?.Rows.FirstOrDefault(r => r.Window == w.Id)
                      ?? WindowGrants.For([w], g.Node, newest.LinkWord, newest.Can, !_control.Sightings.MayGrant(newest.Link.KeyId ?? "")).FirstOrDefault();
                var acknowledged = sent is not null && newest!.SeenRev == sent.Rev && sent.Rows.Any(r => r.Window == w.Id);
                var viewer = viewers.GetValueOrDefault((w.Id, g.Node));
                // A page grant's viewers are the links on its address; a draw grant's, its app's links that draw it once one has reported.
                var open = viewer is null ? 0 : g.How == ScreenWindows.HowDraw && viewer.ShownUtc is null ? 0 : viewer.Connected;
                var appCannot = newest?.SeenCannot ?? "";
                var (state, words) = WindowGrants.State(w, g, row, acknowledged, open, viewer, appCannot);
                list.Add(new GrantObserved(w.Id, g.Node, g.Kind, g.Name, g.How, newest is not null, newest?.LinkWord ?? "",
                    sent?.Rows.Any(r => r.Window == w.Id && r.Cannot.Length == 0) == true, open, state, words, appCannot));
            }
        }
        return list;
    }

    /// <summary>Raised (on a worker thread) when a remote command is rejected.</summary>
    public event Action<string>? CommandRejected;

    public void Start(int port, bool bindAll)
    {
        if (_listener is not null) return;
        var listener = new TcpListener(bindAll ? IPAddress.Any : IPAddress.Loopback, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ExclusiveAddressUse, true);
        listener.Start(backlog: 32);
        _listener = listener;
        Port = port;
        _door = new BoundDoor(port, bindAll);
        _cts = new CancellationTokenSource();
        _control.Snapshots.Subscribe(OnPublished);
        _control.Nodes.Bound += OnBound;
        // Thread pool, not the caller's (UI) context: every request continuation would otherwise
        // resume on the WPF dispatcher — network I/O must never run on the show thread.
        _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
    }

    public async Task StopAsync()
    {
        _door = null;
        _control.Snapshots.Unsubscribe(OnPublished);
        _control.Nodes.Bound -= OnBound;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
        foreach (var c in _sockets.Keys) c.Abort();
        _sockets.Clear();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>Owner thread. O(clients) signal, no serialization, no I/O.</summary>
    private void OnPublished(ShowSnapshot _)
    {
        foreach (var c in _sockets.Keys) c.Signal();
    }

    /// <summary>The latest frame for an access level, serialized once per change however many clients want it.</summary>
    private byte[] CurrentFrameJson(FrameAccess access = FrameAccess.Public)
    {
        var snap = _control.Snapshots.Latest;
        ref var slot = ref access == FrameAccess.Operator ? ref _operatorFrame
                          : ref access == FrameAccess.PublicDetail ? ref _detailFrame : ref _frame;
        var f = Volatile.Read(ref slot);
        if (f is not null && ReferenceEquals(f.Snapshot, snap)) return f.Json;
        f = new Frame(snap, SnapshotJson.Serialize(snap, access));
        Volatile.Write(ref slot, f);
        return f.Json;
    }

    private byte[] CurrentConfigJson()
    {
        var version = _control.Snapshots.Latest.ConfigVersion;
        var c = _config;
        if (c is { } cached && cached.Version == version) return cached.Json;
        var json = SnapshotJson.SerializeConfig(_control.GetRemoteConfig());
        _config = (version, json);
        return json;
    }

    // ------------------------------------------------------------------ accept / HTTP

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            if (!_connectionSlots.Wait(0))
            {
                // Over capacity: shed load rather than queue — in words while a few can still be answered, closed unanswered past them.
                if (_shedSlots.Wait(0)) _ = ShedAsync(tcp, ct);
                else tcp.Dispose();
                continue;
            }
            _ = HandleConnectionAsync(tcp, ct);
        }
    }

    /// <summary>
    /// A connection past <see cref="MaxConnections"/>: its request's head read within <see cref="ShedWithin"/> (over TLS when it speaks
    /// TLS) — with its body when one of at most <see cref="HttpRequest.MaxBody"/> follows, as every head is read, so the close leaves no
    /// unread request behind to turn into a reset — then 429 with <see cref="Full"/> and Retry-After, and closed. A WebSocket upgrade hears
    /// the same before any upgrade. A key in force that came in clear from another machine is marked as on any request (13.6.9). Nothing of
    /// the request is routed or run; a longer body is not drained, and its sender may see the reset instead.
    /// </summary>
    private async Task ShedAsync(TcpClient tcp, CancellationToken ct)
    {
        try
        {
            tcp.NoDelay = true;
            var remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
            var (stream, req, secure) = await ReadHeadAsync(tcp, ShedWithin, ct);
            if (req is null) return;
            NoteClear(req, remote, secure);
            await WriteAsync(stream, 429, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { ok = false, error = Full }), ct, cors: true,
                extra: "Retry-After: " + FullRetryAfter.ToString(CultureInfo.InvariantCulture) + "\r\n");
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException
                                       or System.Security.Authentication.AuthenticationException)
        {
            // It went away, or sent no head in time: closed unanswered, as every connection past the ceiling once was.
        }
        finally
        {
            tcp.Dispose();
            _shedSlots.Release();
        }
    }

    /// <summary>
    /// A connection's request head, read within <paramref name="within"/>: over TLS when a certificate is set and its first byte is a TLS
    /// handshake record (0x16), else plain on the same port. Null for the request when the connection ended before sending one.
    /// </summary>
    private async Task<(Stream Stream, HttpRequest? Request, bool Secure)> ReadHeadAsync(TcpClient tcp, TimeSpan within, CancellationToken ct)
    {
        Stream stream = tcp.GetStream();
        var secure = false;
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headerCts.CancelAfter(within);
        if (Certificate is { } cert)
        {
            var first = new byte[1];
            var n = await tcp.Client.ReceiveAsync(first, SocketFlags.Peek, headerCts.Token);
            if (n == 0) return (stream, null, false);
            if (first[0] == 0x16)
            {
                var ssl = new System.Net.Security.SslStream(stream, leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions
                {
                    ServerCertificate = cert,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                }, headerCts.Token);
                stream = ssl;
                secure = true;
            }
        }
        return (stream, await HttpRequest.ReadAsync(stream, MaxHeaderBytes, headerCts.Token), secure);
    }

    private async Task HandleConnectionAsync(TcpClient tcp, CancellationToken ct)
    {
        try
        {
            tcp.NoDelay = true;
            var remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;

            // TLS or plain on the same port: a TLS client speaks first with a handshake record (0x16).
            var (stream, req, secure) = await ReadHeadAsync(tcp, HeaderTimeout, ct);
            if (req is null) return;

            // 13.6.9: a key in force that came in clear from another machine is marked seen in clear before anything answers the
            // request — its 400, its 413, the Require HTTPS redirect, a refusal or a route — since it was read on the path whatever the
            // answer. A reading of the credential that decides nothing and counts nothing.
            NoteClear(req, remote, secure);

            // A request whose body this server will not read is answered first, whatever its path or credential: a Content-Length it
            // cannot read 400, a body over the cap 413. No route, credential check, lockout count, Require HTTPS redirect or /ws upgrade
            // sees either, so anything that must see every request's credential (13.6.9's mark) stands above these two answers. A
            // context posted too big is noted for the Show network first, by the node its key is bound to or else its address: a
            // reading of the credential that decides nothing and counts nothing.
            if (req.LengthUnreadable)
            {
                await WriteJsonError(stream, 400, HttpRequest.UnreadableLength, ct);
                return;
            }
            if (req.Oversize > 0)
            {
                if (req.Method == "POST" && req.Path == "/api/peers/context") NoteTooBig(req, remote);
                await RefuseOversizeAsync(stream, req, ct);
                return;
            }

            // A session credential (13.7.1) is checked before anything else reads the request: one that came in clear from another
            // machine has been read on the path, so its link ends whatever the rest of the request is; one that is not a live link's is
            // 401 with relink. A live link's S passes this door and, on a side call's route (SpeaksOn), speaks for the key its link
            // proved: the route reads the request as that key's, its scope, its binding and its rate. On any other route S is no
            // credential, and the request is read as one with none.
            if (req.Headers.GetValueOrDefault("authorization") is { } showBus && showBus.TrimStart().StartsWith("ShowBus", StringComparison.OrdinalIgnoreCase))
            {
                var (refusal, keyId) = CheckSession(req, remote, secure, showBus);
                if (refusal is not null)
                {
                    await WriteAsync(stream, 401, "application/json", refusal, ct, cors: true);
                    return;
                }
                if (SpeaksOn(req.Path)) req.SpeakAs(keyId!);
            }

            // Require HTTPS: plain requests from other machines go to the encrypted address (the certificate pages stay
            // reachable, so a screen can fetch the certificate it needs to trust).
            if (!secure && RequireHttps && !IPAddress.IsLoopback(remote) && req.Path is not ("/tls" or "/tls.cer"))
            {
                if (req.IsWebSocketUpgrade) { await WriteJsonError(stream, 403, "this Stagewatch requires wss:// (Settings → Remote → Require HTTPS)", ct); return; }
                var host = req.Headers.GetValueOrDefault("host") ?? $"{Screens.AddressOf(remote)}:{Port}";
                await WriteRedirectAsync(stream, "https://" + host + req.Path + (req.Query.Count == 0 ? "" : "?" + string.Join("&", req.Query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)))), ct);
                return;
            }

            if (req.Path == "/ws" && req.IsWebSocketUpgrade)
            {
                await HandleWebSocketAsync(stream, req, remote, secure, ct);
                return;
            }
            await RouteAsync(stream, req, remote, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or WebSocketException)
        {
            // Client went away or timed out. Nothing to do — the show goes on.
        }
        finally
        {
            tcp.Dispose();
            _connectionSlots.Release();
        }
    }

    /// <summary>
    /// A body over <see cref="HttpRequest.MaxBody"/> on a path that is not deferred (Show Bus §6: "413 and its size"): drained, up to
    /// <see cref="HttpRequest.MaxDrain"/> for at most <see cref="HttpRequest.DrainFor"/> on <see cref="DrainClock"/>, so the sender
    /// reads the 413 instead of the reset a close with unread data makes; then answered with what it declared, and closed. One
    /// declared past MaxDrain is answered at once, undrained, and its sender may see the reset instead. Nothing of the body is kept
    /// or logged. The drain's read is never cut by the deadline: past it, the 413 is written beside the read still pending, and the
    /// close that follows ends that read, so no answer is ever written after a cancelled read, which a socket or a TLS stream need
    /// not survive.
    /// </summary>
    private async Task RefuseOversizeAsync(Stream stream, HttpRequest req, CancellationToken ct)
    {
        var drained = Absorbed(req.DrainOversizeAsync(stream, ct));
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await Task.WhenAny(drained, Task.Delay(HttpRequest.DrainFor, DrainClock, deadline.Token));
            await deadline.CancelAsync();
        }
        await WriteJsonError(stream, 413, HttpRequest.OversizeWords(req.Oversize), ct);

        // A connection that ends under the drain ends it: the 413 is written to it all the same, and fails as any write to a gone client.
        static async Task Absorbed(Task drain)
        {
            try { await drain; }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The sender went away or the server is stopping: either way the drain is over.
            }
        }
    }

    private async Task RouteAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        if (req.Method == "OPTIONS")
        {
            await WriteAsync(stream, 204, "text/plain", Array.Empty<byte>(), ct, cors: true);
            return;
        }
        if (req.Method is not ("GET" or "POST" or "HEAD"))
        {
            await WriteJsonError(stream, 405, "method not allowed", ct);
            return;
        }

        switch (req.Path)
        {
            case "/" or "/index.html":
                await WriteAsset(stream, "index.html", ct); return;
            case "/viewer" or "/viewer.html" or "/speaker":
                await WriteAsset(stream, "viewer.html", ct); return;
            case "/control" or "/control.html":
                await WriteAsset(stream, "control.html", ct); return;
            case "/producer" or "/showcaller":
                await WriteAsset(stream, "producer.html", ct); return;
            case "/moderator" or "/backstage" or "/greenroom" or "/dept" or "/department" or "/interpreter" or "/haptic" or "/watch":
                await WriteAsset(stream, "role.html", ct); return;
            case "/audience" or "/agenda":
                await WriteAsset(stream, "audience.html", ct); return;
            case "/ask":
                await WriteAsset(stream, "ask.html", ct); return;
            case "/rooms":
                await WriteAsset(stream, "rooms.html", ct); return;
            case "/holding":
                await WriteAsset(stream, "holding.html", ct); return;
            case "/clock" or "/studio":
                await WriteAsset(stream, "clock.html", ct); return;
            case "/countdown":
                await WriteAsset(stream, "countdown.html", ct); return;
            case "/timeline":
                await WriteAsset(stream, "timeline.html", ct); return;
            case "/layout":
                await WriteAsset(stream, "layout.html", ct); return;
            case "/designer":
                await WriteAsset(stream, "designer.html", ct); return;
            case "/holo" or "/holographic":
                await WriteAsset(stream, "holo.html", ct); return;
            case HttpRequest.UploadPath:
                await HandleHoldingUploadAsync(stream, req, remote, ct); return;
            case HttpRequest.ScriptPath:
                await HandleSlideScriptAsync(stream, req, remote, ct); return;
            case "/api/slides/feed":
            {
                // A presenter PC's slideshow (Stagewatch Slides). PIN like every control route.
                if (req.Method != "POST") { await WriteJsonError(stream, 405, "POST the feed", ct); return; }
                if (Authorize(req, remote, ApiKeyScope.Control) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                if (SlideFeed.Parse(req.Body) is not { } feed) { await WriteJsonError(stream, 400, "not a slide feed", ct); return; }
                var error = _control.AcceptSlideFeed(feed, Screens.AddressOf(remote), out var needScript);
                if (error is not null) { await WriteJsonError(stream, 403, error, ct); return; }
                await WriteAsync(stream, 200, "application/json", needScript ? "{\"ok\":true,\"needScript\":true,\"proto\":1}"u8.ToArray() : "{\"ok\":true,\"proto\":1}"u8.ToArray(), ct);
                return;
            }
            case "/common.js" or "/viewer.js" or "/control.js" or "/style.css" or "/producer.js" or "/role.js" or "/audience.js" or "/rooms.js" or "/ask.js" or "/sw.css" or "/manifest.json" or "/holding.js" or "/clock.js" or "/countdown.js" or "/timeline.js" or "/layout.js" or "/layout-render.js" or "/designer.js"
                or "/holo.bundle.js" or "/holo-page.js" or "/window.js":
                await WriteAsset(stream, req.Path[1..], ct); return;
            case "/report":
            {
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                var html = _control.Report("html");
                if (html is null) { await WriteJsonError(stream, 404, "no report", ct); return; }
                await WriteAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html), ct); return;
            }
            case "/api/report.csv":
            {
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                var csv = _control.Report("csv");
                if (csv is null) { await WriteJsonError(stream, 404, "no report", ct); return; }
                await WriteAsync(stream, 200, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv), ct, cors: true); return;
            }
            case "/api/report/network.csv":
            {
                // What verified peers' own journals told this Stagewatch, as the report's show network section lists it: a sample, and
                // its first line says so. Read scope and cross-origin, as the report's CSV.
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                var csv = _control.Report(ShowReport.NetworkFormat);
                if (csv is null) { await WriteJsonError(stream, 404, "no report", ct); return; }
                await WriteAsync(stream, 200, "text/csv; charset=utf-8", Encoding.UTF8.GetBytes(csv), ct, cors: true); return;
            }
            case "/api/qr":
            {
                var text = req.Query.GetValueOrDefault("text") ?? "";
                if (text.Length is 0 or > 512) { await WriteJsonError(stream, 400, "text required (max 512)", ct); return; }
                await WriteAsync(stream, 200, "image/svg+xml", Encoding.UTF8.GetBytes(QrSvg.Render(text)), ct, cors: true); return;
            }
            case "/mcp":
                await HandleMcpAsync(stream, req, remote, ct); return;
            case "/tls":
                await WriteAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(TlsPage()), ct); return;
            case "/tls.cer":
                if (Certificate is not { } cer) { await WriteJsonError(stream, 404, "no certificate", ct); return; }
                await WriteAsync(stream, 200, "application/pkix-cert", cer.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert), ct,
                    extra: "Content-Disposition: attachment; filename=\"stagewatch.cer\"\r\n"); return;
            case "/api/proposals":
                await HandleProposalsAsync(stream, req, remote, null, ct); return;
            case "/api/context":
            {
                // Show Bus §6: what this node is doing, for other apps and their AI. Read access (PIN or any key). Only a
                // read: a node's own context goes to /api/peers/context, and a post here is never answered as if taken.
                if (req.Method == "POST") { await WriteJsonError(stream, 405, "this is Stagewatch's own context — POST yours to /api/peers/context", ct); return; }
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                var json = _control.ContextJson();
                if (json is null) { await WriteJsonError(stream, 404, "no context", ct); return; }
                await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes(json), ct, cors: true, extra: RateHeaders(req)); return;
            }
            case "/api/preflight":
            {
                // 'Ready for doors', with a reason per node: read access (the PIN or any key), as the context. Nothing rides in its
                // address, so a query is refused; a host that keeps no preflight says so. What is served is never a confident stale
                // verdict (ServedPreflight): a stuck show thread, or a verdict the host has not rebuilt, answers not ready.
                if (req.Method == "POST") { await WriteJsonError(stream, 405, "GET the preflight", ct); return; }
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                if (req.Query.Count > 0) { await WriteJsonError(stream, 400, PreflightWire.NoQuery, ct); return; }
                if (ServedPreflight() is not { } json) { await WriteJsonError(stream, 404, PreflightWire.None, ct); return; }
                await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes(json), ct, cors: true, extra: RateHeaders(req)); return;
            }
            case "/api/peers/context":
            {
                // Another show app tells us what it's doing (its own context, §6). A node key, a control key or the PIN.
                // The server reads the node it names and speaks for it before the store is called: a body it can't read
                // is a 400 and a refusal a 403, both with words, and neither reaches the store or stores anything. Taken,
                // the answer says what the context is held as (verified: key, operator or none).
                if (req.Method != "POST") { await WriteJsonError(stream, 405, "POST the context", ct); return; }
                if (Authorize(req, remote, ApiKeyScope.Node) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                if (req.Body.Length is 0 or > PeerContexts.MaxBytes) { await WriteJsonError(stream, 400, "a context is 1 byte to 64 KB of JSON", ct); return; }
                var json = Encoding.UTF8.GetString(req.Body);
                if (PeerContexts.Parse(json, "", DateTime.UtcNow) is not { } claimed) { await WriteJsonError(stream, 400, PeerContexts.NotAContext, ct); return; }
                var credential = CredentialOf(req);
                if (_control.Nodes.Check(credential, claimed.Kind, claimed.Id, DateTime.UtcNow) is { Ok: false } refused)
                { await WriteJsonError(stream, 403, refused.Error ?? "refused", ct); return; }
                var taken = _control.AcceptPeerContext(json, Screens.AddressOf(remote), credential);
                if (taken.Status != 200 || taken.Error is not null)
                { await WriteJsonError(stream, taken.Status is >= 400 and < 500 ? taken.Status : 400, taken.Error ?? "not taken", ct); return; }
                BindTaken(taken.Status, credential, claimed.Kind, claimed.Id);
                await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true,\"verified\":\"" + NodeBindings.Word(taken.Proof) + "\"}"), ct, extra: RateHeaders(req)); return;
            }
            case "/api/state":
            {
                if (CheckView(req, remote) is { } viewDenied) { await WriteJsonError(stream, 401, viewDenied, ct); return; }
                // Detail for anyone is the public agenda view; with the PIN it's the full operator frame.
                var wantDetail = req.Query.GetValueOrDefault("detail") is "1" or "true";
                var access = !wantDetail ? FrameAccess.Public
                    : IsAuthorized(req, remote, ApiKeyScope.Read) ? FrameAccess.Operator : FrameAccess.PublicDetail;
                if (KeyOver(req) is { } limited) { await WriteDenied(stream, limited, ct); return; }
                await WriteAsync(stream, 200, "application/json", CurrentFrameJson(access), ct, cors: true, extra: RateHeaders(req)); return;
            }
            case "/api/crew-links":
            {
                // Crew links can confirm cues: the PIN or a control key.
                if (Authorize(req, remote, ApiKeyScope.Control) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                var (secret, required) = _control.CrewKeyPolicy;
                var links = string.IsNullOrEmpty(secret) ? Array.Empty<object>()
                    : CrewKeys.Links(secret, _control.CrewDepartments).Select(l => (object)new { label = l.Label, path = l.Path }).ToArray();
                await WriteAsync(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { required, links }), ct, cors: true);
                return;
            }
            case "/api/config":
                // The config names the running order, presets and messages: behind the viewing code like the state.
                if (CheckView(req, remote) is { } configDenied) { await WriteJsonError(stream, 401, configDenied, ct); return; }
                await WriteAsync(stream, 200, "application/json", CurrentConfigJson(), ct, cors: true); return;
            case "/custom.css":
                // The operator's styling for browser screens. CSS only: the page CSP still blocks scripts and other sites.
                await WriteAsync(stream, 200, "text/css; charset=utf-8", Encoding.UTF8.GetBytes(_control.CustomCss), ct); return;
            case "/api/help":
                await WriteAsync(stream, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ShowCommandParser.PersonHelp + "\n"), ct, cors: true); return;
            case "/api/deptnotes":
            {
                // One department's notes on every item: its crew link (or the PIN) reads them.
                var dept = (req.Query.GetValueOrDefault("dept") ?? "").Trim();
                if (dept.Length is 0 or > 40) { await WriteJsonError(stream, 400, "dept required", ct); return; }
                var isOp = IsAuthorized(req, remote, ApiKeyScope.Read);
                if (!isOp && CheckDeptKey(dept, req.Query.GetValueOrDefault("key"), remote) is { } denied) { await WriteJsonError(stream, 403, denied, ct); return; }
                var notes = _control.DeptNotesFor(dept) ?? Array.Empty<(int, string, string)>();
                await WriteAsync(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    dept,
                    version = _control.Snapshots.Latest.NotesVersion,
                    items = notes.Select(n => new { i = n.Index, title = n.Title, note = n.Note }),
                }), ct, cors: true);
                return;
            }
            case "/api/windows":
                await HandleWindowListAsync(stream, req, remote, ct); return;
            case "/api/screens":
            {
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                await WriteAsync(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    screens = ListScreens().Select(s => new { id = s.Id, name = s.Name, page = s.Page, address = s.Address, device = s.Device,
                        connected = s.ConnectedUtc.ToString("o"), control = s.Control, tabs = s.Tabs, claim = s.Claim }),
                    presets = _control.ScreenPresets.Select(p => new { name = p.Name, path = p.Path }),
                }), ct, cors: true);
                return;
            }
        }

        // A controlled window: the address is its identity and its pass.
        if (req.Path.StartsWith("/w/", StringComparison.Ordinal) && req.Method is "GET" or "HEAD")
        {
            await HandleWindowPageAsync(stream, req, remote, ct);
            return;
        }

        // Saved screen setups: /p/lobby opens whatever page and options "lobby" was saved as.
        if (req.Path.StartsWith("/p/", StringComparison.Ordinal))
        {
            var target = _control.ScreenPresetPath(Uri.UnescapeDataString(req.Path[3..]));
            if (target is null || !ScreenPreset.ValidPath(target)) { await WriteJsonError(stream, 404, "no such screen preset", ct); return; }
            await WriteRedirectAsync(stream, target, ct);
            return;
        }

        if (req.Path.StartsWith("/api/proposals/", StringComparison.Ordinal))
        {
            await HandleProposalsAsync(stream, req, remote, req.Path["/api/proposals/".Length..], ct);
            return;
        }
        if (req.Path == "/api/layouts" || req.Path.StartsWith("/api/layouts/", StringComparison.Ordinal))
        {
            await HandleLayoutsAsync(stream, req, remote, ct);
            return;
        }
        if (req.Path.StartsWith("/api/sounds/", StringComparison.Ordinal) && req.Path.EndsWith(".wav", StringComparison.Ordinal))
        {
            var id = req.Path["/api/sounds/".Length..^4];
            if (id.Length is 0 or > 32 || !id.All(char.IsAsciiLetterOrDigit) || _control.SoundData(id) is not { } wav)
            { await WriteJsonError(stream, 404, "no such sound", ct); return; }
            await WriteAsync(stream, 200, "audio/wav", wav, ct);
            return;
        }
        if (req.Path.StartsWith("/api/holding/", StringComparison.Ordinal) && req.Method is "GET" or "HEAD")
        {
            await HandleHoldingImageAsync(stream, req.Path["/api/holding/".Length..], ct);
            return;
        }
        if (req.Path.StartsWith("/api/", StringComparison.Ordinal))
        {
            await HandleApiCommandAsync(stream, req, remote, ct);
            return;
        }
        await WriteJsonError(stream, 404, "not found", ct);
    }

    /// <summary>
    /// What a request proves about which node it may speak for (Show Bus §11), from the credential it carries: a node key
    /// proves its own node (by the key's id); a control key, or the PIN this Stagewatch asks for given right, proves the
    /// operator; a read or MCP key, a PIN this Stagewatch doesn't ask for, or nothing proves nothing. Read after Authorize
    /// admitted the request, so it counts nothing; a key revoked in between proves nothing.
    /// </summary>
    private NodeCredential CredentialOf(HttpRequest req)
    {
        if (req.SessionKeyId is not null)
            return KeyOf(req) switch
            {
                { Scope: ApiKeyScope.Node } k => NodeCredential.Key(k.Id),
                { Scope: ApiKeyScope.Control } => NodeCredential.Operator,
                _ => NodeCredential.None,
            };
        var supplied = PinFrom(req);
        if (ApiKeys.LooksLikeKey(supplied))
            return ApiKeys.Find(_control.ApiKeys, supplied) switch
            {
                { Scope: ApiKeyScope.Node } k => NodeCredential.Key(k.Id),
                { Scope: ApiKeyScope.Control } => NodeCredential.Operator,
                _ => NodeCredential.None,
            };
        var pin = _pin();
        return !string.IsNullOrEmpty(pin) && supplied is not null
               && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(pin))
            ? NodeCredential.Operator : NodeCredential.None;
    }

    /// <summary>
    /// A node key's request the store took (2xx) binds that key to the node it named, as the store's own speaking does: a
    /// no-op when the store already spoke, and the first binding when a store didn't, so no store behind this server can
    /// skip first-bind-wins any more than it can skip a refusal. A request the store refused binds nothing, and the
    /// operator's or no credential never binds.
    /// </summary>
    private void BindTaken(int status, NodeCredential credential, string? kind, string id)
    {
        if (status is >= 200 and < 300 && credential.Proof == NodeProof.Key) _control.Nodes.Speak(credential, kind, id, DateTime.UtcNow);
    }

    /// <summary>
    /// The shared approval queue (Show Bus §11). POST /api/proposals (a node or control key, or the PIN; an MCP key posts no
    /// proposal here, 403 in §11's words, since an AI on this desk's MCP endpoint proposes through /mcp as the desk's own);
    /// GET /api/proposals/&lt;id&gt; (an id, or p&lt;n&gt; of this desk's run) and ?from=&lt;node&gt; (read); GET
    /// ?target=&lt;node&gt;&amp;state=approved collects what was approved for a node (its node key, or control/PIN), which the desk
    /// records as that node's last collect; POST /api/proposals/&lt;id&gt;/result reports what it did. A proposal's from and a
    /// collect's target are read and spoken for before the desk is called (a 400 when there is no node to speak for, a 403 when the
    /// credential may not, each with words, nothing queued or claimed), and a node key's request the desk took binds it
    /// (<see cref="BindTaken"/>); a result's node is the proposal's target, which only the desk knows, so the desk speaks for it.
    /// </summary>
    private async Task HandleProposalsAsync(Stream stream, HttpRequest req, IPAddress remote, string? rest, CancellationToken ct)
    {
        async Task Reply((int Status, string Json) r) => await WriteAsync(stream, r.Status, "application/json", Encoding.UTF8.GetBytes(r.Json), ct, cors: true, extra: RateHeaders(req));
        Denied? Proposer()
        {
            var denied = Authorize(req, remote, ApiKeyScope.Node);
            return denied is { Status: 403 } && KeyOf(req) is { Scope: ApiKeyScope.Mcp }
                ? denied with { Error = ProposalDesk.McpPostsNone } : denied;
        }
        if (string.IsNullOrEmpty(rest))
        {
            if (req.Method == "POST")
            {
                if (Proposer() is { } denied) { await WriteDenied(stream, denied, ct); return; }
                if (req.Body.Length is 0 or > 16 * 1024) { await WriteJsonError(stream, 400, "a proposal is 1 byte to 16 KB of JSON", ct); return; }
                var json = Encoding.UTF8.GetString(req.Body);
                var (proposer, unread) = ProposalDesk.FromOf(json);
                if (proposer is null) { await WriteJsonError(stream, 400, unread ?? ProposalDesk.NoFrom, ct); return; }
                var credential = CredentialOf(req);
                if (_control.Nodes.Check(credential, proposer.Kind, proposer.Id, DateTime.UtcNow) is { Ok: false } refused)
                { await WriteJsonError(stream, 403, refused.Error ?? "refused", ct); return; }
                var proposed = _control.SubmitProposal(json, credential);
                BindTaken(proposed.Status, credential, proposer.Kind, proposer.Id);
                await Reply(proposed);
                return;
            }
            if (req.Query.GetValueOrDefault("target") is { Length: > 0 } target)
            {
                if (Authorize(req, remote, ApiKeyScope.Node) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                if (req.Query.GetValueOrDefault("state") is not (null or "approved")) { await WriteJsonError(stream, 400, "targets collect state=approved", ct); return; }
                if (!NodeBindings.ValidId(target)) { await WriteJsonError(stream, 400, "a node id is 1-40 letters, digits, - or _", ct); return; }
                var credential = CredentialOf(req);
                if (_control.Nodes.Check(credential, "", target, DateTime.UtcNow) is { Ok: false } refused)
                { await WriteJsonError(stream, 403, refused.Error ?? "refused", ct); return; }
                var collected = _control.CollectApproved(target, credential);
                BindTaken(collected.Status, credential, "", target);
                await Reply(collected);
                return;
            }
            if (Authorize(req, remote, ApiKeyScope.Read) is { } readDenied) { await WriteDenied(stream, readDenied, ct); return; }
            await Reply(_control.ProposalsFrom(req.Query.GetValueOrDefault("from") ?? ""));
            return;
        }
        var parts = rest.Split('/', 2);
        if (parts.Length == 2 && parts[1] == "result")
        {
            if (req.Method != "POST") { await WriteJsonError(stream, 405, "POST the result", ct); return; }
            if (Authorize(req, remote, ApiKeyScope.Node) is { } denied) { await WriteDenied(stream, denied, ct); return; }
            await Reply(_control.ProposalResult(parts[0], Encoding.UTF8.GetString(req.Body), CredentialOf(req)));
            return;
        }
        if (Authorize(req, remote, ApiKeyScope.Read) is { } getDenied) { await WriteDenied(stream, getDenied, ct); return; }
        await Reply(_control.GetProposal(parts[0]));
    }

    /// <summary>The window page's own policy: it frames only this Stagewatch's pages, and nothing may frame it.</summary>
    private const string WindowCsp = "default-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; img-src 'self' data:; frame-src 'self'; " +
                                     "frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>
    /// The PIN's lockout table as it stands: every device (its address) whose one-minute window is open, with its wrong PINs in
    /// that window and how long ago the window began, the oldest first. A device at <see cref="WrongPinsPerMinute"/> is locked
    /// out of the PIN and every key until its window ends. Read-only, any thread; it holds no secret. What a test or a rig reads
    /// to see that a peer's honest dialling locked nothing out.
    /// </summary>
    public IReadOnlyList<AuthFailureCount> AuthFailures() => FailuresIn(_pinGuesses);

    /// <summary>
    /// The wrong-proof table as it stands (Show Bus 13.5.6): every (device, kid) pair with wrong LINK proofs in its open minute, on
    /// <see cref="LinkClock"/>, the oldest first. A copy that holds no secret; reading it counts nothing.
    /// </summary>
    public IReadOnlyList<ProofFailureCount> ProofFailures()
        => _proofGuesses.Open(LinkNowMs).Select(p => new ProofFailureCount(Screens.AddressOf(p.Device), p.Kid, p.Count, p.AgeMs)).ToList();

    /// <summary>
    /// The contexts posted to /api/peers/context and refused 413 within <see cref="PeerContexts.KeepFor"/> of
    /// <paramref name="nowUtc"/>, the newest per sender (the node its key is bound to, else its address), at most
    /// <see cref="PeerContexts.MaxPeers"/> senders, the oldest first. Read-only, any thread; nothing of a body is kept, and no
    /// credential either.
    /// </summary>
    public IReadOnlyList<TooBigContext> TooBigContexts(DateTime nowUtc)
    {
        lock (_tooBigLock) return _tooBig.Where(t => nowUtc - t.AtUtc <= PeerContexts.KeepFor).ToList();
    }

    /// <summary>
    /// Notes a context refused as too big: by the node a node key in force is bound to — carried by its secret, or by a live link's
    /// session credential that verifies (13.7.1) — else by its address alone. Read before the session door, it decides and ends nothing.
    /// </summary>
    private void NoteTooBig(HttpRequest req, IPAddress remote)
    {
        var keyId = LiveSessionKey(req.Headers.GetValueOrDefault("authorization")) is { } linked
                    && _control.ApiKeys.Any(k => k.Id == linked && k.Scope == ApiKeyScope.Node)
            ? linked
            : CredentialOf(req) is { Proof: NodeProof.Key, KeyId: { } k } ? k : null;
        var node = keyId is null ? null : _control.Nodes.NodeOf(keyId);
        var note = new TooBigContext(Screens.AddressOf(remote), node?.Kind, node?.Id, req.Oversize, DateTime.UtcNow);
        lock (_tooBigLock)
        {
            _tooBig.RemoveAll(t => t.NodeId == note.NodeId && (note.NodeId is not null || t.Address == note.Address));
            _tooBig.Add(note);
            if (_tooBig.Count > PeerContexts.MaxPeers) _tooBig.RemoveAt(0);
        }
    }

    private readonly List<TooBigContext> _tooBig = [];
    private readonly Lock _tooBigLock = new();

    /// <summary>
    /// The view passes' table the same way (Show Bus 12.2): wrong window addresses, viewing codes and crew links per device in
    /// its open window, <see cref="WrongViewPassesPerMinute"/> locking it out of every view pass until the window ends.
    /// </summary>
    public IReadOnlyList<AuthFailureCount> ViewPassFailures() => FailuresIn(_passGuesses);

    private static IReadOnlyList<AuthFailureCount> FailuresIn(WrongGuesses table)
        => table.Open(Environment.TickCount64).Select(d => new AuthFailureCount(Screens.AddressOf(d.Device), d.Count, d.AgeMs)).ToList();

    /// <summary>Whether the device is locked out of view passes: <see cref="WrongViewPassesPerMinute"/> wrong ones this minute.</summary>
    private bool PassesLockedOut(IPAddress remote) => _passGuesses.Locked(remote, Environment.TickCount64);

    /// <summary>One wrong view pass (a window address, a viewing code or a crew link) from the device, in their own count.</summary>
    private void CountWrongPass(IPAddress remote) => _passGuesses.Count(remote, Environment.TickCount64);

    /// <summary>
    /// The window an address names, and by which of its passes (its own address, or a page grant's), or null: <paramref name="locked"/>
    /// when the device is locked out of view passes, so the address was not looked at and the caller says wait, never gone; else the
    /// address names no window and is counted as a wrong view pass.
    /// One reading of the lock decides both, so a device that reaches the limit as it asks is told to wait, never that a right
    /// address is gone. Never says how close a guess was.
    /// </summary>
    private WindowHit? WindowFor(string? token, IPAddress remote, out bool locked)
    {
        locked = PassesLockedOut(remote);
        if (locked) return null;
        var hit = ScreenWindows.Find(_control.Windows, token);
        if (hit is null) CountWrongPass(remote);
        return hit;
    }

    /// <summary>GET /w/&lt;token&gt;: the window page. No referrer leaves it, no search engine keeps it, no other site frames it.</summary>
    private async Task HandleWindowPageAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        const string headers = "Referrer-Policy: no-referrer\r\nX-Robots-Tag: noindex\r\n";
        var token = req.Path[3..].TrimEnd('/');
        var window = WindowFor(token, remote, out var locked);
        if (locked)
        {
            await WriteAsync(stream, 429, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(WindowNotice("Too many wrong viewing codes, window addresses or crew links from this device — this page tries again in a minute.", retrySeconds: 60)), ct,
                extra: headers + "Retry-After: 60\r\n", csp: WindowCsp);
            return;
        }
        if (window is null)
        {
            await WriteAsync(stream, 404, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(WindowNotice("This window address isn't in use: it may have been replaced. Ask the Stagewatch operator for the new one.")), ct,
                extra: headers, csp: WindowCsp);
            return;
        }
        var page = Assets.Get("window.html");
        if (page is null) { await WriteJsonError(stream, 404, "not found", ct); return; }
        await WriteAsync(stream, 200, "text/html; charset=utf-8", page, ct, extra: headers, csp: WindowCsp);
    }

    private static string WindowNotice(string words, int retrySeconds = 0)
        => "<!doctype html><meta charset=utf-8><meta name=viewport content=\"width=device-width\"><meta name=robots content=noindex><title>Stagewatch window</title>" +
           (retrySeconds > 0 ? $"<meta http-equiv=refresh content={retrySeconds}>" : "") +
           "<style>html,body{margin:0;height:100%;background:#000;color:#6b7280;font:600 max(16px,2.6vh) system-ui,sans-serif}" +
           "body{display:flex;align-items:center;justify-content:center;text-align:center;padding:0 6vw}</style><p>" + WebUtility.HtmlEncode(words) + "</p>";

    /// <summary>
    /// GET /api/windows (read): every window, what it was told and what it says it shows, how many show apps draw it and where each of
    /// its grants stands (12.5–12.6). A window's own address (its pass) is listed only for the PIN or a control key; a grant's address
    /// for no credential, the PIN included (12.5).
    /// </summary>
    private async Task HandleWindowListAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
        var supplied = PinFrom(req);
        var showPath = !ApiKeys.LooksLikeKey(supplied) || ApiKeys.Find(_control.ApiKeys, supplied) is { } k && ApiKeys.Allows(k.Scope, ApiKeyScope.Control);
        var seen = WindowStatus().ToDictionary(o => o.WindowId, StringComparer.Ordinal);
        var grants = GrantStatus().ToLookup(g => g.WindowId, StringComparer.Ordinal);
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            windows = _control.Windows.Select(w =>
            {
                var o = seen.GetValueOrDefault(w.Id);
                return new
                {
                    id = w.Id, name = w.Name, content = w.Content, hold = w.Hold, holdText = w.HoldText, holdImage = w.HoldImage, onLoss = w.OnLoss, fadeMs = w.FadeMs, rev = w.Revision,
                    path = showPath ? "/w/" + w.Token : null,
                    connected = o?.Connected ?? 0,
                    shown = o is null || o.ShownUtc is null ? null : new { rev = o.Revision, content = o.Content, hold = o.Hold, ms = o.SwapMs, at = o.ShownUtc.Value.ToString("o"), error = o.Error },
                    inStep = o is { Connected: > 0, Error.Length: 0 } && o.Revision == w.Revision,
                    device = o?.Device ?? "", address = o?.Address ?? "",
                    drawn = o?.Drawn ?? 0,
                    grants = grants[w.Id].Select(g => new
                    {
                        node = g.Node, t = g.Kind, name = g.Name, how = g.How, linked = g.Linked, link = g.Link, delivered = g.Delivered,
                        open = g.Open, state = g.State, words = g.Words, cannot = g.AppCannot,
                    }),
                };
            }),
        });
        await WriteAsync(stream, 200, "application/json", body, ct, cors: true, extra: RateHeaders(req));
    }

    /// <summary>How to trust this Stagewatch's certificate on a screen (Show Bus §10).</summary>
    private string TlsPage()
    {
        var fp = Fingerprint;
        var body = fp is null
            ? "<p>This Stagewatch has no certificate yet (Settings → Remote).</p>"
            : $"""
               <p>Fingerprint (SHA-256): <code>{fp[7..]}</code></p>
               <p><a href="/tls.cer">Download the certificate</a>, then trust it on this device:</p>
               <ul>
               <li><b>Windows</b>: open the file → Install Certificate → Local Machine → "Trusted Root Certification Authorities".</li>
               <li><b>macOS</b>: open it in Keychain Access → System → double-click it → Trust → Always Trust.</li>
               <li><b>iPhone / iPad</b>: open the download → Settings → Profile Downloaded → Install; then Settings → General → About → Certificate Trust Settings → turn it on.</li>
               <li><b>Android</b>: Settings → Security → Encryption &amp; credentials → Install a certificate → CA certificate.</li>
               </ul>
               <p>Check the fingerprint matches the one in Stagewatch (Settings → Remote) before you trust it. Then open screens with <code>https://</code>.</p>
               """;
        return $"<!doctype html><meta charset=utf-8><meta name=viewport content=\"width=device-width\"><title>Stagewatch certificate</title>" +
               "<style>body{font:16px system-ui;background:#0b0d12;color:#e6e8ee;max-width:42em;margin:2em auto;padding:0 1em}code{word-break:break-all;color:#22d3ee}a{color:#a78bfa}</style>" +
               "<h1>Stagewatch: encrypted screens</h1>" + body;
    }

    /// <summary>One copilot design at a time: each is a paid model call.</summary>
    private readonly SemaphoreSlim _designGate = new(1, 1);

    /// <summary>
    /// Designed screens. GET /api/layouts (names) and /api/layouts/&lt;name&gt; for screens (like any view);
    /// POST save, delete and ai (a copilot draft, not saved) with the PIN or a control key.
    /// </summary>
    private async Task HandleLayoutsAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        var rest = req.Path.Length > "/api/layouts/".Length ? Uri.UnescapeDataString(req.Path["/api/layouts/".Length..]) : "";
        if (req.Method is "GET" or "HEAD")
        {
            if (CheckView(req, remote) is { } viewDenied) { await WriteJsonError(stream, 401, viewDenied, ct); return; }
            if (rest.Length == 0)
            {
                await WriteAsync(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { layouts = _control.LayoutNames }), ct, cors: true);
                return;
            }
            if (_control.LayoutJson(rest) is not { } json) { await WriteJsonError(stream, 404, "no such layout", ct); return; }
            await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes(json), ct, cors: true);
            return;
        }
        if (req.Method != "POST") { await WriteJsonError(stream, 405, "GET or POST", ct); return; }
        if (Authorize(req, remote, ApiKeyScope.Control) is { } denied) { await WriteDenied(stream, denied, ct); return; }
        var body = Encoding.UTF8.GetString(req.Body);
        switch (rest)
        {
            case "save":
                if (_control.SaveLayout(body) is { } error) { await WriteJsonError(stream, 400, error, ct); return; }
                await WriteAsync(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray(), ct, cors: true, extra: RateHeaders(req));
                return;
            case "delete":
            {
                var name = req.Query.GetValueOrDefault("name") ?? "";
                if (!ScreenLayouts.ValidName(name)) { await WriteJsonError(stream, 400, "name required", ct); return; }
                _control.DeleteLayout(name);
                await WriteAsync(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray(), ct, cors: true, extra: RateHeaders(req));
                return;
            }
            case "ai":
            {
                string request = "", current = "";
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    request = doc.RootElement.TryGetProperty("request", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                    current = doc.RootElement.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Object ? c.GetRawText() : "";
                }
                catch (JsonException) { }
                if (request.Trim().Length is 0 or > 2000) { await WriteJsonError(stream, 400, "describe the screen (up to 2000 characters)", ct); return; }
                if (!await _designGate.WaitAsync(0, ct)) { await WriteJsonError(stream, 429, "the copilot is already designing — one moment", ct); return; }
                try
                {
                    var (json, notes) = await _control.DesignLayoutAsync(request, current.Length > 0 ? current : null);
                    var reply = "{\"ok\":" + (json is null ? "false" : "true") + ",\"layout\":" + (json ?? "null") + ",\"notes\":" + JsonSerializer.Serialize(notes) + "}";
                    await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes(reply), ct, cors: true, extra: RateHeaders(req));
                }
                finally { _designGate.Release(); }
                return;
            }
        }
        await WriteJsonError(stream, 404, "not found", ct);
    }

    /// <summary>
    /// GET|POST /api/&lt;verb&gt;[/arg/arg...][?value=..&amp;urgency=..&amp;flash=..&amp;start=1&amp;pin=..]
    /// e.g. /api/start, /api/nudge/-60, /api/preset/3/start, /api/message?text=Wrap%20up&amp;urgency=warn
    /// </summary>
    private async Task HandleApiCommandAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        var segments = req.Path["/api/".Length..].Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToList();
        if (segments.Count == 0) { await WriteJsonError(stream, 404, "missing command", ct); return; }
        var verb = segments[0];
        // Each path segment is one argument, unescaped once after the path is split: a line sent as one segment arrives whole,
        // its own slashes and quote marks included.
        var args = segments.Skip(1).ToList();
        if (args.Count == 0) args = ArgsFromQuery(req.Query);
        // POST /api/peer: the app and the line as a JSON body, for a line too long or too awkward for a path. Read before any
        // credential is asked for, as every route's shape is; it is two strings, and nothing of it is run or kept here.
        if (segments.Count == 1 && req.Method == "POST" && verb.Equals("peer", StringComparison.OrdinalIgnoreCase))
        {
            if (PeerBody(req.Body) is not { } body) { await WriteJsonError(stream, 400, PeerBodyTakes, ct); return; }
            args = [body.App, body.Line];
        }

        // Acknowledging a cue and asking a question need no PIN (speaker tablets, audience phones). A node key (another
        // show app) may put a message on the stage and nothing else.
        var authorized = false;
        var proved = false;
        if (!ShowCommandParser.IsPublicVerb(verb))
        {
            var need = ShowCommandParser.TryParse(verb, args, out var probe, out _) && ApiKeys.NodeMayRun(probe.Kind)
                ? ApiKeyScope.Node : ApiKeyScope.Control;
            if (Authorize(req, remote, need, out proved) is { } denied) { await WriteDenied(stream, denied, ct); return; }
            authorized = true;
        }
        else if (IsQuestion(verb) && !AllowQuestion(remote))
        {
            await WriteJsonError(stream, 429, "please wait a little before asking again", ct);
            return;
        }
        if (!ShowCommandParser.TryParse(verb, args, out var cmd, out var error))
        {
            Reject(error);
            await WriteJsonError(stream, 400, error, ct);
            return;
        }
        // Only a PIN or key that was given is tested: a crew page without one is a crew page, not a wrong guess.
        // Asked only for crew routes, so a key's request isn't counted twice.
        bool IsOperator() => authorized || IsAuthorized(req, remote, ApiKeyScope.Control);
        if (cmd.Kind == ShowCommandKind.DeptNote && !IsOperator() && CheckDeptKey(DeptOf(cmd), req.Query.GetValueOrDefault("key"), remote) is { } noteRefused)
        {
            await WriteJsonError(stream, 403, noteRefused, ct);
            return;
        }
        if (cmd.Kind == ShowCommandKind.Ack && !IsOperator())
        {
            if (CheckCrewAck(ref cmd, req.Query.GetValueOrDefault("key"), remote) is { } refused)
            {
                await WriteJsonError(stream, 403, refused, ct);
                return;
            }
        }
        var requestId = CommandReceipt.CleanId(req.Headers.GetValueOrDefault("x-request-id"));
        // Another node's air, and a person's line to another app or decision on a proposal: with no PIN set and no key, only this PC
        // or a listed address, never another site's page (a refusal runs nothing and sends nothing).
        if (PlayoutGate.Check(cmd, CommandDoor.Http, WebSender(req, remote, proved), _control.PlayoutFrom) is { } gated)
        {
            Reject(gated);
            if (requestId is not null) await WriteAsync(stream, 403, "application/json", CommandReceipt.Refused(requestId, gated).ToJson(), ct);
            else await WriteJsonError(stream, 403, gated, ct);
            return;
        }
        if (PlayoutGate.Person(cmd.Kind))
        {
            // The door names the sender, never the line: a role for the proposal, and who and from where for the desk's journal.
            cmd = cmd with { By = DeskOrigin.Wire(WireWho(PinFrom(req), req.SessionKeyId, remote, proved), proved) };
            // A person's line is answered with what came of it, whether or not the request named itself: never a bare "ok".
            requestId ??= PersonReceiptId(cmd.Kind);
        }
        // Show Bus receipts: with a request id the sender hears what happened, not just that it was sent.
        if (requestId is not null)
        {
            var receipt = await _control.ExecuteWithReceiptAsync(cmd, requestId, ReceiptWait);
            await WriteAsync(stream, receipt.Ok ? 200 : 409, "application/json", receipt.ToJson(), ct, extra: RateHeaders(req));
            return;
        }
        _control.Execute(cmd);
        await WriteAsync(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray(), ct, extra: RateHeaders(req));
    }

    /// <summary>What POST /api/peer takes, said when its body is anything else.</summary>
    public const string PeerBodyTakes = "POST /api/peer takes {\"app\":\"…\",\"line\":\"…\"}: two strings, in at most 16 KB of JSON";

    /// <summary>The most bytes of a POST /api/peer body: a line is at most <see cref="LineFence.MaxChars"/> characters.</summary>
    public const int MaxPeerBody = 16 * 1024;

    /// <summary>POST /api/peer's body as its two strings, or null when it is not that: each key once, nothing guessed from another type.</summary>
    private static (string App, string Line)? PeerBody(byte[] body)
    {
        if (body.Length is 0 or > MaxPeerBody) return null;
        try
        {
            if (BusJson.Parse(Encoding.UTF8.GetString(body)) is not System.Text.Json.Nodes.JsonObject root) return null;
            if (root["app"]?.GetValueKind() != JsonValueKind.String || root["line"]?.GetValueKind() != JsonValueKind.String) return null;
            return (root["app"]!.GetValue<string>(), root["line"]!.GetValue<string>());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The id of a person's line's receipt when its request named none: the verb.</summary>
    private static string PersonReceiptId(ShowCommandKind kind) => kind == ShowCommandKind.Desk ? "desk" : "peer";

    /// <summary>
    /// Who sent a person's line and from where, for the desk's journal file alone: the key's name (never its secret) or that the PIN
    /// came with it, and the address; with neither, that it is this PC or an address the operator listed, as the gate passed it.
    /// </summary>
    private string WireWho(string? supplied, string? keyId, IPAddress remote, bool proved)
    {
        var address = (remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote).ToString();
        if (!proved) return IsThisPc(remote) ? "this PC, no PIN set" : "listed address " + address;
        var key = keyId is not null ? _control.ApiKeys.FirstOrDefault(k => k.Id == keyId)
            : ApiKeys.LooksLikeKey(supplied) ? ApiKeys.Find(_control.ApiKeys, supplied) : null;
        return key is not null ? $"key '{key.Name}', {address}" : keyId is not null ? "a key, " + address : "PIN, " + address;
    }

    /// <summary>How long a receipt waits for the show thread before saying "accepted" (Show Bus §3).</summary>
    public static readonly TimeSpan ReceiptWait = TimeSpan.FromSeconds(3);

    private static List<string> ArgsFromQuery(IReadOnlyDictionary<string, string> q)
    {
        var args = new List<string>();
        var first = new[] { "value", "text", "slot", "n", "item", "seconds", "time", "mode", "state" }
            .Select(k => q.GetValueOrDefault(k)).FirstOrDefault(v => v is not null);
        if (first is null) return args;
        args.Add(first);
        if (q.TryGetValue("urgency", out var u)) args.Add(u);
        else if (q.TryGetValue("start", out var s) && s is "1" or "true" or "yes") args.Add("start");
        if (q.TryGetValue("flash", out var f)) { if (args.Count == 1) args.Add("info"); args.Add(f); }
        return args;
    }

    /// <returns>Null when allowed, otherwise the reason.</returns>
    private string? CheckPin(string? supplied, IPAddress remote) => CheckPin(supplied, remote, out _);

    /// <summary><see cref="CheckPin(string?, IPAddress)"/>, and whether the PIN was asked for and given right (<paramref name="proved"/>), read from the one PIN it compared.</summary>
    private string? CheckPin(string? supplied, IPAddress remote, out bool proved)
    {
        proved = false;
        var pin = _pin();
        if (string.IsNullOrEmpty(pin)) return null;

        var now = Environment.TickCount64;
        if (_pinGuesses.Locked(remote, now)) return PinsLocked;

        // A request with no PIN guesses nothing, so it is refused without being counted: a keyless app polling this
        // Stagewatch never locks its machine's address (and the browsers on it) out.
        if (supplied is null) return "PIN required";
        // A right PIN clears the PIN's count alone: wrong view passes stay counted (12.2).
        if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(pin)))
        {
            _pinGuesses.Clear(remote);
            proved = true;
            return null;
        }
        _pinGuesses.Count(remote, now);
        return "wrong PIN";
    }

    /// <summary>A refused line, said on the status line under the door it came in by.</summary>
    private void Reject(string error, string door = "Remote")
    {
        try { CommandRejected?.Invoke(door + ": " + error); } catch { }
    }

    private static bool IsQuestion(string verb) => verb.Trim().ToLowerInvariant() is "question" or "ask";

    /// <summary>Audience questions: one every 10 s and 20 an hour per phone, so nobody can flood the moderator.</summary>
    private bool AllowQuestion(IPAddress remote)
    {
        var now = Environment.TickCount64;
        var ok = true;
        _questionRate.AddOrUpdate(remote,
            _ => (1, now, now),
            (_, old) =>
            {
                if (now - old.WindowStartMs > 3_600_000) return (1, now, now);
                if (now - old.LastMs < 10_000 || old.Count >= 20) { ok = false; return old; }
                return (old.Count + 1, old.WindowStartMs, now);
            });
        if (_questionRate.Count > 5000) _questionRate.Clear();
        return ok;
    }

    /// <summary>Model Context Protocol over HTTP (JSON-RPC). PIN-protected like every other control route.</summary>
    private async Task HandleMcpAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        if (req.Method != "POST") { await WriteJsonError(stream, 405, "POST JSON-RPC to /mcp", ct); return; }
        if (Authorize(req, remote, ApiKeyScope.Mcp, out var proved) is { } denied) { await WriteDenied(stream, denied, ct); return; }
        if (!_control.McpAllowed) { await WriteJsonError(stream, 403, "MCP is off, or needs Stagewatch Production", ct); return; }
        var sender = WebSender(req, remote, proved);
        var reply = _mcp.Handle(req.Body, kind =>
        {
            // Refused at the door like the other four, and said on the status line too, so an agent probing for a peer's air is seen.
            if (PlayoutGate.Check(kind, CommandDoor.Mcp, sender, _control.PlayoutFrom) is not { } refused) return null;
            Reject(refused, "MCP");
            return refused;
        });
        if (reply is null) { await WriteAsync(stream, 202, "application/json", Array.Empty<byte>(), ct, cors: true, extra: RateHeaders(req)); return; }
        await WriteAsync(stream, 200, "application/json", reply, ct, cors: true, extra: RateHeaders(req));
    }

    /// <summary>
    /// A holding image for screens. Ids are random and never reused, so browsers may cache them for ever —
    /// a lobby full of tablets fetches each image once.
    /// </summary>
    private async Task HandleHoldingImageAsync(Stream stream, string id, CancellationToken ct)
    {
        id = id.Split('.')[0];
        if (!HoldingLibrary.IsValidId(id) || _control.HoldingImageData(id) is not { } img)
        {
            await WriteJsonError(stream, 404, "no such holding image", ct);
            return;
        }
        var head = new StringBuilder(256)
            .Append("HTTP/1.1 200 OK\r\n")
            .Append("Content-Type: ").Append(img.ContentType).Append("\r\n")
            .Append("Content-Length: ").Append(img.Data.Length).Append("\r\n")
            .Append("Cache-Control: public, max-age=31536000, immutable\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Connection: close\r\n\r\n");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60)); // big images over venue Wi-Fi
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), timeout.Token);
        await stream.WriteAsync(img.Data, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }

    /// <summary>POST /api/slides/script?hash=…  body = the deck's speaker notes (UTF-8), up to 2 MB, once per deck.</summary>
    private async Task HandleSlideScriptAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        async Task Refuse(int status, string error)
        {
            using (var drain = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                drain.CancelAfter(TimeSpan.FromSeconds(30));
                try { await req.SkipDeferredBodyAsync(stream, drain.Token); } catch (OperationCanceledException) { return; }
            }
            await WriteJsonError(stream, status, error, ct);
        }
        if (req.Method != "POST") { await Refuse(405, "POST the script"); return; }
        if (Authorize(req, remote, ApiKeyScope.Control) is { } auth) { await Refuse(auth.Status, auth.Error); return; }
        byte[]? body;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            body = await req.ReadDeferredBodyAsync(stream, timeout.Token);
        }
        if (body is null) return;
        _control.AcceptSlideScript(req.Query.GetValueOrDefault("hash") ?? "", Encoding.UTF8.GetString(body));
        await WriteAsync(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray(), ct);
    }

    /// <summary>POST /api/holding/upload?name=…  body = the image file. PIN-protected like every control route.</summary>
    private async Task HandleHoldingUploadAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        async Task Refuse(int status, string error)
        {
            using (var drain = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                drain.CancelAfter(TimeSpan.FromSeconds(30));
                try { await req.SkipDeferredBodyAsync(stream, drain.Token); } catch (OperationCanceledException) { return; }
            }
            await WriteJsonError(stream, status, error, ct);
        }
        if (req.Method != "POST") { await Refuse(405, "POST the image file"); return; }
        if (Authorize(req, remote, ApiKeyScope.Control) is { } auth) { await Refuse(auth.Status, auth.Error); return; }
        if (req.DeferredLength <= 0) { await WriteJsonError(stream, 400, "empty upload", ct); return; }
        byte[]? body;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(120));
            body = await req.ReadDeferredBodyAsync(stream, timeout.Token);
        }
        if (body is null) return;
        // Saving and checking the file is disk work: keep it off the accept loop's threads' hot path.
        var (id, error) = await Task.Run(() => _control.AddHoldingImage(req.Query.GetValueOrDefault("name") ?? "Uploaded image", body), ct);
        if (id is null) { await WriteJsonError(stream, error?.Contains("needs Stagewatch") == true ? 403 : 400, error ?? "upload failed", ct); return; }
        await WriteAsync(stream, 200, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = true, id })), ct, cors: true);
    }

    /// <summary>
    /// The viewing code, when the operator set one: a screen must give it (?view=), or be a crew link, or have the PIN.
    /// Wrong codes count as wrong view passes (<see cref="ViewPass"/>), apart from wrong PINs. Returns why not, or null.
    /// </summary>
    private string? CheckView(HttpRequest req, IPAddress remote)
    {
        if (string.IsNullOrEmpty(_control.ViewCode)) return null;
        return IsAuthorized(req, remote, ApiKeyScope.Read) ? null : ViewPass(req, remote);
    }

    /// <summary>
    /// <see cref="CheckView"/> for a request whose PIN or key was already checked and did not open the view: only a view pass —
    /// a crew link, a window's address or the code itself — may (so a wrong PIN is never counted twice). A device locked out of
    /// view passes is refused every one. A refused request counts each wrong pass it gave, in the view passes' own count: a
    /// window's address, a code, a key that is no crew link. A pass that decided nothing (a stale crew link beside the right
    /// code, say) is not counted: its answer was the same either way, so it guessed nothing, and a crew page whose link was
    /// replaced still opens on the code a person typed.
    /// </summary>
    private string? ViewPass(HttpRequest req, IPAddress remote)
    {
        var code = _control.ViewCode;
        if (string.IsNullOrEmpty(code)) return null;
        if (PassesLockedOut(remote)) return PassesLocked;
        var (secret, _) = _control.CrewKeyPolicy;
        var crewKey = req.Query.GetValueOrDefault("key");
        if (!string.IsNullOrEmpty(secret) && CrewKeys.IsAnyCrewKey(secret, _control.CrewDepartments, crewKey)) return null;
        // A key that is no crew link is counted only once nothing else opened the view.
        void CountCrewKey() { if (!string.IsNullOrEmpty(secret) && !string.IsNullOrWhiteSpace(crewKey)) CountWrongPass(remote); }
        // The pages a window shows fetch with its address: the operator made the window, so it may view.
        if (req.Query.GetValueOrDefault("win") is { } win)
        {
            if (WindowFor(win, remote, out var locked) is not null) return null;
            if (locked) return PassesLocked;
            CountCrewKey();
            return "Wrong window address";
        }
        var supplied = req.Query.GetValueOrDefault("view");
        if (supplied is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(code))) return null;
        if (supplied is not null) CountWrongPass(remote);
        CountCrewKey();
        return supplied is null ? NeedsViewCode : WrongViewCode;
    }

    private const string NeedsViewCode = "This screen needs the viewing code";
    private const string WrongViewCode = "Wrong viewing code";

    private static string DeptOf(ShowCommand cmd) => cmd.Get<DeptNoteArgs>().Department;

    /// <summary>A department's notes may be read or written without the PIN only from that department's crew link
    /// (when crew links are required). Returns why not, or null.</summary>
    private string? CheckDeptKey(string dept, string? key, IPAddress remote, WsClient? socket = null)
    {
        var (secret, required) = _control.CrewKeyPolicy;
        if (!required || string.IsNullOrEmpty(secret)) return null;
        return CrewLinkFor(secret, dept, key, remote, $"This page can't edit {dept}'s notes. Open {dept}'s crew link (Showcaller page → Crew links).", socket);
    }

    /// <summary>
    /// A cue confirmed without the PIN must come from the crew page it was sent to: the cue has to exist and,
    /// when crew keys are on, the page's key must belong to the cue's target. The device's address is added to
    /// the "by" name, so the show log says where a confirmation came from. Returns why it was refused, or null.
    /// </summary>
    private string? CheckCrewAck(ref ShowCommand cmd, string? key, IPAddress remote, WsClient? socket = null)
    {
        var id = (int)cmd.Number;
        var signal = _control.Snapshots.Latest.Signals.FirstOrDefault(g => g.Id == id);
        if (signal.Id != id || id == 0) return SignalBoard.GoneWords;
        var (secret, required) = _control.CrewKeyPolicy;
        if (required && !string.IsNullOrEmpty(secret)
            && CrewLinkFor(secret, signal.Target, key, remote, $"This screen can't confirm cues for {signal.Target}. Open its crew link (Showcaller page → Crew links).", socket) is { } refused)
            return refused;
        var ack = cmd.Get<AckArgs>();
        var by = SignalBoard.AnsweredBy(ack.By.Replace("|", "/"), remote.ToString());
        cmd = cmd with { Text = by + "|" + ack.State, Args = new AckArgs(by, ack.State) };
        return null;
    }

    /// <summary>
    /// A crew link asked for one page's target (a department's notes, a cue's confirmation): null when <paramref name="key"/> is
    /// that target's, else <paramref name="refusal"/>. No key guesses nothing, and another page's crew link is a real link on the
    /// wrong page, so neither is counted; a key that is no crew link at all is a wrong view pass — once for a socket
    /// (<paramref name="socket"/>), which gave it once, and once for each HTTP request, which gives it again. A device locked
    /// out of view passes is refused even its right crew link.
    /// </summary>
    private string? CrewLinkFor(string secret, string target, string? key, IPAddress remote, string refusal, WsClient? socket)
    {
        if (string.IsNullOrWhiteSpace(key)) return refusal;
        if (PassesLockedOut(remote)) return PassesLocked;
        if (CrewKeys.Matches(secret, target, key)) return null;
        if (!CrewKeys.IsAnyCrewKey(secret, _control.CrewDepartments, key) && (socket is null || socket.FirstCountOfCrewKey())) CountWrongPass(remote);
        return refusal;
    }

    private static string? PinFrom(HttpRequest req)
        => req.Query.GetValueOrDefault("pin") ?? req.Headers.GetValueOrDefault("x-stagewatch-pin") ?? BearerPin(req.Headers.GetValueOrDefault("authorization"));

    private static bool HasPin(HttpRequest req) => PinFrom(req) is not null;

    /// <summary>
    /// A refusal: HTTP status, reason, (for 429) the seconds to wait, and whether the caller links again (<paramref name="Relink"/>: a
    /// session credential whose link ended, 13.7.1).
    /// </summary>
    private sealed record Denied(int Status, string Error, int RetryAfter = 0, bool Relink = false);

    /// <summary>A side call's S whose key went out of force after its door let it in: the link ended, as the session door says it (13.7.1).</summary>
    private static readonly Denied LinkEnded = new(401, "the link ended", Relink: true);

    /// <summary>
    /// The one door for HTTP routes: the PIN opens everything; an API key (swk_…) opens what its scope allows and is
    /// rate limited. Wrong PINs count toward the address's PIN lockout; an unknown or revoked key and a missing PIN never
    /// do, since neither is a guess (a key is 160 random bits), and an app still polling with a revoked key would lock its
    /// whole machine out. A locked address is refused even a valid key. A valid key clears nothing: it vouches only for
    /// itself, never for whoever is typing PINs from its address, so the PIN's count is reset only by the right PIN or by
    /// its minute running out, and a key holder cannot put keyed requests between PIN guesses. Wrong view passes are
    /// counted apart, and nothing here reads or clears their count (12.2). Null = allowed.
    /// </summary>
    private Denied? Authorize(HttpRequest req, IPAddress remote, ApiKeyScope need) => Authorize(req, remote, need, out _);

    /// <summary>
    /// <see cref="Authorize(HttpRequest, IPAddress, ApiKeyScope)"/>, and whether the request proved a credential when it is let in
    /// (<paramref name="proved"/>): the PIN asked for and given right, or a key in force. Let in with neither, it proved nothing: an
    /// open door (no PIN set), which the playout gate does not count as a credential.
    /// </summary>
    private Denied? Authorize(HttpRequest req, IPAddress remote, ApiKeyScope need, out bool proved)
    {
        if (req.SessionKeyId is not { } keyId) return AuthorizeSecret(PinFrom(req), remote, need, out proved);
        // A revocation ends S (13.7.1): a key gone between the session door and the route is answered as the door answers an ended link.
        if (_control.ApiKeys.FirstOrDefault(k => k.Id == keyId) is { } key) return AuthorizeKey(key, remote, need, out proved);
        proved = false;
        return LinkEnded;
    }

    /// <summary><see cref="Authorize(HttpRequest, IPAddress, ApiKeyScope)"/> for a secret already read from the request (a socket's Authorization header).</summary>
    private Denied? AuthorizeSecret(string? supplied, IPAddress remote, ApiKeyScope need) => AuthorizeSecret(supplied, remote, need, out _);

    private Denied? AuthorizeSecret(string? supplied, IPAddress remote, ApiKeyScope need, out bool proved)
    {
        proved = false;
        if (!ApiKeys.LooksLikeKey(supplied))
            return CheckPin(supplied, remote, out proved) is { } pinError ? new Denied(401, pinError) : null;

        return AuthorizeKey(ApiKeys.Find(_control.ApiKeys, supplied), remote, need, out proved);
    }

    /// <summary>
    /// A key's request as <see cref="Authorize(HttpRequest, IPAddress, ApiKeyScope)"/> checks it once the key is found — by its secret, or
    /// by the session credential of the link that proved it (13.7.1): refused while its address is locked out by wrong PINs, unknown or
    /// revoked, or outside its scope; otherwise one use of its rate.
    /// </summary>
    private Denied? AuthorizeKey(ApiKey? key, IPAddress remote, ApiKeyScope need, out bool proved)
    {
        proved = false;
        if (_pinGuesses.Locked(remote, Environment.TickCount64))
            return new Denied(401, PinsLocked);
        if (key is null)
            return new Denied(401, "unknown or revoked API key");
        if (!ApiKeys.Allows(key.Scope, need))
            return new Denied(403, $"this API key is {ApiKeys.Describe(key.Scope)}");
        var over = Spend(key.Id);
        proved = over is null;
        return over;
    }

    /// <summary>Whether the request carries a PIN or key good for <paramref name="need"/>. Only tests what was given.</summary>
    private bool IsAuthorized(HttpRequest req, IPAddress remote, ApiKeyScope need)
        => req.SessionKeyId is not null ? Authorize(req, remote, need) is null
            : HasPin(req) && (ApiKeys.LooksLikeKey(PinFrom(req)) ? Authorize(req, remote, need) is null : CheckPin(PinFrom(req), remote) is null);

    /// <summary>
    /// The key a request's credential is: the one its link's session credential speaks for on a side call, else a key in force it carries;
    /// null for the PIN, nothing, or a key unknown or revoked.
    /// </summary>
    private ApiKey? KeyOf(HttpRequest req)
    {
        if (req.SessionKeyId is { } id) return _control.ApiKeys.FirstOrDefault(k => k.Id == id);
        var supplied = PinFrom(req);
        return ApiKeys.LooksLikeKey(supplied) ? ApiKeys.Find(_control.ApiKeys, supplied) : null;
    }

    /// <summary>Count one request against a key's minute. Null = within its limit.</summary>
    private Denied? Spend(string keyId)
    {
        var now = Environment.TickCount64;
        var over = false;
        var use = _keyUse.AddOrUpdate(keyId,
            _ => (1, now, DateTime.UtcNow),
            (_, old) =>
            {
                if (now - old.WindowStartMs >= 60_000) return (1, now, DateTime.UtcNow);
                if (old.Count >= ApiKeys.PerMinute) { over = true; return old; }
                return (old.Count + 1, old.WindowStartMs, DateTime.UtcNow);
            });
        if (_keyUse.Count > 1000) _keyUse.Clear();
        return over ? new Denied(429, "rate limit: too many requests with this key", (int)Math.Ceiling((60_000 - (now - use.WindowStartMs)) / 1000.0)) : null;
    }

    /// <summary>Requests already counted by IsAuthorized (the /api/state read) are refused here if over the limit.</summary>
    private Denied? KeyOver(HttpRequest req)
    {
        if (KeyOf(req) is not { } key) return null;
        return _keyUse.TryGetValue(key.Id, out var u) && u.Count >= ApiKeys.PerMinute && Environment.TickCount64 - u.WindowStartMs < 60_000
            ? new Denied(429, "rate limit: too many requests with this key", 60) : null;
    }

    /// <summary>X-RateLimit-* headers for a request made with an API key ("" otherwise).</summary>
    private string RateHeaders(HttpRequest req)
    {
        if (KeyOf(req) is not { } key || !_keyUse.TryGetValue(key.Id, out var u)) return "";
        var reset = Math.Max(0, (int)Math.Ceiling((60_000 - (Environment.TickCount64 - u.WindowStartMs)) / 1000.0));
        return $"X-RateLimit-Limit: {ApiKeys.PerMinute}\r\nX-RateLimit-Remaining: {Math.Max(0, ApiKeys.PerMinute - u.Count)}\r\nX-RateLimit-Reset: {reset}\r\n";
    }

    /// <summary>When an API key was last used this session (null = not yet).</summary>
    public DateTime? KeyLastUsed(string id) => _keyUse.TryGetValue(id, out var u) ? u.LastUtc : null;

    private static Task WriteDenied(Stream stream, Denied d, CancellationToken ct)
        => WriteAsync(stream, d.Status, "application/json", d.Relink ? JsonSerializer.SerializeToUtf8Bytes(new { ok = false, error = d.Error, relink = true })
                : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error = d.Error })), ct, cors: true,
            extra: d.RetryAfter > 0 ? $"Retry-After: {d.RetryAfter}\r\nX-RateLimit-Limit: {ApiKeys.PerMinute}\r\nX-RateLimit-Remaining: 0\r\nX-RateLimit-Reset: {d.RetryAfter}\r\n" : null);

    private static string? BearerPin(string? header)
        => header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;

    // ------------------------------------------------------------------ WebSocket

    /// <summary>
    /// Moves on every <see cref="CredentialsChanged"/> and every binding made (<see cref="NodeBindings.Bound"/>), so a socket
    /// let in meanwhile is checked again once it is listed.
    /// </summary>
    private int _credentialsVersion;

    /// <summary>1 while a recheck for a binding made is queued and not yet started: bindings made together share one pass.</summary>
    private int _boundRecheckQueued;

    /// <summary>
    /// The API keys or the PIN changed (owner thread, after the keys in force, the bindings and the PIN were swapped in):
    /// every open socket a key or the PIN admitted is checked again at once (<see cref="NodeLinks.Recheck"/>). A revoked
    /// key's socket drops to the public frame and hears "this key was revoked" (a node link becomes a claim); a moved
    /// binding re-sends a node link's verdict; a changed PIN drops a control page to a viewer. Only the viewing code
    /// closes. Never waits: each socket's own send loop says it.
    /// </summary>
    public void CredentialsChanged()
    {
        Interlocked.Increment(ref _credentialsVersion);
        RecheckAll();
    }

    /// <summary>
    /// A key came to speak for a node (a node's first use on any route, or the operator's choice): every open node link
    /// that names that id, or uses that key, may hear a new verdict. Raised on whatever thread bound it, perhaps under a
    /// socket's <see cref="WsClient.Gate"/>, so the pass is posted to the pool, never run here.
    /// </summary>
    private void OnBound(NodeBound bound)
    {
        Interlocked.Increment(ref _credentialsVersion);
        if (Interlocked.Exchange(ref _boundRecheckQueued, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Volatile.Write(ref _boundRecheckQueued, 0);   // a binding made from here on queues a pass of its own
            RecheckAll();
        });
    }

    private void RecheckAll()
    {
        foreach (var c in _sockets.Keys) Recheck(c);
    }

    /// <summary>
    /// One socket checked again against the keys, the PIN and the bindings as they are now, read under its gate, so of two
    /// rechecks the later one reads the later state and its verdict is the last one posted.
    /// </summary>
    private void Recheck(WsClient client)
    {
        if (client.WindowId is not null) return;
        lock (client.Gate)
        {
            var link = client.Link;
            if (link.KeyId is null && link.PinHash is null) return;
            var r = NodeLinks.Recheck(link, _control.ApiKeys, _control.Nodes, NodeLinks.PinHash(_pin()), ViewRefusalWithoutCredential(client));
            if (ReferenceEquals(r.Link, link)) return;
            client.Link = r.Link;
            // A LINK socket refused by the change has no lid any more: its session credential ends now (13.5.5, 13.7).
            if (link.Lid is { } lid && r.Link.Lid is null && _sessions.TryGetValue(lid, out var held) && ReferenceEquals(held.Client, client))
                _sessions.TryRemove(new KeyValuePair<string, LinkSession>(lid, held));
            if (r.Message is not null) client.Post(r.Message, r.Close);
        }
        client.Signal();   // its send loop says so now, and sends the frame its new access reads
    }

    /// <summary>
    /// Why a socket may no longer view once its key or PIN is gone (the viewing code's words), or null when it still passes
    /// without them: no code set, a crew link, or the code it gave. Counts nothing.
    /// </summary>
    private string? ViewRefusalWithoutCredential(WsClient client)
    {
        var code = _control.ViewCode;
        if (string.IsNullOrEmpty(code)) return null;
        var (secret, _) = _control.CrewKeyPolicy;
        if (!string.IsNullOrEmpty(secret) && CrewKeys.IsAnyCrewKey(secret, _control.CrewDepartments, client.CrewKey)) return null;
        if (client.ViewGiven is not { } given) return NeedsViewCode;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(code)) ? null : WrongViewCode;
    }

    /// <summary>
    /// A session credential on a side call (13.7.1), checked by the core against the live, verified LINK socket its lid names: null when it
    /// passes; otherwise the 401 body, <c>{"ok":false,"error",…,"relink":true}</c>. A right S that came in clear from another machine is
    /// refused and its link ended at once: its socket is aborted and its S forgotten. A wrong one ends nothing, for a lid is no secret.
    /// </summary>
    private (byte[]? Refusal, string? KeyId) CheckSession(HttpRequest req, IPAddress remote, bool secure, string authorization)
    {
        var check = PeerLink.CheckSession(authorization, lid => _sessions.TryGetValue(lid, out var s) ? s.Session : null, !secure && !IsLocal(req, remote));
        if (check.Ends is { } gone && _sessions.TryRemove(gone, out var ended)) ended.Client.Socket.Abort();
        if (check.Status != 200) return (Relink(check.Words, check.Relink), null);
        // The key the link proved, read now: a link a recheck refused has no key and no lid, and its S has ended with them.
        return PeerLink.ParseShowBus(authorization) is { } a && LinkKeyOf(a.Lid) is { } keyId ? (null, keyId) : (Relink("the link ended", true), null);

        static byte[] Relink(string words, bool relink) => JsonSerializer.SerializeToUtf8Bytes(new { ok = false, error = words, relink });
    }

    /// <summary>The key a live, verified link proved, by its lid; null when no link holds that lid now.</summary>
    private string? LinkKeyOf(string lid)
        => _sessions.TryGetValue(lid, out var held) && held.Client.Link is { KeyId: { } keyId, Lid: { } live } && live == lid ? keyId : null;

    /// <summary>
    /// The key a session credential speaks for when it verifies against a live link (13.7.1), read without deciding anything: no link is
    /// ended and nothing is counted. Null for no S, a wrong one, or one whose link is gone.
    /// </summary>
    private string? LiveSessionKey(string? authorization)
        => PeerLink.ParseShowBus(authorization) is { } a
           && PeerLink.CheckSession(authorization, lid => _sessions.TryGetValue(lid, out var s) ? s.Session : null).Status == 200
            ? LinkKeyOf(a.Lid) : null;

    /// <summary>
    /// The routes a live link's session credential speaks for its key on (13.7.1; §8's Stagewatch row): its context
    /// (<c>/api/context</c>), a node's own context (<c>/api/peers/context</c>), the approval queue and its results
    /// (<c>/api/proposals</c> and below it), and a stage message (<c>/api/message</c> and below it, and its clearing). Elsewhere S is
    /// no credential.
    /// </summary>
    public static bool SpeaksOn(string path)
    {
        if (path is "/api/context" or "/api/peers/context" or "/api/proposals" || path.StartsWith("/api/proposals/", StringComparison.Ordinal)) return true;
        if (!path.StartsWith("/api/", StringComparison.Ordinal)) return false;
        var verb = path["/api/".Length..];
        var slash = verb.IndexOf('/');
        return (slash < 0 ? verb : verb[..slash]) is "message" or "clearmessage" or "messageclear" or "clear";
    }

    /// <summary>
    /// 13.6.9: an API key in force sent in clear from another machine — no TLS, and not a local request (<see cref="IsLocal"/>) — is
    /// marked seen in clear, from its address, the first time (<see cref="KeySightings.Mark"/>). Every carrier is read (<c>?pin=</c>,
    /// <c>X-Stagewatch-Pin</c>, <c>Authorization: Bearer</c>), not only the one a route would read first: each was on the path. The PIN, a
    /// view pass and S are no keys; an unknown or revoked key marks nothing.
    /// </summary>
    private void NoteClear(HttpRequest req, IPAddress remote, bool secure)
    {
        if (secure) return;
        string?[] carriers = [req.Query.GetValueOrDefault("pin"), req.Headers.GetValueOrDefault("x-stagewatch-pin"), BearerPin(req.Headers.GetValueOrDefault("authorization"))];
        if (!carriers.Any(ApiKeys.LooksLikeKey) || IsLocal(req, remote)) return;
        var from = Screens.AddressOf(remote);
        foreach (var supplied in carriers)
            if (ApiKeys.LooksLikeKey(supplied) && ApiKeys.Find(_control.ApiKeys, supplied) is { } key)
                _control.Sightings.Mark(key.Id, from, DateTime.UtcNow);
    }

    /// <summary>
    /// One device's place among its unfinished <c>link=</c> sockets (13.6.3), held from before the upgrade until the socket's link message
    /// is answered or the socket ends, whichever comes first; given back once.
    /// </summary>
    private sealed class UnpairedHold(ConcurrentDictionary<IPAddress, int> table, IPAddress device)
    {
        private int _held;

        /// <summary>Takes a place when fewer than <see cref="UnpairedPerAddress"/> are held for the device; false, holding nothing, otherwise.</summary>
        public bool Take()
        {
            while (true)
            {
                var now = table.GetOrAdd(device, 0);
                if (now >= UnpairedPerAddress) return false;
                if (table.TryUpdate(device, now + 1, now))
                {
                    Volatile.Write(ref _held, 1);
                    return true;
                }
            }
        }

        public void Release()
        {
            if (Interlocked.Exchange(ref _held, 0) == 0) return;
            while (true)
            {
                if (!table.TryGetValue(device, out var now)) return;
                if (now <= 1 ? table.TryRemove(new KeyValuePair<IPAddress, int>(device, now)) : table.TryUpdate(device, now - 1, now)) return;
            }
        }
    }

    private async Task HandleWebSocketAsync(Stream stream, HttpRequest req, IPAddress remote, bool secure, CancellationToken ct)
    {
        var unpaired = new UnpairedHold(_unpaired, remote);
        try { await HandleWebSocketAsync(stream, req, remote, secure, unpaired, ct); }
        finally { unpaired.Release(); }
    }

    private async Task HandleWebSocketAsync(Stream stream, HttpRequest req, IPAddress remote, bool secure, UnpairedHold unpaired, CancellationToken ct)
    {
        // A socket that names a node and asks for what a node link can't be (a window's, a PIN in the URL, control, a bad
        // id or kind, this Stagewatch's own id), or asks for LINK with a header credential or no node, is refused before the
        // upgrade and before any credential, window address or viewing code is checked, so it is never counted.
        if (NodeLinks.BadRequest(req.Query, req.Headers.GetValueOrDefault("authorization"), _control.OwnNodeId) is { } bad)
        {
            await WriteJsonError(stream, 400, bad, ct);
            return;
        }

        // A socket that asks for LINK (13.6.5): in clear only from this machine, Host and Origin checked before loopback is trusted;
        // and no more of them unfinished from one device than the bound, the next answered before its upgrade. Nothing is counted.
        var linkAsked = req.Query.ContainsKey("link");
        var bind = "plain";
        if (linkAsked)
        {
            if (!secure && !IsLocal(req, remote))
            {
                await WriteJsonError(stream, 403, LinkNeedsTls, ct);
                return;
            }
            if (!unpaired.Take())
            {
                await WriteAsync(stream, 429, "application/json", JsonSerializer.SerializeToUtf8Bytes(new { ok = false, error = LinkBusy }), ct, cors: true,
                    extra: "Retry-After: " + (PeerLink.HandshakeMs / 1000).ToString(CultureInfo.InvariantCulture) + "\r\n");
                return;
            }
            if (secure && Fingerprint is { } fp) bind = "tls:" + fp;
        }
        // Keys, the PIN or bindings that change from here on are checked again once this socket is listed.
        var credentialsSeen = Volatile.Read(ref _credentialsVersion);

        // A window's link: its address names it and is its pass (it views, and sends only what any public page may). A
        // device locked out of view passes is turned away for now, never told its address is gone.
        var winToken = req.Query.GetValueOrDefault("win");
        var lockedWindow = false;
        var hit = winToken is null ? null : WindowFor(winToken, remote, out lockedWindow);
        var window = hit?.Window;

        var nodeAsked = req.Query.ContainsKey("node");
        var wantsControl = winToken is null && req.Query.GetValueOrDefault("role") == "control";
        // The PIN or a key may come in the Authorization header (Show Bus §7: never in a URL), or ?pin= from a browser.
        var supplied = winToken is not null ? null : BearerPin(req.Headers.GetValueOrDefault("authorization")) ?? req.Query.GetValueOrDefault("pin");
        var pin = _pin();
        var pinAsked = !string.IsNullOrEmpty(pin);
        WsCredential credential;
        if (winToken is not null) credential = WsCredential.None;
        else if (ApiKeys.LooksLikeKey(supplied))
            // Every valid key reads (what more it may do is its scope's); unknown or revoked keys are refused, never counted.
            credential = AuthorizeSecret(supplied, remote, ApiKeyScope.Read) is { } denied
                ? WsCredential.KeyRefused(denied.Error, denied.RetryAfter)
                : ApiKeys.Find(_control.ApiKeys, supplied) is { } k ? WsCredential.Key(k.Id, k.Scope) : WsCredential.KeyRefused("unknown or revoked API key");
        else if (pinAsked && (wantsControl || (nodeAsked && supplied is not null)))
            // A control page, or a node link that brings the operator's PIN: a wrong PIN is a guess and counts.
            credential = CheckPin(supplied, remote) is { } pinError ? WsCredential.PinRefused(pinError) : WsCredential.Pin;
        else credential = WsCredential.None;   // a viewer's ?pin= is the viewing code's business, as it always was

        var key = req.Headers.GetValueOrDefault("sec-websocket-key") ?? "";
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var head = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                   $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);

        using var ws = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });
        var detail = req.Query.GetValueOrDefault("detail") is "1" or "true";

        // LINK (13.6.5): the offer first, then the socket's one link message within peerlink.handshake.ms, judged before anything of
        // the show is sent. A socket that sends none in time is closed; a verdict makes its credential the key it proved, a refusal
        // leaves it a public viewer. Its place among the device's unfinished sockets is given back once it is answered.
        LinkDecision? linkVerdict = null;
        if (linkAsked)
        {
            try { linkVerdict = await LinkHandshakeAsync(ws, req, remote, bind, ct); }
            finally { unpaired.Release(); }
            if (linkVerdict is null) return;
            credential = linkVerdict.Ok ? WsCredential.Key(linkVerdict.Key!.Id, linkVerdict.Key.Scope) : WsCredential.KeyRefused(linkVerdict.Words, linkVerdict.RetrySeconds);
        }
        if (winToken is not null && window is null)
        {
            // Revoked, deleted or never made: the screen says so and stops trying. Locked out: it tries again later.
            try
            {
                if (!lockedWindow) await ws.SendAsync(WindowGone, WebSocketMessageType.Text, true, ct);
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "window address", closeCts.Token);
            }
            catch { }
            return;
        }
        WsAdmission admission;
        if (window is not null) admission = new WsAdmission(new WsLink { BrowserId = Screens.WindowPrefix + window.Id }, null, false);
        else
        {
            // A node link's cid and page are ignored: its node id is its identity.
            var cid = nodeAsked ? null : req.Query.GetValueOrDefault("cid");
            var browserId = Screens.ValidBrowserId(cid) ? cid! : "anon-" + Guid.NewGuid().ToString("N")[..8];
            // A control page or a node link had its credential checked above: only a crew link or the code may open the
            // view now. Any other screen is checked as it always was (its ?pin= or key included).
            WsAdmission Admit(WsCredential c) => NodeLinks.Admit(req.Query, c, pinAsked, NodeLinks.PinHash(pin),
                () => wantsControl || nodeAsked ? ViewPass(req, remote) : CheckView(req, remote), browserId, _control.Nodes, DateTime.UtcNow);
            admission = Admit(credential);
            // 13.5.2: a LINK whose proof verified but whose key may not speak for the id it named — the key bound to another node, or
            // the id held by another key — is refused class binding, as a live link whose binding moves is: the binding's words, retry
            // 60, the socket a public viewer, and counted nowhere (its proof was right). No verdict with "verified":"none" is sent.
            if (linkVerdict is { Ok: true } && admission.Link is { Class: WsClass.Node, Verified: NodeProof.None, KeyId: not null, Words: { } bindingWords })
            {
                linkVerdict = LinkDecision.Refused("binding", bindingWords);
                credential = WsCredential.KeyRefused(bindingWords, NodeLinks.RetrySeconds);
                admission = Admit(credential);
            }
            // A LINK socket's first message is its verdict with LINK's fields, or its refusal with LINK's class (13.6.5); a refused
            // one the viewing code shuts out hears that refusal marked view before it closes.
            if (linkVerdict is { Ok: false } r && admission.Close)
                admission = admission with { First = LinkAcceptor.Refusal(r.Class ?? "refused", r.Words, r.RetrySeconds, view: true) };
            else if (linkVerdict is { } l && !admission.Close)
            {
                var link = admission.Link with { Lid = l.Ok ? l.Lid : null };
                admission = admission with
                {
                    Link = link,
                    First = l.Ok
                        ? LinkAcceptor.Verdict(link, l.Proof!, l.Lid!, LinkCan, Inst, (int)HeartbeatEvery.TotalMilliseconds)
                        : LinkAcceptor.Refusal(l.Class!, l.Words, l.RetrySeconds),
                };
            }
        }
        var client = new WsClient(ws, window is not null || detail, remote, admission.Link)
        {
            CrewKey = window is null ? req.Query.GetValueOrDefault("key") : null,
            ViewGiven = window is null ? req.Query.GetValueOrDefault("view") : null,
            Page = window is not null ? "/w/" + window.Name : nodeAsked ? "/" : Screens.CleanPage(req.Query.GetValueOrDefault("page")),
            Device = Screens.Device(req.Headers.GetValueOrDefault("user-agent")),
            WindowId = window?.Id,
            WindowToken = window is null ? null : winToken,
            WindowVia = hit?.Via,
            Sender = WebSender(req, remote, proved: false),
            // 12.5–12.6: what carries a node link (TLS, this machine's loopback, or neither) and what it offers for windows, read once at
            // its upgrade; a window or a screen offers nothing.
            LinkWord = secure ? WindowGrants.LinkTls : IsLocal(req, remote) ? WindowGrants.LinkLocal : WindowGrants.LinkPlain,
            Can = nodeAsked ? WindowGrants.Offered(req.Query.GetValueOrDefault("can"), req.Query.GetValueOrDefault("t")) : [],
        };
        if (admission.Close)
        {
            // Screens need the viewing code: one message saying so (the page asks for it), then close.
            try
            {
                if (admission.First is { } refusal) await client.SendTextAsync(refusal, ct);
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "viewing code", closeCts.Token);
            }
            catch { }
            return;
        }
        // The session is held before the socket is listed, so a credential change that refuses the link once it is listed ends it too.
        (string Lid, LinkSession? Held) session = linkVerdict is { Ok: true, Lid: { } lid0, Session: { } s0 } ? (lid0, new LinkSession(s0, client)) : ("", null);
        if (session.Held is not null) _sessions[session.Lid] = session.Held;
        _sockets.TryAdd(client, 0);
        try
        {
            // What this Stagewatch made of the socket comes before any frame: a control page's auth, a node link's verdict,
            // a refused key's one refusal. A socket with an accepted key, or none, hears a frame first, as it always did.
            if (admission.First is { } first) await client.SendTextAsync(first, ct);
            if (Volatile.Read(ref _credentialsVersion) != credentialsSeen) Recheck(client);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var send = SendLoopAsync(client, linked.Token);
            var receive = ReceiveLoopAsync(client, linked.Token);
            await Task.WhenAny(send, receive);
            linked.Cancel();
            try { await Task.WhenAll(send, receive); } catch { }

            if (ws.State == WebSocketState.Open)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token); } catch { }
            }
        }
        finally
        {
            _sockets.TryRemove(client, out _);
            if (session.Held is not null) _sessions.TryRemove(new KeyValuePair<string, LinkSession>(session.Lid, session.Held));
        }
    }

    /// <summary>
    /// LINK on a <c>link=</c> socket (13.6.3, 13.6.5): the offer with a fresh snonce, then the one link message, read within
    /// peerlink.handshake.ms on <see cref="LinkClock"/>, decided by <see cref="LinkAcceptor.Decide"/>. A device locked out by wrong PINs
    /// proves no key; a pair the wrong-proof table locks is refused before its proof is checked; a known kid's wrong proof is counted, an
    /// unknown kid's never; a right proof clears no count and spends one use of its key. Null when no link message came in time, or the
    /// socket ended first.
    /// </summary>
    private async Task<LinkDecision?> LinkHandshakeAsync(WebSocket ws, HttpRequest req, IPAddress remote, string bind, CancellationToken ct)
    {
        var ownId = _control.OwnNodeId!;
        var snonce = RandomNumberGenerator.GetHexString(PeerLink.NonceBytes * 2, lowercase: true);
        using (var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            sendCts.CancelAfter(SendTimeout);
            await ws.SendAsync(LinkAcceptor.Offer(ownId, snonce, bind), WebSocketMessageType.Text, true, sendCts.Token);
        }
        if (await ReadLinkMessageAsync(ws, ct) is not { } bytes) return null;
        JsonElement message;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            message = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return LinkDecision.Refused("malformed", LinkAcceptor.MalformedWords);
        }
        if (_pinGuesses.LockedFor(remote, Environment.TickCount64) is var pinsHeld and > 0) return LinkDecision.Refused("locked", PinsLocked, pinsHeld);
        var now = LinkNowMs;
        var keys = _control.ApiKeys;
        // A key in force's proof holds a place in its pair before it is checked and settles it after, so proofs checked together never
        // pass the limit; an unknown kid holds and counts nothing.
        string? held = null;
        LinkDecision? d = null;
        try
        {
            d = LinkAcceptor.Decide(req.RawQuery, req.Query.GetValueOrDefault("node"), req.Query.GetValueOrDefault("t"), message, ownId, snonce, bind, keys, kid =>
            {
                if (LinkAcceptor.KeyOf(keys, kid) is null) return 0;
                var locked = _proofGuesses.Reserve(remote, kid, now);
                if (locked == 0) held = kid;
                return locked;
            });
        }
        finally
        {
            if (held is not null) _proofGuesses.Settle(remote, held, now, wrong: d?.CountKid == held);
            else if (d?.CountKid is { } counted) _proofGuesses.Count(remote, counted, now);
        }
        if (!d.Ok || d.Key is not { } key) return d;
        return Spend(key.Id) is { } over ? LinkDecision.Refused("busy", over.Error, over.RetryAfter) : d;
    }

    /// <summary>
    /// The one link message, or null when none came within peerlink.handshake.ms on <see cref="LinkClock"/> (the read is then cancelled,
    /// which ends the socket) or the socket closed first. A message past <see cref="MaxWsMessageBytes"/>, or one that is not text, is read
    /// to its end and handed on as empty, which no decision takes.
    /// </summary>
    private async Task<byte[]?> ReadLinkMessageAsync(WebSocket ws, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var read = ReadOneAsync(ws, cts.Token);
        var deadline = Task.Delay(TimeSpan.FromMilliseconds(PeerLink.HandshakeMs), LinkClock, cts.Token);
        var first = await Task.WhenAny(read, deadline);
        await cts.CancelAsync();
        if (first == read) return await read;
        try { await read; }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or IOException)
        {
            // The deadline cancelled the read: the socket ends unanswered, as 13.6.3 asks.
        }
        return null;

        static async Task<byte[]?> ReadOneAsync(WebSocket ws, CancellationToken ct)
        {
            var buffer = new byte[MaxWsMessageBytes];
            var count = 0;
            var over = false;
            WebSocketReceiveResult r;
            do
            {
                if (count >= buffer.Length)
                {
                    over = true;
                    count = 0;
                }
                r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), ct);
                if (r.MessageType == WebSocketMessageType.Close) return null;
                count += r.Count;
            } while (!r.EndOfMessage);
            return over || r.MessageType != WebSocketMessageType.Text ? [] : buffer.AsSpan(0, count).ToArray();
        }
    }

    private async Task SendLoopAsync(WsClient client, CancellationToken ct)
    {
        byte[]? lastSent = null;
        byte[]? lastWindow = null;
        var assigned = new Dictionary<string, (ScreenWindow Window, byte[] Json)>(StringComparer.Ordinal);
        while (!ct.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
        {
            // What a credential change made of this socket goes out before anything else, from this one sender, so the
            // newest verdict is the last one heard; only the viewing code closes it.
            if (client.TakePosted() is { } posted)
            {
                await client.SendTextAsync(posted.Json, ct);
                if (posted.Close)
                {
                    using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try { await client.Socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "viewing code", closeCts.Token); }
                    catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException or IOException) { }
                    return;
                }
            }
            // A show app's own key-verified link hears its grants after its verdict and before any frame, then whole whenever they
            // change, and each window granted it to draw (12.5–12.6). No other socket hears either.
            if (GrantsNode(client.Link) is { } node && client.Link.KeyId is { } keyId)
                await SendGrantsAsync(client, node, keyId, assigned, ct);

            // A window hears what to show before any frame, and again whenever that changes; an address that stopped working is told
            // so and let go: the window's own address (revoked, deleted) in words (12.3), a page grant's (withdrawn, revoked, deleted)
            // black (12.7).
            if (client.WindowToken is { } token)
            {
                if (ScreenWindows.Find(_control.Windows, token) is not { } found || found.Via != (client.WindowVia ?? ""))
                {
                    await client.SendTextAsync(string.IsNullOrEmpty(client.WindowVia) ? WindowGone : WindowGoneBlack, ct);
                    return;
                }
                var w = found.Window;
                var assignment = WindowJson(w);
                if (lastWindow is null || !assignment.AsSpan().SequenceEqual(lastWindow))
                {
                    await client.SendTextAsync(assignment, ct);
                    lastWindow = assignment;
                }
            }
            // The link being alive isn't enough: if the show thread has stopped turning over, the last frame is a
            // frozen time. Say so (screens go stale) and send no frame until it recovers — a reconnecting screen
            // must not be handed the frozen frame as if it were current either.
            if (_control.Snapshots.ProducerStuck)
            {
                await client.SendTextAsync(StuckBeat, ct);
                await client.WaitForSignalAsync(HeartbeatEvery, ct);
                lastSent = null;              // resend the current frame as soon as it recovers
                continue;
            }
            var json = CurrentFrameJson(client.Access);
            if (!ReferenceEquals(json, lastSent))
            {
                await client.SendTextAsync(json, ct);
                lastSent = json;
            }
            // Wake on the next change, or after HeartbeatEvery with a tiny heartbeat, so a screen can always tell a
            // quiet show (paused, idle) from a dead link — and say so instead of showing a stale time.
            if (!await client.WaitForSignalAsync(HeartbeatEvery, ct))
                await client.SendTextAsync(Heartbeat, ct);
        }
    }

    /// <summary>What a window whose address stopped working (revoked, deleted) hears before it is let go (Show Bus 12.3).</summary>
    public const string WindowGoneJson = "{\"type\":\"window\",\"revoked\":true}";
    private static readonly byte[] WindowGone = Encoding.UTF8.GetBytes(WindowGoneJson);

    /// <summary>
    /// What a window link on a page grant's address hears when that address stops working — withdrawn, deleted or revoked — before it is
    /// let go (Show Bus 12.7): 12.3's end with black, so the app's screen draws plain black and stops, never words.
    /// </summary>
    public const string WindowGoneBlackJson = "{\"type\":\"window\",\"revoked\":true,\"black\":true}";
    private static readonly byte[] WindowGoneBlack = Encoding.UTF8.GetBytes(WindowGoneBlackJson);

    /// <summary>
    /// The node a socket may be given that node's window grants on (12.5's first point): a node link a key bound to that node proves —
    /// verified by LINK (13.6), or by its node key on /ws (§7.1) — and never one the PIN or a control key proves. Null for any other.
    /// </summary>
    private static string? GrantsNode(WsLink link)
        => link is { Class: WsClass.Node, Verified: NodeProof.Key, Node: { } node, KeyId: not null } ? node : null;

    /// <summary>
    /// A grants node link's turn (12.5–12.6): its grants whole when they differ from what it was last sent (the first time always, an
    /// empty list when its node holds none), its revision rising on this link; then, for each draw grant this link may be given — it
    /// offers window-draw, its key was never seen in clear — the window's assignment when it changed, and an end for each window it was
    /// assigned and may no longer draw (withdrawn, or deleted when the window is gone). Latest wins: only what is current is sent.
    /// </summary>
    private async Task SendGrantsAsync(WsClient client, string node, string keyId, Dictionary<string, (ScreenWindow Window, byte[] Json)> assigned, CancellationToken ct)
    {
        var windows = _control.Windows;
        var rows = WindowGrants.For(windows, node, client.LinkWord, client.Can, keyInClear: !_control.Sightings.MayGrant(keyId));
        var sent = client.Grants;
        if (sent is null || !sent.Rows.SequenceEqual(rows))
        {
            var rev = (sent?.Rev ?? 0) + 1;
            await client.SendTextAsync(WindowGrants.GrantsJson(_control.OwnNodeId ?? "", rev, client.LinkWord, rows), ct);
            client.Grants = new WsClient.GrantsSent(rev, client.LinkWord, rows);
        }
        var draw = rows.Where(r => r.How == ScreenWindows.HowDraw && r.Cannot.Length == 0).Select(r => r.Window).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var ended in assigned.Keys.Where(id => !draw.Contains(id)).ToList())
        {
            await client.SendTextAsync(WindowGrants.EndJson(ended, windows.Any(w => w.Id == ended) ? WindowGrants.EndWithdrawn : WindowGrants.EndDeleted), ct);
            assigned.Remove(ended);
            changed = true;
        }
        foreach (var w in windows)
        {
            if (!draw.Contains(w.Id)) continue;
            var had = assigned.TryGetValue(w.Id, out var was);
            if (had && ReferenceEquals(was.Window, w)) continue;
            var json = WindowGrants.AssignJson(w);
            if (!had || !json.AsSpan().SequenceEqual(was.Json)) await client.SendTextAsync(json, ct);
            assigned[w.Id] = (w, json);
            changed |= !had;
        }
        if (changed) client.Drawing = assigned.Keys.ToArray();
    }

    /// <summary>What a window is told (Show Bus 12.3): everything it acts on, never its own address.</summary>
    public static byte[] WindowJson(ScreenWindow w) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        type = "window", id = w.Id, name = w.Name, content = w.Content, hold = w.Hold, holdText = w.HoldText, holdImage = w.HoldImage,
        onLoss = w.OnLoss, fadeMs = w.FadeMs, rev = w.Revision,
    });

    /// <summary>Browsers flag a screen stale after ~3 missed heartbeats (see common.js).</summary>
    public static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(2);
    private static readonly byte[] Heartbeat = "{\"type\":\"beat\"}"u8.ToArray();
    private static readonly byte[] StuckBeat = "{\"type\":\"beat\",\"live\":false}"u8.ToArray();

    private async Task ReceiveLoopAsync(WsClient client, CancellationToken ct)
    {
        var buffer = new byte[MaxWsMessageBytes];
        while (!ct.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
        {
            var count = 0;
            WebSocketReceiveResult r;
            var oversize = false;
            do
            {
                if (count >= buffer.Length)
                {
                    // A verified LINK socket skips a message past the bound to its end, counts it and stays up (13.14); any other
                    // socket is closed.
                    if (client.Link.Lid is null)
                    {
                        await client.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", ct);
                        return;
                    }
                    oversize = true;
                    count = 0;
                }
                r = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                count += r.Count;
            } while (!r.EndOfMessage);

            if (oversize)
            {
                client.CountOversize();
                continue;
            }
            if (r.MessageType != WebSocketMessageType.Text) continue;
            var reply = HandleWsMessage(client, buffer.AsSpan(0, count));
            if (reply is not null) await client.SendTextAsync(reply, ct);
        }
    }

    /// <summary>{"cmd":"nudge","args":["60"]} → command. Returns an error reply or null.</summary>
    private byte[]? HandleWsMessage(WsClient client, ReadOnlySpan<byte> json)
    {
        string? verb = null;
        string? requestId = null;
        long? pingT = null;
        var args = new List<string>();
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error("expected an object");
            if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
            {
                switch (type.GetString())
                {
                    case "shown":
                        return client.WindowId is null ? Error("only a window says what it shows") : WindowShown(client, root);
                    case "grants-seen":
                        return GrantsNode(client.Link) is null ? Error(WindowGrants.SeenOnlyOnLink) : GrantsSeen(client, root);
                    case "window-shown":
                        return GrantsNode(client.Link) is { } node ? DrawShown(client, node, root) : Error(WindowGrants.DrawOnly);
                }
            }
            if (root.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String) verb = c.GetString();
            if (root.TryGetProperty("id", out var rid) && rid.ValueKind == JsonValueKind.String) requestId = CommandReceipt.CleanId(rid.GetString());
            if (root.TryGetProperty("t", out var pt) && pt.ValueKind == JsonValueKind.Number && pt.TryGetInt64(out var t1)) pingT = t1;
            if (root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var e in a.EnumerateArray())
                    args.Add(e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText());
        }
        catch (JsonException) { return Error("bad JSON"); }

        if (string.IsNullOrEmpty(verb)) return Error("missing cmd");
        // 13.12.6: a LINK-verified link asks what became of its own ids; nothing runs.
        if (verb == "receipts") return ReceiptsAsked(client, requestId, args);
        if (verb == "ping")
        {
            if (pingT is not { } t) return "{\"type\":\"pong\"}"u8.ToArray();
            // Show Bus §5: the client works out the clock offset from its t1/t4 and our t2/t3.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Encoding.UTF8.GetBytes(FormattableString.Invariant($"{{\"type\":\"pong\",\"t\":{t},\"t2\":{now},\"t3\":{now}}}"));
        }
        // The socket's link is read once, as it stands now. A link whose LINK proved a control key operates (§8.1: `operate` is a control
        // key or the PIN), as that key's request over HTTP would; any other node link runs public verbs alone.
        var link = client.Link;
        var controls = link.CanControl || Operates(link);
        if (!controls && !ShowCommandParser.IsPublicVerb(verb)) return Error("read-only connection (PIN required)");
        if (IsQuestion(verb) && !AllowQuestion(client.Remote)) return Error("please wait a little before asking again");
        if (!ShowCommandParser.TryParse(verb, args, out var cmd, out var error))
        {
            Reject(error);
            return Error(error);
        }
        if (cmd.Kind == ShowCommandKind.Ack && !controls && CheckCrewAck(ref cmd, client.CrewKey, client.Remote, client) is { } refused)
            return Error(refused);
        if (cmd.Kind == ShowCommandKind.DeptNote && !controls && CheckDeptKey(DeptOf(cmd), client.CrewKey, client.Remote, client) is { } noteRefused)
            return Error(noteRefused);
        // Another node's air: a control page let in with no PIN set and no key proved nothing, so only this PC or a listed address,
        // never another site's page.
        var proved = (link.CanControl && link.Scope is "pin" or "control") || Operates(link);
        // 13.12.6: on a LINK-verified link an id the record holds is answered from it and never run again; one it does not hold is
        // held as running until its receipt settles it.
        var connector = ConnectorOf(link);
        if (requestId is not null && connector is not null && Receipts.Begin(connector, requestId, LinkNowMs) is { } held)
            return RecordedReceipt(requestId, held);
        Action<CommandReceipt>? settled = requestId is not null && connector is not null
            ? r => Receipts.Keep(connector, requestId, r.RecordJson(), LinkNowMs)
            : null;
        if (PlayoutGate.Check(cmd, CommandDoor.WebSocket, client.Sender with { Credentialed = proved }, _control.PlayoutFrom) is { } gated)
        {
            Reject(gated);
            if (requestId is null) return Error(gated);
            _ = SendReceiptAsync(client, requestId, Task.FromResult(CommandReceipt.Refused(requestId, gated)), settled);
            return null;
        }
        if (PlayoutGate.Person(cmd.Kind))
        {
            // As on HTTP: the door names the sender, and a person's line is always answered with what came of it.
            cmd = cmd with { By = DeskOrigin.Wire(WireWho(null, link.KeyId, client.Remote, proved), proved) };
            requestId ??= PersonReceiptId(cmd.Kind);
        }
        if (requestId is not null)
        {
            // The receipt follows when the show thread has run it; frames keep flowing meanwhile.
            _ = SendReceiptAsync(client, requestId, _control.ExecuteWithReceiptAsync(cmd, requestId, ReceiptWait), settled);
            return null;
        }
        _control.Execute(cmd);
        return null;

        static byte[] Error(string e) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "error", error = e }));
    }

    /// <summary>
    /// The features a LINK verdict names (13.6.6): <c>final</c> — an accepted command's one follow-up — and <c>receipts</c> — the record
    /// of what each verified connector's ids came to (13.12.6). Never <c>stuck</c>: its <c>stuckMs</c> is the shipped stall mark, not yet
    /// set from a measured stall (13.10.5, §8's row); a <c>/ws</c> link hears its show stuck by the beat all the same. Never <c>expire</c>.
    /// </summary>
    public static IReadOnlyList<string> LinkCan { get; } = ["final", "receipts"];

    /// <summary>
    /// The words a RECEIPTS ask hears on a link that keeps no receipts: one LINK did not verify (a bearer node link, a screen, a refused
    /// or revoked link).
    /// </summary>
    public const string NoReceipts = "this link keeps no receipts: only a link whose LINK verified (its verdict lists receipts) asks for them";

    /// <summary>Whether a link's LINK proved a key in force that may operate (a control key): it runs commands as that key's request would.</summary>
    private static bool Operates(WsLink link) => link is { Lid: not null, KeyId: not null } && ApiKeys.Allows(link.KeyScope, ApiKeyScope.Control);

    /// <summary>
    /// The connector a link's record is kept for (13.12.6, per verified connector id): its node's kind and id (<c>t:id</c>) while a node
    /// key that speaks for that id proved it; the same with the key that proved it (<c>t:id#kid</c>) when the operator's control key
    /// proved a link whose id is only claimed, so two holders of keys that name one unheld id never read or pre-empt each other's ids.
    /// Null on a socket LINK did not verify (it holds no lid), which keeps no record and is never answered from one.
    /// </summary>
    private static string? ConnectorOf(WsLink link) => link switch
    {
        { Lid: null } => null,
        { Kind: { } t, Node: { } id, Verified: NodeProof.Key } => t + ":" + id,
        { Kind: { } t, Node: { } id, KeyId: { } kid } => t + ":" + id + "#" + kid,
        _ => null,
    };

    /// <summary>A receipt answered from the record, on a socket: §3's receipt with the id, its kept fields and <c>"repeat":true</c>.</summary>
    private static byte[] RecordedReceipt(string id, string held)
        => Encoding.UTF8.GetBytes("{\"type\":\"receipt\",\"id\":" + JsonSerializer.Serialize(id) + "," + held[1..]);

    /// <summary>
    /// <c>{"cmd":"receipts","args":[ids…],"id":"rq"}</c> (13.12.6): on a LINK-verified link, a receipt to the ask with this node's inst and,
    /// for each of its own ids, its kept receipt, accepted while it still runs, or "unknown"; refused on any other socket, or for an ask
    /// that names no id or too many. Answered at once, off the show thread.
    /// </summary>
    private byte[] ReceiptsAsked(WsClient client, string? requestId, IReadOnlyList<string> args)
    {
        if (requestId is null)
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "error", error = "receipts asks with a request id: {\"cmd\":\"receipts\",\"args\":[…],\"id\":\"rq1\"}" }));
        string? refused = null;
        IReadOnlyList<string>? ids = null;
        if (ConnectorOf(client.Link) is not { } connector) refused = NoReceipts;
        else if ((ids = ReceiptRecord.AskOf(args)) is null) refused = ReceiptRecord.AskWords;
        else
        {
            var answer = Receipts.Answer(connector, ids, Inst, LinkNowMs);
            return Encoding.UTF8.GetBytes("{\"type\":\"receipt\",\"id\":" + JsonSerializer.Serialize(requestId) + ",\"ok\":true," + answer[1..]);
        }
        return CommandReceipt.Refused(requestId, refused).ToJson();
    }

    /// <summary>
    /// A window says what it shows (after each swap, hold or failure): kept for the operator, the context and /api/windows, as the window's
    /// one row and as the row of its viewer by the pass its link came in on.
    /// </summary>
    private byte[]? WindowShown(WsClient client, JsonElement root)
    {
        var via = client.WindowVia ?? "";
        var seen = new WindowObserved(client.WindowId!, 1, Int(root, "rev"), Text(root, "content", 400),
            root.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.True, Math.Clamp(Int(root, "ms"), 0, 600_000), DateTime.UtcNow,
            Text(root, "error", 200), client.Device, Screens.AddressOf(client.Remote), via);
        if (_windowSeen.Count > 4 * ScreenWindows.Max) _windowSeen.Clear();
        _windowSeen[client.WindowId!] = seen;
        KeepViewer(new ViewerObserved(seen.WindowId, via, via.Length == 0 ? "" : ScreenWindows.HowPage, 1, seen.Revision, seen.Content, seen.Hold, seen.SwapMs,
            seen.ShownUtc, seen.Error, seen.Device, seen.Address));
        return null;
    }

    /// <summary>
    /// A show app says which grants it saw (12.6 <c>grants-seen</c>): the revision, and why it shows no page at all ("" when it can). Kept on
    /// its link, so the operator reads a grant as sent until its app acknowledges it, and a held grant in the app's own reason.
    /// </summary>
    private static byte[]? GrantsSeen(WsClient client, JsonElement root)
    {
        client.SeenCannot = Text(root, "cannot", WindowGrants.MaxSeenCannot);
        client.SeenRev = Int(root, "rev");
        return null;
    }

    /// <summary>
    /// A show app says what it draws of a window (12.6 <c>window-shown</c>): taken only for a draw grant of that window held by this link's
    /// node and assigned on this very link (it offers window-draw, its key was never seen in clear), kept as that viewer's row; anything
    /// else is refused in 12.6's words, with no row kept.
    /// </summary>
    private byte[]? DrawShown(WsClient client, string node, JsonElement root)
    {
        var id = Text(root, "window", 40);
        if (!client.Drawing.Contains(id)
            || _control.Windows.FirstOrDefault(w => w.Id == id) is not { } window
            || !(window.Grants ?? []).Any(g => g is { How: ScreenWindows.HowDraw } && g.Node == node))
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "error", error = WindowGrants.DrawOnly }));
        KeepViewer(new ViewerObserved(window.Id, node, ScreenWindows.HowDraw, 1, Int(root, "rev"), Text(root, "content", 400),
            root.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.True, Math.Clamp(Int(root, "ms"), 0, 600_000), DateTime.UtcNow,
            Text(root, "error", 200), client.Device, Screens.AddressOf(client.Remote)));
        return null;
    }

    /// <summary>A string field of a socket's message, its control characters taken out and cut at <paramref name="max"/>; "" when it is no string.</summary>
    private static string Text(JsonElement r, string name, int max)
    {
        var v = r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        v = new string(v.Where(ch => !char.IsControl(ch)).ToArray());
        return v.Length > max ? v[..max] : v;
    }

    /// <summary>A whole-number field of a socket's message; 0 when it is none.</summary>
    private static int Int(JsonElement r, string name) => r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : 0;

    /// <summary>
    /// The receipt, and after an <c>accepted</c> its one follow-up (Show Bus §3, 13.12.4: the same id with <c>"final":true</c>, within
    /// <c>receipt.final.ms</c> — what came of it once the show thread ran it or the node it waits on answered, or unconfirmed when nothing
    /// did in time), on this socket. HTTP answers once, so only a socket hears the follow-up. The receipt that settles the command — the
    /// follow-up where there is one — is handed to <paramref name="settled"/> (the link's record) whether or not the socket still takes it,
    /// for a connector that relinks asks the record; so is what a command reported unrun at its follow-up came to once it ran, and a
    /// receipt that never comes settles it unconfirmed (<see cref="ReceiptRecord.Deliver"/>).
    /// </summary>
    private static Task SendReceiptAsync(WsClient client, string id, Task<CommandReceipt> receipt, Action<CommandReceipt>? settled = null)
    {
        return ReceiptRecord.Deliver(id, receipt, Send, settled);

        async Task Send(CommandReceipt r)
        {
            try { await client.SendTextAsync(r.ToJson(), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException or IOException) { }
        }
    }

    private sealed class WsClient
    {
        private readonly SemaphoreSlim _signal = new(0, 1);
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private volatile WsLink _link;
        private Posted? _posted;

        public WsClient(WebSocket socket, bool detail, IPAddress remote, WsLink link)
        {
            Socket = socket;
            Detail = detail;
            Remote = remote;
            _link = link;
        }

        public WebSocket Socket { get; }
        /// <summary>Producer clients get the detail frame (projected running order, advice, Q&amp;A, rooms).</summary>
        public bool Detail { get; }

        /// <summary>
        /// What this socket is (screen, claim or node link), what proved it and what it may do: set at admission and swapped
        /// whole by a credential change (under <see cref="Gate"/>); every reader takes one copy.
        /// </summary>
        public WsLink Link { get => _link; set => _link = value; }

        /// <summary>Held while a credential change works out this socket's new link, so two changes can't interleave.</summary>
        public object Gate { get; } = new();

        /// <summary>A control page let in by the PIN or a control key (or on a Stagewatch with no PIN), until that changes.</summary>
        public bool CanControl => _link.CanControl;

        /// <summary>
        /// Operator frames for a control page and for a socket a key in force (or the PIN, on a node link) admitted, read-only
        /// unless it controls; public pages get the public view.
        /// </summary>
        public FrameAccess Access => !Detail ? FrameAccess.Public : _link.CanControl || _link.OperatorRead ? FrameAccess.Operator : FrameAccess.PublicDetail;

        /// <summary>The crew link key this page was opened with (?key=), if any.</summary>
        public string? CrewKey { get; init; }

        /// <summary>
        /// Who opened this socket, as the playout gate judges it from its upgrade (its address, this PC by address and Host, another
        /// site's page); what it proved is read from <see cref="Link"/> at each line, since a credential change swaps that.
        /// </summary>
        public CommandSender Sender { get; init; }

        private int _crewKeyCounted;

        /// <summary>
        /// True the first time only: a socket gives its crew link key once, at its upgrade, so a key that is no crew link counts
        /// as one wrong view pass however many cues its page tries to confirm.
        /// </summary>
        public bool FirstCountOfCrewKey() => Interlocked.Exchange(ref _crewKeyCounted, 1) == 0;

        /// <summary>The viewing code this page was opened with (?view=), if any: whether it still views once its key is gone.</summary>
        public string? ViewGiven { get; init; }

        /// <summary>The id Screens and the screen pushes use: a browser's own id (kept in its storage), a claim's, or a node link's.</summary>
        public string ScreenId => _link.ScreenId;

        /// <summary>One message a credential change left for the send loop (latest wins), and whether the socket closes after it.</summary>
        public sealed record Posted(byte[] Json, bool Close);

        /// <summary>The grants a node link was sent: their revision on this link, what carried it then, and the rows.</summary>
        public sealed record GrantsSent(int Rev, string Link, IReadOnlyList<GrantRow> Rows);

        private int _oversize;

        /// <summary>Messages past <see cref="MaxWsMessageBytes"/> this LINK socket sent and was not closed for (13.14).</summary>
        public int Oversize => Volatile.Read(ref _oversize);

        public void CountOversize() => Interlocked.Increment(ref _oversize);

        public void Post(byte[] json, bool close) => Volatile.Write(ref _posted, new Posted(json, close));

        public Posted? TakePosted() => Interlocked.Exchange(ref _posted, null);

        /// <summary>The controlled window this link belongs to (null for an ordinary screen), and the address it came in on.</summary>
        public string? WindowId { get; init; }
        public string? WindowToken { get; init; }

        /// <summary>
        /// Which of the window's passes that address is (<see cref="WindowHit.Via"/>): "" its own, else the node id of the page grant
        /// whose address it is. Null for a socket that is no window's link.
        /// </summary>
        public string? WindowVia { get; init; }

        /// <summary>What carries this socket (12.6's <c>link</c>): <c>tls</c>, <c>local</c> (this machine's loopback, 13.1) or <c>plain</c>.</summary>
        public string LinkWord { get; init; } = WindowGrants.LinkPlain;

        /// <summary>The abilities this node link offers for windows (its <c>can=</c>, read by <see cref="WindowGrants.Offered"/>).</summary>
        public IReadOnlyList<string> Can { get; init; } = [];

        /// <summary>The grants this link was last sent (12.6) and their revision on it; null until it was sent any.</summary>
        public GrantsSent? Grants { get => Volatile.Read(ref _grants); set => Volatile.Write(ref _grants, value); }
        private GrantsSent? _grants;

        /// <summary>The revision of the grants this link's app says it saw (<c>grants-seen</c>), 0 until it says; and why it shows no page at all.</summary>
        public int SeenRev { get => Volatile.Read(ref _seenRev); set => Volatile.Write(ref _seenRev, value); }
        private int _seenRev;
        public string SeenCannot { get => Volatile.Read(ref _seenCannot); set => Volatile.Write(ref _seenCannot, value); }
        private string _seenCannot = "";

        /// <summary>The windows this link was assigned to draw (12.6's <c>window-assign</c>) and not ended since; swapped whole by its send loop.</summary>
        public IReadOnlyCollection<string> Drawing { get => Volatile.Read(ref _drawing); set => Volatile.Write(ref _drawing, value); }
        private IReadOnlyCollection<string> _drawing = [];
        public string Page { get; init; } = "/";
        public string Device { get; init; } = "";
        public DateTime ConnectedUtc { get; } = DateTime.UtcNow;
        public IPAddress Remote { get; }

        /// <summary>One-slot mailbox: many signals collapse into one wake-up.</summary>
        public void Signal()
        {
            try { if (_signal.CurrentCount == 0) _signal.Release(); }
            catch (SemaphoreFullException) { }
        }

        /// <summary>True when signalled, false when the timeout passed first.</summary>
        public Task<bool> WaitForSignalAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

        public async Task SendTextAsync(byte[] payload, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);
            await _sendLock.WaitAsync(timeout.Token);
            try { await Socket.SendAsync(payload, WebSocketMessageType.Text, true, timeout.Token); }
            finally { _sendLock.Release(); }
        }

        public void Abort()
        {
            try { Socket.Abort(); } catch { }
        }
    }

    // ------------------------------------------------------------------ responses

    private static async Task WriteAsset(Stream stream, string name, CancellationToken ct)
    {
        var bytes = Assets.Get(name);
        if (bytes is null) { await WriteJsonError(stream, 404, "not found", ct); return; }
        await WriteAsync(stream, 200, Assets.ContentType(name), bytes, ct);
    }

    private static async Task WriteRedirectAsync(Stream stream, string location, CancellationToken ct)
    {
        var head = "HTTP/1.1 302 Found\r\nLocation: " + location + "\r\nContent-Length: 0\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
    }

    private static Task WriteJsonError(Stream stream, int status, string error, CancellationToken ct)
        => WriteAsync(stream, status, "application/json",
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error })), ct, cors: true);

    private static async Task WriteAsync(Stream stream, int status, string contentType, byte[] body, CancellationToken ct, bool cors = false, string? extra = null, string? csp = null)
    {
        var reason = status switch
        {
            200 => "OK", 202 => "Accepted", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized",
            403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed", 413 => "Payload Too Large", 429 => "Too Many Requests", _ => "Error"
        };
        var sb = new StringBuilder(256)
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n");
        if (contentType.StartsWith("text/html", StringComparison.Ordinal))
            sb.Append("Content-Security-Policy: ").Append(csp ?? "default-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; img-src 'self' data:").Append("\r\n");
        if (cors)
            sb.Append("Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: X-Stagewatch-Pin, Content-Type, Authorization, Mcp-Session-Id, Mcp-Protocol-Version\r\n")
              .Append("Access-Control-Expose-Headers: X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset, Retry-After\r\n");
        if (!string.IsNullOrEmpty(extra)) sb.Append(extra);
        sb.Append("\r\n");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SendTimeout);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), timeout.Token);
        if (body.Length > 0) await stream.WriteAsync(body, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}

/// <summary>Just enough HTTP/1.1 request parsing for our routes. Bounded and strict.</summary>
internal sealed class HttpRequest
{
    public string Method { get; private init; } = "";
    public string Path { get; private init; } = "/";
    public IReadOnlyDictionary<string, string> Query { get; private init; } = new Dictionary<string, string>();
    /// <summary>The query as it came, after its <c>?</c> and undecoded ("" with none): what a LINK socket's link message is checked against.</summary>
    public string RawQuery { get; private init; } = "";

    /// <summary>
    /// The key a live link's session credential speaks for on this request (13.7.1), set once by the session door on a side call's
    /// route; null on any other request. Its routes read it as that key's credential, and never the request's other headers.
    /// </summary>
    public string? SessionKeyId { get; private set; }

    /// <summary>The session door's word that this request speaks for <paramref name="keyId"/>, once.</summary>
    internal void SpeakAs(string keyId) => SessionKeyId ??= keyId;
    public IReadOnlyDictionary<string, string> Headers { get; private init; } = new Dictionary<string, string>();
    /// <summary>Request body (POST), capped at 64 KB. Empty for GET, and for a request whose body is <see cref="Oversize"/>.</summary>
    public byte[] Body { get; private init; } = Array.Empty<byte>();
    public const int MaxBody = 64 * 1024;

    /// <summary>
    /// The length a request declares for a body over <see cref="MaxBody"/> on a path that is not deferred; 0 for any other request.
    /// Such a body is never read into memory, parsed or kept: the server drains it and answers 413 before any route, credential or
    /// count sees the request (<see cref="DrainOversizeAsync"/>).
    /// </summary>
    public long Oversize { get; private init; }

    /// <summary>The most of an oversize body drained so its sender reads the 413; one declared longer is answered at once and closed.</summary>
    public const int MaxDrain = 1024 * 1024;

    /// <summary>How long an oversize body is drained for: the 413 follows when it ends, drained or not.</summary>
    public static readonly TimeSpan DrainFor = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The 413's words: the most a body may be, and what this one declares (Show Bus §6: "413 and its size"). A length past a long's
    /// range is held as <see cref="long.MaxValue"/>, so that value's words say "at least", true of both. ASCII only, so a reader that
    /// matches the raw answer shows them as sent, not as a JSON escape.
    /// </summary>
    public static string OversizeWords(long declared)
        => "a request body is at most " + MaxBody.ToString(CultureInfo.InvariantCulture) + " bytes; this one declares "
           + (declared == long.MaxValue ? "at least " : "") + declared.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether the request carries a Content-Length this server cannot read: one that is not a plain decimal number of bytes, or
    /// two such headers. It is answered 400 with <see cref="UnreadableLength"/> before anything else sees it (RFC 9112 §6.3), never
    /// read as a request with no body.
    /// </summary>
    public bool LengthUnreadable { get; private init; }

    /// <summary>The 400's words for <see cref="LengthUnreadable"/>.</summary>
    public const string UnreadableLength = "a Content-Length this server cannot read: a request's length is one decimal number of bytes";

    /// <summary>Uploads (holding images) are read after routing, with their own size cap and timeout.</summary>
    public const string UploadPath = "/api/holding/upload";
    public const int MaxUpload = 25 * 1024 * 1024;
    /// <summary>A presenter PC's deck script (all speaker notes): read after routing, up to <see cref="SlideFeed.MaxScript"/>.</summary>
    public const string ScriptPath = "/api/slides/script";
    public int DeferredLength { get; private init; }
    private byte[] _prefix = Array.Empty<byte>();

    /// <summary>
    /// Read and discard a deferred body before refusing it: closing with unread data makes Windows reset
    /// the connection, and the client would see a network error instead of our answer.
    /// </summary>
    public Task SkipDeferredBodyAsync(Stream stream, CancellationToken ct) => SkipAsync(stream, DeferredLength, ct);

    /// <summary>
    /// Read and discard an <see cref="Oversize"/> body, for the same reason, up to <see cref="MaxDrain"/>: one declared longer is not
    /// read at all. Its bytes go through one scratch buffer and are dropped; nothing of them is kept or logged.
    /// </summary>
    public Task DrainOversizeAsync(Stream stream, CancellationToken ct)
        => Oversize is > 0 and <= MaxDrain ? SkipAsync(stream, Oversize, ct) : Task.CompletedTask;

    /// <summary>Read and drop what is left of a body of <paramref name="length"/> bytes past what came with the headers; ends early at the sender's end.</summary>
    private async Task SkipAsync(Stream stream, long length, CancellationToken ct)
    {
        var left = length - Math.Min(_prefix.Length, length);
        var scratch = new byte[64 * 1024];
        while (left > 0)
        {
            var n = await stream.ReadAsync(scratch.AsMemory(0, (int)Math.Min(scratch.Length, left)), ct);
            if (n == 0) return;
            left -= n;
        }
    }

    /// <summary>Read a deferred upload body. Null if the client stops sending.</summary>
    public async Task<byte[]?> ReadDeferredBodyAsync(Stream stream, CancellationToken ct)
    {
        if (DeferredLength <= 0) return Array.Empty<byte>();
        var body = new byte[DeferredLength];
        var have = Math.Min(_prefix.Length, DeferredLength);
        Array.Copy(_prefix, body, have);
        while (have < DeferredLength)
        {
            var n = await stream.ReadAsync(body.AsMemory(have), ct);
            if (n == 0) return null;
            have += n;
        }
        return body;
    }

    public bool IsWebSocketUpgrade =>
        Headers.GetValueOrDefault("upgrade")?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true &&
        Headers.ContainsKey("sec-websocket-key");

    public static async Task<HttpRequest?> ReadAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        var buffer = new byte[maxBytes];
        var count = 0;
        int headerEnd;
        while (true)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(count), ct);
            if (n == 0) return null;
            count += n;
            headerEnd = buffer.AsSpan(0, count).IndexOf("\r\n\r\n"u8);
            if (headerEnd >= 0) break;
            if (count == buffer.Length) return null; // oversized header
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var start = lines[0].Split(' ');
        if (start.Length < 3 || !start[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lengths = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim();
            if (name.Equals("content-length", StringComparison.OrdinalIgnoreCase)) lengths++;
            headers[name] = line[(colon + 1)..].Trim();
        }

        var target = start[1];
        var q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;

        // Body: whatever followed the headers in the first read, plus the rest up to Content-Length. The length is digits alone,
        // read as a long, so one past int's range is an oversize body and one past a long's is held as long.MaxValue; anything else
        // in it, or a second length, is a length this server cannot read: the request carries no body and is answered 400 unrouted.
        var body = Array.Empty<byte>();
        var deferred = 0;
        long oversize = 0;
        var prefix = Array.Empty<byte>();
        long declared = 0;
        var unreadable = false;
        if (headers.TryGetValue("content-length", out var cl))
        {
            unreadable = lengths > 1 || cl.Length == 0 || !cl.All(char.IsAsciiDigit);
            if (!unreadable) declared = long.TryParse(cl, NumberStyles.None, CultureInfo.InvariantCulture, out var l) ? l : long.MaxValue;
        }
        if (declared > 0 && path is UploadPath or ScriptPath && start[0].Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            // A deferred path keeps its own cap and its own refusal: past it, the connection closes unanswered.
            if (declared > (path == ScriptPath ? SlideFeed.MaxScript : MaxUpload)) return null;
            deferred = (int)declared;
            prefix = buffer.AsSpan(headerEnd + 4, count - (headerEnd + 4)).ToArray();
        }
        else if (declared > MaxBody)
        {
            oversize = declared;
            prefix = buffer.AsSpan(headerEnd + 4, count - (headerEnd + 4)).ToArray();
        }
        else if (declared > 0)
        {
            var length = (int)declared;
            body = new byte[length];
            var have = Math.Min(length, count - (headerEnd + 4));
            Array.Copy(buffer, headerEnd + 4, body, 0, have);
            while (have < length)
            {
                var n = await stream.ReadAsync(body.AsMemory(have), ct);
                if (n == 0) return null;
                have += n;
            }
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (q >= 0)
        {
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var k = Unescape(eq >= 0 ? pair[..eq] : pair);
                var v = eq >= 0 ? Unescape(pair[(eq + 1)..]) : "";
                query[k] = v;
            }
        }

        return new HttpRequest
        {
            Method = start[0].ToUpperInvariant(),
            Path = path,
            Query = query,
            RawQuery = q >= 0 ? target[(q + 1)..] : "",
            Headers = headers,
            Body = body,
            DeferredLength = deferred,
            Oversize = oversize,
            LengthUnreadable = unreadable,
            _prefix = prefix
        };
    }

    private static string Unescape(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace('+', ' ')); }
        catch (UriFormatException) { return s; }
    }
}

/// <summary>Browser pages embedded in the assembly. Loaded once, cached forever.</summary>
internal static class Assets
{
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new();

    public static byte[]? Get(string name) => Cache.GetOrAdd(name, n =>
    {
        using var s = typeof(Assets).Assembly.GetManifestResourceStream("wwwroot/" + n);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    });

    public static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/manifest+json",
        _ => "application/octet-stream"
    };
}
