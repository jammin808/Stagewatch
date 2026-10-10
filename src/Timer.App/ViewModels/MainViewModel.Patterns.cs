using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Patterns as a playout source: its clip on air feeds the same VT countdown as vMix (when both are on, the one that's
/// playing wins; Patterns if both are), and "Last 10 s" jumps the clip to its last seconds over Patterns' own wire.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Whichever playout source has a clip running (Patterns first), else whichever has a clip at all.</summary>
    private PlayoutState? PickPlayout()
    {
        var p = _patterns?.Value;
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

    /// <summary>end n / restart / play / pause / stop / seek n on the clip Patterns has on air. Owner thread.</summary>
    private void PatternsVideoCommand(string what, int n)
    {
        if (_patterns is not { } link)
        {
            StatusLine = "Patterns isn't set up — Settings → Integrations → Patterns";
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
        StatusLine = error is null ? "Patterns: " + done : "Patterns: " + error;
        if (error is null) _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, "Patterns " + done);
    }
}
