using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// docs/NODES.md, Stagewatch's node page, held to the code it is written from. Each ```json hello in its §2 is exactly what
/// ShowBusHello builds for the inputs that hello shows — its fields, their order and their values, nothing more — and goes where the
/// page says (a Stagewatch's to the network, a Slides agent's always). Every row of its numbers table is one this test reads from the
/// code, each with the code's value, and a row named as a §14 row of docs/SHOWBUS.md has §14's value; every number the code gives
/// that table is a row of it, and the table's own rows are exactly the ones its preamble names as Stagewatch's own. Find's rows are
/// the ports Find listens on, read from the list it listens on; while that list holds Patterns' beacon, the page says it stays until
/// Patterns' OPEN L47 walk and WHATS_NEXT names the change that drops it, and once it does not, the page no longer says it stays.
/// Settings' Find listens for the table's time.
/// </summary>
static class NodesDocTests
{
    const string DiscoveryHeading = "## 2. Discovery: the hello on UDP 9928";
    const string JoiningHeading = "## 8. Joining";
    const string NumbersHeading = "### Stagewatch's numbers";

    /// <summary>
    /// The numbers NODES.md gives, each read from the code. A name that is a §14 row means that row; the others are Stagewatch's own.
    /// A number whose owner holds it as a private field is read by reflection, by the field's name.
    /// </summary>
    static readonly (string Name, Func<long> Code)[] Numbers =
    [
        ("hello.port", () => SlidesBeacon.Port),
        ("hello.every.ms", () => Ms(SlidesBeacon.Every)),
        ("proto", () => ShowBusHello.Proto),
        ("hello.bytes", () => ShowBusHello.HelloBytes),
        ("hello.read.bytes", () => SlidesBeacon.ReadBytes),
        ("hello.caps", () => SlidesBeacon.KeptCaps),
        ("hello.cap.chars", () => ShowBusHello.CapChars),
        ("hello.ports", () => SlidesBeacon.KeptPorts),
        ("hello.name.chars", () => SlidesBeacon.NameChars),
        ("hello.kind.chars", () => SlidesBeacon.KindChars),
        ("hello.id.min", () => ShowBusHello.IdMin),
        ("hello.id.max", () => SlidesBeacon.IdChars),
        ("hello.inst.chars", () => ShowBusHello.InstChars),
        ("peers.max", () => SlidesBeacon.MaxPeers),
        ("peer.addresses", () => SlidesBeacon.KeptAddresses),
        ("peer.stale.ms", () => Ms(SlidesBeacon.StaleAfter)),
        ("peer.forget.ms", () => Ms(SlidesBeacon.ForgetAfter)),
        ("peer.conflict.clear.ms", () => Ms(SlidesBeacon.ConflictClearsAfter)),
        ("hello.failed.beats", () => ShowBusHello.FailedBeats),
        .. FindRows(),
        ("find.listen.ms", () => Ms(PlayoutWire.FindListen)),
        ("find.max", () => PlayoutWire.FindMax),
        ("slides.id.min", () => Shortest(SlideFeed.Valid)),
        ("slides.id.max", () => Longest(SlideFeed.Valid)),
        ("slides.stale.ms", () => Ms(RemoteDecks.StaleAfter)),
        ("slides.script.bytes", () => SlideFeed.MaxScript),
        ("slides.script.hex", () => SlideFeed.HashScript("a script").Length),
        ("cid.min", () => Shortest(Screens.ValidId)),
        ("cid.max", () => Longest(Screens.ValidId)),
        ("http.body.bytes", () => Private<int>(typeof(WebServer).Assembly.GetType("Stagewatch.Remote.HttpRequest"), "MaxBody")),
        ("http.drain.bytes", () => Private<int>(typeof(WebServer).Assembly.GetType("Stagewatch.Remote.HttpRequest"), "MaxDrain")),
        ("http.drain.ms", () => Ms(Private<TimeSpan>(typeof(WebServer).Assembly.GetType("Stagewatch.Remote.HttpRequest"), "DrainFor"))),
        ("ws.beat.ms", () => Ms(WebServer.HeartbeatEvery)),
        ("show.stuck.ms", () => Ms(SnapshotHub.StuckAfter)),
        ("pin.wrong.per.minute", () => WebServer.WrongPinsPerMinute),
        ("view.wrong.per.minute", () => WebServer.WrongViewPassesPerMinute),
        ("key.requests.per.minute", () => ApiKeys.PerMinute),
        // One number for both: Stagewatch's own context as sent, and one posted to it. Apart, the row would say neither.
        ("context.bytes", () => ShowContext.MaxBytes == PeerContexts.MaxBytes ? ShowContext.MaxBytes : -1),
        ("context.items", () => ShowContext.MaxItems),
        ("context.links", () => ShowContext.MaxLinks),
        ("context.commands", () => ShowContext.MaxCommands),
        ("context.kept.ms", () => Ms(PeerContexts.KeepFor)),
        ("ack.by.chars", () => SignalBoard.ByMax),
        ("signal.kept.ms", () => Ms(new SignalBoard().KeepDoneFor)),
        ("read.context.items", () => Heard(1000, 1)?.Items.Count ?? -1),
        ("read.context.commands", () => Heard(1000, 1)?.Commands.Count ?? -1),
        ("read.context.verb.chars", () => Heard(1, 1000) is { Commands: [var first, ..] } ? first.Verb.Length : -1),
        ("cert.days", () => (long)CertificateLife.Span.TotalDays),
        ("cert.renew.days", () => CertificateLife.RenewDays),
        // The Patterns link is a PlayoutLink on the PeerLink core: its liveness and redial are the core's (Show Bus 13.10.3, 13.15.1).
        ("patterns.wire.port", () => PlayoutLink.DefaultPort),
        ("patterns.ping.ms", () => Ms(PlayoutLink.PingEvery)),
        ("link.stale.ms", () => PeerLink.StaleMs),
        ("patterns.drop.ms", () => PeerLink.DropMs),
        ("link.selfgap.ms", () => PeerLink.SelfGapMs),
        ("patterns.redial.first.ms", () => PeerLink.RedialFirstMs),
        ("patterns.redial.max.ms", () => PeerLink.RedialMaxMs),
        ("patterns.nonce.wait.ms", () => Ms(PlayoutLink.NonceWait)),
        ("patterns.clip.stale.ms", () => Ms(PlayoutLink.StaleAfter)),
        ("patterns.line.bytes", () => PlayoutWire.LineBytes),
        ("patterns.context.every.ms", () => Ms(PlayoutLink.ContextEvery)),
        ("clock.apart.ms", () => PeerLink.ClockApartMs),
    ];

