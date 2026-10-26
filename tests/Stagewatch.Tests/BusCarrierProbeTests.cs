using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using P = Stagewatch.BusCarrierProbe;

/// <summary>
/// Lab line LB-1's carrier probe (tools/BusCarrierProbe), which a person runs and whose record decides Show Bus 1.4's carrier: the
/// words it takes and refuses (never guessed); its payload, one run's alone; the listeners it binds, as the apps bind theirs (two on
/// 127.0.0.1 for PlayMap's and Patterns' loopback binds, one on an adapter's own address); which of them count for a carrier; its
/// reading of a layout (a listener that did not bind or stopped is not measured, one the condition has no node for is left out,
/// anything else on the port leaves every verdict unclaimed); what is on the port besides its own sockets, from the machine's table;
/// the limit a listener's own process keeps against the longest run; and runs of its own on a free port — clean, beside a silent
/// listener (as a node bound to loopback is), and beside an app that sends there — read for the record's shape, its exit code and
/// no LAN address of this machine.
/// </summary>
static class BusCarrierProbeTests
{
    public static async Task Run(Action<bool, string> Check)
    {
        Words(Check);
        Payloads(Check);
        Listeners(Check);
        Reading(Check);
        Port(Check);
        await Live(Check);
    }

    static P.Options? Parse(params string[] args) => P.Options.Parse(args, out _);

    static void Words(Action<bool, string> Check)
    {
        Check(Parse() is { Port: 9928, Count: 5, WaitMs: 400, OneProcess: true, Apart: true, ListenerIndex: null, Run: "", Address: null, Help: false }
              && P.Options.HelloPort == 9928,
            "probe: by default the hello port, five datagrams to each carrier, 400 ms after each, both layouts");
        Check(Parse("--layout", "one") is { OneProcess: true, Apart: false } && Parse("--layout", "apart") is { OneProcess: false, Apart: true }
              && Parse("--port", "65535", "--count", "100", "--wait-ms", "10000") is { Port: 65535, Count: 100, WaitMs: 10000 }
              && Parse("--help") is { Help: true },
            "probe: the layouts, the numbers at their bounds and --help are taken");
        string[][] refused =
        [
            ["--bogus", "1"], ["--port", "0"], ["--port", "65536"], ["--port", "+5"], ["--count", "0"], ["--count", "101"],
            ["--wait-ms", "49"], ["--layout", "sideways"], ["--count"], ["--run", "abcdef01"], ["--address", "10.0.0.2"],
            ["--listener", "0"], ["--listener", "6", "--run", "abcdef01"], ["--listener", "0", "--run", "ABCDEF01"],
            ["--listener", "5", "--run", "abcdef01"], ["--listener", "0", "--run", "abcdef01", "--address", "10.0.0.2"],
            ["--listener", "5", "--run", "abcdef01", "--address", "::1"],
        ];
        Check(refused.All(a => Parse(a) is null),
            "probe: an unknown word, a number out of bounds, a word with no value, and a listener's words out of place or without "
            + "the run and the address it needs are refused, never guessed");
        P.Options.Parse(["--port", "0"], out var error);
        Check(error.Contains("--port 0", StringComparison.Ordinal), "probe: a refusal names the word and the value: " + error);
        Check(Parse("--listener", "5", "--run", "abcdef01", "--address", "10.0.0.2") is { ListenerIndex: 5, Run: "abcdef01", Address: { } a }
              && a.Equals(IPAddress.Parse("10.0.0.2"))
              && Parse("--listener", "3", "--run", "abcdef01", "--count", "2", "--wait-ms", "100") is { ListenerIndex: 3, Address: null, Count: 2 },
            "probe: a listener's process takes its index and the run, the address only on an adapter's, and the run's count and wait");
    }

