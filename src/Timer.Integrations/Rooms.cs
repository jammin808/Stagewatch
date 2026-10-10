using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>What each Stagewatch announces about its room once a second.</summary>
public sealed record RoomBeat(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("room")] string Room,
    [property: JsonPropertyName("host")] string Host,
    [property: JsonPropertyName("web")] int WebPort,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("phase")] string Phase,
    [property: JsonPropertyName("offset")] long? Offset,
    [property: JsonPropertyName("hardOut")] string HardOut,
    [property: JsonPropertyName("margin")] long? Margin,
    [property: JsonPropertyName("signals")] int OpenSignals);

/// <summary>
/// Multi-room awareness. Each instance stays the authority for its own room and broadcasts a small
/// summary on the LAN (UDP 9927, ~200 bytes/s); every instance listens, so any of them can show the
/// producer dashboard of all rooms. No server, no cloud, nothing to configure but a room name.
/// </summary>
public sealed class RoomBeacon : IIntegration
{
    private static readonly JsonSerializerOptions Json = new();
    private readonly string _id = Guid.NewGuid().ToString("N")[..12];
    private readonly Func<RoomBeat?> _beat;
    private readonly int _port;
    private readonly UdpClient? _rx;
    private readonly UdpClient _tx = new() { EnableBroadcast = true };
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<string, (RoomBeat Beat, IPAddress From, long SeenMs)> _peers = new();
    private readonly CancellationTokenSource _cts = new();

    public RoomBeacon(int port, Func<string, RoomBeat?> beat)
    {
        _port = port;
        _beat = () => beat(_id);
        try
        {
            var rx = new UdpClient(AddressFamily.InterNetwork);
            rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            rx.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _rx = rx;
            _ = Task.Run(() => ListenAsync(rx, _cts.Token)); // never on the UI thread
            Health = LinkHealth.Ok;
            Status = $"sharing this room on UDP {port}";
        }
        catch (SocketException ex)
        {
            Health = LinkHealth.Error;
            Status = $"can't listen on UDP {port}: {ex.Message}";
        }
        _timer = new Timer(_ => Broadcast(), null, 500, 1000);
    }

    public string Name => "Rooms";
    public LinkHealth Health { get; }
    public string Status { get; }
    public string Id => _id;

    private void Broadcast()
    {
        try
        {
            if (_beat() is not { } b) return;
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(b, Json));
            _tx.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _port));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
    }

    private async Task ListenAsync(UdpClient rx, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await rx.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            if (r.Buffer.Length > 4096) continue;
            try
            {
                var beat = JsonSerializer.Deserialize<RoomBeat>(r.Buffer, Json);
                if (beat is null || beat.Version != 1 || beat.Id == _id || string.IsNullOrEmpty(beat.Id)) continue;
                beat = beat with
                {
                    Room = beat.Room ?? "", Host = beat.Host ?? "", Title = beat.Title ?? "", Text = beat.Text ?? "",
                    State = beat.State ?? "", Phase = beat.Phase ?? "", HardOut = beat.HardOut ?? ""
                };
                if (_peers.Count > 128 && !_peers.ContainsKey(beat.Id)) continue; // bounded
                _peers[beat.Id] = (beat, r.RemoteEndPoint.Address, Environment.TickCount64);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* a bad packet is dropped; keep listening */ }
        }
    }

    /// <summary>Every room we know, newest state first. Stale after 5 s, forgotten after 2 min.</summary>
    public List<RoomView> Rooms()
    {
        var now = Environment.TickCount64;
        var list = new List<RoomView>();
        foreach (var (id, p) in _peers)
        {
            var age = now - p.SeenMs;
            if (age > 120_000) { _peers.TryRemove(id, out _); continue; }
            var b = p.Beat;
            list.Add(new RoomView(b.Room.Length > 0 ? b.Room : b.Host, p.From.ToString(), b.WebPort, b.Title, b.Text, b.State, b.Phase,
                b.Offset, b.HardOut, b.Margin, b.OpenSignals, age > 5000, false));
        }
        return list.OrderBy(r => r.Room, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _rx?.Dispose();
        _tx.Dispose();
    }
}
