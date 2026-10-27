using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stagewatch.Core;

/// <summary>
/// The PeerLink kit's scenarios S1–S22 (Show Bus 13.20): the scripted peer, tools/showbus/peerlink-peer.mjs, on one end of a real
/// socket and Stagewatch's core on the other, the core on injected time. S1–S7 are here; S8–S22, the peer as an attacker as well as
/// honest, are in PeerLinkScenarios.Attacks.cs.
/// <para>
/// The core connecting is the connector 13.20 holds to S1, S2, S4–S7, S11–S14, S17–S19 and S22: <see cref="PeerLinkConnector"/> decides
/// and <see cref="CoreConnector"/> carries, on every carrier the connector keeps — <c>line</c> over TCP and TLS, <c>line</c> over
/// <c>playmap.lines.v1</c> (PlayoutLink's to PlayMap), and <c>ws</c> with its side calls. Each scenario runs on each of them. S21's
/// connector half is the core's crypto (enrolment's K and kid, and proving with the enrolled key): the connector that keeps an enrolled
/// key is PlayoutLink's (SW23.1).
/// </para>
/// <para>
/// The acceptor's rows (S1–S3, S8–S10, S12's emitting, S14–S18, S20) are not Stagewatch's yet: its /ws acceptor speaks LINK with
/// SW22.1a–c, which take them over. Until then <see cref="CoreAcceptor"/>, the kit's stand-in, answers the peer's connector with the
/// core's parsers, proofs, session check and receipt store, so what those checks hold is the peer's connector end — honest, or an
/// attacker's — against the core's crypto, CheckSession and PeerReceipts; an acceptor's own decisions are the stand-in's. What has no
/// part in the core yet — an acceptor's wrong-proof table, its unpaired bound, its reply bound, its Host and Origin checks, its stuck
/// beat — is checked here as the peer's attack on the wire, and named with the unit that brings it. So S16 and S20, S15's slow reader
/// and S12's emitting half run no Stagewatch code yet (<see cref="PeerOnly"/>; the master's header says so beside
/// <see cref="OnCore"/>). Stagewatch offers no enrolment and runs no forwarder, so S21's acceptor half and S20's forwarder are not its
/// rows.
/// </para>
/// <para>
/// The carriers of Stagewatch's own links take these scenarios over as they land on the core: PlayoutLink, the desk's link to Patterns,
/// runs S1, S11, S13, S14, S19 and S22 as the level-0 connector it is today (PeerLinkScenarios.Playout.cs), and the rest as it offers
/// LINK (SW23.1). The peer's own rules are held too: no arguments, keys and certificates only from owner-only files, an unknown command, field, attack or
/// shape refused, its framing in step past an oversize line or message, an open node's answers, and no key, code, K or S of its own on
/// its stdout.
/// </para>
/// </summary>
static partial class PeerLinkScenarios
{
    /// <summary>The injected clock's start, in ms: one clock for the monotonic and the wall time, as 13.20's machine rows have it.</summary>
    const long T0 = 1_000_000_000;

    // The secrets the scenarios prove — canaries too: none may cross a wire in clear or reach the peer's stdout.
    const string Ptk = "ptk_3vq8xk2m9tz7w4hc6r5n2b8d7f4g9j3k";
    const string Pmk = "pmk_7m4k2x9q3w8z5t6r2c4n8b3d7f9g2h5j";
    const string Swk = "swk_abcdefghijkmnpqrstuvwxyz23456789";
    const string Stranger = "ptk_9z8y7x6w5v4u3t2s2r3q4p5n6m7k8j9h";
    const string Code = "K7QM-3XWD-P9RA";
    const string CodeTyped = "k7qm 3xwd-p9ra";
    const string WrongCode = "ABCD-EFGH-JKMN";

    const string PatternsId = "9f8e7d6c";
    const string PlayMapId = "0a1b2c3d";
    const string MainId = CoreAcceptor.Id;

    static readonly object Patterns = new { t = "patterns", id = PatternsId };
    static readonly object PlayMap = new { t = "playmap", id = PlayMapId };
    static readonly object Main = new { t = "main", id = MainId };

    /// <summary>
    /// A carrier the core's connector keeps, with the node it dials there and the key that node issued it: <c>line</c> to Patterns,
    /// <c>line</c> over <c>playmap.lines.v1</c> to PlayMap, and <c>ws</c> to another Stagewatch.
    /// </summary>
    sealed record Leg(string Label, PeerCarrier Carrier, bool OverWs, string Secret, object Node, string Kind, string Id)
    {
        public string Wire => Carrier == PeerCarrier.Line ? "line" : "ws";

        public string Transport => OverWs ? "ws" : "tcp";
    }

    static readonly Leg Line = new("line", PeerCarrier.Line, false, Ptk, Patterns, "patterns", PatternsId);
    static readonly Leg LinesV1 = new("playmap.lines.v1", PeerCarrier.Line, true, Pmk, PlayMap, "playmap", PlayMapId);
    static readonly Leg Ws = new("ws", PeerCarrier.Ws, false, Swk, Main, "main", MainId);
    static readonly Leg[] Legs = [Line, LinesV1, Ws];

