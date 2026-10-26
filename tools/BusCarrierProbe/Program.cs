using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

// Lab line LB-1 (Show Bus 1.3 §2; the records are in docs/WHATS_NEXT.md): which carrier finds the apps on one machine with no
// network. Every node listens on the hello port with SO_REUSEADDR: Stagewatch and AGMessage on every interface; PlayMap on 127.0.0.1
// when its bind is loopback and on every interface otherwise; Patterns on its wire's bind address (every interface, 127.0.0.1 or one
// adapter's own address). On a shared port a datagram sent to one address reaches one listener of several. The probe binds the six
// listeners of Layout.Listeners on the port, in that order, sends to each carrier of Layout.Carriers in turn and prints who heard
// what as Markdown: first with the listeners in this process, then with each in a process of its own, as the apps are.
//   dotnet run --project tools/BusCarrierProbe -- [--port 9928] [--count 5] [--wait-ms 400] [--layout both|one|apart]
// One run measures one condition, the network up or every adapter off; its network line says which and names every adapter
// still up. Before a layout binds and while it listens, the probe reads the machine's table of UDP sockets on the port: a socket
// there that is not the probe's (an app that listens and says nothing, as a node bound to loopback does) leaves that layout's
// verdicts unclaimed, as does a datagram from this machine that is not the probe's.
// Exit 0 when every layout asked for was measured and its verdicts claimed, 1 when one was not, 2 on a word it does not know
// (never guessed). Nothing here passes or fails a carrier: 1.4 names one from the Windows records.
namespace Stagewatch.BusCarrierProbe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = Options.Parse(args, out var error);
        if (options is null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Options.Usage);
            return 2;
        }
        if (options.Help)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }
        return options.ListenerIndex is { } index ? await Apart.ListenAsync(options, index) : await Probe.RunAsync(options, Console.Out);
    }

    internal static string Inv(FormattableString text) => FormattableString.Invariant(text);
}

/// <summary>
/// The probe's words. <see cref="ListenerIndex"/>, <see cref="Run"/> and <see cref="Address"/> are set only in a listener's own
/// process (<c>--listener</c>), <see cref="Address"/> only for the listener on an adapter's address.
/// </summary>
internal sealed record Options(int Port, int Count, int WaitMs, bool OneProcess, bool Apart, int? ListenerIndex, string Run, IPAddress? Address, bool Help)
{
    /// <summary>Show Bus §14's <c>hello.port</c>: the port every node shares.</summary>
    public const int HelloPort = 9928;

    public const string Usage = """
        BusCarrierProbe: lab line LB-1, which carrier reaches every app listening on the Show Bus hello port on this machine.
          --port N       the port the listeners share, 1-65535 (default 9928, the hello port: close Stagewatch, Patterns,
                         PlayMap and AGMessage first, and on an installed PlayMap node stop its engine with
                         PlayMap.Supervisor.exe stop, or a unicast one of them takes is lost to the probe)
          --count N      datagrams sent to each carrier, 1-100 (default 5)
          --wait-ms N    how long each carrier is listened for after its last datagram, 50-10000 (default 400)
          --layout L     one (the listeners in this process), apart (each in a process of its own) or both (default)
        Run it with the network up, then with every adapter off; each run prints its record as Markdown.
        """;

    public static Options? Parse(string[] args, out string error)
    {
        var o = new Options(HelloPort, 5, 400, true, true, null, "", null, false);
        error = "";
        for (var i = 0; i < args.Length; i++)
        {
            var word = args[i];
            if (word is "--help" or "-h")
            {
                o = o with { Help = true };
                continue;
            }
            if (i + 1 >= args.Length)
            {
                error = Program.Inv($"{word}: not a word the probe knows, or it needs a value");
                return null;
            }
            var value = args[++i];
            int? Number(int min, int max)
                => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max ? n : null;
            Options? next = word switch
            {
                "--port" => Number(1, 65535) is { } port ? o with { Port = port } : null,
                "--count" => Number(1, 100) is { } count ? o with { Count = count } : null,
                "--wait-ms" => Number(50, 10000) is { } wait ? o with { WaitMs = wait } : null,
                "--layout" => value switch
                {
                    "both" => o with { OneProcess = true, Apart = true },
                    "one" => o with { OneProcess = true, Apart = false },
                    "apart" => o with { OneProcess = false, Apart = true },
                    _ => null,
                },
                "--listener" => Number(0, Layout.Listeners.Length - 1) is { } index ? o with { ListenerIndex = index } : null,
                "--run" => Payload.ValidRun(value) ? o with { Run = value } : null,
                "--address" => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork
                    ? o with { Address = address }
                    : null,
                _ => null,
            };
            if (next is null)
            {
                error = Program.Inv($"{word} {value}: not a word or a value the probe knows");
                return null;
            }
            o = next;
        }
        if (o.ListenerIndex is null && (o.Run.Length > 0 || o.Address is not null))
        {
            error = "--run and --address are a listener process's words: the probe starts it with them";
            return null;
        }
        if (o.ListenerIndex is { } listener && (o.Run.Length == 0 || (Layout.Listeners[listener].Bind == BindTo.Adapter) != (o.Address is not null)))
        {
            error = "--listener needs --run, and --address exactly when it is the listener on an adapter's address: the probe starts it";
            return null;
        }
        return o;
    }
}

/// <summary>Where a listener binds: every interface, 127.0.0.1, or one adapter's own IPv4 address.</summary>
internal enum BindTo { Any, Loopback, Adapter }

