using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>One auxiliary timer on the Timers tab. Every button sends the same <c>aux …</c> command remotes use.</summary>
public sealed partial class AuxTimerItem : ObservableObject
{
    private readonly MainViewModel _vm;
    public int Slot { get; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _text = "05:00";
    [ObservableProperty] private bool _running;
    [ObservableProperty] private bool _countUp;
    [ObservableProperty] private bool _onStage;
    [ObservableProperty] private bool _overtime;
    [ObservableProperty] private string _durationInput = "5:00";

    public AuxTimerItem(MainViewModel vm, int slot) { _vm = vm; Slot = slot; }

    [RelayCommand] private void Toggle() => _vm.RunLine($"aux {Slot} toggle");
    [RelayCommand] private void Reset() => _vm.RunLine($"aux {Slot} reset");
    [RelayCommand] private void Stage() => _vm.RunLine($"aux {Slot} {(OnStage ? "unstage" : "stage")}");
    [RelayCommand] private void Direction() => _vm.RunLine($"aux {Slot} {(CountUp ? "down" : "up")}");
    [RelayCommand] private void Load() => _vm.RunLine($"aux {Slot} set {DurationInput.Trim()}");
    [RelayCommand] private void Add(string? seconds) => _vm.RunLine($"aux {Slot} nudge {seconds}");

    /// <summary>Names are typed in place; committed when the box loses focus.</summary>
    public void CommitName(string name) { if (name.Trim() != _vm.AuxName(Slot)) _vm.RunLine($"aux {Slot} name {name.Trim()}"); }

    public void Apply(AuxView v)
    {
        Name = v.Name;
        Text = v.Text;
        Running = v.Running;
        CountUp = v.CountUp;
        OnStage = v.OnStage;
        Overtime = !v.CountUp && v.Seconds < 0;
    }
}

/// <summary>Auxiliary timers: up to three named clocks beside the show (VT, changeover, a speaker's own clock).</summary>
public sealed partial class MainViewModel
{
    private readonly AuxTimer[] _aux = Enumerable.Range(0, ShowCommandParser.AuxSlots).Select(_ => new AuxTimer()).ToArray();

    public ObservableCollection<AuxTimerItem> AuxItems { get; } = new();

    private void InitAux()
    {
        for (int i = 0; i < _aux.Length; i++)
        {
            var saved = i < Settings.AuxTimers.Count ? Settings.AuxTimers[i] : null;
            _aux[i].Name = saved?.Name ?? "";
            _aux[i].CountUp = saved?.CountUp ?? false;
            _aux[i].SetDuration(TimeSpan.FromSeconds(Math.Max(0, saved?.DurationSeconds ?? 300)));
            var item = new AuxTimerItem(this, i + 1) { DurationInput = TimerTick.FormatSeconds(saved?.DurationSeconds ?? 300) };
            item.Apply(_aux[i].View(i + 1));
            AuxItems.Add(item);
        }
    }

    internal string AuxName(int slot) => _aux[slot - 1].Name;

    /// <summary>Parse and run one command line exactly as a remote would (so every path is the same path).</summary>
    internal void RunLine(string line)
    {
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return;
        if (ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var cmd, out var error)) Execute(cmd);
        else Refuse(error);
    }

    private EquatableArray<AuxView> AuxViews()
    {
        var views = new AuxView[_aux.Length];
        for (int i = 0; i < _aux.Length; i++) views[i] = _aux[i].View(i + 1);
        // Keep the Timers tab in step with the frame (cheap: setters no-op on equal values).
        for (int i = 0; i < views.Length && i < AuxItems.Count; i++) AuxItems[i].Apply(views[i]);
        return views.ToEquatableArray();
    }

    private string AuxStageLine()
    {
        var on = _aux.Select((a, i) => (a, i)).FirstOrDefault(x => x.a.OnStage);
        if (on.a is null) return "";
        var v = on.a.View(on.i + 1);
        return $"{v.Name.ToUpperInvariant()}  {v.Text}";
    }

    private void ApplyAuxCommand(double number, AuxArgs args)
    {
        var slot = (int)number;
        if (slot < 1 || slot > _aux.Length) return;
        var a = _aux[slot - 1];
        var (action, arg) = (args.Action, args.Argument);
        switch (action)
        {
            case "start": a.Start(); break;
            case "pause": a.Pause(); break;
            case "toggle": a.Toggle(); break;
            case "reset": a.Reset(); break;
            case "stage":
                // One on stage at a time: the speaker gets one extra line, not a list.
                foreach (var other in _aux) other.OnStage = false;
                a.OnStage = true;
                break;
            case "unstage": a.OnStage = false; break;
            case "up": a.CountUp = true; a.Reset(); Persist(); break;
            case "down": a.CountUp = false; a.Reset(); Persist(); break;
            case "set":
                if (long.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var secs))
                {
                    a.SetDuration(TimeSpan.FromSeconds(secs));
                    if (secs > 0) a.CountUp = false;
                    AuxItems[slot - 1].DurationInput = TimerTick.FormatSeconds(secs);
                    Persist();
                }
                break;
            case "nudge":
                if (double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) a.Nudge(TimeSpan.FromSeconds(d));
                break;
            case "name":
                a.Name = arg.Trim();
                Persist();
                break;
        }
        MarkDirty();
        Publish();

        void Persist() => _store.Update(s =>
        {
            while (s.AuxTimers.Count < _aux.Length) s.AuxTimers.Add(new AuxTimerSettings());
            for (int i = 0; i < _aux.Length; i++)
            {
                s.AuxTimers[i].Name = _aux[i].Name;
                s.AuxTimers[i].CountUp = _aux[i].CountUp;
                s.AuxTimers[i].DurationSeconds = (int)_aux[i].Duration.TotalSeconds;
            }
        });
    }
}
