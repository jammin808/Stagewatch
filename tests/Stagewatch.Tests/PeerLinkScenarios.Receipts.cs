using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// The acceptor rows of 13.20 that Stagewatch's <c>/ws</c> keeps with its receipts and its bounds, against a real <see cref="WebServer"/>
/// and the scripted peer: S14 (final, the record — a repeat answered from it and run once, RECEIPTS after a relink — and a link that keeps
/// none), S19's canaries in what the acceptor publishes, S20's foreign Origin and local forwarder on a side call, and the words a
/// connection past the server's ceiling hears before it closes (13.14).
/// </summary>
static partial class PeerLinkScenarios
{
    static async Task WsAcceptorReceipts(Action<bool, string> Check, string script, Material kit)
    {
        await AcceptorS14(Check, script, kit);
        await AcceptorS19(Check, script, kit);
        await AcceptorS20(Check, script, kit);
        await AcceptorShed(Check, kit);
    }

    // ------------------------------------------------------------------ S14 at /ws: receipts, final, the record, RECEIPTS

    static async Task AcceptorS14(Action<bool, string> Check, string script, Material kit)
    {
        // A control key's LINK operates; the show thread answers s2 later (accepted now, its follow-up when the test lets it run).
        await using var hall = new Hall(kit, keys: [KeyInForce("kC", ControlSecret, ApiKeyScope.Control), KeyInForce("kC2", ControlSecret2, ApiKeyScope.Control)]);
        hall.Control.OwnInst = "5ca1ab1e";
        var later = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        hall.Control.Receipts = (_, id) => id == "s2-0a1b2c3d" ? CommandReceipt.Accepted(id) with { Final = later.Task } : null;
        await using var peer = Peer(script);
        var conn = await DialHall(peer, hall, Key(kit, ControlSecret));
        var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        var verdict = ReadJson(peer, conn).FirstOrDefault(m => Str(m, "type") == "auth");
        Check(link is not null && verdict.ValueKind == JsonValueKind.Object && verdict.TryGetProperty("can", out var can)
              && can.EnumerateArray().Select(c => c.GetString()).SequenceEqual(["final", "receipts"]) && !verdict.TryGetProperty("stuckMs", out _),
            $"peerlink scenarios: S14 /ws acceptor — the verdict names final and receipts, never stuck or expire, and no stuckMs ({verdict.GetRawText()}) {peer.Faults}");

        async Task<JsonElement?> Send(object message, string id, string also = "")
        {
            var mark = peer.Events.Count > 0 ? Int(peer.Events[^1], "n") ?? 0 : 0;
            await peer.Ask(new { @do = "send", conn, message });
            return await Receipt(id, also, mark);
        }
        async Task<JsonElement?> Receipt(string id, string also, int after)
        {
            var read = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && Str(e, "message") is { } m
                                            && m.Contains("\"id\":\"" + id + "\"", StringComparison.Ordinal) && m.Contains(also, StringComparison.Ordinal), after: after);
            return read is { } r ? JsonDocument.Parse(Str(r, "message")!).RootElement.Clone() : null;
        }
        int Runs(ShowCommandKind kind) { lock (hall.Control.Commands) return hall.Control.Commands.Count(c => c.Kind == kind); }

        var applied = await Send(new { cmd = "next", id = "s1-0a1b2c3d" }, "s1-0a1b2c3d");
        var refused = await Send(new { cmd = "start", id = "s3-0a1b2c3d" }, "s3-0a1b2c3d");
        var accepted = await Send(new { cmd = "next", id = "s2-0a1b2c3d" }, "s2-0a1b2c3d");
        var stillRunning = await Send(new { cmd = "next", id = "s2-0a1b2c3d" }, "s2-0a1b2c3d", "\"repeat\":true");
        var mark = Int(peer.Events[^1], "n") ?? 0;
        later.SetResult(new CommandReceipt("s2-0a1b2c3d", true, "applied", "Next item", null) { IsFinal = true, LateMs = 4200 });
        var final = await Receipt("s2-0a1b2c3d", "\"final\":true", mark);
        Check(applied is { } a && True(a) && Str(a, "stage") == "applied" && refused is { } f && False(f, "ok") && Str(f, "error") == "Start needs a licence"
              && accepted is { } p && Str(p, "stage") == "accepted" && !p.TryGetProperty("final", out _)
              && stillRunning is { } sr && Str(sr, "stage") == "accepted" && True(sr, "repeat")
              && final is { } fi && Str(fi, "stage") == "applied" && True(fi, "final") && Int(fi, "lateMs") == 4200,
            $"peerlink scenarios: S14 /ws acceptor — applied, refused, accepted then its one final follow-up with lateMs, and a repeat while it runs answered accepted "
            + $"with repeat ({accepted?.GetRawText()} | {stillRunning?.GetRawText()} | {final?.GetRawText()}) {peer.Faults}");