/// <summary>A listener as the apps bind theirs, in the order the probe binds them (the order can decide which takes a unicast).</summary>
internal sealed record ListenerSpec(string Column, BindTo Bind, bool Joins, string Why);

internal enum CarrierKind { Unicast, Broadcast, Multicast }

internal sealed record Carrier(string Label, IPAddress To, CarrierKind Kind, string Why);

internal static class Layout
{
    /// <summary>The group a multicast carrier would use: organisation-local scope, the hello port in its last two bytes.</summary>
    public static readonly IPAddress Group = IPAddress.Parse("239.255.99.28");

    /// <summary>How long the probe waits for a listener's process to say it bound.</summary>
    public const int ReadySeconds = 20;

    public static readonly ListenerSpec[] Listeners =
    [
        new("1: 0.0.0.0, joined", BindTo.Any, true,
            "every interface, as Stagewatch and AGMessage listen, PlayMap on any bind but loopback and Patterns with its wire on every interface; joined to the group on 127.0.0.1"),
        new("2: 0.0.0.0, joined", BindTo.Any, true, "a second app on every interface, joined the same way"),
        new("3: 0.0.0.0, not joined", BindTo.Any, false, "every interface, joined to nothing: a reader as every app is today"),
        new("4: 127.0.0.1, joined", BindTo.Loopback, true,
            "127.0.0.1 only, as PlayMap listens when its bind is loopback; joined to the group on 127.0.0.1"),
        new("5: 127.0.0.1, joined", BindTo.Loopback, true,
            "127.0.0.1 only, as Patterns listens when its wire is bound to loopback: a second node there, joined the same way"),
        new("6: an adapter's address, joined", BindTo.Adapter, true,
            "one adapter's own IPv4 address, as Patterns listens when its wire is bound to its control network, joined the same way; with every adapter off no node can bind one, and it is left out"),
    ];

    public static readonly Carrier[] Carriers =
    [
        new("127.0.0.1", IPAddress.Loopback, CarrierKind.Unicast, "the copy a networked node sends to this machine today"),
        new("127.255.255.255", IPAddress.Parse("127.255.255.255"), CarrierKind.Broadcast, "loopback's own broadcast address"),
        new("255.255.255.255", IPAddress.Broadcast, CarrierKind.Broadcast, "the hello every networked node sends today"),
        new("239.255.99.28 via 127.0.0.1", Group, CarrierKind.Multicast, "the group, sent out of 127.0.0.1 with multicast loop on"),
    ];

    /// <summary>Where a listener of <paramref name="bind"/> binds; null for the adapter's address when no adapter is up.</summary>
    public static IPAddress? Address(BindTo bind, IPAddress? adapter) => bind switch
    {
        BindTo.Any => IPAddress.Any,
        BindTo.Loopback => IPAddress.Loopback,
        _ => adapter,
    };

    /// <summary>
    /// Whether a node using <paramref name="carrier"/> would be listener <paramref name="listener"/>: a node that names multicast joins
    /// the group, so for that carrier only the listeners that join count; for the others every listener does.
    /// </summary>
    public static bool Counts(int listener, int carrier) => Carriers[carrier].Kind != CarrierKind.Multicast || Listeners[listener].Joins;

    /// <summary>
    /// How long a listener's own process listens before it stops by itself, from the run's numbers: every other listener's wait to
    /// bind, every carrier's sends at five times their pace and its wait, and a minute more. A process that reaches it says so, and
    /// the probe counts that listener as not measured.
    /// </summary>
    public static TimeSpan ChildLimit(Options o)
        => TimeSpan.FromSeconds(Listeners.Length * ReadySeconds + 60)
           + TimeSpan.FromMilliseconds(Carriers.Length * ((long)o.Count * Sender.PaceMs * 5 + o.WaitMs));
}

/// <summary>One datagram a listener heard: <see cref="Carrier"/> and <see cref="Seq"/> from this run's payload, or -1 for anything else.</summary>
internal readonly record struct Heard(int Carrier, int Seq, IPAddress From)
{
    public static Heard Foreign(IPAddress from) => new(-1, -1, from);
}

internal static class Payload
{
    /// <summary>A fresh run id, so a datagram of an earlier run, another layout or another probe is never counted.</summary>
    public static string NewRun() => RandomNumberGenerator.GetHexString(8, lowercase: true);

    public static bool ValidRun(string run) => run.Length == 8 && run.All(char.IsAsciiHexDigitLower);

    /// <summary>"LB1 &lt;run&gt; &lt;carrier&gt; &lt;seq&gt;" in ASCII: a hello reader that hears it drops it, as it is not JSON.</summary>
    public static byte[] Bytes(string run, int carrier, int seq) => Encoding.ASCII.GetBytes(Program.Inv($"LB1 {run} {carrier} {seq}"));

    public static Heard Read(ReadOnlySpan<byte> bytes, string run, IPAddress from)
    {
        if (bytes.Length > 64) return Heard.Foreign(from);
        var parts = Encoding.ASCII.GetString(bytes).Split(' ');
        return parts.Length == 4 && parts[0] == "LB1" && parts[1] == run
               && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var carrier) && carrier < Layout.Carriers.Length
               && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
            ? new Heard(carrier, seq, from)
            : Heard.Foreign(from);
    }
}

/// <summary>
/// A listener's state: whether it bound, and its join ("joined", "joins nothing" or "join failed: …") or the socket error that kept
/// it from binding; <see cref="Absent"/> when this condition has no such node (an adapter's address with every adapter off); and
/// <see cref="Stopped"/>, why it stopped listening before the probe was done with it.
/// </summary>
internal sealed record ListenerState(bool Bound, string Words, bool Absent = false, string? Stopped = null)
{
    public static readonly ListenerState NoAdapter = new(false, "no adapter is up, so no node can bind one's address", Absent: true);