    static void Payloads(Action<bool, string> Check)
    {
        var run = P.Payload.NewRun();
        var other = run == "00000000" ? "00000001" : "00000000";
        var from = IPAddress.Loopback;
        Check(P.Payload.ValidRun(run) && run != P.Payload.NewRun() && !P.Payload.ValidRun("ABCDEF01") && !P.Payload.ValidRun("abcdef0"),
            "probe: each run draws its own id, eight lowercase hex digits");
        Check(P.Payload.Read(P.Payload.Bytes(run, 2, 3), run, from) == new P.Heard(2, 3, from),
            "probe: a datagram of this run reads back as its carrier and number");
        Check(P.Payload.Read(P.Payload.Bytes(other, 2, 3), run, from) == P.Heard.Foreign(from)
              && P.Payload.Read(P.Payload.Bytes(run, P.Layout.Carriers.Length, 0), run, from).Carrier == -1
              && P.Payload.Read(new byte[65], run, from).Carrier == -1
              && P.Payload.Read("{\"t\":\"main\",\"v\":1}"u8, run, from).Carrier == -1
              && P.Payload.Read(System.Text.Encoding.ASCII.GetBytes("LB1 " + run + " 1"), run, from).Carrier == -1,
            "probe: another run's datagram, a carrier beyond the table, an oversize datagram, a hello and a short line are foreign");
    }

    static void Listeners(Action<bool, string> Check)
    {
        var l = P.Layout.Listeners;
        Check(l.Length == 6 && l.Count(x => x.Bind == P.BindTo.Any) == 3 && l.Count(x => x.Bind == P.BindTo.Loopback) == 2
              && l.Count(x => x.Bind == P.BindTo.Adapter) == 1 && l[3].Bind == P.BindTo.Loopback && l[4].Bind == P.BindTo.Loopback
              && l[5].Bind == P.BindTo.Adapter && l.Count(x => !x.Joins) == 1 && !l[2].Joins
              && l[3].Why.Contains("PlayMap", StringComparison.Ordinal) && l[4].Why.Contains("Patterns", StringComparison.Ordinal),
            "probe: three listeners on every interface (one joined to nothing), PlayMap's and Patterns' on 127.0.0.1 and one on an "
            + "adapter's own address, bound in that order");
        Check(P.Layout.Address(P.BindTo.Any, null)!.Equals(IPAddress.Any) && P.Layout.Address(P.BindTo.Loopback, null)!.Equals(IPAddress.Loopback)
              && P.Layout.Address(P.BindTo.Adapter, null) is null && P.Layout.Address(P.BindTo.Adapter, IPAddress.Parse("10.0.0.2"))!.Equals(IPAddress.Parse("10.0.0.2")),
            "probe: with no adapter up there is no adapter's address to bind");
        var multicast = Array.FindIndex(P.Layout.Carriers, c => c.Kind == P.CarrierKind.Multicast);
        Check(multicast >= 0 && Enumerable.Range(0, l.Length).All(i => Enumerable.Range(0, P.Layout.Carriers.Length)
                  .All(c => P.Layout.Counts(i, c) == (c != multicast || l[i].Joins))),
            "probe: every listener counts for the unicast and the broadcasts, only the joined ones for the group");
        var max = Parse("--count", "100", "--wait-ms", "10000")!;
        Check(P.Layout.ChildLimit(max) > TimeSpan.FromSeconds(l.Length * P.Layout.ReadySeconds)
                  + TimeSpan.FromMilliseconds(P.Layout.Carriers.Length * (100L * P.Sender.PaceMs * 2 + 10000)),
            "probe: a listener's own process outlasts every other listener's wait to bind and the longest run's sends at twice their pace");
    }

