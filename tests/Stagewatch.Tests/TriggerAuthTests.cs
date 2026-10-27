using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// HTTP triggers that carry a secret (Show Bus 13.6.8, 13.7, 13.8, 13.12.3): the rules a rule is saved and fired by — plain HTTP only to
/// this PC, a fixed address, no secret in a query beside a profile, a <c>?pin=</c> rule with none firing as before with a warning — and
/// the runner against targets played in this process on loopback: each kind's headers and request id, its answer read by 13.12.3, no
/// redirect followed, show text escaped into the path alone, a refused or rate-limited secret held, the secret in no status or row (an
/// echo of it masked). Over HTTPS, the local-address seam playing another machine: the first certificate pinned first and sent nothing,
/// trusted by one press bound to its whole fingerprint, a changed one refused at the handshake until a person types 8 hex of it; pins
/// kept by address and by node id in a file of their own, a person's pin under either the pin. Patterns' real answers (CUE GO's outcome
/// inside msg) read by the line rows; a redirect, a cut-off answer and a late accepted read for what they are; holds shared by equal
/// secrets and kept across a new runner and a restart. Fields first, words beside them; the targets are real sockets.
/// </summary>
static class TriggerAuthTests
{
    const string Secret = "s3cret-XYZ";
    const string Inst = "1a2b3c4d";

