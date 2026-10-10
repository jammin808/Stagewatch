using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// The live show, saved continuously so a crash, power cut or slip of the mouse doesn't lose it.
/// Separate from settings.json: it changes every couple of seconds and is disposable.
/// </summary>
public sealed class SessionState
{
    public int Version { get; set; } = 1;
    public DateTime SavedAtUtc { get; set; }
    /// <summary>True when the operator closed Stagewatch on purpose (nothing to offer back).</summary>
    public bool ClosedCleanly { get; set; }

    public EngineState? Engine { get; set; }
    public string Title { get; set; } = "";
    public int CurrentItem { get; set; } = -1;
    public bool RundownActive { get; set; }
    public bool CurrentItemStarted { get; set; }
    public DateTime? ShowAnchorUtc { get; set; }
    public DateTime? PinnedStartUtc { get; set; }

    public string MessageText { get; set; } = "";
    public MessageUrgency Urgency { get; set; }
    public FlashRate Flash { get; set; }
    public bool ManualTimerFlash { get; set; }
    public bool Blackout { get; set; }

    public OutputMode OutputMode { get; set; } = OutputMode.Closed;
    public bool NdiOn { get; set; }

    // 2.0: running-order truth and the journal, so a crash mid-show loses nothing for the report.
    public Dictionary<int, ItemActual>? Actuals { get; set; }
    public double PendingDelaySeconds { get; set; }
    public string? JournalPath { get; set; }

    /// <summary>Is there a show in progress worth offering back? (A loaded-but-idle timer isn't.)</summary>
    [JsonIgnore]
    public bool IsWorthRestoring =>
        Engine is { } e && (e.Counting || e.State is TimerState.Paused or TimerState.Expired)
        || RundownActive || MessageText.Length > 0 || OutputMode != OutputMode.Closed || Blackout;
}

/// <summary>Atomic, never-throwing persistence for <see cref="SessionState"/>.</summary>
public sealed class SessionStore
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly object _writeLock = new();
    private string _lastWritten = "";

    public SessionStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "session.json")) { }

    public SessionStore(string path) { _path = path; }

    /// <summary>A session worth offering back: not closed cleanly, recent, and with something in it.</summary>
    public SessionState? LoadRecoverable(DateTime nowUtc)
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var s = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(_path), Json);
            if (s is null || s.Version != 1 || s.ClosedCleanly) return null;
            if (nowUtc - s.SavedAtUtc > MaxAge || s.SavedAtUtc > nowUtc.AddMinutes(5)) return null;
            return s.IsWorthRestoring ? s : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null; // a damaged session file must never block startup
        }
    }

    /// <summary>Write if changed. Safe to call from any thread; temp-file + move, so never half-written.</summary>
    public void Save(SessionState s)
    {
        string text, key;
        try
        {
            // SavedAtUtc (and the capture time) change every call; compare without them so an idle
            // show isn't rewritten every couple of seconds.
            var saved = s.SavedAtUtc;
            var engine = s.Engine;
            s.SavedAtUtc = default;
            if (engine is not null && !engine.Counting) s.Engine = engine with { CapturedAtUtc = default };
            key = JsonSerializer.Serialize(s, Json);
            s.SavedAtUtc = saved;
            s.Engine = engine;
            text = JsonSerializer.Serialize(s, Json);
        }
        catch (NotSupportedException) { return; }
        lock (_writeLock)
        {
            if (key == _lastWritten && !s.ClosedCleanly) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, text);
                File.Move(tmp, _path, overwrite: true);
                _lastWritten = key;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
