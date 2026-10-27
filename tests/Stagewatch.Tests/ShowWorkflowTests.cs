using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Show;

/// <summary>
/// Whole show workflows on the headless <see cref="ShowController"/>, driven by a manual clock: no WPF, no sleeping,
/// the same result every run. Each test is a transition the operator makes live — switching running orders, nudging
/// during Roll, a loop pass, undo, recovery, editing the running order mid-show, a crash restore.
/// </summary>
static class ShowWorkflowTests
{
    public static void Run(Action<bool, string> Check)
    {
        SwitchingRunningOrders(Check);
        UndoBelongsToItsRunningOrder(Check);
        NudgeDuringRollHandsOver(Check);
        LoopPassesAreSeparateRuns(Check);
        AutoAdvanceOnExpiry(Check);
        RecoveryIsOneUndoableStep(Check);
        EditingMidShow(Check);
        RestoreOnlyIntoTheSameRunningOrder(Check);
        DelayAndDeptNotesUndo(Check);
        FramesFromTheShow(Check);
        ReportsFromTheJournal(Check);
        WhatTheAiSees(Check);
        TrimReachesTheCountdown(Check);
        RestoreFollowsIdentity(Check);
        RefusalsAreExplicit(Check);
    }

    // ------------------------------------------------------------------ harness

    /// <summary>A show on a manual clock: settings in a temp file, an engine and a controller sharing one time source.</summary>
    sealed class Rig : IDisposable
    {
        public readonly ManualTime Time;
        public readonly SettingsStore Store;
        public readonly TimerEngine Engine;
        public readonly FakeHost Host = new();
        public readonly ShowController Show;
        private readonly ScratchFile _file;

        public Rig(DateTime startUtc, Action<AppSettings> setup)
        {
            Time = new ManualTime(startUtc);
            _file = Scratch.File("show", ".json");
            Store = new SettingsStore(_file.Path);
            Store.Update(setup);
            Store.Settings.Rundown.EnsureIds();
            foreach (var o in Store.Settings.OtherRundowns) o.Rundown.EnsureIds();
            Engine = new TimerEngine(Time);
            Show = new ShowController(Store, Engine, Host, Time);
            Engine.Tick += t => Show.OnTick(t);   // same thread: the manual clock fires timers inside Advance
        }

        /// <summary>Let time pass in 50 ms steps (engine ticks fire on the way), with Roll's housekeeping every 250 ms.</summary>
        public void Run(TimeSpan span)
        {
            var end = Time.GetUtcNow() + span;
            var sinceHousekeeping = TimeSpan.Zero;
            while (Time.GetUtcNow() < end)
            {
                var step = TimeSpan.FromMilliseconds(50);
                Time.Advance(step);
                sinceHousekeeping += step;
                if (sinceHousekeeping >= TimeSpan.FromMilliseconds(250)) { sinceHousekeeping = TimeSpan.Zero; Show.Housekeeping(); }
            }
        }

        public AppSettings S => Store.Settings;

        public void Dispose()
        {
            Engine.Dispose();
            Store.Dispose();
            _file.Dispose();
        }
    }

    sealed class FakeHost : IShowHost
    {
        public readonly List<string> Statuses = new();
        public readonly List<(int Index, string Title)> Started = new(), Ended = new(), Loaded = new();
        public bool Allowed = true;
        public int Changes, RundownChanges, ConfigChanges, NotesChanges;
        public string LastStatus => Statuses.Count > 0 ? Statuses[^1] : "";
        public bool Allow(ShowFeature feature) { if (!Allowed) Statuses.Add($"{feature} not licensed"); return Allowed; }
        public void Status(string line) => Statuses.Add(line);
        public void ItemLoaded(int index, RundownItem item) => Loaded.Add((index, item.Title));
        public void ItemStarted(int index, RundownItem item, bool firstOfShow) => Started.Add((index, item.Title));
        public void ItemEnded(int index, RundownItem item, TimeSpan ran) => Ended.Add((index, item.Title));
        public void RundownChanged() => RundownChanges++;
        public void ConfigChanged() => ConfigChanges++;
        public void DeptNotesChanged() => NotesChanges++;
        public void ControlsChanged() { }
        public readonly List<string> Refusals = new();
        public void Refused(string why) { Refusals.Add(why); Statuses.Add(why); }
        public void Changed(bool publishNow) => Changes++;
    }