    /// <summary>Bound, and listening until the probe was done: what it heard is all that came to it.</summary>
    public bool Measured => Bound && Stopped is null;

    /// <summary>As the record shows it.</summary>
    public string Shown => Absent ? "none: " + Words
        : !Bound ? "not bound: " + Words
        : Stopped is null ? Words
        : Words + ", then stopped: " + Stopped;
}

/// <summary>One listener's socket, bound as the apps bind theirs: IPv4, SO_REUSEADDR, then the join where the spec asks for one.</summary>
internal sealed class Listener : IDisposable
{
    /// <summary>Receives that fail one after another before a listener stops and says why.</summary>
    public const int FailuresInARow = 100;

    private readonly Socket? _socket;

    public ListenerState State { get; }

    /// <param name="bind">Where it binds; null when this condition has no such node (<see cref="ListenerState.NoAdapter"/>).</param>
    public Listener(ListenerSpec spec, IPAddress? bind, int port)
    {
        if (bind is null)
        {
            State = ListenerState.NoAdapter;
            return;
        }
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            // On Linux .NET sets SO_REUSEPORT with it, as for every .NET app; the record's listeners line says so.
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(bind, port));
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            State = new ListenerState(false, ex.SocketErrorCode.ToString());
            return;
        }
        var words = "joins nothing";
        if (spec.Joins)
        {
            // IP_MULTICAST_LOOP is on by default at both ends (Windows reads it on the receiving socket, Linux on the sending one);
            // the sender sets it too, so a join here is all a listener does.
            try
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Layout.Group, IPAddress.Loopback));
                words = "joined";
            }
            catch (SocketException ex)
            {
                words = "join failed: " + ex.SocketErrorCode;
            }
        }
        _socket = socket;
        State = new ListenerState(true, words);
    }

    /// <summary>
    /// Every datagram heard until <paramref name="stop"/>, read against <paramref name="run"/>. Null when it listened until stopped;
    /// otherwise why it stopped first.
    /// </summary>
    public async Task<string?> ListenAsync(string run, Action<Heard> heard, CancellationToken stop)
    {
        if (_socket is null) return null;
        // 64 KiB holds any UDP datagram, so no receive fails for the size of what came (Windows fails one larger than the buffer).
        var buffer = new byte[65536];
        var failures = 0;
        while (true)
        {
            SocketReceiveFromResult r;
            try
            {
                r = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), stop);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            catch (SocketException ex)
            {
                // A failed receive is not a datagram: keep listening, but a socket that only fails stops and says why rather than spin.
                if (++failures >= FailuresInARow) return Program.Inv($"{failures} receives in a row failed, the last with {ex.SocketErrorCode}");
                continue;
            }
            failures = 0;
            heard(Payload.Read(buffer.AsSpan(0, r.ReceivedBytes), run, ((IPEndPoint)r.RemoteEndPoint).Address));
        }
    }

    public void Dispose() => _socket?.Dispose();
}

/// <summary>What one carrier's sends did: how many went, and the first failure's socket error.</summary>
internal sealed record SendResult(int Sent, string? Failure)
{
    public string Words(int count) => Failure is null && Sent == count ? Program.Inv($"{Sent}") : Program.Inv($"{Sent} · {Failure}");
}

internal static class Sender
{
    /// <summary>The gap between two datagrams to one carrier.</summary>
    public const int PaceMs = 20;

    /// <summary>
    /// Each carrier in turn from a socket of its own, then a wait for its stragglers. The unicast and the broadcasts leave an unbound
    /// socket and follow the route, as Stagewatch's hello does; only the group is sent out of 127.0.0.1.
    /// </summary>
    public static async Task<SendResult[]> SendAllAsync(Options o, string run)
    {
        var results = new SendResult[Layout.Carriers.Length];
        for (var c = 0; c < Layout.Carriers.Length; c++)
        {
            results[c] = await SendAsync(o, run, c);
            await Task.Delay(o.WaitMs);
        }
        return results;
    }

    private static async Task<SendResult> SendAsync(Options o, string run, int index)
    {
        var carrier = Layout.Carriers[index];
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (carrier.Kind == CarrierKind.Broadcast) socket.EnableBroadcast = true;
            if (carrier.Kind == CarrierKind.Multicast)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, IPAddress.Loopback.GetAddressBytes());
                socket.MulticastLoopback = true;
            }
        }
        catch (SocketException ex)
        {
            return new SendResult(0, "not set up: " + ex.SocketErrorCode);
        }
        var to = new IPEndPoint(carrier.To, o.Port);
        var sent = 0;
        string? failure = null;
        for (var seq = 0; seq < o.Count; seq++)
        {
            try
            {
                socket.SendTo(Payload.Bytes(run, index, seq), to);
                sent++;
            }
            catch (SocketException ex)
            {
                failure ??= ex.SocketErrorCode.ToString();
            }
            await Task.Delay(PaceMs);
        }
        return new SendResult(sent, failure);
    }
}

