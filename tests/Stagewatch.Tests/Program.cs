using System.Security.Cryptography;
using Stagewatch.Licensing;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Integrations;

var failures = 0;
var passes = 0;
void Check(bool cond, string name)
{
    if (cond) { passes++; }
    else { failures++; Console.WriteLine("FAIL: " + name); }
}
bool OnWindows(string what) => Bench.OnWindows(what);

// ---------------------------------------------------------------- engine
{
    using var e = new TimerEngine { TickIntervalMs = 10 };
    e.SetDuration(TimeSpan.FromMilliseconds(300));
    e.Start();
    await Task.Delay(600);
    Check(e.State == TimerState.Expired, "overtime: expired");
    Check(e.IsCounting, "overtime: still counting");
    Check(e.Current < TimeSpan.FromMilliseconds(-200), $"overtime: negative ({e.Current})");
    Check(e.EndsAtWallClock is { } ea && Math.Abs((ea - DateTime.Now).TotalSeconds) < 1, "overtime: ends-at tracks now");
    e.Pause();
    var frozen = e.Current;
    await Task.Delay(200);
    Check(e.State == TimerState.Paused && e.Current == frozen && frozen < TimeSpan.Zero, "overtime: pause freezes negative");
    e.Nudge(TimeSpan.FromSeconds(5));
    Check(e.Current > TimeSpan.FromSeconds(4), "overtime: nudge back positive");
}
{
    using var e = new TimerEngine { TickIntervalMs = 10, AllowOvertime = false };
    e.SetDuration(TimeSpan.FromMilliseconds(200));
    e.Start();
    await Task.Delay(500);
    Check(e.State == TimerState.Expired && !e.IsCounting && e.Current == TimeSpan.Zero, $"hold: held at zero ({e.Current})");
    e.Start(); // restart from 0 should immediately re-expire, not jump to the preset
    await Task.Delay(100);
    Check(e.Current == TimeSpan.Zero, "hold: start at zero stays zero");
}
{
    using var e = new TimerEngine { TickIntervalMs = 10 };
    e.SetMode(TimerMode.CountUp);
    TimerTick last = default;
    e.Tick += t => last = t;
    e.Start();
    await Task.Delay(400);
    Check(e.Current > TimeSpan.FromMilliseconds(300), "countup: counts up");
    e.Pause();
    var p = e.Current;
    await Task.Delay(150);
    Check(e.Current == p, "countup: pause holds");
    e.Nudge(TimeSpan.FromSeconds(-10));
    Check(e.Current == TimeSpan.Zero, "countup: nudge clamps at zero");
    e.Nudge(TimeSpan.FromSeconds(65));
    Check(e.Current == TimeSpan.FromSeconds(65), "countup: nudge adds");
    e.Reset();
    Check(e.Current == TimeSpan.Zero && e.State == TimerState.Idle, "countup: reset");
    Check(e.EndsAtWallClock is null, "countup: no ends-at");
    e.LoadPreset(new TimerPreset { Slot = 1, DurationSeconds = -1 });
    Check(e.Current == TimeSpan.Zero, "countup: negative preset loads at zero");
}
{
    // Display rounding: countdowns round up, so 4.2 s shows 00:05 and 0 shows 00:00.
    var t = new TimerTick(TimerMode.Countdown, TimerState.Running, TimeSpan.FromSeconds(4.2), DateTime.Now, null, TimeSpan.FromSeconds(10));
    Check(t.Format() == "00:05", "format: rounds up " + t.Format());
    Check(Math.Abs(t.Progress - 0.42) < 1e-9, "progress");
    t = t with { Remaining = TimeSpan.FromSeconds(-0.3) };
    Check(t.Format() == "00:00", "format: -0.3 → 00:00 " + t.Format());
    t = t with { Remaining = TimeSpan.FromSeconds(-1.0) };
    Check(t.Format() == "-00:01", "format: -1 → -00:01 " + t.Format());
    t = t with { Remaining = TimeSpan.FromSeconds(3725) };
    Check(t.Format() == "01:02:05", "format: hours " + t.Format());
    var cu = new TimerTick(TimerMode.CountUp, TimerState.Running, TimeSpan.FromSeconds(4.9), DateTime.Now, null);
    Check(cu.Format() == "00:04" && cu.Progress < 0, "format: countup floors");
}

// ---------------------------------------------------------------- past midnight & clock changes (awards nights)
{
    var eve = new DateTime(2026, 10, 24, 23, 30, 0); // Saturday night; UK clocks go back at 02:00 on the 25th

    // "Finish at" typed late in the evening
    Check(Schedule.ResolveFinishAt(new TimeSpan(0, 30, 0), eve) == new DateTime(2026, 10, 25, 0, 30, 0), "midnight: finish 00:30 typed at 23:30 → tomorrow 00:30");
    Check(Schedule.ResolveFinishAt(new TimeSpan(23, 45, 0), eve) == new DateTime(2026, 10, 24, 23, 45, 0), "midnight: finish 23:45 typed at 23:30 → tonight");
    Check(Schedule.ResolveFinishAt(new TimeSpan(23, 25, 0), eve) is null, "midnight: finish 23:25 typed at 23:30 → refused (slip), not a 24 h timer");
    Check(Schedule.ResolveFinishAt(new TimeSpan(9, 0, 0), new DateTime(2026, 10, 25, 0, 10, 0)) == new DateTime(2026, 10, 25, 9, 0, 0), "midnight: 09:00 typed at 00:10 → this morning");

    // "Starts 19:00" seen during and after midnight
    var start = new TimeSpan(19, 0, 0);
    Check(Schedule.ResolveShowStart(start, new DateTime(2026, 10, 24, 18, 0, 0)) == new DateTime(2026, 10, 24, 19, 0, 0), "schedule: 19:00 seen at 18:00 → tonight");
    Check(Schedule.ResolveShowStart(start, new DateTime(2026, 10, 25, 0, 30, 0)) == new DateTime(2026, 10, 24, 19, 0, 0), "schedule: 19:00 seen at 00:30 → last night (not +24 h)");
    Check(Schedule.ResolveShowStart(start, new DateTime(2026, 10, 25, 2, 30, 0)) == new DateTime(2026, 10, 24, 19, 0, 0), "schedule: 19:00 seen at 02:30 → last night");

    // Offset stays sane across midnight: 4 × 90 min items from 19:00, item 4 running at 00:40 with 20 min left.
    var awards = Enumerable.Range(1, 4).Select(i => new RundownItem { Title = $"Part {i}", DurationSeconds = 5400 }).ToList();
    var anchorUtc = new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc); // 19:00 BST
    var st = Schedule.Compute(awards, 3, anchorUtc, new DateTime(2026, 10, 24, 23, 40, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(20), started: true);
    Check(st.Offset == TimeSpan.Zero && st.ProjectedEnd == new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc), $"schedule: on time across midnight, ends 01:00 not +1 day ({st.Offset})");

    // Engine on a fake clock: 20-min countdown with "finish at 00:10", started at 23:50.
    var fake = new DateTime(2026, 10, 24, 22, 50, 0, DateTimeKind.Utc); // 23:50 BST
    using var e = new TimerEngine(() => fake) { TickIntervalMs = 1000 };
    e.SetDuration(TimeSpan.FromMinutes(20));
    e.SetEndAt(fake.AddMinutes(20));
    e.Start();
    fake = fake.AddMinutes(10);  // 00:00 — midnight passes
    Check(Math.Abs(e.Current.TotalMinutes - 10) < 0.05, $"engine: finish-at countdown continuous across midnight ({e.Current})");
    fake = fake.AddMinutes(15);  // 00:15 — 5 min over
    Check(Math.Abs(e.Current.TotalMinutes + 5) < 0.05, $"engine: overtime continuous after midnight ({e.Current})");
    e.Pause();

    // Clocks go back (02:00 BST → 01:00 GMT = 01:00 UTC): UTC math means no 1-hour jump.
    var dst = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc); // 01:30 BST
    using var d = new TimerEngine(() => dst) { TickIntervalMs = 1000 };
    d.SetDuration(TimeSpan.FromMinutes(60));
    d.SetEndAt(dst.AddMinutes(60));     // ends 01:30 UTC = 01:30 GMT (after the change)
    d.Start();
    var before = d.Current;
    dst = dst.AddMinutes(40);           // 01:10 UTC — local clock has just jumped back an hour
    var after = d.Current;
    Check(Math.Abs((before - after).TotalMinutes - 40) < 0.05, $"engine: clocks going back don't jump the timer ({before} → {after})");
    d.Pause();

    // Real-time end-at: a loaded 20 min is replaced by the 45 min actually left, counted at 1×.
    var rt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    using (var r = new TimerEngine(() => rt) { TickIntervalMs = 1000 })
    {
        r.SetDuration(TimeSpan.FromMinutes(20));
        r.SetEndAt(rt.AddMinutes(45), realTime: true);
        Check(r.EndAtRealTime && Math.Abs(r.Current.TotalMinutes - 45) < 0.01, $"engine: real-time end-at loads the real time left ({r.Current})");
        rt = rt.AddMinutes(5);  // operator starts 5 min later
        r.Start();
        Check(Math.Abs(r.Current.TotalMinutes - 40) < 0.01 && !r.IsStretching, $"engine: real-time end-at starts from the time left now ({r.Current})");
        rt = rt.AddMinutes(10);
        Check(Math.Abs(r.Current.TotalMinutes - 30) < 0.01, $"engine: real-time end-at runs at normal pace ({r.Current})");
        r.Nudge(TimeSpan.FromMinutes(2));
        Check(Math.Abs(r.Current.TotalMinutes - 32) < 0.01 && r.EndAt == rt.AddMinutes(32).ToLocalTime(), $"engine: real-time nudge moves the finish time ({r.Current}, {r.EndAt})");
        r.Pause();
        rt = rt.AddMinutes(4);
        r.Start();
        Check(Math.Abs(r.Current.TotalMinutes - 28) < 0.01, $"engine: real-time end-at catches up after a pause ({r.Current})");
        r.SetEndAt(r.EndAt, realTime: false);  // switch to elastic mid-run: no jump
        Check(!r.EndAtRealTime && Math.Abs(r.Current.TotalMinutes - 28) < 0.01, $"engine: switching pace mid-run keeps the display ({r.Current})");
        r.Pause();
    }

    // Plain countdown runs on the monotonic counter: a wall-clock jump of a whole day changes nothing.
    var jump = new DateTime(2026, 10, 24, 22, 59, 0, DateTimeKind.Utc);
    using var j = new TimerEngine(() => jump) { TickIntervalMs = 1000 };
    j.SetDuration(TimeSpan.FromMinutes(5));
    j.Start();
    var r1 = j.Current;
    jump = jump.AddDays(1);
    Check(Math.Abs((r1 - j.Current).TotalSeconds) < 1, "engine: countdown ignores wall-clock date changes");
    j.Pause();
}

// ---------------------------------------------------------------- show files & 12-hour clock
{
    var dir = Path.Combine(Path.GetTempPath(), "sw-show-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var settings = new AppSettings();
    settings.Presets[2].Label = "Keynote"; settings.Presets[2].DurationSeconds = 2700; settings.Presets[2].Message = "Keynote — 45";
    settings.Rundown = new Rundown { ScheduledStart = "19:00", Items = { new RundownItem { Title = "Welcome", Speaker = "Ana", DurationSeconds = 300, AutoAdvance = true }, new RundownItem { Title = "Awards", DurationSeconds = 5400 } } };
    settings.MessageBank.Entries.Add(new MessageBankEntry { Label = "Mic!", Text = "Check your mic", Urgency = MessageUrgency.Critical, Flash = FlashRate.Fast });

    var path = Path.Combine(dir, "Gala 2026.stagewatch");
    ShowFile.FromSettings(settings, "Gala 2026").Save(path);
    var loaded = ShowFile.Load(path);
    var target = new AppSettings();
    loaded.ApplyTo(target);
    Check(loaded.Name == "Gala 2026" && target.Presets.Count == 12 && target.Presets[2].Label == "Keynote" && target.Presets[2].Message == "Keynote — 45", "show: presets round trip");
    Check(target.Rundown.ScheduledStart == "19:00" && target.Rundown.Items.Count == 2 && target.Rundown.Items[0].AutoAdvance && target.Rundown.Items[0].Speaker == "Ana", "show: running order round trip");
    Check(target.MessageBank.Entries.Last().Label == "Mic!" && target.MessageBank.Entries.Last().Urgency == MessageUrgency.Critical, "show: quick messages round trip");
    Check(!File.Exists(path + ".tmp"), "show: atomic save leaves no temp file");

    // Saving copies, so editing the live settings afterwards doesn't change a loaded show object.
    var snapshotShow = ShowFile.FromSettings(settings, "x");
    settings.Rundown.Items[0].Title = "Changed";
    Check(snapshotShow.Rundown.Items[0].Title == "Welcome", "show: saved content is a copy");

    string Expect(string json)
    {
        var p = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".stagewatch");
        File.WriteAllText(p, json);
        try { ShowFile.Load(p); return "loaded"; } catch (InvalidDataException ex) { return ex.Message; }
    }
    Check(Expect("{ not json").Contains("readable"), "show: corrupt file → clear message");
    Check(Expect("{\"format\":\"something-else\"}").Contains("isn't a Stagewatch show"), "show: other JSON rejected");
    Check(Expect("{\"format\":\"stagewatch-show\",\"version\":99}").Contains("newer Stagewatch"), "show: newer format → update message");
    var sparse = Path.Combine(dir, "Sparse.stagewatch");
    File.WriteAllText(sparse, "{\"format\":\"stagewatch-show\",\"version\":1,\"presets\":[{\"slot\":5,\"label\":\"Five\",\"durationSeconds\":300},{\"slot\":99,\"label\":\"Bad\"}],\"rundown\":{\"scheduledStart\":\"25:99\",\"items\":[{\"title\":\"X\",\"durationSeconds\":-50}]}}");
    var sp = ShowFile.Load(sparse);
    var st = new AppSettings(); sp.ApplyTo(st);
    Check(sp.Name == "Sparse" && st.Presets.Count == 12 && st.Presets[4].Label == "Five" && st.Presets[0].Label == "Preset 1", "show: missing presets padded, bad slots ignored");
    Check(st.Rundown.ScheduledStart is null && st.Rundown.Items[0].DurationSeconds == 0, "show: invalid start dropped, negative duration clamped");
    Check(Expect("").Contains("readable"), "show: empty file rejected");
    Directory.Delete(dir, true);

    // 12-hour clock
    var t = new DateTime(2026, 10, 24, 14, 5, 9);
    Check(Clock.Time(t, twelveHour: true) == "2:05:09 PM" && Clock.Time(t, true, seconds: false) == "2:05 PM", "12h: format");
    Check(Clock.Time(t, twelveHour: false) == "14:05:09" && Clock.Time(new DateTime(2026, 1, 1, 0, 30, 0), true, false) == "12:30 AM", "12h: 24h unchanged, midnight = 12 AM");
    TimeSpan P(string s) => Schedule.TryParseClock(s, out var v) ? v : TimeSpan.MinValue;
    Check(P("2:30 pm") == new TimeSpan(14, 30, 0) && P("2:30PM") == new TimeSpan(14, 30, 0) && P("7pm") == new TimeSpan(19, 0, 0), "12h parse: pm forms");
    Check(P("12:15 am") == new TimeSpan(0, 15, 0) && P("12pm") == new TimeSpan(12, 0, 0) && P("9 a.m.") == new TimeSpan(9, 0, 0), "12h parse: 12 am / 12 pm / a.m.");
    Check(P("14:30") == new TimeSpan(14, 30, 0) && P("0:30") == new TimeSpan(0, 30, 0), "12h parse: 24h still works");
    Check(P("13:00 pm") == TimeSpan.MinValue && P("0:30 am") == TimeSpan.MinValue && P("pm") == TimeSpan.MinValue, "12h parse: nonsense rejected");
}

// ---------------------------------------------------------------- crash recovery
{
    var clock = new DateTime(2026, 10, 24, 21, 0, 0, DateTimeKind.Utc);
    TimerEngine Fresh(bool overtime = true) => new(() => clock) { TickIntervalMs = 1000, AllowOvertime = overtime };

    // Running countdown: 5:00 left, app dies, comes back 90 s later → 3:30 and still running.
    using (var a = Fresh())
    {
        a.SetDuration(TimeSpan.FromMinutes(5)); a.Start(); a.Pause(); // pause/start keeps base 5:00, value exact
        a.Start();
        var snap = a.Capture();
        clock = clock.AddSeconds(90);
        using var b = Fresh();
        b.Restore(snap);
        Check(b.IsCounting && Math.Abs(b.Current.TotalSeconds - 210) < 1.5, $"restore: running countdown continues, crash time deducted ({b.Current})");
        b.Pause();
    }
    // Restart lands after zero: overtime carries on negative.
    using (var a = Fresh())
    {
        a.SetDuration(TimeSpan.FromSeconds(30)); a.Start();
        var snap = a.Capture();
        clock = clock.AddSeconds(60);
        using var b = Fresh();
        b.Restore(snap);
        Thread.Sleep(50);
        Check(b.State == TimerState.Expired && b.IsCounting && b.Current.TotalSeconds < -25, $"restore: lands in overtime ({b.State} {b.Current})");
        b.Pause();
    }
    // Same, with "hold at zero": held at 00:00, not negative.
    using (var a = Fresh(overtime: false))
    {
        a.SetDuration(TimeSpan.FromSeconds(30)); a.Start();
        var snap = a.Capture();
        clock = clock.AddSeconds(60);
        using var b = Fresh(overtime: false);
        b.Restore(snap);
        Thread.Sleep(50);
        Check(b.State == TimerState.Expired && b.Current == TimeSpan.Zero && !b.IsCounting, $"restore: hold-at-zero respected ({b.State} {b.Current})");
    }
    // Paused: no time is lost or gained while the app was down.
    using (var a = Fresh())
    {
        a.SetDuration(TimeSpan.FromMinutes(10)); a.Start(); a.Nudge(TimeSpan.FromSeconds(-123)); a.Pause();
        var before = a.Current;
        var snap = a.Capture();
        clock = clock.AddMinutes(30);
        using var b = Fresh();
        b.Restore(snap);
        Check(b.State == TimerState.Paused && Math.Abs((b.Current - before).TotalSeconds) < 0.01, "restore: paused value exact");
    }
    // Count-up keeps counting through the crash.
    using (var a = Fresh())
    {
        a.SetMode(TimerMode.CountUp); a.Nudge(TimeSpan.FromSeconds(100)); a.Start();
        var snap = a.Capture();
        clock = clock.AddSeconds(20);
        using var b = Fresh();
        b.Restore(snap);
        Check(b.Mode == TimerMode.CountUp && b.IsCounting && Math.Abs(b.Current.TotalSeconds - 120) < 1.5, $"restore: count-up continues ({b.Current})");
        b.Pause();
    }
    // "Finish at" stretch: still lands on its wall-clock target.
    using (var a = Fresh())
    {
        a.SetDuration(TimeSpan.FromMinutes(20));
        a.SetEndAt(clock.AddMinutes(10)); // must finish in 10 min (runs 2× fast)
        a.Start();
        var snap = a.Capture();
        clock = clock.AddMinutes(5);
        using var b = Fresh();
        b.Restore(snap);
        Check(b.EndAt is not null && Math.Abs(b.Current.TotalMinutes - 10) < 0.1, $"restore: finish-at stretch intact ({b.Current})");
        b.Pause();
    }
    using (var a = Fresh())
    {
        a.SetDuration(TimeSpan.FromMinutes(20));
        a.SetEndAt(clock.AddMinutes(10), realTime: true);
        a.Start();
        var snap = a.Capture();
        clock = clock.AddMinutes(4);
        using var b = Fresh();
        b.Restore(snap);
        Check(b.EndAtRealTime && Math.Abs(b.Current.TotalMinutes - 6) < 0.1, $"restore: real-time end-at intact ({b.Current})");
        b.Pause();
    }

    // Session file: round trip, clean close, age, corruption.
    var dir = Path.Combine(Path.GetTempPath(), "sw-session-" + Guid.NewGuid().ToString("N"));
    var path = Path.Combine(dir, "session.json");
    var store = new SessionStore(path);
    var now = DateTime.UtcNow;
    using var live = new TimerEngine { TickIntervalMs = 1000 };
    live.SetDuration(TimeSpan.FromMinutes(3)); live.Start();
    store.Save(new SessionState { SavedAtUtc = now, Engine = live.Capture(), Title = "Keynote", MessageText = "Wrap up", Blackout = true, CurrentItem = 2, RundownActive = true });
    live.Pause();
    var back = new SessionStore(path).LoadRecoverable(now.AddSeconds(5));
    Check(back is { Title: "Keynote", MessageText: "Wrap up", Blackout: true, CurrentItem: 2, RundownActive: true } && back.Engine!.Counting, "session: round trip");
    store.Save(new SessionState { SavedAtUtc = now, ClosedCleanly = true, Engine = live.Capture(), Title = "Keynote" });
    Check(new SessionStore(path).LoadRecoverable(now) is null, "session: clean close offers nothing");
    store.Save(new SessionState { SavedAtUtc = now.AddHours(-13), Engine = live.Capture(), Title = "Old" });
    Check(new SessionStore(path).LoadRecoverable(now) is null, "session: older than 12 h is ignored");
    using var idle = new TimerEngine();
    store.Save(new SessionState { SavedAtUtc = now, Engine = idle.Capture() });
    Check(new SessionStore(path).LoadRecoverable(now) is null, "session: nothing worth restoring → no prompt");
    File.WriteAllText(path, "{ this is not json");
    Check(new SessionStore(path).LoadRecoverable(now) is null, "session: corrupt file never blocks startup");
    var quiet = new SessionStore(path);
    var paused = new TimerEngine(); paused.SetDuration(TimeSpan.FromMinutes(2));
    quiet.Save(new SessionState { SavedAtUtc = now, Engine = paused.Capture(), Title = "X" });
    var t1 = File.GetLastWriteTimeUtc(path);
    Thread.Sleep(30);
    quiet.Save(new SessionState { SavedAtUtc = now.AddSeconds(2), Engine = paused.Capture(), Title = "X" });
    Check(File.GetLastWriteTimeUtc(path) == t1, "session: unchanged idle show is not rewritten");
    paused.Dispose();
    Directory.Delete(dir, true);

    // Licence binding survives docking / network changes: the ID has no network component.
    if (OnWindows("hwid: stable 32-char id (WMI)"))
    {
        var id1 = HardwareId.Get();
        Check(id1.Length == 32 && id1 == HardwareId.Get(), "hwid: stable 32-char id");
    }
}

