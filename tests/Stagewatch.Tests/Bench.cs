/// <summary>
/// Where the suite runs. A check that needs Windows itself (WMI, COM apartments, Windows file-name rules, Windows
/// voices) runs on the Windows job; on any other OS it is named here and counted in the summary, never passed.
/// </summary>
static class Bench
{
    private static readonly List<string> NotRun = new();

    public static IReadOnlyList<string> WindowsOnlyNotRun { get { lock (NotRun) return NotRun.ToList(); } }

    /// <summary>True on Windows; elsewhere records <paramref name="what"/> as not run here and returns false.</summary>
    public static bool OnWindows(string what)
    {
        if (OperatingSystem.IsWindows()) return true;
        lock (NotRun) NotRun.Add(what);
        return false;
    }
}