/// <summary>Who heard what in one layout, by listener and carrier: distinct datagrams, copies, and where they came from.</summary>
internal sealed class Tally(Func<IPAddress, string> name)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(int Listener, int Carrier), Hearing> _cells = [];
    private readonly SortedSet<string> _foreignHere = new(StringComparer.Ordinal);
    private int _foreignHereCount;
    private int _foreignElsewhereCount;

    public void Add(int listener, Heard heard)
    {
        var from = name(heard.From);
        lock (_gate)
        {
            if (heard.Carrier < 0)
            {
                if (from == Machine.Elsewhere)
                {
                    _foreignElsewhereCount++;
                }
                else
                {
                    _foreignHereCount++;
                    _foreignHere.Add(from);
                }
                return;
            }
            if (!_cells.TryGetValue((listener, heard.Carrier), out var cell)) _cells[(listener, heard.Carrier)] = cell = new Hearing();
            cell.Seqs.Add(heard.Seq);
            cell.From.Add(from);
            cell.Copies++;
        }
    }

    /// <summary>The distinct datagrams of this run a listener heard from a carrier (a copy heard twice counts once).</summary>
    public int Distinct(int listener, int carrier, int count)
    {
        lock (_gate) return _cells.TryGetValue((listener, carrier), out var cell) ? cell.Seqs.Count(s => s < count) : 0;
    }

    public string Cell(int listener, int carrier, int count)
    {
        lock (_gate)
        {
            if (!_cells.TryGetValue((listener, carrier), out var cell)) return "0";
            var distinct = cell.Seqs.Count(s => s < count);
            var copies = cell.Copies > distinct ? Program.Inv($" ({cell.Copies} copies)") : "";
            return Program.Inv($"{distinct} from {string.Join(", ", cell.From)}{copies}");
        }
    }

    /// <summary>What one listener heard from one carrier.</summary>
    private sealed class Hearing
    {
        public HashSet<int> Seqs { get; } = [];
        public SortedSet<string> From { get; } = new(StringComparer.Ordinal);
        public int Copies { get; set; }
    }

    /// <summary>Datagrams that were not this run's: how many came from this machine and the names of its sources, and how many from others.</summary>
    public (int Here, IReadOnlyList<string> HereFrom, int Elsewhere) Foreign()
    {
        lock (_gate) return (_foreignHereCount, _foreignHere.ToList(), _foreignElsewhereCount);
    }
}

/// <summary>Whether a carrier reached every node in one layout: yes, no, not measured (a listener that counts did not bind or stopped), or unclaimed (something else was on the port).</summary>
internal enum Reach { Yes, No, NotMeasured, Unclaimed }

/// <summary>One layout's record, or why it was not measured; <see cref="Warnings"/> are its △ lines, and any leaves its verdicts unclaimed.</summary>
internal sealed record LayoutRecord(string Title, ListenerState[] Listeners, SendResult[] Sends, Tally? Tally, string? Failure, IReadOnlyList<string> Warnings)
{
    public static LayoutRecord NotMeasured(string title, string why, IReadOnlyList<string> warnings) => new(title, [], [], null, why, warnings);

    /// <summary>Measured, with nothing but the probe on the port: its verdicts stand.</summary>
    public bool Claimed => Tally is not null && Warnings.Count == 0;

    /// <summary>
    /// Whether every listener that counts for the carrier (<see cref="Layout.Counts"/>; one this condition has no node for left out)
    /// heard every datagram, all of them sent. Not measured when one of them did not bind or stopped; unclaimed when the layout is.
    /// </summary>
    public Reach EveryNode(int carrier, int count)
    {
        if (Tally is null) return Reach.NotMeasured;
        if (Warnings.Count > 0) return Reach.Unclaimed;
        var counting = Enumerable.Range(0, Layout.Listeners.Length).Where(l => Layout.Counts(l, carrier) && !Listeners[l].Absent).ToList();
        if (counting.Any(l => !Listeners[l].Measured)) return Reach.NotMeasured;
        return Sends[carrier].Sent == count && counting.All(l => Tally.Distinct(l, carrier, count) == count) ? Reach.Yes : Reach.No;
    }

    public static string Words(Reach reach) => reach switch
    {
        Reach.Yes => "**yes**",
        Reach.No => "no",
        Reach.NotMeasured => "not measured",
        _ => "unclaimed",
    };
}

internal static class Machine
{
    /// <summary>How a source that is no address of this machine is named.</summary>
    public const string Elsewhere = "another machine";

    /// <summary>An adapter's own IPv4 address and the adapter's name: where listener 6 binds, named in the record by the name alone.</summary>
    internal sealed record AdapterAddress(IPAddress Address, string Name);

    private static IEnumerable<NetworkInterface> Up()
        => NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

    private static IEnumerable<IPAddress> V4(NetworkInterface n)
        => n.GetIPProperties().UnicastAddresses.Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));

    /// <summary>The adapters up with an IPv4 address, loopback aside: none is the every-adapter-off condition.</summary>
    public static IReadOnlyList<string> AdaptersUp()
    {
        try
        {
            return Up().Where(n => V4(n).Any()).Select(n => n.Name).ToList();
        }
        catch (NetworkInformationException ex)
        {
            return [Program.Inv($"unknown ({ex.ErrorCode})")];
        }
    }

    /// <summary>The first IPv4 address of the first adapter <see cref="AdaptersUp"/> names; null when none is up.</summary>
    public static AdapterAddress? FirstAdapter()
    {
        try
        {
            return Up().SelectMany(n => V4(n).Select(a => new AdapterAddress(a, n.Name))).FirstOrDefault();
        }
        catch (NetworkInformationException)
        {
            return null;   // the network line says the adapters are unknown, and listener 6 reads as absent
        }
    }

    /// <summary>
    /// A source named as the record shows it: a loopback address as itself, this machine's other addresses by their adapter's name
    /// (so a record carries no LAN address), anything else as <see cref="Elsewhere"/>.
    /// </summary>
    public static Func<IPAddress, string> Names()
    {
        var map = new Dictionary<IPAddress, string>();
        try
        {
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            foreach (var u in n.GetIPProperties().UnicastAddresses)
                map.TryAdd(u.Address, n.Name);
        }
        catch (NetworkInformationException)
        {
            map.Clear();   // unnamed: every source that is not loopback then reads as another machine's, which the record says
        }
        return a => IPAddress.IsLoopback(a) ? a.ToString() : map.TryGetValue(a, out var name) ? name : Elsewhere;
    }

    /// <summary>
    /// An address a socket is bound to, as the record shows it: every interface, loopback, a group or the broadcast address as
    /// itself; this machine's own by its adapter (no LAN address in a record); anything else said as such.
    /// </summary>
    public static string BoundName(IPAddress a, Func<IPAddress, string> names)
    {
        if (a.Equals(IPAddress.Any)) return "0.0.0.0";
        if (a.Equals(IPAddress.IPv6Any)) return "[::]";
        if (IPAddress.IsLoopback(a) || a.IsIPv6Multicast || a.Equals(IPAddress.Broadcast)
            || (a.AddressFamily == AddressFamily.InterNetwork && a.GetAddressBytes()[0] is >= 224 and <= 239))
            return a.ToString();
        var name = names(a);
        return name == Elsewhere ? "an address no adapter here has" : name + "'s address";
    }
}

