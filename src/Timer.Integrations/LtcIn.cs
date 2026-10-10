using System.Diagnostics;
using System.Globalization;

namespace Stagewatch.Integrations;

/// <summary>One decoded LTC frame (the time at the start of the frame).</summary>
public readonly record struct LtcFrame(int Hours, int Minutes, int Seconds, int Frames, bool DropFrame)
{
    public string Text => $"{Hours:00}:{Minutes:00}:{Seconds:00}{(DropFrame ? ';' : ':')}{Frames:00}";
    /// <summary>Frames since midnight — drop-frame aware, so consecutive frames always differ by one.</summary>
    public long Number(int fps) => Timecode.FrameNumber(Hours, Minutes, Seconds, Frames, fps, DropFrame);
    public TimeSpan ToTime(int fps) => Timecode.FrameTime(Number(fps), fps, DropFrame);
}

/// <summary>
/// Timecode text helpers: "01:02:03:04" (or "01:02:03;04" for drop frame) ⇄ time. Drop frame exists only at
/// 30 fps nominal (29.97): frame labels ;00 and ;01 are skipped at the start of every minute except every tenth,
/// so the labels stay in step with the clock. Times here are real elapsed time from 00:00:00:00.
/// </summary>
public static class Timecode
{
    private const long DfPerTenMinutes = 17982, DfPerMinute = 1798;

    public static bool IsDropFrame(int fps, bool dropFrame) => dropFrame && fps == 30;

    /// <summary>Frames per second of real time: 29.97 for drop frame, otherwise the nominal rate.</summary>
    public static double Rate(int fps, bool dropFrame) => IsDropFrame(fps, dropFrame) ? 30000.0 / 1001 : Math.Max(1, fps);

    public static long FrameNumber(int h, int m, int s, int f, int fps, bool dropFrame)
    {
        var n = ((long)(h * 60 + m) * 60 + s) * fps + f;
        if (IsDropFrame(fps, dropFrame)) { long minutes = h * 60L + m; n -= 2 * (minutes - minutes / 10); }
        return n;
    }

    public static TimeSpan FrameTime(long frameNumber, int fps, bool dropFrame) => TimeSpan.FromSeconds(frameNumber / Rate(fps, dropFrame));

    /// <summary>The label of a frame number (drop-frame labels skip ;00 and ;01 where SMPTE says so).</summary>
    public static (int H, int M, int S, int F) Label(long frameNumber, int fps, bool dropFrame)
    {
        fps = Math.Max(1, fps);
        var n = Math.Max(0, frameNumber);
        if (IsDropFrame(fps, dropFrame))
        {
            long tens = n / DfPerTenMinutes, rest = n % DfPerTenMinutes;
            n += 18 * tens + (rest > 1 ? 2 * ((rest - 2) / DfPerMinute) : 0);
        }
        var f = (int)(n % fps); var secs = n / fps;
        return ((int)(secs / 3600 % 24), (int)(secs / 60 % 60), (int)(secs % 60), f);
    }

    /// <summary>
    /// Parse HH:MM:SS:FF (or HH:MM:SS) at a frame rate. A ';' (or <paramref name="dropFrame"/>) means drop frame at
    /// 30 fps. Null when it isn't a timecode — including a drop-frame label that can't exist (e.g. 01:01:00;00);
    /// written without ';' for a drop-frame source, such a label means the first real frame of that second.
    /// </summary>
    public static TimeSpan? Parse(string? text, int fps, bool dropFrame = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        var df = IsDropFrame(fps, dropFrame || trimmed.Contains(';'));
        var parts = trimmed.Replace(';', ':').Replace('.', ':').Split(':');
        if (parts.Length is not (3 or 4)) return null;
        var n = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i])) return null;
        if (n[0] > 23 || n[1] > 59 || n[2] > 59 || n[3] >= Math.Max(1, fps)) return null;
        if (df && n[2] == 0 && n[1] % 10 != 0 && n[3] < 2)
        {
            if (trimmed.Contains(';')) return null; // written as drop frame, but a label drop frame never produces
            n[3] = 2;                               // typed without ';' for a drop-frame source: the first real frame
        }
        return FrameTime(FrameNumber(n[0], n[1], n[2], n[3], fps, df), fps, df);
    }

    public static string Format(TimeSpan t, int fps, bool dropFrame = false)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        fps = Math.Max(1, fps);
        var df = IsDropFrame(fps, dropFrame);
        var (h, m, s, f) = Label((long)Math.Floor(t.TotalSeconds * Rate(fps, df) + 1e-6), fps, df);
        return $"{h:00}:{m:00}:{s:00}{(df ? ';' : ':')}{f:00}";
    }

    /// <summary>Did rolling timecode pass this point between two readings? (Forward only; a jump — locate, rewind — doesn't count.)</summary>
    public static bool Crossed(TimeSpan previous, TimeSpan now, TimeSpan point)
        => now >= previous && now - previous < TimeSpan.FromSeconds(2) && previous < point && point <= now;
}

