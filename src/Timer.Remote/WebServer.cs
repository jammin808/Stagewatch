using System.Collections.Concurrent;
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
    private const int MaxConnections = 64;
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
    /// Wrong PINs per device. A right PIN or a key in force clears it. Known gap, not this table's invariant: so a key holder can
    /// go on guessing the PIN below the limit from its address (counts per address and key close it).
    /// </summary>
    private readonly WrongGuesses _pinGuesses = new(WrongPinsPerMinute);

    /// <summary>
    /// Wrong view passes per device, a count of their own (Show Bus 12.2): nothing clears it but its minute's end, so a right PIN,
    /// a key or a right view pass never lets a device go on guessing, and wrong view passes never lock out the PIN or a key.
    /// </summary>
    private readonly WrongGuesses _passGuesses = new(WrongViewPassesPerMinute);
    /// <summary>Per API key: requests this minute, when the minute started, and when it was last used.</summary>
    private readonly ConcurrentDictionary<string, (int Count, long WindowStartMs, DateTime LastUtc)> _keyUse = new();
    private readonly SemaphoreSlim _connectionSlots = new(MaxConnections, MaxConnections);
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
        _mcp = new McpEndpoint(control, () => CurrentFrameJson(FrameAccess.Operator));
    }

    public bool IsRunning => _listener is not null;

    /// <summary>
    /// This install's certificate (Show Bus §10). With one, the port also speaks TLS: a connection whose first byte is a
    /// TLS handshake is encrypted, any other is plain HTTP — one port, both kinds of client.
    /// </summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? Certificate { get; set; }

    /// <summary>Refuse plain HTTP and WebSockets from other machines (this machine and the certificate pages still work).</summary>
    public bool RequireHttps { get; set; }

    /// <summary>The certificate's fingerprint ("sha256:…"), or null without one.</summary>
    public string? Fingerprint => Certificate is { } c ? TlsPins.Fingerprint(c.RawData) : null;
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
                    Screens.AddressOf(first.Client.Remote), first.Client.ConnectedUtc, g.Count(), first.Link.Words);
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

    /// <summary>The windows changed (owner thread): each window's link re-reads its own on its next turn, latest wins.
    /// A window whose address was revoked or deleted is told so and let go. Never blocks.</summary>
    public void PushWindows()
    {
        foreach (var c in _sockets.Keys)
            if (c.WindowId is not null) c.Signal();
    }

    /// <summary>Show windows' names full screen for a few seconds. Returns how many window links were told.</summary>
    public int IdentifyWindows(IEnumerable<string> windowIds)
    {
        var n = 0;
        foreach (var id in windowIds.Distinct())
        {
            var name = _control.Windows.FirstOrDefault(w => w.Id == id)?.Name ?? "";
            n += PushToWindows(new[] { id }, IdentifyJson(name, id));
        }
        return n;
    }

    /// <summary>What a window hears when its operator asks it to show its name (Show Bus 12.3).</summary>
    public static byte[] IdentifyJson(string name, string id) => JsonSerializer.SerializeToUtf8Bytes(new { type = "identify", name, id });

    /// <summary>Make windows reload themselves (they come back to what they were showing).</summary>
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

    /// <summary>Every window's link and what it last said it shows. Thread-safe.</summary>
    public IReadOnlyList<WindowObserved> WindowStatus()
    {
        var live = _sockets.Keys.Where(c => c.WindowId is not null).GroupBy(c => c.WindowId!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var ids = new HashSet<string>(_windowSeen.Keys, StringComparer.Ordinal);
        ids.UnionWith(live.Keys);
        var list = new List<WindowObserved>(ids.Count);
        foreach (var id in ids)
        {
            var seen = _windowSeen.GetValueOrDefault(id);
            var clients = live.GetValueOrDefault(id);
            var newest = clients?.OrderByDescending(c => c.ConnectedUtc).First();
            list.Add(new WindowObserved(id, clients?.Count ?? 0, seen?.Revision ?? 0, seen?.Content ?? "", seen?.Hold ?? false, seen?.SwapMs ?? 0, seen?.ShownUtc,
                seen?.Error ?? "", newest?.Device ?? seen?.Device ?? "", newest is null ? seen?.Address ?? "" : Screens.AddressOf(newest.Remote)));
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
                tcp.Dispose(); // over capacity: shed load rather than queue
                continue;
            }
            _ = HandleConnectionAsync(tcp, ct);
        }
    }

    private async Task HandleConnectionAsync(TcpClient tcp, CancellationToken ct)
    {
        try
        {
            tcp.NoDelay = true;
            Stream stream = tcp.GetStream();
            var remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;

            HttpRequest? req;
            var secure = false;
            using (var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                headerCts.CancelAfter(HeaderTimeout);
                // TLS or plain on the same port: a TLS client speaks first with a handshake record (0x16).
                if (Certificate is { } cert)
                {
                    var first = new byte[1];
                    var n = await tcp.Client.ReceiveAsync(first, SocketFlags.Peek, headerCts.Token);
                    if (n == 0) return;
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
                req = await HttpRequest.ReadAsync(stream, MaxHeaderBytes, headerCts.Token);
            }
            if (req is null) return;

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
                await HandleWebSocketAsync(stream, req, remote, ct);
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
            case "/api/peers/context":
            {
                // Another show app tells us what it's doing (its own context, §6). A node key, a control key or the PIN.
                // The server reads the node it names and speaks for it before the store is called: a body it can't read
                // is a 400 and a refusal a 403, both with words, and neither reaches the store or stores anything. Taken,
                // the answer says what the context is held as (verified: key, operator or none).
                if (req.Method != "POST") { await WriteJsonError(stream, 405, "POST the context", ct); return; }
                if (Authorize(req, remote, ApiKeyScope.Node) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                if (req.Body.Length is 0 or > 64 * 1024) { await WriteJsonError(stream, 400, "a context is 1 byte to 64 KB of JSON", ct); return; }
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
                await WriteAsync(stream, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(ShowCommandParser.Help + "\n"), ct, cors: true); return;
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
    /// The shared approval queue (Show Bus §11). POST /api/proposals (a node, MCP or control key, or the PIN);
    /// GET /api/proposals/&lt;id&gt; and ?from=&lt;node&gt; (read); GET ?target=&lt;node&gt;&amp;state=approved collects what was
    /// approved for a node (its node key, or control/PIN); POST /api/proposals/&lt;id&gt;/result reports what it did. A
    /// proposal's from and a collect's target are read and spoken for before the desk is called (a 400 when there is no
    /// node to speak for, a 403 when the credential may not, each with words, nothing queued or claimed), and a node key's
    /// request the desk took binds it (<see cref="BindTaken"/>); a result's node is the proposal's target, which only the
    /// desk knows, so the desk speaks for it.
    /// </summary>
    private async Task HandleProposalsAsync(Stream stream, HttpRequest req, IPAddress remote, string? rest, CancellationToken ct)
    {
        async Task Reply((int Status, string Json) r) => await WriteAsync(stream, r.Status, "application/json", Encoding.UTF8.GetBytes(r.Json), ct, cors: true, extra: RateHeaders(req));
        Denied? Proposer()
        {
            var supplied = PinFrom(req);
            if (ApiKeys.LooksLikeKey(supplied) && ApiKeys.Find(_control.ApiKeys, supplied) is { Scope: ApiKeyScope.Mcp })
                return Authorize(req, remote, ApiKeyScope.Mcp);
            return Authorize(req, remote, ApiKeyScope.Node);
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

    /// <summary>Whether the device is locked out of view passes: <see cref="WrongViewPassesPerMinute"/> wrong ones this minute.</summary>
    private bool PassesLockedOut(IPAddress remote) => _passGuesses.Locked(remote, Environment.TickCount64);

    /// <summary>One wrong view pass (a window address, a viewing code or a crew link) from the device, in their own count.</summary>
    private void CountWrongPass(IPAddress remote) => _passGuesses.Count(remote, Environment.TickCount64);

    /// <summary>
    /// The window an address names, or null: <paramref name="locked"/> when the device is locked out of view passes, so the address
    /// was not looked at and the caller says wait, never gone; else the address names no window and is counted as a wrong view pass.
    /// One reading of the lock decides both, so a device that reaches the limit as it asks is told to wait, never that a right
    /// address is gone. Never says how close a guess was.
    /// </summary>
    private ScreenWindow? WindowFor(string? token, IPAddress remote, out bool locked)
    {
        locked = PassesLockedOut(remote);
        if (locked) return null;
        var w = ScreenWindows.ByToken(_control.Windows, token);
        if (w is null) CountWrongPass(remote);
        return w;
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
    /// GET /api/windows (read): every window, what it was told and what it says it shows. A window's address (its pass)
    /// is listed only for the PIN or a control key.
    /// </summary>
    private async Task HandleWindowListAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
        var supplied = PinFrom(req);
        var showPath = !ApiKeys.LooksLikeKey(supplied) || ApiKeys.Find(_control.ApiKeys, supplied) is { } k && ApiKeys.Allows(k.Scope, ApiKeyScope.Control);
        var seen = WindowStatus().ToDictionary(o => o.WindowId, StringComparer.Ordinal);
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

        // Acknowledging a cue and asking a question need no PIN (speaker tablets, audience phones). A node key (another
        // show app) may put a message on the stage and nothing else.
        var authorized = false;
        if (!ShowCommandParser.IsPublicVerb(verb))
        {
            var probeArgs = segments.Skip(1).ToList();
            if (probeArgs.Count == 0) probeArgs = ArgsFromQuery(req.Query);
            var need = ShowCommandParser.TryParse(verb, probeArgs, out var probe, out _) && ApiKeys.NodeMayRun(probe.Kind)
                ? ApiKeyScope.Node : ApiKeyScope.Control;
            if (Authorize(req, remote, need) is { } denied) { await WriteDenied(stream, denied, ct); return; }
            authorized = true;
        }
        else if (IsQuestion(verb) && !AllowQuestion(remote))
        {
            await WriteJsonError(stream, 429, "please wait a little before asking again", ct);
            return;
        }
        var args = segments.Skip(1).ToList();
        if (args.Count == 0) args = ArgsFromQuery(req.Query);

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
        // Show Bus receipts: with a request id the sender hears what happened, not just that it was sent.
        if (CommandReceipt.CleanId(req.Headers.GetValueOrDefault("x-request-id")) is { } requestId)
        {
            var receipt = await _control.ExecuteWithReceiptAsync(cmd, requestId, ReceiptWait);
            await WriteAsync(stream, receipt.Ok ? 200 : 409, "application/json", receipt.ToJson(), ct, extra: RateHeaders(req));
            return;
        }
        _control.Execute(cmd);
        await WriteAsync(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray(), ct, extra: RateHeaders(req));
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
    private string? CheckPin(string? supplied, IPAddress remote)
    {
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
            return null;
        }
        _pinGuesses.Count(remote, now);
        return "wrong PIN";
    }

    private void Reject(string error)
    {
        try { CommandRejected?.Invoke("Remote: " + error); } catch { }
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
        if (Authorize(req, remote, ApiKeyScope.Mcp) is { } denied) { await WriteDenied(stream, denied, ct); return; }
        if (!_control.McpAllowed) { await WriteJsonError(stream, 403, "MCP is off, or needs Stagewatch Production", ct); return; }
        var reply = _mcp.Handle(req.Body);
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
        if (signal.Id != id || id == 0) return "That cue has already been answered or cleared.";
        var (secret, required) = _control.CrewKeyPolicy;
        if (required && !string.IsNullOrEmpty(secret)
            && CrewLinkFor(secret, signal.Target, key, remote, $"This screen can't confirm cues for {signal.Target}. Open its crew link (Showcaller page → Crew links).", socket) is { } refused)
            return refused;
        var ack = cmd.Get<AckArgs>();
        var by = (ack.By.Length > 0 ? $"{ack.By} ({remote})" : remote.ToString()).Replace("|", "/");
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

    /// <summary>A refusal: HTTP status, reason, and (for 429) the seconds to wait.</summary>
    private sealed record Denied(int Status, string Error, int RetryAfter = 0);

    /// <summary>
    /// The one door for HTTP routes: the PIN opens everything; an API key (swk_…) opens what its scope allows and is
    /// rate limited. Wrong PINs count toward the address's PIN lockout; an unknown or revoked key and a missing PIN never
    /// do, since neither is a guess (a key is 160 random bits), and an app still polling with a revoked key would lock its
    /// whole machine out. A locked address is refused even a valid key. Wrong view passes are counted apart, and nothing
    /// here reads or clears their count (12.2). Known gap, not this door's invariant: a valid key's request clears the
    /// address's PIN count, so a key holder on that address can go on guessing the PIN below the lockout (counts per
    /// address and key close it). Null = allowed.
    /// </summary>
    private Denied? Authorize(HttpRequest req, IPAddress remote, ApiKeyScope need) => AuthorizeSecret(PinFrom(req), remote, need);

    /// <summary><see cref="Authorize"/> for a secret already read from the request (a socket's Authorization header).</summary>
    private Denied? AuthorizeSecret(string? supplied, IPAddress remote, ApiKeyScope need)
    {
        if (!ApiKeys.LooksLikeKey(supplied))
            return CheckPin(supplied, remote) is { } pinError ? new Denied(401, pinError) : null;

        if (_pinGuesses.Locked(remote, Environment.TickCount64))
            return new Denied(401, PinsLocked);
        if (ApiKeys.Find(_control.ApiKeys, supplied) is not { } key)
            return new Denied(401, "unknown or revoked API key");
        _pinGuesses.Clear(remote);
        if (!ApiKeys.Allows(key.Scope, need))
            return new Denied(403, $"this API key is {ApiKeys.Describe(key.Scope)}");
        return Spend(key.Id);
    }

    /// <summary>Whether the request carries a PIN or key good for <paramref name="need"/>. Only tests what was given.</summary>
    private bool IsAuthorized(HttpRequest req, IPAddress remote, ApiKeyScope need)
        => HasPin(req) && (ApiKeys.LooksLikeKey(PinFrom(req)) ? Authorize(req, remote, need) is null : CheckPin(PinFrom(req), remote) is null);

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
        var supplied = PinFrom(req);
        if (!ApiKeys.LooksLikeKey(supplied) || ApiKeys.Find(_control.ApiKeys, supplied) is not { } key) return null;
        return _keyUse.TryGetValue(key.Id, out var u) && u.Count >= ApiKeys.PerMinute && Environment.TickCount64 - u.WindowStartMs < 60_000
            ? new Denied(429, "rate limit: too many requests with this key", 60) : null;
    }

    /// <summary>X-RateLimit-* headers for a request made with an API key ("" otherwise).</summary>
    private string RateHeaders(HttpRequest req)
    {
        var supplied = PinFrom(req);
        if (!ApiKeys.LooksLikeKey(supplied) || ApiKeys.Find(_control.ApiKeys, supplied) is not { } key || !_keyUse.TryGetValue(key.Id, out var u)) return "";
        var reset = Math.Max(0, (int)Math.Ceiling((60_000 - (Environment.TickCount64 - u.WindowStartMs)) / 1000.0));
        return $"X-RateLimit-Limit: {ApiKeys.PerMinute}\r\nX-RateLimit-Remaining: {Math.Max(0, ApiKeys.PerMinute - u.Count)}\r\nX-RateLimit-Reset: {reset}\r\n";
    }

    /// <summary>When an API key was last used this session (null = not yet).</summary>
    public DateTime? KeyLastUsed(string id) => _keyUse.TryGetValue(id, out var u) ? u.LastUtc : null;

    private static Task WriteDenied(Stream stream, Denied d, CancellationToken ct)
        => WriteAsync(stream, d.Status, "application/json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error = d.Error })), ct, cors: true,
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

    private async Task HandleWebSocketAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        // A socket that names a node and asks for what a node link can't be (a window's, a PIN in the URL, control, a bad
        // id or kind, this Stagewatch's own id) is refused before the upgrade and before any credential, window address or
        // viewing code is checked, so it is never counted.
        if (NodeLinks.BadRequest(req.Query, req.Headers.GetValueOrDefault("authorization"), _control.OwnNodeId) is { } bad)
        {
            await WriteJsonError(stream, 400, bad, ct);
            return;
        }
        // Keys, the PIN or bindings that change from here on are checked again once this socket is listed.
        var credentialsSeen = Volatile.Read(ref _credentialsVersion);

        // A window's link: its address names it and is its pass (it views, and sends only what any public page may). A
        // device locked out of view passes is turned away for now, never told its address is gone.
        var winToken = req.Query.GetValueOrDefault("win");
        var lockedWindow = false;
        var window = winToken is null ? null : WindowFor(winToken, remote, out lockedWindow);

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
            admission = NodeLinks.Admit(req.Query, credential, pinAsked, NodeLinks.PinHash(pin),
                () => wantsControl || nodeAsked ? ViewPass(req, remote) : CheckView(req, remote), browserId, _control.Nodes, DateTime.UtcNow);
        }
        var client = new WsClient(ws, window is not null || detail, remote, admission.Link)
        {
            CrewKey = window is null ? req.Query.GetValueOrDefault("key") : null,
            ViewGiven = window is null ? req.Query.GetValueOrDefault("view") : null,
            Page = window is not null ? "/w/" + window.Name : nodeAsked ? "/" : Screens.CleanPage(req.Query.GetValueOrDefault("page")),
            Device = Screens.Device(req.Headers.GetValueOrDefault("user-agent")),
            WindowId = window?.Id,
            WindowToken = window is null ? null : winToken,
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
        }
    }

    private async Task SendLoopAsync(WsClient client, CancellationToken ct)
    {
        byte[]? lastSent = null;
        byte[]? lastWindow = null;
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
            // A window hears what to show before any frame, and again whenever that changes; an address that stopped
            // working (revoked, deleted) is told so and let go.
            if (client.WindowToken is { } token)
            {
                if (ScreenWindows.ByToken(_control.Windows, token) is not { } w)
                {
                    await client.SendTextAsync(WindowGone, ct);
                    return;
                }
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
            do
            {
                if (count >= buffer.Length)
                {
                    await client.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "too big", ct);
                    return;
                }
                r = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer, count, buffer.Length - count), ct);
                if (r.MessageType == WebSocketMessageType.Close) return;
                count += r.Count;
            } while (!r.EndOfMessage);

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
            if (root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "shown")
                return client.WindowId is null ? Error("only a window says what it shows") : WindowShown(client, root);
            if (root.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String) verb = c.GetString();
            if (root.TryGetProperty("id", out var rid) && rid.ValueKind == JsonValueKind.String) requestId = CommandReceipt.CleanId(rid.GetString());
            if (root.TryGetProperty("t", out var pt) && pt.ValueKind == JsonValueKind.Number && pt.TryGetInt64(out var t1)) pingT = t1;
            if (root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var e in a.EnumerateArray())
                    args.Add(e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText());
        }
        catch (JsonException) { return Error("bad JSON"); }

        if (string.IsNullOrEmpty(verb)) return Error("missing cmd");
        if (verb == "ping")
        {
            if (pingT is not { } t) return "{\"type\":\"pong\"}"u8.ToArray();
            // Show Bus §5: the client works out the clock offset from its t1/t4 and our t2/t3.
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return Encoding.UTF8.GetBytes(FormattableString.Invariant($"{{\"type\":\"pong\",\"t\":{t},\"t2\":{now},\"t3\":{now}}}"));
        }
        if (!client.CanControl && !ShowCommandParser.IsPublicVerb(verb)) return Error("read-only connection (PIN required)");
        if (IsQuestion(verb) && !AllowQuestion(client.Remote)) return Error("please wait a little before asking again");
        if (!ShowCommandParser.TryParse(verb, args, out var cmd, out var error))
        {
            Reject(error);
            return Error(error);
        }
        if (cmd.Kind == ShowCommandKind.Ack && !client.CanControl && CheckCrewAck(ref cmd, client.CrewKey, client.Remote, client) is { } refused)
            return Error(refused);
        if (cmd.Kind == ShowCommandKind.DeptNote && !client.CanControl && CheckDeptKey(DeptOf(cmd), client.CrewKey, client.Remote, client) is { } noteRefused)
            return Error(noteRefused);
        if (requestId is not null)
        {
            // The receipt follows when the show thread has run it; frames keep flowing meanwhile.
            _ = SendReceiptAsync(client, _control.ExecuteWithReceiptAsync(cmd, requestId, ReceiptWait));
            return null;
        }
        _control.Execute(cmd);
        return null;

        static byte[] Error(string e) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "error", error = e }));
    }

    /// <summary>A window says what it shows (after each swap, hold or failure): kept for the operator, the context and /api/windows.</summary>
    private byte[]? WindowShown(WsClient client, JsonElement root)
    {
        static string Text(JsonElement r, string name, int max)
        {
            var v = r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
            v = new string(v.Where(ch => !char.IsControl(ch)).ToArray());
            return v.Length > max ? v[..max] : v;
        }
        static int Int(JsonElement r, string name) => r.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var i) ? i : 0;
        var seen = new WindowObserved(client.WindowId!, 1, Int(root, "rev"), Text(root, "content", 400),
            root.TryGetProperty("hold", out var h) && h.ValueKind == JsonValueKind.True, Math.Clamp(Int(root, "ms"), 0, 600_000), DateTime.UtcNow,
            Text(root, "error", 200), client.Device, Screens.AddressOf(client.Remote));
        if (_windowSeen.Count > 4 * ScreenWindows.Max) _windowSeen.Clear();
        _windowSeen[client.WindowId!] = seen;
        return null;
    }

    private static async Task SendReceiptAsync(WsClient client, Task<CommandReceipt> receipt)
    {
        try { await client.SendTextAsync((await receipt.ConfigureAwait(false)).ToJson(), CancellationToken.None).ConfigureAwait(false); }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException or IOException) { }
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

        public void Post(byte[] json, bool close) => Volatile.Write(ref _posted, new Posted(json, close));

        public Posted? TakePosted() => Interlocked.Exchange(ref _posted, null);

        /// <summary>The controlled window this link belongs to (null for an ordinary screen), and the address it came in on.</summary>
        public string? WindowId { get; init; }
        public string? WindowToken { get; init; }
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
    public IReadOnlyDictionary<string, string> Headers { get; private init; } = new Dictionary<string, string>();
    /// <summary>Request body (POST), capped at 64 KB. Empty for GET.</summary>
    public byte[] Body { get; private init; } = Array.Empty<byte>();
    public const int MaxBody = 64 * 1024;

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
    public async Task SkipDeferredBodyAsync(Stream stream, CancellationToken ct)
    {
        var left = DeferredLength - Math.Min(_prefix.Length, DeferredLength);
        var scratch = new byte[64 * 1024];
        while (left > 0)
        {
            var n = await stream.ReadAsync(scratch.AsMemory(0, Math.Min(scratch.Length, left)), ct);
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
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var target = start[1];
        var q = target.IndexOf('?');
        var path = q >= 0 ? target[..q] : target;

        // Body: whatever followed the headers in the first read, plus the rest up to Content-Length.
        var body = Array.Empty<byte>();
        var deferred = 0;
        var prefix = Array.Empty<byte>();
        if (headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, out var length) && length > 0
            && path is UploadPath or ScriptPath && start[0].Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            if (length > (path == ScriptPath ? SlideFeed.MaxScript : MaxUpload)) return null;
            deferred = length;
            prefix = buffer.AsSpan(headerEnd + 4, count - (headerEnd + 4)).ToArray();
        }
        else if (headers.TryGetValue("content-length", out cl) && int.TryParse(cl, out length) && length > 0)
        {
            if (length > MaxBody) return null;
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
            Headers = headers,
            Body = body,
            DeferredLength = deferred,
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
