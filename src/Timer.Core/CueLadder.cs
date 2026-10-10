using System.Globalization;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>What a cue step sounds like (operator / talent audio, never the PA by design).</summary>
public enum CueSound { None, Warning, WrapUp, Zero, Speak }

/// <summary>
/// One rung of a cue ladder: at <see cref="AtSeconds"/> remaining the speaker display takes this
/// colour, shows this label and (optionally) flashes / sounds. The step carries a
/// <see cref="TimerPhase"/> so Companion, OSC and older remotes keep understanding the state.
/// </summary>
public sealed record CueStep
{
    [JsonPropertyName("at")] public int AtSeconds { get; init; }
    [JsonPropertyName("label")] public string Label { get; init; } = "";
    [JsonPropertyName("color")] public string ColorHex { get; init; } = "#FFFFFF";
    [JsonPropertyName("phase")] public TimerPhase Phase { get; init; } = TimerPhase.Warning;
    [JsonPropertyName("flash")] public FlashRate Flash { get; init; }
    [JsonPropertyName("sound")] public CueSound Sound { get; init; }
    /// <summary>For <see cref="CueSound.Speak"/>: words spoken in the talent's ear ("two minutes").</summary>
    [JsonPropertyName("say")] public string? Say { get; init; }
}

/// <summary>
/// A graduated, pre-agreed signal sequence — the practice speakers and planners recommend
/// (Toastmasters green/yellow/red, the corporate 10/5/2/0 ladder). Steps are evaluated against
/// the remaining time; the lowest step whose threshold has been crossed wins.
/// </summary>
public sealed record CueLadder
{
    [JsonPropertyName("id")] public string Id { get; init; } = "standard";
    [JsonPropertyName("name")] public string Name { get; init; } = "Standard";
    [JsonPropertyName("normalColor")] public string NormalColorHex { get; init; } = "#FFFFFF";
    [JsonPropertyName("steps")] public List<CueStep> Steps { get; init; } = new();
    /// <summary>False = contest rules: no extra signal in overtime (the last step simply holds).</summary>
    [JsonPropertyName("overtimeSignal")] public bool OvertimeSignal { get; init; } = true;
    [JsonPropertyName("overtimeLabel")] public string OvertimeLabel { get; init; } = "OVER";
    [JsonPropertyName("overtimeColor")] public string OvertimeColorHex { get; init; } = "#DC2626";
    [JsonPropertyName("overtimeFlash")] public FlashRate OvertimeFlash { get; init; }

    /// <summary>The step in force at this remaining time, or null while still "normal".</summary>
    public CueStep? StepAt(long remainingSeconds)
    {
        CueStep? best = null;
        foreach (var s in Steps)
            if (remainingSeconds <= s.AtSeconds && (best is null || s.AtSeconds < best.AtSeconds)) best = s;
        return best;
    }

    /// <summary>The step that is crossed when moving from <paramref name="prev"/> to <paramref name="cur"/> seconds (counting down).</summary>
    public CueStep? Crossed(long prev, long cur)
    {
        if (cur >= prev) return null;
        CueStep? hit = null;
        foreach (var s in Steps)
            if (prev > s.AtSeconds && cur <= s.AtSeconds && (hit is null || s.AtSeconds < hit.AtSeconds)) hit = s;
        return hit;
    }

    // ---------------------------------------------------------------- built-in profiles

    /// <summary>The classic two-threshold ladder built from the Display settings (backwards compatible).</summary>
    public static CueLadder FromDisplay(DisplaySettings d) => new()
    {
        Id = "standard",
        Name = "Standard (Settings → thresholds)",
        NormalColorHex = d.NormalColorHex,
        OvertimeColorHex = d.OvertimeColorHex,
        OvertimeFlash = d.ExpiredFlashRate,
        Steps = new List<CueStep>
        {
            new() { AtSeconds = d.WarningSeconds, Label = LabelFor(d.WarningSeconds), ColorHex = d.WarningColorHex, Phase = TimerPhase.Warning, Sound = CueSound.Warning },
            new() { AtSeconds = d.WrapUpSeconds, Label = "WRAP UP", ColorHex = d.WrapUpColorHex, Phase = TimerPhase.WrapUp, Sound = CueSound.WrapUp },
            new() { AtSeconds = 0, Label = "TIME", ColorHex = d.OvertimeColorHex, Phase = TimerPhase.Overtime, Sound = CueSound.Zero }
        }.Where(s => s.AtSeconds > 0 || s.Phase == TimerPhase.Overtime).ToList()
    };