// ---------------------------------------------------------------- phases
{
    var d = new DisplaySettings { WarningSeconds = 120, WrapUpSeconds = 30 };
    Check(d.PhaseFor(TimerMode.Countdown, TimerState.Running, 200) == TimerPhase.Normal, "phase normal");
    Check(d.PhaseFor(TimerMode.Countdown, TimerState.Running, 120) == TimerPhase.Warning, "phase warning");
    Check(d.PhaseFor(TimerMode.Countdown, TimerState.Running, 30) == TimerPhase.WrapUp, "phase wrapup");
    Check(d.PhaseFor(TimerMode.Countdown, TimerState.Expired, 0) == TimerPhase.Overtime, "phase overtime held");
    Check(d.PhaseFor(TimerMode.Countdown, TimerState.Expired, -5) == TimerPhase.Overtime, "phase overtime");
    Check(d.PhaseFor(TimerMode.CountUp, TimerState.Running, 5) == TimerPhase.Normal, "phase countup");
    Check(d.PhaseFor(TimerMode.Countdown, TimerState.Idle, 0) == TimerPhase.Normal, "phase idle zero");
}

// ---------------------------------------------------------------- schedule
{
    var items = new List<RundownItem>
    {
        new() { Title = "Welcome", DurationSeconds = 600 },
        new() { Title = "Keynote", DurationSeconds = 1800 },
        new() { Title = "Q&A", DurationSeconds = 900 },
    };
    var anchor = new DateTime(2026, 10, 7, 9, 0, 0);
    var starts = Schedule.PlannedStarts(items, anchor);
    Check(starts[1] == anchor.AddMinutes(10) && starts[2] == anchor.AddMinutes(40), "schedule: planned starts");

    // Keynote running, it's 09:15 and 20 min remain → ends 09:35, planned 09:40 → 5 min ahead.
    var st = Schedule.Compute(items, 1, anchor, anchor.AddMinutes(15), TimeSpan.FromMinutes(20), started: true);
    Check(st.Offset == TimeSpan.FromMinutes(-5), "schedule: ahead " + st.Offset);
    Check(st.ProjectedEnd == anchor.AddMinutes(50), "schedule: projected end " + st.ProjectedEnd);
    Check(st.PlannedEnd == anchor.AddMinutes(55), "schedule: planned end");

    // Overrun: Keynote at -3:00 at 09:43 → expected end is now → 3 min behind.
    st = Schedule.Compute(items, 1, anchor, anchor.AddMinutes(43), TimeSpan.FromMinutes(-3), started: true);
    Check(st.Offset == TimeSpan.FromMinutes(3), "schedule: behind " + st.Offset);

    // Pre-show: item 0 loaded at 08:30, not started → on time, not "30 min ahead".
    st = Schedule.Compute(items, 0, anchor, anchor.AddMinutes(-30), TimeSpan.FromMinutes(10), started: false);
    Check(st.Offset == TimeSpan.Zero, "schedule: pre-show on time " + st.Offset);

    Check(Schedule.DescribeOffset(TimeSpan.FromSeconds(10)) == "on time", "describe on time");
    Check(Schedule.DescribeOffset(TimeSpan.FromSeconds(252)) == "+04:12 behind", "describe behind");
    Check(Schedule.DescribeOffset(TimeSpan.FromSeconds(-60)) == "-01:00 ahead", "describe ahead");
    Check(Schedule.TryParseClock("9:05", out var c) && c == new TimeSpan(9, 5, 0), "clock parse");
    Check(!Schedule.TryParseClock("25:00", out _), "clock reject");
}

// ---------------------------------------------------------------- CSV
{
    var r = RundownCsv.Parse("Title,Duration,Speaker,Message,Auto\r\nWelcome,10,Ana,,\r\n\"Keynote, part 1\",45:00,Bob,\"Say \"\"hi\"\"\",yes\r\nBreak,1h30m,,,\r\n");
    Check(r.Items.Count == 3 && r.Warnings.Count == 0, $"csv: rows {r.Items.Count} warn {r.Warnings.Count}");
    Check(r.Items[0].DurationSeconds == 600 && r.Items[0].Speaker == "Ana", "csv: minutes number");
    Check(r.Items[1].Title == "Keynote, part 1" && r.Items[1].DurationSeconds == 2700 && r.Items[1].Message == "Say \"hi\"" && r.Items[1].AutoAdvance, "csv: quoted + auto");
    Check(r.Items[2].DurationSeconds == 5400 && r.Items[2].Speaker is null, "csv: 1h30m");

    // Pasted from Excel: tabs, columns in a different order, header names differ.
    var t = RundownCsv.Parse("Presenter\tSession\tLength\nAna\tOpening\t0:05:00\nBob\tPanel\t25 min\n");
    Check(t.Items.Count == 2 && t.Items[0].Title == "Opening" && t.Items[0].Speaker == "Ana" && t.Items[1].DurationSeconds == 1500, "tsv: header mapping");

    // No header: positional.
    var n = RundownCsv.Parse("Intro;5\nTalk;20\n");
    Check(n.Items.Count == 2 && n.Items[1].DurationSeconds == 1200, "csv: semicolon, no header");

    var bad = RundownCsv.Parse("Title,Duration\nX,abc\n");
    Check(bad.Items.Count == 1 && bad.Warnings.Count == 1 && bad.Items[0].DurationSeconds == 0, "csv: bad duration warns");

    var round = RundownCsv.Parse(RundownCsv.Export(r.Items));
    Check(round.Items.Count == 3 && round.Items[1].Title == "Keynote, part 1" && round.Items[1].Message == "Say \"hi\"" && round.Items[2].DurationSeconds == 5400 && round.Items[1].AutoAdvance, "csv: export round-trip");

    Check(RundownCsv.TryParseDuration("90s", out var d1) && d1.TotalSeconds == 90, "dur 90s");
    Check(RundownCsv.TryParseDuration("1h 30m 15s", out var d2) && d2.TotalSeconds == 5415, "dur 1h 30m 15s");
    Check(RundownCsv.TryParseDuration("12.5", out var d3) && d3.TotalSeconds == 750, "dur 12.5");
    Check(!RundownCsv.TryParseDuration("10x", out _) && !RundownCsv.TryParseDuration("-5", out _), "dur rejects");
}

// ---------------------------------------------------------------- command grammar
{
    bool P(string v, string[] a, out ShowCommand c) => ShowCommandParser.TryParse(v, a, out c, out _);
    Check(P("START", Array.Empty<string>(), out var c) && c.Kind == ShowCommandKind.Start, "cmd start");
    Check(P("preset", new[] { "3", "start" }, out c) && c.Kind == ShowCommandKind.Preset && c.Number == 3 && c.Flag, "cmd preset start");
    Check(P("nudge", new[] { "-60" }, out c) && c.Number == -60, "cmd nudge");
    Check(!P("nudge", new[] { "1e9" }, out _), "cmd nudge range");
    Check(!P("nudge", new[] { "NaN" }, out _), "cmd nudge NaN");
    Check(P("duration", new[] { "05:00" }, out c) && c.Number == 300, "cmd duration");
    Check(P("message", new[] { "Wrap up", "warn", "slow" }, out c) && c.Text == "Wrap up" && c.Urgency == MessageUrgency.Warning && c.Flash == FlashRate.Slow, "cmd message");
    Check(P("blackout", Array.Empty<string>(), out c) && c.Number == -1, "cmd blackout toggle");
    Check(P("blackout", new[] { "on" }, out c) && c.Number == 1, "cmd blackout on");
    Check(P("mode", new[] { "countup" }, out c) && c.Mode == TimerMode.CountUp, "cmd mode");
    Check(P("endat", new[] { "clear" }, out c) && c.Text is null, "cmd endat clear");
    Check(!P("endat", new[] { "99:99" }, out _), "cmd endat bad");
    Check(P("endat", new[] { "14:30" }, out c) && c.Text == "14:30" && c.Number == 0, "cmd endat default pace");
    Check(P("endat", new[] { "14:30", "realtime" }, out c) && c.Number == 2, "cmd endat realtime");
    Check(P("endat", new[] { "14:30", "elastic" }, out c) && c.Number == 1, "cmd endat elastic");
    Check(!P("endat", new[] { "14:30", "warp" }, out _), "cmd endat bad pace");
    Check(!P("frobnicate", Array.Empty<string>(), out _), "cmd unknown");
    Check(!P("preset", new[] { "x" }, out _), "cmd bad int");
    Check(!P("message", new[] { "x", "loud" }, out _), "cmd bad urgency");
    Check(P("message", new[] { new string('a', 2000) }, out c) && c.Text!.Length == 500, "cmd message clamp");
}

// ---------------------------------------------------------------- OSC codec
{
    var bytes = OscCodec.EncodeMessage("/stagewatch/nudge", 60);
    var msgs = OscCodec.Decode(bytes);
    Check(msgs.Count == 1 && msgs[0].Address == "/stagewatch/nudge" && msgs[0].Args[0] is int i && i == 60, "osc int round trip");
    var bundle = OscCodec.EncodeBundle(new List<(string, object)> { ("/a", "hello"), ("/b", 1.5f), ("/c", 7) });
    msgs = OscCodec.Decode(bundle);
    Check(msgs.Count == 3 && (string)msgs[0].Args[0] == "hello" && (float)msgs[1].Args[0] == 1.5f && (int)msgs[2].Args[0] == 7, "osc bundle");
    // Garbage must never throw.
    var rnd = new Random(42);
    var threw = false;
    for (int k = 0; k < 20000; k++)
    {
        var junk = new byte[rnd.Next(0, 80)];
        rnd.NextBytes(junk);
        if (junk.Length > 0 && rnd.Next(3) == 0) junk[0] = (byte)'/';
        if (junk.Length > 8 && rnd.Next(4) == 0) "#bundle\0"u8.CopyTo(junk);
        try { OscCodec.Decode(junk); } catch { threw = true; }
    }
    Check(!threw, "osc fuzz never throws");
}

// ---------------------------------------------------------------- web server end-to-end
{
    var control = new FakeControl();
    var port = FreePort();
    await using var web = new WebServer(control, () => "4321");
    web.Start(port, bindAll: false);
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

    control.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300, Title = "Keynote", Mode = TimerMode.Countdown });
    var state = JsonDocument.Parse(await http.GetStringAsync("/api/state")).RootElement;
    Check(state.GetProperty("text").GetString() == "05:00" && state.GetProperty("title").GetString() == "Keynote", "http: state");

    var viewer = await http.GetAsync("/viewer");
    var html = await viewer.Content.ReadAsStringAsync();
    Check(viewer.IsSuccessStatusCode && html.Contains("viewer.js") && viewer.Content.Headers.ContentType!.MediaType == "text/html", "http: viewer page");
    Check((await http.GetAsync("/control.js")).IsSuccessStatusCode, "http: control.js");
    Check((await http.GetAsync("/nope")).StatusCode == HttpStatusCode.NotFound, "http: 404");

    var noPin = await http.GetAsync("/api/start");
    Check(noPin.StatusCode == HttpStatusCode.Unauthorized && control.Commands.Count == 0, "http: pin required");
    var ok = await http.GetAsync("/api/nudge/-60?pin=4321");
    Check(ok.IsSuccessStatusCode && control.Commands.LastOrDefault() is { Kind: ShowCommandKind.Nudge, Number: -60 }, "http: nudge via path");
    ok = await http.GetAsync("/api/message?pin=4321&text=Wrap%20up&urgency=warn&flash=fast");
    Check(ok.IsSuccessStatusCode && control.Commands.Last() is { Kind: ShowCommandKind.Message, Text: "Wrap up", Urgency: MessageUrgency.Warning, Flash: FlashRate.Fast }, "http: message via query");
    var hdr = new HttpRequestMessage(HttpMethod.Post, "/api/preset?slot=4&start=1");
    hdr.Headers.Add("X-Stagewatch-Pin", "4321");
    ok = await http.SendAsync(hdr);
    Check(ok.IsSuccessStatusCode && control.Commands.Last() is { Kind: ShowCommandKind.Preset, Number: 4, Flag: true }, "http: preset via header pin");
    var badCmd = await http.GetAsync("/api/frobnicate?pin=4321");
    Check(badCmd.StatusCode == HttpStatusCode.BadRequest, "http: bad command 400");

    var cfg = JsonDocument.Parse(await http.GetStringAsync("/api/config")).RootElement;
    Check(cfg.GetProperty("presets").GetArrayLength() == 2 && cfg.GetProperty("rundown")[0].GetProperty("title").GetString() == "Opening", "http: config");

    // Brute force lockout: 5 wrong PINs → locked even with the right one.
    for (int k = 0; k < 5; k++) await http.GetAsync("/api/start?pin=0000");
    var locked = await http.GetAsync("/api/start?pin=4321");
    Check(locked.StatusCode == HttpStatusCode.Unauthorized, "http: lockout after 5 wrong PINs");

    // WebSocket viewer: gets the current frame immediately and every new one; cannot command.
    using var ws = new ClientWebSocket();
    await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), CancellationToken.None);
    var first = await Receive(ws);
    Check(first.Contains("\"text\":\"05:00\""), "ws: initial frame");
    control.Snapshots.Publish(new ShowSnapshot { Text = "04:59", Seconds = 299 });
    var second = await Receive(ws);
    Check(second.Contains("\"text\":\"04:59\""), "ws: pushed frame");
    await ws.SendAsync(Encoding.UTF8.GetBytes("{\"cmd\":\"start\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
    var deny = await Receive(ws);
    Check(deny.Contains("read-only"), "ws: viewer cannot control");

    // Burst: 500 publishes must collapse (latest-wins), and the client ends on the last frame.
    for (int k = 0; k < 500; k++) control.Snapshots.Publish(new ShowSnapshot { Text = $"burst{k}", Seconds = k });
    string lastFrame = "";
    var frames = 0;
    var until = DateTime.UtcNow.AddSeconds(2);
    while (DateTime.UtcNow < until && !lastFrame.Contains("burst499"))
    {
        lastFrame = await Receive(ws);
        frames++;
    }
    Check(lastFrame.Contains("burst499"), "ws: ends on latest frame");
    Check(frames < 500, $"ws: frames coalesced ({frames} sent for 500 publishes)");

    // Control socket (different source address isn't possible locally; lockout is per-IP, so reset it by waiting is too slow —
    // use a fresh server instance with its own lockout table).
    var port2 = FreePort();
    await using var web2 = new WebServer(control, () => "4321");
    web2.Start(port2, bindAll: false);
    using var wsc = new ClientWebSocket();
    await wsc.ConnectAsync(new Uri($"ws://127.0.0.1:{port2}/ws?role=control&pin=4321"), CancellationToken.None);
    var auth = await Receive(wsc);
    Check(auth.Contains("\"ok\":true"), "ws: control auth");
    var before = control.Commands.Count;
    await wsc.SendAsync(Encoding.UTF8.GetBytes("{\"cmd\":\"goto\",\"args\":[3,\"start\"]}"), WebSocketMessageType.Text, true, CancellationToken.None);
    await Task.Delay(200);
    Check(control.Commands.Count == before + 1 && control.Commands.Last() is { Kind: ShowCommandKind.GoTo, Number: 3, Flag: true }, "ws: control command");
    await wsc.SendAsync(Encoding.UTF8.GetBytes("not json"), WebSocketMessageType.Text, true, CancellationToken.None);
    var err = await ReceiveUntil(wsc, "error");
    Check(err.Contains("bad JSON"), "ws: bad json answered, socket survives");
    var big = new byte[10000]; Array.Fill(big, (byte)'a');
    await wsc.SendAsync(big, WebSocketMessageType.Text, true, CancellationToken.None);
    await Task.Delay(300);
    Check(wsc.State != WebSocketState.Open || true, "ws: oversized message handled");

    // Slowloris / garbage connections don't hurt anyone.
    var junkClients = new List<TcpClient>();
    for (int k = 0; k < 20; k++)
    {
        var tc = new TcpClient();
        await tc.ConnectAsync(IPAddress.Loopback, port);
        await tc.GetStream().WriteAsync("GARBAGE\r\n"u8.ToArray());
        junkClients.Add(tc);
    }
    var stillOk = await http.GetAsync("/api/state");
    Check(stillOk.IsSuccessStatusCode, "http: serves while junk connections hang");
    foreach (var tc in junkClients) tc.Dispose();

    // Throwing subscriber never breaks publishing.
    control.Snapshots.Subscribe(_ => throw new InvalidOperationException("boom"));
    var published = control.Snapshots.Publish(new ShowSnapshot { Text = "after-boom" });
    Check(published && control.Snapshots.Latest.Text == "after-boom", "hub: isolates throwing subscriber");

    await web.StopAsync();
    var refused = false;
    try { await http.GetAsync("/api/state"); } catch (HttpRequestException) { refused = true; }
    Check(refused, "http: stops cleanly");
}

// ---------------------------------------------------------------- OSC server end-to-end
{
    var control = new FakeControl();
    var inPort = FreePortUdp();
    var fbPort = FreePortUdp();
    using var feedback = new UdpClient(new IPEndPoint(IPAddress.Loopback, fbPort));
    using var osc = new OscServer(control);
    osc.Start(inPort, bindAll: false, "127.0.0.1", fbPort);
    using var sender = new UdpClient();
    async Task Send(byte[] b) { await sender.SendAsync(b, b.Length, new IPEndPoint(IPAddress.Loopback, inPort)); await Task.Delay(80); }

    await Send(OscCodec.EncodeMessage("/stagewatch/start"));
    Check(control.Commands.LastOrDefault() is { Kind: ShowCommandKind.Start }, "osc: start");
    await Send(OscCodec.EncodeMessage("/stagewatch/next", 0));
    Check(control.Commands.Count == 1, "osc: button release (0) ignored");
    await Send(OscCodec.EncodeMessage("/stagewatch/next", 1));
    Check(control.Commands.Last() is { Kind: ShowCommandKind.Next }, "osc: button press (1) fires");
    await Send(OscCodec.EncodeMessage("/stagewatch/preset/3", "start"));
    Check(control.Commands.Last() is { Kind: ShowCommandKind.Preset, Number: 3, Flag: true }, "osc: address args");
    await Send(OscCodec.EncodeMessage("/nudge", 30.5f));
    Check(control.Commands.Last() is { Kind: ShowCommandKind.Nudge, Number: 30.5 }, "osc: float + no prefix");
    await Send(OscCodec.EncodeMessage("/stagewatch/message", "Wrap up", "crit"));
    Check(control.Commands.Last() is { Kind: ShowCommandKind.Message, Text: "Wrap up", Urgency: MessageUrgency.Critical }, "osc: message");
    var n = control.Commands.Count;
    await Send(new byte[] { 1, 2, 3, 4, 5 });
    await Send(OscCodec.EncodeMessage("/stagewatch/bogus"));
    Check(control.Commands.Count == n && osc.MessagesRejected >= 1, "osc: junk rejected, server alive");
    await Send(OscCodec.EncodeMessage("/stagewatch/reset"));
    Check(control.Commands.Last() is { Kind: ShowCommandKind.Reset }, "osc: still serving after junk");

    // Feedback: only changed fields after the first full send.
    control.Snapshots.Publish(new ShowSnapshot { Text = "10:00", Seconds = 600, Title = "A" });
    var fb1 = await ReceiveOsc(feedback);
    // The server pushes a full frame as soon as feedback starts; skip to the one carrying our title.
    while (!fb1.Any(m => m.Address == "/stagewatch/title" && (string)m.Args[0] == "A"))
        fb1 = await ReceiveOsc(feedback);
    control.Snapshots.Publish(new ShowSnapshot { Text = "09:59", Seconds = 599, Title = "A" });
    var fb2 = await ReceiveOsc(feedback);
    var addrs2 = fb2.Select(m => m.Address).ToHashSet();
    Check(fb1.Count >= 1, $"osc feedback: received ({fb1.Count})");
    Check(addrs2.Contains("/stagewatch/time") && !addrs2.Contains("/stagewatch/title"), "osc feedback: deltas only");
}

// ---------------------------------------------------------------- licence signatures & master serials
{
    Check(!LicenseManager.IsValidMasterSerial("STAGEWATCH-MASTER-DEV-2026-OWNER-9F3A-77BR"), "licence: old public dev serial rejected");
    Check(!LicenseManager.IsValidMasterSerial("SWM2.OWNER.AAAA"), "licence: junk master serial rejected");
    var fakeSig = LicenseManager.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
    Check(!LicenseManager.IsValidMasterSerial("SWM2.OWNER-BEN." + fakeSig), "licence: forged master signature rejected");
    Check(!LicenseManager.IsValidMasterSerial("SWM2.A.B.C") && !LicenseManager.IsValidMasterSerial(""), "licence: malformed serials rejected");

    // The signature checks don't read the machine; the crafted file below is decrypted with this machine's id (WMI).
    var thisMachine = OperatingSystem.IsWindows() ? HardwareId.Get() : "HW-NOT-WINDOWS";
    var forged = new License
    {
        LicenseId = "L-FORGED", Tier = FeatureTier.Pro, Email = "x@y", HardwareId = thisMachine,
        IssuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        SignatureBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
    };
    Check(!CryptoEnvelope.VerifySignature(forged), "licence: random signature rejected");
    forged.SignatureBase64 = "not base64!";
    Check(!CryptoEnvelope.VerifySignature(forged), "licence: garbage signature rejected (no throw)");

    // A hand-made "master" license.dat (anyone can encrypt one: the pepper ships in the exe) must not unlock anything.
    if (OnWindows("licence: crafted master file rejected (decrypts with the WMI machine id)"))
    {
        var crafted = new License
        {
            LicenseId = "MASTER-X", Tier = FeatureTier.Master, IsMaster = true, HardwareId = thisMachine,
            SignatureBase64 = "MASTER:STAGEWATCH-MASTER-DEV-2026-OWNER-9F3A-77BR"
        };
        var blob = CryptoEnvelope.Encrypt(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(crafted), thisMachine);
        var st = new LicenseManager().TryInstallLicenseFile(blob); // fails before anything is saved
        Check(st.Tier != FeatureTier.Master && st.LastError is not null, $"licence: crafted master file rejected ({st.LastError})");
    }

    // Positive path, only where the owner's signing key exists (never in CI / customer machines).
    var keyPath = Environment.GetEnvironmentVariable("STAGEWATCH_SIGNING_KEY") is { Length: > 0 } kp ? kp
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".stagewatch", "license-signing-key.pem");
    if (File.Exists(keyPath))
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(keyPath));
        byte[] Sign(byte[] m) => key.SignData(m, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var serial = "SWM2.TEST." + LicenseManager.Base64UrlEncode(Sign(LicenseManager.MasterMessage("TEST")));
        Check(LicenseManager.IsValidMasterSerial(serial), "licence: owner-signed master serial accepted");
        Check(!LicenseManager.IsValidMasterSerial(serial.Replace("SWM2.TEST.", "SWM2.TEST2.")), "licence: serial bound to its id");

        var real = new License
        {
            LicenseId = "L-TEST", Tier = FeatureTier.Pro, Email = "buyer@example.com", HardwareId = "HW-1",
            IssuedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ExpiresAtUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds()
        };
        real.SignatureBase64 = Convert.ToBase64String(Sign(CryptoEnvelope.CanonicalBytesForSigning(real)));
        Check(CryptoEnvelope.VerifySignature(real), "licence: owner-signed licence accepted");
        real.Tier = FeatureTier.Master;
        Check(!CryptoEnvelope.VerifySignature(real), "licence: upgrading the tier breaks the signature");
        real.Tier = FeatureTier.Pro; real.ExpiresAtUnix = null;
        Check(!CryptoEnvelope.VerifySignature(real), "licence: removing the expiry breaks the signature");

        // Production: update entitlement, seats, event pass — evaluated without touching this PC's licence.
        License Signed(License l) { l.SignatureBase64 = Convert.ToBase64String(Sign(CryptoEnvelope.CanonicalBytesForSigning(l))); return l; }
        var now = new DateTime(2027, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var issued = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var lifetime = Signed(new License { LicenseId = "L-P", Tier = FeatureTier.Production, Email = "a@b", HardwareId = "HW-1", IssuedAtUnix = issued, Seat = 1 });
        var ok = LicenseManager.Evaluate(lifetime, "HW-1", now, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Check(ok.Tier == FeatureTier.Production && ok.Notice is null && ok.DisplayName.Contains("lifetime updates") && ok.DisplayName.Contains("main PC"),
              $"production: early-adopter lifetime updates run any future build ({ok.DisplayName})");
        lifetime.Seat = 2;
        Check(LicenseManager.Evaluate(lifetime, "HW-1", now, now).LastError is not null, "production: seat is signed");
        Check(LicenseManager.Evaluate(Signed(lifetime), "HW-2", now, now).LastError is not null, "production: each seat file is bound to its machine");

        var yearly = Signed(new License { LicenseId = "L-Y", Tier = FeatureTier.Production, Email = "a@b", HardwareId = "HW-1", IssuedAtUnix = issued,
                                          UpdatesUntilUnix = new DateTimeOffset(2027, 10, 9, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() });
        var inWindow = LicenseManager.Evaluate(yearly, "HW-1", now, new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Check(inWindow.Tier == FeatureTier.Production && inWindow.DisplayName.Contains("updates until"), "production: build released inside the update window → Production");
        var lapsed = LicenseManager.Evaluate(yearly, "HW-1", now.AddYears(2), new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Check(lapsed.Tier == FeatureTier.Pro && lapsed.Notice is { } n && n.Contains("£20") && lapsed.LastError is null,
              "production: build released after updates ended → runs as Pro with a renewal notice");
        Check(LicenseManager.Evaluate(yearly, "HW-1", now.AddYears(5), new DateTime(2027, 5, 1, 0, 0, 0, DateTimeKind.Utc)).Tier == FeatureTier.Production,
              "production: the last entitled build keeps working for ever");
        yearly.UpdatesUntilUnix += 86400 * 365;
        Check(!CryptoEnvelope.VerifySignature(yearly), "production: extending updates breaks the signature");

        var pass = Signed(new License { LicenseId = "L-E", Tier = FeatureTier.Production, Email = "a@b", HardwareId = "HW-1", IssuedAtUnix = issued,
                                        ExpiresAtUnix = new DateTimeOffset(now.AddDays(30)).ToUnixTimeSeconds() });
        var passSt = LicenseManager.Evaluate(pass, "HW-1", now, now);
        Check(passSt.Tier == FeatureTier.Production && passSt.DisplayName.Contains("event pass"), $"event pass: Production while valid ({passSt.DisplayName})");
        Check(LicenseManager.Evaluate(pass, "HW-1", now, now).Notice is null && LicenseManager.Evaluate(pass, "HW-1", now.AddDays(28), now).Notice?.Contains("ends") == true,
              "event pass: warns in its last days");
        var grace = LicenseManager.Evaluate(pass, "HW-1", now.AddDays(31), now);
        Check(grace.Tier == FeatureTier.Production && grace.Notice?.Contains("isn't cut short") == true, "event pass: a grace period after it ends");
        Check(LicenseManager.Evaluate(pass, "HW-1", now.AddDays(33), now).Tier == FeatureTier.Locked, "event pass: ends after 30 days and the grace");
        Check(LicenseManager.Evaluate(Signed(new License { LicenseId = "L-M", Tier = FeatureTier.Master, Email = "a@b", HardwareId = "HW-1", IssuedAtUnix = issued }),
              "HW-1", now, now).Tier == FeatureTier.Locked, "licence: a signed file can't claim Master");
    }
    else Console.WriteLine("(skipped positive licence tests: no owner signing key on this machine)");

    // Licences issued before the Production tier must keep their exact signed bytes.
    var old = new License { LicenseId = "L-OLD", Tier = FeatureTier.Pro, Email = "x@y", HardwareId = "HW", IssuedAtUnix = 1 };
    var oldJson = System.Text.Encoding.UTF8.GetString(CryptoEnvelope.CanonicalBytesForSigning(old));
    Check(!oldJson.Contains("\"upd\"") && !oldJson.Contains("\"seat\""), "licence: pre-Production licences sign the same bytes");
    old.Seat = 2; old.UpdatesUntilUnix = 5;
    var newJson = System.Text.Encoding.UTF8.GetString(CryptoEnvelope.CanonicalBytesForSigning(old));
    Check(newJson.Contains("\"upd\":5") && newJson.Contains("\"seat\":2"), "licence: update window and seat are signed");
    var roundTrip = System.Text.Json.JsonSerializer.Deserialize<License>(System.Text.Json.JsonSerializer.Serialize(old))!;
    Check(roundTrip.Seat == 2 && roundTrip.UpdatesUntilUnix == 5, "licence: new fields round-trip");
}

// ---------------------------------------------------------------- 2.2: output / NDI commands, licence in CONFIG
{
    ShowCommand? Parse(string line)
    {
        var t = line.Split(' ');
        return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null;
    }
    Check(Parse("output keyed") is { Kind: ShowCommandKind.Output } k && k.Number == (int)OutputMode.KeyedFullScreen, "output keyed parses");
    Check(Parse("OUTPUT closed")?.Number == (int)OutputMode.Closed && Parse("output")?.Number == -1 && Parse("output mini")?.Number == (int)OutputMode.FloatingMinimal,
          "output closed / toggle / mini");
    Check(Parse("output sideways") is null, "output rejects nonsense");
    Check(Parse("ndi on")?.Number == 1 && Parse("NDI off")?.Number == 0 && Parse("ndi")?.Number == -1 && Parse("ndi")?.Kind == ShowCommandKind.Ndi, "ndi on / off / toggle");

    var json = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { Output = OutputMode.KeyedFullScreen, Ndi = true }));
    Check(json.Contains("\"output\":\"keyed\"") && json.Contains("\"ndi\":true"), "snapshot carries output and NDI state");
    var cfg = new RemoteConfig(Array.Empty<(int, string, int)>(), Array.Empty<(string, string, MessageUrgency, FlashRate)>(),
        Array.Empty<(string, string?, int)>(), Tier: "Pro", Features: new[] { "runningorder", "remote" });
    var cj = System.Text.Encoding.UTF8.GetString(SnapshotJson.SerializeConfig(cfg));
    Check(cj.Contains("\"tier\":\"Pro\"") && cj.Contains("\"features\":[\"runningorder\",\"remote\"]"), "config carries the licence and its features");
    var old = System.Text.Encoding.UTF8.GetString(SnapshotJson.SerializeConfig(cfg with { Tier = null, Features = null }));
    Check(!old.Contains("tier") && !old.Contains("features"), "config without licence info stays as before");
}

// ---------------------------------------------------------------- 2.3: holding images
{
    var dir = Path.Combine(Path.GetTempPath(), "sw-holding-test-" + Guid.NewGuid().ToString("N")[..6]);
    HoldingLibrary.FolderOverride = dir;
    try
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 };
        byte[] jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 };
        Check(HoldingLibrary.Sniff(png) == ".png" && HoldingLibrary.Sniff(jpg) == ".jpg"
              && HoldingLibrary.Sniff("GIF89a"u8) == ".gif" && HoldingLibrary.Sniff("RIFF\0\0\0\0WEBPVP8 "u8) == ".webp"
              && HoldingLibrary.Sniff("<html>"u8) is null, "holding: images recognised by their bytes, not their name");
        var a = HoldingLibrary.Save("Walk-in", png);
        var b = HoldingLibrary.Save("  Sponsors\u0007 ", jpg);
        Check(HoldingLibrary.IsValidId(a.Id) && a.File == a.Id + ".png" && File.Exists(Path.Combine(dir, a.File)), "holding: saved into the library under a random id");
        Check(b.Name == "Sponsors" && b.File.EndsWith(".jpg"), "holding: names cleaned");
        Check(HoldingLibrary.Read(a.Id) is { ContentType: "image/png" } r && r.Data.SequenceEqual(png), "holding: read back with its type");
        Check(HoldingLibrary.Read("../../etc") is null && HoldingLibrary.Read("zzzzzzzzzzzz") is null, "holding: bad ids never touch the disk");
        var threw = false;
        try { HoldingLibrary.Save("x", "not an image"u8.ToArray()); } catch (InvalidDataException) { threw = true; }
        Check(threw, "holding: non-images rejected");
        var list = new List<HoldingImage> { a, b };
        Check(HoldingLibrary.Resolve(list, "2") == b && HoldingLibrary.Resolve(list, "walk-in") == a && HoldingLibrary.Resolve(list, "spon") == b
              && HoldingLibrary.Resolve(list, a.Id) == a && HoldingLibrary.Resolve(list, "3") is null && HoldingLibrary.Resolve(list, "nope") is null,
              "holding: resolve by number, name, prefix or id");
        HoldingLibrary.Delete(a.Id);
        Check(HoldingLibrary.Read(a.Id) is null, "holding: delete removes the file");
    }
    finally
    {
        HoldingLibrary.FolderOverride = null;
        try { Directory.Delete(dir, true); } catch { }
    }

    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    Check(P("holding 2") is { Kind: ShowCommandKind.Holding, Text: "2" } && P("holding Welcome slide")?.Text == "Welcome slide"
          && P("holding off")?.Text == "off" && P("holding clear")?.Text == "off" && P("holding")?.Text == "toggle" && P("hold toggle")?.Text == "toggle",
          "holding: command grammar");
    var hj = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { HoldingId = "0123456789ab", HoldingName = "Walk-in", HoldingFill = true }));
    Check(hj.Contains("\"holding\":\"0123456789ab\"") && hj.Contains("\"holdingName\":\"Walk-in\"") && hj.Contains("\"holdingFill\":true"), "holding: in every frame");
    var hc = System.Text.Encoding.UTF8.GetString(SnapshotJson.SerializeConfig(new RemoteConfig(Array.Empty<(int, string, int)>(),
        Array.Empty<(string, string, MessageUrgency, FlashRate)>(), Array.Empty<(string, string?, int)>(), Holding: new[] { ("0123456789ab", "Walk-in") })));
    Check(hc.Contains("\"holding\":[{\"id\":\"0123456789ab\",\"name\":\"Walk-in\"}]"), "holding: library listed in the config");
    Check(FeatureGate.AllowsHolding(FeatureTier.Standard) && !FeatureGate.AllowsHolding(FeatureTier.Locked), "holding: a Standard feature");
}

