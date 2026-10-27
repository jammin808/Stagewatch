using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// Show Bus 1.1 (docs/SHOWBUS.md): frame numbers, receipts, node keys, context in and out, clock offset, discovery,
/// the Patterns pairing proof — pure parts first, then the web server end to end.
/// </summary>
static class ShowBusTests
{
    internal sealed class BusControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(), Array.Empty<(string, string?, int)>());
        public IReadOnlyList<ApiKey> ApiKeys { get; set; } = Array.Empty<ApiKey>();
        public Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
        {
            Execute(command);
            return Task.FromResult(command.Kind == ShowCommandKind.Start
                ? CommandReceipt.Refused(id, "Start needs a licence")
                : new CommandReceipt(id, true, "applied", "Message on the speaker screen", null, new Dictionary<string, long> { ["messageId"] = 12 }));
        }
        // One set of bindings for every node route, as the desk keeps them.
        private readonly NodeBindings _nodes = new();
        /// <summary>
        /// Run once, at the next read of <see cref="Nodes"/>: a /ws admission reads the bindings after the socket's
        /// credential was checked and before it is listed, the moment a credential change could slip between the two.
        /// </summary>
        public Action? OnNextNodesRead;
        public NodeBindings Nodes
        {
            get
            {
                Interlocked.Exchange(ref OnNextNodesRead, null)?.Invoke();
                return _nodes;
            }
        }
        public readonly PeerContexts Peers;
        public readonly ProposalDesk Desk;
        public BusControl()
        {
            Peers = new PeerContexts(Nodes);
            Desk = new ProposalDesk(() => "desk01", Nodes);
        }
        // What each node route's request proved, as the server classed it (newest last).
        public readonly List<NodeCredential> Credentials = new();
        NodeCredential Seen(NodeCredential c) { lock (Credentials) Credentials.Add(c); return c; }
        public NodeCredential LastCredential { get { lock (Credentials) return Credentials.Count > 0 ? Credentials[^1] : default; } }
        public (int Status, string Json) SubmitProposal(string body, NodeCredential credential)
        {
            var (p, error, forbidden) = Desk.Submit(body, Seen(credential), (_, line) => line.StartsWith("note") ? "low" : "high", DateTime.UtcNow);
            return p is null ? (forbidden ? 403 : 400, JsonSerializer.Serialize(new { ok = false, error })) : (201, JsonSerializer.Serialize(new { ok = true, id = p.Id, state = p.StateName }));
        }
        public (int Status, string Json) GetProposal(string id) => Desk.Get(id) is { } p ? (200, JsonSerializer.Serialize(p.ToJson())) : (404, "{}");
        public (int Status, string Json) CollectApproved(string targetId, NodeCredential credential)
        {
            var (items, error) = Desk.Collect(targetId, Seen(credential), DateTime.UtcNow);
            return items is null ? (403, JsonSerializer.Serialize(new { ok = false, error })) : (200, JsonSerializer.Serialize(new { proposals = items.Select(x => x.ToJson()) }));
        }
        public (int Status, string Json) ProposalResult(string id, string body, NodeCredential credential)
        {
            using var d = JsonDocument.Parse(body);
            return Desk.Result(id, d.RootElement.GetProperty("ok").GetBoolean(), d.RootElement.GetProperty("words").GetString() ?? "", Seen(credential), DateTime.UtcNow) is { } e
                ? (409, JsonSerializer.Serialize(new { ok = false, error = e })) : (200, "{\"ok\":true}");
        }
        public string? ContextJson() => "{\"v\":1,\"node\":{\"t\":\"main\",\"id\":\"abc123abc123\",\"name\":\"Hall A\"},\"light\":\"green\",\"headline\":\"Keynote\"}";
        public string? OwnNodeId => "abc123abc123";
        public volatile string? View;
        public string? ViewCode => View;
        public (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
            => Peers.Accept(json, address, DateTime.UtcNow, "abc123abc123", Seen(credential));
    }

    public static async Task Run(Action<bool, string> Check)
    {
        Pure(Check);
        ContextSizes(Check);
        DeskRules(Check);
        CertificateLives(Check);
        CertificateOnDisk(Check);
        await Web(Check);
        await Tls(Check);
        await Approvals(Check);
        await OpenDesk(Check);
        await Gate(Check);
        await PatternsDesk(Check);
        await PatternsPairingRules(Check);
    }

    static void Pure(Action<bool, string> Check)
    {
        // Frames are numbered after duplicates are dropped: equal frames stay equal, the count only moves on change.
        var hub = new SnapshotHub();
        var a = new ShowSnapshot { Text = "05:00", Seconds = 300 };
        hub.Publish(a);
        var s1 = hub.Latest.Seq.Value;
        Check(!hub.Publish(a with { }) && hub.Latest.Seq.Value == s1, "bus: an unchanged frame isn't published again (the number doesn't count as a change)");
        hub.Publish(a with { Seconds = 299, Text = "04:59" });
        Check(hub.Latest.Seq.Value == s1 + 1, "bus: every published frame gets the next number");
        using (var doc = JsonDocument.Parse(SnapshotJson.Serialize(hub.Latest, FrameAccess.Public)))
            Check(doc.RootElement.GetProperty("v").GetInt32() == 1 && doc.RootElement.GetProperty("seq").GetInt64() == s1 + 1, "bus: frames carry v and seq");

        // Receipts.
        Check(CommandReceipt.CleanId("a1_b-2") == "a1_b-2" && CommandReceipt.CleanId("bad id") is null && CommandReceipt.CleanId(new string('x', 33)) is null, "bus: request ids are short and plain");
        using (var r = JsonDocument.Parse(new CommandReceipt("a1", true, "applied", "done", null, new Dictionary<string, long> { ["messageId"] = 7 }).ToJson()))
            Check(r.RootElement.GetProperty("type").GetString() == "receipt" && r.RootElement.GetProperty("refs").GetProperty("messageId").GetInt64() == 7, "bus: a receipt says what was made");

        // Node keys: read, stage messages, their own context — nothing else.
        Check(ApiKeys.Allows(ApiKeyScope.Node, ApiKeyScope.Read) && ApiKeys.Allows(ApiKeyScope.Node, ApiKeyScope.Node) && !ApiKeys.Allows(ApiKeyScope.Node, ApiKeyScope.Control)
              && ApiKeys.Allows(ApiKeyScope.Control, ApiKeyScope.Node) && !ApiKeys.Allows(ApiKeyScope.Read, ApiKeyScope.Node), "bus: what a node key may do");
        Check(ApiKeys.NodeMayRun(ShowCommandKind.Message) && !ApiKeys.NodeMayRun(ShowCommandKind.Start) && !ApiKeys.NodeMayRun(ShowCommandKind.SignalGoAll), "bus: a node key runs stage messages only");

        // Clock offset, NTP-style, from the least-delayed exchange.
        var est = new ClockOffsetEstimator();
        Check(est.Offset is null, "clock: no offset before a sample");
        est.Add(1000, 4060, 4060, 1120);   // rtt 120, offset 3000
        est.Add(2000, 5010, 5010, 2020);   // rtt 20, offset 3000
        est.Add(3000, 9000, 9000, 3900);   // slow: rtt 900 (ignored by min-rtt)
        Check(est.Offset == TimeSpan.FromMilliseconds(3000) && est.Apart, $"clock: offset from the fastest round trip ({est.Offset})");

        // Peer contexts: validated, capped, kept 30 s, never ourselves.
        var peers = new PeerContexts(new NodeBindings());
        var patKey = NodeCredential.Key("kPat");
        var t0 = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);
        var pat = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"Patterns FOH\"},\"light\":\"red\",\"headline\":\"Projector 1 mismatch\"," +
                  "\"items\":[{\"id\":\"screen:1\",\"kind\":\"screen\",\"label\":\"Projector 1\",\"light\":\"red\",\"state\":\"MISMATCH 1080p60 vs 50\"},{\"id\":\"x\",\"light\":\"purple\"}]," +
                  "\"recent\":[{\"at\":1,\"text\":\"TAKE VT1\"}]}";
        Check(peers.Accept(pat, "10.0.0.5", t0, null, patKey) is (200, null, NodeProof.Key) && peers.All(t0) is [{ Name: "Patterns FOH", Light: "red", Items.Count: 2, Proof: NodeProof.Key } e]
              && e.Items[1].Light == Light.Grey && e.Recent[0] == "TAKE VT1",
              "context: a peer's context is kept with its proof, unknown lights read as grey");
        Check(peers.Accept("{\"v\":1}", "x", t0, null, patKey) is (400, not null, _) && peers.Accept("not json", "x", t0, null, patKey).Status == 400
              && peers.Accept(pat, "x", t0, "9f8e7d6c", patKey) is (400, "that's this Stagewatch", _),
              "context: junk and our own context are refused");
        Check(peers.All(t0.AddSeconds(31)).Count == 0, "context: a peer that stops talking is forgotten after 30 s");

        // Stagewatch's own context: lights from facts, a headline, no secrets.
        var frame = new ShowSnapshot
        {
            Mode = TimerMode.Countdown, State = TimerState.Running, Seconds = -45, Text = "-00:45", Phase = TimerPhase.Overtime, Title = "Keynote — Ana",
            ItemIndex = 1, ItemCount = 4, OffsetSeconds = 300, ScheduleText = "5:00 behind",
            Signals = new[]
            {
                new SignalView(1, SignalKind.Standby, "Lighting", "LX 12", SignalState.Done, false, 10, "Sam"),
                new SignalView(2, SignalKind.Standby, "Sound", "SQ 4", SignalState.Sent, true, 30, ""),
            }.ToEquatableArray(),
        };
        var input = new ContextInput("abc123abc123", "Hall A", t0, frame, false,
            new[] { new ContextItem("link:patterns", "playout", "Patterns", Light.Green, "VT1: 0:42 left") },
            new[] { new PeerSummary("patterns", "9f8e7d6c", "Patterns FOH", Light.Red, "Projector 1 mismatch", "10.0.0.5", 1, true) },
            Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 12);
        var (light, headline, items) = ShowContext.Assess(input);
        Check(light == Light.Red && items.First(i => i.Id == "timer").Light == Light.Red && items.First(i => i.Id == "schedule").Light == Light.Amber,
              "context: overtime is red, five minutes behind is amber");
        Check(items.First(i => i.Id == "cue:sound").State.Contains("NO ACKNOWLEDGEMENT") && items.First(i => i.Id == "cue:lighting").State.StartsWith("in position"),
              "context: each department's cue, unanswered ones flagged");
        Check(headline.StartsWith("Keynote — Ana") && headline.Contains("over") && headline.Contains("behind") && headline.Contains("Lighting, Sound standing by"),
              $"context: a one-line headline from the facts ({headline})");
        using (var doc = JsonDocument.Parse(ShowContext.Build(input)))
        {
            var root = doc.RootElement;
            Check(root.GetProperty("v").GetInt32() == 1 && root.GetProperty("node").GetProperty("t").GetString() == "main"
                  && root.GetProperty("peers")[0].GetProperty("headline").GetString() == "Projector 1 mismatch"
                  && root.GetProperty("commands").EnumerateArray().Any(c => c.GetProperty("verb").GetString() == "signal go all" && c.GetProperty("risk").GetString() == "high"),
                  "context: the §6 shape with peers one hop and the verbs with their risk");
        }

        // Discovery: every kind of node is heard; ports.http wins over the older web port.
        var hello = new SlidesBeacon.Hello("messenger", 1, "ag-1234", "AGM room", "LAPTOP", 0, false, new[] { "messages" }, 1,
            new Dictionary<string, int> { ["http"] = 3023 }, 1760101234567, "LAPTOP");
        Check(hello.HttpPort == 3023 && SlidesBeacon.ValidToken("patterns", 16) && !SlidesBeacon.ValidToken("bad kind!", 16), "discovery: a messenger's hello, its http port");

        // Patterns pairing: the token never goes on the wire, only a proof of it.
        var proof = PatternsLink.Proof("ab12-cd34", "a1b2", "Stagewatch");
        using (var h = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes("AB12CD34")))
            Check(proof == Convert.ToHexString(h.ComputeHash(Encoding.UTF8.GetBytes("a1b2Stagewatch"))).ToLowerInvariant() && proof == PatternsLink.Proof(" AB12CD34 ", "a1b2", "Stagewatch"),
                  "patterns: the pairing proof is HMAC(canonical token, nonce + name), never the token itself");
    }

    static async Task Web(Action<bool, string> Check)
    {
        var control = new BusControl();
        var (nodeKey, nodeSecret) = ApiKeys.Create("AGMessage", ApiKeyScope.Node, DateTime.UtcNow);
        var (readKey, readSecret) = ApiKeys.Create("Patterns", ApiKeyScope.Read, DateTime.UtcNow);
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        control.ApiKeys = new[] { nodeKey, readKey, ctlKey };
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300, Title = "Keynote" });

        // A receipt over HTTP: what happened, and the message's id to follow its acknowledgement.
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/message/Wrap%20up/warning/off/ack"))
        {
            req.Headers.Add("Authorization", "Bearer " + nodeSecret);
            req.Headers.Add("X-Request-Id", "m1");
            var res = await http.SendAsync(req);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            Check(res.StatusCode == HttpStatusCode.OK && doc.RootElement.GetProperty("id").GetString() == "m1" && doc.RootElement.GetProperty("stage").GetString() == "applied"
                  && doc.RootElement.GetProperty("refs").GetProperty("messageId").GetInt64() == 12, "web: a node key sends a stage message and gets its receipt and message id");
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/start"))
        {
            req.Headers.Add("Authorization", "Bearer " + nodeSecret);
            var res = await http.SendAsync(req);
            Check(res.StatusCode == HttpStatusCode.Forbidden, $"web: a node key can't run the show ({(int)res.StatusCode})");
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/start"))
        {
            req.Headers.Add("X-Stagewatch-Pin", "4321");
            req.Headers.Add("X-Request-Id", "s1");
            var res = await http.SendAsync(req);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            Check(res.StatusCode == HttpStatusCode.Conflict && !doc.RootElement.GetProperty("ok").GetBoolean() && doc.RootElement.GetProperty("error").GetString()!.Contains("licence"),
                  "web: a refused command says why in its receipt");
        }

        // Context out and in.
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/context"))
        {
            req.Headers.Add("Authorization", "Bearer " + readSecret);
            var res = await http.SendAsync(req);
            Check(res.StatusCode == HttpStatusCode.OK && (await res.Content.ReadAsStringAsync()).Contains("\"headline\":\"Keynote\""), "web: a read key gets the context");
        }
        Check((await http.GetAsync("/api/context")).StatusCode == HttpStatusCode.Unauthorized, "web: the context needs a key or the PIN");
        var agContext = "{\"v\":1,\"node\":{\"t\":\"messenger\",\"id\":\"ag1\",\"name\":\"AGM\"},\"light\":\"amber\",\"headline\":\"2 action prompts open\"}";
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/peers/context") { Content = new StringContent(agContext, Encoding.UTF8, "application/json") })
        {
            req.Headers.Add("Authorization", "Bearer " + nodeSecret);
            var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            Check(res.StatusCode == HttpStatusCode.OK && body == "{\"ok\":true,\"verified\":\"key\"}" && control.LastCredential == NodeCredential.Key(nodeKey.Id)
                  && control.Peers.All(DateTime.UtcNow) is [{ Headline: "2 action prompts open", Proof: NodeProof.Key }],
                  $"web: another app posts its context with a node key, and hears it is held as verified by its key ({body})");
        }
        var forged = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"Patterns FOH\"},\"light\":\"green\",\"headline\":\"all fine\"}";
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/peers/context") { Content = new StringContent(forged, Encoding.UTF8, "application/json") })
        {
            req.Headers.Add("Authorization", "Bearer " + nodeSecret);
            var asked = control.Credentials.Count;
            var res = await http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            var error = JsonDocument.Parse(body).RootElement.GetProperty("error").GetString();
            Check(res.StatusCode == HttpStatusCode.Forbidden && error == "this key speaks for messenger ag1 — make a key for patterns pat1"
                  && control.Peers.All(DateTime.UtcNow) is [{ Id: "ag1" }] && control.Credentials.Count == asked
                  && control.Nodes.LastRefused(nodeKey.Id) is { Kind: "patterns", Id: "pat1" },
                  $"web: a node key can't post a context for another node: 403 with words before the store is asked, nothing stored, the refusal kept for the operator ({(int)res.StatusCode} {error})");
        }
        async Task<(HttpStatusCode Code, string Body)> PostContext(string path, string json, string? credential)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (credential is not null) req.Headers.Add("Authorization", "Bearer " + credential);
            var res = await http.SendAsync(req);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        }
        var pinPost = await PostContext("/api/peers/context", "{\"v\":1,\"node\":{\"t\":\"playmap\",\"id\":\"pm1\",\"name\":\"Stage left\"},\"light\":\"green\",\"headline\":\"playing\"}", "4321");
        Check(pinPost is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"operator\"}") && control.LastCredential == NodeCredential.Operator
              && control.Peers.Find("pm1", DateTime.UtcNow) is { Proof: NodeProof.Operator },
              $"web: the PIN posts a context for a node with no key, held as the operator's ({pinPost.Body})");
        var ctlPost = await PostContext("/api/peers/context", "{\"v\":1,\"node\":{\"t\":\"playmap\",\"id\":\"pm2\"},\"light\":\"green\",\"headline\":\"idle\"}", ctlSecret);
        Check(ctlPost is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"operator\"}") && control.LastCredential == NodeCredential.Operator,
              $"web: a control key proves the operator too ({ctlPost.Body})");
        var pinForKeyed = await PostContext("/api/peers/context", "{\"v\":1,\"node\":{\"t\":\"messenger\",\"id\":\"ag1\",\"name\":\"AGM\"},\"light\":\"red\",\"headline\":\"all wrong\"}", "4321");
        Check(pinForKeyed.Code == HttpStatusCode.Forbidden && JsonDocument.Parse(pinForKeyed.Body).RootElement.GetProperty("error").GetString() == "messenger ag1 speaks with its own key"
              && control.Peers.Find("ag1", DateTime.UtcNow) is { Headline: "2 action prompts open", Proof: NodeProof.Key },
              $"web: the PIN can't speak for a node that has its own key (403 with words), and its context is unchanged ({pinForKeyed.Body})");
        var heldBefore = control.Peers.All(DateTime.UtcNow).Count;
        var misdirected = await PostContext("/api/context", agContext, nodeSecret);
        Check(misdirected.Code == HttpStatusCode.MethodNotAllowed && misdirected.Body.Contains("/api/peers/context") && control.Peers.All(DateTime.UtcNow).Count == heldBefore,
              $"web: /api/context is only read — a context posted there is refused (405, pointing at /api/peers/context), never answered as if taken ({misdirected.Body})");
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/peers/context") { Content = new StringContent(agContext, Encoding.UTF8, "application/json") })
        {
            req.Headers.Add("Authorization", "Bearer " + readSecret);
            Check((await http.SendAsync(req)).StatusCode == HttpStatusCode.Forbidden, "web: a read key can't post a context");
        }

        // WebSocket: a key in the Authorization header (never the URL), the operator frame, a timed ping.
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer " + readSecret);
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?detail=1&cid=bus-test"), CancellationToken.None);
            var first = await Receive(ws);
            var t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await ws.SendAsync(Encoding.UTF8.GetBytes($"{{\"cmd\":\"ping\",\"t\":{t1}}}"), WebSocketMessageType.Text, true, CancellationToken.None);
            string pong = "";
            for (int i = 0; i < 5 && !pong.Contains("pong"); i++) pong = await Receive(ws);
            using var doc = JsonDocument.Parse(pong);
            Check(first.Contains("\"seq\"") && doc.RootElement.GetProperty("t").GetInt64() == t1 && doc.RootElement.GetProperty("t2").GetInt64() >= t1 - 5000,
                  "web: a header key opens the socket; a timed ping gets the server's times");
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
    }

    /// <summary>A fake Round 87 Patterns desk: greeting STATE, NONCE, the proof checked, the probe, receipts and CONTEXT.</summary>
    public static async Task PatternsDesk(Action<bool, string> Check)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var heard = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var paired = false;
        var desk = Task.Run(async () =>
        {
            using var c = await listener.AcceptTcpClientAsync();
            using var stream = c.GetStream();
            using var r = new StreamReader(stream);
            await using var w = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
            const string nonce = "00112233445566778899aabbccddeeff";
            await w.WriteLineAsync("STATE {\"video\":{\"file\":\"VT1.mp4\",\"positionMs\":1000,\"lengthMs\":60000,\"playing\":true,\"seq\":3}}");
            await w.WriteLineAsync("NONCE " + nonce);
            var name = "";
            try
            {
                for (var l = await r.ReadLineAsync(); l is not null; l = await r.ReadLineAsync())
                {
                    heard.Enqueue(l);
                    var id = "";
                    var line = l;
                    if (line.StartsWith('@')) { var sp = line.IndexOf(' '); id = line[..sp] + " "; line = line[(sp + 1)..]; }
                    if (line.StartsWith("HELLO ")) { name = line[6..]; continue; }
                    if (line.StartsWith("AUTH-PROOF "))
                    {
                        paired = line[11..] == PatternsLink.Proof("ab12-cd34", nonce, name);
                        await w.WriteLineAsync(paired ? "OK paired" : "ERR wrong token");
                        continue;
                    }
                    if (line.StartsWith("PING ")) { var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000; await w.WriteLineAsync($"OK {id}PONG {line[5..]} {now} {now}"); continue; }
                    if (line == "PING") { await w.WriteLineAsync("OK PONG"); continue; }
                    if (line == "CONTEXT")
                    {
                        await w.WriteLineAsync($"OK {id}{{\"v\":1,\"node\":{{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"Patterns FOH\"}},\"light\":\"green\",\"headline\":\"3 screens green\"}}");
                        continue;
                    }
                    if (line.StartsWith("VIDEO END")) { await w.WriteLineAsync($"OK {id}jumped to the last 10 s".Trim()); continue; }
                    await w.WriteLineAsync($"ERR {id}unknown word".Replace("  ", " "));
                }
            }
            catch (IOException) { }
        });

        using var link = new PatternsLink("127.0.0.1", port, "AB12CD34");
        var receipts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        link.Receipt += (line, ok, words) => receipts.Enqueue($"{ok}|{line}|{words}");
        link.Start();
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until && (link.Context is null || link.ClockOffset is null)) await Task.Delay(100);
        Check(paired && !heard.Any(l => l.Contains("AB12CD34") || l.StartsWith("AUTH ")), "patterns link: pairs by proof — the token never crosses the wire");
        Check(link.Pairing == PatternsPairing.Paired && link.Health == LinkHealth.Ok && link.SetAsideBeforePairing >= 1 && link.Video is null,
              $"patterns link: the greeting STATE came before OK paired, so it is set aside and no clip is read from it ({link.SetAsideBeforePairing} set aside, {link.Status})");
        Check(link.Context is { } ctx && ctx.Contains("3 screens green"), "patterns link: reads the desk's context");
        Check(link.ClockOffset is { } off && Math.Abs(off.TotalMilliseconds - 5000) < 1000, $"patterns link: measures the desk's clock offset ({link.ClockOffset})");
        Check(link.EndIn(10) is null, "patterns link: sends a command");
        until = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < until && receipts.IsEmpty) await Task.Delay(50);
        Check(receipts.TryPeek(out var rc) && rc.StartsWith("True|VIDEO END 10|jumped"), $"patterns link: the desk's answer comes back matched to the command ({rc})");
        link.Dispose();
        listener.Stop();
        try { await desk.WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { }
    }

    /// <summary>
    /// A desk on loopback for one connection, scripted: it greets with its first lines, answers each line it hears with
    /// <c>answer</c>'s lines (none = silence) and says more when the test asks. Every line it heard is kept.
    /// </summary>
    sealed class ScriptedDesk : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly object _gate = new();
        private readonly Task _run;
        private StreamWriter? _writer;
        private TcpClient? _client;
        public System.Collections.Concurrent.ConcurrentQueue<string> Heard { get; } = new();
        public int Port { get; }

        public ScriptedDesk(IReadOnlyList<string> greeting, Func<string, IEnumerable<string>> answer)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _run = Task.Run(async () =>
            {
                try
                {
                    _client = await _listener.AcceptTcpClientAsync();
                    var stream = _client.GetStream();
                    var reader = new StreamReader(stream);
                    lock (_gate) _writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
                    foreach (var g in greeting) Say(g);
                    for (var l = await reader.ReadLineAsync(); l is not null; l = await reader.ReadLineAsync())
                    {
                        Heard.Enqueue(l);
                        foreach (var a in answer(l)) Say(a);
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            });
        }

        public void Say(string line)
        {
            lock (_gate) _writer?.WriteLine(line);
        }

        /// <summary>A line the link wrote that carries the token in clear, or plain AUTH of any kind.</summary>
        public bool HeardSecret(string token) => Heard.Any(l => l.Contains(token, StringComparison.Ordinal) || l.StartsWith("AUTH ", StringComparison.Ordinal));

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _client?.Dispose();
            try { await _run.WaitAsync(TimeSpan.FromSeconds(3)); } catch (TimeoutException) { }
        }
    }

    /// <summary>
    /// PatternsLink's pairing rules against scripted desks: plain AUTH only to a loopback socket (the seam plays a desk on
    /// another PC from loopback), a proof only for a NONCE of 32 lowercase hex, and nothing taken before the desk pairs.
    /// The scenarios run side by side; their checks are reported in order.
    /// </summary>
    public static async Task PatternsPairingRules(Action<bool, string> Check)
    {
        const string token = "AB12CD34";
        const string nonce = "00112233445566778899aabbccddeeff";
        static bool Remote(IPAddress _) => false;
        static async Task<bool> Until(Func<bool> cond, int ms = 5000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until && !cond()) await Task.Delay(25);
            return cond();
        }
        // A clock sample the link would take if it read it: sent now, the desk 5 s ahead.
        static string AheadBy5s()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return FormattableString.Invariant($"OK PONG {now} {now + 5000} {now + 5000}");
        }
        static string State(string file) => "STATE {\"video\":{\"file\":\"" + file + "\",\"positionMs\":1000,\"lengthMs\":60000,\"playing\":true,\"seq\":1}}";
        static string Pong(string ping) => "OK @hi PONG " + ping["@hi PING ".Length..] + " " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        // A desk older than Show Bus 1.1: no NONCE, "@hi" refused, the token taken as plain AUTH.
        static IEnumerable<string> Older(string l) => l.StartsWith("@hi ", StringComparison.Ordinal) ? new[] { "ERR unknown command: @hi" }
            : l == "AUTH " + token ? new[] { "OK paired" } : l == "PING" ? new[] { "OK", State("older.mp4") } : new[] { "OK" };
        // Past the link's wait for a NONCE (1.5 s from the connection), with margin.
        const int pastTheWait = 2300;

        async Task<List<(bool, string)>> OlderOffThisPc()
        {
            var r = new List<(bool, string)>();
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4") }, Older);
            using var link = new PatternsLink("127.0.0.1", desk.Port, token, Remote);
            link.Start();
            await Task.Delay(pastTheWait);
            r.Add((!desk.HeardSecret(token) && !desk.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal)) && desk.Heard.Contains("HELLO Stagewatch"),
                $"patterns pairing: an older desk on another PC (no NONCE) never hears AUTH or the token ({string.Join(" | ", desk.Heard)})"));
            r.Add((link.Pairing == PatternsPairing.None && link.Health == LinkHealth.Starting
                   && link.Status == $"127.0.0.1:{desk.Port} is older than Show Bus 1.1 and pairs only by sending its token in clear — update Patterns; the token stays on this PC",
                $"patterns pairing: that link is amber and names the desk and why ({link.Health}: {link.Status})"));
            r.Add((link.Video is null && link.Value is null && link.SetAsideBeforePairing >= 1 && link.Context is null && !desk.Heard.Contains("@ctx CONTEXT"),
                $"patterns pairing: nothing it says is taken — its STATE set aside ({link.SetAsideBeforePairing}), no context asked"));
            r.Add((link.EndIn(10) is { } end && end.Contains("hasn't paired") && link.Play() is { } play && play.Contains("hasn't paired")
                   && !desk.Heard.Any(l => l.Contains("VIDEO", StringComparison.Ordinal)),
                $"patterns pairing: commands to an unpaired desk are refused here with the reason, nothing written ({link.EndIn(10)})"));
            return r;
        }

        async Task<List<(bool, string)>> OlderOnThisPc()
        {
            var r = new List<(bool, string)>();
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4") }, Older);
            using var link = new PatternsLink("127.0.0.1", desk.Port, token);
            link.Start();
            var paired = await Until(() => link.Pairing == PatternsPairing.Paired);
            r.Add((paired && desk.Heard.Contains("AUTH " + token) && link.Health == LinkHealth.Ok && !desk.Heard.Any(l => l.Contains("STATUS", StringComparison.Ordinal)),
                $"patterns pairing: the same older desk on this PC (a loopback socket) gets the token as AUTH and pairs; it answers untagged, so it is not asked for its STATE ({link.Status})"));
            desk.Say(State("after.mp4"));
            r.Add((await Until(() => link.Video is { File: "after.mp4" }) && link.SetAsideBeforePairing >= 1,
                $"patterns pairing: a STATE after OK paired is read; the greeting before it was not ({link.Video?.File})"));
            return r;
        }

        async Task<List<(bool, string)>> NonceShapes()
        {
            var r = new List<(bool, string)>();
            const string short31 = "00112233445566778899aabbccddeef";
            const string upper = "00112233445566778899AABBCCDDEEFF";
            await using var desk31 = new ScriptedDesk(new[] { State("greeting.mp4"), "NONCE " + short31 }, Older);
            await using var deskUp = new ScriptedDesk(new[] { State("greeting.mp4"), "NONCE " + upper }, l => l.StartsWith("AUTH-PROOF ", StringComparison.Ordinal) ? new[] { "OK paired" } : Array.Empty<string>());
            // The default seam: both desks are on this PC, where plain AUTH could go — but not on a connection that heard a NONCE.
            using var link31 = new PatternsLink("127.0.0.1", desk31.Port, token);
            using var linkUp = new PatternsLink("127.0.0.1", deskUp.Port, token);
            link31.Start();
            linkUp.Start();
            await Task.Delay(pastTheWait);
            r.Add((!desk31.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal)) && !desk31.HeardSecret(token) && link31.Pairing == PatternsPairing.None
                   && link31.Status.Contains("not 32 lowercase hex") && link31.Health == LinkHealth.Starting,
                $"patterns pairing: a 31-hex NONCE is not proved, and the connection that heard it never gets plain AUTH ({link31.Status})"));
            r.Add((!deskUp.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal)) && linkUp.Pairing == PatternsPairing.None && linkUp.Status.Contains("not 32 lowercase hex"),
                $"patterns pairing: an upper-case NONCE is not proved ({string.Join(" | ", deskUp.Heard)})"));
            deskUp.Say("NONCE " + nonce);
            r.Add((await Until(() => linkUp.Pairing == PatternsPairing.Paired) && deskUp.Heard.Count(l => l.StartsWith("AUTH", StringComparison.Ordinal)) == 1
                   && deskUp.Heard.Contains("AUTH-PROOF " + PatternsLink.Proof(token, nonce, "Stagewatch")) && !deskUp.HeardSecret(token),
                $"patterns pairing: the desk's next NONCE, 32 lowercase hex, gets the one proof and pairs ({linkUp.Status})"));
            r.Add((PatternsLink.IsProvableNonce(nonce) && !PatternsLink.IsProvableNonce(short31) && !PatternsLink.IsProvableNonce(upper)
                   && !PatternsLink.IsProvableNonce(nonce + "0") && !PatternsLink.IsProvableNonce(" " + nonce[1..]) && !PatternsLink.IsProvableNonce(nonce[..31] + "g"),
                "patterns pairing: only exactly 32 lowercase hex is a nonce this link proves"));
            return r;
        }

        async Task<List<(bool, string)>> NothingBeforePaired()
        {
            var r = new List<(bool, string)>();
            // A desk on another PC that answers the proof only after a STATE, a receipt, a clock sample and a context:
            // all of them came before OK paired.
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4"), "NONCE " + nonce }, l => l.StartsWith("AUTH-PROOF ", StringComparison.Ordinal)
                ? new[] { State("early.mp4"), "OK @s1 jumped to the last 10 s", AheadBy5s(),
                          "OK @ctx {\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"forged\"},\"light\":\"green\",\"headline\":\"forged\"}", "OK paired" }
                : Array.Empty<string>());
            using var link = new PatternsLink("127.0.0.1", desk.Port, token, Remote);
            var receipts = new System.Collections.Concurrent.ConcurrentQueue<string>();
            link.Receipt += (line, ok, words) => receipts.Enqueue(line + "|" + words);
            link.Start();
            var paired = await Until(() => link.Pairing == PatternsPairing.Paired);
            r.Add((paired && desk.Heard.Contains("AUTH-PROOF " + PatternsLink.Proof(token, nonce, "Stagewatch")) && !desk.HeardSecret(token),
                $"patterns pairing: a desk on another PC pairs by proof alone ({link.Status})"));
            r.Add((link.Video is null && link.ClockOffset is null && link.Context is null && receipts.IsEmpty && link.SetAsideBeforePairing >= 5,
                $"patterns pairing: a STATE, a receipt, a clock sample and a context before OK paired move nothing ({link.SetAsideBeforePairing} set aside; clip {link.Video?.File ?? "none"}, offset {link.ClockOffset})"));
            desk.Say(State("late.mp4"));
            r.Add((await Until(() => link.Video is { File: "late.mp4" }) && link.Health == LinkHealth.Ok && link.Status.StartsWith("late.mp4", StringComparison.Ordinal),
                $"patterns pairing: the first STATE after OK paired is the one read ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> OpenDeskOffThisPc(string held)
        {
            var r = new List<(bool, string)>();
            // A Show Bus 1.1 desk with no token: no NONCE, so its answer to the probe says it asks for none.
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4") }, l =>
                l.StartsWith("@hi PING ", StringComparison.Ordinal) ? new[] { Pong(l) }
                : l == "@ctx CONTEXT" ? new[] { "OK @ctx {\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"open1\"},\"light\":\"amber\",\"headline\":\"open desk\"}" }
                : Array.Empty<string>());
            using var link = new PatternsLink("127.0.0.1", desk.Port, held, Remote);
            link.Start();
            var open = await Until(() => link.Pairing == PatternsPairing.Open);
            desk.Say(State("open.mp4"));
            var read = await Until(() => link.Video is { File: "open.mp4" } && link.Context is not null);
            var what = held.Length > 0 ? "holding a token" : "holding none";
            r.Add((open && read && !desk.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal)) && (held.Length == 0 || !desk.HeardSecret(held)) && link.SetAsideBeforePairing >= 1,
                $"patterns pairing: an open 1.1 desk on another PC ({what}) is followed once its answer to the probe shows it asks for no key; no AUTH, the greeting set aside ({link.Status})"));
            r.Add((held.Length > 0
                    ? link.Health == LinkHealth.Starting && link.Status.EndsWith("· asks for no key — the token stays on this PC", StringComparison.Ordinal)
                    : link.Health == LinkHealth.Ok && link.Status.EndsWith("· asks this link for no key", StringComparison.Ordinal),
                $"patterns pairing: it is {(held.Length > 0 ? "amber while this link holds a token it never asked for" : "green, and says it asks for no key")} ({link.Health}: {link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> TokenDeskNoTokenHere()
        {
            var r = new List<(bool, string)>();
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4"), "NONCE " + nonce }, l =>
                l.StartsWith("@hi PING ", StringComparison.Ordinal) ? new[] { Pong(l) } : l.StartsWith("PING ", StringComparison.Ordinal) ? new[] { State("still.mp4") } : Array.Empty<string>());
            using var link = new PatternsLink("127.0.0.1", desk.Port, "");
            link.Start();
            await Task.Delay(pastTheWait);
            r.Add((link.Pairing == PatternsPairing.None && link.Health == LinkHealth.Starting && link.Status.Contains("asks for its pairing token")
                   && link.Video is null && !desk.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal)) && !desk.Heard.Contains("@ctx CONTEXT"),
                $"patterns pairing: a desk that asks for a proof, with no token here, stays unpaired and amber with the reason; nothing read, no context asked ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> OlderOffThisPcNoTokenHere()
        {
            var r = new List<(bool, string)>();
            // Neither side holds a token: the older desk off this PC asks for nothing, and is still not followed.
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4") }, Older);
            using var link = new PatternsLink("127.0.0.1", desk.Port, "", Remote);
            link.Start();
            await Task.Delay(pastTheWait);
            desk.Say(State("pushed.mp4"));
            await Task.Delay(300);
            r.Add((link.Pairing == PatternsPairing.None && link.Health == LinkHealth.Starting && link.Video is null && link.SetAsideBeforePairing >= 2
                   && !desk.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal))
                   && link.Status == $"127.0.0.1:{desk.Port} is older than Show Bus 1.1 and pairs only on its own PC — update Patterns; nothing is taken from it until it pairs",
                $"patterns pairing: an older desk on another PC is not followed even when neither side holds a token; amber, its STATEs set aside ({link.SetAsideBeforePairing}, {link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> SilentOffThisPc()
        {
            var r = new List<(bool, string)>();
            // A desk on another PC that sends no NONCE and never answers the probe: nothing secret goes, and it is called
            // older only once the probe has gone unanswered (3 s), not at the end of the wait for a NONCE (1.5 s).
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4") }, _ => Array.Empty<string>());
            using var link = new PatternsLink("127.0.0.1", desk.Port, token, Remote);
            var since = System.Diagnostics.Stopwatch.StartNew();
            link.Start();
            await Task.Delay(2000);
            var early = link.Status;
            var earlyInTime = since.ElapsedMilliseconds < 3000;   // read before the probe could have timed out
            r.Add((!earlyInTime || (!early.Contains("older", StringComparison.Ordinal) && link.Health == LinkHealth.Starting && link.Pairing == PatternsPairing.None),
                $"patterns pairing: past the wait for a NONCE, a silent desk off this PC is still pairing, not yet called older ({early})"));
            var older = $"127.0.0.1:{desk.Port} is older than Show Bus 1.1 and pairs only by sending its token in clear — update Patterns; the token stays on this PC";
            var named = await Until(() => link.Status == older, 4000);
            r.Add((named && link.Health == LinkHealth.Starting && link.Pairing == PatternsPairing.None && !desk.HeardSecret(token)
                   && !desk.Heard.Any(l => l.StartsWith("AUTH", StringComparison.Ordinal)) && link.Video is null,
                $"patterns pairing: once the probe goes unanswered, the silent desk is amber, named and told to update; no AUTH, nothing read ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> WrongProof()
        {
            var r = new List<(bool, string)>();
            const string fresh = "ffeeddccbbaa99887766554433221100";
            // A 1.1 desk on another PC that refuses the proof as Patterns does: its words, then a fresh NONCE. It leaves the
            // probe unanswered, so the refusal arrives while the probe is still out.
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4"), "NONCE " + nonce }, l => l.StartsWith("AUTH-PROOF ", StringComparison.Ordinal)
                ? new[] { "ERR wrong token — the pairing token is on the desk's Remote page, TRUST", "NONCE " + fresh, State("refused.mp4") }
                : Array.Empty<string>());
            using var link = new PatternsLink("127.0.0.1", desk.Port, "ZZ99YY88", Remote);
            link.Start();
            var red = await Until(() => link.Health == LinkHealth.Error);
            await Task.Delay(pastTheWait);
            r.Add((red && link.Health == LinkHealth.Error && link.Status.Contains("wrong token", StringComparison.Ordinal) && link.Pairing == PatternsPairing.None
                   && desk.Heard.Count(l => l.StartsWith("AUTH", StringComparison.Ordinal)) == 1 && desk.Heard.Contains("AUTH-PROOF " + PatternsLink.Proof("ZZ99YY88", nonce, "Stagewatch")),
                $"patterns pairing: a refused proof turns the link red; the desk's fresh NONCE gets no second proof ({link.Health}: {link.Status}; {desk.Heard.Count(l => l.StartsWith("AUTH", StringComparison.Ordinal))} proof)"));
            r.Add((link.Video is null && link.SetAsideBeforePairing >= 2 && !desk.Heard.Contains("@ctx CONTEXT") && !desk.Heard.Contains("@st STATUS") && !desk.HeardSecret("ZZ99YY88"),
                $"patterns pairing: nothing a desk that refused the proof says is read, and nothing is asked of it ({link.SetAsideBeforePairing} set aside)"));
            return r;
        }

        async Task<List<(bool, string)>> WrongTokenOnThisPc()
        {
            var r = new List<(bool, string)>();
            // An older desk on this PC and a token that is not its own: the one plain AUTH is refused, and the link stays
            // unread although the desk would answer this PC's lines.
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4") }, l => l.StartsWith("@hi ", StringComparison.Ordinal) ? new[] { "ERR unknown command: @hi" }
                : l.StartsWith("AUTH ", StringComparison.Ordinal) ? new[] { "ERR wrong token — the pairing token is on the desk's Remote page, TRUST" }
                : l == "PING" ? new[] { "OK", State("older.mp4") } : new[] { "OK" });
            using var link = new PatternsLink("127.0.0.1", desk.Port, "STALE1");
            link.Start();
            var red = await Until(() => link.Health == LinkHealth.Error);
            desk.Say(State("pushed.mp4"));
            await Task.Delay(300);
            r.Add((red && link.Status.Contains("wrong token", StringComparison.Ordinal) && link.Pairing == PatternsPairing.None && link.Video is null
                   && link.SetAsideBeforePairing >= 2 && desk.Heard.Count(l => l.StartsWith("AUTH", StringComparison.Ordinal)) == 1 && link.EndIn(10) is { } why && why.Contains("hasn't paired"),
                $"patterns pairing: a wrong token on this PC is red and unread — one AUTH, its STATEs set aside, commands refused here ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> AskedAfterPairing()
        {
            var r = new List<(bool, string)>();
            // A 1.1 desk that pushes nothing after its greeting (a still desk): the link asks for its STATE once it paired.
            await using var desk = new ScriptedDesk(new[] { State("greeting.mp4"), "NONCE " + nonce }, l =>
                l.StartsWith("@hi PING ", StringComparison.Ordinal) ? new[] { Pong(l) }
                : l.StartsWith("AUTH-PROOF ", StringComparison.Ordinal) ? new[] { "OK paired" }
                : l == "@st STATUS" ? new[] { "OK @st " + State("asked.mp4")["STATE ".Length..] }
                : Array.Empty<string>());
            using var link = new PatternsLink("127.0.0.1", desk.Port, token, Remote);
            link.Start();
            var read = await Until(() => link.Video is { File: "asked.mp4" });
            var lines = desk.Heard.ToList();
            var proof = lines.FindIndex(l => l.StartsWith("AUTH-PROOF ", StringComparison.Ordinal));
            r.Add((read && link.Pairing == PatternsPairing.Paired && link.Health == LinkHealth.Ok && lines.Count(l => l == "@st STATUS") == 1
                   && proof >= 0 && lines.IndexOf("@st STATUS") > proof,
                $"patterns pairing: once paired, a 1.1 desk is asked for its STATE once and the clip is read without waiting for a push ({link.Status})"));
            return r;
        }

        var scenarios = new[]
        {
            OlderOffThisPc(), OlderOnThisPc(), NonceShapes(), NothingBeforePaired(), OpenDeskOffThisPc(token), OpenDeskOffThisPc(""), TokenDeskNoTokenHere(),
            OlderOffThisPcNoTokenHere(), SilentOffThisPc(), WrongProof(), WrongTokenOnThisPc(), AskedAfterPairing(),
        };
        await Task.WhenAll(scenarios);
        foreach (var scenario in scenarios)
            foreach (var (ok, name) in scenario.Result) Check(ok, name);
        Check(PatternsLink.IsLoopbackAddress(IPAddress.Loopback) && PatternsLink.IsLoopbackAddress(IPAddress.IPv6Loopback)
              && PatternsLink.IsLoopbackAddress(IPAddress.Loopback.MapToIPv6()) && !PatternsLink.IsLoopbackAddress(IPAddress.Parse("10.0.0.5"))
              && !PatternsLink.IsLoopbackAddress(IPAddress.Parse("::ffff:10.0.0.5")),
              "patterns pairing: the default seam calls a socket loopback by its address alone (IPv4, IPv6, IPv4-mapped)");
    }

    /// <summary>
    /// Stagewatch's own context within §14's sizes (Show Bus §6): its panic, air and transport commands first, a department's GO among
    /// them; a context within its sizes sent whole; every text within its field, cut with "…" and never inside a surrogate pair, an id
    /// and a kind clipped, an unknown light grey, and its name the hello's; a crowded room trimmed in §6's order, each step only until it
    /// fits, with one grey context:trimmed item, last, whose words are what it left out; the last resort only when nothing else is left,
    /// amber before red at each of its steps, within the bytes even with every peer and name at its longest; past a count, the counts
    /// kept and no link naming an item left out.
    /// </summary>
    static void ContextSizes(Action<bool, string> Check)
    {
        var t0 = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);

        // §6's order of commands.
        Check(ShowContext.Commands.SkipWhile(c => c.Air).All(c => !c.Air)
              && new[] { "blackout", "next", "start / pause", "signal go all", "signal go <dept> <cue>" }.All(v => ShowContext.Commands.Any(c => c.Verb == v && c.Air)),
            "context: its panic, air and transport commands come first, blackout, next, start / pause and a department's GO among them ("
            + string.Join(", ", ShowContext.Commands.TakeWhile(c => c.Air).Select(c => c.Verb)) + ")");

        // Within its sizes, whole.
        var quiet = Crowded(t0, windows: 4, links: 2, departments: 3, recent: 5, peers: 2, words: 20, letter: 'x');
        var whole = Read(ShowContext.Build(quiet));
        Check(whole.Items.Count == ShowContext.Assess(quiet).Items.Count && whole.Items.All(i => i.Id != ShowContext.TrimmedId)
              && whole.Links.Count == quiet.Links.Count && whole.Recent.Count == 5 && whole.Commands.Count == ShowContext.Commands.Count && whole.Peers.Count == 2,
            "context: a context within its sizes is sent whole, with no context:trimmed item");

        // Every text within its field: cut with "…" at the field's size, a surrogate pair never split.
        static bool Whole(string s) => Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(s)) == s;
        var emoji = string.Concat(Enumerable.Repeat("😀", 150));
        var small = Crowded(t0, windows: 0, links: 1, departments: 1, recent: 1, peers: 1, words: 300, letter: 'x');
        var longest = small with { Name = "Hall\u0007 A\nx" + string.Concat(Enumerable.Repeat("😀", 50)), Frame = small.Frame with { Title = emoji } };
        var cut = Read(ShowContext.Build(longest));
        var timer = cut.Items.First(i => i.Id == "timer");
        var linkState = cut.Items.First(i => i.Id == "link:l0").State;
        var deptState = cut.Items.First(i => i.Id.StartsWith("cue:", StringComparison.Ordinal)).State;
        Check(timer.Label.Length <= ShowContext.LabelChars && timer.Label.EndsWith('…') && Whole(timer.Label)
              && cut.Headline.Length <= ShowContext.HeadlineChars && cut.Headline.EndsWith('…') && Whole(cut.Headline),
            $"context: a title of emoji is cut within its label and the headline, never inside a pair ({timer.Label.Length}, {cut.Headline.Length})");
        Check(linkState.Length == ShowContext.StateChars && linkState.EndsWith('…') && deptState.Length == ShowContext.StateChars && deptState.EndsWith('…')
              && cut.Recent is [{ Length: ShowContext.RecentChars } line] && line.EndsWith('…')
              && cut.Peers is [{ Name.Length: ShowBusText.NameChars, Headline.Length: ShowContext.HeadlineChars }],
            $"context: a link's and a department's state, a recent line and a peer's name and headline are cut to their sizes ({linkState.Length}, "
            + $"{deptState.Length}, {cut.Recent.FirstOrDefault()?.Length})");
        Check(cut.Name == ShowBusHello.Name(longest.Name) && cut.Name.StartsWith("Hall Ax😀", StringComparison.Ordinal) && cut.Name.Length == ShowBusText.NameChars - 1
              && Whole(cut.Name) && ShowBusHello.Main("3f9c2a1b7d0e", "c3d4e5f6", longest.Name, "FOH-PC", new HelloDoors(9924, true, null, 0, false, 0, false), false, 1)
                  .Hello?.Name == cut.Name,
            $"context: its name is the one its hello says — no control characters, at most a hello's characters, a pair never split ({cut.Name.Length})");
        var odd = Crowded(t0, windows: 0, links: 0, departments: 0, recent: 0, peers: 0, words: 10, letter: 'x');
        var oddLink = new ContextItem("link:" + new string('i', 65), "link-" + new string('k', 35), "Odd", "blue", "up");
        var oddSent = Read(ShowContext.Build(odd with { Links = [oddLink] }));
        Check(oddSent.Items.FirstOrDefault(i => i.Label == "Odd") is { } oddItem && oddItem.Id == oddLink.Id[..ShowContext.IdChars]
              && oddItem.Kind == oddLink.Kind[..ShowContext.KindChars] && oddItem.Light == Light.Grey
              && oddSent.Links is [{ From: "timer" } oddRow] && oddRow.To == oddItem.Id && oddRow.Light == Light.Grey,
            "context: a link's id and kind are clipped to their sizes with no mark, an unknown light is sent grey, and its links row names the id as sent");

        // A crowded room (64 windows, 8 links, 30 departments, 20 long recent lines, 32 peers): trimmed in §6's order, no red or amber touched.
        var room = Crowded(t0, windows: 64, links: 8, departments: 30, recent: 20, peers: 32, words: 160, letter: '€', name: '€');
        var crowded = Read(ShowContext.Build(room));
        var (steps, words) = LeftOut(room, crowded);
        var trimmed = crowded.Items.Where(i => i.Id == ShowContext.TrimmedId).ToList();
        Check(crowded.Bytes <= ShowContext.MaxBytes && crowded.Items.Count <= ShowContext.MaxItems && crowded.Links.Count <= ShowContext.MaxLinks,
            $"context: a crowded room's context is within its bytes, items and links ({crowded.Bytes} bytes, {crowded.Items.Count} items, {crowded.Links.Count} links)");
        Check(OutOfOrder(steps) is null, "context: a crowded room's context is trimmed in §6's order, each step only from its end (" + OutOfOrder(steps) + ")");
        Check(trimmed is [{ Light: Light.Grey, Kind: "health", Label: "Context" } one] && one.State == words && crowded.Items[^1] == one && words.Length > 0,
            $"context: one grey context:trimmed item, last, says what it left out ({string.Join(" | ", trimmed.Select(x => x.State))} / {words})");
        bool All(string step) => steps.First(x => x.Name == step).Out.All(o => o);
        bool None(string step) => steps.First(x => x.Name == step).Out.All(o => !o);
        Check(All("recent lines") && All("grey links") && All("other links") && All("other commands") && !None("grey items")
              && new[] { "green items", "amber states", "red states", "amber items", "red items", "panic, air and transport commands" }.All(None),
            "context: in a crowded room the recent lines, the links and the other commands go, then grey items, until it fits; no green, amber or red "
            + "item and no panic, air or transport command is touched (" + words + ")");

        // Just over: only as much as it takes.
        ContextInput? over = null;
        for (var k = 0; k <= 20 && over is null; k++)
        {
            var next = Crowded(t0, windows: 20, links: 8, departments: 20, recent: k, peers: 32, words: 160, letter: '€');
            if (Read(ShowContext.Build(next)).Items.Any(i => i.Id == ShowContext.TrimmedId)) over = next;
        }
        var justWords = "never over";
        var justEnough = false;
        if (over is not null)
        {
            var (justSteps, words1) = LeftOut(over, Read(ShowContext.Build(over)));
            justWords = words1;
            justEnough = justSteps[0].Out.Count(o => o) is 1 or 2 && justSteps.Skip(1).All(step => step.Out.All(o => !o)) && OutOfOrder(justSteps) is null;
        }
        Check(justEnough, $"context: a context just over its bytes leaves out the oldest recent line or two and nothing else ({justWords})");
        // Room for the context:trimmed item: one more link whose state grows six bytes at a time moves the room left after the oldest line
        // goes through every size a recent line frees, and every one is sent within the bytes, with its one context:trimmed item.
        var sizes = new List<int>();
        if (over is not null)
            for (var grow = 0; grow <= ShowContext.StateChars; grow++)
            {
                var padded = over with { Links = [.. over.Links, new ContextItem("link:pad", "link", "Pad", Light.Green, new string('€', grow))] };
                var sentPadded = Read(ShowContext.Build(padded));
                sizes.Add(sentPadded.Items.Count(i => i.Id == ShowContext.TrimmedId) == 1 ? sentPadded.Bytes : int.MaxValue);
            }
        Check(sizes.Count > 0 && sizes.All(b => b <= ShowContext.MaxBytes) && sizes.Max() > ShowContext.MaxBytes - 200,
            $"context: whatever room trimming leaves, the context:trimmed item fits in it ({(sizes.Count > 0 ? sizes.Min() : 0)}–{(sizes.Count > 0 ? sizes.Max() : 0)} bytes)");

        // The last resort: every peer and name at its longest, sixty red links; reds' states cut short, then reds left out from the end,
        // and still within the bytes with the panic, air and transport commands kept.
        var reds = Enumerable.Range(0, 60).Select(i => new ContextItem("link:r" + i.ToString(CultureInfo.InvariantCulture), "link", new string('€', 100), Light.Red,
            new string('€', 200))).ToList();
        var bare = Crowded(t0, windows: 0, links: 0, departments: 0, recent: 20, peers: 32, words: 200, letter: '€', name: '€', heading: '€');
        var worst = bare with { Links = reds, ShowThreadStuck = true, Name = new string('€', 100), Frame = bare.Frame with { Title = new string('€', 300) } };
        var dire = Read(ShowContext.Build(worst));
        var (direSteps, direWords) = LeftOut(worst, dire);
        Check(dire.Bytes <= ShowContext.MaxBytes && OutOfOrder(direSteps) is null && !NoneOut("red states", direSteps) && !NoneOut("red items", direSteps)
              && NoneOut("panic, air and transport commands", direSteps) && dire.Items.Count(i => i.Light == Light.Red) > 0
              && dire.Items.Where(i => i.Id.StartsWith("link:r", StringComparison.Ordinal)).All(i => i.State.Length <= ShowContext.LastResortChars && i.State.EndsWith('…'))
              && dire.Items[^1] is { Id: ShowContext.TrimmedId } direTrimmed && direTrimmed.State == direWords,
            $"context: the last resort cuts reds' states short, then leaves reds out from the end, within the bytes with its peers and names at their longest "
            + $"({dire.Bytes} bytes; {direWords}; {OutOfOrder(direSteps)})");

        // The last resort, amber before red at each of its steps: twenty departments standing by (amber) and k red links beside 32 peers
        // whose headlines are at their longest past ASCII. As k grows the last resort cuts ambers' states, then reds', then leaves out
        // ambers, then reds; every k is sent within the bytes in §6's order, its context:trimmed item last, and each stage is reached with
        // reds there to be taken too early.
        var stages = new SortedSet<string>(StringComparer.Ordinal);
        string? broke = null;
        var lit = Crowded(t0, windows: 0, links: 0, departments: 20, recent: 20, peers: 32, words: 160, letter: '€', heading: '€');
        for (var k = 0; k <= 40 && broke is null; k++)
        {
            var mixed = lit with
            {
                Links = Enumerable.Range(0, k).Select(i => new ContextItem("link:r" + i.ToString(CultureInfo.InvariantCulture), "link", new string('€', 80), Light.Red,
                    new string('€', 200))).ToList(),
            };
            var sentMixed = Read(ShowContext.Build(mixed));
            var (mixedSteps, mixedWords) = LeftOut(mixed, sentMixed);
            if (sentMixed.Bytes > ShowContext.MaxBytes || OutOfOrder(mixedSteps) is not null
                || sentMixed.Items[^1] is not { Id: ShowContext.TrimmedId } mixedTrimmed || mixedTrimmed.State != mixedWords)
            {
                broke = $"{k} reds: {sentMixed.Bytes} bytes; {OutOfOrder(mixedSteps)}; {mixedWords}";
                continue;
            }
            bool Took(string step) => mixedSteps.First(x => x.Name == step).Out.Contains(true);
            if (k > 0 && Took("amber states") && !Took("red states")) stages.Add("1 amber states");
            if (Took("red states") && !Took("amber items")) stages.Add("2 red states");
            if (Took("amber items") && !Took("red items")) stages.Add("3 amber items");
            if (Took("red items") && !Took("panic, air and transport commands")) stages.Add("4 red items");
        }
        Check(broke is null && stages.SequenceEqual(["1 amber states", "2 red states", "3 amber items", "4 red items"]),
            "context: the last resort cuts every amber state before a red one and leaves out every amber item before a red one, each stage "
            + $"reached with reds there ({string.Join(", ", stages)}{(broke is null ? "" : "; broke at " + broke)})");

        // Past a count: 450 links. The counts are kept, the context:trimmed item among the items, and no link names an item left out.
        var many = Crowded(t0, windows: 0, links: 450, departments: 0, recent: 0, peers: 0, words: 10, letter: 'x');
        var counted = Read(ShowContext.Build(many));
        var ids = counted.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var manyWords = LeftOut(many, counted).Words;
        Check(counted.Items.Count == ShowContext.MaxItems && counted.Links.Count <= ShowContext.MaxLinks
              && counted.Links.All(row => ids.Contains(row.From) && ids.Contains(row.To))
              && counted.Items.Select(item => item.Id).Take(3).SequenceEqual(["timer", "schedule", "link:l0"]) && counted.Items[^2].Id == "link:l196"
              && counted.Items[^1] is { Id: ShowContext.TrimmedId } countedTrimmed && countedTrimmed.State == manyWords && manyWords == "left out: 253 links, 253 items",
            $"context: past its counts it keeps context.items items, the context:trimmed item among them, leaves out the last first, and no link "
            + $"names an item left out ({counted.Items.Count} items, {counted.Links.Count} links; {manyWords})");
    }

    static bool NoneOut(string step, List<(string Name, List<bool> Out, bool FromStart)> steps) => steps.First(x => x.Name == step).Out.All(o => !o);

    /// <summary>
    /// A room for Stagewatch's own context: a countdown running on time in a running order (its timer and schedule green),
    /// <paramref name="windows"/> windows not opened yet (grey), <paramref name="links"/> green links, <paramref name="departments"/>
    /// departments standing by (amber), <paramref name="recent"/> journal notes, numbered, and <paramref name="peers"/> peers. Every
    /// text is <paramref name="words"/> of <paramref name="letter"/> (a notes' 1000), a peer's name 100 of <paramref name="name"/> and its
    /// headline 300 of <paramref name="heading"/>: a letter past ASCII is six bytes as sent, escaped.
    /// </summary>
    internal static ContextInput Crowded(DateTime now, int windows, int links, int departments, int recent, int peers, int words, char letter,
        char name = 'p', char heading = 'h')
    {
        static string N(int i, string format = "D") => i.ToString(format, CultureInfo.InvariantCulture);
        var text = new string(letter, words);
        var frame = new ShowSnapshot
        {
            Mode = TimerMode.Countdown, State = TimerState.Running, Seconds = 300, Text = "05:00", Phase = TimerPhase.Normal, Title = "Keynote",
            ItemIndex = 0, ItemCount = 3, OffsetSeconds = 0, ScheduleText = "on time",
            Signals = Enumerable.Range(0, departments).Select(i => new SignalView(i + 1, SignalKind.Standby, "Dept" + N(i, "D2"), text, SignalState.Seen, false, 5, ""))
                .ToEquatableArray(),
        };
        var shown = Enumerable.Range(0, windows)
            .Select(i => new ScreenWindow { Id = "w" + N(i), Name = "window-" + N(i), Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 1 }).ToList();
        var items = ScreenWindows.ContextItems(shown, [])
            .Concat(Enumerable.Range(0, links).Select(i => new ContextItem("link:l" + N(i), "link", "Link " + N(i), Light.Green, text))).ToList();
        var journal = Enumerable.Range(0, recent).Select(i => new JournalEntry(now.AddSeconds(i - recent), JournalKind.Note, 0, N(i) + " " + new string(letter, 1000))).ToList();
        var heard = Enumerable.Range(0, peers)
            .Select(i => new PeerSummary("patterns", "pat" + N(i, "D4"), new string(name, 100), Light.Green, new string(heading, 300), "10.0.1." + N(i), 1, true)).ToList();
        return new ContextInput("abc123abc123", "Hall A", now, frame, false, items, heard, journal, ShowClock.For("UTC"), 0);
    }

    /// <summary>A context as sent: its UTF-8 bytes, its name and headline, its items in order, links, recent lines, commands' verbs and peers.</summary>
    internal sealed record Sent(int Bytes, string Name, string Headline, List<ContextItem> Items, List<(string From, string To, string Light)> Links,
        List<string> Recent, List<string> Commands, List<(string Name, string Headline)> Peers);

    internal static Sent Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string S(JsonElement e, string name) => e.GetProperty(name).GetString() ?? "";
        List<T> Each<T>(string name, Func<JsonElement, T> read) => root.GetProperty(name).EnumerateArray().Select(read).ToList();
        return new Sent(Encoding.UTF8.GetByteCount(json), S(root.GetProperty("node"), "name"), S(root, "headline"),
            Each("items", i => new ContextItem(S(i, "id"), S(i, "kind"), S(i, "label"), S(i, "light"), S(i, "state"))),
            Each("links", l => (S(l, "from"), S(l, "to"), S(l, "light"))), Each("recent", r => S(r, "text")), Each("commands", c => S(c, "verb")),
            Each("peers", p => (S(p, "name"), S(p, "headline"))));
    }

    /// <summary>
    /// What §6's trimming left out of <paramref name="x"/>'s context, as <paramref name="sent"/> shows it: its steps in §6's order, each
    /// with its elements in their list's order, true for one left out (a state: cut short) and whether its step takes them from the
    /// start of the list; and the context:trimmed words those counts make.
    /// </summary>
    internal static (List<(string Name, List<bool> Out, bool FromStart)> Steps, string Words) LeftOut(ContextInput x, Sent sent)
    {
        var all = ShowContext.Assess(x).Items;
        var kept = sent.Items.Where(i => i.Id != ShowContext.TrimmedId).ToDictionary(i => i.Id, StringComparer.Ordinal);
        List<bool> Items(string light) => all.Where(i => i.Light == light).Select(i => !kept.ContainsKey(i.Id)).ToList();
        List<bool> States(string light) => all.Where(i => i.Light == light && i.State.Length > ShowContext.LastResortChars && kept.ContainsKey(i.Id))
            .Select(i => kept[i.Id].State != i.State).ToList();
        var linked = sent.Links.Select(l => l.To).ToHashSet(StringComparer.Ordinal);
        List<bool> Links(bool grey) => x.Links.Where(l => (Light.Clean(l.Light) == Light.Grey) == grey).Select(l => !linked.Contains(l.Id)).ToList();
        var said = sent.Commands.ToHashSet(StringComparer.Ordinal);
        List<bool> Commands(bool air) => ShowContext.Commands.Where(c => c.Air == air).Select(c => !said.Contains(c.Verb)).ToList();
        var recent = x.Journal.TakeLast(ShowContext.MaxRecent)
            .Select(e => !sent.Recent.Any(r => r.Contains(" Note: " + e.Text[..e.Text.IndexOf(' ')] + " ", StringComparison.Ordinal))).ToList();
        var steps = new List<(string Name, List<bool> Out, bool FromStart)>
        {
            ("recent lines", recent, true),
            ("grey links", Links(true), false),
            ("other links", Links(false), false),
            ("other commands", Commands(false), false),
            ("grey items", Items(Light.Grey), false),
            ("green items", Items(Light.Green), false),
            ("amber states", States(Light.Amber), true),
            ("red states", States(Light.Red), true),
            ("amber items", Items(Light.Amber), false),
            ("red items", Items(Light.Red), false),
            ("panic, air and transport commands", Commands(true), false),
        };
        static string N(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
        var parts = new List<string>();
        if (recent.Count(o => o) is var r and > 0) parts.Add(N(r, "recent", "recent"));
        if (x.Links.Count - sent.Links.Count is var l and > 0) parts.Add(N(l, "link", "links"));
        if (ShowContext.Commands.Count - sent.Commands.Count is var c and > 0) parts.Add(N(c, "command", "commands"));
        if (all.Count - kept.Count is var i and > 0) parts.Add(N(i, "item", "items"));
        return (steps, parts.Count > 0 ? "left out: " + string.Join(", ", parts) : "");
    }

    /// <summary>
    /// Null when <paramref name="steps"/> keep §6's order: a step touches its list only once every step before it has left out all of
    /// its own, and takes from its list's end (the recent lines and the states from its start). Else what broke it.
    /// </summary>
    internal static string? OutOfOrder(List<(string Name, List<bool> Out, bool FromStart)> steps)
    {
        for (var k = 0; k < steps.Count; k++)
        {
            var (name, outs, fromStart) = steps[k];
            var shaped = fromStart ? outs.SkipWhile(o => o).All(o => !o) : outs.SkipWhile(o => !o).All(o => o);
            if (!shaped) return $"{name} not taken from the {(fromStart ? "start" : "end")} of their list";
            if (outs.Contains(true) && steps.Take(k).FirstOrDefault(p => p.Out.Contains(false)) is { Name: { } before })
                return $"{name} touched while some {before} were kept";
        }
        return null;
    }

    /// <summary>The shared approval queue's rules (Show Bus §11), pure.</summary>
    static void DeskRules(Action<bool, string> Check)
    {
        var desk = new ProposalDesk(() => "desk01", new NodeBindings());
        var t0 = new DateTime(2026, 10, 11, 19, 0, 0, DateTimeKind.Utc);
        var keyP = NodeCredential.Key("keyP");
        var keyA = NodeCredential.Key("keyA");
        string Risk(NodeRef target, string line) => target.Id == "desk01" ? "review" : ProposalDesk.RiskFromCommands(new[] { ("EYE FOCUS <thing>", "low"), ("VIDEO END <n>", "high") }, line);
        var (a, _, _) = desk.Submit("{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"Patterns FOH\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"EYE FOCUS screen 2\",\"reason\":\"Projector 1 is wrong\"}", keyP, Risk, t0);
        var (b, _, _) = desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"main\"},\"line\":\"message Wrap up\"}", keyA, Risk, t0);
        var (none, refused, forbidden) = desk.Submit("{\"from\":{\"t\":\"messenger\",\"id\":\"pat1\"},\"target\":{\"t\":\"main\"},\"line\":\"next\"}", keyA, Risk, t0);
        Check(a is { State: SharedProposalState.Pending, Risk: "low", TargetIsDesk: false } && b is { TargetIsDesk: true, Target.Id: "desk01" }
              && none is null && forbidden && refused == "this key speaks for messenger ag1 — make a key for messenger pat1",
              "desk: proposals queue with the target's own risk; a key can't speak for another node (forbidden, with words)");
        Check(ProposalDesk.RiskFromCommands(new[] { ("VIDEO END <n>", "high") }, "VIDEO PLAY") == "high" && ProposalDesk.RiskFromCommands(new[] { ("note <text>", "low") }, "note hi") == "low",
              "desk: an unknown verb is high risk");
        Check(desk.Collect("pat1", keyP, t0).Items!.Count == 0, "desk: nothing to collect before approval");
        desk.Approve(a!.Id, "operator", t0);
        Check(desk.Collect("pat1", keyA, t0).Error is not null, "desk: a node can't collect another node's approvals");
        var got = desk.Collect("pat1", keyP, t0.AddSeconds(2)).Items!;
        Check(got.Count == 1 && got[0].Line == "EYE FOCUS screen 2" && desk.Collect("pat1", keyP, t0.AddSeconds(3)).Items!.Count == 0, "desk: the target collects an approved proposal once");
        Check(desk.Result(a.Id, true, "focused screen 2", keyP, t0.AddSeconds(4)) is null && desk.Get(a.Id) is { State: SharedProposalState.Done, Result: "focused screen 2" },
              "desk: the target reports what it did");
        var (c, _, _) = desk.Submit("{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"VIDEO END 10\"}", keyP, Risk, t0);
        desk.Approve(c!.Id, "operator", t0);
        desk.Expire(t0.AddSeconds(61));
        Check(desk.Get(c.Id) is { State: SharedProposalState.Undelivered } && c.Risk == "high", "desk: approved but never collected becomes undelivered");
        var (d, _, _) = desk.Submit("{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"EYE RESET\"}", keyP, Risk, t0);
        desk.Reject(d!.Id, "operator", t0);
        Check(desk.Get(d.Id)!.State == SharedProposalState.Rejected && desk.Approve(d.Id, "x", t0) is null && desk.From("pat1").Count == 3, "desk: a rejected proposal stays rejected");

        // Certificate pins: first use, trusted, changed.
        var pins = new TlsPins();
        var fp1 = TlsPins.Fingerprint(new byte[] { 1, 2, 3 });
        var fp2 = TlsPins.Fingerprint(new byte[] { 4, 5, 6 });
        Check(TlsPins.IsFingerprint(fp1) && pins.Check("n1", fp1) == TlsPins.Verdict.FirstUse, "pins: an unknown node is first use");
        pins.Pin("n1", fp1);
        Check(pins.Check("n1", fp1) == TlsPins.Verdict.Trusted && pins.Check("n1", fp2) == TlsPins.Verdict.Changed && new TlsPins(pins.Snapshot()).Check("n1", fp1) == TlsPins.Verdict.Trusted,
              "pins: the pinned certificate is trusted, another is CHANGED, and pins survive a restart");
    }

    /// <summary>
    /// Show Bus §10: a certificate's whole span is 825 days, a day's backdating included, whatever the moment it is made, and an
    /// X.509 made over that window keeps it to the second. At a start one is replaced when fewer than 30 days remain or when it spans
    /// more than 825 days: a certificate an older Stagewatch made (826 days), Patterns' (1,826) or AGMessage's (825 and an hour) is
    /// replaced once, and the new one is kept until it nears its end.
    /// </summary>
    static void CertificateLives(Action<bool, string> Check)
    {
        var days825 = TimeSpan.FromDays(825);
        var t = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var moments = Enumerable.Range(0, 64).Select(i => t.AddMinutes(i * 437.3).AddTicks(i * 1_234_567L))
            .Concat([new DateTimeOffset(2027, 3, 28, 1, 30, 0, TimeSpan.FromHours(1)), new DateTimeOffset(2028, 2, 29, 23, 59, 59, 999, TimeSpan.FromHours(-8))]).ToList();
        Check(moments.All(now => CertificateLife.Window(now) is var w && w.NotAfter - w.NotBefore == days825 && w.NotBefore.Ticks % TimeSpan.TicksPerSecond == 0
                                 && w.NotBefore.Offset == TimeSpan.Zero && now - w.NotBefore <= TimeSpan.FromDays(1) && now - w.NotBefore > TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1)),
            "certificate: NotAfter − NotBefore is 825 days for any moment of making, NotBefore on the whole second no more than a day back");

        // An X.509 keeps whole seconds: made over the window from a moment with a fraction, its span is still exactly 825 days.
        var made = t.AddTicks(9_876_543);
        var (notBefore, notAfter) = CertificateLife.Window(made);
        using (var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))
        using (var cert = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Stagewatch life", key, System.Security.Cryptography.HashAlgorithmName.SHA256)
                   .CreateSelfSigned(notBefore, notAfter))
        {
            var from = new DateTimeOffset(cert.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            var to = new DateTimeOffset(cert.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            Check(to - from == days825 && from == notBefore && to == notAfter && !CertificateLife.NeedsRenewal(cert.NotBefore, cert.NotAfter, made),
                $"certificate: an X.509 made over the window spans exactly 825 days and is kept at its making ({from:O} to {to:O})");
        }

        // Fewer than 30 days left: renewed at 29, not at 31, nor at exactly 30.
        Check(CertificateLife.NeedsRenewal(notBefore, notAfter, notAfter - TimeSpan.FromDays(29))
              && !CertificateLife.NeedsRenewal(notBefore, notAfter, notAfter - TimeSpan.FromDays(31))
              && !CertificateLife.NeedsRenewal(notBefore, notAfter, notAfter - TimeSpan.FromDays(30))
              && CertificateLife.NeedsRenewal(notBefore, notAfter, notAfter - TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1))
              && CertificateLife.NeedsRenewal(notBefore, notAfter, notAfter + TimeSpan.FromDays(3)),
            "certificate: renewed at a start with 29 days left or after its end, kept with 31 or exactly 30");

        // Over-long certificates, each with 800 days left: replaced, whatever is left.
        (string Who, TimeSpan Span, string Days)[] longer =
        [
            ("an older Stagewatch's", TimeSpan.FromDays(826), "826"),
            ("Patterns'", TimeSpan.FromDays(1826), "1826"),
            ("AGMessage's", days825 + TimeSpan.FromHours(1), "826"),
            ("a second-too-long", days825 + TimeSpan.FromSeconds(1), "826"),
        ];
        foreach (var (who, span, said) in longer)
        {
            var end = t + TimeSpan.FromDays(800);
            var why = CertificateLife.Why(end - span, end, t);
            Check(CertificateLife.NeedsRenewal(end - span, end, t) && why == "it spanned " + said + " days, more than the 825 Apple devices and Show Bus 1.3 allow",
                $"certificate: {who} certificate, spanning {span} with 800 days left, is replaced at a start ({why})");
        }
        Check(!CertificateLife.NeedsRenewal(t - days825 + TimeSpan.FromDays(800), t + TimeSpan.FromDays(800), t),
            "certificate: one spanning exactly 825 days with 800 left is kept");

        // Once: a daily start from an older Stagewatch's certificate replaces it at the first start, and never again until fewer than 30
        // days of the new one remain.
        var (heldFrom, heldTo) = (t - TimeSpan.FromDays(26), t - TimeSpan.FromDays(26) + TimeSpan.FromDays(826));
        var replacedAt = new List<int>();
        for (var day = 0; day <= 900; day++)
        {
            var start = t + TimeSpan.FromDays(day) + TimeSpan.FromMinutes(day * 37 % 600);
            if (!CertificateLife.NeedsRenewal(heldFrom, heldTo, start)) continue;
            replacedAt.Add(day);
            (heldFrom, heldTo) = CertificateLife.Window(start);
        }
        Check(replacedAt.Count == 2 && replacedAt[0] == 0 && replacedAt[1] >= 794 && replacedAt[1] <= 796,
            "certificate: an older Stagewatch's certificate is replaced once, at the first start, and the new one only once fewer than 30 days remain ("
            + string.Join(", ", replacedAt) + ")");

        // The journal's words.
        Check(CertificateLife.Why(notBefore, notAfter, made) is null && CertificateLife.Why(notBefore, notAfter, notAfter) == "it had ended"
              && CertificateLife.Why(notBefore, notAfter, notAfter - TimeSpan.FromDays(29.9)) == "it had 29 days left, fewer than 30"
              && CertificateLife.Why(notBefore, notAfter, notAfter - TimeSpan.FromDays(1.5)) == "it had 1 day left, fewer than 30"
              && CertificateLife.Why(notBefore, notAfter, notAfter - TimeSpan.FromHours(5)) == "it had less than a day left, fewer than 30",
            "certificate: why one is replaced, in whole days that never reach the limit they are under");
        var words = CertificateLife.ReplacedWords("it spanned 826 days, more than the 825 Apple devices and Show Bus 1.3 allow", "sha256:" + new string('a', 4) + "0123" + new string('f', 56));
        Check(words.StartsWith("Certificate replaced: it spanned 826 days, more than the 825 Apple devices and Show Bus 1.3 allow. ", StringComparison.Ordinal)
              && words.Contains("fingerprint starts aaaa 0123 …", StringComparison.Ordinal) && !words.Contains(new string('f', 56), StringComparison.Ordinal)
              && words.Contains("says CERTIFICATE CHANGED once, until its operator accepts the new one", StringComparison.Ordinal),
            "certificate: the journal says why it was replaced, the new fingerprint's first 8 hex, and that each pinned peer says CERTIFICATE CHANGED once");

        // One grouping of the first hex everywhere a person compares them: the show log, the desk's line, Settings → Remote and a
        // presenter PC's row; the agent's row says when a Stagewatch makes a new one and asks the comparison before Trust.
        var fpX = "sha256:ab12cd34" + new string('e', 56);
        var row = SlidesAgent.ChangedWords(fpX);
        var why826 = CertificateLife.Why(t - TimeSpan.FromDays(26), t + TimeSpan.FromDays(800), t)!;
        Check(TlsPins.Shown(fpX) == "ab12 cd34" && TlsPins.Shown(fpX, 32) == "ab12 cd34 eeee eeee eeee eeee eeee eeee" && TlsPins.Shown("abc12") == "abc1 2"
              && TlsPins.Shown(fpX, 0) == "" && TlsPins.Shown(fpX, 100).Length == 64 + 15,
            "certificate: a fingerprint's first hex are shown in groups of four, its sha256: left off");
        Check(row.StartsWith("CERTIFICATE CHANGED — ", StringComparison.Ordinal) && row.Contains("starting ab12 cd34,", StringComparison.Ordinal)
              && !row.Contains("eeee", StringComparison.Ordinal) && !row.Contains("ab12cd34", StringComparison.Ordinal)
              && row.Contains("when it is reinstalled, at its first start of 2.19, and when one nears its end (about every 795 days)", StringComparison.Ordinal)
              && row.Contains("show log", StringComparison.Ordinal)
              && row.EndsWith("If these 8 hex match that Stagewatch's Settings → Remote, press Trust new certificate.", StringComparison.Ordinal),
            "certificate: a presenter PC's CERTIFICATE CHANGED row shows the new one's first 8 hex, says a reinstall, the 2.19 upgrade and a "
            + "renewal make one, and asks the comparison with Settings → Remote before Trust new certificate: " + row);
        Check(CertificateLife.ReplacedWords(why826, fpX).Contains("fingerprint starts ab12 cd34 …, as Settings → Remote shows it (its tooltip has all of it)", StringComparison.Ordinal)
              && CertificateLife.ReplacedLine(why826, fpX) == "Certificate replaced (" + why826 + "): apps and presenter PCs that pinned the old one say "
                 + "CERTIFICATE CHANGED once — the new one starts ab12 cd34 (Settings → Remote)",
            "certificate: the show log and the desk's line show the same 8 hex as the presenter PC's row");
    }

    /// <summary>
    /// Show Bus §10 on the disk, through <see cref="CertificateFiles"/> with a reversible seal and injected time, reads and saves, so it
    /// runs on every platform: an over-long certificate is replaced once and the new one kept, saved before it serves under the stored
    /// password; a new one that can't be made or saved leaves the old one serving and said; a stored pair that can't be opened is
    /// replaced instead of failing at every start; files that can't be read are never overwritten. Every word is public.
    /// </summary>
    static void CertificateOnDisk(Action<bool, string> Check)
    {
        using var folder = Scratch.Folder("cert", make: false);   // each case's folder is made by the files under test or by Store
        var root = folder.Path;
        static byte[] Seal(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();
        static byte[] Broken(byte[] b) => throw new System.Security.Cryptography.CryptographicException("sealed for another user");
        var t = new DateTimeOffset(2026, 11, 2, 18, 0, 0, TimeSpan.Zero);
        var makes = 0;
        System.Security.Cryptography.X509Certificates.X509Certificate2 Make(DateTimeOffset now, string cn = "CN=Stagewatch disk")
        {
            makes++;
            var (from, to) = CertificateLife.Window(now);
            return Made(from, to, cn);
        }
        static System.Security.Cryptography.X509Certificates.X509Certificate2 Made(DateTimeOffset from, DateTimeOffset to, string cn)
        {
            using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            return new System.Security.Cryptography.X509Certificates.CertificateRequest(cn, key, System.Security.Cryptography.HashAlgorithmName.SHA256).CreateSelfSigned(from, to);
        }
        // A pair in CertificateFiles' format, made by an older writer: its span and its password.
        static string Store(string folder, DateTimeOffset from, DateTimeOffset to, string pw)
        {
            using var c = Made(from, to, "CN=Stagewatch older");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "node.pfx"), c.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, pw));
            File.WriteAllBytes(Path.Combine(folder, "node.key"), Seal(Encoding.UTF8.GetBytes(pw)));
            return c.Thumbprint;
        }
        static TimeSpan Span(System.Security.Cryptography.X509Certificates.X509Certificate2 c) => c.NotAfter.ToUniversalTime() - c.NotBefore.ToUniversalTime();
        CertificateFiles Files(string folder, DateTimeOffset now, Func<byte[], byte[]>? unseal = null, Action<string, byte[]>? save = null,
                               Func<string, byte[]>? read = null, bool makeFails = false)
            => new(folder, Seal, unseal ?? Seal, n => makeFails ? throw new System.Security.Cryptography.CryptographicException("no key could be made") : Make(n))
            {
                Clock = () => now,
                Save = save ?? AtomicFile.Write,
                Read = read ?? File.ReadAllBytes,
            };
        static void DiskFull(string path, byte[] bytes) => throw new IOException("disk full");
        static bool Public(CertificateRead r, string pw) => (r.Note ?? "").Contains(pw, StringComparison.Ordinal) is false
            && (r.Line ?? "").Contains(pw, StringComparison.Ordinal) is false
            && (r.Certificate is not { } c || !(r.Note ?? "").Contains(TlsPins.Fingerprint(c.RawData)[15..31], StringComparison.Ordinal));

        // A first start makes one, saves it and says nothing; the next start keeps it.
        var first = Path.Combine(root, "first");
        var a = Files(first, t).Load();
        var a2 = Files(first, t + TimeSpan.FromDays(1)).Load();
        Check(a is { Certificate.HasPrivateKey: true, Replaced: null, Problem: null, Note: null, Line: null } && Span(a.Certificate) == CertificateLife.Span
              && File.Exists(Path.Combine(first, "node.pfx")) && File.Exists(Path.Combine(first, "node.key"))
              && a2 is { Replaced: null, Problem: null, Note: null } && a2.Certificate!.Thumbprint == a.Certificate.Thumbprint,
            "certificate on disk: a first start makes one of exactly 825 days, saved, and the next start keeps it, saying nothing");

        // An older Stagewatch's 826 days, with 800 left: replaced once through the real path, then kept.
        var older = Path.Combine(root, "older");
        var oldThumb = Store(older, t - TimeSpan.FromDays(26), t - TimeSpan.FromDays(26) + TimeSpan.FromDays(826), "older-password");
        var sealedBefore = File.ReadAllBytes(Path.Combine(older, "node.key"));
        var saved = new List<string>();
        var b = Files(older, t, save: (path, bytes) => { saved.Add(Path.GetFileName(path)); AtomicFile.Write(path, bytes); }).Load();
        var b2 = Files(older, t + TimeSpan.FromHours(20)).Load();
        var b3 = Files(older, t + TimeSpan.FromDays(400)).Load();
        Check(b is { Certificate: not null, Problem: null } && b.Replaced == "it spanned 826 days, more than the 825 Apple devices and Show Bus 1.3 allow"
              && b.Certificate.Thumbprint != oldThumb && Span(b.Certificate) == CertificateLife.Span
              && b.Note == CertificateLife.ReplacedWords(b.Replaced, TlsPins.Fingerprint(b.Certificate.RawData)) && b.Line!.StartsWith("Certificate replaced (it spanned 826 days", StringComparison.Ordinal)
              && saved.SequenceEqual(["node.pfx"]) && File.ReadAllBytes(Path.Combine(older, "node.key")).SequenceEqual(sealedBefore)
              && b2 is { Replaced: null, Problem: null, Note: null } && b2.Certificate!.Thumbprint == b.Certificate.Thumbprint
              && b3 is { Replaced: null, Problem: null } && b3.Certificate!.Thumbprint == b.Certificate.Thumbprint && Public(b, "older-password"),
            "certificate on disk: an older Stagewatch's 826-day certificate is replaced once at the first start, under its stored password "
            + "(node.key untouched: one swap of node.pfx), and the new one, exactly 825 days, is kept at the next starts");

        // The replacement can't be saved, or can't be made: the old one, which has not ended, keeps serving and it is said.
        var full = Path.Combine(root, "full");
        var fullThumb = Store(full, t - TimeSpan.FromDays(26), t - TimeSpan.FromDays(26) + TimeSpan.FromDays(826), "full-password");
        var pfxBefore = File.ReadAllBytes(Path.Combine(full, "node.pfx"));
        var c1 = Files(full, t, save: DiskFull).Load();
        var c2 = Files(full, t, makeFails: true).Load();
        var untouched = File.ReadAllBytes(Path.Combine(full, "node.pfx")).SequenceEqual(pfxBefore);
        var c3 = Files(full, t + TimeSpan.FromDays(1)).Load();
        Check(c1 is { Replaced: null } && c1.Certificate!.Thumbprint == fullThumb && untouched
              && c1.Problem == "Certificate kept, not replaced (it spanned 826 days, more than the 825 Apple devices and Show Bus 1.3 allow): the new one could not "
                 + "be saved (disk full). Every peer keeps the one it pinned; the next start tries again"
              && c1.Note == c1.Problem && c1.Line == c1.Problem
              && c2 is { Replaced: null } && c2.Certificate!.Thumbprint == fullThumb && c2.Problem!.Contains("a new one could not be made (no key could be made)", StringComparison.Ordinal)
              && c3 is { Problem: null, Replaced: not null } && c3.Certificate!.Thumbprint != fullThumb && Public(c1, "full-password"),
            "certificate on disk: a replacement that can't be saved or made leaves the old one serving, its files as they were, and says so; "
            + "a later start replaces it (" + c1.Problem + ")");

        // An ended one whose replacement can't be saved: the new one serves this run only, said with why it was replaced.
        var ended = Path.Combine(root, "ended");
        var endedThumb = Store(ended, t - TimeSpan.FromDays(826), t - TimeSpan.FromDays(1), "ended-password");
        var d = Files(ended, t, save: DiskFull).Load();
        Check(d is { Replaced: "it had ended" } && d.Certificate!.Thumbprint != endedThumb && d.Problem!.StartsWith("Certificate made for this run only: it could not be saved (disk full)", StringComparison.Ordinal)
              && d.Note!.StartsWith("Certificate replaced: it had ended. ", StringComparison.Ordinal) && d.Note.EndsWith(d.Problem, StringComparison.Ordinal) && d.Line == d.Problem,
            "certificate on disk: an ended certificate whose replacement can't be saved is replaced for this run only, and said: " + d.Note);

        // A pair torn by an older writer (a new key beside the old PKCS#12) is replaced, not failed at every start.
        var torn = Path.Combine(root, "torn");
        Store(torn, t - TimeSpan.FromDays(1), t + TimeSpan.FromDays(824), "torn-password");
        File.WriteAllBytes(Path.Combine(torn, "node.key"), Seal(Encoding.UTF8.GetBytes("another-password")));
        var e1 = Files(torn, t).Load();
        var e2 = Files(torn, t + TimeSpan.FromHours(1)).Load();
        Check(e1 is { Certificate: not null, Problem: null } && e1.Replaced!.StartsWith("the stored one could not be opened (", StringComparison.Ordinal)
              && e2 is { Replaced: null, Problem: null } && e2.Certificate!.Thumbprint == e1.Certificate.Thumbprint,
            "certificate on disk: a torn pair is replaced once and the new pair is kept (" + e1.Replaced + ")");

        // Sealed for another user: a new pair, both files, under this user's seal.
        var foreign = Path.Combine(root, "foreign");
        var foreignThumb = Store(foreign, t - TimeSpan.FromDays(1), t + TimeSpan.FromDays(824), "foreign-password");
        var f1 = Files(foreign, t, unseal: Broken).Load();
        var f2 = Files(foreign, t + TimeSpan.FromHours(1)).Load();
        Check(f1 is { Problem: null } && f1.Replaced == "the stored one could not be opened (sealed for another user)" && f1.Certificate!.Thumbprint != foreignThumb
              && f2 is { Replaced: null, Problem: null } && f2.Certificate!.Thumbprint == f1.Certificate.Thumbprint,
            "certificate on disk: a pair sealed for another user is replaced under this user's seal, then kept");

        // Files that can't be read: no certificate this run, said, and nothing overwritten.
        var locked = Path.Combine(root, "locked");
        Store(locked, t - TimeSpan.FromDays(1), t + TimeSpan.FromDays(824), "locked-password");
        var lockedPfx = File.ReadAllBytes(Path.Combine(locked, "node.pfx"));
        var made = makes;
        var g = Files(locked, t, read: _ => throw new IOException("locked by another process")).Load();
        Check(g is { Certificate: null, Replaced: null } && g.Problem == "No certificate this run: it could not be read (locked by another process). HTTPS is off; the next start reads it again"
              && g.Note == g.Problem && g.Line == g.Problem && makes == made && File.ReadAllBytes(Path.Combine(locked, "node.pfx")).SequenceEqual(lockedPfx),
            "certificate on disk: files that can't be read are left as they are, with no certificate this run, said");

        // A first start that can't save: the new one serves this run, said; one that can't make: none, said.
        var h = Files(Path.Combine(root, "nosave"), t, save: DiskFull).Load();
        var i = Files(Path.Combine(root, "nomake"), t, makeFails: true).Load();
        Check(h is { Certificate.HasPrivateKey: true, Replaced: null } && h.Problem!.StartsWith("Certificate made for this run only: it could not be saved (disk full). The next start makes another", StringComparison.Ordinal)
              && !File.Exists(Path.Combine(root, "nosave", "node.pfx"))
              && i is { Certificate: null, Replaced: null } && i.Problem == "No certificate this run: one could not be made (no key could be made). HTTPS is off until a start can make one",
            "certificate on disk: a first certificate that can't be saved serves this run only, and one that can't be made leaves none, each said");
    }

    static System.Security.Cryptography.X509Certificates.X509Certificate2 TestCertificate()
    {
        using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=Stagewatch test", key, System.Security.Cryptography.HashAlgorithmName.SHA256);
        var san = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        req.CertificateExtensions.Add(san.Build());
        using var made = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(made.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, "t"), "t");
    }

    /// <summary>Show Bus §10: HTTPS and HTTP on one port, the certificate pages, pinned clients, wss.</summary>
    static async Task Tls(Action<bool, string> Check)
    {
        var control = new BusControl();
        var cert = TestCertificate();
        var fp = TlsPins.Fingerprint(cert.RawData);
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321") { Certificate = cert };
        web.Start(port, bindAll: false);
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300, Title = "Keynote" });
        string? seen = null;
        using var https = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, c, _, _) => { seen = c is null ? null : TlsPins.Fingerprint(c.RawData); return seen == fp; },
        }) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        var secure = await https.GetAsync("/api/state");
        using var plain = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var open = await plain.GetAsync("/api/state");
        Check(secure.IsSuccessStatusCode && seen == fp && open.IsSuccessStatusCode && web.Fingerprint == fp,
              "tls: one port speaks HTTPS (the pinned certificate) and plain HTTP");
        var der = await plain.GetByteArrayAsync("/tls.cer");
        var page = await plain.GetStringAsync("/tls");
        Check(der.SequenceEqual(cert.RawData) && page.Contains(fp[7..]), "tls: /tls.cer is the certificate, /tls shows its fingerprint and how to trust it");
        web.RequireHttps = true;
        Check((await plain.GetAsync("/api/state")).IsSuccessStatusCode, "tls: Require HTTPS still lets this machine use plain HTTP");
        using (var ws = new ClientWebSocket())
        {
            ws.Options.RemoteCertificateValidationCallback = (_, c, _, _) => c is not null && TlsPins.Fingerprint(c.GetRawCertData()) == fp;
            await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/ws?cid=tls-test"), CancellationToken.None);
            var first = await Receive(ws);
            Check(first.Contains("\"type\":\"state\""), "tls: frames over wss://");
            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
    }

    /// <summary>Show Bus §11 over HTTP: propose, approve (here, as the operator would), collect, report.</summary>
    static async Task Approvals(Action<bool, string> Check)
    {
        var control = new BusControl();
        var (nodeKey, nodeSecret) = ApiKeys.Create("Patterns", ApiKeyScope.Node, DateTime.UtcNow);
        var (otherKey, otherSecret) = ApiKeys.Create("AGMessage", ApiKeyScope.Node, DateTime.UtcNow);
        var (readKey, readSecret) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, DateTime.UtcNow);
        var (mcpKey, mcpSecret) = ApiKeys.Create("Agent", ApiKeyScope.Mcp, DateTime.UtcNow);
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        control.ApiKeys = new[] { nodeKey, otherKey, readKey, mcpKey, ctlKey };
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        async Task<(HttpStatusCode Code, JsonElement Body)> Call(HttpMethod m, string path, string secret, string? body = null)
        {
            using var req = new HttpRequestMessage(m, path);
            req.Headers.Add("Authorization", "Bearer " + secret);
            if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            var res = await http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            return (res.StatusCode, JsonDocument.Parse(text.Length > 0 ? text : "{}").RootElement.Clone());
        }
        var proposal = "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\",\"name\":\"Patterns FOH\"},\"target\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"line\":\"EYE FOCUS screen 2\",\"reason\":\"check it\"}";
        var (code, made) = await Call(HttpMethod.Post, "/api/proposals", nodeSecret, proposal);
        var id = made.TryGetProperty("id", out var idEl) ? idEl.GetString()! : "";
        Check(code == HttpStatusCode.Created && made.GetProperty("state").GetString() == "pending", "approvals: an app proposes with its node key");
        Check((await Call(HttpMethod.Post, "/api/proposals", readSecret, proposal)).Code == HttpStatusCode.Forbidden, "approvals: a read key can't propose");
        var (stolenCode, stolen) = await Call(HttpMethod.Post, "/api/proposals", otherSecret, proposal);
        Check(stolenCode == HttpStatusCode.Forbidden && stolen.GetProperty("error").GetString() == "patterns pat1 speaks with its own key",
              $"approvals: another app's key can't propose from a node that has its own key (403, {stolenCode})");
        var (asOtherCode, asOther) = await Call(HttpMethod.Post, "/api/proposals", nodeSecret,
            "{\"from\":{\"t\":\"messenger\",\"id\":\"ag1\"},\"target\":{\"t\":\"main\"},\"line\":\"note hi\"}");
        Check(asOtherCode == HttpStatusCode.Forbidden && asOther.GetProperty("error").GetString() == "this key speaks for patterns pat1 — make a key for messenger ag1"
              && control.Desk.All().Count == 1, $"approvals: a key speaking for another node's from is 403 with words, and nothing is queued ({asOtherCode})");
        var (pinFromCode, pinFrom) = await Call(HttpMethod.Post, "/api/proposals", "4321", proposal);
        Check(pinFromCode == HttpStatusCode.Forbidden && pinFrom.GetProperty("error").GetString() == "patterns pat1 speaks with its own key" && control.Desk.All().Count == 1,
              $"approvals: the PIN can't propose from a node that has its own key ({pinFromCode})");
        var (mcpCode, _) = await Call(HttpMethod.Post, "/api/proposals", mcpSecret, "{\"from\":{\"t\":\"assistant\",\"id\":\"ai1\"},\"target\":{\"t\":\"main\"},\"line\":\"note from the agent\"}");
        Check(mcpCode == HttpStatusCode.Created && control.LastCredential == NodeCredential.None,
              $"approvals: an MCP key proposes, and proves no node (its from is only claimed) ({mcpCode})");
        var (opCode, _) = await Call(HttpMethod.Post, "/api/proposals", ctlSecret, "{\"from\":{\"t\":\"messenger\",\"id\":\"ag9\"},\"target\":{\"t\":\"main\"},\"line\":\"note from the deck\"}");
        Check(opCode == HttpStatusCode.Created && control.LastCredential == NodeCredential.Operator, $"approvals: a control key proposes as the operator ({opCode})");
        control.Desk.Approve(id, "operator", DateTime.UtcNow);
        Check((await Call(HttpMethod.Get, "/api/proposals?target=pat1&state=approved", otherSecret)).Code == HttpStatusCode.Forbidden,
              "approvals: another app's key can't collect this node's approvals");
        var asked = control.Credentials.Count;
        var (pinCollectCode, pinCollect) = await Call(HttpMethod.Get, "/api/proposals?target=pat1&state=approved", "4321");
        var (ctlCollectCode, _) = await Call(HttpMethod.Get, "/api/proposals?target=pat1&state=approved", ctlSecret);
        Check(pinCollectCode == HttpStatusCode.Forbidden && ctlCollectCode == HttpStatusCode.Forbidden && pinCollect.GetProperty("error").GetString() == "patterns pat1 speaks with its own key"
              && control.Credentials.Count == asked && control.Desk.Get(id) is { ClaimedUtc: null },
              $"approvals: neither the PIN nor a control key collects a key-bound node's approvals, and nothing is claimed ({pinCollectCode}/{ctlCollectCode})");
        Check((await Call(HttpMethod.Get, "/api/proposals?target=no.such&state=approved", nodeSecret)).Code == HttpStatusCode.BadRequest,
              "approvals: a collect for something that isn't a node id is a bad request");
        var (_, collected) = await Call(HttpMethod.Get, "/api/proposals?target=pat1&state=approved", nodeSecret);
        Check(collected.GetProperty("proposals").GetArrayLength() == 1 && collected.GetProperty("proposals")[0].GetProperty("line").GetString() == "EYE FOCUS screen 2",
              "approvals: the target collects what was approved for it");
        var (pinResultCode, pinResult) = await Call(HttpMethod.Post, $"/api/proposals/{id}/result", "4321", "{\"ok\":false,\"words\":\"not me\"}");
        Check(pinResultCode == HttpStatusCode.Conflict && pinResult.GetProperty("error").GetString() == "patterns pat1 speaks with its own key"
              && control.LastCredential == NodeCredential.Operator && control.Desk.Get(id) is { State: SharedProposalState.Approved },
              $"approvals: the PIN can't report a key-bound node's result ({pinResultCode})");
        var (rc, _) = await Call(HttpMethod.Post, $"/api/proposals/{id}/result", nodeSecret, "{\"ok\":true,\"words\":\"focused screen 2\"}");
        var (_, final) = await Call(HttpMethod.Get, $"/api/proposals/{id}", readSecret);
        Check(rc == HttpStatusCode.OK && final.GetProperty("state").GetString() == "done" && final.GetProperty("result").GetString() == "focused screen 2",
              "approvals: the target reports back; anyone with read access sees the outcome");
    }

    /// <summary>An open Stagewatch (no PIN): a control key still proves the operator; nothing, or a PIN it doesn't ask for, proves nothing.</summary>
    static async Task OpenDesk(Action<bool, string> Check)
    {
        var control = new BusControl();
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        var (mcpKey, mcpSecret) = ApiKeys.Create("Agent", ApiKeyScope.Mcp, DateTime.UtcNow);
        var (readKey, readSecret) = ApiKeys.Create("Dashboard", ApiKeyScope.Read, DateTime.UtcNow);
        control.ApiKeys = new[] { ctlKey, mcpKey, readKey };
        var port = FreePort();
        await using var web = new WebServer(control, () => null);
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        async Task<(HttpStatusCode Code, string Body)> Post(string path, string json, string? credential)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (credential is not null) req.Headers.Add("Authorization", "Bearer " + credential);
            var res = await http.SendAsync(req);
            return (res.StatusCode, await res.Content.ReadAsStringAsync());
        }
        static string Ctx(string id) => "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"" + id + "\"},\"light\":\"green\",\"headline\":\"fine\"}";
        var byKey = await Post("/api/peers/context", Ctx("pat1"), ctlSecret);
        Check(byKey is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"operator\"}") && control.LastCredential == NodeCredential.Operator,
              $"open: a control key proves the operator on an open Stagewatch ({byKey.Body})");
        var bare = await Post("/api/peers/context", Ctx("pat2"), null);
        Check(bare is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"none\"}") && control.LastCredential == NodeCredential.None
              && control.Peers.Find("pat2", DateTime.UtcNow) is { Proof: NodeProof.None },
              $"open: a context with no credential is held unverified ({bare.Body})");
        var unasked = await Post("/api/peers/context", Ctx("pat3"), "1234");
        Check(unasked is (HttpStatusCode.OK, "{\"ok\":true,\"verified\":\"none\"}") && control.LastCredential == NodeCredential.None,
              $"open: a PIN this Stagewatch doesn't ask for proves nothing ({unasked.Body})");
        Check((await Post("/api/peers/context", Ctx("pat4"), readSecret)).Code == HttpStatusCode.Forbidden && control.Peers.Find("pat4", DateTime.UtcNow) is null,
              "open: a read key still can't post a context");
        var proposal = await Post("/api/proposals", "{\"from\":{\"t\":\"assistant\",\"id\":\"ai1\"},\"target\":{\"t\":\"main\"},\"line\":\"note hi\"}", mcpSecret);
        Check(proposal.Code == HttpStatusCode.Created && control.LastCredential == NodeCredential.None, $"open: an MCP key's proposal proves no node ({proposal.Code})");
    }

    /// <summary>
    /// A store that takes whatever it is handed (or, with <see cref="Refuse"/>, refuses it all) and never speaks for a node:
    /// the server's own gate refuses for it before it is asked, and binds what it took.
    /// </summary>
    sealed class NaiveControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public void Execute(ShowCommand command) { }
        public RemoteConfig GetRemoteConfig() => new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(), Array.Empty<(string, string?, int)>());
        public IReadOnlyList<ApiKey> ApiKeys { get; set; } = Array.Empty<ApiKey>();
        public NodeBindings Nodes { get; } = new();
        public int Taken, Asked;
        public bool Refuse;
        bool Take()
        {
            Interlocked.Increment(ref Asked);
            if (Refuse) return false;
            Interlocked.Increment(ref Taken);
            return true;
        }
        public (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
            => Take() ? (200, null, NodeProof.Key) : (400, "no room", NodeProof.None);
        public (int Status, string Json) SubmitProposal(string body, NodeCredential credential)
            => Take() ? (201, "{\"ok\":true,\"id\":\"p1\",\"state\":\"pending\"}") : (400, "{\"ok\":false,\"error\":\"too many proposals waiting\"}");
        public (int Status, string Json) CollectApproved(string targetId, NodeCredential credential)
            => Take() ? (200, "{\"proposals\":[]}") : (403, "{\"ok\":false,\"error\":\"no\"}");
    }

    /// <summary>The checks a request shows run in the server, before the store: a store that skips them can't let a key speak for another node.</summary>
    static async Task Gate(Action<bool, string> Check)
    {
        var control = new NaiveControl();
        var (patKey, patSecret) = ApiKeys.Create("Patterns", ApiKeyScope.Node, DateTime.UtcNow);
        var (agKey, agSecret) = ApiKeys.Create("AGMessage", ApiKeyScope.Node, DateTime.UtcNow);
        control.ApiKeys = new[] { patKey, agKey };
        control.Nodes.Speak(NodeCredential.Key(patKey.Id), "patterns", "pat1", DateTime.UtcNow);
        var port = FreePort();
        await using var web = new WebServer(control, () => "4321");
        web.Start(port, bindAll: false);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        async Task<HttpStatusCode> Send(HttpMethod m, string path, string secret, string? json = null)
        {
            using var req = new HttpRequestMessage(m, path);
            req.Headers.Add("Authorization", "Bearer " + secret);
            if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return (await http.SendAsync(req)).StatusCode;
        }
        var forged = "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"light\":\"green\",\"headline\":\"all fine\"}";
        var codes = new[]
        {
            await Send(HttpMethod.Post, "/api/peers/context", agSecret, forged),
            await Send(HttpMethod.Post, "/api/peers/context", "4321", forged),
            await Send(HttpMethod.Post, "/api/proposals", agSecret, "{\"from\":{\"t\":\"patterns\",\"id\":\"pat1\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}"),
            await Send(HttpMethod.Get, "/api/proposals?target=pat1&state=approved", agSecret),
        };
        Check(codes.All(c => c == HttpStatusCode.Forbidden) && control.Taken == 0,
              $"gate: another key or the PIN speaking for a key-bound node is refused by the server itself, and the store is never asked ({string.Join(",", codes.Select(c => (int)c))})");
        Check(await Send(HttpMethod.Post, "/api/peers/context", patSecret, forged) == HttpStatusCode.OK && control.Taken == 1,
              "gate: the node's own key is let through to the store");

        // First-bind-wins doesn't rest on the store speaking either: what it took binds the key that sent it.
        static string Ctx(string kind, string id) => "{\"v\":1,\"node\":{\"t\":\"" + kind + "\",\"id\":\"" + id + "\"},\"light\":\"green\",\"headline\":\"fine\"}";
        var first = await Send(HttpMethod.Post, "/api/peers/context", agSecret, Ctx("messenger", "ag1"));
        Check(first == HttpStatusCode.OK && control.Taken == 2 && control.Nodes.NodeOf(agKey.Id) == ("messenger", "ag1") && control.Nodes.KeyOf("ag1") == agKey.Id,
              "gate: a store that never speaks took a key's first context, and the server bound the key to its node");
        var second = new[]
        {
            await Send(HttpMethod.Post, "/api/peers/context", agSecret, Ctx("messenger", "ag2")),
            await Send(HttpMethod.Post, "/api/proposals", agSecret, "{\"from\":{\"t\":\"messenger\",\"id\":\"ag2\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}"),
            await Send(HttpMethod.Get, "/api/proposals?target=ag2&state=approved", agSecret),
        };
        Check(second.All(c => c == HttpStatusCode.Forbidden) && control.Taken == 2 && control.Nodes.LastRefused(agKey.Id) is { Id: "ag2" },
              $"gate: so the same key is refused for a second node on every route, and the store isn't asked ({string.Join(",", second.Select(c => (int)c))})");

        var (pmKey, pmSecret) = ApiKeys.Create("PlayMap", ApiKeyScope.Node, DateTime.UtcNow);
        var (amKey, amSecret) = ApiKeys.Create("AGMessage 2", ApiKeyScope.Node, DateTime.UtcNow);
        control.ApiKeys = new[] { patKey, agKey, pmKey, amKey };
        Check(await Send(HttpMethod.Get, "/api/proposals?target=pm1&state=approved", pmSecret) == HttpStatusCode.OK && control.Nodes.NodeOf(pmKey.Id) == ("", "pm1"),
              "gate: a key's first collect the store took binds it to the id alone");
        Check(await Send(HttpMethod.Post, "/api/proposals", amSecret, "{\"from\":{\"t\":\"messenger\",\"id\":\"am1\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}") == HttpStatusCode.Created
              && control.Nodes.NodeOf(amKey.Id) == ("messenger", "am1"),
              "gate: a key's first proposal the store took binds it to its from");

        control.Refuse = true;
        var (newKey, newSecret) = ApiKeys.Create("Fresh", ApiKeyScope.Node, DateTime.UtcNow);
        control.ApiKeys = new[] { patKey, agKey, pmKey, amKey, newKey };
        var refusedByStore = new[]
        {
            await Send(HttpMethod.Post, "/api/peers/context", newSecret, Ctx("playmap", "pm9")),
            await Send(HttpMethod.Post, "/api/proposals", newSecret, "{\"from\":{\"t\":\"playmap\",\"id\":\"pm9\"},\"target\":{\"t\":\"main\"},\"line\":\"note x\"}"),
            await Send(HttpMethod.Get, "/api/proposals?target=pm9&state=approved", newSecret),
        };
        Check(refusedByStore is [HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.Forbidden] && control.Nodes.NodeOf(newKey.Id) is null && control.Nodes.KeyOf("pm9") is null,
              $"gate: a request the store refused binds no key ({string.Join(",", refusedByStore.Select(c => (int)c))})");
    }

    static async Task<string> Receive(ClientWebSocket ws)
    {
        var buf = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var r = await ws.ReceiveAsync(buf, cts.Token);
        return Encoding.UTF8.GetString(buf, 0, r.Count);
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