/// <summary>The machine's own table of UDP sockets (every process's, both address families), read for one port.</summary>
internal static class PortCheck
{
    /// <summary>The address of every socket bound on <paramref name="port"/>; null, with the reason, when the table cannot be read.</summary>
    public static IReadOnlyList<IPAddress>? Bound(int port, out string error)
    {
        error = "";
        try
        {
            return IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Where(e => e.Port == port).Select(e => e.Address).ToList();
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or IOException
                                       or UnauthorizedAccessException or FormatException or InvalidOperationException)
        {
            error = ex.GetType().Name;
            return null;
        }
    }

    /// <summary>What is bound beyond the probe's own sockets: each address as many times as it is bound more often than the probe bound it.</summary>
    public static List<IPAddress> Others(IEnumerable<IPAddress> bound, IEnumerable<IPAddress> ours)
    {
        var left = ours.GroupBy(a => a).ToDictionary(g => g.Key, g => g.Count());
        var others = new List<IPAddress>();
        foreach (var a in bound)
        {
            if (left.TryGetValue(a, out var n) && n > 0) left[a] = n - 1;
            else others.Add(a);
        }
        return others;
    }

    /// <summary>A △ line when anything is bound on the port before the probe binds it, or when the table cannot be read.</summary>
    public static string? Before(int port, Func<IPAddress, string> names)
    {
        var bound = Bound(port, out var error);
        if (bound is null) return Unread(port, error);
        return bound.Count == 0
            ? null
            : Program.Inv($"- △ Before the probe bound, port {port} was already bound on {List(bound, names)}: an app on this machine listens there (one bound to loopback says no hello, so only this check sees it) and may take a unicast this record would miss.");
    }

    /// <summary>A △ line when, while the probe listens, the port holds a socket that is not one of <paramref name="ours"/>.</summary>
    public static string? During(int port, IEnumerable<IPAddress> ours, Func<IPAddress, string> names)
    {
        var bound = Bound(port, out var error);
        if (bound is null) return Unread(port, error);
        var others = Others(bound, ours);
        return others.Count == 0
            ? null
            : Program.Inv($"- △ While the probe listened, port {port} was also bound on {List(others, names)}, besides the probe's own sockets: an app on this machine listened there and may have taken a unicast this record misses.");
    }

    private static string Unread(int port, string error)
        => Program.Inv($"- △ The machine's table of UDP sockets could not be read ({error}): an app on port {port} that says nothing would go unseen.");

    private static string List(IEnumerable<IPAddress> addresses, Func<IPAddress, string> names)
        => string.Join(", ", addresses.Select(a => Machine.BoundName(a, names)).GroupBy(n => n, StringComparer.Ordinal)
            .Select(g => g.Count() == 1 ? g.Key : Program.Inv($"{g.Key} ({g.Count()} sockets)")));
}

internal static class Probe
{
    private const string OneTitle = "Listeners in one process";
    private const string ApartTitle = "Each listener in a process of its own";

    private const string LinuxReuse = " (on Linux .NET sets SO_REUSEPORT with it, as it does in Stagewatch, Patterns and PlayMap, so a unicast to "
                                      + "an address several sockets share goes to one of them by a hash of where it came from; Node's reuseAddr, "
                                      + "AGMessage's, sets SO_REUSEADDR alone)";

