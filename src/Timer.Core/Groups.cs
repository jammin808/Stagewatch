namespace Stagewatch.Core;

/// <summary>
/// Groups of running-order items: consecutive items that share a group name (a session, an act, a block). Each group
/// adds up its planned length and, when the producer set one, compares it with a target — so an overfull session
/// shows while planning, and live you can see when the session will really end. Pure: no clocks, no state.
/// </summary>
public static class RundownGroups
{
    /// <summary>Contiguous runs of the same (non-empty) group name, as [first, last] item indexes.</summary>
    public static IReadOnlyList<(string Name, int First, int Last)> Spans(IReadOnlyList<RundownItem> items)
    {
        var spans = new List<(string, int, int)>();
        for (int i = 0; i < items.Count;)
        {
            var g = (items[i].Group ?? "").Trim();
            if (g.Length == 0) { i++; continue; }
            var j = i;
            while (j + 1 < items.Count && string.Equals((items[j + 1].Group ?? "").Trim(), g, StringComparison.OrdinalIgnoreCase)) j++;
            spans.Add((g, i, j));
            i = j + 1;
        }
        return spans;
    }

    /// <summary>The target for a group, if the producer set one (case-insensitive name).</summary>
    public static int? Target(Rundown rundown, string group)
    {
        foreach (var (k, v) in rundown.GroupTargets)
            if (string.Equals(k, group, StringComparison.OrdinalIgnoreCase) && v > 0) return v;
        return null;
    }

    /// <summary>Planned seconds of a group (effective lengths: the plan plus live trims).</summary>
    public static long PlannedSeconds(IReadOnlyList<RundownItem> items, int first, int last)
    {
        long sum = 0;
        for (int i = first; i <= last && i < items.Count; i++) sum += items[i].EffectiveSeconds;
        return sum;
    }

    /// <summary>Group views for a frame. <paramref name="p"/> may be empty (no plan yet): then only lengths are known.</summary>
    public static GroupView[] Build(Rundown rundown, ScheduleProjection p, DateTime nowUtc, Func<DateTime, string> hm)
    {
        var items = rundown.Items;
        var spans = Spans(items);
        var result = new GroupView[spans.Count];
        var timed = p.Items.Length == items.Count && items.Count > 0;
        for (int k = 0; k < spans.Count; k++)
        {
            var (name, first, last) = spans[k];
            long? startIn = null, endIn = null;
            string planned = "", expectedEnd = "";
            var status = ItemStatus.Upcoming;
            if (timed)
            {
                var a = p.Items[first];
                var b = p.Items[last];
                var start = a.ActualStartUtc ?? a.ExpectedStartUtc;
                var end = b.ActualEndUtc ?? b.ExpectedEndUtc;
                startIn = (long)Math.Round((start - nowUtc).TotalSeconds);
                endIn = (long)Math.Round((end - nowUtc).TotalSeconds);
                planned = hm(a.PlannedStartUtc);
                expectedEnd = hm(end);
                var statuses = p.Items.Skip(first).Take(last - first + 1).Select(x => x.Status).ToList();
                status = statuses.All(s => s == ItemStatus.Done) ? ItemStatus.Done
                    : statuses.Any(s => s is ItemStatus.Live or ItemStatus.Loaded) ? ItemStatus.Live
                    : statuses.Any(s => s == ItemStatus.Done) ? ItemStatus.Live
                    : ItemStatus.Upcoming;
            }
            result[k] = new GroupView(name, first, last, PlannedSeconds(items, first, last), Target(rundown, name), startIn, endIn, planned, expectedEnd, status);
        }
        return result;
    }

    /// <summary>"over by 05:00" / "02:00 spare" / "" — the planned length against the target.</summary>
    public static string TargetText(GroupView g)
    {
        if (g.TargetSeconds is not { } t) return "";
        var diff = g.PlannedSeconds - t;
        return Math.Abs(diff) < 30 ? "on target" : diff > 0 ? $"over by {TimerTick.FormatSeconds(diff)}" : $"{TimerTick.FormatSeconds(-diff)} spare";
    }
}