    static void Reading(Action<bool, string> Check)
    {
        const int count = 3;
        var carriers = P.Layout.Carriers.Length;
        var multicast = Array.FindIndex(P.Layout.Carriers, c => c.Kind == P.CarrierKind.Multicast);
        P.Tally Heard(Func<int, int, int, bool> hears, int copies = 1)
        {
            var t = new P.Tally(a => a.ToString());
            for (var l = 0; l < P.Layout.Listeners.Length; l++)
            for (var c = 0; c < carriers; c++)
            for (var s = 0; s < count; s++)
                if (hears(l, c, s))
                    for (var k = 0; k < copies; k++) t.Add(l, new P.Heard(c, s, IPAddress.Loopback));
            return t;
        }
        P.ListenerState[] Bound() => P.Layout.Listeners.Select(x => new P.ListenerState(true, x.Joins ? "joined" : "joins nothing")).ToArray();
        P.SendResult[] Sent() => Enumerable.Range(0, carriers).Select(_ => new P.SendResult(count, null)).ToArray();
        P.Reach[] Reach(P.ListenerState[] states, P.Tally tally, P.SendResult[]? sends = null, string[]? warnings = null)
        {
            var r = new P.LayoutRecord("t", states, sends ?? Sent(), tally, null, warnings ?? []);
            return Enumerable.Range(0, carriers).Select(c => r.EveryNode(c, count)).ToArray();
        }
        bool All(P.Reach[] r, P.Reach want) => r.All(x => x == want);

        var all = Heard((_, _, _) => true);
        Check(All(Reach(Bound(), all), P.Reach.Yes) && All(Reach(Bound(), Heard((_, _, _) => true, copies: 2)), P.Reach.Yes)
              && new P.LayoutRecord("t", Bound(), Sent(), all, null, []).Claimed,
            "probe: every listener heard every datagram, a copy counting once: every carrier reached every node, and the layout is claimed");
        var shortOne = Reach(Bound(), Heard((l, c, s) => !(l == 4 && c == 0 && s == 1)));
        Check(shortOne[0] == P.Reach.No && shortOne.Skip(1).All(x => x == P.Reach.Yes),
            "probe: one datagram one listener missed is a no for that carrier alone");
        Check(Reach(Bound(), Heard((l, c, _) => c != multicast || l != 2))[multicast] == P.Reach.Yes
              && Reach(Bound(), Heard((l, c, _) => c != multicast || l != 3))[multicast] == P.Reach.No,
            "probe: the group need not reach the listener that joined nothing, and must reach every one that joined");
        var noAdapter = Bound();
        noAdapter[5] = P.ListenerState.NoAdapter;
        Check(All(Reach(noAdapter, Heard((l, _, _) => l != 5)), P.Reach.Yes) && !noAdapter[5].Measured
              && noAdapter[5].Shown.StartsWith("none: ", StringComparison.Ordinal),
            "probe: with every adapter off the adapter's listener has no node, and is left out rather than counted against a carrier");
        var notBound = Bound();
        notBound[4] = new P.ListenerState(false, "AddressAlreadyInUse");
        var stopped = Bound();
        stopped[1] = stopped[1] with { Stopped = "100 receives in a row failed, the last with MessageSize" };
        Check(All(Reach(notBound, Heard((l, _, _) => l != 4)), P.Reach.NotMeasured) && All(Reach(stopped, all), P.Reach.NotMeasured)
              && notBound[4].Shown == "not bound: AddressAlreadyInUse"
              && stopped[1].Shown == "joined, then stopped: 100 receives in a row failed, the last with MessageSize" && !stopped[1].Measured,
            "probe: a listener that did not bind or stopped leaves every carrier it counts for not measured, never a no it did not see");
        var sends = Sent();
        sends[2] = new P.SendResult(0, "NetworkUnreachable");
        var unsent = Reach(Bound(), all, sends);
        Check(unsent[2] == P.Reach.No && sends[2].Words(count) == "0 · NetworkUnreachable" && Sent()[0].Words(count) == "3",
            "probe: a carrier whose sends failed reached no one, and its sent cell says why");
        Check(All(Reach(Bound(), all, warnings: ["- △ something else"]), P.Reach.Unclaimed)
              && !new P.LayoutRecord("t", Bound(), Sent(), all, null, ["- △ something else"]).Claimed
              && P.LayoutRecord.NotMeasured("t", "why", []) is { Claimed: false } nm && nm.EveryNode(0, count) == P.Reach.NotMeasured,
            "probe: a layout with a △ line claims no verdict, and one not measured claims none either");
        Check(P.LayoutRecord.Words(P.Reach.Yes) == "**yes**" && P.LayoutRecord.Words(P.Reach.No) == "no"
              && P.LayoutRecord.Words(P.Reach.NotMeasured) == "not measured" && P.LayoutRecord.Words(P.Reach.Unclaimed) == "unclaimed"
              && Heard((_, _, _) => true, copies: 2).Cell(0, 0, count) == "3 from 127.0.0.1 (6 copies)" && Heard((_, _, _) => false).Cell(0, 0, count) == "0",
            "probe: the every-node words, and a cell's distinct datagrams with their copies");
        Check(P.Apart.ReadyLine("ready joined", 0) is { Bound: true, Words: "joined" }
              && P.Apart.ReadyLine("bind-failed AddressAlreadyInUse", 0) is { Bound: false, Words: "AddressAlreadyInUse" }
              && Throws<FormatException>(() => P.Apart.ReadyLine("heard 0 0 127.0.0.1", 0)),
            "probe: a listener's process says ready or bind-failed first, and anything else first is refused");
    }

