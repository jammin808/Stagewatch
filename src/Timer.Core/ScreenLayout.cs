using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Stagewatch.Core;

/// <summary>
/// A designed screen: elements placed on a 100×100 canvas (percent of the screen), each showing the timer, a message,
/// a progress bar, labels, a box, a holding image, or text with placeholders ("Next: {next} at {nextStart}"). Browser
/// screens open one at /layout?name=…; the designer edits them live. Untrusted input (browsers, the copilot): everything
/// is checked by <see cref="ScreenLayouts.Sanitise"/> before it's stored or served.
/// </summary>
public sealed class ScreenLayout
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("background")] public string Background { get; set; } = "#000000";
    [JsonPropertyName("font")] public string Font { get; set; } = "";
    [JsonPropertyName("elements")] public List<LayoutElement> Elements { get; set; } = new();
}

public sealed class LayoutElement
{
    /// <summary>timer, text, message, progress, labels, box or image.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "text";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; } = 50;
    [JsonPropertyName("h")] public double H { get; set; } = 10;
    /// <summary>Text with placeholders (text elements; a prefix for message elements).</summary>
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    /// <summary>Font size in percent of the screen height.</summary>
    [JsonPropertyName("size")] public double Size { get; set; } = 6;
    /// <summary>"#RRGGBB", or "" for the default (the timer's cue colour, white otherwise).</summary>
    [JsonPropertyName("color")] public string Color { get; set; } = "";
    [JsonPropertyName("bg")] public string Background { get; set; } = "";
    [JsonPropertyName("align")] public string Align { get; set; } = "center";
    [JsonPropertyName("bold")] public bool Bold { get; set; }
    /// <summary>Image elements: a holding image id.</summary>
    [JsonPropertyName("image")] public string Image { get; set; } = "";
}

public static class ScreenLayouts
{
    public const int MaxElements = 40;
    public const int MaxLayouts = 30;
    public static readonly string[] Kinds = { "timer", "text", "message", "progress", "labels", "box", "image" };
    public static readonly string[] Fonts = { "", "Segoe UI", "Arial", "Helvetica", "Verdana", "Georgia", "Consolas", "Bahnschrift", "Impact" };

    /// <summary>Every placeholder a text element may use, and what it shows.</summary>
    public static readonly IReadOnlyList<(string Token, string Meaning)> Placeholders = new[]
    {
        ("time", "the timer as the speaker sees it"),
        ("title", "the live item's title"),
        ("speaker", "the live item's speaker"),
        ("next", "the next item"),
        ("nextStart", "when the next item starts (expected)"),
        ("nextIn", "time until the next item, counting down"),
        ("item", "\"3 of 12\""),
        ("group", "the live item's group"),
        ("end", "when the live item ends"),
        ("showEnd", "when the show is expected to end"),
        ("clock", "time of day"),
        ("date", "today's date"),
        ("room", "the room name"),
        ("message", "the message on screen"),
        ("cue", "the cue label (\"5 MIN\", \"WRAP UP\")"),
        ("aux", "the extra timer line"),
        ("slide", "\"Slide 4 / 20\""),
    };

    private static readonly Regex Token = new(@"\{([A-Za-z]+)\}", RegexOptions.CultureInvariant);
    private static readonly Regex Hex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Placeholders in a template that Stagewatch doesn't know (shown to the designer, never an error).</summary>
    public static IReadOnlyList<string> UnknownTokens(string template)
        => Token.Matches(template ?? "").Select(m => m.Groups[1].Value)
            .Where(t => !Placeholders.Any(p => p.Token.Equals(t, StringComparison.Ordinal))).Distinct().ToList();

    public static bool ValidName(string? name) => ScreenPreset.ValidName(name);

    /// <summary>A checked copy, or null with the reason. Positions are clamped onto the screen; unknown kinds dropped.</summary>
    public static ScreenLayout? Sanitise(ScreenLayout? l, out string error)
    {
        error = "";
        if (l is null) { error = "no layout"; return null; }
        var name = (l.Name ?? "").Trim();
        if (!ValidName(name)) { error = "give the layout a short name (letters, digits, - or _)"; return null; }
        var outL = new ScreenLayout
        {
            Name = name,
            Background = HexOr(l.Background, "#000000"),
            Font = Fonts.Contains(l.Font ?? "") ? l.Font ?? "" : "",
        };
        foreach (var e in (l.Elements ?? new()).Where(e => e is not null).Take(MaxElements))
        {
            var kind = (e.Kind ?? "").Trim().ToLowerInvariant();
            if (!Kinds.Contains(kind)) continue;
            double C(double v, double lo, double hi) => double.IsFinite(v) ? Math.Round(Math.Clamp(v, lo, hi), 2) : lo;
            var x = C(e.X, 0, 99);
            var y = C(e.Y, 0, 99);
            var image = (e.Image ?? "").Trim();
            outL.Elements.Add(new LayoutElement
            {
                Kind = kind,
                X = x,
                Y = y,
                W = C(e.W, 1, 100 - x),
                H = C(e.H, 1, 100 - y),
                Text = Clean(e.Text, 300),
                Size = C(e.Size, 1, 80),
                Color = HexOr(e.Color, ""),
                Background = HexOr(e.Background, ""),
                Align = e.Align is "left" or "right" ? e.Align : "center",
                Bold = e.Bold,
                Image = image.Length <= 32 && image.All(char.IsAsciiLetterOrDigit) ? image : "",
            });
        }
        return outL;
    }

    public static string ToJson(ScreenLayout l) => JsonSerializer.Serialize(l);

    public static ScreenLayout? FromJson(string json, out string error)
    {
        error = "";
        try { return Sanitise(JsonSerializer.Deserialize<ScreenLayout>(json, Json), out error); }
        catch (JsonException) { error = "that isn't a layout"; return null; }
    }

    /// <summary>A starting point: the timer, title and next item, message underneath.</summary>
    public static ScreenLayout Starter(string name) => new()
    {
        Name = name,
        Elements =
        {
            new() { Kind = "text", X = 4, Y = 4, W = 92, H = 10, Text = "{title}", Size = 7, Bold = true },
            new() { Kind = "timer", X = 4, Y = 18, W = 92, H = 42, Size = 34, Bold = true },
            new() { Kind = "progress", X = 10, Y = 62, W = 80, H = 2 },
            new() { Kind = "message", X = 4, Y = 68, W = 92, H = 14, Size = 7, Bold = true },
            new() { Kind = "text", X = 4, Y = 86, W = 92, H = 8, Text = "Next: {next}  ·  {nextStart}", Size = 4.5, Color = "#94A3B8" },
        }
    };

    private static string HexOr(string? s, string fallback) => s is not null && Hex.IsMatch(s.Trim()) ? s.Trim().ToUpperInvariant() : fallback;

    private static string Clean(string? s, int max)
    {
        var t = new string((s ?? "").Where(c => !char.IsControl(c) || c == '\n').ToArray());
        return t.Length > max ? t[..max] : t;
    }
}
