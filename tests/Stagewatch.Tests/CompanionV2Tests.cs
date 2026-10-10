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
