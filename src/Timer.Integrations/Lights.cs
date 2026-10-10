using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>An RGB colour plus whether it should be flashing, derived from the show state.</summary>
public readonly record struct CueLight(byte R, byte G, byte B, FlashRate Flash, bool Off)
{
    public static CueLight FromHex(string hex, FlashRate flash)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            return new CueLight(255, 255, 255, flash, false);
        return new CueLight((byte)(v >> 16), (byte)(v >> 8), (byte)v, flash, false);
    }

    public static readonly CueLight Dark = new(0, 0, 0, FlashRate.Off, true);
}

/// <summary>Where a cue light gets sent.</summary>
public interface ILightSink : IDisposable
{
    string Name { get; }
    void Send(CueLight light, bool flashOn);
}

/// <summary>
/// Lectern / stage-edge cue lights driven by the show state: off while normal (or a dim white),
/// then the cue ladder's colours, flashing with the cue. The thing hardware speaker timers are kept
/// for — a light the speaker can't miss — but rundown-aware and on cheap DMX / Hue fixtures.
///
/// It's a renderer: it reads the latest snapshot, never writes show state. A 30 Hz timer drives
/// flashing and re-sends DMX continuously (fixtures expect a steady stream).
/// </summary>
public sealed class CueLightOutput : IIntegration
{
    private readonly ILightSink[] _sinks;
    private readonly Func<ShowSnapshot> _latest;
    private readonly Timer _timer;
    private int _tick;
    private int _busy;

    public CueLightOutput(Func<ShowSnapshot> latest, params ILightSink[] sinks)
    {
        _latest = latest;
        _sinks = sinks;
        _timer = new Timer(_ => Tick(), null, 0, 33);
        Status = "driving " + string.Join(", ", sinks.Select(s => s.Name));
    }

    public string Name => "Cue lights";
    public LinkHealth Health => _sinks.Length == 0 ? LinkHealth.Off : LinkHealth.Ok;
    public string Status { get; }

    /// <summary>Pure: what the light should be for this frame.</summary>
    public static CueLight LightFor(ShowSnapshot s)
    {
        if (s.Blackout) return CueLight.Dark;
        if (s.Mode != TimerMode.Countdown || s.State is TimerState.Idle) return CueLight.Dark;
        if (s.Phase == TimerPhase.Normal) return CueLight.Dark;
        var flash = s.Phase == TimerPhase.Overtime || s.FlashTimer ? (s.Flash == FlashRate.Off ? FlashRate.Slow : s.Flash) : FlashRate.Off;
        return CueLight.FromHex(s.ColorHex, flash);
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // never overlap: a slow sink skips a frame
        try
        {
            _tick++;
            var light = LightFor(_latest());
            // Slow flash ~1 Hz, fast ~3 Hz at 30 ticks/s.
            var on = light.Flash switch
            {
                FlashRate.Slow => _tick / 15 % 2 == 0,
                FlashRate.Fast => _tick / 5 % 2 == 0,
                _ => true
            };
            foreach (var sink in _sinks)
            {
                try { sink.Send(light, on); } catch (Exception) { /* a fixture problem must not stop the show */ }
            }
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    public void Dispose()
    {
        _timer.Dispose();
        foreach (var s in _sinks)
        {
            try { s.Send(CueLight.Dark, false); } catch { }
            s.Dispose();
        }
    }
}

/// <summary>Builds the DMX slot values for a fixture in the chosen mode.</summary>
public static class DmxFixture
{
    public static void Write(Span<byte> universe, int address, DmxMode mode, CueLight light, bool flashOn)
    {
        var on = !light.Off && flashOn;
        byte r = on ? light.R : (byte)0, g = on ? light.G : (byte)0, b = on ? light.B : (byte)0;
        Span<byte> values = stackalloc byte[4];
        var n = 0;
        switch (mode)
        {
            case DmxMode.Rgb: values[0] = r; values[1] = g; values[2] = b; n = 3; break;
            case DmxMode.Rgbw: values[0] = r; values[1] = g; values[2] = b; values[3] = 0; n = 4; break;
            case DmxMode.RgbDimmer: values[0] = (byte)(on ? 255 : 0); values[1] = r; values[2] = g; values[3] = b; n = 4; break;
            case DmxMode.Dimmer: values[0] = (byte)(on ? Math.Max(r, Math.Max(g, b)) : 0); n = 1; break;
        }
        var start = Math.Clamp(address, 1, 512) - 1;
        for (int k = 0; k < n && start + k < universe.Length; k++) universe[start + k] = values[k];
    }
}

/// <summary>Art-Net 4 ArtDmx packets over UDP 6454 (broadcast or unicast to a node).</summary>
public sealed class ArtNetSink : ILightSink
{
    private readonly UdpClient _udp = new();
    private readonly IPEndPoint _target;
    private readonly int _universe;
    private readonly int _address;
    private readonly DmxMode _mode;
    private readonly byte[] _dmx = new byte[512];
    private byte _sequence;

    public ArtNetSink(string host, int universe, int address, DmxMode mode)
    {
        _udp.EnableBroadcast = true;
        _target = new IPEndPoint(IPAddress.TryParse(host, out var ip) ? ip : IPAddress.Broadcast, 6454);
        _universe = Math.Max(0, universe);
        _address = address;
        _mode = mode;
    }

    public string Name => $"Art-Net {_target.Address} U{_universe}";

    public void Send(CueLight light, bool flashOn)
    {
        DmxFixture.Write(_dmx, _address, _mode, light, flashOn);
        var p = Packet(_dmx, _universe, ++_sequence == 0 ? _sequence = 1 : _sequence);
        _udp.Send(p, p.Length, _target);
    }

    public static byte[] Packet(ReadOnlySpan<byte> dmx, int universe, byte sequence)
    {
        var p = new byte[18 + 512];
        Encoding.ASCII.GetBytes("Art-Net\0").CopyTo(p, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(8), 0x5000); // OpDmx
        p[10] = 0; p[11] = 14;                                          // protocol version 14
        p[12] = sequence;
        p[13] = 0;                                                      // physical
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(14), (ushort)(universe & 0x7FFF)); // SubUni + Net
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(16), 512);
        dmx[..Math.Min(512, dmx.Length)].CopyTo(p.AsSpan(18));
        return p;
    }