    public static async Task<int> RunAsync(Options o, TextWriter w)
    {
        var adapters = Machine.AdaptersUp();
        var adapter = Machine.FirstAdapter();
        var names = Machine.Names();
        w.WriteLine("## LB-1: the same-machine carrier");
        w.WriteLine();
        w.WriteLine(Program.Inv($"- machine: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}) · {RuntimeInformation.FrameworkDescription} · {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC"));
        w.WriteLine(adapters.Count > 0
            ? "- network: **up** (adapters up with IPv4 besides loopback: " + string.Join(", ", adapters) + ")"
            : "- network: **every adapter off** (no adapter but loopback is up with IPv4)");
        w.WriteLine(Program.Inv($"- port {o.Port}{(o.Port == Options.HelloPort ? " (the hello port)" : ", not the hello port")} · {o.Count} datagrams to each carrier, listened for {o.WaitMs} ms after each"));
        w.WriteLine("- listeners, bound in this order, each IPv4 with SO_REUSEADDR" + (OperatingSystem.IsLinux() ? LinuxReuse : "") + ":");
        foreach (var l in Layout.Listeners)
        {
            var here = l.Bind != BindTo.Adapter ? "" : adapter is null ? " (this run: none, no adapter is up)" : $" (this run: {adapter.Name}'s address)";
            w.WriteLine($"  - {l.Column}: {l.Why}{here}");
        }
        w.WriteLine("- carriers, each sent from a socket of its own (the unicast and the broadcasts unbound, following the route, as Stagewatch's hello leaves):");
        foreach (var c in Layout.Carriers) w.WriteLine($"  - {c.Label}: {c.Why}");
        w.WriteLine("- the port's sockets: the machine's table of UDP sockets (every process's) is read before each layout binds and while it listens; a socket on the port that is not the probe's, or a datagram from this machine that is not, leaves that layout's verdicts unclaimed (△)");

        var records = new List<LayoutRecord>();
        if (o.OneProcess) records.Add(await InOneProcessAsync(o, adapter, names));
        if (o.Apart) records.Add(await ApartAsync(o, adapter, names));
        foreach (var r in records) Print(w, r, o);
        Verdict(w, records, o, adapters.Count > 0);
        return records.All(r => r.Claimed) ? 0 : 1;
    }

    private static async Task<LayoutRecord> InOneProcessAsync(Options o, Machine.AdapterAddress? adapter, Func<IPAddress, string> names)
    {
        var run = Payload.NewRun();
        var tally = new Tally(names);
        var listeners = new List<Listener>();
        var loops = new List<Task<string?>>();
        using var stop = new CancellationTokenSource();
        var before = PortCheck.Before(o.Port, names);
        try
        {
            for (var i = 0; i < Layout.Listeners.Length; i++)
            {
                var spec = Layout.Listeners[i];
                var listener = new Listener(spec, Layout.Address(spec.Bind, adapter?.Address), o.Port);
                listeners.Add(listener);
                var index = i;
                loops.Add(listener.ListenAsync(run, heard => tally.Add(index, heard), stop.Token));
            }
            var states = listeners.Select(l => l.State).ToArray();
            if (!states.Any(s => s.Bound)) return LayoutRecord.NotMeasured(OneTitle, NoneBound(o, states), Warnings(o.Port, before, null, null));
            var sends = await Sender.SendAllAsync(o, run);
            var during = PortCheck.During(o.Port, Ours(states, adapter), names);
            await stop.CancelAsync();
            var stopped = await Task.WhenAll(loops);
            states = states.Select((s, i) => stopped[i] is { } why ? s with { Stopped = why } : s).ToArray();
            return new LayoutRecord(OneTitle, states, sends, tally, null, Warnings(o.Port, before, during, tally));
        }
        finally
        {
            foreach (var l in listeners) l.Dispose();
        }
    }

    private static async Task<LayoutRecord> ApartAsync(Options o, Machine.AdapterAddress? adapter, Func<IPAddress, string> names)
    {
        var run = Payload.NewRun();
        var tally = new Tally(names);
        var children = new List<(int Index, Process Child, Task<string?> Reader)>();
        var states = new ListenerState[Layout.Listeners.Length];
        var before = PortCheck.Before(o.Port, names);
        try
        {
            // One process at a time, each bound before the next starts, so the bind order is the columns' order as in one process.
            for (var i = 0; i < Layout.Listeners.Length; i++)
            {
                var spec = Layout.Listeners[i];
                if (Layout.Address(spec.Bind, adapter?.Address) is not { } bind)
                {
                    states[i] = ListenerState.NoAdapter;
                    continue;
                }
                var child = Process.Start(Apart.StartInfo(o, i, run, spec.Bind == BindTo.Adapter ? bind : null))
                            ?? throw new InvalidOperationException(Program.Inv($"listener {i + 1}'s process did not start"));
                children.Add((i, child, Task.FromResult<string?>(null)));   // listed before anything can fail, so the finally stops it
                using var ready = new CancellationTokenSource(TimeSpan.FromSeconds(Layout.ReadySeconds));
                var first = await child.StandardOutput.ReadLineAsync(ready.Token)
                            ?? throw new InvalidOperationException(Program.Inv($"listener {i + 1}'s process ended before it bound"));
                states[i] = Apart.ReadyLine(first, i);
                children[^1] = (i, child, Apart.ReadHeardAsync(child, i, tally));
            }
            if (!states.Any(s => s.Bound)) return LayoutRecord.NotMeasured(ApartTitle, NoneBound(o, states), Warnings(o.Port, before, null, null));
            var sends = await Sender.SendAllAsync(o, run);
            var during = PortCheck.During(o.Port, Ours(states, adapter), names);
            foreach (var c in children) Apart.EndInput(c.Child);
            using var done = new CancellationTokenSource(TimeSpan.FromSeconds(Layout.ReadySeconds));
            var stopped = await Task.WhenAll(children.Select(c => c.Reader)).WaitAsync(done.Token);
            await Task.WhenAll(children.Select(c => c.Child.WaitForExitAsync(done.Token)));
            for (var k = 0; k < children.Count; k++)
            {
                var (index, child, _) = children[k];
                if (!states[index].Bound) continue;
                var why = stopped[k] ?? (child.ExitCode != 0 ? Program.Inv($"its process ended with exit code {child.ExitCode}") : null);
                if (why is not null) states[index] = states[index] with { Stopped = why };
            }
            return new LayoutRecord(ApartTitle, states, sends, tally, null, Warnings(o.Port, before, during, tally));
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException or IOException or FormatException
                                       or System.ComponentModel.Win32Exception)
        {
            return LayoutRecord.NotMeasured(ApartTitle, ex.Message, Warnings(o.Port, before, null, null));
        }
        finally
        {
            foreach (var (_, c, _) in children)
            {
                try
                {
                    if (!c.HasExited) c.Kill();
                }
                catch (InvalidOperationException)
                {
                    // It ended between the check and the kill: nothing is left to stop.
                }
                c.Dispose();
            }
        }
    }

