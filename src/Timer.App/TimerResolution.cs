using System.Runtime.InteropServices;

namespace Stagewatch.App;

/// <summary>
/// Windows' timer resolution at 1 ms while Stagewatch runs, so ticks, sends and sleeps land when they're due instead of
/// on the default 15.6 ms grid. Asked for once at start, given back at exit.
/// </summary>
public static class TimerResolution
{
    private static bool _on;

    public static void Begin()
    {
        if (_on) return;
        try { _on = timeBeginPeriod(1) == 0; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }

    public static void End()
    {
        if (!_on) return;
        try { timeEndPeriod(1); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        _on = false;
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
