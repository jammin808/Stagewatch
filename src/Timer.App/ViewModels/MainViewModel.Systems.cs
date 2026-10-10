using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// The 2.0 show systems, run inside the single-authority frame loop (owner thread only):
/// cue ladders, acknowledged signals, Q&amp;A, schedule recovery, forecasting, the producer
/// assistant, the journal and reports. Each system is a plain object updated once per frame; the
/// frame is then built from all of them and published as one immutable snapshot.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly string DataFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch");

    private readonly SignalBoard _signals = new();
    private readonly QaBoard _qa = new();
    private ShowJournal _journal = new(null);
    private readonly OverrunHistory _history = new(Path.Combine(DataFolder, "history.json"));
    private OverrunModel _model = OverrunModel.Learn(Array.Empty<HistoryRecord>());
    private readonly RehearsalRecorder _rehearsal = new();
    private readonly RehearsalStore _rehearsals = new(Path.Combine(DataFolder, "rehearsals.json"));
    private readonly RuleAdvisor _ruleAdvisor = new();

    // Running-order truth (owner thread).
    private readonly Dictionary<int, ItemActual> _actuals = new();
    private TimeSpan _pendingDelay;
    private ScheduleProjection _projection = ScheduleProjection.Empty;
    private ForecastResult? _forecast;
    private string _forecastKey = "";
    private DateTime _forecastAtUtc = DateTime.MinValue;
    private int _forecastRunning;

    // Briefing preview: the cue ladder at 10× on every output.
    private DateTime? _previewStartUtc;
    private TimeSpan _previewTotal;

    // Message acknowledgement.
    private int _messageSignalId;

    // Edge detection on published frames (for the journal and triggers).
    private ShowSnapshot? _lastPublished;
    private long? _lastCueSecondsForSound;

    [ObservableProperty] private string _cueLabelText = "";
    [ObservableProperty] private string _finishByText = "";
    [ObservableProperty] private string _slidesText = "";
    [ObservableProperty] private string _paceText = "";
    [ObservableProperty] private string _playoutText = "";
    [ObservableProperty] private string _forecastLine = "";
    [ObservableProperty] private string _hardOutLine = "";
    [ObservableProperty] private bool _hardOutAtRisk;
    [ObservableProperty] private bool _previewActive;
    [ObservableProperty] private bool _rehearsing;
    [ObservableProperty] private bool _listening;
    [ObservableProperty] private string _messageAckText = "";
    [ObservableProperty] private bool _ackRequested;
    [ObservableProperty] private string _activeLadderId = "";
    [ObservableProperty] private SpeakerTimerFormat _speakerFormat;
    [ObservableProperty] private string _delayText = "";
    [ObservableProperty] private string _roomsLine = "";
    [ObservableProperty] private string _clockLine = "";
    [ObservableProperty] private string _questionsLine = "";

    public ObservableCollection<Advice> AdviceItems { get; } = new();
    public ObservableCollection<SignalView> SignalItems { get; } = new();
    public ObservableCollection<QuestionView> QuestionItems { get; } = new();
    public IReadOnlyList<CueLadder> Ladders => Settings.Display.AllLadders();
    public IReadOnlyList<string> Departments => Settings.Signals.Departments;
    public IReadOnlyList<SpeakerTimerFormat> SpeakerFormats { get; } =
        new[] { SpeakerTimerFormat.Standard, SpeakerTimerFormat.Calm, SpeakerTimerFormat.ProgressOnly };

    public ShowClock ShowClock => ShowClock.For(Settings.Rundown.TimeZoneId);

    private void InitSystems()
    {
        _model = OverrunModel.Learn(_history.Records);
        _signals.EscalateAfter = TimeSpan.FromSeconds(Math.Clamp(Settings.Signals.EscalateSeconds, 5, 600));
        OpenJournal(null);
    }

    private void OpenJournal(string? path)
    {
        var old = _journal;
        _journal = new ShowJournal(path ?? Path.Combine(DataFolder, "journal", DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl"));
        // Continuing a show after a crash: the report must include everything before it.
        if (path is not null && File.Exists(path)) _journal.Seed(ShowJournal.Load(path));
        _ = old.DisposeAsync();
    }

    public string? JournalPath => _journal.Path;

    // ================================================================== the frame

    /// <summary>The cue ladder for whatever is loaded: the item's own, else the show default.</summary>
    private CueLadder CurrentLadder()
    {
        var items = Settings.Rundown.Items;
        string? id = _rundownActive && _currentItem >= 0 && _currentItem < items.Count ? items[_currentItem].CueLadderId : null;
        return Settings.Display.LadderFor(string.IsNullOrWhiteSpace(id) ? ActiveLadderId : id);
    }

    private readonly record struct CueState(TimerPhase Phase, string Color, string Label, FlashRate Flash, CueStep? Step);

    private CueState EvaluateCue(TimerTick t, long secs, CueLadder ladder)
    {
        var d = Settings.Display;
        if (t.Mode != TimerMode.Countdown || (t.State == TimerState.Idle && secs <= 0))
            return new CueState(TimerPhase.Normal, SpeakerFace.ColorFor(d.Palette, TimerPhase.Normal, ladder.NormalColorHex), "", FlashRate.Off, null);
        var step = ladder.StepAt(secs);
        var over = t.State == TimerState.Expired || secs < 0;
        if (over && ladder.OvertimeSignal)
            return new CueState(TimerPhase.Overtime, SpeakerFace.ColorFor(d.Palette, TimerPhase.Overtime, ladder.OvertimeColorHex),
                ladder.OvertimeLabel, ladder.OvertimeFlash, step);
        if (step is null)
            return new CueState(TimerPhase.Normal, SpeakerFace.ColorFor(d.Palette, TimerPhase.Normal, ladder.NormalColorHex), "", FlashRate.Off, null);
        var phase = over ? TimerPhase.Overtime : step.Phase;
        return new CueState(phase, SpeakerFace.ColorFor(d.Palette, phase, step.ColorHex), step.Label, step.Flash, step);
    }

    /// <summary>The engine's tick, or the simulated one while a briefing preview is playing.</summary>
    private TimerTick FrameTick(DateTime nowUtc)
    {
        if (_previewStartUtc is not { } start) return _lastTick;
        var rem = _previewTotal - TimeSpan.FromTicks((nowUtc - start).Ticks * 10);
        if (rem < TimeSpan.FromSeconds(-30)) { StopPreview(); return _lastTick; }
        return new TimerTick(TimerMode.Countdown, rem > TimeSpan.Zero ? TimerState.Running : TimerState.Expired, rem, DateTime.Now, null, _previewTotal);
    }

    private ShowSnapshot BuildSnapshot()
    {
        var now = DateTime.UtcNow;
        var t = FrameTick(now);
        var preview = _previewStartUtc is not null;
        var settings = _store.Settings;
        var disp = settings.Display;
        var msg = settings.MessageState;
        var secs = t.DisplaySeconds;
        var ladder = CurrentLadder();
        var cue = EvaluateCue(t, secs, ladder);
        var phase = cue.Phase;
        var clock = ShowClock;

        // Effective message flash/urgency: operator's choice, overridden by the cue ladder and thresholds.
        var urgency = msg.Urgency;
        var flash = msg.Flash;
        var flashTimer = _manualTimerFlash;
        if (t.Mode == TimerMode.Countdown && t.State is TimerState.Running or TimerState.Expired)
        {
            if (phase == TimerPhase.Overtime)
            {
                if (cue.Flash != FlashRate.Off) { flashTimer = true; flash = cue.Flash; }
            }
            else if (cue.Flash != FlashRate.Off) { flashTimer = true; flash = cue.Flash; }
            else if (secs <= msg.AutoFlashCriticalSeconds) { flash = FlashRate.Fast; urgency = MessageUrgency.Critical; }
            else if (secs <= msg.AutoFlashWarnSeconds) { flash = FlashRate.Slow; urgency = MessageUrgency.Warning; }
        }

        var items = settings.Rundown.Items;
        var next = _rundownActive && _currentItem >= 0 && _currentItem + 1 < items.Count ? items[_currentItem + 1].DisplayTitle : "";

        // ---- schedule recovery
        long? offset = null;
        var scheduleText = "";
        var projection = ScheduleProjection.Empty;
        if (items.Count > 0 && ScheduleAnchor() is { } anchor)
        {
            var cur = _rundownActive ? _currentItem : -1;
            var rem = cur >= 0 && t.Mode == TimerMode.Countdown && !preview ? _lastTick.Remaining : TimeSpan.Zero;
            projection = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, cur, _currentItemStarted, rem, _actuals, now, clock, _pendingDelay,
                EndOfShow: Settings.Rundown.EndOfShow));
            if (cur >= 0 && cur < items.Count && t.Mode == TimerMode.Countdown)
            {
                offset = projection.OffsetSeconds;
                var end = clock.ToShow(projection.ExpectedEndUtc);
                scheduleText = $"{Schedule.DescribeOffset(TimeSpan.FromSeconds(offset ?? 0))} · show ends {Fmt(end, seconds: false)}"
                               + (end.Date != clock.ToShow(now).Date ? $" {end:ddd}" : "");
            }
        }
        _projection = projection;

        // ---- speaker face (the operator always sees the true engine value)
        var operatorText = t.Mode == TimerMode.TimeOfDay && disp.Use12HourClock ? Fmt(DateTime.Today + t.Remaining) : t.Format();
        var speakerSeconds = secs;
        var speakerBase = operatorText;
        var stretching = !preview && _engine.IsStretching;
        if (stretching && disp.HonestSpeakerClock && _engine.EndAt is { } endAt)
        {
            // Never show the speaker a stretched clock: give them the real time left.
            speakerSeconds = (long)Math.Ceiling((endAt - DateTime.Now).TotalSeconds - 0.0005);
            speakerBase = TimerTick.FormatSeconds(speakerSeconds);
        }
        var speakerText = t.Mode == TimerMode.Countdown ? SpeakerFace.Format(SpeakerFormat, speakerSeconds, speakerBase) : operatorText;
        var finishBy = t.Mode == TimerMode.Countdown && t.State != TimerState.Expired && !preview
            ? SpeakerFace.FinishBy(_engine.EndsAtWallClock, disp.Use12HourClock) : "";

        // ---- message (placeholders re-rendered every frame) and its acknowledgement
        var curItem = _rundownActive && _currentItem >= 0 && _currentItem < items.Count ? items[_currentItem] : null;
        var message = Placeholders.Render(msg.Text, new Placeholders.Context(
            next, finishBy.Replace("FINISH BY ", ""), Placeholders.OverUnder(offset), TimerTick.FormatSeconds(Math.Max(0, secs)),
            _activeTitle, curItem?.Speaker ?? "", Fmt(DateTime.Now, seconds: false)));
        var ack = "";
        var ackBy = "";
        if (_messageSignalId > 0 && _signals.Find(_messageSignalId) is { } ms)
        {
            ack = ms.State switch { SignalState.Sent => "sent", SignalState.Seen => "seen", SignalState.Done => "done", _ => "" };
            ackBy = ms.By;
        }

        // ---- presentation awareness
        var slides = BuildSlides(now, t);
        var speech = _speech is { } sp && sp.Listening && sp.Sample(now) is { } pos
            ? new SpeechView(pos.Percent, pos.WordsPerMinute, pos.ProjectedRemainingSeconds, true) : (SpeechView?)null;
        var playout = PickPlayout() is { } vt
            ? new PlayoutView(vt.Title, (long)Math.Ceiling(vt.RemainingSeconds), (long)Math.Round(vt.DurationSeconds), vt.Running, vt.Source) : (PlayoutView?)null;

        // ---- hard-outs, forecast, advice
        var hardOutText = "";
        long? hardOutMargin = null;
        if (projection.NextHardOut is { } h)
        {
            hardOutMargin = h.MarginSeconds;
            hardOutText = $"Curfew {Fmt(clock.ToShow(h.CurfewUtc), seconds: false)}: " + (h.MarginSeconds >= 0
                ? $"{TimerTick.FormatSeconds(h.MarginSeconds)} to spare"
                : $"{TimerTick.FormatSeconds(-h.MarginSeconds)} OVER");
        }
        MaybeForecast(now, projection);
        var forecastText = _forecast is { } f && _rundownActive
            ? $"Likely end {Fmt(clock.ToShow(f.P50Utc), seconds: false)} · 80% by {Fmt(clock.ToShow(f.P80Utc), seconds: false)}"
              + (f.CurfewRisk > 0 ? $" · curfew risk {f.CurfewRisk:P0}" : "")
            : "";

        var advice = BuildAdvice(now, projection, t, slides);

        return new ShowSnapshot
        {
            Mode = t.Mode,
            State = t.State,
            Seconds = secs,
            Text = operatorText,
            TotalSeconds = (long)Math.Round(t.Total.TotalSeconds),
            Phase = phase,
            ColorHex = cue.Color,
            Title = preview ? "PREVIEW — " + (_activeTitle.Length > 0 ? _activeTitle : "cue briefing") : _activeTitle,
            NextTitle = next,
            ItemIndex = _rundownActive ? _currentItem : -1,
            ItemCount = items.Count,
            PresetSlot = !_rundownActive && t.Mode != TimerMode.TimeOfDay ? t.PresetSlot ?? 0 : 0,
            EndTimeText = preview ? "" : ComputeEndTimeText(),
            TimeOfDay = TimeOfDayText,
            OffsetSeconds = offset,
            ScheduleText = scheduleText,
            Message = message,
            Urgency = urgency,
            Flash = flash,
            FlashTimer = flashTimer,
            Blackout = Blackout,
            Output = _outputWindow is null ? OutputMode.Closed : SelectedOutputMode,
            Ndi = _ndi is not null,
            HoldingId = _holdingId,
            HoldingName = _store.Settings.Holding.Images.FirstOrDefault(i => i.Id == _holdingId)?.Name ?? "",
            HoldingFill = HoldingFill,
            LtcText = _ltcView.Text,
            LtcRunning = _ltcView.Running,
            LtcChase = Settings.Integrations.LtcChase && _ltcIn is not null,
            Aux = AuxViews(),
            AuxLine = AuxStageLine(),
            Watermark = _license.ShowsWatermark ? FeatureGate.WatermarkText : "",
            Elements = disp.Elements,
            ConfigVersion = _configVersion,

            // "Hide at zero": speaker screens go blank in overtime (a space, so they don't switch to the progress-only bar);
            // the operator's own display still counts on.
            SpeakerText = Settings.HideAtZero && t.Mode == TimerMode.Countdown && secs <= 0 && t.State is TimerState.Running or TimerState.Expired ? " " : speakerText,
            CueLabel = cue.Label,
            FinishBy = finishBy,
            Preview = preview,
            Stretching = stretching,
            StableLayout = disp.StableLayout,
            Holographic = disp.Holographic && FeatureGate.AllowsHolographic(_license.Tier),
            HoloFloor = disp.HoloFloor,
            EggTimer = disp.EggTimer,
            Animations3D = disp.Animations3D,
            Particles3D = disp.Particles3D,
            MessageSignalId = ack.Length > 0 ? _messageSignalId : 0,
            MessageAck = ack,
            MessageAckBy = ackBy,
            Signals = _signals.Views(now),
            Slides = slides,
            Speech = speech,
            Playout = playout,
            Room = settings.Integrations.RoomName,
            Timings = BuildTimings(projection, items, clock, now),
            Groups = RundownGroups.Build(Settings.Rundown, projection, now,
                u => Clock.Time(clock.ToShow(u), Settings.Display.Use12HourClock, seconds: false)).ToEquatableArray(),
            ClockSeconds = (long)clock.ToShow(now).TimeOfDay.TotalSeconds,
            UtcSeconds = new DateTimeOffset(now).ToUnixTimeSeconds(),
            NotesVersion = Volatile.Read(ref _notesVersion),
            ChimeSeq = Volatile.Read(ref _chimeSeq),
            Chime = _chimeSpec,
            LabelColors = string.Join(";", Stagewatch.Core.Labels.InUse(items).Select((l, i) => l + "=" + Stagewatch.Core.Labels.ColorFor(Settings.Rundown, l, i))),
            Roll = RollMode,
            RundownName = Settings.RundownName,
            RundownNames = RundownNames.ToEquatableArray(),
            DateText = clock.ToShow(now).ToString("dddd d MMMM", System.Globalization.CultureInfo.CurrentCulture),
            HardOutText = hardOutText,
            HardOutMarginSeconds = hardOutMargin,
            ForecastText = forecastText,
            PendingDelaySeconds = (long)_pendingDelay.TotalSeconds,
            Advice = advice,
            Rooms = _rooms?.Rooms().ToEquatableArray() ?? EquatableArray<RoomView>.Empty,
            ClockOffsetMs = _ntp?.Value is { } c ? (int)Math.Round(c.Offset.TotalMilliseconds / 50) * 50 : null,
            QaLive = _qa.Live?.Text ?? "",
            QaPending = _qa.Pending,
            QaQueued = _qa.Queued,
            QaFit = _qa.FitIn(t.Remaining),
            Questions = _qa.Views()
        };
    }

    /// <summary>What an item-start/end trigger can match: its number, "label:VT", "group:Morning", "kind:break".</summary>
    private string ItemFilters(int index)
    {
        var it = Settings.Rundown.Items[index];
        var parts = new List<string> { (index + 1).ToString(CultureInfo.InvariantCulture), "kind:" + it.Kind.ToString().ToLowerInvariant() };
        parts.AddRange(it.Labels.Select(l => "label:" + l));
        if (!string.IsNullOrWhiteSpace(it.Group)) parts.Add("group:" + it.Group.Trim());
        return string.Join("|", parts);
    }

    private EquatableArray<ItemTimingView> BuildTimings(ScheduleProjection p, IReadOnlyList<RundownItem> items, ShowClock clock, DateTime now)
    {
        if (p.Items.Length != items.Count) return EquatableArray<ItemTimingView>.Empty;
        var twelve = Settings.Display.Use12HourClock;
        string Hm(DateTime utc) => Clock.Time(clock.ToShow(utc), twelve, seconds: false);
        string Hms(DateTime? utc) => utc is { } u ? Clock.Time(clock.ToShow(u), twelve, seconds: true) : "";
        var list = new ItemTimingView[items.Count];
        long In(DateTime utc) => (long)Math.Round((utc - now).TotalSeconds);
        for (int i = 0; i < items.Count; i++)
        {
            var t = p.Items[i];
            var it = items[i];
            list[i] = new ItemTimingView(i, it.Title, it.Speaker ?? "", it.Kind, it.EffectiveSeconds, it.AdjustSeconds,
                Hm(t.PlannedStartUtc), Hm(t.ExpectedStartUtc), Hm(t.ExpectedEndUtc), Hms(t.ActualStartUtc), Hms(t.ActualEndUtc),
                t.Status, t.DelaySeconds / 30 * 30, t.Conflict, it.StartMode == StartMode.Fixed, it.HardOut ?? "",
                (it.Group ?? "").Trim(),
                In(t.ActualStartUtc ?? t.ExpectedStartUtc), In(t.ActualEndUtc ?? t.ExpectedEndUtc), In(t.PlannedStartUtc),
                string.Join(",", it.Labels));
        }
        return list.ToEquatableArray();
    }

    private SlideView? BuildSlides(DateTime now, TimerTick t)
    {
        if (CurrentDeck() is not { } deck) return null;
        if (_rehearsal.Active) _rehearsal.Observe(deck.Slide, deck.Count, now);
        var budgets = _rehearsals.For(deck.Presentation) ?? deck.RehearsalBudgets;
        GhostResult ghost = default;
        if (budgets.Length > 0 && !_rehearsal.Active)
            ghost = GhostRun.Evaluate(budgets, deck.Slide, (now - deck.SlideSinceUtc).TotalSeconds, (now - deck.ShowSinceUtc).TotalSeconds);
        return new SlideView(deck.Slide, deck.Count, deck.NextTitle, deck.Notes, ghost.DeltaSeconds, ghost.ProjectedTotalSeconds, ghost.State, _rehearsal.Active);
    }

    private EquatableArray<Advice> BuildAdvice(DateTime now, ScheduleProjection p, TimerTick t, SlideView? slides)
    {
        if (!Settings.Integrations.Assistant || !FeatureGate.AllowsAssistant(_license.Tier)) return EquatableArray<Advice>.Empty;
        GhostResult? ghost = slides is { Pace: not PaceState.Unknown } s ? new GhostResult(s.GhostDeltaSeconds, s.ProjectedSeconds, s.Pace) : null;
        ScriptPosition? script = _speech is { Listening: true } sp ? sp.Position : null;
        var input = new AdvisorInput(Settings.Rundown.Items, p, _rundownActive ? _currentItem : -1, _engine.IsCounting,
            t.Remaining, _forecast, ghost, script, _signals.All, _qa.Queued, _qa.FitIn(t.Remaining), now, ShowClock,
            Settings.Display.Use12HourClock);
        var list = new List<Advice>(_ruleAdvisor.Advise(input));
        if (_cloud is { } cloud)
        {
            list.AddRange(cloud.Latest);
            if (_rundownActive && _currentItemStarted) cloud.MaybeAsk(DescribeShowForAssistant(p, now), now);
        }
        return list.Take(8).ToEquatableArray();
    }

    /// <summary>Compact JSON the cloud assistant sees (titles, plan, expected/actual, curfews, live remaining).</summary>
    private string DescribeShowForAssistant(ScheduleProjection p, DateTime now)
    {
        var clock = ShowClock;
        var items = Settings.Rundown.Items;
        var rows = items.Select((it, i) => new
        {
            n = i + 1, title = it.Title, kind = it.Kind.ToString(), planned_min = it.DurationSeconds / 60.0, adjust_s = it.AdjustSeconds,
            min_s = it.MinDurationSeconds, start_mode = it.StartMode.ToString(), fixed_start = it.FixedStart, hard_out = it.HardOut,
            planned_start = i < p.Items.Length ? clock.ToShow(p.Items[i].PlannedStartUtc).ToString("HH:mm") : null,
            expected_start = i < p.Items.Length ? clock.ToShow(p.Items[i].ExpectedStartUtc).ToString("HH:mm") : null,
            status = i < p.Items.Length ? p.Items[i].Status.ToString() : null
        });
        return JsonSerializer.Serialize(new
        {
            now = clock.ToShow(now).ToString("HH:mm:ss"),
            live_item = _rundownActive ? _currentItem + 1 : 0,
            live_remaining_s = (long)_lastTick.Remaining.TotalSeconds,
            offset_s = p.OffsetSeconds,
            pending_delay_s = (long)_pendingDelay.TotalSeconds,
            qa_queued = _qa.Queued,
            items = rows
        });
    }

    private void MaybeForecast(DateTime now, ScheduleProjection p)
    {
        if (!_rundownActive || p.Items.Length == 0 || !FeatureGate.AllowsForecast(_license.Tier)) { _forecast = null; return; }
        var key = $"{_currentItem}|{_currentItemStarted}|{_configVersion}|{(long)_pendingDelay.TotalSeconds}";
        if (key == _forecastKey && now - _forecastAtUtc < TimeSpan.FromSeconds(30)) return;
        if (Interlocked.Exchange(ref _forecastRunning, 1) == 1) return;
        _forecastKey = key;
        _forecastAtUtc = now;
        var items = Settings.Rundown.Items.Select(i => i.Clone()).ToList();
        var cur = _currentItem;
        var model = _model;
        DateTime? curfew = p.NextHardOut?.CurfewUtc;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var f = Forecaster.Run(items, p, cur, now, model, curfew);
                _ui.BeginInvoke(() => { _forecast = f; MarkDirty(); });
            }
            catch (Exception) { }
            finally { Volatile.Write(ref _forecastRunning, 0); }
        });
    }

    /// <summary>4 Hz housekeeping: signals escalate, sensors are sampled, the frame is rebuilt if anything changed.</summary>
    private void UpdateSystems()
    {
        UpdateRoll();
        var now = DateTime.UtcNow;
        foreach (var esc in _signals.Update(now))
        {
            _journal.Add(now, JournalKind.Escalated, _rundownActive ? _currentItem : -1, $"{esc.Kind} {esc.Target}: {esc.Text}");
            FireTrigger(TriggerEvent.Escalated, esc.Target, esc.Text);
            StatusLine = $"No acknowledgement from {esc.Target} for \"{(esc.Text.Length > 0 ? esc.Text : esc.Kind.ToString())}\" — radio them.";
        }
        if (_speech is { } sp && CurrentDeck() is { Script.Length: > 0 } deck && Settings.Integrations.ScriptPath.Length == 0) sp.SetScript(deck.Script);
        MarkDirty();
        Publish();
    }

    /// <summary>After a frame is published: journal and trigger the edges (phase changes, overtime, curfew risk).</summary>
    private void OnFramePublished(ShowSnapshot s)
    {
        var prev = _lastPublished;
        _lastPublished = s;
        RefreshSystemsUi(s);
        if (prev is null || s.Preview || prev.Preview) return;
        var now = DateTime.UtcNow;
        var item = s.ItemIndex;
        if (s.Phase != prev.Phase && s.Mode == TimerMode.Countdown && s.State != TimerState.Idle)
        {
            _journal.Add(now, JournalKind.Phase, item, s.Phase.ToString());
            FireTrigger(TriggerEvent.Phase, s.Phase.ToString().ToLowerInvariant(), s.CueLabel);
            if (s.Phase == TimerPhase.Overtime) FireTrigger(TriggerEvent.Overtime, "", s.Title);
        }
        if ((prev.HardOutMarginSeconds ?? 0) >= 0 && s.HardOutMarginSeconds is < 0)
            FireTrigger(TriggerEvent.HardOutRisk, "", s.HardOutText);
        if (s.Blackout != prev.Blackout)
        {
            _journal.Add(now, JournalKind.Blackout, item, s.Blackout ? "on" : "off");
            FireTrigger(TriggerEvent.Blackout, s.Blackout ? "on" : "off", "");
        }
    }

    private void RefreshSystemsUi(ShowSnapshot s)
    {
        CueLabelText = s.CueLabel;
        FinishByText = s.FinishBy;
        SlidesText = s.Slides is { } sl
            ? $"Slide {sl.Slide}/{sl.Count}" + (sl.NextTitle.Length > 0 ? $" · next: {sl.NextTitle}" : "") + (sl.Rehearsing ? " · REHEARSING" : "")
            : "";
        PaceText = s.Slides is { Pace: not PaceState.Unknown } g
            ? (g.Pace switch { PaceState.Behind => $"{TimerTick.FormatSeconds(g.GhostDeltaSeconds)} behind rehearsal", PaceState.Ahead => $"{TimerTick.FormatSeconds(-g.GhostDeltaSeconds)} ahead of rehearsal", _ => "on rehearsal pace" })
              + $" · projected {TimerTick.FormatSeconds(g.ProjectedSeconds)}"
            : s.Speech is { } sp ? $"Script {sp.Percent:0}% · {sp.WordsPerMinute:0} wpm" + (sp.ProjectedRemainingSeconds is { } r ? $" · ~{TimerTick.FormatSeconds(r)} to go" : "") : "";
        PlayoutText = s.Playout is { } p ? $"VT {p.Title}: {TimerTick.FormatSeconds(p.RemainingSeconds)}" + (p.Running ? "" : " (paused)") : "";
        RefreshPatternsButton();
        ForecastLine = s.ForecastText;
        HardOutLine = s.HardOutText;
        HardOutAtRisk = s.HardOutMarginSeconds is < 0;
        MessageAckText = s.MessageAck switch
        {
            "sent" => "⏳ waiting for the speaker to acknowledge",
            "seen" => "✓ seen" + (s.MessageAckBy.Length > 0 ? $" by {s.MessageAckBy}" : ""),
            "done" => "✓ acknowledged" + (s.MessageAckBy.Length > 0 ? $" by {s.MessageAckBy}" : ""),
            _ => ""
        };
        DelayText = s.PendingDelaySeconds > 0 ? $"Delay {TimerTick.FormatSeconds(s.PendingDelaySeconds)} before next item" : "";
        RoomsLine = s.Rooms.Count == 0 ? "" : string.Join("  ·  ", s.Rooms.Select(r =>
            $"{r.Room}: {r.Text}{(r.OffsetSeconds is > 30 ? $" (+{TimerTick.FormatSeconds(r.OffsetSeconds.Value)})" : "")}{(r.Stale ? " ?" : "")}"));
        ClockLine = s.ClockOffsetMs is { } ms && Math.Abs(ms) > 1000 ? $"⚠ PC clock {Math.Abs(ms) / 1000.0:0.0} s {(ms > 0 ? "slow" : "fast")}" : "";
        QuestionsLine = s.QaPending + s.QaQueued > 0 || s.QaLive.Length > 0
            ? $"Q&A: {s.QaPending} new · {s.QaQueued} queued · ~{s.QaFit} fit" + (s.QaLive.Length > 0 ? $" · live: {s.QaLive}" : "") : "";
        Sync(AdviceItems, s.Advice);
        Sync(SignalItems, s.Signals);
        Sync(QuestionItems, s.Questions);
    }

    private static void Sync<T>(ObservableCollection<T> target, EquatableArray<T> source)
    {
        if (target.Count == source.Count && target.SequenceEqual(source)) return;
        target.Clear();
        foreach (var x in source) target.Add(x);
    }

    // ================================================================== cue sounds (cue ladder)

    private void CheckCueSounds(TimerTick t)
    {
        if (t.Mode != TimerMode.Countdown || t.State is not (TimerState.Running or TimerState.Expired) || _previewStartUtc is not null)
        {
            _lastCueSecondsForSound = null;
            return;
        }
        var cur = t.DisplaySeconds;
        var prev = _lastCueSecondsForSound;
        _lastCueSecondsForSound = cur;
        if (prev is not { } p || cur >= p) return; // downward crossings while the clock runs
        if (!FeatureGate.AllowsAlerts(_license.Tier)) return;
        if (CurrentLadder().Crossed(p, cur) is not { Sound: not CueSound.None } step) return;

        var a = Settings.Alerts;
        string Device(string specific) => specific.Length > 0 ? specific : a.Device ?? "";
        switch (step.Sound)
        {
            // Each moment: the live item's own chime if it has one, else the show's alert when that's switched on.
            case CueSound.Warning:
                PlayChime(ChimeFor("warning", a.OnWarning), Device(a.WarningDevice));
                break;
            case CueSound.WrapUp:
                PlayChime(ChimeFor("wrapup", a.OnWrapUp), Device(a.WrapUpDevice));
                break;
            case CueSound.Zero:
                PlayChime(ChimeFor("zero", a.OnZero), Device(a.ZeroDevice));
                break;
            case CueSound.Speak:
                var words = string.IsNullOrWhiteSpace(step.Say) ? step.Label.ToLowerInvariant() : step.Say;
                var device = Settings.Integrations.TalentDevice.Length > 0 ? Settings.Integrations.TalentDevice : a.Device ?? "";
                var voice = Settings.Integrations.Voice;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    if (_voices.Render(words, voice) is { } pcm && !WavePlayer.PlayOnce(pcm, SpokenCues.SampleRate, device))
                        _ui.BeginInvoke(() => AudioMissing(device));
                });
                break;
        }
    }

    private void AudioMissing(string device)
        => StatusLine = $"Cue not played: audio device \"{device}\" isn't connected (Settings → Timer / Integrations).";

    // ================================================================== running-order bookkeeping

    /// <summary>The live item has started for the first time.</summary>
    private void MarkItemStarted(int index)
    {
        var now = DateTime.UtcNow;
        var first = _actuals.Count == 0;
        _actuals[index] = new ItemActual(now, null);
        _pendingDelay = TimeSpan.Zero;
        _journal.Add(now, JournalKind.ItemStart, index, Settings.Rundown.Items[index].Title);
        if (first) FireTrigger(TriggerEvent.ShowStart, "", Settings.Rundown.Items[index].Title);
        if (FeatureGate.AllowsAlerts(_license.Tier)) PlayChime(ChimeFor("start", Settings.Alerts.OnStart), Settings.Alerts.Device ?? "");
        FireTrigger(TriggerEvent.ItemStart, ItemFilters(index), Settings.Rundown.Items[index].Title);
    }

    /// <summary>Leaving an item that ran: record its end (journal, overrun history, triggers).</summary>
    private void MarkItemEnded(int index)
    {
        if (index < 0 || !_actuals.TryGetValue(index, out var a) || a.StartUtc is not { } start || a.EndUtc is not null) return;
        var now = DateTime.UtcNow;
        _actuals[index] = a with { EndUtc = now };
        var items = Settings.Rundown.Items;
        if (index >= items.Count) return;
        var it = items[index];
        _journal.Add(now, JournalKind.ItemEnd, index, it.Title, (now - start).TotalSeconds);
        _history.Add(new HistoryRecord(it.Kind, it.Speaker ?? "", it.EffectiveSeconds, (int)Math.Round((now - start).TotalSeconds), now));
        _model = OverrunModel.Learn(_history.Records);
        FireTrigger(TriggerEvent.ItemEnd, ItemFilters(index), it.Title);
    }

    // ================================================================== 2.0 commands

    private void ExecuteSystemCommand(ShowCommand c)
    {
        var now = DateTime.UtcNow;
        var item = _rundownActive ? _currentItem : -1;
        switch (c.Kind)
        {
            case ShowCommandKind.Signal:
            {
                if (!Gate(CrewCuesAllowed, "Crew cues", FeatureTier.Production)) break;
                var parts = (c.Text ?? "").Split('\u0001');
                var kind = (SignalKind)(int)c.Number;
                SendSignal(kind, parts[0], parts.Length > 1 ? parts[1] : "", c.Flag);
                break;
            }
            case ShowCommandKind.Ack:
            {
                var parts = (c.Text ?? "|seen").Split('|');
                var before = _signals.Find((int)c.Number);
                if (_signals.Ack((int)c.Number, parts[0], parts.Length > 1 ? parts[1] : "seen", now) is { } s)
                {
                    var latency = before is null ? 0 : (now - before.SentUtc).TotalSeconds;
                    _journal.Add(now, JournalKind.Ack, item, $"{s.Kind} {s.Target}: {s.Text} ({s.State}{(s.By.Length > 0 ? " by " + s.By : "")})", Math.Round(latency, 1));
                    FireTrigger(TriggerEvent.Ack, s.Target, s.Text);
                    StatusLine = $"✓ {s.Target} {(s.State == SignalState.Done ? "confirmed" : "saw")} \"{(s.Text.Length > 0 ? s.Text : s.Kind.ToString())}\"" + (s.By.Length > 0 ? $" ({s.By})" : "");
                }
                break;
            }
            case ShowCommandKind.SignalClear:
                if (c.Number > 0) _signals.Cancel((int)c.Number, now);
                else _signals.ClearTarget(c.Text ?? "*", now);
                break;
            case ShowCommandKind.SignalResend:
                if (CrewCuesAllowed && _signals.Find((int)c.Number) is { } old)
                {
                    _signals.Cancel(old.Id, now);
                    SendSignal(old.Kind, old.Target, old.Text, ackRequired: true);
                }
                break;
            case ShowCommandKind.Trim:
                TrimItem((int)c.Number - 1, int.Parse(c.Text ?? "0", CultureInfo.InvariantCulture));
                break;
            case ShowCommandKind.Delay:
                if (Math.Abs(_pendingDelay.TotalSeconds - c.Number) > 0.5)
                    RecordEdit(c.Number > 0 ? $"Delay {TimerTick.FormatSeconds((long)c.Number)}" : "Clear the delay",
                        new ShowCommand(ShowCommandKind.Delay, Number: _pendingDelay.TotalSeconds), c, targets: LiveTarget());
                _pendingDelay = TimeSpan.FromSeconds(c.Number);
                _journal.Add(now, JournalKind.Delay, item, "", c.Number);
                StatusLine = c.Number > 0 ? $"Delay of {TimerTick.FormatSeconds((long)c.Number)} before the next item" : "Delay cleared";
                break;
            case ShowCommandKind.CatchUp:
                CatchUp();
                break;
            case ShowCommandKind.Preview:
                var on = c.Number < 0 ? _previewStartUtc is null : c.Number > 0;
                if (on) StartPreview(); else StopPreview();
                break;
            case ShowCommandKind.Ladder:
                ActiveLadderId = c.Text ?? "";
                break;
            case ShowCommandKind.Format:
                SpeakerFormat = (SpeakerTimerFormat)(int)c.Number;
                break;
            case ShowCommandKind.Question:
            {
                if (!FeatureGate.AllowsRemote(_license.Tier)) break;
                var parts = (c.Text ?? "").Split('\u0001');
                if (_qa.Submit(parts[0], parts.Length > 1 ? parts[1] : "", now) is { } q)
                    _journal.Add(now, JournalKind.Question, item, q.Text);
                break;
            }
            case ShowCommandKind.QuestionSet:
                SetQuestion((int)c.Number, c.Text ?? "");
                break;
            case ShowCommandKind.Rehearse:
                ToggleRehearsal(c.Number < 0 ? !_rehearsal.Active : c.Number > 0);
                break;
            case ShowCommandKind.Listen:
                ToggleListening(c.Number < 0 ? !Listening : c.Number > 0);
                break;
            case ShowCommandKind.Report:
            {
                var format = c.Text == "csv" || !FeatureGate.AllowsFullReport(_license.Tier) ? "csv" : "html";
                if (!Gate(ReportAllowed(format), "The show report", FeatureTier.Pro)) break;
                StatusLine = WriteReport(format, open: false) is { } path ? $"Report saved: {path}" : "Report: nothing to report yet";
            }
                break;
            case ShowCommandKind.Note:
                _journal.Add(now, JournalKind.Note, item, c.Text ?? "");
                StatusLine = "Noted in the show log";
                break;
        }
    }

    private void SendSignal(SignalKind kind, string target, string text, bool ackRequired)
    {
        var now = DateTime.UtcNow;
        var s = _signals.Send(kind, target, text, now);
        _journal.Add(now, JournalKind.Signal, _rundownActive ? _currentItem : -1, $"{kind} {s.Target}: {text}");
        // A message for the speaker also goes on the speaker displays.
        if (kind == SignalKind.Message && s.Target == "speaker")
        {
            MessageInput = text;
            SetMessage(text, MessageUrgency.Warning, FlashRate.Slow, signalId: ackRequired ? s.Id : 0);
        }
        if (!ackRequired) _signals.Ack(s.Id, "", "done", now);
        FireTrigger(TriggerEvent.Message, s.Target, text);
        StatusLine = kind switch
        {
            SignalKind.Standby => $"{s.Target}: STANDBY {text}",
            SignalKind.Go => $"{s.Target}: GO {text}",
            SignalKind.Call => $"Called to stage: {text}",
            SignalKind.Nudge => $"Nudge sent to {s.Target}",
            _ => $"Cue sent to {s.Target}" + (ackRequired ? " (waiting for acknowledgement)" : "")
        };
    }

    private void TrimItem(int index, int deltaSeconds)
    {
        var items = Settings.Rundown.Items;
        if (index < 0 || index >= items.Count || deltaSeconds == 0) return;
        if (!Gate(FeatureGate.AllowsRunOfShow(_license.Tier), "Schedule recovery", FeatureTier.Pro)) return;
        var it = items[index];
        var newAdjust = it.AdjustSeconds + deltaSeconds;
        if (it.DurationSeconds + newAdjust < 0) newAdjust = -it.DurationSeconds;
        var applied = newAdjust - it.AdjustSeconds;
        _store.Update(_ => it.AdjustSeconds = newAdjust);
        RecordEdit($"{(applied < 0 ? "Cut" : "Extend")} \"{it.Title}\" {TimerTick.FormatSeconds(Math.Abs(applied))}",
            new ShowCommand(ShowCommandKind.Trim, Number: index + 1, Text: (-applied).ToString(CultureInfo.InvariantCulture)),
            new ShowCommand(ShowCommandKind.Trim, Number: index + 1, Text: applied.ToString(CultureInfo.InvariantCulture)),
            targets: ItemTarget(index) is { } t ? new[] { t } : null);
        // The live item's clock moves with it.
        if (index == _currentItem && _rundownActive && applied != 0) _engine.Nudge(TimeSpan.FromSeconds(applied));
        _journal.Add(DateTime.UtcNow, JournalKind.Trim, index, it.Title, applied);
        StatusLine = $"{(applied < 0 ? "Cut" : "Extended")} \"{it.Title}\" by {TimerTick.FormatSeconds(Math.Abs(applied))}";
        RebuildRundownRows();
        BumpConfig();
    }

    /// <summary>"Reach schedule": re-time the live item (elastic) so it lands on its planned end.</summary>
    private void CatchUp()
    {
        if (!_rundownActive || _currentItem < 0 || _projection.Items.Length <= _currentItem) { StatusLine = "Catch up needs a running-order item"; return; }
        var plannedEnd = _projection.Items[_currentItem].PlannedStartUtc.AddSeconds(Settings.Rundown.Items[_currentItem].EffectiveSeconds);
        if (plannedEnd <= DateTime.UtcNow.AddSeconds(30)) { StatusLine = "Too late to catch up within this item — trim later items instead"; return; }
        _engine.SetEndAt(plannedEnd.ToLocalTime(), realTime: false);
        StatusLine = $"Catching up: this item now lands on its planned end, {Fmt(ShowClock.ToShow(plannedEnd), seconds: false)} (elastic)";
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _currentItem, "Catch up to planned end");
    }

    private void StartPreview()
    {
        var total = _engine.BaseDuration > TimeSpan.Zero ? _engine.BaseDuration : TimeSpan.FromMinutes(10);
        // Start just above the ladder's first step so the briefing shows every cue.
        var first = CurrentLadder().Steps.Select(s => s.AtSeconds).DefaultIfEmpty(0).Max();
        if (first > 0 && total.TotalSeconds > first + 60) total = TimeSpan.FromSeconds(first + 60);
        _previewTotal = total;
        _previewStartUtc = DateTime.UtcNow;
        PreviewActive = true;
        _journal.Add(DateTime.UtcNow, JournalKind.Preview, -1, "start");
        StatusLine = $"Briefing preview: {TimerTick.FormatSeconds((long)total.TotalSeconds)} of cues at 10× on every output";
    }

    private void StopPreview()
    {
        if (_previewStartUtc is null) return;
        _previewStartUtc = null;
        PreviewActive = false;
        StatusLine = "Preview finished — outputs back to the live timer";
        MarkDirty();
    }

    private void SetQuestion(int id, string action)
    {
        var now = DateTime.UtcNow;
        switch (action)
        {
            case "next": StatusLine = _qa.Next(now) is { } q ? $"Q&A live: {q.Text}" : "Q&A queue is empty"; break;
            case "clear": _qa.Clear(); break;
            default:
                var state = action switch
                {
                    "approve" => QuestionState.Approved, "star" => QuestionState.Starred, "live" => QuestionState.Live,
                    "answered" => QuestionState.Answered, "dismiss" => QuestionState.Dismissed, _ => QuestionState.New
                };
                _qa.Set(id, state, now);
                break;
        }
    }

    private void ToggleRehearsal(bool on)
    {
        var now = DateTime.UtcNow;
        if (on)
        {
            if (CurrentDeck() is not { } deck) { StatusLine = "Start the PowerPoint slideshow first (here or on a presenter PC), then press Rehearse"; return; }
            _rehearsal.Begin(deck.Presentation, deck.Count, now);
            Rehearsing = true;
            StatusLine = $"Rehearsing \"{deck.Presentation}\" — Stagewatch is timing every slide";
        }
        else
        {
            var name = _rehearsal.Presentation;
            var budgets = _rehearsal.Finish(now);
            Rehearsing = false;
            if (budgets is null) { StatusLine = "Rehearsal stopped — nothing captured"; return; }
            _rehearsals.Save(name, budgets);
            StatusLine = $"Rehearsal saved for \"{name}\": {TimerTick.FormatSeconds((long)budgets.Sum())} over {budgets.Count(b => b > 0)} slides";
        }
    }

    private void ToggleListening(bool on)
    {
        if (on && !Gate(FeatureGate.AllowsSpeechPace(_license.Tier), "Speech pace tracking", FeatureTier.Production)) return;
        EnsureSpeech();
        if (_speech is null) return;
        if (on)
        {
            var path = Settings.Integrations.ScriptPath;
            if (path.Length > 0)
            {
                try { _speech.SetScript(File.ReadAllText(path)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusLine = "Couldn't read the script: " + ex.Message; return; }
            }
            else if (CurrentDeck() is { } deck) _speech.SetScript(deck.Script);
            Listening = _speech.Start();
            StatusLine = Listening ? "Listening: tracking the speaker against the script (audio never leaves this PC)" : _speech.Status;
        }
        else
        {
            _speech.Stop();
            Listening = false;
            StatusLine = "Stopped listening";
        }
    }

    // ================================================================== report

    public ShowReport BuildReport()
        => ShowReport.Build(ShowName, Settings.Rundown.Items, _journal.Entries, ScheduleAnchor(), ShowClock, DateTime.UtcNow);

    /// <summary>Write the planned-vs-actual report next to the shows. Returns the path, or null when nothing ran.</summary>
    public string? WriteReport(string format, bool open)
    {
        if (Settings.Rundown.Items.Count == 0 || !ReportAllowed(format)) return null;
        var report = BuildReport();
        var folder = Path.Combine(DefaultShowFolder, "Reports");
        var safe = string.Concat(ShowName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var path = Path.Combine(folder, $"{safe} {DateTime.Now:yyyy-MM-dd HHmm}.{format}");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, format == "csv" ? report.ToCsv(ShowClock) : report.ToHtml(ShowClock), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusLine = "Couldn't save the report: " + ex.Message;
            return null;
        }
        if (open)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }
        return path;
    }

    /// <summary>CSV is Pro; the full printable HTML report is Production.</summary>
    private bool ReportAllowed(string format) => format == "csv"
        ? FeatureGate.AllowsCsvReport(_license.Tier)
        : FeatureGate.AllowsFullReport(_license.Tier);

    /// <summary>IShowControl: the MCP endpoint answers only when enabled and licensed (Production).</summary>
    public bool McpAllowed => Settings.Integrations.Mcp && FeatureGate.AllowsMcp(_license.Tier);

    public string ReportText(string format) => format == "csv" ? BuildReport().ToCsv(ShowClock) : BuildReport().ToHtml(ShowClock);

    /// <summary>IShowControl: the report for remotes (web / MCP threads). Built on the owner thread, never racing the journal.</summary>
    public string? Report(string format)
    {
        if (Settings.Rundown.Items.Count == 0 || !ReportAllowed(format == "csv" ? "csv" : "html")) return null;
        // Called from network threads. Never from the UI thread (that would wait on itself).
        if (_ui.CheckAccess()) return ReportText(format == "csv" ? "csv" : "html");
        try
        {
            var op = _ui.InvokeAsync(() => ReportText(format == "csv" ? "csv" : "html"), System.Windows.Threading.DispatcherPriority.Background);
            return op.Task.Wait(TimeSpan.FromSeconds(3)) ? op.Result : null;
        }
        catch (Exception) { return null; }
    }

    // ================================================================== UI commands

    [RelayCommand] private void TogglePreview() { RecordUi("preview"); ExecuteNow(new ShowCommand(ShowCommandKind.Preview, Number: -1)); }
    [RelayCommand] private void CatchUpNow() { RecordUi("catchup"); ExecuteNow(new ShowCommand(ShowCommandKind.CatchUp)); }
    [RelayCommand] private void ToggleRehearse() { RecordUi("rehearse"); ExecuteNow(new ShowCommand(ShowCommandKind.Rehearse, Number: -1)); }
    [RelayCommand] private void ToggleListen() { RecordUi("listen"); ExecuteNow(new ShowCommand(ShowCommandKind.Listen, Number: -1)); }
    [RelayCommand] private void OpenReport()
    {
        if (!Gate(ReportAllowed("html"), "The full show report", FeatureTier.Production)) return;
        if (WriteReport("html", open: true) is { } p) StatusLine = "Report: " + p; else StatusLine = "Add a running order first";
    }
    [RelayCommand] private void ExportReportCsv()
    {
        if (!Gate(ReportAllowed("csv"), "The CSV report", FeatureTier.Pro)) return;
        if (WriteReport("csv", open: true) is { } p) StatusLine = "Report: " + p;
    }
    [RelayCommand] private void ApplyAdvice(Advice a) { if (a.Command is { } cmd) { RecordUi(cmd); RunCommandLine(cmd); } }
    [RelayCommand] private void DepartmentStandby(string dept) { RecordUi($"signal standby {Q(dept)}"); ExecuteNow(new ShowCommand(ShowCommandKind.Signal, Number: (int)SignalKind.Standby, Text: dept + "\u0001", Flag: true)); }
    [RelayCommand] private void DepartmentGo(string dept) { RecordUi($"signal go {Q(dept)}"); ExecuteNow(new ShowCommand(ShowCommandKind.Signal, Number: (int)SignalKind.Go, Text: dept + "\u0001", Flag: true)); }
    [RelayCommand] private void ClearSignal(SignalView s) => ExecuteNow(new ShowCommand(ShowCommandKind.SignalClear, Number: s.Id));
    [RelayCommand] private void QuestionNext() { RecordUi("q next"); ExecuteNow(new ShowCommand(ShowCommandKind.QuestionSet, Text: "next")); }
    [RelayCommand] private void QuestionApprove(QuestionView q) => ExecuteNow(new ShowCommand(ShowCommandKind.QuestionSet, Number: q.Id, Text: "approve"));
    [RelayCommand] private void QuestionStar(QuestionView q) => ExecuteNow(new ShowCommand(ShowCommandKind.QuestionSet, Number: q.Id, Text: "star"));
    [RelayCommand] private void QuestionDismiss(QuestionView q) => ExecuteNow(new ShowCommand(ShowCommandKind.QuestionSet, Number: q.Id, Text: "dismiss"));

    /// <summary>Call the next speaker (green room) — the next item's speaker, or its title.</summary>
    [RelayCommand]
    private void CallNextSpeaker()
    {
        var items = Settings.Rundown.Items;
        var n = _currentItem + 1;
        if (n < 0 || n >= items.Count) { StatusLine = "No next item to call"; return; }
        var who = string.IsNullOrWhiteSpace(items[n].Speaker) ? items[n].Title : items[n].Speaker!;
        RecordUi($"signal call backstage {Q(who + " to stage")}");
        ExecuteNow(new ShowCommand(ShowCommandKind.Signal, Number: (int)SignalKind.Call, Text: "backstage\u0001" + who + " to stage", Flag: true));
    }

    /// <summary>Discreet nudge to the speaker's phone/watch (haptic page).</summary>
    [RelayCommand]
    private void NudgeSpeaker(string text)
    {
        RecordUi($"signal nudge speaker {Q(text)}");
        ExecuteNow(new ShowCommand(ShowCommandKind.Signal, Number: (int)SignalKind.Nudge, Text: "speaker\u0001" + text, Flag: true));
    }

    [RelayCommand]
    private void AddDelay(string seconds)
    {
        if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) return;
        RecordUi($"delay {(long)Math.Max(0, _pendingDelay.TotalSeconds + s)}");
        ExecuteNow(new ShowCommand(ShowCommandKind.Delay, Number: Math.Max(0, _pendingDelay.TotalSeconds + s)));
    }

    partial void OnActiveLadderIdChanged(string value)
    {
        _store.Update(s => s.Display.CueLadderId = value ?? "");
        StatusLine = "Cue ladder: " + Settings.Display.LadderFor(value).Name;
        BumpConfig();
    }

    partial void OnSpeakerFormatChanged(SpeakerTimerFormat value)
    {
        _store.Update(s => s.Display.SpeakerFormat = value);
        MarkDirty();
        Publish();
    }

    partial void OnAckRequestedChanged(bool value) => _store.Update(s => s.Signals.AckByDefault = value);

    /// <summary>Run a command line from a trigger, the assistant or the UI through the one grammar.</summary>
    public void RunCommandLine(string line)
    {
        var tokens = CloudAdvisor.Tokenize(line);
        if (tokens.Count == 0) return;
        if (ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out var error)) Execute(cmd);
        else StatusLine = "Command rejected: " + error;
    }
}
