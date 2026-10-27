using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>2.0 web: role pages, detail frames, public verbs (ack / question) without the PIN, rate limits, QR, report, MCP.</summary>
static class WebV2Tests
{
    sealed class ReportControl : IShowControl
    {
        public SnapshotHub Snapshots { get; init; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(),
            Array.Empty<(string, string?, int)>(), new[] { ("keynote", "Keynote") }, new[] { "Lighting", "Sound" }, "Hall A", "keynote");
        public string? Report(string format) => format == "csv" ? "#,Title\r\n1,Opening\r\n" : "<!doctype html><h1>Summit</h1>";
        public volatile bool Mcp = true;
        public (string? Secret, bool Required) CrewKeyPolicy { get; set; } = ("test-secret", true);
        public IReadOnlyList<string> CrewDepartments => new[] { "Lighting", "Sound" };
        public bool McpAllowed => Mcp;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Data, string Name)> Uploads = new();
        public (byte[] Data, string ContentType)? HoldingImageData(string id)
            => Uploads.TryGetValue(id, out var u) ? (u.Data, "image/png") : null;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
        public readonly Stagewatch.Integrations.RemoteDecks Decks = new();
        public string? AcceptSlideFeed(SlideFeed feed, string address, out bool needScript) { needScript = Decks.Accept(feed, address); return null; }
        public void AcceptSlideScript(string hash, string script) => Decks.AcceptScript(hash, script);
        public IReadOnlyList<(int Index, string Title, string Note)>? DeptNotesFor(string dept)
            => new[] { (0, "Keynote", dept.Equals("Lighting", StringComparison.OrdinalIgnoreCase) ? "Blue wash" : "") };
        public volatile string? View;
        public string? ViewCode => View;
        public volatile IReadOnlyList<ScreenWindow> WindowList = Array.Empty<ScreenWindow>();
        public IReadOnlyList<ScreenWindow> Windows => WindowList;
        public volatile IReadOnlyList<ApiKey> Keys = Array.Empty<ApiKey>();
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Layouts = new(StringComparer.OrdinalIgnoreCase);
        public string? LayoutJson(string name) => Layouts.TryGetValue(name, out var j) ? j : null;
        public IReadOnlyList<string> LayoutNames => Layouts.Keys.ToList();
        public string? SaveLayout(string json)
        {
            if (ScreenLayouts.FromJson(json, out var e) is not { } l) return e;
            Layouts[l.Name] = ScreenLayouts.ToJson(l);
            return null;
        }
        public void DeleteLayout(string name) => Layouts.TryRemove(name, out _);
        public string? LastDesignRequest;
        public Task<(string? Json, IReadOnlyList<string> Notes)> DesignLayoutAsync(string request, string? currentJson)
        {
            LastDesignRequest = request + "|" + (currentJson is null ? "" : "current");
            return Task.FromResult<(string?, IReadOnlyList<string>)>((ScreenLayouts.ToJson(ScreenLayouts.Starter("ai-draft")), new[] { "check the colours" }));
        }
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public byte[]? SoundData(string id) => id == "abc123" ? Wav.Encode(new Wav.Sound(new short[800], 8000)) : null;
        public volatile bool AgentApproval;
        public readonly List<string> Proposed = new();
        public AgentReply? ProposeFromAgent(string line, string reason, bool mustPropose)
        {
            if (!AgentApproval && !mustPropose) return null;
            lock (Proposed) Proposed.Add(line + " | " + reason);
            return AgentReply.Waiting("Queued for the operator's approval (proposal #1).");
        }
        public string? ScreenName(string screenId) => Names.TryGetValue(screenId, out var n) ? n : null;
        public string? ScreenPresetPath(string name) => name.Equals("lobby", StringComparison.OrdinalIgnoreCase) ? "/audience?holding=0" : null;
        public IReadOnlyList<(string Name, string Path)> ScreenPresets => new[] { ("lobby", "/audience?holding=0") };
        public (string? Id, string? Error) AddHoldingImage(string name, byte[] data)
        {
            if (HoldingLibrary.Sniff(data) is null) return (null, "not an image");
            var id = Guid.NewGuid().ToString("N")[..12];
            Uploads[id] = (data, name);
            return (id, null);
        }
    }

    public static async Task Run(Action<bool, string> Check)
    {
        var control = new ReportControl();
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        control.Snapshots.Publish(new ShowSnapshot
        {
            Text = "05:00", Seconds = 300, Mode = TimerMode.Countdown, Title = "Keynote", Room = "Hall A",
            Timings = new[] { new ItemTimingView(0, "Keynote", "Ana", SegmentKind.Talk, 1800, 0, "09:00", "09:05", "09:35", "09:05:10", "", ItemStatus.Live, 300, false, false, "") }.ToEquatableArray(),
            Signals = new[]
            {
                new SignalView(3, SignalKind.Message, "speaker", "Wrap up", SignalState.Sent, false, 4, ""),
                new SignalView(5, SignalKind.Go, "Lighting", "Q12", SignalState.Sent, false, 2, ""),
            }.ToEquatableArray(),
            Slides = new SlideView(4, 20, "Results", "SECRET SPEAKER NOTES", 0, 0, PaceState.Unknown, false),
            Advice = new[] { new Advice("cut", AdviceLevel.Warn, "PRIVATE: cut the panel by 4 min", "trim 2 -240", "Cut") }.ToEquatableArray(),
            Questions = new[]
            {
                new QuestionView(1, "Approved question", "Sam", QuestionState.Approved),
                new QuestionView(2, "RAW UNMODERATED SUBMISSION", "Troll", QuestionState.New),
            }.ToEquatableArray()
        });

        foreach (var page in new[] { "/producer", "/moderator", "/backstage", "/dept?name=Sound", "/interpreter", "/haptic", "/audience", "/ask", "/rooms", "/speaker" })
        {
            var r = await http.GetAsync(page);
            var body = await r.Content.ReadAsStringAsync();
            Check(r.IsSuccessStatusCode && body.Contains("<script src=\"/") && !body.Contains("<script>"), $"web: {page} served, scripts external (CSP)");
        }
        foreach (var asset in new[] { "/producer.js", "/role.js", "/audience.js", "/ask.js", "/rooms.js", "/sw.css", "/manifest.json" })
            Check((await http.GetAsync(asset)).IsSuccessStatusCode, $"web: {asset}");

        var basic = await http.GetStringAsync("/api/state");
        var detail = await http.GetStringAsync("/api/state?detail=1");
        Check(!basic.Contains("\"timings\"") && detail.Contains("\"timings\":[{\"i\":0,\"title\":\"Keynote\""), "web: detail frames on request");
        Check(!basic.Contains("SECRET SPEAKER NOTES") && !detail.Contains("SECRET SPEAKER NOTES"), "review P1: speaker notes never in public frames");
        Check(!detail.Contains("PRIVATE: cut the panel") && !detail.Contains("RAW UNMODERATED") && detail.Contains("Approved question"),
              "review P1: public detail has no assistant advice and only moderated questions");
        var opDetail = await http.GetStringAsync("/api/state?detail=1&pin=4321");
        Check(opDetail.Contains("SECRET SPEAKER NOTES") && opDetail.Contains("PRIVATE: cut the panel") && opDetail.Contains("RAW UNMODERATED"),
              "review P1: the operator (PIN) still sees everything");
        // API keys: scoped, revocable, rate limited — and never the PIN.
        var (readKey, readSecret) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, DateTime.UtcNow);
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        var (mcpKey, mcpSecret) = ApiKeys.Create("Agent", ApiKeyScope.Mcp, DateTime.UtcNow);
        control.Keys = new[] { readKey, ctlKey, mcpKey };
        HttpRequestMessage Keyed(HttpMethod m, string path, string secret, string? body = null)
        {
            var r = new HttpRequestMessage(m, path);
            r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
            if (body is not null) r.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return r;
        }
        var readState = await http.SendAsync(Keyed(HttpMethod.Get, "/api/state?detail=1", readSecret));
        Check((await readState.Content.ReadAsStringAsync()).Contains("SECRET SPEAKER NOTES")
              && readState.Headers.TryGetValues("X-RateLimit-Limit", out var lim) && lim.Single() == ApiKeys.PerMinute.ToString()
              && readState.Headers.TryGetValues("X-RateLimit-Remaining", out var rem) && int.Parse(rem.Single()) == ApiKeys.PerMinute - 1,
              "api keys: a read key sees the operator's state, with rate-limit headers");
        Check((await http.SendAsync(Keyed(HttpMethod.Get, "/report", readSecret))).IsSuccessStatusCode, "api keys: a read key reads reports");
        var keyBefore = control.Commands.Count;
        var readCmd = await http.SendAsync(Keyed(HttpMethod.Post, "/api/start", readSecret));
        Check(readCmd.StatusCode == HttpStatusCode.Forbidden && control.Commands.Count == keyBefore, "api keys: a read key can't run commands");
        var ctlCmd = await http.SendAsync(Keyed(HttpMethod.Post, "/api/start", ctlSecret));
        Check(ctlCmd.IsSuccessStatusCode && control.Commands.Count == keyBefore + 1 && ctlCmd.Headers.Contains("X-RateLimit-Remaining"), "api keys: a control key runs commands");
        var mcpInit = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";
        Check((await http.SendAsync(Keyed(HttpMethod.Post, "/mcp", ctlSecret, mcpInit))).StatusCode == HttpStatusCode.Forbidden
              && (await http.SendAsync(Keyed(HttpMethod.Post, "/mcp", mcpSecret, mcpInit))).IsSuccessStatusCode, "api keys: only an MCP key reaches /mcp");
        Check((await http.GetAsync("/api/start?pin=" + ctlSecret)).IsSuccessStatusCode, "api keys: also accepted as ?pin=");
        control.Keys = new[] { readKey, mcpKey };
        Check((await http.SendAsync(Keyed(HttpMethod.Post, "/api/start", ctlSecret))).StatusCode == HttpStatusCode.Unauthorized, "api keys: revoking works at once");
        Check((await http.GetAsync("/api/start?pin=4321")).IsSuccessStatusCode, "api keys: the PIN still opens everything");
        for (var i = 0; i < ApiKeys.PerMinute; i++) await http.SendAsync(Keyed(HttpMethod.Get, "/api/screens", mcpSecret));
        var limited = await http.SendAsync(Keyed(HttpMethod.Get, "/api/screens", mcpSecret));
        Check(limited.StatusCode == (HttpStatusCode)429 && limited.Headers.Contains("Retry-After")
              && limited.Headers.GetValues("X-RateLimit-Remaining").Single() == "0", "api keys: rate limited with Retry-After");
        Check(web.KeyLastUsed(mcpKey.Id) is not null && web.KeyLastUsed("nope") is null, "api keys: last use is tracked");
        // Screen designer: anyone can view a layout; saving and AI drafts need the PIN or a control key.
        var lj = ScreenLayouts.ToJson(ScreenLayouts.Starter("foyer"));
        Check((await http.PostAsync("/api/layouts/save", new StringContent(lj))).StatusCode == HttpStatusCode.Unauthorized
              && (await http.PostAsync("/api/layouts/save?pin=" + readSecret, new StringContent(lj))).StatusCode == HttpStatusCode.Forbidden, "layouts: saving needs control");
        Check((await http.PostAsync("/api/layouts/save?pin=4321", new StringContent(lj))).IsSuccessStatusCode
              && (await http.GetStringAsync("/api/layouts")).Contains("\"foyer\"")
              && (await http.GetStringAsync("/api/layouts/foyer")).Contains("\"timer\""), "layouts: saved, listed and served");
        Check((await http.PostAsync("/api/layouts/save?pin=4321", new StringContent("{\"name\":\"bad name!\"}"))).StatusCode == HttpStatusCode.BadRequest, "layouts: a bad layout is refused");
        var draft = await http.PostAsync("/api/layouts/ai?pin=4321", new StringContent("{\"request\":\"foyer sign, big clock\",\"current\":" + lj + "}"));
        var draftJson = await draft.Content.ReadAsStringAsync();
        Check(draft.IsSuccessStatusCode && draftJson.Contains("\"layout\":{") && draftJson.Contains("check the colours") && control.LastDesignRequest == "foyer sign, big clock|current"
              && !control.Layouts.ContainsKey("ai-draft"), "layouts: an AI draft comes back for preview and isn't saved");
        Check((await http.PostAsync("/api/layouts/delete?name=foyer&pin=4321", new StringContent(""))).IsSuccessStatusCode && control.LayoutJson("foyer") is null
              && (await http.GetAsync("/api/layouts/foyer")).StatusCode == HttpStatusCode.NotFound, "layouts: deleted");
        Check((await http.GetAsync("/layout")).IsSuccessStatusCode && (await http.GetAsync("/designer")).IsSuccessStatusCode && (await http.GetAsync("/layout-render.js")).IsSuccessStatusCode,
              "layouts: pages served");
        // Viewing code protects the configuration (running-order and preset names) as well as the state.
        control.View = "lobby42";
        Check((await http.GetAsync("/api/config")).StatusCode == HttpStatusCode.Unauthorized
              && (await http.GetAsync("/api/config?view=lobby42")).IsSuccessStatusCode
              && (await http.GetAsync("/api/config?pin=4321")).IsSuccessStatusCode, "review: /api/config needs the viewing code too");
        control.View = null;
        var wav = await http.GetAsync("/api/sounds/abc123.wav");
        Check(wav.IsSuccessStatusCode && wav.Content.Headers.ContentType?.MediaType == "audio/wav"
              && (await http.GetAsync("/api/sounds/nope.wav")).StatusCode == HttpStatusCode.NotFound
              && (await http.GetAsync("/api/sounds/..%2Fsettings.wav")).StatusCode == HttpStatusCode.NotFound, "chimes: screens fetch library sounds by id only");
        Check((await http.GetAsync("/report")).StatusCode == HttpStatusCode.Unauthorized
              && (await http.GetAsync("/api/report.csv")).StatusCode == HttpStatusCode.Unauthorized, "review P1: reports need the PIN");

        var cfg = JsonDocument.Parse(await http.GetStringAsync("/api/config")).RootElement;
        Check(cfg.GetProperty("departments").GetArrayLength() == 2 && cfg.GetProperty("room").GetString() == "Hall A" && cfg.GetProperty("ladders")[0].GetProperty("id").GetString() == "keynote", "web: config has departments, room, ladders");

        // Public verbs: ack and question work without the PIN; nothing else does.
        var before = control.Commands.Count;
        var noKey = await http.GetAsync("/api/ack/3/done/Ana");
        var wrongKey = await http.GetAsync("/api/ack/3/done/Ana?key=" + CrewKeys.For("test-secret", "Lighting"));
        var unknownCue = await http.GetAsync("/api/ack/99/done?key=" + CrewKeys.For("test-secret", "speaker"));
        Check(noKey.StatusCode == HttpStatusCode.Forbidden && wrongKey.StatusCode == HttpStatusCode.Forbidden && unknownCue.StatusCode == HttpStatusCode.Forbidden
              && control.Commands.Count == before, "review P1: no key / another target's key / unknown cue can't confirm");
        var ack = await http.GetAsync("/api/ack/3/done/Ana?key=" + CrewKeys.For("test-secret", "speaker"));
        Check(ack.IsSuccessStatusCode && control.Commands.Last() is { Kind: ShowCommandKind.Ack, Number: 3, Text: "Ana (127.0.0.1)|done" },
              "review P1: the speaker's crew link confirms, and the log records where from");
        for (var i = 0; i < 6; i++) await http.GetAsync("/api/ack/3/done");
        Check((await http.GetAsync("/api/ack/3/done/Ana?key=" + CrewKeys.For("test-secret", "speaker"))).IsSuccessStatusCode,
              "public acks never count as wrong PINs (no lockout)");
        control.CrewKeyPolicy = ("test-secret", false);
        Check((await http.GetAsync("/api/ack/3/done")).IsSuccessStatusCode, "crew keys off: any screen may confirm an existing cue");
        control.CrewKeyPolicy = ("test-secret", true);
        var q1 = await http.PostAsync("/api/question/" + Uri.EscapeDataString("Why now?") + "/Sam", null);
        Check(q1.IsSuccessStatusCode && control.Commands.Last() is { Kind: ShowCommandKind.Question, Text: "Why now?\u0001Sam" }, "web: question without PIN");
        var q2 = await http.PostAsync("/api/question/" + Uri.EscapeDataString("Again?"), null);
        Check((int)q2.StatusCode == 429, "web: question rate-limited");
        Check((await http.GetAsync("/api/start")).StatusCode == HttpStatusCode.Unauthorized, "web: control verbs still need the PIN");

        // Read-only WebSocket may acknowledge.
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?detail=1&key={CrewKeys.For("test-secret", "lighting")}"), CancellationToken.None);
            var first = await Receive(ws);
            Check(first.Contains("\"detail\":true"), "ws: detail frame");
            var n = control.Commands.Count;
            await ws.SendAsync(Encoding.UTF8.GetBytes("{\"cmd\":\"ack\",\"args\":[\"5\",\"seen\"]}"), WebSocketMessageType.Text, true, CancellationToken.None);
            await ws.SendAsync(Encoding.UTF8.GetBytes("{\"cmd\":\"start\",\"args\":[]}"), WebSocketMessageType.Text, true, CancellationToken.None);
            var err = await Receive(ws);
            await Task.Delay(100);
            Check(control.Commands.Count == n + 1 && control.Commands.Last() is { Kind: ShowCommandKind.Ack, Number: 5 } && err.Contains("read-only"), "ws: read-only may ack, may not start");
            await ws.SendAsync(Encoding.UTF8.GetBytes("{\"cmd\":\"ack\",\"args\":[\"3\",\"done\"]}"), WebSocketMessageType.Text, true, CancellationToken.None);
            var refused = await Receive(ws);
            for (var tries = 0; tries < 5 && !refused.Contains("\"error\""); tries++) refused = await Receive(ws);
            Check(refused.Contains("confirm cues for speaker") && control.Commands.Count == n + 1, $"review P1: Lighting's page can't confirm the speaker's cue [{refused[..Math.Min(160, refused.Length)]}] cmds={control.Commands.Count - n}");

            // Review P1 (stale screens): a quiet show still sends a heartbeat at least every 2 s.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string beat;
            do { beat = await Receive(ws, 4000); } while (!beat.Contains("\"beat\"") && sw.Elapsed < TimeSpan.FromSeconds(5));
            Check(beat.Contains("\"type\":\"beat\"") && sw.Elapsed < TimeSpan.FromSeconds(3), $"review P1: heartbeat on an idle link ({sw.Elapsed.TotalSeconds:0.0} s)");
        }

        // Review P2 (frozen producer): heartbeats prove the link, not the show. When the show thread stops turning
        // over, screens get "live":false beats (so they go stale) and no frozen frame — even on reconnect.
        {
            long nowMs = 1_000_000;
            var frozen = new ReportControl { Snapshots = new SnapshotHub(() => Volatile.Read(ref nowMs)) };
            frozen.Snapshots.Publish(new ShowSnapshot { State = TimerState.Running, Seconds = 300, Text = "05:00" });
            frozen.Snapshots.MarkAlive();
            var fport = FreePort();
            await using var fweb = new WebServer(frozen, () => "4321");
            fweb.Start(fport, bindAll: false);
            Check(!frozen.Snapshots.ProducerStuck, "producer: alive right after it reports");
            Interlocked.Exchange(ref nowMs, nowMs + 60_000);     // 60 s with no sign of life, as in the review's probe
            Check(frozen.Snapshots.ProducerStuck, "producer: stuck after 60 s of silence");
            using var fws = new ClientWebSocket();
            await fws.ConnectAsync(new Uri($"ws://127.0.0.1:{fport}/ws"), CancellationToken.None);
            var stuckMsg = await Receive(fws, 4000);
            Check(stuckMsg.Contains("\"live\":false") && !stuckMsg.Contains("\"state\""), $"review P2: a stuck producer sends live:false, not the frozen 05:00 ({stuckMsg[..Math.Min(80, stuckMsg.Length)]})");
            frozen.Snapshots.MarkAlive();                       // recovers
            frozen.Snapshots.Publish(new ShowSnapshot { State = TimerState.Running, Seconds = 299, Text = "04:59" });
            string back;
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            do { back = await Receive(fws, 4000); } while (!back.Contains("\"state\"") && sw2.Elapsed < TimeSpan.FromSeconds(5));
            Check(back.Contains("04:59"), "review P2: frames resume when the producer recovers");
            Check(!new SnapshotHub().ProducerStuck, "producer: a hub nobody reports to never claims to be stuck");
        }

        // Screens 2: reload and disconnect reach the screen; the viewing code keeps unknown screens out.
        {
            using var tv = new ClientWebSocket();
            await tv.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=tvreload01&page=/viewer"), CancellationToken.None);
            await Receive(tv);
            Check(web.Reload(new[] { "tvreload01" }) == 1, "screens: reload sent");
            string m;
            var swr = System.Diagnostics.Stopwatch.StartNew();
            do { m = await Receive(tv); } while (!m.Contains("reload") && swr.Elapsed < TimeSpan.FromSeconds(4));
            Check(m.Contains("\"type\":\"reload\""), "screens: the screen is told to reload");
            web.Disconnect(new[] { "tvreload01" });
            swr.Restart();
            do { m = await Receive(tv); } while (!m.Contains("disconnect") && swr.Elapsed < TimeSpan.FromSeconds(4));
            await Task.Delay(600);
            Check(m.Contains("\"type\":\"disconnect\"") && !web.ListScreens().Any(x => x.Id == "tvreload01"), "screens: disconnect tells the page, then closes it");

            control.View = "lobby42";
            using var noCode = new ClientWebSocket();
            await noCode.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=nocode0001"), CancellationToken.None);
            var refused = await Receive(noCode);
            using var withCode = new ClientWebSocket();
            await withCode.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=withcode01&view=lobby42"), CancellationToken.None);
            var allowed = await Receive(withCode);
            using var crew = new ClientWebSocket();
            await crew.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=crewlink01&key=" + CrewKeys.For("test-secret", "Lighting")), CancellationToken.None);
            var crewFrame = await Receive(crew);
            var stateNoCode = await http.GetAsync("/api/state");
            var stateCode = await http.GetAsync("/api/state?view=lobby42");
            Check(refused.Contains("\"view\":true") && allowed.Contains("\"type\":\"state\"") && crewFrame.Contains("\"type\":\"state\"")
                  && stateNoCode.StatusCode == HttpStatusCode.Unauthorized && stateCode.IsSuccessStatusCode,
                  "viewing code: screens need it (crew links and the PIN pass)");
            control.View = null;
        }

        // Department notes: a department's crew link reads and edits its own notes, not another's.
        {
            var lightKey = CrewKeys.For("test-secret", "Lighting");
            var mine = await http.GetAsync("/api/deptnotes?dept=Lighting&key=" + lightKey);
            var mineBody = await mine.Content.ReadAsStringAsync();
            var other = await http.GetAsync("/api/deptnotes?dept=Sound&key=" + lightKey);
            var withPin = await SendWithPin(http, "/api/deptnotes?dept=Sound");
            Check(mine.IsSuccessStatusCode && mineBody.Contains("Blue wash") && other.StatusCode == HttpStatusCode.Forbidden && withPin.IsSuccessStatusCode,
                  "dept notes: read with the department's own crew link (or the PIN)");
            var n0 = control.Commands.Count;
            var editOwn = await http.GetAsync("/api/deptnote/1/Lighting/" + Uri.EscapeDataString("Red wash") + "?key=" + lightKey);
            var editOther = await http.GetAsync("/api/deptnote/1/Sound/" + Uri.EscapeDataString("Mute all") + "?key=" + lightKey);
            Check(editOwn.IsSuccessStatusCode && editOther.StatusCode == HttpStatusCode.Forbidden && control.Commands.Count == n0 + 1
                  && control.Commands.Last() is { Kind: ShowCommandKind.DeptNote, Text: "Lighting\u0001Red wash" }, "dept notes: a department edits only its own");
        }

        // Presenter PCs: feeds need the PIN; the script is asked for once and checked against its hash.
        {
            var deck = new Stagewatch.Integrations.DeckState("Keynote.pptx", 2, 9, "Agenda", "", Array.Empty<double>(), DateTime.UtcNow, DateTime.UtcNow, "The script");
            var feed = Stagewatch.Integrations.SlidesAgent.BuildFeed(deck, "pctest0001", "Lectern", 1, DateTime.UtcNow);
            var slNoPin = await http.PostAsync("/api/slides/feed", new ByteArrayContent(feed.ToJson()));
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/slides/feed") { Content = new ByteArrayContent(feed.ToJson()) };
            msg.Headers.Add("X-Stagewatch-Pin", "4321");
            var ok = await http.SendAsync(msg);
            var okBody = await ok.Content.ReadAsStringAsync();
            Check(slNoPin.StatusCode == HttpStatusCode.Unauthorized && ok.IsSuccessStatusCode && okBody.Contains("\"needScript\":true"), $"slides: feed needs the PIN, then asks for the script ({okBody})");
            var script = new HttpRequestMessage(HttpMethod.Post, "/api/slides/script?hash=" + feed.ScriptHash) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("The script")) };
            script.Headers.Add("X-Stagewatch-Pin", "4321");
            Check((await http.SendAsync(script)).IsSuccessStatusCode && control.Decks.Pick("auto", null, DateTime.UtcNow) is { Slide: 2, Script: "The script" },
                  "slides: the script arrives once and the deck is followed");
            var slJunk = new HttpRequestMessage(HttpMethod.Post, "/api/slides/feed") { Content = new StringContent("{\"hello\":1}") };
            slJunk.Headers.Add("X-Stagewatch-Pin", "4321");
            Check((await http.SendAsync(slJunk)).StatusCode == HttpStatusCode.BadRequest, "slides: slJunk is refused");

            // The real agent loop, end to end (PowerPoint isn't running here, so it reports "not presenting"). Its PowerPoint
            // sensor runs on a COM (STA) thread, which only Windows has.
            if (Bench.OnWindows("slides agent: the real agent loop (PowerPoint sensor on a COM thread)"))
            {
                using var agent = new Stagewatch.Integrations.SlidesAgent("pcagent001", () => "Test PC", () => new[] { new Stagewatch.Integrations.AgentTarget("127.0.0.1", port, "4321") });
                var target = new Stagewatch.Integrations.AgentTarget("127.0.0.1", port, "4321");
                var sw4 = System.Diagnostics.Stopwatch.StartNew();
                while (!agent.StatusOf(target).Ok && sw4.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(100);
                Check(agent.StatusOf(target).Ok && control.Decks.List().Any(p => p.Agent == "pcagent001" && p.Name == "Test PC" && p.Connected),
                      $"slides agent: reaches Stagewatch and is listed ({agent.StatusOf(target).Text})");
                using var wrongPin = new Stagewatch.Integrations.SlidesAgent("pcagent002", () => "Wrong", () => new[] { new Stagewatch.Integrations.AgentTarget("127.0.0.1", port, "0000") });
                var slBad = new Stagewatch.Integrations.AgentTarget("127.0.0.1", port, "0000");
                sw4.Restart();
                while (wrongPin.StatusOf(slBad).Text == "connecting…" && sw4.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(100);
                Check(!wrongPin.StatusOf(slBad).Ok && wrongPin.StatusOf(slBad).Text.Contains("PIN"), $"slides agent: a wrong PIN is said plainly ({wrongPin.StatusOf(slBad).Text})");

                // Review P2: a Stagewatch that restarted (and lost the script) gets it again when it asks.
                var restartDeck = new Stagewatch.Integrations.DeckState("Talk.pptx", 1, 5, "", "", Array.Empty<double>(), DateTime.UtcNow, DateTime.UtcNow, "Speech text");
                var scriptHash = SlideFeed.HashScript("Speech text");
                using var scripted = new Stagewatch.Integrations.SlidesAgent("pcagent003", () => "Script PC",
                    () => new[] { new Stagewatch.Integrations.AgentTarget("127.0.0.1", port, "4321") }, () => restartDeck, beaconPort: 19929);
                sw4.Restart();
                while (control.Decks.Pick("pcagent003", null, DateTime.UtcNow)?.Script != "Speech text" && sw4.Elapsed < TimeSpan.FromSeconds(6)) await Task.Delay(100);
                var firstOk = control.Decks.Pick("pcagent003", null, DateTime.UtcNow)?.Script == "Speech text";
                control.Decks.ForgetScripts();                                       // the receiving Stagewatch restarts
                sw4.Restart();
                while (control.Decks.Pick("pcagent003", null, DateTime.UtcNow)?.Script != "Speech text" && sw4.Elapsed < TimeSpan.FromSeconds(9)) await Task.Delay(200);
                Check(firstOk && control.Decks.Pick("pcagent003", null, DateTime.UtcNow)?.Script == "Speech text",
                      $"slides agent review P2: the script is sent again after Stagewatch restarts ({sw4.Elapsed.TotalSeconds:0.0} s)");
            }
        }

        // Screens: a browser reports its own id and page (never the PIN); the operator names it, finds it and moves it.
        {
            using var lobby = new ClientWebSocket();
            lobby.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Safari/604.1");
            await lobby.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=lobbytv01&page=" + Uri.EscapeDataString("/viewer?cue=0&pin=4321")), CancellationToken.None);
            await Receive(lobby);
            control.Names["lobbytv01"] = "Lobby TV";
            var seen = web.ListScreens().FirstOrDefault(x => x.Id == "lobbytv01");
            Check(seen is { Name: "Lobby TV", Page: "/viewer?cue=0", Device: "iPad · Safari", Control: false }, $"screens: listed with name, page (no PIN) and device ({seen})");
            Check(web.Redirect(new[] { "lobbytv01" }, "/audience?holding=0") == 1, "screens: redirect reaches the screen");
            string msg;
            var sw3 = System.Diagnostics.Stopwatch.StartNew();
            do { msg = await Receive(lobby); } while (!msg.Contains("redirect") && sw3.Elapsed < TimeSpan.FromSeconds(4));
            Check(msg.Contains("\"to\":\"/audience?holding=0\""), "screens: the screen is told where to go");
            Check(web.Redirect(new[] { "lobbytv01" }, "https://evil.example/") == 0 && web.Redirect(new[] { "lobbytv01" }, "//evil.example/x") == 0
                  && web.Redirect(new[] { "lobbytv01" }, "/viewer?pin=1") == 0, "screens: never sent to another site or with a PIN");
            web.Identify(new[] { "lobbytv01" });
            sw3.Restart();
            do { msg = await Receive(lobby); } while (!msg.Contains("identify") && sw3.Elapsed < TimeSpan.FromSeconds(4));
            Check(msg.Contains("\"name\":\"Lobby TV\""), "screens: identify shows the screen's name on it");
            var list = await http.GetAsync("/api/screens");
            var listPin = await SendWithPin(http, "/api/screens");
            var body = await listPin.Content.ReadAsStringAsync();
            Check(list.StatusCode == HttpStatusCode.Unauthorized && listPin.IsSuccessStatusCode && body.Contains("Lobby TV") && body.Contains("\"lobby\""),
                  "screens: the list (and saved setups) need the PIN");
            using var noFollow = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = http.BaseAddress };
            var p = await noFollow.GetAsync("/p/lobby");
            Check((int)p.StatusCode == 302 && p.Headers.Location?.OriginalString == "/audience?holding=0" && (await noFollow.GetAsync("/p/nothing")).StatusCode == HttpStatusCode.NotFound,
                  "screen setups: /p/name opens the saved page");
        }

        var links = JsonDocument.Parse(await (await SendWithPin(http, "/api/crew-links")).Content.ReadAsStringAsync()).RootElement;
        Check(links.GetProperty("required").GetBoolean() && links.GetProperty("links").EnumerateArray().Any(l => l.GetProperty("path").GetString() == "/dept?name=Lighting&key=" + CrewKeys.For("test-secret", "Lighting")),
              "crew links listed with their keys (PIN)");
        Check((await http.GetAsync("/api/crew-links")).StatusCode == HttpStatusCode.Unauthorized, "crew links need the PIN");

        var qr = await http.GetAsync("/api/qr?text=" + Uri.EscapeDataString("http://10.0.0.5:9924/ask"));
        var svg = await qr.Content.ReadAsStringAsync();
        Check(qr.IsSuccessStatusCode && qr.Content.Headers.ContentType!.MediaType == "image/svg+xml" && svg.Contains("<svg"), "web: QR as SVG");
        Check((await http.GetAsync("/api/qr")).StatusCode == HttpStatusCode.BadRequest, "web: QR needs text");

        var report = await http.GetStringAsync("/report?pin=4321");
        var csv = await http.GetStringAsync("/api/report.csv?pin=4321");
        Check(report.Contains("<h1>Summit</h1>") && csv.StartsWith("#,Title"), "web: report HTML + CSV");

        // MCP (JSON-RPC over POST).
        async Task<JsonElement> Mcp(string json, string? pin = "4321")
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (pin is not null) msg.Headers.Add("Authorization", "Bearer " + pin);
            var r = await http.SendAsync(msg);
            var text = await r.Content.ReadAsStringAsync();
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
        }
        var noAuth = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        Check((await http.SendAsync(noAuth)).StatusCode == HttpStatusCode.Unauthorized, "mcp: PIN required");
        control.Mcp = false;
        var unlicensed = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        unlicensed.Headers.Add("X-Stagewatch-Pin", "4321");
        Check((await http.SendAsync(unlicensed)).StatusCode == HttpStatusCode.Forbidden, "mcp: off / not Production → 403");
        control.Mcp = true;
        var init = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}");
        Check(init.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString() == "stagewatch", "mcp: initialize");
        var noteReply = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", Encoding.UTF8, "application/json") };
        noteReply.Headers.Add("X-Stagewatch-Pin", "4321");
        Check((await http.SendAsync(noteReply)).StatusCode == HttpStatusCode.Accepted, "mcp: notification → 202");
        var tools = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
        var names = tools.GetProperty("result").GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Check(names.SequenceEqual(new[] { "get_state", "get_running_order", "run_command", "get_report", "get_proposal", "get_context" }), "mcp: tools/list (with a proposal's fate and the room's context)");
        var call = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"run_command\",\"arguments\":{\"command\":\"signal message speaker \\\"Wrap up\\\"\"}}}");
        Check(call.GetProperty("result").GetProperty("isError").GetBoolean() == false && control.Commands.Last() is { Kind: ShowCommandKind.Signal, Text: "speaker\u0001Wrap up" }, "mcp: run_command goes through the grammar");
        // Human in the loop: with approval on, an agent's command is queued for the operator and does not run.
        control.AgentApproval = true;
        var mcpBefore = control.Commands.Count;
        var queued = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"run_command\",\"arguments\":{\"command\":\"delay 300\",\"reason\":\"keynote overran\"}}}");
        var queuedText = queued.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        Check(queuedText.Contains("approval") && control.Commands.Count == mcpBefore && control.Proposed.SequenceEqual(new[] { "delay 300 | keynote overran" }),
              "mcp: with approval on, an agent proposes and nothing runs");
        control.AgentApproval = false;
        var bad = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"run_command\",\"arguments\":{\"command\":\"frobnicate\"}}}");
        Check(bad.GetProperty("result").GetProperty("isError").GetBoolean(), "mcp: bad command is a tool error");
        var ro = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"get_running_order\",\"arguments\":{}}}");
        Check(ro.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!.Contains("\"title\":\"Keynote\""), "mcp: get_running_order");
        var unknown = await Mcp("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"nope\"}");
        Check(unknown.GetProperty("error").GetProperty("code").GetInt32() == -32601, "mcp: unknown method");

        // Holding images: PIN-protected upload (bigger than the 64 KB request cap), then served cacheably.
        var big = new byte[300_000];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(big, 0);
        var noPin = await http.PostAsync("/api/holding/upload?name=Walk-in", new ByteArrayContent(big));
        Check(noPin.StatusCode == HttpStatusCode.Unauthorized, "holding upload: PIN required");
        var up = new HttpRequestMessage(HttpMethod.Post, "/api/holding/upload?name=Walk-in") { Content = new ByteArrayContent(big) };
        up.Headers.Add("X-Stagewatch-Pin", "4321");
        var upReply = await http.SendAsync(up);
        var upJson = JsonDocument.Parse(await upReply.Content.ReadAsStringAsync()).RootElement;
        var newId = upJson.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        Check(upReply.StatusCode == HttpStatusCode.OK && newId is not null && control.Uploads[newId].Name == "Walk-in" && control.Uploads[newId].Data.Length == big.Length,
              "holding upload: 300 KB image stored intact");
        // A deferred path keeps its own cap: 25 MB, over the 1 MiB an oversize body is drained to, is read whole by the upload's own route.
        var large = new byte[25_000_000];
        big.AsSpan(0, 8).CopyTo(large);
        var upLarge = new HttpRequestMessage(HttpMethod.Post, "/api/holding/upload?name=Large") { Content = new ByteArrayContent(large) };
        upLarge.Headers.Add("X-Stagewatch-Pin", "4321");
        var largeReply = await http.SendAsync(upLarge);
        var largeId = JsonDocument.Parse(await largeReply.Content.ReadAsStringAsync()).RootElement.TryGetProperty("id", out var largeEl) ? largeEl.GetString() : null;
        Check(largeReply.StatusCode == HttpStatusCode.OK && largeId is not null && control.Uploads[largeId].Data.Length == large.Length,
              $"holding upload: a 25 MB image is read whole by the upload's own path, never refused as an oversize body ({largeReply.StatusCode})");
        if (largeId is not null) control.Uploads.TryRemove(largeId, out _);
        var junk = new HttpRequestMessage(HttpMethod.Post, "/api/holding/upload?name=x") { Content = new ByteArrayContent("hello"u8.ToArray()) };
        junk.Headers.Add("X-Stagewatch-Pin", "4321");
        Check((await http.SendAsync(junk)).StatusCode == HttpStatusCode.BadRequest, "holding upload: non-image refused");
        var got = await http.GetAsync("/api/holding/" + newId);
        Check(got.StatusCode == HttpStatusCode.OK && (await got.Content.ReadAsByteArrayAsync()).Length == big.Length
              && got.Headers.CacheControl?.MaxAge > TimeSpan.FromDays(300) && got.Content.Headers.ContentType?.MediaType == "image/png",
              "holding image: served with a long cache");
        Check((await http.GetAsync("/api/holding/000000000000")).StatusCode == HttpStatusCode.NotFound
              && (await http.GetAsync("/api/holding/..%2F..%2Fsettings")).StatusCode == HttpStatusCode.NotFound, "holding image: unknown / bad ids 404");
        var holdPage = await http.GetStringAsync("/holding");
        Check(holdPage.Contains("holding.js") && (await http.GetStringAsync("/holding.js")).Contains("SW.holding"), "holding page served");
        Check((await http.GetStringAsync("/holo")).Contains("holo.bundle.js") && (await http.GetByteArrayAsync("/holo.bundle.js")).Length > 200_000
              && (await http.GetStringAsync("/holo-page.js")).Contains("chrome.webview"), "holo page and three.js bundle served");

        await NodeRoutes(Check);
        await OversizeBodies(Check);
        await NodeLinksOnWire(Check);
        GuessCounts(Check);
        await ViewPassCounts(Check);
        await LockoutTablesRead(Check);
    }

    /// <summary>
    /// The lockout tables read on a real server (WebServer.AuthFailures, ViewPassFailures): what the rig's <c>auth</c> line and the
    /// interop lane read after each row. Three wrong PINs from 127.0.0.1 are one row with 3 in the PIN's table and none in the view
    /// passes'; the right PIN empties the PIN's table. A wrong view pass is a row of the view passes' own, which the right PIN
    /// leaves standing. What guesses nothing (no PIN, an unknown key) is in neither.
    /// </summary>
    static async Task LockoutTablesRead(Action<bool, string> Check)
    {
        var control = new ReportControl();
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var unknownKey = ApiKeys.Create("never saved", ApiKeyScope.Read, DateTime.UtcNow).Secret;
        using (var keyed = new HttpRequestMessage(HttpMethod.Get, "/api/screens"))
        {
            keyed.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", unknownKey);
            await http.SendAsync(keyed);
        }
        await http.GetAsync("/api/start");
        var nothing = web.AuthFailures().Count == 0 && web.ViewPassFailures().Count == 0;

        for (var i = 0; i < 3; i++) await http.GetAsync("/api/start?pin=0000");
        var three = web.AuthFailures();
        var passes = web.ViewPassFailures();
        Check(nothing && three.Count == 1 && three[0].Address == "127.0.0.1" && three[0].Failures == 3 && three[0].WindowAgeMs is >= 0 and < 60_000 && passes.Count == 0,
              $"lockout tables: no PIN and an unknown key count nothing; 3 wrong PINs are one row, 127.0.0.1 with 3, in the PIN's table alone ({string.Join(", ", three)})");

        var right = await http.GetAsync("/api/start?pin=4321");
        Check(right.IsSuccessStatusCode && web.AuthFailures().Count == 0, $"lockout tables: the right PIN empties the PIN's table ({right.StatusCode})");

        await http.GetAsync("/w/" + ScreenWindows.NewToken());
        await http.GetAsync("/api/start?pin=4321");
        var view = web.ViewPassFailures();
        Check(view.Count == 1 && view[0] is { Address: "127.0.0.1", Failures: 1 } && web.AuthFailures().Count == 0,
              $"lockout tables: a wrong window address is a row of the view passes' own, which the right PIN leaves standing ({string.Join(", ", view)})");
    }

    /// <summary>
    /// One kind of wrong guess per device, on an injected clock: the limit within a device's minute locks it out until that minute
    /// ends, one below does not; devices count apart; only the minute's end or Clear resets one; the count stops at the limit; and
    /// devices whose minute ended are dropped once more than SweepAbove are held, at most once a minute, changing no outcome.
    /// </summary>
    static void GuessCounts(Action<bool, string> Check)
    {
        var a = IPAddress.Parse("10.0.0.5");
        var b = IPAddress.Parse("10.0.0.6");
        var t = new WrongGuesses(5);
        for (var i = 0; i < 4; i++) t.Count(a, 1_000 + i);
        var below = t.Locked(a, 1_010);
        t.Count(a, 30_000);
        Check(!below && t.Locked(a, 30_000) && t.Locked(a, 60_999) && !t.Locked(a, 61_000) && t.Of(a, 61_000) == 0 && !t.Locked(b, 30_000),
              "guess counts: four wrong guesses in a device's minute lock nothing, the fifth locks it until the minute that began with its first one ends, and not another device");
        for (var i = 0; i < 9; i++) t.Count(b, 2_000);
        t.Count(a, 61_000);
        Check(t.Of(b, 2_000) == 5 && t.Of(a, 61_000) == 1 && !t.Locked(a, 61_000),
              $"guess counts: the count stops at the limit, and the first guess after a minute ended starts a new one ({t.Of(b, 2_000)}, {t.Of(a, 61_000)})");
        t.Clear(b);
        Check(!t.Locked(b, 2_001) && t.Of(b, 2_001) == 0, "guess counts: only its owner's Clear resets a device before its minute ends");

        // The table as it stands: only open minutes, each with its count and its minute's age, the oldest first; a read changes nothing.
        var r = new WrongGuesses(5);
        var c = IPAddress.Parse("10.0.0.7");
        r.Count(c, 5_000);
        r.Count(a, 10_000);
        r.Count(a, 11_000);
        r.Count(b, 10_000);
        var open = r.Open(20_000);
        var again = r.Open(20_000);
        Check(open.SequenceEqual(new[] { (c, 1, 15_000L), (a, 2, 10_000L), (b, 1, 10_000L) }) && again.SequenceEqual(open) && r.Of(a, 20_000) == 2,
              $"guess counts: the open table lists each device with its count and its minute's age, oldest first, then by address; reading it twice reads the same ({string.Join(", ", open)})");
        Check(r.Open(65_000).SequenceEqual(new[] { (a, 2, 55_000L), (b, 1, 55_000L) }) && r.Open(70_000).Count == 0 && r.Held == 3,
              "guess counts: a device whose minute has ended is absent from the open table, as from Of, though still held until a sweep");
        r.Clear(a);
        Check(r.Open(20_000).Select(d => d.Device).SequenceEqual(new[] { c, b }), "guess counts: a cleared device leaves the open table");

        // The sweep: past SweepAbove devices it drops those whose minute ended, at most once a minute.
        var many = new WrongGuesses(5);
        static IPAddress Device(uint n) => new(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(0x0A000000u + n));
        var first = WrongGuesses.SweepAbove + 1;
        for (var i = 0u; i < first; i++) many.Count(Device(i), 0);               // a sweep at 0 drops nothing: no minute has ended
        var atZero = many.Held;
        for (var i = 0u; i < first; i++) many.Count(Device(100_000 + i), 1);     // no sweep: the next is due at 60 000
        var late = Device(200_000);
        for (var i = 0; i < 5; i++) many.Count(late, 1);
        many.Count(Device(300_000), 60_000);                                       // the minutes that began at 0 have ended: dropped
        var swept = many.Held;
        many.Count(Device(300_001), 60_001);                                       // those that began at 1 have ended too: kept to the next
        Check(atZero == first && swept == first + 2 && many.Held == first + 3 && many.Locked(late, 60_000) && !many.Locked(late, 60_001)
              && many.Of(Device(100_000), 60_001) == 0,
              $"guess counts: past SweepAbove devices, those whose minute ended are dropped, none sooner and at most once a minute, and every answer is as before ({atZero}, {swept}, {many.Held})");
    }

    /// <summary>
    /// Show Bus 12.2 on a real server: wrong view passes — window addresses, viewing codes and crew links — count per device in a
    /// count of their own, WebServer.WrongViewPassesPerMinute a minute locking the device out of every view pass for the minute.
    /// What guesses nothing counts nothing: no key, another page's crew link, no code, a stale crew link beside the right code.
    /// A right PIN or key never clears the count, and the PIN and a key still open everything they open; wrong PINs lock out the
    /// PIN and keys alone. A socket's crew link counts once however many cues it tries, an HTTP request's each time. Every
    /// request here comes from 127.0.0.1 within a minute, so each table is read by counts on a fresh server.
    /// </summary>
    static async Task ViewPassCounts(Action<bool, string> Check)
    {
        var lightKey = CrewKeys.For("test-secret", "Lighting");
        var soundKey = CrewKeys.For("test-secret", "Sound");
        const string notACrewLink = "0123456789";
        Check(!CrewKeys.IsAnyCrewKey("test-secret", ["Lighting", "Sound"], notACrewLink), "view passes: the test's stranger key is no crew link");
        var lobby = new ScreenWindow { Id = "wk3m9x2", Name = "lobby", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 1 };
        var (readKey, readSecret) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, DateTime.UtcNow);

        async Task<(WebServer Web, HttpClient Http, ReportControl Control, int Port)> Fresh()
        {
            var control = new ReportControl { View = "lobby42", WindowList = [lobby], Keys = [readKey] };
            control.Snapshots.Publish(new ShowSnapshot
            {
                Text = "05:00", Seconds = 300, Title = "Keynote",
                Signals = new[] { new SignalView(5, SignalKind.Go, "Lighting", "Q12", SignalState.Sent, false, 2, "") }.ToEquatableArray(),
            });
            var port = FreePort();
            var web = new WebServer(control, () => "4321");
            web.Start(port, bindAll: false);
            await Task.Yield();
            return (web, new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") }, control, port);
        }
        static async Task<(HttpStatusCode Code, string? Error)> Get(HttpClient http, string path, string? bearer = null)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            if (bearer is not null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            string? error = null;
            if (body.StartsWith('{'))
            {
                using var d = JsonDocument.Parse(body);
                if (d.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
            }
            return (res.StatusCode, error);
        }
        static async Task<string> Admitted(int port, string query)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?" + query), CancellationToken.None);
            return await Receive(ws);
        }
        // Whether the link's first frame is the server's close, with nothing before it: a link let in hears its window first.
        static async Task<bool> ClosedFirst(int port, string query)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?" + query), CancellationToken.None);
            using var cts = new CancellationTokenSource(3000);
            var r = await ws.ReceiveAsync(new byte[65536], cts.Token);
            return r.MessageType == WebSocketMessageType.Close && r.Count == 0;
        }
        static string? ViewError(string message)
        {
            if (!message.StartsWith('{')) return null;
            using var d = JsonDocument.Parse(message);
            return d.RootElement.TryGetProperty("view", out var v) && v.GetBoolean() && d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        var unknownKey = ApiKeys.Create("never saved", ApiKeyScope.Read, DateTime.UtcNow).Secret;

        // 1. Wrong view passes of every kind lock the device out of view passes, and only of view passes.
        {
            var (web, http, control, port) = await Fresh();
            await using var _ = web;
            using var __ = http;
            // What guesses nothing counts nothing: 12 of each.
            for (var i = 0; i < 12; i++)
            {
                await Get(http, "/api/ack/5/seen");                                           // no key
                await Get(http, "/api/ack/5/seen?key=" + soundKey);                           // another page's crew link
                await Get(http, "/api/deptnotes?dept=Lighting&key=" + soundKey);
                await Get(http, "/api/config");                                               // no code
                await Get(http, "/api/config?view=lobby42&key=" + notACrewLink);              // a stale crew link beside the right code
                await Get(http, "/api/screens", unknownKey);                                  // an unknown key
                await Get(http, "/api/start");                                                // no PIN
            }
            var stillOpen = (await http.GetAsync("/w/" + lobby.Token)).StatusCode == HttpStatusCode.OK
                            && (await Get(http, "/api/config?view=lobby42")).Code == HttpStatusCode.OK
                            && (await Get(http, "/api/ack/5/seen?key=" + lightKey)).Code == HttpStatusCode.OK;
            Check(stillOpen, "view passes: no key, another page's crew link, no code, a stale crew link beside the right code, an unknown key and no PIN count nothing (12 of each)");

            // Five wrong view passes of three kinds: two window addresses, two codes, one key that is no crew link.
            await http.GetAsync("/w/" + ScreenWindows.NewToken());
            await http.GetAsync("/w/" + ScreenWindows.NewToken());
            await Get(http, "/api/config?view=nope1");
            await Get(http, "/api/config?view=nope2");
            var commands = control.Commands.Count;
            var stranger = await Get(http, "/api/ack/5/seen?key=" + notACrewLink);
            var window = await http.GetAsync("/w/" + lobby.Token);
            var code = await Get(http, "/api/config?view=lobby42");
            var crewAck = await Get(http, "/api/ack/5/seen?key=" + lightKey);
            var crewNotes = await Get(http, "/api/deptnotes?dept=Lighting&key=" + lightKey);
            var crewDoor = await Get(http, "/api/config?key=" + lightKey);
            var viewer = ViewError(await Admitted(port, "cid=viewer0001&view=lobby42"));
            var crewViewer = ViewError(await Admitted(port, "cid=viewer0002&key=" + lightKey));
            var windowLinkClosed = await ClosedFirst(port, "win=" + lobby.Token + "&detail=1");
            Check(stranger.Code == HttpStatusCode.Forbidden && window.StatusCode == (HttpStatusCode)429
                  && code == (HttpStatusCode.Unauthorized, WebServer.PassesLocked) && crewAck == (HttpStatusCode.Forbidden, WebServer.PassesLocked)
                  && crewNotes == (HttpStatusCode.Forbidden, WebServer.PassesLocked) && control.Commands.Count == commands
                  && crewDoor == (HttpStatusCode.Unauthorized, WebServer.PassesLocked) && viewer == WebServer.PassesLocked && crewViewer == WebServer.PassesLocked
                  && windowLinkClosed,
                  $"view passes: {WebServer.WrongViewPassesPerMinute} wrong window addresses, codes and crew links lock the device out of every view pass: a right window address (429), "
                  + "code, crew link (at the view door, on a socket, on a cue and on notes) and a viewer's socket are refused with the lockout's words, and a window's link is "
                  + $"closed before a word, never told it is gone ({window.StatusCode}, {code.Error}, {crewDoor.Error}, {crewAck.Error}, {viewer}, {crewViewer}, closed {windowLinkClosed})");

            // Apart: the PIN and a key still open what they open, and neither clears the view passes' count.
            var pinState = await Get(http, "/api/state?detail=1&pin=4321");
            var pinConfig = await Get(http, "/api/config?pin=4321");
            var keyRead = await Get(http, "/api/screens", readSecret);
            var operatorSocket = await Admitted(port, "role=control&pin=4321");
            var stillLocked = (await http.GetAsync("/w/" + lobby.Token)).StatusCode == (HttpStatusCode)429
                              && await Get(http, "/api/config?view=lobby42") == (HttpStatusCode.Unauthorized, WebServer.PassesLocked);
            Check(pinState.Code == HttpStatusCode.OK && pinConfig.Code == HttpStatusCode.OK && keyRead.Code == HttpStatusCode.OK
                  && operatorSocket.Contains("\"ok\":true", StringComparison.Ordinal) && stillLocked,
                  $"view passes: a device locked out of view passes keeps the PIN and its keys, and their right answers never clear the count ({pinConfig.Code}, {keyRead.Code})");
        }

        // 2. Wrong PINs lock out the PIN and keys alone; one below the limit of wrong view passes locks nothing, a right one clears nothing.
        {
            var (web, http, _, _) = await Fresh();
            await using var _w = web;
            using var _h = http;
            for (var i = 0; i < WebServer.WrongPinsPerMinute; i++) await Get(http, "/api/start?pin=0000");
            var pin = await Get(http, "/api/start?pin=4321");
            var key = await Get(http, "/api/screens", readSecret);
            var passesOpen = (await http.GetAsync("/w/" + lobby.Token)).StatusCode == HttpStatusCode.OK
                             && (await Get(http, "/api/config?view=lobby42")).Code == HttpStatusCode.OK
                             && (await Get(http, "/api/ack/5/seen?key=" + lightKey)).Code == HttpStatusCode.OK;
            Check(pin == (HttpStatusCode.Unauthorized, WebServer.PinsLocked) && key == (HttpStatusCode.Unauthorized, WebServer.PinsLocked) && passesOpen,
                  $"view passes: {WebServer.WrongPinsPerMinute} wrong PINs lock out the PIN and keys, and no view pass ({pin.Error}, {key.Error})");
            for (var i = 0; i < WebServer.WrongViewPassesPerMinute - 1; i++) await Get(http, "/api/config?view=nope" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var below = (await Get(http, "/api/config?view=lobby42")).Code == HttpStatusCode.OK && (await http.GetAsync("/w/" + lobby.Token)).StatusCode == HttpStatusCode.OK;
            await Get(http, "/api/config?view=nope");
            var at = await Get(http, "/api/config?view=lobby42");
            Check(below && at == (HttpStatusCode.Unauthorized, WebServer.PassesLocked),
                  $"view passes: one below the limit locks nothing and the right code between clears nothing; the limit's last one locks ({at.Error})");
        }

        // 3. A socket gives its crew link once: a key that is no crew link counts once however many cues it tries; HTTP each time.
        {
            var (web, http, control, port) = await Fresh();
            await using var _w = web;
            using var _h = http;
            control.View = null;
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=strangr001&key=" + notACrewLink), CancellationToken.None);
            await Receive(ws);
            // The refusal is JSON, its apostrophe escaped: match the words after it.
            var refusals = 0;
            for (var i = 0; i < 6; i++)
            {
                await ws.SendAsync(Encoding.UTF8.GetBytes("{\"cmd\":\"ack\",\"args\":[\"5\",\"seen\"]}"), WebSocketMessageType.Text, true, CancellationToken.None);
                for (var tries = 0; tries < 6; tries++)
                    if ((await Receive(ws)).Contains("confirm cues for Lighting", StringComparison.Ordinal)) { refusals++; break; }
            }
            for (var i = 0; i < WebServer.WrongViewPassesPerMinute - 2; i++) await Get(http, "/api/ack/5/seen?key=" + notACrewLink);
            var open = (await http.GetAsync("/w/" + lobby.Token)).StatusCode;
            var last = await Get(http, "/api/ack/5/seen?key=" + notACrewLink);
            var locked = (await http.GetAsync("/w/" + lobby.Token)).StatusCode;
            Check(refusals == 6 && open == HttpStatusCode.OK && last.Code == HttpStatusCode.Forbidden && locked == (HttpStatusCode)429,
                  $"view passes: a socket's stranger key counts once for its 6 refused confirmations, an HTTP request's each time ({refusals} refused, {open}, then {locked})");
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }

        // 4. At the view door, with a viewing code set and none given, a key that is no crew link counts: once per HTTP request
        //    and once for a socket's upgrade. One fewer than the limit leaves the right code open; the limit's last locks it.
        {
            var (web, http, _, port) = await Fresh();
            await using var _w = web;
            using var _h = http;
            var refused = 0;
            for (var i = 0; i < WebServer.WrongViewPassesPerMinute - 1; i++)
                if (await Get(http, "/api/config?key=" + notACrewLink) == (HttpStatusCode.Unauthorized, "This screen needs the viewing code")) refused++;
            var below = await Get(http, "/api/config?view=lobby42");
            var upgrade = ViewError(await Admitted(port, "cid=strangr002&key=" + notACrewLink));
            var at = await Get(http, "/api/config?view=lobby42");
            Check(refused == WebServer.WrongViewPassesPerMinute - 1 && below.Code == HttpStatusCode.OK && upgrade == "This screen needs the viewing code"
                  && at == (HttpStatusCode.Unauthorized, WebServer.PassesLocked),
                  $"view passes: at the view door a key that is no crew link counts, by request and by a socket's upgrade: one fewer than the limit leaves the code open, the limit's last locks it ({refused} refused, {below.Code}, {upgrade}, {at.Error})");
        }
    }

    /// <summary>
    /// The node routes on a real server, with a fresh lockout table (every request here comes from 127.0.0.1 within one
    /// minute, so the lockout is read by counts, not clocks). One node key speaks for one node, checked before anything is
    /// stored; the lockout counts guesses only: wrong PINs lock an address out, an unknown or revoked key and a missing
    /// PIN never do, so an app polling or redialling with a revoked key, or with none, never locks its machine out.
    /// </summary>
    static async Task NodeRoutes(Action<bool, string> Check)
    {
        var control = new ShowBusTests.BusControl();
        var (patKey, patSecret) = ApiKeys.Create("Patterns FOH", ApiKeyScope.Node, DateTime.UtcNow);
        var (agKey, agSecret) = ApiKeys.Create("AGMessage", ApiKeyScope.Node, DateTime.UtcNow);
        control.ApiKeys = new[] { patKey, agKey };
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300, Title = "Keynote" });
        async Task<(HttpStatusCode Code, string? Error, string Body)> Send(HttpMethod m, string path, string? secret, string? json = null)
        {
            using var req = new HttpRequestMessage(m, path);
            if (secret is not null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
            if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            string? error = null;
            if (body.StartsWith('{'))
            {
                using var d = JsonDocument.Parse(body);
                if (d.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
            }
            return (res.StatusCode, error, body);
        }
        static string Ctx(string id, string headline) => "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"" + id + "\",\"name\":\"FOH\"},\"light\":\"green\",\"headline\":\"" + headline + "\"}";

        // One node, one key: a forged context answers 403 and stores nothing; a key pasted into a second install is refused there.
        var own = await Send(HttpMethod.Post, "/api/peers/context", patSecret, Ctx("pat1", "FOH fine"));
        Check(own is (HttpStatusCode.OK, null, "{\"ok\":true,\"verified\":\"key\"}") && control.Nodes.NodeOf(patKey.Id) == ("patterns", "pat1"),
              $"node routes: a node key's first context binds it to its node, held as verified by key ({own.Body})");
        var forged = await Send(HttpMethod.Post, "/api/peers/context", agSecret, Ctx("pat1", "forged"));
        Check(forged is (HttpStatusCode.Forbidden, "patterns pat1 speaks with its own key", _) && control.Peers.Find("pat1", DateTime.UtcNow) is { Headline: "FOH fine", Proof: NodeProof.Key }
              && control.Nodes.NodeOf(agKey.Id) is null,
              $"node routes: a forged context for a node with its own key answers 403 with words and stores nothing; the forging key binds to nothing ({forged.Error})");
        var twin = await Send(HttpMethod.Post, "/api/peers/context", patSecret, Ctx("pat2", "the twin"));
        Check(twin is (HttpStatusCode.Forbidden, "this key speaks for patterns pat1 — make a key for patterns pat2", _) && control.Peers.Find("pat2", DateTime.UtcNow) is null
              && control.Nodes.NodeOf(patKey.Id) == ("patterns", "pat1"),
              $"node routes: one key shared by two installs is refused for the second id, nothing stored, and still speaks for the first ({twin.Error})");

        // A body that repeats a key is not read two ways (or thrown on): the server refuses it with words before the store.
        var asked = control.Credentials.Count;
        var queued = control.Desk.All().Count;
        var twoVs = await Send(HttpMethod.Post, "/api/peers/context", patSecret, "{\"v\":1,\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"light\":\"red\",\"headline\":\"repeated\"}");
        var twoNodeIds = await Send(HttpMethod.Post, "/api/peers/context", agSecret, "{\"v\":1,\"node\":{\"t\":\"messenger\",\"id\":\"ag1\",\"id\":\"pat1\"},\"light\":\"red\",\"headline\":\"repeated\"}");
        var twoFroms = await Send(HttpMethod.Post, "/api/proposals", agSecret,
            "{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}");
        Check(twoVs is (HttpStatusCode.BadRequest, PeerContexts.NotAContext, _) && twoNodeIds is (HttpStatusCode.BadRequest, PeerContexts.NotAContext, _)
              && twoFroms is (HttpStatusCode.BadRequest, ProposalDesk.NotAProposal, _)
              && control.Credentials.Count == asked && control.Desk.All().Count == queued && control.Nodes.NodeOf(agKey.Id) is null
              && control.Peers.Find("pat1", DateTime.UtcNow) is { Headline: "FOH fine" } && control.Peers.Find("ag1", DateTime.UtcNow) is null,
              $"node routes: a context or proposal that repeats a key answers 400 with words; the store is never asked, nothing is stored or bound ({twoVs.Error} / {twoNodeIds.Error} / {twoFroms.Error})");

        // Unknown and revoked keys, and requests with no PIN, are never counted toward the address's lockout.
        control.ApiKeys = new[] { agKey };   // Patterns FOH's key revoked
        var revoked = new List<(HttpStatusCode, string?)>();
        for (var i = 0; i < 12; i++)
        {
            var r = await Send(HttpMethod.Post, "/api/peers/context", patSecret, Ctx("pat1", "still here"));
            revoked.Add((r.Code, r.Error));
        }
        Check(revoked.All(r => r == (HttpStatusCode.Unauthorized, "unknown or revoked API key")) && control.Peers.Find("pat1", DateTime.UtcNow) is { Headline: "FOH fine" },
              "node routes: a revoked key is refused (401, the same words every time) and stores nothing");
        var unknownSecret = ApiKeys.Create("never saved", ApiKeyScope.Node, DateTime.UtcNow).Secret;
        var framed = 0;
        var told = 0;
        for (var i = 0; i < 12; i++)
        {
            using var ws = new ClientWebSocket();
            ws.Options.SetRequestHeader("Authorization", "Bearer " + unknownSecret);
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?cid=legacy01&page=/patterns"), CancellationToken.None);
            if ((await Receive(ws)) == "{\"type\":\"auth\",\"ok\":false,\"error\":\"unknown or revoked API key\",\"retry\":60}") told++;
            if ((await Receive(ws)).Contains("\"seq\"")) framed++;
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "redial", CancellationToken.None);
        }
        Check(told == 12 && framed == 12,
              $"node routes: a socket redialled with an unknown key hears one refusal, then still follows the timer ({told} told, {framed} of 12 heard a frame)");
        var bare = new List<(HttpStatusCode, string?)>();
        for (var i = 0; i < 12; i++)
        {
            var r = await Send(HttpMethod.Get, "/api/context", null);
            bare.Add((r.Code, r.Error));
        }
        Check(bare.All(r => r == (HttpStatusCode.Unauthorized, "PIN required")), "node routes: a keyless app's context read is refused, PIN required");
        Check((await Send(HttpMethod.Get, "/api/context?pin=4321", null)).Code == HttpStatusCode.OK,
              "node routes: after 12 revoked-key posts, 12 unknown-key redials and 12 reads with no PIN, the PIN still opens this machine's address");
        using (var control2 = new ClientWebSocket())
        {
            await control2.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control&pin=4321"), CancellationToken.None);
            var auth = await Receive(control2);
            Check(auth.Contains("\"type\":\"auth\"") && auth.Contains("\"ok\":true"), $"node routes: and a control page on it still signs in ({auth})");
            await control2.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        Check((await Send(HttpMethod.Get, "/api/context", agSecret)).Code == HttpStatusCode.OK, "node routes: and a key in force still reads");

        // Wrong PINs are guesses, and still count: five lock the address out for the minute, a valid key included.
        for (var i = 0; i < 5; i++) await Send(HttpMethod.Get, "/api/context?pin=0000", null);
        var lockedPin = await Send(HttpMethod.Get, "/api/context?pin=4321", null);
        var lockedKey = await Send(HttpMethod.Get, "/api/context", agSecret);
        Check(lockedPin is (HttpStatusCode.Unauthorized, "too many wrong PINs — wait a minute", _) && lockedKey is (HttpStatusCode.Unauthorized, "too many wrong PINs — wait a minute", _),
              $"node routes: five wrong PINs lock the address out, the right PIN and a valid key too ({lockedPin.Error} / {lockedKey.Error})");
    }

    /// <summary>
    /// A request body over 64 KiB, on a real server and real sockets (Show Bus §6: a desk answers a context over context.bytes with
    /// 413 and its size). The sender reads the 413 and its words, the size it declared among them, not a reset: the server drains a
    /// body of up to 1 MiB first (waiting for it while it has not come), and answers one declared longer at once, undrained. Nothing
    /// of the body reaches a route: no context is stored, no key bound, no credential classed, and a wrong PIN on it is no guess. The
    /// next request is read as before, a body of exactly 64 KiB taken; the deferred paths keep their own caps and refusals.
    /// </summary>
    static async Task OversizeBodies(Action<bool, string> Check)
    {
        var request = typeof(WebServer).Assembly.GetType("Stagewatch.Remote.HttpRequest");
        var maxBody = request?.GetField("MaxBody")?.GetValue(null) as int? ?? 0;
        var maxDrain = request?.GetField("MaxDrain")?.GetValue(null) as int? ?? 0;
        var drainFor = request?.GetField("DrainFor")?.GetValue(null) as TimeSpan? ?? TimeSpan.Zero;
        var maxUpload = request?.GetField("MaxUpload")?.GetValue(null) as int? ?? 0;
        Check(maxBody == 65_536 && maxDrain == 1_048_576 && drainFor == TimeSpan.FromSeconds(10) && maxUpload > maxDrain,
            $"oversize: a body is at most 64 KiB; one over it is drained up to 1 MiB, for 10 s ({maxBody}, {maxDrain}, {drainFor})");

        var control = new ShowBusTests.BusControl();
        var (patKey, patSecret) = ApiKeys.Create("Patterns FOH", ApiKeyScope.Node, DateTime.UtcNow);
        control.ApiKeys = new[] { patKey };
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        static string Words(long declared) => "a request body is at most 65536 bytes; this one declares " + declared.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // A context of exactly that many UTF-8 bytes, its headline padding it out.
        static string Ctx(int bytes)
        {
            var head = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"FOH\"},\"light\":\"red\",\"headline\":\"";
            return head + new string('x', bytes - head.Length - 2) + "\"}";
        }
        async Task<(HttpStatusCode Code, string? Error)> Post(string path, byte[] body, string? bearer)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
            if (bearer is not null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            using var res = await http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            string? error = null;
            if (text.StartsWith('{'))
            {
                using var d = JsonDocument.Parse(text);
                if (d.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
            }
            return (res.StatusCode, error);
        }

        // A context over the cap, with a node key: the sender reads 413 with what it declared; nothing is stored, bound or classed.
        var classed = control.Credentials.Count;
        var over = Encoding.UTF8.GetBytes(Ctx(70_000));
        var refused = await Post("/api/peers/context", over, patSecret);
        Check(over.Length == 70_000 && refused == (HttpStatusCode.RequestEntityTooLarge, Words(70_000)),
            $"oversize: a 70,000-byte context is answered 413 with its size, read by the sender, not a reset ({refused.Code}: {refused.Error})");
        Check(control.Peers.Find("pat1", DateTime.UtcNow) is null && control.Nodes.NodeOf(patKey.Id) is null && control.Credentials.Count == classed,
            "oversize: no route reads it — no context stored, the key bound to nothing, no credential classed");
        Check(web.TooBigContexts(DateTime.UtcNow) is [{ Address: "127.0.0.1", NodeId: null, Kind: null, Bytes: 70_000 }],
            "oversize: the context refused is noted for the Show network by its address, its key bound to no node yet");

        // Over the cap anywhere but a deferred path: a command, a proposal, MCP. A wrong PIN on one is never read, so never counted.
        var elsewhere = new List<(HttpStatusCode, string?)>();
        foreach (var path in new[] { "/api/proposals", "/mcp", "/api/slides/feed", "/api/start?pin=0000" })
            elsewhere.Add(await Post(path, new byte[maxBody + 1], null));
        var guesses = new List<(HttpStatusCode, string?)>();
        for (var i = 0; i < 6; i++) guesses.Add(await Post("/api/peers/context?pin=0000", over, null));
        Check(elsewhere.All(r => r == (HttpStatusCode.RequestEntityTooLarge, Words(maxBody + 1))) && control.Commands.Count == 0
              && control.Desk.All().Count == 0,
            $"oversize: a body one byte over is 413 on any path that is not deferred, and runs and queues nothing ({string.Join(", ", elsewhere)})");
        Check(guesses.All(r => r == (HttpStatusCode.RequestEntityTooLarge, Words(70_000))) && web.AuthFailures().Count == 0,
            $"oversize: six wrong PINs on bodies over the cap are never read, so count nothing ({string.Join(", ", guesses)})");

        // The next request, on a new connection, is read as before: a context of exactly 64 KiB is taken, and the PIN still opens.
        var whole = await Post("/api/peers/context", Encoding.UTF8.GetBytes(Ctx(maxBody)), patSecret);
        var pin = await http.GetAsync("/api/context?pin=4321");
        Check(whole == (HttpStatusCode.OK, null) && control.Peers.Find("pat1", DateTime.UtcNow) is { Proof: NodeProof.Key } && pin.StatusCode == HttpStatusCode.OK,
            $"oversize: after the 413s, a 65,536-byte context is taken whole and the PIN opens this machine's address ({whole.Code}, {pin.StatusCode})");
        var bound = await Post("/api/peers/context", over, patSecret);
        Check(bound.Code == HttpStatusCode.RequestEntityTooLarge
              && web.TooBigContexts(DateTime.UtcNow) is [{ NodeId: null, Address: "127.0.0.1" }, { NodeId: "pat1", Kind: "patterns", Address: "127.0.0.1", Bytes: 70_000 }]
              && web.TooBigContexts(DateTime.UtcNow.Add(PeerContexts.KeepFor).AddSeconds(1)).Count == 0,
            "oversize: once its key speaks for a node, a context it posts too big is noted by that node, beside the address's own note, each kept as long as a context");

        // Drained: declared and not yet sent, the server waits for the body before it answers; sent, the answer follows, then the close.
        const string Head = "POST /api/peers/context HTTP/1.1\r\nHost: stagewatch\r\nContent-Type: application/json\r\nContent-Length: ";
        var waited = await RawRequest(port, Head + "70000\r\n\r\n", over, quietMs: 1000);
        Check(!waited.AnsweredEarly && waited.Closed && waited.Text.StartsWith("HTTP/1.1 413 Payload Too Large\r\n", StringComparison.Ordinal)
              && waited.Text.Contains("Connection: close\r\n", StringComparison.Ordinal) && JsonError(waited.Text) == Words(70_000),
            $"oversize: the server drains a declared body before answering — nothing for 1 s while it is unsent, then the 413 and a clean close ({First(waited.Text)})");
        var most = await Post("/api/state", new byte[maxDrain], null);
        Check(most == (HttpStatusCode.RequestEntityTooLarge, Words(maxDrain)),
            $"oversize: a body of 1 MiB is drained whole and its sender reads the 413 ({most.Code}: {most.Error})");

        // Past 1 MiB: answered at once, with nothing of the body sent or read — well within the drain's 10 s.
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var past = await RawRequest(port, Head + (maxDrain + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n\r\n", null,
            timeoutMs: (int)drainFor.TotalMilliseconds / 2);
        Check(past.Closed && timer.Elapsed < drainFor / 2 && past.Text.StartsWith("HTTP/1.1 413 ", StringComparison.Ordinal) && JsonError(past.Text) == Words(maxDrain + 1),
            $"oversize: a body declared past 1 MiB is answered 413 at once, undrained, and closed ({timer.ElapsedMilliseconds} ms, {First(past.Text)})");
        var huge = await RawRequest(port, Head + "5000000000\r\n\r\n", null, timeoutMs: (int)drainFor.TotalMilliseconds / 2);
        Check(huge.Closed && JsonError(huge.Text) == Words(5_000_000_000),
            $"oversize: a length past int's range is an oversize body, never one with none ({First(huge.Text)})");
        var vast = await RawRequest(port, Head + "99999999999999999999\r\n\r\n", null, timeoutMs: (int)drainFor.TotalMilliseconds / 2);
        Check(vast.Closed && vast.Text.StartsWith("HTTP/1.1 413 ", StringComparison.Ordinal)
              && JsonError(vast.Text) == "a request body is at most 65536 bytes; this one declares at least 9223372036854775807",
            $"oversize: a length past a long's range is an oversize body too, answered 413 at once with words true of it ({First(vast.Text)}: {JsonError(vast.Text)})");

        // A /ws upgrade is a request like any other: a body over the cap on one is answered 413, never upgraded.
        var upgrade = await RawRequest(port, "GET /ws?cid=oversize HTTP/1.1\r\nHost: stagewatch\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
            + "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\nContent-Length: 70000\r\n\r\n", over);
        Check(upgrade.Closed && upgrade.Text.StartsWith("HTTP/1.1 413 ", StringComparison.Ordinal) && JsonError(upgrade.Text) == Words(70_000)
              && web.ConnectedClients == 0,
            $"oversize: a /ws upgrade declaring a body over the cap is answered 413 and closed, never 101 ({First(upgrade.Text)})");

        // A Content-Length this server cannot read is answered 400 before anything reads the request, never taken as no body: letters,
        // a sign, a list, two lengths. A wrong PIN on one is never read either, so it counts nothing.
        var unreadable = new List<string>();
        foreach (var length in new[] { "Content-Length: abc", "Content-Length: -5", "Content-Length: +70000", "Content-Length: 70000, 70000",
                     "Content-Length: 0x11170", "Content-Length:", "Content-Length: 12\r\nContent-Length: 12" })
        {
            var reply = await RawRequest(port, "POST /api/start?pin=0000 HTTP/1.1\r\nHost: stagewatch\r\n" + length + "\r\n\r\n", null);
            unreadable.Add(reply.Closed && reply.Text.StartsWith("HTTP/1.1 400 ", StringComparison.Ordinal) ? JsonError(reply.Text) ?? "" : First(reply.Text));
        }
        var unreadableWords = request?.GetField("UnreadableLength")?.GetValue(null) as string;
        Check(unreadableWords is { Length: > 0 } && unreadable.All(e => e == unreadableWords) && control.Commands.Count == 0 && web.AuthFailures().Count == 0,
            $"oversize: a Content-Length that is not one decimal number is answered 400 unrouted, never taken as no body, and its wrong PIN counts nothing ({string.Join(" | ", unreadable)})");
        var padded = await RawRequest(port, "GET /api/context?pin=4321 HTTP/1.1\r\nHost: stagewatch\r\nContent-Length: 00000\r\n\r\n", null);
        Check(padded.Text.StartsWith("HTTP/1.1 200 ", StringComparison.Ordinal),
            $"oversize: a length of digits alone is read, leading zeros and all ({First(padded.Text)})");

        // The deferred paths keep their own caps: past its cap, an upload closes unanswered as before, never with this 413.
        var upload = await RawRequest(port, "POST /api/holding/upload?name=x HTTP/1.1\r\nHost: stagewatch\r\nX-Stagewatch-Pin: 4321\r\nContent-Length: "
            + (maxUpload + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\n\r\n", null);
        Check(upload.Closed && upload.Text.Length == 0, $"oversize: a deferred path keeps its own cap and refusal, an upload past it closed unanswered ({First(upload.Text)})");

        await OversizeDeadline(Check, over, drainFor);
        await OversizeBeforeRedirect(Check, over);
    }

    /// <summary>
    /// The drain's deadline, on a manual clock: a sender that declares 70,000 bytes, sends 1,000 and goes quiet is waited for until
    /// the deadline and not a tick less; then it reads the 413 its size gets, written beside the read still pending, and the close.
    /// </summary>
    static async Task OversizeDeadline(Action<bool, string> Check, byte[] over, TimeSpan drainFor)
    {
        var clock = new ShowWorkflowTests.ManualTime(DateTime.UtcNow);
        var port = FreePort();
        await using var web = new WebServer(new ShowBusTests.BusControl(), () => "4321") { DrainClock = clock };
        web.Start(port, bindAll: false);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("POST /api/peers/context HTTP/1.1\r\nHost: stagewatch\r\nContent-Length: 70000\r\n\r\n"));
        await stream.WriteAsync(over.AsMemory(0, 1000));
        using var cts = new CancellationTokenSource(15_000);
        var answer = ReadToClose(stream, cts.Token);
        var armed = await Until(() => clock.Timers > 0, 5000);
        clock.Advance(drainFor - TimeSpan.FromTicks(1));
        var early = await Task.WhenAny(answer, Task.Delay(500)) == answer;
        clock.Advance(TimeSpan.FromTicks(1));
        var (text, closed) = await answer;
        Check(armed && !early && closed && text.StartsWith("HTTP/1.1 413 ", StringComparison.Ordinal)
              && JsonError(text) == "a request body is at most 65536 bytes; this one declares 70000",
            $"oversize: a sender that stops partway is drained until the deadline on the drain's clock and not a tick less, then reads the 413 and the close (armed {armed}, early {early}: {First(text)})");
    }

    /// <summary>
    /// The 413 comes before Require HTTPS: a plain request from this machine's own LAN address (not loopback, so Require HTTPS applies
    /// to it) is redirected when its body fits, and answered 413 when it is over the cap, never redirected with its <c>?pin=</c>.
    /// </summary>
    static async Task OversizeBeforeRedirect(Action<bool, string> Check, byte[] over)
    {
        var lan = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        Check(lan is not null, "oversize: this machine has a LAN address that is not loopback, to reach Require HTTPS at");
        if (lan is null) return;
        var port = FreePort();
        await using var web = new WebServer(new ShowBusTests.BusControl(), () => "4321") { RequireHttps = true };
        web.Start(port, bindAll: true);
        const string Head = "POST /api/peers/context?pin=4321 HTTP/1.1\r\nHost: stagewatch\r\nContent-Length: ";
        var fits = await RawRequest(port, Head + "2\r\n\r\n", "{}"u8.ToArray(), host: lan);
        var refused = await RawRequest(port, Head + "70000\r\n\r\n", over, host: lan);
        Check(fits.Text.StartsWith("HTTP/1.1 302 ", StringComparison.Ordinal)
              && refused.Closed && refused.Text.StartsWith("HTTP/1.1 413 ", StringComparison.Ordinal) && !refused.Text.Contains("Location:", StringComparison.Ordinal)
              && !refused.Text.Contains("4321", StringComparison.Ordinal),
            $"oversize: from another address under Require HTTPS, a body that fits is redirected and one over the cap is answered 413, its PIN never echoed ({First(fits.Text)}; {First(refused.Text)})");
    }

    /// <summary>
    /// One request on a socket of its own: <paramref name="head"/>, then <paramref name="body"/>, sent only once nothing has come back
    /// for <paramref name="quietMs"/>; what came back until the server closed, whether anything came before the body was sent, and
    /// whether the server closed it (not a reset, and not <paramref name="timeoutMs"/> running out).
    /// </summary>
    static async Task<(string Text, bool AnsweredEarly, bool Closed)> RawRequest(int port, string head, byte[]? body, int quietMs = 0, int timeoutMs = 5000,
        IPAddress? host = null)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host ?? IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        using var cts = new CancellationTokenSource(timeoutMs);
        var answer = ReadToClose(stream, cts.Token);
        var early = body is not null && quietMs > 0 && await Task.WhenAny(answer, Task.Delay(quietMs)) == answer;
        try
        {
            if (body is not null && !early) await stream.WriteAsync(body, cts.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException)
        {
            var (sent, _) = await answer;
            return (sent + "[" + e.GetType().Name + " sending]", early, false);
        }
        var (text, closed) = await answer;
        return (text, early, closed);
    }

    /// <summary>What a socket reads until its far end closes; and whether it did close (not a reset, and not <paramref name="ct"/> ending first).</summary>
    static async Task<(string Text, bool Closed)> ReadToClose(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var text = new StringBuilder();
        try
        {
            for (var n = await stream.ReadAsync(buffer, ct); n > 0; n = await stream.ReadAsync(buffer, ct))
                text.Append(Encoding.UTF8.GetString(buffer, 0, n));
            return (text.ToString(), true);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException)
        {
            return (text.ToString() + "[" + e.GetType().Name + "]", false);
        }
    }

    /// <summary>The <c>error</c> of a raw HTTP answer's JSON body, or null.</summary>
    static string? JsonError(string response)
    {
        var at = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (at < 0) return null;
        try
        {
            using var d = JsonDocument.Parse(response[(at + 4)..]);
            return d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A raw answer's first line, for a check's words.</summary>
    static string First(string response) => response.Split("\r\n")[0];

    /// <summary>
    /// Show apps on /ws, on a real server (Show Bus §7.1): a node link hears its verdict before any frame and never a screen
    /// command; a claim stays in Screens, out of "all"; a refused key is told once and keeps the frames; what a node link
    /// can't be is a 400 that counts nothing; and a key revoked, a binding moved or the PIN changed reaches the open
    /// sockets at once.
    /// </summary>
    static async Task NodeLinksOnWire(Action<bool, string> Check)
    {
        var control = new ShowBusTests.BusControl();
        var (pat1Key, pat1Secret) = ApiKeys.Create("Patterns FOH", ApiKeyScope.Node, DateTime.UtcNow);
        var (pat2Key, pat2Secret) = ApiKeys.Create("Patterns spare", ApiKeyScope.Node, DateTime.UtcNow);
        var (agKey, agSecret) = ApiKeys.Create("AGMessage", ApiKeyScope.Node, DateTime.UtcNow);
        var (readKey, readSecret) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, DateTime.UtcNow);
        var (earlyKey, earlySecret) = ApiKeys.Create("Early", ApiKeyScope.Node, DateTime.UtcNow);
        var (pmKey, pmSecret) = ApiKeys.Create("PlayMap", ApiKeyScope.Node, DateTime.UtcNow);
        var pm2Key = ApiKeys.Create("PlayMap 2", ApiKeyScope.Node, DateTime.UtcNow).Key;
        control.ApiKeys = new[] { pat1Key, pat2Key, agKey, readKey, earlyKey, pmKey, pm2Key };
        control.Nodes.Keep(control.ApiKeys);   // as the desk does before it serves anything
        var pin = new[] { "4321" };
        var port = FreePort();
        await using var web = new WebServer(control, () => Volatile.Read(ref pin[0]));
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var seq = 0;
        void Publish() => control.Snapshots.Publish(new ShowSnapshot
        {
            Text = "05:00", Seconds = 300 - Interlocked.Increment(ref seq), Title = "Keynote",
            Slides = new SlideView(4, 20, "Results", "SECRET SPEAKER NOTES", 0, 0, PaceState.Unknown, false),
        });
        Publish();
        static bool IsFrame(string m) => m.Contains("\"type\":\"state\"", StringComparison.Ordinal);
        static string Verdict(string t, string id, string verified, string scope = "node")
            => "{\"type\":\"auth\",\"ok\":true,\"scope\":\"" + scope + "\",\"node\":{\"t\":\"" + t + "\",\"id\":\"" + id + "\",\"verified\":\"" + verified + "\"}}";
        static string? Words(string message)
        {
            using var d = JsonDocument.Parse(message);
            return d.RootElement.TryGetProperty("words", out var w) ? w.GetString() : d.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }

        // A key revoked after its socket's credential was checked and before the socket was listed: the socket is checked
        // again once listed, so it never reads the operator frame on a key no longer in force.
        control.OnNextNodesRead = () =>
        {
            control.ApiKeys = control.ApiKeys.Where(k => k.Id != earlyKey.Id).ToArray();
            control.Nodes.Keep(control.ApiKeys);
            web.CredentialsChanged();
        };
        await using (var early = await WsProbe.Open(port, "node=early01&t=messenger&detail=1", earlySecret))
        {
            var earlyRevoked = await early.WaitFor(m => m.Contains("this key was revoked", StringComparison.Ordinal));
            Publish();
            var earlyFrame = await early.WaitForAfter(m => m.Contains("this key was revoked", StringComparison.Ordinal), IsFrame);
            Check(control.OnNextNodesRead is null && earlyRevoked == "{\"type\":\"auth\",\"ok\":false,\"error\":\"this key was revoked\",\"retry\":60}"
                  && earlyFrame is not null && !early.Saw("SECRET SPEAKER NOTES") && web.ListNodes().Count == 0
                  && web.ListScreens() is [{ Claim: "messenger early01" }] && control.Nodes.NodeOf(earlyKey.Id) is null,
                  $"node links: a key revoked between a socket's credential check and its listing is caught once it is listed: told once, public frames only, a claim ({earlyRevoked})");
        }
        await Until(() => web.ListScreens().Count == 0);

        // A node key: the verdict first, then frames; the link is a show app, never a screen.
        await using var ag = await WsProbe.Open(port, "node=ag00001&t=messenger&cid=agmessage&page=%2Fagmessage", agSecret);
        var agFrame = await ag.WaitFor(IsFrame);
        Check(ag.Messages.FirstOrDefault() == Verdict("messenger", "ag00001", "key") && agFrame is not null && ag.Messages.ToList().IndexOf(agFrame) > 0,
              $"node links: a node key's link hears its verdict before any frame, then follows the timer ({ag.Messages.FirstOrDefault()})");
        Check(web.ListScreens().Count == 0 && web.ListNodes() is [{ Id: "ag00001", Kind: "messenger", Verified: NodeProof.Key, KeyName: "AGMessage", Links: 1 }]
              && control.Nodes.NodeOf(agKey.Id) == ("messenger", "ag00001"),
              "node links: it is listed as a show app with its key, not in Screens, and its first proven use bound the key");

        // A browser and a claim beside it.
        await using var tv = await WsProbe.Open(port, "cid=screen01&page=/viewer");
        await using var claim = await WsProbe.Open(port, "node=pat9&t=patterns&cid=patterns&page=/patterns");
        await tv.WaitFor(IsFrame);
        await claim.WaitFor(IsFrame);
        var claimId = NodeLinks.ClaimId("patterns", "pat9");
        var listed = web.ListScreens();
        Check(claim.Messages.FirstOrDefault() is { } cv && cv.StartsWith("{\"type\":\"auth\",\"ok\":true,\"scope\":\"none\"", StringComparison.Ordinal)
              && cv.Contains("\"verified\":\"none\"", StringComparison.Ordinal) && Words(cv) == NodeLinks.NoKey,
              $"node links: a keyless link that names a node hears it is only claimed ({claim.Messages.FirstOrDefault()})");
        Check(listed.Count == 2 && listed.Single(x => x.Id == "screen01").Claim == "" && listed.Single(x => x.Id == claimId) is { Claim: "patterns pat9", Page: "/" },
              "node links: Screens lists the browser and the claim, flagged with what it claims");
        var api = await (await SendKey(http, HttpMethod.Get, "/api/screens", readSecret)).Content.ReadAsStringAsync();
        Check(api.Contains("\"claim\":\"patterns pat9\"}", StringComparison.Ordinal) && api.Contains("\"claim\":\"\"}", StringComparison.Ordinal) && !api.Contains("ag00001", StringComparison.Ordinal),
              "node links: /api/screens says which row is a claim (last field), and lists no show app");

        // Screen commands reach browsers only.
        var renamed = new List<(string Id, string Name)>();
        var saved = new List<(string Name, string Path)>();
        (bool Ok, string Words) Run(string action, string arg, string who)
            => ScreenDirector.Run(new ScreenArgs(action, arg, who), web, id => id == "ag00001" ? "AGMessage FOH" : null,
                (id, name) => renamed.Add((id, name)), (name, path) => saved.Add((name, path)), t => t == "lobby" ? "/audience?holding=0" : null);
        var all = Run("identify", "", "all");
        var heard = await tv.WaitFor(m => m.Contains("\"type\":\"identify\"", StringComparison.Ordinal));
        await Task.Delay(400);
        Check(all == (true, "Identifying 1 screen") && heard is not null && !ag.Saw("\"identify\"") && !claim.Saw("\"identify\""),
              "node links: 'screen identify all' reaches the browser, never the show app or the claim");
        var toApp = web.Identify(new[] { "node:ag00001" }) + web.Reload(new[] { "node:ag00001", "ag00001" }) + web.Redirect(new[] { "node:ag00001" }, "/viewer")
                    + web.Disconnect(new[] { "node:ag00001", "ag00001" });
        await Task.Delay(300);
        Check(toApp == 0 && ag.IsOpen && !ag.Saw("\"disconnect\"") && !ag.Saw("\"reload\"") && web.ListNodes().Any(n => n.Id == "ag00001"),
              "node links: no screen push or disconnect reaches a show app, whatever id is passed; it stays linked");
        var claimed = Run("identify", "", claimId);
        Check(claimed.Ok && await claim.WaitFor(m => m.Contains("\"type\":\"identify\"", StringComparison.Ordinal)) is not null,
              "node links: a claim is reached by its own id, so a stray client can still be found");
        var byId = Run("identify", "", "ag00001");
        var byName = Run("reload", "", "AGMessage FOH");
        Check(byId == (false, "Screens: ag00001 is a show app (AGMessage FOH), not a screen — screen commands reach browsers only (Producer → Show network)")
              && !byName.Ok && byName.Words.StartsWith("Screens: AGMessage FOH is a show app (messenger ag00001)", StringComparison.Ordinal),
              $"node links: a show app named by its id or its key-verified name is refused as one ({byId.Words})");
        var nameApp = Run("name", "Lobby", "ag00001");
        var nameTv = Run("name", "Lobby", "screen01");
        var saveApp = Run("save", "lobbyset", "ag00001");
        var saveTv = Run("save", "lobbyset", "screen01");
        Check(!nameApp.Ok && nameApp.Words.Contains("is a show app", StringComparison.Ordinal) && nameTv.Ok && renamed is [("screen01", "Lobby")]
              && !saveApp.Ok && saveTv.Ok && saved is [("lobbyset", "/viewer")],
              "node links: name and save follow the same order: a screen's id is taken, a show app's refused");
        var go = Run("go", "lobby", "all");
        var redirected = await tv.WaitFor(m => m.Contains("\"type\":\"redirect\"", StringComparison.Ordinal));
        Check(go.Ok && redirected is not null && !ag.Saw("\"redirect\"") && !claim.Saw("\"redirect\""), "node links: 'screen go' moves browsers only");
        var off = Run("disconnect", "", "all");
        var dropped = await tv.WaitFor(m => m.Contains("\"type\":\"disconnect\"", StringComparison.Ordinal));
        await Task.Delay(500);
        await ag.Send("{\"cmd\":\"ping\"}");
        await claim.Send("{\"cmd\":\"ping\"}");
        Check(off == (true, "Disconnected 1 screen") && dropped is not null && !ag.Saw("\"disconnect\"") && !claim.Saw("\"disconnect\"")
              && await ag.WaitFor(m => m.Contains("pong", StringComparison.Ordinal)) is not null && await claim.WaitFor(m => m.Contains("pong", StringComparison.Ordinal)) is not null
              && web.ListScreens().All(x => x.Id != "screen01"),
              "node links: 'screen disconnect all' drops the browser; the show app and the claim stay linked");
        Check(Run("identify", "", "all") == (false, "Screens: no browser screen is connected"), "node links: with only a claim left, 'all' reaches nothing and says so");

        // A browser can't take a claim's id: it is listed under one of its own, and 'all' reaches it, never the claim.
        await using (var impostor = await WsProbe.Open(port, $"cid={claimId}&page=/viewer"))
        {
            await impostor.WaitFor(IsFrame);
            var both = web.ListScreens();
            var reloadAll = Run("reload", "", "all");
            var reloaded = await impostor.WaitFor(m => m.Contains("\"type\":\"reload\"", StringComparison.Ordinal));
            await Task.Delay(300);
            Check(both.Count == 2 && both.Single(x => x.Id == claimId) is { Claim: "patterns pat9", Tabs: 1 }
                  && both.Single(x => x.Id != claimId) is { Claim: "" } own && own.Id.StartsWith("anon-", StringComparison.Ordinal)
                  && reloadAll == (true, "Reloading 1 screen") && reloaded is not null && !claim.Saw("\"type\":\"reload\""),
                  $"node links: a browser that chooses a claim's id is listed apart under an id of its own; 'all' reaches it, never the claim ({string.Join(", ", both.Select(x => x.Id))})");
        }
        await Until(() => web.ListScreens().Count == 1);

        // One install, one row (G32); a key pasted into a second install keeps its frames, verified none.
        await using var p1 = await WsProbe.Open(port, "node=pat1&t=patterns", pat1Secret);
        await using var p1Tab = await WsProbe.Open(port, "node=pat1&t=patterns", pat1Secret);
        await using var p2 = await WsProbe.Open(port, "node=pat2&t=patterns", pat2Secret);
        await using var twin = await WsProbe.Open(port, "node=pat2b&t=patterns", pat1Secret);
        var twinFrame = await twin.WaitFor(IsFrame);
        await p1Tab.WaitFor(IsFrame);
        await p2.WaitFor(IsFrame);
        Check(p1.Messages.FirstOrDefault() == Verdict("patterns", "pat1", "key") && p2.Messages.FirstOrDefault() == Verdict("patterns", "pat2", "key")
              && twin.Messages.FirstOrDefault() is { } tw && tw.Contains("\"verified\":\"none\"", StringComparison.Ordinal)
              && Words(tw) == "this key speaks for patterns pat1 — make a key for patterns pat2b" && twinFrame is not null,
              $"node links: two installs, two verdicts; a key pasted into a second install is verified none with words, and keeps the frames ({twin.Messages.FirstOrDefault()})");
        var rows = web.ListNodes();
        Check(rows.Count == 4 && rows.Single(n => n.Id == "pat1") is { Links: 2, Verified: NodeProof.Key, KeyName: "Patterns FOH" }
              && rows.Single(n => n.Id == "pat2") is { Verified: NodeProof.Key, KeyName: "Patterns spare" }
              && rows.Single(n => n.Id == "pat2b") is { Verified: NodeProof.None, KeyName: "Patterns FOH" } twinRow && twinRow.Words!.StartsWith("this key speaks for patterns pat1", StringComparison.Ordinal),
              $"node links: grouped by id: two Patterns desks are two rows, two tabs of one are one, the twin's row says why ({string.Join(", ", rows.Select(r => $"{r.Id}:{r.Verified}:{r.Links}"))})");
        var ctxTwin = await PostContext(http, pat1Secret, "pat2b");
        var ctxOwn = await PostContext(http, pat1Secret, "pat1");
        Check(ctxTwin.Code == HttpStatusCode.Forbidden && ctxOwn is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"key\"}"),
              "node links: the twin's context is refused 403, its own install's taken: the binding governs the node routes, not reading");

        // Every reader joins the same facts (ShowNetwork): the context's peers rows, the AI's facts, the desk's rows, Screens'
        // subtitle and diagnostics read the links the server lists, so none of them can call a show app a screen.
        var census = new WsCensus(0, 0, 5, 1);
        var counted = await Until(() => web.Census() == census);
        Check(counted && web.Census().Describe() == "0 browsers, 0 window links, 5 show app links, 1 unproven claim",
              $"node links: the census counts browsers, windows' links, show apps' links and claims apart ({web.Census().Describe()})");
        var nowNet = DateTime.UtcNow;
        var net = ShowNetwork.Summaries(new[] { new HeardNode("messenger", "ag00001", "AGMessage FOH", "127.0.0.1", 1) }, control.Peers.Held(nowNet),
            web.ListNodes(), control.OwnNodeId, nowNet);
        Check(net.Rows.Select(r => r.Peer.Id).SequenceEqual(new[] { "ag00001", "pat1", "pat2" }) && net.More == 0
              && net.Rows.All(r => r.Peer is { Link: "ws", Verified: "key", Liveness: "live" })
              && net.Rows.Single(r => r.Peer.Id == "pat1") is { Peer.HasContext: true, Link.Links: 2 } p1Net
              && ShowNetwork.LinkLine(p1Net, id => control.ApiKeys.FirstOrDefault(k => k.Id == id)?.Name) == "follows over /ws · key 'Patterns FOH' · 127.0.0.1 · 2 links"
              && net.Rows.Single(r => r.Peer.Id == "pat2") is { Peer.Headline: ShowNetwork.LinkOnly, Named: false }
              && net.Apart is [{ Id: "pat2b", Verified: NodeProof.None }],
              $"node links: the show network lists each proven install once, with its link and what proves it; the twin is apart, the claim nowhere ({string.Join(", ", net.Rows.Select(r => r.Peer.Id))})");
        using (var ctx = JsonDocument.Parse(ShowContext.Build(new ContextInput("abc123abc123", "Hall A", nowNet, control.Snapshots.Latest, false, Array.Empty<ContextItem>(),
                   net.Rows.Select(r => r.Peer).ToList(), Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 0))))
        {
            var peerRows = ctx.RootElement.GetProperty("peers").EnumerateArray().ToList();
            static string? Field(JsonElement row, string name) => row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            Check(peerRows.Count == 3 && peerRows.All(r => Field(r, "link") == "ws" && Field(r, "verified") == "key")
                  && peerRows.All(r => r.GetProperty("id").GetString() is not ("pat2b" or "pat9")),
                  "node links: the context's peers rows carry link ws and verified key for the proven links; a conflict or a claim adds no row");
        }
        Check(Screens.Subtitle(web.ListScreens(), web.ListNodes(), port).StartsWith(
                  "0 screens and 0 control pages, 1 unproven claim (never in 'all') · 4 show apps follow this Stagewatch too (Producer → Show network); screen commands never reach them.",
                  StringComparison.Ordinal),
              $"node links: Screens' subtitle names the claim and the show apps it never reaches ({Screens.Subtitle(web.ListScreens(), web.ListNodes(), port)})");

        // A refused key on a node link: one refusal with a retry, then the public frame; never a closed socket.
        var unknownSecret = ApiKeys.Create("never saved", ApiKeyScope.Node, DateTime.UtcNow).Secret;
        var refusals = 0;
        for (var i = 0; i < 10; i++)
        {
            await using var bad = await WsProbe.Open(port, "node=ag00009&t=messenger", unknownSecret);
            var frame = await bad.WaitFor(IsFrame);
            if (bad.Messages.FirstOrDefault() == "{\"type\":\"auth\",\"ok\":false,\"error\":\"unknown or revoked API key\",\"retry\":60}" && frame is not null
                && !frame.Contains("SECRET SPEAKER NOTES", StringComparison.Ordinal)) refusals++;
        }
        await using var page = await WsProbe.Open(port, "role=control&pin=4321");
        Check(refusals == 10 && await page.WaitFor(m => m.StartsWith("{\"type\":\"auth\"", StringComparison.Ordinal)) == "{\"type\":\"auth\",\"ok\":true}",
              $"node links: an unknown key is refused once per dial and keeps the public frame; ten dials later the PIN still signs a control page in ({refusals} of 10)");

        // What a node link can't be is a 400 before the upgrade, and never counted.
        var winBad = await RawUpgrade(port, "/ws?node=pat1&t=patterns&win=abc", null);
        var pinBad = await RawUpgrade(port, "/ws?node=pat1&t=patterns&pin=0000", null);
        var ownBad = await RawUpgrade(port, "/ws?node=abc123abc123&t=main", "Bearer " + pat1Secret);
        var windowBad = await RawUpgrade(port, "/ws?node=pat1&t=patterns", "Window abcdef");
        var ctlBad = await RawUpgrade(port, "/ws?node=pat1&t=patterns&role=control", null);
        var kindBad = await RawUpgrade(port, "/ws?node=pat1", null);
        static string? Refused400(string response)
        {
            if (!response.StartsWith("HTTP/1.1 400", StringComparison.Ordinal)) return null;
            using var d = JsonDocument.Parse(response[(response.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..]);
            return d.RootElement.GetProperty("ok").GetBoolean() ? null : d.RootElement.GetProperty("error").GetString();
        }
        Check(Refused400(winBad) == NodeLinks.WindowNotNode && Refused400(windowBad) == NodeLinks.WindowNotNode && Refused400(pinBad) == NodeLinks.PinInUrl
              && Refused400(ownBad) == NodeLinks.OwnId && Refused400(ctlBad) == NodeLinks.NodeDoesNotControl && Refused400(kindBad) == NodeLinks.BadKind,
              $"node links: a window's address, a PIN in the URL, control, a missing kind or this Stagewatch's own id is a 400 with words, before any upgrade ({winBad.Split("\r\n")[0]})");
        for (var i = 0; i < 5; i++) await RawUpgrade(port, "/ws?node=pat1&t=patterns&pin=0000", null);
        Check((await http.GetAsync("/api/context?pin=4321")).StatusCode == HttpStatusCode.OK, "node links: a 400 counts nothing toward the lockout");

        // The PIN in the Authorization header proves the operator: verified "operator" while no key speaks for the id. A
        // wrong one is one refusal with a retry, and the socket keeps the public frame as a claim.
        await using var pm = await WsProbe.Open(port, "node=pm00001&t=playmap", "4321");
        await using var pmWrong = await WsProbe.Open(port, "node=pm00002&t=playmap", "0000");
        await pm.WaitFor(IsFrame);
        var wrongFrame = await pmWrong.WaitFor(IsFrame);
        Check(pm.Messages.FirstOrDefault() == Verdict("playmap", "pm00001", "operator", "pin")
              && web.ListNodes().Single(n => n.Id == "pm00001") is { Verified: NodeProof.Operator, KeyId: null, KeyName: "" },
              $"node links: the PIN in the Authorization header makes a node link verified by the operator ({pm.Messages.FirstOrDefault()})");
        Check(pmWrong.Messages.FirstOrDefault() == "{\"type\":\"auth\",\"ok\":false,\"error\":\"wrong PIN\",\"retry\":60}" && wrongFrame is not null
              && web.ListScreens().Any(x => x.Claim == "playmap pm00002") && web.ListNodes().All(n => n.Id != "pm00002"),
              $"node links: a wrong PIN is one refusal with a retry; the socket keeps the public frame, as a claim ({pmWrong.Messages.FirstOrDefault()})");

        // A key's first use takes a PIN link's id: the open link hears verified none at once, with no other change made.
        var pmCtx = await PostContext(http, pmSecret, "pm00001", "playmap");
        var pmTaken = await pm.WaitFor(m => m.Contains("\"verified\":\"none\"", StringComparison.Ordinal));
        Check(pmCtx is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"key\"}")
              && pmTaken == "{\"type\":\"auth\",\"ok\":true,\"scope\":\"pin\",\"node\":{\"t\":\"playmap\",\"id\":\"pm00001\",\"verified\":\"none\"},\"words\":\"playmap pm00001 speaks with its own key\"}"
              && web.ListNodes().Single(n => n.Id == "pm00001") is { Verified: NodeProof.None, Words: "playmap pm00001 speaks with its own key" },
              $"node links: another key's first context POST takes an open link's id, and the link hears what it is now ({pmTaken})");

        // The operator lets a key speak for a PIN link's id: a fresh verdict, verified none, with the binding's words.
        await using var pm2 = await WsProbe.Open(port, "node=pm00003&t=playmap", "4321");
        await pm2.WaitFor(IsFrame);
        control.Nodes.Rebind(pm2Key.Id, "playmap", "pm00003");
        web.CredentialsChanged();
        var pm2Moved = await pm2.WaitFor(m => m.Contains("\"verified\":\"none\"", StringComparison.Ordinal));
        Check(pm2.Messages.FirstOrDefault() == Verdict("playmap", "pm00003", "operator", "pin") && pm2Moved is not null
              && Words(pm2Moved) == "playmap pm00003 speaks with its own key" && pm2Moved.Contains("\"scope\":\"pin\"", StringComparison.Ordinal),
              $"node links: the operator rebinds a PIN link's id and the link hears it at once ({pm2Moved})");

        // A key revoked reaches the open sockets at once: the node link and a keyed dashboard drop to the public frame.
        await using var board = await WsProbe.Open(port, "detail=1&cid=dash0001", readSecret);
        var boardFirst = await board.WaitFor(IsFrame);
        Check(board.Messages.FirstOrDefault() == boardFirst && boardFirst!.Contains("SECRET SPEAKER NOTES", StringComparison.Ordinal),
              "node links: a keyed dashboard hears a frame first (unchanged), the operator's");
        control.ApiKeys = new[] { pat1Key, pat2Key };
        control.Nodes.Keep(control.ApiKeys);
        web.CredentialsChanged();
        var agRevoked = await ag.WaitFor(m => m.Contains("revoked", StringComparison.Ordinal));
        var boardRevoked = await board.WaitFor(m => m.Contains("revoked", StringComparison.Ordinal));
        Publish();
        var agAfter = await ag.WaitForAfter(m => m.Contains("revoked", StringComparison.Ordinal), IsFrame);
        var boardAfter = await board.WaitFor(m => IsFrame(m) && !m.Contains("SECRET SPEAKER NOTES", StringComparison.Ordinal));
        Check(agRevoked == "{\"type\":\"auth\",\"ok\":false,\"error\":\"this key was revoked\",\"retry\":60}" && boardRevoked == agRevoked && agAfter is not null && boardAfter is not null
              && ag.IsOpen && board.IsOpen,
              $"node links: revoked, the node link and the dashboard hear it once and keep the public frame, still open ({agRevoked})");
        Check(web.ListNodes().All(n => n.Id != "ag00001") && web.ListScreens().Any(x => x.Claim == "messenger ag00001"),
              "node links: the revoked node link is a claim now: in Screens, flagged, no longer a show app");

        // A binding moved by the operator: the open links hear what they are now, latest wins. The key it freed binds to the
        // twin's open link (the link is its proven use, as its admission would have been), so pat1's own links end up told
        // that their key speaks for the twin's id: a link is verified by key only while its key's binding holds its id.
        control.Nodes.Rebind(pat2Key.Id, "patterns", "pat1");
        web.CredentialsChanged();
        const string p1Words = "this key speaks for patterns pat2b — make a key for patterns pat1";
        static bool IsAuth(string m) => m.StartsWith("{\"type\":\"auth\"", StringComparison.Ordinal);
        var p2Moved = await p2.WaitFor(m => IsAuth(m) && m.Contains("\"verified\":\"none\"", StringComparison.Ordinal));
        var twinFreed = await twin.WaitFor(m => IsAuth(m) && m.Contains("\"verified\":\"key\"", StringComparison.Ordinal));
        var p1Moved = await p1.WaitFor(m => IsAuth(m) && Words(m) == p1Words);
        var p1TabMoved = await p1Tab.WaitFor(m => IsAuth(m) && Words(m) == p1Words);
        Check(p2Moved is not null && Words(p2Moved) == "this key speaks for patterns pat1 — make a key for patterns pat2" && p1Moved is not null && p1TabMoved is not null
              && web.ListNodes().Where(n => n.Id == "pat1").ToList() is [{ Verified: NodeProof.None, Links: 2, Words: p1Words } pat1Row] && pat1Row.KeyId == pat1Key.Id,
              $"node links: the operator moves a binding and the open links hear what they are now ({p1Moved} / {p2Moved})");
        Check(twinFreed == Verdict("patterns", "pat2b", "key") && control.Nodes.NodeOf(pat1Key.Id) == ("patterns", "pat2b") && control.Nodes.KeyOf("pat1") == pat2Key.Id
              && web.ListNodes().Single(n => n.Id == "pat2b") is { Verified: NodeProof.Key, Words: null },
              "node links: the key the operator freed binds to the twin's open link, which hears it is verified by key; the bindings hold what the links say");

        // The PIN changed: a control page the old PIN let in drops to a viewer, and is told.
        Volatile.Write(ref pin[0], "9999");
        web.CredentialsChanged();
        var changed = await page.WaitFor(m => m.Contains("PIN changed", StringComparison.Ordinal));
        await page.Send("{\"cmd\":\"start\",\"args\":[]}");
        var after = await page.WaitFor(m => m.Contains("\"type\":\"error\"", StringComparison.Ordinal));
        Check(changed is not null && Words(changed) == NodeLinks.PinChanged && after is not null && after.Contains("read-only", StringComparison.Ordinal) && page.IsOpen
              && !control.Commands.Any(c => c.Kind == ShowCommandKind.Start),
              $"node links: a new PIN drops a control page to a viewer at once; its next command is refused ({changed})");
        var pmChanged = await pm.WaitFor(m => m.Contains("PIN changed", StringComparison.Ordinal));
        Check(pmChanged is not null && Words(pmChanged) == NodeLinks.PinChanged && pmChanged.EndsWith(",\"retry\":60}", StringComparison.Ordinal) && pm.IsOpen
              && web.ListScreens().Any(x => x.Claim == "playmap pm00001") && web.ListNodes().All(n => n.Id != "pm00001"),
              $"node links: a new PIN makes a PIN's node link a claim, told once with a retry, still open ({pmChanged})");

        // With a viewing code set, a revoked key's socket with no other pass hears one message and closes.
        control.View = "lobby42";
        await using var spare = await WsProbe.Open(port, "node=pat2&t=patterns", pat2Secret);
        await spare.WaitFor(IsFrame);
        control.ApiKeys = new[] { pat1Key };
        control.Nodes.Keep(control.ApiKeys);
        web.CredentialsChanged();
        var closing = await spare.WaitFor(m => m.Contains("revoked", StringComparison.Ordinal));
        var closed = await spare.WaitClosed();
        Check(closing == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\",\"retry\":60,\"refused\":\"this key was revoked\"}" && closed,
              $"node links: where a viewing code is set, a revoked key's link hears one message with the code's words and the key's, and closes ({closing})");
        await using var lateBad = await WsProbe.Open(port, "node=ag00010&t=messenger", unknownSecret);
        var lateClosed = await lateBad.WaitClosed();
        Check(lateClosed && lateBad.Messages is [var only]
              && only == "{\"type\":\"auth\",\"ok\":false,\"view\":true,\"error\":\"This screen needs the viewing code\",\"retry\":60,\"refused\":\"unknown or revoked API key\"}",
              $"node links: where a viewing code is set, an unknown key's dial hears one message with both refusals, and closes ({string.Join(" | ", lateBad.Messages)})");
        control.View = null;
    }

    static Task<HttpResponseMessage> SendKey(HttpClient http, HttpMethod method, string path, string secret)
    {
        var m = new HttpRequestMessage(method, path);
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        return http.SendAsync(m);
    }

    static async Task<(HttpStatusCode Code, string Body)> PostContext(HttpClient http, string secret, string id, string kind = "patterns")
    {
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/peers/context")
        {
            Content = new StringContent("{\"v\":1,\"node\":{\"t\":\"" + kind + "\",\"id\":\"" + id + "\",\"name\":\"FOH\"},\"light\":\"green\",\"headline\":\"fine\"}", Encoding.UTF8, "application/json"),
        };
        m.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", secret);
        var r = await http.SendAsync(m);
        return (r.StatusCode, await r.Content.ReadAsStringAsync());
    }

    /// <summary>A WebSocket upgrade written by hand, so a refusal before the upgrade can be read whole (status and body).</summary>
    static async Task<string> RawUpgrade(int port, string path, string? authorization)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var head = $"GET {path} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\n" +
                   "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" + (authorization is null ? "" : $"Authorization: {authorization}\r\n") + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        using var cts = new CancellationTokenSource(3000);
        var buffer = new byte[8192];
        var text = new StringBuilder();
        try
        {
            int n;
            while ((n = await stream.ReadAsync(buffer, cts.Token)) > 0)
            {
                text.Append(Encoding.UTF8.GetString(buffer, 0, n));
                if (text.ToString().StartsWith("HTTP/1.1 101", StringComparison.Ordinal)) break;
            }
        }
        catch (OperationCanceledException) { text.Append("[timed out]"); }
        catch (IOException) { text.Append("[reset]"); }
        return text.ToString();
    }

    /// <summary>A WebSocket client that reads every message in the background, so a test can wait for one, or see that one never came.</summary>
    sealed class WsProbe : IAsyncDisposable
    {
        private readonly ClientWebSocket _ws = new();
        private readonly List<string> _messages = new();
        private Task _reader = Task.CompletedTask;

        public static async Task<WsProbe> Open(int port, string query, string? bearer = null)
        {
            var p = new WsProbe();
            if (bearer is not null) p._ws.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
            await p._ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?{query}"), CancellationToken.None);
            p._reader = p.Read();
            return p;
        }

        private async Task Read()
        {
            var buffer = new byte[65536];
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(buffer, CancellationToken.None);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);
                    lock (_messages) _messages.Add(Encoding.UTF8.GetString(message.ToArray()));
                }
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or OperationCanceledException)
            {
                // The server dropped the link: what was heard stays readable.
            }
        }

        public IReadOnlyList<string> Messages { get { lock (_messages) return _messages.ToArray(); } }

        public bool IsOpen => _ws.State == WebSocketState.Open;

        public bool Saw(string fragment) => Messages.Any(m => m.Contains(fragment, StringComparison.Ordinal));

        /// <summary>The first message (already heard or still to come) that matches, or null after <paramref name="timeoutMs"/>.</summary>
        public async Task<string?> WaitFor(Func<string, bool> match, int timeoutMs = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                if (Messages.FirstOrDefault(match) is { } hit) return hit;
                if (sw.ElapsedMilliseconds > timeoutMs) return null;
                await Task.Delay(20);
            }
        }

        /// <summary>The first message that matches after the first one that is <paramref name="after"/>, or null after <paramref name="timeoutMs"/>.</summary>
        public async Task<string?> WaitForAfter(Func<string, bool> after, Func<string, bool> match, int timeoutMs = 3000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                var all = Messages;
                var from = all.Select((m, i) => (m, i)).FirstOrDefault(x => after(x.m)) is { m: not null } mark ? mark.i : -1;
                if (from >= 0 && all.Skip(from + 1).FirstOrDefault(match) is { } hit) return hit;
                if (sw.ElapsedMilliseconds > timeoutMs) return null;
                await Task.Delay(20);
            }
        }

        /// <summary>Whether the server closed the link within <paramref name="timeoutMs"/>.</summary>
        public async Task<bool> WaitClosed(int timeoutMs = 3000) => await Task.WhenAny(_reader, Task.Delay(timeoutMs)) == _reader && !IsOpen;

        public Task Send(string json) => _ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            if (_ws.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(1000);
                try { await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", cts.Token); }
                catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // Already gone: nothing left to close.
                }
            }
            _ws.Dispose();
        }
    }

    /// <summary>Whether <paramref name="condition"/> came true within <paramref name="timeoutMs"/> (a socket's close reaching the server's list).</summary>
    static async Task<bool> Until(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(20);
        }
        return true;
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    static Task<HttpResponseMessage> SendWithPin(HttpClient http, string path)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, path);
        m.Headers.Add("X-Stagewatch-Pin", "4321");
        return http.SendAsync(m);
    }

    static async Task<string> Receive(ClientWebSocket ws, int timeoutMs = 3000)
    {
        var buf = new byte[65536];
        using var cts = new CancellationTokenSource(timeoutMs);
        var r = await ws.ReceiveAsync(buf, cts.Token);
        return Encoding.UTF8.GetString(buf, 0, r.Count);
    }
}
