namespace Stagewatch.Core;

/// <summary>Single place to change product naming.</summary>
public static class AppInfo
{
    public const string ProductName = "Stagewatch";
    public const string CompanyName = "Stagewatch";
    public const string MainPipeName = "Stagewatch.PowerPoint.Bridge";
    public const int DefaultCompanionPort = 9923;
    public const string AuthorLinkedInUrl = "https://www.linkedin.com/in/ben-rohleder-234aa36";

    /// <summary>
    /// The day this build was released (yyyy-MM-dd). Set by tools\release.ps1. A licence whose
    /// update entitlement ended before this date runs this build at the tier below.
    /// </summary>
    public const string ReleaseDate = "2026-10-10";

    public static DateTime ReleaseDateUtc => DateTime.SpecifyKind(DateTime.ParseExact(ReleaseDate, "yyyy-MM-dd",
        System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
}

/// <summary>Standard nudge amounts surfaced by the UI.</summary>
public static class NudgeAmounts
{
    public static readonly TimeSpan OneSecond  = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan TenSeconds = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan OneMinute  = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan OneHour    = TimeSpan.FromHours(1);
}
