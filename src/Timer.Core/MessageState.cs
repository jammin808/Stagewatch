using System.Text.Json.Serialization;

namespace Stagewatch.Core;

public enum MessageUrgency
{
    Info,
    Warning,
    Critical
}

public enum FlashRate
{
    Off,
    Slow,   // ~1 Hz
    Fast    // ~3 Hz
}

/// <summary>The current operator-driven message + flash state shown on outputs.</summary>
public sealed class MessageState
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("urgency")]
    public MessageUrgency Urgency { get; set; } = MessageUrgency.Info;

    [JsonPropertyName("flash")]
    public FlashRate Flash { get; set; } = FlashRate.Off;

    /// <summary>If true the timer face itself flashes (not just the message).</summary>
    [JsonPropertyName("flashTimer")]
    public bool FlashTimer { get; set; }

    /// <summary>Auto-flash thresholds applied by the engine in countdown mode.</summary>
    [JsonPropertyName("autoFlashWarnSeconds")]
    public int AutoFlashWarnSeconds { get; set; } = 120;

    [JsonPropertyName("autoFlashCriticalSeconds")]
    public int AutoFlashCriticalSeconds { get; set; } = 30;

    public MessageState Clone() => new()
    {
        Text = Text,
        Urgency = Urgency,
        Flash = Flash,
        FlashTimer = FlashTimer,
        AutoFlashWarnSeconds = AutoFlashWarnSeconds,
        AutoFlashCriticalSeconds = AutoFlashCriticalSeconds
    };
}

/// <summary>Pre-canned messages for one-tap send.</summary>
public sealed class MessageBank
{
    [JsonPropertyName("entries")]
    public List<MessageBankEntry> Entries { get; set; } = new();

    public static MessageBank Default() => new()
    {
        Entries = new List<MessageBankEntry>
        {
            new() { Label = "5 min remaining",  Text = "5 minutes remaining",  Urgency = MessageUrgency.Info,     Flash = FlashRate.Off  },
            new() { Label = "2 min remaining",  Text = "2 minutes remaining",  Urgency = MessageUrgency.Warning,  Flash = FlashRate.Off  },
            new() { Label = "Wrap up",          Text = "Please wrap up",       Urgency = MessageUrgency.Warning,  Flash = FlashRate.Slow },
            new() { Label = "Stop now",         Text = "STOP",                 Urgency = MessageUrgency.Critical, Flash = FlashRate.Fast },
            new() { Label = "Q&A",              Text = "Q&A",                  Urgency = MessageUrgency.Info,     Flash = FlashRate.Off  },
            new() { Label = "Slow down",        Text = "Slow down",            Urgency = MessageUrgency.Info,     Flash = FlashRate.Off  },
            new() { Label = "Speak up",         Text = "Speak up",             Urgency = MessageUrgency.Info,     Flash = FlashRate.Off  },
            new() { Label = "Look at camera",   Text = "Look at camera",       Urgency = MessageUrgency.Info,     Flash = FlashRate.Off  }
        }
    };
}

public sealed class MessageBankEntry
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("urgency")] public MessageUrgency Urgency { get; set; }
    [JsonPropertyName("flash")] public FlashRate Flash { get; set; }
}
