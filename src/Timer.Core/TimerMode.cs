namespace Stagewatch.Core;

/// <summary>The kind of clock the engine is currently driving.</summary>
public enum TimerMode
{
    /// <summary>
    /// Counts down from a duration, an end-time, or both. With both set the timer
    /// is "elastic" — drains at a rate that lands at the end-time.
    /// </summary>
    Countdown = 0,

    /// <summary>Shows the wall-clock time of day. Read-only.</summary>
    TimeOfDay = 1,

    // 2 was the retired CountdownToTime mode; never reuse it (old settings files may hold it).

    /// <summary>Counts up from zero (elapsed). For open-ended segments like Q&amp;A.</summary>
    CountUp = 3
}
