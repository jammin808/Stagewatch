namespace Stagewatch.Integrations;

/// <summary>
/// SMPTE 12M linear timecode (LTC) encoder: 80-bit frames, biphase-mark modulated audio. Pure and
/// deterministic, so it is unit-tested by decoding its own output.
/// </summary>
public sealed class LtcEncoder
{
    public const int SampleRate = 48000;
    private readonly int _fps;
    private readonly int _samplesPerFrame;
    private short _level = 1;
    private readonly short _amplitude;

    public LtcEncoder(int fps, double amplitude = 0.5)
    {
        if (fps is not (24 or 25 or 30)) throw new ArgumentOutOfRangeException(nameof(fps), "LTC supports 24, 25 or 30 fps");
        _fps = fps;
        _samplesPerFrame = SampleRate / fps;
        _amplitude = (short)(short.MaxValue * Math.Clamp(amplitude, 0.05, 0.9));
    }

    public int Fps => _fps;
    public int SamplesPerFrame => _samplesPerFrame;

    /// <summary>The 80 bits of one frame, in transmit order.</summary>
    public static bool[] FrameBits(int hours, int minutes, int seconds, int frames, int fps)
    {
        var b = new bool[80];
        void Bcd(int value, int start, int count) { for (int i = 0; i < count; i++) b[start + i] = ((value >> i) & 1) == 1; }
        Bcd(frames % 10, 0, 4);
        Bcd(frames / 10, 8, 2);
        Bcd(seconds % 10, 16, 4);
        Bcd(seconds / 10, 24, 3);
        Bcd(minutes % 10, 32, 4);
        Bcd(minutes / 10, 40, 3);
        Bcd(hours % 10, 48, 4);
        Bcd(hours / 10, 56, 2);
        // Sync word 0011 1111 1111 1101 (bits 64..79).
        var sync = new[] { false, false, true, true, true, true, true, true, true, true, true, true, true, true, false, true };
        Array.Copy(sync, 0, b, 64, 16);
        // Biphase-mark polarity correction: make the number of zeros even (so every frame starts on the
        // same polarity). The bit lives at 27 for 30/24 fps and 59 for 25 fps.
        var polarityBit = fps == 25 ? 59 : 27;
        var zeros = b.Count(x => !x);
        if (zeros % 2 == 1) b[polarityBit] = true;
        return b;
    }

    /// <summary>Write one frame of audio for this timecode.</summary>
    /// <remarks>
    /// The frame number is rounded with a tolerance: times are 100 ns ticks, so e.g. frame 2 at 24 fps is
    /// 1.9999992 frames — a plain floor sent the previous frame again (duplicated, then skipped, frames).
    /// </remarks>
    public void Encode(TimeSpan time, Span<short> output) => Encode((long)Math.Floor(time.TotalSeconds * _fps + 1e-4), output);

    /// <summary>Write one frame of audio for this frame number (frames since midnight).</summary>
    public void Encode(long totalFrames, Span<short> output)
    {
        var frames = (int)(totalFrames % _fps);
        var secs = (int)(totalFrames / _fps % 60);
        var mins = (int)(totalFrames / _fps / 60 % 60);
        var hours = (int)(totalFrames / _fps / 3600 % 24);
        var bits = FrameBits(hours, mins, secs, frames, _fps);
        var perBit = (double)_samplesPerFrame / 80;
        for (int s = 0; s < _samplesPerFrame && s < output.Length; s++)
        {
            var bit = (int)(s / perBit);
            // Transition at the start of every bit cell; a second one mid-cell for a 1.
            if (s == (int)Math.Round(bit * perBit)) _level = (short)-_level;
            else if (bits[Math.Min(bit, 79)] && s == (int)Math.Round((bit + 0.5) * perBit)) _level = (short)-_level;
            output[s] = (short)(_level * _amplitude);
        }
    }
}

/// <summary>
/// Streams LTC on a chosen audio output. The time comes from a callback (time of day, or the
/// countdown as timecode), with the stream's latency added so the code that leaves the speaker is
/// the code for that instant.
/// </summary>
public sealed class LtcOutput : IIntegration
{
    private readonly LtcEncoder _encoder;
    private readonly Func<TimeSpan> _time;
    private readonly WaveStream? _stream;
    private long _frame = -1;

    public LtcOutput(string device, int fps, Func<TimeSpan> time)
    {
        _encoder = new LtcEncoder(fps);
        _time = time;
        if (WavePlayer.DeviceId(device) is not { } id)
        {
            Health = LinkHealth.NotFound;
            Status = $"audio device \"{device}\" not found";
            return;
        }
        // 2 frames per buffer, 4 buffers: ~160 ms latency at 25 fps, robust against UI hiccups.
        _stream = new WaveStream(id, LtcEncoder.SampleRate, _encoder.SamplesPerFrame * 2, 4, Fill);
        _stream.Start();
        Health = LinkHealth.Ok;
        Status = $"LTC {fps} fps on {(device.Length == 0 ? "default device" : device)}";
    }

    public string Name => "LTC";
    public LinkHealth Health { get; }
    public string Status { get; }

    private void Fill(short[] buffer)
    {
        var fps = _encoder.Fps;
        var now = _time();
        if (now < TimeSpan.Zero) now = TimeSpan.Zero; // timecode has no negative: overtime holds at zero
        var target = (long)Math.Floor((now + (_stream?.Latency ?? TimeSpan.Zero)).TotalSeconds * fps);
        // Free-run frame by frame; re-sync on a jump (countdown nudged, clock corrected).
        if (_frame < 0 || Math.Abs(_frame - target) > 2) _frame = target;
        var spf = _encoder.SamplesPerFrame;
        for (int f = 0; f * spf < buffer.Length; f++)
        {
            _encoder.Encode(_frame, buffer.AsSpan(f * spf, Math.Min(spf, buffer.Length - f * spf)));
            _frame++;
        }
    }

    public void Dispose() => _stream?.Dispose();
}