/// <summary>
/// SMPTE LTC decoder: biphase-mark audio → frames. Polarity-independent (only transitions count), self-calibrating
/// to the bit rate (24, 25, 29.97 and 30 fps, and varispeed around them) and to the level, with DC removal and
/// hysteresis against noise. Feed it any amount of audio; it calls back once per complete, valid frame.
/// </summary>
public sealed class LtcDecoder
{
    private const ushort SyncWord = 0x3FFD;          // 0011 1111 1111 1101, in transmit order
    private readonly int _rate;
    private readonly Action<LtcFrame, double> _onFrame;   // frame, measured frames per second
    private double _dc;
    private double _peak;
    private int _state;                // +1 / -1: which side of the hysteresis band the signal is on
    private long _sample;
    private long _lastEdge = -1;
    private double _bit;               // estimated samples per bit (0 = not yet known)
    private bool _half;                // first half of a 1 seen
    private readonly bool[] _bits = new bool[80];
    private int _count;                // bits received since the last resync
    private ushort _window;
    private int _calibrated;           // intervals seen while calibrating
    private double _longest;

    public LtcDecoder(int sampleRate, Action<LtcFrame, double> onFrame)
    {
        _rate = sampleRate;
        _onFrame = onFrame;
    }

    /// <summary>Samples per bit right now (0 until locked) — for diagnostics and the frame-rate estimate.</summary>
    public double SamplesPerBit => _bit;

    public void Feed(ReadOnlySpan<short> samples)
    {
        foreach (var raw in samples)
        {
            _sample++;
            double x = raw;
            _dc += (x - _dc) * 0.0015;                 // remove DC (≈ 10 Hz high-pass at 48 kHz)
            var v = x - _dc;
            var a = Math.Abs(v);
            _peak = a > _peak ? a : _peak * 0.9997;    // level follower (fast attack, ~70 ms release)
            if (_peak < 250) { _state = 0; _lastEdge = -1; continue; }   // below ≈ −42 dBFS: no signal
            var hyst = _peak * 0.2;
            int s = v > hyst ? 1 : v < -hyst ? -1 : _state;
            if (_state == 0) { _state = s; continue; }
            if (s == _state) continue;
            _state = s;
            OnEdge();
        }
    }

    private void OnEdge()
    {
        var at = _sample;
        if (_lastEdge < 0) { _lastEdge = at; return; }
        double interval = at - _lastEdge;
        _lastEdge = at;

        // Bit periods at 48 kHz: 30 fps = 20 samples, 25 = 24, 24 = 25 (half that for each half of a 1).
        var min = _rate / (35.0 * 80) * 0.4;           // shorter than half a bit at 35 fps: noise
        var max = _rate / (20.0 * 80) * 1.3;           // longer than a bit at 20 fps: dropout
        if (interval < min) return;
        if (interval > max) { Resync(); _bit = 0; _calibrated = 0; _longest = 0; return; }
        // Calibrate on the first 24 transitions: frames are mostly zeros, so the longest gap is one whole bit.
        if (_bit <= 0)
        {
            _longest = Math.Max(_longest, interval);
            if (++_calibrated < 24) return;
            _bit = _longest;
        }

        if (interval > _bit * 0.75)
        {
            // A whole bit cell: a 0 (if we were half way through a 1, that 1 was damaged: resync).
            if (_half) { _half = false; Resync(); }
            _bit += (interval - _bit) * 0.08;
            Push(false);
        }
        else
        {
            _bit += (interval * 2 - _bit) * 0.04;       // a half cell also tells us the bit rate
            if (_half) { _half = false; Push(true); }
            else _half = true;
        }
    }