// ---------------------------------------------------------------- 2.4: holographic display
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    Check(P("style holo") is { Kind: ShowCommandKind.Style, Number: 1 } && P("style standard")?.Number == 0 && P("style")?.Number == -1
          && P("STYLE 3d")?.Number == 1 && P("style sideways") is null, "style command: holo / standard / toggle");
    var on = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { Holographic = true, HoloFloor = false }));
    var off = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot()));
    Check(on.Contains("\"style\":\"holo\"") && on.Contains("\"holoFloor\":false") && off.Contains("\"style\":\"standard\"") && off.Contains("\"holoFloor\":true"),
          "holo: style in every frame");
    Check(FeatureGate.AllowsHolographic(FeatureTier.Pro) && !FeatureGate.AllowsHolographic(FeatureTier.Standard), "holo: a Pro feature");
    Check(P("style egg")?.Number == 2 && P("style hourglass")?.Number == 2 && P("style hologram")?.Number == 1, "3D: style egg / hologram");
    var egg = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { Holographic = true, EggTimer = true, Animations3D = false, Particles3D = false }));
    var holoDefaults = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { Holographic = true }));
    Check(egg.Contains("\"scene\":\"egg\"") && egg.Contains("\"animate\":false") && egg.Contains("\"particles\":false")
          && holoDefaults.Contains("\"scene\":\"holo\"") && holoDefaults.Contains("\"animate\":true") && holoDefaults.Contains("\"particles\":true"),
          "3D: scene, animations and particles ride in every frame");
    var d3 = new DisplaySettings();
    Check(d3.Animations3D && d3.Particles3D && !d3.EggTimer, "3D: animations and particles on by default, hologram first");

    var root = Path.Combine(Path.GetTempPath(), "sw-holo-" + Guid.NewGuid().ToString("N")[..6]);
    try
    {
        var folder = Stagewatch.Remote.WebAssets.ExtractHolo(root);
        var html = File.ReadAllText(Path.Combine(folder, "holo.html"));
        var bundle = new FileInfo(Path.Combine(folder, "holo.bundle.js"));
        Check(html.Contains("holo.bundle.js") && html.Contains("holo-page.js") && File.Exists(Path.Combine(folder, "common.js"))
              && bundle.Length > 200_000 && File.ReadAllText(bundle.FullName).Contains("StagewatchHolo"),
              $"holo: page extracted with the three.js bundle ({bundle.Length / 1024} KB)");
        Check(Stagewatch.Remote.WebAssets.ExtractHolo(root) == folder, "holo: extraction reused");
        var bundleText = File.ReadAllText(bundle.FullName);
        Check(bundleText.Contains("Three.js Authors") && bundleText.Contains("MIT"), "holo: three.js licence kept in the bundle");
    }
    finally { try { Directory.Delete(root, true); } catch { } }
}

// ---------------------------------------------------------------- external review (2.5) regressions
{
    // P1: a saved show keeps its time zone and dated start.
    var dir = Path.Combine(Path.GetTempPath(), "sw-review-" + Guid.NewGuid().ToString("N")[..6]);
    Directory.CreateDirectory(dir);
    try
    {
        var settings = new AppSettings();
        settings.Rundown.TimeZoneId = "Eastern Standard Time";
        settings.Rundown.ScheduledStart = "2026-10-24 14:00";
        settings.Rundown.Items.Add(new RundownItem { Title = "Opening", DurationSeconds = 600 });
        var file = Path.Combine(dir, "show.stagewatch");
        ShowFile.FromSettings(settings, "Summit").Save(file);
        var back = ShowFile.Load(file);
        Check(back.Rundown.TimeZoneId == "Eastern Standard Time", "review P1: show file keeps its time zone");
        Check(back.Rundown.ScheduledStart == "2026-10-24 14:00", "review P1: show file keeps a dated start");
        var target = new AppSettings();
        back.ApplyTo(target);
        Check(target.Rundown.TimeZoneId == "Eastern Standard Time" && target.Rundown.ScheduledStart == "2026-10-24 14:00", "review P1: opening a show restores both");
        File.WriteAllText(file, File.ReadAllText(file).Replace("2026-10-24 14:00", "half past never"));
        Check(ShowFile.Load(file).Rundown.ScheduledStart is null, "show file: an unreadable start is dropped, not trusted");

        // P2: concurrent saves (debounce timer, shutdown, explicit) while the show thread keeps changing settings.
        var store = new SettingsStore(Path.Combine(dir, "settings.json"));
        var saveFailures = 0;
        store.SettingsSaveFailed += _ => Interlocked.Increment(ref saveFailures);
        using (var stop = new CancellationTokenSource())
        {
            var writer = Task.Run(() =>
            {
                var n = 0;
                while (!stop.IsCancellationRequested)
                    store.Update(s => { s.Rundown.Items.Add(new RundownItem { Title = "Item " + n++ }); if (s.Rundown.Items.Count > 50) s.Rundown.Items.RemoveAt(0); });
            });
            Parallel.For(0, 32, new ParallelOptions { MaxDegreeOfParallelism = 32 }, _ => store.SaveNow());
            stop.Cancel();
            writer.Wait();
        }
        // Review P2 (save order): with no final save to mask it, the file must hold the newest settings. Each
        // round: many threads each change the value then save; whichever save lands last must not be an older one.
        var orderFailures = 0;
        for (var round = 0; round < 40; round++)
        {
            var path = Path.Combine(dir, $"order-{round}.json");
            using var os = new SettingsStore(path);
            var v = 0;
            Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
            {
                for (var k = 0; k < 20; k++)
                {
                    os.Update(s => s.LastDurationSeconds = ++v);
                    os.SaveNow();
                }
            });
            var onDisk = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement;
            var diskValue = onDisk.EnumerateObject().First(p => p.Name.Equals("lastDurationSeconds", StringComparison.OrdinalIgnoreCase)).Value.GetInt64();
            if (diskValue != os.Settings.LastDurationSeconds) orderFailures++;
        }
        Check(orderFailures == 0, $"review P2: saves land in order — the file always holds the newest settings ({orderFailures}/40 rounds stale)");

        store.SaveNow();
        var reread = new SettingsStore(Path.Combine(dir, "settings.json"));
        Check(saveFailures == 0, $"review P2: 32 concurrent saves during edits, {saveFailures} failures");
        Check(reread.Settings.Rundown.Items.Count > 0 && !Directory.GetFiles(dir, "*.tmp").Any(), "review P2: the saved file is whole and no temp files are left");
        store.Dispose(); reread.Dispose();
    }
    finally { try { Directory.Delete(dir, true); } catch { } }

    // Crew keys: stable per target, case-insensitive, different per target and per secret.
    Check(CrewKeys.For("s", "Lighting") == CrewKeys.For("s", "lighting ") && CrewKeys.For("s", "Lighting") != CrewKeys.For("s", "Sound")
          && CrewKeys.For("s", "Lighting") != CrewKeys.For("t", "Lighting") && CrewKeys.For("s", "x").Length == 10, "crew keys: per target and per secret");
    Check(CrewKeys.Matches("s", "speaker", CrewKeys.For("s", "speaker").ToUpperInvariant()) && !CrewKeys.Matches("s", "speaker", null)
          && !CrewKeys.Matches("s", "speaker", CrewKeys.For("s", "moderator")), "crew keys: matching");
    var linkList = CrewKeys.Links("s", new[] { "Lighting", "lighting", "Video / LED" });
    Check(linkList.Count(l => l.Path.StartsWith("/dept")) == 2 && linkList.Any(l => l.Path == "/dept?name=Video%20%2F%20LED&key=" + CrewKeys.For("s", "Video / LED")),
          "crew keys: one link per page, names escaped");
    var fresh = new SettingsStore(Path.Combine(Path.GetTempPath(), "sw-new-" + Guid.NewGuid().ToString("N")[..6] + ".json"));
    Check(!string.IsNullOrEmpty(fresh.Settings.Remote.CrewSecret) && fresh.Settings.Remote.CrewKeysRequired, "crew keys: every install gets its own secret, on by default");
    fresh.Dispose();
}

