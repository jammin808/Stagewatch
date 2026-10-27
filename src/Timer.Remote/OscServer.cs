using System.Net;
using System.Net.Sockets;
using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// OSC over UDP for show-control tools (QLab, vMix, Bitfocus, lighting desks).
///
/// In:  /stagewatch/&lt;verb&gt;[/&lt;arg&gt;...] [args...]   e.g. /stagewatch/start,
///      /stagewatch/preset/3 "start", /stagewatch/nudge 60, /stagewatch/message "Wrap up" "warn".
///      The "/stagewatch" prefix is optional. Verbs are the shared command grammar.
///      OSC carries no credential: a line that drives another node's air (playout, patterns) runs only from this PC or an
///      address the operator listed (<see cref="PlayoutGate"/>); every other verb runs from any sender, as it always has.
/// Out: optional feedback to host:port — only the fields that changed, one bundle per change.
/// </summary>
public sealed class OscServer : IDisposable
{
    private readonly IShowCommands _control;
    private readonly IShowAccess? _access;
    private UdpClient? _listener;
    private UdpClient? _sender;
    private IPEndPoint? _feedback;
    private CancellationTokenSource? _cts;
    private ShowSnapshot? _lastSent;
    private readonly object _feedbackLock = new();

    /// <param name="access">Who may drive another node's air without a credential (its <see cref="IShowAccess.PlayoutFrom"/>); none
    /// given, those lines run only from this PC.</param>
    public OscServer(IShowCommands control, IShowAccess? access = null)
    {
        _control = control;
        _access = access;
    }