    static void Port(Action<bool, string> Check)
    {
        IPAddress A(string s) => IPAddress.Parse(s);
        var others = P.PortCheck.Others([A("0.0.0.0"), A("127.0.0.1"), A("0.0.0.0"), A("127.0.0.1"), A("::")], [A("0.0.0.0"), A("0.0.0.0"), A("127.0.0.1")]);
        Check(others.Count == 2 && others[0].Equals(A("127.0.0.1")) && others[1].Equals(IPAddress.IPv6Any)
              && P.PortCheck.Others([A("127.0.0.1")], [A("127.0.0.1"), A("0.0.0.0")]).Count == 0,
            "probe: what is on the port beyond its own sockets is counted address by address, a second 127.0.0.1 included");
        var lan = A("10.20.30.40");
        Func<IPAddress, string> names = a => IPAddress.IsLoopback(a) ? a.ToString() : a.Equals(lan) ? "eth9" : P.Machine.Elsewhere;
        Check(P.Machine.BoundName(IPAddress.Any, names) == "0.0.0.0" && P.Machine.BoundName(IPAddress.IPv6Any, names) == "[::]"
              && P.Machine.BoundName(IPAddress.Loopback, names) == "127.0.0.1" && P.Machine.BoundName(P.Layout.Group, names) == "239.255.99.28"
              && P.Machine.BoundName(lan, names) == "eth9's address" && P.Machine.BoundName(A("10.20.30.41"), names) == "an address no adapter here has",
            "probe: a socket's address is shown by its adapter's name, never as a LAN address");

        using var silent = Silent(out var port);
        var bound = P.PortCheck.Bound(port, out var why);
        Check(bound is { } b && b.Any(x => x.Equals(IPAddress.Loopback)) && P.PortCheck.During(port, [], names) is { } during
              && during.Contains("△", StringComparison.Ordinal) && during.Contains("127.0.0.1", StringComparison.Ordinal)
              && P.PortCheck.During(port, [IPAddress.Loopback], names) is null,
            "probe: the machine's table shows a socket that listens and says nothing, and the probe's own are not counted against it: " + why);
    }