    public static IReadOnlyList<CueLadder> BuiltIn { get; } = new[]
    {
        new CueLadder
        {
            Id = "keynote", Name = "Keynote 10 / 5 / 2 / 0",
            Steps =
            {
                new() { AtSeconds = 600, Label = "10 MIN", ColorHex = "#22C55E", Phase = TimerPhase.Warning },
                new() { AtSeconds = 300, Label = "5 MIN", ColorHex = "#F59E0B", Phase = TimerPhase.Warning, Sound = CueSound.Warning },
                new() { AtSeconds = 120, Label = "2 MIN", ColorHex = "#F97316", Phase = TimerPhase.WrapUp, Sound = CueSound.WrapUp },
                new() { AtSeconds = 0, Label = "TIME", ColorHex = "#DC2626", Phase = TimerPhase.Overtime, Flash = FlashRate.Slow, Sound = CueSound.Zero }
            }
        },
        new CueLadder
        {
            Id = "panel", Name = "Panel (moderator) 10 / 5 / 1",
            Steps =
            {
                new() { AtSeconds = 600, Label = "10 MIN", ColorHex = "#22C55E", Phase = TimerPhase.Warning },
                new() { AtSeconds = 300, Label = "LAST QUESTION", ColorHex = "#F59E0B", Phase = TimerPhase.Warning, Sound = CueSound.Warning },
                new() { AtSeconds = 60, Label = "WRAP UP", ColorHex = "#F97316", Phase = TimerPhase.WrapUp, Sound = CueSound.WrapUp },
                new() { AtSeconds = 0, Label = "TIME", ColorHex = "#DC2626", Phase = TimerPhase.Overtime, Sound = CueSound.Zero }
            }
        },
        new CueLadder
        {
            // Toastmasters contest lights: green at the minimum, yellow at the midpoint, red at the maximum,
            // and no further signal in overtime.
            Id = "toastmasters", Name = "Toastmasters green / yellow / red",
            OvertimeSignal = false,
            Steps =
            {
                new() { AtSeconds = 120, Label = "", ColorHex = "#16A34A", Phase = TimerPhase.Warning },
                new() { AtSeconds = 60, Label = "", ColorHex = "#EAB308", Phase = TimerPhase.WrapUp },
                new() { AtSeconds = 0, Label = "", ColorHex = "#DC2626", Phase = TimerPhase.Overtime }
            }
        },
        new CueLadder
        {
            Id = "awards", Name = "Awards (acceptance speech)",
            Steps =
            {
                new() { AtSeconds = 30, Label = "30 SEC", ColorHex = "#F59E0B", Phase = TimerPhase.Warning },
                new() { AtSeconds = 10, Label = "PLEASE CLOSE", ColorHex = "#F97316", Phase = TimerPhase.WrapUp, Flash = FlashRate.Slow },
                new() { AtSeconds = 0, Label = "PLAY-OFF", ColorHex = "#DC2626", Phase = TimerPhase.Overtime, Flash = FlashRate.Fast, Sound = CueSound.Zero }
            }
        },
        new CueLadder
        {
            Id = "spoken", Name = "Spoken cues (talent in-ear)",
            Steps =
            {
                new() { AtSeconds = 300, Label = "5 MIN", ColorHex = "#F59E0B", Phase = TimerPhase.Warning, Sound = CueSound.Speak, Say = "five minutes" },
                new() { AtSeconds = 120, Label = "2 MIN", ColorHex = "#F97316", Phase = TimerPhase.WrapUp, Sound = CueSound.Speak, Say = "two minutes" },
                new() { AtSeconds = 30, Label = "WRAP UP", ColorHex = "#F97316", Phase = TimerPhase.WrapUp, Sound = CueSound.Speak, Say = "wrap up" },
                new() { AtSeconds = 0, Label = "TIME", ColorHex = "#DC2626", Phase = TimerPhase.Overtime, Sound = CueSound.Speak, Say = "time" }
            }
        }
    };

