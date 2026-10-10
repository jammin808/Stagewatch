using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Stagewatch.App.ViewModels;

namespace Stagewatch.App;

/// <summary>The copilot: ask, review the proposed plan, tick, run. Bound straight to the main view model.</summary>
public partial class CopilotWindow : Window
{
    private readonly MainViewModel _vm;

    public CopilotWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        Closed += (_, _) => vm.PropertyChanged -= OnVmChanged;
        Loaded += (_, _) => { UpdateState(); Input.Focus(); };
        Activated += (_, _) => UpdateState();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F9) return;
            if (!e.IsRepeat && !_vm.Talking) _vm.BeginTalk();
            e.Handled = true;
        };
        PreviewKeyUp += (_, e) => { if (e.Key == Key.F9 && _vm.Talking) { _ = _vm.EndTalk(); e.Handled = true; } };
    }

    private void OnTalkDown(object sender, MouseButtonEventArgs e) { _vm.BeginTalk(); e.Handled = true; }

    private void OnTalkUp(object sender, System.Windows.Input.MouseEventArgs e) { if (_vm.Talking) _ = _vm.EndTalk(); }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CopilotBusy)) UpdateState();
    }

    private void UpdateState()
    {
        var why = _vm.CopilotUnavailable;
        Unavailable.Visibility = why is null ? Visibility.Collapsed : Visibility.Visible;
        UnavailableText.Text = why ?? "";
        AskButton.Visibility = _vm.CopilotBusy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = _vm.CopilotBusy ? Visibility.Visible : Visibility.Collapsed;
        AskButton.IsEnabled = why is null;
    }

    private void OnAsk(object sender, RoutedEventArgs e) => Ask(null);

    private void OnChip(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) Ask(text);
    }

    private void Ask(string? text)
    {
        if (_vm.CopilotBusy) return;
        if (_vm.AskCopilotCommand.CanExecute(text)) _vm.AskCopilotCommand.Execute(text);
    }

    private void OnInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { Ask(null); e.Handled = true; }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
