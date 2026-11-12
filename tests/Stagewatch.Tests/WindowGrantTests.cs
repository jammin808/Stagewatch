using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>
/// Window grants delivered (Show Bus 12.5–12.7): what an app's own key-verified link hears, where a page grant's address may travel and
/// where it never does, the black end of a grant's address, the draw messages, and the viewers and grants the operator reads.
/// </summary>
static class WindowGrantTests
{
    const string Pat = "pat00001", Pm = "pm000001", OwnId = "abc123abc123";

    // ---------------------------------------------------------------- pure: what a link offers, what it hears, where a grant stands
    public static void Run(Action<bool, string> Check)
    {
        Check(WindowGrants.Offered("pages", "patterns").SequenceEqual(["pages"]) && WindowGrants.Offered("window-draw", "playmap").SequenceEqual(["window-draw"])
              && WindowGrants.Offered("pages,window-draw", "patterns").SequenceEqual(["pages"]) && WindowGrants.Offered("pages, pages ,x", "patterns").SequenceEqual(["pages"]),
            "grants: can= offers an ability only for the kinds its cap row names, once");
        Check(WindowGrants.Offered("pages", "playmap").Count == 0 && WindowGrants.Offered("window-draw", "patterns").Count == 0
              && WindowGrants.Offered("PAGES", "patterns").Count == 0 && WindowGrants.Offered("", "patterns").Count == 0 && WindowGrants.Offered("pages", null).Count == 0,
            "grants: can= fails closed — another kind's ability, an unknown word, no kind: nothing offered");
        var past = string.Join(',', Enumerable.Range(0, WindowGrants.MaxCanWords).Select(i => "w" + i.ToString(CultureInfo.InvariantCulture))) + ",pages";
        Check(WindowGrants.Offered(past, "patterns").Count == 0 && WindowGrants.Offered("pages," + past, "patterns").SequenceEqual(["pages"]),
            "grants: can= is read for its first hello.caps words only");

        var page = new WindowGrant { Node = Pat, Kind = "patterns", Name = "Patterns FOH", How = ScreenWindows.HowPage, Token = ScreenWindows.NewToken() };
        var draw = new WindowGrant { Node = Pm, Kind = "playmap", Name = "PlayMap Stage left", How = ScreenWindows.HowDraw };
        var lobby = new ScreenWindow { Id = "wlobby1", Name = "lobby", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 3, Grants = [page] };
        var stage = new ScreenWindow { Id = "wstage1", Name = "stage", Token = ScreenWindows.NewToken(), Content = "/viewer", Revision = 7, Grants = [draw] };
        IReadOnlyList<ScreenWindow> windows = [lobby, stage];
        string[] pages = ["pages"], none = [];
        var given = WindowGrants.For(windows, Pat, WindowGrants.LinkTls, pages, keyInClear: false);
        Check(given is [{ Window: "wlobby1", Name: "lobby", How: "page", Cannot: "" } row] && row.Path == "/w/" + page.Token
              && WindowGrants.For(windows, Pat, WindowGrants.LinkLocal, pages, false).Single().Path == row.Path,
            "grants: a page grant's address is on a TLS or local link that offers pages, for its own node only");
        Check(WindowGrants.For(windows, Pat, WindowGrants.LinkPlain, pages, false).Single() is { Path: "", Cannot: WindowGrants.NotEncrypted }
              && WindowGrants.For(windows, Pat, WindowGrants.LinkTls, pages, true).Single() is { Path: "", Cannot: WindowGrants.KeyInClear }
              && WindowGrants.For(windows, Pat, WindowGrants.LinkLocal, none, false).Single() is { Path: "", Cannot: WindowGrants.NoPages }
              && WindowGrants.For(windows, Pat, WindowGrants.LinkPlain, none, true).Single().Cannot == WindowGrants.NotEncrypted,
            "grants: no address on a plain link, on a key seen in clear or on a link without pages, each said, the carrier first");
        Check(WindowGrants.For(windows, Pm, WindowGrants.LinkPlain, ["window-draw"], false).Single() is { Window: "wstage1", How: "draw", Path: "", Cannot: "" }
              && WindowGrants.For(windows, Pm, WindowGrants.LinkTls, ["window-draw"], true).Single().Cannot == WindowGrants.KeyInClear
              && WindowGrants.For(windows, Pm, WindowGrants.LinkTls, none, false).Single().Cannot == WindowGrants.NoDraw
              && WindowGrants.For(windows, "zz000001", WindowGrants.LinkTls, pages, false).Count == 0,
            "grants: a draw grant has no address, is given on a link that offers window-draw on a key never seen in clear; another node hears none");

        using (var doc = JsonDocument.Parse(WindowGrants.GrantsJson(OwnId, 3, WindowGrants.LinkTls, given)))
        {
            var r = doc.RootElement;
            Check(r.EnumerateObject().Select(p => p.Name).SequenceEqual(["type", "from", "rev", "link", "grants"]) && r.GetProperty("type").GetString() == "grants"
                  && r.GetProperty("from").GetString() == OwnId && r.GetProperty("rev").GetInt32() == 3
                  && r.GetProperty("grants")[0].EnumerateObject().Select(p => p.Name).SequenceEqual(["window", "name", "how", "path", "cannot"]),
                "grants: the grants message is 12.6's shape");
        }
        using (var assign = JsonDocument.Parse(WindowGrants.AssignJson(stage)))
        using (var told = JsonDocument.Parse(WebServer.WindowJson(stage)))
        {
            var a = assign.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            var w = told.RootElement.EnumerateObject().Select(p => p.Name == "id" ? "window" : p.Name).ToList();
            Check(a.Skip(1).SequenceEqual(w.Skip(1)) && assign.RootElement.GetProperty("type").GetString() == "window-assign"
                  && assign.RootElement.GetProperty("window").GetString() == "wstage1" && assign.RootElement.GetProperty("rev").GetInt32() == 7,
                "grants: window-assign is 12.3's window message for the window, named by window");
        }
        Check(Encoding.UTF8.GetString(WindowGrants.EndJson("wstage1", WindowGrants.EndWithdrawn)) == "{\"type\":\"window-end\",\"window\":\"wstage1\",\"why\":\"withdrawn\"}"
              && Encoding.UTF8.GetString(WindowGrants.IdentifyJson("wstage1", "stage")) == "{\"type\":\"window-identify\",\"window\":\"wstage1\",\"name\":\"stage\"}"
              && WebServer.WindowGoneBlackJson == WebServer.WindowGoneJson[..^1] + ",\"black\":true}",
            "grants: window-end, window-identify and the black end are 12.6's and 12.7's");

        // Where a grant stands: a viewer first, then the link, then the acknowledgement.
        ViewerObserved Shown(int rev, string error = "") => new("wlobby1", Pat, "page", 1, rev, "/clock", false, 30, DateTime.UtcNow, error, "", "");
        var rowOk = given[0];
        Check(WindowGrants.State(lobby, page, null, false, 0, null).State == "waiting"
              && WindowGrants.State(lobby, page, rowOk with { Path = "", Cannot = WindowGrants.NotEncrypted }, false, 0, null).State == "plain"
              && WindowGrants.State(lobby, page, rowOk with { Path = "", Cannot = WindowGrants.KeyInClear }, false, 0, null).State == "key-in-clear"
              && WindowGrants.State(lobby, page, rowOk with { Path = "", Cannot = WindowGrants.NoPages }, false, 0, null).State == "cannot"
              && WindowGrants.State(lobby, page, rowOk, false, 0, null).State == "sent"
              && WindowGrants.State(lobby, page, rowOk, true, 0, null).State == "held"
              && WindowGrants.State(lobby, page, rowOk, true, 1, null).State == "open"
              && WindowGrants.State(lobby, page, rowOk, true, 1, Shown(3)) is ("shown", "Patterns FOH shows it")
              && WindowGrants.State(lobby, page, rowOk, true, 1, Shown(2)).State == "stale"
              && WindowGrants.State(lobby, page, rowOk, true, 1, Shown(3, "no page")).State == "stale"
              && WindowGrants.State(lobby, page, null, false, 1, Shown(3)).State == "shown"
              && WindowGrants.States.Count == 9,
            "grants: a grant's state reads its viewer first, then its app's link, then whether it was acknowledged");
        Check(WindowGrants.State(lobby, page, rowOk, true, 0, null, "no web engine here") is ("held", "Patterns FOH has its address and shows no page: no web engine here")
              && WindowGrants.State(stage, draw, rowOk with { Window = "wstage1", How = "draw", Path = "" }, true, 0, null, "no output") is ("held", "PlayMap Stage left has it and draws nothing: no output")
              && WindowGrants.State(lobby, page, rowOk, false, 0, null, "no web engine here").State == "sent"
              && WindowGrants.State(lobby, page, rowOk, true, 1, Shown(3), "no web engine here").State == "shown",
            "grants: a held grant says the app's own reason for showing no page; a viewer or no acknowledgement reads as before");
    }

