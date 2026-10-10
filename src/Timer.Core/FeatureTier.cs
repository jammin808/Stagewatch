namespace Stagewatch.Core;

/// <summary>
/// The licensing tier the app is currently authorised for.
/// The numeric values are stored in signed licence files: never renumber, only append.
/// </summary>
public enum FeatureTier
{
    /// <summary>Trial ended, no license: "Free" mode — a basic watermarked timer.</summary>
    Locked = 0,
    /// <summary>Free 14-day trial: every feature, no watermark.</summary>
    Trial = 1,
    /// <summary>Standard — the core timer.</summary>
    Standard = 2,
    /// <summary>Pro — one operator, one room: running order, remotes, Companion, PowerPoint.</summary>
    Pro = 3,
    /// <summary>Master / owner — all features, never expires, never watermarked.</summary>
    Master = 4,
    /// <summary>Production — the crew loop and multi-room show kit (2 machines: main + backup).</summary>
    Production = 5
}

/// <summary>Prices and licence terms shown in the app and docs (GBP, one-off unless stated).</summary>
public static class Pricing
{
    public const int Standard = 9;
    public const int Pro = 29;
    public const int Production = 79;
    /// <summary>Launch price for Production.</summary>
    public const int ProductionLaunch = 59;
    /// <summary>Production update renewal per year, once early-adopter lifetime updates close.</summary>
    public const int UpdateRenewalPerYear = 20;
    /// <summary>30-day Production event pass, credited towards the full licence.</summary>
    public const int EventPass = 15;
    public const int EventPassDays = 30;
    /// <summary>A pass keeps working this long after it ends, so an overrunning or postponed event is never cut short.</summary>
    public const int EventPassGraceDays = 2;
    /// <summary>The app warns this many days before a pass ends.</summary>
    public const int EventPassWarnDays = 3;
    /// <summary>Charities, churches, schools and universities.</summary>
    public const int NonprofitDiscountPercent = 30;
    /// <summary>Rental houses buying five licences at once.</summary>
    public const int RentalPackDiscountPercent = 20;

    /// <summary>A price after the nonprofit discount, rounded to whole pounds.</summary>
    public static int Nonprofit(int price) => (int)Math.Round(price * (100 - NonprofitDiscountPercent) / 100.0, MidpointRounding.AwayFromZero);
    /// <summary>Main + backup.</summary>
    public const int ProductionMachines = 2;

    public static string TierName(FeatureTier t) => t switch
    {
        FeatureTier.Locked => "Free",
        _ => t.ToString()
    };
}

/// <summary>Single source of truth for which features each tier unlocks.</summary>
public static class FeatureGate
{
    public static readonly TimeSpan TrialLength = TimeSpan.FromDays(14);

    /// <summary>Burned into every output once the trial has ended without a license.</summary>
    public const string WatermarkText = "StageWatch by Ben Rohleder";

    /// <summary>Tier ladder: Free &lt; Standard &lt; Pro &lt; Production; Trial and Master have everything.</summary>
    public static int Level(FeatureTier t) => t switch
    {
        FeatureTier.Locked => 0,
        FeatureTier.Standard => 1,
        FeatureTier.Pro => 2,
        FeatureTier.Production => 3,
        FeatureTier.Trial or FeatureTier.Master => 4,
        _ => 0
    };

    public static bool AtLeastStandard(FeatureTier t) => Level(t) >= 1;
    public static bool AtLeastPro(FeatureTier t) => Level(t) >= 2;
    public static bool AtLeastProduction(FeatureTier t) => Level(t) >= 3;

    // ---- Free: a usable basic timer (countdown / count-up / clock, start-stop-reset, nudge, typed
    //      messages and a full-screen output). Everything else needs a license.
    public static bool AllowsCountdownTimer(FeatureTier t) => true;
    public static bool AllowsTimeOfDay(FeatureTier t) => true;
    public static bool AllowsSessionElapsed(FeatureTier t) => true;
    public static bool IsLicensedOrTrial(FeatureTier t) => t != FeatureTier.Locked;

