using System.Diagnostics;

namespace Stagewatch.Core;

/// <summary>
/// The core countdown / clock engine. Drift-free: ticks compute remaining time
/// from a reference timestamp rather than accumulating per-tick deltas.
///
/// Modes:
///   Countdown    — counts down. Configured by setting a Duration, an End-at
///                  wall-clock time, or both. With only a duration it counts
///                  down that span. With only an end-at it counts down to the
///                  wall-clock target. With both, it stretches so the displayed
///                  remaining lands on zero exactly at the end-at time ("elastic").
///                  End-at can instead be set to real time: the loaded duration is
///                  replaced by the actual time left, counted at normal pace.
///   CountUp      — counts up from zero (elapsed). Never expires.
///   TimeOfDay    — live wall clock. Auto-ticks.
///
/// Thread model: all mutating methods are called on one thread (typically UI).
/// The Tick event fires on a worker timer thread — subscribers marshal to UI.
/// </summary>
public sealed class TimerEngine : IDisposable
{
    private readonly object _lock = new();
    private readonly System.Threading.Timer _ticker;
    private readonly Stopwatch _wallClock = Stopwatch.StartNew();

    // Countdown:
    private TimeSpan _baseDuration;          // configured duration; can be zero
    private TimeSpan _remainingAtPause;
    private long _runStartedAtMs;
    private bool _inOvertime;                // Expired but still counting negative

    // Count-up:
    private TimeSpan _elapsedAtPause;

    // End-at (was "stretch target"). Optional wall-clock end time, held in UTC so midnight and
    // daylight-saving changes (clocks going back at 02:00 mid-awards-night) can't make it jump.
    private DateTime? _endAtUtc;
    private DateTime _stretchAnchorAtUtc;    // when the current stretch was anchored
    private TimeSpan _stretchAnchorRemaining;// what the timer was showing at anchor time
    private bool _endAtRealTime;             // count the real time left at 1× instead of stretching

    // Wall clock source (UTC). Injectable so tests can cross midnight / DST instantly.
    // Countdowns themselves never read it: they run on the monotonic Stopwatch.
    private readonly Func<DateTime> _utcNow;

