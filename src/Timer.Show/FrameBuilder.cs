using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// Everything the show contributes to one frame, read on the owner thread. The host gathers it; the builder only reads it.
/// </summary>
/// <param name="Tick">What the timer shows: the engine's tick, or the simulated one during a briefing preview.</param>
/// <param name="EngineTick">The engine's own tick (the schedule always projects from the real clock).</param>
/// <param name="EndAt">An explicit "end at" time (local), when one is set.</param>
/// <param name="EndsAtWallClock">When the running countdown will reach zero (local), if it is counting.</param>
/// <param name="DefaultLadderId">The show's cue ladder, used when the live item doesn't pick its own.</param>
public sealed record FrameInput(
    DateTime NowUtc,
    DateTime LocalNow,
    TimerTick Tick,
    TimerTick EngineTick,
    bool Preview,
    bool Stretching,
    DateTime? EndAt,
    DateTime? EndsAtWallClock,
    bool ManualTimerFlash,
    SpeakerTimerFormat SpeakerFormat,
    string DefaultLadderId,
    AppSettings Settings,
    ShowController Show)
{
    public ShowClock Clock { get; } = ShowClock.For(Settings.Rundown.TimeZoneId);
}

/// <summary>A cue ladder's verdict for this second: phase, colour, label and flash.</summary>
public readonly record struct CueState(TimerPhase Phase, string Color, string Label, FlashRate Flash, CueStep? Step);

/// <summary>
/// Builds the show's part of a frame — cue phase and colours, flashing, the schedule projection, the speaker's face,
/// message placeholders, hard-outs and the running-order timings — as a pure function of <see cref="FrameInput"/>.
/// The host supplies the rest (sensors, outputs, signals, Q&amp;A) as an "ambient" snapshot the builder completes, so
/// one frame is still one immutable object. No UI, no clocks of its own: tested headless.
/// </summary>
public static class FrameBuilder
{
    /// <summary>The cue ladder for whatever is loaded: the item's own, else the show default.</summary>
    public static CueLadder LadderFor(AppSettings settings, ShowController show, string defaultLadderId)
    {
        var items = settings.Rundown.Items;
        string? id = show.RundownActive && show.CurrentItem >= 0 && show.CurrentItem < items.Count ? items[show.CurrentItem].CueLadderId : null;
        return settings.Display.LadderFor(string.IsNullOrWhiteSpace(id) ? defaultLadderId : id);
    }

