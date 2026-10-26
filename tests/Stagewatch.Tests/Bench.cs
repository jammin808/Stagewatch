/// <summary>
/// Where the suite runs. A check that needs Windows itself (WMI, COM apartments, Windows file-name rules, Windows
/// voices) runs on the Windows job; on any other OS it is named here and counted in the summary, never passed. A check of a
/// tool the Linux bench alone runs (a bash script) runs off Windows; on Windows it is named here the same way.
/// </summary>
static class Bench
{
    private static readonly List<string> NotRun = new();
    private static readonly List<string> NotRunOnWindows = new();

    public static IReadOnlyList<string> WindowsOnlyNotRun { get { lock (NotRun) return NotRun.ToList(); } }

    public static IReadOnlyList<string> LinuxBenchNotRun { get { lock (NotRunOnWindows) return NotRunOnWindows.ToList(); } }

    /// <summary>True on Windows; elsewhere records <paramref name="what"/> as not run here and returns false.</summary>
    public static bool OnWindows(string what)
    {
        if (OperatingSystem.IsWindows()) return true;
        lock (NotRun) NotRun.Add(what);
        return false;
    }

    /// <summary>True off Windows; on Windows records <paramref name="what"/> as not run here and returns false.</summary>
    public static bool OffWindows(string what)
    {
        if (!OperatingSystem.IsWindows()) return true;
        lock (NotRunOnWindows) NotRunOnWindows.Add(what);
        return false;
    }
}
