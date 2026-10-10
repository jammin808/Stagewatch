using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;

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
        public string? ProposeFromAgent(string line, string reason)
        {
            if (!AgentApproval) return null;
            lock (Proposed) Proposed.Add(line + " | " + reason);
            return "Queued for the operator's approval (proposal #1).";
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

            // The real agent loop, end to end (PowerPoint isn't running here, so it reports "not presenting").
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
        Check(names.SequenceEqual(new[] { "get_state", "get_running_order", "run_command", "get_report" }), "mcp: tools/list");
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