    private void Resync()
    {
        _half = false;
        _count = 0;
        _window = 0;
    }

    private void Push(bool bit)
    {
        _bits[_count % 80] = bit;
        _count++;
        _window = (ushort)((_window << 1) | (bit ? 1 : 0));
        if (_window != SyncWord || _count < 80) return;

        // The frame is the 80 bits ending with this sync word.
        var start = _count - 80;
        bool B(int i) => _bits[(start + i) % 80];
        int Bcd(int at, int n) { var v = 0; for (int i = 0; i < n; i++) if (B(at + i)) v |= 1 << i; return v; }
        var frames = Bcd(0, 4) + 10 * Bcd(8, 2);
        var seconds = Bcd(16, 4) + 10 * Bcd(24, 3);
        var minutes = Bcd(32, 4) + 10 * Bcd(40, 3);
        var hours = Bcd(48, 4) + 10 * Bcd(56, 2);
        if (frames > 29 || seconds > 59 || minutes > 59 || hours > 23) return;
        _onFrame(new LtcFrame(hours, minutes, seconds, frames, B(10)), _rate / (_bit * 80));
    }
}

/// <summary>The latest timecode, for the show thread (immutable; published latest-wins).</summary>
public sealed record LtcReading(LtcFrame Frame, int Fps, bool Running, long AtTicks)
{
    public string Text => Frame.Text;
    /// <summary>Drop-frame timecode (29.97): labels and arithmetic follow SMPTE's skipped frame numbers.</summary>
    public bool DropFrame => Timecode.IsDropFrame(Fps, Frame.DropFrame);

    /// <summary>The timecode now: the last frame plus the time since it arrived, while rolling (never more than half a second ahead).</summary>
    public TimeSpan Now(long nowTicks)
    {
        var t = Frame.ToTime(Fps) + TimeSpan.FromSeconds(1.0 / Timecode.Rate(Fps, DropFrame));   // complete one frame after its start
        if (!Running) return t;
        var age = Stopwatch.GetElapsedTime(AtTicks, nowTicks);
        return t + (age < TimeSpan.FromSeconds(0.5) ? age : TimeSpan.FromSeconds(0.5));
    }

    public bool Fresh(long nowTicks) => Stopwatch.GetElapsedTime(AtTicks, nowTicks) < TimeSpan.FromSeconds(1);
}

/// <summary>
/// Turns decoded frames into a reading: the frame rate (from the bit rate, snapped to 24 / 25 / 30) and whether
/// the code is rolling (consecutive frames) or parked (repeating, or none for a few frames).
/// </summary>
public sealed class LtcTracker
{
    private LtcFrame _last;
    private bool _has;
    private int _rolling;              // consecutive +1 frames
    private int _fps = 25;

    public LtcReading? Reading { get; private set; }

    public static int SnapFps(double measured) => measured switch { < 24.5 => 24, < 27.5 => 25, _ => 30 };

    public LtcReading OnFrame(LtcFrame frame, double measuredFps, long atTicks)
    {
        _fps = SnapFps(measuredFps);
        if (_has)
        {
            var step = frame.Number(_fps) - _last.Number(_fps);
            _rolling = step is 1 or 2 ? Math.Min(_rolling + 1, 100) : 0;
        }
        _last = frame;
        _has = true;
        return Reading = new LtcReading(frame, _fps, _rolling >= 3, atTicks);
    }

