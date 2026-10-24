using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>Controlled windows: their addresses, what they may show, and every "window …" command, refusals first.</summary>
static class WindowTests
{
    static readonly HoldingImage[] Images =
    {
        new() { Id = "aaaaaaaaaaaa", Name = "Sponsors" },
        new() { Id = "bbbbbbbbbbbb", Name = "Back soon" },
    };

    static WindowContext Ctx()
    {
        var n = 0;
        return new WindowContext(
            name => name.Equals("lobby", StringComparison.OrdinalIgnoreCase) ? "/audience?holding=0" : name == "ops" ? "/control" : null,
            Images,
            () => ScreenWindows.NewToken(),
            () => "w" + (++n).ToString("000000", System.Globalization.CultureInfo.InvariantCulture));
    }

    static WindowChange Run(IReadOnlyList<ScreenWindow> list, string line, WindowContext? ctx = null)
    {
        var parts = line.Split(' ');
        if (!ShowCommandParser.TryParse(parts[0], parts.Skip(1).ToList(), out var cmd, out var error))
            return WindowChange.Refused(list, "parse: " + error);
        return ScreenWindows.Apply(list, cmd.Get<WindowArgs>(), ctx ?? Ctx());
    }

    public static void Run(Action<bool, string> Check)
    {
        // ---- addresses
        var t1 = ScreenWindows.NewToken();
        var t2 = ScreenWindows.NewToken();
        Check(t1.Length == 26 && ScreenWindows.ValidToken(t1) && t1 != t2 && !t1.Any(c => c is '0' or 'o' or '1' or 'l'),
              "windows: a token is 26 characters from an alphabet that can't be misread, and never repeats");
        Check(!ScreenWindows.ValidToken("") && !ScreenWindows.ValidToken(t1[..25]) && !ScreenWindows.ValidToken(t1.ToUpperInvariant())
              && !ScreenWindows.ValidToken(t1[..25] + "/") && !ScreenWindows.ValidToken(null), "windows: only strings a token could be reach a lookup");
        var a = new ScreenWindow { Id = "wa", Name = "lobby", Token = t1 };
        var b = new ScreenWindow { Id = "wb", Name = "foyer", Token = t2 };
        Check(ScreenWindows.ByToken(new[] { a, b }, t2) == b && ScreenWindows.ByToken(new[] { a, b }, ScreenWindows.NewToken()) is null
              && ScreenWindows.ByToken(new[] { a, b }, "lobby") is null, "windows: an address finds its window, and only its own");

        // ---- what a window may show
        Check(ScreenWindows.CanShow("/clock?h=12", out _) && ScreenWindows.CanShow("/layout?name=foyer", out _) && ScreenWindows.CanShow("/viewer", out _),
              "windows: public pages are content");
        foreach (var bad in new[] { "https://evil.example/", "//evil.example/x", "/viewer?pin=1234", "/w/" + t1, "/api/state", "/mcp", "/control", "/control.html",
                                    "/producer?x=1", "/designer", "/report", "/p/lobby", "/ws", "clock", "" })
            Check(!ScreenWindows.CanShow(bad, out var why) && why.Length > 0, $"windows: {(bad.Length == 0 ? "nothing" : bad)} is refused as content, with the reason");

        // ---- the grammar (one for every transport)
        Check(ShowCommandParser.TryParse("window", new[] { "show", "lobby", "/clock?h=12" }, out var c, out _) && c.Kind == ShowCommandKind.Window
              && c.Get<WindowArgs>() is { Action: "show", Who: "lobby", Argument: "/clock?h=12" }, "windows: window show lobby /clock parses typed");
        Check(ShowCommandParser.TryParse("window", new[] { "hold", "lobby", "text", "Back", "at", "two" }, out c, out _)
              && c.Get<WindowArgs>().Argument == "text Back at two", "windows: the rest of the line is the argument whole");
        Check(ShowCommandParser.TryParse("window", new[] { "release" }, out c, out _) && c.Get<WindowArgs>().Who == "all", "windows: no window means all");
        foreach (var bad in new[] { new[] { "show", "lobby" }, new[] { "explode", "lobby" }, new[] { "new" }, new[] { "show", "Lobby TV", "/clock" }, Array.Empty<string>() })
            Check(!ShowCommandParser.TryParse("window", bad, out _, out var err) && err.Length > 0, $"windows: \"window {string.Join(' ', bad)}\" is refused with words");
        Check(ShowCommandParser.Help.Contains("window show", StringComparison.Ordinal), "windows: the help lists the window commands");

        // ---- new
        IReadOnlyList<ScreenWindow> list = Array.Empty<ScreenWindow>();
        var r = Run(list, "window new lobby");
        Check(r.Ok && r.Windows.Count == 1 && r.Windows[0] is { Name: "lobby", Content: ScreenWindows.Holding, Hold: false, Revision: 1 }
              && ScreenWindows.ValidToken(r.Windows[0].Token) && r.Effect == WindowEffect.Changed, "windows: new makes a window on its holding page with an address");
        list = r.Windows;
        Check(!Run(list, "window new LOBBY").Ok && !Run(list, "window new lobby_tv/2").Ok, "windows: names are unique (any case) and plain");
        r = Run(list, "window new stage /viewer?cue=0");
        Check(r.Ok && r.Windows[1].Content == "/viewer?cue=0", "windows: new may say what the window shows");
        list = r.Windows;
        var full = Enumerable.Range(0, ScreenWindows.Max).Select(i => new ScreenWindow { Id = "x" + i, Name = "n" + i, Token = ScreenWindows.NewToken() }).ToList();
        Check(!Run(full, "window new one-more").Ok, $"windows: at most {ScreenWindows.Max}");

        // ---- show / cue / hold / release
        var before = list;
        r = Run(list, "window show lobby /clock");
        Check(r.Ok && r.Windows[0] is { Content: "/clock", Hold: false, Revision: 2 } && r.Touched.SequenceEqual(new[] { r.Windows[0].Id })
              && before[0].Content == ScreenWindows.Holding, "windows: show changes the one window, on a copy (the caller's list is untouched)");
        list = r.Windows;
        r = Run(list, "window show lobby /clock");
        Check(!r.Ok && r.Words.Contains("already shows", StringComparison.Ordinal) && ReferenceEquals(r.Windows, list), "windows: a show that changes nothing is refused with the reason");
        r = Run(list, "window show lobby lobby");
        Check(r.Ok && r.Windows[0].Content == "/audience?holding=0", "windows: a saved setup's name shows its page");
        Check(!Run(list, "window show lobby ops").Ok && !Run(list, "window show lobby nowhere").Ok && !Run(list, "window show lobby /control").Ok,
              "windows: a setup that is an operator page, an unknown setup and a PIN page are refused");
        Check(!Run(list, "window show nobody /clock").Ok && !Run(Array.Empty<ScreenWindow>(), "window show all /clock").Ok, "windows: an unknown window or no windows is refused");
        r = Run(list, "window show all black");
        Check(r.Ok && r.Windows.All(w => w.Content == ScreenWindows.Black) && r.Touched.Count == 2, "windows: all reaches every window");

        r = Run(list, "window cue lobby /timeline");
        Check(r.Ok && r.Windows[0] is { Content: "/timeline", Hold: true }, "windows: cue loads a page behind the holding page");
        list = r.Windows;
        r = Run(list, "window release lobby");
        Check(r.Ok && r.Windows[0] is { Content: "/timeline", Hold: false } && r.Words.Contains("/timeline", StringComparison.Ordinal), "windows: release takes the cued page");
        list = r.Windows;
        Check(!Run(list, "window release lobby").Ok, "windows: releasing a window that isn't holding is refused");

        r = Run(list, "window hold lobby text Back at 2pm");
        Check(r.Ok && r.Windows[0] is { Hold: true, HoldText: "Back at 2pm", HoldImage: "" }, "windows: hold with words");
        list = r.Windows;
        Check(!Run(list, "window hold lobby").Ok, "windows: holding a window already holding is refused");
        r = Run(list, "window hold lobby image Sponsors");
        Check(r.Ok && r.Windows[0] is { HoldImage: "aaaaaaaaaaaa", HoldText: "" }, "windows: hold with a holding image (by name) replaces the words");
        list = r.Windows;
        Check(!Run(list, "window hold lobby image Nothing").Ok && !Run(list, "window hold lobby sideways").Ok, "windows: an unknown image or option is refused");
        r = Run(list, "window hold all toggle");
        Check(r.Ok && r.Windows.All(w => !w.Hold) && r.Touched.Count == 1, "windows: toggle with any holding releases them (only the ones that change)");
        r = Run(r.Windows, "window hold all toggle");
        Check(r.Ok && r.Windows.All(w => w.Hold) && r.Windows[0].HoldImage == "aaaaaaaaaaaa", "windows: toggle again holds them all, keeping each window's holding card");

        // ---- loss, fade, rename, revoke, delete, identify
        r = Run(list, "window loss stage warn");
        Check(r.Ok && r.Windows[1].OnLoss == ScreenWindows.LossWarn && !Run(r.Windows, "window loss stage warn").Ok && !Run(list, "window loss stage maybe").Ok,
              "windows: loss is hold or warn, and a no-op is refused");
        r = Run(list, "window fade all 400");
        Check(r.Ok && r.Windows.All(w => w.FadeMs == 400) && !Run(list, "window fade all 9000").Ok && !Run(list, "window fade all soon").Ok, "windows: fade is 0-5000 ms");
        r = Run(list, "window rename lobby foyer");
        Check(r.Ok && r.Windows[0].Name == "foyer" && !Run(list, "window rename lobby stage").Ok && !Run(list, "window rename all x").Ok,
              "windows: rename one, to a free name");
        var oldToken = list[0].Token;
        r = Run(list, "window revoke lobby");
        Check(r.Ok && r.Windows[0].Token != oldToken && ScreenWindows.ValidToken(r.Windows[0].Token) && ScreenWindows.ByToken(r.Windows, oldToken) is null,
              "windows: revoke gives a new address and the old one finds nothing");
        r = Run(list, "window delete stage");
        Check(r.Ok && r.Windows.Count == 1 && !Run(list, "window delete all").Ok, "windows: delete one at a time");
        r = Run(list, "window identify all");
        Check(r.Ok && r.Effect == WindowEffect.Identify && ReferenceEquals(r.Windows, list) && r.Touched.Count == 2, "windows: identify changes nothing stored");

        // ---- what the Eye and an AI read
        var w0 = list[0];
        var items = ScreenWindows.ContextItems(list, Array.Empty<WindowObserved>());
        Check(items.Count == 2 && items.All(i => i.Light == Light.Grey && i.State.Contains("not opened yet", StringComparison.Ordinal)), "windows: never opened → grey, never guessed");
        WindowObserved Seen(int rev, int connected = 1, string error = "", bool hold = false) =>
            new(w0.Id, connected, rev, w0.Content, hold, 40, DateTime.UtcNow, error, "Linux · Chrome", "10.0.0.5");
        Check(ScreenWindows.ContextItems(list, new[] { Seen(w0.Revision) })[0].Light == Light.Green, "windows: green only when it says it shows what it was sent");
        Check(ScreenWindows.ContextItems(list, new[] { Seen(w0.Revision - 1) })[0] is { Light: Light.Amber } i1 && i1.State.Contains("not yet shown", StringComparison.Ordinal),
              "windows: sent but not shown is amber");
        Check(ScreenWindows.ContextItems(list, new[] { Seen(w0.Revision, error: "didn't load in 8 s") })[0] is { Light: Light.Amber } i2 && i2.State.Contains("couldn't show", StringComparison.Ordinal),
              "windows: a page that wouldn't load is amber, with the reason");
        Check(ScreenWindows.ContextItems(list, new[] { Seen(w0.Revision, connected: 0) })[0] is { Light: Light.Amber } i3 && i3.State.Contains("link lost", StringComparison.Ordinal),
              "windows: a lost link is amber (a lobby screen never turns the show red)");
        Check(ShowCommandParser.TryParse("window", new[] { "show", "lobby", "/clock" }, out var line, out _) && line.Kind == ShowCommandKind.Window
              && Stagewatch.Show.ShowContext.Commands.Any(v => v.Verb.StartsWith("window show", StringComparison.Ordinal)),
              "windows: the context lists window verbs for peers and AI, with their risk");
    }