    // ---------------------------------------------------------------- the server: delivery, the black end, draw, status
    sealed class GrantControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public void Execute(ShowCommand command) { }
        public RemoteConfig GetRemoteConfig() => new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(), Array.Empty<(string, string?, int)>());
        public WindowDirector Director = new(Array.Empty<ScreenWindow>(), _ => { });
        public IReadOnlyList<ScreenWindow> Windows => Director.Windows;
        public volatile IReadOnlyList<ApiKey> Keys = Array.Empty<ApiKey>();
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public string? OwnNodeId => OwnId;
        public readonly KeySightings Marks = new();
        public KeySightings Sightings => Marks;
        public readonly NodeBindings Bindings = new();
        public NodeBindings Nodes => Bindings;
    }

    static readonly ShowApp FohApp = new("patterns", Pat, "Patterns FOH", ["pages"], true);
    static readonly ShowApp StageApp = new("playmap", Pm, "PlayMap Stage left", ["window-draw"], true);

    static WindowChange Do(GrantControl c, string line)
    {
        var parts = line.Split(' ');
        if (!ShowCommandParser.TryParse(parts[0], parts.Skip(1).ToList(), out var cmd, out var error)) return WindowChange.Refused(c.Windows, error);
        return c.Director.Run(cmd.Get<WindowArgs>(), _ => null, Array.Empty<HoldingImage>(), want => ScreenWindows.FindApp([FohApp, StageApp], want));
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    /// <summary>A socket and every message it has heard, read in the background.</summary>
    sealed class Link : IAsyncDisposable
    {
        public readonly ClientWebSocket Ws = new();
        private readonly List<string> _heard = new();
        private Task? _reader;

        public static async Task<Link> Open(int port, string query, string? bearer = null, bool tls = false)
        {
            var l = new Link();
            if (bearer is not null) l.Ws.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
            if (tls) l.Ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            await l.Ws.ConnectAsync(new Uri($"{(tls ? "wss" : "ws")}://127.0.0.1:{port}/ws?{query}"), CancellationToken.None);
            l._reader = l.ReadAsync();
            return l;
        }

        private async Task ReadAsync()
        {
            var buf = new byte[65536];
            try
            {
                while (Ws.State == WebSocketState.Open)
                {
                    var r = await Ws.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                    lock (_heard) _heard.Add(text);
                }
            }
            // The server let it go, or the test aborted it: what it heard stays.
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException) { return; }
        }

        public List<string> Heard { get { lock (_heard) return _heard.ToList(); } }

        /// <summary>The first message heard whose text contains <paramref name="needle"/> after <paramref name="skip"/> of them ("" when none came in time).</summary>
        public async Task<string> Next(string needle, int skip = 0, int timeoutMs = 4000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                var hit = Heard.Skip(skip).FirstOrDefault(m => m.Contains(needle, StringComparison.Ordinal));
                if (hit is not null) return hit;
                if (DateTime.UtcNow >= until) return "";
                await Task.Delay(20);
            }
        }

        public Task Say(string json) => Ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            Ws.Abort();
            if (_reader is not null) await _reader;
            Ws.Dispose();
        }
    }

    static async Task<bool> Until(Func<bool> done, int timeoutMs = 4000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!done()) { if (DateTime.UtcNow >= until) return false; await Task.Delay(20); }
        return true;
    }

    static JsonElement Parse(string json) { using var d = JsonDocument.Parse(json); return d.RootElement.Clone(); }

    /// <summary>Whether <paramref name="l"/> hears an error whose words are <paramref name="words"/> (read as JSON, so escapes don't matter).</summary>
    static Task<bool> HearsError(Link l, string words) => Until(() => l.Heard.Any(m =>
        m.Contains("\"type\":\"error\"", StringComparison.Ordinal) && Parse(m).TryGetProperty("error", out var e) && e.GetString() == words));

    static JsonElement GrantRowOf(string grants, string window)
        => Parse(grants).GetProperty("grants").EnumerateArray().First(g => g.GetProperty("window").GetString() == window);

    public static async Task RunWeb(Action<bool, string> Check)
    {
        var t0 = DateTime.UtcNow;
        var (patKey, patSecret) = ApiKeys.Create("Patterns FOH", ApiKeyScope.Node, t0);
        var (pmKey, pmSecret) = ApiKeys.Create("PlayMap Stage left", ApiKeyScope.Node, t0);
        var (ctlKey, ctlSecret) = ApiKeys.Create("Desk", ApiKeyScope.Control, t0);
        var (readKey, readSecret) = ApiKeys.Create("Dash", ApiKeyScope.Read, t0);
        var control = new GrantControl { Keys = [patKey, pmKey, ctlKey, readKey] };
        control.Bindings.Keep(control.Keys);
        control.Marks.Keep(control.Keys);
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        control.Director.Web = web;
        control.Snapshots.Publish(new ShowSnapshot { Text = "12:34", Seconds = 754 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        Check(Do(control, "window new lobby /clock").Ok && Do(control, "window new stage /viewer").Ok
              && Do(control, $"window grant lobby {Pat}").Ok && Do(control, $"window grant stage {Pm}").Ok,
            "grants web: lobby granted to Patterns as a page, stage to PlayMap to draw");
        ScreenWindow Lobby() => control.Windows.First(w => w.Name == "lobby");
        ScreenWindow Stage() => control.Windows.First(w => w.Name == "stage");
        string GrantToken() => Lobby().Grants.Single(g => g.Node == Pat).Token;
        var lobbyId = Lobby().Id;
        var stageId = Stage().Id;

        // ---- delivery: the verdict, then the grants with the path, before any frame, on the app's own key-verified link over loopback.
        await using var pat = await Link.Open(port, $"node={Pat}&t=patterns&can=pages", patSecret);
        var firstFrame = await pat.Next("\"type\":\"state\"");
        var heard = pat.Heard;
        var verdictAt = heard.FindIndex(m => m.Contains("\"type\":\"auth\"", StringComparison.Ordinal));
        var grantsAt = heard.FindIndex(m => m.Contains("\"type\":\"grants\"", StringComparison.Ordinal));
        var frameAt = heard.FindIndex(m => m.Contains("\"type\":\"state\"", StringComparison.Ordinal));
        var grants = grantsAt >= 0 ? heard[grantsAt] : "{}";
        Check(firstFrame.Length > 0 && verdictAt == 0 && grantsAt == 1 && frameAt > grantsAt && heard[0].Contains("\"verified\":\"key\"", StringComparison.Ordinal),
            $"grants web: a key-verified link hears its verdict, then its grants, then frames ({string.Join(" | ", heard.Take(3))})");
        var lobbyRow = GrantRowOf(grants, lobbyId);
        Check(Parse(grants).GetProperty("from").GetString() == OwnId && Parse(grants).GetProperty("link").GetString() == "local" && Parse(grants).GetProperty("rev").GetInt32() == 1
              && lobbyRow.GetProperty("path").GetString() == "/w/" + GrantToken() && lobbyRow.GetProperty("cannot").GetString() == ""
              && Parse(grants).GetProperty("grants").GetArrayLength() == 1,
            $"grants web: over loopback with can=pages the grant's address is given, and only its own node's grants ({grants})");
        Check(!grants.Contains(Lobby().Token, StringComparison.Ordinal) && !heard.Any(m => m.Contains("window-assign", StringComparison.Ordinal)),
            "grants web: never the window's own address, and no draw message to a page link");

        // Without pages: the row says why, with no path.
        await using (var bare = await Link.Open(port, $"node={Pat}&t=patterns", patSecret))
        {
            var g = await bare.Next("\"type\":\"grants\"");
            Check(g.Length > 0 && GrantRowOf(g, lobbyId) is var r && r.GetProperty("path").GetString() == "" && r.GetProperty("cannot").GetString() == WindowGrants.NoPages,
                $"grants web: a link that doesn't offer pages gets no path, and the reason ({g})");
        }
        // The operator's credential naming the node, and another node's key, get no grants.
        await using (var ctl = await Link.Open(port, $"node={Pat}&t=patterns&can=pages", ctlSecret))
        await using (var other = await Link.Open(port, $"node={Pat}&t=patterns&can=pages", pmSecret))
        {
            var ctlFrame = await ctl.Next("\"type\":\"state\"");
            var otherFrame = await other.Next("\"type\":\"state\"");
            await Task.Delay(300);
            Check(ctlFrame.Length > 0 && otherFrame.Length > 0
                  && !ctl.Heard.Concat(other.Heard).Any(m => m.Contains("\"grants\"", StringComparison.Ordinal) || m.Contains(GrantToken(), StringComparison.Ordinal)),
                "grants web: a control key claiming the node, and another node's key, hear no grants");
        }

        // ---- states: sent, held after grants-seen, open once its page links, shown once it reports.
        GrantObserved LobbyGrant() => web.GrantStatus().Single(g => g.WindowId == lobbyId);
        Check(LobbyGrant() is { State: "sent", Linked: true, Link: "local", Delivered: true, How: "page" }, $"grants web: given, not yet acknowledged: sent ({LobbyGrant()})");
        await pat.Say("{\"type\":\"grants-seen\",\"rev\":1,\"cannot\":\"\"}");
        Check(await Until(() => LobbyGrant().State == "held"), $"grants web: grants-seen makes it held ({LobbyGrant()})");
        await pat.Say("{\"type\":\"grants-seen\",\"rev\":1,\"cannot\":\"no web engine on this machine\"}");
        Check(await Until(() => LobbyGrant() is { State: "held", AppCannot: "no web engine on this machine" }), $"grants web: the app's reason is kept ({LobbyGrant()})");
        var cannotListed = Parse(await (await http.GetAsync("/api/windows?pin=4321")).Content.ReadAsStringAsync()).GetProperty("windows").EnumerateArray()
            .First(w => w.GetProperty("id").GetString() == lobbyId).GetProperty("grants")[0];
        Check(LobbyGrant().Words == "Patterns FOH has its address and shows no page: no web engine on this machine"
              && cannotListed.GetProperty("cannot").GetString() == "no web engine on this machine" && cannotListed.GetProperty("words").GetString() == LobbyGrant().Words,
            $"grants web: a held grant reads the app's own reason, in its words and in /api/windows ({cannotListed})");
        await pat.Say("{\"type\":\"grants-seen\",\"rev\":1,\"cannot\":\"\"}");
        Check(await Until(() => LobbyGrant() is { State: "held", AppCannot: "" }), $"grants web: and drops it when the app says it can ({LobbyGrant()})");
        await using var tv = await Link.Open(port, "win=" + Lobby().Token + "&detail=1");
        await using var patPage = await Link.Open(port, "win=" + GrantToken() + "&detail=1");
        var patTold = await patPage.Next("\"type\":\"window\"");
        Check(patTold.Contains("\"content\":\"/clock\"", StringComparison.Ordinal) && !patTold.Contains(GrantToken(), StringComparison.Ordinal),
            "grants web: the grant's address opens the window as a window link");
        Check(await Until(() => LobbyGrant().State == "open"), $"grants web: its page on the grant's address makes it open ({LobbyGrant()})");
        await patPage.Say($"{{\"type\":\"shown\",\"rev\":{Lobby().Revision},\"content\":\"/clock\",\"hold\":false,\"ms\":40,\"error\":\"\"}}");
        await tv.Say($"{{\"type\":\"shown\",\"rev\":{Lobby().Revision},\"content\":\"/clock\",\"hold\":false,\"ms\":41,\"error\":\"\"}}");
        Check(await Until(() => LobbyGrant().State == "shown"), $"grants web: its report makes it shown ({LobbyGrant()})");
        Check(web.WindowStatus().Count(o => o.WindowId == lobbyId) == 1 && web.WindowStatus().Single(o => o.WindowId == lobbyId).Connected == 2,
            "grants web: WindowStatus keeps one row per window, over both of its viewers");
        // The two reports come in on two sockets, each read on its own: wait for both rather than for the grant's alone.
        List<ViewerObserved> LobbyViewers() => web.ViewerStatus().Where(v => v.WindowId == lobbyId).OrderBy(v => v.Via, StringComparer.Ordinal).ToList();
        await Until(() => LobbyViewers() is [{ SwapMs: 41 }, { SwapMs: 40 }]);
        var viewers = LobbyViewers();
        Check(viewers is [{ Via: "", How: "", Connected: 1, SwapMs: 41 }, { Via: Pat, How: "page", Connected: 1, SwapMs: 40 }],
            $"grants web: ViewerStatus has a row per pass ({string.Join("; ", viewers)})");

        // ---- the app's messages only on its own link: a window link or a public screen hears 12.6's errors, and nothing is kept.
        await using (var screen = await Link.Open(port, "detail=1"))
        {
            await screen.Next("\"type\":\"state\"");
            var kept = web.ViewersKept;
            await screen.Say("{\"type\":\"grants-seen\",\"rev\":1,\"cannot\":\"\"}");
            await tv.Say("{\"type\":\"grants-seen\",\"rev\":1,\"cannot\":\"\"}");
            await tv.Say($"{{\"type\":\"window-shown\",\"window\":\"{lobbyId}\",\"rev\":{Lobby().Revision},\"content\":\"/clock\"}}");
            Check(await HearsError(screen, WindowGrants.SeenOnlyOnLink) && await HearsError(tv, WindowGrants.SeenOnlyOnLink)
                  && await HearsError(tv, WindowGrants.DrawOnly) && web.ViewersKept == kept,
                "grants web: grants-seen from a public screen or a window link, and window-shown from a window link, are refused in 12.6's words");
        }

        // ---- no grant address anywhere a person or a key reads: the window list for every credential, the context, frames.
        var listPin = await http.GetAsync("/api/windows?pin=4321");
        var bodyPin = await listPin.Content.ReadAsStringAsync();
        var bodyCtl = await (await http.GetAsync("/api/windows?pin=" + ctlSecret)).Content.ReadAsStringAsync();
        var bodyRead = await (await http.GetAsync("/api/windows?pin=" + readSecret)).Content.ReadAsStringAsync();
        var context = await (await http.GetAsync("/api/context?pin=4321")).Content.ReadAsStringAsync();
        Check(listPin.IsSuccessStatusCode && bodyPin.Contains("/w/" + Lobby().Token, StringComparison.Ordinal)
              && new[] { bodyPin, bodyCtl, bodyRead, context }.All(b => !b.Contains(GrantToken(), StringComparison.Ordinal)),
            "grants web: /api/windows lists the window's own address for the PIN and a grant's for nobody; the context carries none");
        var listed = Parse(bodyPin).GetProperty("windows").EnumerateArray().First(w => w.GetProperty("id").GetString() == lobbyId);
        var listedGrant = listed.GetProperty("grants")[0];
        Check(listed.GetProperty("connected").GetInt32() == 2 && listed.GetProperty("drawn").GetInt32() == 0
              && listedGrant.EnumerateObject().Select(p => p.Name).SequenceEqual(["node", "t", "name", "how", "linked", "link", "delivered", "open", "state", "words", "cannot"])
              && listedGrant.GetProperty("state").GetString() == "shown" && listedGrant.GetProperty("open").GetInt32() == 1
              && listedGrant.GetProperty("words").GetString() == "Patterns FOH shows it",
            $"grants web: /api/windows adds drawn and each grant's row, with no path ({listed})");
        await using (var desk = await Link.Open(port, "role=control&detail=1&pin=4321"))
        {
            await desk.Next("\"type\":\"state\"");
            Check(desk.Heard.All(m => !m.Contains(GrantToken(), StringComparison.Ordinal) && !m.Contains("\"grants\"", StringComparison.Ordinal)),
                "grants web: no frame carries a grant's address, nor grants");
        }

        // ---- a PlayMap link that doesn't offer window-draw: the row says why, no assignment, and its report is refused with no row kept.
        await using (var noDraw = await Link.Open(port, $"node={Pm}&t=playmap", pmSecret))
        {
            var g = await noDraw.Next("\"type\":\"grants\"");
            await noDraw.Say($"{{\"type\":\"window-shown\",\"window\":\"{stageId}\",\"rev\":{Stage().Revision},\"content\":\"/viewer\"}}");
            var refused = await HearsError(noDraw, WindowGrants.DrawOnly);
            Check(g.Length > 0 && GrantRowOf(g, stageId).GetProperty("cannot").GetString() == WindowGrants.NoDraw && refused
                  && !noDraw.Heard.Any(m => m.Contains("window-assign", StringComparison.Ordinal))
                  && web.ViewerStatus().All(v => v.Via != Pm) && web.WindowStatus().All(o => o.WindowId != stageId || o.Drawn == 0),
                $"grants web: a link without window-draw gets no assignment and may not report a drawing ({g})");
        }

        // ---- draw: PlayMap's link gets the assignment and no path; its report is a viewer; a page link may not report a drawing.
        await using var pm = await Link.Open(port, $"node={Pm}&t=playmap&can=window-draw", pmSecret);
        var pmGrants = await pm.Next("\"type\":\"grants\"");
        var assign = await pm.Next("\"type\":\"window-assign\"");
        Check(GrantRowOf(pmGrants, stageId) is var dr && dr.GetProperty("how").GetString() == "draw" && dr.GetProperty("path").GetString() == "" && dr.GetProperty("cannot").GetString() == ""
              && Parse(assign).GetProperty("window").GetString() == stageId && Parse(assign).GetProperty("content").GetString() == "/viewer"
              && pm.Heard.FindIndex(m => m.Contains("window-assign", StringComparison.Ordinal)) > pm.Heard.FindIndex(m => m.Contains("\"type\":\"grants\"", StringComparison.Ordinal)),
            $"grants web: a draw link hears its grants, then the window to draw, with no address ({assign})");
        Check(await Until(() => web.WindowStatus().FirstOrDefault(o => o.WindowId == stageId)?.Drawn == 1), "grants web: the window counts one app drawing it");
        await pm.Say($"{{\"type\":\"window-shown\",\"window\":\"{stageId}\",\"rev\":{Stage().Revision},\"content\":\"/viewer\",\"hold\":false,\"ms\":12,\"error\":\"\"}}");
        Check(await Until(() => web.ViewerStatus().Any(v => v is { Via: Pm, How: "draw", Connected: 1, SwapMs: 12 } && v.WindowId == stageId)),
            "grants web: window-shown from the drawing app is its viewer's row");
        Check(await Until(() => web.GrantStatus().Single(g => g.WindowId == stageId).State == "shown"), "grants web: and its grant reads shown");
        await pat.Say($"{{\"type\":\"window-shown\",\"window\":\"{lobbyId}\",\"rev\":1,\"content\":\"/clock\"}}");
        Check(await HearsError(pat, WindowGrants.DrawOnly), "grants web: a page grant's app may not say what it draws");
        var id = Do(control, "window identify stage");
        Check(id.Ok && Parse(await pm.Next("window-identify")).GetProperty("name").GetString() == "stage", $"grants web: identify reaches the drawing app as window-identify ({id.Words})");
        var reload = Do(control, "window reload stage");
        Check(!reload.Ok && reload.Words == "stage is drawn by PlayMap Stage left, which has no page to reload — window show stage <page> sends it again",
            $"grants web: reload of a window only an app draws is refused ({reload.Words})");
        Check(Do(control, "window show stage /clock").Ok && (await pm.Next("\"content\":\"/clock\"")).Contains("window-assign", StringComparison.Ordinal)
              && await Until(() => web.GrantStatus().Single(g => g.WindowId == stageId).State == "stale"),
            "grants web: a change reaches the drawing app, and its grant is stale until it reports");
        await Task.Delay(200);
        Check(!pm.Heard.Any(m => m.Contains("\"type\":\"identify\"", StringComparison.Ordinal) || m.Contains("\"type\":\"reload\"", StringComparison.Ordinal)),
            "grants web: a node link never hears a screen's identify or reload");

        // ---- withdraw: the grant's page ends black; the app hears grants without it; the window's own link carries on.
        var patSeen = pat.Heard.Count;
        var oldGrant = GrantToken();
        Check(Do(control, $"window withdraw lobby {Pat}").Ok, "grants web: withdraw applies");
        var black = await patPage.Next("\"revoked\":true");
        Check(black == WebServer.WindowGoneBlackJson && await Until(() => patPage.Ws.State != WebSocketState.Open), $"grants web: the grant's address hears the black end and is let go ({black})");
        var after = await pat.Next("\"type\":\"grants\"", patSeen);
        Check(after.Length > 0 && Parse(after).GetProperty("grants").GetArrayLength() == 0 && Parse(after).GetProperty("rev").GetInt32() > 1,
            $"grants web: the app hears its grants without the window ({after})");
        Check(tv.Ws.State == WebSocketState.Open && !tv.Heard.Any(m => m.Contains("revoked", StringComparison.Ordinal)), "grants web: the window's own address is untouched");
        Check((await http.GetAsync("/w/" + oldGrant)).StatusCode == HttpStatusCode.NotFound, "grants web: the withdrawn address opens nothing");
        // No tombstone is kept (D15): a link that opens the address after it stopped working hears 12.3's end in words, as for any address
        // that names no window; its holder draws black by 12.9.
        await using (var late = await Link.Open(port, "win=" + oldGrant + "&detail=1"))
            Check(await late.Next("\"revoked\":true") == WebServer.WindowGoneJson, "grants web: a link opened on a stopped grant address hears 12.3's end, with no tombstone");

        // ---- revoke: the grant's page ends black, the window's own link hears today's words, the app hears its new address.
        Check(Do(control, $"window grant lobby {Pat}").Ok, "grants web: granted again");
        await using var patPage2 = await Link.Open(port, "win=" + GrantToken() + "&detail=1");
        await patPage2.Next("\"type\":\"window\"");
        patSeen = pat.Heard.Count;
        var before = GrantToken();
        Check(Do(control, "window revoke lobby").Ok, "grants web: revoke applies");
        Check(await patPage2.Next("\"revoked\":true") == WebServer.WindowGoneBlackJson && await tv.Next("\"revoked\":true") == WebServer.WindowGoneJson,
            "grants web: on revoke the grant's address ends black and the window's own in words");
        var renewed = await pat.Next("/w/" + GrantToken(), patSeen);
        Check(renewed.Length > 0 && GrantToken() != before && !renewed.Contains(before, StringComparison.Ordinal), "grants web: the app hears its new address");

        // ---- a draw grant withdrawn ends its drawing.
        var pmSeen = pm.Heard.Count;
        Check(Do(control, $"window withdraw stage {Pm}").Ok, "grants web: draw withdraw applies");
        Check(Parse(await pm.Next("window-end", pmSeen)) is var end && end.GetProperty("window").GetString() == stageId && end.GetProperty("why").GetString() == "withdrawn",
            "grants web: the drawing app hears window-end withdrawn");

        // ---- viewers stay bounded through churn over distinct windows: each gone window's report is dropped as it goes, and the window's
        // own viewer is never cleared to make room.
        await using var tv2 = await Link.Open(port, "win=" + Lobby().Token + "&detail=1");
        await tv2.Next("\"type\":\"window\"");
        await tv2.Say($"{{\"type\":\"shown\",\"rev\":{Lobby().Revision},\"content\":\"/clock\",\"hold\":false,\"ms\":5,\"error\":\"\"}}");
        Check(await Until(() => web.ViewerStatus().Any(v => v.WindowId == lobbyId && v.Via == "" && v.SwapMs == 5)), "grants web: the window's own viewer reported");
        var baseline = web.ViewersKept;
        async Task<bool> DrawnAndReported(string id)
        {
            var told = await pm.Next($"\"type\":\"window-assign\",\"window\":\"{id}\"");
            var drawing = await Until(() => web.WindowStatus().Any(o => o.WindowId == id && o.Drawn == 1));
            await pm.Say($"{{\"type\":\"window-shown\",\"window\":\"{id}\",\"rev\":{control.Windows.First(w => w.Id == id).Revision},\"content\":\"/viewer\",\"hold\":false,\"ms\":1,\"error\":\"\"}}");
            return told.Length > 0 && drawing && await Until(() => web.ViewersKept == baseline + 1);
        }
        var churned = baseline >= 1;
        for (var i = 0; i < 24 && churned; i++)
        {
            var name = "churn" + i.ToString(CultureInfo.InvariantCulture);
            churned &= Do(control, $"window new {name} /viewer").Ok && Do(control, $"window grant {name} {Pm}").Ok;
            var cid = control.Windows.First(w => w.Name == name).Id;
            churned &= await DrawnAndReported(cid) && Do(control, $"window delete {name}").Ok && web.ViewersKept == baseline;
        }
        Check(churned && web.ViewerStatus().Any(v => v.WindowId == lobbyId && v.Via == "" && v.ShownUtc is not null),
            $"grants web: 24 windows drawn, reported and deleted leave the kept viewers where they were, the window's own row with them ({web.ViewersKept}, {baseline})");
        Check(Do(control, $"window grant stage {Pm}").Ok && await DrawnAndReported(stageId), "grants web: stage drawn again and reported");
        Check(Do(control, "window delete stage").Ok && web.ViewersKept == baseline && web.ViewerStatus().All(v => v.WindowId != stageId)
              && web.ViewerStatus().Any(v => v.WindowId == lobbyId && v.Via == ""),
            $"grants web: a deleted window's viewer reports are dropped from the table at once, and the other window's stay ({web.ViewersKept})");
    }

    /// <summary>A key seen in clear gets no grant, even over TLS; a plain link from another machine gets no address.</summary>
    public static async Task RunClear(Action<bool, string> Check)
    {
        var t0 = DateTime.UtcNow;
        var (patKey, patSecret) = ApiKeys.Create("Patterns FOH", ApiKeyScope.Node, t0);
        var (pmKey, pmSecret) = ApiKeys.Create("PlayMap Stage left", ApiKeyScope.Node, t0);
        var control = new GrantControl { Keys = [patKey, pmKey] };
        control.Bindings.Keep(control.Keys);
        control.Marks.Keep(control.Keys);
        var port = FreePort();
        using var cert = CertificateMaker.Create("Stagewatch", "Grants Hall", DateTimeOffset.UtcNow);
        // Every sender is another machine: loopback stands in for the LAN.
        await using var web = new WebServer(control, () => "4321") { IsThisPc = _ => false };
        web.Certificate = cert;
        web.Start(port, bindAll: false);
        control.Director.Web = web;
        control.Snapshots.Publish(new ShowSnapshot { Text = "12:34", Seconds = 754 });
        control.Snapshots.MarkAlive();
        Check(Do(control, "window new lobby /clock").Ok && Do(control, $"window grant lobby {Pat}").Ok, "grants clear: a page grant");
        var lobbyId = control.Windows.Single().Id;
        var grantToken = control.Windows.Single().Grants.Single().Token;

        await using (var tls = await Link.Open(port, $"node={Pat}&t=patterns&can=pages", patSecret, tls: true))
        {
            var g = await tls.Next("\"type\":\"grants\"");
            Check(Parse(g).GetProperty("link").GetString() == "tls" && GrantRowOf(g, lobbyId).GetProperty("path").GetString() == "/w/" + grantToken,
                $"grants clear: over TLS from another machine the address is given ({g})");
        }
        await using (var plain = await Link.Open(port, $"node={Pat}&t=patterns&can=pages", patSecret))
        {
            var g = await plain.Next("\"type\":\"grants\"");
            Check(Parse(g).GetProperty("link").GetString() == "plain" && GrantRowOf(g, lobbyId) is var r && r.GetProperty("path").GetString() == ""
                  && r.GetProperty("cannot").GetString() == WindowGrants.NotEncrypted && !g.Contains(grantToken, StringComparison.Ordinal),
                $"grants clear: a plain link from another machine gets no address, and why ({g})");
        }
        Check(!control.Marks.MayGrant(patKey.Id), "grants clear: the key that came in clear is marked");
        await using (var again = await Link.Open(port, $"node={Pat}&t=patterns&can=pages", patSecret, tls: true))
        {
            var g = await again.Next("\"type\":\"grants\"");
            Check(Parse(g).GetProperty("link").GetString() == "tls" && GrantRowOf(g, lobbyId) is var r && r.GetProperty("path").GetString() == ""
                  && r.GetProperty("cannot").GetString() == WindowGrants.KeyInClear && !g.Contains(grantToken, StringComparison.Ordinal),
                $"grants clear: once seen in clear the key gets no address, even over TLS ({g})");
            Check(web.GrantStatus().Single().State == "key-in-clear", "grants clear: the grant reads key-in-clear");
        }

        // A draw grant to a key seen in clear: no assignment, even over TLS with window-draw offered, and no report taken.
        Check(Do(control, "window new stage /viewer").Ok && Do(control, $"window grant stage {Pm}").Ok, "grants clear: a draw grant");
        var stageId = control.Windows.First(w => w.Name == "stage").Id;
        await using (var plain = await Link.Open(port, $"node={Pm}&t=playmap&can=window-draw", pmSecret))
            await plain.Next("\"type\":\"grants\"");
        Check(!control.Marks.MayGrant(pmKey.Id), "grants clear: PlayMap's key that came in clear is marked");
        await using (var drawTls = await Link.Open(port, $"node={Pm}&t=playmap&can=window-draw", pmSecret, tls: true))
        {
            var g = await drawTls.Next("\"type\":\"grants\"");
            await drawTls.Say($"{{\"type\":\"window-shown\",\"window\":\"{stageId}\",\"rev\":1,\"content\":\"/viewer\"}}");
            var refused = await HearsError(drawTls, WindowGrants.DrawOnly);
            Check(GrantRowOf(g, stageId).GetProperty("cannot").GetString() == WindowGrants.KeyInClear && refused
                  && !drawTls.Heard.Any(m => m.Contains("window-assign", StringComparison.Ordinal)) && web.ViewersKept == 0
                  && web.GrantStatus().Single(r => r.WindowId == stageId).State == "key-in-clear",
                $"grants clear: a draw grant's key seen in clear gets no assignment over TLS, and its report is refused ({g})");
        }
    }
}
