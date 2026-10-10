namespace Stagewatch.Core;

/// <summary>
/// Roll: the running order follows the clock with no operator — for walk-in loops, breaks and unattended rooms. At
/// any moment the item whose planned window contains now is running (with the time left to its planned end); in a
/// gap or before the first item, the next item is loaded and the timer counts down to its start; after the last item
/// the show is over. Pure: give it the plan and the time.
/// </summary>
public static class Roll
{
    public enum Kind { Run, Wait, Over }

    /// <param name="Item">The item to run, or to count down to.</param>
    /// <param name="Remaining">Run: time to the item's planned end. Wait: time to its planned start.</param>
    public readonly record struct Decision(Kind What, int Item, TimeSpan Remaining);

    /// <summary>
    /// For a looping running order: the anchor of the pass that's on now. <paramref name="loops"/> = extra passes
    /// (-1 = forever). Past the last pass it stays on the last pass, so Decide reports the show is over.
    /// </summary>
    public static DateTime LoopAnchor(DateTime anchorUtc, TimeSpan cycle, int loops, DateTime nowUtc)
    {
        if (loops == 0 || cycle <= TimeSpan.Zero || nowUtc < anchorUtc + cycle) return anchorUtc;
        var pass = (long)Math.Floor((nowUtc - anchorUtc).TotalSeconds / cycle.TotalSeconds);
        if (loops > 0) pass = Math.Min(pass, loops);
        return anchorUtc + TimeSpan.FromTicks(cycle.Ticks * pass);
    }

    public static Decision Decide(IReadOnlyList<(DateTime Start, DateTime End, bool Conflict)> plan, DateTime nowUtc)
    {
        for (int i = 0; i < plan.Count; i++)
        {
            var (start, end, _) = plan[i];
            if (end <= start) continue;                                   // zero-length items are skipped
            if (nowUtc >= start && nowUtc < end) return new Decision(Kind.Run, i, end - nowUtc);
            if (nowUtc < start) return new Decision(Kind.Wait, i, start - nowUtc);
        }
        return new Decision(Kind.Over, -1, TimeSpan.Zero);
    }
}
