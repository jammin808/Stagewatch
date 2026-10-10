using System.Globalization;

namespace Stagewatch.Core;

/// <summary>
/// The show's wall clock: a time zone plus parsing of "HH:mm" / "yyyy-MM-dd HH:mm" into UTC.
/// Everything inside the engine is UTC so midnight and daylight-saving changes can't move the plan.
/// </summary>
public sealed class ShowClock
{
    public TimeZoneInfo Zone { get; }

    public ShowClock(TimeZoneInfo zone) { Zone = zone; }

    public static ShowClock Local { get; } = new(TimeZoneInfo.Local);

    /// <summary>The named zone (Windows or IANA id), or this PC's zone if it's blank or unknown.</summary>
    public static ShowClock For(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return Local;
        try { return new ShowClock(TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim())); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return Local; }
    }

    public static bool IsKnownZone(string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId)) return true;
        try { TimeZoneInfo.FindSystemTimeZoneById(zoneId.Trim()); return true; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }

    public DateTime ToShow(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public DateTime ToUtc(DateTime showTime)
    {
        var t = DateTime.SpecifyKind(showTime, DateTimeKind.Unspecified);
        // A time that doesn't exist (clocks going forward) is nudged past the gap.
        while (Zone.IsInvalidTime(t)) t = t.AddMinutes(30);
        return TimeZoneInfo.ConvertTimeToUtc(t, Zone);
    }

    /// <summary>
    /// "yyyy-MM-dd HH:mm" → that moment. "HH:mm" (any form <see cref="Schedule.TryParseClock"/> takes)
    /// → the occurrence within 12 h of <paramref name="referenceUtc"/>, so a 01:00 hard-out on a show that
    /// starts at 19:00 means the next morning.
    /// </summary>
    public bool TryResolve(string? text, DateTime referenceUtc, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (DateTime.TryParseExact(text, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var abs))
        {
            utc = ToUtc(abs);
            return true;
        }
        if (!Schedule.TryParseClock(text, out var tod)) return false;
        var refShow = ToShow(referenceUtc);
        var candidate = refShow.Date + tod;
        if (candidate - refShow > Schedule.ClockWindow) candidate = candidate.AddDays(-1);
        else if (refShow - candidate > Schedule.ClockWindow) candidate = candidate.AddDays(1);
        utc = ToUtc(candidate);
        return true;
    }

    public static bool IsValidTimeText(string? text)
        => string.IsNullOrWhiteSpace(text)
           || Schedule.TryParseClock(text, out _)
           || DateTime.TryParseExact(text.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
               CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

public enum ItemStatus { Done, Live, Loaded, Upcoming }

/// <summary>One running-order line: the plan, the current expectation and what actually happened (all UTC).</summary>
public readonly record struct ItemTiming(
    int Index,
    DateTime PlannedStartUtc,
    DateTime PlannedEndUtc,
    DateTime ExpectedStartUtc,
    DateTime ExpectedEndUtc,
    DateTime? ActualStartUtc,
    DateTime? ActualEndUtc,
    ItemStatus Status,
    /// <summary>Expected start minus planned start (positive = late).</summary>
    long DelaySeconds,
    /// <summary>A fixed-start item the show can no longer reach on time, or a plan that overlaps.</summary>
    bool Conflict);

/// <summary>A curfew and how the show is tracking against it (negative margin = will overrun).</summary>
public sealed record HardOutStatus(int Index, DateTime CurfewUtc, DateTime ExpectedUtc, long MarginSeconds);

/// <summary>"Cut item 7 by 3 minutes" — produced only for items marked compressible.</summary>
public sealed record TrimSuggestion(int Index, int Seconds, string Reason);

public sealed record ScheduleProjection(
    ItemTiming[] Items,
    long? OffsetSeconds,
    DateTime PlannedEndUtc,
    DateTime ExpectedEndUtc,
    HardOutStatus? NextHardOut,
    TrimSuggestion[] Trims)
{
    public static readonly ScheduleProjection Empty = new(Array.Empty<ItemTiming>(), null, default, default, null, Array.Empty<TrimSuggestion>());
}

/// <summary>When an item really started and ended (UTC). Recorded by the app as the show runs.</summary>
public readonly record struct ItemActual(DateTime? StartUtc, DateTime? EndUtc);

/// <summary>
/// Schedule recovery, as a pure function of (plan, what happened, now). No clocks, no state, so it
/// can run every frame and be tested exhaustively.
///
/// Rules (the delay model experienced show callers expect):
///   * Sequential items follow the previous one, so lateness ripples on.
///   * Fixed-start items are held to their published time: early → they wait (absorbing delay);
///     late → they start as soon as possible and are flagged as a conflict.
///   * Buffer items shrink to absorb lateness that arrives at them.
///   * A pending "delay" (e.g. a stage reset announced in advance) shifts the next item.
///   * Hard-outs are checked against the expected end; when one is at risk, compressible items
///     (with a minimum duration) are proposed for trimming — Q&amp;A and panels first, latest first.
/// </summary>
public static class ScheduleEngine
{
    public sealed record Input(
        IReadOnlyList<RundownItem> Items,
        DateTime AnchorUtc,
        int Current,
        bool CurrentStarted,
        TimeSpan CurrentRemaining,
        IReadOnlyDictionary<int, ItemActual> Actuals,
        DateTime NowUtc,
        ShowClock Clock,
        TimeSpan PendingDelay = default,
        bool CurrentIsLive = true,
        string? EndOfShow = null);

    /// <summary>
    /// Back-timing: the start that makes the plan end exactly at <paramref name="endUtc"/> (fixed starts included —
    /// the start is moved until the planned end lands, a few passes at most). Null when it can't (only fixed items).
    /// </summary>
    public static DateTime? BackTimedAnchor(IReadOnlyList<RundownItem> items, DateTime endUtc, ShowClock clock)
    {
        if (items.Count == 0) return null;
        var anchor = endUtc.AddSeconds(-items.Sum(i => (double)Math.Max(0, i.DurationSeconds)));
        for (int pass = 0; pass < 6; pass++)
        {
            var plan = Plan(items, anchor, clock);
            var diff = plan[^1].End - endUtc;
            if (Math.Abs(diff.TotalSeconds) < 1) return anchor;
            var before = anchor;
            anchor -= diff;
            if (anchor == before) break;
        }
        return Math.Abs((Plan(items, anchor, clock)[^1].End - endUtc).TotalSeconds) < 1 ? anchor : null;
    }

    /// <summary>Planned starts only (what the running-order panel prints next to each item).</summary>
    public static (DateTime Start, DateTime End, bool Conflict)[] Plan(IReadOnlyList<RundownItem> items, DateTime anchorUtc, ShowClock clock)
    {
        var plan = new (DateTime, DateTime, bool)[items.Count];
        var t = anchorUtc;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var start = t;
            var conflict = false;
            if (it.StartMode == StartMode.Fixed && clock.TryResolve(it.FixedStart, t, out var fixedUtc))
            {
                conflict = fixedUtc < t; // the plan itself doesn't fit before this fixed start
                start = fixedUtc;
            }
            var end = start.AddSeconds(Math.Max(0, it.DurationSeconds));
            plan[i] = (start, end, conflict);
            t = end;
        }
        return plan;
    }

    public static ScheduleProjection Project(Input x)
    {
        var items = x.Items;
        if (items.Count == 0) return ScheduleProjection.Empty;
        var plan = Plan(items, x.AnchorUtc, x.Clock);
        var timings = new ItemTiming[items.Count];
        var now = x.NowUtc;
        var cur = x.Current;
        var prevEnd = x.AnchorUtc;

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var (ps, pe, planConflict) = plan[i];
            x.Actuals.TryGetValue(i, out var act);
            DateTime es, ee;
            ItemStatus status;
            var conflict = planConflict;

            if (cur >= 0 && i < cur)
            {
                status = ItemStatus.Done;
                es = act.StartUtc ?? ps;
                ee = act.EndUtc ?? NextActualStart(x.Actuals, i, items.Count) ?? (act.StartUtc is { } st ? st.AddSeconds(it.EffectiveSeconds) : pe);
            }
            else if (i == cur)
            {
                var rem = x.CurrentRemaining < TimeSpan.Zero ? TimeSpan.Zero : x.CurrentRemaining;
                if (x.CurrentStarted)
                {
                    status = ItemStatus.Live;
                    es = act.StartUtc ?? now - TimeSpan.FromSeconds(Math.Max(0, it.EffectiveSeconds - rem.TotalSeconds));
                    ee = now + rem;
                }
                else
                {
                    status = ItemStatus.Loaded;
                    // Not started: it can't start before its slot (a pre-show "now" isn't "hours ahead").
                    var earliest = now < ps ? ps : now;
                    if (it.StartMode == StartMode.Fixed && now < ps) earliest = ps;
                    es = earliest;
                    ee = es + rem;
                }
            }
            else
            {
                status = ItemStatus.Upcoming;
                es = prevEnd;
                if (cur >= 0 && i == cur + 1 && x.PendingDelay > TimeSpan.Zero) es += x.PendingDelay;
                if (cur < 0 && es < ps) es = ps; // nothing running yet: the plan stands
                if (it.StartMode == StartMode.Fixed && it.FixedStart is not null)
                {
                    if (es < ps) es = ps;                 // hold to the published time
                    else if (es > ps.AddSeconds(30)) conflict = true; // can't make it
                }
                var dur = (double)it.EffectiveSeconds;
                if (it.Kind == SegmentKind.Buffer)
                    dur = Math.Max(0, dur - Math.Max(0, (es - ps).TotalSeconds)); // buffers soak up lateness
                ee = es.AddSeconds(dur);
            }

            timings[i] = new ItemTiming(i, ps, pe, es, ee, act.StartUtc, act.EndUtc, status,
                (long)Math.Round((es - ps).TotalSeconds), conflict);
            prevEnd = ee;
        }

        long? offset = null;
        if (cur >= 0 && cur < items.Count)
        {
            var t = timings[cur];
            offset = (long)Math.Round((t.ExpectedEndUtc - PlannedEndOfCurrent(items, plan, cur)).TotalSeconds);
        }

        var hardOuts = new List<HardOutStatus>();
        for (int i = Math.Max(0, cur); i < items.Count; i++)
        {
            // The show's end time counts as the last item's curfew (unless that item has its own).
            var hardOut = items[i].HardOut;
            if (string.IsNullOrWhiteSpace(hardOut) && i == items.Count - 1) hardOut = x.EndOfShow;
            if (string.IsNullOrWhiteSpace(hardOut)) continue;
            if (!x.Clock.TryResolve(hardOut, plan[i].End, out var curfew)) continue;
            hardOuts.Add(new HardOutStatus(i, curfew, timings[i].ExpectedEndUtc, (long)Math.Round((curfew - timings[i].ExpectedEndUtc).TotalSeconds)));
        }
        // The one that matters: the first at risk, else the next one coming.
        var next = hardOuts.FirstOrDefault(h => h.MarginSeconds < 0) ?? hardOuts.FirstOrDefault();

        var trims = next is { MarginSeconds: < 0 } risk
            ? SuggestTrims(items, timings, cur, risk.Index, -risk.MarginSeconds, now)
            : Array.Empty<TrimSuggestion>();

        return new ScheduleProjection(timings, offset, plan[^1].End, timings[^1].ExpectedEndUtc, next, trims);
    }

    /// <summary>The planned end of the current item, trims included (a trim moves the target, not the clock).</summary>
    private static DateTime PlannedEndOfCurrent(IReadOnlyList<RundownItem> items, (DateTime Start, DateTime End, bool Conflict)[] plan, int cur)
        => plan[cur].Start.AddSeconds(items[cur].EffectiveSeconds);

    private static DateTime? NextActualStart(IReadOnlyDictionary<int, ItemActual> actuals, int i, int count)
    {
        for (int j = i + 1; j < count; j++)
            if (actuals.TryGetValue(j, out var a) && a.StartUtc is { } s) return s;
        return null;
    }

    private static int TrimPriority(SegmentKind k) => k switch
    {
        SegmentKind.QA => 0,
        SegmentKind.Panel => 1,
        SegmentKind.Break => 2,
        SegmentKind.Other => 3,
        SegmentKind.Talk => 4,
        SegmentKind.Award => 5,
        _ => 9 // video and buffers are never proposed
    };

    /// <summary>Greedy: Q&amp;A, then panels, then breaks… latest first, each down to its minimum.</summary>
    public static TrimSuggestion[] SuggestTrims(IReadOnlyList<RundownItem> items, ItemTiming[] timings, int current, int upTo, long needSeconds, DateTime nowUtc)
    {
        var candidates = new List<(int Index, int Available)>();
        for (int i = Math.Max(0, current); i <= upTo && i < items.Count; i++)
        {
            var it = items[i];
            if (it.MinDurationSeconds is not { } min || it.Kind is SegmentKind.Video or SegmentKind.Buffer) continue;
            var span = timings[i].Status == ItemStatus.Live
                ? (int)Math.Max(0, (timings[i].ExpectedEndUtc - timings[i].ExpectedStartUtc).TotalSeconds)
                : it.EffectiveSeconds;
            var available = span - Math.Max(0, min);
            if (timings[i].Status == ItemStatus.Live)
                available = Math.Min(available, (int)Math.Max(0, (timings[i].ExpectedEndUtc - nowUtc).TotalSeconds)); // can't cut time already spent
            if (available >= 30) candidates.Add((i, available));
        }

        var result = new List<TrimSuggestion>();
        var need = needSeconds;
        foreach (var c in candidates.OrderBy(c => TrimPriority(items[c.Index].Kind)).ThenByDescending(c => c.Index))
        {
            if (need <= 0) break;
            // Round to whole minutes where possible: humans announce "cut Q&A by 3 minutes", not 2:47.
            var take = (int)Math.Min(c.Available, need);
            var rounded = (int)Math.Min(c.Available, Math.Ceiling(take / 60.0) * 60);
            result.Add(new TrimSuggestion(c.Index, rounded,
                $"{items[c.Index].Kind} \"{items[c.Index].Title}\" can lose {TimerTick.FormatSeconds(rounded)} (min {TimerTick.FormatSeconds(items[c.Index].MinDurationSeconds ?? 0)})"));
            need -= rounded;
        }
        return result.ToArray();
    }
}