    public void Dispose() => _udp.Dispose();
}

/// <summary>sACN (ANSI E1.31) multicast to 239.255.u.u:5568.</summary>
public sealed class SacnSink : ILightSink
{
    private readonly UdpClient _udp = new();
    private readonly IPEndPoint _target;
    private readonly int _universe;
    private readonly int _address;
    private readonly DmxMode _mode;
    private readonly byte[] _dmx = new byte[512];
    private readonly Guid _cid = Guid.NewGuid();
    private byte _sequence;

    public SacnSink(int universe, int address, DmxMode mode)
    {
        _universe = Math.Clamp(universe, 1, 63999);
        _target = new IPEndPoint(new IPAddress(new byte[] { 239, 255, (byte)(_universe >> 8), (byte)_universe }), 5568);
        _address = address;
        _mode = mode;
        _udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 8);
    }

    public string Name => $"sACN U{_universe}";

    public void Send(CueLight light, bool flashOn)
    {
        DmxFixture.Write(_dmx, _address, _mode, light, flashOn);
        var p = Packet(_dmx, _universe, ++_sequence, _cid, "Stagewatch");
        _udp.Send(p, p.Length, _target);
    }

    public static byte[] Packet(ReadOnlySpan<byte> dmx, int universe, byte sequence, Guid cid, string source)
    {
        const int dmxLen = 513; // start code + 512
        var p = new byte[126 + 512];
        // Root layer
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(0), 0x0010);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), 0x0000);
        Encoding.ASCII.GetBytes("ASC-E1.17\0\0\0").CopyTo(p, 4);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(16), (ushort)(0x7000 | (p.Length - 16)));
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(18), 0x00000004);
        cid.ToByteArray().CopyTo(p, 22);
        // Framing layer
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(38), (ushort)(0x7000 | (p.Length - 38)));
        BinaryPrimitives.WriteUInt32BigEndian(p.AsSpan(40), 0x00000002);
        var name = Encoding.UTF8.GetBytes(source);
        name.AsSpan(0, Math.Min(63, name.Length)).CopyTo(p.AsSpan(44));
        p[108] = 100; // priority
        p[111] = sequence;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(113), (ushort)universe);
        // DMP layer
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(115), (ushort)(0x7000 | (p.Length - 115)));
        p[117] = 0x02; p[118] = 0xA1;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(119), 0x0000);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(121), 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(123), dmxLen);
        p[125] = 0; // start code
        dmx[..Math.Min(512, dmx.Length)].CopyTo(p.AsSpan(126));
        return p;
    }

    public void Dispose() => _udp.Dispose();
}

/// <summary>
/// Philips Hue (local bridge API v1). Rate-limited — the bridge takes ~10 commands/s in total —
/// so we only send on change, at most once a second per light, and flash with the bridge's own
/// "alert" effect instead of toggling.
/// </summary>
public sealed class HueSink : ILightSink
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly string _base;
    private readonly string[] _lights;
    private CueLight? _last;
    private long _lastSentMs;
    private int _sending;

    public HueSink(string bridge, string user, string lights)
    {
        _base = $"http://{bridge.Trim()}/api/{user.Trim()}/lights/";
        _lights = lights.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public string Name => $"Hue ({_lights.Length} light{(_lights.Length == 1 ? "" : "s")})";

    public void Send(CueLight light, bool flashOn)
    {
        var now = Environment.TickCount64;
        if (_last == light || now - _lastSentMs < 1000) return;
        if (Interlocked.Exchange(ref _sending, 1) == 1) return;
        _last = light;
        _lastSentMs = now;
        var body = Body(light);
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var id in _lights)
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    await _http.PutAsync(_base + Uri.EscapeDataString(id) + "/state", content);
                }
            }
            catch (Exception) { /* bridge offline: next change retries */ }
            finally { Volatile.Write(ref _sending, 0); }
        });
    }

    /// <summary>Hue v1 state JSON for a cue light (RGB → hue/sat/bri).</summary>
    public static string Body(CueLight light)
    {
        if (light.Off) return "{\"on\":false}";
        var (h, s, v) = ToHsv(light.R, light.G, light.B);
        var alert = light.Flash == FlashRate.Off ? "none" : "lselect";
        return string.Create(CultureInfo.InvariantCulture,
            $"{{\"on\":true,\"hue\":{(int)(h / 360 * 65535)},\"sat\":{(int)(s * 254)},\"bri\":{Math.Max(1, (int)(v * 254))},\"transitiontime\":2,\"alert\":\"{alert}\"}}");
    }

    private static (double H, double S, double V) ToHsv(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * ((g - b) / d % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public void Dispose() => _http.Dispose();
}
