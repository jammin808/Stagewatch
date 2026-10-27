using System.Text;
using Stagewatch.Core;

/// <summary>Stagewatch 2.0 core systems: cue ladders, signals, Q&amp;A, schedule recovery, forecast, pacing, report.</summary>
static class V2Tests
{
    public static void Run(Action<bool, string> Check)
    {
        CueLadders(Check);
        SignalsAndQa(Check);
        Grammar(Check);
        ScheduleRecovery(Check);
        TimeZones(Check);
        ForecastAndAdvice(Check);
        Pacing(Check);
        JournalAndReport(Check);
        Json(Check);
        SettingsMigration(Check);
    }

    static void SettingsMigration(Action<bool, string> Check)
    {
        // A 1.x file (no 2.0 sections) and a hand-edited one (sections nulled) must both load with defaults.
        using var folder = Scratch.Folder("settings");
        var dir = folder.Path;
        var p1 = Path.Combine(dir, "a.json");
        File.WriteAllText(p1, "{\"schemaVersion\":1,\"rundown\":{\"items\":[{\"title\":\"Old\",\"durationSeconds\":60}]}}");
        using (var a = new SettingsStore(p1))
            Check(a.Settings.Integrations is not null && a.Settings.Signals.Departments.Count == 4 && a.Settings.Triggers is not null
                  && a.Settings.Rundown.Items[0].Kind == SegmentKind.Talk && a.Settings.Rundown.Items[0].StartMode == StartMode.Sequential,
                "settings: 1.x file gains 2.0 defaults");
        var p2 = Path.Combine(dir, "b.json");
        File.WriteAllText(p2, "{\"integrations\":null,\"signals\":{\"departments\":null},\"triggers\":null,\"display\":{\"customLadders\":null}}");
        using (var b = new SettingsStore(p2))
            Check(b.Settings.Integrations is not null && b.Settings.Signals.Departments is not null && b.Settings.Triggers is not null
                  && b.Settings.Display.CustomLadders is not null && b.Settings.Display.AllLadders().Count >= 6,
                "settings: nulled 2.0 sections repaired");
    }

    static void CueLadders(Action<bool, string> Check)
    {
        var keynote = CueLadder.BuiltIn.First(l => l.Id == "keynote");
        Check(keynote.StepAt(700) is null, "ladder: normal above 10 min");
        Check(keynote.StepAt(600)?.Label == "10 MIN", "ladder: 10 MIN at 600");
        Check(keynote.StepAt(250)?.Label == "5 MIN", "ladder: 5 MIN under 300");
        Check(keynote.StepAt(0)?.Phase == TimerPhase.Overtime && keynote.StepAt(-30)?.Label == "TIME", "ladder: TIME at/after zero");
        Check(keynote.Crossed(301, 300)?.Label == "5 MIN" && keynote.Crossed(299, 298) is null, "ladder: crossing detected once");
        Check(keynote.Crossed(601, 290)?.Label == "5 MIN", "ladder: big jump reports the lowest crossed step");
        var tm = CueLadder.BuiltIn.First(l => l.Id == "toastmasters");
        Check(!tm.OvertimeSignal && tm.StepAt(90)!.ColorHex == "#16A34A" && tm.StepAt(30)!.ColorHex == "#EAB308", "ladder: toastmasters green/yellow");

        var d = new DisplaySettings { WarningSeconds = 120, WrapUpSeconds = 30 };
        var std = CueLadder.FromDisplay(d);
        Check(std.StepAt(100)?.Phase == TimerPhase.Warning && std.StepAt(20)?.Phase == TimerPhase.WrapUp && std.StepAt(100)?.Label == "2 MIN", "ladder: standard from thresholds");
        Check(d.LadderFor("awards").Id == "awards" && d.LadderFor("nope").Id == "standard", "ladder: lookup + fallback");
        d.CustomLadders.Add(new CueLadder { Id = "mine", Name = "Mine", Steps = { new CueStep { AtSeconds = 45, Label = "45" } } });
        Check(d.AllLadders().Any(l => l.Id == "mine") && d.LadderFor("mine").StepAt(40)?.Label == "45", "ladder: custom ladders");

        Check(SpeakerFace.Format(SpeakerTimerFormat.Calm, 725, "12:05") == "13 MIN", "calm: minutes, rounded up");
        Check(SpeakerFace.Format(SpeakerTimerFormat.Calm, 45, "00:45") == "00:45", "calm: seconds in the last minute");
        Check(SpeakerFace.Format(SpeakerTimerFormat.ProgressOnly, 300, "05:00") == "", "progress-only: no digits");
        Check(SpeakerFace.ColorFor(CuePalette.ColourBlindSafe, TimerPhase.Overtime, "#DC2626") == "#D55E00", "palette: colour-blind safe");
        Check(SpeakerFace.FinishBy(new DateTime(2026, 1, 1, 10, 49, 30), false) == "FINISH BY 10:49", "finish-by text");

        var ctx = new Placeholders.Context("Q&A", "10:49", "on time", "04:00", "Keynote", "Ana", "10:45");
        Check(Placeholders.Render("Wrap up by {FINISH_BY}, then {next}", ctx) == "Wrap up by 10:49, then Q&A", "placeholders: render (case-insensitive)");
        Check(Placeholders.Render("plain", ctx) == "plain" && Placeholders.OverUnder(95) == "01:35 behind" && Placeholders.OverUnder(-10) == "on time", "placeholders: plain + over/under");
    }