    /// <summary>A row per port Find listens on, named by what it hears there: the hello, Patterns' beacon, or any other port by its number.</summary>
    static IEnumerable<(string Name, Func<long> Code)> FindRows() => PlayoutWire.FindPorts.Select(port => (
        port == PlayoutWire.HelloPort ? "find.hello.port"
        : port == PlayoutWire.BeaconPort ? "find.beacon.port"
        : "find.port." + port.ToString(CultureInfo.InvariantCulture),
        (Func<long>)(() => port)));

    /// <summary>The fewest characters an id of one letter repeated may have and pass <paramref name="valid"/>, trying each length to 256; -1 for none.</summary>
    static long Shortest(Func<string?, bool> valid) => Enumerable.Range(1, 256).FirstOrDefault(n => valid(new string('a', n)), -1);

    /// <summary>The most characters such an id may have and pass <paramref name="valid"/>; -1 for none.</summary>
    static long Longest(Func<string?, bool> valid) => Enumerable.Range(1, 256).LastOrDefault(n => valid(new string('a', n)), -1);

    /// <summary>A peer's context as Stagewatch reads one: <paramref name="many"/> items and as many commands, each verb <paramref name="verb"/> characters.</summary>
    static PeerContexts.Entry? Heard(int many, int verb) => PeerContexts.Parse(
        "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"pat9\"},\"items\":[" + string.Join(",", Enumerable.Repeat("{\"id\":\"i\"}", many))
        + "],\"commands\":[" + string.Join(",", Enumerable.Repeat("{\"verb\":\"" + new string('v', verb) + "\",\"risk\":\"low\"}", many)) + "]}",
        "", DateTime.UnixEpoch);

