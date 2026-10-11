using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// LTC in: shows the incoming timecode, optionally makes the timer chase it (start when it rolls, pause when it
/// stops, and with an end timecode keep remaining = end − timecode), and fires timecode triggers as it passes them.
/// Polled at 10 Hz on the UI thread; the capture/decoder run on their own thread and publish latest-wins.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly record struct LtcView(string Text, bool Running);

    private LtcView _ltcView = new("", false);
    private bool _ltcWasRunning;
    private TimeSpan? _ltcPrevTime;
    private DispatcherTimer? _ltcTimer;

    [ObservableProperty] private string _ltcLine = "";

    private void InitLtc()
    {
        _ltcTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) };
        _ltcTimer.Tick += (_, _) => PollLtc();   // started by StartServices
    }

    private void PollLtc()
    {
        var reading = _ltcIn?.Value;
        var now = Stopwatch.GetTimestamp();
        if (reading is null || !reading.Fresh(now))
        {
            // No code: it stopped arriving, or the source changed (a new input, LTC in switched off) and has nothing
            // yet. Either way the timer was following tape that is no longer there, so it pauses like a stopped tape.
            if (Settings.Integrations.LtcChase && LtcChase.Lost(_ltcWasRunning, _engine.IsCounting)) _engine.Pause();
            _ltcWasRunning = false;
            _ltcPrevTime = null;
            SetLtcView(reading is null ? "" : reading.Text, false);
            return;
        }

        var tc = reading.Now(now);
        if (Settings.Integrations.LtcChase && _engine.Mode == TimerMode.Countdown)
        {
            var end = Timecode.Parse(Settings.Integrations.LtcEnd, reading.Fps, reading.DropFrame);
            var act = LtcChase.Decide(_ltcWasRunning, reading, now, end, _lastTick.Remaining, _engine.IsCounting);
            if (act.SetRemaining is { } want)
            {
                if (_engine.IsCounting) _engine.Nudge(want - _lastTick.Remaining);
                else _engine.SetDuration(want < TimeSpan.Zero ? TimeSpan.Zero : want);
            }
            if (act.Start) StartTimer();
            else if (act.Pause) _engine.Pause();
        }

        if (reading.Running && _ltcPrevTime is { } prev && _triggers is not null)
        {
            var s = _hub.Latest;
            _triggers.FireTimecode(prev, tc, reading.Fps, new TriggerContext(
                _show.RundownActive ? _show.CurrentItem + 1 : 0, s.Title, s.Phase.ToString().ToLowerInvariant(),
                TimerTick.FormatSeconds(Math.Max(0, s.Seconds)), reading.Text, Settings.Integrations.RoomName), reading.DropFrame);
        }
        _ltcPrevTime = reading.Running ? tc : null;
        _ltcWasRunning = reading.Running;
        SetLtcView(reading.Running ? Timecode.Format(tc, reading.Fps, reading.DropFrame) : reading.Text, reading.Running);
    }

    private void SetLtcView(string text, bool running)
    {
        var view = new LtcView(text, running);
        var line = _ltcIn is null ? ""
            : text.Length == 0 ? "⏱  LTC · no signal"
            : $"⏱  LTC {text} · {(running ? "rolling" : "parked")}{(Settings.Integrations.LtcChase ? " · chase" : "")}"
              + (Settings.Integrations.LtcEnd.Length > 0 && Settings.Integrations.LtcChase ? $" → {Settings.Integrations.LtcEnd}" : "");
        if (line != LtcLine) LtcLine = line;
        if (view == _ltcView) return;
        // Only the rolling state and whole seconds matter to the outputs; frames would republish 25×/s.
        var secondsChanged = view.Running != _ltcView.Running || Trim(view.Text) != Trim(_ltcView.Text);
        _ltcView = view;
        if (secondsChanged) { MarkDirty(); Publish(); }

        static string Trim(string t) => t.Length >= 8 ? t[..8] : t;
    }

    private void SetLtcChase(bool on)
    {
        _store.Update(s => s.Integrations.LtcChase = on);
        StatusLine = on ? "Timer follows LTC" : "Timer no longer follows LTC";
        _ltcWasRunning = false;          // re-arm: the next roll starts the timer
        SetLtcView(_ltcView.Text, _ltcView.Running);
        MarkDirty(); Publish();
    }

    private void SetLtcEnd(string tc)
    {
        _store.Update(s => s.Integrations.LtcEnd = tc.Trim());
        StatusLine = tc.Length > 0 ? $"Counting down to LTC {tc}" : "LTC end timecode cleared";
        SetLtcView(_ltcView.Text, _ltcView.Running);
        MarkDirty(); Publish();
    }

    [RelayCommand] private void ToggleLtcChase() => SetLtcChase(!Settings.Integrations.LtcChase);
    [RelayCommand] private void ClearLtcEnd() => SetLtcEnd("");
}
