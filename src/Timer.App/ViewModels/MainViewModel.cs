using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Companion;
using Stagewatch.Core;
using Stagewatch.Licensing;
using Stagewatch.Output;
using Stagewatch.Remote;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// The application-level VM and the show's single authority.
///
/// Loop: the engine ticks (20 Hz) → OnTick on the UI thread → if anything visible changed,
/// build one immutable <see cref="ShowSnapshot"/> → publish it. The operator UI, the output
/// window, browser viewers and OSC feedback all render that same frame. Every input —
/// buttons, keyboard, Companion, HTTP, WebSocket, OSC — ends up as a call on this thread,
/// so there is exactly one writer and nothing to race.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, ICompanionBridge, IShowControl, IDisposable
{
    private readonly SettingsStore _store;
    private readonly LicenseStatus _license;
    private readonly TimerEngine _engine = new();
    private readonly OutputViewModel _outputVm = new();
    private readonly SnapshotHub _hub = new();
    private readonly Dispatcher _ui;
    private readonly CompanionServer _companion;
    private readonly AlertSounds _sounds = new();
    private OutputWindow? _outputWindow;
    private NdiSender? _ndi;
    private WebServer? _web;
    private OscServer? _osc;
    private string _remoteSignature = "";
    private bool _suppressOutputModeNotify;

    // Frame state (UI thread only).
    private TimerTick _lastTick;
    private bool _dirty = true;
    private bool _manualTimerFlash;
    private int _configVersion;
    private RemoteConfig _remoteConfig = new(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(), Array.Empty<(string, string?, int)>());

    // The show's core: the running-order cursor and every transition around it (UI-independent, owner thread).
    private readonly ShowController _show;
    public ShowController Show => _show;

    public TimerEngine Engine => _engine;
    public OutputViewModel OutputVm => _outputVm;
    public AppSettings Settings => _store.Settings;
    public LicenseStatus License => _license;
    public SnapshotHub Snapshots => _hub;
    public bool CompanionAllowed => FeatureGate.AllowsCompanion(_license.Tier);

    [ObservableProperty] private string _timerText = "00:00";
    [ObservableProperty] private string _timeOfDayText = "";
    [ObservableProperty] private string _endTimeText = "";
    [ObservableProperty] private bool _showEndTime;
    [ObservableProperty] private TimerState _timerState = TimerState.Idle;
    [ObservableProperty] private TimerMode _mode = TimerMode.Countdown;
    [ObservableProperty] private string _statusLine = "Ready";
    [ObservableProperty] private string _licenseLine = "";
    [ObservableProperty] private string _messageInput = "";
    [ObservableProperty] private MessageUrgency _selectedUrgency;
    [ObservableProperty] private FlashRate _selectedFlash;
    [ObservableProperty] private string _customDurationInput = "00:00:00";

    /// <summary>Wall-clock time at which the current countdown should land at zero (was "finish by").</summary>
    [ObservableProperty] private string _endAtInput = "";
    /// <summary>End-at pace: false = elastic (stretch the loaded duration), true = real time left at 1×.</summary>
    [ObservableProperty] private bool _endAtRealTime;
    [ObservableProperty] private string _stretchInfo = "";
    [ObservableProperty] private bool _stretchActive;

    [ObservableProperty] private OutputMode _selectedOutputMode = OutputMode.Closed;
    [ObservableProperty] private KeyColor _selectedKeyColor = KeyColor.Blue;
    [ObservableProperty] private System.Windows.Media.Brush _timerColorBrush = System.Windows.Media.Brushes.White;

    [ObservableProperty] private string _currentTitle = "";
    [ObservableProperty] private string _nextTitleText = "";
    [ObservableProperty] private string _scheduleText = "";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private bool _blackout;
    [ObservableProperty] private string _remoteLine = "";
    [ObservableProperty] private string _scheduledStartInput = "";
    [ObservableProperty] private bool _hasRundown;

    public ObservableCollection<TimerPreset> Presets { get; }
    public ObservableCollection<MessageBankEntry> MessageBank { get; }
    public ObservableCollection<RundownRow> RundownRows { get; } = new();

    public IReadOnlyList<OutputMode> OutputModeChoices { get; } =
        new[] { OutputMode.Closed, OutputMode.FullScreen, OutputMode.KeyedFullScreen, OutputMode.FloatingMinimal };

    public IReadOnlyList<KeyColor> KeyColorChoices { get; } =
        new[] { KeyColor.Blue, KeyColor.Green, KeyColor.Magenta, KeyColor.Black };

    public event Action<MessageState>? MessageChanged;
    public event Action<string>? CommandRecorded;

    // True while a remote command runs, so only the operator's own actions are offered to Companion's recorder.
    private bool _executingRemote;

    /// <summary>Offer an operator action to Companion's action recorder (no-op for remote commands).</summary>
    private void RecordUi(string line)
    {
        if (_executingRemote) return;
        try { CommandRecorded?.Invoke(line); } catch (Exception) { }
    }

    private static string Q(string s) => "\"" + s.Replace("\"", "'") + "\"";

    public MainViewModel(SettingsStore store, LicenseStatus license)
    {
        _store = store;
        _license = license;
        _peerContexts = new PeerContexts(_nodes);
        _ui = Dispatcher.CurrentDispatcher;
        _nodes.Bound += OnNodeBound;   // raised on any thread; saved and journalled on the show thread
        _supervisor = new Stagewatch.Integrations.IntegrationSupervisor(work => _ui.BeginInvoke(work), () => { if (!_disposed) ApplyIntegrations(); }, m => StatusLine = m);

        Presets = new ObservableCollection<TimerPreset>(_store.Settings.Presets);
        MessageBank = new ObservableCollection<MessageBankEntry>(_store.Settings.MessageBank.Entries);

        // Clean startup: open at 00:00, idle, no message, output closed.
        _selectedUrgency = MessageUrgency.Info;
        _selectedFlash = FlashRate.Off;
        _messageInput = "";
        _customDurationInput = "00:00:00";
        _endAtInput = "";
        _endAtRealTime = _store.Settings.EndAtRealTime;
        _activeLadderId = _store.Settings.Display.CueLadderId;
        _speakerFormat = _store.Settings.Display.SpeakerFormat;
        _ackRequested = _store.Settings.Signals.AckByDefault;
        _selectedKeyColor = _store.Settings.Display.KeyColor;
        _selectedOutputMode = OutputMode.Closed;
        _scheduledStartInput = _store.Settings.Rundown.ScheduledStart ?? "";
        _endOfShowInput = _store.Settings.Rundown.EndOfShow ?? "";
        _holdingFill = _store.Settings.Holding.Fill;
        InitHolding();

        _store.Update(s =>
        {
            s.MessageState.Text = "";
            s.MessageState.Urgency = MessageUrgency.Info;
            s.MessageState.Flash = FlashRate.Off;
            s.LastMode = TimerMode.Countdown;
            s.LastDurationSeconds = 0;
            s.Display.OutputMode = OutputMode.Closed;
        });

        _hub.SubscriberFailed += ex => _ui.BeginInvoke(() => StatusLine = "Output error: " + ex.Message);
        // A consumer that takes too long is reported in diagnostics; the show never waited for it.
        _hub.SubscriberSlow += (name, took) => Interlocked.Exchange(ref _slowSubscriberNote, $"{name} took {took.TotalMilliseconds:0} ms on one frame at {DateTime.Now:HH:mm:ss}");
        _show = new ShowController(_store, _engine, this);

        _engine.AllowOvertime = _store.Settings.AllowOvertime;
        // Latest-wins: the engine ticks every 50 ms on a timer thread; the UI thread only ever needs the newest
        // tick, and never more than one queued callback — a busy moment can't build a backlog of stale ticks.
        _engine.Tick += t =>
        {
            lock (_tickLock) { _pendingTick = t; _hasPendingTick = true; }
            if (Interlocked.Exchange(ref _tickQueued, 1) == 0) _ui.BeginInvoke(DispatcherPriority.Render, DrainTick);
        };
        _engine.ModeChanged += m => _ui.BeginInvoke(() => { Mode = m; MarkDirty(); });
        _engine.SetMode(TimerMode.Countdown);
        _engine.SetDuration(TimeSpan.Zero);

        _companion = new CompanionServer(this);

        InitSystems();
        InitLtc();
        RefreshScreenCopies();
        _ = Windowing;   // the saved windows, ready before any server asks for them
        InitAux();
        InitSlides();

        // 4 Hz housekeeping: wall clock, stretch info, signals, sensors. The frame is rebuilt and
        // republished only if something visible changed (snapshot equality).
        var todTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        todTimer.Tick += (_, _) =>
        {
            _hub.MarkAlive();      // first: proves the show thread is turning over, even when nothing changes
            TimeOfDayText = Fmt(DateTime.Now);
            UpdateStretchInfo();
            UpdateSystems();
        };
        todTimer.Start();

        RebuildRundownRows();
        RebuildRemoteConfig();
        UpdateLicenseLine();
        UpdateOutputBackground();
        LoadPendingRecovery();
        UpdateWindowTitle();
        if (_license.IsLocked)
            StatusLine = "Trial ended — Free mode: basic timer with watermark. Help → License to unlock everything.";
        else if (_license.Notice is { } notice)
            StatusLine = notice;
    }

    private string? _slowSubscriberNote;
    /// <summary>The last time a frame consumer was slow (null = never), for diagnostics.</summary>
    public string? SlowSubscriberNote => Volatile.Read(ref _slowSubscriberNote);

    private bool _servicesStarted;

    /// <summary>
    /// Start what talks to the outside world: the Companion server, browser remotes and OSC, integrations, the LTC
    /// poll. Kept out of the constructor so building a view model (in a test, a harness, a tool) opens no sockets and
    /// starts no devices. The app calls this once its window exists. Idempotent.
    /// </summary>
    public void StartServices()
    {
        if (_servicesStarted) return;
        _servicesStarted = true;
        // Started even in Free mode, so a connecting Companion is told *why* it can't control the show.
        if (_store.Settings.Companion.Enabled)
        {
            try { _companion.Start(_store.Settings.Companion.Port, _store.Settings.Companion.BindAllInterfaces); }
            catch (Exception ex) { StatusLine = $"Companion server failed: {ex.Message}"; }
        }
        ApplyRemoteSettings();
        ApplyIntegrations();
        _ltcTimer?.Start();
    }

    private bool Gate(bool allowed, string feature, FeatureTier needs = FeatureTier.Standard)
    {
        if (allowed) return true;
        StatusLine = _license.IsLocked && needs == FeatureTier.Standard
            ? $"{feature} needs a Stagewatch license (trial ended). Help → License."
            : $"{feature} {FeatureGate.Needs(needs)}. Help → License.";
        ReceiptRefused(StatusLine);
        return false;
    }

    /// <summary>The acknowledgement loop is Production; on lower tiers a message simply goes out.</summary>
    private bool CrewCuesAllowed => FeatureGate.AllowsCrewCues(_license.Tier);

    // ================================================================== frame loop

    private readonly object _tickLock = new();
    private TimerTick _pendingTick;
    private bool _hasPendingTick;
    private int _tickQueued;

    private void DrainTick()
    {
        Interlocked.Exchange(ref _tickQueued, 0);
        TimerTick t;
        lock (_tickLock)
        {
            if (!_hasPendingTick) return;
            t = _pendingTick;
            _hasPendingTick = false;
        }
        OnTick(t);
    }

    private void OnTick(TimerTick t)
    {
        var prev = _lastTick;
        _lastTick = t;
        if (t.State != prev.State) UpdateKeepAwake();
        TimerState = t.State;
        Mode = t.Mode;

        // Smooth progress every tick — a transform scale, no layout, so it's cheap.
        var progress = _previewStartUtc is null ? t.Progress : FrameTick(DateTime.UtcNow).Progress;
        ShowProgress = progress >= 0; // operator always sees it; the output follows its own element setting
        ProgressValue = progress < 0 ? 0 : progress;
        _outputVm.Progress = progress;

        CheckCueSounds(t);

        if (t.DisplaySeconds != prev.DisplaySeconds || t.State != prev.State || t.Mode != prev.Mode || t.Total != prev.Total)
            _dirty = true;
        Publish();

        _show.OnTick(t);   // a countdown that just reached zero moves the show on (loops, linked items)
    }

    private void MarkDirty() => _dirty = true;

    /// <summary>Build and publish a frame if something changed. The only place outputs get updated.</summary>
    private void Publish()
    {
        if (!_dirty) return;
        _dirty = false;

        var s = BuildSnapshot();
        if (!_hub.Publish(s)) return;
        GoPublished();
        OnFramePublished(s);

        // Operator UI and the WPF output render the same frame the network gets.
        TimerText = s.Text;
        EndTimeText = s.EndTimeText;
        ShowEndTime = s.EndTimeText.Length > 0;
        CurrentTitle = s.Title;
        NextTitleText = s.NextTitle.Length > 0 ? "Next: " + s.NextTitle : "";
        ScheduleText = s.ScheduleText;
        Blackout = s.Blackout;
        var brush = OutputViewModel.ParseBrush(s.ColorHex);
        if (!BrushEquals(brush, TimerColorBrush)) TimerColorBrush = brush;
        _outputVm.Apply(s);
        PublishHolo(s);
    }

    private void UpdateStretchInfo()
    {
        if (_engine.EndAt is null)
        {
            StretchActive = false;
            StretchInfo = "";
            return;
        }
        StretchActive = true;

        if (_engine.State != TimerState.Running)
        {
            StretchInfo = _engine.EndAtRealTime
                ? $"real time to {Fmt(_engine.EndAt.Value)} once started"
                : $"will end at {Fmt(_engine.EndAt.Value)} once started";
            return;
        }

        if (_engine.EndAtRealTime)
        {
            StretchInfo = $"real time to {Fmt(_engine.EndAt.Value)}";
            return;
        }

        // No duration → not stretching, just running to wall-clock target.
        if (_engine.BaseDuration <= TimeSpan.Zero)
        {
            StretchInfo = $"running to {Fmt(_engine.EndAt.Value)}";
            return;
        }

        var ratio = _engine.StretchRatio;
        if (double.IsInfinity(ratio) || double.IsNaN(ratio))
            StretchInfo = "stretch: target reached";
        else if (ratio > 1.02)
            StretchInfo = $"stretch: {ratio:0.00}× faster than wall clock";
        else if (ratio < 0.98)
            StretchInfo = $"stretch: {ratio:0.00}× slower than wall clock";
        else
            StretchInfo = "stretch: 1× normal";
    }

    private static bool BrushEquals(System.Windows.Media.Brush a, System.Windows.Media.Brush b)
        => a is System.Windows.Media.SolidColorBrush sa
        && b is System.Windows.Media.SolidColorBrush sb
        && sa.Color == sb.Color;

    // ================================================================== transport

    [RelayCommand] private void Start() { RecordUi("start"); StartTimer(); }
    [RelayCommand] private void Stop() { RecordUi("pause"); _engine.Pause(); }
    [RelayCommand] private void Toggle() { RecordUi("toggle"); if (_engine.IsCounting) _engine.Pause(); else StartTimer(); }

    private void StartTimer() => _show.Start();

    [RelayCommand]
    private void Reset()
    {
        RecordUi("reset");
        _engine.Reset();
        EndAtInput = "";
        MarkDirty();
    }

    [RelayCommand]
    private void ToggleBlackout()
    {
        RecordUi("blackout toggle");
        Blackout = !Blackout;
        StatusLine = Blackout ? "Blackout ON — outputs hidden" : "Blackout off";
        MarkDirty();
        Publish();
    }

    [RelayCommand]
    private void LoadPreset(TimerPreset p)
    {
        var maxSlot = FeatureGate.MaxPresets(_license.Tier);
        if (p.Slot > maxSlot)
        {
            StatusLine = $"Preset {p.Slot} requires Standard or Pro";
            return;
        }
        RecordUi($"preset {p.Slot}");
        // Negative duration = count up (documented on TimerPreset).
        _engine.SetMode(p.DurationSeconds < 0 ? TimerMode.CountUp : TimerMode.Countdown);
        _engine.LoadPreset(p);
        EndAtInput = "";
        _show.LeaveRundown(p.Label);
        if (!string.IsNullOrEmpty(p.Message))
        {
            MessageInput = p.Message;
            ApplyMessage();
        }
        _store.Update(s => { s.LastDurationSeconds = p.DurationSeconds; s.LastMode = TimerMode.Countdown; });
        StatusLine = $"Preset {p.Slot}: {p.Label}";
        RefreshRundownHighlight();
        MarkDirty();
    }

    [RelayCommand]
    private void StartPreset(TimerPreset p)
    {
        LoadPreset(p);
        StartTimer();
    }

    [RelayCommand]
    private void Nudge(string amount)
    {
        if (!TryParseNudge(amount, out var ts)) return;
        RecordUi($"nudge {(long)ts.TotalSeconds}");
        _show.Nudge(ts);
        StatusLine = (ts >= TimeSpan.Zero ? "+" : "") + amount;
    }

    [RelayCommand]
    private void ApplyDuration()
    {
        if (TryParseDuration(CustomDurationInput, out var ts))
        {
            RecordUi($"duration {(long)ts.TotalSeconds}");
            _engine.SetMode(TimerMode.Countdown);
            _engine.SetDuration(ts);
            _show.LeaveRundown("");
            RefreshRundownHighlight();
            _store.Update(s => { s.LastDurationSeconds = (int)ts.TotalSeconds; });
            StatusLine = $"Duration: {ts}";
            MarkDirty();
        }
        else StatusLine = "Bad duration. Try MM:SS or HH:MM:SS";
    }

    /// <summary>
    /// Apply a wall-clock end-at target. The countdown ends at this time.
    /// Elastic: a loaded duration stretches to fit; otherwise it just runs to the target.
    /// Real time: the countdown becomes the actual time left, at normal pace.
    /// </summary>
    [RelayCommand]
    private void ApplyEndAt()
    {
        if (string.IsNullOrWhiteSpace(EndAtInput)) { ClearEndAt(); return; }
        if (!Gate(FeatureGate.AllowsEndAt(_license.Tier), "End-at timing")) { EndAtInput = ""; return; }
        if (Schedule.TryParseClock(EndAtInput, out var ts))
        {
            // Next occurrence (so 00:30 typed at 23:30 means in an hour) — but a time that passed
            // within the last 12 h is a slip, not "this time tomorrow": refuse it.
            if (Schedule.ResolveFinishAt(ts, DateTime.Now) is not { } target)
            {
                Refuse($"{Fmt(DateTime.Today + ts, seconds: false)} has already passed — enter a later time.");
                return;
            }
            RecordUi($"endat {Q(EndAtInput.Trim())} {(EndAtRealTime ? "realtime" : "elastic")}");
            _engine.SetMode(TimerMode.Countdown);
            _engine.SetEndAt(target, EndAtRealTime);
            var pace = EndAtRealTime ? "real time" : "elastic";
            StatusLine = target.Date == DateTime.Today
                ? $"Will end at {Fmt(target)} ({pace})"
                : $"Will end at {Fmt(target)} ({target:ddd}, after midnight, {pace})";
            MarkDirty();
        }
        else StatusLine = "Bad time. Try 14:30, 14:30:15 or 2:30 pm";
    }

    partial void OnEndAtRealTimeChanged(bool value)
    {
        _store.Update(s => s.EndAtRealTime = value);
        // Switch the pace of an end-at that's already set, keeping its target.
        if (_engine.EndAt is { } target)
        {
            _engine.SetEndAt(target, value);
            StatusLine = value ? $"Real time to {Fmt(target)}" : $"Elastic to {Fmt(target)}";
            MarkDirty();
        }
    }

    [RelayCommand]
    private void ClearEndAt()
    {
        EndAtInput = "";
        _engine.SetEndAt(null);
        StatusLine = "End-at cleared";
        MarkDirty();
    }

    [RelayCommand] private void SetModeCountdown() => SetMode(TimerMode.Countdown);
    [RelayCommand] private void SetModeCountUp()   => SetMode(TimerMode.CountUp);
    [RelayCommand] private void SetModeTimeOfDay() => SetMode(TimerMode.TimeOfDay);

    private void SetMode(TimerMode mode)
    {
        if (_engine.Mode == mode) return;
        RecordUi("mode " + SnapshotJson.ModeName(mode));
        _engine.SetMode(mode);
        _show.LeaveRundown(mode == TimerMode.CountUp ? "Count up" : "");
        RefreshRundownHighlight();
        MarkDirty();
    }

    // ================================================================== running order

    [RelayCommand]
    private void NextItem()
    {
        if (Settings.Rundown.Items.Count > 0 && _show.CurrentItem + 1 < Settings.Rundown.Items.Count) RecordUi("next");
        _show.Next();
    }

    [RelayCommand]
    private void PreviousItem()
    {
        if (Settings.Rundown.Items.Count == 0) return;
        RecordUi("previous");
        _show.Previous();
    }

    [RelayCommand]
    private void LoadItem(RundownRow row) { RecordUi($"goto {row.Index}"); GoToItem(row.Index - 1, start: false); }

    public void StartItem(RundownRow row) { RecordUi($"goto {row.Index} start"); GoToItem(row.Index - 1, start: true); }

    private void GoToItem(int index, bool start) => _show.GoTo(index, start);

    [RelayCommand]
    private void ApplyScheduledStart() => _show.SetScheduledStart(ScheduledStartInput);

    /// <summary>Planned start of item 1, in UTC.</summary>
    private DateTime? ScheduleAnchor() => _show.ScheduleAnchor();

    public bool BackTimed => _show.BackTimed;

    /// <summary>Call after the running order is edited.</summary>
    public void OnRundownEdited()
    {
        _show.RundownEdited();
        ScheduledStartInput = Settings.Rundown.ScheduledStart ?? "";
        EndOfShowInput = Settings.Rundown.EndOfShow ?? "";
        RebuildRundownRows();
        BumpConfig();
    }

    [ObservableProperty] private string _endOfShowInput = "";

    [RelayCommand]
    private void ApplyEndOfShow() => SetEndOfShow(EndOfShowInput.Trim());

    private void SetEndOfShow(string text)
    {
        if (_show.SetEndOfShow(text)) EndOfShowInput = text.Trim();
    }

    [RelayCommand]
    private void ResetShow() => _show.ResetShow();

    private static System.Windows.Media.Brush Brush(string hex)
    {
        try { var b = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
        catch (FormatException) { return System.Windows.Media.Brushes.Gray; }
    }

    private void RebuildRundownRows()
    {
        RefreshNotesCopy();
        RefreshRundownNames();
        var items = Settings.Rundown.Items;
        var anchor = ScheduleAnchor();
        var clock = ShowClock;
        var plan = anchor is { } a ? ScheduleEngine.Plan(items, a, clock) : null;
        RundownRows.Clear();
        var groupStarts = RundownGroups.Spans(items).ToDictionary(g => g.First);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            string header = "", detail = "";
            var over = false;
            if (groupStarts.TryGetValue(i, out var g))
            {
                var planned = RundownGroups.PlannedSeconds(items, g.First, g.Last);
                var target = RundownGroups.Target(Settings.Rundown, g.Name);
                header = g.Name.ToUpperInvariant();
                detail = TimerTick.FormatSeconds(planned) + (target is { } t ? $" / {TimerTick.FormatSeconds(t)}" : "");
                over = target is { } t2 && planned - t2 >= 30;
            }
            var labelInUse = Stagewatch.Core.Labels.InUse(items);
            RundownRows.Add(new RundownRow
            {
                LabelChips = it.Labels.Select(l => new LabelChip(l, Brush(Stagewatch.Core.Labels.ColorFor(Settings.Rundown, l, labelInUse.FindIndex(x => string.Equals(x, l, StringComparison.OrdinalIgnoreCase)))))).ToList(),
                GroupHeader = header,
                GroupDetail = detail,
                GroupOver = over,
                Index = i + 1,
                Title = it.Title,
                Speaker = it.Speaker ?? "",
                DurationText = TimerTick.FormatSeconds(it.EffectiveSeconds),
                PlannedStartText = plan is null ? "" : Fmt(clock.ToShow(plan[i].Start), seconds: false) + (plan[i].Conflict ? " ⚠" : ""),
                AutoAdvance = it.AutoAdvance,
                Kind = it.Kind,
                Fixed = it.StartMode == StartMode.Fixed,
                HardOut = it.HardOut ?? "",
                AdjustSeconds = it.AdjustSeconds
            });
        }
        HasRundown = items.Count > 0;
        RefreshRundownHighlight();
    }

    private void RefreshRundownHighlight()
    {
        foreach (var r in RundownRows)
        {
            var i = r.Index - 1;
            r.IsCurrent = _show.RundownActive && i == _show.CurrentItem;
            r.IsDone = _show.CurrentItem >= 0 && i < _show.CurrentItem;
        }
    }

    // ================================================================== messages

    [RelayCommand]
    private void ApplyMessage()
    {
        if (!string.IsNullOrWhiteSpace(MessageInput))
            RecordUi($"message {Q(MessageInput)} {UrgencyWord(SelectedUrgency)} {SelectedFlash.ToString().ToLowerInvariant()}{(AckRequested ? " ack" : "")}");
        if (AckRequested && !string.IsNullOrWhiteSpace(MessageInput) && CrewCuesAllowed)
        {
            var s = _signals.Send(SignalKind.Message, "speaker", MessageInput, DateTime.UtcNow);
            _journal.Add(DateTime.UtcNow, JournalKind.Signal, _show.RundownActive ? _show.CurrentItem : -1, $"Message speaker: {MessageInput}");
            SetMessage(MessageInput, SelectedUrgency, SelectedFlash, s.Id);
            StatusLine = "Message sent — waiting for the speaker to acknowledge";
            return;
        }
        SetMessage(MessageInput, SelectedUrgency, SelectedFlash);
        StatusLine = "Message sent";
    }

    [RelayCommand]
    private void ClearMessage()
    {
        RecordUi("clearmessage");
        MessageInput = "";
        SetMessage("", MessageUrgency.Info, FlashRate.Off);
    }

    [RelayCommand]
    private void SendBankMessage(MessageBankEntry entry)
    {
        if (!Gate(FeatureGate.AllowsQuickMessages(_license.Tier), "Quick messages")) return;
        RecordUi($"message {Q(entry.Text)} {UrgencyWord(entry.Urgency)} {entry.Flash.ToString().ToLowerInvariant()}");
        MessageInput = entry.Text;
        SelectedUrgency = entry.Urgency;
        SelectedFlash = entry.Flash;
        SetMessage(entry.Text, entry.Urgency, entry.Flash);
    }

    /// <summary>Save the typed message (with its urgency and flash) as a quick message.</summary>
    public void AddQuickMessage()
    {
        var text = MessageInput.Trim();
        if (text.Length == 0) { StatusLine = "Type a message first, then save it as a quick message"; return; }
        if (!Gate(FeatureGate.AllowsQuickMessages(_license.Tier), "Quick messages")) return;
        var label = text.Length <= 18 ? text : text[..17].TrimEnd() + "…";
        var entry = new MessageBankEntry { Label = label, Text = text, Urgency = SelectedUrgency, Flash = SelectedFlash };
        _store.Update(s => s.MessageBank.Entries.Add(entry));
        MessageBank.Add(entry);
        BumpConfig();
        StatusLine = $"Quick message saved: {label}";
    }

    public void RemoveQuickMessage(MessageBankEntry entry)
    {
        _store.Update(s => s.MessageBank.Entries.Remove(entry));
        MessageBank.Remove(entry);
        BumpConfig();
        StatusLine = $"Quick message removed: {entry.Label}";
    }

    /// <summary>An operator action from a menu, through the one command grammar (and offered to Companion's recorder).</summary>
    public void RunOperatorLine(string line)
    {
        RecordUi(line);
        RunCommandLine(line);
    }

    public void SetMessage(string text, MessageUrgency urgency, FlashRate flash) => SetMessage(text, urgency, flash, 0);

    /// <param name="signalId">The acknowledgement signal behind this message (0 = none asked).</param>
    public void SetMessage(string text, MessageUrgency urgency, FlashRate flash, int signalId)
    {
        // Companion threads call this: hop to the owner thread (single writer).
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => SetMessage(text, urgency, flash, signalId)); return; }
        if (_messageSignalId > 0 && _messageSignalId != signalId) _signals.Cancel(_messageSignalId, DateTime.UtcNow);
        _messageSignalId = string.IsNullOrEmpty(text) ? 0 : signalId;
        if (_messageSignalId > 0) ReceiptRef("messageId", _messageSignalId);
        if (text.Length > 0 && signalId == 0) _journal.Add(DateTime.UtcNow, JournalKind.Message, _show.RundownActive ? _show.CurrentItem : -1, text);
        FireTrigger(TriggerEvent.Message, "speaker", text);
        _store.Update(s =>
        {
            s.MessageState.Text = text;
            s.MessageState.Urgency = urgency;
            s.MessageState.Flash = flash;
        });
        MessageChanged?.Invoke(_store.Settings.MessageState.Clone());
        MarkDirty();
        Publish();
    }

    public void SetFlash(FlashRate rate)
    {
        // Companion threads call this: hop to the owner thread (single writer).
        if (!_ui.CheckAccess()) { _ui.BeginInvoke(() => SetFlash(rate)); return; }
        SelectedFlash = rate;
        _store.Update(s => s.MessageState.Flash = rate);
        MessageChanged?.Invoke(_store.Settings.MessageState.Clone());
        MarkDirty();
        Publish();
    }

    public void SetTimerFlash(bool flashTimer)
    {
        // Called from Companion threads; hop to the owner thread.
        _ui.BeginInvoke(() =>
        {
            _manualTimerFlash = flashTimer;
            MarkDirty();
            Publish();
        });
    }

    // ================================================================== remote commands (IShowControl)

    /// <summary>Thread-safe entry for every remote transport. Never blocks the caller.</summary>
    public void Execute(ShowCommand command)
    {
        _ui.BeginInvoke(() =>
        {
            _executingRemote = true;
            try { ExecuteNow(command); }
            catch (Exception ex) { StatusLine = $"Remote command failed: {ex.Message}"; }
            finally { _executingRemote = false; }
        });
    }

    internal void ExecuteNow(ShowCommand c)
    {
        MarkGo(c);
        switch (c.Kind)
        {
            case ShowCommandKind.Start: StartTimer(); break;
            case ShowCommandKind.Pause: _engine.Pause(); break;
            case ShowCommandKind.Toggle: Toggle(); break;
            case ShowCommandKind.Reset: Reset(); break;
            case ShowCommandKind.Next: NextItem(); break;
            case ShowCommandKind.Previous: PreviousItem(); break;
            case ShowCommandKind.GoTo: GoToItem((int)c.Number - 1, c.Flag); break;
            case ShowCommandKind.Preset:
                var p = Presets.FirstOrDefault(x => x.Slot == (int)c.Number);
                if (p is null) { Refuse($"No preset {(int)c.Number}"); break; }
                if (c.Flag) StartPreset(p); else LoadPreset(p);
                break;
            case ShowCommandKind.Nudge:
                _show.Nudge(TimeSpan.FromSeconds(c.Number));
                break;
            case ShowCommandKind.SetDuration:
                _engine.SetMode(TimerMode.Countdown);
                _engine.SetDuration(TimeSpan.FromSeconds(c.Number));
                _show.LeaveRundown("");
                RefreshRundownHighlight();
                break;
            case ShowCommandKind.EndAt:
                if (c.Text is not null && c.Number is 1 or 2) EndAtRealTime = c.Number == 2;
                EndAtInput = c.Text ?? "";
                ApplyEndAt();
                break;
            case ShowCommandKind.Message:
                MessageInput = c.Text ?? "";
                if (c.Flag && !string.IsNullOrEmpty(c.Text) && CrewCuesAllowed)
                {
                    var sig = _signals.Send(SignalKind.Message, "speaker", c.Text, DateTime.UtcNow);
                    _journal.Add(DateTime.UtcNow, JournalKind.Signal, _show.RundownActive ? _show.CurrentItem : -1, $"Message speaker: {c.Text}");
                    SetMessage(c.Text, c.Urgency, c.Flash, sig.Id);
                }
                else SetMessage(c.Text ?? "", c.Urgency, c.Flash);
                break;
            case ShowCommandKind.ClearMessage: ClearMessage(); break;
            case ShowCommandKind.Blackout:
                var on = c.Number < 0 ? !Blackout : c.Number > 0;
                if (on != Blackout) ToggleBlackout();
                break;
            case ShowCommandKind.Output:
            {
                var mode = c.Number < 0
                    ? (_outputWindow is null ? OutputMode.FullScreen : OutputMode.Closed)
                    : (OutputMode)(int)c.Number;
                if (mode is OutputMode.KeyedFullScreen or OutputMode.FloatingMinimal
                    && !Gate(FeatureGate.AllowsChromaKey(_license.Tier), "Keyed and floating outputs")) break;
                SelectedOutputMode = mode;
                break;
            }
            case ShowCommandKind.Style:
                if (c.Number == 2) SetThreeDScene(egg: true);
                else if (c.Number == 1) SetThreeDScene(egg: false);
                else SetHolographic(c.Number < 0 ? !Settings.Display.Holographic : false);
                break;
            case ShowCommandKind.Screen: ApplyScreenCommand(c.Get<ScreenArgs>()); break;
            case ShowCommandKind.Window: ApplyWindowCommand(c.Get<WindowArgs>()); break;
            case ShowCommandKind.Aux: ApplyAuxCommand(c.Number, c.Get<AuxArgs>()); break;
            case ShowCommandKind.DeptNote:
            {
                var note = c.Get<DeptNoteArgs>();
                _show.SetDeptNote((int)c.Number, note.Department, note.Note);
                break;
            }
            case ShowCommandKind.Roll: _show.SetRoll(c.Number < 0 ? !RollMode : c.Number > 0); break;
            case ShowCommandKind.Rundown: SwitchRundown(c.Text ?? ""); break;
            case ShowCommandKind.Undo: if (c.Number < 0) _show.Redo(); else _show.Undo(); break;
            case ShowCommandKind.EndOfShow: SetEndOfShow(c.Text ?? ""); break;
            case ShowCommandKind.Patterns:
                PatternsVideoCommand(c.Text ?? "end", (int)c.Number);
                break;
            case ShowCommandKind.Loop: _show.SetLoop((int)c.Number); break;
            case ShowCommandKind.Ltc:
            {
                var ltc = c.Get<LtcArgs>();
                if (ltc.Action == "chase") SetLtcChase(c.Number < 0 ? !Settings.Integrations.LtcChase : c.Number > 0);
                else if (ltc.Action == "end") SetLtcEnd(ltc.Timecode);
                break;
            }
            case ShowCommandKind.Holding:
                SetHoldingByKey(c.Text ?? "toggle");
                break;
            case ShowCommandKind.Ndi:
            {
                var want = c.Number < 0 ? _ndi is null : c.Number > 0;
                if (want != (_ndi is not null)) ToggleNdi();
                break;
            }
            case ShowCommandKind.Mode: SetMode(c.Mode); break;
            default: ExecuteSystemCommand(c); break;
        }
        MarkDirty();
        Publish();
    }

    public RemoteConfig GetRemoteConfig() => Volatile.Read(ref _remoteConfig);

    /// <summary>Feature keys this licence unlocks (Companion dims keys for the rest).</summary>
    public static IReadOnlyList<string> FeatureKeys(FeatureTier t)
    {
        var f = new List<string>();
        void Add(bool on, string key) { if (on) f.Add(key); }
        Add(FeatureGate.AllowsEndAt(t), "endat");
        Add(FeatureGate.AllowsQuickMessages(t), "quickmessages");
        Add(FeatureGate.AllowsHolding(t), "holding");
        Add(FeatureGate.AllowsChromaKey(t), "keyed");
        Add(FeatureGate.AllowsNdi(t), "ndi");
        Add(FeatureGate.AllowsRunOfShow(t), "runningorder");
        Add(FeatureGate.AllowsRemote(t), "remote");
        Add(FeatureGate.AllowsPowerPointAddIn(t), "powerpoint");
        Add(FeatureGate.AllowsCsvReport(t), "csvreport");
        Add(FeatureGate.AllowsHolographic(t), "holo");
        Add(FeatureGate.AllowsCrewCues(t), "crewcues");
        Add(FeatureGate.AllowsForecast(t), "forecast");
        Add(FeatureGate.AllowsAssistant(t), "assistant");
        Add(FeatureGate.AllowsSpeechPace(t), "speech");
        Add(FeatureGate.AllowsPlayout(t), "vmix");
        Add(FeatureGate.AllowsTimecode(t), "timecode");
        Add(FeatureGate.AllowsCueLights(t), "cuelights");
        Add(FeatureGate.AllowsMultiRoom(t), "multiroom");
        Add(FeatureGate.AllowsFullReport(t), "fullreport");
        Add(FeatureGate.AllowsMcp(t), "mcp");
        return f;
    }

    private void RebuildRemoteConfig()
    {
        var s = Settings;
        var cfg = new RemoteConfig(
            s.Presets.Select(p => (p.Slot, p.Label, p.DurationSeconds)).ToArray(),
            s.MessageBank.Entries.Select(m => (m.Label, m.Text, m.Urgency, m.Flash)).ToArray(),
            s.Rundown.Items.Select(i => (i.Title, i.Speaker, i.EffectiveSeconds)).ToArray(),
            s.Display.AllLadders().Select(l => (l.Id, l.Name)).ToArray(),
            s.Signals.Departments.ToArray(),
            s.Integrations.RoomName,
            s.Display.CueLadderId,
            Pricing.TierName(_license.Tier),
            FeatureKeys(_license.Tier),
            s.Holding.Images.Select(i => (i.Id, i.Name)).ToArray());
        Volatile.Write(ref _remoteConfig, cfg);
    }

    /// <summary>Presets / quick messages / running order changed: tell remotes to refetch.</summary>
    public void BumpConfig()
    {
        RebuildRemoteConfig();
        _configVersion++;
        MarkDirty();
        Publish();
    }

    /// <summary>A wall-clock time as text, in the operator's 12/24-hour choice.</summary>
    private string Fmt(DateTime t, bool seconds = true) => Clock.Time(t, Settings.Display.Use12HourClock, seconds);

    /// <summary>Re-read settings that affect live behaviour. Call after the Settings dialog closes.</summary>
    public void ApplySettings()
    {
        RefreshScreenCopies();
        RebuildRundownRows();
        _engine.AllowOvertime = Settings.AllowOvertime;
        UpdateOutputBackground();
        ApplyRemoteSettings();
        ApplyIntegrations();
        _signals.EscalateAfter = TimeSpan.FromSeconds(Math.Clamp(Settings.Signals.EscalateSeconds, 5, 600));
        OnPropertyChanged(nameof(Ladders));
        OnPropertyChanged(nameof(Departments));
        RebuildRemoteConfig();
        _configVersion++;
        ApplyHolo();
        MarkDirty();
        Publish();
    }

    private void ApplyRemoteSettings()
    {
        var r = Settings.Remote;
        var allowed = FeatureGate.AllowsRemote(_license.Tier);
        var signature = $"{allowed}|{r.WebEnabled}|{r.WebPort}|{r.WebBindAll}|{r.OscEnabled}|{r.OscPort}|{r.OscBindAll}|{r.OscFeedbackHost}|{r.OscFeedbackPort}|{r.Https}";
        if (_web is not null) _web.RequireHttps = r.Https && r.RequireHttps && _web.Certificate is not null;
        if (signature == _remoteSignature) { UpdateRemoteLine(); return; }
        _remoteSignature = signature;

        StopRemote();
        if (!allowed) { RemoteLine = ""; return; }

        var problems = new List<string>();
        if (r.WebEnabled)
        {
            var web = new WebServer(this, () => _store.Snapshot.Remote.Pin);   // web threads read the snapshot, never the live settings
            web.CommandRejected += m => _ui.BeginInvoke(() => StatusLine = m);
            if (r.Https) web.Certificate = Certificate;
            web.RequireHttps = r.Https && r.RequireHttps && web.Certificate is not null;
            try { web.Start(r.WebPort, r.WebBindAll); _web = web; Windowing.Web = web; }
            catch (Exception ex) when (ex is SocketException or ArgumentOutOfRangeException) { problems.Add($"web port {r.WebPort} is unavailable"); }
        }
        if (r.OscEnabled)
        {
            var osc = new OscServer(this);
            osc.CommandRejected += m => _ui.BeginInvoke(() => StatusLine = m);
            try { osc.Start(r.OscPort, r.OscBindAll, r.OscFeedbackHost, r.OscFeedbackPort); _osc = osc; }
            catch (Exception ex) when (ex is SocketException or ArgumentOutOfRangeException) { problems.Add($"OSC port {r.OscPort} is unavailable"); osc.Dispose(); }
        }
        if (problems.Count > 0) StatusLine = "Remote: " + string.Join(", ", problems) + " — change it in Settings → Remote";
        UpdateRemoteLine();
    }

    private void UpdateRemoteLine()
    {
        if (_web is null) { RemoteLine = _osc is null ? "" : $"OSC :{_osc.Port}"; return; }
        var host = Settings.Remote.WebBindAll ? LanAddresses().FirstOrDefault() ?? "localhost" : "localhost";
        RemoteLine = $"Remote: {(_web.RequireHttps ? "https" : "http")}://{host}:{_web.Port}" + (_osc is null ? "" : $"  ·  OSC :{_osc.Port}");
    }

    private System.Security.Cryptography.X509Certificates.X509Certificate2? _certificate;
    private bool _certificateTried;

    /// <summary>This install's certificate, made on first use (null if it couldn't be — the web stays plain HTTP).</summary>
    public System.Security.Cryptography.X509Certificates.X509Certificate2? Certificate
    {
        get
        {
            if (!_certificateTried) { _certificateTried = true; _certificate = NodeCertificate.LoadOrCreate(Settings.Integrations.RoomName); }
            return _certificate;
        }
    }

    /// <summary>"sha256:…" of this install's certificate, or null.</summary>
    public string? CertificateFingerprint => Certificate is { } c ? TlsPins.Fingerprint(c.RawData) : null;

    private void StopRemote()
    {
        try { _web?.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { _osc?.Dispose(); } catch { }
        _web = null;
        if (_windowDirector is not null) _windowDirector.Web = null;
        _osc = null;
    }

    /// <summary>This PC's IPv4 addresses on active, non-virtual-loopback adapters — what a phone would type.</summary>
    public static IReadOnlyList<string> LanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException) { return Array.Empty<string>(); }
    }

    public int? WebPort => _web?.Port;

    // ================================================================== output window / NDI

    partial void OnSelectedOutputModeChanged(OutputMode value)
    {
        if (_suppressOutputModeNotify) return;
        if (value is OutputMode.KeyedFullScreen or OutputMode.FloatingMinimal
            && !Gate(FeatureGate.AllowsChromaKey(_license.Tier), "Keyed and floating outputs"))
        {
            // Bounce the combo back on the next dispatcher turn (can't re-set inside its own change).
            _ui.BeginInvoke(() =>
            {
                _suppressOutputModeNotify = true;
                SelectedOutputMode = _outputWindow is null ? OutputMode.Closed : OutputMode.FullScreen;
                _suppressOutputModeNotify = false;
            });
            return;
        }
        _store.Update(s => s.Display.OutputMode = value);
        if (value == OutputMode.Closed)
        {
            CloseOutputInternal();
            return;
        }
        if (_outputWindow is null) OpenOutputInternal(value);
        else { _outputWindow.ApplyMode(value, _store.Settings.Display.KeyColor); ConfigureHolo(); }
        UpdateOutputBackground();
        StatusLine = $"Output: {value}";
    }

    partial void OnSelectedKeyColorChanged(KeyColor value)
    {
        _store.Update(s => s.Display.KeyColor = value);
        UpdateOutputBackground();
        if (_outputWindow is not null)
            _outputWindow.ApplyMode(_store.Settings.Display.OutputMode, value);
        StatusLine = $"Key color: {value}";
    }

    [RelayCommand]
    private void OpenOutput()
    {
        if (_outputWindow is not null) { _outputWindow.Activate(); return; }
        var mode = _store.Settings.Display.OutputMode == OutputMode.Closed
            || !FeatureGate.AllowsChromaKey(_license.Tier)
            ? OutputMode.FullScreen
            : _store.Settings.Display.OutputMode;
        OpenOutputInternal(mode);
        _suppressOutputModeNotify = true;
        SelectedOutputMode = mode;
        _suppressOutputModeNotify = false;
    }

    private void OpenOutputInternal(OutputMode mode)
    {
        _outputWindow ??= new OutputWindow();
        _outputWindow.Closed += (_, _) =>
        {
            StopNdi();
            _outputWindow = null;
            _suppressOutputModeNotify = true;
            SelectedOutputMode = OutputMode.Closed;
            _suppressOutputModeNotify = false;
            UpdateKeepAwake();
        MarkDirty(); // remotes and Companion see the output / NDI state
        };
        _outputWindow.Bind(_outputVm);
        _outputWindow.Show();
        UpdateKeepAwake();
        MarkDirty(); // remotes and Companion see the output / NDI state
        _outputWindow.MoveToMonitor(_store.Settings.Display.TargetMonitorDeviceName);
        _outputWindow.ApplyMode(mode, _store.Settings.Display.KeyColor);
        _store.Update(s => s.Display.OutputMode = mode);
        UpdateOutputBackground();
        ApplyHolo();
    }

    [RelayCommand]
    private void CloseOutput() => CloseOutputInternal();

    private void CloseOutputInternal()
    {
        _outputWindow?.Close();
        _outputWindow = null;
        StopNdi();
        _store.Update(s => s.Display.OutputMode = OutputMode.Closed);
        UpdateKeepAwake();
        MarkDirty(); // remotes and Companion see the output / NDI state
    }

    [RelayCommand]
    private void ToggleNdi()
    {
        if (!Gate(FeatureGate.AllowsNdi(_license.Tier), "NDI output")) return;
        if (_ndi is null) StartNdi();
        else StopNdi();
    }

    private void StartNdi()
    {
        if (_outputWindow is null) { Refuse("Open output window first"); return; }
        _ndi = new NdiSender(_outputWindow.Content as FrameworkElement ?? _outputWindow,
            _store.Settings.Display.NdiSourceName,
            fps: _store.Settings.Display.NdiFrameRate);
        if (!_ndi.IsAvailable)
        {
            _ndi = null;
            Refuse("NDI needs the free NDI Runtime — install NDI Tools from ndi.video/tools, then try again.");
            return;
        }
        try { _ndi.Start(); }
        catch (InvalidOperationException ex)
        {
            _ndi = null;
            StatusLine = "NDI failed to start: " + ex.Message;
            return;
        }
        _store.Update(s => s.Display.NdiEnabled = true);
        UpdateKeepAwake();
        MarkDirty(); // remotes and Companion see the output / NDI state
        StatusLine = $"NDI sending as \"{Environment.MachineName} ({_ndi.SourceName})\" — 1080p{_store.Settings.Display.NdiFrameRate}";
    }

    private void StopNdi()
    {
        _ndi?.Stop();
        _ndi = null;
        _store.Update(s => s.Display.NdiEnabled = false);
        UpdateKeepAwake();
        MarkDirty(); // remotes and Companion see the output / NDI state
    }

    private void UpdateOutputBackground()
    {
        var disp = _store.Settings.Display;
        _outputVm.Background = disp.OutputMode is OutputMode.KeyedFullScreen or OutputMode.FloatingMinimal
            ? ChromaKey.ToBrush(disp.KeyColor)
            : System.Windows.Media.Brushes.Black;
        _outputVm.Watermark = _license.ShowsWatermark;
        _outputVm.Elements = disp.Elements;
        MarkDirty();
    }

    private void UpdateLicenseLine()
    {
        LicenseLine = _license.DisplayName;
    }

    // ================================================================== presets (Companion bridge)

    public void LoadPreset(int slot, bool autoStart)
    {
        var p = Presets.FirstOrDefault(p => p.Slot == slot);
        if (p is null) return;
        _ui.BeginInvoke(() =>
        {
            _executingRemote = true;
            try
            {
                LoadPreset(p);
                if (autoStart) StartTimer();
                Publish();
            }
            finally { _executingRemote = false; }
        });
    }

    public void ReplacePreset(TimerPreset updated)
    {
        var idx = Presets.ToList().FindIndex(p => p.Slot == updated.Slot);
        if (idx < 0) return;
        Presets[idx] = updated;
        _store.Update(s =>
        {
            var i = s.Presets.FindIndex(p => p.Slot == updated.Slot);
            if (i >= 0) s.Presets[i] = updated;
        });
        _store.SaveNow();
        BumpConfig();
    }

    public void ReloadPresets()
    {
        Presets.Clear();
        foreach (var p in _store.Settings.Presets) Presets.Add(p);
        BumpConfig();
    }

    // ================================================================== parsing helpers

    private static string UrgencyWord(MessageUrgency u) => u switch { MessageUrgency.Warning => "warn", MessageUrgency.Critical => "crit", _ => "info" };

    private static bool TryParseDuration(string s, out TimeSpan ts)
    {
        ts = default;
        var parts = s.Trim().Split(':');
        try
        {
            if (parts.Length == 3)
            {
                ts = new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
                return true;
            }
            if (parts.Length == 2)
            {
                ts = new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1]));
                return true;
            }
            if (int.TryParse(s, out var secs)) { ts = TimeSpan.FromSeconds(secs); return true; }
        }
        catch { }
        return false;
    }

    private static bool TryParseNudge(string token, out TimeSpan ts)
    {
        ts = default;
        if (string.IsNullOrEmpty(token)) return false;
        var sign = token[0] == '-' ? -1 : 1;
        var body = token.TrimStart('+', '-');
        if (body.Length < 2) return false;
        var unit = body[^1];
        if (!int.TryParse(body[..^1], out var n)) return false;
        ts = unit switch
        {
            's' => TimeSpan.FromSeconds(n * sign),
            'm' => TimeSpan.FromMinutes(n * sign),
            'h' => TimeSpan.FromHours(n * sign),
            _ => TimeSpan.Zero
        };
        return ts != TimeSpan.Zero;
    }

    /// <summary>The operator closed Stagewatch on purpose: nothing to offer back next time.</summary>
    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _sessionTimer?.Stop();
        if (_pendingRecovery is null) SaveSession(cleanClose: true); // keep an unanswered recovery for next launch
        try { SetThreadExecutionState(EsContinuous); } catch (EntryPointNotFoundException) { }
        StopRemote();
        // No first use can arrive now: a binding whose save was still posted to the show thread is saved here, so the
        // settings written at exit carry every pair and a restart never leaves a used key free for whichever app comes first.
        SaveBindings();
        DisposeIntegrations();
        _companion.StopAsync().GetAwaiter().GetResult();
        _engine.Dispose();
        _ndi?.Dispose();
        _sounds.Dispose();
    }
}