    public static void Run(Action<bool, string> Check)
    {
        var master = ShowBusSpecTests.Master();
        Check(master is not null, "nodes doc: docs/ found above the suite");
        if (master is null) return;
        var docs = Path.GetDirectoryName(master)!;
        var nodes = Read(Path.Combine(docs, "NODES.md"));
        Check(nodes is not null, "nodes doc: docs/NODES.md is there");
        if (nodes is null) return;

        Hellos(Check, Section(nodes, DiscoveryHeading));
        Table(Check, Section(nodes, JoiningHeading), Fourteen(Read(master) ?? ""));
        Find(Check, nodes, Read(Path.Combine(docs, "WHATS_NEXT.md")) ?? "",
            Read(Path.Combine(Path.GetDirectoryName(docs)!, "src", "Timer.App", "SettingsDialog.xaml.cs")));
    }

    // ------------------------------------------------------------------ §2's hellos

    /// <summary>
    /// Every ```json block of §2 is a hello of kind main or agent, at least one of each, and each is what the builder makes of the
    /// inputs it shows: a Stagewatch's from its id, inst, name, host, clock, its certificate and the doors its ports name (each bound to
    /// the network, as only those are named), with mcp while its caps say so; an agent's from its id, inst, name, host, clock and whether
    /// it presents. The comparison is of the JSON as it goes on the wire — property order, values and no extra field — so a field the
    /// builder adds, drops or moves fails here until the page shows it.
    /// </summary>
    static void Hellos(Action<bool, string> Check, string s2)
    {
        var blocks = Regex.Matches(s2, "^```json\n(.*?)\n```$", RegexOptions.Multiline | RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        Check(s2.Length > 0, $"nodes doc: NODES.md has its section '{DiscoveryHeading}'");
        var kinds = new List<string>();
        foreach (var block in blocks)
        {
            JsonElement h;
            try { h = JsonDocument.Parse(block).RootElement.Clone(); }
            catch (JsonException e) { Check(false, "nodes doc: §2's hello is JSON (" + e.Message + ")"); continue; }
            var kind = Str(h, "t");
            kinds.Add(kind);
            var id = Str(h, "id");
            var inst = Str(h, "inst");
            var name = Str(h, "name");
            var host = Str(h, "host");
            var clock = h.TryGetProperty("clock", out var c) && c.TryGetInt64(out var ms) ? ms : -1;
            byte[]? built = null;
            if (kind == "main")
            {
                var ports = h.TryGetProperty("ports", out var p) && p.ValueKind == JsonValueKind.Object
                    ? p.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.Number).ToDictionary(x => x.Name, x => x.Value.GetInt32())
                    : new Dictionary<string, int>();
                var web = ports.GetValueOrDefault("http");
                var tls = h.TryGetProperty("tls", out var t) ? t.GetString() : null;
                var doors = new HelloDoors(web, true, tls, ports.GetValueOrDefault("companion"), true, ports.GetValueOrDefault("osc"), true);
                var mcp = h.TryGetProperty("caps", out var caps) && caps.ValueKind == JsonValueKind.Array && caps.EnumerateArray().Any(x => x.GetString() == "mcp");
                var (hello, scope) = ShowBusHello.Main(id, inst, name, host, doors, mcp, clock);
                Check(hello is not null && scope == HelloScope.Network,
                    $"nodes doc: §2's Stagewatch hello is one ShowBusHello.Main builds and sends to the network ({scope})");
                Check(Regex.IsMatch(id, "^[0-9a-f]{12}$"), $"nodes doc: §2's Stagewatch hello has an id of the shape the settings make: 12 lowercase hex ({id})");
                if (hello is not null) built = ShowBusHello.Bytes(hello);
            }
            else if (kind == "agent")
            {
                var presenting = h.TryGetProperty("presenting", out var pr) && pr.ValueKind == JsonValueKind.True;
                built = ShowBusHello.Bytes(ShowBusHello.Agent(id, inst, name, host, presenting, clock));
                Check(SlideFeed.Valid(id), $"nodes doc: §2's agent hello has an id a feed may carry ({id})");
            }
            else
            {
                Check(false, $"nodes doc: §2's hellos are a Stagewatch's (main) and a Slides agent's (agent), each held to its builder ({kind})");
                continue;
            }
            if (built is null) continue;
            var shown = Canonical(h);
            var made = Canonical(JsonDocument.Parse(built).RootElement);
            Check(shown == made, $"nodes doc: §2's {kind} hello is what ShowBusHello builds for its own inputs, field for field and in order\n  page:    {shown}\n  builder: {made}");
            Check(built.Length <= ShowBusHello.HelloBytes && ShowBusHello.Read(built) is { } back && back.Id == id && back.Inst == inst,
                $"nodes doc: §2's {kind} hello fits {ShowBusHello.HelloBytes} bytes ({built.Length}) and a reader takes it back whole");
        }
        Check(kinds.Contains("main") && kinds.Contains("agent"), "nodes doc: §2 shows a Stagewatch's hello and a Slides agent's (" + string.Join(", ", kinds) + ")");
    }