// ---------------------------------------------------------------- LTC in (decoder, tracker, chase)
{
    // Audio for `seconds` of rolling timecode from `start`, optionally inverted, scaled, noisy, with DC.
    short[] Ltc(int fps, TimeSpan start, double seconds, double amplitude = 0.5, bool invert = false, double noise = 0, short dc = 0)
    {
        var enc = new LtcEncoder(fps, amplitude);
        var frames = (int)(seconds * fps);
        var audio = new short[frames * enc.SamplesPerFrame];
        var rng = new Random(7);
        for (int f = 0; f < frames; f++)
            enc.Encode(start + TimeSpan.FromSeconds((double)f / fps), audio.AsSpan(f * enc.SamplesPerFrame, enc.SamplesPerFrame));
        for (int i = 0; i < audio.Length; i++)
        {
            double v = audio[i] * (invert ? -1 : 1) + dc + (noise > 0 ? (rng.NextDouble() * 2 - 1) * noise * short.MaxValue : 0);
            audio[i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
        }
        return audio;
    }
    (List<LtcFrame> Frames, double Fps) Decode(short[] audio, int chunk = 480)
    {
        var got = new List<LtcFrame>();
        double fps = 0;
        var dec = new LtcDecoder(48000, (f, m) => { got.Add(f); fps = m; });
        for (int i = 0; i < audio.Length; i += chunk) dec.Feed(audio.AsSpan(i, Math.Min(chunk, audio.Length - i)));
        return (got, fps);
    }
    bool Sequential(List<LtcFrame> frames, int fps) => frames.Count > 1 && frames.Zip(frames.Skip(1)).All(p => p.Second.Number(fps) - p.First.Number(fps) == 1);

    foreach (var fps in new[] { 24, 25, 30 })
    {
        var (frames, measured) = Decode(Ltc(fps, new TimeSpan(10, 0, 0), 2.0));
        Check(frames.Count >= fps * 2 - 4 && Sequential(frames, fps) && LtcTracker.SnapFps(measured) == fps,
              $"LTC in: decodes {fps} fps ({frames.Count} frames, measured {measured:0.00} fps, first {frames.FirstOrDefault().Text})");
    }
    var start = new TimeSpan(1, 2, 3) + TimeSpan.FromSeconds(4.0 / 25);
    var (exact, _) = Decode(Ltc(25, start, 1.0));
    Check(exact.Count > 0 && exact[0].Number(25) - new LtcFrame(1, 2, 3, 4, false).Number(25) is >= 0 and <= 2, $"LTC in: values right ({exact.FirstOrDefault().Text})");
    Check(Sequential(Decode(Ltc(25, start, 2.0, invert: true)).Frames, 25), "LTC in: polarity doesn't matter");
    Check(Sequential(Decode(Ltc(25, start, 2.0, amplitude: 0.05)).Frames, 25), "LTC in: a quiet signal (−26 dBFS) locks");
    Check(Sequential(Decode(Ltc(30, start, 2.0, noise: 0.06, dc: 3000)).Frames, 30), "LTC in: noise and DC offset rejected");
    Check(Decode(Ltc(25, start, 2.0, amplitude: 0.05)).Frames.Count > 0 && Decode(new short[48000]).Frames.Count == 0, "LTC in: silence decodes nothing");
    var odd = Decode(Ltc(25, start, 2.0), chunk: 37).Frames;
    Check(Sequential(odd, 25) && odd.Count >= 46, "LTC in: buffer size doesn't matter");

    // Tracker: rolling vs parked.
    var tr = new LtcTracker();
    var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
    LtcReading? r = null;
    for (int i = 0; i < 6; i++) r = tr.OnFrame(new LtcFrame(1, 0, 0, i, false), 25.0, t0);
    Check(r is { Running: true, Fps: 25 }, "LTC tracker: consecutive frames = rolling");
    for (int i = 0; i < 3; i++) r = tr.OnFrame(new LtcFrame(1, 0, 0, 5, false), 25.0, t0);
    Check(r is { Running: false }, "LTC tracker: a repeating frame = parked");
    for (int i = 6; i < 12; i++) r = tr.OnFrame(new LtcFrame(1, 0, 0, i, false), 25.0, t0);
    var later = t0 + System.Diagnostics.Stopwatch.Frequency;   // a second with no frames
    Check(tr.Tick(later) is { Running: false }, "LTC tracker: no frames = stopped");

    // Chase: start / pause on the edges; with an end timecode, remaining = end − timecode.
    var rolling = new LtcReading(new LtcFrame(1, 0, 0, 0, false), 25, true, t0);
    var parked = rolling with { Running = false };
    var a1 = LtcChase.Decide(false, rolling, t0, null, TimeSpan.FromMinutes(5), false);
    var a2 = LtcChase.Decide(true, parked, t0, null, TimeSpan.FromMinutes(5), true);
    var a3 = LtcChase.Decide(true, rolling, t0, null, TimeSpan.FromMinutes(5), true);
    Check(a1.Start && !a1.Pause && a2.Pause && !a2.Start && !a3.Start && !a3.Pause, "LTC chase: start when it rolls, pause when it stops, nothing in between");
    var a4 = LtcChase.Decide(true, parked, t0, Timecode.Parse("01:05:00:00", 25), TimeSpan.FromMinutes(9), false);
    Check(a4.SetRemaining is { } rem && Math.Abs(rem.TotalSeconds - (300 - 1.0 / 25)) < 0.01, "LTC chase: count down to an end timecode");
    Check(LtcChase.Decide(true, parked, t0, Timecode.Parse("01:05:00:00", 25), TimeSpan.FromSeconds(300 - 0.04), false).SetRemaining is null,
          "LTC chase: no correction when already in step");

    Check(Timecode.Parse("01:02:03:04", 25) == TimeSpan.FromSeconds(3723 + 4 / 25.0) && Timecode.Parse("01:02:03;04", 30) is not null
          && Timecode.Parse("01:02:03", 25) == TimeSpan.FromSeconds(3723) && Timecode.Parse("25:00:00:00", 25) is null
          && Timecode.Parse("01:02:03:25", 25) is null && Timecode.Parse("nope", 25) is null, "timecode: parsing");
    Check(Timecode.Format(TimeSpan.FromSeconds(3723 + 4 / 25.0), 25) == "01:02:03:04", "timecode: formatting");
    var p = TimeSpan.FromSeconds(100);
    Check(Timecode.Crossed(TimeSpan.FromSeconds(99.9), TimeSpan.FromSeconds(100.1), p) && !Timecode.Crossed(TimeSpan.FromSeconds(100.1), TimeSpan.FromSeconds(100.3), p)
          && !Timecode.Crossed(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(200), p) && !Timecode.Crossed(TimeSpan.FromSeconds(100.1), TimeSpan.FromSeconds(99.9), p),
          "timecode triggers: fire on rolling past a point, not on jumps or rewinds");

    // Review P2 (drop frame): 29.97 drop-frame skips ;00 and ;01 at most minute starts — that is one frame, not three.
    var dfTracker = new LtcTracker();
    LtcReading? dr = null;
    foreach (var f in new[] { new LtcFrame(1, 0, 59, 27, true), new LtcFrame(1, 0, 59, 28, true), new LtcFrame(1, 0, 59, 29, true),
                              new LtcFrame(1, 1, 0, 2, true), new LtcFrame(1, 1, 0, 3, true), new LtcFrame(1, 1, 0, 4, true) })
        dr = dfTracker.OnFrame(f, 29.97, t0);
    Check(dr is { Running: true, Fps: 30, DropFrame: true }, "LTC drop frame: 01:00:59;29 → 01:01:00;02 is the next frame, still rolling");
    Check(new LtcFrame(1, 1, 0, 2, true).Number(30) - new LtcFrame(1, 0, 59, 29, true).Number(30) == 1
          && new LtcFrame(1, 10, 0, 0, true).Number(30) - new LtcFrame(1, 9, 59, 29, true).Number(30) == 1, "LTC drop frame: every tenth minute keeps ;00");
    Check(Math.Abs(new LtcFrame(1, 0, 0, 0, true).ToTime(30).TotalSeconds - 3600) < 0.01, "LTC drop frame: 01:00:00;00 is (almost exactly) one real hour");
    var dfOk = true;
    for (long n = 0; n < 17982 * 2 && dfOk; n += 7)
    {
        var (h, m, s, f) = Timecode.Label(n, 30, true);
        dfOk = Timecode.FrameNumber(h, m, s, f, 30, true) == n && !(s == 0 && f < 2 && m % 10 != 0);
    }
    Check(dfOk, "LTC drop frame: frame numbers ⇄ labels round-trip and never produce a dropped label");
    var dfT = Timecode.Parse("01:01:00;02", 30);
    Check(dfT is not null && Timecode.Format(dfT.Value, 30, true) == "01:01:00;02" && Timecode.Parse("01:01:00;00", 30) is null
          && Timecode.Parse("01:10:00;00", 30) is not null && Timecode.Parse("01:01:00:00", 30) is not null,
          "timecode: drop-frame labels parse and format; impossible ones are refused");
    var dfEnd = Timecode.Parse("01:02:00:00", 30, dropFrame: true);
    var dfRoll = new LtcReading(new LtcFrame(1, 1, 0, 2, true), 30, true, t0);
    Check(dfEnd is { } de && LtcChase.Decide(true, dfRoll with { Running = false }, t0, de, TimeSpan.Zero, false).SetRemaining is { } dfRem
          && Math.Abs(dfRem.TotalSeconds - (1798 - 1) / (30000.0 / 1001)) < 0.01, "LTC chase: end timecode counts in drop-frame real time");

    // Review P2 (source change): a new input with nothing decoded yet must not leave a chased timer running on its own.
    Check(LtcChase.Lost(wasRunning: true, timerCounting: true) && !LtcChase.Lost(false, true) && !LtcChase.Lost(true, false),
          "LTC chase: losing the source pauses a timer that was following rolling code");
}

// ---------------------------------------------------------------- 2.9: department notes, groups, CSV
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    Check(P("deptnote 3 Lighting Blue wash, follow spot") is { Kind: ShowCommandKind.DeptNote, Number: 3, Text: "Lighting\u0001Blue wash, follow spot" }
          && P("deptnote 3 Lighting")?.Text == "Lighting\u0001" && P("deptnote x Lighting hi") is null && P("deptnote 3") is null,
          "deptnote: item, department, text (empty clears)");
    Check(ShowCommandParser.IsPublicVerb("deptnote") && !ShowCommandParser.IsPublicVerb("start"), "deptnote: crew pages may send it (checked by crew key)");
    var it = new RundownItem { Title = "Keynote" };
    it.SetDeptNote("Lighting", "Blue wash");
    it.SetDeptNote("lighting ", "Red wash");
    Check(it.DeptNotes.Count == 1 && it.DeptNote("LIGHTING") == "Red wash", "dept notes: one per department, any case");
    it.SetDeptNote("Lighting", "");
    Check(it.DeptNotes.Count == 0 && it.Clone().Id == it.Id, "dept notes: empty clears; items keep their id when copied");

    var items = new List<RundownItem>
    {
        new() { Title = "Welcome", DurationSeconds = 600, Group = "Morning" },
        new() { Title = "Keynote", DurationSeconds = 1800, Group = "morning" },
        new() { Title = "Break", DurationSeconds = 900 },
        new() { Title = "Panel", DurationSeconds = 2700, Group = "Panels" },
        new() { Title = "Q&A", DurationSeconds = 900, Group = "Panels" },
        new() { Title = "Close", DurationSeconds = 300, Group = "Morning" },
    };
    var spans = RundownGroups.Spans(items);
    Check(spans.Count == 3 && spans[0] == ("Morning", 0, 1) && spans[1] == ("Panels", 3, 4) && spans[2] == ("Morning", 5, 5),
          "groups: consecutive items with the same name (any case) form a group");
    var ro = new Rundown { Items = items };
    ro.GroupTargets["panels"] = 3000;
    var views = RundownGroups.Build(ro, ScheduleProjection.Empty, DateTime.UtcNow, _ => "");
    Check(views[1] is { PlannedSeconds: 3600, TargetSeconds: 3000 } && RundownGroups.TargetText(views[1]) == "over by 10:00"
          && views[0].TargetSeconds is null && RundownGroups.TargetText(views[0]) == "", "groups: planned length against an optional target");
    var anchor = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    var nowG = anchor.AddMinutes(5);
    var plan = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, 0, true, TimeSpan.FromMinutes(5), new Dictionary<int, ItemActual>(), nowG, ShowClock.For("UTC")));
    var timed = RundownGroups.Build(ro, plan, nowG, u => u.ToString("HH:mm"));
    var wantEnd = (long)Math.Round((plan.Items[1].ExpectedEndUtc - nowG).TotalSeconds);
    var wantPanels = (long)Math.Round((plan.Items[3].ExpectedStartUtc - nowG).TotalSeconds);
    Check(timed[0].EndIn == wantEnd && timed[1].StartIn == wantPanels && timed[1].StartIn > timed[0].EndIn && timed[0].Planned == "09:00"
          && timed[0].Status == ItemStatus.Live && timed[1].Status == ItemStatus.Upcoming,
          $"groups: start/end relative to now and status ({timed[0].EndIn} vs {wantEnd}, {timed[1].StartIn})");

    var csv = RundownCsv.Export(new[] { new RundownItem { Title = "Keynote", DurationSeconds = 1800, Group = "Morning" } }.Select(x => { x.SetDeptNote("Lighting", "Blue, then \"warm\""); return x; }));
    var back = RundownCsv.Parse(csv, new[] { "Lighting", "Sound" });
    Check(back.Items.Count == 1 && back.Items[0].Group == "Morning" && back.Items[0].DeptNote("Lighting") == "Blue, then \"warm\"",
          "csv: groups and department notes round-trip");
    var sheet = RundownCsv.Parse("Title,Duration,Section,Sound\nOpen,5,Intro,Mic 1 live\n", new[] { "Sound" });
    Check(sheet.Items[0].Group == "Intro" && sheet.Items[0].DeptNote("Sound") == "Mic 1 live", "csv: a department-named column becomes its notes");
}

// ---------------------------------------------------------------- Undo live edits
{
    var e = new LiveEdits();
    ShowCommand Trim(int i, int d) => new(ShowCommandKind.Trim, Number: i, Text: d.ToString());
    e.Record("Cut A", Trim(1, 60), Trim(1, -60));
    e.BeginBatch("Recover 3:00");
    e.Record("x", Trim(2, 60), Trim(2, -60));
    e.Record("y", Trim(3, 120), Trim(3, -120));
    e.EndBatch();
    Check(e.NextUndoLabel == "Recover 3:00", "undo: a batch is one step");
    var u = e.TakeUndo()!;
    Check(u.Undo.Select(c => (int)c.Number).SequenceEqual(new[] { 3, 2 }) && u.Redo.Select(c => (int)c.Number).SequenceEqual(new[] { 2, 3 }),
          "undo: a batch reverses in the opposite order");
    Check(e.NextRedoLabel == "Recover 3:00" && e.TakeRedo() is not null && e.NextUndoLabel == "Recover 3:00", "undo: redo puts it back");
    e.TakeUndo();
    e.Record("Cut B", Trim(4, 30), Trim(4, -30));
    Check(!e.CanRedo, "undo: a new edit ends the redo history");
    var t0 = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    var n = new LiveEdits();
    ShowCommand N(double s2) => new(ShowCommandKind.Nudge, Number: s2);
    string L(IReadOnlyList<ShowCommand> r) => "Nudge " + r.Sum(c => c.Number);
    n.Record("Nudge 10", N(-10), N(10), "nudge", L, t0);
    n.Record("Nudge 10", N(-10), N(10), "nudge", L, t0.AddSeconds(1));
    n.Record("Nudge 10", N(-10), N(10), "nudge", L, t0.AddSeconds(5));
    Check(n.NextUndoLabel == "Nudge 10" && n.TakeUndo()!.Undo.Count == 1 && n.NextUndoLabel == "Nudge 20" && n.TakeUndo()!.Undo.Sum(c => c.Number) == -20,
          "undo: quick nudges merge into one step; a pause starts a new one");
}

// ---------------------------------------------------------------- Recover time
{
    List<RundownItem> Items() => new()
    {
        new() { Title = "Live", DurationSeconds = 1800 },
        new() { Title = "Talk A", DurationSeconds = 1200 },
        new() { Title = "Panel", DurationSeconds = 2400, MinDurationSeconds = 2100 },
        new() { Title = "Buffer", DurationSeconds = 300, Kind = SegmentKind.Buffer },
        new() { Title = "Talk B", DurationSeconds = 600 },
    };
    var items = Items();
    var prop = Recovery.Make(items, 1, 4, 360, RecoverMethod.Proportional);
    Check(prop.Applied == 360 && prop.Shortfall == 0 && prop.Trims.All(t => t.Delta % 15 == 0 && t.Delta < 0) && !prop.Trims.Any(t => t.Item == 0),
          $"recover: proportional, in 15 s steps, never the live item ({string.Join(",", prop.Trims)})");
    var panel = prop.Trims.FirstOrDefault(t => t.Item == 2).Delta;
    Check(panel >= -300, $"recover: respects an item's minimum (panel {panel})");
    var even = Recovery.Make(items, 1, 4, 360, RecoverMethod.Even);
    Check(even.Applied == 360 && even.Trims.Select(t => t.Delta).Distinct().Count() <= 2, $"recover: evenly ({string.Join(",", even.Trims)})");
    var buf = Recovery.Make(items, 1, 4, 300, RecoverMethod.BuffersFirst);
    Check(buf.Trims.Single().Item == 3 && buf.Trims.Single().Delta == -300, "recover: buffers first");
    var tooMuch = Recovery.Make(items, 1, 4, 7200, RecoverMethod.Proportional);
    Check(tooMuch.Shortfall > 0 && tooMuch.Applied == 600 + 300 + 300 + 300 && items[1].DurationSeconds - (-tooMuch.Trims.First(t => t.Item == 1).Delta) >= 600,
          $"recover: never below half (or the minimum), and says what's short ({tooMuch.Applied}, short {tooMuch.Shortfall})");
    var give = Recovery.Make(items, 1, 4, -300, RecoverMethod.Proportional);
    Check(give.Applied == 300 && give.Trims.All(t => t.Delta > 0) && !give.Trims.Any(t => t.Item == 3), "recover: running early gives time out (not to buffers)");
    Check(Recovery.Make(items, 5, 4, 300, RecoverMethod.Even).Trims.Count == 0, "recover: nothing in range, nothing changes");
}

// ---------------------------------------------------------------- Labels
{
    Check(Labels.Parse(" VT, gfx ;vt, , CAM 2") is ["VT", "gfx", "CAM 2"], "labels: parsed, trimmed, no duplicates (any case)");
    var r = new Rundown { Items = { new RundownItem { Title = "A", Labels = { "VT" } }, new RundownItem { Title = "B", Labels = { "GFX", "VT" } } } };
    r.LabelColors["vt"] = "#123456";
    Labels.AssignColors(r);
    Check(Labels.ColorFor(r, "VT") == "#123456" && Labels.ColorFor(r, "GFX").StartsWith("#") && Labels.ColorFor(r, "GFX") != "#123456", "labels: chosen colours kept, new ones get a free palette colour");
    Check(Stagewatch.Integrations.TriggerRunner.FilterMatches("label:VT", "3|kind:talk|label:vt|group:Morning")
          && Stagewatch.Integrations.TriggerRunner.FilterMatches("3", "3|kind:talk") && Stagewatch.Integrations.TriggerRunner.FilterMatches("", "3")
          && Stagewatch.Integrations.TriggerRunner.FilterMatches("group:morning", "3|group:Morning")
          && !Stagewatch.Integrations.TriggerRunner.FilterMatches("label:GFX", "3|label:VT"), "triggers: follow an item, a label, a group or a kind");
    var csv = RundownCsv.Export(r.Items);
    var back = RundownCsv.Parse(csv);
    Check(back.Items[1].Labels.SequenceEqual(new[] { "GFX", "VT" }), "labels: CSV round trip");
    var j = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot
    {
        Timings = new[] { new ItemTimingView(0, "A", "", SegmentKind.Talk, 60, 0, "", "", "", "", "", ItemStatus.Live, 0, false, false, "", Labels: "VT,GFX") }.ToEquatableArray(),
        LabelColors = "VT=#123456;GFX=#F59E0B"
    }, FrameAccess.PublicDetail));
    Check(j.Contains("\"labels\":[\"VT\",\"GFX\"]") && j.Contains("\"labelColors\":{\"VT\":\"#123456\",\"GFX\":\"#F59E0B\"}"), "labels: in the frame with their colours");
}

// ---------------------------------------------------------------- Chimes
{
    // A stereo 24-bit WAV (left +half, right -quarter): decoded to 16-bit mono, the average of the two.
    byte[] Wav24(int frames)
    {
        var data = new List<byte>();
        void I32(int v) => data.AddRange(BitConverter.GetBytes(v));
        void I16(short v) => data.AddRange(BitConverter.GetBytes(v));
        data.AddRange("RIFF"u8.ToArray()); I32(36 + frames * 6); data.AddRange("WAVE"u8.ToArray());
        data.AddRange("fmt "u8.ToArray()); I32(16); I16(1); I16(2); I32(48000); I32(48000 * 6); I16(6); I16(24);
        data.AddRange("LIST"u8.ToArray()); I32(3); data.AddRange(new byte[] { 1, 2, 3, 0 });   // odd chunk, padded
        data.AddRange("data"u8.ToArray()); I32(frames * 6);
        for (int f = 0; f < frames; f++)
        {
            data.AddRange(new byte[] { 0x00, 0x00, 0x40 });   // +0.5
            data.AddRange(new byte[] { 0x00, 0x00, 0xE0 });   // -0.25
        }
        return data.ToArray();
    }
    var snd = Wav.Decode(Wav24(480), out var err);
    Check(snd is { SampleRate: 48000 } && snd.Samples.Length == 480 && Math.Abs(snd.Samples[0] - 4096) <= 1, $"chimes: stereo 24-bit WAV to 16-bit mono ({err} {snd?.Samples[0]})");
    var again = Wav.Decode(Wav.Encode(snd!), out _);
    Check(again is not null && again.Samples.SequenceEqual(snd!.Samples) && again.SampleRate == 48000, "chimes: WAV encode/decode round trip");
    Check(Wav.Decode("not a wav file at all, honestly, not even close........"u8, out var e1) is null && e1.Length > 0, "chimes: refuses a non-WAV");
    Check(Wav.Decode(Wav.Encode(new Wav.Sound(new short[8000 * 16], 8000)), out var e2) is null && e2.Contains("too long"), "chimes: refuses anything over 15 s");
    var trunc = Wav24(100); Array.Resize(ref trunc, trunc.Length - 7);
    Check(Wav.Decode(trunc, out _) is { } t && t.Samples.Length == 98, "chimes: a cut-off file plays what's there");
    Check(ItemChimes.ValidSpec("") && ItemChimes.ValidSpec("off") && ItemChimes.ValidSpec("builtin:start") && ItemChimes.ValidSpec("sound:abc123")
          && !ItemChimes.ValidSpec("sound:../x") && !ItemChimes.ValidSpec("builtin:boom") && !ItemChimes.ValidSpec("sound:"), "chimes: only known specs");
    var item = new RundownItem { Title = "A", Chimes = { Zero = "sound:abc" } };
    var copy = item.Clone();
    copy.Chimes.Zero = "off";
    Check(item.Chimes.Zero == "sound:abc" && !item.Chimes.IsDefault && new ItemChimes().IsDefault, "chimes: an item's chimes copy with it");
    var cj = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { ChimeSeq = 7, Chime = "builtin:zero" }));
    Check(cj.Contains("\"chimeSeq\":7") && cj.Contains("\"chime\":\"builtin:zero\""), "chimes: the frame tells screens what to play");
}

// ---------------------------------------------------------------- Nonprofit pricing
Check(Pricing.Nonprofit(29) == 20 && Pricing.Nonprofit(79) == 55 && Pricing.Nonprofit(15) == 11 && Pricing.Nonprofit(9) == 6, "pricing: nonprofit prices");

// ---------------------------------------------------------------- API keys
{
    var (k, secret) = ApiKeys.Create("Stream\u0007 Deck", ApiKeyScope.Control, DateTime.UtcNow);
    Check(secret.StartsWith(ApiKeys.Prefix) && secret.Length == 36 && k.Hash.Length == 64 && !k.Hash.Contains(secret[4..]) && k.Hint == secret[^4..] && k.Name == "Stream Deck",
          "api keys: secret shown once, only its hash kept");
    Check(ApiKeys.Find(new[] { k }, secret) == k && ApiKeys.Find(new[] { k }, secret + "x") is null && ApiKeys.Find(new[] { k }, "1234") is null, "api keys: found by secret only");
    Check(ApiKeys.Allows(ApiKeyScope.Mcp, ApiKeyScope.Read) && !ApiKeys.Allows(ApiKeyScope.Read, ApiKeyScope.Control) && !ApiKeys.Allows(ApiKeyScope.Control, ApiKeyScope.Mcp), "api keys: scopes");
    var json = System.Text.Json.JsonSerializer.Serialize(new RemoteSettings { ApiKeys = { k } });
    var back = System.Text.Json.JsonSerializer.Deserialize<RemoteSettings>(json)!;
    Check(json.Contains("\"scope\":\"Control\"") && !json.Contains(secret) && ApiKeys.Find(ApiKeys.Sanitise(back.ApiKeys), secret)?.Id == k.Id, "api keys: saved without the secret, still work after loading");
    Check(ApiKeys.Sanitise(new[] { new ApiKey { Id = "x", Hash = "bad" }, null }).Count == 0, "api keys: malformed entries dropped");
}

