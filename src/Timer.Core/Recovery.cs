namespace Stagewatch.Core;

public enum RecoverMethod
{
    /// <summary>Each item gives in proportion to its length (a 40-minute talk gives four times a 10-minute one).</summary>
    Proportional,
    /// <summary>Every item gives the same, as far as its floor allows.</summary>
    Even,
    /// <summary>Buffers give everything first, then the rest proportionally.</summary>
    BuffersFirst
}

/// <summary>
/// Win back (or give out) time across upcoming items. Better than "shorten evenly": it respects each item's minimum
/// (or, with none set, never cuts an item below half), redistributes what an item can't give, can take buffers first,
/// never touches what's already over, rounds to 15 s so cues read cleanly, and says plainly when the time isn't there.
/// Pure: items in, trims out. Nothing changes until the operator applies the trims.
/// </summary>
public static class Recovery
{
    public const int Step = 15;

    /// <param name="seconds">Positive = win back (shorten); negative = give out (lengthen, e.g. running early).</param>
    /// <param name="first">First item that may change (the item after the live one, usually).</param>
    /// <param name="last">Last item that may change (end of show, the item with the next curfew, or a group's end).</param>
    public sealed record Plan(IReadOnlyList<(int Item, int Delta)> Trims, int Applied, int Shortfall);

    public static Plan Make(IReadOnlyList<RundownItem> items, int first, int last, int seconds, RecoverMethod method)
    {
        first = Math.Max(0, first);
        last = Math.Min(items.Count - 1, last);
        var range = Enumerable.Range(first, Math.Max(0, last - first + 1)).ToList();
        if (range.Count == 0 || seconds == 0) return new Plan(Array.Empty<(int, int)>(), 0, Math.Abs(seconds));

        if (seconds < 0)
        {
            // Giving time out: proportionally to length, buffers excluded (they'd just grow the gap).
            var take = range.Where(i => items[i].Kind != SegmentKind.Buffer && items[i].EffectiveSeconds > 0).ToList();
            var share = Distribute(take.ToDictionary(i => i, i => (double)int.MaxValue), take.ToDictionary(i => i, i => (double)items[i].EffectiveSeconds), -seconds);
            var outTrims = Round(share).Where(t => t.Delta != 0).Select(t => (t.Item, t.Delta)).ToList();
            var given = outTrims.Sum(t => t.Delta);
            return new Plan(outTrims, given, Math.Max(0, -seconds - given));
        }

        // How much each item can give: down to its minimum, or to half its planned length when it has none.
        double Room(int i)
        {
            var it = items[i];
            var floor = it.MinDurationSeconds ?? (it.Kind == SegmentKind.Buffer ? 0 : it.DurationSeconds / 2);
            return Math.Max(0, it.EffectiveSeconds - Math.Max(0, floor));
        }
        var capacity = range.ToDictionary(i => i, Room);   // fixed: what each item can give in total
        var room = new Dictionary<int, double>(capacity);   // what's still left to give as trims are made
        var trims = new Dictionary<int, double>();
        var remaining = (double)seconds;

        if (method == RecoverMethod.BuffersFirst)
        {
            foreach (var i in range.Where(i => items[i].Kind == SegmentKind.Buffer))
            {
                var t = Math.Min(room[i], remaining);
                if (t <= 0) continue;
                trims[i] = t;
                room[i] -= t;
                remaining -= t;
            }
        }
        if (remaining > 0)
        {
            var weights = range.ToDictionary(i => i, i => method == RecoverMethod.Even ? 1.0 : Math.Max(1, items[i].EffectiveSeconds));
            foreach (var (i, t) in Distribute(room, weights, remaining))
                trims[i] = trims.GetValueOrDefault(i) + t;
        }
        // Rounding to 15 s steps may never take an item past its floor: cap at its total capacity, counted once.
        var rounded = Round(trims, cap: i => capacity.GetValueOrDefault(i));
        var result = rounded.Where(t => t.Delta > 0).Select(t => (t.Item, -t.Delta)).OrderBy(t => t.Item).ToList();
        var applied = -result.Sum(t => t.Item2);
        return new Plan(result, applied, Math.Max(0, seconds - applied));
    }

    /// <summary>Share <paramref name="amount"/> by weight, never past an item's cap; what a capped item can't take goes to the others.</summary>
    private static Dictionary<int, double> Distribute(Dictionary<int, double> caps, Dictionary<int, double> weights, double amount)
    {
        var result = caps.Keys.ToDictionary(k => k, _ => 0.0);
        var open = caps.Keys.Where(k => caps[k] > 0).ToHashSet();
        for (var guard = 0; amount > 0.5 && open.Count > 0 && guard < 100; guard++)
        {
            var total = open.Sum(k => weights[k]);
            var used = 0.0;
            foreach (var k in open.ToList())
            {
                var want = amount * weights[k] / total;
                var can = caps[k] - result[k];
                var give = Math.Min(want, can);
                result[k] += give;
                used += give;
                if (caps[k] - result[k] < 0.5) open.Remove(k);
            }
            amount -= used;
            if (used < 0.5) break;
        }
        return result;
    }

    /// <summary>Round to 15 s steps while keeping the total (largest remainders first), never past a cap.</summary>
    private static List<(int Item, int Delta)> Round(Dictionary<int, double> shares, Func<int, double>? cap = null)
    {
        var total = (int)Math.Round(shares.Values.Sum() / Step) * Step;
        var list = shares.Select(kv => (kv.Key, Floor: (int)Math.Floor(kv.Value / Step) * Step, Remainder: kv.Value % Step)).ToList();
        var assigned = list.Sum(x => x.Floor);
        var result = list.ToDictionary(x => x.Key, x => x.Floor);
        foreach (var x in list.OrderByDescending(x => x.Remainder))
        {
            if (assigned >= total) break;
            if (cap is not null && result[x.Key] + Step > cap(x.Key) + 0.5) continue;
            result[x.Key] += Step;
            assigned += Step;
        }
        return result.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