    public static CueState EvaluateCue(DisplaySettings d, TimerTick t, long secs, CueLadder ladder)
    {
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

    /// <summary>
    /// Where the running order is heading: expected starts and ends, the offset, curfews. Computed first, because the
    /// host's forecast and advice are built from it before the frame is finished.
    /// </summary>
    public static ScheduleProjection Project(FrameInput f)
    {
        var items = f.Settings.Rundown.Items;
        if (items.Count == 0 || f.Show.ScheduleAnchor() is not { } anchor) return ScheduleProjection.Empty;
        var cur = f.Show.RundownActive ? f.Show.CurrentItem : -1;
        var rem = cur >= 0 && f.Tick.Mode == TimerMode.Countdown && !f.Preview ? f.EngineTick.Remaining : TimeSpan.Zero;
        return ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, cur, f.Show.ItemStarted, rem, f.Show.Actuals, f.NowUtc, f.Clock,
            f.Show.PendingDelay, EndOfShow: f.Settings.Rundown.EndOfShow));
    }

    /// <summary>
    /// Complete <paramref name="ambient"/> (the host's sensors and outputs) with the show's part of the frame.
    /// </summary>
    public static ShowSnapshot Build(FrameInput f, ScheduleProjection projection, ForecastResult? forecast, EquatableArray<Advice> advice, ShowSnapshot ambient)
    {
        var t = f.Tick;
        var show = f.Show;
        var settings = f.Settings;
        var disp = settings.Display;
        var msg = settings.MessageState;
        var clock = f.Clock;
        var twelve = disp.Use12HourClock;
        string Fmt(DateTime local, bool seconds = true) => Stagewatch.Core.Clock.Time(local, twelve, seconds);

        var secs = t.DisplaySeconds;
        var cue = EvaluateCue(disp, t, secs, LadderFor(settings, show, f.DefaultLadderId));
        var phase = cue.Phase;

        // Effective message flash/urgency: operator's choice, overridden by the cue ladder and thresholds.
        var urgency = msg.Urgency;
        var flash = msg.Flash;
        var flashTimer = f.ManualTimerFlash;
        if (t.Mode == TimerMode.Countdown && t.State is TimerState.Running or TimerState.Expired)
        {
            if (cue.Flash != FlashRate.Off) { flashTimer = true; flash = cue.Flash; }
            else if (phase == TimerPhase.Overtime) { }
            else if (secs <= msg.AutoFlashCriticalSeconds) { flash = FlashRate.Fast; urgency = MessageUrgency.Critical; }
            else if (secs <= msg.AutoFlashWarnSeconds) { flash = FlashRate.Slow; urgency = MessageUrgency.Warning; }
        }

        var items = settings.Rundown.Items;
        var live = show.RundownActive && show.CurrentItem >= 0 && show.CurrentItem < items.Count ? items[show.CurrentItem] : null;
        var next = show.RundownActive && show.CurrentItem >= 0 && show.CurrentItem + 1 < items.Count ? items[show.CurrentItem + 1].DisplayTitle : "";

        // ---- schedule recovery
        long? offset = null;
        var scheduleText = "";
        if (live is not null && t.Mode == TimerMode.Countdown && projection.Items.Length > 0)
        {
            offset = projection.OffsetSeconds;
            var end = clock.ToShow(projection.ExpectedEndUtc);
            scheduleText = $"{Schedule.DescribeOffset(TimeSpan.FromSeconds(offset ?? 0))} · show ends {Fmt(end, seconds: false)}"
                           + (end.Date != clock.ToShow(f.NowUtc).Date ? $" {end:ddd}" : "");
        }

        // ---- speaker face (the operator always sees the true engine value)
        var operatorText = t.Mode == TimerMode.TimeOfDay && twelve ? Fmt(f.LocalNow.Date + t.Remaining) : t.Format();
        var speakerSeconds = secs;
        var speakerBase = operatorText;
        var stretching = !f.Preview && f.Stretching;
        if (stretching && disp.HonestSpeakerClock && f.EndAt is { } endAt)
        {
            // Never show the speaker a stretched clock: give them the real time left.
            speakerSeconds = (long)Math.Ceiling((endAt - f.LocalNow).TotalSeconds - 0.0005);
            speakerBase = TimerTick.FormatSeconds(speakerSeconds);
        }
        var speakerText = t.Mode == TimerMode.Countdown ? SpeakerFace.Format(f.SpeakerFormat, speakerSeconds, speakerBase) : operatorText;
        // "Hide at zero": speaker screens go blank in overtime (a space, so they don't switch to the progress-only bar);
        // the operator's own display still counts on.
        if (settings.HideAtZero && t.Mode == TimerMode.Countdown && secs <= 0 && t.State is TimerState.Running or TimerState.Expired) speakerText = " ";
        var finishBy = t.Mode == TimerMode.Countdown && t.State != TimerState.Expired && !f.Preview
            ? SpeakerFace.FinishBy(f.EndsAtWallClock, twelve) : "";

        // ---- the message, placeholders re-rendered every frame
        var message = Placeholders.Render(msg.Text, new Placeholders.Context(
            next, finishBy.Replace("FINISH BY ", ""), Placeholders.OverUnder(offset), TimerTick.FormatSeconds(Math.Max(0, secs)),
            show.ActiveTitle, live?.Speaker ?? "", Fmt(f.LocalNow, seconds: false)));

        // ---- hard-outs and the forecast
        var hardOutText = "";
        long? hardOutMargin = null;
        if (projection.NextHardOut is { } h)
        {
            hardOutMargin = h.MarginSeconds;
            hardOutText = $"Curfew {Fmt(clock.ToShow(h.CurfewUtc), seconds: false)}: " + (h.MarginSeconds >= 0
                ? $"{TimerTick.FormatSeconds(h.MarginSeconds)} to spare"
                : $"{TimerTick.FormatSeconds(-h.MarginSeconds)} OVER");
        }
        var forecastText = forecast is { } fc && show.RundownActive
            ? $"Likely end {Fmt(clock.ToShow(fc.P50Utc), seconds: false)} · 80% by {Fmt(clock.ToShow(fc.P80Utc), seconds: false)}"
              + (fc.CurfewRisk > 0 ? $" · curfew risk {fc.CurfewRisk:P0}" : "")
            : "";

        var showNow = clock.ToShow(f.NowUtc);
        return ambient with
        {
            Mode = t.Mode,
            State = t.State,
            Seconds = secs,
            Text = operatorText,
            TotalSeconds = (long)Math.Round(t.Total.TotalSeconds),
            Phase = phase,
            ColorHex = cue.Color,
            Title = f.Preview ? "PREVIEW — " + (show.ActiveTitle.Length > 0 ? show.ActiveTitle : "cue briefing") : show.ActiveTitle,
            NextTitle = next,
            ItemIndex = show.RundownActive ? show.CurrentItem : -1,
            ItemCount = items.Count,
            PresetSlot = !show.RundownActive && t.Mode != TimerMode.TimeOfDay ? t.PresetSlot ?? 0 : 0,
            EndTimeText = f.Preview ? "" : EndTimeText(f),
            TimeOfDay = Fmt(f.LocalNow),
            OffsetSeconds = offset,
            ScheduleText = scheduleText,
            Message = message,
            Urgency = urgency,
            Flash = flash,
            FlashTimer = flashTimer,
            Elements = disp.Elements,
            SpeakerText = speakerText,
            CueLabel = cue.Label,
            FinishBy = finishBy,
            Preview = f.Preview,
            Stretching = stretching,
            StableLayout = disp.StableLayout,
            HoloFloor = disp.HoloFloor,
            EggTimer = disp.EggTimer,
            Animations3D = disp.Animations3D,
            Particles3D = disp.Particles3D,
            Room = settings.Integrations.RoomName,
            Timings = Timings(projection, items, clock, twelve, f.NowUtc),
            Groups = RundownGroups.Build(settings.Rundown, projection, f.NowUtc,
                u => Stagewatch.Core.Clock.Time(clock.ToShow(u), twelve, seconds: false)).ToEquatableArray(),
            ClockSeconds = (long)showNow.TimeOfDay.TotalSeconds,
            UtcSeconds = new DateTimeOffset(f.NowUtc).ToUnixTimeSeconds(),
            LabelColors = string.Join(";", Labels.InUse(items).Select((l, i) => l + "=" + Labels.ColorFor(settings.Rundown, l, i))),
            Roll = show.RollMode,
            RundownName = settings.RundownName,
            DateText = showNow.ToString("dddd d MMMM", CultureInfo.CurrentCulture),
            HardOutText = hardOutText,
            HardOutMarginSeconds = hardOutMargin,
            ForecastText = forecastText,
            PendingDelaySeconds = (long)show.PendingDelay.TotalSeconds,
            Advice = advice
        };
    }

    /// <summary>"ends 14:32:10" (or "ENDS …" for an explicit end time), or "" when nothing is counting down.</summary>
    public static string EndTimeText(FrameInput f)
    {
        if (f.EndsAtWallClock is not { } endsAt || f.EngineTick.Mode != TimerMode.Countdown) return "";
        var explicitEnd = f.EndAt is not null;
        if (!explicitEnd && !f.Settings.Display.ShowEndTime) return "";
        var label = explicitEnd ? "ENDS" : "ends";
        if (endsAt.Date != f.LocalNow.Date) label += " " + endsAt.ToString("ddd", CultureInfo.CurrentCulture);
        return $"{label} {Stagewatch.Core.Clock.Time(endsAt, f.Settings.Display.Use12HourClock, true)}";
    }

    /// <summary>Each item's planned, expected and actual times, as the running-order screens show them.</summary>
    public static EquatableArray<ItemTimingView> Timings(ScheduleProjection p, IReadOnlyList<RundownItem> items, ShowClock clock, bool twelve, DateTime nowUtc)
    {
        if (p.Items.Length != items.Count) return EquatableArray<ItemTimingView>.Empty;
        string Hm(DateTime utc) => Stagewatch.Core.Clock.Time(clock.ToShow(utc), twelve, seconds: false);
        string Hms(DateTime? utc) => utc is { } u ? Stagewatch.Core.Clock.Time(clock.ToShow(u), twelve, seconds: true) : "";
        long In(DateTime utc) => (long)Math.Round((utc - nowUtc).TotalSeconds);
        var list = new ItemTimingView[items.Count];
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
}
