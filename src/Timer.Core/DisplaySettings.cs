using System.Text.Json.Serialization;

namespace Stagewatch.Core;

public enum OutputMode
{
    /// <summary>Output is closed.</summary>
    Closed,

    /// <summary>Borderless full-screen on a chosen monitor.</summary>
    FullScreen,

    /// <summary>Borderless full-screen with chroma-key colored background.</summary>
    KeyedFullScreen,

    /// <summary>Floating, resizable, always-on-top mini view.</summary>
    FloatingMinimal
}

public enum KeyColor
{
    Blue,
    Green,
    Magenta,
    Black
}

/// <summary>Which elements appear on the output window.</summary>
[Flags]
public enum DisplayElements
{
    None        = 0,
    Timer       = 1 << 0,
    Message     = 1 << 1,
    TimeOfDay   = 1 << 2,
    EndTime     = 1 << 3,
    PresetName  = 1 << 4,
    NextUp      = 1 << 5,
    ProgressBar = 1 << 6,
    Schedule    = 1 << 7,
    /// <summary>The cue-ladder label ("5 MIN", "WRAP UP") — a cue that doesn't rely on colour.</summary>
    CueLabel    = 1 << 8,
    /// <summary>"FINISH BY 10:49".</summary>
    FinishBy    = 1 << 9,
    /// <summary>Slide n / N and the next slide's title (PowerPoint).</summary>
    Slides      = 1 << 10,
    /// <summary>Video (VT) countdown from vMix.</summary>
    Playout     = 1 << 11,
    All         = Timer | Message | TimeOfDay | EndTime | PresetName | NextUp | ProgressBar | Schedule | CueLabel | FinishBy | Slides | Playout
}

/// <summary>Where the countdown sits relative to its thresholds. Drives colour on every output.</summary>
public enum TimerPhase
{
    Normal,
    Warning,
    WrapUp,
    Overtime
}

public sealed class DisplaySettings
{
    [JsonPropertyName("outputMode")]
    public OutputMode OutputMode { get; set; } = OutputMode.Closed;

    [JsonPropertyName("targetMonitorDeviceName")]
    public string? TargetMonitorDeviceName { get; set; }

    [JsonPropertyName("keyColor")]
    public KeyColor KeyColor { get; set; } = KeyColor.Blue;

    [JsonPropertyName("elements")]
    public DisplayElements Elements { get; set; } = DisplayElements.Timer | DisplayElements.Message;

    /// <summary>Floating window bounds (saved between runs).</summary>
    [JsonPropertyName("floatingBounds")]
    public WindowBounds FloatingBounds { get; set; } = new() { X = 80, Y = 80, Width = 480, Height = 240 };

    [JsonPropertyName("ndiEnabled")]
    public bool NdiEnabled { get; set; }

    [JsonPropertyName("ndiSourceName")]
    public string NdiSourceName { get; set; } = "Stagewatch";

    [JsonPropertyName("ndiFrameRate")]
    public int NdiFrameRate { get; set; } = 30;

    [JsonPropertyName("fontFamily")]
    public string FontFamily { get; set; } = "Segoe UI";

    [JsonPropertyName("timerFontWeight")]
    public int TimerFontWeight { get; set; } = 700;

    /// <summary>Show "ends HH:MM:SS" beneath the timer in countdown modes.</summary>
    [JsonPropertyName("showEndTime")]
    public bool ShowEndTime { get; set; } = true;

    /// <summary>Seconds remaining at which the timer face turns warning color.</summary>
    [JsonPropertyName("warningSeconds")]
    public int WarningSeconds { get; set; } = 120;

    /// <summary>Color of the timer when remaining is between WarningSeconds and 0.</summary>
    [JsonPropertyName("warningColorHex")]
    public string WarningColorHex { get; set; } = "#F59E0B";

    /// <summary>Second, later threshold ("wrap up now"). Must be below WarningSeconds to show.</summary>
    [JsonPropertyName("wrapUpSeconds")]
    public int WrapUpSeconds { get; set; } = 30;

    [JsonPropertyName("wrapUpColorHex")]
    public string WrapUpColorHex { get; set; } = "#F97316";

    /// <summary>Color of the timer when in overtime (remaining &lt; 0).</summary>
    [JsonPropertyName("overtimeColorHex")]
    public string OvertimeColorHex { get; set; } = "#DC2626";

    /// <summary>Color of the timer when remaining is above the warning threshold.</summary>
    [JsonPropertyName("normalColorHex")]
    public string NormalColorHex { get; set; } = "#FFFFFF";

