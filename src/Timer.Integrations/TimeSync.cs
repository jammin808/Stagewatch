using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Stagewatch.Integrations;

/// <summary>Result of one SNTP exchange: how far this PC's clock is from the server's.</summary>
public sealed record ClockCheck(string Server, TimeSpan Offset, TimeSpan RoundTrip, DateTime CheckedUtc);

/// <summary>
/// Clock discipline: asks a time server (SNTP, RFC 4330) how wrong this PC's clock is, at start-up
/// and every 10 minutes. Wall-clock targets ("finish at 14:30", fixed starts, hard-outs) are only as
/// good as the clock, and rooms / remote viewers need clocks that agree. We warn; we never change
/// the system clock.
/// </summary>
public sealed class TimeSync : PollingSensor<ClockCheck>
{
    private readonly string _server;
    private DateTime _nextCheck = DateTime.MinValue;

    public TimeSync(string server) : base("Time server", TimeSpan.FromSeconds(5))
    {
        _server = string.IsNullOrWhiteSpace(server) ? "pool.ntp.org" : server.Trim();
    }

    protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(30);

    protected override ClockCheck? Poll(CancellationToken ct)
    {
        if (DateTime.UtcNow < _nextCheck) return Value;
        var r = Query(_server, TimeSpan.FromSeconds(3), ct);
        _nextCheck = DateTime.UtcNow + (r is null ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(10));
        if (r is null) { Report(LinkHealth.NotFound, $"no answer from {_server}"); return Value; }
        var ms = (int)Math.Round(r.Offset.TotalMilliseconds);
        Report(Math.Abs(ms) > 1000 ? LinkHealth.Error : LinkHealth.Ok,
            Math.Abs(ms) > 1000 ? $"this PC's clock is {Math.Abs(ms) / 1000.0:0.0} s {(ms > 0 ? "slow" : "fast")} — fix Windows time before the show"
                                : $"clock within {Math.Abs(ms)} ms of {_server}");
        return r;
    }

    public static ClockCheck? Query(string server, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            var addresses = Dns.GetHostAddresses(server).Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            if (addresses.Length == 0) return null;
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;
            var req = new byte[48];
            req[0] = 0x1B; // LI 0, version 3, mode 3 (client)
            var t1 = DateTime.UtcNow;
            WriteTimestamp(req.AsSpan(40), t1);
            // A connected socket: the OS binds it to the one outgoing interface rather than a wildcard
            // listener, and only the server can answer it.
            udp.Connect(new IPEndPoint(addresses[0], 123));
            udp.Send(req, req.Length);
            var from = new IPEndPoint(IPAddress.Any, 0);
            var resp = udp.Receive(ref from);
            var t4 = DateTime.UtcNow;
            if (resp.Length < 48) return null;
            var t2 = ReadTimestamp(resp.AsSpan(32));
            var t3 = ReadTimestamp(resp.AsSpan(40));
            var offset = ((t2 - t1) + (t3 - t4)) / 2;
            var rtt = (t4 - t1) - (t3 - t2);
            return new ClockCheck(server, offset, rtt, t4);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException) { return null; }
    }

    private static readonly DateTime Epoch = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime ReadTimestamp(ReadOnlySpan<byte> b)
    {
        var secs = BinaryPrimitives.ReadUInt32BigEndian(b);
        var frac = BinaryPrimitives.ReadUInt32BigEndian(b[4..]);
        return Epoch.AddSeconds(secs).AddTicks((long)(frac / (double)uint.MaxValue * TimeSpan.TicksPerSecond));
    }

    private static void WriteTimestamp(Span<byte> b, DateTime utc)
    {
        var span = utc - Epoch;
        var secs = (uint)Math.Floor(span.TotalSeconds);
        var frac = (uint)((span.TotalSeconds - secs) * uint.MaxValue);
        BinaryPrimitives.WriteUInt32BigEndian(b, secs);
        BinaryPrimitives.WriteUInt32BigEndian(b[4..], frac);
    }
}
