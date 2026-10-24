using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>What Stagewatch knows about the room when it builds its context (gathered on the owner thread).</summary>
public sealed record ContextInput(
    string NodeId,
    string Name,
    DateTime NowUtc,
    ShowSnapshot Frame,
    bool ShowThreadStuck,
    IReadOnlyList<ContextItem> Links,
    IReadOnlyList<PeerSummary> Peers,
    IReadOnlyList<JournalEntry> Journal,
    ShowClock Clock,
    double GoLatencyP95Ms);

/// <summary>
/// Stagewatch's Show Bus context (§6): its light, a one-line headline and its parts — the timer, the schedule, each
/// department's cue, the speaker's message, every link — plus recent events, the verbs a peer or an AI may propose, and
/// the peers it hears. Computed from facts by rules (no model), so the Eye, a dashboard, a Stream Deck and an AI all read
/// the same truth, online or not. Pure; tested headless.
/// </summary>
public static class ShowContext
{
    /// <summary>The verbs a peer or an AI may propose, with their risk (high is never pre-ticked by any assistant).</summary>
    public static readonly IReadOnlyList<(string Verb, string Risk, string Words)> Commands = new[]
    {
        ("note <text>", "low", "Add a line to the show log"),
        ("message <text> warning", "review", "Put a message on the speaker's screen"),
        ("signal standby <dept> <cue>", "review", "Stand a department by"),
        ("signal go <dept> <cue>", "high", "GO one department"),
        ("signal go all", "high", "GO every department standing by"),
        ("nudge <seconds>", "review", "Add or take time from the live item"),
        ("trim <item> <seconds>", "review", "Lengthen or shorten a coming item"),
        ("delay <seconds>", "review", "Announce a delay before the next item"),
        ("next", "high", "Go to the next item"),
        ("start / pause", "high", "Start or pause the timer"),
        ("blackout", "high", "Black out every screen"),
        ("window show <window> <page>", "high", "Put a page on a controlled browser window (the audience sees it at once)"),
        ("window hold <window>", "high", "Put a window on its holding page"),
        ("window revoke <window>", "high", "Give a window a new address (the screen on the old one goes dark)"),
        ("patterns end 10", "high", "Jump Patterns' clip to its last 10 seconds"),
    };