/// <summary>One line of the running-order panel.</summary>
/// <summary>A label on a running-order row.</summary>
public sealed record LabelChip(string Name, System.Windows.Media.Brush Color);

public sealed partial class RundownRow : ObservableObject
{
    public int Index { get; init; }
    public string Title { get; init; } = "";
    public string Speaker { get; init; } = "";
    public string DurationText { get; init; } = "";
    public string PlannedStartText { get; init; } = "";
    public bool AutoAdvance { get; init; }
    public SegmentKind Kind { get; init; }
    public bool Fixed { get; init; }
    public string HardOut { get; init; } = "";
    public int AdjustSeconds { get; init; }
    public bool HasSpeaker => Speaker.Length > 0;
    /// <summary>On the first item of a group: the group's name (upper case), its length and target.</summary>
    public string GroupHeader { get; init; } = "";
    /// <summary>Label chips: (name, colour).</summary>
    public IReadOnlyList<LabelChip> LabelChips { get; init; } = Array.Empty<LabelChip>();
    public bool HasLabels => LabelChips.Count > 0;
    public string GroupDetail { get; init; } = "";
    public bool GroupOver { get; init; }
    /// <summary>Planned start (📌 fixed), curfew, trims, plus ↪ when the item rolls into the next on its own.</summary>
    public string Subtitle => (PlannedStartText + (Fixed ? " 📌" : "")
        + (HardOut.Length > 0 ? $"  ⏹ {HardOut}" : "")
        + (AdjustSeconds != 0 ? $"  {(AdjustSeconds < 0 ? "−" : "+")}{TimerTick.FormatSeconds(Math.Abs(AdjustSeconds))}" : "")
        + (AutoAdvance ? "  ↪" : "")).Trim();

    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isDone;
}