    /// <summary>The JSON as the wire has it: compact, its properties in their order, every string escaped one way.</summary>
    static string Canonical(JsonElement e) => JsonSerializer.Serialize(e);

    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ------------------------------------------------------------------ §8's numbers

    /// <summary>
    /// The numbers table: each row once, each a number this test reads from the code and with the code's value, every one of those a
    /// row, and a row named as a §14 row with §14's value. Its preamble names Stagewatch's own rows (a prefix ending in a dot, or a
    /// whole name): every row that is not a §14 row is one of them, and none of them is a §14 row.
    /// </summary>
    static void Table(Action<bool, string> Check, string s8, IReadOnlyDictionary<string, string> fourteen)
    {
        var table = Section(s8, NumbersHeading);
        Check(table.Length > 0, $"nodes doc: §8 has '{NumbersHeading}'");
        var rows = Regex.Matches(table, @"^\| `([a-z][a-z0-9.]*)` \| ([0-9]+) \| [^|\n]+ \|$", RegexOptions.Multiline)
            .Select(m => (Name: m.Groups[1].Value, Value: m.Groups[2].Value)).ToList();
        var lines = table.Split('\n').Count(l => l.StartsWith("| `", StringComparison.Ordinal));
        Check(rows.Count > 0 && rows.Count == lines, $"nodes doc: every row of the numbers table is '| `name` | whole number | what it is |' ({rows.Count} of {lines})");
        var twice = rows.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check(twice.Count == 0, "nodes doc: each number is given once (" + string.Join(", ", twice) + ")");

        var code = Numbers.ToDictionary(n => n.Name, n => n.Code, StringComparer.Ordinal);
        var unknown = rows.Where(r => !code.ContainsKey(r.Name)).Select(r => r.Name).ToList();
        Check(unknown.Count == 0, "nodes doc: every number the page gives is one the code is read for here (" + string.Join(", ", unknown) + ")");
        var missing = Numbers.Select(n => n.Name).Where(n => rows.All(r => r.Name != n)).ToList();
        Check(missing.Count == 0, "nodes doc: every number read from the code here is a row of the page (" + string.Join(", ", missing) + ")");

        foreach (var (name, value) in rows)
        {
            if (!code.TryGetValue(name, out var read)) continue;
            long actual;
            try { actual = read(); }
            catch (InvalidOperationException e)
            {
                Check(false, $"nodes doc: {name} is read from the code ({e.Message})");
                continue;
            }
            Check(value == actual.ToString(CultureInfo.InvariantCulture), $"nodes doc: {name} is the code's {actual}, as the page says ({value})");
            if (fourteen.TryGetValue(name, out var row))
                Check(row == value, $"nodes doc: {name} is a §14 row, and the page gives §14's value {row} ({value})");
        }
        Check(fourteen.Count > 0, "nodes doc: §14's numbers block is read from docs/SHOWBUS.md");

        var preamble = string.Join('\n', table.Split('\n').TakeWhile(l => !l.StartsWith('|')));
        var own = Regex.Matches(preamble, "`([a-z][a-z0-9.]*)`").Select(m => m.Groups[1].Value).ToList();
        bool Own(string name) => own.Any(o => o.EndsWith('.') ? name.StartsWith(o, StringComparison.Ordinal) : name == o);
        var unnamed = rows.Where(r => !fourteen.ContainsKey(r.Name) && !Own(r.Name)).Select(r => r.Name).ToList();
        var claimed = rows.Where(r => fourteen.ContainsKey(r.Name) && Own(r.Name)).Select(r => r.Name).ToList();
        Check(own.Count > 0 && unnamed.Count == 0 && claimed.Count == 0,
            "nodes doc: the table's preamble names every row that is not a §14 row as Stagewatch's own, and no §14 row "
            + $"(not named: {string.Join(", ", unnamed)}; §14's: {string.Join(", ", claimed)})");
    }