    /// <summary>Show clock times as 2:30:15 PM instead of 14:30:15.</summary>
    [JsonPropertyName("use12HourClock")]
    public bool Use12HourClock { get; set; }

    /// <summary>Flash rate the moment the countdown crosses zero ("time's up").</summary>
    [JsonPropertyName("expiredFlashRate")]
    public FlashRate ExpiredFlashRate { get; set; } = FlashRate.Off;

    /// <summary>Cue ladder used when an item doesn't name its own ("" = Standard, from the thresholds above).</summary>
    [JsonPropertyName("cueLadder")]
    public string CueLadderId { get; set; } = "";

    /// <summary>Operator-defined ladders, alongside the built-in ones.</summary>
    [JsonPropertyName("customLadders")]
    public List<CueLadder> CustomLadders { get; set; } = new();

    [JsonPropertyName("palette")]
    public CuePalette Palette { get; set; } = CuePalette.Standard;

    /// <summary>How the speaker-facing timer is written (standard / calm / progress only).</summary>
    [JsonPropertyName("speakerFormat")]
    public SpeakerTimerFormat SpeakerFormat { get; set; } = SpeakerTimerFormat.Standard;

    /// <summary>
    /// Never show a stretched clock to the speaker: while elastic end-at is stretching, speaker outputs
    /// show the real time left. The operator still sees (and is told about) the stretch.
    /// </summary>
    [JsonPropertyName("honestSpeakerClock")]
    public bool HonestSpeakerClock { get; set; }

    /// <summary>Reserve the message area so the timer never jumps when a message appears.</summary>
    [JsonPropertyName("stableLayout")]
    public bool StableLayout { get; set; } = true;

    /// <summary>Holographic 3D style (three.js) on the output window and browser screens.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("holographic")]
    public bool Holographic { get; set; }

    /// <summary>Holographic style: show the projector base, light cone and floor reflection.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("holoFloor")]
    public bool HoloFloor { get; set; } = true;

    /// <summary>3D display: the egg timer (hourglass) instead of the hologram.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("eggTimer")]
    public bool EggTimer { get; set; }

    /// <summary>3D display: animated transitions and attention effects (messages build in, warnings breathe, critical alarms).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("animations3d")]
    public bool Animations3D { get; set; } = true;

    /// <summary>3D display: particles (dust in the beam, motes around the egg timer).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("particles3d")]
    public bool Particles3D { get; set; } = true;

    /// <summary>All ladders the operator can pick from: Standard (from the thresholds), built-ins, then custom.</summary>
    public IReadOnlyList<CueLadder> AllLadders()
    {
        var list = new List<CueLadder> { CueLadder.FromDisplay(this) };
        list.AddRange(CueLadder.BuiltIn);
        list.AddRange(CustomLadders.Where(c => !string.IsNullOrWhiteSpace(c.Id) && list.All(l => l.Id != c.Id)));
        return list;
    }

    public CueLadder LadderFor(string? id)
    {
        var want = string.IsNullOrWhiteSpace(id) ? CueLadderId : id;
        return AllLadders().FirstOrDefault(l => l.Id.Equals(want, StringComparison.OrdinalIgnoreCase)) ?? CueLadder.FromDisplay(this);
    }

    public TimerPhase PhaseFor(TimerMode mode, TimerState state, long displaySeconds)
    {
        if (mode != TimerMode.Countdown || state == TimerState.Idle && displaySeconds <= 0) return TimerPhase.Normal;
        if (state == TimerState.Expired || displaySeconds < 0) return TimerPhase.Overtime;
        if (displaySeconds <= WrapUpSeconds) return TimerPhase.WrapUp;
        if (displaySeconds <= WarningSeconds) return TimerPhase.Warning;
        return TimerPhase.Normal;
    }

    public string ColorFor(TimerPhase phase) => phase switch
    {
        TimerPhase.Warning  => WarningColorHex,
        TimerPhase.WrapUp   => WrapUpColorHex,
        TimerPhase.Overtime => OvertimeColorHex,
        _                   => NormalColorHex
    };
}

/// <summary>One place that turns a wall-clock time into text, honouring the 12/24-hour setting.</summary>
public static class Clock
{
    public static string Time(DateTime t, bool twelveHour, bool seconds = true) =>
        t.ToString(twelveHour ? (seconds ? "h:mm:ss tt" : "h:mm tt") : (seconds ? "HH:mm:ss" : "HH:mm"),
            System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class WindowBounds
{
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}