    /// <summary>The addresses the probe's bound listeners hold, for <see cref="PortCheck.During"/>.</summary>
    private static IEnumerable<IPAddress> Ours(ListenerState[] states, Machine.AdapterAddress? adapter)
        => Layout.Listeners.Select((l, i) => states[i].Bound ? Layout.Address(l.Bind, adapter?.Address) : null).OfType<IPAddress>();

    /// <summary>A layout's △ lines: something bound on the port besides the probe, before or during, and datagrams from this machine that were not the probe's.</summary>
    private static List<string> Warnings(int port, string? before, string? during, Tally? tally)
    {
        var warnings = new List<string>();
        if (before is not null) warnings.Add(before);
        if (during is not null) warnings.Add(during);
        if (tally?.Foreign() is { Here: > 0 } foreign)
            warnings.Add(Program.Inv($"- △ {foreign.Here} datagrams that were not this run's came from this machine ({string.Join(", ", foreign.HereFrom)}): an app on it sends to port {port}, and one that listens there too may take a unicast this record misses."));
        return warnings;
    }

    private static string NoneBound(Options o, ListenerState[] states)
        => Program.Inv($"no listener could bind port {o.Port} ({string.Join(", ", states.Where(s => !s.Absent).Select(s => s.Words).Distinct())})");

    private static void Print(TextWriter w, LayoutRecord r, Options o)
    {
        w.WriteLine();
        w.WriteLine("### " + r.Title);
        w.WriteLine();
        if (r.Tally is null)
        {
            w.WriteLine("Not measured: " + r.Failure);
            PrintWarnings(w, r, o);
            return;
        }
        w.WriteLine("| carrier | sent | " + string.Join(" | ", Layout.Listeners.Select(l => l.Column)) + " | every node |");
        w.WriteLine("|---|---|" + string.Concat(Layout.Listeners.Select(_ => "---|")) + "---|");
        for (var c = 0; c < Layout.Carriers.Length; c++)
        {
            var cells = new List<string> { Layout.Carriers[c].Label, r.Sends[c].Words(o.Count) };
            for (var l = 0; l < Layout.Listeners.Length; l++)
                cells.Add(r.Listeners[l] switch
                {
                    { Bound: true } s => r.Tally.Cell(l, c, o.Count) + (s.Stopped is null ? "" : " · stopped"),
                    { Absent: true } => "none",   // the Listeners line under the table says why
                    var s => s.Shown,
                });
            cells.Add(LayoutRecord.Words(r.EveryNode(c, o.Count)));
            w.WriteLine("| " + string.Join(" | ", cells) + " |");
        }
        w.WriteLine();
        w.WriteLine("Listeners: " + string.Join(" · ", r.Listeners.Select((s, i) => Program.Inv($"{i + 1} {s.Shown}"))) + ".");
        if (r.Tally.Foreign().Elsewhere is > 0 and var elsewhere)
        {
            w.WriteLine();
            w.WriteLine(Program.Inv($"{elsewhere} datagrams from other machines were heard (their hellos); they take nothing from this machine's carriers."));
        }
        PrintWarnings(w, r, o);
    }

    private static void PrintWarnings(TextWriter w, LayoutRecord r, Options o)
    {
        if (r.Warnings.Count == 0) return;
        w.WriteLine();
        foreach (var line in r.Warnings) w.WriteLine(line);
        w.WriteLine();
        w.WriteLine(Program.Inv($"Its verdicts are left unclaimed: close Stagewatch, Patterns, PlayMap and AGMessage (on an installed PlayMap node, PlayMap.Supervisor.exe stop holds its engine until resume), or pass --port with a port nothing else uses instead of {o.Port}, and run it again."));
    }

    private static void Verdict(TextWriter w, IReadOnlyList<LayoutRecord> records, Options o, bool networkUp)
    {
        w.WriteLine();
        w.WriteLine("### What this run says");
        w.WriteLine();
        foreach (var r in records)
        {
            if (r.Tally is null)
            {
                w.WriteLine($"- {r.Title}: not measured: {r.Failure}.");
                continue;
            }
            if (!r.Claimed)
            {
                w.WriteLine(Program.Inv($"- {r.Title}: unclaimed: something besides the probe was on port {o.Port}, or the port's sockets could not be read (△ above); run it again."));
                continue;
            }
            var reach = Enumerable.Range(0, Layout.Carriers.Length).Select(c => r.EveryNode(c, o.Count)).ToArray();
            var every = Layout.Carriers.Where((_, c) => reach[c] == Reach.Yes).Select(c => c.Label).ToList();
            var unmeasured = Layout.Carriers.Where((_, c) => reach[c] == Reach.NotMeasured).Select(c => c.Label).ToList();
            w.WriteLine((every.Count == 0 ? $"- {r.Title}: no carrier reached every node." : $"- {r.Title}: reached every node: {string.Join(", ", every)}.")
                        + (unmeasured.Count == 0 ? "" : $" Not measured for {string.Join(", ", unmeasured)}: a listener that counts did not bind or stopped (its Listeners line says why)."));
        }
        w.WriteLine("- Every node: every listener heard every datagram; for the multicast carrier, every listener that joined (a node using it");
        w.WriteLine("  would join), with listener 3 shown for the readers of today, which join nothing; listener 6 only while an adapter is up.");
        w.WriteLine("- Show Bus 1.3 names no carrier for apps on one machine with no network (docs/SHOWBUS.md §2: a typed 127.0.0.1 address");
        w.WriteLine("  links them); 1.4 names one from the Windows records (docs/WHATS_NEXT.md, lab line LB-1).");
        w.WriteLine(networkUp
            ? "- This is the network-up record: turn every adapter off (Wi-Fi, Ethernet and the virtual ones: Hyper-V, WSL, VPN) and run it again."
            : "- This is the every-adapter-off record: turn the network on and run it again.");
    }
}