    // ---------------------------------------------------------------- the server: addresses, the link, reports
    sealed class WindowControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(), Array.Empty<(string, string?, int)>());
        public WindowDirector Director = new(Array.Empty<ScreenWindow>(), _ => { });
        public IReadOnlyList<ScreenWindow> Windows => Director.Windows;
        public volatile string? View;
        public string? ViewCode => View;
        public volatile IReadOnlyList<ApiKey> Keys = Array.Empty<ApiKey>();
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
    }

    static WindowChange Do(WindowControl c, string line)
    {
        var parts = line.Split(' ');
        if (!ShowCommandParser.TryParse(parts[0], parts.Skip(1).ToList(), out var cmd, out var error)) return WindowChange.Refused(c.Windows, error);
        return c.Director.Run(cmd.Get<WindowArgs>(), _ => null, Array.Empty<HoldingImage>());
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    /// <summary>The next message whose text contains <paramref name="needle"/> ("" when none came in time).</summary>
    static async Task<string> Next(ClientWebSocket ws, string needle, int timeoutMs = 4000)
    {
        var buf = new byte[65536];
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until && ws.State == WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(until - DateTime.UtcNow);
            try
            {
                var r = await ws.ReceiveAsync(buf, cts.Token);
                if (r.MessageType == WebSocketMessageType.Close) return "";
                var text = Encoding.UTF8.GetString(buf, 0, r.Count);
                if (text.Contains(needle, StringComparison.Ordinal)) return text;
            }
            catch (OperationCanceledException) { return ""; }
            catch (WebSocketException) { return ""; }
        }
        return "";
    }

    static async Task<ClientWebSocket> Open(int port, string query)
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?{query}"), CancellationToken.None);
        return ws;
    }

    static Task Say(ClientWebSocket ws, string json) => ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    public static async Task RunWeb(Action<bool, string> Check)
    {
        var control = new WindowControl();
        var saved = new List<IReadOnlyList<ScreenWindow>>();
        control.Director = new WindowDirector(Array.Empty<ScreenWindow>(), l => { lock (saved) saved.Add(l); });
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        control.Director.Web = web;
        control.Snapshots.Publish(new ShowSnapshot { Text = "12:34", Seconds = 754 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        Check(Do(control, "window new lobby /clock").Ok && Do(control, "window new foyer").Ok && saved.Count == 2 && saved[^1].Count == 2,
              "windows web: windows are made through the director and saved");
        var lobby = control.Windows.First(w => w.Name == "lobby");
        var foyer = control.Windows.First(w => w.Name == "foyer");

        // The address page.
        var page = await http.GetAsync("/w/" + lobby.Token);
        var csp = page.Headers.TryGetValues("Content-Security-Policy", out var cspValues) ? string.Join(";", cspValues) : "";
        var html = await page.Content.ReadAsStringAsync();
        Check(page.IsSuccessStatusCode && html.Contains("/window.js", StringComparison.Ordinal) && csp.Contains("frame-ancestors 'none'", StringComparison.Ordinal)
              && csp.Contains("frame-src 'self'", StringComparison.Ordinal) && page.Headers.GetValues("Referrer-Policy").Single() == "no-referrer"
              && page.Headers.CacheControl?.NoStore == true, "windows web: /w/<token> serves the window page: frames only this Stagewatch, framed by nothing, no referrer, no cache");
        Check((await http.GetAsync("/window.js")).IsSuccessStatusCode && (await http.GetAsync("/window.html")).StatusCode == HttpStatusCode.NotFound,
              "windows web: the script is served; the page only at a window's address");
        var wrong = await http.GetAsync("/w/" + ScreenWindows.NewToken());
        Check(wrong.StatusCode == HttpStatusCode.NotFound && (await wrong.Content.ReadAsStringAsync()).Contains("in use: it may have been replaced", StringComparison.Ordinal),
              "windows web: an address that isn't in use says so (and gives nothing away)");

        // The link: what to show first, then frames; never its own address.
        using var a = await Open(port, "win=" + lobby.Token + "&detail=1");
        var told = await Next(a, "\"type\":\"window\"");
        Check(told.Contains("\"content\":\"/clock\"", StringComparison.Ordinal) && told.Contains("\"name\":\"lobby\"", StringComparison.Ordinal)
              && !told.Contains(lobby.Token, StringComparison.Ordinal), $"windows web: a window hears what to show, without its address ({told})");
        var frame = await Next(a, "\"type\":\"state\"");
        Check(frame.Contains("\"detail\":true", StringComparison.Ordinal), "windows web: a window gets the public detail frame (what any agenda screen gets)");
        Check(web.ListScreens().All(x => !x.Page.StartsWith("/w/", StringComparison.Ordinal)) && web.WindowStatus().Single(o => o.WindowId == lobby.Id).Connected == 1,
              "windows web: a window is listed as a window, not as a screen");

        // Report what it shows → the operator, the context and /api/windows read it.
        await Say(a, $"{{\"type\":\"shown\",\"rev\":{lobby.Revision},\"content\":\"/clock\",\"hold\":false,\"ms\":42,\"error\":\"\"}}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (web.WindowStatus().FirstOrDefault(o => o.WindowId == lobby.Id)?.ShownUtc is null && sw.ElapsedMilliseconds < 3000) await Task.Delay(20);
        var seen = web.WindowStatus().Single(o => o.WindowId == lobby.Id);
        Check(seen is { Revision: var rv, Content: "/clock", SwapMs: 42, Connected: 1 } && rv == lobby.Revision, $"windows web: the window's report is kept ({seen})");
        Check(ScreenWindows.ContextItems(control.Windows, web.WindowStatus()).Single(i => i.Label == "Window lobby").Light == Light.Green,
              "windows web: in step → green for the Eye and the AI");

        // A change reaches the window at once; until it reports, it is amber.
        Check(Do(control, "window hold lobby text Back soon").Ok, "windows web: hold applies");
        told = await Next(a, "\"hold\":true");
        Check(told.Contains("\"holdText\":\"Back soon\"", StringComparison.Ordinal), $"windows web: the window is told at once ({told})");
        Check(ScreenWindows.ContextItems(control.Windows, web.WindowStatus()).Single(i => i.Label == "Window lobby").Light == Light.Amber,
              "windows web: sent but not yet shown → amber");

        // A window may not drive the show, and an ordinary screen can't pose as a window.
        await Say(a, "{\"cmd\":\"next\",\"args\":[]}");
        var refused = await Next(a, "\"type\":\"error\"");
        Check(refused.Contains("read-only", StringComparison.Ordinal) && control.Commands.Count == 0, "windows web: a window's link can't run show commands");
        using var plain = await Open(port, "cid=plainscreen1&page=%2Fviewer");
        await Say(plain, "{\"type\":\"shown\",\"rev\":1,\"content\":\"/x\"}");
        Check((await Next(plain, "\"type\":\"error\"")).Contains("only a window", StringComparison.Ordinal), "windows web: only a window reports what it shows");

        // Identify and reload reach open windows only, and say so.
        var id = Do(control, "window identify lobby");
        Check(id.Ok && (await Next(a, "\"type\":\"identify\"")).Contains("\"name\":\"lobby\"", StringComparison.Ordinal), "windows web: identify shows the name on the window");
        var idFoyer = Do(control, "window identify foyer");
        Check(!idFoyer.Ok && idFoyer.Words.Contains("isn't open", StringComparison.Ordinal), $"windows web: identifying a window nobody has open is refused ({idFoyer.Words})");

        // The list: read access; the addresses only for the PIN or a control key.
        var (readKey, readSecret) = ApiKeys.Create("dash", ApiKeyScope.Read, DateTime.UtcNow);
        control.Keys = new[] { readKey };
        Check((await http.GetAsync("/api/windows")).StatusCode == HttpStatusCode.Unauthorized, "windows web: the list needs the PIN or a key");
        var withPin = await (await http.GetAsync("/api/windows?pin=4321")).Content.ReadAsStringAsync();
        var withRead = await (await http.GetAsync("/api/windows?pin=" + readSecret)).Content.ReadAsStringAsync();
        Check(withPin.Contains("/w/" + lobby.Token, StringComparison.Ordinal) && withPin.Contains("\"connected\":1", StringComparison.Ordinal)
              && withPin.Contains("\"ms\":42", StringComparison.Ordinal), "windows web: with the PIN the list has the addresses and what each shows");
        Check(withRead.Contains("\"name\":\"lobby\"", StringComparison.Ordinal) && !withRead.Contains(lobby.Token, StringComparison.Ordinal),
              "windows web: a read key sees the windows, never their addresses");

        // The viewing code: a window's pages pass with its address.
        control.View = "secret";
        Check((await http.GetAsync("/api/config?win=" + foyer.Token)).IsSuccessStatusCode && (await http.GetAsync("/api/config")).StatusCode == HttpStatusCode.Unauthorized,
              "windows web: a window's pages fetch past the viewing code with its address");
        control.View = null;

        // Revoke: the open window is told and let go; its old address finds nothing.
        var oldToken = lobby.Token;
        Check(Do(control, "window revoke lobby").Ok, "windows web: revoke applies");
        var gone = await Next(a, "\"revoked\":true");
        var after = await Next(a, "nothing comes after the close", 3000);   // reads on until the server's close arrives
        Check(gone.Length > 0 && after.Length == 0 && a.State != WebSocketState.Open, $"windows web: a revoked window is told and its link closes ({a.State})");
        Check((await http.GetAsync("/w/" + oldToken)).StatusCode == HttpStatusCode.NotFound
              && (await http.GetAsync("/w/" + control.Windows.First(w => w.Name == "lobby").Token)).IsSuccessStatusCode,
              "windows web: the old address is dead, the new one works");
        using var stale = await Open(port, "win=" + oldToken);
        Check((await Next(stale, "\"revoked\":true")).Length > 0, "windows web: a window that comes back on an old address is told it's gone");

        // Wrong addresses count like wrong PINs: the fifth locks the device out for a minute, even for a right address,
        // and a window on that device is turned away for now (to retry), never told its address is gone.
        for (var i = 0; i < 5; i++) await http.GetAsync("/w/" + ScreenWindows.NewToken());
        var locked = await http.GetAsync("/w/" + foyer.Token);
        Check((int)locked.StatusCode == 429 && (await locked.Content.ReadAsStringAsync()).Contains("refresh", StringComparison.Ordinal),
              "windows web: guessing addresses locks the device out, and the page retries by itself");
        using var lockedWs = await Open(port, "win=" + foyer.Token);
        var lockedMsg = await Next(lockedWs, "\"type\"", 1500);
        Check(!lockedMsg.Contains("revoked", StringComparison.Ordinal), "windows web: a locked-out window is never told its address is gone");
    }
}
