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
        Check(await PlayoutLinkTests.Until(() => { lock (bridge.Messages) return bridge.Messages.Any(m => m.Item1 == "Wrap up" && m.Item2 == MessageUrgency.Warning); }),
            "companion: MESSAGE still works");
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

    /// <summary>
    /// The Companion module's window grant lines, held to Stagewatch from its end: <c>companion-module/test/window-lines.tsv</c> is the
    /// file the module's own tests press <c>window_grant</c> against. Each press row's line is read as the Companion server reads it
    /// (tokenized, then the shared grammar) and must give the row's window action, window and app; then every press line goes to a real
    /// Companion server from this PC with a request id, and the door row for its action says what comes back: refused, under its id in
    /// exactly the row's words (the words the module's log shows), and nothing reaching the show thread; passed on to the show thread; or
    /// asked — a grant is a person's act (Show Bus 12.4) that Companion TCP proves no person for, so it reaches the show thread named a
    /// deck's and the host asks it of the operator on the desk (here a real <see cref="Stagewatch.Show.DeskService"/>): accepted at once
    /// in the row's words, and, once the operator decides (here: rejects), its final follow-up in the row's last words.
    /// </summary>
    public static async Task ModuleWindowLines(Action<bool, string> Check)
    {
        var master = ShowBusSpecTests.Master();
        var path = master is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(master)!)!, "companion-module", "test", "window-lines.tsv");
        Check(path is not null && File.Exists(path), "companion window lines: companion-module/test/window-lines.tsv is there");
        if (path is null || !File.Exists(path)) return;
        var rows = File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).ToList();
        var doors = rows.Where(r => r[0] == "door").ToDictionary(r => r[1], r => r, StringComparer.Ordinal);
        Check(doors.Count == 2 && doors.ContainsKey("grant") && doors.ContainsKey("withdraw")
              && doors.Values.All(d => d[2] switch
              {
                  "passes" => d.Length == 3,
                  "refused" => d.Length == 4 && d[3].Length > 0,
                  "asked" => d.Length == 5 && d[3].Contains("p<n>", StringComparison.Ordinal) && d[4].Contains("p<n>", StringComparison.Ordinal),
                  _ => false,
              }),
              "companion window lines: one door row for grant and one for withdraw, each refused with words, passed on, or asked of the operator");
        Check(doors.TryGetValue("grant", out var grantDoor) && grantDoor[2] == "asked" && doors["withdraw"][2] == "passes",
              "companion window lines: Companion's door asks a grant of the operator (Show Bus 12.4 lets it queue one) and passes a withdraw");
        Check(rows.All(r => r[0] is "door" or "press" or "refuse"), "companion window lines: every row is a door, a press or a refuse row");

        var presses = rows.Where(r => r[0] == "press").ToList();
        foreach (var r in presses)
        {
            var tokens = Stagewatch.Companion.CompanionProtocol.Tokenize("@c1 " + r[3]);
            var read = tokens.Count > 1 && tokens[0] == "@c1"
                && ShowCommandParser.TryParse(tokens[1], tokens.Skip(2).ToList(), out var cmd, out _) && cmd.Kind == ShowCommandKind.Window
                ? cmd.Get<WindowArgs>() : null;
            var said = read is { } a ? $"{a.Action}\t{a.Who}\t{a.Argument}" : "not read";
            Check(r.Length == 7 && said == $"{r[4]}\t{r[5]}\t{r[6]}", $"companion window lines: {r[3]} reads as {r[4]} {r[5]} {r[6]} ({said})");
        }
        Check(presses.Count(r => r[4] == "grant") >= 3 && presses.Count(r => r[4] == "withdraw") >= 3,
              $"companion window lines: grants and withdraws both pressed ({presses.Count} presses)");

        // Every press line, sent as the module sends it, against a real Companion server; a grant named a deck's is asked of the
        // operator on a real desk, as the app's and the rig's executors ask it, its dry run passed so the door's words are the desk's.
        var desk = DeskTests.Room();
        var asked = new List<string>();
        var bridge = new FakeBridge { Allowed = true };
        bridge.Receipts = (cmd, rid) =>
        {
            if (cmd.By is not { Asks: true } by || !PlayoutGate.Grants(cmd)) return null;
            var outcome = desk.Desk.AskGrant(cmd.Get<WindowArgs>(), by, _ => new WindowChange(true, "", [], [], WindowEffect.Changed));
            if (outcome.Proposal is not { } p) return Task.FromResult(CommandReceipt.Refused(rid, outcome.Words));
            lock (asked) asked.Add(p.Id);
            return Stagewatch.Show.DeskReceipts.Within(rid, desk.Desk, p.Id, TimeSpan.FromSeconds(3), TimeProvider.System, TimeProvider.System.GetTimestamp());
        };
        var port = FreePort();
        await using var server = new Stagewatch.Companion.CompanionServer(bridge);
        server.Start(port, bindAllInterfaces: false);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);
        var n = 0;
        foreach (var r in presses)
        {
            var id = $"w{++n}";
            int before;
            lock (bridge.Commands) before = bridge.Commands.Count;
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"@{id} {r[3]}\n"));
            string? answer = null;
            for (var i = 0; i < 20 && answer is null; i++)
            {
                var line = await ReadLine(reader);
                if (line is null) break;
                if (line.StartsWith($"OK @{id}", StringComparison.Ordinal) || line.StartsWith($"ERROR @{id}", StringComparison.Ordinal)) answer = line;
            }
            List<ShowCommand> heard;
            lock (bridge.Commands) heard = [.. bridge.Commands.Skip(before).Where(c => c.Kind == ShowCommandKind.Window)];
            var door = doors[r[4]];
            if (door[2] == "refused")
                Check(answer == $"ERROR @{id} {door[3]}" && heard.Count == 0,
                      $"companion window lines: {r[3]} is refused at Companion's door under its id in the door row's words, and reaches no show thread ({answer})");
            else if (door[2] == "passes")
                Check(answer?.StartsWith($"OK @{id}", StringComparison.Ordinal) == true && heard is [{ By: null } w] && w.Get<WindowArgs>() is { } a
                      && a.Action == r[4] && a.Who == r[5] && a.Argument == r[6],
                      $"companion window lines: {r[3]} passes Companion's door to the show thread, no one's ({answer})");
            else
            {
                string tag;
                lock (asked) tag = asked.Count > 0 ? Stagewatch.Show.DeskService.Short(asked[^1]) : "?";
                Check(answer == $"OK @{id} accepted {door[3].Replace("p<n>", tag, StringComparison.Ordinal)}"
                      && heard is [{ By: { Asks: true, Door: "Companion TCP" } } g] && g.Get<WindowArgs>() is { } a && a.Action == r[4] && a.Who == r[5] && a.Argument == r[6],
                      $"companion window lines: {r[3]} reaches the show thread named a deck's and is asked of the operator — accepted at once in the door row's words ({answer})");
                string whole;
                lock (asked) whole = asked[^1];
                desk.Desk.Decide(whole, approve: false, DeskOrigin.Operator);
                string? final = null;
                for (var i = 0; i < 20 && final is null; i++)
                {
                    var line = await ReadLine(reader);
                    if (line is null) break;
                    if (line.StartsWith($"OK @{id} final", StringComparison.Ordinal) || line.StartsWith($"ERROR @{id} final", StringComparison.Ordinal)) final = line;
                }
                Check(final == $"ERROR @{id} final {door[4].Replace("p<n>", tag, StringComparison.Ordinal)}",
                      $"companion window lines: once the operator decides (rejects), {r[3]}'s final follow-up says so in the door row's words ({final})");
            }
        }
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
