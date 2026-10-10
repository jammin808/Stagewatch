using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Roll mode: the running order runs itself by the planned times. It loads and starts each item at its planned start,
/// with the time left to its planned end, and between items counts down to the next start. The moment the operator
/// does something else — pauses, loads another item — Roll hands over and switches off (it never fights a person).
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private bool _rollMode;
    [ObservableProperty] private string _rollLine = "";

    private int _rollItem = -1;          // what Roll last put up
    private bool _rollWaiting;           // counting down to _rollItem's start rather than running it
    private bool _rollApplying;          // Roll's own changes aren't "the operator took over"
    private DateTime _rollStartUtc;      // the planned start of what Roll put up — a new loop pass through the same item differs
    private DateTime? _rollEndUtc;       // when Roll's countdown should reach zero; a nudge or trim moves the engine off it

    [RelayCommand] private void ToggleRoll() => SetRoll(!RollMode);

    private void SetRoll(bool on)
    {
        if (on == RollMode) return;
        if (on)
        {
            if (Settings.Rundown.Items.Count == 0) { StatusLine = "Roll needs a running order"; return; }
            if (ScheduleAnchor() is null) { StatusLine = "Roll follows the planned times: set Starts on the Running order tab first"; return; }
            if (!Gate(FeatureGate.AllowsRunOfShow(_license.Tier), "Roll", FeatureTier.Pro)) return;
        }
        RollMode = on;
        _rollItem = -1;
        _rollWaiting = false;
        _rollEndUtc = null;
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, on ? "Roll on" : "Roll off");
        StatusLine = on ? "Roll on — the running order follows the clock" : "Roll off";
        if (on) UpdateRoll();
        else RollLine = "";
    }

    /// <summary>4 Hz from the housekeeping timer.</summary>
    private void UpdateRoll()
    {
        if (!RollMode) return;
        var items = Settings.Rundown.Items;
        if (items.Count == 0 || ScheduleAnchor() is not { } anchor) { SetRoll(false); return; }

        // Did a person take over since Roll's last move? Then step aside.
        if (_rollItem >= 0 && !_rollApplying)
        {
            var moved = !_rundownActive || _currentItem != _rollItem;
            var stopped = !_engine.IsCounting && _engine.State != TimerState.Expired;
            // Retimed: the operator nudged, trimmed or set an end time, so the clock no longer lands where Roll put it.
            var retimed = _rollEndUtc is { } end && _engine.IsCounting && _engine.State != TimerState.Expired
                          && Math.Abs(((end - DateTime.UtcNow) - _engine.Current).TotalSeconds) > 2;
            if (moved || stopped || retimed)
            {
                SetRoll(false);
                StatusLine = "Roll off — you took over";
                return;
            }
        }

        var now = DateTime.UtcNow;
        var plan = ScheduleEngine.Plan(items, anchor, ShowClock);
        if (Settings.Rundown.Loop != 0 && plan.Length > 0)
        {
            // A looping running order: follow the pass that's on now.
            var cycle = plan[^1].End - plan[0].Start;
            var passAnchor = Roll.LoopAnchor(anchor, cycle, Settings.Rundown.Loop, now);
            if (passAnchor != anchor) plan = ScheduleEngine.Plan(items, passAnchor, ShowClock);
        }
        var d = Roll.Decide(plan, now);
        _rollApplying = true;
        try
        {
            switch (d.What)
            {
                case Roll.Kind.Over:
                    RollLine = "ROLL · the running order is over";
                    if (_rollItem >= 0 && _engine.State != TimerState.Expired) { /* leave the last item as it ends */ }
                    break;

                case Roll.Kind.Wait:
                    if (_rollItem != d.Item || !_rollWaiting || Math.Abs((_rollStartUtc - plan[d.Item].Start).TotalSeconds) > 1)
                    {
                        GoToItem(d.Item, start: false);
                        _activeTitle = "Next: " + items[d.Item].DisplayTitle;
                        _engine.SetDuration(d.Remaining);
                        _engine.Start();                       // counts to the start — the item itself isn't marked started
                        _rollItem = d.Item;
                        _rollWaiting = true;
                        _rollStartUtc = plan[d.Item].Start;
                        _rollEndUtc = now + d.Remaining;
                        MarkDirty();
                        Publish();
                    }
                    RollLine = $"ROLL · {items[d.Item].Title} starts in {TimerTick.FormatSeconds((long)Math.Ceiling(d.Remaining.TotalSeconds))}";
                    break;

                case Roll.Kind.Run:
                    // A new pass through the same item (a one-item loop) has a new planned start: reload it.
                    if (_rollItem != d.Item || _rollWaiting || !_currentItemStarted || Math.Abs((_rollStartUtc - plan[d.Item].Start).TotalSeconds) > 1)
                    {
                        GoToItem(d.Item, start: false);
                        _engine.SetDuration(d.Remaining);
                        StartTimer();
                        _rollItem = d.Item;
                        _rollWaiting = false;
                        _rollStartUtc = plan[d.Item].Start;
                        _rollEndUtc = now + d.Remaining;
                        MarkDirty();
                        Publish();
                    }
                    RollLine = $"ROLL · {items[d.Item].Title} until {Fmt(ShowClock.ToShow(now + d.Remaining), seconds: false)}";
                    break;
            }
        }
        finally { _rollApplying = false; }
    }
}
