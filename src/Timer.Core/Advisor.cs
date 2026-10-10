namespace Stagewatch.Core;

public enum AdviceLevel { Info, Suggest, Warn, Urgent }

/// <summary>
/// One proposal for the show caller. <see cref="Command"/> is a line in the shared command grammar
/// ("trim 7 -180", "signal message speaker \"Please wrap up\""), so applying it goes through the same
/// single input path as a button press — the assistant can never do anything an operator can't.
/// </summary>
public readonly record struct Advice(string Key, AdviceLevel Level, string Text, string? Command, string? CommandLabel);

/// <summary>Everything the assistant looks at, gathered once per frame by the app.</summary>
public sealed record AdvisorInput(
    IReadOnlyList<RundownItem> Items,
    ScheduleProjection Projection,
    int Current,
    bool Live,
    TimeSpan Remaining,
    ForecastResult? Forecast,
    GhostResult? Ghost,
    ScriptPosition? Script,
    IReadOnlyList<Signal> Signals,
    int QaQueued,
    int QaFit,
    DateTime NowUtc,
    ShowClock Clock,
    bool TwelveHour);

/// <summary>Turns a proposal into a command, for advisors that only produce text (e.g. a language model).</summary>
public interface IShowAdvisor
{
    string Name { get; }
    IReadOnlyList<Advice> Advise(AdvisorInput input);
}

/// <summary>
/// The producer assistant, rule edition: deterministic, explainable, offline. It never acts —
/// it proposes, the operator approves with one click.
/// </summary>
public sealed class RuleAdvisor : IShowAdvisor
{
    public string Name => "Rules";

    public IReadOnlyList<Advice> Advise(AdvisorInput x)
    {
        var list = new List<Advice>();
        string Time(DateTime utc) => Clock.Time(x.Clock.ToShow(utc), x.TwelveHour, seconds: false);
        var p = x.Projection;

        // 1. A hard-out is at risk: propose concrete trims.
        if (p.NextHardOut is { } h)
        {
            var title = x.Items[h.Index].Title;
            if (h.MarginSeconds < 0)
            {
                list.Add(new Advice($"hardout-{h.Index}", AdviceLevel.Urgent,
                    $"Curfew {Time(h.CurfewUtc)} after \"{title}\": on course to overrun by {TimerTick.FormatSeconds(-h.MarginSeconds)}.", null, null));
                foreach (var t in p.Trims)
                    list.Add(new Advice($"trim-{t.Index}-{t.Seconds}", AdviceLevel.Suggest, t.Reason,
                        $"trim {t.Index + 1} {-t.Seconds}", $"Cut {TimerTick.FormatSeconds(t.Seconds)}"));
                if (p.Trims.Length == 0)
                    list.Add(new Advice("hardout-notrim", AdviceLevel.Warn,
                        "Nothing is marked compressible before the curfew — set minimum durations in the running order, or ask the producer what can go.", null, null));
            }
            else if (x.Forecast is { CurfewRisk: >= 0.3 } f)
            {
                list.Add(new Advice($"hardout-risk-{h.Index}", AdviceLevel.Warn,
                    $"On plan for the {Time(h.CurfewUtc)} curfew, but history says {f.CurfewRisk:P0} chance of missing it (80% finish {Time(f.P80Utc)}).", null, null));
            }
        }

        // 2. The live speaker is heading over — say so before zero, not at zero.
        if (x.Live && x.Current >= 0 && x.Current < x.Items.Count && x.Remaining > TimeSpan.Zero)
        {
            long? projectedOver = null;
            string source = "";
            if (x.Ghost is { State: PaceState.Behind } g && g.ProjectedTotalSeconds > 0)
            {
                var budget = x.Items[x.Current].EffectiveSeconds;
                projectedOver = g.ProjectedTotalSeconds - budget;
                source = $"slides: {TimerTick.FormatSeconds(g.DeltaSeconds)} behind rehearsal";
            }
            else if (x.Script is { ProjectedRemainingSeconds: { } rem })
            {
                projectedOver = rem - (long)x.Remaining.TotalSeconds;
                source = $"script: {x.Script.Value.Percent:0}% delivered at {x.Script.Value.WordsPerMinute:0} wpm";
            }
            if (projectedOver is > 60 && x.Remaining < TimeSpan.FromMinutes(15))
                list.Add(new Advice($"pace-{x.Current}", AdviceLevel.Suggest,
                    $"\"{x.Items[x.Current].Title}\" is pacing to finish {TimerTick.FormatSeconds(projectedOver.Value)} over ({source}).",
                    "signal message speaker \"Please start to wrap up\"", "Send wrap-up (with ack)"));
        }

        // 3. Running late with a fixed start ahead: say what will happen.
        if (p.OffsetSeconds is > 120 && FirstIndex(p, t => t.Status == ItemStatus.Upcoming && t.Conflict) is { } ci)
        {
            var c = p.Items[ci];
            list.Add(new Advice($"fixed-{ci}", AdviceLevel.Warn,
                $"\"{x.Items[ci].Title}\" is published for {Time(c.PlannedStartUtc)} but can't start before {Time(c.ExpectedStartUtc)}.", null, null));
        }

        // 4. Running early ahead of a fixed start: there is time to give.
        if (p.OffsetSeconds is < -180
            && FirstIndex(p, t => t.Status == ItemStatus.Upcoming && x.Items[t.Index].StartMode == StartMode.Fixed) is { } fi)
        {
            list.Add(new Advice($"early-{fi}", AdviceLevel.Info,
                $"{TimerTick.FormatSeconds(-p.OffsetSeconds.Value)} ahead of plan — \"{x.Items[fi].Title}\" is held to {Time(p.Items[fi].PlannedStartUtc)}, so the current item can run longer.",
                $"nudge {Math.Min(-p.OffsetSeconds.Value, 600)}", "Give the time to this item"));
        }

        // 5. Unanswered cues.
        foreach (var s in x.Signals.Where(s => s.Escalated && s.State == SignalState.Sent))
            list.Add(new Advice($"unacked-{s.Id}", AdviceLevel.Warn,
                $"Nobody has acknowledged \"{(s.Text.Length > 0 ? s.Text : s.Kind.ToString())}\" to {s.Target} — radio them.",
                $"signal resend {s.Id}", "Resend"));

        // 6. Q&A: more questions queued than will fit.
        if (x.QaQueued > 0 && x.Live && x.QaQueued > x.QaFit && x.QaFit <= 1)
            list.Add(new Advice("qa-last", AdviceLevel.Suggest,
                x.QaFit == 1 ? $"Time for one more question ({x.QaQueued} queued)." : $"No time left for the {x.QaQueued} queued questions.",
                "signal message moderator \"Last question\"", "Tell moderator: last question"));

        return list;
    }

    private static int? FirstIndex(ScheduleProjection p, Func<ItemTiming, bool> match)
    {
        for (int i = 0; i < p.Items.Length; i++) if (match(p.Items[i])) return i;
        return null;
    }
}