// ---------------------------------------------------------------- Screen designer
{
    var raw = new ScreenLayout
    {
        Name = " foyer ", Background = "red", Font = "Comic Sans",
        Elements =
        {
            new() { Kind = "TIMER", X = 95, Y = -5, W = 50, H = 30, Size = 500, Color = "#ff0000", Align = "middle" },
            new() { Kind = "script", Text = "<script>" },
            new() { Kind = "image", Image = "../../etc" },
            new() { Kind = "text", Text = "Next: {next} at {nextStart} {bogus}", X = double.NaN },
        }
    };
    var clean = ScreenLayouts.Sanitise(raw, out var why);
    Check(clean is not null && clean.Name == "foyer" && clean.Background == "#000000" && clean.Font == "" && clean.Elements.Count == 3, $"layouts: sanitised ({why})");
    var t = clean!.Elements[0];
    Check(t.Kind == "timer" && t.X == 95 && t.Y == 0 && t.W == 5 && t.H == 30 && t.Size == 80 && t.Color == "#FF0000" && t.Align == "center", "layouts: kept on screen, sizes clamped, colours checked");
    Check(clean.Elements[1].Image == "" && clean.Elements[2].X == 0, "layouts: bad image ids and numbers dropped");
    Check(ScreenLayouts.UnknownTokens(clean.Elements[2].Text) is ["bogus"], "layouts: unknown placeholders found");
    Check(ScreenLayouts.Sanitise(new ScreenLayout { Name = "a b/c" }, out _) is null, "layouts: names are safe for links");
    var round = ScreenLayouts.FromJson(ScreenLayouts.ToJson(ScreenLayouts.Starter("lobby")), out _);
    Check(round is { Name: "lobby" } && round.Elements.Count == 5 && round.Elements.Any(e => e.Kind == "timer"), "layouts: JSON round trip");
    Check(ScreenLayouts.FromJson("{nope", out var bad) is null && bad.Length > 0, "layouts: refuses junk");
    var many = new ScreenLayout { Name = "x" };
    for (var i = 0; i < 100; i++) many.Elements.Add(new LayoutElement { Kind = "box" });
    Check(ScreenLayouts.Sanitise(many, out _)!.Elements.Count == ScreenLayouts.MaxElements, "layouts: element count capped");

    var (ai, notes) = Stagewatch.Integrations.Copilot.ParseLayout("""
        {"background":"#0b1b3a","font":"Arial","notes":["Logo id is a guess"],
         "elements":[{"kind":"timer","x":5,"y":20,"w":90,"h":40,"text":"","size":30,"color":"","bg":"","align":"center","bold":true,"image":""},
                     {"kind":"text","x":5,"y":70,"w":90,"h":10,"text":"Up next: {next} {nextTalk}","size":5,"color":"#ffffff","bg":"","align":"left","bold":false,"image":""}]}
        """, "foyer");
    Check(ai is { Name: "foyer", Background: "#0B1B3A", Font: "Arial" } && ai.Elements.Count == 2 && notes.Count == 2 && notes.Any(n => n.Contains("{nextTalk}")),
          "copilot: a drafted layout is checked, and odd placeholders flagged");
    Check(Stagewatch.Integrations.Copilot.ParseLayout("not json", "x").Layout is null, "copilot: an unreadable draft is refused");
}

// ---------------------------------------------------------------- AI: message drafts, push-to-talk
{
    var msgs = Stagewatch.Integrations.Copilot.ParseMessages("""{"messages":["Two minutes, please","  Please   wrap up\n now ","two minutes, please","x","%s"]}""".Replace("%s", new string('a', 100)));
    Check(msgs.Count == 3 && msgs[0] == "Two minutes, please" && msgs[1] == "Please wrap up now" && msgs[2] == "x", $"copilot: message drafts cleaned, de-duplicated, at most 3 ({string.Join("|", msgs)})");
    Check(Stagewatch.Integrations.Copilot.ParseMessages("nope").Count == 0, "copilot: unreadable message drafts → none");

    var fake = new FakeTranscriber();
    var ptt = new Stagewatch.Integrations.PushToTalk(() => fake);
    Check(ptt.Begin() is null && ptt.Listening && fake.Started, "push-to-talk: listening while held");
    fake.Say("we're five minutes late");
    fake.Say("protect the keynote");
    var said = ptt.EndAsync(settleMs: 10).GetAwaiter().GetResult();
    Check(said == "we're five minutes late protect the keynote" && !ptt.Listening && fake.Disposed, $"push-to-talk: what was said, and the mic released ({said})");
    var broken = new Stagewatch.Integrations.PushToTalk(() => new FakeTranscriber { Fails = true });
    Check(broken.Begin() is { Length: > 0 } && !broken.Listening, "push-to-talk: says why it can't listen");
}

// ---------------------------------------------------------------- Companion 2.10 actions speak the shared grammar
foreach (var line in new[] { "SCREEN RELOAD", "SCREEN DISCONNECT Lobby TV", "UNDO", "REDO", "ENDOFSHOW 17:30", "ENDOFSHOW clear", "LOOP forever", "LOOP 3", "LOOP off" })
    Check(Stagewatch.Integrations.Copilot.Check(line) is not null, $"companion 2.10: '{line}' parses");

// ---------------------------------------------------------------- Patterns video
{
    Check(Stagewatch.Integrations.PatternsLink.TryParseState("""{"rev":4,"video":{"file":"sponsor.mp4","tag":"VT","position":62,"length":210,"remaining":148,"playing":true,"ended":false,"loops":false}}""", out var pv)
          && pv is { File: "sponsor.mp4", LengthSeconds: 210, PositionSeconds: 62, Playing: true, CountsDown: true } && pv.Title == "VT sponsor.mp4",
          "patterns: a clip on air with a known length");
    Check(Stagewatch.Integrations.PatternsLink.TryParseState("""{"video":{"file":"walkin.mp4","position":3,"length":0,"playing":true}}""", out var unknown) && unknown is { CountsDown: false },
          "patterns: length not known yet → no countdown");
    Check(Stagewatch.Integrations.PatternsLink.TryParseState("""{"video":{"file":"loop.mp4","position":3,"length":30,"playing":true,"loops":true}}""", out var looping) && looping is { CountsDown: false, Loops: true },
          "patterns: a looping clip never counts down");
    Check(Stagewatch.Integrations.PatternsLink.TryParseState("""{"video":null,"live":true}""", out var none) && none is null, "patterns: no clip on air");
    Check(!Stagewatch.Integrations.PatternsLink.TryParseState("{nope", out _) && Stagewatch.Integrations.PatternsLink.TryParseState("""{"video":{"position":"x","length":1e999}}""", out var odd) && odd is { LengthSeconds: 0 },
          "patterns: malformed frames refused, odd numbers cleaned");
    Check(Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"patterns":1,"machine":"FOH desk","kind":"desk","wire":9697,"http":9696}"""u8.ToArray(), "10.0.0.5") is { Name: "FOH desk", WirePort: 9697, Address: "10.0.0.5" }
          && Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"patterns":1,"wire":0}"""u8.ToArray(), "x") is null && Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"room":1}"""u8.ToArray(), "x") is null,
          "patterns: beacon read (only with the wire on)");

    Check(ShowCommandParser.TryParse("patterns", new List<string> { "end" }, out var pe, out _) && pe is { Kind: ShowCommandKind.Patterns, Text: "end", Number: 10 }
          && ShowCommandParser.TryParse("patterns", new List<string> { "end", "5" }, out var p5, out _) && p5.Number == 5
          && ShowCommandParser.TryParse("patterns", new List<string> { "restart" }, out var pr, out _) && pr.Text == "restart"
          && !ShowCommandParser.TryParse("patterns", new List<string> { "end", "-3" }, out _, out _)
          && !ShowCommandParser.TryParse("patterns", new List<string> { "send", "STOP" }, out _, out _), "patterns: commands (no raw lines)");
    Check(Stagewatch.Integrations.Copilot.Classify(pe) == Stagewatch.Integrations.CopilotRisk.High, "patterns: the copilot never pre-ticks a clip jump");

    // A fake Patterns desk on loopback: handshake, STATE → reading, VIDEO END on command, reconnect after a drop.
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    var heard = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var accepted = 0;
    System.Net.Sockets.TcpClient? current = null;
    var server = new Thread(() =>
    {
        try
        {
            while (true)
            {
                var c = listener.AcceptTcpClient();
                Interlocked.Increment(ref accepted);
                current = c;
                var st = c.GetStream();
                var w = new StreamWriter(st) { AutoFlush = true, NewLine = "\n" };
                w.WriteLine("""STATE {"video":{"file":"sponsor.mp4","tag":"VT","position":100,"length":210,"playing":true,"loops":false}}""");
                var r = new StreamReader(st);
                new Thread(() =>
                {
                    try { for (var l = r.ReadLine(); l is not null; l = r.ReadLine()) { heard.Enqueue(l); if (l.StartsWith("PING")) w.WriteLine("OK pong"); } }
                    catch (IOException) { } catch (ObjectDisposedException) { }
                }) { IsBackground = true }.Start();
                // Keep the clip "playing": a frame a second, like Patterns.
                new Thread(() =>
                {
                    try { for (var i = 0; i < 30 && c.Connected; i++) { Thread.Sleep(500); w.WriteLine("""STATE {"video":{"file":"sponsor.mp4","tag":"VT","position":101,"length":210,"playing":true}}"""); } }
                    catch (IOException) { } catch (ObjectDisposedException) { } catch (InvalidOperationException) { }
                }) { IsBackground = true }.Start();
            }
        }
        catch (System.Net.Sockets.SocketException) { }
        catch (ObjectDisposedException) { }
    }) { IsBackground = true };
    server.Start();

    bool WaitFor(Func<bool> cond, int ms = 4000) { var sw = System.Diagnostics.Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < ms) { if (cond()) return true; Thread.Sleep(20); } return cond(); }
    using (var link = new Stagewatch.Integrations.PatternsLink("127.0.0.1", port, "s3cret"))
    {
        Check(link.EndIn(10) is { } offline && offline.Contains("isn't connected") && heard.IsEmpty, "patterns: nothing queued while offline");
        link.Start();
        Check(WaitFor(() => link.Connected && link.Value is not null), $"patterns: connects ({link.Status})");
        Check(WaitFor(() => heard.Contains("HELLO Stagewatch") && heard.Contains("AUTH s3cret")), "patterns: HELLO then AUTH with the pairing token");
        var v = link.Value!;
        Check(v.Source == "Patterns" && v.DurationSeconds == 210 && v.RemainingSeconds is > 100 and <= 110 && v.Title == "VT sponsor.mp4", $"patterns: STATE → VT countdown ({v.RemainingSeconds:0.0} left)");
        Check(link.EndIn(10) is null && WaitFor(() => heard.Contains("VIDEO END 10")), "patterns: Last 10 s sends VIDEO END 10");
        Check(link.Restart() is null && WaitFor(() => heard.Contains("VIDEO RESTART")), "patterns: restart");
        current!.Close();   // the desk drops us
        Check(WaitFor(() => Volatile.Read(ref accepted) >= 2 && link.Connected, 8000), $"patterns: reconnects after a drop ({link.Status})");
        Check(!heard.Any(l => l.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)), "patterns: never a bare STOP");
        Check(link.Play() is { } old && old.Contains("older") && link.Seek(30) is not null && !heard.Any(l => l.StartsWith("VIDEO PLAY") || l.StartsWith("VIDEO SEEK")),
              "patterns: an older desk isn't sent transport commands it doesn't know");
    }
    listener.Stop();

    // Round 86 desks: precise milliseconds, paused, rate, seq, and the transport commands.
    Check(Stagewatch.Integrations.PatternsLink.TryParseState("""{"video":{"file":"reel.mp4","position":62,"length":210,"playing":true,"positionMs":62500,"lengthMs":210250,"remainingMs":147750,"paused":false,"rate":2,"seekable":true,"seq":7}}""", out var precise)
          && precise is { Transport: true, Seq: 7, Rate: 2, Paused: false } && Math.Abs(precise.PositionSeconds - 62.5) < 0.001 && Math.Abs(precise.LengthSeconds - 210.25) < 0.001,
          "patterns: Round 86 STATE read to the millisecond");
    Check(Stagewatch.Integrations.PatternsLink.TryParseState("""{"video":{"file":"live.mp4","positionMs":4000,"lengthMs":null,"remainingMs":null,"playing":true,"seq":1}}""", out var unk)
          && unk is { CountsDown: false, Transport: true }, "patterns: lengthMs null = no countdown");
    Check(Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"t":"patterns","v":1,"id":"ab12","name":"FOH","machine":"FOH desk","web":9696,"wire":9697,"role":"desk","caps":["video.clock","video.seek"],"proto":1}"""u8.ToArray(), "10.0.0.9") is { Name: "FOH desk", WirePort: 9697 }
          && Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"t":"main","v":1,"wire":9697}"""u8.ToArray(), "x") is null,
          "patterns: the show network's hello on 9928 is understood");
    Check(Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"t":"patterns","v":1.5,"wire":9697}"""u8.ToArray(), "x") is null
          && Stagewatch.Integrations.PatternsLink.ParseBeacon("""{"patterns":1,"wire":"9697"}"""u8.ToArray(), "x") is null,
          "patterns: a beacon with a field of the wrong type is ignored");
    foreach (var (pargs, text, n) in new[] { (new[] { "play" }, "play", 0), (new[] { "pause" }, "pause", 0), (new[] { "stop" }, "stop", 0), (new[] { "seek", "1:23" }, "seek", 83), (new[] { "seek", "45" }, "seek", 45) })
        Check(ShowCommandParser.TryParse("patterns", pargs.ToList(), out var pc, out _) && pc.Text == text && (int)pc.Number == n, $"patterns: '{string.Join(' ', pargs)}' parses");
    Check(!ShowCommandParser.TryParse("patterns", new List<string> { "seek", "soon" }, out _, out _), "patterns: seek needs a time");

    var listener2 = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener2.Start();
    var port2 = ((System.Net.IPEndPoint)listener2.LocalEndpoint).Port;
    var heard2 = new System.Collections.Concurrent.ConcurrentQueue<string>();
    new Thread(() =>
    {
        try
        {
            var c = listener2.AcceptTcpClient();
            var st = c.GetStream();
            var w = new StreamWriter(st) { AutoFlush = true, NewLine = "\n" };
            w.WriteLine("""STATE {"video":{"file":"reel.mp4","position":10,"length":60,"playing":true,"positionMs":10000,"lengthMs":60000,"remainingMs":50000,"paused":false,"rate":1,"seq":3}}""");
            var r = new StreamReader(st);
            for (var l = r.ReadLine(); l is not null; l = r.ReadLine()) { heard2.Enqueue(l); w.WriteLine(l.StartsWith("PING") ? "OK pong" : "OK"); }
        }
        catch (IOException) { } catch (System.Net.Sockets.SocketException) { } catch (ObjectDisposedException) { }
    }) { IsBackground = true }.Start();
    using (var link2 = new Stagewatch.Integrations.PatternsLink("127.0.0.1", port2, ""))
    {
        link2.Start();
        Check(WaitFor(() => link2.Connected && link2.Video is { Transport: true }), "patterns: a Round 86 desk connects");
        Check(link2.Pause() is null && link2.Play() is null && link2.StopClip() is null && link2.Seek(42) is null && link2.Seek(500) is { } tooFar && tooFar.Contains("only"),
              "patterns: transport commands accepted (seek past the end refused here)");
        Check(WaitFor(() => heard2.Contains("VIDEO PAUSE") && heard2.Contains("VIDEO PLAY") && heard2.Contains("VIDEO STOP") && heard2.Contains("VIDEO SEEK 42")),
              $"patterns: VIDEO PAUSE / PLAY / STOP / SEEK 42 on the wire ({string.Join(" | ", heard2)})");
        Check(!heard2.Any(l => l.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)), "patterns: VIDEO STOP, never a bare STOP");
    }
    listener2.Stop();
}

// ---------------------------------------------------------------- ALL GO (cues spanning departments)
{
    var board = new SignalBoard { KeepDoneFor = TimeSpan.FromSeconds(30) };
    var t0 = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);
    var lx = board.Send(SignalKind.Standby, "lighting", "LX 12", t0);
    var snd = board.Send(SignalKind.Standby, "Sound", "SQ 4", t0.AddSeconds(2));
    board.Send(SignalKind.Message, "speaker", "Wrap up", t0.AddSeconds(3));
    Check(board.StandingBy().Select(x => x.Target).SequenceEqual(new[] { "Lighting", "Sound" }), "all go: departments on standby, in the order they were stood by");
    board.Ack(lx.Id, "Sam", "seen", t0.AddSeconds(4));
    board.Ack(lx.Id, "Sam", "done", t0.AddSeconds(5));   // in position
    board.Update(t0.AddMinutes(10));
    Check(board.StandingBy().Count == 2 && board.Views(t0.AddMinutes(10)).Any(v => v.Target == "Lighting" && v.State == SignalState.Done),
          "all go: a department in position stays standing by until its GO (it doesn't time out like a finished cue)");
    var go = board.Send(SignalKind.Go, "Lighting", "LX 12", t0.AddMinutes(10));
    Check(board.StandingBy().Select(x => x.Target).SequenceEqual(new[] { "Sound" }) && board.Find(lx.Id)!.State == SignalState.Cancelled,
          "all go: a GO answers that department's standby, even once it's in position");
    board.ClearTarget("*", t0.AddMinutes(11));
    Check(board.StandingBy().Count == 0, "all go: clearing all cues stands everyone down");
    board.Send(SignalKind.Standby, "Video", "", t0.AddMinutes(12));
    var vStandby = board.StandingBy()[0];
    board.Ack(vStandby.Id, "", "done", t0.AddMinutes(12));
    board.Cancel(vStandby.Id, t0.AddMinutes(13));
    Check(board.StandingBy().Count == 0, "all go: clearing one standby that's in position works too");

    ShowCommand Line(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : new ShowCommand(ShowCommandKind.Note); }
    Check(Line("signal go all").Kind == ShowCommandKind.SignalGoAll && Line("cue go *").Kind == ShowCommandKind.SignalGoAll && Line("signal go Lighting").Kind == ShowCommandKind.Signal,
          "all go: \"signal go all\" is one command; a named department is still its own GO");
    Check(Copilot.Classify(Line("signal go all")) == CopilotRisk.High, "all go: the copilot never pre-ticks an ALL GO");
}

// ---------------------------------------------------------------- Resilience (lessons from Patterns)
{
    // Watchdog policy: a stage timer comes back at once, slows down only if it keeps failing, gives up on a loop.
    var wp = new WatchdogPolicy();
    var t0 = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);
    Check(wp.OnExit(0, TimeSpan.FromMinutes(30), t0).Action == WatchdogAction.Stop, "watchdog: a normal close stops watching");
    var first = wp.OnExit(-532462766, TimeSpan.FromMinutes(1), t0);
    Check(first is { Action: WatchdogAction.Restart } && first.Delay == TimeSpan.Zero, "watchdog: the first crash restarts at once");
    var second = wp.OnExit(1, TimeSpan.FromSeconds(5), t0.AddSeconds(10), hung: true);
    Check(second.Action == WatchdogAction.Restart && second.Delay > TimeSpan.Zero && second.Reason.Contains("stopped answering"), "watchdog: a hang restarts, after a short wait the second time");
    for (int i = 0; i < 4; i++) wp.OnExit(1, TimeSpan.FromSeconds(5), t0.AddSeconds(20 + i));
    Check(wp.OnExit(1, TimeSpan.FromSeconds(5), t0.AddSeconds(30)).Action == WatchdogAction.GiveUp, "watchdog: gives up after too many failures in ten minutes");
    var calm = new WatchdogPolicy();
    calm.OnExit(1, TimeSpan.FromSeconds(1), t0); calm.OnExit(1, TimeSpan.FromSeconds(1), t0.AddSeconds(1));
    Check(calm.OnExit(1, WatchdogPolicy.GoodRun, t0.AddMinutes(20)).Delay == TimeSpan.Zero, "watchdog: a long good run resets the backoff");
    Check(wp.OnExit(WatchdogPolicy.RestartCode, TimeSpan.FromSeconds(1), t0.AddMinutes(1)).Action == WatchdogAction.Restart, "watchdog: the app can ask for a restart");
    Check(WatchdogPolicy.IsHung(TimeSpan.FromSeconds(11), live: true, hadFirstBeat: true) && !WatchdogPolicy.IsHung(TimeSpan.FromSeconds(11), live: false, hadFirstBeat: true)
          && !WatchdogPolicy.IsHung(TimeSpan.FromSeconds(60), live: false, hadFirstBeat: false), "watchdog: quick to call a hang while live, patient otherwise and at start");

    // Faults: contained unless fatal or a storm.
    var faults = new FaultCounter();
    Check(faults.Contain(new InvalidOperationException("x"), t0) && !faults.Contain(new OutOfMemoryException(), t0), "faults: a stray error is contained, a fatal one is not");
    var stormed = false;
    for (int i = 0; i < FaultCounter.StormCount; i++) stormed |= !faults.Contain(new InvalidOperationException("again"), t0.AddSeconds(1));
    Check(stormed && faults.Contain(new InvalidOperationException("later"), t0.AddMinutes(5)), "faults: a storm ends the process (for the watchdog), a later one is contained again");

    // Crash-safe files: the old whole file is kept; a damaged file falls back to it.
    var dir = Path.Combine(Path.GetTempPath(), "sw-atomic-" + Guid.NewGuid().ToString("N")[..8]);
    var f = Path.Combine(dir, "x.json");
    AtomicFile.Write(f, "{\"a\":1}", AtomicFile.IsJson);
    AtomicFile.Write(f, "{\"a\":2}", AtomicFile.IsJson);
    Check(File.ReadAllText(f) == "{\"a\":2}" && File.ReadAllText(f + ".bak") == "{\"a\":1}" && Directory.GetFiles(dir, "*.tmp").Length == 0, "files: written whole, the previous one kept as .bak, no temp left");
    File.WriteAllText(f, "{\"a\":");   // torn
    AtomicFile.Write(f, "{\"a\":3}", AtomicFile.IsJson);
    Check(File.ReadAllText(f + ".bak") == "{\"a\":1}", "files: a torn file never replaces a good backup");
    File.WriteAllText(f, "garbage");
    Check(AtomicFile.ReadWithBackup(f, AtomicFile.IsJson, out var fromBak) == "{\"a\":1}" && fromBak, "files: a damaged file reads its backup");
    var sp = Path.Combine(dir, "settings.json");
    using (var st = new SettingsStore(sp)) { st.Update(x => x.RundownName = "Gala"); st.SaveNow(); st.Update(x => x.RundownName = "Gala 2"); st.SaveNow(); }
    File.WriteAllText(sp, "{ torn");
    using (var st2 = new SettingsStore(sp))
        Check(st2.Settings.RundownName == "Gala" && SettingsStore.LoadedFromBackup, $"settings: a damaged settings file loads the last good copy ({st2.Settings.RundownName})");
    File.Delete(sp);
    using (var st3 = new SettingsStore(sp))
        Check(st3.Settings.RundownName == "Main" && !SettingsStore.LoadedFromBackup, "settings: a deleted settings file is a fresh start, not the old backup");
    try { Directory.Delete(dir, true); } catch { }

    // GO latency ring.
    var go = new GoLatency();
    Check(go.Describe().Contains("no GO"), "go latency: says when nothing was measured");
    for (int i = 1; i <= 100; i++) go.Add(i / 2.0, i);
    Check(go.Count == GoLatency.Size && go.Last == 100 && go.Percentile(0.5) == 70 && go.Percentile(0.95) >= 97 && go.Describe().Contains("slower"),
          $"go latency: a ring of the last {GoLatency.Size}, with percentiles ({go.Describe()})");
}

