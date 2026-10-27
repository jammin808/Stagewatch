using System.Net;
using System.Net.Sockets;
using System.Text;
using Stagewatch.Core;

/// <summary>Companion protocol 2.0: detail frames for the module, RECORD lines for Companion's action recorder.</summary>
static class CompanionV2Tests
{
    public static async Task Run(Action<bool, string> Check)
    {
        var bridge = new FakeBridge { Allowed = true };
        var port = FreePort();
        await using var server = new Stagewatch.Companion.CompanionServer(bridge);
        server.Start(port, bindAllInterfaces: false);
        bridge.Snapshots.Publish(new ShowSnapshot
        {
            Text = "04:00", Seconds = 240, Mode = TimerMode.Countdown,
            Timings = new[] { new ItemTimingView(0, "Keynote", "", SegmentKind.Talk, 600, 0, "09:00", "09:00", "09:10", "", "", ItemStatus.Live, 0, false, false, "") }.ToEquatableArray()
        });

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        await stream.WriteAsync("SUBSCRIBE JSON\n"u8.ToArray());
        string? snap = null;
        for (int i = 0; i < 6 && snap is null; i++)
        {
            var line = await ReadLine(reader);
            if (line?.StartsWith("SNAPSHOT ") == true) snap = line;
        }
        Check(snap is not null && snap.Contains("\"detail\":true") && snap.Contains("\"timings\":[{\"i\":0"), "companion: module gets detail frames");

        bridge.Record("nudge 60");
        string? rec = null;
        for (int i = 0; i < 6 && rec is null; i++)
        {
            var line = await ReadLine(reader);
            if (line?.StartsWith("RECORD ") == true) rec = line;
        }
        Check(rec == "RECORD nudge 60", $"companion: operator actions sent as RECORD ({rec})");

        // MESSAGE from Companion reaches the bridge (the app marshals it to its own thread).
        await stream.WriteAsync("MESSAGE \"Wrap up\" warn slow\n"u8.ToArray());
        await Task.Delay(200);
        lock (bridge.Messages) Check(bridge.Messages.Any(m => m.Item1 == "Wrap up" && m.Item2 == MessageUrgency.Warning), "companion: MESSAGE still works");
    }

    /// <summary>
    /// The Companion module's playout lines, held to this grammar from Stagewatch's end: <c>companion-module/test/playout-lines.tsv</c>
    /// is the file the module's own tests press every action against. Each press row's line is read as the Companion server reads a
    /// line with a request id (tokenized, then the shared grammar) and must give the row's kind, packed arguments and number; each name
    /// row must agree with <see cref="PlayoutRules.NameProblem"/>, and every word this grammar keeps from links has a row the module
    /// refuses — so a reserved name or action added here fails until the module learns it.
    /// </summary>
    public static void ModuleLines(Action<bool, string> Check)
    {
        var master = ShowBusSpecTests.Master();
        var path = master is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(master)!)!, "companion-module", "test", "playout-lines.tsv");
        Check(path is not null && File.Exists(path), "companion module lines: companion-module/test/playout-lines.tsv is there");
        if (path is null || !File.Exists(path)) return;
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).ToList();
        int presses = 0, names = 0;
        foreach (var r in rows)
        {
            switch (r[0])
            {
                case "name":
                    names++;
                    Check((PlayoutRules.NameProblem(r[1].ToLowerInvariant()) is null) == (r[2] == "ok"), $"companion module lines: {r[1]} {(r[2] == "ok" ? "names" : "can't name")} a playout link here too");
                    break;
                case "press":
                {
                    presses++;
                    var tokens = Stagewatch.Companion.CompanionProtocol.Tokenize("@c1 " + r[3]);
                    var read = tokens.Count > 1 && tokens[0] == "@c1"
                        && ShowCommandParser.TryParse(tokens[1], tokens.Skip(2).ToList(), out var cmd, out _)
                        ? cmd : (ShowCommand?)null;
                    var said = read is { } c ? $"{c.Kind}\t{(c.Text ?? "").Replace('\u0001', '|')}\t{c.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : "not read";
                    Check(said == $"{r[4]}\t{r[5]}\t{r[6]}", $"companion module lines: {r[3]} reads as {r[4]} {r[5]} {r[6]} ({said})");
                    Check(read is { } d && PlayoutGate.Drives(d.Kind), $"companion module lines: {r[3]} drives another app's air, so the playout gate guards it");
                    break;
                }
                case "refuse":
                    break;
                default:
                    Check(false, $"companion module lines: a row of kind {r[0]}");
                    break;
            }
        }
        Check(presses >= 20 && names >= 20, $"companion module lines: {presses} presses and {names} names read");
        var refused = rows.Where(r => r[0] == "name" && r[2] == "no").Select(r => r[1]).ToHashSet(StringComparer.Ordinal);
        var kept = PlayoutRules.Actions.Concat(PlayoutRules.Reserved).Append(PlayoutRules.TrustWord).Where(w => !refused.Contains(w)).ToList();
        Check(kept.Count == 0, $"companion module lines: every word kept from links has a name row the module refuses ({string.Join(", ", kept)})");
    }

    static async Task<string?> ReadLine(StreamReader r)
    {
        using var cts = new CancellationTokenSource(2000);
        try { return await r.ReadLineAsync(cts.Token); } catch (OperationCanceledException) { return null; }
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
    }
}
