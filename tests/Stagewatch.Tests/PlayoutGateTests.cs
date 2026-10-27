using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>
/// Who may drive another node's air: <see cref="PlayoutGate"/>'s table, Settings → Remote → May drive playout as files keep it, and
/// the gate at every door a line comes in by — OSC, Companion TCP, HTTP, WebSocket and MCP — on real servers, a sender on another
/// machine played from loopback by each server's local-address seam, and a page of another site or a rebound name on this PC by the
/// headers a browser sends. A refused line never reaches the show thread, so no link carries it to a peer; fields and counts first,
/// words beside them.
/// </summary>
static class PlayoutGateTests
{
    static readonly IPAddress Lan = IPAddress.Parse("10.0.0.40");
    static readonly IPAddress OtherLan = IPAddress.Parse("10.0.0.41");
    static readonly CommandDoor[] Doors = [CommandDoor.Osc, CommandDoor.Companion, CommandDoor.Http, CommandDoor.WebSocket, CommandDoor.Mcp];

    public static async Task Run(Action<bool, string> Check)
    {
        Table(Check);
        Proposals(Check);
        Addresses(Check);
        Settings(Check);
        await Osc(Check);
        await Companion(Check);
        await Web(Check);
        await WebThisPc(Check);
    }

    /// <summary>
    /// Show Bus §11: a line only a person on this desk runs (trusting a certificate) is never a proposal aimed at it — refused (400) as it
    /// is proposed, nothing queued and no key spoken for, so the approval desk never shows one it could not run; any other line, and
    /// the same words aimed at another node (its own grammar), is proposed as before.
    /// </summary>
    static void Proposals(Action<bool, string> Check)
    {
        const string Desk = "abc123abc123";
        var nodes = new NodeBindings();
        var desk = new ProposalDesk(() => Desk, nodes);
        string? Never(NodeRef target, string line) => target.Id == Desk ? PlayoutGate.NeverProposed(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)) : null;
        string Body(string target, string line) => "{\"from\":{\"t\":\"playmap\",\"id\":\"pm5e6f7a\"},\"target\":" + target + ",\"line\":\"" + line + "\"}";
        var trust = desk.Submit(Body("""{"t":"main"}""", "playout stage-left trust 03e9 b152"), NodeCredential.Key("kP"), (_, _) => "high", DateTime.UnixEpoch, Never);
        Check(trust is (null, { } why, false) && why == $"{PlayoutRules.TrustDeskOnly} ({PlayoutRules.TrustWhere}), never a proposal"
              && desk.All().Count == 0 && nodes.NodeOf("kP") is null,
              $"playout gate: playout trust proposed to this desk is refused as it comes — nothing queued, no key spoken for ({trust.Error})");
        var go = desk.Submit(Body("""{"t":"main"}""", "playout stage-left go show intro"), NodeCredential.Key("kP"), (_, _) => "high", DateTime.UnixEpoch, Never);
        var elsewhere = desk.Submit(Body("""{"t":"patterns","id":"pat1"}""", "playout stage-left trust 03e9b152"), NodeCredential.Key("kP"), (_, _) => "high", DateTime.UnixEpoch, Never);
        Check(go.Proposal is { State: SharedProposalState.Pending } && elsewhere.Proposal is { State: SharedProposalState.Pending } && desk.All().Count == 2
              && PlayoutGate.NeverProposed(["playout", "stage-left", "go", "show"]) is null && PlayoutGate.NeverProposed([]) is null
              && PlayoutGate.NeverProposed(["nonsense"]) is null,
              "playout gate: any other line to this desk, and a line aimed at another node in its own grammar, is proposed as before");
    }

    static void Table(Action<bool, string> Check)
    {
        IReadOnlyList<IPAddress> none = [], listed = [Lan];
        string? Gate(ShowCommandKind kind, CommandDoor door, IPAddress from, bool thisPc, bool credentialed, IReadOnlyList<IPAddress> list)
            => PlayoutGate.Check(kind, door, new CommandSender(from, thisPc, credentialed), list);

        Check(PlayoutGate.Drives(ShowCommandKind.Playout) && PlayoutGate.Drives(ShowCommandKind.Patterns)
              && Enum.GetValues<ShowCommandKind>().Count(PlayoutGate.Drives) == 2, "playout gate: decides playout and patterns lines, and no other");
        foreach (var door in Doors)
        {
            var name = door.ToString();
            Check(Gate(ShowCommandKind.Playout, door, Lan, false, false, none) is { } p && p.StartsWith("playout from another machine", StringComparison.Ordinal)
                  && p.Contains(PlayoutGate.SettingWords, StringComparison.Ordinal),
                  $"playout gate: {name} — an unlisted machine with nothing proved is refused, naming where to list it");
            Check(Gate(ShowCommandKind.Patterns, door, Lan, false, false, none) is { } q && q.StartsWith("patterns from another machine", StringComparison.Ordinal),
                  $"playout gate: {name} — a patterns line is refused in its own verb's words");
            Check(Gate(ShowCommandKind.Playout, door, IPAddress.Loopback, true, false, none) is null, $"playout gate: {name} — this PC passes");
            Check(Gate(ShowCommandKind.Playout, door, Lan, false, false, listed) is null, $"playout gate: {name} — a listed address passes");
            Check(Gate(ShowCommandKind.Playout, door, OtherLan, false, false, listed) is not null, $"playout gate: {name} — the list names addresses, not a network");
            Check(Gate(ShowCommandKind.Start, door, Lan, false, false, none) is null && Gate(ShowCommandKind.Message, door, Lan, false, false, none) is null,
                  $"playout gate: {name} — every other verb is not the gate's");
            var carries = door is CommandDoor.Http or CommandDoor.WebSocket or CommandDoor.Mcp;
            Check((Gate(ShowCommandKind.Playout, door, Lan, false, true, none) is null) == carries,
                  carries ? $"playout gate: {name} — a credential (the PIN asked for, or a key) passes"
                          : $"playout gate: {name} carries no credential, so a server's word that one was proved opens nothing");
            // A page of another site proves nothing wherever its browser runs: this PC and a listed address vouch for no page.
            string? Page(bool thisPc, bool credentialed, IReadOnlyList<IPAddress> list)
                => PlayoutGate.Check(ShowCommandKind.Playout, door, new CommandSender(Lan, thisPc, credentialed, OtherSite: true), list);
            if (carries)
            {
                Check(Page(true, false, none) == PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, door) && Page(false, false, listed) == Page(true, false, none),
                      $"playout gate: {name} — another site's page is refused from this PC and from a listed address alike, in its own words");
                Check(Page(true, true, none) is null && Page(false, true, none) is null, $"playout gate: {name} — another site's page with a credential passes");
            }
            else Check(Page(true, false, none) is null, $"playout gate: {name} has no pages, so a page's mark is not its business");
        }
        // Trusting a certificate is a person's choice on the desk (Show Bus 13.8.3): no door takes the line from anyone — this PC, a listed
        // address, a credential, a page of this site — and it says so in its door's words.
        Check(PlayoutGate.DeskOnly(ShowCommandKind.PlayoutTrust) && Enum.GetValues<ShowCommandKind>().Count(PlayoutGate.DeskOnly) == 1
              && !PlayoutGate.Drives(ShowCommandKind.PlayoutTrust), "playout gate: trusting a certificate is the one desk-only line, and no line of the air's");
        foreach (var door in Doors)
        {
            var words = PlayoutGate.DeskOnlyWords(door);
            var every = new[] { new CommandSender(IPAddress.Loopback, true, true), new CommandSender(IPAddress.Loopback, true, false), new CommandSender(Lan, false, true) }
                .All(sender => PlayoutGate.Check(ShowCommandKind.PlayoutTrust, door, sender, listed) == words);
            Check(every && words.StartsWith(PlayoutRules.TrustDeskOnly, StringComparison.Ordinal) && words.Contains(PlayoutRules.TrustWhere, StringComparison.Ordinal)
                  && words.EndsWith("whoever sends it", StringComparison.Ordinal),
                  $"playout gate: {door} — playout trust is refused from this PC, a listed address and with a credential, naming where a person does it ({words})");
        }
        Check(PlayoutGate.OtherSiteWords(ShowCommandKind.Patterns, CommandDoor.Http)
              == "patterns from another site's web page over HTTP needs a control key while no PIN is set"
              && PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, CommandDoor.WebSocket).Contains("over a WebSocket needs a control key", StringComparison.Ordinal)
              && PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, CommandDoor.Mcp).Contains("over MCP needs an MCP key", StringComparison.Ordinal),
              "playout gate: another site's page hears its door and its credential");

        // Another site's page, by the headers a browser sets itself and a page's script cannot.
        foreach (var (origin, host, site, other, why) in new (string?, string?, string?, bool, string)[]
                 {
                     (null, "127.0.0.1:9924", null, false, "a device or script sends neither header"),
                     (null, "127.0.0.1:9924", "none", false, "typed in the address bar"),
                     ("http://evil.example", "127.0.0.1:9924", "same-origin", false, "Sec-Fetch-Site's word stands over Origin's"),
                     (null, "127.0.0.1:9924", "cross-site", true, "an img or a form of another site"),
                     (null, "127.0.0.1:9924", "Same-Site", true, "another port or app on this host"),
                     ("http://evil.example", "127.0.0.1:9924", null, true, "an Origin that is not this Host"),
                     ("http://127.0.0.1:8080", "127.0.0.1:9924", null, true, "another port's page"),
                     ("null", "127.0.0.1:9924", null, true, "a sandboxed or file page"),
                     ("http://127.0.0.1:9924", "127.0.0.1:9924", null, false, "Stagewatch's own page"),
                     ("HTTP://LOCALHOST:9924", "localhost:9924", null, false, "its own page by name, in any case"),
                     ("http://127.0.0.1:9924", null, null, true, "an Origin with no Host to match"),
                 })
            Check(PlayoutGate.OtherSite(origin, host, site) == other, $"playout gate: another site's page? {other} — {why}");

        // A Host that names this PC's server over loopback; a rebound name or a forwarder's port is another machine's.
        foreach (var (host, port, names, why) in new (string?, int, bool, string)[]
                 {
                     (null, 9924, true, "no Host: a device or a script"),
                     ("127.0.0.1:9924", 9924, true, "the loopback address and the port"),
                     ("localhost:9924", 9924, true, "localhost"),
                     ("LocalHost:9924", 9924, true, "localhost in any case"),
                     ("[::1]:9924", 9924, true, "IPv6 loopback, bracketed"),
                     ("127.0.0.2:9924", 9924, true, "any loopback address"),
                     ("127.0.0.1", 80, true, "no port on port 80"),
                     ("[::1]", 443, true, "no port on port 443"),
                     ("127.0.0.1", 9924, false, "no port on another port"),
                     ("127.0.0.1:8080", 9924, false, "a forwarder's port"),
                     ("evil.example:9924", 9924, false, "a name rebound to 127.0.0.1"),
                     ("stage-pc:9924", 9924, false, "a machine name"),
                     ("10.0.0.40:9924", 9924, false, "a LAN address"),
                     ("localhost.:9924", 9924, false, "a dotted root name"),
                     ("127.1:9924", 9924, false, "an address no browser writes"),
                     ("0177.0.0.1:9924", 9924, false, "an octal address"),
                     ("::1:9924", 9924, false, "IPv6 unbracketed"),
                     ("[fe80::1]:9924", 9924, false, "IPv6 not loopback"),
                     ("127.0.0.1:09924", 9924, false, "a port with a leading zero"),
                     ("127.0.0.1:", 9924, false, "an empty port"),
                     ("127.0.0.1:+9924", 9924, false, "a port with a sign"),
                     ("[::1", 9924, false, "an open bracket"),
                 })
            Check(PlayoutGate.NamesThisPc(host, port) == names, $"playout gate: Host \"{host}\" names this PC at {port}? {names} — {why}");
        Check(PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Osc)
              == "playout from another machine over OSC needs its address in Settings → Remote → May drive playout (OSC carries no PIN)",
              "playout gate: OSC's words");
        Check(PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Companion)
              == "patterns from another machine over Companion TCP needs its address in Settings → Remote → May drive playout (Companion TCP carries no PIN)",
              "playout gate: Companion TCP's words");
        Check(PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Http)
              == "playout from another machine over HTTP needs a control key while no PIN is set, or its address in Settings → Remote → May drive playout",
              "playout gate: HTTP's words");
        Check(PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Mcp).Contains("an MCP key while no PIN is set", StringComparison.Ordinal)
              && PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.WebSocket).Contains("over a WebSocket needs a control key", StringComparison.Ordinal),
              "playout gate: MCP's and the WebSocket's words name their own credential");

        // Addresses compare by their bytes: an IPv4-mapped sender is its IPv4 address, and an IPv6 scope is not part of it.
        Check(PlayoutGate.IsLoopback(IPAddress.Loopback) && PlayoutGate.IsLoopback(IPAddress.IPv6Loopback)
              && PlayoutGate.IsLoopback(IPAddress.Parse("::ffff:127.0.0.1")) && !PlayoutGate.IsLoopback(Lan) && !PlayoutGate.IsLoopback(IPAddress.Parse("::ffff:10.0.0.40")),
              "playout gate: this PC is loopback, IPv4-mapped included");
        Check(PlayoutGate.Listed(IPAddress.Parse("::ffff:10.0.0.40"), listed) && PlayoutGate.Listed(Lan, [IPAddress.Parse("::ffff:10.0.0.40")]),
              "playout gate: an IPv4-mapped sender or entry is its IPv4 address");
        Check(PlayoutGate.Listed(IPAddress.Parse("fe80::1%3"), [IPAddress.Parse("fe80::1")]) && !PlayoutGate.Listed(IPAddress.Parse("fe80::2"), [IPAddress.Parse("fe80::1")]),
              "playout gate: a link-local sender matches its listed address whatever its scope");
    }

    static void Addresses(Action<bool, string> Check)
    {
        foreach (var good in new[] { "10.0.0.40", " 192.168.1.20 ", "fe80::1", "2001:db8::7", "::ffff:10.0.0.5" })
            Check(PlayoutGate.Parse(good) is not null, $"may drive playout: \"{good}\" is an address");
        Check(PlayoutGate.Parse("::ffff:10.0.0.5") is { } mapped && mapped.ToString() == "10.0.0.5", "may drive playout: an IPv4-mapped entry is kept as its IPv4 address");
        foreach (var bad in new[] { "", " ", "10.1", "1", "010.0.0.1", "0x0a.0.0.1", "10.0.0.40:9925", "[::1]", "[::1]:80", "10.0.0.0/24", "stage-pc.local",
                                    "0.0.0.0", "::", "255.255.255.255", "10.0.0.40 10.0.0.41", "10.0.0.256", "127.0.0.1", "127.0.0.2", "::1", "::ffff:127.0.0.1" })
            Check(PlayoutGate.Parse(bad) is null, $"may drive playout: \"{bad}\" is not one address, so it is never listed");
        Check(PlayoutGate.Parse(null) is null, "may drive playout: nothing is no address");
        Check(PlayoutGate.AddressWords.Contains("loopback", StringComparison.Ordinal), "may drive playout: the words say loopback is never listed (this PC has its own rule)");

        var notes = new LoadNotes();
        var many = Enumerable.Range(1, 18).Select(i => "10.0.1." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var kept = PlayoutGate.Sanitise([.. many.Take(3), "10.0.1.2", "stage-pc", null, .. many.Skip(3)], notes);
        Check(kept.Count == PlayoutGate.MaxFrom && PlayoutGate.MaxFrom == 16 && kept.SequenceEqual(many.Take(16)),
              "may drive playout: the first 16 addresses are kept in order, a repeat once");
        Check(notes.Words.Count == 3 && notes.Words[0] == "May drive playout: \"stage-pc\" left out: " + PlayoutGate.AddressWords
              && notes.Words[1] == "May drive playout: \"\" left out: " + PlayoutGate.AddressWords
              && notes.Words[2] == "May drive playout keeps 16 addresses: 2 more left out",
              $"may drive playout: what a load leaves out is said, a repeat silently kept once ({string.Join(" | ", notes.Words)})");
        Check(PlayoutGate.Addresses(["10.0.0.40", "bad", "10.0.0.40"]) is [var one] && one.Equals(Lan), "may drive playout: the gate's copy holds the readable addresses once");
    }

    static void Settings(Action<bool, string> Check)
    {
        using (var file = Scratch.File("playout-from", ".json"))
        {
            File.WriteAllText(file.Path, """{"remote":{"pin":"1234","playoutFrom":["10.0.0.40","stage-pc.local","10.0.0.40","::ffff:10.0.0.41"]}}""");
            using (var store = new SettingsStore(file.Path))
            {
                Check(store.Settings.Remote.PlayoutFrom.SequenceEqual(new[] { "10.0.0.40", "10.0.0.41" }), "may drive playout: a settings file keeps its addresses, readable and once");
                Check(store.Notes.Contains("May drive playout: \"stage-pc.local\" left out: " + PlayoutGate.AddressWords), "may drive playout: a name left out is said at load");
                store.SaveNow();
            }
            var remote = JsonNode.Parse(File.ReadAllText(file.Path))!["remote"]!.AsObject();
            Check(remote.Last().Key == "playoutFrom" && remote["playoutFrom"]!.AsArray().Select(a => (string?)a).SequenceEqual(new[] { "10.0.0.40", "10.0.0.41" }),
                  "may drive playout: saved last in remote, as kept");
        }
        using (var fresh = Scratch.File("playout-from-fresh", ".json"))
        using (var store = new SettingsStore(fresh.Path))
            Check(store.Settings.Remote.PlayoutFrom.Count == 0 && store.Notes.Count == 0, "may drive playout: empty unless the operator lists an address");
        using (var nulled = Scratch.File("playout-from-null", ".json"))
        {
            File.WriteAllText(nulled.Path, """{"remote":{"pin":"1234","playoutFrom":null}}""");
            using var store = new SettingsStore(nulled.Path);
            Check(store.Settings.Remote.PlayoutFrom is { Count: 0 }, "may drive playout: a list nulled by hand is empty, never null");
        }
    }

    static async Task Osc(Action<bool, string> Check)
    {
        var control = new FakeControl();
        var access = new Access();
        var port = FreeUdpPort();
        var rejected = new List<string>();
        using var osc = new OscServer(control, access) { IsThisPc = _ => false };   // every sender is "another machine"
        osc.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        osc.Start(port, bindAll: false, null, 0);
        using var sender = new UdpClient();
        async Task Send(string address, params object[] args)
        {
            var b = OscCodec.EncodeMessage(address, args);
            await sender.SendAsync(b, b.Length, new IPEndPoint(IPAddress.Loopback, port));
        }
        async Task<bool> Ran(ShowCommandKind kind, int at)
        {
            for (var i = 0; i < 100; i++)
            {
                lock (control.Commands) if (control.Commands.Skip(at).Any(c => c.Kind == kind)) return true;
                await Task.Delay(20);
            }
            return false;
        }
        int Count() { lock (control.Commands) return control.Commands.Count; }
        bool Any(ShowCommandKind kind) { lock (control.Commands) return control.Commands.Any(c => c.Kind == kind); }

        await Send("/stagewatch/playout", "stage-left", "go", "show", "intro");
        await Send("/stagewatch/patterns", "pause");
        await Send("/stagewatch/playout/pause");
        await Send("/stagewatch/start");   // the same sender's other verbs run as they always have; the server reads in order
        Check(await Ran(ShowCommandKind.Start, 0), "osc gate: start from another machine still runs");
        Check(!Any(ShowCommandKind.Playout) && !Any(ShowCommandKind.Patterns) && osc.MessagesRejected == 3,
              $"osc gate: playout and patterns from an unlisted machine never reach the show thread ({osc.MessagesRejected} rejected)");
        lock (rejected)
            Check(rejected.Count == 3 && rejected[0] == "OSC: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Osc)
                  && rejected[1] == "OSC: " + PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Osc),
                  "osc gate: the refusal's words reach the status line");

        access.From = [IPAddress.Loopback];   // the sender's address, listed
        var at = Count();
        await Send("/stagewatch/playout", "stage-left", "go", "show", "intro");
        Check(await Ran(ShowCommandKind.Playout, at), "osc gate: a listed address drives playout");

        // The default seam: a sender on this PC passes with nothing listed.
        var local = new FakeControl();
        var localPort = FreeUdpPort();
        using var plain = new OscServer(local);
        plain.Start(localPort, bindAll: false, null, 0);
        var bytes = OscCodec.EncodeMessage("/stagewatch/patterns", "pause");
        await sender.SendAsync(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, localPort));
        var ok = false;
        for (var i = 0; i < 100 && !ok; i++) { lock (local.Commands) ok = local.Commands.Any(c => c.Kind == ShowCommandKind.Patterns); if (!ok) await Task.Delay(20); }
        Check(ok, "osc gate: this PC (loopback) drives patterns, as before");
        Check(typeof(OscServer).GetConstructors().Single().GetParameters() is [{ } first, { } second]
              && first.ParameterType == typeof(IShowCommands) && second.ParameterType == typeof(IShowAccess) && second.HasDefaultValue,
              "osc gate: OSC needs commands and frames, and who may drive playout (none given: this PC only)");
    }

    static async Task Companion(Action<bool, string> Check)
    {
        var bridge = new FakeBridge { Allowed = true };
        var port = FreeTcpPort();
        var rejected = new List<string>();
        await using var server = new Stagewatch.Companion.CompanionServer(bridge) { IsThisPc = _ => false };
        server.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        server.Start(port, bindAllInterfaces: false);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        async Task Say(string line) => await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"));
        async Task<string?> Answer(string prefix)
        {
            for (var i = 0; i < 20; i++)
            {
                using var cts = new CancellationTokenSource(2000);
                string? line;
                try { line = await reader.ReadLineAsync(cts.Token); }
                catch (OperationCanceledException) { return null; }
                if (line is null) return null;
                if (line.StartsWith(prefix, StringComparison.Ordinal)) return line;
            }
            return null;
        }
        bool Any(ShowCommandKind kind) { lock (bridge.Commands) return bridge.Commands.Any(c => c.Kind == kind); }

        await Say("PATTERNS PAUSE");
        Check(await Answer("ERROR ") == "ERROR " + PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Companion),
              "companion gate: patterns from an unlisted machine is refused in words");
        await Say("@c1 playout stage-left go show intro");
        Check(await Answer("ERROR @c1 ") == "ERROR @c1 " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Companion),
              "companion gate: a playout line with a request id is refused under its id");
        await Say("@c2 next");
        Check(await Answer("OK @c2") == "OK @c2 accepted" && Any(ShowCommandKind.Next), "companion gate: other verbs from that machine run as before");
        Check(!Any(ShowCommandKind.Patterns) && !Any(ShowCommandKind.Playout), "companion gate: nothing refused reached the show thread");
        lock (rejected)
            Check(rejected.Count == 2 && rejected[1] == "Companion: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Companion),
                  "companion gate: each refusal reaches the status line");
        await Say("FROBNICATE");
        Check(await Answer("ERROR ") is { } unknown && !unknown.Contains("playout", StringComparison.Ordinal) && server.MessagesRejected == 3,
              $"companion gate: diagnostics count every line refused at the door, the grammar's and the gate's ({server.MessagesRejected})");

        bridge.From = [IPAddress.Loopback];
        await Say("@c3 patterns pause");
        Check(await Answer("OK @c3") == "OK @c3 accepted" && Any(ShowCommandKind.Patterns), "companion gate: a listed address drives patterns");
    }

    static async Task Web(Action<bool, string> Check)
    {
        var control = new GateControl();
        string? pin = null;
        var port = FreeTcpPort();
        var rejected = new List<string>();
        await using var web = new WebServer(control, () => pin) { IsThisPc = _ => false };
        web.CommandRejected += m => { lock (rejected) rejected.Add(m); };
        web.Start(port, bindAll: false);
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        var (mcpKey, mcpSecret) = ApiKeys.Create("Agent", ApiKeyScope.Mcp, DateTime.UtcNow);
        control.Keys = [ctlKey, mcpKey];
        HttpRequestMessage Post(string path, string? bearer = null, string? requestId = null, string? body = null)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, path);
            if (bearer is not null) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            if (requestId is not null) m.Headers.Add("X-Request-Id", requestId);
            if (body is not null) m.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return m;
        }
        int Count(ShowCommandKind kind) { lock (control.Commands) return control.Commands.Count(c => c.Kind == kind); }

        // HTTP with no PIN set: the open door lets anyone run the show, but another node's air needs a key or a listed address.
        var refused = await http.SendAsync(Post("/api/patterns/pause"));
        var refusedBody = JsonNode.Parse(await refused.Content.ReadAsStringAsync())!;
        Check(refused.StatusCode == HttpStatusCode.Forbidden && (bool?)refusedBody["ok"] == false
              && (string?)refusedBody["error"] == PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Http) && Count(ShowCommandKind.Patterns) == 0,
              "http gate: no PIN set, an unlisted machine's patterns is refused 403 in words, and nothing runs");
        var receipt = await http.SendAsync(Post("/api/playout/stage-left/go/show/intro", requestId: "g1"));
        var receiptBody = JsonNode.Parse(await receipt.Content.ReadAsStringAsync())!;
        Check(receipt.StatusCode == HttpStatusCode.Forbidden && (string?)receiptBody["type"] == "receipt" && (string?)receiptBody["id"] == "g1"
              && (bool?)receiptBody["ok"] == false && (string?)receiptBody["error"] == PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Http)
              && Count(ShowCommandKind.Playout) == 0,
              "http gate: with a request id the refusal is a refused receipt under that id");
        Check((await http.SendAsync(Post("/api/start"))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Start) == 1,
              "http gate: every other verb runs through the open door as before");
        Check((await http.SendAsync(Post("/api/patterns/pause", ctlSecret))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Patterns) == 1,
              "http gate: a control key drives patterns from another machine");
        lock (rejected)
            Check(rejected.Count == 2 && rejected[0] == "Remote: " + PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Http), "http gate: each refusal reaches the status line");

        // MCP with no PIN set: the sender must be one that may send such a line, then a person decides, always.
        async Task<JsonNode> Mcp(string line, string? bearer = null)
        {
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 7, method = "tools/call", @params = new { name = "run_command", arguments = new { command = line, reason = "clip overran" } } });
            var r = await http.SendAsync(Post("/mcp", bearer, body: body));
            return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!;
        }
        static string Text(JsonNode result) => (string?)result["content"]![0]!["text"] ?? "";
        var gated = await Mcp("playout stage-left go show intro");
        Check((bool?)gated["isError"] == true && Text(gated) == "Refused: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Mcp)
              && control.ProposedCount == 0 && Count(ShowCommandKind.Playout) == 0,
              "mcp gate: no PIN set and no key, an unlisted machine's playout line is refused, never even proposed");
        lock (rejected)
            Check(rejected.Count == 3 && rejected[2] == "MCP: " + PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.Mcp),
                  "mcp gate: the refusal reaches the status line like the other doors'");
        var proposed = await Mcp("playout stage-left go show intro", mcpSecret);
        Check((bool?)proposed["isError"] == false && Text(proposed).Contains("approval", StringComparison.Ordinal) && control.ProposedCount == 1
              && control.LastMust && Count(ShowCommandKind.Playout) == 0,
              "mcp gate: with an MCP key, a playout line is a proposal even with agents acting directly, and nothing runs");
        var direct = await Mcp("nudge 30");
        Check((bool?)direct["isError"] == false && Count(ShowCommandKind.Nudge) == 1 && control.ProposedCount == 1,
              "mcp gate: any other line from that machine runs directly as before when agents act directly");
        control.IgnoreMust = true;   // a host that would let it run: the endpoint still never runs it
        var unqueued = await Mcp("patterns pause", mcpSecret);
        Check((bool?)unqueued["isError"] == true && Text(unqueued) == IShowAgents.NobodyApproves && Count(ShowCommandKind.Patterns) == 1,
              "mcp gate: a line that drives another app's air never runs directly, even when the host queues nothing");
        control.IgnoreMust = false;

        // WebSocket: a control page let in with no PIN set proved nothing.
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"patterns","args":["pause"]}""");
            var error = await Next(ws, "\"type\":\"error\"");
            Check(error is not null && (string?)JsonNode.Parse(error)!["error"] == PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.WebSocket),
                  "ws gate: no PIN set, an unlisted machine's control page is refused patterns in words");
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"w1"}""");
            var wsReceipt = await Next(ws, "\"type\":\"receipt\"");
            Check(wsReceipt is not null && JsonNode.Parse(wsReceipt) is { } rr && (string?)rr["id"] == "w1" && (bool?)rr["ok"] == false
                  && (string?)rr["error"] == PlayoutGate.Words(ShowCommandKind.Playout, CommandDoor.WebSocket),
                  "ws gate: with an id the refusal is a refused receipt");
            await Send(ws, """{"cmd":"next","args":[]}""");
            var ran = false;
            for (var i = 0; i < 100 && !ran; i++) { ran = Count(ShowCommandKind.Next) == 1; if (!ran) await Task.Delay(20); }
            Check(ran && Count(ShowCommandKind.Playout) == 0 && Count(ShowCommandKind.Patterns) == 1, "ws gate: other verbs run; nothing refused ran");
        }
        // A control page let in by a control key with no PIN set proved that key.
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer " + ctlSecret);
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"w0"}""");
            var keyed = await Next(ws, "\"type\":\"receipt\"");
            Check(keyed is not null && JsonNode.Parse(keyed) is { } kr && (string?)kr["id"] == "w0" && (bool?)kr["ok"] == true && Count(ShowCommandKind.Playout) == 1,
                  "ws gate: no PIN set, a control page let in by a control key drives playout from another machine");
        }

        // A listed address passes every door with nothing proved.
        control.From = [IPAddress.Loopback];
        Check((await http.SendAsync(Post("/api/playout/stage-left/pause"))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Playout) == 2,
              "http gate: a listed address drives playout with no key");
        control.From = [];

        // With a PIN set, the PIN given right is the credential; a missing one is refused at the door as before.
        pin = "4321";
        Check((await http.SendAsync(Post("/api/patterns/pause"))).StatusCode == HttpStatusCode.Unauthorized, "http gate: with a PIN set, no PIN is refused at the door");
        Check((await http.SendAsync(Post("/api/patterns/pause?pin=4321"))).StatusCode == HttpStatusCode.OK && Count(ShowCommandKind.Patterns) == 2,
              "http gate: the PIN asked for and given right drives patterns from another machine");
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer 4321");
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"w2"}""");
            var wsOk = await Next(ws, "\"type\":\"receipt\"");
            Check(wsOk is not null && JsonNode.Parse(wsOk) is { } ok && (string?)ok["id"] == "w2" && (bool?)ok["ok"] == true && Count(ShowCommandKind.Playout) == 3,
                  "ws gate: a control page let in by the PIN drives playout");
        }
    }

    /// <summary>
    /// This PC by the default seam (loopback): a program here drives another node's air with nothing proved, but a page of another site
    /// in a browser here, a name rebound to 127.0.0.1 or a forwarder's port does not; and a surface with nobody to approve an agent's
    /// line refuses it as an error, never as queued.
    /// </summary>
    static async Task WebThisPc(Action<bool, string> Check)
    {
        var control = new PlainControl();
        var port = FreeTcpPort();
        await using var web = new WebServer(control, () => null);
        web.Start(port, bindAll: false);
        control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300 });
        control.Snapshots.MarkAlive();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var (ctlKey, ctlSecret) = ApiKeys.Create("Deck", ApiKeyScope.Control, DateTime.UtcNow);
        control.Keys = [ctlKey];
        int Count(ShowCommandKind kind) { lock (control.Commands) return control.Commands.Count(c => c.Kind == kind); }
        async Task<(HttpStatusCode Status, string? Error)> Get(string path, string? host = null, string? origin = null, string? site = null, string? bearer = null)
        {
            using var m = new HttpRequestMessage(HttpMethod.Get, path);
            if (host is not null) m.Headers.Host = host;
            if (origin is not null) m.Headers.TryAddWithoutValidation("Origin", origin);
            if (site is not null) m.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
            if (bearer is not null) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            using var r = await http.SendAsync(m);
            var body = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, r.IsSuccessStatusCode ? null : (string?)JsonNode.Parse(body)?["error"]);
        }
        var page = PlayoutGate.OtherSiteWords(ShowCommandKind.Patterns, CommandDoor.Http);
        var machine = PlayoutGate.Words(ShowCommandKind.Patterns, CommandDoor.Http);

        Check(await Get("/api/patterns/pause") is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Patterns) == 1,
              "this pc: a program here drives patterns with no PIN set and nothing proved");
        Check(await Get("/api/patterns/pause", host: $"localhost:{port}", origin: $"http://localhost:{port}", site: "same-origin") is (HttpStatusCode.OK, null)
              && Count(ShowCommandKind.Patterns) == 2,
              "this pc: Stagewatch's own page by localhost drives patterns");
        Check(await Get("/api/patterns/pause", site: "cross-site") == (HttpStatusCode.Forbidden, page) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: an img of another site in a browser here is refused, and nothing runs");
        Check(await Get("/api/patterns/pause", origin: "http://evil.example") == (HttpStatusCode.Forbidden, page) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a script of another origin in a browser here is refused");
        Check(await Get("/api/patterns/pause", host: $"evil.example:{port}", origin: $"http://evil.example:{port}", site: "same-origin")
              == (HttpStatusCode.Forbidden, machine) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a name rebound to 127.0.0.1 is another machine's, however same-origin its page is");
        Check(await Get("/api/patterns/pause", host: "127.0.0.1:8080") == (HttpStatusCode.Forbidden, machine) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a forwarder's port over loopback is another machine's");
        Check(await Get("/api/start", site: "cross-site") is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Start) == 1,
              "this pc: every other verb keeps its rules (the open door), another site's page included");
        control.From = [IPAddress.Loopback];
        Check(await Get("/api/patterns/pause", site: "cross-site") == (HttpStatusCode.Forbidden, page) && Count(ShowCommandKind.Patterns) == 2,
              "this pc: a listed address vouches for no page of another site");
        control.From = [];
        Check(await Get("/api/patterns/pause", site: "cross-site", bearer: ctlSecret) is (HttpStatusCode.OK, null) && Count(ShowCommandKind.Patterns) == 3,
              "this pc: another site's page with a control key passes on the key");

        // A WebSocket's upgrade says whose page it is, for every line it sends.
        using (var ws = new ClientWebSocket())
        {
            ws.Options.SetRequestHeader("Origin", "http://evil.example");
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"x1"}""");
            var receipt = await Next(ws, "\"type\":\"receipt\"");
            Check(receipt is not null && JsonNode.Parse(receipt) is { } rr && (bool?)rr["ok"] == false
                  && (string?)rr["error"] == PlayoutGate.OtherSiteWords(ShowCommandKind.Playout, CommandDoor.WebSocket) && Count(ShowCommandKind.Playout) == 0,
                  "this pc: a control page of another site in a browser here is refused playout on its socket");
        }
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","pause"],"id":"x2"}""");
            var receipt = await Next(ws, "\"type\":\"receipt\"");
            Check(receipt is not null && JsonNode.Parse(receipt) is { } rr && (bool?)rr["ok"] == true && Count(ShowCommandKind.Playout) == 1,
                  "this pc: a control socket a program here opened drives playout");
        }

        // MCP on a surface that keeps IShowAgents' defaults: nobody approves, so the line is refused as an error and nothing runs.
        async Task<JsonNode> Mcp(string line, string? origin = null)
        {
            var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 9, method = "tools/call", @params = new { name = "run_command", arguments = new { command = line, reason = "test" } } });
            using var m = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (origin is not null) m.Headers.TryAddWithoutValidation("Origin", origin);
            using var r = await http.SendAsync(m);
            return JsonNode.Parse(await r.Content.ReadAsStringAsync())!["result"]!;
        }
        static string Text(JsonNode result) => (string?)result["content"]![0]!["text"] ?? "";
        var nobody = await Mcp("playout stage-left go show intro");
        Check((bool?)nobody["isError"] == true && Text(nobody) == IShowAgents.NobodyApproves && Count(ShowCommandKind.Playout) == 1,
              "this pc: an agent's playout line where nobody approves is an error, never read as queued, and nothing runs");
        var direct = await Mcp("nudge 30");
        Check((bool?)direct["isError"] == false && Count(ShowCommandKind.Nudge) == 1, "this pc: any other agent line runs directly on such a surface, as before");
        // A certificate is trusted by a person on the desk, never by a line: refused on a socket and to an agent this PC runs, and
        // neither is a proposal.
        using (var ws = new ClientWebSocket())
        {
            await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws?role=control"), CancellationToken.None);
            await Send(ws, """{"cmd":"playout","args":["stage-left","trust","03e9b152"],"id":"x3"}""");
            var receipt = await Next(ws, "\"type\":\"receipt\"");
            Check(receipt is not null && JsonNode.Parse(receipt) is { } rr && (bool?)rr["ok"] == false
                  && (string?)rr["error"] == PlayoutGate.DeskOnlyWords(CommandDoor.WebSocket) && Count(ShowCommandKind.PlayoutTrust) == 0,
                  "this pc: playout trust on a control socket a program here opened is refused in words, and nothing runs");
        }
        var trust = await Mcp("playout stage-left trust 03e9 b152");
        Check((bool?)trust["isError"] == true && Text(trust) == "Refused: " + PlayoutGate.DeskOnlyWords(CommandDoor.Mcp) && Count(ShowCommandKind.PlayoutTrust) == 0,
              $"this pc: an agent's playout trust is refused at the door, never a proposal ({Text(trust)})");
        var fromPage = await Mcp("patterns pause", origin: "http://evil.example");
        Check((bool?)fromPage["isError"] == true && Text(fromPage) == "Refused: " + PlayoutGate.OtherSiteWords(ShowCommandKind.Patterns, CommandDoor.Mcp)
              && Count(ShowCommandKind.Patterns) == 3,
              "this pc: an agent call from another site's page is refused at the door");
    }

    static Task Send(ClientWebSocket ws, string json) => ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

    /// <summary>The next message holding <paramref name="marker"/>, frames and beats passed over; null when none comes.</summary>
    static async Task<string?> Next(ClientWebSocket ws, string marker)
    {
        var buf = new byte[65536];
        for (var i = 0; i < 40; i++)
        {
            using var cts = new CancellationTokenSource(3000);
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buf, cts.Token); }
            catch (OperationCanceledException) { return null; }
            if (r.MessageType == WebSocketMessageType.Close) return null;
            var text = Encoding.UTF8.GetString(buf, 0, r.Count);
            if (text.Contains(marker, StringComparison.Ordinal)) return text;
        }
        return null;
    }

    static int FreeTcpPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }

    static int FreeUdpPort()
    {
        using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
    }

    sealed class Access : IShowAccess
    {
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
    }

    /// <summary>A surface whose agents act directly (no approval asked) unless a line must wait for a person.</summary>
    sealed class GateControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new([], [], []);
        public volatile IReadOnlyList<ApiKey> Keys = [];
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
        private int _proposed;
        public int ProposedCount => Volatile.Read(ref _proposed);
        public volatile bool LastMust;
        public volatile bool IgnoreMust;
        public AgentReply? ProposeFromAgent(string commandLine, string reason, bool mustPropose)
        {
            if (!mustPropose || IgnoreMust) return null;
            LastMust = true;
            return AgentReply.Waiting($"Queued for the operator's approval (proposal #{Interlocked.Increment(ref _proposed)}).");
        }
    }

    /// <summary>A surface that keeps every IShowAgents default (as the rig does): nobody here approves an agent's line.</summary>
    sealed class PlainControl : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new([], [], []);
        public volatile IReadOnlyList<ApiKey> Keys = [];
        public IReadOnlyList<ApiKey> ApiKeys => Keys;
        public volatile IReadOnlyList<IPAddress> From = [];
        public IReadOnlyList<IPAddress> PlayoutFrom => From;
    }
}
