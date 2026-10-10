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
        _current = Load(path);
        _debounceTimer = new System.Threading.Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppSettings Settings
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Mutate settings under the lock and queue a debounced save.</summary>
    public void Update(Action<AppSettings> mutate)
    {
        lock (_lock) { mutate(_current); }
        ScheduleSave();
    }

    /// <summary>Defer a save by 500ms — repeated calls coalesce.</summary>
    public void ScheduleSave() => _debounceTimer.Change(500, Timeout.Infinite);

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
                lock (_lock) { json = JsonSerializer.Serialize(_current, JsonOpts); }
            }
            catch (Exception ex)
            {
                SettingsSaveFailed?.Invoke(ex);
                return;
            }

            if (json == _lastWritten) return;
            var tmp = $"{_path}.{Environment.CurrentManagedThreadId}.{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(tmp, json);
                // Atomic replace: a crash leaves the old file or the new one, never half of either.
                // Retry briefly: antivirus or backup tools sometimes hold the file for a moment.
                for (var attempt = 0; ; attempt++)
                {
                    try { File.Move(tmp, _path, overwrite: true); break; }
                    catch (IOException) when (attempt < 4) { Thread.Sleep(25 * (attempt + 1)); }
                    catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(25 * (attempt + 1)); }
                }
                _lastWritten = json;
            }
            catch (Exception ex)
            {
                // Settings save failure must not crash the app mid-show.
                // Surface via SettingsSaveFailed for UI to log.
                try { File.Delete(tmp); } catch { }
                SettingsSaveFailed?.Invoke(ex);
            }
        }
    }

    private readonly object _writeLock = new();
    private string? _lastWritten;

    public event Action<Exception>? SettingsSaveFailed;

    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Stagewatch", "settings.json");
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Migrate(new AppSettings());
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            return Migrate(s);
        }
        catch
        {
            // Corrupted file — back it up and start fresh, never block startup.
            try { File.Copy(path, path + ".corrupt-" + DateTime.UtcNow.Ticks, overwrite: true); } catch { }
            return Migrate(new AppSettings());
        }
    }

    private static AppSettings Migrate(AppSettings s)
    {
        // CountdownToTime mode was removed in favor of unified Countdown + end-at.
        // Anything saved as that mode collapses back to Countdown.
        if (!Enum.IsDefined(s.LastMode)) s.LastMode = TimerMode.Countdown;

        // No migrations yet. When SchemaVersion bumps, add upgraders here.
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
        // 2.0 sections: absent in older files, or nulled by hand edits.
        s.Integrations ??= new IntegrationSettings();
        s.Signals ??= new SignalSettings();
        s.Signals.Departments ??= new List<string>();
        s.Triggers ??= new List<TriggerRule>();
        s.Holding ??= new HoldingSettings();
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
