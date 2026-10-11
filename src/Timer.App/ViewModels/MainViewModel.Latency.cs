using System.Diagnostics;
using System.Runtime;
using System.Windows.Media;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

// Measured, then governed: how long a GO takes to reach the screens, and the garbage collector held to short pauses
// while a show is live. Owner thread only.
public sealed partial class MainViewModel
{
    private readonly GoLatency _goLatency = new();
    private long _goStart;
    private long _goPublished;
    private GCLatencyMode? _gcBefore;

    /// <summary>"GO → screen: last 12 ms, median 11 ms, 95% 18 ms …" for Diagnostics and the context.</summary>
    public string GoLatencyLine => _goLatency.Describe();

    private static bool IsGo(ShowCommandKind k) => k is ShowCommandKind.Start or ShowCommandKind.Toggle or ShowCommandKind.Next
        or ShowCommandKind.Previous or ShowCommandKind.GoTo or ShowCommandKind.Preset or ShowCommandKind.SignalGoAll
        or ShowCommandKind.Signal or ShowCommandKind.Blackout or ShowCommandKind.Message;

    /// <summary>A GO arrived: start the clock (the first one wins until its frame is on screen).</summary>
    private void MarkGo(ShowCommand c)
    {
        if (_goStart == 0 && IsGo(c.Kind)) _goStart = Stopwatch.GetTimestamp();
    }

    /// <summary>The frame carrying it was published: note that, then time the next paint.</summary>
    private void GoPublished()
    {
        if (_goStart == 0 || _goPublished != 0) return;
        _goPublished = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnGoPainted;
    }

    private void OnGoPainted(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnGoPainted;
        var now = Stopwatch.GetTimestamp();
        _goLatency.Add(Stopwatch.GetElapsedTime(_goStart, _goPublished).TotalMilliseconds, Stopwatch.GetElapsedTime(_goStart, now).TotalMilliseconds);
        _goStart = 0;
        _goPublished = 0;
    }

    /// <summary>While a show is live the collector avoids long blocking pauses; off air it goes back to normal.</summary>
    private void GovernGc()
    {
        var live = IsShowLive;
        if (live && _gcBefore is null)
        {
            _gcBefore = GCSettings.LatencyMode;
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        }
        else if (!live && _gcBefore is { } before)
        {
            GCSettings.LatencyMode = before;
            _gcBefore = null;
        }
    }
}
