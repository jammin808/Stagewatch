namespace Stagewatch.Core;

/// <summary>Engine run state.</summary>
public enum TimerState
{
    Idle,
    Running,
    Paused,
    /// <summary>Countdown reached zero. Engine may continue counting negative if <see cref="TimerEngine.AllowOvertime"/>.</summary>
    Expired
}
