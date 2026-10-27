using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in %APPDATA%\Stagewatch.
/// Saves are debounced — call <see cref="ScheduleSave"/> liberally; the store coalesces.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly System.Threading.Timer _debounceTimer;
    private AppSettings _current;
    private readonly object _lock = new();

    public SettingsStore() : this(DefaultPath()) { }

    public SettingsStore(string path)
    {
        _path = path;
        var notes = new LoadNotes();
        _current = Load(path, notes);
        Notes = notes.Words;
        // Persist identities assigned on load at once, so a crash before the next save can't change them.
        if (File.Exists(path) && (_current.Rundown.Id.Length > 0) && !File.ReadAllText(path).Contains(_current.Rundown.Id, StringComparison.Ordinal))
            SaveNow();
        _debounceTimer = new System.Threading.Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// The live settings. <b>Owner thread only</b> (the UI thread in the app): read them there, and change them only
    /// through <see cref="Update"/>. Other threads use <see cref="Snapshot"/>.
    /// </summary>
    public AppSettings Settings
    {
        get { lock (_lock) return _current; }
    }

    private long _version;
    private long _snapshotVersion = -1;
    private AppSettings? _snapshot;

    /// <summary>
    /// A private copy of the settings as they were after the last change, for readers on other threads (web requests,
    /// trigger workers). Never the live object, so a reader can't see a half-made change or trip over a list being edited.
    /// Rebuilt on the first read after a change; treat it as read-only (changing it changes nothing).
    /// </summary>
    public AppSettings Snapshot
    {
        get
        {
            lock (_lock)
            {
                if (_snapshot is null || _snapshotVersion != _version)
                {
                    _snapshot = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(_current, JsonOpts), JsonOpts) ?? new AppSettings();
                    _snapshotVersion = _version;
                }
                return _snapshot;
            }
        }
    }

    /// <summary>Mutate settings under the lock and queue a debounced save.</summary>
    public void Update(Action<AppSettings> mutate)
    {
        lock (_lock) { mutate(_current); _version++; }
        ScheduleSave();
    }

    /// <summary>Defer a save by 500ms — repeated calls coalesce. (Also marks the snapshot stale.)</summary>
    public void ScheduleSave()
    {
        Interlocked.Increment(ref _version);
        _debounceTimer.Change(500, Timeout.Infinite);
    }

    /// <summary>Force an immediate write (e.g. on shutdown).</summary>
    public void SaveNow()
    {
        // One writer at a time (the debounce timer, shutdown and explicit saves can overlap). The settings are
        // captured *inside* the writer lock, so saves reach the disk in the order they captured: a slow caller can
        // never write an older moment over a newer one. Lock order is always _writeLock → _lock (Update() takes
        // only _lock), so this can't deadlock.
        lock (_writeLock)
        {
            // Serialize under the same lock as Update(): the show thread can't change the settings half-way
            // through, so the file is always one consistent moment. (Small object; well under a millisecond.)
            string json;
            try
            {
                lock (_lock)
                {
                    // Every file written carries link "patterns" in the 2.17 keys too, so a rollback keeps that link.
                    if (_current.Integrations is { } integrations) PlayoutRules.WriteMirror(integrations);
                    json = JsonSerializer.Serialize(_current, JsonOpts);
                }
            }
            catch (Exception ex)
            {
                SettingsSaveFailed?.Invoke(ex);
                return;
            }

            if (json == _lastWritten) return;
            try
            {
                // Flushed to the disk and swapped in; the last whole file is kept as .bak (see AtomicFile).
                AtomicFile.Write(_path, json, AtomicFile.IsJson);
                _lastWritten = json;
            }
            catch (Exception ex)
            {
                // Settings save failure must not crash the app mid-show.
                // Surface via SettingsSaveFailed for UI to log.
                SettingsSaveFailed?.Invoke(ex);
            }
        }
    }

    private readonly object _writeLock = new();
    private string? _lastWritten;

    public event Action<Exception>? SettingsSaveFailed;

    /// <summary>True when the settings file was damaged and the last whole copy (.bak) was loaded instead.</summary>
    public static bool LoadedFromBackup { get; private set; }

    /// <summary>
    /// What this store's load left out of the file or changed in it (a playout link or cue it could not keep, an address it would not
    /// dial), in words for the desk to say once: by name, never a token. Empty when the file was kept as it was.
    /// </summary>
    public IReadOnlyList<string> Notes { get; }

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Stagewatch", "settings.json");
    }

    private static AppSettings Load(string path, LoadNotes notes)
    {
        LoadedFromBackup = false;
        try
        {
            // No file is a fresh start (someone reset it on purpose) — never a reason to bring back an old backup.
            if (!File.Exists(path)) return Migrate(new AppSettings(), notes);
            // A damaged file falls back to the last whole one (.bak) before ever starting from nothing.
            var json = AtomicFile.ReadWithBackup(path, IsSettings, out var fromBackup);
            if (fromBackup && File.Exists(path)) Quarantine(path);
            if (json is null) throw new JsonException("no readable settings");
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            LoadedFromBackup = fromBackup;
            return Migrate(s, notes);
        }
        catch
        {
            // Corrupted file and no good backup — keep it for inspection and start fresh, never block startup.
            Quarantine(path);
            notes.Clear();   // what a load that failed half-way said is not what this fresh start did
            return Migrate(new AppSettings(), notes);
        }

        static bool IsSettings(string text)
        {
            try { return JsonSerializer.Deserialize<AppSettings>(text, JsonOpts) is not null; }
            catch (JsonException) { return false; }
            catch (NotSupportedException) { return false; }
        }

        static void Quarantine(string p)
        {
            try { if (File.Exists(p)) File.Copy(p, p + ".corrupt-" + DateTime.UtcNow.Ticks, overwrite: true); } catch { }
        }
    }

    private static AppSettings Migrate(AppSettings s, LoadNotes notes)
    {
        // CountdownToTime mode was removed in favor of unified Countdown + end-at.
        // Anything saved as that mode collapses back to Countdown.
        if (!Enum.IsDefined(s.LastMode)) s.LastMode = TimerMode.Countdown;

        // Every load: PlayoutRules.Upgrade (below) keeps link "patterns" in step with the 2.17 keys, which win when they differ.
        if (s.SchemaVersion < AppSettings.CurrentSchemaVersion)
            s.SchemaVersion = AppSettings.CurrentSchemaVersion;

        // First run (or pre-remote settings file): give the remote control a PIN so the
        // LAN can watch freely but can't drive the show without it.
        s.Remote ??= new RemoteSettings();
        s.Alerts ??= new AlertSettings();
        if (string.IsNullOrEmpty(s.Remote.CrewSecret)) s.Remote.CrewSecret = CrewKeys.NewSecret();
        s.Remote.ApiKeys = ApiKeys.Sanitise(s.Remote.ApiKeys);
        s.Remote.Layouts = (s.Remote.Layouts ?? new()).Select(l => ScreenLayouts.Sanitise(l, out _)).OfType<ScreenLayout>()
            .DistinctBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Take(ScreenLayouts.MaxLayouts).ToList();
        if (s.Remote.Pin is null)
            s.Remote.Pin = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1000, 10000).ToString();
        s.Rundown ??= new Rundown();
        s.Rundown.Items ??= new List<RundownItem>();
        // Stable identities for history (journal, reports): assigned once, then saved.
        s.Rundown.EnsureIds();
        foreach (var other in s.OtherRundowns ?? new()) other.Rundown?.EnsureIds();
        // Playout cues are input like a show file's: kept only as PlayoutRules allows them.
        PlayoutRules.SanitiseCues(s.Rundown, notes, s.RundownName);
        foreach (var other in s.OtherRundowns ?? new()) PlayoutRules.SanitiseCues(other?.Rundown, notes, other?.Name ?? "");
        // 2.0 sections: absent in older files, or nulled by hand edits.
        s.Integrations ??= new IntegrationSettings();
        PlayoutRules.Upgrade(s.Integrations, notes);
        s.Signals ??= new SignalSettings();
        s.Signals.Departments ??= new List<string>();
        s.Triggers ??= new List<TriggerRule>();
        s.Holding ??= new HoldingSettings();
        s.Panels ??= new PanelLayoutSettings();
        if (s.NodeId is not { Length: 12 } || !s.NodeId.All(char.IsAsciiHexDigitLower))
            s.NodeId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        PanelLayout.Normalize(s.Panels);
        s.Holding.Images ??= new List<HoldingImage>();
        s.Holding.Images.RemoveAll(i => i is null || !HoldingLibrary.IsValidId(i.Id));
        s.Display ??= new DisplaySettings();
        s.Display.CustomLadders ??= new List<CueLadder>();
        s.MessageBank ??= MessageBank.Default();
        s.MessageBank.Entries ??= new List<MessageBankEntry>();

        // Defensive: ensure the deck always has 12 slots.
        if (s.Presets.Count < 12)
        {
            var have = s.Presets.Select(p => p.Slot).ToHashSet();
            for (int i = 1; i <= 12; i++)
                if (!have.Contains(i)) s.Presets.Add(TimerPreset.Default(i));
            s.Presets = s.Presets.OrderBy(p => p.Slot).ToList();
        }
        return s;
    }

    public void Dispose()
    {
        SaveNow();
        _debounceTimer.Dispose();
    }
}