    static void SignalsAndQa(Action<bool, string> Check)
    {
        var t0 = new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
        var b = new SignalBoard { EscalateAfter = TimeSpan.FromSeconds(20), KeepDoneFor = TimeSpan.FromSeconds(30) };
        var m = b.Send(SignalKind.Message, "speaker", "Wrap up", t0);
        Check(m.Id == 1 && m.State == SignalState.Sent, "signals: send");
        var acked = b.Ack(1, "Ana", "seen", t0.AddSeconds(4));
        Check(acked?.State == SignalState.Done && acked.By == "Ana", "signals: a message ack completes it");
        var call = b.Send(SignalKind.Call, "backstage", "Ana to stage", t0);
        Check(b.Ack(call.Id, "", "seen", t0.AddSeconds(2))?.State == SignalState.Seen, "signals: call → found (seen)");
        Check(b.Ack(call.Id, "Wrangler", "done", t0.AddSeconds(30))?.State == SignalState.Done, "signals: call → in position (done)");
        var sb1 = b.Send(SignalKind.Standby, "lighting", "LX 12", t0);
        Check(sb1.Target == "Lighting", "signals: department names normalised");
        var go = b.Send(SignalKind.Go, "Lighting", "LX 12", t0.AddSeconds(5));
        Check(b.Find(sb1.Id)!.State == SignalState.Cancelled && go.State == SignalState.Sent, "signals: GO supersedes standby");
        var esc = b.Update(t0.AddSeconds(26));
        Check(esc.Count == 1 && esc[0].Id == go.Id && b.Find(go.Id)!.Escalated, "signals: unanswered escalates once");
        Check(b.Update(t0.AddSeconds(27)).Count == 0, "signals: escalation not repeated");
        b.Update(t0.AddMinutes(5));
        Check(b.Find(1) is null && b.Find(go.Id) is not null, "signals: finished ones expire, open ones stay");
        Check(b.Ack(999, "x", "seen", t0) is null, "signals: unknown id ignored");

        // The frame's three message fields, one rule for the app's frames and the rig's: the signal behind the stage's message
        // while it is sent or answered, nothing once it is cancelled, gone from the board or never asked.
        var mb = new SignalBoard { KeepDoneFor = TimeSpan.FromSeconds(30) };
        Check(mb.MessageAck(0) == MessageAckView.None && mb.MessageAck(7) == MessageAckView.None, "message ack: none asked, or no such signal → 0, \"\", \"\"");
        var w1 = mb.Send(SignalKind.Message, "speaker", "Wrap up", t0);
        Check(mb.MessageAck(w1.Id) == new MessageAckView(w1.Id, "sent", ""), "message ack: sent, by nobody yet");
        mb.Ack(w1.Id, "Patterns FOH (127.0.0.1)", "seen", t0.AddSeconds(3));
        Check(mb.MessageAck(w1.Id) == new MessageAckView(w1.Id, "done", "Patterns FOH (127.0.0.1)"), "message ack: a message's first answer is done, by whom");
        var w2 = mb.Send(SignalKind.Message, "speaker", "Two minutes", t0.AddSeconds(5));
        mb.Cancel(w2.Id, t0.AddSeconds(6));
        Check(mb.MessageAck(w2.Id) == MessageAckView.None, "message ack: a cancelled signal says nothing");
        var call2 = mb.Send(SignalKind.Call, "backstage", "Ana to stage", t0);
        mb.Ack(call2.Id, "Wrangler", "seen", t0.AddSeconds(1));
        Check(mb.MessageAck(call2.Id) == new MessageAckView(call2.Id, "seen", "Wrangler"), "message ack: seen is said as seen");
        mb.Update(t0.AddMinutes(5));
        Check(mb.MessageAck(w1.Id) == MessageAckView.None, "message ack: a signal gone from the board says nothing");

        // Who answered, as the web server names a device the PIN does not speak for: the address is kept whole and ends it,
        // only the name is cut, so neither a long desk name nor a made-up "(address)" pushes the real address out.
        Check(SignalBoard.AnsweredBy("Patterns FOH", "127.0.0.1") == "Patterns FOH (127.0.0.1)", "answered by: the name, then the address");
        var longDesk = "Patterns Front of House 2nd Main Hall AB";
        Check(longDesk.Length == SignalBoard.ByMax
              && SignalBoard.AnsweredBy(longDesk, "127.0.0.1") == "Patterns Front of House 2nd (127.0.0.1)",
            "answered by: a 40-character name is cut (and trimmed) to fit, never the address");
        Check(SignalBoard.AnsweredBy("Patterns FOH (10.0.0.5)".PadRight(SignalBoard.ByMax), "127.0.0.1") == "Patterns FOH (10.0.0.5) (127.0.0.1)",
            "answered by: a name dressed as an address stays a name; the real address still ends it");
        Check(SignalBoard.AnsweredBy("", "127.0.0.1") == "127.0.0.1" && SignalBoard.AnsweredBy("   ", "10.0.0.5") == "10.0.0.5",
            "answered by: no name, the address alone");
        Check(SignalBoard.AnsweredBy("Desk", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff") == "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff",
            "answered by: no room for a name beside a long IPv6 address, the address alone");
        var w3 = mb.Send(SignalKind.Message, "speaker", "Lights", t0.AddMinutes(5));
        var ackBy = SignalBoard.AnsweredBy(longDesk, "127.0.0.1");
        mb.Ack(w3.Id, ackBy, "seen", t0.AddMinutes(5).AddSeconds(1));
        Check(mb.MessageAck(w3.Id).By == ackBy, "answered by: the board keeps it as the web server made it");
        var v1 = b.Views(t0.AddMinutes(5));
        var v2 = b.Views(t0.AddMinutes(5).AddSeconds(2));
        Check(v1 == v2, "signals: views stable within the age quantum");

        var q = new QaBoard();
        var a = q.Submit("  How long until lunch?  ", "Sam", t0);
        Check(a is not null && a.Text == "How long until lunch?" && q.Pending == 1, "qa: submit trims");
        Check(q.Submit("how long until lunch?", "", t0) is null, "qa: duplicate dropped");
        var b2 = q.Submit("Second", "", t0)!;
        var c2 = q.Submit("Third", "", t0)!;
        q.Set(a!.Id, QuestionState.Approved, t0);
        q.Set(c2.Id, QuestionState.Starred, t0);
        Check(q.Next(t0)!.Id == c2.Id, "qa: starred goes first");
        Check(q.Next(t0.AddSeconds(60))!.Id == a.Id && q.All.First(x => x.Id == c2.Id).State == QuestionState.Answered, "qa: next answers the live one");
        Check(Math.Abs(q.AverageAnswerSeconds - 60) < 0.01, "qa: answer length measured");
        Check(q.FitIn(TimeSpan.FromSeconds(150)) == 2 && q.FitIn(TimeSpan.Zero) == 0, "qa: fit estimate");
        Check(q.Views()[0].State == QuestionState.Live && q.Views().Any(v => v.Id == b2.Id), "qa: views order live first");
        Check(q.Submit(new string('x', 1000), "", t0)!.Text.Length == QaBoard.MaxLength, "qa: length capped");
    }

    static void Grammar(Action<bool, string> Check)
    {
        bool P(string verb, string[] args, out ShowCommand c) => ShowCommandParser.TryParse(verb, args, out c, out _);
        Check(P("signal", new[] { "go", "lighting", "LX 12" }, out var c) && c.Kind == ShowCommandKind.Signal && (SignalKind)c.Number == SignalKind.Go && c.Text == "lighting\u0001LX 12" && c.Flag, "cmd signal go");
        Check(P("signal", new[] { "call", "", "Ana" }, out c) && c.Text!.StartsWith("backstage"), "cmd signal call defaults to backstage");
        Check(P("signal", new[] { "message", "speaker", "Hi", "noack" }, out c) && !c.Flag, "cmd signal noack");
        Check(P("signal", new[] { "clear", "Lighting" }, out c) && c.Kind == ShowCommandKind.SignalClear && c.Text == "Lighting", "cmd signal clear target");
        Check(P("signal", new[] { "clear", "7" }, out c) && c.Number == 7, "cmd signal clear id");
        Check(!P("signal", new[] { "boom" }, out _), "cmd signal bad kind");
        Check(P("ack", new[] { "3" }, out c) && c.Number == 3 && c.Text == "|seen", "cmd ack");
        Check(P("ack", new[] { "3", "done", "Wrangler" }, out c) && c.Text == "Wrangler|done", "cmd ack done by");
        Check(P("ack", new[] { "3", "Ana" }, out c) && c.Text == "Ana|seen", "cmd ack by");
        Check(P("trim", new[] { "7", "-180" }, out c) && c.Number == 7 && c.Text == "-180", "cmd trim");
        Check(P("delay", new[] { "5:00" }, out c) && c.Number == 300 && P("delay", new[] { "clear" }, out c) && c.Number == 0, "cmd delay");
        Check(!P("delay", new[] { "-5" }, out _), "cmd delay negative");
        Check(P("preview", Array.Empty<string>(), out c) && c.Number == -1 && P("preview", new[] { "on" }, out c) && c.Number == 1, "cmd preview");
        Check(P("format", new[] { "calm" }, out c) && (SpeakerTimerFormat)c.Number == SpeakerTimerFormat.Calm, "cmd format");
        Check(P("question", new[] { "Why?", "Sam" }, out c) && c.Text == "Why?\u0001Sam", "cmd question");
        Check(P("q", new[] { "next" }, out c) && c.Text == "next" && P("q", new[] { "4", "star" }, out c) && c.Number == 4, "cmd q");
        Check(!P("q", new[] { "4", "explode" }, out _), "cmd q bad");
        Check(P("message", new[] { "Wrap", "warn", "slow", "ack" }, out c) && c.Flag, "cmd message ack");
        Check(P("message", new[] { "Wrap" }, out c) && !c.Flag, "cmd message no ack");
        Check(ShowCommandParser.IsPublicVerb("ACK") && ShowCommandParser.IsPublicVerb("question") && !ShowCommandParser.IsPublicVerb("start"), "public verbs");
        Check(P("catchup", Array.Empty<string>(), out c) && c.Kind == ShowCommandKind.CatchUp, "cmd catchup");
        Check(P("report", new[] { "csv" }, out c) && c.Text == "csv", "cmd report");
    }

    static RundownItem Item(string t, int mins, SegmentKind k = SegmentKind.Talk) => new() { Title = t, DurationSeconds = mins * 60, Kind = k };

    static void ScheduleRecovery(Action<bool, string> Check)
    {
        var utc = new ShowClock(TimeZoneInfo.Utc);
        var anchor = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
        var items = new List<RundownItem> { Item("Opening", 10), Item("Keynote", 30), Item("Panel", 30, SegmentKind.Panel), Item("Q&A", 15, SegmentKind.QA), Item("Close", 5) };
        var none = new Dictionary<int, ItemActual>();

        var plan = ScheduleEngine.Plan(items, anchor, utc);
        Check(plan[2].Start == anchor.AddMinutes(40) && plan[4].End == anchor.AddMinutes(90), "plan: sequential");

        // Keynote live, 5 min over (overtime: remaining 0, now = planned end + 5).
        var now = anchor.AddMinutes(45);
        var actuals = new Dictionary<int, ItemActual> { [0] = new(anchor, anchor.AddMinutes(10)), [1] = new(anchor.AddMinutes(10), null) };
        var p = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, 1, true, TimeSpan.Zero, actuals, now, utc));
        Check(p.OffsetSeconds == 300, $"project: 5 min late ({p.OffsetSeconds})");
        Check(p.Items[2].ExpectedStartUtc == anchor.AddMinutes(45) && p.Items[2].DelaySeconds == 300, "project: lateness ripples");
        Check(p.Items[0].Status == ItemStatus.Done && p.Items[1].Status == ItemStatus.Live && p.Items[3].Status == ItemStatus.Upcoming, "project: statuses");

