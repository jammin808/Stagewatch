using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// What a presenter PC (Stagewatch Slides) reports about its slideshow. Sent whole, every change and every second —
/// like a game client's state packet: a lost one doesn't matter, the next one carries everything. Times are
/// durations ("how long has this slide been up"), never clock readings, so the two PCs' clocks never need to agree.
/// </summary>
public sealed record SlideFeed
{
    [JsonPropertyName("v")] public int Version { get; init; } = 1;
    /// <summary>The agent's own id (kept in its settings).</summary>
    [JsonPropertyName("agent")] public string Agent { get; init; } = "";
    /// <summary>What the operator calls this PC ("Lectern laptop").</summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("machine")] public string Machine { get; init; } = "";
    /// <summary>True while a slideshow is running (not just the editor).</summary>
    [JsonPropertyName("presenting")] public bool Presenting { get; init; }
    [JsonPropertyName("presentation")] public string Presentation { get; init; } = "";
    [JsonPropertyName("slide")] public int Slide { get; init; }
    [JsonPropertyName("count")] public int Count { get; init; }
    [JsonPropertyName("next")] public string NextTitle { get; init; } = "";
    /// <summary>The current slide's notes (capped).</summary>
    [JsonPropertyName("notes")] public string Notes { get; init; } = "";
    [JsonPropertyName("budgets")] public double[] Budgets { get; init; } = Array.Empty<double>();
    [JsonPropertyName("slideMs")] public long SlideMs { get; init; }
    [JsonPropertyName("showMs")] public long ShowMs { get; init; }
    /// <summary>SHA-256 (hex, 16 chars) of the whole-deck script; the script itself is sent once, on request.</summary>
    [JsonPropertyName("script")] public string ScriptHash { get; init; } = "";
    /// <summary>Agent sequence number: older packets arriving late are ignored.</summary>
    [JsonPropertyName("seq")] public long Seq { get; init; }

    public const int MaxNotes = 8000;
    public const int MaxScript = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new();

    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json);

    /// <summary>Parse and sanity-check a feed from the network (untrusted). Null when it isn't one.</summary>
    public static SlideFeed? Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            var f = JsonSerializer.Deserialize<SlideFeed>(json, Json);
            if (f is null || f.Version != 1 || !Valid(f.Agent)) return null;
            f = f with { Budgets = f.Budgets ?? Array.Empty<double>() };   // "budgets": null is valid JSON
            return f with
            {
                Name = Cap(f.Name, 60), Machine = Cap(f.Machine, 60), Presentation = Cap(f.Presentation, 200),
                NextTitle = Cap(f.NextTitle, 200), Notes = Cap(f.Notes, MaxNotes),
                Slide = Math.Clamp(f.Slide, 0, 10_000), Count = Math.Clamp(f.Count, 0, 10_000),
                Budgets = f.Budgets.Length > 2000 ? f.Budgets[..2000] : f.Budgets.Select(b => double.IsFinite(b) ? Math.Clamp(b, 0, 86400) : 0).ToArray(),
                SlideMs = Math.Clamp(f.SlideMs, 0, 86_400_000), ShowMs = Math.Clamp(f.ShowMs, 0, 86_400_000),
                ScriptHash = Cap(f.ScriptHash, 32),
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or NullReferenceException) { return null; }
    }

    public static bool Valid(string? id) => !string.IsNullOrEmpty(id) && id.Length is >= 6 and <= 32 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string HashScript(string script)
        => script.Length == 0 ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)))[..16].ToLowerInvariant();

    private static string Cap(string? s, int n) => s is null ? "" : s.Length > n ? s[..n] : s;
}

