using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// One event's content — presets, running order and quick messages — saved as a
/// <c>.stagewatch</c> file so a freelancer can keep a file per client / event and open it on the day.
/// Display settings, licence and network settings stay with the PC, not the show.
/// </summary>
public sealed class ShowFile
{
    public const string Extension = ".stagewatch";
    public const string FormatId = "stagewatch-show";
    public const int CurrentVersion = 1;

    [JsonPropertyName("format")]   public string Format { get; set; } = FormatId;
    [JsonPropertyName("version")]  public int Version { get; set; } = CurrentVersion;
    [JsonPropertyName("name")]     public string Name { get; set; } = "";
    [JsonPropertyName("savedAt")]  public DateTime SavedAtUtc { get; set; }
    [JsonPropertyName("presets")]  public List<TimerPreset> Presets { get; set; } = new();
    [JsonPropertyName("messageBank")] public MessageBank MessageBank { get; set; } = new();
    [JsonPropertyName("rundown")]  public Rundown Rundown { get; set; } = new();
    [JsonPropertyName("rundownName")] public string RundownName { get; set; } = "Main";
    /// <summary>The show's other running orders (e.g. Day 2, the rehearsal version).</summary>
    [JsonPropertyName("otherRundowns")] public List<NamedRundown> OtherRundowns { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static ShowFile FromSettings(AppSettings s, string name) => new()
    {
        Name = name,
        SavedAtUtc = DateTime.UtcNow,
        Presets = s.Presets.Select(Clone).ToList(),
        MessageBank = new MessageBank { Entries = s.MessageBank.Entries.Select(e => new MessageBankEntry { Label = e.Label, Text = e.Text, Urgency = e.Urgency, Flash = e.Flash }).ToList() },
        Rundown = s.Rundown.Clone(),
        RundownName = s.RundownName,
        OtherRundowns = s.OtherRundowns.Select(r => new NamedRundown { Name = r.Name, Rundown = r.Rundown.Clone() }).ToList(),
    };

    /// <summary>Copy this show's content into the live settings (presets padded/trimmed to 12 slots).</summary>
    public void ApplyTo(AppSettings s)
    {
        var bySlot = Presets.Where(p => p.Slot is >= 1 and <= 12).GroupBy(p => p.Slot).ToDictionary(g => g.Key, g => g.First());
        s.Presets = Enumerable.Range(1, 12).Select(i => bySlot.TryGetValue(i, out var p) ? Clone(p) : TimerPreset.Default(i)).ToList();
        s.MessageBank = MessageBank;
        s.Rundown = Rundown;
        s.RundownName = RundownName;
        s.OtherRundowns = OtherRundowns;
    }

    public void Save(string path)
    {
        SavedAtUtc = DateTime.UtcNow;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Load and validate. Throws <see cref="InvalidDataException"/> with a readable reason.</summary>
    public static ShowFile Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new InvalidDataException("The file doesn't exist.");
        if (info.Length > 10 * 1024 * 1024) throw new InvalidDataException("That file is far too big to be a Stagewatch show.");

        ShowFile? show;
        try { show = JsonSerializer.Deserialize<ShowFile>(File.ReadAllText(path), Json); }
        catch (JsonException ex) { throw new InvalidDataException("This isn't a readable Stagewatch show file (" + ex.Message + ")."); }

        if (show is null || show.Format != FormatId) throw new InvalidDataException("This isn't a Stagewatch show file.");
        if (show.Version > CurrentVersion) throw new InvalidDataException($"This show was saved by a newer Stagewatch (format {show.Version}). Update Stagewatch to open it.");

        show.Presets ??= new();
        show.MessageBank ??= new MessageBank();
        show.MessageBank.Entries ??= new();
        show.Rundown = Sanitize(show.Rundown);
        show.OtherRundowns = (show.OtherRundowns ?? new())
            .Where(r => r is not null && NamedRundown.ValidName(r.Name))
            .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .Select(r => new NamedRundown { Name = r.Name.Trim(), Rundown = Sanitize(r.Rundown) })
            .ToList();
        show.RundownName = NamedRundown.ValidName(show.RundownName) ? show.RundownName.Trim() : "Main";
        show.OtherRundowns.RemoveAll(r => string.Equals(r.Name, show.RundownName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(show.Name)) show.Name = Path.GetFileNameWithoutExtension(path);
        return show;
    }

    /// <summary>
    /// Same rules as typing it in: "HH:mm" or a dated "yyyy-MM-dd HH:mm" (multi-day shows). The time zone is kept even
    /// if this PC doesn't know it, so re-saving never loses it (ShowClock falls back to the PC's zone meanwhile).
    /// </summary>
    private static Rundown Sanitize(Rundown? r)
    {
        r ??= new Rundown();
        r.Items ??= new();
        r.Items.RemoveAll(i => i is null);
        r.EnsureIds();
        r.GroupTargets = new Dictionary<string, int>(r.GroupTargets ?? new(), StringComparer.OrdinalIgnoreCase);
        r.LabelColors = new Dictionary<string, string>((r.LabelColors ?? new())
            .Where(kv => System.Text.RegularExpressions.Regex.IsMatch(kv.Value ?? "", "^#[0-9A-Fa-f]{6}$")), StringComparer.OrdinalIgnoreCase);
        if (!ShowClock.IsValidTimeText(r.ScheduledStart)) r.ScheduledStart = null;
        if (string.IsNullOrWhiteSpace(r.TimeZoneId)) r.TimeZoneId = null;
        var ids = new HashSet<string>();
        foreach (var i in r.Items)
        {
            i.Title ??= "";
            if (i.DurationSeconds < 0) i.DurationSeconds = 0;
            i.DeptNotes = new Dictionary<string, string>(i.DeptNotes ?? new(), StringComparer.OrdinalIgnoreCase);
            i.Labels = Labels.Parse(string.Join(",", i.Labels ?? new()));
            i.Chimes ??= new ItemChimes();
            foreach (var spec in new[] { i.Chimes.Start, i.Chimes.Warning, i.Chimes.WrapUp, i.Chimes.Zero })
                if (!ItemChimes.ValidSpec(spec)) { i.Chimes = new ItemChimes(); break; }
            if (string.IsNullOrWhiteSpace(i.Id) || !ids.Add(i.Id)) { i.Id = Guid.NewGuid().ToString("N")[..10]; ids.Add(i.Id); }
        }
        return r;
    }

    private static TimerPreset Clone(TimerPreset p) => new()
    {
        Slot = p.Slot, Label = p.Label ?? $"Preset {p.Slot}", DurationSeconds = p.DurationSeconds, Message = p.Message, ColorHex = p.ColorHex
    };
}
