using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// The planned-vs-actual report: who may have which format, building it from the journal for the live running order,
/// rendering it, and where it is saved. No UI; the host only decides when to ask and whether to open the file.
/// </summary>
public static class ShowReports
{
    /// <summary>"csv", or "html" for anything else.</summary>
    public static string Format(string? format) => format == "csv" ? "csv" : "html";

    /// <summary>CSV is Pro; the full printable HTML report is Production.</summary>
    public static bool Allowed(string format, FeatureTier tier) => Format(format) == "csv"
        ? FeatureGate.AllowsCsvReport(tier)
        : FeatureGate.AllowsFullReport(tier);

    /// <summary>Whether there is anything to report: a running order with items.</summary>
    public static bool HasContent(AppSettings settings) => settings.Rundown.Items.Count > 0;

    /// <summary>The report for the live running order. Owner thread: reads the journal and the controller.</summary>
    public static ShowReport Build(string showName, AppSettings settings, ShowController show, IReadOnlyList<JournalEntry> journal, DateTime nowUtc)
        => ShowReport.Build(showName, settings.Rundown, journal, show.ScheduleAnchor(), ShowClock.For(settings.Rundown.TimeZoneId), nowUtc);

    public static string Render(ShowReport report, string format, ShowClock clock)
        => Format(format) == "csv" ? report.ToCsv(clock) : report.ToHtml(clock);

    /// <summary>"Reports/My show 2026-10-10 1432.html" in <paramref name="showFolder"/>, with unsafe characters replaced.</summary>
    public static string PathFor(string showFolder, string showName, DateTime localNow, string format)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(showName.Select(ch => invalid.Contains(ch) ? '_' : ch));
        if (safe.Trim().Length == 0) safe = "Show";
        return Path.Combine(showFolder, "Reports", $"{safe} {localNow:yyyy-MM-dd HHmm}.{Format(format)}");
    }

    /// <summary>Write the rendered report, creating its folder. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    public static void Save(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.UTF8);
    }
}
