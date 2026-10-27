using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Patterns as a playout source: its clip on air feeds the same VT countdown as vMix (when both are on, the one that's
/// playing wins; Patterns if both are), and "Last 10 s" jumps the clip to its last seconds over Patterns' own wire.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Whichever playout source has a clip running (Patterns first), else whichever has a clip at all. Patterns' clip counts only while
    /// link "patterns" may feed the VT countdown (its followVt).
    /// </summary>
    private PlayoutState? PickPlayout()
    {
        var p = PlayoutRules.PatternsLink(Settings.Integrations) is { FollowVt: true } ? _patterns?.Value : null;
        var v = _vmix?.Value;
        if (p is { Running: true }) return p;
        if (v is { Running: true }) return v;
        return p ?? v;
    }

    /// <summary>"Last 10 s" can work: Patterns connected with a clip of known length on air.</summary>
    [ObservableProperty] private bool _patternsCanJump;

    /// <summary>Called once a frame (owner thread) to keep the button's state current.</summary>
    private void RefreshPatternsButton()
        => PatternsCanJump = _patterns is { Connected: true } p && p.Video is { CountsDown: true };

    [RelayCommand]
    private void PatternsLast10() => PatternsVideoCommand("end", 10);

    private string _clipKey = "";
    private bool _clipEndHandled;

    /// <summary>
    /// 4 Hz (owner thread): the clip on air reached its end — once per clip run. It goes in the show log, fires the
    /// "clip ends" trigger and, when asked, moves a video item on to the next. A clip that is rewound or restarted
    /// (Patterns' seq moves, or its position goes back) can end again.
    /// </summary>
    private void WatchClipEnd()
    {
        if (_patterns?.Video is not { } v || !v.CountsDown) return;
        var key = v.File + "|" + v.Tag;
        if (key != _clipKey) { _clipKey = key; _clipEndHandled = false; }
        var left = v.LengthSeconds - v.PositionSeconds;
        if (left > 1) { _clipEndHandled = false; return; }
        if (_clipEndHandled || !(v.Ended || (left <= 0.25 && !v.Paused))) return;
        _clipEndHandled = true;
        var item = _show.RundownActive ? _show.CurrentItem : -1;
        _journal.Add(DateTime.UtcNow, JournalKind.Playout, item, "Patterns clip ended: " + v.Title);
        FireTrigger(TriggerEvent.ClipEnd, v.Tag.Length > 0 ? v.Tag : v.File, v.Title);
        var items = Settings.Rundown.Items;
        var isVideo = item >= 0 && item < items.Count && (items[item].Kind == SegmentKind.Video || items[item].Labels.Any(l => l.Equals("VT", StringComparison.OrdinalIgnoreCase)));
        if (PlayoutRules.PatternsLink(Settings.Integrations) is { NextOnEnd: true } && isVideo && item + 1 < items.Count)
        {
            StatusLine = $"VT {v.Title} ended — next item";
            _show.Next();
        }
        else StatusLine = $"VT {v.Title} ended";
    }

    /// <summary>
    /// A command's outcome as the link read it (link thread; Show Bus 13.12.3): say what Patterns actually did — applied, taken and
    /// not run yet, refused with its words, or unconfirmed (it may have run: look at its state), never a "done" it did not say. The
    /// show log gets one row when the outcome of a command that reached the socket first settles, and one more when an unconfirmed one
    /// is resolved (13.12.10), at the link's time and against the running-order item it was asked for (its mark), however far the show
    /// has moved since; a command that never reached the socket, or any other move, has the status line alone.
    /// </summary>
    private void OnPatternsAnswer(PlayoutAnswer a)
        => _ui.BeginInvoke(() =>
        {
            var label = PlayoutRules.Label(PlayoutRules.PatternsName);
            StatusLine = a.StatusLine(label);
            if (a.JournalLine(label) is { } row)
                _journal.Add(a.At.UtcDateTime, JournalKind.Playout, a.Mark, row);
        });

    /// <summary>
    /// A change of the link's state (link thread; Show Bus 13.16.7): one row in the show log, at the link's time — its repeats folded
    /// into one summary row a minute (13.19.4), never a row per beat.
    /// </summary>
    private void OnPatternsChanged(PlayoutLinkChange c)
        => _ui.BeginInvoke(() => _journal.Add(c.At.UtcDateTime, JournalKind.Playout, -1, c.Line(PlayoutRules.Label(PlayoutRules.PatternsName))));

    /// <summary>A line the link gives the show's log (link thread; Show Bus 13.10.4: its evaluator's own pause), journalled on the owner thread.</summary>
    private void OnPatternsLogged(string line)
        => _ui.BeginInvoke(() => _journal.Add(DateTime.UtcNow, JournalKind.Playout, -1, line));

    /// <summary>end n / restart / play / pause / stop / seek n on the clip Patterns has on air. Owner thread.</summary>
    private void PatternsVideoCommand(string what, int n)
    {
        if (_patterns is not { } link)
        {
            Refuse("Patterns isn't set up — Settings → Integrations → Patterns");
            return;
        }
        // What was asked, never that it was done: Patterns' own answer comes back on the status line (OnPatternsAnswer), its rows
        // filed against the item on now, which each command carries as its mark.
        var item = _show.RundownActive ? _show.CurrentItem : -1;
        var (error, asked) = what switch
        {
            "restart" => (link.Restart(item), "restart the clip"),
            "play" => (link.Play(item), "play the clip"),
            "pause" => (link.Pause(item), "pause the clip"),
            "stop" => (link.StopClip(item), "stop the clip at the top"),
            "seek" => (link.Seek(n, item), $"move the clip to {TimerTick.FormatSeconds(n)}"),
            _ => (link.EndIn(n, item), $"jump the clip to its last {n} s"),
        };
        if (error is not null) { Refuse("Patterns: " + error); return; }
        // On the link's lane, not yet its answer: the outcome comes back on the status line, and to the show log once it settles.
        StatusLine = "Patterns: " + asked + " — on its way, waiting for its answer";
        ReceiptHandedOn();   // Patterns confirms (or refuses) it itself
    }

    /// <summary>
    /// <c>playout [&lt;link&gt;] &lt;action&gt; …</c> (owner thread). <see cref="PlayoutRules.Route"/> decides: the clip's transport on
    /// link "patterns" does what the same <c>patterns …</c> line does; every other answer is a refusal with its reason, and nothing is
    /// sent.
    /// </summary>
    private void PlayoutCommand(ShowCommand c)
    {
        var a = c.Get<PlayoutArgs>();
        var (link, why) = PlayoutRules.Route(Settings.Integrations.PlayoutLinks, a, FeatureGate.AllowsPlayout(_license.Tier), _patterns is not null, VtLink());
        if (link is null) { Refuse(why!); return; }
        PatternsVideoCommand(a.Action, (int)c.Number);
    }

    /// <summary>The link whose clip feeds the VT countdown now, or null: link "patterns" while its clip is the one picked (<see cref="PickPlayout"/>).</summary>
    private string? VtLink() => _patterns is not null && PickPlayout() is { Source: "Patterns" } ? PlayoutRules.PatternsName : null;

    /// <summary>
    /// The playout links a peer or an AI may propose lines for, for the context's commands: the ones this Stagewatch runs and the
    /// licence allows — link "patterns" while its Patterns link runs, its clip's transport only.
    /// </summary>
    private IReadOnlyList<PlayoutRow> PlayoutRows()
        => _patterns is not null && FeatureGate.AllowsPlayout(_license.Tier) && PlayoutRules.PatternsLink(Settings.Integrations) is { Enabled: true } own
            ? [new PlayoutRow(own.Name, PlayoutRules.Label(own.Name), PlayoutRules.Patterns, Starts: false)]
            : [];
}