    /// <summary>§14's ```showbus block as name → value, for its rows of one name and one value.</summary>
    static Dictionary<string, string> Fourteen(string master)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var block = Regex.Match(master.Replace("\r\n", "\n", StringComparison.Ordinal), "^```showbus\n(.*?)\n```$", RegexOptions.Multiline | RegexOptions.Singleline);
        if (!block.Success) return rows;
        foreach (var line in block.Groups[1].Value.Split('\n'))
            if (line.Split(' ') is [var name, var value]) rows.TryAdd(name, value);
        return rows;
    }

    // ------------------------------------------------------------------ Find and 9700

    /// <summary>
    /// Find listens on <see cref="PlayoutWire.FindPorts"/>, the list it binds. While that list holds Patterns' beacon, the page says so
    /// with the walk it waits for, and WHATS_NEXT names the change that drops it once that walk is filled; once it does not, the page no
    /// longer says Find keeps it. Settings' Find listens for <see cref="PlayoutWire.FindListen"/>, the table's time, and no other, for
    /// Patterns desks, the kind its box links to (Timer.App is not built beside this suite on every platform, so its source is read).
    /// </summary>
    static void Find(Action<bool, string> Check, string nodes, string next, string? dialog)
    {
        const string Stays = "9700 stays in Find until Patterns' own hello on 9928 has been heard on a real network (Patterns' OPEN row L47, a walk)";
        var listens = PlayoutWire.FindPorts.Contains(PlayoutWire.BeaconPort);
        Check(Flat(nodes).Contains(Stays, StringComparison.Ordinal) == listens,
            listens ? "nodes doc: NODES.md says Find keeps 9700 until Patterns' L47 walk" : "nodes doc: Find no longer listens on 9700, and NODES.md no longer says it stays");
        if (listens)
            Check(Flat(next).Contains("Drop UDP 9700 from Find once Patterns' 9928 walk (its OPEN row L47) is filled", StringComparison.Ordinal),
                "nodes doc: WHATS_NEXT names the change that drops 9700 from Find, and the walk it waits for");
        var calls = Regex.Matches(dialog ?? "", @"DiscoverAsync\(((?:[^()]|\([^()]*\))*)\)").Select(m => m.Groups[1].Value.Trim()).ToList();
        Check(calls.Count > 0 && calls.All(a => a == "PlayoutWire.FindListen, [PlayoutRules.Patterns]"),
            "nodes doc: Settings' Find listens for PlayoutWire.FindListen, for Patterns desks (" + string.Join("; ", calls) + ")");
    }

    // ------------------------------------------------------------------ helpers

    static long Ms(TimeSpan t) => (long)t.TotalMilliseconds;

    /// <summary>A static field's value by its name, public or not.</summary>
    static T Private<T>(Type? type, string field)
    {
        var f = type?.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"{type?.Name ?? "a type"} has no static field {field}");
        return f.GetValue(null) is T value ? value : throw new InvalidOperationException($"{type!.Name}.{field} is not a {typeof(T).Name}");
    }

    /// <summary>A file's text with LF line ends, or null when it is not there.</summary>
    static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal) : null;

    /// <summary>From a heading's line to the next heading of its level or higher, outside code fences; "" when it is not there.</summary>
    static string Section(string text, string heading)
    {
        var lines = text.Split('\n');
        var level = heading.TakeWhile(ch => ch == '#').Count();
        var start = Array.IndexOf(lines, heading);
        if (start < 0) return "";
        var fenced = false;
        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            if (fenced) continue;
            var m = Regex.Match(lines[i], "^(#+) ");
            if (m.Success && m.Groups[1].Length <= level) { end = i; break; }
        }
        return string.Join('\n', lines[start..end]);
    }

    /// <summary>Text with its line breaks and the indent after them read as one space, so a sentence is found however it wraps.</summary>
    static string Flat(string text) => Regex.Replace(text, @"\s*\n\s*", " ");
}