    public static async Task Run(Action<bool, string> Check)
    {
        Rules(Check);
        Judge(Check);
        NodeAt(Check);
        await PinFile(Check);
        await HoldFile(Check);
        Settings(Check);
        AtRest(Check);
        PatternsAnswers(Check);
        await Kinds(Check);
        await Fixed(Check);
        await Refusals(Check);
        await Deadlines(Check);
        await Holds(Check);
        await Legacy(Check);
        await Pinned(Check);
        await Keys(Check);
        await Bounded(Check);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The desk's protector played: a stored secret is <c>prot:</c> and the secret, or <c>prot:&lt;salt&gt;|</c> and the secret — two
    /// protected forms of one secret, as DPAPI makes each time; anything else cannot be read here.
    /// </summary>
    static string Unprotect(string stored)
    {
        if (!stored.StartsWith("prot:", StringComparison.Ordinal)) return "";
        var v = stored[5..];
        return v.IndexOf('|') is var bar and >= 0 ? v[(bar + 1)..] : v;
    }

    static TriggerRule Rule(string target, string kind = TriggerAuthRules.PlayMap, string payload = "PLAYLIST GO show #4", string secret = "prot:" + Secret,
        TriggerAction action = TriggerAction.Http, string name = "GO VT")
        => new()
        {
            Name = name, Event = TriggerEvent.ItemStart, Action = action, Target = target, Payload = payload,
            Auth = kind == TriggerAuthRules.None ? null : new TriggerAuth { Kind = kind, SecretProtected = secret },
        };

    static readonly TriggerContext Ctx = new(4, "Intro", "normal", "02:00", "", "Hall A");

    /// <summary>A trigger runner on the played protector and inst, whose every HTTP outcome is heard here.</summary>
    sealed class Harness : IDisposable
    {
        readonly Channel<TriggerSent> _sent = Channel.CreateUnbounded<TriggerSent>();
        volatile TriggerRule[] _rules = [];
        public readonly List<TriggerSent> All = [];
        public readonly TriggerRunner Runner;
        /// <summary>Rules the runner holds beside the one fired, switched off so they never fire: what a hold looks among.</summary>
        public TriggerRule[] Also = [];

        public Harness(Func<TriggerRunnerOptions, TriggerRunnerOptions>? set = null)
        {
            var o = new TriggerRunnerOptions { Unprotect = Unprotect, Inst = Inst, Sent = s => _sent.Writer.TryWrite(s) };
            Runner = new TriggerRunner(() => _rules, _ => { }, set is null ? o : set(o));
        }

        /// <summary>Fires a copy of <paramref name="rule"/> (a copy each time, so no fire is the last one's within its second) and waits for its outcome.</summary>
        public async Task<TriggerSent> Fire(TriggerRule rule, TriggerContext? ctx = null)
        {
            var copy = rule.Clone();
            _rules = [copy, .. Also.Select(r => { var off = r.Clone(); off.Enabled = false; return off; })];
            Runner.Fire(TriggerEvent.ItemStart, "1", ctx ?? Ctx);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var sent = await _sent.Reader.ReadAsync(cts.Token);
            All.Add(sent);
            return sent;
        }

        public void Dispose() => Runner.Dispose();
    }

    sealed record Heard(string Method, string Path, Dictionary<string, string> Headers, string Body);

    /// <summary>
    /// A trigger's target on loopback: plain HTTP, or TLS with <see cref="Cert"/> (swapped when a test changes it); each request is read
    /// whole and answered by <see cref="Answer"/>, then closed. It counts connections taken and requests read, and keeps each request.
    /// </summary>
    sealed class Target : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _stop = new();
        readonly Task _accepting;
        int _accepts, _requests, _disposed;

        public int Port { get; }
        public volatile KitCert? Cert;
        public volatile Func<Heard, (int Status, string Body, string Extra)> Answer = _ => (200, "{\"ok\":true}", "");
        /// <summary>Bytes the answer's Content-Length claims beyond its body: the connection then closes short of it.</summary>
        public volatile int Short;
        /// <summary>How long the target waits after reading a request before it answers.</summary>
        public volatile int DelayMs;
        public ConcurrentQueue<Heard> Heard { get; } = new();
        public int Accepts => Volatile.Read(ref _accepts);
        public int Requests => Volatile.Read(ref _requests);

        public Target(KitCert? cert = null)
        {
            Cert = cert;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = Task.Run(Accept);
        }

        public Heard? Last() => Heard.LastOrDefault();

        async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
                _ = Task.Run(() => Serve(client));
            }
        }

        async Task Serve(TcpClient client)
        {
            using var _ = client;
            Interlocked.Increment(ref _accepts);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                Stream stream = client.GetStream();
                if (Cert is { } cert)
                {
                    var ssl = new SslStream(stream, false);
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = cert.Server }, cts.Token);
                    stream = ssl;
                }
                if (await Kit.Head(stream) is not { } head) return;
                var start = head.Start.Split(' ');
                var length = head.Headers.TryGetValue("Content-Length", out var l)
                             && int.TryParse(l, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
                var body = new byte[length];
                var got = 0;
                while (got < length && await stream.ReadAsync(body.AsMemory(got), cts.Token) is var r and > 0) got += r;
                var heard = new Heard(start[0], start.Length > 1 ? start[1] : "", head.Headers, Encoding.UTF8.GetString(body, 0, got));
                Heard.Enqueue(heard);
                Interlocked.Increment(ref _requests);
                var (status, text, extra) = Answer(heard);
                if (DelayMs > 0) await Task.Delay(DelayMs, cts.Token);
                var bytes = Encoding.UTF8.GetBytes(text);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status.ToString(System.Globalization.CultureInfo.InvariantCulture)} X\r\nContent-Type: application/json\r\n"
                    + $"Content-Length: {(bytes.Length + Short).ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\nConnection: close\r\n{extra}\r\n"), cts.Token);
                await stream.WriteAsync(bytes, cts.Token);
                await stream.FlushAsync(cts.Token);
            }
            catch (Exception e) when (e is AuthenticationException or IOException or OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // The connection ended: the trigger refused the certificate, or the test did.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _stop.Cancel();
            _listener.Stop();
            await _accepting;
            _stop.Dispose();
        }
    }

    static string Applied(string effect) => $"{{\"type\":\"receipt\",\"id\":\"x\",\"ok\":true,\"stage\":\"applied\",\"effect\":\"{effect}\",\"error\":null,\"refs\":null}}";

    // Patterns' /api/cmd as its desk answers CUE GO: the router's line ("OK " and the outcome's JSON) in msg, the id beside it, each
    // serialised as its ControlService does.
    static readonly object Row12 = new { id = "c12", number = "12", name = "Walk-in", outcome = "Done", execution = "e7", pending = 0, error = "",
        at = new DateTime(2026, 10, 27, 19, 30, 0, DateTimeKind.Utc), origin = "http", actionsDone = 3, actionsTotal = 3 };
    static readonly object Standby13 = new { id = "c13", number = "13", name = "Talk", requireConfirm = false, notes = "", plannedStart = (string?)null, followSeconds = 0 };
    static string PatternsCmd(string line, string? id)
        => id is null
            ? $"{{\"ok\":{(line.StartsWith("OK", StringComparison.Ordinal) ? "true" : "false")},\"msg\":{System.Text.Json.JsonSerializer.Serialize(line)}}}"
            : $"{{\"ok\":{(line.StartsWith("OK", StringComparison.Ordinal) ? "true" : "false")},\"msg\":{System.Text.Json.JsonSerializer.Serialize(line)},\"id\":{System.Text.Json.JsonSerializer.Serialize(id)}}}";
    static string CueGo(string outcome, string? id = "tr1-x") => PatternsCmd("OK " + System.Text.Json.JsonSerializer.Serialize(outcome == "Confirm"
        ? new { outcome, confirm = "Cue 12 'Walk-in' asks to be confirmed — GO again", standby = Standby13 } as object
        : new { outcome, last = Row12, standby = Standby13 }), id);

    // ------------------------------------------------------------------ the rules (pure)

    static void Rules(Action<bool, string> Check)
    {
        static string? P(TriggerRule r, Func<IPAddress, bool>? loopback = null) => TriggerAuthRules.Problem(r, loopback);
        Check(P(Rule("https://10.0.0.12:9940/api/cmd")) is null && P(Rule("http://127.0.0.1:9941/api/cmd")) is null
              && P(Rule("http://localhost:9924/api/next", TriggerAuthRules.Stagewatch)) is null && P(Rule("https://[::1]:9940/api/cmd")) is null
              && P(Rule("https://stage-left.local:9940/api/{TITLE}?n={ITEM}", TriggerAuthRules.Bearer)) is null
              && P(Rule("http://[::1]:9941/api/cmd")) is null,
            "trigger auth: HTTPS anywhere, plain HTTP to this PC (an address or localhost), placeholders in the path and query — all may be saved");
        Check(P(Rule("http://10.0.0.12:9940/api/cmd")) is { } lan && lan.Contains("plain HTTP only to this PC", StringComparison.Ordinal)
              && lan.Contains("10.0.0.12:9940", StringComparison.Ordinal) && lan.Contains("https://", StringComparison.Ordinal),
            "trigger auth: a secret over plain HTTP to another machine is refused, naming where and what to use instead");
        Check(P(Rule("http://stage-pc:9941/api/cmd")) is not null && P(Rule("http://127.0.0.1:9941/api/cmd"), _ => false) is not null,
            "trigger auth: plain HTTP to a name that is not localhost is refused, and loopback is the seam's to judge");
        Check(P(Rule("https://{ROOM}:9940/api/cmd")) is { } host && host.Contains("placeholder in its host or port", StringComparison.Ordinal)
              && P(Rule("https://10.0.0.12:{ITEM}/api/cmd")) is { } port && port.Contains("placeholder", StringComparison.Ordinal)
              && P(Rule("{SCHEME}://10.0.0.12:9940/api/cmd")) is { } scheme && scheme.Contains("http:// or https://", StringComparison.Ordinal),
            "trigger auth: with a secret, a placeholder in the scheme, host or port is refused — where it goes is fixed text");
        Check(P(Rule("https://user:pw@10.0.0.12:9940/api/cmd")) is { } user && user.Contains("user or password", StringComparison.Ordinal)
              && P(Rule("https://10.0.0.12:99999/api/cmd")) is not null && P(Rule("https:///api/cmd")) is not null,
            "trigger auth: a user or password in the address, a port out of range or no host is refused");
        Check(P(Rule("https://10.0.0.12:9940/api/cmd", "telnet")) is { } kind && kind.Contains("'telnet' is not one Stagewatch knows", StringComparison.Ordinal)
              && P(Rule("10.0.0.12:53000", action: TriggerAction.Osc)) is { } osc && osc.Contains("only an HTTP trigger", StringComparison.Ordinal)
              && P(Rule("https://10.0.0.12:9940/api/cmd", secret: "")) is { } none && none.Contains("no secret", StringComparison.Ordinal)
              && P(Rule("https://10.0.0.12:9940/api/cmd", " PlayMap ")) is null && TriggerAuthRules.KindOf(Rule("https://x/", " PlayMap ")) == TriggerAuthRules.PlayMap
              && P(Rule("http://10.0.0.12:9940/api/cmd", TriggerAuthRules.None)) is null,
            "trigger auth: an unknown kind, a profile on an OSC rule or one with no secret is refused; kinds read in any case; no profile is never refused");
        Check(P(Rule("https://10.0.0.12:9940/api/cmd?pin=1234")) is { } both && both.Contains("pin=", StringComparison.Ordinal) && !both.Contains("1234", StringComparison.Ordinal),
            "trigger auth: a secret in the query beside a profile is refused, naming the parameter, never its value");

        var legacy = Rule("http://10.0.0.5:9924/api/next?x=1&pin=12%2034&y=2#top", TriggerAuthRules.None, "");
        Check(P(legacy) is null && TriggerAuthRules.Warning(legacy) is { } warn && warn.Contains("pin=", StringComparison.Ordinal)
              && !warn.Contains("12", StringComparison.Ordinal) && warn.Contains("stagewatch", StringComparison.Ordinal),
            "trigger auth: a ?pin= rule with no profile is never refused — a warning offers to move it into a profile of kind stagewatch");
        Check(TriggerAuthRules.QueryPin(legacy) is { Target: "http://10.0.0.5:9924/api/next?x=1&y=2#top", Pin: "12 34" }
              && TriggerAuthRules.QueryPin(Rule("http://h/api?pin={ITEM}", TriggerAuthRules.None)) is null
              && TriggerAuthRules.QueryPin(Rule("http://h/api?pin=9", TriggerAuthRules.None)) is { Target: "http://h/api", Pin: "9" },
            "trigger auth: the move takes the PIN out of the address whole, unescaped, and never a placeholder");
        Check(TriggerAuthRules.QuerySecret("http://h/a?api_key=1") == "api_key" && TriggerAuthRules.QuerySecret("http://h/a?apikey=1") == "apikey"
              && TriggerAuthRules.QuerySecret("http://h/a?accessToken=1") == "accessToken" && TriggerAuthRules.QuerySecret("http://h/a?TOKEN=1") == "TOKEN"
              && TriggerAuthRules.QuerySecret("http://h/a?keyword=1&spinner=2&apin=3") is null && TriggerAuthRules.QuerySecret("http://h/a#?pin=1") is null,
            "trigger auth: a query parameter is a secret by its name or a part of it, never by a longer word that contains one");
        Check(TriggerAuthRules.Warning(Rule("http://h/a?token=1", TriggerAuthRules.None)) is { } other && !other.Contains("kind stagewatch", StringComparison.Ordinal),
            "trigger auth: a token in a query is warned of, and the PIN's move is offered only for a pin");

        Check(TriggerAuthRules.Headers(TriggerAuthRules.PlayMap, "s", "tr1-x").SequenceEqual([("X-PlayMap-Token", "s"), ("X-Request-Id", "tr1-x")])
              && TriggerAuthRules.Headers(TriggerAuthRules.Patterns, "s", "tr1-x").SequenceEqual([("X-Patterns-Token", "s"), ("X-Patterns-Client", "Stagewatch")])
              && TriggerAuthRules.Headers(TriggerAuthRules.Stagewatch, "s", "tr1-x").SequenceEqual([("X-Stagewatch-Pin", "s"), ("X-Request-Id", "tr1-x")])
              && TriggerAuthRules.Headers(TriggerAuthRules.Bearer, "s", "tr1-x").SequenceEqual([("Authorization", "Bearer s")])
              && TriggerAuthRules.Headers(TriggerAuthRules.None, "s", "tr1-x").Count == 0,
            "trigger auth: each kind's headers — PlayMap's token and request id, Patterns' token and client, Stagewatch's PIN and id, a bearer");
        Check(TriggerAuthRules.Body(TriggerAuthRules.Patterns, "tr1-x", "CUE GO 12") == "@tr1-x CUE GO 12"
              && TriggerAuthRules.Body(TriggerAuthRules.PlayMap, "tr1-x", "PLAYLIST GO a") == "PLAYLIST GO a",
            "trigger auth: Patterns' line carries its id before it; the others post the payload as it is");
        Check(TriggerAuthRules.SecretProblem("ab\r\ncd") is { } ctl && ctl.Contains("control character", StringComparison.Ordinal)
              && TriggerAuthRules.SecretProblem(new string('x', TriggerAuthRules.SecretMaxChars + 1)) is not null && TriggerAuthRules.SecretProblem("ok") is null,
            "trigger auth: a secret with a control character (a header would break) or too long is not sent");
        Check(TriggerAuthRules.SecretProblem("pässwort") is { } wide && wide.Contains("printable ASCII", StringComparison.Ordinal)
              && TriggerAuthRules.SecretProblem("tok\u00a0en") is not null && TriggerAuthRules.SecretProblem("A-z 0~9!") is null,
            "trigger auth: a secret with any character outside printable ASCII is not sent — no header carries it");
        var c = Rule("https://10.0.0.12:9940/api/cmd");
        var copy = c.Clone();
        copy.Auth!.SecretProtected = "changed";
        Check(c.Auth!.SecretProtected == "prot:" + Secret && copy.Name == c.Name && copy.Target == c.Target,
            "trigger auth: a rule's copy shares no profile with it");
    }

    static void Judge(Action<bool, string> Check)
    {
        var a = new string('a', 64);
        var b = new string('b', 64);
        Check(TriggerPinStore.Judge(null, a, 5) is (false, { Fingerprint: var f1, Trust: PinTrust.First, At: 5 }, false) && f1 == a,
            "trigger pins: no pin — the certificate shown is pinned first and nothing is sent");
        Check(TriggerPinStore.Judge(new TriggerPin(a, PinTrust.First, 1), a, 5) is (false, null, false),
            "trigger pins: a first pin it matches sends nothing — kept for comparison, never trust");
        Check(TriggerPinStore.Judge(new TriggerPin(a, PinTrust.First, 1), b, 5) is (false, { Fingerprint: var f2, Trust: PinTrust.First }, false) && f2 == b,
            "trigger pins: a first pin of another certificate gives way to this one, pinned first, nothing sent");
        Check(TriggerPinStore.Judge(new TriggerPin(a, PinTrust.Person, 1), a, 5) is (true, null, false),
            "trigger pins: a person's pin it matches — the secret goes");
        Check(TriggerPinStore.Judge(new TriggerPin(a, PinTrust.Person, 1), b, 5) is (false, null, true),
            "trigger pins: a person's pin it does not match — CERTIFICATE CHANGED, nothing sent, the pin kept");
        var target = TriggerAuthRules.ParseTarget("https://Stage-PC:9940/x", out _)!;
        Check(TriggerPinStore.AddrKey(target) == "addr:stage-pc:9940" && TriggerPinStore.NodeKey("pm5e6f7a") == "node:pm5e6f7a"
              && TriggerPinStore.NodeKey("bad id!") is null && TriggerPinStore.NodeKey(null) is null
              && TriggerPinStore.AddrKey(TriggerAuthRules.ParseTarget("https://[::1]/x", out _)!) == "addr:[::1]:443",
            "trigger pins: kept by host and port (the default port named), and by the node id a hello gives besides");

        var pa = new TriggerPin(a, PinTrust.Person, 1);
        var pb = new TriggerPin(b, PinTrust.Person, 1);
        var fa = new TriggerPin(a, PinTrust.First, 1);
        var fb = new TriggerPin(b, PinTrust.First, 1);
        var c = new string('c', 64);
        Check(TriggerPinStore.Effective(null, null, a) is null && TriggerPinStore.Effective(pa, null, a) == pa && TriggerPinStore.Effective(null, pa, a) == pa,
            "trigger pins: a person's pin under the node or under the address is the pin");
        Check(TriggerPinStore.Effective(pb, pa, a) == pa && TriggerPinStore.Effective(pa, pb, a) == pa && TriggerPinStore.Effective(pb, pa, c) == pb,
            "trigger pins: two persons' pins — the one the certificate matches, else the node's (CERTIFICATE CHANGED against it)");
        Check(TriggerPinStore.Effective(fa, pb, a) == pb && TriggerPinStore.Effective(pb, fa, a) == pb,
            "trigger pins: a person's pin under either outranks a first pin under the other — a hello cannot trade it for a first pin");
        Check(TriggerPinStore.Effective(fa, fb, b) == fb && TriggerPinStore.Effective(fa, fb, c) == fa && TriggerPinStore.Effective(null, fb, a) == fb,
            "trigger pins: with first pins only, the one the certificate matches, else the node's, else the address's");

        var full = new Dictionary<string, TriggerPin> { ["addr:p:1"] = new(a, PinTrust.Person, 1), ["addr:f:2"] = new(a, PinTrust.First, 5), ["addr:f:3"] = new(a, PinTrust.First, 3) };
        var persons = new Dictionary<string, TriggerPin> { ["addr:p:1"] = new(a, PinTrust.Person, 9), ["addr:p:2"] = new(a, PinTrust.Person, 4) };
        Check(TriggerPinStore.Room(full, PinTrust.First) == "addr:f:3" && TriggerPinStore.Room(full, PinTrust.Person) == "addr:f:3"
              && TriggerPinStore.Room(persons, PinTrust.First) is null && TriggerPinStore.Room(persons, PinTrust.Person) == "addr:p:2",
            "trigger pins: in a full store a first pin makes room before any person's; a first pin never takes a person's place");
    }

    static void NodeAt(Action<bool, string> Check)
    {
        static SlidesBeacon.Peer Peer(string id, string address, double age = 1, int https = 9940)
            => new(new SlidesBeacon.Hello(PlayoutRules.PlayMap, 1, id, "Stage left", "STAGE-PC", 0, false, Proto: 3,
                Ports: new() { ["https"] = https, ["wire"] = 9941 }, Host: address), address, age) { Addresses = [address] };
        Check(TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.12")], "10.0.0.12", 9940) == "pm5e6f7a"
              && TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.12")], "10.0.0.12", 9941) == "pm5e6f7a",
            "trigger pins: the node id of the hello heard at the target's host with its port among the hello's doors");
        Check(TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.12")], "10.0.0.12", 8080) is null
              && TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.13")], "10.0.0.12", 9940) is null
              && TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.12", age: 60)], "10.0.0.12", 9940) is null
              && TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.12"), Peer("pm00ff11", "10.0.0.12")], "10.0.0.12", 9940) is null
              && TriggerPinStore.NodeAt([Peer("pm5e6f7a", "10.0.0.12") with { Conflict = new HelloConflict("pm5e6f7a", "STAGE-PC", "10.0.0.12", "OTHER-PC", "10.0.0.40") }], "10.0.0.12", 9940) is null,
            "trigger pins: none for another port or host, a stale hello, two nodes at one door, or a hello under a conflict mark — then the address keys it");
    }

    static async Task PinFile(Action<bool, string> Check)
    {
        using var folder = Scratch.Folder("trigger-pins");
        var path = Path.Combine(folder.Path, "trigger-pins.json");
        var store = new TriggerPinStore(path);
        var fp = new string('c', 64);
        var landed = await store.Put("addr:10.0.0.12:9940", new TriggerPin(fp, PinTrust.Person, 1000));
        var again = new TriggerPinStore(path);
        Check(!store.Lost && landed && again.Get("addr:10.0.0.12:9940") == new TriggerPin(fp, PinTrust.Person, 1000) && !again.Lost,
            "trigger pins: a pin is written on the store's lane and read back by the next store");
        Check(!await store.Put("elsewhere", new TriggerPin(fp, PinTrust.Person, 1)) && !await store.Put("addr:x:1", new TriggerPin("ab", PinTrust.Person, 1))
              && store.Get("elsewhere") is null,
            "trigger pins: a key or fingerprint of the wrong shape is kept nowhere");
        var parsed = TriggerPinStore.Parse("""{"v":1,"pins":{"node:pm5e6f7a":{"fingerprint":"%F","trust":"person","at":3},"bad key":{"fingerprint":"%F","trust":"first"},"addr:a:1":{"fingerprint":"%F","trust":"proof"},"addr:b:1":{"fingerprint":"abc","trust":"first"}}}""".Replace("%F", fp), out var unreadable);
        Check(!unreadable && parsed.Count == 1 && parsed["node:pm5e6f7a"].Trust == PinTrust.Person,
            "trigger pins: an entry it cannot read — a bad key, a trust no person made, a short fingerprint — is dropped, never guessed");
        File.WriteAllText(path, "not json {");
        var lost = new TriggerPinStore(path);
        Check(lost.Lost && lost.Pins.Count == 0, "trigger pins: a file that cannot be read is no pins, and said lost (13.8.4)");
        using var h = new Harness(o => o with { Pins = lost });
        Check(h.Runner.Status.Contains(TriggerPinStore.LostWords, StringComparison.Ordinal) && h.Runner.Health == LinkHealth.Starting,
            "trigger pins: lost pins are amber in the triggers' status, in words");
        var wrote = await lost.Put("addr:10.0.0.12:9940", new TriggerPin(fp, PinTrust.First, 5));
        Check(wrote && !lost.Lost && !h.Runner.Status.Contains(TriggerPinStore.LostWords, StringComparison.Ordinal) && h.Runner.Health == LinkHealth.Ok
              && new TriggerPinStore(path) is { Lost: false } reread && reread.Get("addr:10.0.0.12:9940") is not null,
            "trigger pins: once a write lands the file holds what is kept — the pins are lost no more, and the light says so");

        static string N(int i) => "addr:h:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var full = new TriggerPinStore(null);
        for (var i = 1; i <= TriggerPinStore.MaxEntries + 1; i++) _ = full.Put(N(i), new TriggerPin(fp, PinTrust.First, i));
        Check(TriggerPinStore.MaxEntries == 128 && full.Pins.Count == TriggerPinStore.MaxEntries && full.Get(N(1)) is null && full.Get(N(2)) is not null,
            "trigger pins: at most 128 (64 targets by address and node), the first pin made longest ago making room");
        var trusted = new TriggerPinStore(null);
        _ = trusted.Put(N(1), new TriggerPin(fp, PinTrust.First, 50));
        for (var i = 2; i <= TriggerPinStore.MaxEntries; i++) _ = trusted.Put(N(i), new TriggerPin(fp, PinTrust.Person, i));
        var personIn = await trusted.Put(N(500), new TriggerPin(fp, PinTrust.Person, 600));
        var firstOut = await trusted.Put(N(501), new TriggerPin(fp, PinTrust.First, 601));
        Check(personIn && trusted.Get(N(1)) is null && trusted.Get(N(2)) is { Trust: PinTrust.Person } && !firstOut && trusted.Get(N(501)) is null
              && trusted.Pins.Values.All(p => p.Trust == PinTrust.Person) && trusted.Pins.Count == TriggerPinStore.MaxEntries,
            "trigger pins: a person's pin pushes out a first pin before any person's, and a first pin never takes a person's place");
    }

    static async Task HoldFile(Action<bool, string> Check)
    {
        using var folder = Scratch.Folder("trigger-holds");
        var path = Path.Combine(folder.Path, "trigger-pins.json");
        var store = new TriggerPinStore(path);
        const long Now = 1_800_000_000_000;
        var tag = new string('1', 16);
        var other = new string('2', 16);
        var mark = new string('m', 64);
        var landed = await store.Hold(new TriggerHold("10.0.0.12:9940", TriggerAuthRules.PlayMap, mark, [tag], Now + 60_000, 60_000, "10.0.0.12:9940 refused its secret (401)"), Now);
        var text = File.ReadAllText(path);
        Check(landed && text.Contains("\"until\":" + (Now + 60_000).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
              && text.Contains("\"tags\":[\"" + tag + "\"]", StringComparison.Ordinal) && !text.Contains(mark, StringComparison.Ordinal),
            "trigger holds: a hold's end is written in wall time beside the pins, and its mark never is");
        var back = new TriggerPinStore(path);
        Check(back.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, "another-mark", tag, Now + 1_000) is { Hold.Mark: null, LeftMs: 59_000 }
              && back.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, mark, other, Now + 1_000) is null
              && back.Held("10.0.0.12:9940", TriggerAuthRules.Patterns, "x", tag, Now + 1_000) is null
              && back.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, "x", tag, Now + 60_000) is null,
            "trigger holds: read back at start, a hold holds by its tags (no mark survives a restart) — another secret, kind or a hold over is not held");
        Check(store.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, mark, other, Now + 1_000) is { LeftMs: 59_000 },
            "trigger holds: in the process that held it, a secret is held by its mark, whatever protected form it came from");
        Check(back.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, "x", tag, Now - 3_600_000) is { LeftMs: 60_000 }
              && back.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, "x", tag, Now - 3_600_000 + 60_000) is null,
            "trigger holds: a wall clock stepped back never lengthens a hold — it ends its length from then");
        Check(TriggerPinStore.ParseHolds("""{"v":1,"pins":{},"holds":[{"where":"a:1","kind":"telnet","tags":["1111111111111111"],"until":5,"ms":5,"why":""},{"where":"a:1","kind":"playmap","tags":["xyz"],"until":5,"ms":5,"why":""},{"where":"a:1","kind":"playmap","tags":["1111111111111111"],"until":5,"ms":999999999,"why":""},{"where":"a:1","kind":"playmap","tags":["1111111111111111"],"until":5,"ms":5,"why":"ok"}]}""") is [{ Where: "a:1", Why: "ok", Mark: null }]
              && TriggerPinStore.ParseHolds("not json").Count == 0 && TriggerPinStore.ParseHolds("""{"v":1,"pins":{}}""").Count == 0,
            "trigger holds: an entry it cannot read — an unknown kind, a tag of the wrong shape, a hold longer than ten minutes — is dropped");
        _ = store.Hold(new TriggerHold("10.0.0.12:9940", TriggerAuthRules.PlayMap, mark, [other], Now + 120_000, 120_000, "429"), Now);
        Check(store.Holds(Now).Count == 1 && store.Held("10.0.0.12:9940", TriggerAuthRules.PlayMap, mark, "", Now) is { Hold.Why: "429" },
            "trigger holds: holding the same secret again replaces its hold");
        await store.Flushed;
    }

    static void Settings(Action<bool, string> Check)
    {
        using var folder = Scratch.Folder("trigger-settings");
        var path = Path.Combine(folder.Path, "settings.json");
        using (var store = new SettingsStore(path))
        {
            store.Update(s =>
            {
                s.Triggers.Add(Rule("https://10.0.0.12:9940/api/cmd"));
                s.Triggers.Add(Rule("http://10.0.0.5:9924/api/next?pin=1", TriggerAuthRules.None, "", name: "Next"));
            });
            store.SaveNow();
        }
        var text = File.ReadAllText(path);
        Check(Regex.Matches(text, "\"auth\"").Count == 1 && text.Contains("\"kind\": \"playmap\"", StringComparison.Ordinal)
              && text.Contains("\"secret\": \"prot:" + Secret + "\"", StringComparison.Ordinal),
            "trigger auth: a profile is kept in the settings file as its kind and the secret as the desk protected it; a rule with none has no auth");
        using (var back = new SettingsStore(path))
        {
            Check(back.Settings.Triggers[0].Auth is { Kind: TriggerAuthRules.PlayMap, SecretProtected: "prot:" + Secret } && back.Settings.Triggers[1].Auth is null
                  && back.Notes.Count == 0,
                "trigger auth: a file read back keeps each rule's profile, and says nothing of rules that may fire");
            back.Update(s => s.Triggers.Add(Rule("http://10.0.0.6:9941/api/cmd", name: "Bad")));
            back.SaveNow();
        }
        using var noted = new SettingsStore(path);
        Check(noted.Notes.Any(n => n.Contains("trigger 'Bad' is refused as it fires", StringComparison.Ordinal) && n.Contains("plain HTTP", StringComparison.Ordinal))
              && noted.Notes.All(n => !n.Contains(Secret, StringComparison.Ordinal)) && noted.Settings.Triggers.Count == 3,
            "trigger auth: a file whose rule would send a secret over plain HTTP to another machine keeps it and says it is refused as it fires");
    }

    static void AtRest(Action<bool, string> Check)
    {
        if (!Bench.OnWindows("trigger secret protected at rest (DPAPI round trip for this Windows user)")) return;
        var stored = Stagewatch.App.ViewModels.ProtectedKey.Protect(Secret);
        Check(stored.Length > 0 && !stored.Contains(Secret, StringComparison.Ordinal) && Stagewatch.App.ViewModels.ProtectedKey.Unprotect(stored) == Secret
              && Stagewatch.App.ViewModels.ProtectedKey.Unprotect("bm90IGRwYXBp") == "",
            "trigger auth: a secret is DPAPI-protected at rest, read back for this user, and one that is not DPAPI's is none");
        using var folder = Scratch.Folder("trigger-dpapi");
        var path = Path.Combine(folder.Path, "settings.json");
        using (var store = new SettingsStore(path))
        {
            store.Update(s => s.Triggers.Add(Rule("https://10.0.0.12:9940/api/cmd", secret: stored)));
            store.SaveNow();
        }
        Check(!File.ReadAllText(path).Contains(Secret, StringComparison.Ordinal), "trigger auth: the settings file holds no secret in clear");
    }

    /// <summary>13.12.3 for Patterns' real answers: CUE GO's outcome inside msg read by the line rows, never its JSON as words.</summary>
    static void PatternsAnswers(Action<bool, string> Check)
    {
        var done = TriggerRunner.Read(200, CueGo("Done"));
        Check(done.Outcome == PeerOutcome.Applied && done.Words == "cue 12 'Walk-in'",
            "trigger answers: Patterns' CUE GO Done (its outcome's JSON inside msg) is applied, naming the cue it ran — never the JSON");
        var confirm = TriggerRunner.Read(200, CueGo("Confirm"));
        Check(confirm.Outcome == PeerOutcome.Accepted && confirm.Words.Contains("asks to be confirmed", StringComparison.Ordinal)
              && confirm.Words.Contains("second GO", StringComparison.Ordinal) && !confirm.Words.Contains('{'),
            "trigger answers: Confirm is accepted, never applied — the cue waits for a person's second GO");
        var requested = TriggerRunner.Read(200, CueGo("Requested"));
        Check(requested.Outcome == PeerOutcome.Accepted && requested.Words == "cue 12 'Walk-in'",
            "trigger answers: Requested is accepted — asked for, not yet run");
        Check(TriggerRunner.Read(200, PatternsCmd("OK @tr9-x " + System.Text.Json.JsonSerializer.Serialize(new { outcome = "Done" }), "tr9-x")).Outcome == PeerOutcome.Applied
              && TriggerRunner.Read(200, CueGo("Done", id: null)).Outcome == PeerOutcome.Applied
              && TriggerRunner.Read(200, PatternsCmd("OK " + System.Text.Json.JsonSerializer.Serialize(new { standby = Standby13 }), "tr9-x")) is { Outcome: PeerOutcome.Applied, Words: "" },
            "trigger answers: the object is read with an @id before it or none, and an outcome-less object is applied with no words of its JSON");
        Check(TriggerRunner.Read(200, PatternsCmd("OK Look 'Walk-in' applied", "tr9-x")) is { Outcome: PeerOutcome.Applied, Words: "Look 'Walk-in' applied" }
              && TriggerRunner.Read(200, PatternsCmd("ERR no cue 99", "tr9-x")) is { Outcome: PeerOutcome.Refused, Words: "no cue 99" }
              && TriggerRunner.Read(500, CueGo("Done")).Outcome == PeerOutcome.Unconfirmed
              && TriggerRunner.Read(200, "{\"ok\":true,\"stage\":\"accepted\",\"effect\":\"queued\"}").Outcome == PeerOutcome.Accepted,
            "trigger answers: a plain OK or ERR line in msg, a 5xx and a receipt read by 13.12.3's HTTP rows as before");
    }

    // ------------------------------------------------------------------ the runner against targets

    static async Task Kinds(Action<bool, string> Check)
    {
        await using var t = new Target();
        using var h = new Harness();
        var url = $"http://127.0.0.1:{t.Port}/api/cmd";

        t.Answer = q => (409, $"{{\"type\":\"receipt\",\"id\":\"{q.Headers.GetValueOrDefault("X-Request-Id")}\",\"ok\":false,\"stage\":\"refused\",\"effect\":\"\",\"error\":\"Main show has no item it9\",\"refs\":null}}", "");
        var s = await h.Fire(Rule(url));
        var q = t.Last();
        Check(q is { Method: "POST", Path: "/api/cmd", Body: "PLAYLIST GO show #4" } && q.Headers.GetValueOrDefault("X-PlayMap-Token") == Secret
              && q.Headers.GetValueOrDefault("Content-Type") is { } type && type.StartsWith("text/plain", StringComparison.Ordinal)
              && q.Headers.GetValueOrDefault("X-Request-Id") is { } rid && rid == s.RequestId && Regex.IsMatch(rid, "^tr[0-9]+-" + Inst + "$"),
            "trigger auth: PlayMap gets its token in X-PlayMap-Token and the request id tr<n>-<inst> in X-Request-Id, the line as the body");
        Check(s is { Outcome: PeerOutcome.Refused, Authenticated: true, Kind: TriggerAuthRules.PlayMap } && s.Words.Contains("Main show has no item it9", StringComparison.Ordinal)
              && s.Where == "127.0.0.1:" + t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "trigger auth: PlayMap's 409 receipt is refused, in its words (13.12.3)");
        t.Answer = _ => (200, Applied("playing Intro"), "");
        var applied = await h.Fire(Rule(url));
        Check(applied.Outcome == PeerOutcome.Applied && applied.Words.Contains("playing Intro", StringComparison.Ordinal) && applied.RequestId != s.RequestId
              && h.Runner.Status.Contains("applied", StringComparison.Ordinal) && h.Runner.Health == LinkHealth.Ok,
            "trigger auth: an applied receipt is applied, each fire with an id of its own, in the triggers' status");

        t.Answer = r => { var id = r.Body.Split(' ')[0].TrimStart('@'); return (200, $"{{\"ok\":false,\"msg\":\"ERR @{id} standby moved\",\"id\":\"{id}\"}}", ""); };
        s = await h.Fire(Rule(url, TriggerAuthRules.Patterns, "CUE GO 12"));
        q = t.Last();
        Check(q is not null && q.Body == "@" + s.RequestId + " CUE GO 12" && q.Headers.GetValueOrDefault("X-Patterns-Token") == Secret
              && q.Headers.GetValueOrDefault("X-Patterns-Client") == "Stagewatch" && !q.Headers.ContainsKey("X-Request-Id") && !q.Headers.ContainsKey("X-PlayMap-Token"),
            "trigger auth: Patterns gets X-Patterns-Token and X-Patterns-Client, its request id before the line in the body");
        Check(s.Outcome == PeerOutcome.Refused && s.Words.Contains("standby moved", StringComparison.Ordinal) && !s.Words.Contains("ERR @", StringComparison.Ordinal),
            "trigger auth: Patterns' 200 with ok:false is refused, its words after ERR and the id (13.12.3)");
        t.Answer = r => (200, CueGo("Done", r.Body.Split(' ')[0].TrimStart('@')), "");
        s = await h.Fire(Rule(url, TriggerAuthRules.Patterns, "CUE GO 12"));
        Check(s.Outcome == PeerOutcome.Applied && s.Words == "cue 12 'Walk-in'" && !h.Runner.Status.Contains("outcome", StringComparison.Ordinal),
            "trigger auth: Patterns' CUE GO Done, as its desk answers it, is applied in words — its JSON in no status");
        t.Answer = r => (200, CueGo("Confirm", r.Body.Split(' ')[0].TrimStart('@')), "");
        s = await h.Fire(Rule(url, TriggerAuthRules.Patterns, "CUE GO 12"));
        Check(s.Outcome == PeerOutcome.Accepted && s.Words.Contains("second GO", StringComparison.Ordinal) && s.Row.Contains("accepted", StringComparison.Ordinal),
            "trigger auth: Patterns' Confirm is accepted in the status and the show log's row, never applied — a person's second GO is owed");
        t.Answer = r => (200, CueGo("Requested", r.Body.Split(' ')[0].TrimStart('@')), "");
        s = await h.Fire(Rule(url, TriggerAuthRules.Patterns, "CUE GO 12"));
        Check(s.Outcome == PeerOutcome.Accepted, "trigger auth: Patterns' Requested is accepted");

        t.Answer = _ => (200, "{\"ok\":true,\"stage\":\"accepted\",\"effect\":\"queued on the show thread\"}", "");
        s = await h.Fire(Rule(url, TriggerAuthRules.Stagewatch, "next"));
        q = t.Last();
        Check(q is not null && q.Headers.GetValueOrDefault("X-Stagewatch-Pin") == Secret && q.Headers.GetValueOrDefault("X-Request-Id") == s.RequestId
              && s.Outcome == PeerOutcome.Accepted && s.Words.Contains("queued", StringComparison.Ordinal),
            "trigger auth: Stagewatch gets its PIN in X-Stagewatch-Pin with a request id; an accepted receipt is accepted, never done");

        t.Answer = _ => (200, "{\"ok\":true}", "");
        s = await h.Fire(Rule(url, TriggerAuthRules.Bearer, ""));
        q = t.Last();
        Check(q is { Method: "GET" } && q.Headers.GetValueOrDefault("Authorization") == "Bearer " + Secret && s.Outcome == PeerOutcome.Accepted
              && s.Words.Contains("does not say whether it ran", StringComparison.Ordinal),
            "trigger auth: a bearer goes in Authorization; a 2xx that says neither stage nor msg is accepted — it did not say it ran");

        t.Answer = _ => (503, "", "");
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.Unconfirmed && s.Words.Contains("may have run", StringComparison.Ordinal),
            "trigger auth: a 5xx after the request went is unconfirmed — it may have run");
        Check(h.Runner.Health == LinkHealth.Starting && h.Runner.Status.Contains("'GO VT' was unconfirmed at its last fire", StringComparison.Ordinal),
            "trigger auth: a rule whose last fire with a secret is unconfirmed holds the triggers amber, naming it");
        t.Answer = _ => (200, Applied("ok"), "");
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.Applied && h.Runner.Health == LinkHealth.Ok && !h.Runner.Status.Contains("at its last fire", StringComparison.Ordinal),
            "trigger auth: its next answer clears it");
        Check(h.All.All(x => !x.Row.Contains(Secret, StringComparison.Ordinal)) && !h.Runner.Status.Contains(Secret, StringComparison.Ordinal),
            "trigger auth: the secret is in no outcome's row and not in the status");
    }

    static async Task Fixed(Action<bool, string> Check)
    {
        await using var t = new Target();
        using var h = new Harness();
        var port = t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        t.Answer = r => r.Path == "/api/cmd" ? (302, "", "Location: /elsewhere\r\n") : (200, Applied("ran elsewhere"), "");
        var s = await h.Fire(Rule($"http://127.0.0.1:{port}/api/cmd"));
        Check(t.Requests == 1 && s.Outcome == PeerOutcome.Unconfirmed && s.Words.Contains("a redirect — not followed; it may have run", StringComparison.Ordinal)
              && t.Last()?.Path == "/api/cmd",
            "trigger auth: a 302 after the request went is not followed and not called refused — unconfirmed, it may have run; nothing goes where it points");

        t.Answer = _ => (200, Applied("next"), "");
        var ctx = new TriggerContext(4, "Walk in/out?", "wrapup", "00:30", "x", "Hall A&B");
        s = await h.Fire(Rule($"http://127.0.0.1:{port}/api/{{TITLE}}?room={{ROOM}}&n={{ITEM}}", TriggerAuthRules.Stagewatch, "next"), ctx);
        Check(s.Outcome == PeerOutcome.Applied && t.Last()?.Path == "/api/Walk%20in%2Fout%3F?room=Hall%20A%26B&n=4",
            "trigger auth: show text fills the path and query alone, escaped, so it never changes the address's shape");

        var before = t.Accepts;
        var hostPlaceholder = Rule("http://{ROOM}:" + port + "/api/cmd");
        s = await h.Fire(hostPlaceholder, new TriggerContext(1, "", "", "", "", "127.0.0.1"));
        Check(s.Outcome == PeerOutcome.NotSent && s.Words.Contains("placeholder", StringComparison.Ordinal) && t.Accepts == before,
            "trigger auth: a placeholder in the host is refused as it fires too, even when show text would name this PC — nothing dialled");
    }

    static async Task Refusals(Action<bool, string> Check)
    {
        await using var t = new Target();
        var port = t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var url = $"http://127.0.0.1:{port}/api/cmd";
        using (var lan = new Harness(o => o with { IsLoopback = _ => false }))
        {
            var s = await lan.Fire(Rule(url));
            Check(s.Outcome == PeerOutcome.NotSent && s.Words.Contains("plain HTTP only to this PC", StringComparison.Ordinal) && t.Accepts == 0,
                "trigger auth: a secret rule over plain HTTP to another machine is refused as it fires — the target hears nothing");
        }
        using var h = new Harness();
        t.Answer = _ => (200, Applied("ok"), "");
        var local = await h.Fire(Rule($"http://localhost:{port}/api/cmd"));
        Check(local.Outcome == PeerOutcome.Applied && t.Requests == 1 && t.Last()?.Headers.GetValueOrDefault("X-PlayMap-Token") == Secret,
            "trigger auth: localhost is this PC — its address checked as it connects — and the secret goes");
        using (var elsewhere = new Harness(o => o with { Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("10.255.255.1"), IPAddress.Parse("192.0.2.7") }) }))
        {
            var accepts = t.Accepts;
            var moved = await elsewhere.Fire(Rule($"http://localhost:{port}/api/cmd"));
            Check(moved.Outcome == PeerOutcome.NotSent && moved.Words.Contains("is not this PC", StringComparison.Ordinal) && t.Accepts == accepts,
                "trigger auth: localhost that resolves to another machine is dialled nowhere — a secret over plain HTTP goes to this PC's address alone");
        }
        var unreadable = await h.Fire(Rule(url, secret: "kept-by-another-user"));
        Check(unreadable.Outcome == PeerOutcome.NotSent && unreadable.Words.Contains("can't be read on this PC", StringComparison.Ordinal) && t.Requests == 1,
            "trigger auth: a secret this PC cannot read is not sent, in words");
        var control = await h.Fire(Rule(url, secret: "prot:ab\r\nX-Evil: 1"));
        Check(control.Outcome == PeerOutcome.NotSent && control.Words.Contains("control character", StringComparison.Ordinal) && t.Requests == 1,
            "trigger auth: a secret that would break its header is not sent");
        var acceptsBefore = t.Accepts;
        var wide = await h.Fire(Rule(url, secret: "prot:pässwort"));
        Check(wide.Outcome == PeerOutcome.NotSent && wide.Words.Contains("printable ASCII", StringComparison.Ordinal) && t.Accepts == acceptsBefore,
            "trigger auth: a secret outside printable ASCII is not sent and nothing is dialled — never 'it may have run'");
        t.Answer = _ => (200, Applied("ran"), "");
        t.Short = 40;
        var cut = await h.Fire(Rule(url));
        t.Short = 0;
        Check(cut.Outcome == PeerOutcome.Unconfirmed && cut.Words.Contains("ended before it answered — it may have run", StringComparison.Ordinal)
              && cut.RequestId is not null && t.Requests == 2 && h.All[^1] == cut && h.Runner.Health == LinkHealth.Starting,
            "trigger auth: an answer cut off after its headers is unconfirmed — the request was written — and settles as a row like any other");
        t.Answer = _ => (403, $"{{\"ok\":false,\"error\":\"token {Secret} is not this node's\"}}", "");
        var echo = await h.Fire(Rule(url));
        Check(echo.Outcome == PeerOutcome.Refused && echo.Words.Contains("•••", StringComparison.Ordinal) && !echo.Words.Contains(Secret, StringComparison.Ordinal)
              && !h.Runner.Status.Contains(Secret, StringComparison.Ordinal),
            "trigger auth: a target that echoes the secret back has it masked in the outcome and the status");
        using (var throwing = new Harness(o => o with { Unprotect = _ => throw new InvalidOperationException("the protector failed") }))
        {
            var accepts = t.Accepts;
            var failed = await throwing.Fire(Rule(url));
            Check(failed is { Outcome: PeerOutcome.NotSent, Authenticated: true } && failed.Words.Contains("failed — InvalidOperationException; not sent", StringComparison.Ordinal)
                  && !failed.Words.Contains("protector", StringComparison.Ordinal) && t.Accepts == accepts,
                "trigger auth: a fire that fails before anything went still settles as one row — not sent, by the exception's type, never its message");
        }
        await using var closed = new Target();
        var closedPort = closed.Port;
        await closed.DisposeAsync();
        var down = await h.Fire(Rule($"http://127.0.0.1:{closedPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}/api/cmd"));
        Check(down.Outcome == PeerOutcome.NotSent && down.Words.Contains("could not reach", StringComparison.Ordinal),
            "trigger auth: no connection is not sent — it did not run");
    }

    static async Task Holds(Action<bool, string> Check)
    {
        var clock = new ShowWorkflowTests.ManualTime(DateTime.UtcNow);
        await using var t = new Target();
        using var h = new Harness(o => o with { Time = clock });
        var url = $"http://127.0.0.1:{t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/api/cmd";
        t.Answer = _ => (401, "{\"ok\":false,\"error\":\"wrong token\"}", "");
        var s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.Refused && s.Words.Contains("wrong token", StringComparison.Ordinal) && h.Runner.Health == LinkHealth.Error
              && h.Runner.Status.Contains("held:", StringComparison.Ordinal),
            "trigger auth: a 401 is refused and holds the secret (13.15.3) — red, in words");
        var held = await h.Fire(Rule(url));
        Check(held.Outcome == PeerOutcome.NotSent && held.Words.Contains("held", StringComparison.Ordinal) && t.Requests == 1,
            "trigger auth: a held secret is not sent again — the target is not taught to lock this PC out");
        t.Answer = _ => (200, Applied("ok"), "");
        var fresh = await h.Fire(Rule(url, secret: "prot:another"));
        Check(fresh.Outcome == PeerOutcome.Applied && t.Requests == 2, "trigger auth: a new secret is not held by the old one's refusal");
        clock.Advance(TimeSpan.FromSeconds(61));
        var after = await h.Fire(Rule(url));
        Check(after.Outcome == PeerOutcome.Applied && t.Requests == 3 && h.Runner.Health == LinkHealth.Ok,
            "trigger auth: a hold ends after its minute, and the secret goes again");
        t.Answer = _ => (429, "{\"ok\":false,\"error\":\"locked\"}", "Retry-After: 120\r\n");
        var locked = await h.Fire(Rule(url));
        clock.Advance(TimeSpan.FromSeconds(61));
        var still = await h.Fire(Rule(url));
        clock.Advance(TimeSpan.FromSeconds(60));
        t.Answer = _ => (200, Applied("ok"), "");
        var free = await h.Fire(Rule(url));
        Check(locked.Outcome == PeerOutcome.Refused && locked.Words.Contains("again in 120 s", StringComparison.Ordinal) && still.Outcome == PeerOutcome.NotSent
              && free.Outcome == PeerOutcome.Applied && t.Requests == 5,
            "trigger auth: a 429 holds the secret for its Retry-After (a minute at least), then it goes again");

        // One secret in two rules, each its own protected form (DPAPI's differ each time): one 401 holds both.
        using var folder = Scratch.Folder("trigger-holds-run");
        var path = Path.Combine(folder.Path, "trigger-pins.json");
        var store = new TriggerPinStore(path);
        var ruleA = Rule(url, secret: "prot:1|" + Secret, name: "A");
        var ruleB = Rule(url, secret: "prot:2|" + Secret, name: "B");
        using (var two = new Harness(o => o with { Time = clock, Pins = store }))
        {
            t.Answer = _ => (401, "{\"ok\":false,\"error\":\"wrong token\"}", "");
            two.Also = [ruleB];
            var a1 = await two.Fire(ruleA);
            two.Also = [];
            var b1 = await two.Fire(ruleB);
            Check(a1.Outcome == PeerOutcome.Refused && b1.Outcome == PeerOutcome.NotSent && b1.Words.Contains("held", StringComparison.Ordinal) && t.Requests == 6,
                "trigger holds: two rules with one secret — one 401 holds both, so the same wrong token never reaches its target twice");
            var again = await two.Fire(Rule(url, secret: "prot:9|" + Secret, name: "C"));
            Check(again.Outcome == PeerOutcome.NotSent && t.Requests == 6,
                "trigger holds: the same wrong secret typed again (a protected form no rule had when it was held) is held by its mark");
        }
        t.Answer = _ => (200, Applied("ok"), "");
        using (var rebuilt = new Harness(o => o with { Time = clock, Pins = store }))
        {
            var a2 = await rebuilt.Fire(ruleA);
            Check(a2.Outcome == PeerOutcome.NotSent && t.Requests == 6 && rebuilt.Runner.Health == LinkHealth.Error,
                "trigger holds: a hold outlasts its runner — one the supervisor builds anew finds it in the store");
        }
        await store.Flushed;
        using (var restarted = new Harness(o => o with { Time = clock, Pins = new TriggerPinStore(path) }))
        {
            var a3 = await restarted.Fire(ruleA);
            var b3 = await restarted.Fire(ruleB);
            var c3 = await restarted.Fire(Rule(url, secret: "prot:3|a-new-secret", name: "A"));
            Check(a3.Outcome == PeerOutcome.NotSent && b3.Outcome == PeerOutcome.NotSent && c3.Outcome == PeerOutcome.Applied && t.Requests == 7,
                "trigger holds: a hold outlasts a restart — read back with its end in wall time, it holds both rules by their protected forms, and a new secret goes");
            clock.Advance(TimeSpan.FromSeconds(61));
            var a4 = await restarted.Fire(ruleA);
            Check(a4.Outcome == PeerOutcome.Applied && t.Requests == 8, "trigger holds: a restored hold ends at its end");
        }
    }

    static async Task Legacy(Action<bool, string> Check)
    {
        await using var t = new Target();
        using var h = new Harness();
        var rule = Rule($"http://127.0.0.1:{t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/api/next?pin=4321", TriggerAuthRules.None, "");
        t.Answer = _ => (200, "{\"ok\":true,\"stage\":\"applied\",\"effect\":\"next item\"}", "");
        var s = await h.Fire(rule);
        var q = t.Last();
        Check(s is { Authenticated: false, Outcome: PeerOutcome.Applied, RequestId: null } && q is { Method: "GET", Path: "/api/next?pin=4321" }
              && !q.Headers.ContainsKey("X-Request-Id"),
            "trigger auth: a ?pin= rule with no profile fires as before — its address whole, no headers added — its answer read by 13.12.3");
        Check(h.Runner.Status.Contains("pin=", StringComparison.Ordinal) && !h.Runner.Status.Contains("4321", StringComparison.Ordinal) && !s.Row.Contains("4321", StringComparison.Ordinal),
            "trigger auth: its status carries the warning, naming pin= and never the PIN");
        t.Answer = _ => (403, "{\"ok\":false,\"error\":\"wrong PIN 4321\"}", "");
        s = await h.Fire(rule);
        Check(s.Outcome == PeerOutcome.Refused && !s.Words.Contains("4321", StringComparison.Ordinal), "trigger auth: a PIN in a query echoed back is masked too");
    }

    static async Task Pinned(Action<bool, string> Check)
    {
        using var files = new KitFiles();
        var a = files.Cert("trigger-a");
        var b = files.Cert("trigger-b");
        await using var t = new Target(a);
        var port = t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var url = $"https://127.0.0.1:{port}/api/cmd";
        var key = "addr:127.0.0.1:" + port;
        var pins = new TriggerPinStore(null);
        using var h = new Harness(o => o with { IsLoopback = _ => false, Pins = pins });
        t.Answer = _ => (200, Applied("playing Intro"), "");
        Check(TriggerAuthRules.Problem(Rule(url), _ => false) is null, "trigger pins: a secret over HTTPS to another machine may be saved");

        var s = await h.Fire(Rule(url));
        var waits = h.Runner.CertificatesWaiting();
        Check(s.Outcome == PeerOutcome.NotSent && s.Words.Contains("pinned first", StringComparison.Ordinal) && s.Words.Contains(PlayoutWire.Short(a.Fp), StringComparison.Ordinal)
              && t.Accepts == 1 && t.Requests == 0 && pins.Get(key) is { Trust: PinTrust.First } p1 && p1.Fingerprint == a.Fp,
            "trigger pins: first use — the handshake's certificate pinned first, by its short form, and no request written");
        Check(waits is [{ Changed: false } w1] && w1.Key == key && w1.Fingerprint == a.Fp && w1.Where == "127.0.0.1:" + port && w1.Rule == "GO VT" && w1.NodeId is null
              && h.Runner.Health == LinkHealth.Starting && h.Runner.Status.Contains("pinned first only", StringComparison.Ordinal),
            "trigger pins: a first pin waits for a person — amber, offered to the desk with its rule and where");
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.NotSent && s.Words.Contains("pinned first only", StringComparison.Ordinal) && t.Requests == 0,
            "trigger pins: a first pin carries no secret, however often it fires (13.6.8)");

        Check(h.Runner.Trust(key, a.Fp[..8], typed: true) is (false, _) && h.Runner.Trust(key, b.Fp, typed: false) is (false, _)
              && pins.Get(key)?.Trust == PinTrust.First,
            "trigger pins: a first pin is trusted by its whole fingerprint alone — 8 hex or another certificate's trust nothing");
        Check(h.Runner.Trust(key, a.Fp, typed: false) is (true, var trustedWords) && trustedWords.Contains("trusted by a person", StringComparison.Ordinal)
              && pins.Get(key)?.Trust == PinTrust.Person && h.Runner.CertificatesWaiting().Count == 0,
            "trigger pins: the press beside its short form makes it a person's pin");
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.Applied && t.Requests == 1 && t.Last()?.Headers.GetValueOrDefault("X-PlayMap-Token") == Secret,
            "trigger pins: over HTTPS to the certificate a person pinned, the secret goes and the answer is read");

        t.Cert = b;
        s = await h.Fire(Rule(url));
        waits = h.Runner.CertificatesWaiting();
        Check(s.Outcome == PeerOutcome.NotSent && s.Words.StartsWith("CERTIFICATE CHANGED", StringComparison.Ordinal) && t.Requests == 1
              && waits is [{ Changed: true } w2] && w2.Fingerprint == b.Fp && w2.Pinned == a.Fp && h.Runner.Health == LinkHealth.Error,
            "trigger pins: a changed certificate is refused at the handshake — no request, red, offered for a typed trust");
        Check(h.Runner.Trust(key, b.Fp, typed: false) is (false, _) && h.Runner.Trust(key, "deadbeef", typed: true) is (false, _)
              && h.Runner.Trust(key, b.Fp[..7], typed: true) is (false, _) && pins.Get(key)?.Fingerprint == a.Fp,
            "trigger pins: a changed one is trusted only by typing at least 8 hex of it — a press, other hex or 7 hex trust nothing");
        Check(h.Runner.Trust(key, b.Fp[..4] + " " + b.Fp[4..8], typed: true) is (true, _) && pins.Get(key) is { Trust: PinTrust.Person } p2 && p2.Fingerprint == b.Fp,
            "trigger pins: 8 hex typed as the short form reads makes the new certificate the person's pin");
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.Applied && t.Requests == 2, "trigger pins: the newly trusted certificate gets the secret");

        using (var byNode = new Harness(o => o with { IsLoopback = _ => false, Pins = pins, NodeAt = (host, p) => host == "127.0.0.1" && p == t.Port ? "pm5e6f7a" : null }))
        {
            s = await byNode.Fire(Rule(url));
            Check(s.Outcome == PeerOutcome.Applied && t.Requests == 3 && byNode.Runner.CertificatesWaiting().Count == 0,
                "trigger pins: trusted by its address, then a hello names its node — the person's pin still holds, and the secret goes");
        }
        using (var forged = new Harness(o => o with { IsLoopback = _ => false, Pins = pins, NodeAt = (_, _) => "pmf0f0f0f0" }))
        {
            s = await forged.Fire(Rule(url));
            Check(s.Outcome == PeerOutcome.Applied && t.Requests == 4 && pins.Get("node:pmf0f0f0f0") is null,
                "trigger pins: a forged hello naming another node at its door moves it off nothing — the secret goes to the certificate a person trusted, and no pin is made for the forged id");
        }
        using (var thisPc = new Harness(o => o with { Pins = new TriggerPinStore(null) }))
        {
            s = await thisPc.Fire(Rule(url));
            Check(s.Outcome == PeerOutcome.Applied && t.Requests == 5, "trigger pins: HTTPS to this PC needs no pin — it is local (13.1)");
        }
        Check(h.All.All(x => !x.Row.Contains(Secret, StringComparison.Ordinal)) && !h.Runner.Status.Contains(Secret, StringComparison.Ordinal),
            "trigger pins: the secret is in no row and not in the status");
    }

    /// <summary>
    /// A pin's key kept stable (13.8): a first use with a hello pinned by address and node, trusted under both; then the hello stale, the
    /// node moved, or a forged hello naming the trusted node at another door — the person's pin is found, or the change refused.
    /// </summary>
    static async Task Keys(Action<bool, string> Check)
    {
        using var files = new KitFiles();
        var a = files.Cert("trigger-keys-a");
        var b = files.Cert("trigger-keys-b");
        await using var t = new Target(a) { Answer = _ => (200, Applied("ok"), "") };
        var port = t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var url = $"https://127.0.0.1:{port}/api/cmd";
        var addr = "addr:127.0.0.1:" + port;
        var pins = new TriggerPinStore(null);
        string? heard = "pm5e6f7a";
        using var h = new Harness(o => o with { IsLoopback = _ => false, Pins = pins, NodeAt = (_, _) => Volatile.Read(ref heard) });

        var s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.NotSent && t.Requests == 0 && pins.Get(addr) is { Trust: PinTrust.First } pa && pa.Fingerprint == a.Fp
              && pins.Get("node:pm5e6f7a") is { Trust: PinTrust.First } pn && pn.Fingerprint == a.Fp
              && h.Runner.CertificatesWaiting() is [{ Key: var key, NodeId: "pm5e6f7a", Changed: false }] && key == addr,
            "trigger pins: first use with a hello at its door — pinned first by its address and its node, offered once by its address");
        Check(h.Runner.Trust(addr, a.Fp, typed: false) is (true, _) && pins.Get(addr) is { Trust: PinTrust.Person } && pins.Get("node:pm5e6f7a") is { Trust: PinTrust.Person },
            "trigger pins: the person's trust is kept under both keys");
        Volatile.Write(ref heard, null);
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.Applied && t.Requests == 1,
            "trigger pins: trusted with its node named, then the hello goes stale — the address's pin holds, and the secret goes");

        // The node moves: a new address, no pin there, the hello naming the trusted node — its pin goes with it.
        var moved = new TriggerPinStore(null);
        _ = moved.Put("node:pm5e6f7a", new TriggerPin(a.Fp, PinTrust.Person, 1));
        using (var m = new Harness(o => o with { IsLoopback = _ => false, Pins = moved, NodeAt = (_, _) => "pm5e6f7a" }))
        {
            s = await m.Fire(Rule(url));
            Check(s.Outcome == PeerOutcome.Applied && t.Requests == 2 && m.Runner.CertificatesWaiting().Count == 0,
                "trigger pins: a node trusted at one address and heard at another keeps its pin there — the secret goes");
        }
        // A forged hello naming the trusted node at a door showing another certificate: CERTIFICATE CHANGED against the node's pin.
        t.Cert = b;
        using (var f = new Harness(o => o with { IsLoopback = _ => false, Pins = moved, NodeAt = (_, _) => "pm5e6f7a" }))
        {
            s = await f.Fire(Rule(url));
            Check(s.Outcome == PeerOutcome.NotSent && s.Words.StartsWith("CERTIFICATE CHANGED", StringComparison.Ordinal) && t.Requests == 2
                  && f.Runner.CertificatesWaiting() is [{ Changed: true, Pinned: var was }] && was == a.Fp && f.Runner.Health == LinkHealth.Error,
                "trigger pins: a hello naming a trusted node at a door that shows another certificate is CERTIFICATE CHANGED — nothing sent");
        }
        // Back on the first desk: the address's person pin refuses the new certificate whichever hello is heard.
        Volatile.Write(ref heard, "pmabcdef01");
        s = await h.Fire(Rule(url));
        Check(s.Outcome == PeerOutcome.NotSent && s.Words.StartsWith("CERTIFICATE CHANGED", StringComparison.Ordinal) && t.Requests == 2,
            "trigger pins: a person's pin under the address is not traded for a first pin by a hello naming a new node");
    }

    /// <summary>The runner's tables are bounded: the targets met (64, the oldest out) — node ids come from hellos anyone can send.</summary>
    static async Task Bounded(Action<bool, string> Check)
    {
        // 65 ports no one listens on, each its own: every listener is held open until all are drawn, so none is drawn twice.
        var listeners = Enumerable.Range(0, TriggerRunner.MetMax + 1).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToList();
        listeners.ForEach(l => l.Start());
        var ports = listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToList();
        listeners.ForEach(l => l.Stop());
        var pins = new TriggerPinStore(null);
        var fp = new string('d', 64);
        foreach (var p in ports) _ = pins.Put("addr:127.0.0.1:" + p.ToString(System.Globalization.CultureInfo.InvariantCulture), new TriggerPin(fp, PinTrust.First, 1));
        using var h = new Harness(o => o with { IsLoopback = _ => false, Pins = pins });
        foreach (var p in ports) await h.Fire(Rule($"https://127.0.0.1:{p.ToString(System.Globalization.CultureInfo.InvariantCulture)}/api/cmd"));
        var waits = h.Runner.CertificatesWaiting();
        Check(waits.Count == TriggerRunner.MetMax && waits.All(w => w.Key != "addr:127.0.0.1:" + ports[0].ToString(System.Globalization.CultureInfo.InvariantCulture))
              && h.All.All(x => x.Outcome == PeerOutcome.NotSent),
            "trigger pins: the targets met this run are at most 64, the one met longest ago making room");
    }

    /// <summary>The fire's deadlines: its connection, then — once its request may have gone — an acceptor's deadline and a margin for its answer.</summary>
    static async Task Deadlines(Action<bool, string> Check)
    {
        Check(new TriggerRunnerOptions().AnswerWait == TimeSpan.FromMilliseconds(PeerLink.ReceiptDeadlineMs + 2000) && new TriggerRunnerOptions().Timeout == TimeSpan.FromSeconds(3),
            "trigger deadlines: 3 s to reach the target, then receipt.deadline.ms and 2 s for the answer");
        await using var t = new Target { Answer = _ => (200, "{\"ok\":true,\"stage\":\"accepted\",\"effect\":\"queued\"}", "") };
        var url = $"http://127.0.0.1:{t.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}/api/next";
        // Scaled: 300 ms to connect, 1.5 s to answer; the target answers after 700 ms — past the connect deadline, within the answer's.
        using var h = new Harness(o => o with { Timeout = TimeSpan.FromMilliseconds(300), AnswerWait = TimeSpan.FromMilliseconds(1500) });
        t.DelayMs = 700;
        var s = await h.Fire(Rule(url, TriggerAuthRules.Stagewatch, "next"));
        Check(s.Outcome == PeerOutcome.Accepted && s.Words.Contains("queued", StringComparison.Ordinal),
            "trigger deadlines: an accepted said at the acceptor's own deadline is read, never taken for silence");
        t.DelayMs = 3000;
        s = await h.Fire(Rule(url, TriggerAuthRules.Stagewatch, "next"));
        t.DelayMs = 0;
        Check(s.Outcome == PeerOutcome.Unconfirmed && s.Words.Contains("no answer from", StringComparison.Ordinal) && s.Words.Contains("in 1.5 s — it may have run", StringComparison.Ordinal),
            "trigger deadlines: no answer within the answer's wait once the request went is unconfirmed");
    }
}
