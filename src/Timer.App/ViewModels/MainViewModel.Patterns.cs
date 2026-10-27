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

    /// <summary>A command Patterns answered (link thread): say what it actually did.</summary>
    private void OnPatternsReceipt(string line, bool ok, string words)
        => _ui.BeginInvoke(() => StatusLine = ok ? $"Patterns did it: {line}" + (words.Length > 0 ? $" ({words})" : "") : $"Patterns refused {line}: {words}");

    /// <summary>end n / restart / play / pause / stop / seek n on the clip Patterns has on air. Owner thread.</summary>
    private void PatternsVideoCommand(string what, int n)
    {
        if (_patterns is not { } link)
        {
            Refuse("Patterns isn't set up — Settings → Integrations → Patterns");
            return;
        }
        var (error, done) = what switch
        {
            "restart" => (link.Restart(), "clip restarted"),
            "play" => (link.Play(), "clip playing"),
            "pause" => (link.Pause(), "clip paused"),
            "stop" => (link.StopClip(), "clip stopped at the top"),
            "seek" => (link.Seek(n), $"clip moved to {TimerTick.FormatSeconds(n)}"),
            _ => (link.EndIn(n), $"clip jumped to its last {n} s"),
        };
        if (error is not null) { Refuse("Patterns: " + error); return; }
        StatusLine = "Patterns: " + done + " (sent — waiting for Patterns)";
        ReceiptHandedOn();   // Patterns confirms (or refuses) it itself
        if (error is null) _journal.Add(DateTime.UtcNow, JournalKind.Playout, _show.RundownActive ? _show.CurrentItem : -1, "Patterns " + done);
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