    /// <summary>The kit's files: three certificates (the node's, the node's new one, another machine's) and each secret's file.</summary>
    sealed record Material(KitFiles Files, KitCert A, KitCert B, KitCert M)
    {
        public string File(string secret) => Files.Write(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(secret)))[..12] + ".secret", secret);
    }

    static object Key(Material kit, string key) => new { file = kit.File(key), kind = "key" };

    static object CodeOf(Material kit, string code) => new { file = kit.File(code), kind = "code" };

    /// <summary>
    /// The scenarios this group runs against Stagewatch's PeerLink core — its connector, or the core behind the kit's stand-in. The
    /// master's header names them as what this tree runs against its core.
    /// </summary>
    static readonly int[] OnCore = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 18, 19, 21, 22];

    /// <summary>
    /// The scenarios that run no Stagewatch code yet, only the peer's attack on the wire, until Stagewatch's /ws acceptor speaks LINK
    /// (SW22.1a–c). The master's header names them so, with S15's slow reader and S12's emitting half, which are halves of rows above.
    /// </summary>
    static readonly int[] PeerOnly = [16, 20];

    /// <summary>Every connector the scenarios made, for S19's capture of what the core published.</summary>
    static readonly List<CoreConnector> Every = [];

    /// <summary>Every scripted peer the scenarios started, for S19's look at each one's stdout.</summary>
    static readonly List<ScriptedPeer> Peers = [];

    /// <summary>Every stand-in the scenarios started, for the session credentials it made (S19).</summary>
    static readonly List<CoreAcceptor> Stands = [];

    /// <summary>A scripted peer on <paramref name="script"/>, kept for S19.</summary>
    static ScriptedPeer Peer(string script)
    {
        var peer = ScriptedPeer.Start(script);
        Peers.Add(peer);
        return peer;
    }

    /// <summary>The kit's stand-in acceptor (<see cref="CoreAcceptor"/>), kept for S19.</summary>
    static CoreAcceptor Stand(PeerCarrier carrier, KitCert? cert, IEnumerable<(byte[] K, string Kid, string Kind)> keys, System.Net.IPAddress? on = null)
    {
        var stand = new CoreAcceptor(carrier, cert, keys, on);
        Stands.Add(stand);
        return stand;
    }

    /// <summary>
    /// Scenario numbers as the master writes them: a run of three or more as <c>Sa–Sb</c>, the rest one by one, joined by commas and
    /// <c>and</c> before the last.
    /// </summary>
    static string Ranges(IReadOnlyList<int> n)
    {
        var parts = new List<string>();
        for (var i = 0; i < n.Count;)
        {
            var j = i;
            while (j + 1 < n.Count && n[j + 1] == n[j] + 1) j++;
            if (j - i >= 2) parts.Add("S" + n[i].ToString(CultureInfo.InvariantCulture) + "–S" + n[j].ToString(CultureInfo.InvariantCulture));
            else for (var k = i; k <= j; k++) parts.Add("S" + n[k].ToString(CultureInfo.InvariantCulture));
            i = j + 1;
        }
        return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
    }

    public static async Task Run(Action<bool, string> Check)
    {
        var script = ScriptedPeer.Script();
        Check(script is not null, "peerlink scenarios: tools/showbus/peerlink-peer.mjs is in this tree");
        if (script is null) return;
        Words(Check, script);
        if (!await NodeRuns(Check)) return;
        using var files = new KitFiles();
        var kit = new Material(files, files.Cert("node-a"), files.Cert("node-b"), files.Cert("between"));
        await PeerRules(Check, script, kit);
        await S1(Check, script, kit);
        await S2AndS3(Check, script, kit);
        await S4(Check, script, kit);
        await S5(Check, script, kit);
        await S6(Check, script, kit);
        await S7(Check, script, kit);
        await S8(Check, script, kit);
        await S9(Check, script, kit);
        await S10(Check, script, kit);
        await S11(Check, script, kit);
        await S12(Check, script, kit);
        await S13(Check, script, kit);
        await S14(Check, script, kit);
        await S15(Check, script, kit);
        await S16(Check, script);
        await S17(Check, script, kit);
        await S18(Check, script, kit);
        await S20(Check, script, kit);
        await S21(Check, script, kit);
        await S22(Check, script, kit);
        await Playout(Check, script, kit);
        await S19(Check, script, kit);
    }

    /// <summary>
    /// The master and the peer agree: 13.20 names S1–S22 in order, and the commands it names are the peer's; the header names the
    /// scenarios this group runs against the core and those that check only the peer's attack; the peer is ASCII with LF line ends, so
    /// its bytes are one in every tree (.gitattributes keeps them LF in any checkout); every §14 number the peer keeps is its row's;
    /// and §14's <c>peerlink.peer.sha256</c>, pinned with 1.3 final, is the peer's SHA-256 (a copy holds the peer to the same row).
    /// </summary>
    static void Words(Action<bool, string> Check, string script)
    {
        var text = File.ReadAllText(ShowBusSpecTests.Master()!, new UTF8Encoding(false)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var conformance = text[text.IndexOf("### 13.20 Conformance", StringComparison.Ordinal)..];
        var ids = Regex.Matches(conformance, @"^\| (S\d+) \|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Check(ids.SequenceEqual(Enumerable.Range(1, 22).Select(i => "S" + i.ToString(CultureInfo.InvariantCulture))),
            "peerlink scenarios: 13.20 names scenarios S1–S22, in order (" + string.Join(", ", ids) + ")");
        var drives = Regex.Match(conformance, @"one JSON object a line: (.*?), each answered", RegexOptions.Singleline).Groups[1].Value;
        var named = Regex.Matches(drives, "`([a-z]+)`").Select(m => m.Groups[1].Value).ToList();
        var bytes = File.ReadAllBytes(script);
        var source = Encoding.UTF8.GetString(bytes);
        var commands = Regex.Match(source, @"const commands = \{ ([a-z, ]+) \};").Groups[1].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToList();
        Check(named.Count > 0 && named.SequenceEqual(commands) && commands.All(c => source.Contains("//   " + c + " ", StringComparison.Ordinal)),
            $"peerlink scenarios: the commands 13.20 names are the peer's, each documented at the head of its file ({string.Join(", ", named)} / {string.Join(", ", commands)})");
        var header = text[..text.IndexOf("\n## 1. ", StringComparison.Ordinal)].Replace("\n", " ", StringComparison.Ordinal);
        var onCore = Ranges(OnCore);
        var peerOnly = Ranges(PeerOnly);
        Check(OnCore.Concat(PeerOnly).Order().SequenceEqual(Enumerable.Range(1, 22)) && OnCore.Intersect(PeerOnly).Any() == false,
            "peerlink scenarios: each of S1–S22 runs against the core or checks only the peer's attack, never both");
        Check(header.Contains("this tree runs " + onCore + " with it against its PeerLink core", StringComparison.Ordinal)
              && header.Contains(peerOnly + " run only the peer's attack", StringComparison.Ordinal)
              && !header.Contains(" come with the rest of the conformance kit", StringComparison.Ordinal),
            $"peerlink scenarios: the master's header says this tree runs {onCore} with the scripted peer against its core, and that {peerOnly} run only the peer's attack");
        PeerNumbers(Check, text, script);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(script)));
        var pinned = Regex.Match(text, @"^peerlink\.peer\.sha256 (\S+)$", RegexOptions.Multiline).Groups[1].Value;
        Check(pinned == hash,
            $"peerlink scenarios: §14's peerlink.peer.sha256 is the scripted peer's SHA-256 (it reads {pinned}; the peer hashes to {hash}) — "
            + "an edit to the peer is a new pin in §14, and so a new body hash");
        Check(bytes.All(b => b < 0x80) && !bytes.Contains((byte)'\r') && bytes[^1] == (byte)'\n',
            "peerlink scenarios: the scripted peer is ASCII — any other character a \\u escape, so no editor drops or changes an invisible one — with LF line ends and one final LF");
        var attributes = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(script)!)!)!, ".gitattributes");
        Check(File.Exists(attributes) && File.ReadAllLines(attributes).Contains("tools/showbus/*.mjs text eol=lf"),
            "peerlink scenarios: .gitattributes keeps the scripted peer's line ends LF in every checkout");
    }

    /// <summary>The peer's numbers it keeps for itself, none of them §14's: an expect's wait (13.20's 2 s), an HTTP head, the events kept.</summary>
    static readonly string[] PeerOwn = ["WAIT_MAX_MS", "HEAD_MAX", "LOG_MAX"];

    /// <summary>
    /// The scripted peer's numbers are §14's: each <c>const NAME = value;</c> that names a row (<c>// 14: &lt;name&gt;</c>) holds that
    /// row's value — the features §14's <c>peerlink.feature</c> rows, in order — and every other whole-number constant is one of
    /// <see cref="PeerOwn"/>. The kit's carriers read §14's <c>link.read.min.bytes</c> whole.
    /// </summary>
    static void PeerNumbers(Action<bool, string> Check, string text, string script)
    {
        const string Fence = "\n```showbus\n";
        var at = text.IndexOf(Fence, StringComparison.Ordinal);
        var block = at < 0 ? "" : text[(at + Fence.Length)..];
        block = block[..Math.Max(0, block.IndexOf("\n```", StringComparison.Ordinal))];
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        var features = new List<string>();
        foreach (var w in block.Split('\n').Select(l => l.Split(' ')).Where(w => w.Length == 2))
        {
            if (w[0] == "peerlink.feature") features.Add(w[1]);
            else rows.TryAdd(w[0], w[1]);
        }
        var peer = File.ReadAllText(script, new UTF8Encoding(false));
        var wrong = new List<string>();
        var named = new List<string>();
        foreach (Match m in Regex.Matches(peer, @"^const ([A-Z][A-Z0-9_]*) = (.+?);(?: // 14: ([a-z0-9.]+))?$", RegexOptions.Multiline))
        {
            var (name, value) = (m.Groups[1].Value, m.Groups[2].Value);
            if (!m.Groups[3].Success)
            {
                if (Regex.IsMatch(value, "^[0-9]+$") && !PeerOwn.Contains(name)) wrong.Add(name + " names no §14 row");
                continue;
            }
            var row = m.Groups[3].Value;
            named.Add(row);
            var want = row == "peerlink.feature" ? "[" + string.Join(", ", features.Select(f => "'" + f + "'")) + "]"
                : rows.TryGetValue(row, out var v) ? Regex.IsMatch(v, "^[0-9]+$") ? v : "'" + v + "'" : null;
            if (value != want) wrong.Add($"{name} = {value}, §14's {row} {want ?? "is no row"}");
        }
        Check(wrong.Count == 0 && named.Count >= 17 && named.Distinct(StringComparer.Ordinal).Count() == named.Count && features.Count > 0,
            $"peerlink scenarios: every §14 number the scripted peer keeps names its row and holds its value, and its other numbers are its own ({named.Count} named; {string.Join("; ", wrong)})");
        Check(rows.TryGetValue("link.read.min.bytes", out var read) && KitIo.ReadMax.ToString(CultureInfo.InvariantCulture) == read,
            $"peerlink scenarios: the kit's carriers read §14's link.read.min.bytes whole ({KitIo.ReadMax} / {read})");
    }

    /// <summary>Node 22 or later answers on PATH: the peer runs on it.</summary>
    static async Task<bool> NodeRuns(Action<bool, string> Check)
    {
        string version;
        try
        {
            using var node = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false },
            };
            node.Start();
            version = (await node.StandardOutput.ReadToEndAsync()).Trim();
            await node.WaitForExitAsync();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            version = "";
        }
        var major = version.StartsWith('v') && int.TryParse(version[1..].Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var m) ? m : 0;
        Check(major >= 22, $"peerlink scenarios: Node 22 or later on PATH runs the scripted peer (found '{version}'; tools/setup-dev.sh checks for it, CI's setup-node provides it)");
        return major >= 22;
    }

    static string? Str(JsonElement e, string name) => ScriptedPeer.Str(e, name);

    static int? Int(JsonElement e, string name) => ScriptedPeer.Int(e, name);

    static bool Ev(JsonElement e, string ev) => Str(e, "ev") == ev;

    /// <summary>Whether an event's <paramref name="name"/> is true: false when it is missing or not a boolean, as on a peer that stalled.</summary>
    static bool True(JsonElement e, string name = "ok")
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Whether an event's <paramref name="name"/> is false (and present).</summary>
    static bool False(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.False;

    /// <summary>Whether an event's words start with <paramref name="start"/>: false when it has none.</summary>
    static bool Says(JsonElement e, string start) => Str(e, "words") is { } w && w.StartsWith(start, StringComparison.Ordinal);

    /// <summary>
    /// Lets what is on its way reach the peer for 200 ms of real time and every event before its answer reach this suite: an absence
    /// read after it is one the peer saw.
    /// </summary>
    static Task Settle(ScriptedPeer peer) => peer.Ask(new { @do = "silence", match = new { ev = "settled" }, ms = 200 });

    /// <summary>The connection end "a" took, once the peer's event for it has come.</summary>
    static async Task<int> Taken(ScriptedPeer peer)
        => await peer.Wait(e => Ev(e, "connected") && Str(e, "end") == "a") is { } e ? Int(e, "conn") ?? -1 : -1;

    /// <summary>The first line or message the peer wrote on <paramref name="conn"/> that starts with <paramref name="start"/>, once it has come.</summary>
    static async Task<string> Wrote(ScriptedPeer peer, int conn, string start)
        => await peer.Wait(e => Ev(e, "wrote") && Int(e, "conn") == conn && (Str(e, "line") ?? Str(e, "message") ?? "").StartsWith(start, StringComparison.Ordinal)) is { } e
            ? Str(e, "line") ?? Str(e, "message") ?? ""
            : "";

    static async Task<int> Listen(Action<bool, string> Check, ScriptedPeer peer, string name, object command)
    {
        var e = await peer.Ask(command);
        Check(Ev(e, "listening") && Int(e, "port") > 0, $"peerlink scenarios: {name}: the peer listens ({e.GetRawText()}) {peer.Faults}");
        return Int(e, "port") ?? 1;
    }

    /// <summary>The peer as the acceptor of <paramref name="leg"/>: TLS sniffed with <paramref name="cert"/>, pongs stamped T0 + 25.</summary>
    static object ListenAs(Leg leg, KitCert cert, object[] keys, int level = 1, string tls = "sniff") => new
    {
        @do = "listen", end = "a", carrier = leg.Wire, transport = leg.Transport, tls, cert = cert.CertFile, certKey = cert.KeyFile, node = leg.Node,
        keys, level, clock = T0 + 25,
    };

    /// <summary>
    /// S1's last two parts on a live link the core dialled: a command with its request id runs once at the peer and reads applied, and
    /// a pong that answers the core's ping is a clock sample — rtt 10, errMs 6, offset 0, with the peer stamping T0 + 25 — that
    /// schedules a timed command when the link is TLS or local (13.11.6).
    /// </summary>
    static async Task CommandAndPong(Action<bool, string> Check, string name, CoreConnector c, ScriptedPeer peer, bool schedules)
    {
        var id = c.M.NextId();
        var sent = c.M.Send(T0 + 10, id, "NOTE hello");
        await c.Flush(T0 + 10);
        await c.Pump(T0 + 10, () => c.M.OutcomeOf(id) is not null);
        var ran = await peer.Wait(e => Ev(e, "command") && Str(e, "id") == id);
        Check(sent is null && c.M.OutcomeOf(id) == PeerOutcome.Applied && ran is not null && id == "s1-11aa22bb"
              && peer.Events.Count(e => Ev(e, "command") && Str(e, "id") == id) == 1,
            $"peerlink scenarios: {name} — a command with its request id runs once at the peer and reads applied ({c.M.OutcomeOf(id)}) {peer.Faults}");
        await c.Ping(T0 + 20, T0 + 20);
        await c.Pump(T0 + 30, () => c.M.Clock.Samples == 1);
        Check(c.M.Clock.Believed is { RttMs: 10, ErrMs: 6 } b && Math.Abs(b.OffsetMs) < 0.001 && c.M.CanSchedule(T0 + 30, 6) == schedules,
            $"peerlink scenarios: {name} — a matched pong is a clock sample (rtt 10, errMs 6, offset 0){(schedules ? " fit for a timed command" : "")} ({c.M.Clock.Samples} samples)");
    }

    /// <summary>Stagewatch's core dialling as a connector to Hall A, holding <paramref name="secret"/> (a key, or a code).</summary>
    static CoreConnector Connector(PeerCarrier carrier, string secret, bool code, string kind, string id, int port, PeerKept? kept = null,
        bool local = false, bool linesOverWs = false, Func<double>? jitter = null, long? timed = null)
    {
        var k = code ? PeerLink.CodeK(PeerLink.Canon(secret)!) : PeerLink.KeyK(secret);
        return ConnectorWith(carrier, k, code ? "code" : PeerLink.Kid(k), code ? secret : null, kind, id, port, kept, local, linesOverWs, jitter, timed);
    }

    /// <summary>
    /// Stagewatch's core dialling as a connector to Hall A, proving with <paramref name="k"/> under <paramref name="kid"/> (an enrolled
    /// key's, say); <paramref name="code"/> is the code it answers a level-0 NONCE with, if any. Its j is 0.5 unless
    /// <paramref name="jitter"/> gives others; <paramref name="timed"/> is the tolerance of the timed commands it schedules.
    /// </summary>
    static CoreConnector ConnectorWith(PeerCarrier carrier, byte[] k, string kid, string? code, string kind, string id, int port, PeerKept? kept = null,
        bool local = false, bool linesOverWs = false, Func<double>? jitter = null, long? timed = null)
    {
        var settings = new PeerLinkSettings
        {
            Carrier = carrier, Key = kid, Name = "Hall A", Kind = kind, NodeId = id, Address = "127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture),
            Local = local, ShowNeeds = true, Inst = "11aa22bb", TimedToleranceMs = timed,
        };
        var c = new CoreConnector(new PeerLinkConnector(settings, kept, T0, jitter ?? (() => 0.5)), carrier, k, kid, code, linesOverWs) { Port = port };
        Every.Add(c);
        return c;
    }

    static string Kid(string key) => PeerLink.Kid(PeerLink.KeyK(key));

    /// <summary>A port nothing listens on: a dial to it fails to connect.</summary>
    static int ClosedPort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>The secrets a scenario held, and what is made from them: each a canary that no wire in clear and no stdout may carry.</summary>
    static List<string> Canaries(params string[] secrets)
    {
        var all = new List<string>();
        foreach (var s in secrets)
        {
            all.Add(s);
            all.Add(Convert.ToHexStringLower(PeerLink.KeyK(s)));
            if (PeerLink.Canon(s) is { } c)
            {
                all.Add(c);
                all.Add(Convert.ToHexStringLower(PeerLink.CodeK(c)));
            }
        }
        return all;
    }

    static string? Found(IEnumerable<string> texts, IEnumerable<string> canaries)
        => texts.SelectMany(t => canaries.Where(c => t.Contains(c, StringComparison.Ordinal))).FirstOrDefault();

    /// <summary>The peer's stdout, every event as it printed it.</summary>
    static IEnumerable<string> Stdout(ScriptedPeer peer) => peer.Events.Select(e => e.GetRawText());

    // ------------------------------------------------------------------ the peer's own rules

    static async Task PeerRules(Action<bool, string> Check, string script, Material kit)
    {
        using (var withArgs = ScriptedPeer.Node(script, "--key", Swk))
        {
            var stderr = await withArgs.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(5000);
            await withArgs.WaitForExitAsync(cts.Token);
            Check(withArgs.ExitCode == 2 && stderr.Contains("takes no arguments", StringComparison.Ordinal),
                $"peerlink scenarios: the peer takes no arguments, so no key rides argv (exit {withArgs.ExitCode}: {stderr.Trim()})");
        }
        await using var peer = Peer(script);
        var ready = await peer.Wait(e => Ev(e, "ready"));
        Check(ready is { } r && Int(r, "peerlink") == PeerLink.Version, "peerlink scenarios: the peer says it is ready, speaking PeerLink " + PeerLink.Version);
        var open = kit.Files.Write("open.secret", Swk, open: true);
        var e = await peer.Ask(new { @do = "listen", end = "open", carrier = "ws", tls = "plain", node = Main, keys = new[] { new { file = open, kind = "key" } } });
        if (OperatingSystem.IsWindows())
            Check(Ev(e, "listening"), "peerlink scenarios: on Windows the peer reads no mode — the file's own ACL keeps it, 13.20's named exemption — and takes the key file");
        else
            Check(Ev(e, "error") && Str(e, "words") is { } w && w.Contains("0600", StringComparison.Ordinal),
                $"peerlink scenarios: the peer refuses a key file others can read — keys and certificates come from 0600 files ({e.GetRawText()})");
        var unknown = await peer.Ask(new { @do = "trust", everything = true });
        Check(Ev(unknown, "error") && Says(unknown, "unknown command"), "peerlink scenarios: the peer refuses a command it does not know, and defaults nothing");
        var bad = await peer.Ask(new { @do = "dial", carrier = "line", port = 1, tls = "yes", node = Main });
        Check(Ev(bad, "error"), "peerlink scenarios: the peer refuses a field it cannot read (tls \"yes\"), and defaults nothing");
        var extra = await peer.Ask(new { @do = "listen", end = "extra", carrier = "ws", tls = "plain", node = Main, trust = true });
        Check(Ev(extra, "error") && Str(extra, "words") == "listen: unknown field trust", "peerlink scenarios: the peer refuses a field it does not know");
        var lid = await peer.Ask(new { @do = "expect", match = new { ev = "link" }, within = 5000 });
        Check(Ev(lid, "expect") && False(lid, "ok"), "peerlink scenarios: expect answers within 2 s whatever it asks for, and says what it did not see");

        // A shape a field does not take is refused, never coerced: a host that is not one address to bind (a number would be taken
        // as a backlog and bind every interface), every interface itself, an end that is not a name, a legacy that is not a boolean.
        var numberHost = await peer.Ask(new { @do = "listen", end = "h1", carrier = "line", tls = "plain", node = Main, host = 5 });
        var everyHost = await peer.Ask(new { @do = "listen", end = "h2", carrier = "line", tls = "plain", node = Main, host = "0.0.0.0" });
        var nameHost = await peer.Ask(new { @do = "listen", end = "h3", carrier = "line", tls = "plain", node = Main, host = "localhost" });
        Check(new[] { numberHost, everyHost, nameHost }.All(x => Ev(x, "error") && Says(x, "listen: host is the one IP address")),
            "peerlink scenarios: the peer binds one address it is named (13.3.5) — never a number, a name or every interface");
        var numberEnd = await peer.Ask(new { @do = "listen", end = 5, carrier = "line", tls = "plain", node = Main });
        Check(Ev(numberEnd, "error") && Says(numberEnd, "listen: end is a name"), $"peerlink scenarios: the peer refuses an end that is not a name, before asking whether it is open ({numberEnd.GetRawText()})");
        var legacyYes = await peer.Ask(new { @do = "dial", carrier = "line", port = 1, tls = false, node = Main, legacy = "yes" });
        Check(Ev(legacyYes, "error") && Says(legacyYes, "dial: legacy is true or false"),
            $"peerlink scenarios: the peer refuses a legacy that is not a boolean, and dials nothing ({legacyYes.GetRawText()})");
        // A verdict names stuckMs with stuck and expireMs with expire, each within its §14 bounds; a feature is named once; a level-0
        // acceptor gives no verdict, so it lists none (13.6.6).
        var noStuckMs = await peer.Ask(new { @do = "listen", end = "f1", carrier = "line", tls = "plain", node = Main, can = new[] { "stuck" } });
        var stuckLong = await peer.Ask(new { @do = "listen", end = "f2", carrier = "line", tls = "plain", node = Main, can = new[] { "stuck" }, stuckMs = 10_001 });
        var expireAlone = await peer.Ask(new { @do = "listen", end = "f3", carrier = "line", tls = "plain", node = Main, expireMs = 2000 });
        var twice = await peer.Ask(new { @do = "listen", end = "f4", carrier = "line", tls = "plain", node = Main, can = new[] { "final", "final" } });
        var levelZero = await peer.Ask(new { @do = "listen", end = "f5", carrier = "line", tls = "plain", node = Main, level = 0, can = new[] { "receipts" } });
        Check(Says(noStuckMs, "listen: stuckMs") && Says(stuckLong, "listen: stuckMs") && Says(expireAlone, "listen: expireMs") && Says(twice, "listen: can lists")
              && Says(levelZero, "listen: an acceptor that does not speak LINK"),
            "peerlink scenarios: the peer's acceptor names stuckMs with stuck and expireMs with expire, within §14's bounds, each feature once, and none at level 0 (13.6.6)");
        // An attack is a closed set of modes, each of one shape: anything else is refused, never defaulted.
        var noMode = await peer.Ask(new { @do = "dial", carrier = "line", port = 1, tls = false, node = Main, attack = new { everything = true } });
        var badShape = await peer.Ask(new { @do = "dial", carrier = "line", port = 1, tls = false, node = Main, attack = new { reads = true } });
        var noKey = await peer.Ask(new { @do = "dial", carrier = "line", port = 1, tls = false, node = Main, attack = new { role = true } });
        var badVerdict = await peer.Ask(new { @do = "listen", end = "f6", carrier = "line", tls = "plain", node = Main, attack = new { verdict = "forge" } });
        Check(Says(noMode, "dial: attack has no mode everything") && Says(badShape, "dial: attack reads does not take true") && Says(noKey, "dial: an attack on LINK names the key file")
              && Says(badVerdict, "listen: attack verdict does not take"),
            $"peerlink scenarios: the peer refuses an attack mode it does not have, a mode's value of another shape, and an attack on LINK with no key ({noMode.GetRawText()})");
        var plainEnd = await peer.Ask(new { @do = "listen", end = "g", carrier = "line", tls = "plain", node = Main });
        var badSet = await peer.Ask(new { @do = "set", end = "g", clock = 5, answer = "maybe" });
        var finalWithout = await peer.Ask(new { @do = "set", end = "g", final = "applied" });
        var revokeNone = await peer.Ask(new { @do = "set", end = "g", revoke = "0123456789abcdef" });
        Check(Ev(plainEnd, "listening") && Says(badSet, "set: answer is") && Says(finalWithout, "set: final is applied or refused, on an end whose verdict lists final")
              && Says(revokeNone, "set: revoke names the kid of a key this end holds"),
            "peerlink scenarios: the peer's set checks every field before it applies any, sends no final its verdict does not list, and revokes only a key it holds");

        await Framing(Check, script);
        await OpenNode(Check, script, kit);
        await LocalByAddress(Check, script, kit);
        await ReplayControl(Check, script, kit);
    }

    /// <summary>
    /// S8's control: the peer's replay is a true one. Against the peer's own acceptor offering one snonce on every connection — the
    /// flaw S8 is there to find — a LINK replayed as it was sent, byte for byte, links: so where S8's replay is refused, it is the
    /// acceptor's fresh snonce that refuses it. A replay names a connection of this peer's that sent its LINK, from its node.
    /// </summary>
    static async Task ReplayControl(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in new[] { Line, Ws })
        {
            var name = "S8's control, " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, With(ListenAs(leg, kit.A, [Key(kit, leg.Secret)]), new { attack = new { snonce = "00112233445566778899aabbccddeeff" } }));
            var client = leg.Carrier == PeerCarrier.Line ? Main : Patterns;
            var first = await peer.Ask(new { @do = "dial", carrier = leg.Wire, port, tls = true, node = client, key = Key(kit, leg.Secret), meant = leg.Node });
            var conn1 = Int(first, "conn") ?? -1;
            var honest = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector" && Int(e, "conn") == conn1);
            var again = await peer.Ask(new { @do = "dial", carrier = leg.Wire, port, tls = true, node = client, key = Key(kit, leg.Secret), meant = leg.Node, attack = new { replay = conn1 } });
            var conn2 = Int(again, "conn") ?? -1;
            var replayed = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector" && Int(e, "conn") == conn2);
            Check(honest is { } h && replayed is { } r && Str(r, "lid") == Str(h, "lid") && LinkSent(peer, conn2).SequenceEqual(LinkSent(peer, conn1))
                  && peer.Events.Count(e => Ev(e, "link") && Str(e, "role") == "acceptor") == 2,
                $"peerlink scenarios: {name} — at an acceptor that offers its snonce again, a LINK replayed byte for byte links, one lid at both: the replay S8 sends is a true one ({string.Join(" | ", LinkSent(peer, conn2))}) {peer.Faults}");
            var none = await peer.Ask(new { @do = "dial", carrier = leg.Wire, port, tls = true, node = client, key = Key(kit, leg.Secret), attack = new { replay = 999 } });
            var other = await peer.Ask(new { @do = "dial", carrier = leg.Wire, port, tls = true, node = new { t = "main", id = "0f0f0f0f0f0f" }, key = Key(kit, leg.Secret), attack = new { replay = conn1 } });
            Check(Says(none, "dial: replay names a connection this peer dialled that sent its LINK") && Says(other, "dial: a replayed LINK goes as it was sent"),
                $"peerlink scenarios: {name} — a replay names a connection of the peer's that sent its LINK, and goes from its node ({none.GetRawText()}; {other.GetRawText()})");
        }
    }

    /// <summary>One of this machine's IPv4 addresses that is not loopback: a socket to it is not local by 13.1's rule; null when it has none.</summary>
    static System.Net.IPAddress? Lan()
        => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(u => u.Address)
            .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a));

    /// <summary>
    /// 13.1 and 13.3.2: whether a keyed <c>ws</c> dial without TLS is local is the socket's remote address, never a name. Dialled at one
    /// of this machine's own LAN addresses — not loopback, though the machine is the same — the peer sends nothing and says why.
    /// </summary>
    static async Task LocalByAddress(Action<bool, string> Check, string script, Material kit)
    {
        var lan = Lan();
        Check(lan is not null, "peerlink scenarios: this machine has a LAN address that is not loopback, to dial the peer's locality rule at");
        if (lan is null) return;
        await using var server = new RawServer((_, _) => Task.CompletedTask, lan);
        await using var peer = Peer(script);
        var dial = await peer.Ask(new { @do = "dial", carrier = "ws", host = lan.ToString(), port = server.Port, tls = false, node = Main, key = Key(kit, Swk) });
        var conn = Int(dial, "conn") ?? -1;
        var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
        await Settle(peer);
        Check(Ev(dial, "connected") && Str(dial, "remote") == lan.ToString() && refused is { } r && Str(r, "class") == "no-tls" && Says(r, lan + " offers no TLS")
              && peer.On("wrote", conn).Count == 0 && server.Received == 0,
            $"peerlink scenarios: a keyed ws dial without TLS to this machine's own LAN address is not local — the remote address decides, so nothing is sent ({refused?.GetRawText()}; {server.Received} bytes)");
    }

    /// <summary>
    /// The peer's framing stays in step past what it will not read whole (13.14): as an acceptor, a line past link.command.bytes is
    /// answered ERR … — closed and nothing after it is run; as a connector, a line or a ws message past link.read.min.bytes is reported
    /// oversize and skipped to its end, its tail never read as a line or a frame of its own, and the next one read whole.
    /// </summary>
    static async Task Framing(Action<bool, string> Check, string script)
    {
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "framing, acceptor", new { @do = "listen", end = "a", carrier = "line", tls = "plain", node = Patterns });
            var heard = new List<string>();
            using (var tcp = await Kit.Connect(port))
            {
                var stream = tcp?.GetStream() ?? Stream.Null;
                await stream.WriteAsync(Encoding.ASCII.GetBytes("@big " + new string('A', 70_000) + "\n@after NOTE after\n"));
                using var reader = new StreamReader(stream, new UTF8Encoding(false));
                using var cts = new CancellationTokenSource(3000);
                try
                {
                    while (await reader.ReadLineAsync(cts.Token) is { } line) heard.Add(line);
                }
                catch (Exception x) when (x is OperationCanceledException or IOException)
                {
                    heard.Add("(" + x.GetType().Name + ")");
                }
            }
            var conn = await Taken(peer);
            var closed = await peer.Wait(x => Ev(x, "closed") && Int(x, "conn") == conn);
            Check(heard.Contains("ERR @big a line longer than 65536 bytes — closed") && !heard.Any(l => l.Contains("@after", StringComparison.Ordinal))
                  && closed is { } y && Str(y, "by") == "peer" && peer.On("oversize", conn).Count == 1 && !peer.Events.Any(x => Ev(x, "command")),
                $"peerlink scenarios: the peer as an acceptor answers a line past link.command.bytes ERR … — closed, ends the line and runs nothing after it ({string.Join(" | ", heard)})");
        }
        {
            await using var server = new RawServer(async (stream, ct) =>
                await stream.WriteAsync(Encoding.ASCII.GetBytes(new string('B', 270_000) + "TAIL\nSTATE {\"rev\":2}\n"), ct));
            await using var peer = Peer(script);
            var dial = await peer.Ask(new { @do = "dial", carrier = "line", port = server.Port, tls = false, node = Main });
            var conn = Int(dial, "conn") ?? -1;
            var next = await peer.Wait(x => Ev(x, "read") && Int(x, "conn") == conn && Str(x, "line") == "STATE {\"rev\":2}");
            Check(next is not null && peer.On("oversize", conn).Count == 1 && peer.ReadOn(conn).SequenceEqual(["STATE {\"rev\":2}"])
                  && peer.On("closed", conn).Count == 0,
                $"peerlink scenarios: the peer as a connector reports a line past link.read.min.bytes oversize, skips it to its LF, reads the next whole, and stays up ({string.Join(" | ", peer.ReadOn(conn).Select(l => l.Length > 40 ? l[..40] + "…" : l))})");
        }
        {
            await using var server = new RawServer(async (stream, ct) =>
            {
                if (!await RawServer.Upgrade(stream, ct)) return;
                // A frame that declares 300000 bytes in a 64-bit length, its payload full of bytes that would read as frame heads.
                var payload = Enumerable.Repeat((byte)0x81, 300_000).ToArray();
                await stream.WriteAsync(RawServer.TextFrame(payload.AsSpan(0, 1000), payload.Length), ct);
                await stream.FlushAsync(ct);
                await Task.Delay(50, ct);
                await stream.WriteAsync(payload.AsMemory(1000), ct);
                await stream.WriteAsync(RawServer.TextFrame("{\"type\":\"state\",\"seq\":7}"u8), ct);
            });
            await using var peer = Peer(script);
            var dial = await peer.Ask(new { @do = "dial", carrier = "ws", port = server.Port, tls = false, node = Main });
            var conn = Int(dial, "conn") ?? -1;
            var next = await peer.Wait(x => Ev(x, "read") && Int(x, "conn") == conn);
            Check(next is { } n && Str(n, "message") == "{\"type\":\"state\",\"seq\":7}" && peer.On("oversize", conn) is [var o] && Int(o, "bytes") == 300_000
                  && peer.ReadOn(conn).Count == 1 && peer.On("closed", conn).Count == 0,
                $"peerlink scenarios: the peer as a ws connector skips a message past link.read.min.bytes byte for byte — reported oversize with its size — and reads the next frame whole ({string.Join(" | ", peer.ReadOn(conn))})");
        }
    }

    /// <summary>
    /// An open node (13.6.7, §7.2): the peer listening with no key greets a line with STATE, not NONCE, and answers LINK in 13.6.7's
    /// words; the core, dialling it with a code, links unverified with the flag <c>open</c>, amber while it holds a key.
    /// </summary>
    static async Task OpenNode(Action<bool, string> Check, string script, Material kit)
    {
        await using var peer = Peer(script);
        var port = await Listen(Check, peer, "an open node", new { @do = "listen", end = "a", carrier = "line", tls = "plain", node = Patterns });
        await using var c = Connector(PeerCarrier.Line, CodeTyped, true, "patterns", PatternsId, port, local: true);
        await c.Dial(T0);
        var up = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Live);
        var rec = c.M.Record(T0);
        // The core tried TLS first on a socket of its own; the link's connection is the one that heard LINK.
        var conn = await peer.Wait(x => Ev(x, "open")) is { } said ? Int(said, "conn") ?? -1 : -1;
        var wrote = peer.On("wrote", conn).Select(x => Str(x, "line") ?? "").ToList();
        Check(up && rec is { State: "live", Light: "amber", Proof: "open" } && rec.Flags.Contains("open") && c.Credentials.Count == 0
              && wrote.FirstOrDefault()?.StartsWith("STATE ", StringComparison.Ordinal) == true && !wrote.Any(l => l.StartsWith("NONCE", StringComparison.Ordinal))
              && wrote.Contains("ERR @l1 LINK open — this node asks for no key") && peer.On("open", conn).Count == 1,
            $"peerlink scenarios: an open node greets with STATE and answers LINK open in 13.6.7's words; the core links with the flag open, amber, and proves nothing ({rec.ToJson()}; {string.Join(" | ", wrote)}) {peer.Faults}");
        await Settle(peer);
        Check(Found(Stdout(peer), Canaries(Code, CodeTyped)) is null, "peerlink scenarios: an open node — no code or K on the peer's stdout");
    }

    // ------------------------------------------------------------------ S1: an honest link, each carrier and role

    static async Task S1(Action<bool, string> Check, string script, Material kit)
    {
        // line over TLS: the core dials Hall A as Patterns, holding the key Patterns issued it.
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S1 line TLS", new
            {
                @do = "listen", end = "a", carrier = "line", tls = "sniff", cert = kit.A.CertFile, certKey = kit.A.KeyFile, node = Patterns,
                keys = new[] { Key(kit, Ptk) }, inst = "a41f09c2", clock = T0 + 25,
            });
            await using var c = Connector(PeerCarrier.Line, Ptk, false, "patterns", PatternsId, port);
            var scheme = await c.Dial(T0);
            var live = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Live);
            var rec = c.M.Record(T0);
            Check(scheme == "tls" && live && rec is { State: "live", Light: "green", Mark: "✓", Tls: "pinned", Proof: "mutual", Level: 1, Scope: "post", Refusal: "" }
                  && rec.Flags.Count == 0 && rec.Kid == Kid(Ptk) && rec.PeerId == PatternsId && rec.PeerT == "patterns",
                $"peerlink scenarios: S1 line over TLS — the core's link to the peer is live, green, pinned, mutual, level 1 ({rec.ToJson()}) {peer.Faults}");
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "acceptor");
            Check(link is { } l && c.T is { } t && Str(l, "lid") == PeerLink.Lid(t) && Str(l, "verified") == "key" && Str(l, "kid") == Kid(Ptk),
                "peerlink scenarios: S1 line over TLS — the peer verified the core's proof, and both ends hold one lid");
            Check(c.M.Kept is { Pin: { Trust: PinTrust.Proof } pin, Floor: { Tls: true, Level: 1, Proof: "mutual" } } && pin.Fingerprint == kit.A.Fp && pin.NodeId == PatternsId,
                "peerlink scenarios: S1 line over TLS — the certificate the proof verified over is pinned for the node, and its floor is TLS, level 1, mutual");
            Check(c.Credentials.SetEquals(["proof"]) && c.Unmade.Count == 0,
                $"peerlink scenarios: S1 line over TLS — the core put a proof on the wire and no other credential ({string.Join(", ", c.Credentials)})");
            await CommandAndPong(Check, "S1 line over TLS", c, peer, schedules: true);
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Ptk)) is null, "peerlink scenarios: S1 — no key or K on the peer's stdout");
        }

        // line, plain on this machine, a code typed with spaces: the core tries TLS, finds none on offer, and proves on plain.
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S1 line plain", new
            {
                @do = "listen", end = "a", carrier = "line", tls = "plain", node = Patterns, keys = new[] { CodeOf(kit, Code) }, clock = T0 + 25,
            });
            await using var c = Connector(PeerCarrier.Line, CodeTyped, true, "patterns", PatternsId, port, local: true);
            var scheme = await c.Dial(T0);
            var live = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Live);
            var rec = c.M.Record(T0);
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "acceptor");
            Check(scheme == "tls" && live && c.Sockets == 2 && rec is { State: "live", Light: "green", Tls: "local", Proof: "mutual", Kid: "code", Scope: "operate" }
                  && rec.Flags.Count == 0 && link is { } l && Str(l, "verified") == "code",
                $"peerlink scenarios: S1 line plain on this machine — no TLS on offer, the core proves the code on plain: live, green, local, no plain flag ({rec.ToJson()}) {peer.Faults}");
            Check(c.Credentials.SetEquals(["proof"]) && !c.Wrote.Any(w => w.Contains("AUTH", StringComparison.Ordinal)),
                "peerlink scenarios: S1 line plain — the NONCE greeting is ignored once LINK answers: no AUTH-PROOF, no AUTH");
            await CommandAndPong(Check, "S1 line plain on this machine", c, peer, schedules: true);
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Code, CodeTyped)) is null, "peerlink scenarios: S1 — no code or K on the peer's stdout");
        }

        // line over playmap.lines.v1, TLS only: the core dials Hall A as PlayMap.
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S1 playmap.lines.v1", new
            {
                @do = "listen", end = "a", carrier = "line", transport = "ws", tls = "only", cert = kit.A.CertFile, certKey = kit.A.KeyFile, node = PlayMap,
                keys = new[] { Key(kit, Pmk) }, clock = T0 + 25,
            });
            await using var c = Connector(PeerCarrier.Line, Pmk, false, "playmap", PlayMapId, port, linesOverWs: true);
            await c.Dial(T0);
            var live = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Live);
            var rec = c.M.Record(T0);
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "acceptor");
            Check(live && rec is { State: "live", Light: "green", Tls: "pinned", Proof: "mutual", Carrier: "line" } && link is { } l && c.T is { } t && Str(l, "lid") == PeerLink.Lid(t)
                  && c.M.Kept is { Pin: { Trust: PinTrust.Proof } pin } && pin.Fingerprint == kit.A.Fp,
                $"peerlink scenarios: S1 line over playmap.lines.v1 — one line a frame, verified both ways, one lid, pinned by the proof ({rec.ToJson()}) {peer.Faults}");
            await CommandAndPong(Check, "S1 line over playmap.lines.v1", c, peer, schedules: true);
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Pmk)) is null, "peerlink scenarios: S1 playmap.lines.v1 — no key or K on the peer's stdout");
        }

        // ws: the core dials Hall A as a Stagewatch's node door, then makes a side call with S.
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S1 ws", new
            {
                @do = "listen", end = "a", carrier = "ws", tls = "sniff", cert = kit.A.CertFile, certKey = kit.A.KeyFile, node = Main, keys = new[] { Key(kit, Swk) },
                clock = T0 + 25,
            });
            await using var c = Connector(PeerCarrier.Ws, Swk, false, "main", MainId, port);
            await c.Dial(T0);
            var live = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Live && c.M.Show == PeerShow.Live);
            var rec = c.M.Record(T0);
            var upgrade = await peer.Wait(e => Ev(e, "upgrade"));
            Check(live && rec is { State: "live", Light: "green", Tls: "pinned", Proof: "mutual", Carrier: "ws", Show: "live" }
                  && upgrade is { } u && Str(u, "authorization") == "none" && u.TryGetProperty("query", out var q) && Str(q, "link") == "1",
                $"peerlink scenarios: S1 ws — the core opens /ws?node&t&link=1 with no Authorization, proves, and is live ({rec.ToJson()}) {peer.Faults}");
            var status = await c.SideCall(T0 + 10, "/api/context");
            var served = await peer.Wait(e => Ev(e, "served"));
            Check(status == 200 && served is { } sv && Int(sv, "status") == 200 && Str(sv, "authorization") == "showbus" && c.M.State(T0 + 10) == PeerLinkState.Live,
                "peerlink scenarios: S1 ws — a side call carries S in Authorization: ShowBus, only over the pinned TLS, and the peer takes it");
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "acceptor");
            Check(link is { } l && c.T is { } t && Str(l, "lid") == PeerLink.Lid(t) && c.M.Kept is { Pin: { Trust: PinTrust.Proof } pin } && pin.Fingerprint == kit.A.Fp,
                "peerlink scenarios: S1 ws — one lid at both ends, and the certificate the proof verified over pinned");
            await CommandAndPong(Check, "S1 ws", c, peer, schedules: true);
            await Settle(peer);
            Check(Found(Stdout(peer), Canaries(Swk).Append(PeerLink.Session(PeerLink.KeyK(Swk), c.T!))) is null,
                "peerlink scenarios: S1 ws — no key, K or S on the peer's stdout");
        }

        // ws, the peer connecting: its connector end against the core's parsers, proofs and session check, behind the kit's stand-in
        // acceptor. The acceptor's own half of S1 (a command run and answered, a ping answered with a pong) is the /ws acceptor's: SW22.1a.
        {
            await using var acceptor = Stand(PeerCarrier.Ws, kit.B, [(PeerLink.KeyK(Swk), Kid(Swk), "key")]);
            await using var peer = Peer(script);
            var dial = await peer.Ask(new { @do = "dial", carrier = "ws", port = acceptor.Port, tls = true, node = Patterns, key = Key(kit, Swk), meant = Main });
            var conn = Int(dial, "conn") ?? -1;
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector");
            Check(Ev(dial, "connected") && Str(dial, "fingerprint") == kit.B.Fp && link is { } l && acceptor.Entries.Contains("link " + Str(l, "lid"))
                  && Str(l, "verified") == "key",
                $"peerlink scenarios: S1 ws, the peer connecting — its proof verifies by the core's ClientProof, the core's ServerProof verifies at the peer, one lid (the kit's stand-in acceptor: {string.Join("; ", acceptor.Entries)}) {peer.Faults}");
            var answer = await peer.Ask(new { @do = "call", conn, path = "/api/context" });
            Check(Ev(answer, "answer") && Int(answer, "status") == 200 && acceptor.Entries.Contains("served 200 ShowBus"),
                $"peerlink scenarios: S1 ws, the peer connecting — its side call carries the S the core's Session makes, and the core's CheckSession takes it ({answer.GetRawText()})");
        }

        // line, the peer connecting over TLS with a code: its connector end against the core's line parser and proofs (a line acceptor
        // is no role of Stagewatch's; the stand-in is the kit's).
        {
            await using var acceptor = Stand(PeerCarrier.Line, kit.B, [(PeerLink.CodeK(PeerLink.Canon(Code)!), "code", "code")]);
            await using var peer = Peer(script);
            await peer.Ask(new { @do = "dial", carrier = "line", port = acceptor.Port, tls = true, node = Patterns, key = CodeOf(kit, CodeTyped), meant = Main });
            var link = await peer.Wait(e => Ev(e, "link") && Str(e, "role") == "connector");
            Check(link is { } l && acceptor.Entries.Contains("link " + Str(l, "lid")) && Str(l, "verified") == "code" && Str(l, "scope") == "operate",
                $"peerlink scenarios: S1 line, the peer connecting — LINK over TLS with a code: its proof verifies by the core's, the core's at the peer ({string.Join("; ", acceptor.Entries)}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S2 and S3: a wrong key, an unknown kid

    static async Task S2AndS3(Action<bool, string> Check, string script, Material kit)
    {
        // line, over TCP and TLS and over playmap.lines.v1, the core connecting: S2 holds the wrong code, S3 a key the peer does not
        // know. The same words, the line closed, the key held 60 s.
        foreach (var leg in new[] { Line, LinesV1 })
        {
            var lineRefusals = new List<string>();
            var lineWords = new List<string>();
            foreach (var (s, secret, code) in new[] { ("S2", WrongCode, true), ("S3", Stranger, false) })
            {
                var name = s + " " + leg.Label;
                await using var peer = Peer(script);
                var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [CodeOf(kit, Code), Key(kit, leg.Secret)]));
                await using var c = Connector(PeerCarrier.Line, secret, code, leg.Kind, leg.Id, ClosedPort(), linesOverWs: leg.OverWs);
                await c.Dial(T0);
                var before = c.M.Attempt;
                c.Port = port;
                const long At = T0 + 600;
                var scheme = await c.Dial(At);
                var held = await c.Pump(At, () => c.M.State(At) == PeerLinkState.Held);
                var rec = c.M.Record(At);
                var conn = await Taken(peer);
                var refusal = await Wrote(peer, conn, "ERR ");
                lineRefusals.Add(refusal);
                lineWords.Add(rec.Words);
                Check(before == 1 && scheme == "tls" && held && rec is { State: "held", Refusal: "key", Light: "red", Mark: "✕", Attempt: 2 }
                      && c.M.HeldUntilWall(At) == At + PeerLink.HoldRefusedMs,
                    $"peerlink scenarios: {name} — refused, the key held 60 s, red as the show needs it, the attempt not reset (1 then 2) ({rec.ToJson()}) {peer.Faults}");
                Check(refusal == "ERR @l2 LINK refused — " + PeerLink.ProofWords && await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == conn) is { } closed
                      && Str(closed, "by") == "peer" && !c.M.KeepsConnection(At),
                    $"peerlink scenarios: {name} — the peer refuses in 13.6.3's words and ends the line, and the core ends its side ({refusal})");
                Check(c.M.DialDue(At + PeerLink.HoldRefusedMs - 1) is null && c.M.DialDue(At + PeerLink.HoldRefusedMs) == "tls" && c.M.Kept is { Pin: null, Floor: null },
                    $"peerlink scenarios: {name} — no dial while the hold runs, one at its end, and nothing pinned or floored");
                var verdict = await peer.Wait(e => Ev(e, "refused") && Str(e, "role") == "acceptor");
                Check(verdict is { } v && (s == "S2" ? True(v, "known") : False(v, "known")),
                    $"peerlink scenarios: {name} — the peer knew the kid in S2 and did not in S3, and said the same to both");
            }
            Check(lineRefusals.Count == 2 && lineRefusals[0] == lineRefusals[1] && lineWords[0] == lineWords[1],
                $"peerlink scenarios: S3 {leg.Label} — an unknown kid is refused in S2's words, byte for byte, and the core's record says the same ({string.Join(" | ", lineRefusals)})");
        }

        // ws, the core connecting: refused, held 60 s, the socket kept as a public viewer; a fresh link= socket when the hold ends.
        var wsVerdicts = new List<string>();
        foreach (var (s, secret, code) in new[] { ("S2", WrongCode, true), ("S3", Stranger, false) })
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, s + " ws", new
            {
                @do = "listen", end = "a", carrier = "ws", tls = "sniff", cert = kit.A.CertFile, certKey = kit.A.KeyFile, node = Main,
                keys = new[] { CodeOf(kit, Code), Key(kit, Swk) },
            });
            await using var c = Connector(PeerCarrier.Ws, secret, code, "main", MainId, port);
            await c.Dial(T0);
            var viewing = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Held && c.M.Show == PeerShow.Live);
            var rec = c.M.Record(T0);
            var conn = await Taken(peer);
            wsVerdicts.Add(await Wrote(peer, conn, "{\"type\":\"auth\""));
            Check(viewing && rec is { State: "held", Refusal: "key", Light: "red" } && rec.Flags.Contains("keyless") && c.M.Follows(T0) && c.M.KeepsConnection(T0),
                $"peerlink scenarios: {s} ws — refused and held, the socket kept as a public viewer whose frames are read ({rec.ToJson()}) {peer.Faults}");
            var silent = await peer.Ask(new { @do = "silence", match = new { ev = "closed", conn }, ms = 300, after = 0 });
            Check(True(silent), $"peerlink scenarios: {s} ws — no socket closes for a key's sake");
            Check(!c.M.KeepsConnection(T0 + PeerLink.HoldRefusedMs) && c.M.DialDue(T0 + PeerLink.HoldRefusedMs) == "tls",
                $"peerlink scenarios: {s} ws — when the hold ends the viewer ends and a fresh link= socket is dialled at once");
        }
        Check(wsVerdicts.Count == 2 && wsVerdicts[0].Length > 0 && wsVerdicts[0] == wsVerdicts[1]
              && wsVerdicts[0] == "{\"type\":\"auth\",\"ok\":false,\"error\":\"" + PeerLink.ProofWords + "\",\"class\":\"refused\",\"retry\":60}",
            $"peerlink scenarios: S3 ws — an unknown kid's verdict is S2's, byte for byte ({string.Join(" | ", wsVerdicts)})");

        // The peer connecting, on ws and on line, to the kit's stand-in acceptor: it proves a wrong code (S2) and an unknown key (S3), and
        // the core's proof check refuses both. What these hold is the peer's connector end: it reads a refusal as it came, keeps a ws
        // socket as a public viewer, and is ended on a line. The acceptor's half — its words, its refusal, and the wrong-proof table that
        // counts per (address, kid) and leaves an unknown kid uncounted — is the /ws acceptor's: SW22.1a.
        var acceptorWords = new List<string>();
        foreach (var (s, secret, code) in new[] { ("S2", WrongCode, true), ("S3", Stranger, false) })
        {
            object held = code ? CodeOf(kit, secret) : Key(kit, secret);
            await using (var acceptor = Stand(PeerCarrier.Ws, kit.B, [(PeerLink.CodeK(PeerLink.Canon(Code)!), "code", "code"), (PeerLink.KeyK(Swk), Kid(Swk), "key")]))
            await using (var peer = Peer(script))
            {
                var dial = await peer.Ask(new { @do = "dial", carrier = "ws", port = acceptor.Port, tls = true, node = Patterns, key = held, meant = Main });
                var conn = Int(dial, "conn") ?? -1;
                var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
                var frame = await peer.Wait(e => Ev(e, "read") && Int(e, "conn") == conn && (Str(e, "message") ?? "").Contains("\"public\":true", StringComparison.Ordinal));
                var silent = await peer.Ask(new { @do = "silence", match = new { ev = "closed", conn }, ms = 300 });
                acceptorWords.Add(refused is { } r ? Str(r, "words") ?? "" : "");
                Check(refused is { } x && Str(x, "class") == "refused" && Str(x, "words") == PeerLink.ProofWords && Int(x, "retry") == 60 && frame is not null
                      && True(silent),
                    $"peerlink scenarios: {s} ws, the peer connecting — the core's proof check refuses it; the peer reads the class, words and retry as they came and keeps the socket as a public viewer ({string.Join("; ", acceptor.Entries)}) {peer.Faults}");
            }
            await using (var acceptor = Stand(PeerCarrier.Line, kit.B, [(PeerLink.CodeK(PeerLink.Canon(Code)!), "code", "code")]))
            await using (var peer = Peer(script))
            {
                var dial = await peer.Ask(new { @do = "dial", carrier = "line", port = acceptor.Port, tls = true, node = Patterns, key = held, meant = Main });
                var conn = Int(dial, "conn") ?? -1;
                var refused = await peer.Wait(e => Ev(e, "refused") && Int(e, "conn") == conn);
                var closed = await peer.Wait(e => Ev(e, "closed") && Int(e, "conn") == conn);
                Check(refused is { } x && Str(x, "class") == "refused" && Str(x, "words") == PeerLink.ProofWords && closed is { } y && Str(y, "by") == "other",
                    $"peerlink scenarios: {s} line, the peer connecting — the core's proof check refuses it; the peer reads the refusal as it came, and the other end ends the line {peer.Faults}");
            }
        }
        Check(acceptorWords.Count == 2 && acceptorWords[0] == acceptorWords[1] && acceptorWords[0] == PeerLink.ProofWords,
            "peerlink scenarios: S3, the peer connecting — it reports an unknown kid's refusal in the words it came in, the same as S2's");
    }

    // ------------------------------------------------------------------ S4: another certificate at first use

    static async Task S4(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        foreach (var rewrite in new[] { false, true })
        {
            var name = $"S4 {leg.Label}{(rewrite ? ", the offer rewritten" : "")}";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            await using var relay = rewrite
                ? new TlsRelay(kit.M, port, "tls:sha256:" + kit.A.Fp, "tls:sha256:" + kit.M.Fp)
                : new TlsRelay(kit.M, port);
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, relay.Port, linesOverWs: leg.OverWs);
            await c.Dial(T0);
            var settled = await c.Pump(T0, () => c.M.State(T0) is PeerLinkState.Down or PeerLinkState.Held);
            var rec = c.M.Record(T0);
            var canaries = Canaries(leg.Secret);
            if (c.T is { } t) canaries.Add(PeerLink.Session(PeerLink.KeyK(leg.Secret), t));
            await Settle(peer);
            var wire = peer.AllRead();
            Check(settled && c.Shown == kit.M.Fp && c.M.Kept.Pin is null && c.M.Kept.Floor is null && rec.Tls == "none",
                $"peerlink scenarios: {name} — the certificate between is pinned nowhere, and no floor is set ({rec.ToJson()}) {peer.Faults}");
            Check(Found(wire, canaries) is null && Found(Stdout(peer), canaries) is null && !c.Credentials.Contains("session") && !c.Credentials.Contains("bearer"),
                $"peerlink scenarios: {name} — no key, code, K or S crossed the wire ({Found(wire, canaries)})");
            if (!rewrite)
                Check(rec.State == "down" && rec.Words.Contains(PeerLink.ProofWords, StringComparison.Ordinal) && c.Credentials.Count == 0
                      && !wire.Any(w => w.Contains("PROOF", StringComparison.Ordinal) || w.Contains("\"proof\"", StringComparison.Ordinal)),
                    $"peerlink scenarios: {name} — the offer names the acceptor's certificate, not the one shown, so the core proves nothing and the dial fails in 13.6.3's words ({rec.Words})");
            else
            {
                var refused = await peer.Wait(e => Ev(e, "refused") && Str(e, "role") == "acceptor");
                Check(rec is { State: "held", Refusal: "key" } && c.Credentials.SetEquals(["proof"]) && refused is not null,
                    $"peerlink scenarios: {name} — the core proves over the certificate it was shown, the acceptor over its own, and the proof fails ({rec.ToJson()})");
            }
        }
    }

    // ------------------------------------------------------------------ S5: a pinned id and a forged hello

    static async Task S5(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S5 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [Key(kit, leg.Secret)]));
            var kept = new PeerKept(new PeerPin(leg.Id, kit.A.Fp, PinTrust.Proof), new PeerFloor(true, 1, "mutual"), null, null, leg.Id, leg.Kind);
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, kept, linesOverWs: leg.OverWs);
            // The forged hellos name another host as well: the core keeps no address of its own — its carrier dials the one it was
            // given — so a hello cannot move the dial here, and a note for "another address only" (13.4.2) comes with the reader
            // that keeps every address per id (SW19.3).
            c.M.Hello(T0, leg.Id, null, host: "10.9.9.9");
            var noTls = c.M.Flags(T0).Contains("note") && c.M.DialDue(T0) == "tls";
            c.M.Hello(T0 + 1, leg.Id, "sha256:" + kit.B.Fp, host: "10.9.9.9");
            var other = c.M.Flags(T0 + 1).Contains("note") && c.M.DialDue(T0 + 1) == "tls";
            var scheme = await c.Dial(T0 + 2);
            var live = await c.Pump(T0 + 2, () => c.M.State(T0 + 2) == PeerLinkState.Live);
            var rec = c.M.Record(T0 + 2);
            Check(noTls && other && scheme == "tls" && live && rec is { State: "live", Light: "green", Tls: "pinned", Refusal: "" } && rec.Flags.Contains("note")
                  && c.Shown == kit.A.Fp,
                $"peerlink scenarios: {name} — a hello with no tls, then another fingerprint: TLS dialled anyway to the pinned certificate, a note, no refusal ({rec.ToJson()}) {peer.Faults}");
        }
    }

    // ------------------------------------------------------------------ S6: CERTIFICATE CHANGED

    static async Task S6(Action<bool, string> Check, string script, Material kit)
    {
        foreach (var leg in Legs)
        {
            var name = "S6 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.B, [Key(kit, leg.Secret)]));
            var kept = new PeerKept(new PeerPin(leg.Id, kit.A.Fp, PinTrust.Proof), new PeerFloor(true, 1, "mutual"), null, null, leg.Id, leg.Kind);
            await using var c = Connector(leg.Carrier, leg.Secret, false, leg.Kind, leg.Id, port, kept, linesOverWs: leg.OverWs);
            var scheme = await c.Dial(T0);
            var rec = c.M.Record(T0);
            var silent = await peer.Ask(new { @do = "silence", match = new { ev = "read" }, ms = 300, after = 0 });
            Check(scheme == "tls" && c.EndedAtCertificate && rec is { State: "refused", Refusal: "certificate", Light: "red" }
                  && rec.Words.StartsWith("CERTIFICATE CHANGED: Hall A now shows " + kit.B.Fp[..4] + " " + kit.B.Fp[4..8], StringComparison.Ordinal),
                $"peerlink scenarios: {name} — another certificate than the pin's is CERTIFICATE CHANGED, refused, red ({rec.ToJson()}) {peer.Faults}");
            Check(True(silent) && c.Wrote.Count == 0 && c.Credentials.Count == 0 && c.Sockets == 1 && !peer.Events.Any(e => Ev(e, "upgrade")),
                $"peerlink scenarios: {name} — nothing of the connector's is sent: the connection ends at the certificate, and the peer reads nothing ({silent.GetRawText()}; wrote {c.Wrote.Count})");
            Check(c.M.DialDue(T0 + 60_000) is null && c.M.DialDue(T0 + 600_000) is null && !c.M.Follows(T0 + 600_000),
                $"peerlink scenarios: {name} — refused until a person acts: no dial, and so no probe at all (13.8.3's probe every peerlink.cert.probe.ms is a MAY that Stagewatch's carriers do not take; no unit schedules it)");
            var short7 = c.M.AcceptCertificate(kit.B.Fp[..7]);
            var other = c.M.AcceptCertificate(kit.A.Fp[..8]);
            var typed = c.M.AcceptCertificate(kit.B.Fp[..4].ToUpperInvariant() + " " + kit.B.Fp[4..8]);
            const long At = T0 + 600_001;
            var again = await c.Dial(At);
            var live = await c.Pump(At, () => c.M.State(At) == PeerLinkState.Live);
            Check(!short7 && !other && typed && again == "tls" && live && c.M.Kept.Pin is { Trust: PinTrust.Proof } pin && pin.Fingerprint == kit.B.Fp,
                $"peerlink scenarios: {name} — Accept certificate takes 8 hex of the new one as the desk shows it (not 7, not the old one's), and the next link pins it by its proof");
        }
    }

    // ------------------------------------------------------------------ S7: LINK stripped on a floored id

    static async Task S7(Action<bool, string> Check, string script, Material kit)
    {
        var floor = new PeerFloor(true, 1, "mutual");
        // line and playmap.lines.v1: a level-0 acceptor with Patterns 88.1's grammar (STATE, then NONCE; LINK an unknown command), TLS
        // as before.
        foreach (var leg in new[] { Line, LinesV1 })
        {
            var name = "S7 " + leg.Label;
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [CodeOf(kit, Code)], level: 0));
            var kept = new PeerKept(new PeerPin(leg.Id, kit.A.Fp, PinTrust.Proof), floor, null, null, leg.Id, leg.Kind);
            await using var c = Connector(PeerCarrier.Line, CodeTyped, true, leg.Kind, leg.Id, port, kept, linesOverWs: leg.OverWs);
            await c.Dial(T0);
            var refused = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Refused);
            var rec = c.M.Record(T0);
            var silent = await peer.Ask(new { @do = "silence", match = new { ev = "read", line = "AUTH*" }, ms = 300, after = 0 });
            var conn = await Taken(peer);
            Check(refused && rec is { State: "refused", Refusal: "floor", Light: "red" }
                  && rec.Words == "Hall A used to prove itself with PeerLink 1 over TLS; it now offers less — not linking" && rec.Counts.Malformed >= 1,
                $"peerlink scenarios: {name} — LINK answered `unknown command` on a floored id: floor-refused, the greeting before it discarded and counted ({rec.ToJson()}) {peer.Faults}");
            Check(c.Credentials.Count == 0 && peer.ReadOn(conn) is [var hello, var ask] && hello == "HELLO Stagewatch" && ask.StartsWith("@l1 LINK 1 main ", StringComparison.Ordinal)
                  && True(silent),
                $"peerlink scenarios: {name} — the peer read HELLO and LINK and nothing more: never AUTH-PROOF, never AUTH ({string.Join(" | ", peer.ReadOn(conn))})");
        }

        // line and playmap.lines.v1: TLS stripped as well — the acceptor offers none now. Refused before any plain socket opens.
        foreach (var leg in new[] { Line, LinesV1 })
        {
            var name = "S7 " + leg.Label + ", no TLS";
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, name, ListenAs(leg, kit.A, [CodeOf(kit, Code)], level: 0, tls: "plain"));
            var kept = new PeerKept(new PeerPin(leg.Id, kit.A.Fp, PinTrust.Proof), floor, null, null, leg.Id, leg.Kind);
            await using var c = Connector(PeerCarrier.Line, CodeTyped, true, leg.Kind, leg.Id, port, kept, linesOverWs: leg.OverWs);
            var scheme = await c.Dial(T0);
            var rec = c.M.Record(T0);
            var silent = await peer.Ask(new { @do = "silence", match = new { ev = "read", line = "HELLO*" }, ms = 300, after = 0 });
            Check(scheme == "tls" && rec is { State: "refused", Refusal: "floor" } && c.Sockets == 1 && c.Wrote.Count == 0 && True(silent)
                  && !peer.AllRead().Any(l => l.StartsWith("LINK", StringComparison.Ordinal) || l.Contains("AUTH", StringComparison.Ordinal)),
                $"peerlink scenarios: {name} — plain where TLS was is floor-refused with nothing sent, no plain socket opened ({rec.ToJson()}) {peer.Faults}");
        }

        // ws: a level-0 acceptor whose first message is a state frame, not link.
        {
            await using var peer = Peer(script);
            var port = await Listen(Check, peer, "S7 ws", ListenAs(Ws, kit.A, [Key(kit, Swk)], level: 0));
            var kept = new PeerKept(new PeerPin(MainId, kit.A.Fp, PinTrust.Proof), floor, null, null, MainId, "main");
            await using var c = Connector(PeerCarrier.Ws, Swk, false, "main", MainId, port, kept);
            await c.Dial(T0);
            var refused = await c.Pump(T0, () => c.M.State(T0) == PeerLinkState.Refused);
            var rec = c.M.Record(T0);
            var upgrade = await peer.Wait(e => Ev(e, "upgrade"));
            var conn = await Taken(peer);
            var silent = await peer.Ask(new { @do = "silence", match = new { ev = "read", conn }, ms = 300, after = 0 });
            Check(refused && True(silent) && rec is { State: "refused", Refusal: "floor", Light: "red" } && c.Credentials.Count == 0 && upgrade is { } u
                  && Str(u, "authorization") == "none" && peer.ReadOn(conn).Count == 0,
                $"peerlink scenarios: S7 ws — a first frame where LINK was is floor-refused: no bearer, no link message, nothing read by the peer ({rec.ToJson()}) {peer.Faults}");
        }
    }
}
