using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// A named timer that runs alongside the show without touching it — a VT, a changeover, a speaker's own clock.
/// Like a game clock it is derived, never accumulated: the value is computed from a monotonic timestamp each time
/// it's read, so it can't drift however often (or rarely) it's displayed, and a stalled frame can't lose time.
/// Owned by the show thread.
/// </summary>
public sealed class AuxTimer
{
    private readonly Func<long> _now;
    private long? _startedAt;              // monotonic ticks when it last started running
    private TimeSpan _banked;              // time run before the current start

    public AuxTimer(Func<long>? now = null) => _now = now ?? Stopwatch.GetTimestamp;

    public string Name { get; set; } = "";
    public bool CountUp { get; set; }
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(5);
    /// <summary>Shown as a second line on the output and speaker screens.</summary>
    public bool OnStage { get; set; }

    public bool Running => _startedAt is not null;

    public TimeSpan Elapsed
    {
        get
        {
            var run = _startedAt is { } at ? Stopwatch.GetElapsedTime(at, _now()) : TimeSpan.Zero;
            return _banked + run;
        }
    }

    /// <summary>Count up: time run. Count down: time left (negative once it passes zero).</summary>
    public TimeSpan Value => CountUp ? Elapsed : Duration - Elapsed;

    public void Start() { if (_startedAt is null) _startedAt = _now(); }

    public void Pause()
    {
        if (_startedAt is not { } at) return;
        _banked += Stopwatch.GetElapsedTime(at, _now());
        _startedAt = null;
    }

    public void Toggle() { if (Running) Pause(); else Start(); }

    public void Reset() { _banked = TimeSpan.Zero; _startedAt = Running ? _now() : null; }

    public void SetDuration(TimeSpan d)
    {
        Duration = d < TimeSpan.Zero ? TimeSpan.Zero : d;
        Reset();
    }

    /// <summary>Add (or remove) time: more time left for a countdown, more elapsed for a count-up.</summary>
    public void Nudge(TimeSpan delta) => _banked += CountUp ? delta : -delta;

    /// <summary>Whole seconds for display (counting down rounds up, like the main timer).</summary>
    public long DisplaySeconds
    {
        get
        {
            var v = Value.TotalSeconds;
            return CountUp ? (long)Math.Floor(v) : (long)Math.Ceiling(v - 1e-9);
        }
    }

    public AuxView View(int slot)
        => new(slot, Name.Length > 0 ? Name : $"Timer {slot}", TimerTick.FormatSeconds(DisplaySeconds), DisplaySeconds, Running, CountUp, OnStage,
               (long)Duration.TotalSeconds);
}

/// <summary>One auxiliary timer in a frame (immutable).</summary>
public sealed record AuxView(int Slot, string Name, string Text, long Seconds, bool Running, bool CountUp, bool OnStage, long DurationSeconds);

/// <summary>What an auxiliary timer slot remembers between runs.</summary>
public sealed class AuxTimerSettings
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("countUp")] public bool CountUp { get; set; }
    [JsonPropertyName("seconds")] public int DurationSeconds { get; set; } = 300;
}