    static async Task Live(Action<bool, string> Check)
    {
        var header = "| carrier | sent | " + string.Join(" | ", P.Layout.Listeners.Select(l => l.Column)) + " | every node |";
        async Task<(int Exit, string Text, string[] Rows)> RunOn(int port)
        {
            var o = Parse("--port", port.ToString(CultureInfo.InvariantCulture), "--count", "2", "--wait-ms", "100", "--layout", "one")!;
            using var w = new StringWriter(CultureInfo.InvariantCulture);
            var exit = await P.Probe.RunAsync(o, w);
            var text = w.ToString().Replace("\r", "", StringComparison.Ordinal);
            var rows = text.Split('\n').Where(line => P.Layout.Carriers.Any(c => line.StartsWith("| " + c.Label + " |", StringComparison.Ordinal))).ToArray();
            return (exit, text, rows);
        }
        bool Shaped(string text, string[] rows)
            => text.Split('\n').Contains(header) && rows.Length == P.Layout.Carriers.Length
               && rows.All(r => r.Split('|').Length == header.Split('|').Length) && text.Contains("### What this run says", StringComparison.Ordinal)
               && LanAddresses().All(a => !System.Text.RegularExpressions.Regex.IsMatch(text, "(?<![0-9.])" + System.Text.RegularExpressions.Regex.Escape(a) + "(?![0-9])"));

        var (exit, text, rows) = await RunOn(FreePort());
        Check(exit == 0 && Shaped(text, rows) && !text.Split('\n').Any(line => line.StartsWith("- △", StringComparison.Ordinal)) && rows.All(r => r.EndsWith("| **yes** |", StringComparison.Ordinal)
                  || r.EndsWith("| no |", StringComparison.Ordinal) || r.EndsWith("| not measured |", StringComparison.Ordinal)),
            "probe: a run on a free port prints its table, one row a carrier and a cell a listener, claims its verdicts, exits 0 and "
            + "names no LAN address of this machine:\n" + text);

        using (Silent(out var port))
        {
            var (e, t, r) = await RunOn(port);
            Check(e == 1 && Shaped(t, r) && t.Contains("△ Before the probe bound", StringComparison.Ordinal)
                  && t.Contains("△ While the probe listened", StringComparison.Ordinal) && r.All(x => x.EndsWith("| unclaimed |", StringComparison.Ordinal))
                  && t.Contains("unclaimed: something besides the probe", StringComparison.Ordinal),
                "probe: beside a socket on 127.0.0.1 that listens and says nothing, as a node bound to loopback does, every verdict is "
                + "unclaimed and the run exits 1:\n" + t);
        }

        var noisy = FreePort();
        using var stop = new CancellationTokenSource();
        var noise = Task.Run(async () =>
        {
            using var tx = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var failed = 0;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    tx.SendTo("{\"t\":\"main\",\"v\":1}"u8.ToArray(), new IPEndPoint(IPAddress.Loopback, noisy));
                }
                catch (SocketException)
                {
                    failed++;   // nothing listens yet: the next send is the same hello
                }
                await Task.Delay(10);
            }
            return failed;
        });
        var (en, tn, rn) = await RunOn(noisy);
        await stop.CancelAsync();
        await noise;
        Check(en == 1 && Shaped(tn, rn) && tn.Contains("came from this machine (127.0.0.1)", StringComparison.Ordinal)
              && rn.All(x => x.EndsWith("| unclaimed |", StringComparison.Ordinal)),
            "probe: datagrams from this machine that are not the run's leave every verdict unclaimed, and the run exits 1:\n" + tn);
    }

    /// <summary>A port no UDP socket holds now: the system's pick for a socket of its own, closed again.</summary>
    static int FreePort()
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Bind(new IPEndPoint(IPAddress.Any, 0));
        return ((IPEndPoint)s.LocalEndPoint!).Port;
    }

    /// <summary>A socket on 127.0.0.1 that shares the port and says nothing, as a node bound to loopback listens.</summary>
    static Socket Silent(out int port)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        port = ((IPEndPoint)s.LocalEndPoint!).Port;
        return s;
    }

    /// <summary>This machine's IPv4 addresses besides loopback, none of which a record may carry (matched as whole addresses).</summary>
    static IEnumerable<string> LanAddresses()
        => NetworkInterface.GetAllNetworkInterfaces().SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(u => u.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString());

    static bool Throws<T>(Action act) where T : Exception
    {
        try
        {
            act();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }
}