    public static (string Light, string Headline, IReadOnlyList<ContextItem> Items) Assess(ContextInput x)
    {
        var f = x.Frame;
        var items = new List<ContextItem>();

        // The timer.
        var counting = f.State is TimerState.Running or TimerState.Expired;
        var timerLight = x.ShowThreadStuck ? Light.Red
            : f.Mode != TimerMode.Countdown || f.State == TimerState.Idle ? Light.Grey
            : f.Phase == TimerPhase.Overtime ? Light.Red
            : f.Phase is TimerPhase.WrapUp or TimerPhase.Warning ? Light.Amber
            : Light.Green;
        var timerState = x.ShowThreadStuck ? "the show thread has stopped turning over"
            : f.Mode == TimerMode.TimeOfDay ? "showing the time of day"
            : f.State == TimerState.Idle ? "idle"
            : $"{(f.State == TimerState.Paused ? "paused" : counting ? "running" : f.State.ToString().ToLowerInvariant())}, " +
              (f.Seconds < 0 ? $"{TimerTick.FormatSeconds(-f.Seconds)} over" : $"{f.Text} left");
        items.Add(new ContextItem("timer", "timer", f.Title.Length > 0 ? f.Title : "Timer", timerLight, timerState));

        // The schedule.
        if (f.ItemCount > 0)
        {
            var schedLight = f.HardOutMarginSeconds is < 0 ? Light.Red
                : f.OffsetSeconds is > 120 ? Light.Amber
                : f.OffsetSeconds is null ? Light.Grey : Light.Green;
            var parts = new List<string>();
            if (f.ScheduleText.Length > 0) parts.Add(f.ScheduleText);
            if (f.HardOutText.Length > 0) parts.Add(f.HardOutText);
            if (f.ForecastText.Length > 0) parts.Add(f.ForecastText);
            items.Add(new ContextItem("schedule", "schedule", $"Running order {(f.ItemIndex >= 0 ? $"item {f.ItemIndex + 1} of {f.ItemCount}" : $"{f.ItemCount} items")}",
                schedLight, parts.Count > 0 ? string.Join(" · ", parts) : "not started"));
        }

        // Departments and the speaker's cues: each target's newest cue.
        foreach (var g in f.Signals.GroupBy(s => s.Target).Select(grp => grp.Last()))
        {
            var cueLight = g.Escalated ? Light.Red
                : g.Kind == SignalKind.Standby ? Light.Amber
                : g.State == SignalState.Sent ? Light.Amber : Light.Green;
            var what = g.Kind switch
            {
                SignalKind.Standby => g.State switch { SignalState.Sent => "standby sent", SignalState.Seen => "standing by", _ => "in position" },
                SignalKind.Go => g.State == SignalState.Sent ? "GO sent" : "GO done",
                _ => g.State == SignalState.Sent ? $"{g.Kind.ToString().ToLowerInvariant()} sent" : $"{g.Kind.ToString().ToLowerInvariant()} {g.State.ToString().ToLowerInvariant()}",
            };
            items.Add(new ContextItem("cue:" + g.Target.ToLowerInvariant(), g.Kind is SignalKind.Standby or SignalKind.Go ? "department" : "cue", g.Target, cueLight,
                what + (g.Text.Length > 0 ? ": " + g.Text : "") + (g.Escalated ? " — NO ACKNOWLEDGEMENT" : "") + (g.By.Length > 0 ? $" ({g.By})" : "")));
        }

        // The message on the speaker's screen.
        if (f.Message.Length > 0)
            items.Add(new ContextItem("message", "message", "Speaker message", f.MessageAck == "sent" ? Light.Amber : Light.Green,
                f.MessageAck switch { "sent" => "waiting for the speaker to see it", "seen" => "seen", "done" => "acknowledged", _ => "on screen" }));

        // Links (integrations, presenter PCs, other apps) as the app reports them.
        items.AddRange(x.Links);
        if (x.GoLatencyP95Ms > GoLatency.AmberMs)
            items.Add(new ContextItem("go-latency", "health", "GO to screen", Light.Amber, $"95% of GOs take {x.GoLatencyP95Ms:0} ms or less — slower than it should be"));

        var light = Light.Worst(items.Select(i => i.Light));
        var standing = f.Signals.Where(s => s.Kind == SignalKind.Standby).Select(s => s.Target).ToList();
        var headline = new List<string>
        {
            f.Title.Length > 0 ? f.Title : "No item",
            timerState,
        };
        if (f.ItemCount > 0 && f.OffsetSeconds is { } off && Math.Abs(off) >= 30)
            headline.Add(off > 0 ? $"{TimerTick.FormatSeconds(off)} behind" : $"{TimerTick.FormatSeconds(-off)} ahead");
        if (standing.Count > 0) headline.Add(string.Join(", ", standing) + " standing by");
        var worst = items.Where(i => i.Light == Light.Red).Concat(items.Where(i => i.Light == Light.Amber && i.Id is not "timer" and not "schedule" && !i.Id.StartsWith("cue:", StringComparison.Ordinal))).FirstOrDefault();
        if (worst is not null && worst.Id != "timer") headline.Add($"{worst.Label}: {worst.State}");
        return (light, string.Join(" · ", headline), items);
    }

    /// <summary>The §6 JSON.</summary>
    public static string Build(ContextInput x)
    {
        var (light, headline, items) = Assess(x);
        var links = new List<object>();
        foreach (var l in x.Links) links.Add(new { from = "timer", to = l.Id, kind = l.Kind == "playout" ? "follows" : "hears", light = l.Light });
        return JsonSerializer.Serialize(new
        {
            v = 1,
            node = new { t = "main", id = x.NodeId, name = x.Name },
            at = new DateTimeOffset(x.NowUtc).ToUnixTimeMilliseconds(),
            light,
            headline,
            items = items.Select(i => new { id = i.Id, kind = i.Kind, label = i.Label, light = i.Light, state = i.State }),
            links,
            recent = x.Journal.TakeLast(20).Select(e => new { at = new DateTimeOffset(DateTime.SpecifyKind(e.Utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(), text = $"{x.Clock.ToShow(e.Utc):HH:mm:ss} {e.Kind}: {e.Text}" }),
            commands = Commands.Select(c => new { verb = c.Verb, risk = c.Risk, words = c.Words }),
            peers = x.Peers.Select(p => new { t = p.Kind, id = p.Id, name = p.Name, light = p.Light, headline = p.Headline, heard = p.Liveness }),
        });
    }
}