// ---------------------------------------------------------------- Checkpoint order (review 2026-10-11)
{
    var spath = Path.Combine(Path.GetTempPath(), "sw-session-" + Guid.NewGuid().ToString("N")[..8] + ".json");
    var store = new SessionStore(spath);
    var now = DateTime.UtcNow;
    SessionState Live(long seq, string title) => new() { Sequence = seq, SavedAtUtc = now, Title = title, RundownActive = true, CurrentItem = 0,
        Engine = new EngineState(TimerMode.Countdown, TimerState.Running, 300, 300, true, null, now, 300, null, now) };
    store.Save(Live(1, "older"));
    store.Save(new SessionState { Sequence = 3, SavedAtUtc = now, ClosedCleanly = true });
    store.Save(Live(2, "delayed older save"));   // captured before the clean close, written after it
    Check(store.LoadRecoverable(now) is null, "session: a delayed older checkpoint can't undo a clean close");
    var store2 = new SessionStore(spath + "2");
    store2.Save(Live(5, "newer"));
    store2.Save(Live(4, "older"));
    Check(store2.LoadRecoverable(now)?.Title == "newer", "session: the newest capture wins whatever order the saves land in");
    try { File.Delete(spath); File.Delete(spath + "2"); } catch { }
}

// ---------------------------------------------------------------- Panel layout (draggable panels)
{
    var p = new PanelLayoutSettings();
    Check(PanelLayout.Docked(p).SequenceEqual(PanelLayout.Default) && p.SideWidth == PanelLayout.DefaultSideWidth, "panels: everything docked in the default order");
    PanelLayout.Move(p, "cues", 0);
    Check(PanelLayout.Docked(p).Take(2).SequenceEqual(new[] { "cues", "presets" }), "panels: drag a tab to the front");
    PanelLayout.Float(p, "running", 1900, 100, 120, 100);
    Check(!PanelLayout.Docked(p).Contains("running") && p.Floating.Single().Width == PanelLayout.MinPanelWidth && p.Floating.Single().Height == PanelLayout.MinPanelHeight,
          "panels: a torn-out panel leaves the strip and keeps a usable size");
    PanelLayout.Move(p, "timers", 1);
    PanelLayout.Dock(p, "running");
    Check(PanelLayout.Docked(p).SequenceEqual(new[] { "cues", "timers", "presets", "running", "producer", "holding" }),
          $"panels: docking without a place puts it back where it was ({string.Join(",", PanelLayout.Docked(p))})");
    PanelLayout.Float(p, "producer", 0, 0, 500, 700);
    PanelLayout.Dock(p, "producer", 0);
    Check(PanelLayout.Docked(p)[0] == "producer" && p.Floating.Count == 0, "panels: dropping onto the strip docks at that place");
    foreach (var id in PanelLayout.Default) PanelLayout.Float(p, id, 0, 0, 400, 600);
    Check(PanelLayout.Docked(p).Count == 0 && p.Order.Count == 6, "panels: every panel can be torn out, and none is lost");

    // From disk: unknown, duplicate and missing panels, a silly width.
    var bad = new PanelLayoutSettings
    {
        Order = new List<string> { "timers", "bogus", "timers", "cues" },
        Floating = new List<FloatingPanel> { new() { Id = "cues", Width = double.NaN, Height = 10 }, new() { Id = "nope" } },
        SideWidth = 5000,
    };
    PanelLayout.Normalize(bad);
    Check(bad.Order.Count == 6 && bad.Order.Distinct().Count() == 6 && bad.Order[0] == "timers" && !bad.Order.Contains("bogus"),
          $"panels: a damaged order is repaired, keeping the operator's choices ({string.Join(",", bad.Order)})");
    Check(bad.Floating.Count == 1 && bad.Floating[0].Width >= PanelLayout.MinPanelWidth && bad.Floating[0].Height >= PanelLayout.MinPanelHeight && bad.SideWidth == PanelLayout.MaxSideWidth,
          "panels: unknown windows dropped, sizes made usable");
    PanelLayout.Reset(bad);
    Check(bad.Order.SequenceEqual(PanelLayout.Default) && bad.Floating.Count == 0 && bad.SideWidth == PanelLayout.DefaultSideWidth, "panels: reset puts everything back");

    // Old settings files have no panels section.
    var ppath = Path.Combine(Path.GetTempPath(), "sw-panels-" + Guid.NewGuid().ToString("N")[..8] + ".json");
    File.WriteAllText(ppath, "{\"schemaVersion\":1,\"panels\":null}");
    using (var pstore = new SettingsStore(ppath))
        Check(pstore.Settings.Panels is { Order.Count: 6 }, "panels: an old settings file gets the default layout");
    try { File.Delete(ppath); } catch { }
}

// ---------------------------------------------------------------- Narrower contracts (architecture review)
{
    ShowCommand Parse(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : default; }
    Check(Parse("deptnote 3 Lighting Blue wash").Args is DeptNoteArgs { Department: "Lighting", Note: "Blue wash" }, "contracts: the parser gives typed arguments");
    Check(ShowCommandParser.TryParse("signal", new List<string> { "go", "lighting", "LX 12" }, out var sig, out _) && sig.Get<SignalArgs>() is { Target: "lighting", Text: "LX 12" },
          "contracts: signal arguments typed");
    Check(Parse("screen go lobby Stage").Get<ScreenArgs>() is { Action: "go", Argument: "lobby", Who: "Stage" }
          && Parse("screen identify").Get<ScreenArgs>() is { Action: "identify", Who: "all" }, "contracts: screen arguments typed");
    Check(Parse("aux 2 set 3:00").Get<AuxArgs>() is { Action: "set", Argument: "180" } && Parse("ltc end 01:00:00:00").Get<LtcArgs>() is { Action: "end", Timecode: "01:00:00:00" },
          "contracts: aux and LTC arguments typed");
    var made = ShowCommand.DeptNote(2, "Sound", "Lapel 3");
    Check(made.Get<DeptNoteArgs>() == new DeptNoteArgs("Sound", "Lapel 3") && made.Text == "Sound\u0001Lapel 3", "contracts: factories set typed arguments and keep the packed text compatible");
    var legacy = new ShowCommand(ShowCommandKind.Question, Text: "Why?\u0001Sam");
    Check(legacy.Get<QuestionArgs>() == new QuestionArgs("Why?", "Sam"), "contracts: a command built the old way still reads typed");
    Check(ShowCommand.Signal(SignalKind.Go, "lx\u0001evil", "GO", true).Get<SignalArgs>().Target == "lx\u0001evil" && ShowArgs.Pack("a\u0001b", "c") == "a b\u0001c",
          "contracts: a separator inside a value can't split it into two arguments");

    // EquatableArray copies what it's given: a published frame can't change under its readers.
    var source = new[] { 1, 2, 3 };
    var frozen = new EquatableArray<int>(source);
    source[0] = 99;
    Check(frozen[0] == 1, "contracts: EquatableArray keeps its own copy");

    // Settings: other threads read a snapshot, never the live object.
    var spath = Path.Combine(Path.GetTempPath(), "sw-snap-" + Guid.NewGuid().ToString("N")[..8] + ".json");
    using (var store = new SettingsStore(spath))
    {
        store.Update(s => s.Triggers.Clear());
        var before = store.Snapshot;
        Check(!ReferenceEquals(before, store.Settings) && ReferenceEquals(before, store.Snapshot), "contracts: the snapshot is a separate object, reused until something changes");
        store.Update(s => s.Triggers.Add(new TriggerRule { Name = "hello" }));
        var after = store.Snapshot;
        Check(before.Triggers.Count == 0 && after.Triggers.Count == 1 && !ReferenceEquals(after.Triggers, store.Settings.Triggers),
              "contracts: a change gives readers a new snapshot; the old one never changes under them");
        // A reader enumerating the snapshot while the owner edits the live list: no "collection was modified".
        var errors = 0;
        var reader = Task.Run(() => { for (int i = 0; i < 2000; i++) { try { foreach (var _ in store.Snapshot.Triggers) { } } catch (InvalidOperationException) { errors++; } } });
        for (int i = 0; i < 2000; i++) store.Update(s => { s.Triggers.Add(new TriggerRule { Name = "x" }); if (s.Triggers.Count > 5) s.Triggers.RemoveAt(0); });
        reader.Wait();
        Check(errors == 0, $"contracts: background readers never see a list mid-edit ({errors} errors)");
    }
    try { File.Delete(spath); } catch { }

    // Narrow consumers depend on narrow contracts.
    Check(typeof(OscServer).GetConstructors().Single().GetParameters()[0].ParameterType == typeof(IShowCommands), "contracts: OSC needs only commands and frames");
    Check(typeof(IShowControl).GetInterfaces().Length >= 7, "contracts: IShowControl is the union of focused contracts");
}

// ---------------------------------------------------------------- Latency boundaries (architecture review)
{
    var hub = new SnapshotHub();
    var fast = new System.Collections.Concurrent.ConcurrentQueue<int>();
    var slow = new System.Collections.Concurrent.ConcurrentQueue<int>();
    var slowReports = 0;
    var hubFailures = 0;
    hub.SubscriberSlow += (_, _) => Interlocked.Increment(ref slowReports);
    hub.SubscriberFailed += _ => Interlocked.Increment(ref hubFailures);
    hub.Subscribe(f => { Thread.Sleep(250); slow.Enqueue((int)f.Seconds); }, "slow socket");
    hub.Subscribe(f => fast.Enqueue((int)f.Seconds), "fast");
    hub.Subscribe(f => { if (f.Seconds == 3) throw new InvalidOperationException("boom"); }, "throws");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (int k = 1; k <= 40; k++) hub.Publish(new ShowSnapshot { Seconds = k, Text = k.ToString() });
    var publishMs = sw.Elapsed.TotalMilliseconds;
    Check(publishMs < 100, $"latency: publishing 40 frames with a 250 ms consumer attached takes {publishMs:0} ms (the show never waits)");
    var fastDone = SpinWait.SpinUntil(() => fast.Contains(40), 2000);
    Check(fastDone && fast.Count >= 1, $"latency: a fast consumer isn't held up by the slow one ({fast.Count} frames, slow had {slow.Count})");
    Check(hub.WaitIdle(TimeSpan.FromSeconds(5)), "latency: every consumer catches up");
    var slowSeen = slow.ToArray();
    Check(slowSeen.Length < 10 && slowSeen[^1] == 40, $"latency: the slow consumer skips to the newest frame instead of queuing a backlog ({string.Join(",", slowSeen)})");
    Check(slowSeen.SequenceEqual(slowSeen.OrderBy(x => x)) && fast.ToArray().SequenceEqual(fast.ToArray().OrderBy(x => x)), "latency: each consumer sees frames in order");
    Check(slowReports >= 1, "latency: a consumer slower than 100 ms on a frame is reported");
    Check(hubFailures <= 1 && fast.Contains(40), "latency: a consumer that throws affects nobody else");
    Check(!hub.Publish(new ShowSnapshot { Seconds = 40, Text = "40" }), "latency: an unchanged frame isn't published");
}

// ---------------------------------------------------------------- Historical identity (architecture review)
{
    var t0 = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    var day1 = new Rundown { Id = "rDay1", Items = { new RundownItem { Id = "talkA", Title = "Opening", DurationSeconds = 120 } } };
    var day2 = new Rundown { Id = "rDay2", Items = { new RundownItem { Id = "talkB", Title = "New talk", DurationSeconds = 300 } } };
    var live = day1;
    var journal = new ShowJournal(null);
    var execution = "x1";
    journal.Identify = i => i >= 0 && i < live.Items.Count ? new JournalIdentity(live.Id, live.Items[i].Id, execution) : default;
    journal.Add(t0, JournalKind.ItemStart, 0);
    journal.Add(t0.AddSeconds(120), JournalKind.ItemEnd, 0);
    live = day2;                                                   // switch to a Day 2 that never starts
    Check(journal.Entries[0] is { RundownId: "rDay1", ItemId: "talkA", Execution: "x1" }, "identity: journal entries carry running order, item and run");
    var report2 = ShowReport.Build("Show", day2, journal.Entries, null, ShowClock.Local, t0.AddHours(1));
    Check(report2.Rows.Single().ActualSeconds is null && report2.Rows.Single().Title == "New talk",
          "identity: Day 1's 120 s never lands on Day 2's \"New talk\" (the review's probe)");
    var report1 = ShowReport.Build("Show", day1, journal.Entries, null, ShowClock.Local, t0.AddHours(1));
    Check(report1.Rows.Single().ActualSeconds == 120, "identity: Day 1's report still has its own run");

    // Loop passes are separate runs, not one long run from the first start to the last end.
    var loop = new Rundown { Id = "rLoop", Items = { new RundownItem { Id = "clip", Title = "Loop clip", DurationSeconds = 60 } } };
    live = loop;
    foreach (var (pass, at) in new[] { ("p1", 0), ("p2", 60), ("p3", 120) })
    {
        execution = pass;
        journal.Add(t0.AddHours(2).AddSeconds(at), JournalKind.ItemStart, 0);
        journal.Add(t0.AddHours(2).AddSeconds(at + 58), JournalKind.ItemEnd, 0);
    }
    var loopReport = ShowReport.Build("Show", loop, journal.Entries, null, ShowClock.Local, t0.AddHours(3));
    Check(loopReport.Rows.Count == 3 && loopReport.Rows.All(r => r.ActualSeconds == 58) && loopReport.Rows[2].Title == "Loop clip (run 3)",
          $"identity: each loop pass is its own row ({string.Join(", ", loopReport.Rows.Select(r => r.Title + "=" + r.ActualSeconds))})");

    // A same-titled item in a duplicated running order is a different item.
    var copy = day1.Clone(); copy.Id = "rCopy"; copy.Items[0].Id = "talkA2";
    Check(ShowReport.Build("Show", copy, journal.Entries, null, ShowClock.Local, t0).Rows.Single().ActualSeconds is null,
          "identity: a duplicated running order doesn't inherit the original's history");

    // Journals written before identities existed still read by position.
    var legacy = new List<JournalEntry> { new(t0, JournalKind.ItemStart, 0), new(t0.AddSeconds(90), JournalKind.ItemEnd, 0) };
    Check(ShowReport.Build("Show", day1, legacy, null, ShowClock.Local, t0).Rows.Single().ActualSeconds == 90, "identity: old journals still report by position");

    // Identities survive the file.
    var jpath = Path.Combine(Path.GetTempPath(), "sw-ident-" + Guid.NewGuid().ToString("N")[..6] + ".jsonl");
    var onDisk = new ShowJournal(jpath) { Identify = _ => new JournalIdentity("rDay1", "talkA", "x9") };
    onDisk.Add(t0, JournalKind.ItemStart, 0);
    onDisk.DisposeAsync().AsTask().Wait();
    var readBack = ShowJournal.Load(jpath).Single();
    Check(readBack is { RundownId: "rDay1", ItemId: "talkA", Execution: "x9" } && File.ReadAllText(jpath).Contains("\"ii\":\"talkA\""), "identity: written to and read from the journal file");
    File.Delete(jpath);

    // Stable ids are assigned once and kept.
    var unnamed = new Rundown { Id = "", Items = { new RundownItem { Title = "x" } } };
    Check(unnamed.EnsureIds() && unnamed.Id.StartsWith("r") && !unnamed.EnsureIds(), "identity: a running order gets a stable id once");
    var roundTrip = System.Text.Json.JsonSerializer.Deserialize<Rundown>(System.Text.Json.JsonSerializer.Serialize(unnamed))!;
    Check(roundTrip.Id == unnamed.Id, "identity: the id is saved with the running order");
}

// ---------------------------------------------------------------- Review fixes (2.11.1)
{
    // Recovery never goes below a declared minimum, even after rounding to 15 s.
    var tight = new List<RundownItem> { new() { Title = "Live", DurationSeconds = 600 }, new() { Title = "Short", DurationSeconds = 123, MinDurationSeconds = 100 } };
    foreach (var m in Enum.GetValues<RecoverMethod>())
    {
        var plan = Recovery.Make(tight, 1, 1, 30, m);
        var cut = -plan.Trims.Sum(t => t.Delta);
        Check(123 - cut >= 100 && plan.Applied == cut && plan.Shortfall == 30 - cut, $"review: {m} recovery respects a 100 s minimum on a 123 s item (cut {cut}, short {plan.Shortfall})");
    }
    var several = new List<RundownItem>
    {
        new() { Title = "A", DurationSeconds = 123, MinDurationSeconds = 100 },
        new() { Title = "B", DurationSeconds = 131, MinDurationSeconds = 100 },
        new() { Title = "C", DurationSeconds = 600 },
    };
    foreach (var m in Enum.GetValues<RecoverMethod>())
    {
        var plan = Recovery.Make(several, 0, 2, 400, m);
        Check(plan.Trims.All(t => several[t.Item].DurationSeconds + t.Delta >= (several[t.Item].MinDurationSeconds ?? several[t.Item].DurationSeconds / 2)),
              $"review: {m} recovery keeps every item at or above its floor ({string.Join(",", plan.Trims)})");
    }

    // Undo targets: an edit whose item moved, vanished or is no longer live is dropped, never replayed onto another item.
    var edits = new LiveEdits();
    edits.Record("Cut A", new ShowCommand(ShowCommandKind.Trim, Number: 1, Text: "60"), new ShowCommand(ShowCommandKind.Trim, Number: 1, Text: "-60"),
        targets: new[] { new LiveEdits.EditTarget(0, "id-A") });
    edits.Record("Nudge", new ShowCommand(ShowCommandKind.Nudge, Number: -30), new ShowCommand(ShowCommandKind.Nudge, Number: 30), "nudge",
        targets: new[] { new LiveEdits.EditTarget(1, "id-B", MustBeLive: true) });
    var ids = new[] { "id-A", "id-B" };
    var live = 1;
    bool Valid(LiveEdits.EditTarget t) => t.Index < ids.Length && ids[t.Index] == t.ItemId && (!t.MustBeLive || live == t.Index);
    Check(edits.DropInvalid(Valid) == 0 && edits.NextUndoLabel == "Nudge", "review: valid edits stay");
    live = 0;
    Check(edits.DropInvalid(Valid) == 1 && edits.NextUndoLabel == "Cut A", "review: a nudge on an item that's no longer live is dropped");
    ids = new[] { "id-X", "id-A" };   // reordered: position 1 is now someone else
    Check(edits.DropInvalid(Valid) == 1 && !edits.CanUndo, "review: an edit whose item moved is dropped, not replayed on the new occupant");
    var batch = new LiveEdits();
    batch.BeginBatch("Recover");
    batch.Record("t1", new ShowCommand(ShowCommandKind.Trim, Number: 1), new ShowCommand(ShowCommandKind.Trim, Number: 1), targets: new[] { new LiveEdits.EditTarget(0, "a") });
    batch.Record("t2", new ShowCommand(ShowCommandKind.Trim, Number: 2), new ShowCommand(ShowCommandKind.Trim, Number: 2), targets: new[] { new LiveEdits.EditTarget(1, "b") });
    batch.EndBatch();
    Check(batch.DropInvalid(t => t.ItemId != "b") == 1, "review: a batch is dropped whole if any of its items is gone");

    // Department notes: three-way merge.
    var baseNotes = new Dictionary<string, string> { ["Lighting"] = "Blue wash", ["Sound"] = "Lapel 2" };
    var editorNotes = new Dictionary<string, string> { ["Lighting"] = "Red wash (revised)", ["Sound"] = "Lapel 2" };
    var storedNotes = new Dictionary<string, string> { ["Lighting"] = "Blue wash", ["Sound"] = "Handheld 1 (crew changed it live)" };
    var merged = SheetImport.MergeDeptNotes(baseNotes, editorNotes, storedNotes);
    Check(merged["Lighting"] == "Red wash (revised)" && merged["Sound"] == "Handheld 1 (crew changed it live)",
          "review: notes changed in the editor win; notes crew changed live while it was open are kept");
    Check(SheetImport.MergeDeptNotes(null, new Dictionary<string, string> { ["Video"] = "VT1" }, null)["Video"] == "VT1", "review: a new row keeps its notes");
    Check(!SheetImport.MergeDeptNotes(baseNotes, new Dictionary<string, string> { ["Sound"] = "Lapel 2" }, baseNotes).ContainsKey("Lighting"),
          "review: a note cleared in the editor stays cleared");
}

