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
    private const int MaxWsMessageBytes = 4096;
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly IShowControl _control;
    private readonly Func<string?> _pin;
    private readonly ConcurrentDictionary<WsClient, byte> _sockets = new();
    private readonly ConcurrentDictionary<IPAddress, (int Failures, long WindowStartMs)> _authFailures = new();
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
    public int Port { get; private set; }
    public int ConnectedClients => _sockets.Count;

    // ------------------------------------------------------------------ screens

    /// <summary>Every connected browser screen, grouped by its id (tabs of one browser count as one screen). Thread-safe.</summary>
    public IReadOnlyList<ScreenInfo> ListScreens()
        => _sockets.Keys.GroupBy(c => c.ScreenId)
            .Select(g =>
            {
                var first = g.OrderBy(c => c.ConnectedUtc).First();
                var name = _control.ScreenName(g.Key) ?? "";
                return new ScreenInfo(g.Key, name, first.Page, Screens.AddressOf(first.Remote), first.Device, first.ConnectedUtc,
                    g.All(c => c.CanControl), g.Count());
            })
            .OrderBy(s => s.Control).ThenBy(s => s.Name.Length == 0).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.ConnectedUtc)
            .ToList();

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
    /// and reloading it reconnects — this is for a stray tablet, not a security wall (that's the viewing code).
    /// </summary>
    public int Disconnect(IEnumerable<string> screenIds)
    {
        var ids = new HashSet<string>(screenIds, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var c in _sockets.Keys)
        {
            if (!ids.Contains(c.ScreenId)) continue;
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

    private int Push(IEnumerable<string> screenIds, byte[] payload)
    {
        var ids = new HashSet<string>(screenIds, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var c in _sockets.Keys)
        {
            if (!ids.Contains(c.ScreenId)) continue;
            n++;
            _ = Task.Run(async () => { try { await c.SendTextAsync(payload, CancellationToken.None); } catch { } });
        }
        return n;
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
        _cts = new CancellationTokenSource();
        _control.Snapshots.Subscribe(OnPublished);
        // Thread pool, not the caller's (UI) context: every request continuation would otherwise
        // resume on the WPF dispatcher — network I/O must never run on the show thread.
        _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
    }

    public async Task StopAsync()
    {
        _control.Snapshots.Unsubscribe(OnPublished);
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
            var stream = tcp.GetStream();
            var remote = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;

            HttpRequest? req;
            using (var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                headerCts.CancelAfter(HeaderTimeout);
                req = await HttpRequest.ReadAsync(stream, MaxHeaderBytes, headerCts.Token);
            }
            if (req is null) return;

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
                or "/holo.bundle.js" or "/holo-page.js":
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
                if (!isOp && CheckDeptKey(dept, req.Query.GetValueOrDefault("key")) is { } denied) { await WriteJsonError(stream, 403, denied, ct); return; }
                var notes = _control.DeptNotesFor(dept) ?? Array.Empty<(int, string, string)>();
                await WriteAsync(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    dept,
                    version = _control.Snapshots.Latest.NotesVersion,
                    items = notes.Select(n => new { i = n.Index, title = n.Title, note = n.Note }),
                }), ct, cors: true);
                return;
            }
            case "/api/screens":
            {
                if (Authorize(req, remote, ApiKeyScope.Read) is { } denied) { await WriteDenied(stream, denied, ct); return; }
                await WriteAsync(stream, 200, "application/json", JsonSerializer.SerializeToUtf8Bytes(new
                {
                    screens = ListScreens().Select(s => new { id = s.Id, name = s.Name, page = s.Page, address = s.Address, device = s.Device,
                        connected = s.ConnectedUtc.ToString("o"), control = s.Control, tabs = s.Tabs }),
                    presets = _control.ScreenPresets.Select(p => new { name = p.Name, path = p.Path }),
                }), ct, cors: true);
                return;
            }
        }

        // Saved screen setups: /p/lobby opens whatever page and options "lobby" was saved as.
        if (req.Path.StartsWith("/p/", StringComparison.Ordinal))
        {
            var target = _control.ScreenPresetPath(Uri.UnescapeDataString(req.Path[3..]));
            if (target is null || !ScreenPreset.ValidPath(target)) { await WriteJsonError(stream, 404, "no such screen preset", ct); return; }
            await WriteRedirectAsync(stream, target, ct);
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

        // Acknowledging a cue and asking a question need no PIN (speaker tablets, audience phones).
        var authorized = false;
        if (!ShowCommandParser.IsPublicVerb(verb))
        {
            if (Authorize(req, remote, ApiKeyScope.Control) is { } denied) { await WriteDenied(stream, denied, ct); return; }
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
        // Only test a PIN that was actually given: a missing one would count as a wrong guess.
        // Asked only for crew routes, so a key's request isn't counted twice.
        bool IsOperator() => authorized || IsAuthorized(req, remote, ApiKeyScope.Control);
        if (cmd.Kind == ShowCommandKind.DeptNote && !IsOperator() && CheckDeptKey(DeptOf(cmd), req.Query.GetValueOrDefault("key")) is { } noteRefused)
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
        _control.Execute(cmd);
        await WriteAsync(stream, 200, "application/json", "{\"ok\":true}"u8.ToArray(), ct, extra: RateHeaders(req));
    }

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
        if (_authFailures.TryGetValue(remote, out var f) && f.Failures >= 5 && now - f.WindowStartMs < 60_000)
            return "too many wrong PINs — wait a minute";

        var ok = supplied is not null &&
                 CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(pin));
        if (ok)
        {
            _authFailures.TryRemove(remote, out _);
            return null;
        }
        _authFailures.AddOrUpdate(remote,
            _ => (1, now),
            (_, old) => now - old.WindowStartMs > 60_000 ? (1, now) : (old.Failures + 1, old.WindowStartMs));
        return supplied is null ? "PIN required" : "wrong PIN";
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
        if (CheckPin(PinFrom(req), remote) is { } auth) { await Refuse(401, auth); return; }
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
        var auth = CheckPin(req.Query.GetValueOrDefault("pin") ?? req.Headers.GetValueOrDefault("x-stagewatch-pin")
                            ?? BearerPin(req.Headers.GetValueOrDefault("authorization")), remote);
        if (auth is not null) { await Refuse(401, auth); return; }
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
    /// A cue confirmed without the PIN must come from the crew page it was sent to: the cue has to exist and,
    /// when crew keys are on, the page's key must belong to the cue's target. The device's address is added to
    /// the "by" name, so the show log says where a confirmation came from. Returns why it was refused, or null.
    /// </summary>
    /// <summary>
    /// The viewing code, when the operator set one: a screen must give it (?view=), or be a crew link, or have the PIN.
    /// Wrong codes count like wrong PINs (five a minute per address), so it can't be guessed. Returns why not, or null.
    /// </summary>
    private string? CheckView(HttpRequest req, IPAddress remote)
    {
        var code = _control.ViewCode;
        if (string.IsNullOrEmpty(code)) return null;
        if (IsAuthorized(req, remote, ApiKeyScope.Read)) return null;
        var (secret, _) = _control.CrewKeyPolicy;
        if (!string.IsNullOrEmpty(secret) && CrewKeys.IsAnyCrewKey(secret, _control.CrewDepartments, req.Query.GetValueOrDefault("key"))) return null;
        var supplied = req.Query.GetValueOrDefault("view");
        var now = Environment.TickCount64;
        if (_authFailures.TryGetValue(remote, out var f) && f.Failures >= 5 && now - f.WindowStartMs < 60_000) return "too many wrong codes — wait a minute";
        if (supplied is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(code))) return null;
        if (supplied is not null)
            _authFailures.AddOrUpdate(remote, _ => (1, now), (_, old) => now - old.WindowStartMs > 60_000 ? (1, now) : (old.Failures + 1, old.WindowStartMs));
        return supplied is null ? "This screen needs the viewing code" : "Wrong viewing code";
    }

    private static string DeptOf(ShowCommand cmd) => (cmd.Text ?? "").Split('\u0001')[0];

    /// <summary>A department's notes may be read or written without the PIN only from that department's crew link
    /// (when crew links are required). Returns why not, or null.</summary>
    private string? CheckDeptKey(string dept, string? key)
    {
        var (secret, required) = _control.CrewKeyPolicy;
        if (!required || string.IsNullOrEmpty(secret)) return null;
        return CrewKeys.Matches(secret, dept, key) ? null
            : $"This page can't edit {dept}'s notes. Open {dept}'s crew link (Showcaller page → Crew links).";
    }

    private string? CheckCrewAck(ref ShowCommand cmd, string? key, IPAddress remote)
    {
        var id = (int)cmd.Number;
        var signal = _control.Snapshots.Latest.Signals.FirstOrDefault(g => g.Id == id);
        if (signal.Id != id || id == 0) return "That cue has already been answered or cleared.";
        var (secret, required) = _control.CrewKeyPolicy;
        if (required && !string.IsNullOrEmpty(secret) && !CrewKeys.Matches(secret, signal.Target, key))
            return $"This screen can't confirm cues for {signal.Target}. Open its crew link (Showcaller page → Crew links).";
        var parts = (cmd.Text ?? "|seen").Split('|');
        var by = parts[0].Length > 0 ? $"{parts[0]} ({remote})" : remote.ToString();
        cmd = cmd with { Text = by.Replace("|", "/") + "|" + (parts.Length > 1 ? parts[1] : "seen") };
        return null;
    }

    private static string? PinFrom(HttpRequest req)
        => req.Query.GetValueOrDefault("pin") ?? req.Headers.GetValueOrDefault("x-stagewatch-pin") ?? BearerPin(req.Headers.GetValueOrDefault("authorization"));

    private static bool HasPin(HttpRequest req) => PinFrom(req) is not null;

    /// <summary>A refusal: HTTP status, reason, and (for 429) the seconds to wait.</summary>
    private sealed record Denied(int Status, string Error, int RetryAfter = 0);

    /// <summary>
    /// The one door for HTTP routes: the PIN opens everything; an API key (swk_…) opens what its scope allows and is
    /// rate limited. Wrong PINs and unknown keys both count toward the lockout. Null = allowed.
    /// </summary>
    private Denied? Authorize(HttpRequest req, IPAddress remote, ApiKeyScope need)
    {
        var supplied = PinFrom(req);
        if (!ApiKeys.LooksLikeKey(supplied))
            return CheckPin(supplied, remote) is { } pinError ? new Denied(401, pinError) : null;

        var now = Environment.TickCount64;
        if (_authFailures.TryGetValue(remote, out var f) && f.Failures >= 5 && now - f.WindowStartMs < 60_000)
            return new Denied(401, "too many wrong PINs or keys — wait a minute");
        if (ApiKeys.Find(_control.ApiKeys, supplied) is not { } key)
        {
            _authFailures.AddOrUpdate(remote, _ => (1, now), (_, old) => now - old.WindowStartMs > 60_000 ? (1, now) : (old.Failures + 1, old.WindowStartMs));
            return new Denied(401, "unknown or revoked API key");
        }
        _authFailures.TryRemove(remote, out _);
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

    private async Task HandleWebSocketAsync(Stream stream, HttpRequest req, IPAddress remote, CancellationToken ct)
    {
        var wantsControl = req.Query.GetValueOrDefault("role") == "control";
        string? authError = wantsControl ? CheckPin(req.Query.GetValueOrDefault("pin"), remote) : null;
        var viewError = wantsControl && authError is null ? null : CheckView(req, remote);

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
        var cid = req.Query.GetValueOrDefault("cid");
        var client = new WsClient(ws, canControl: wantsControl && authError is null, detail, remote)
        {
            CrewKey = req.Query.GetValueOrDefault("key"),
            ScreenId = Screens.ValidId(cid) ? cid! : "anon-" + Guid.NewGuid().ToString("N")[..8],
            Page = Screens.CleanPage(req.Query.GetValueOrDefault("page")),
            Device = Screens.Device(req.Headers.GetValueOrDefault("user-agent")),
        };
        if (viewError is not null)
        {
            // Screens need the viewing code: say so (the page asks for it) and close.
            try
            {
                await client.SendTextAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "auth", ok = false, view = true, error = viewError }), ct);
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await ws.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "viewing code", closeCts.Token);
            }
            catch { }
            return;
        }
        _sockets.TryAdd(client, 0);
        try
        {
            if (wantsControl)
                await client.SendTextAsync(authError is null
                    ? "{\"type\":\"auth\",\"ok\":true}"u8.ToArray()
                    : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "auth", ok = false, error = authError })), ct);

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
        while (!ct.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
        {
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
        var args = new List<string>();
        try
        {
            var reader = new Utf8JsonReader(json);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Error("expected an object");
            if (root.TryGetProperty("cmd", out var c) && c.ValueKind == JsonValueKind.String) verb = c.GetString();
            if (root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
                foreach (var e in a.EnumerateArray())
                    args.Add(e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.GetRawText());
        }
        catch (JsonException) { return Error("bad JSON"); }

        if (string.IsNullOrEmpty(verb)) return Error("missing cmd");
        if (verb == "ping") return "{\"type\":\"pong\"}"u8.ToArray();
        if (!client.CanControl && !ShowCommandParser.IsPublicVerb(verb)) return Error("read-only connection (PIN required)");
        if (IsQuestion(verb) && !AllowQuestion(client.Remote)) return Error("please wait a little before asking again");
        if (!ShowCommandParser.TryParse(verb, args, out var cmd, out var error))
        {
            Reject(error);
            return Error(error);
        }
        if (cmd.Kind == ShowCommandKind.Ack && !client.CanControl && CheckCrewAck(ref cmd, client.CrewKey, client.Remote) is { } refused)
            return Error(refused);
        if (cmd.Kind == ShowCommandKind.DeptNote && !client.CanControl && CheckDeptKey(DeptOf(cmd), client.CrewKey) is { } noteRefused)
            return Error(noteRefused);
        _control.Execute(cmd);
        return null;

        static byte[] Error(string e) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "error", error = e }));
    }

    private sealed class WsClient
    {
        private readonly SemaphoreSlim _signal = new(0, 1);
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public WsClient(WebSocket socket, bool canControl, bool detail, IPAddress remote)
        {
            Socket = socket;
            CanControl = canControl;
            Detail = detail;
            Remote = remote;
        }

        public WebSocket Socket { get; }
        public bool CanControl { get; }
        /// <summary>Producer clients get the detail frame (projected running order, advice, Q&amp;A, rooms).</summary>
        public bool Detail { get; }

        /// <summary>Operator frames only for a PIN-authenticated control connection; public pages get the public view.</summary>
        public FrameAccess Access => !Detail ? FrameAccess.Public : CanControl ? FrameAccess.Operator : FrameAccess.PublicDetail;

        /// <summary>The crew link key this page was opened with (?key=), if any.</summary>
        public string? CrewKey { get; init; }

        /// <summary>The browser's own id (kept in its storage), the page it shows and what it is — for Screens.</summary>
        public string ScreenId { get; init; } = "";
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

    private static async Task WriteAsync(Stream stream, int status, string contentType, byte[] body, CancellationToken ct, bool cors = false, string? extra = null)
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
            sb.Append("Content-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws: wss:; img-src 'self' data:\r\n");
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
