using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>What a change would do, before it's made: the end of the show, the curfew margin and every item that moves.</summary>
public sealed record SimRow(int Item, string Title, int Delta, string StartBefore, string StartAfter, string LengthAfter);

public sealed record SimResult(string EndBefore, string EndAfter, long? OffsetBefore, long? OffsetAfter, string Curfew,
    long? CurfewMarginBefore, long? CurfewMarginAfter, IReadOnlyList<SimRow> Rows)
{
    /// <summary>"Ends 17:38 (was 17:45) · curfew 18:00: +4:00 spare (was 3:00 over)".</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { EndAfter == EndBefore ? $"Ends {EndAfter}" : $"Ends {EndAfter} (was {EndBefore})" };
            if (CurfewMarginAfter is { } a)
            {
                static string M(long m) => m >= 0 ? $"{TimerTick.FormatSeconds(m)} spare" : $"{TimerTick.FormatSeconds(-m)} over";
                parts.Add($"curfew {Curfew}: {M(a)}" + (CurfewMarginBefore is { } b && b != a ? $" (was {M(b)})" : ""));
            }
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>Recover time across upcoming items, and the simulation that shows any timing change before it runs.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Lateness now (seconds, positive = late), for the Recover window's default.</summary>
    public long CurrentLateness => _projection.OffsetSeconds is { } o ? Math.Max(0, o) : 0;

    /// <summary>The first item a recovery may change: after the live one (it keeps its own clock), or the loaded one.</summary>
    public int RecoverFirstItem => !_show.RundownActive || _show.CurrentItem < 0 ? 0 : _show.ItemStarted ? _show.CurrentItem + 1 : _show.CurrentItem;

    /// <summary>Last item for a scope: "rest", "curfew" (the item carrying the next curfew) or "group" (end of the first item's group).</summary>
    public int RecoverLastItem(string scope)
    {
        var items = Settings.Rundown.Items;
        var first = RecoverFirstItem;
        if (scope == "curfew" && _projection.NextHardOut is { } h && h.Index >= first) return h.Index;
        if (scope == "group")
            foreach (var g in RundownGroups.Spans(items))
                if (first >= g.First && first <= g.Last) return g.Last;
        return items.Count - 1;
    }

    public bool HasCurfewAhead => _projection.NextHardOut is { } h && h.Index >= RecoverFirstItem;

    public Recovery.Plan PlanRecovery(int seconds, RecoverMethod method, string scope)
        => Recovery.Make(Settings.Rundown.Items, RecoverFirstItem, RecoverLastItem(scope), seconds, method);

    /// <summary>Apply a recovery as one undoable step.</summary>
    public void ApplyRecovery(Recovery.Plan plan, string label)
    {
        _show.ApplyTrims(plan.Trims, label);
    }

    /// <summary>
    /// Simulate trims (item, ±seconds) and an optional change to the announced delay, against the live projection.
    /// Nothing changes: the running order is cloned and projected twice. Null when there's no plan to compare.
    /// </summary>
    public SimResult? Simulate(IEnumerable<(int Item, int Delta)> trims, double? delaySeconds = null, double nudgeSeconds = 0)
    {
        var items = Settings.Rundown.Items;
        if (items.Count == 0 || ScheduleAnchor() is not { } anchor) return null;
        var clock = ShowClock;
        var now = DateTime.UtcNow;
        var cur = _show.RundownActive ? _show.CurrentItem : -1;
        var rem = cur >= 0 && _engine.Mode == TimerMode.Countdown ? _lastTick.Remaining : TimeSpan.Zero;
        var before = ScheduleEngine.Project(new ScheduleEngine.Input(items, anchor, cur, _show.ItemStarted, rem, _show.Actuals, now, clock, _show.PendingDelay,
            EndOfShow: Settings.Rundown.EndOfShow));

        var changed = items.Select(i => i.Clone()).ToList();
        var deltas = new Dictionary<int, int>();
        foreach (var (i, d) in trims)
        {
            if (i < 0 || i >= changed.Count) continue;
            var it = changed[i];
            var newAdjust = Math.Max(-it.DurationSeconds, it.AdjustSeconds + d);
            var applied = newAdjust - it.AdjustSeconds;
            it.AdjustSeconds = newAdjust;
            deltas[i] = deltas.GetValueOrDefault(i) + applied;
            if (i == cur) rem += TimeSpan.FromSeconds(applied);       // the live item's clock moves with it
        }
        rem += TimeSpan.FromSeconds(nudgeSeconds);
        var delay = delaySeconds is { } ds ? TimeSpan.FromSeconds(Math.Max(0, ds)) : _show.PendingDelay;
        var after = ScheduleEngine.Project(new ScheduleEngine.Input(changed, anchor, cur, _show.ItemStarted, rem, _show.Actuals, now, clock, delay,
            EndOfShow: Settings.Rundown.EndOfShow));

        string Hm(DateTime utc) => Clock.Time(clock.ToShow(utc), Settings.Display.Use12HourClock, seconds: false);
        var rows = new List<SimRow>();
        for (int i = 0; i < changed.Count && i < before.Items.Length && i < after.Items.Length; i++)
        {
            var moved = Math.Abs((after.Items[i].ExpectedStartUtc - before.Items[i].ExpectedStartUtc).TotalSeconds) >= 30;
            if (!deltas.ContainsKey(i) && !moved) continue;
            rows.Add(new SimRow(i, changed[i].Title, deltas.GetValueOrDefault(i), Hm(before.Items[i].ExpectedStartUtc), Hm(after.Items[i].ExpectedStartUtc),
                TimerTick.FormatSeconds(changed[i].EffectiveSeconds)));
        }
        var curfew = after.NextHardOut ?? before.NextHardOut;
        return new SimResult(Hm(before.ExpectedEndUtc), Hm(after.ExpectedEndUtc), before.OffsetSeconds, after.OffsetSeconds,
            curfew is null ? "" : Hm(curfew.CurfewUtc), before.NextHardOut?.MarginSeconds, after.NextHardOut?.MarginSeconds, rows);
    }

    /// <summary>What a copilot plan's timing steps (trims, delay, nudges) would do, if they all ran. "" when it has none.</summary>
    private string PlanConsequence(IEnumerable<string> commandLines)
    {
        var trims = new List<(int, int)>();
        double? delay = null;
        double nudge = 0;
        foreach (var line in commandLines)
        {
            if (Stagewatch.Integrations.Copilot.Check(line) is not { } c) continue;
            switch (c.Kind)
            {
                case ShowCommandKind.Trim:
                    if (int.TryParse(c.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) trims.Add(((int)c.Number - 1, d));
                    break;
                case ShowCommandKind.Delay: delay = c.Number; break;
                case ShowCommandKind.Nudge: nudge += c.Number; break;
            }
        }
        if (trims.Count == 0 && delay is null && nudge == 0) return "";
        return Simulate(trims, delay, nudge) is { } sim ? "If the timing steps run: " + sim.Summary : "";
    }
}