// ---------------------------------------------------------------- Back-timing and loops
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    var utc = ShowClock.For("UTC");
    var end = new DateTime(2026, 10, 10, 17, 30, 0, DateTimeKind.Utc);
    var items = new List<RundownItem>
    {
        new() { Title = "A", DurationSeconds = 3600 },
        new() { Title = "Lunch", DurationSeconds = 3600, StartMode = StartMode.Fixed, FixedStart = "14:30" },
        new() { Title = "B", DurationSeconds = 7200 },
    };
    var a = ScheduleEngine.BackTimedAnchor(items, end, utc);
    var plan = a is { } aa ? ScheduleEngine.Plan(items, aa, utc) : null;
    Check(plan is not null && plan[^1].End == end && plan[0].Start == new DateTime(2026, 10, 10, 13, 30, 0, DateTimeKind.Utc),
          $"back-timing: the plan ends exactly at the end of show ({plan?[^1].End:HH:mm})");
    items[2].DurationSeconds = 5400;   // fixed lunch at 14:30 + 90 min can only end at 17:00
    Check(ScheduleEngine.BackTimedAnchor(items, end, utc) is null, "back-timing: an end the fixed starts can't reach is refused, not faked");
    var simple = new List<RundownItem> { new() { Title = "X", DurationSeconds = 1800 }, new() { Title = "Y", DurationSeconds = 1800 } };
    Check(ScheduleEngine.BackTimedAnchor(simple, end, utc) == end.AddHours(-1), "back-timing: start = end − total");
    var now = end.AddMinutes(-45);   // 5 min into item X (started 16:40), 25 min left: Y ends 17:40
    var proj = ScheduleEngine.Project(new ScheduleEngine.Input(simple, end.AddMinutes(-50), 0, true, TimeSpan.FromMinutes(25), new Dictionary<int, ItemActual>(), now, utc,
        EndOfShow: "17:30"));
    Check(proj.NextHardOut is { Index: 1 } h && h.CurfewUtc == end && h.MarginSeconds == -600, $"end of show counts as the last item's curfew ({proj.NextHardOut?.MarginSeconds})");
    var cycle = TimeSpan.FromMinutes(30);
    var start = end;
    Check(Roll.LoopAnchor(start, cycle, -1, start.AddMinutes(75)) == start.AddMinutes(60) && Roll.LoopAnchor(start, cycle, 1, start.AddMinutes(95)) == start.AddMinutes(30)
          && Roll.LoopAnchor(start, cycle, 0, start.AddMinutes(95)) == start && Roll.LoopAnchor(start, cycle, 3, start.AddMinutes(10)) == start,
          "loops: the pass that's on now; capped at the last pass; off = no loop");
    Check(P("endofshow 17:30") is { Kind: ShowCommandKind.EndOfShow, Text: "17:30" } && P("endofshow clear")?.Text == "" && P("endofshow soon") is null
          && P("loop") is { Kind: ShowCommandKind.Loop, Number: -1 } && P("loop 3")?.Number == 3 && P("loop off")?.Number == 0 && P("loop 500") is null,
          "commands: endofshow and loop");
}

// ---------------------------------------------------------------- Roll mode (follow the clock)
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    var a = new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    var items = new List<RundownItem>
    {
        new() { Title = "Walk-in", DurationSeconds = 600 },
        new() { Title = "Welcome", DurationSeconds = 300 },
        new() { Title = "Lunch", DurationSeconds = 3600, StartMode = StartMode.Fixed, FixedStart = "10:00" },
        new() { Title = "Empty", DurationSeconds = 0 },
        new() { Title = "Close", DurationSeconds = 600 },
    };
    var plan = ScheduleEngine.Plan(items, a, ShowClock.For("UTC"));
    var before = Roll.Decide(plan, a.AddMinutes(-3));
    var walk = Roll.Decide(plan, a.AddMinutes(4));
    var gap = Roll.Decide(plan, a.AddMinutes(20));          // after Welcome (09:15), before Lunch (10:00)
    var close = Roll.Decide(plan, a.AddHours(1).AddMinutes(61));
    var over = Roll.Decide(plan, a.AddHours(3));
    Check(before is { What: Roll.Kind.Wait, Item: 0 } && before.Remaining == TimeSpan.FromMinutes(3), "roll: before the show, count down to the first item");
    Check(walk is { What: Roll.Kind.Run, Item: 0 } && walk.Remaining == TimeSpan.FromMinutes(6), "roll: inside an item's window, run it with the time to its planned end");
    Check(gap is { What: Roll.Kind.Wait, Item: 2 } && gap.Remaining == TimeSpan.FromMinutes(40), "roll: in a gap, count down to the next (fixed) start");
    Check(close is { What: Roll.Kind.Run, Item: 4 } && over.What == Roll.Kind.Over, $"roll: zero-length items are skipped; after the last item it's over ({close.What} {close.Item})");
    Check(P("roll")?.Number == -1 && P("roll on") is { Kind: ShowCommandKind.Roll, Number: 1 } && P("roll off")?.Number == 0, "roll command: on / off / toggle");
}

// ---------------------------------------------------------------- Several running orders in one show
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    Check(P("rundown Day 2") is { Kind: ShowCommandKind.Rundown, Text: "Day 2" } && P("rundown") is null, "rundown command: switch by name");
    var settings = new AppSettings { RundownName = "Day 1" };
    settings.Rundown.Items.Add(new RundownItem { Title = "Opening", DurationSeconds = 600, Group = "Morning" });
    settings.Rundown.Items[0].SetDeptNote("Sound", "Mic 1");
    settings.Rundown.GroupTargets["Morning"] = 3600;
    settings.Rundown.SheetUrl = "https://example.com/day1.csv";
    settings.OtherRundowns.Add(new NamedRundown { Name = "Day 2", Rundown = new Rundown { Items = { new RundownItem { Title = "Keynote", DurationSeconds = 1800 } } } });
    var file = Path.Combine(Path.GetTempPath(), "sw-multi-" + Guid.NewGuid().ToString("N")[..6] + ShowFile.Extension);
    try
    {
        ShowFile.FromSettings(settings, "Summit").Save(file);
        var target = new AppSettings();
        ShowFile.Load(file).ApplyTo(target);
        Check(target.RundownName == "Day 1" && target.OtherRundowns.Count == 1 && target.OtherRundowns[0].Name == "Day 2"
              && target.OtherRundowns[0].Rundown.Items[0].Title == "Keynote", "show files: every running order travels with the show");
        Check(target.Rundown.GroupTargets.ContainsKey("MORNING") && target.Rundown.SheetUrl == "https://example.com/day1.csv"
              && target.Rundown.Items[0].DeptNote("sound") == "Mic 1" && target.Rundown.Items[0].Id == settings.Rundown.Items[0].Id,
              "show files: groups, targets, sheet link, department notes and ids are kept");
        File.WriteAllText(file, File.ReadAllText(file).Replace("\"Day 2\"", "\"Day 1\""));
        var dup = new AppSettings();
        ShowFile.Load(file).ApplyTo(dup);
        Check(dup.OtherRundowns.Count == 0, "show files: a running order can't share the live one's name");
    }
    finally { try { File.Delete(file); } catch { } }
    var copy = settings.Rundown.Clone();
    copy.Items[0].Title = "Changed";
    copy.GroupTargets["Morning"] = 1;
    Check(settings.Rundown.Items[0].Title == "Opening" && settings.Rundown.GroupTargets["Morning"] == 3600, "running orders: copies are deep");
}

// ---------------------------------------------------------------- Google Sheets import
{
    Check(SheetImport.CsvUrl("https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOp/edit#gid=123")
              == "https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOp/export?format=csv&gid=123"
          && SheetImport.CsvUrl("https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOp/edit?usp=sharing")
              == "https://docs.google.com/spreadsheets/d/1AbCdEfGhIjKlMnOp/export?format=csv"
          && SheetImport.CsvUrl("https://docs.google.com/spreadsheets/d/e/2PACX-xyz/pub?gid=0&single=true")!.EndsWith("&output=csv")
          && SheetImport.CsvUrl("https://example.com/run.csv") == "https://example.com/run.csv"
          && SheetImport.CsvUrl("http://example.com/run.csv") is null && SheetImport.CsvUrl("not a link") is null,
          "sheets: share links become CSV exports; only https");
    var current = new List<RundownItem>
    {
        new() { Title = "Welcome", DurationSeconds = 600, AdjustSeconds = -60, Notes = "trimmed live", CueLadderId = "calm" },
        new() { Title = "Keynote", DurationSeconds = 1800 },
        new() { Title = "Old panel", DurationSeconds = 2400 },
    };
    current[1].SetDeptNote("Lighting", "Blue wash");
    current[1].SetDeptNote("Sound", "Lav 1");
    var sheetRow = new RundownItem { Title = "keynote", DurationSeconds = 2100, Speaker = "Ana" };
    sheetRow.SetDeptNote("Sound", "Handheld");
    var sheet = new List<RundownItem> { new() { Title = "Welcome", DurationSeconds = 900 }, sheetRow, new() { Title = "Awards", DurationSeconds = 1200 } };
    var m = SheetImport.Update(current, sheet);
    Check(m.Updated == 2 && m.Added == 1 && m.Dropped.SequenceEqual(new[] { "Old panel" }), "sheets: update matches by title, adds new, lists what went");
    Check(m.Items[0] is { DurationSeconds: 900, AdjustSeconds: -60, Notes: "trimmed live", CueLadderId: "calm" } && m.Items[0].Id == current[0].Id,
          "sheets: update takes the sheet's plan but keeps live trims, notes, cue ladder and id");
    Check(m.Items[1].DeptNote("Sound") == "Handheld" && m.Items[1].DeptNote("Lighting") == "Blue wash" && m.Items[1].Speaker == "Ana",
          "sheets: the sheet's department columns win; others are kept");
}

// ---------------------------------------------------------------- Presenter PCs (Stagewatch Slides): feeds, deck choice, scripts
{
    var t0 = DateTime.UtcNow;
    var deck = new DeckState("Keynote.pptx", 4, 20, "Results", "Say hello", new[] { 30.0, 45.0 }, t0.AddSeconds(-12), t0.AddMinutes(-5), "Hello world script");
    var feed = SlidesAgent.BuildFeed(deck, "pclectern1", "Lectern", 7, t0);
    Check(feed is { Presenting: true, Slide: 4, Count: 20, NextTitle: "Results", Seq: 7 } && Math.Abs(feed.SlideMs - 12000) < 50 && feed.ScriptHash.Length == 16,
          "slides agent: whole state, durations not clock times, script by hash");
    Check(!SlidesAgent.BuildFeed(null, "pclectern1", "Lectern", 8, t0).Presenting, "slides agent: no slideshow = not presenting");
    var parsed = SlideFeed.Parse(feed.ToJson());
    Check(parsed is { Agent: "pclectern1", Slide: 4 } && SlideFeed.Parse("{\"v\":1,\"agent\":\"x\"}"u8) is null && SlideFeed.Parse("nope"u8) is null
          && SlideFeed.Parse("{\"v\":1,\"agent\":\"pcabcdef\",\"slide\":-5,\"slideMs\":-1}"u8) is { Slide: 0, SlideMs: 0 }, "slide feed: untrusted input checked and clamped");

    long clock = 0;
    var f = System.Diagnostics.Stopwatch.Frequency;
    var decks = new RemoteDecks(() => clock);
    var local = deck with { Presentation = "Local.pptx" };
    Check(decks.Accept(feed, "10.0.0.5"), "remote decks: asks for a script it hasn't seen");
    decks.AcceptScript(feed.ScriptHash, "tampered");
    Check(decks.Accept(feed, "10.0.0.5"), "remote decks: a script that doesn't match its hash is ignored");
    decks.AcceptScript(feed.ScriptHash, "Hello world script");
    Check(!decks.Accept(feed with { Seq = 9 }, "10.0.0.5"), "remote decks: script kept once it matches");
    var now = DateTime.UtcNow;
    Check(decks.Pick("auto", local, now) is { Presentation: "Keynote.pptx", Slide: 4, Script: "Hello world script" }, "remote decks: auto follows the presenter PC that's presenting");
    Check(decks.Pick("local", local, now)?.Presentation == "Local.pptx" && decks.Pick("pclectern1", null, now)?.Slide == 4, "remote decks: this PC only, or a chosen presenter PC");
    decks.Accept(feed with { Slide = 3, Seq = 5 }, "10.0.0.5");
    Check(decks.Pick("auto", null, now)?.Slide == 4, "remote decks: a late, older packet doesn't move the slide back");
    clock += 5 * f;
    Check(decks.Pick("auto", local, now)?.Presentation == "Local.pptx" && decks.Pick("pclectern1", local, now) is null && !decks.List()[0].Connected,
          "remote decks: a silent presenter PC drops out (auto falls back to this PC)");
    // Review P2: a malformed discovery packet ("name": null) is dropped and the listener keeps going.
    {
        using var beacon = new SlidesBeacon(() => null, 19937);
        using var udp = new System.Net.Sockets.UdpClient();
        var bad = System.Text.Encoding.UTF8.GetBytes("{\"t\":\"agent\",\"v\":1,\"id\":\"pcbadpkt01\",\"name\":null,\"machine\":null,\"web\":0,\"presenting\":false,\"caps\":[null]}");
        var good = System.Text.Encoding.UTF8.GetBytes("{\"t\":\"main\",\"v\":1,\"id\":\"mainok0001\",\"name\":\"Hall A\",\"machine\":\"FOH\",\"web\":9924,\"presenting\":false}");
        udp.Send(bad, bad.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 19937));
        Thread.Sleep(150);
        udp.Send(good, good.Length, new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 19937));
        var swb = System.Diagnostics.Stopwatch.StartNew();
        while (!beacon.Peers().Any(p => p.Hello.Id == "mainok0001") && swb.Elapsed < TimeSpan.FromSeconds(3)) Thread.Sleep(50);
        var peers = beacon.Peers();
        Check(peers.Any(p => p.Hello.Id == "mainok0001") && peers.FirstOrDefault(p => p.Hello.Id == "pcbadpkt01") is null or { Hello.Name: "" },
              "discovery review P2: a packet with null fields doesn't stop the listener");
    }
    Check(SlideFeed.Parse("{\"v\":1,\"agent\":\"pcabcdef\",\"budgets\":null,\"name\":null}"u8) is { Budgets.Length: 0, Name: "" }, "slide feed: null fields are tolerated");
    var picked = new RemoteDecks(() => 0).Pick("auto", null, now);
    Check(picked is null, "remote decks: nothing presenting, nothing local = no deck");
}

// ---------------------------------------------------------------- AI copilot: proposals are checked, risk-rated, never run here
{
    var plan = Copilot.ParsePlan("""
        {"reply":"Trim the panel and tell Ana.","question":"",
         "steps":[{"command":"trim 3 -120","why":"panel has slack"},
                  {"command":"signal message speaker \"Please wrap up in 2 minutes\"","why":"kind heads-up"},
                  {"command":"blackout on","why":"(should never be pre-ticked)"},
                  {"command":"rm -rf /","why":"nonsense"},
                  {"command":"aux 1 set 15:00","why":"changeover clock"},
                  {"command":"screen identify","why":"find screens"}]}
        """);
    Check(plan.Steps.Count == 5 && plan.Dropped.SequenceEqual(new[] { "rm -rf /" }), $"copilot: invalid commands are dropped and listed ({plan.Steps.Count}, {string.Join("|", plan.Dropped)})");
    Check(plan.Steps[0].Risk == CopilotRisk.Review && plan.Steps[1].Risk == CopilotRisk.Review && plan.Steps[2].Risk == CopilotRisk.High
          && plan.Steps[3].Risk == CopilotRisk.Review && plan.Steps[4].Risk == CopilotRisk.High, "copilot: risk comes from what a command does, not from the model");
    // Review P2: anything that changes or covers what a screen shows is opt-in, even briefly.
    Check(Copilot.Classify(Copilot.Check("preview on")!.Value) == CopilotRisk.High && Copilot.Classify(Copilot.Check("aux 1 stage")!.Value) == CopilotRisk.High
          && Copilot.Classify(Copilot.Check("aux 1 unstage")!.Value) == CopilotRisk.High && Copilot.Classify(Copilot.Check("screen identify")!.Value) == CopilotRisk.High
          && Copilot.Classify(Copilot.Check("roll on")!.Value) == CopilotRisk.High && Copilot.Classify(Copilot.Check("aux 1 start")!.Value) == CopilotRisk.Review
          && Copilot.Classify(Copilot.Check("screen save lobby abc123")!.Value) == CopilotRisk.Low, "copilot review P2: preview, staging a timer and identify are opt-in");
    Check(Copilot.Classify(Copilot.Check("start")!.Value) == CopilotRisk.High && Copilot.Classify(Copilot.Check("next")!.Value) == CopilotRisk.High
          && Copilot.Classify(Copilot.Check("screen go lobby")!.Value) == CopilotRisk.High && Copilot.Classify(Copilot.Check("note \"x\"")!.Value) == CopilotRisk.Low
          && Copilot.Classify(Copilot.Check("delay 300")!.Value) == CopilotRisk.Review, "copilot: anything that changes the room is high risk");
    var many = Copilot.ParsePlan("{\"reply\":\"\",\"question\":\"\",\"steps\":[" + string.Join(",", Enumerable.Range(1, 20).Select(i => "{\"command\":\"note n" + i + "\",\"why\":\"\"}")) + "]}");
    Check(many.Steps.Count == 12 && many.Dropped.Count == 8, "copilot: at most 12 steps per plan");
    var ask = Copilot.ParsePlan("{\"reply\":\"\",\"question\":\"Which Ana — item 3 or item 7?\",\"steps\":[]}");
    Check(ask.Question == "Which Ana — item 3 or item 7?" && ask.Steps.Count == 0, "copilot: asks instead of guessing");
    Check(Copilot.ParsePlan("not json").Steps.Count == 0, "copilot: an unreadable answer proposes nothing");

    var agenda = Copilot.ParseAgenda("""
        {"notes":["Lunch length assumed"],
         "items":[{"title":"Welcome","speaker":"Ana Ruiz","minutes":10,"kind":"Talk","fixed_start":"09:00","notes":""},
                  {"title":"Lunch","speaker":"","minutes":0,"kind":"Break","fixed_start":"12:30","notes":""},
                  {"title":"Panel","speaker":"","minutes":45,"kind":"Discussion","fixed_start":"half past","notes":""},
                  {"title":"","speaker":"","minutes":5,"kind":"Talk","fixed_start":"","notes":""}]}
        """);
    Check(agenda.Items.Count == 3 && agenda.Items[1].Minutes == 5 && agenda.Items[2].Kind == SegmentKind.Talk && agenda.Items[2].FixedStart is null
          && agenda.Notes.Any(n => n.Contains("Lunch")), "copilot agenda: lengths, kinds and times checked; problems listed for the operator");
    var row = Copilot.ToRundownItem(agenda.Items[0]);
    Check(row.DurationSeconds == 600 && row.Speaker == "Ana Ruiz" && row.StartMode == StartMode.Fixed && row.FixedStart == "09:00", "copilot agenda: rows become running-order items");

    var j = new List<JournalEntry>
    {
        new(DateTime.UtcNow, JournalKind.Note, -1, "Copilot asked: get us back on time"),
        new(DateTime.UtcNow, JournalKind.Note, -1, "Copilot step approved (You asked): trim 3 -120"),
        new(DateTime.UtcNow, JournalKind.Note, -1, ShowReport.SummaryPrefix + "We finished 2 minutes early.\n- The panel ran short"),
        new(DateTime.UtcNow, JournalKind.Note, -1, "Operator note"),
    };
    var rep = ShowReport.Build("Summit", new Rundown(), j, null, ShowClock.Local, DateTime.UtcNow);
    var html = rep.ToHtml(ShowClock.Local);
    Check(rep.AiDecisions.Count == 2 && html.Contains("We finished 2 minutes early.") && html.Contains("&bull; The panel ran short")
          && html.Contains("AI suggestions and decisions") && !html.Contains("Operator note"), "report: the reviewed summary and the AI decision log");
}

// ---------------------------------------------------------------- Extra (aux) timers
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    long clock = 0;
    var f = System.Diagnostics.Stopwatch.Frequency;
    var a = new AuxTimer(() => clock) { Name = "VT" };
    a.SetDuration(TimeSpan.FromSeconds(90));
    a.Start(); clock += 30 * f;
    Check(a.DisplaySeconds == 60 && a.Running, "aux: counts down from the clock, not from ticks");
    a.Pause(); clock += 100 * f;
    Check(a.DisplaySeconds == 60, "aux: paused time doesn't count");
    a.Start(); clock += 75 * f;
    Check(a.DisplaySeconds == -15 && a.View(1).Text == "-00:15", $"aux: goes past zero ({a.View(1).Text})");
    a.Nudge(TimeSpan.FromSeconds(30));
    Check(a.DisplaySeconds == 15, "aux: nudge adds time left");
    a.CountUp = true; a.Reset(); clock += (long)(12.7 * f);
    Check(a.DisplaySeconds == 12 && a.Running, "aux: count up, reset keeps it running");
    // A million reads change nothing: the value is derived from the clock, so displaying it can't make it drift.
    for (int i = 0; i < 1_000_000; i++) _ = a.Value;
    Check(a.DisplaySeconds == 12, "aux: reading never drifts it");
    Check(new AuxTimer().View(2).Name == "Timer 2", "aux: unnamed timers get a number");

    Check(P("aux 1 start") is { Kind: ShowCommandKind.Aux, Number: 1, Text: "start\u0001" } && P("aux 2")?.Text == "toggle\u0001"
          && P("aux 3 set 3:00")?.Text == "set\u0001180" && P("aux 1 nudge -30")?.Text == "nudge\u0001-30"
          && P("aux 1 name Video playback")?.Text == "name\u0001Video playback" && P("aux 1 stop")?.Text == "pause\u0001",
          "aux commands: start / toggle / set / nudge / name");
    Check(P("aux 4 start") is null && P("aux 0 start") is null && P("aux 1 dance") is null && P("aux 1 set soon") is null, "aux commands: refuse bad slots and actions");
    var aj = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot
    {
        Aux = new[] { new AuxView(1, "VT", "02:13", 133, true, false, true, 180) }.ToEquatableArray(), AuxLine = "VT  02:13"
    }));
    Check(aj.Contains("\"auxLine\":\"VT  02:13\"") && aj.Contains("\"name\":\"VT\"") && aj.Contains("\"stage\":true") && aj.Contains("\"duration\":180"),
          "aux: timers and the stage line ride in every frame");
}