    /// <summary>
    /// The local-address seam: whether a sender is this PC (loopback, an IPv4-mapped one included). Fixed when the server is made;
    /// a test plays a sender on another machine from loopback with it.
    /// </summary>
    public Func<IPAddress, bool> IsThisPc { get; init; } = PlayoutGate.IsLoopback;

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }

    /// <summary>The door as <see cref="Start"/> bound it, published whole once the listener runs; null while it does not.</summary>
    public BoundDoor? Door => _door;
    private volatile BoundDoor? _door;
    public long MessagesReceived => Interlocked.Read(ref _received);
    public long MessagesRejected => Interlocked.Read(ref _rejected);
    private long _received, _rejected;

    /// <summary>Raised (on a worker thread) when a command is rejected — for the status line.</summary>
    public event Action<string>? CommandRejected;

    public void Start(int port, bool bindAll, string? feedbackHost, int feedbackPort)
    {
        if (_listener is not null) return;
        _listener = new UdpClient(new IPEndPoint(bindAll ? IPAddress.Any : IPAddress.Loopback, port));
        IgnoreConnReset(_listener);
        Port = port;
        _door = new BoundDoor(port, bindAll);
        _cts = new CancellationTokenSource();
        var listener = _listener;
        var token = _cts.Token;
        // Thread pool, not the caller's (UI) context: network I/O must never run on the show thread.
        _ = Task.Run(() => ReceiveLoopAsync(listener, token));

        if (!string.IsNullOrWhiteSpace(feedbackHost) && feedbackPort is > 0 and < 65536)
        {
            _sender = new UdpClient(AddressFamily.InterNetwork);
            IgnoreConnReset(_sender);
            var host = feedbackHost.Trim();
            _ = Task.Run(() => ResolveFeedbackAsync(host, feedbackPort, token));
        }
    }

    public void Stop()
    {
        _door = null;
        _control.Snapshots.Unsubscribe(OnPublished);
        _cts?.Cancel();
        try { _listener?.Dispose(); } catch { }
        try { _sender?.Dispose(); } catch { }
        _listener = null;
        _sender = null;
        _feedback = null;
        _lastSent = null;
    }

    public void Dispose() => Stop();

    private async Task ResolveFeedbackAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            if (!IPAddress.TryParse(host, out var ip))
            {
                var addrs = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
                ip = addrs.FirstOrDefault();
            }
            if (ip is null || ct.IsCancellationRequested) return;
            _feedback = new IPEndPoint(ip, port);
            _control.Snapshots.Subscribe(OnPublished);
            OnPublished(_control.Snapshots.Latest);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            CommandRejected?.Invoke($"OSC feedback host '{host}' not found");
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult packet;
            try { packet = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; } // transient (e.g. ICMP noise) — keep listening

            if (packet.Buffer.Length > 8192) { Interlocked.Increment(ref _rejected); continue; }
            foreach (var msg in OscCodec.Decode(packet.Buffer))
            {
                Interlocked.Increment(ref _received);
                Dispatch(msg, packet.RemoteEndPoint.Address);
            }
        }
    }

    private void Dispatch(OscMessage msg, IPAddress sender)
    {
        var segments = msg.Address.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments.Count > 0 && segments[0].Equals("stagewatch", StringComparison.OrdinalIgnoreCase))
            segments.RemoveAt(0);
        if (segments.Count == 0) { Reject("empty address"); return; }

        var verb = segments[0];
        var args = segments.Skip(1).Concat(msg.Args.Select(OscCodec.ArgToString)).ToList();

        // A bare trigger with value 0 is a button release on most control surfaces — ignore it,
        // so start/next don't fire twice (press = 1, release = 0).
        if (args.Count == 1 && segments.Count == 1 && msg.Args.Count == 1 && IsTrigger(verb) && args[0] is "0")
            return;
        if (IsTrigger(verb) && segments.Count == 1 && msg.Args.Count == 1) args.Clear();

        if (!ShowCommandParser.TryParse(verb, args, out var cmd, out var error)) { Reject(error); return; }
        // Refused before the show thread hears of it: nothing reaches a peer's air for a sender that proved nothing.
        var from = new CommandSender(sender, IsThisPc(sender), Credentialed: false);
        if (PlayoutGate.Check(cmd.Kind, CommandDoor.Osc, from, _access?.PlayoutFrom ?? []) is { } refused) { Reject(refused); return; }
        _control.Execute(cmd);
    }

    private static bool IsTrigger(string verb) => verb.ToLowerInvariant() is
        "start" or "play" or "go" or "pause" or "stop" or "toggle" or "reset" or
        "next" or "previous" or "prev" or "back" or "clearmessage" or "clear";

    private void Reject(string error)
    {
        Interlocked.Increment(ref _rejected);
        try { CommandRejected?.Invoke("OSC: " + error); } catch { }
    }

    /// <summary>Called on the owner thread at most once per visible change. Sends deltas only.</summary>
    private void OnPublished(ShowSnapshot s)
    {
        var target = _feedback;
        var sender = _sender;
        if (target is null || sender is null) return;

        List<(string, object)> changes;
        lock (_feedbackLock)
        {
            var p = _lastSent;
            changes = new List<(string, object)>(12);
            if (p is null || p.Text != s.Text) changes.Add(("/stagewatch/time", s.Text));
            if (p is null || p.Seconds != s.Seconds) changes.Add(("/stagewatch/seconds", (int)Math.Clamp(s.Seconds, int.MinValue, int.MaxValue)));
            if (p is null || p.State != s.State) changes.Add(("/stagewatch/state", s.State.ToString().ToLowerInvariant()));
            if (p is null || p.Mode != s.Mode) changes.Add(("/stagewatch/mode", SnapshotJson.ModeName(s.Mode)));
            if (p is null || p.Phase != s.Phase) changes.Add(("/stagewatch/phase", s.Phase.ToString().ToLowerInvariant()));
            if (p is null || p.Title != s.Title) changes.Add(("/stagewatch/title", s.Title));
            if (p is null || p.NextTitle != s.NextTitle) changes.Add(("/stagewatch/next", s.NextTitle));
            if (p is null || p.ItemIndex != s.ItemIndex) changes.Add(("/stagewatch/item", s.ItemIndex + 1));
            if (p is null || p.OffsetSeconds != s.OffsetSeconds) changes.Add(("/stagewatch/offset", (int)(s.OffsetSeconds ?? 0)));
            if (p is null || p.Message != s.Message) changes.Add(("/stagewatch/message", s.Message));
            if (p is null || p.Blackout != s.Blackout) changes.Add(("/stagewatch/blackout", s.Blackout ? 1 : 0));
            if (p is null || p.Seconds != s.Seconds || p.TotalSeconds != s.TotalSeconds)
            {
                var progress = s.TotalSeconds > 0 ? Math.Clamp((float)s.Seconds / s.TotalSeconds, 0f, 1f) : 0f;
                changes.Add(("/stagewatch/progress", progress));
            }
            _lastSent = s;
        }
        if (changes.Count == 0) return;

        try
        {
            var bytes = OscCodec.EncodeBundle(changes);
            sender.Send(bytes, bytes.Length, target);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
    }

    /// <summary>
    /// Windows reports an ICMP "port unreachable" from an earlier send as an exception on the
    /// next receive (WSAECONNRESET). That would kill a naive loop; switch it off.
    /// </summary>
    private static void IgnoreConnReset(UdpClient c)
    {
        const int SIO_UDP_CONNRESET = -1744830452;
        try { c.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null); } catch { }
    }
}
