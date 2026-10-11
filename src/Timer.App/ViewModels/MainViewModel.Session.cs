using System.Runtime.InteropServices;
using System.Windows.Threading;
using Stagewatch.Core;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Show safety: crash recovery, keep-awake, and "is a show live?" for the close guard.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly SessionStore _session = new();
    private SessionState? _pendingRecovery;
    private DispatcherTimer? _sessionTimer;
    private bool _keepingAwake;

    /// <summary>A show is on: the clock is running or mid-item, or an output is up.</summary>
    public bool IsShowLive =>
        _outputWindow is not null || _ndi is not null ||
        (_engine.Mode != TimerMode.TimeOfDay && (_engine.IsCounting || _engine.State is TimerState.Paused or TimerState.Expired));

    /// <summary>One line for dialogs: "Keynote — 12:34 running".</summary>
    public string LiveSummary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(CurrentTitle)) parts.Add(CurrentTitle);
            if (_engine.Mode != TimerMode.TimeOfDay) parts.Add($"{TimerText} {TimerState.ToString().ToLowerInvariant()}");
            if (_outputWindow is not null) parts.Add("output on screen");
            if (_ndi is not null) parts.Add("NDI sending");
            return string.Join(" · ", parts);
        }
    }

    // ------------------------------------------------------------------ crash recovery

    /// <summary>Called from the constructor: read (but don't touch) the previous session.</summary>
    private void LoadPendingRecovery() => _pendingRecovery = _session.LoadRecoverable(DateTime.UtcNow);

    /// <summary>Description of the show we could bring back, or null if there's nothing to offer.</summary>
    public string? RecoveryDescription
    {
        get
        {
            var s = _pendingRecovery;
            if (s?.Engine is not { } e) return null;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(s.Title)) parts.Add(s.Title);
            var shown = e.Mode switch
            {
                TimerMode.TimeOfDay => "time of day",
                TimerMode.CountUp => TimerTick.FormatSeconds((long)(e.ValueSeconds + (e.Counting ? (DateTime.UtcNow - e.CapturedAtUtc).TotalSeconds : 0))) + " elapsed",
                _ => TimerTick.FormatSeconds((long)Math.Ceiling(e.ValueSeconds - (e.Counting ? (DateTime.UtcNow - e.CapturedAtUtc).TotalSeconds : 0) - 0.0005))
            };
            parts.Add(e.Counting ? $"{shown} (still running)" : $"{shown} ({e.State.ToString().ToLowerInvariant()})");
            if (s.MessageText.Length > 0) parts.Add($"message \"{s.MessageText}\"");
            if (s.OutputMode != OutputMode.Closed) parts.Add("output window");
            if (s.Blackout) parts.Add("BLACKOUT on");
            return string.Join(" · ", parts) + $"\n\nLast saved {Fmt(s.SavedAtUtc.ToLocalTime())}.";
        }
    }

    /// <summary>Operator answered the recovery question. Then (and only then) start saving the new session.</summary>
    public void ResolveRecovery(bool restore)
    {
        var s = _pendingRecovery;
        _pendingRecovery = null;
        if (restore && s?.Engine is { } e)
        {
            _engine.Restore(e);
            // The cursor is restored only into the same running order (by id); the controller checks.
            _show.Restore(new ShowCursor(s.Title, s.CurrentItem, s.RundownActive, s.CurrentItemStarted, s.ShowAnchorUtc, s.PinnedStartUtc,
                s.Actuals ?? new Dictionary<int, ItemActual>(), s.PendingDelaySeconds, s.RundownId, s.Execution, s.CurrentItemId, s.ActualIds));
            _manualTimerFlash = s.ManualTimerFlash;
            Blackout = s.Blackout;
            if (s.JournalPath is { Length: > 0 } jp && System.IO.File.Exists(jp)) OpenJournal(jp);
            if (s.MessageText.Length > 0)
            {
                MessageInput = s.MessageText;
                SetMessage(s.MessageText, s.Urgency, s.Flash);
            }
            if (s.OutputMode != OutputMode.Closed)
            {
                var mode = s.OutputMode is OutputMode.KeyedFullScreen or OutputMode.FloatingMinimal
                           && !FeatureGate.AllowsChromaKey(_license.Tier) ? OutputMode.FullScreen : s.OutputMode;
                OpenOutputInternal(mode);
                _suppressOutputModeNotify = true;
                SelectedOutputMode = mode;
                _suppressOutputModeNotify = false;
                if (s.NdiOn && FeatureGate.AllowsNdi(_license.Tier)) StartNdi();
            }
            RebuildRundownRows();
            StatusLine = "Show restored after an unexpected close — check the timer before carrying on.";
            MarkDirty();
            Publish();
        }
        StartSessionSaving();
    }

    private void StartSessionSaving()
    {
        if (_sessionTimer is not null) return;
        _sessionTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _sessionTimer.Tick += (_, _) => SaveSession(cleanClose: false);
        _sessionTimer.Start();
        SaveSession(cleanClose: false);
    }

    private SessionState BuildSession(bool cleanClose)
    {
        var cursor = _show.Capture();
        var msg = Settings.MessageState;
        return new SessionState
        {
            Sequence = ++_sessionSequence,   // captured on the show thread, in order
            SavedAtUtc = DateTime.UtcNow,
            ClosedCleanly = cleanClose,
            Engine = _engine.Capture(),
            Title = cursor.Title,
            CurrentItem = cursor.CurrentItem,
            RundownActive = cursor.RundownActive,
            CurrentItemStarted = cursor.ItemStarted,
            ShowAnchorUtc = cursor.ShowAnchorUtc,
            PinnedStartUtc = cursor.PinnedStartUtc,
            RundownId = cursor.RundownId,
            Execution = cursor.Execution,
            CurrentItemId = cursor.CurrentItemId,
            ActualIds = cursor.ActualIds is { } ids ? new Dictionary<int, string>(ids) : null,
            MessageText = msg.Text,
            Urgency = msg.Urgency,
            Flash = msg.Flash,
            ManualTimerFlash = _manualTimerFlash,
            Blackout = Blackout,
            OutputMode = _outputWindow is null ? OutputMode.Closed : Settings.Display.OutputMode,
            NdiOn = _ndi is not null,
            Actuals = new Dictionary<int, ItemActual>(cursor.Actuals),
            PendingDelaySeconds = cursor.PendingDelaySeconds,
            JournalPath = _journal.Path
        };
    }

    private long _sessionSequence;

    private void SaveSession(bool cleanClose)
    {
        var state = BuildSession(cleanClose);
        if (cleanClose) _session.Save(state); // on exit: synchronously
        else ThreadPool.QueueUserWorkItem(_ => _session.Save(state));
    }

    // ------------------------------------------------------------------ keep awake

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    private const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;

    /// <summary>While a show is live, Windows may not sleep or blank the screens. Called on the UI thread.</summary>
    private void UpdateKeepAwake()
    {
        var want = IsShowLive;
        if (want == _keepingAwake) return;
        _keepingAwake = want;
        try { SetThreadExecutionState(EsContinuous | (want ? EsSystemRequired | EsDisplayRequired : 0)); }
        catch (EntryPointNotFoundException) { }
    }
}