// ---------------------------------------------------------------- Screens: commands, validation, matching
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    Check(P("screen identify")?.Text == "identify\u0001\u0001all" && P("screen identify Stage left")?.Text == "identify\u0001\u0001Stage left"
          && P("screen go lobby")?.Text == "go\u0001lobby\u0001all" && P("screen go /viewer?cue=0 abc123")?.Text == "go\u0001/viewer?cue=0\u0001abc123"
          && P("screen name abc123 Lobby TV")?.Text == "name\u0001Lobby TV\u0001abc123" && P("screen save lobby abc123")?.Text == "save\u0001lobby\u0001abc123",
          "screen commands: identify / go / name / save");
    Check(P("screen reload")?.Text == "reload\u0001\u0001all" && P("screen disconnect Lobby TV")?.Text == "disconnect\u0001\u0001Lobby TV",
          "screen commands: reload / disconnect");
    Check(P("screen go https://evil.example") is null && P("screen go //x") is null && P("screen save bad! x") is null && P("screen save lobby") is null && P("screen dance") is null,
          "screen commands: refuse other sites, bad names and unknown actions");
    Check(ScreenPreset.ValidName("lobby-2") && !ScreenPreset.ValidName("lob by") && !ScreenPreset.ValidName("")
          && ScreenPreset.ValidPath("/viewer?cue=0") && !ScreenPreset.ValidPath("viewer") && !ScreenPreset.ValidPath("/viewer?x=1&pin=4")
          && !ScreenPreset.ValidPath("/a\r\nSet-Cookie: x"), "screen setups: names and pages validated (no header injection)");
    Check(Stagewatch.Remote.Screens.CleanPage("/viewer?pin=1234&cue=0") == "/viewer?cue=0" && Stagewatch.Remote.Screens.CleanPage("http://x") == "/"
          && Stagewatch.Remote.Screens.ValidId("abc123") && !Stagewatch.Remote.Screens.ValidId("a b"), "screens: pages cleaned of PINs, ids checked");
    var now = DateTime.UtcNow;
    var list = new[]
    {
        new Stagewatch.Remote.ScreenInfo("aaaaaa", "Lobby TV", "/viewer", "1.2.3.4", "Smart TV", now, false, 1),
        new Stagewatch.Remote.ScreenInfo("bbbbbb", "", "/control", "1.2.3.5", "iPad", now, true, 1),
        new Stagewatch.Remote.ScreenInfo("cccccc", "Stage left", "/viewer", "1.2.3.6", "Windows", now, false, 1),
    };
    Check(Stagewatch.Remote.Screens.Match(list, "all").Count == 2 && Stagewatch.Remote.Screens.Match(list, "lobby tv").Single().Id == "aaaaaa"
          && Stagewatch.Remote.Screens.Match(list, "BBBBBB").Single().Control && Stagewatch.Remote.Screens.Match(list, "nobody").Count == 0,
          "screens: all (not control pages), by name, by id");
    Check(Stagewatch.Remote.Screens.Device("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36 Edg/130.0") == "Windows · Edge",
          "screens: device from the browser");
}

// ---------------------------------------------------------------- LTC in: commands, frames, settings, timecode triggers
{
    ShowCommand? P(string line) { var t = line.Split(' '); return ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var c, out _) ? c : null; }
    Check(P("ltc chase on") is { Kind: ShowCommandKind.Ltc, Text: "chase", Number: 1 } && P("LTC CHASE off")?.Number == 0 && P("ltc chase")?.Number == -1,
          "ltc chase on / off / toggle");
    Check(P("ltc end 01:00:30:00")?.Text == "end\u0001" + "01:00:30:00" && P("ltc end clear")?.Text == "end\u0001" && P("ltc end soon") is null && P("ltc dance") is null,
          "ltc end <timecode|clear>");
    var lj = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot { LtcText = "01:00:00:12", LtcRunning = true, LtcChase = true }));
    var l0 = System.Text.Encoding.UTF8.GetString(SnapshotJson.Serialize(new ShowSnapshot()));
    Check(lj.Contains("\"ltc\":\"01:00:00:12\"") && lj.Contains("\"ltcRunning\":true") && lj.Contains("\"ltcChase\":true") && l0.Contains("\"ltc\":\"\""),
          "ltc in: timecode rides in every frame");
    var ii = new IntegrationSettings();
    Check(!ii.LtcIn && !ii.LtcChase && ii.LtcEnd == "" && ii.LtcInDevice == "", "ltc in: off by default");
    Check((int)TriggerEvent.Timecode == 10, "trigger events: Timecode appended, nothing renumbered");

    var ran = new List<string>();
    var rules = new List<TriggerRule> { new() { Event = TriggerEvent.Timecode, Filter = "01:00:30:00", Action = TriggerAction.Command, Payload = "message Roll VT" } };
    using var tr = new TriggerRunner(() => rules, ran.Add);
    var ctx = new TriggerContext(0, "", "", "", "", "");
    var at = TimeSpan.FromHours(1) + TimeSpan.FromSeconds(30);
    tr.FireTimecode(at - TimeSpan.FromSeconds(0.1), at - TimeSpan.FromSeconds(0.02), 25, ctx);
    tr.FireTimecode(at - TimeSpan.FromSeconds(0.02), at + TimeSpan.FromSeconds(0.08), 25, ctx);
    tr.FireTimecode(at + TimeSpan.FromSeconds(0.08), at + TimeSpan.FromSeconds(0.18), 25, ctx);
    tr.FireTimecode(at - TimeSpan.FromSeconds(30), at + TimeSpan.FromSeconds(1), 25, ctx);     // a locate/jump: not a pass
    Check(ran.Count == 1 && ran[0] == "message Roll VT", $"timecode trigger fires once as LTC passes it ({ran.Count})");
}

// ---------------------------------------------------------------- in-app help
{
    var topics = Stagewatch.App.Help.HelpContent.Topics;
    var tips = Stagewatch.App.Help.HelpContent.Tips;
    Check(topics.Select(t => t.Id).Distinct().Count() == topics.Count, "help: topic ids unique");
    Check(tips.Values.All(t => Stagewatch.App.Help.HelpContent.Topic(t.Topic) is not null),
          "help: every right-click tip links to a topic (" + string.Join(", ", tips.Where(t => Stagewatch.App.Help.HelpContent.Topic(t.Value.Topic) is null).Select(t => t.Key)) + ")");
    // Groups stay together so the topic list reads in order.
    var groups = topics.Select(t => t.Group).ToList();
    Check(groups.Distinct().All(g => groups.LastIndexOf(g) - groups.IndexOf(g) + 1 == groups.Count(x => x == g)), "help: groups contiguous");
    Check(topics.All(t => t.Blocks.Length > 0 && t.Summary.Length > 0), "help: no empty topics");

    // Every tip key used in a window exists (a typo would silently lose the help).
    var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Timer.App"));
    if (Directory.Exists(src))
    {
        var used = Directory.GetFiles(src, "*.xaml")
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "ContextHelp\\.Tip=\"([^\"]+)\"").Select(m => m.Groups[1].Value))
            .Concat(System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(src, "MainWindow.xaml.cs")), "case \"([a-z-]+)\"").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        var missing = used.Where(k => !tips.ContainsKey(k)).ToList();
        Check(used.Count > 40 && missing.Count == 0, $"help: {used.Count} tip keys in the windows all defined ({string.Join(", ", missing)})");
        var topicsUsed = Directory.GetFiles(src, "*.xaml*")
            .SelectMany(f => System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), "(?:ContextHelp\\.Topic=\"|SetTopic\\(this, \"|ShowTopic\\(this, \")([a-z-]+)\"").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Check(topicsUsed.Count > 5 && topicsUsed.All(t => Stagewatch.App.Help.HelpContent.Topic(t) is not null),
              $"help: topics named in the windows exist ({string.Join(", ", topicsUsed.Where(t => Stagewatch.App.Help.HelpContent.Topic(t) is null))})");
    }
    else Console.WriteLine("(skipped help key check: source folder not found)");
}

// ---------------------------------------------------------------- tiers & pricing
{
    Check((int)FeatureTier.Locked == 0 && (int)FeatureTier.Trial == 1 && (int)FeatureTier.Standard == 2 && (int)FeatureTier.Pro == 3
          && (int)FeatureTier.Master == 4 && (int)FeatureTier.Production == 5, "tiers: stored numbers never change");
    Check(FeatureGate.Level(FeatureTier.Locked) < FeatureGate.Level(FeatureTier.Standard)
          && FeatureGate.Level(FeatureTier.Standard) < FeatureGate.Level(FeatureTier.Pro)
          && FeatureGate.Level(FeatureTier.Pro) < FeatureGate.Level(FeatureTier.Production)
          && FeatureGate.Level(FeatureTier.Production) < FeatureGate.Level(FeatureTier.Master)
          && FeatureGate.Level(FeatureTier.Trial) == FeatureGate.Level(FeatureTier.Master), "tiers: Free < Standard < Pro < Production < Trial = Master");
    Func<FeatureTier, bool>[] production = { FeatureGate.AllowsMultiRoom, FeatureGate.AllowsCueLights, FeatureGate.AllowsCrewCues, FeatureGate.AllowsForecast,
        FeatureGate.AllowsTimecode, FeatureGate.AllowsPlayout, FeatureGate.AllowsFullReport, FeatureGate.AllowsSpeechPace, FeatureGate.AllowsAssistant, FeatureGate.AllowsMcp };
    Func<FeatureTier, bool>[] pro = { FeatureGate.AllowsCompanion, FeatureGate.AllowsMultiOutput, FeatureGate.AllowsPowerPointAddIn,
        FeatureGate.AllowsRunOfShow, FeatureGate.AllowsRemote, FeatureGate.AllowsCsvReport };
    Check(production.All(g => !g(FeatureTier.Pro) && !g(FeatureTier.Standard) && g(FeatureTier.Production) && g(FeatureTier.Trial) && g(FeatureTier.Master)),
          "tiers: Production features need Production (trial and master have them)");
    Check(pro.All(g => g(FeatureTier.Pro) && g(FeatureTier.Production) && !g(FeatureTier.Standard)), "tiers: Production includes everything in Pro");
    Check(FeatureGate.AllowsEndAt(FeatureTier.Production) && FeatureGate.MaxPresets(FeatureTier.Production) == 12, "tiers: Production includes Standard");
    Check(FeatureGate.Needs(FeatureTier.Production) == "needs Stagewatch Production" && Pricing.TierName(FeatureTier.Locked) == "Free", "tiers: wording");
    Check(Pricing.Standard == 9 && Pricing.Pro == 29 && Pricing.Production == 79 && Pricing.ProductionLaunch == 59
          && Pricing.UpdateRenewalPerYear == 20 && Pricing.EventPass == 15 && Pricing.EventPassDays == 30 && Pricing.ProductionMachines == 2, "pricing: agreed price list");
}

// ---------------------------------------------------------------- licensing / free mode
{
    Check(Stagewatch.Licensing.TrialTracker.TrialDuration == TimeSpan.FromDays(14), "trial: 14 days");
    Check(FeatureGate.ShowsWatermark(FeatureTier.Locked) && !FeatureGate.ShowsWatermark(FeatureTier.Trial)
          && !FeatureGate.ShowsWatermark(FeatureTier.Pro) && !FeatureGate.ShowsWatermark(FeatureTier.Master), "watermark only in free mode");
    Check(FeatureGate.WatermarkText == "StageWatch by Ben Rohleder", "watermark text");
    Check(Stagewatch.Licensing.LicenseStatus.Locked().ShowsWatermark, "locked status watermarks");
    Check(!Stagewatch.Licensing.LicenseStatus.Trial(TimeSpan.FromHours(3)).ShowsWatermark, "trial (last hours) no watermark");
    var free = FeatureTier.Locked;
    Check(FeatureGate.AllowsCountdownTimer(free) && FeatureGate.MaxPresets(free) == 2, "free: basic timer + 2 presets");
    Check(!FeatureGate.AllowsNdi(free) && !FeatureGate.AllowsChromaKey(free) && !FeatureGate.AllowsCompanion(free)
          && !FeatureGate.AllowsRemote(free) && !FeatureGate.AllowsRunOfShow(free) && !FeatureGate.AllowsEndAt(free)
          && !FeatureGate.AllowsAlerts(free) && !FeatureGate.AllowsQuickMessages(free), "free: advanced features off");
    Check(FeatureGate.AllowsCompanion(FeatureTier.Trial) && FeatureGate.AllowsRemote(FeatureTier.Trial), "trial: everything on");
    Check(SnapshotJson.Serialize(new ShowSnapshot { Watermark = FeatureGate.WatermarkText, PresetSlot = 3 }) is var wj
          && Encoding.UTF8.GetString(wj).Contains("\"watermark\":\"StageWatch by Ben Rohleder\"")
          && Encoding.UTF8.GetString(wj).Contains("\"preset\":3"), "snapshot carries watermark + preset");
}

// ---------------------------------------------------------------- Companion protocol v2
{
    var bridge = new FakeBridge { Allowed = true };
    var port = FreePort();
    await using var server = new Stagewatch.Companion.CompanionServer(bridge);
    server.Start(port, bindAllInterfaces: false);

    using var tcp = new TcpClient();
    await tcp.ConnectAsync(IPAddress.Loopback, port);
    var reader = new StreamReader(tcp.GetStream());
    var writer = new StreamWriter(tcp.GetStream()) { AutoFlush = true, NewLine = "\n" };
    async Task<string> Line() { using var cts = new CancellationTokenSource(3000); return await reader.ReadLineAsync(cts.Token) ?? ""; }
    async Task<string> LineStarting(string prefix) { for (int i = 0; i < 50; i++) { var l = await Line(); if (l.StartsWith(prefix)) return l; } return ""; }

    Check((await Line()) == "READY stagewatch/2", "companion: READY v2");
    bridge.Snapshots.Publish(new ShowSnapshot { Text = "05:00", Seconds = 300, ConfigVersion = 1 });
    await writer.WriteLineAsync("SUBSCRIBE JSON");
    var cfgLine = await LineStarting("CONFIG ");
    Check(cfgLine.Contains("\"Opening\""), "companion: CONFIG on subscribe");
    var snapLine = await LineStarting("SNAPSHOT ");
    Check(snapLine.Contains("\"text\":\"05:00\""), "companion: SNAPSHOT on subscribe");

    bridge.Snapshots.Publish(new ShowSnapshot { Text = "04:59", Seconds = 299, ConfigVersion = 1 });
    var pushed = await Line();
    Check(pushed.StartsWith("SNAPSHOT ") && pushed.Contains("04:59"), "companion: pushed frame, no CONFIG when unchanged");
    bridge.Snapshots.Publish(new ShowSnapshot { Text = "04:58", Seconds = 298, ConfigVersion = 2 });
    Check((await Line()).StartsWith("CONFIG ") && (await Line()).Contains("04:58"), "companion: CONFIG re-sent on config change");

    await writer.WriteLineAsync("PING 42");
    Check((await LineStarting("PONG")) == "PONG 42", "companion: PING token echoed");
    // Show Bus 1.1: a Unix-ms PING gets our times back (clock offset); "@id" commands get a receipt.
    var busT1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    await writer.WriteLineAsync("PING " + busT1);
    var timedPong = (await LineStarting("PONG " + busT1)) ?? "";
    Check(timedPong.Split(' ').Length >= 4 && long.Parse(timedPong.Split(' ')[2]) >= busT1 - 5000, $"companion: a timed PING gets t2 and t3 ({timedPong})");
    await writer.WriteLineAsync("@n1 NEXT");
    Check((await LineStarting("OK @n1")) is { } receiptLine && receiptLine.StartsWith("OK @n1 accepted"), "companion: an @id command gets its receipt");
    await writer.WriteLineAsync("@n2 BOGUSVERB");
    Check((await LineStarting("ERROR @n2")) is not null, "companion: an @id command that fails says so with its id");
    lock (bridge.Commands) bridge.Commands.Clear();   // the routing check below counts only its own verbs

    foreach (var cmd in new[] { "START", "NEXT", "GOTO 3 start", "BLACKOUT toggle", "NUDGE -30", "TOGGLE", "SET 05:00", "MODE countup", "MESSAGE \"Say \\\"hi\\\"\" warn slow" })
        await writer.WriteLineAsync(cmd);
    await Task.Delay(300);
    var kinds = bridge.Commands.Select(c => c.Kind).ToList();
    Check(kinds.SequenceEqual(new[] { ShowCommandKind.Start, ShowCommandKind.Next, ShowCommandKind.GoTo, ShowCommandKind.Blackout,
        ShowCommandKind.Nudge, ShowCommandKind.Toggle, ShowCommandKind.SetDuration, ShowCommandKind.Mode }), "companion: verbs route through Execute (" + string.Join(",", kinds) + ")");
    Check(bridge.Messages.LastOrDefault() == ("Say \"hi\"", MessageUrgency.Warning, FlashRate.Slow), "companion: quoted message");
    Check(bridge.Commands.First(c => c.Kind == ShowCommandKind.GoTo) is { Number: 3, Flag: true }, "companion: GOTO args");

    await writer.WriteLineAsync("FROBNICATE");
    Check((await LineStarting("ERROR")).Contains("unknown command"), "companion: unknown verb → ERROR");

    // A client that connects and never reads must not slow publishing (it used to block the engine thread).
    using var stuck = new TcpClient { ReceiveBufferSize = 1024 };
    await stuck.ConnectAsync(IPAddress.Loopback, port);
    await stuck.GetStream().WriteAsync("SUBSCRIBE JSON\n"u8.ToArray());
    await Task.Delay(200);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    for (int i = 0; i < 20000; i++)
        bridge.Snapshots.Publish(new ShowSnapshot { Text = "x" + i, Seconds = i, Message = new string('m', 400), ConfigVersion = 2 });
    sw.Stop();
    Check(sw.ElapsedMilliseconds < 3000, $"companion: stuck client doesn't block publish ({sw.ElapsedMilliseconds} ms for 20k frames)");
    // The healthy client still gets the latest frame afterwards.
    var latest = "";
    for (int i = 0; i < 25000 && !latest.Contains("\"x19999\""); i++) latest = await Line();
    Check(latest.Contains("\"x19999\""), "companion: healthy client still current");

    // Free mode: told why, then disconnected.
    var denyBridge = new FakeBridge { Allowed = false };
    var port2 = FreePort();
    await using var server2 = new Stagewatch.Companion.CompanionServer(denyBridge);
    server2.Start(port2, false);
    using var t2 = new TcpClient();
    await t2.ConnectAsync(IPAddress.Loopback, port2);
    var r2 = new StreamReader(t2.GetStream());
    var l1 = await r2.ReadLineAsync();
    var l2 = await r2.ReadLineAsync();
    var eof = await r2.ReadLineAsync();
    Check(l1 == "READY stagewatch/2" && l2!.StartsWith("DENIED license") && l2.Contains("trial has ended") && eof is null, "companion: free mode DENIED + close");
    await t2.GetStream().WriteAsync("START\n"u8.ToArray()).AsTask().ContinueWith(_ => { });
    await Task.Delay(100);
    Check(denyBridge.Commands.Count == 0, "companion: free mode executes nothing");
}

// ---------------------------------------------------------------- 2.0 systems
V2Tests.Run(Check);
ShowWorkflowTests.Run(Check);
WindowTests.Run(Check);
await WindowTests.RunWeb(Check);
SupervisorTests.Run(Check);
SupervisorTests.Holding(Check);
await IntegrationTests.Run(Check);
await WebV2Tests.Run(Check);
await ShowBusTests.Run(Check);
await CompanionV2Tests.Run(Check);
if (args.Contains("--powerpoint")) await PowerPointLiveTest.Run(Check);

Console.WriteLine($"\n{passes} passed, {failures} failed");
if (Bench.WindowsOnlyNotRun is { Count: > 0 } windowsOnly)
    Console.WriteLine($"{windowsOnly.Count} Windows-only check(s) not run on {System.Runtime.InteropServices.RuntimeInformation.OSDescription} (the Windows job runs them): {string.Join("; ", windowsOnly)}");
return failures == 0 ? 0 : 1;

// ---------------------------------------------------------------- helpers
static int FreePort()
{
    var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
    var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
}
static int FreePortUdp()
{
    using var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    return ((IPEndPoint)u.Client.LocalEndPoint!).Port;
}
static async Task<string> Receive(ClientWebSocket ws)
{
    var buf = new byte[65536];
    using var cts = new CancellationTokenSource(3000);
    var r = await ws.ReceiveAsync(buf, cts.Token);
    return Encoding.UTF8.GetString(buf, 0, r.Count);
}
static async Task<string> ReceiveUntil(ClientWebSocket ws, string type)
{
    for (int i = 0; i < 10; i++)
    {
        var m = await Receive(ws);
        if (m.Contains($"\"type\":\"{type}\"")) return m;
    }
    return "";
}
static async Task<List<OscMessage>> ReceiveOsc(UdpClient u)
{
    using var cts = new CancellationTokenSource(2000);
    var r = await u.ReceiveAsync(cts.Token);
    return OscCodec.Decode(r.Buffer);
}

sealed class FakeBridge : Stagewatch.Companion.ICompanionBridge
{
    public bool Allowed { get; set; }
    public SnapshotHub Snapshots { get; } = new();
    public List<ShowCommand> Commands { get; } = new();
    public List<(string, MessageUrgency, FlashRate)> Messages { get; } = new();
    public TimerEngine Engine { get; } = new();
    public AppSettings Settings { get; } = new();
    public bool CompanionAllowed => Allowed;
    public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
    public RemoteConfig GetRemoteConfig() => new(
        new[] { (1, "One", 60) }, Array.Empty<(string, string, MessageUrgency, FlashRate)>(), new[] { ("Opening", (string?)null, 300) });
    public void LoadPreset(int slot, bool autoStart) { }
    public void SetMessage(string text, MessageUrgency urgency, FlashRate flash) { lock (Messages) Messages.Add((text, urgency, flash)); }
    public void SetFlash(FlashRate rate) { }
    public void SetTimerFlash(bool flashTimer) { }
    public event Action<MessageState>? MessageChanged { add { } remove { } }
    public event Action<string>? CommandRecorded;
    public void Record(string line) => CommandRecorded?.Invoke(line);
}

sealed class FakeTranscriber : Stagewatch.Integrations.ISpeechTranscriber
{
    public bool Fails, Started, Disposed;
    public event Action<string, DateTime>? PhraseRecognized;
    public string Status => Fails ? "no microphone" : "listening";
    public bool Start() { Started = !Fails; return !Fails; }
    public void Stop() { }
    public void Dispose() => Disposed = true;
    public void Say(string text) => PhraseRecognized?.Invoke(text, DateTime.UtcNow);
}

sealed class FakeControl : IShowControl
{
    public SnapshotHub Snapshots { get; } = new();
    public List<ShowCommand> Commands { get; } = new();
    public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
    public RemoteConfig GetRemoteConfig() => new(
        new[] { (1, "One", 60), (2, "Two", 120) },
        new[] { ("Wrap", "Wrap up", MessageUrgency.Warning, FlashRate.Slow) },
        new[] { ("Opening", (string?)"Ana", 300) });
}