/// <summary>The apart layout: each listener in a process of its own, started from this executable with <c>--listener</c>.</summary>
internal static class Apart
{
    public static ProcessStartInfo StartInfo(Options o, int index, string run, IPAddress? address)
    {
        var host = Environment.ProcessPath ?? throw new InvalidOperationException("this process's own path is unknown, so no listener can start apart");
        var start = new ProcessStartInfo(host) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        // Started through the dotnet host (dotnet BusCarrierProbe.dll), the host needs the assembly; started as its own executable, not.
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        // The run's count and wait go too: the process's own limit (Layout.ChildLimit) is reckoned from them.
        foreach (var a in new[]
                 {
                     "--listener", Program.Inv($"{index}"), "--port", Program.Inv($"{o.Port}"), "--run", run,
                     "--count", Program.Inv($"{o.Count}"), "--wait-ms", Program.Inv($"{o.WaitMs}"),
                 })
            start.ArgumentList.Add(a);
        if (address is not null)
        {
            start.ArgumentList.Add("--address");
            start.ArgumentList.Add(address.ToString());
        }
        return start;
    }

    /// <summary>Tells a listener process the probe has sent everything: its standard input ends, and it says the rest and stops.</summary>
    public static void EndInput(Process child)
    {
        try
        {
            child.StandardInput.Close();
        }
        catch (IOException)
        {
            // A listener that could not bind has already ended, and its input with it.
        }
    }

    /// <summary>A listener process's first line: "ready &lt;join words&gt;" once bound, or "bind-failed &lt;words&gt;".</summary>
    public static ListenerState ReadyLine(string line, int index)
    {
        if (line.StartsWith("ready ", StringComparison.Ordinal)) return new ListenerState(true, line["ready ".Length..]);
        if (line.StartsWith("bind-failed ", StringComparison.Ordinal)) return new ListenerState(false, line["bind-failed ".Length..]);
        throw new FormatException(Program.Inv($"listener {index + 1}'s process said \"{line}\" before it was ready"));
    }

    /// <summary>
    /// A listener process's lines after its first: "heard &lt;carrier&gt; &lt;seq&gt; &lt;address&gt;", "foreign &lt;address&gt;", and
    /// at most once "stopped &lt;why&gt;", nothing else. Null when it never said it stopped; otherwise why.
    /// </summary>
    public static async Task<string?> ReadHeardAsync(Process child, int index, Tally tally)
    {
        string? stopped = null;
        while (await child.StandardOutput.ReadLineAsync() is { } line)
        {
            var parts = line.Split(' ');
            if (line.StartsWith("stopped ", StringComparison.Ordinal))
                stopped = line["stopped ".Length..];
            else if (parts is ["heard", var c, var s, var a]
                && int.TryParse(c, NumberStyles.None, CultureInfo.InvariantCulture, out var carrier)
                && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
                && IPAddress.TryParse(a, out var from) && carrier < Layout.Carriers.Length)
                tally.Add(index, new Heard(carrier, seq, from));
            else if (parts is ["foreign", var f] && IPAddress.TryParse(f, out var foreign))
                tally.Add(index, Heard.Foreign(foreign));
            else
                throw new FormatException(Program.Inv($"listener {index + 1}'s process said \"{line}\""));
        }
        return stopped;
    }

    /// <summary>
    /// A listener's own process: bind, say "ready", then a line for each datagram until the probe closes standard input. One whose
    /// socket stops, or that reaches <see cref="Layout.ChildLimit"/> first, says "stopped" and why, so the probe never reads a short
    /// count as a measured one.
    /// </summary>
    public static async Task<int> ListenAsync(Options o, int index)
    {
        var spec = Layout.Listeners[index];
        using var listener = new Listener(spec, Layout.Address(spec.Bind, o.Address), o.Port);
        if (!listener.State.Bound)
        {
            Console.WriteLine("bind-failed " + listener.State.Words);
            return 1;
        }
        // Said before the first receive: the socket is bound, so a datagram that comes now waits in it, and no line comes first.
        Console.WriteLine("ready " + listener.State.Words);
        using var stop = new CancellationTokenSource();
        var loop = listener.ListenAsync(o.Run, heard => Console.WriteLine(heard.Carrier < 0
            ? Program.Inv($"foreign {heard.From}")
            : Program.Inv($"heard {heard.Carrier} {heard.Seq} {heard.From}")), stop.Token);
        var limit = Layout.ChildLimit(o);
        var inputEnded = Task.Run(() => Console.In.ReadToEnd());
        var first = await Task.WhenAny(inputEnded, Task.Delay(limit), loop);
        await stop.CancelAsync();
        var stopped = await loop;
        // After the loop has ended, so no "heard" line follows it.
        if (first != inputEnded)
            Console.WriteLine("stopped " + (stopped ?? Program.Inv($"it reached its limit of {limit.TotalSeconds:0} s before the probe had sent everything")));
        return 0;
    }
}