        // A repeated id, once settled: its receipt from the record with repeat, never run again.
        var repeat = await Send(new { cmd = "next", id = "s1-0a1b2c3d" }, "s1-0a1b2c3d", "\"repeat\":true");
        var repeatFinal = await Send(new { cmd = "next", id = "s2-0a1b2c3d" }, "s2-0a1b2c3d", "\"repeat\":true,\"lateMs\":4200");
        Check(repeat is { } r1 && Str(r1, "stage") == "applied" && Str(r1, "effect") == "Message on the speaker screen" && repeatFinal is { } r2 && Str(r2, "stage") == "applied"
              && !r2.TryGetProperty("final", out _) && Runs(ShowCommandKind.Next) == 2 && Runs(ShowCommandKind.Start) == 1,
            $"peerlink scenarios: S14 /ws acceptor — a repeated id is answered from the record with repeat (before its lateMs), and runs once ({repeat?.GetRawText()} | "
            + $"{repeatFinal?.GetRawText()}; next ran {Runs(ShowCommandKind.Next)})");

        // RECEIPTS after a relink to a server rebuilt under the same inst, as the desk rebuilds its server on a Remote settings change: the
        // record handed over answers each id's kept receipt, unknown for one never read, and a repeat is still answered from it, not run.
        await peer.Ask(new { @do = "close", conn });
        await hall.Web.StopAsync();
        await using var rebuilt = new Hall(kit, after: hall);
        conn = await DialHall(peer, rebuilt, Key(kit, ControlSecret));
        var relinked = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        var asked = await Send(new { cmd = "receipts", args = new[] { "s1-0a1b2c3d", "s2-0a1b2c3d", "s3-0a1b2c3d", "s9-0a1b2c3d" }, id = "rq1" }, "rq1");
        var receipts = asked is { } q && q.TryGetProperty("receipts", out var rs) ? rs : default;
        Check(relinked is { } l2 && asked is { } ans && True(ans) && Str(ans, "inst") == Str(l2, "inst") && Str(ans, "inst") == "5ca1ab1e"
              && receipts.ValueKind == JsonValueKind.Object
              && Str(receipts.GetProperty("s1-0a1b2c3d"), "stage") == "applied" && Int(receipts.GetProperty("s2-0a1b2c3d"), "lateMs") == 4200
              && False(receipts.GetProperty("s3-0a1b2c3d"), "ok") && receipts.GetProperty("s9-0a1b2c3d").GetString() == "unknown",
            $"peerlink scenarios: S14 /ws acceptor — after a relink to a rebuilt server RECEIPTS answers its own ids from the record handed over, with the "
            + $"same inst, unknown for one never read ({asked?.GetRawText()}) {peer.Faults}");
        var rebuiltRepeat = await Send(new { cmd = "next", id = "s1-0a1b2c3d" }, "s1-0a1b2c3d", "\"repeat\":true");
        Check(rebuiltRepeat is { } rr && Str(rr, "stage") == "applied" && Runs(ShowCommandKind.Next) == 2,
            $"peerlink scenarios: S14 /ws acceptor — a rebuilt server under the same inst answers a repeat from the record handed over and never runs it "
            + $"again (next ran {Runs(ShowCommandKind.Next)})");
        var tooMany = await Send(new { cmd = "receipts", args = Array.Empty<string>(), id = "rq2" }, "rq2");
        Check(tooMany is { } t && False(t, "ok") && Str(t, "error") == ReceiptRecord.AskWords,
            "peerlink scenarios: S14 /ws acceptor — an ask that names no id is refused in words");