        // Fixed start held: lunch at 11:00 absorbs the lateness.
        items.Insert(4, new RundownItem { Title = "Lunch", DurationSeconds = 3600, Kind = SegmentKind.Break, StartMode = StartMode.Fixed, FixedStart = "11:00" });
        p = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, 1, true, TimeSpan.Zero, actuals, now, utc));
        Check(p.Items[4].PlannedStartUtc == anchor.AddHours(2) && p.Items[4].ExpectedStartUtc == anchor.AddHours(2) && !p.Items[4].Conflict, "fixed: waits for its published time");
        Check(p.Items[5].DelaySeconds == 0, "fixed: absorbs delay for what follows");

        // Fixed start missed: 60 min late → conflict.
        var late = now.AddMinutes(55);
        p = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, 1, true, TimeSpan.Zero, actuals, late, utc));
        Check(p.Items[4].Conflict && p.Items[4].ExpectedStartUtc > p.Items[4].PlannedStartUtc, "fixed: conflict when it can't be reached");

        // Buffer absorbs.
        var withBuffer = new List<RundownItem> { Item("A", 10), new() { Title = "Buffer", DurationSeconds = 600, Kind = SegmentKind.Buffer }, Item("B", 10) };
        var bAct = new Dictionary<int, ItemActual> { [0] = new(anchor, null) };
        p = ScheduleEngine.Project(new ScheduleEngine.Input(withBuffer, anchor, 0, true, TimeSpan.Zero, bAct, anchor.AddMinutes(14), utc));
        Check(p.Items[2].ExpectedStartUtc == anchor.AddMinutes(20) && p.Items[2].DelaySeconds == 0, "buffer: 4 min late absorbed by a 10 min buffer");

        // Pending delay shifts the next item.
        p = ScheduleEngine.Project(new ScheduleEngine.Input(withBuffer, anchor, 0, true, TimeSpan.FromMinutes(5), bAct, anchor.AddMinutes(5), utc, TimeSpan.FromMinutes(3)));
        Check(p.Items[1].ExpectedStartUtc == anchor.AddMinutes(13), "delay: shifts the next item");

        // Not started yet before the show: no "hours ahead".
        p = ScheduleEngine.Project(new ScheduleEngine.Input(withBuffer, anchor, 0, false, TimeSpan.FromMinutes(10), none, anchor.AddHours(-3), utc));
        Check(p.OffsetSeconds == 0, "project: pre-show isn't hours ahead");

        // Hard-out at risk → trims, Q&A first.
        var conf = new List<RundownItem>
        {
            Item("Keynote", 30),
            new() { Title = "Panel", DurationSeconds = 1800, Kind = SegmentKind.Panel, MinDurationSeconds = 1200 },
            new() { Title = "Q&A", DurationSeconds = 900, Kind = SegmentKind.QA, MinDurationSeconds = 300 },
            new() { Title = "Close", DurationSeconds = 300, HardOut = "10:20" }
        };
        var cAct = new Dictionary<int, ItemActual> { [0] = new(anchor, null) };
        p = ScheduleEngine.Project(new ScheduleEngine.Input(conf, anchor, 0, true, TimeSpan.Zero, cAct, anchor.AddMinutes(42), utc));
        Check(p.NextHardOut is { MarginSeconds: -720 }, $"hard-out: 12 min over ({p.NextHardOut?.MarginSeconds})");
        Check(p.Trims.Length >= 1 && p.Trims[0].Index == 2 && p.Trims[0].Seconds == 600, $"trims: Q&A first, down to its minimum ({string.Join(",", p.Trims.Select(t => $"{t.Index}:{t.Seconds}"))})");
        Check(p.Trims.Sum(t => t.Seconds) >= 720 && p.Trims.Any(t => t.Index == 1), "trims: then the panel, enough to cover");

        conf[0].AdjustSeconds = -300;
        Check(conf[0].EffectiveSeconds == 1500, "adjust: effective duration");
    }

    static void TimeZones(Action<bool, string> Check)
    {
        ShowClock ny;
        try { ny = ShowClock.For("America/New_York"); }
        catch { ny = ShowClock.Local; }
        Check(ny.Zone.Id is "America/New_York" or "Eastern Standard Time", $"tz: IANA id resolves ({ny.Zone.Id})");
        Check(ShowClock.For("Not/AZone") == ShowClock.Local && !ShowClock.IsKnownZone("Not/AZone"), "tz: unknown falls back to local");
        // 19:00 show in New York; a "01:00" hard-out means the next morning.
        var start = ny.ToUtc(new DateTime(2026, 10, 24, 19, 0, 0));
        Check(ny.TryResolve("01:00", start.AddHours(5), out var curfew) && ny.ToShow(curfew) == new DateTime(2026, 10, 25, 1, 0, 0), "tz: HH:mm resolves near reference (across midnight)");
        Check(ny.TryResolve("2026-10-26 09:30", start, out var abs) && ny.ToShow(abs) == new DateTime(2026, 10, 26, 9, 30, 0), "tz: dated times for multi-day shows");
        Check(ShowClock.IsValidTimeText("2026-10-26 09:30") && ShowClock.IsValidTimeText("7pm") && !ShowClock.IsValidTimeText("soon"), "tz: validation");
        var london = ShowClock.For("Europe/London");
        // Clocks go back 25 Oct 2026 at 02:00 BST: 01:30 happens twice; conversion must not throw.
        Check(london.TryResolve("2026-10-25 01:30", DateTime.UtcNow, out _), "tz: ambiguous time resolves");
        Check(london.TryResolve("2026-03-29 01:30", DateTime.UtcNow, out var gap) && london.ToShow(gap).Hour >= 2, "tz: non-existent time nudged past the gap");
    }

    static void ForecastAndAdvice(Action<bool, string> Check)
    {
        var utc = new ShowClock(TimeZoneInfo.Utc);
        var anchor = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
        var items = new List<RundownItem>
        {
            Item("Talk", 20), Item("Panel", 40, SegmentKind.Panel), Item("Q&A", 20, SegmentKind.QA), Item("Video", 5, SegmentKind.Video)
        };
        var act = new Dictionary<int, ItemActual> { [0] = new(anchor, null) };
        var now = anchor.AddMinutes(5);
        var p = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, 0, true, TimeSpan.FromMinutes(15), act, now, utc));
        var model = OverrunModel.Learn(Array.Empty<HistoryRecord>());
        var f1 = Forecaster.Run(items, p, 0, now, model, anchor.AddMinutes(85));
        var f2 = Forecaster.Run(items, p, 0, now, model, anchor.AddMinutes(85));
        Check(f1 is not null && f1 == f2, "forecast: deterministic");
        Check(f1!.P50Utc >= p.ExpectedEndUtc && f1.P80Utc >= f1.P50Utc && f1.P95Utc >= f1.P80Utc, $"forecast: panels/Q&A run long, quantiles ordered ({f1.P50Utc:HH:mm} {f1.P80Utc:HH:mm})");
        Check(f1.CurfewRisk > 0.5, $"forecast: curfew at plan end is risky ({f1.CurfewRisk})");
        Check(f1.P50Utc.Second == 0, "forecast: minute-quantised");

        // Learning: this speaker always runs 50% long.
        var hist = Enumerable.Range(0, 6).Select(i => new HistoryRecord(SegmentKind.Talk, "Bob", 600, 900, anchor)).ToList();
        var learned = OverrunModel.Learn(hist);
        Check(learned.For(SegmentKind.Talk, "Bob").Mu > OverrunModel.Prior(SegmentKind.Talk).Mu + 0.15, "forecast: learns a speaker's habit");
        Check(learned.For(SegmentKind.Talk, "Somebody").Mu > OverrunModel.Prior(SegmentKind.Talk).Mu, "forecast: kind learned too (shrunk to prior)");

        using var file = Scratch.File("hist", ".json");
        var h = new OverrunHistory(file.Path);
        h.Add(new HistoryRecord(SegmentKind.Panel, "", 1800, 2100, anchor));
        h.Add(new HistoryRecord(SegmentKind.Panel, "", 5, 2100, anchor)); // nonsense, ignored
        Check(h.Records.Count == 1, "history: sanity filter");
        // The one record kept is saved on a worker: read back by a new history before the file goes, so no write lands after it.
        Check(Scratch.Until(() => new OverrunHistory(file.Path).Records.Count == 1, TimeSpan.FromSeconds(10)), "history: the worker's save lands, and a new history reads it");

        // Advisor: curfew at risk → trim proposals with commands.
        var conf = new List<RundownItem>
        {
            Item("Keynote", 30),
            new() { Title = "Q&A", DurationSeconds = 900, Kind = SegmentKind.QA, MinDurationSeconds = 300, HardOut = "09:40" }
        };
        var cp = ScheduleEngine.Project(new ScheduleEngine.Input(conf, anchor, 0, true, TimeSpan.Zero, new Dictionary<int, ItemActual> { [0] = new(anchor, null) }, anchor.AddMinutes(33), utc));
        var advice = new RuleAdvisor().Advise(new AdvisorInput(conf, cp, 0, true, TimeSpan.Zero, null, null, null, Array.Empty<Signal>(), 0, 0, anchor.AddMinutes(33), utc, false));
        Check(advice.Any(a => a.Level == AdviceLevel.Urgent) && advice.Any(a => a.Command == "trim 2 -480"), $"advisor: curfew → trim command ({string.Join(" | ", advice.Select(a => a.Command))})");

        var sig = new SignalBoard();
        var s = sig.Send(SignalKind.Go, "Sound", "", anchor);
        sig.Update(anchor.AddMinutes(1));
        var adv2 = new RuleAdvisor().Advise(new AdvisorInput(conf, ScheduleProjection.Empty with { Items = cp.Items }, 0, true, TimeSpan.FromMinutes(5), null, null, null, sig.All, 3, 1, anchor, utc, false));
        Check(adv2.Any(a => a.Command == $"signal resend {s.Id}") && adv2.Any(a => a.Key == "qa-last"), "advisor: unacked cue + last question");

        // Pace warning from the ghost run.
        var adv3 = new RuleAdvisor().Advise(new AdvisorInput(conf, ScheduleProjection.Empty with { Items = cp.Items }, 0, true, TimeSpan.FromMinutes(8), null,
            new GhostResult(150, 2000, PaceState.Behind), null, Array.Empty<Signal>(), 0, 0, anchor, utc, false));
        Check(adv3.Any(a => a.Key.StartsWith("pace-") && a.Command!.StartsWith("signal message speaker")), "advisor: pacing to overrun → wrap-up cue");
    }

    static void Pacing(Action<bool, string> Check)
    {
        var budgets = new double[] { 60, 60, 60, 60, 60 }; // 5-minute talk
        var on = GhostRun.Evaluate(budgets, 3, 30, 150);
        Check(on.State == PaceState.OnPace && on.DeltaSeconds == 0 && on.ProjectedTotalSeconds == 300, $"ghost: on pace ({on})");
        var behind = GhostRun.Evaluate(budgets, 2, 10, 130);
        Check(behind.State == PaceState.Behind && behind.DeltaSeconds == 60 && behind.ProjectedTotalSeconds > 300, $"ghost: behind ({behind})");
        var ahead = GhostRun.Evaluate(budgets, 4, 5, 120);
        Check(ahead.State == PaceState.Ahead && ahead.DeltaSeconds < 0, $"ghost: ahead ({ahead})");
        Check(GhostRun.Evaluate(Array.Empty<double>(), 1, 0, 0).State == PaceState.Unknown, "ghost: no rehearsal = unknown");

        var script = "Good morning everyone. Today I want to talk about the future of live events, " +
                     "and why timing is the invisible craft behind every great show. Let us begin with a story about a keynote that ran long.";
        var tr = new ScriptTracker(script);
        var t = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        tr.Feed("good morning everyone today I want", t.AddSeconds(3));
        var p1 = tr.Current(t.AddSeconds(3));
        Check(p1.Word >= 6 && p1.Word <= 8, $"script: tracks the opening ({p1.Word})");
        tr.Feed("uh so um", t.AddSeconds(5)); // filler — no jump
        Check(tr.Position == p1.Word, "script: ad-libs don't move it");
        tr.Feed("timing is the invisable craft behind every", t.AddSeconds(15)); // recogniser error 'invisable'
        Check(tr.Position > 20, $"script: fuzzy match jumps ahead ({tr.Position})");
        var p2 = tr.Current(t.AddSeconds(20));
        Check(p2.WordsPerMinute > 0 && p2.Percent > 40 && p2.TotalWords == ScriptTracker.Tokenize(script).Length, $"script: % and wpm ({p2.Percent}% {p2.WordsPerMinute} wpm)");
        Check(ScriptTracker.Tokenize("Don't stop—café!").SequenceEqual(new[] { "don't", "stop", "cafe" }), "script: tokenizer");
    }

    static void JournalAndReport(Action<bool, string> Check)
    {
        var utc = new ShowClock(TimeZoneInfo.Utc);
        var anchor = new DateTime(2026, 10, 24, 9, 0, 0, DateTimeKind.Utc);
        var items = new List<RundownItem> { Item("Opening", 10), Item("Keynote", 30) };
        items[1].Speaker = "Ana";
        items[1].AdjustSeconds = -120;
        using var file = Scratch.File("journal", ".jsonl");
        var path = file.Path;
        var j = new ShowJournal(path);
        j.Add(anchor, JournalKind.ItemStart, 0);
        j.Add(anchor.AddMinutes(11), JournalKind.ItemEnd, 0);
        j.Add(anchor.AddMinutes(11), JournalKind.ItemStart, 1);
        j.Add(anchor.AddMinutes(35), JournalKind.Phase, 1, "WrapUp");
        j.Add(anchor.AddMinutes(36), JournalKind.Signal, 1, "Wrap up");
        j.Add(anchor.AddMinutes(36).AddSeconds(5), JournalKind.Ack, 1, "Ana", 5);
        j.Add(anchor.AddMinutes(40), JournalKind.ItemEnd, 1);
        j.DisposeAsync().AsTask().Wait();
        var back = ShowJournal.Load(path);
        Check(back.Count == 7 && back[5].Kind == JournalKind.Ack && back[5].Value == 5, "journal: written and read back");
        File.AppendAllText(path, "{broken\n");
        Check(ShowJournal.Load(path).Count == 7, "journal: damaged lines skipped");

        var r = ShowReport.Build("Summit", new Rundown { Items = items.ToList() }, back, anchor, utc, anchor.AddHours(1));
        Check(r.Rows[0].ActualSeconds == 660 && r.Rows[0].OverrunSeconds == 60, "report: actual + overrun");
        Check(r.Rows[1].ActualSeconds == 1740 && r.Rows[1].OverrunSeconds == 1740 - 1680 && r.Rows[1].WrapUpToFinishSeconds == 300, "report: trims count, wrap-up → finish");
        Check(r.Rows[1].Acks == 1 && r.Rows[1].AvgAckSeconds == 5 && r.Rows[1].Messages == 1, "report: cues and acks");
        var csv = r.ToCsv(utc);
        Check(csv.StartsWith("#,Title") && csv.Contains("Keynote,Ana,Talk"), "report: CSV");
        var html = r.ToHtml(utc);
        Check(html.Contains("<h1>Summit</h1>") && html.Contains("Keynote") && html.Contains("class=\"n over\""), "report: HTML");
    }

    static void Json(Action<bool, string> Check)
    {
        var a = new ShowSnapshot
        {
            Signals = new[] { new SignalView(1, SignalKind.Go, "Lighting", "", SignalState.Sent, false, 0, "") }.ToEquatableArray(),
            Timings = new[] { new ItemTimingView(0, "A", "", SegmentKind.Talk, 60, 0, "09:00", "09:00", "09:01", "", "", ItemStatus.Live, 0, false, false, "") }.ToEquatableArray()
        };
        var b = a with
        {
            Signals = new[] { new SignalView(1, SignalKind.Go, "Lighting", "", SignalState.Sent, false, 0, "") }.ToEquatableArray(),
            Timings = new[] { new ItemTimingView(0, "A", "", SegmentKind.Talk, 60, 0, "09:00", "09:00", "09:01", "", "", ItemStatus.Live, 0, false, false, "") }.ToEquatableArray()
        };
        Check(a.Equals(b), "snapshot: value equality through arrays");
        Check(!a.Equals(b with { Signals = EquatableArray<SignalView>.Empty }), "snapshot: array change detected");
        var basic = Encoding.UTF8.GetString(SnapshotJson.Serialize(a));
        var detail = Encoding.UTF8.GetString(SnapshotJson.Serialize(a, detail: true));
        Check(basic.Contains("\"signals\":[{\"id\":1,\"kind\":\"go\"") && !basic.Contains("\"timings\""), "json: basic frame has signals, no timings");
        Check(detail.Contains("\"timings\":[{\"i\":0") && detail.Contains("\"detail\":true"), "json: detail frame has timings");
        using var doc = System.Text.Json.JsonDocument.Parse(detail);
        Check(doc.RootElement.GetProperty("slides").ValueKind == System.Text.Json.JsonValueKind.Null, "json: valid, nullable sections");
    }
}