    // ---- Standard: the core timer and speaker cues.
    public static bool AllowsAllPresets(FeatureTier t) => AtLeastStandard(t);
    public static bool AllowsNdi(FeatureTier t) => AtLeastStandard(t);
    public static bool AllowsChromaKey(FeatureTier t) => AtLeastStandard(t);
    public static bool AllowsEndAt(FeatureTier t) => AtLeastStandard(t);
    public static bool AllowsAlerts(FeatureTier t) => AtLeastStandard(t);
    public static bool AllowsQuickMessages(FeatureTier t) => AtLeastStandard(t);
    /// <summary>Holding images on every screen.</summary>
    public static bool AllowsHolding(FeatureTier t) => AtLeastStandard(t);

    // ---- Pro: one operator, one room.
    public static bool AllowsCompanion(FeatureTier t) => AtLeastPro(t);
    public static bool AllowsMultiOutput(FeatureTier t) => AtLeastPro(t);
    public static bool AllowsPowerPointAddIn(FeatureTier t) => AtLeastPro(t);
    public static bool AllowsRunOfShow(FeatureTier t) => AtLeastPro(t);
    /// <summary>Browser viewer/remote, HTTP API and OSC.</summary>
    public static bool AllowsRemote(FeatureTier t) => AtLeastPro(t);
    /// <summary>Planned-vs-actual as CSV (Ontime gives basic reports away, so this is not a Production lever).</summary>
    public static bool AllowsCsvReport(FeatureTier t) => AtLeastPro(t);
    /// <summary>Holographic 3D display style (three.js) on the output and browser screens.</summary>
    public static bool AllowsHolographic(FeatureTier t) => AtLeastPro(t);

    // ---- Production: the crew loop and multi-room show kit.
    /// <summary>Every Stagewatch on the LAN on one dashboard (/rooms), unlimited rooms and viewers.</summary>
    public static bool AllowsMultiRoom(FeatureTier t) => AtLeastProduction(t);
    /// <summary>Art-Net / sACN / Hue cue lights.</summary>
    public static bool AllowsCueLights(FeatureTier t) => AtLeastProduction(t);
    /// <summary>Acknowledged cues, department STANDBY/GO, green-room calls, pocket buzz.</summary>
    public static bool AllowsCrewCues(FeatureTier t) => AtLeastProduction(t);
    /// <summary>Probabilistic end-of-show forecast and curfew-aware trim suggestions.</summary>
    public static bool AllowsForecast(FeatureTier t) => AtLeastProduction(t);
    /// <summary>LTC timecode output and the time-server drift check.</summary>
    public static bool AllowsTimecode(FeatureTier t) => AtLeastProduction(t);
    /// <summary>vMix VT countdowns.</summary>
    public static bool AllowsPlayout(FeatureTier t) => AtLeastProduction(t);
    /// <summary>The full printable (PDF) report.</summary>
    public static bool AllowsFullReport(FeatureTier t) => AtLeastProduction(t);
    /// <summary>Speech-to-script pace tracking.</summary>
    public static bool AllowsSpeechPace(FeatureTier t) => AtLeastProduction(t);
    /// <summary>The producer assistant (rules, and optionally Claude).</summary>
    public static bool AllowsAssistant(FeatureTier t) => AtLeastProduction(t);
    /// <summary>Model Context Protocol endpoint for AI tools and Patterns.</summary>
    public static bool AllowsMcp(FeatureTier t) => AtLeastProduction(t);

    public static int MaxPresets(FeatureTier t) => t == FeatureTier.Locked ? 2 : 12;

    public static bool ShowsWatermark(FeatureTier t) => t == FeatureTier.Locked;

    /// <summary>"… needs Stagewatch Production" — consistent wording for gated features.</summary>
    public static string Needs(FeatureTier required) => $"needs Stagewatch {Pricing.TierName(required)}";
}