        // Another holder's control key that names the same unheld id is a connector of its own: it reads none of the first holder's ids,
        // and its own id of the same text runs.
        await peer.Ask(new { @do = "close", conn });
        conn = await DialHall(peer, rebuilt, Key(kit, ControlSecret2));
        var other = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        var otherAsked = await Send(new { cmd = "receipts", args = new[] { "s1-0a1b2c3d" }, id = "rq3" }, "rq3");
        var otherRun = await Send(new { cmd = "next", id = "s1-0a1b2c3d" }, "s1-0a1b2c3d");
        Check(other is not null && otherAsked is { } oa && oa.TryGetProperty("receipts", out var ors) && ors.GetProperty("s1-0a1b2c3d").GetString() == "unknown"
              && otherRun is { } or && Str(or, "stage") == "applied" && !or.TryGetProperty("repeat", out _) && Runs(ShowCommandKind.Next) == 3,
            $"peerlink scenarios: S14 /ws acceptor — two control keys that name one unheld id keep records apart: neither reads nor pre-empts the other's ids "
            + $"({otherAsked?.GetRawText()} | {otherRun?.GetRawText()})");

        // A bearer node link keeps no record: RECEIPTS is refused, and an id it repeats is run again (it was never promised otherwise).
        await using var bearer = new Hall(kit, tls: false);
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", "Bearer " + Swk);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{bearer.Port.ToString(CultureInfo.InvariantCulture)}/ws?node=9f8e7d6c&t=patterns"), CancellationToken.None);
        await ws.SendAsync("{\"cmd\":\"receipts\",\"args\":[\"s1-0a1b2c3d\"],\"id\":\"rq1\"}"u8.ToArray(), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
        var noRecord = await ReadUntil(ws, m => m.Contains("\"id\":\"rq1\"", StringComparison.Ordinal));
        Check(noRecord is { } nr && nr.Contains("\"ok\":false", StringComparison.Ordinal) && nr.Contains(JsonSerializer.Serialize(WebServer.NoReceipts)[1..^1], StringComparison.Ordinal),
            $"peerlink scenarios: S14 /ws acceptor — a link LINK did not verify keeps no record, and RECEIPTS is refused in words ({noRecord})");

        // A node key's LINK reads; it operates only as a control key's would: start and next are refused read-only and never run.
        await using var nodeHall = new Hall(kit);
        await using var nodePeer = Peer(script);
        var nodeConn = await DialHall(nodePeer, nodeHall, Key(kit, Swk));
        var nodeLink = await nodePeer.Wait(e => Ev(e, "link") && Int(e, "conn") == nodeConn);
        await nodePeer.Ask(new { @do = "send", conn = nodeConn, message = new { cmd = "start", id = "s1-0a1b2c3d" } });
        await nodePeer.Ask(new { @do = "send", conn = nodeConn, message = new { cmd = "next", id = "s2-0a1b2c3d" } });
        var readOnly = await Until(() => nodePeer.ReadOn(nodeConn).Count(m => m.Contains("read-only connection (PIN required)", StringComparison.Ordinal)) >= 2);
        int nodeRuns;
        lock (nodeHall.Control.Commands) nodeRuns = nodeHall.Control.Commands.Count(c => c.Kind is ShowCommandKind.Start or ShowCommandKind.Next);
        Check(nodeLink is not null && readOnly && nodeRuns == 0,
            $"peerlink scenarios: S14 /ws acceptor — a node key's LINK runs no show verb: start and next are refused read-only and never run (ran {nodeRuns}) "
            + nodePeer.Faults);
    }

    /// <summary>The status line, head and body a plain POST of <paramref name="body"/> to <paramref name="path"/> on loopback is answered with, read raw.</summary>
    static async Task<string> RawPost(int port, string path, string body)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"POST {path} HTTP/1.1\r\nHost: 127.0.0.1:{port.ToString(CultureInfo.InvariantCulture)}\r\nContent-Type: application/json\r\n"
                   + $"Content-Length: {bytes.Length.ToString(CultureInfo.InvariantCulture)}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head).Concat(bytes).ToArray());
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var cts = new CancellationTokenSource(3000);
        try { return await reader.ReadToEndAsync(cts.Token); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException) { return ""; }
    }

    /// <summary>The first text message on <paramref name="ws"/> that <paramref name="match"/> takes, within 3 s; null when none came.</summary>
    static async Task<string?> ReadUntil(System.Net.WebSockets.ClientWebSocket ws, Func<string, bool> match)
    {
        using var cts = new CancellationTokenSource(3000);
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var r = await ws.ReceiveAsync(buffer, cts.Token);
                if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
                var text = Encoding.UTF8.GetString(buffer, 0, r.Count);
                if (match(text)) return text;
            }
        }
        catch (OperationCanceledException) { return null; }
    }

    // ------------------------------------------------------------------ S19 at /ws: nothing secret in what the acceptor publishes

    static async Task AcceptorS19(Action<bool, string> Check, string script, Material kit)
    {
        // Loopback plays another machine, so the link and its side calls run over TLS; a wrong key and an unknown kid are tried too.
        await using var hall = new Hall(kit, isThisPc: _ => false, keys: [KeyInForce("kC", ControlSecret, ApiKeyScope.Control), KeyInForce("kS", Swk)]);
        var rejected = new List<string>();
        hall.Web.CommandRejected += w => { lock (rejected) rejected.Add(w); };
        await using var peer = Peer(script);
        var conn = await DialHall(peer, hall, Key(kit, ControlSecret));
        var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
        await peer.Ask(new { @do = "send", conn, message = new { cmd = "next", id = "s1-0a1b2c3d" } });
        await peer.Ask(new { @do = "send", conn, message = new { cmd = "nosuchverb", id = "s2-0a1b2c3d" } });
        await peer.Ask(new { @do = "send", conn, message = new { cmd = "receipts", args = new[] { "s1-0a1b2c3d" }, id = "rq1" } });
        var context = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
        var wrong = await DialHall(peer, hall, Key(kit, Stranger));
        await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == wrong);
        await Settle(peer);

        // Every secret of the run: the keys and their K, each S the server held, and every nonce and proof that crossed the handshake.
        var canaries = Canaries(ControlSecret, Swk, Stranger);
        canaries.AddRange(Sessions(hall.Web));
        foreach (var m in peer.AllRead().Concat(peer.Events.Where(e => Ev(e, "wrote")).Select(e => Str(e, "message") ?? "")))
        {
            if (!m.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(m);
                foreach (var name in new[] { "snonce", "cnonce", "proof" })
                    if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: >= 32 } secret) canaries.Add(secret);
            }
            catch (JsonException) { }
        }
        // What the acceptor publishes: every message after each socket's handshake, the side call's answer, its node list, its lockout
        // tables, the words it said of refused lines, its state and its context — none of them may carry one.
        var published = new List<string>();
        foreach (var c in new[] { conn, wrong })
            published.AddRange(peer.ReadOn(c).Where(m => !m.Contains("\"snonce\"", StringComparison.Ordinal) && !m.Contains("\"proof\"", StringComparison.Ordinal)));
        published.Add(context.GetRawText());
        published.AddRange(hall.Web.ListNodes().Select(n => n.ToString()));
        published.AddRange(hall.Web.ProofFailures().Select(f => f.ToString()));
        published.AddRange(hall.Web.AuthFailures().Select(f => f.ToString()));
        lock (rejected) published.AddRange(rejected);
        published.Add(await RawGet(hall.Port, "/api/state?detail=1"));
        published.Add(await RawGet(hall.Port, "/api/context"));
        var found = Found(published, canaries);
        Check(link is not null && canaries.Count >= 12 && published.Count >= 6 && found is null && Sessions(hall.Web).Count > 0,
            $"peerlink scenarios: S19 /ws acceptor — none of {canaries.Count} canaries (keys, K, S, nonces, proofs) in any of the {published.Count} things the "
            + $"acceptor published: frames, receipts, refusals, side-call answers, its node list and lockout tables, its state and context ({found}) {peer.Faults}");
        Check(Found(Stdout(peer), Canaries(ControlSecret, Swk, Stranger).Concat(Sessions(hall.Web))) is null,
            "peerlink scenarios: S19 /ws acceptor — no key, K or S on the peer's stdout");
    }

    /// <summary>Each live link's S as the server holds it: read by reflection, for a canary only (no reader of the server's has it).</summary>
    static List<string> Sessions(WebServer web)
    {
        var field = typeof(WebServer).GetField("_sessions", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(web) is not System.Collections.IDictionary sessions) return [];
        var all = new List<string>();
        foreach (System.Collections.DictionaryEntry e in sessions)
            if (e.Value?.GetType().GetProperty("Session")?.GetValue(e.Value) is string s) all.Add(s);
        return all;
    }

    // ------------------------------------------------------------------ S20 at /ws: loopback trust through a forwarder or a foreign Origin

    static async Task AcceptorS20(Action<bool, string> Check, string script, Material kit)
    {
        // A plain link on this machine is local, so its side calls ride in clear here. The same call from a page of another site, or
        // through a local forwarder on another port, is another machine's: its S in clear is refused and its link ended.
        foreach (var (label, attack) in new (string, object)[]
                 {
                     ("a foreign Origin", new { origin = "http://evil.example" }),
                     ("a local forwarder", new { host = "127.0.0.1:1" }),
                 })
        {
            await using var hall = new Hall(kit, tls: false);
            await using var peer = Peer(script);
            var conn = await DialHall(peer, hall, Key(kit, Swk), tls: false);
            var link = await peer.Wait(e => Ev(e, "link") && Int(e, "conn") == conn);
            var honest = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
            var foreign = await peer.Ask(new { @do = "call", conn, path = "/api/context", attack });
            var ended = await Until(() => hall.Web.ListNodes().Count == 0);
            Check(link is not null && Int(honest, "status") == 200 && Int(foreign, "status") == 401 && True(Body(foreign), "relink")
                  && (Str(Body(foreign), "error") ?? "").Contains("in clear from another machine", StringComparison.Ordinal) && ended,
                $"peerlink scenarios: S20 /ws acceptor — a side call through {label} is not this machine's: its S in clear is refused and its link ended "
                + $"({foreign.GetRawText()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ 13.14: a connection past the ceiling hears why

    static async Task AcceptorShed(Action<bool, string> Check, Material kit)
    {
        // Every slot held by a connection that sends nothing yet; the next is answered 429 in words, with Retry-After, before it closes.
        await using var hall = new Hall(kit, tls: false);
        var held = new List<TcpClient>();
        try
        {
            for (var i = 0; i < WebServer.MaxConnections; i++)
            {
                var c = new TcpClient();
                await c.ConnectAsync(IPAddress.Loopback, hall.Port);
                held.Add(c);
            }
            await Task.Delay(300);
            var answer = await RawGet(hall.Port, "/api/state");
            Check(answer.StartsWith("HTTP/1.1 429", StringComparison.Ordinal)
                  && answer.Contains("Retry-After: " + WebServer.FullRetryAfter.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                  && BodyError(answer) == WebServer.Full,
                $"peerlink scenarios: /ws acceptor — a connection past its {WebServer.MaxConnections} hears 429 with Retry-After and why before it closes "
                + $"({answer.Split("\r\n")[0]}; {BodyError(answer)})");
            // A side call that posts a body hears the same: its body is read with its head, so the close resets nothing before the words.
            var posted = await RawPost(hall.Port, "/api/proposals", "{\"line\":\"" + new string('x', 8000) + "\"}");
            Check(posted.StartsWith("HTTP/1.1 429", StringComparison.Ordinal) && BodyError(posted) == WebServer.Full,
                $"peerlink scenarios: /ws acceptor — a POST past the ceiling hears 429 and why, not a reset ({posted.Split("\r\n")[0]})");
        }
        finally
        {
            foreach (var c in held) c.Dispose();
        }
        // Once the slots are free again, a request is served as before.
        var served = await Until(async () => (await RawGet(hall.Port, "/api/state")).StartsWith("HTTP/1.1 200", StringComparison.Ordinal));
        Check(served, "peerlink scenarios: /ws acceptor — once its connections close, the server answers again");
    }
}
