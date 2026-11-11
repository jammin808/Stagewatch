using System.Globalization;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// The planned-vs-actual report: who may have which format, building it from the journal for the live running order,
/// rendering it, and where it is saved. No UI; the host only decides when to ask and whether to open the file.
/// </summary>
public static class ShowReports
{
    /// <summary>The format that is the show network's lines as CSV (network.csv): what verified peers' own journals told this Stagewatch.</summary>
    public const string Network = ShowReport.NetworkFormat;

    /// <summary>"csv", <see cref="Network"/>, or "html" for anything else.</summary>
    public static string Format(string? format) => format is "csv" or Network ? format : "html";

    /// <summary>CSV — the report's and the show network's — is Pro; the full printable HTML report is Production.</summary>
    public static bool Allowed(string format, FeatureTier tier) => Format(format) is "csv" or Network
        ? FeatureGate.AllowsCsvReport(tier)
        : FeatureGate.AllowsFullReport(tier);

    /// <summary>Whether there is anything to report: a running order with items.</summary>
    public static bool HasContent(AppSettings settings) => settings.Rundown.Items.Count > 0;

    /// <summary>The report for the live running order. Owner thread: reads the journal and the controller.</summary>
    public static ShowReport Build(string showName, AppSettings settings, ShowController show, IReadOnlyList<JournalEntry> journal, DateTime nowUtc)
        => ShowReport.Build(showName, settings.Rundown, journal, show.ScheduleAnchor(), ShowClock.For(settings.Rundown.TimeZoneId), nowUtc);

    public static string Render(ShowReport report, string format, ShowClock clock) => Format(format) switch
    {
        "csv" => report.ToCsv(clock),
        Network => report.NetworkCsv(clock),
        _ => report.ToHtml(clock),
    };

    /// <summary>
    /// The show network's lines as network.csv straight from the journal: its peer rows in one pass, with no report built around
    /// them — what a host answers <see cref="Network"/> with, so a reader that polls the route costs the show thread no report.
    /// </summary>
    public static string NetworkCsv(IReadOnlyList<JournalEntry> journal, ShowClock clock) => ShowReport.NetworkCsv(ShowReport.NetworkOf(journal), clock);

    /// <summary>
    /// The report's text in <paramref name="format"/>, as every host gives it: the show network's lines straight from the journal,
    /// else the report <paramref name="build"/> makes, rendered — so only a format that needs the report builds one.
    /// </summary>
    public static string Text(string? format, IReadOnlyList<JournalEntry> journal, ShowClock clock, Func<ShowReport> build)
        => Format(format) == Network ? NetworkCsv(journal, clock) : Render(build(), Format(format), clock);

    /// <summary>
    /// "Reports/My show 2026-10-10 1432.html" in <paramref name="showFolder"/>, with unsafe characters replaced; the show network's
    /// lines as "… 1432.network.csv". The date is written the same on every PC, whatever its calendar.
    /// </summary>
    public static string PathFor(string showFolder, string showName, DateTime localNow, string format)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(showName.Select(ch => invalid.Contains(ch) ? '_' : ch));
        if (safe.Trim().Length == 0) safe = "Show";
        var extension = Format(format) == Network ? "network.csv" : Format(format);
        return Path.Combine(showFolder, "Reports", string.Create(CultureInfo.InvariantCulture, $"{safe} {localNow:yyyy-MM-dd HHmm}.{extension}"));
    }

    /// <summary>Write the rendered report, creating its folder. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    public static void Save(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, Encoding.UTF8);
    }
}