    public static string LabelFor(int seconds) => seconds switch
    {
        <= 0 => "TIME",
        < 60 => $"{seconds} SEC",
        _ when seconds % 60 == 0 => $"{seconds / 60} MIN",
        _ => TimerTick.FormatSeconds(seconds)
    };
}

/// <summary>Colour sets for the four phases. The colour-blind-safe set avoids red/green contrast.</summary>
public enum CuePalette { Standard, ColourBlindSafe }

/// <summary>How the speaker-facing timer is written.</summary>
public enum SpeakerTimerFormat
{
    /// <summary>MM:SS / H:MM:SS, as the operator sees it.</summary>
    Standard,
    /// <summary>"12 MIN" until the last minute, then MM:SS — no ticking seconds to fixate on.</summary>
    Calm,
    /// <summary>No numbers: the progress bar and cue label only.</summary>
    ProgressOnly
}

/// <summary>Pure presentation rules for the speaker-facing outputs.</summary>
public static class SpeakerFace
{
    /// <summary>Okabe–Ito based: distinguishable with protan/deutan/tritan colour vision.</summary>
    public static readonly (string Normal, string Warning, string WrapUp, string Overtime) ColourBlindColors =
        ("#FFFFFF", "#56B4E9", "#F0E442", "#D55E00");

    public static string ColorFor(CuePalette palette, TimerPhase phase, string ladderColor) => palette switch
    {
        CuePalette.ColourBlindSafe => phase switch
        {
            TimerPhase.Warning => ColourBlindColors.Warning,
            TimerPhase.WrapUp => ColourBlindColors.WrapUp,
            TimerPhase.Overtime => ColourBlindColors.Overtime,
            _ => ColourBlindColors.Normal
        },
        _ => ladderColor
    };

    /// <summary>The speaker's timer text for a countdown showing <paramref name="seconds"/>.</summary>
    public static string Format(SpeakerTimerFormat format, long seconds, string standardText) => format switch
    {
        SpeakerTimerFormat.Calm when seconds > 60 => (seconds + 59) / 60 + " MIN",
        SpeakerTimerFormat.ProgressOnly => "",
        _ => standardText
    };

    /// <summary>"FINISH BY 10:49" — what speakers think in, per the coaches. No seconds.</summary>
    public static string FinishBy(DateTime? endsAtLocal, bool twelveHour)
        => endsAtLocal is { } t ? "FINISH BY " + Clock.Time(t, twelveHour, seconds: false) : "";
}

/// <summary>
/// Message templates: <c>{NEXT}</c>, <c>{FINISH_BY}</c>, <c>{OVER_UNDER}</c>, <c>{REMAINING}</c>,
/// <c>{TITLE}</c>, <c>{SPEAKER}</c>, <c>{TIME}</c>. Re-rendered every frame, so a message such as
/// "Wrap up by {FINISH_BY}" stays true while the operator nudges.
/// </summary>
public static class Placeholders
{
    public sealed record Context(string Next, string FinishBy, string OverUnder, string Remaining, string Title, string Speaker, string Time);

    public static bool HasAny(string? text) => text is not null && text.IndexOf('{') >= 0 && text.IndexOf('}') > 0;

    public static string Render(string text, Context c)
    {
        if (!HasAny(text)) return text;
        return text
            .Replace("{NEXT}", c.Next, StringComparison.OrdinalIgnoreCase)
            .Replace("{FINISH_BY}", c.FinishBy, StringComparison.OrdinalIgnoreCase)
            .Replace("{OVER_UNDER}", c.OverUnder, StringComparison.OrdinalIgnoreCase)
            .Replace("{REMAINING}", c.Remaining, StringComparison.OrdinalIgnoreCase)
            .Replace("{TITLE}", c.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{SPEAKER}", c.Speaker, StringComparison.OrdinalIgnoreCase)
            .Replace("{TIME}", c.Time, StringComparison.OrdinalIgnoreCase);
    }

    public static string OverUnder(long? offsetSeconds)
    {
        if (offsetSeconds is not { } o || Math.Abs(o) < 30) return "on time";
        var t = TimerTick.FormatSeconds(Math.Abs(o));
        return o > 0 ? $"{t} behind" : $"{t} ahead";
    }

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