    /// <summary>Called periodically: rolling stops when frames stop arriving.</summary>
    public LtcReading? Tick(long nowTicks)
    {
        if (Reading is { Running: true } r && Stopwatch.GetElapsedTime(r.AtTicks, nowTicks) > TimeSpan.FromSeconds(4.0 / r.Fps + 0.1))
        {
            _rolling = 0;
            Reading = r with { Running = false };
        }
        return Reading;
    }
}

/// <summary>
/// What the timer should do when it follows LTC. Edge-triggered for start/pause (the operator can still override
/// between changes); with an end timecode, the remaining time is kept equal to end − timecode.
/// </summary>
public static class LtcChase
{
    public readonly record struct Action(bool Start, bool Pause, TimeSpan? SetRemaining);

    public static Action Decide(bool wasRunning, LtcReading reading, long nowTicks, TimeSpan? endTimecode, TimeSpan timerRemaining, bool timerCounting)
    {
        var start = reading.Running && !wasRunning && !timerCounting;
        var pause = !reading.Running && wasRunning && timerCounting;
        TimeSpan? set = null;
        if (endTimecode is { } end)
        {
            var want = end - reading.Now(nowTicks);
            if (Math.Abs((want - timerRemaining).TotalSeconds) > 0.15) set = want;
        }
        return new Action(start, pause, set);
    }

    /// <summary>The reading is gone (no signal, or a new source with nothing decoded yet): pause if we were following rolling code.</summary>
    public static bool Lost(bool wasRunning, bool timerCounting) => wasRunning && timerCounting;
}

/// <summary>LTC in: an audio input → decoder → the latest reading, on its own capture thread.</summary>
public sealed class LtcInput : IIntegration
{
    private readonly WaveCapture? _capture;
    private readonly LtcDecoder _decoder;
    private readonly LtcTracker _tracker = new();
    private readonly Latest<LtcReading> _latest = new();
    private readonly string _device;
    private long _framesDecoded;

    public LtcInput(string device)
    {
        _device = device.Length == 0 ? "the default input" : device;
        _decoder = new LtcDecoder(48000, (frame, fps) =>
        {
            Interlocked.Increment(ref _framesDecoded);
            _latest.Set(_tracker.OnFrame(frame, fps, Stopwatch.GetTimestamp()));
        });
        if (WaveRecorder.DeviceId(device) is not { } id)
        {
            _health = LinkHealth.NotFound;
            StatusText = $"audio input \"{device}\" not found";
            return;
        }
        _capture = new WaveCapture(id, 48000, samples => _decoder.Feed(samples));
        _capture.Start();
        StatusText = $"listening for LTC on {_device}";
    }

    /// <summary>Latest reading (thread-safe). "Rolling" ends when frames stop arriving (4 frames' grace).</summary>
    public LtcReading? Value
    {
        get
        {
            var r = _latest.Value;
            if (r is { Running: true } && Stopwatch.GetElapsedTime(r.AtTicks) > TimeSpan.FromSeconds(4.0 / r.Fps + 0.1))
                return r with { Running = false };
            return r;
        }
    }

    public string Name => "LTC in";

    public LinkHealth Health => _capture is null ? _health
        : Value is { } r && r.Fresh(Stopwatch.GetTimestamp()) ? LinkHealth.Ok : LinkHealth.Starting;

    private readonly LinkHealth _health = LinkHealth.Starting;
    private string StatusText { get; } = "";

    public string Status
    {
        get
        {
            if (_capture is null) return StatusText;
            if (!_capture.Running && _capture.Status != "listening" && _capture.Status != "starting") return _capture.Status;
            var r = Value;
            if (r is null) return $"no timecode on {_device}";
            if (!r.Fresh(Stopwatch.GetTimestamp())) return $"timecode stopped at {r.Text} ({r.Fps} fps)";
            return $"{r.Text} · {r.Fps} fps · {(r.Running ? "rolling" : "parked")}";
        }
    }

    public void Dispose() => _capture?.Dispose();
}
