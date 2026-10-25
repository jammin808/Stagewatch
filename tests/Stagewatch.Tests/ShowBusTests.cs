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
        public NodeBindings Nodes { get; } = new();
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
        public (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
            => Peers.Accept(json, address, DateTime.UtcNow, "abc123abc123", Seen(credential));
    }

    public static async Task Run(Action<bool, string> Check)
    {
        Pure(Check);
        DeskRules(Check);
        await Web(Check);
        await Tls(Check);
        await Approvals(Check);
        await OpenDesk(Check);
        await Gate(Check);
        await PatternsDesk(Check);
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
