using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Roll mode, as the operator sees it. The behaviour lives in <see cref="Show.ShowController"/>: the running order
/// runs itself by the planned times until the operator does anything else (pause, load, nudge, trim), when it hands over.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private bool _rollMode;
    [ObservableProperty] private string _rollLine = "";

    [RelayCommand] private void ToggleRoll() => _show.SetRoll(!RollMode);

    private void SetRoll(bool on) => _show.SetRoll(on);
}
