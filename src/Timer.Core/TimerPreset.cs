using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>One of 12 quick-select preset slots on the main panel.</summary>
public sealed class TimerPreset
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>Duration in seconds. Negative means count up.</summary>
    [JsonPropertyName("durationSeconds")]
    public int DurationSeconds { get; set; }

    /// <summary>Optional message that auto-loads when this preset is selected.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Hex color tag, e.g. "#22C55E". Null = use theme default.</summary>
    [JsonPropertyName("colorHex")]
    public string? ColorHex { get; set; }

    public static TimerPreset Default(int slot) => new()
    {
        Slot = slot,
        Label = $"Preset {slot}",
        DurationSeconds = slot * 60
    };

    public static TimerPreset[] DefaultDeck()
    {
        // Sensible event defaults so the deck is useful out-of-the-box.
        return new[]
        {
            new TimerPreset { Slot = 1,  Label = "1 min",        DurationSeconds = 60 },
            new TimerPreset { Slot = 2,  Label = "2 min",        DurationSeconds = 120 },
            new TimerPreset { Slot = 3,  Label = "5 min",        DurationSeconds = 300 },
            new TimerPreset { Slot = 4,  Label = "10 min",       DurationSeconds = 600 },
            new TimerPreset { Slot = 5,  Label = "15 min",       DurationSeconds = 900 },
            new TimerPreset { Slot = 6,  Label = "20 min",       DurationSeconds = 1200 },
            new TimerPreset { Slot = 7,  Label = "30 min",       DurationSeconds = 1800 },
            new TimerPreset { Slot = 8,  Label = "45 min",       DurationSeconds = 2700 },
            new TimerPreset { Slot = 9,  Label = "1 hour",       DurationSeconds = 3600 },
            new TimerPreset { Slot = 10, Label = "Q&A 5",        DurationSeconds = 300, Message = "Q&A — 5 minutes" },
            new TimerPreset { Slot = 11, Label = "Wrap 1 min",   DurationSeconds = 60,  Message = "Wrap up please" },
            new TimerPreset { Slot = 12, Label = "Hard stop",    DurationSeconds = 30,  Message = "STOP", ColorHex = "#DC2626" }
        };
    }
}