    public TimerEngine(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _ticker = new System.Threading.Timer(_ => RaiseTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Local wall-clock time as this engine sees it.</summary>
    public DateTime LocalNow => _utcNow().ToLocalTime();

    public TimerMode Mode { get; private set; } = TimerMode.Countdown;
    public TimerState State { get; private set; } = TimerState.Idle;
    public bool AllowOvertime { get; set; } = true;
    public int? CurrentPresetSlot { get; private set; }

    /// <summary>True while the clock is advancing: running, or counting negative in overtime.</summary>
    public bool IsCounting
    {
        get { lock (_lock) return IsCountingLocked(); }
    }

    private bool IsCountingLocked()
        => State == TimerState.Running || (State == TimerState.Expired && _inOvertime);

    private bool IsStoppable => Mode is TimerMode.Countdown or TimerMode.CountUp;

    public int TickIntervalMs { get; set; } = 50;

    public event Action<TimerTick>? Tick;
    public event Action<TimerState>? StateChanged;
    public event Action<TimerMode>? ModeChanged;

    /// <summary>The configured countdown duration.</summary>
    public TimeSpan BaseDuration
    {
        get { lock (_lock) return _baseDuration; }
    }

    /// <summary>Optional wall-clock time the countdown should land on zero at. Null when not set.</summary>
    public DateTime? EndAt
    {
        get { lock (_lock) return _endAtUtc?.ToLocalTime(); }
    }

    /// <summary>
    /// True when the end-at counts the real time left at normal pace (the loaded duration is
    /// replaced) rather than stretching the loaded duration to fit. Only meaningful with an end-at.
    /// </summary>
    public bool EndAtRealTime
    {
        get { lock (_lock) return _endAtRealTime; }
    }

    /// <summary>
    /// True when an elastic stretch is in effect — i.e. we have both a non-zero
    /// duration and an end-at time, and they don't match the current run wall span.
    /// </summary>
    public bool IsStretching
    {
        get
        {
            lock (_lock)
            {
                if (_endAtUtc is null || State != TimerState.Running) return false;
                // No duration, or real time → end-at is just the target; effective rate is 1.
                if (_baseDuration <= TimeSpan.Zero || _endAtRealTime) return false;
                var ratio = StretchRatioLocked();
                return ratio < 0.99 || ratio > 1.01;
            }
        }
    }

    /// <summary>Ratio of timer rate to wall rate (>1 = faster, &lt;1 = slower).</summary>
    public double StretchRatio
    {
        get { lock (_lock) return StretchRatioLocked(); }
    }

    private double StretchRatioLocked()
    {
        if (_endAtUtc is null) return 1.0;
        var nominalRem = _stretchAnchorRemaining.TotalMilliseconds;
        var wallRem = (_endAtUtc.Value - _stretchAnchorAtUtc).TotalMilliseconds;
        if (wallRem <= 0) return double.PositiveInfinity;
        return nominalRem / wallRem;
    }

    public void SetMode(TimerMode mode)
    {
        bool needTick = false;
        lock (_lock)
        {
            if (Mode == mode) return;
            Mode = mode;

            if (mode == TimerMode.TimeOfDay)
            {
                ChangeState(TimerState.Running);
                needTick = true;
            }
            else
            {
                if (mode == TimerMode.CountUp) _endAtUtc = null;
                StopInternal();
            }
        }
        if (needTick) _ticker.Change(0, TickIntervalMs);
        else RaiseTick();
        ModeChanged?.Invoke(mode);
    }

    public void LoadPreset(TimerPreset preset)
    {
        lock (_lock)
        {
            CurrentPresetSlot = preset.Slot;
            _baseDuration = TimeSpan.FromSeconds(Math.Abs(preset.DurationSeconds));
            ResetCountersLocked();
            _endAtUtc = null; // Loading a preset clears any prior end-at override.
            ChangeState(TimerState.Idle);
        }
        RaiseTick();
    }

    public void SetDuration(TimeSpan duration)
    {
        lock (_lock)
        {
            CurrentPresetSlot = null;
            _baseDuration = duration;
            ResetCountersLocked();
            _endAtUtc = null;
            ChangeState(TimerState.Idle);
        }
        RaiseTick();
    }

    /// <summary>
    /// Set or clear the wall-clock end-at target. Pass null to clear.
    /// Elastic (default): while idle/paused this just configures it; while running the rate
    /// adjusts so the displayed remaining drains to zero at this wall-clock time.
    /// Real time: the countdown becomes the actual time left until the target and runs at
    /// normal pace. After a pause it catches up with the clock when started again.
    /// </summary>
    public void SetEndAt(DateTime? target, bool realTime = false)
    {
        lock (_lock)
        {
            if (target is null)
            {
                _endAtUtc = null;
                _endAtRealTime = false;
            }
            else if (realTime)
            {
                var targetUtc = target.Value.Kind == DateTimeKind.Utc ? target.Value : target.Value.ToUniversalTime();
                _endAtUtc = targetUtc;
                _endAtRealTime = true;
                var now = _utcNow();
                var left = targetUtc - now;
                if (left < TimeSpan.Zero) left = TimeSpan.Zero;
                if (IsCountingLocked())
                {
                    // Shift the run so the nominal remaining is exactly the real time left.
                    _baseDuration += left - ComputeNominalRemainingLocked();
                    if (State == TimerState.Expired && left > TimeSpan.Zero)
                        ChangeState(TimerState.Running);
                }
                else
                {
                    _baseDuration = left;
                    _remainingAtPause = left;
                    if (State == TimerState.Expired) ChangeState(TimerState.Idle);
                }
                _stretchAnchorAtUtc = now;
                _stretchAnchorRemaining = left;
            }
            else
            {
                _endAtRealTime = false;
                // Capture currently visible remaining (stretched if applicable, otherwise nominal)
                // so changing target mid-run doesn't make the display jump.
                var currentVisible = ComputeRemainingLocked();
                var targetUtc = target.Value.Kind == DateTimeKind.Utc ? target.Value : target.Value.ToUniversalTime();
                _endAtUtc = targetUtc;
                _stretchAnchorAtUtc = _utcNow();
                _stretchAnchorRemaining = currentVisible > TimeSpan.Zero
                    ? currentVisible
                    : targetUtc - _stretchAnchorAtUtc;
            }
        }
        RaiseTick();
    }

    public void Start()
    {
        lock (_lock)
        {
            if (Mode == TimerMode.Countdown)
            {
                if (IsCountingLocked()) return;
                if (_endAtRealTime && _endAtUtc is { } realEnd)
                {
                    // Real time: whatever happened while stopped, start from the actual time left.
                    var left = realEnd - _utcNow();
                    _remainingAtPause = left > TimeSpan.Zero ? left : TimeSpan.Zero;
                    if (_remainingAtPause > _baseDuration) _baseDuration = _remainingAtPause;
                }
                _runStartedAtMs = _wallClock.ElapsedMilliseconds - (long)(_baseDuration - _remainingAtPause).TotalMilliseconds;
                ChangeState(TimerState.Running);
                _ticker.Change(0, TickIntervalMs);

                // Re-anchor the stretch from current nominal at the moment of Start.
                // If duration is zero (end-at-only mode), anchor from "target - now".
                if (_endAtUtc is not null)
                {
                    _stretchAnchorAtUtc = _utcNow();
                    _stretchAnchorRemaining = ComputeNominalRemainingLocked();
                    if (_stretchAnchorRemaining <= TimeSpan.Zero)
                        _stretchAnchorRemaining = _endAtUtc.Value - _stretchAnchorAtUtc;
                }
            }
            else if (Mode == TimerMode.CountUp)
            {
                if (State == TimerState.Running) return;
                _runStartedAtMs = _wallClock.ElapsedMilliseconds - (long)_elapsedAtPause.TotalMilliseconds;
                ChangeState(TimerState.Running);
                _ticker.Change(0, TickIntervalMs);
            }
            else
            {
                ChangeState(TimerState.Running);
                _ticker.Change(0, TickIntervalMs);
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (Mode == TimerMode.Countdown)
            {
                if (State == TimerState.Running)
                {
                    // Real time freezes what the wall clock says is left; elastic freezes the nominal.
                    _remainingAtPause = _endAtRealTime ? ComputeRemainingLocked() : ComputeNominalRemainingLocked();
                }
                else if (State == TimerState.Expired && _inOvertime)
                {
                    // Freeze the overtime value shown; the end-at target has already passed.
                    _remainingAtPause = ComputeRemainingLocked();
                    _endAtUtc = null;
                    _endAtRealTime = false;
                }
                else return;
            }
            else if (Mode == TimerMode.CountUp)
            {
                if (State != TimerState.Running) return;
                _elapsedAtPause = ComputeNominalRemainingLocked();
            }
            else return;

            ChangeState(TimerState.Paused);
            _ticker.Change(Timeout.Infinite, Timeout.Infinite);
        }
        RaiseTick();
    }

    /// <summary>Start if stopped, pause if counting. The one-button transport.</summary>
    public void Toggle()
    {
        if (IsCounting) Pause();
        else Start();
    }

    public void Stop()
    {
        lock (_lock) { StopInternal(); }
        RaiseTick();
    }

    public void Reset()
    {
        lock (_lock)
        {
            if (IsStoppable)
            {
                ResetCountersLocked();
                _endAtUtc = null;
                _endAtRealTime = false;
                ChangeState(TimerState.Idle);
                _ticker.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        RaiseTick();
    }

    public void Nudge(TimeSpan delta)
    {
        lock (_lock)
        {
            if (Mode == TimerMode.CountUp)
            {
                if (State == TimerState.Running)
                {
                    // Shift the start so elapsed moves by delta, never below zero.
                    var elapsed = ComputeNominalRemainingLocked() + delta;
                    if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
                    _runStartedAtMs = _wallClock.ElapsedMilliseconds - (long)elapsed.TotalMilliseconds;
                }
                else
                {
                    _elapsedAtPause += delta;
                    if (_elapsedAtPause < TimeSpan.Zero) _elapsedAtPause = TimeSpan.Zero;
                }
            }
            else if (Mode == TimerMode.Countdown)
            {
                // Real time: a nudge moves the finish time itself, so the pace stays 1×.
                if (_endAtRealTime && _endAtUtc is not null) _endAtUtc += delta;
                if (IsCountingLocked())
                {
                    _baseDuration += delta;
                }
                else
                {
                    _remainingAtPause += delta;
                    _baseDuration += delta;
                }
                if (_endAtUtc is not null)
                {
                    // Re-anchor so the stretch math still drains to zero at end-at.
                    _stretchAnchorAtUtc = _utcNow();
                    _stretchAnchorRemaining = _endAtRealTime && IsCountingLocked()
                        ? _endAtUtc.Value - _stretchAnchorAtUtc
                        : ComputeNominalRemainingLocked();
                }
                if (State == TimerState.Expired && ComputeNominalRemainingLocked() > TimeSpan.Zero)
                {
                    // Held at zero → restart the clock from the nudged value. In overtime it never stopped.
                    if (!_inOvertime)
                        _runStartedAtMs = _wallClock.ElapsedMilliseconds - (long)(_baseDuration - _remainingAtPause).TotalMilliseconds;
                    ChangeState(TimerState.Running);
                    _ticker.Change(0, TickIntervalMs);
                }
            }
            else return;
        }
        RaiseTick();
    }

    /// <summary>Everything needed to put the clock back exactly where it was (crash recovery).</summary>
    public EngineState Capture()
    {
        lock (_lock)
        {
            var value = Mode == TimerMode.TimeOfDay ? TimeSpan.Zero : ComputeNominalRemainingLocked();
            return new EngineState(Mode, State, _baseDuration.TotalSeconds, value.TotalSeconds,
                Counting: Mode == TimerMode.CountUp ? State == TimerState.Running : IsCountingLocked(),
                _endAtUtc, _stretchAnchorAtUtc, _stretchAnchorRemaining.TotalSeconds, CurrentPresetSlot, _utcNow(),
                _endAtUtc is not null && _endAtRealTime);
        }
    }

    /// <summary>
    /// Resume a captured state. If the clock was running, the time that passed since the capture
    /// (e.g. while the app was restarting after a crash) is applied, so the display shows what it
    /// would have shown had nothing happened — overtime included.
    /// </summary>
    public void Restore(EngineState s)
    {
        bool needTicker;
        lock (_lock)
        {
            Mode = s.Mode;
            CurrentPresetSlot = s.PresetSlot;
            _baseDuration = TimeSpan.FromSeconds(s.BaseSeconds);
            _endAtUtc = s.EndAtUtc;
            _endAtRealTime = s.EndAtUtc is not null && s.EndAtRealTime;
            _stretchAnchorAtUtc = s.StretchAnchorAtUtc;
            _stretchAnchorRemaining = TimeSpan.FromSeconds(s.StretchAnchorRemainingSeconds);
            ResetCountersLocked();

            var passed = s.Counting ? _utcNow() - s.CapturedAtUtc : TimeSpan.Zero;
            if (passed < TimeSpan.Zero) passed = TimeSpan.Zero;
            var value = TimeSpan.FromSeconds(s.ValueSeconds);
            var nowMs = _wallClock.ElapsedMilliseconds;

            if (s.Mode == TimerMode.TimeOfDay)
            {
                ChangeState(TimerState.Running);
                needTicker = true;
            }
            else if (s.Mode == TimerMode.CountUp)
            {
                var elapsed = value + passed;
                if (s.Counting)
                {
                    _runStartedAtMs = nowMs - (long)elapsed.TotalMilliseconds;
                    ChangeState(TimerState.Running);
                }
                else
                {
                    _elapsedAtPause = elapsed;
                    ChangeState(s.State == TimerState.Running ? TimerState.Paused : s.State);
                }
                needTicker = s.Counting;
            }
            else
            {
                var remaining = value - passed;
                if (s.Counting)
                {
                    // Run from here; the first tick re-applies overtime / hold-at-zero as configured.
                    _runStartedAtMs = nowMs - (long)(_baseDuration - remaining).TotalMilliseconds;
                    ChangeState(TimerState.Running);
                }
                else
                {
                    _remainingAtPause = s.State == TimerState.Expired && remaining > TimeSpan.Zero ? TimeSpan.Zero : remaining;
                    ChangeState(s.State == TimerState.Running ? TimerState.Paused : s.State);
                }
                needTicker = s.Counting;
            }
        }
        if (needTicker) _ticker.Change(0, TickIntervalMs);
        else _ticker.Change(Timeout.Infinite, Timeout.Infinite);
        RaiseTick();
        ModeChanged?.Invoke(s.Mode);
    }

    private void ResetCountersLocked()
    {
        _remainingAtPause = _baseDuration;
        _elapsedAtPause = TimeSpan.Zero;
    }

    private void StopInternal()
    {
        _ticker.Change(Timeout.Infinite, Timeout.Infinite);
        ResetCountersLocked();
        ChangeState(TimerState.Idle);
    }

    private void ChangeState(TimerState s)
    {
        if (State == s) return;
        State = s;
        if (s != TimerState.Expired) _inOvertime = false;
        StateChanged?.Invoke(s);
    }

    /// <summary>The "real" countdown remaining ignoring any elastic stretch. In CountUp mode: elapsed.</summary>
    private TimeSpan ComputeNominalRemainingLocked()
    {
        return Mode switch
        {
            TimerMode.Countdown => IsCountingLocked()
                ? _baseDuration - TimeSpan.FromMilliseconds(_wallClock.ElapsedMilliseconds - _runStartedAtMs)
                : _remainingAtPause,
            TimerMode.CountUp => State == TimerState.Running
                ? TimeSpan.FromMilliseconds(_wallClock.ElapsedMilliseconds - _runStartedAtMs)
                : _elapsedAtPause,
            TimerMode.TimeOfDay => LocalNow.TimeOfDay,
            _ => TimeSpan.Zero
        };
    }

    /// <summary>Remaining as seen by the UI, with stretch applied while running.</summary>
    private TimeSpan ComputeRemainingLocked()
    {
        if (Mode == TimerMode.Countdown
            && IsCountingLocked()
            && _endAtUtc is { } endAt)
        {
            // Past the end-at target: overtime runs in real wall-clock time. Real time: always.
            var now = _utcNow();
            if (now >= endAt || _endAtRealTime) return endAt - now;
            var totalWallMs = (endAt - _stretchAnchorAtUtc).TotalMilliseconds;
            if (totalWallMs <= 0) return TimeSpan.Zero;
            var elapsedMs = (now - _stretchAnchorAtUtc).TotalMilliseconds;
            var fraction = elapsedMs / totalWallMs;
            var remMs = _stretchAnchorRemaining.TotalMilliseconds * (1.0 - fraction);
            return TimeSpan.FromMilliseconds(remMs);
        }
        return ComputeNominalRemainingLocked();
    }

    /// <summary>The span the progress bar measures against. Zero when there is nothing to measure.</summary>
    private TimeSpan ComputeTotalLocked()
    {
        if (Mode != TimerMode.Countdown) return TimeSpan.Zero;
        if (_baseDuration > TimeSpan.Zero) return _baseDuration;
        return _endAtUtc is not null && _stretchAnchorRemaining > TimeSpan.Zero ? _stretchAnchorRemaining : TimeSpan.Zero;
    }

    /// <summary>Wall-clock time at which the current countdown will reach zero. Null if not configured.</summary>
    public DateTime? EndsAtWallClock
    {
        get
        {
            lock (_lock)
            {
                if (Mode != TimerMode.Countdown) return null;
                var now = _utcNow();
                // At or past zero (overtime, held at zero, or paused while over) the
                // earliest possible end is now, so the end time slides with the overrun.
                if (State == TimerState.Expired) return now.ToLocalTime();
                DateTime? end = _endAtUtc
                    ?? (_baseDuration > TimeSpan.Zero ? now + ComputeNominalRemainingLocked() : null);
                return (end < now ? now : end)?.ToLocalTime();
            }
        }
    }

    /// <summary>Current remaining (countdown) or elapsed (count-up), as shown.</summary>
    public TimeSpan Current
    {
        get { lock (_lock) return ComputeRemainingLocked(); }
    }

    private void RaiseTick()
    {
        TimerTick t;
        lock (_lock)
        {
            var rem = ComputeRemainingLocked();
            t = new TimerTick(Mode, State, rem, LocalNow, CurrentPresetSlot, ComputeTotalLocked());

            if (Mode == TimerMode.Countdown
                && State == TimerState.Running
                && rem <= TimeSpan.Zero)
            {
                if (AllowOvertime)
                {
                    // Keep the ticker going; remaining continues into negative.
                    _inOvertime = true;
                    ChangeState(TimerState.Expired);
                    t = t with { State = TimerState.Expired };
                }
                else
                {
                    // Hold at zero.
                    _inOvertime = false;
                    _remainingAtPause = TimeSpan.Zero;
                    ChangeState(TimerState.Expired);
                    _ticker.Change(Timeout.Infinite, Timeout.Infinite);
                    t = t with { State = TimerState.Expired, Remaining = TimeSpan.Zero };
                }
            }
        }

        Tick?.Invoke(t);
    }

    public void Dispose() => _ticker.Dispose();
}

/// <summary>Serializable engine state (see <see cref="TimerEngine.Capture"/>). Times in seconds / UTC.</summary>
public sealed record EngineState(
    TimerMode Mode,
    TimerState State,
    double BaseSeconds,
    double ValueSeconds,          // countdown: nominal remaining (may be negative); count-up: elapsed
    bool Counting,
    DateTime? EndAtUtc,
    DateTime StretchAnchorAtUtc,
    double StretchAnchorRemainingSeconds,
    int? PresetSlot,
    DateTime CapturedAtUtc,
    bool EndAtRealTime = false);  // absent in older session files → elastic

public readonly record struct TimerTick(
    TimerMode Mode,
    TimerState State,
    TimeSpan Remaining,
    DateTime WallClockNow,
    int? PresetSlot,
    TimeSpan Total = default
)
{
    /// <summary>
    /// The whole-second value on the display. Countdowns round up (so "00:00" appears
    /// exactly at zero and the first overtime second reads "-00:01"); clocks round down.
    /// Cheap to compare — consumers use it to skip work when nothing visible changed.
    /// </summary>
    public long DisplaySeconds => Mode == TimerMode.Countdown
        ? (long)Math.Ceiling(Remaining.TotalSeconds - 0.0005)
        : (long)Math.Floor(Remaining.TotalSeconds);

    /// <summary>Fraction of the countdown left, 0..1. -1 when there is no meaningful total.</summary>
    public double Progress => Mode == TimerMode.Countdown && Total > TimeSpan.Zero
        ? Math.Clamp(Remaining.TotalMilliseconds / Total.TotalMilliseconds, 0.0, 1.0)
        : -1.0;

    public string Format() => FormatSeconds(DisplaySeconds, Mode == TimerMode.TimeOfDay);

    public static string FormatSeconds(long seconds, bool forceHours = false)
    {
        var neg = seconds < 0;
        if (neg) seconds = -seconds;
        var h = seconds / 3600;
        var m = (seconds / 60) % 60;
        var s = seconds % 60;
        var sign = neg ? "-" : "";
        return forceHours || h > 0
            ? $"{sign}{h:D2}:{m:D2}:{s:D2}"
            : $"{sign}{m:D2}:{s:D2}";
    }
}
