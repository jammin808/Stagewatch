using System.Windows;
using System.Windows.Controls;
using Stagewatch.App.ViewModels;
using Stagewatch.Core;

namespace Stagewatch.App;

/// <summary>
/// Recover time: choose how much, from where (rest of the show, up to the next curfew, this group) and how
/// (proportional, even, buffers first). Every item that would change, the new end and the curfew margin are shown
/// live as you choose; Apply makes it one undoable step.
/// </summary>
public partial class RecoverWindow : Window
{
    private readonly MainViewModel _vm;
    private Recovery.Plan? _plan;
    private bool _ready;

    public RecoverWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Scope.Items.Add(new ComboBoxItem { Content = "the rest of the show", Tag = "rest" });
        if (vm.HasCurfewAhead) Scope.Items.Add(new ComboBoxItem { Content = "items up to the next curfew", Tag = "curfew" });
        Scope.Items.Add(new ComboBoxItem { Content = "this group", Tag = "group" });
        Scope.SelectedIndex = vm.HasCurfewAhead ? 1 : 0;
        var late = vm.CurrentLateness;
        Amount.Text = TimerTick.FormatSeconds(late > 0 ? (late + 14) / 15 * 15 : 300);
        Intro.Text = late > 0
            ? $"The show is running {TimerTick.FormatSeconds(late)} late. Choose where the time comes from — nothing changes until you press Apply."
            : "Choose how much time to win back (or give out when running early) and where it comes from — nothing changes until you press Apply.";
        _ready = true;
        Recompute();
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Recompute();

    private void Recompute()
    {
        if (!_ready) return;
        if (!RundownCsv.TryParseDuration(Amount.Text, out var ts) || ts.TotalSeconds < 1)
        {
            Summary.Text = "Type how much, e.g. 6:00";
            Shortfall.Text = "";
            Rows.ItemsSource = null;
            ApplyButton.IsEnabled = false;
            return;
        }
        var give = (Direction.SelectedItem as ComboBoxItem)?.Tag as string == "give";
        var seconds = (int)ts.TotalSeconds * (give ? -1 : 1);
        var method = Enum.TryParse<RecoverMethod>((Method.SelectedItem as ComboBoxItem)?.Tag as string, out var m) ? m : RecoverMethod.Proportional;
        var scope = (Scope.SelectedItem as ComboBoxItem)?.Tag as string ?? "rest";
        _plan = _vm.PlanRecovery(seconds, method, scope);
        var sim = _vm.Simulate(_plan.Trims);
        var items = _vm.Settings.Rundown.Items;
        Rows.ItemsSource = _plan.Trims.Select(t =>
        {
            var row = sim?.Rows.FirstOrDefault(r => r.Item == t.Item);
            return new
            {
                Number = t.Item + 1,
                Title = items[t.Item].Title,
                Change = (t.Delta < 0 ? "−" : "+") + TimerTick.FormatSeconds(Math.Abs(t.Delta)),
                Length = TimerTick.FormatSeconds(Math.Max(0, items[t.Item].EffectiveSeconds + t.Delta)),
                Starts = row is null ? "" : row.StartBefore == row.StartAfter ? row.StartAfter : $"{row.StartAfter} (was {row.StartBefore})",
            };
        }).ToList();
        Summary.Text = _plan.Trims.Count == 0
            ? "Nothing can change in that range."
            : $"{(give ? "Give out" : "Win back")} {TimerTick.FormatSeconds(_plan.Applied)} across {_plan.Trims.Count} item{(_plan.Trims.Count == 1 ? "" : "s")}"
              + (sim is null ? "" : " · " + sim.Summary);
        Shortfall.Text = _plan.Shortfall >= Recovery.Step
            ? $"Only {TimerTick.FormatSeconds(_plan.Applied)} is there without cutting items below their minimum (or half their length). Widen the range, or set minimums in the editor."
            : "";
        ApplyButton.IsEnabled = _plan.Trims.Count > 0;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (_plan is null || _plan.Trims.Count == 0) return;
        var give = _plan.Trims.Sum(t => t.Delta) > 0;
        _vm.ApplyRecovery(_plan, $"{(give ? "Gave out" : "Recovered")} {TimerTick.FormatSeconds(_plan.Applied)} across {_plan.Trims.Count} items");
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