    /// <summary>A <see cref="TimeProvider"/> that only moves when told to; timers fire inside <see cref="Advance"/>.</summary>
    internal sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now;
        private long _ticks;
        private readonly List<ManualTimer> _timers = new();

        public ManualTime(DateTime startUtc) => _now = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new ManualTimer(this, callback, state);
            lock (_timers) _timers.Add(t);
            t.Change(dueTime, period);
            return t;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            _ticks += by.Ticks;
            List<ManualTimer> due;
            lock (_timers) due = _timers.Where(t => t.DueAt is { } d && d <= _ticks).ToList();
            foreach (var t in due) t.Fire(_ticks);
        }

        internal void Remove(ManualTimer t) { lock (_timers) _timers.Remove(t); }

        /// <summary>The timers made on this clock and not yet disposed: one is armed once its owner has started waiting on it.</summary>
        public int Timers { get { lock (_timers) return _timers.Count; } }

        internal sealed class ManualTimer(ManualTime owner, TimerCallback callback, object? state) : ITimer
        {
            public long? DueAt;
            private long _period;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._ticks + dueTime.Ticks;
                _period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }

            public void Fire(long now)
            {
                DueAt = _period > 0 ? now + _period : null;
                callback(state);
            }

            public void Dispose() => owner.Remove(this);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    static readonly DateTime T0 = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    static string LocalHhMm(DateTime utc) => ShowClock.Local.ToShow(utc).ToString("HH:mm");

    // ------------------------------------------------------------------ workflows

    static void SwitchingRunningOrders(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.RundownName = "Day 1";
            s.Rundown = new Rundown { Items = { new RundownItem { Title = "Opening", DurationSeconds = 300 } } };
            s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = { new RundownItem { Title = "New talk", DurationSeconds = 600 } } } });
        });
        r.Show.GoTo(0, start: true);
        r.Run(TimeSpan.FromSeconds(120));
        Check(r.Engine.State == TimerState.Running && r.Show.ItemStarted, "workflow: Day 1's item is running");
        Check(r.Show.SwitchRundown("Day 2"), "workflow: switch to Day 2");
        Check(r.Engine.State == TimerState.Idle && r.Show.ActiveTitle == "New talk" && r.Show.CurrentItem == 0 && !r.Show.ItemStarted,
              $"workflow: the old timer stops and Day 2's first item is loaded, not started ({r.Engine.State}, \"{r.Show.ActiveTitle}\")");
        Check(r.Host.Ended.SequenceEqual(new[] { (0, "Opening") }), "workflow: Day 1's item ended when Day 2 went live");
        var day1 = r.S.OtherRundowns.Single(o => o.Name == "Day 1").Rundown;
        var report1 = ShowReport.Build("Show", day1, r.Show.Journal.Entries, null, ShowClock.Local, r.Time.GetUtcNow().UtcDateTime);
        var report2 = ShowReport.Build("Show", r.S.Rundown, r.Show.Journal.Entries, null, ShowClock.Local, r.Time.GetUtcNow().UtcDateTime);
        Check(report1.Rows[0].ActualSeconds == 120 && report2.Rows[0].ActualSeconds is null,
              $"workflow: Day 1's 120 s stays with Day 1 ({report1.Rows[0].ActualSeconds}; Day 2: {report2.Rows[0].ActualSeconds?.ToString() ?? "none"})");
        Check(r.Show.SwitchRundown("Day 1") && r.S.RundownName == "Day 1" && r.Show.CurrentItem == 0, "workflow: and back again");
        Check(!r.Show.SwitchRundown("Day 9") && r.Host.LastStatus.Contains("No running order"), "workflow: an unknown running order is refused");
    }

    static void UndoBelongsToItsRunningOrder(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.RundownName = "Day 1";
            s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 300 } } };
            s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = { new RundownItem { Title = "B", DurationSeconds = 300 } } } });
        });
        r.Show.Trim(0, -60);
        r.Show.SwitchRundown("Day 2");
        r.Show.Undo();
        Check(r.S.Rundown.Items[0].EffectiveSeconds == 300 && r.Show.UndoLabel is null, "workflow: undo in Day 2 has nothing of Day 1's to undo");
        r.Show.SwitchRundown("Day 1");
        Check(r.Show.UndoLabel is { } u && u.Contains("Cut"), $"workflow: Day 1's history comes back with it ({r.Show.UndoLabel})");
        r.Show.Undo();
        Check(r.S.Rundown.Items[0].AdjustSeconds == 0, "workflow: undo in Day 1 reverses Day 1's trim");
        r.Show.Redo();
        Check(r.S.Rundown.Items[0].AdjustSeconds == -60, "workflow: redo");
    }

    static void NudgeDuringRollHandsOver(Action<bool, string> Check)
    {
        // A show planned to start at the top of the minute, a minute ago.
        var start = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        using var r = new Rig(start.AddSeconds(70), s =>
        {
            s.Rundown = new Rundown
            {
                ScheduledStart = LocalHhMm(start),
                Items = { new RundownItem { Title = "Walk-in", DurationSeconds = 60 }, new RundownItem { Title = "Keynote", DurationSeconds = 1800 } },
            };
        });
        r.Show.SetRoll(true);
        r.Run(TimeSpan.FromSeconds(1));
        Check(r.Show.RollMode && r.Show.CurrentItem == 1 && r.Engine.State == TimerState.Running && r.Engine.Current.TotalSeconds is > 1780 and < 1791,
              $"workflow: Roll runs the item whose window is now ({r.Show.CurrentItem}, {r.Engine.Current.TotalSeconds:0} s left)");
        r.Show.Nudge(TimeSpan.FromSeconds(60));
        r.Run(TimeSpan.FromSeconds(1));
        Check(!r.Show.RollMode && r.Host.Statuses.Contains("Roll off — you took over"), "workflow: adding a minute hands over from Roll");
        Check(r.Engine.State == TimerState.Running && r.Engine.Current.TotalSeconds > 1760, "workflow: the operator's extra minute stands");
    }

    static void LoopPassesAreSeparateRuns(Action<bool, string> Check)
    {
        var start = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
        using var r = new Rig(start.AddSeconds(2), s =>
        {
            s.Rundown = new Rundown { ScheduledStart = LocalHhMm(start), Loop = -1, Items = { new RundownItem { Title = "Loop clip", DurationSeconds = 10 } } };
        });
        r.Show.SetRoll(true);
        r.Run(TimeSpan.FromSeconds(1));
        var firstRun = r.Show.Execution;
        Check(r.Engine.State == TimerState.Running && r.Engine.Current.TotalSeconds is > 6 and < 8.5, $"workflow: a one-item loop's first pass runs ({r.Engine.Current.TotalSeconds:0.0} s left)");
        r.Run(TimeSpan.FromSeconds(10));
        Check(r.Show.RollMode && r.Engine.State == TimerState.Running && r.Show.Execution != firstRun && r.Engine.Current.TotalSeconds is > 6 and < 9.5,
              $"workflow: the next pass restarts it as a new run ({r.Engine.Current.TotalSeconds:0.0} s left)");
        r.Run(TimeSpan.FromSeconds(10));
        r.Show.SetRoll(false);
        r.Engine.Pause();
        r.Show.GoTo(0, start: false);   // ends the run in progress
        var report = ShowReport.Build("Show", r.S.Rundown, r.Show.Journal.Entries, null, ShowClock.Local, r.Time.GetUtcNow().UtcDateTime);
        Check(report.Rows.Count >= 3 && report.Rows.All(x => x.ActualSeconds is null or <= 10),
              $"workflow: each pass is its own row in the report ({string.Join(", ", report.Rows.Select(x => x.Title + "=" + x.ActualSeconds))})");
    }

    static void AutoAdvanceOnExpiry(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.Rundown = new Rundown
            {
                Loop = 1,
                Items =
                {
                    new RundownItem { Title = "Sting", DurationSeconds = 5, AutoAdvance = true },
                    new RundownItem { Title = "Talk", DurationSeconds = 5 },
                },
            };
        });
        r.Show.GoTo(0, start: true);
        r.Run(TimeSpan.FromSeconds(5.5));
        Check(r.Show.CurrentItem == 1 && r.Engine.State == TimerState.Running, $"workflow: a linked item rolls on by itself at zero ({r.Show.CurrentItem}, {r.Engine.State})");
        r.Run(TimeSpan.FromSeconds(5.5));
        Check(r.Show.CurrentItem == 0 && r.Show.LoopPass == 1 && r.Engine.State == TimerState.Running, $"workflow: a looping running order starts again at the end ({r.Show.CurrentItem}, pass {r.Show.LoopPass})");
        r.Run(TimeSpan.FromSeconds(11));
        Check(r.Show.CurrentItem == 1 && r.Engine.State == TimerState.Expired, $"workflow: and stops after its last pass ({r.Show.CurrentItem}, {r.Engine.State})");
        Check(r.Host.Started.Count == 4 && r.Host.Ended.Count == 3, $"workflow: every run started and ended once ({r.Host.Started.Count} / {r.Host.Ended.Count})");
    }

    /// <summary>The frame builder: the show's part of every frame, built headless from the controller and settings.</summary>
    static void FramesFromTheShow(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.Rundown = new Rundown
            {
                TimeZoneId = "UTC",
                Items =
                {
                    new RundownItem { Title = "Welcome", Speaker = "Ana", DurationSeconds = 60, HardOut = "09:00:30" },
                    new RundownItem { Title = "Panel", DurationSeconds = 600 },
                    new RundownItem { Title = "Close", DurationSeconds = 300 },
                },
            };
            s.MessageState.Text = "Next up: {NEXT}, {SPEAKER}";
            s.HideAtZero = true;
        });
        TimerTick last = default;
        r.Engine.Tick += t => last = t;
        r.Show.GoTo(0, start: true);
        r.Run(TimeSpan.FromSeconds(20));

        FrameInput Input(bool preview = false) => new(r.Time.GetUtcNow().UtcDateTime, r.Time.GetUtcNow().UtcDateTime, last, last, preview,
            Stretching: false, EndAt: null, EndsAtWallClock: r.Time.GetUtcNow().UtcDateTime + last.Remaining, ManualTimerFlash: false,
            SpeakerTimerFormat.Standard, "", r.S, r.Show);
        var ambient = new ShowSnapshot { Blackout = true, HoldingId = "img1", QaQueued = 3 };
        var f = Input();
        var p = FrameBuilder.Project(f);
        var frame = FrameBuilder.Build(f, p, null, EquatableArray<Advice>.Empty, ambient);

        Check(frame.Title == "Welcome — Ana" && frame.NextTitle == "Panel" && frame.ItemIndex == 0 && frame.ItemCount == 3 && frame.Seconds == 40,
              $"frames: the live item, what's next and the time left ({frame.Title}, {frame.NextTitle}, {frame.Seconds})");
        Check(frame.Message == "Next up: Panel, Ana", $"frames: message placeholders are filled from the show ({frame.Message})");
        Check(frame.Blackout && frame.HoldingId == "img1" && frame.QaQueued == 3, "frames: the host's sensors and outputs are kept");
        Check(frame.Timings.Count == 3 && frame.Timings[1].Planned == "09:01" && frame.ScheduleText.Contains("show ends"),
              $"frames: running-order timings and the schedule line ({(frame.Timings.Count == 3 ? frame.Timings[1].Planned : "-")}, {frame.ScheduleText})");
        Check(frame.HardOutMarginSeconds < 0 && frame.HardOutText.Contains("OVER"), $"frames: a curfew the item can't make shows as over ({frame.HardOutText})");
        Check(frame.EndTimeText.StartsWith("ends ") && frame.TimeOfDay.Length > 0, $"frames: the end time and time of day ({frame.EndTimeText})");
        Check(FrameBuilder.Build(f, p, null, EquatableArray<Advice>.Empty, ambient) == frame, "frames: the same show gives the same frame (a pure function)");

        var withForecast = FrameBuilder.Build(f, p, new ForecastResult(T0.AddMinutes(20), T0.AddMinutes(22), T0.AddMinutes(25), 0.4), EquatableArray<Advice>.Empty, ambient);
        Check(withForecast.ForecastText.StartsWith("Likely end 09:20") && withForecast.ForecastText.Contains("curfew risk"), $"frames: the forecast line ({withForecast.ForecastText})");

        var preview = FrameBuilder.Build(Input(preview: true), p, null, EquatableArray<Advice>.Empty, ambient);
        Check(preview.Title.StartsWith("PREVIEW") && preview.Preview && preview.EndTimeText == "" && preview.FinishBy == "",
              "frames: a briefing preview is labelled and makes no promises about the end");

        r.Run(TimeSpan.FromSeconds(45));   // into overtime
        var over = FrameBuilder.Build(Input(), FrameBuilder.Project(Input()), null, EquatableArray<Advice>.Empty, ambient);
        Check(over.SpeakerText == " " && over.Text.Length > 1 && over.Phase == TimerPhase.Overtime,
              $"frames: hide at zero blanks the speaker in overtime, the operator still counts ({over.Text}, {over.Phase})");
    }

    /// <summary>Reports: built from the journal for the live running order, gated by tier, saved under a safe name.</summary>
    static void ReportsFromTheJournal(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.Rundown = new Rundown { TimeZoneId = "UTC", Items = { new RundownItem { Title = "Keynote", DurationSeconds = 60 }, new RundownItem { Title = "Q&A", DurationSeconds = 60 } } };
        });
        r.Show.Journal = new ShowJournal(null);
        r.Show.GoTo(0, start: true);
        r.Run(TimeSpan.FromSeconds(75));
        r.Show.Next();
        r.Run(TimeSpan.FromSeconds(10));
        var report = ShowReports.Build("Gala", r.S, r.Show, r.Show.Journal!.Entries, r.Time.GetUtcNow().UtcDateTime);
        var csv = ShowReports.Render(report, "csv", ShowClock.For("UTC"));
        var html = ShowReports.Render(report, "anything", ShowClock.For("UTC"));
        Check(csv.Contains("Keynote") && csv.Contains("Q&A") && !csv.Contains("<html", StringComparison.OrdinalIgnoreCase) && html.Contains("<", StringComparison.Ordinal),
              "reports: built from the journal; unknown formats fall back to HTML");
        Check(ShowReports.Allowed("csv", FeatureTier.Pro) && !ShowReports.Allowed("html", FeatureTier.Pro) && ShowReports.Allowed("html", FeatureTier.Production)
              && !ShowReports.Allowed("csv", FeatureTier.Standard), "reports: CSV is Pro, the full report is Production");
        if (Bench.OnWindows("reports: safe file names (Windows file-name rules and paths)"))
        {
            var path = ShowReports.PathFor(@"C:\Shows", "Gala: \"Night\"", new DateTime(2026, 10, 10, 14, 32, 0), "csv");
            Check(path == @"C:\Shows\Reports\Gala_ _Night_ 2026-10-10 1432.csv", $"reports: saved under a safe file name ({path})");
            Check(ShowReports.PathFor(@"C:\Shows", "  ", DateTime.Now, "html").Contains(@"\Reports\Show "), "reports: an unnamed show still gets a file name");
        }
    }

    /// <summary>The AI's view of the show: the copilot's context, the assistant's summary and the journal a recap is drafted from.</summary>
    static void WhatTheAiSees(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.RundownName = "Day 1";
            s.Rundown = new Rundown { TimeZoneId = "UTC", Items = { new RundownItem { Title = "Keynote", Speaker = "Ana", DurationSeconds = 600 }, new RundownItem { Title = "Panel", DurationSeconds = 1200 } } };
            s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = { new RundownItem { Title = "Workshop", DurationSeconds = 900 } } } });
        });
        r.Show.Journal = new ShowJournal(null);
        r.Show.GoTo(0, start: true);
        r.Run(TimeSpan.FromSeconds(30));
        for (int i = 0; i < 40; i++) r.Show.Journal!.Add(r.Time.GetUtcNow().UtcDateTime, JournalKind.Note, 0, "note " + i);
        var clock = ShowClock.For("UTC");
        var now = r.Time.GetUtcNow().UtcDateTime;

        var facts = new CopilotFacts(now, clock, "{\"title\":\"Keynote\"}"u8.ToArray(), "{\"presets\":[]}"u8.ToArray(),
            new[] { new ScreenFact("s1", "Lectern", "/viewer", "iPad", false) }, new[] { new ScreenPreset { Name = "Foyer", Path = "/clock" } },
            new[] { new IntegrationFact("vMix", "Error", "connection refused") }, 12, OverrunModel.Learn(Array.Empty<HistoryRecord>()),
            r.S.Rundown.Items, r.Show.Journal!.Entries);
        using (var doc = JsonDocument.Parse(AiContext.Copilot(facts)))
        {
            var root = doc.RootElement;
            var recent = root.GetProperty("recent");
            Check(root.GetProperty("now").GetString() == clock.ToShow(now).ToString("yyyy-MM-dd HH:mm:ss") && root.GetProperty("show").GetProperty("title").GetString() == "Keynote"
                  && root.GetProperty("config").TryGetProperty("presets", out _), "ai: the copilot sees the time, the operator frame and the config");
            Check(root.GetProperty("screens")[0].GetProperty("name").GetString() == "Lectern" && root.GetProperty("screen_setups")[0].GetProperty("page").GetString() == "/clock"
                  && root.GetProperty("integrations")[0].GetProperty("status").GetString() == "connection refused", "ai: screens, setups and why a link is down");
            var hist = root.GetProperty("history");
            Check(hist.GetProperty("items_learned_from").GetInt32() == 12 && hist.GetProperty("items").GetArrayLength() == 2
                  && hist.GetProperty("items")[0].GetProperty("speaker").GetString() == "Ana", "ai: each item's likely overrun from this operator's history");
            Check(recent.GetArrayLength() == AiContext.RecentLines && recent[AiContext.RecentLines - 1].GetString()!.EndsWith("note 39"), $"ai: only the latest {AiContext.RecentLines} journal lines");
        }

        var p = ScheduleEngine.Project(new ScheduleEngine.Input(r.S.Rundown.Items, r.Show.ScheduleAnchor()!.Value, 0, true, TimeSpan.FromSeconds(570), r.Show.Actuals, now, clock, TimeSpan.Zero));
        using (var doc = JsonDocument.Parse(AiContext.Assistant(r.S.Rundown.Items, p, clock, now, 0, TimeSpan.FromSeconds(570), TimeSpan.FromSeconds(60), 4)))
        {
            var root = doc.RootElement;
            Check(root.GetProperty("live_item").GetInt32() == 1 && root.GetProperty("live_remaining_s").GetInt64() == 570 && root.GetProperty("pending_delay_s").GetInt64() == 60
                  && root.GetProperty("qa_queued").GetInt32() == 4 && root.GetProperty("items")[1].GetProperty("planned_start").GetString() == "09:10",
                  "ai: the assistant sees the live item, time left, delay, questions and the plan");
        }
        using (var doc = JsonDocument.Parse(AiContext.Assistant(r.S.Rundown.Items, p, clock, now, -1, TimeSpan.Zero, TimeSpan.Zero, 0)))
            Check(doc.RootElement.GetProperty("live_item").GetInt32() == 0, "ai: nothing live is item 0");

        // A recap names items by identity, even when the entry came from another running order.
        var day2 = r.S.OtherRundowns[0].Rundown.Items[0];
        r.Show.Journal!.Add(new JournalEntry(now, JournalKind.ItemEnd, 0, "Workshop", 0) { RundownId = r.S.OtherRundowns[0].Rundown.Id, ItemId = day2.Id });
        using (var doc = JsonDocument.Parse(AiContext.SummaryInput(r.S, r.Show.Journal!.Entries, clock)))
        {
            var journal = doc.RootElement.GetProperty("journal");
            var items = journal.EnumerateArray().Select(e => e.GetProperty("item").GetString()).ToList();
            Check(items.Contains("Day 2 1. Workshop") && items.Any(x => x!.StartsWith("Day 1 1. Keynote")),
                  $"ai: recap entries name their item by identity across running orders ({string.Join(" | ", items.Distinct())})");
            Check(doc.RootElement.GetProperty("running_order").GetArrayLength() == 2, "ai: the recap includes the live running order's plan");
        }
    }

    static void RecoveryIsOneUndoableStep(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.Rundown = new Rundown { Items = { new RundownItem { Title = "Live", DurationSeconds = 1800 }, new RundownItem { Title = "Panel", DurationSeconds = 2400 }, new RundownItem { Title = "Close", DurationSeconds = 900 } } };
        });
        r.Show.GoTo(0, start: true);
        var plan = Recovery.Make(r.S.Rundown.Items, 1, 2, 300, RecoverMethod.Proportional);
        r.Show.ApplyTrims(plan.Trims, "Recovered 5:00");
        Check(r.S.Rundown.Items.Sum(i => i.AdjustSeconds) == -plan.Applied && r.Host.ConfigChanges >= 1, "workflow: a recovery applies (and remotes refetch)");
        r.Show.Undo();
        Check(r.S.Rundown.Items.Sum(i => i.AdjustSeconds) == 0 && r.Show.UndoLabel is null, "workflow: one undo puts the whole recovery back");
        r.Host.Allowed = false;
        r.Show.Trim(1, -60);
        Check(r.S.Rundown.Items[1].AdjustSeconds == 0 && r.Host.LastStatus.Contains("not licensed"), "workflow: an unlicensed trim does nothing and says why");
    }

    static void EditingMidShow(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 300 }, new RundownItem { Title = "B", DurationSeconds = 300 }, new RundownItem { Title = "C", DurationSeconds = 300 } } };
        });
        r.Show.GoTo(2, start: true);
        r.Show.Trim(1, -60);
        // The editor saves: B is deleted (as a sheet re-import might), C moves up.
        r.Store.Update(s => s.Rundown.Items.RemoveAt(1));
        r.Show.RundownEdited();
        Check(r.Show.CurrentItem == 1 && r.S.Rundown.Items[r.Show.CurrentItem].Title == "C" && r.Show.RundownActive,
              $"workflow: deleting an item above the live one keeps the cursor on the live item ({r.Show.CurrentItem})");
        Check(r.Show.Actuals.Count == 1 && r.Show.Actuals.ContainsKey(1), "workflow: the record of what ran moves with its item");
        // Moving an item above the live one: the cursor follows its item, not its old position.
        r.Store.Update(s => s.Rundown.Items.Insert(0, new RundownItem { Title = "Inserted", DurationSeconds = 60 }));
        r.Show.RundownEdited();
        Check(r.Show.CurrentItem == 2 && r.S.Rundown.Items[2].Title == "C", $"workflow: inserting above the live item moves the cursor with it ({r.Show.CurrentItem})");
        // Deleting the live item itself: nothing stays live.
        r.Store.Update(s => s.Rundown.Items.RemoveAt(2));
        r.Show.RundownEdited();
        Check(!r.Show.RundownActive, "workflow: deleting the live item leaves nothing live");
        Check(r.Show.UndoLabel is null, "workflow: the trim on a deleted item can't be undone onto whatever moved into its place");
        r.Store.Update(s => s.Rundown.Items.Clear());
        r.Show.RundownEdited();
        Check(r.Show.CurrentItem == -1 && !r.Show.RundownActive && r.Show.ScheduleAnchor() is null, "workflow: an emptied running order leaves nothing live");
    }

    static void RestoreOnlyIntoTheSameRunningOrder(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s =>
        {
            s.RundownName = "Day 1";
            s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 300 }, new RundownItem { Title = "B", DurationSeconds = 300 } } };
            s.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = { new RundownItem { Title = "X", DurationSeconds = 300 }, new RundownItem { Title = "Y", DurationSeconds = 300 } } } });
        });
        r.Show.GoTo(1, start: true);
        r.Run(TimeSpan.FromSeconds(30));
        var cursor = r.Show.Capture();
        Check(cursor.CurrentItem == 1 && cursor.RundownId == r.S.Rundown.Id && cursor.Execution == r.Show.Execution, "workflow: a checkpoint names its running order and run");

        using var same = new Rig(T0, s => { s.RundownName = "Day 1"; s.Rundown = r.S.Rundown.Clone(); });
        same.Show.Restore(cursor);
        Check(same.Show.CurrentItem == 1 && same.Show.ItemStarted && same.Show.Execution == cursor.Execution, "workflow: restored into the same running order");

        using var other = new Rig(T0, s => { s.RundownName = "Day 2"; s.Rundown = r.S.OtherRundowns[0].Rundown.Clone(); });
        other.Show.Restore(cursor);
        Check(other.Show.CurrentItem == -1 && !other.Show.RundownActive && other.Show.Actuals.Count == 0, "workflow: never restored into a different running order");
    }

    /// <summary>Review 2026-10-11: a cut on a coming item must reach its countdown when it's loaded.</summary>
    static void TrimReachesTheCountdown(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s => { s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 120 }, new RundownItem { Title = "B", DurationSeconds = 300 } } }; });
        r.Show.GoTo(0, start: true);
        r.Show.Trim(1, -60);
        Check(r.S.Rundown.Items[1].EffectiveSeconds == 240, "trim: the coming item is now 4 minutes");
        r.Show.GoTo(1, start: false);
        Check(Math.Abs(r.Engine.Capture().ValueSeconds - 240) < 0.5, $"trim: loading it loads the trimmed length ({r.Engine.Capture().ValueSeconds:0.0} s)");
        r.Show.Start();
        r.Run(TimeSpan.FromSeconds(10));
        Check(Math.Abs(r.Engine.Capture().ValueSeconds - 230) < 1, $"trim: and it counts down from there ({r.Engine.Capture().ValueSeconds:0.0} s)");
    }

    /// <summary>Review 2026-10-11: a checkpoint restores the same item, by identity, even after the order was edited.</summary>
    static void RestoreFollowsIdentity(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s => { s.RundownName = "Day 1"; s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 300 }, new RundownItem { Title = "B", DurationSeconds = 300 } } }; });
        r.Show.GoTo(0, start: true);
        r.Run(TimeSpan.FromSeconds(30));
        r.Show.GoTo(1, start: true);
        r.Run(TimeSpan.FromSeconds(30));
        var cursor = r.Show.Capture();
        Check(cursor.CurrentItemId == r.S.Rundown.Items[1].Id && cursor.ActualIds is { Count: 2 }, "restore: a checkpoint names the live item and the timed items by id");

        // The order is edited after the checkpoint: an item inserted at the top.
        var edited = r.S.Rundown.Clone();
        edited.Items.Insert(0, new RundownItem { Title = "Walk-in", DurationSeconds = 60 });
        edited.EnsureIds();
        using var after = new Rig(T0, s => { s.RundownName = "Day 1"; s.Rundown = edited; });
        after.Show.Restore(cursor);
        Check(after.Show.CurrentItem == 2 && after.Show.ActiveTitle == "B" && after.Show.ItemStarted, $"restore: B is live again at its new place ({after.Show.CurrentItem}, {after.Show.ActiveTitle})");
        Check(after.Show.Actuals.ContainsKey(1) && after.Show.Actuals.ContainsKey(2) && !after.Show.Actuals.ContainsKey(0),
              "restore: the recorded times follow A and B, not the new walk-in");

        // The live item was deleted since: nothing is live, and its times are gone.
        var shrunk = r.S.Rundown.Clone();
        shrunk.Items.RemoveAt(1);
        using var gone = new Rig(T0, s => { s.RundownName = "Day 1"; s.Rundown = shrunk; });
        gone.Show.Restore(cursor);
        Check(gone.Show.CurrentItem == -1 && !gone.Show.RundownActive && gone.Show.ActiveTitle == "" && gone.Show.Actuals.Keys.SequenceEqual(new[] { 0 }),
              "restore: a deleted live item isn't replaced by its neighbour");

        // A checkpoint from before identities still restores by position.
        var legacy = cursor with { CurrentItemId = null, ActualIds = null };
        using var old = new Rig(T0, s => { s.RundownName = "Day 1"; s.Rundown = r.S.Rundown.Clone(); });
        old.Show.Restore(legacy);
        Check(old.Show.CurrentItem == 1 && old.Show.Actuals.Count == 2, "restore: an older checkpoint still restores by position");
    }

    /// <summary>Review 2026-10-11: what the controller can't do is a refusal, not a status line that looks like success.</summary>
    static void RefusalsAreExplicit(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s => { s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 300 } } }; });
        r.Show.SwitchRundown("Nowhere");
        r.Show.GoTo(5, start: false);
        r.Show.Undo();
        r.Show.Trim(9, -60);
        Check(r.Host.Refusals.Count == 4 && r.Host.Refusals[0].Contains("Nowhere") && r.Host.Refusals[1].Contains("No item 6") && r.Host.Refusals[2].StartsWith("Nothing to undo"),
              $"refusals: a missing running order, a missing item, nothing to undo, a bad trim ({string.Join(" | ", r.Host.Refusals)})");
    }

    static void DelayAndDeptNotesUndo(Action<bool, string> Check)
    {
        using var r = new Rig(T0, s => { s.Rundown = new Rundown { Items = { new RundownItem { Title = "A", DurationSeconds = 300 }, new RundownItem { Title = "B", DurationSeconds = 300 } } }; });
        r.Show.GoTo(0, start: true);
        r.Show.SetDelay(120);
        r.Show.SetDeptNote(2, "Lighting", "Blue wash");
        Check(r.Show.PendingDelay.TotalSeconds == 120 && r.S.Rundown.Items[1].DeptNote("Lighting") == "Blue wash" && r.Host.NotesChanges == 1, "workflow: delay and a department note");
        r.Show.Undo();
        r.Show.Undo();
        Check(r.Show.PendingDelay == TimeSpan.Zero && r.S.Rundown.Items[1].DeptNote("Lighting") == "", "workflow: both undo, newest first");
        r.Show.SetDelay(60);
        r.Show.Next();   // the delayed item starts: the delay is used up, and an undo of it would target an item no longer live
        Check(r.Show.PendingDelay == TimeSpan.Zero && r.Show.UndoLabel is null, "workflow: a delay's undo goes once its item is no longer live");
    }
}
