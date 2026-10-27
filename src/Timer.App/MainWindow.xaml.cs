using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Stagewatch.App.Help;
using Stagewatch.App.ViewModels;
using Stagewatch.Core;

namespace Stagewatch.App;

public partial class MainWindow : Window, IContextActions
{
    private readonly MainViewModel _vm;

    private readonly string? _showToOpen;

    public MainWindow(string? showToOpen = null)
    {
        _showToOpen = showToOpen;
        InitializeComponent();
        _vm = new MainViewModel(App.SettingsStore, App.License);
        _vm.StartServices();
        DataContext = _vm;
        Loaded += (_, _) => SyncRundownPicker();
        _dock = new PanelDock(this, SideTabs, new Dictionary<string, TabItem>
        {
            ["presets"] = TabPresets, ["running"] = TabRunning, ["cues"] = TabCues,
            ["producer"] = TabProducer, ["holding"] = TabHolding, ["timers"] = TabAux,
        }, SideCol, SplitCol, SideSplitter, SidePlate, SidePanel, DockHint, App.SettingsStore);
        // Torn-out panels need the main window up first (they're owned by it).
        Loaded += (_, _) => _dock.Restore();
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.LiveRundownName)) SyncRundownPicker(); };
        _vm.NodeKeyMade += ShowNewNodeKey;

        if (App.SettingsStore.Settings.MainWindowBounds is { } b)
        {
            Left = b.X; Top = b.Y; Width = b.Width; Height = b.Height;
        }

        Closing += OnClosing;
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        ContextHelp.Attach(this);
        ContentRendered += OnFirstRender;
    }

    /// <summary>After a crash or power cut, offer the show back (once the window is up).</summary>
    private void OnFirstRender(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstRender;
        var what = _vm.RecoveryDescription;
        if (App.RestartNote is { } note)
        {
            // The watchdog restarted us after a fault: put the show straight back — nobody should be asked mid-show.
            _vm.ResolveRecovery(restore: what is not null);
            _vm.StatusLine = note + (what is not null ? " — the show is back where it was" : "");
        }
        else if (what is null)
        {
            _vm.ResolveRecovery(restore: false);
        }
        else
        {
            var answer = MessageBox.Show(this,
                "Stagewatch didn't close normally last time. Restore the show where it was?\n\n" + what,
                "Restore show?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
            _vm.ResolveRecovery(answer == MessageBoxResult.Yes);
        }
        if (_vm.SettingsLoadLine is { } said) _vm.StatusLine = said;
        if (SettingsStore.LoadedFromBackup)
            _vm.StatusLine = "The settings file was damaged — Stagewatch loaded the last good copy (nothing else was lost)";
        // Launched by double-clicking a .stagewatch file.
        if (_showToOpen is not null) OpenShowFile(_showToOpen);
    }

    /// <summary>For the watchdog's heartbeat: a show is live (hang detection is quicker then).</summary>
    public bool ShowLive => _vm.IsShowLive;

    /// <summary>A UI fault was contained: say so (once per kind) and carry on.</summary>
    public void ReportFault(Exception ex)
        => _vm.StatusLine = $"Something went wrong ({ex.GetType().Name}: {ex.Message}) — logged; the show carries on";

    /// <summary>A second launch of Stagewatch hands over to this window instead of starting again.</summary>
    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true;   // nudge past focus-stealing prevention
        Topmost = false;
        Focus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // One stray click on × mid-keynote must not end the show.
        if (_vm.IsShowLive)
        {
            var answer = MessageBox.Show(this,
                $"A show is running:\n{_vm.LiveSummary}\n\nClose Stagewatch anyway? The timer and all outputs will stop.",
                "Close Stagewatch?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
        }
        _dock.Shutdown();
        App.SettingsStore.Update(s => s.MainWindowBounds = new WindowBounds
        {
            X = (int)Left, Y = (int)Top, Width = (int)Width, Height = (int)Height
        });
        _vm.Dispose();
    }

    /// <summary>Push-to-talk: hold F9, speak a request, let go. The copilot window opens to show what it heard and the plan.</summary>
    private PanelDock _dock = null!;

    private TabItem TabFor(string panel) => panel switch
    {
        "presets" => TabPresets, "running" => TabRunning, "cues" => TabCues, "producer" => TabProducer, "holding" => TabHolding, _ => TabAux,
    };

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9 && _vm.Talking) { _ = _vm.EndTalk(); e.Handled = true; }
    }

    private void OnReviewPlan(object sender, RoutedEventArgs e)
    {
        OnOpenCopilot(this, new RoutedEventArgs());
        _vm.ReviewPlanCommand.Execute(null);
    }

    private void OnExplainProblem(object sender, RoutedEventArgs e)
    {
        OnOpenCopilot(this, new RoutedEventArgs());
        _vm.ExplainProblemsCommand.Execute(null);
    }

    /// <summary>Message drafts: a small menu of three; picking one puts it in the box to edit or send.</summary>
    private async void OnSuggestMessages(object sender, RoutedEventArgs e)
    {
        SuggestButton.IsEnabled = false;
        SuggestButton.Content = "…";
        try
        {
            var drafts = await _vm.DraftMessagesAsync();
            if (drafts.Count == 0) return;
            var menu = new ContextMenu { PlacementTarget = SuggestButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var d in drafts)
            {
                var item = new MenuItem { Header = d, FontSize = 16 };
                item.Click += (_, _) => _vm.MessageInput = d;
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }
        finally
        {
            SuggestButton.IsEnabled = true;
            SuggestButton.Content = "\u2728";
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            if (!e.IsRepeat && !_vm.Talking) { OnOpenCopilot(this, new RoutedEventArgs()); _vm.BeginTalk(); }
            e.Handled = true;
            return;
        }
        // File shortcuts work everywhere, even while typing in a box.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.O) { OnOpenShow(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.K) { OnOpenCopilot(this, new RoutedEventArgs()); e.Handled = true; return; }
            // Undo / redo live edits — but inside a text box they belong to the text box.
            if (Keyboard.FocusedElement is not TextBox && e.Key == Key.Z) { _vm.UndoCommand.Execute(null); e.Handled = true; return; }
            if (Keyboard.FocusedElement is not TextBox && e.Key == Key.Y) { _vm.RedoCommand.Execute(null); e.Handled = true; return; }
            // ALL GO: every department standing by, at once.
            if (Keyboard.FocusedElement is not TextBox && e.Key == Key.G && !e.IsRepeat) { _vm.AllGoCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.S)
            {
                SaveShow(saveAs: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                e.Handled = true;
                return;
            }
        }
        if (!_vm.Settings.OperatorShortcuts) return;
        if (Keyboard.FocusedElement is TextBox) return;

        switch (e.Key)
        {
            case Key.Space:
                _vm.ToggleCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N:
                _vm.NextItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P:
                _vm.PreviousItemCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.B:
                _vm.ToggleBlackoutCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.R:
                _vm.ResetCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F:
                _vm.SetFlash(_vm.Engine.State == TimerState.Expired ? FlashRate.Off : FlashRate.Fast);
                e.Handled = true;
                break;
            case Key.O:
                _vm.OpenOutputCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.H:
                _vm.ToggleHolding();
                e.Handled = true;
                break;
            case Key.D1: case Key.NumPad1: TriggerPreset(1); e.Handled = true; break;
            case Key.D2: case Key.NumPad2: TriggerPreset(2); e.Handled = true; break;
            case Key.D3: case Key.NumPad3: TriggerPreset(3); e.Handled = true; break;
            case Key.D4: case Key.NumPad4: TriggerPreset(4); e.Handled = true; break;
            case Key.D5: case Key.NumPad5: TriggerPreset(5); e.Handled = true; break;
            case Key.D6: case Key.NumPad6: TriggerPreset(6); e.Handled = true; break;
            case Key.D7: case Key.NumPad7: TriggerPreset(7); e.Handled = true; break;
            case Key.D8: case Key.NumPad8: TriggerPreset(8); e.Handled = true; break;
            case Key.D9: case Key.NumPad9: TriggerPreset(9); e.Handled = true; break;
            case Key.D0: case Key.NumPad0: TriggerPreset(10); e.Handled = true; break;
            case Key.OemMinus: TriggerPreset(11); e.Handled = true; break;
            case Key.OemPlus:  TriggerPreset(12); e.Handled = true; break;
        }
    }

    // The layout is designed for this client size; smaller windows scale the whole interface down evenly.
    private const double DesignWidth = 1300, DesignHeight = 875;

    private void FitToStage(object sender, SizeChangedEventArgs e)
    {
        var w = Stage.ActualWidth;
        var h = Stage.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var scale = Math.Min(1.0, Math.Min(w / DesignWidth, h / DesignHeight));
        if (scale >= 0.999)
        {
            Root.LayoutTransform = Transform.Identity;
            Root.Width = double.NaN;
            Root.Height = double.NaN;
        }
        else
        {
            Root.LayoutTransform = new ScaleTransform(scale, scale);
            // Width excludes the margin, which is scaled too.
            Root.Width = w / scale - Root.Margin.Left - Root.Margin.Right;
            Root.Height = h / scale - Root.Margin.Top - Root.Margin.Bottom;
        }
    }

    private void TriggerPreset(int slot)
    {
        var preset = _vm.Presets.FirstOrDefault(p => p.Slot == slot);
        if (preset is null) return;
        _vm.LoadPresetCommand.Execute(preset);
    }

    private void OnTimerWheel(object sender, MouseWheelEventArgs e)
    {
        // 10 s per notch; Shift = 1 min; Ctrl = 5 min (either side). No hour step: one stray notch must never cost an hour.
        var mods = Keyboard.Modifiers;
        var step = mods.HasFlag(ModifierKeys.Control) ? TimeSpan.FromMinutes(5)
                 : mods.HasFlag(ModifierKeys.Shift) ? TimeSpan.FromMinutes(1)
                 : TimeSpan.FromSeconds(10);
        var delta = e.Delta > 0 ? step : -step;
        _vm.NudgeFromWheel(delta);
        e.Handled = true;
    }

    private void OnPresetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is TimerPreset p)
            _vm.StartPresetCommand.Execute(p);
    }

    private void EditPreset(TimerPreset p)
    {
        var dlg = new PresetEditor(p) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } updated)
            _vm.ReplacePreset(updated);
    }

    // ================================================================== holding images

    private static readonly string HoldingFilter =
        "Images (*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp|All files (*.*)|*.*";

    private void OnAddHolding(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = HoldingFilter, Multiselect = true, Title = "Add holding images" };
        if (dlg.ShowDialog(this) == true) _vm.AddHoldingFiles(dlg.FileNames);
    }

    private void OnHoldingDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnHoldingDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _vm.AddHoldingFiles(files.Where(f => Help.HoldingFiles.LooksLikeImage(f)).ToArray());
        e.Handled = true;
    }

    private void OnHoldingItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HoldingItem item }) _vm.ToggleHoldingItem(item);
    }

    private void RenameHolding(HoldingItem item)
    {
        if (Help.Prompt.Ask(this, "Rename holding image", "Name", item.Name) is { } name) _vm.RenameHolding(item, name);
    }

    private void RemoveHolding(HoldingItem item)
    {
        if (MessageBox.Show(this, $"Remove \"{item.Name}\" from the holding images?{(item.IsLive ? "\n\nIt is on air now and will be taken down." : "")}",
                "Remove holding image", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            _vm.RemoveHolding(item);
    }

    private void OnEditAllPresets(object sender, RoutedEventArgs e)
    {
        var dlg = new PresetListEditor(App.SettingsStore) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.ReloadPresets();
    }

    private void OnEditRundown(object sender, RoutedEventArgs e)
    {
        var dlg = new RundownEditor(App.SettingsStore, _vm.CopilotUnavailable is null ? _vm.DraftAgendaAsync : null) { Owner = this, PreviewChime = _vm.PreviewChime };
        if (dlg.ShowDialog() == true)
            _vm.OnRundownEdited();
    }

    private void OnRundownDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button b && b.Tag is RundownRow row)
            _vm.StartItem(row);
    }

    private void OnScheduledStartKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _vm.ApplyScheduledStartCommand.Execute(null); e.Handled = true; }
    }

    private void OnScheduledStartLostFocus(object sender, RoutedEventArgs e)
    {
        if ((_vm.Settings.Rundown.ScheduledStart ?? "") != _vm.ScheduledStartInput.Trim())
            _vm.ApplyScheduledStartCommand.Execute(null);
    }

    private void OnEndOfShowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _vm.ApplyEndOfShowCommand.Execute(null); e.Handled = true; }
    }

    private void OnEndOfShowLostFocus(object sender, RoutedEventArgs e)
    {
        if ((_vm.Settings.Rundown.EndOfShow ?? "") != _vm.EndOfShowInput.Trim())
            _vm.ApplyEndOfShowCommand.Execute(null);
    }

    private void OnDurationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _vm.ApplyDurationCommand.Execute(null); e.Handled = true; }
    }

    private void OnEndAtKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { _vm.ApplyEndAtCommand.Execute(null); e.Handled = true; }
    }

    private void OnModeCountdown(object sender, RoutedEventArgs e) => _vm.SetModeCountdownCommand.Execute(null);
    private void OnModeTod(object sender, RoutedEventArgs e)       => _vm.SetModeTimeOfDayCommand.Execute(null);

    private bool ConfirmReplaceWhileLive() =>
        !_vm.IsShowLive || MessageBox.Show(this,
            "A show is running. Opening a show file replaces the presets, running order and quick messages.\n" +
            "The timer and outputs keep going.\n\nOpen another show?",
            "Open show", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Open a show file passed in (double-clicked in Explorer, or handed over by a second launch).</summary>
    public void OpenShowFile(string path)
    {
        if (!ConfirmReplaceWhileLive()) return;
        if (_vm.OpenShow(path) is { } error)
            MessageBox.Show(this, error, "Couldn't open show", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnOpenShow(object sender, RoutedEventArgs e)
    {
        if (!ConfirmReplaceWhileLive()) return;

        Directory.CreateDirectory(MainViewModel.DefaultShowFolder);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Stagewatch shows (*.stagewatch)|*.stagewatch|All files (*.*)|*.*",
            InitialDirectory = _vm.ShowPath is { } p && File.Exists(p) ? Path.GetDirectoryName(p) : MainViewModel.DefaultShowFolder
        };
        if (dlg.ShowDialog(this) != true) return;
        if (_vm.OpenShow(dlg.FileName) is { } error)
            MessageBox.Show(this, error, "Couldn't open show", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnSaveShow(object sender, RoutedEventArgs e) => SaveShow(saveAs: false);

    private void SaveShow(bool saveAs)
    {
        var path = _vm.ShowPath;
        if (saveAs || string.IsNullOrEmpty(path) || !Directory.Exists(Path.GetDirectoryName(path)))
        {
            Directory.CreateDirectory(MainViewModel.DefaultShowFolder);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Stagewatch show (*.stagewatch)|*.stagewatch",
                DefaultExt = ShowFile.Extension,
                AddExtension = true,
                FileName = _vm.ShowName == "Untitled show" ? "My event" : _vm.ShowName,
                InitialDirectory = path is not null && Directory.Exists(Path.GetDirectoryName(path)) ? Path.GetDirectoryName(path) : MainViewModel.DefaultShowFolder
            };
            if (dlg.ShowDialog(this) != true) return;
            path = dlg.FileName;
        }
        if (_vm.SaveShow(path) is { } error)
            MessageBox.Show(this, error, "Couldn't save show", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ------------------------------------------------------------------ several running orders

    private bool _pickingRundown;

    private void SyncRundownPicker()
    {
        _pickingRundown = true;
        RundownPicker.SelectedItem = _vm.LiveRundownName;
        _pickingRundown = false;
    }

    private void OnRundownPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_pickingRundown || RundownPicker.SelectedItem is not string name || _vm.IsLiveRundown(name)) return;
        if (_vm.RundownBusy && MessageBox.Show(this, $"\"{_vm.LiveRundownName}\" is running. Make \"{name}\" live instead? The current item ends now.",
                "Switch running order", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
        {
            SyncRundownPicker();
            return;
        }
        _vm.SwitchRundown(name);
        SyncRundownPicker();
    }

    private void OnRundownMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        void Item(string header, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = header, IsEnabled = enabled };
            mi.Click += (_, _) => act();
            menu.Items.Add(mi);
        }
        var selected = RundownPicker.SelectedItem as string ?? _vm.LiveRundownName;
        Item("New running order…", () => { if (Help.Prompt.Ask(this, "New running order", "Name (e.g. Day 2):", "") is { } n) _vm.NewRundown(n, duplicateLive: false); });
        Item($"Duplicate \"{_vm.LiveRundownName}\"…", () => { if (Help.Prompt.Ask(this, "Duplicate running order", "Name of the copy:", _vm.LiveRundownName + " (copy)") is { } n) _vm.NewRundown(n, duplicateLive: true); });
        menu.Items.Add(new Separator());
        foreach (var name in _vm.RundownNames.ToList())
        {
            var sub = new MenuItem { Header = name + (_vm.IsLiveRundown(name) ? "  (live)" : "") };
            var edit = new MenuItem { Header = "Edit…" };
            edit.Click += (_, _) => EditRundown(name);
            var rename = new MenuItem { Header = "Rename…" };
            rename.Click += (_, _) => { if (Help.Prompt.Ask(this, "Rename running order", "New name:", name) is { } n) { _vm.RenameRundown(name, n); SyncRundownPicker(); } };
            var delete = new MenuItem { Header = "Delete…", IsEnabled = !_vm.IsLiveRundown(name) };
            delete.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Delete the running order \"{name}\"?", "Delete running order", MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK)
                    _vm.DeleteRundown(name);
            };
            sub.Items.Add(edit);
            sub.Items.Add(rename);
            sub.Items.Add(delete);
            menu.Items.Add(sub);
        }
        menu.IsOpen = true;
    }

    private void EditRundown(string name)
    {
        var live = _vm.IsLiveRundown(name);
        var dlg = new RundownEditor(App.SettingsStore, _vm.CopilotUnavailable is null ? _vm.DraftAgendaAsync : null, live ? null : name) { Owner = this, PreviewChime = _vm.PreviewChime };
        if (dlg.ShowDialog() == true && live) _vm.OnRundownEdited();
    }

    private void OnAuxNameCommit(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Tag: Stagewatch.App.ViewModels.AuxTimerItem item } box) item.CommitName(box.Text);
    }

    private void OnAuxNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox { Tag: Stagewatch.App.ViewModels.AuxTimerItem item } box)
        {
            item.CommitName(box.Text);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnRecover(object sender, RoutedEventArgs e)
    {
        var dlg = new RecoverWindow(_vm) { Owner = this };
        dlg.ShowDialog();
    }

    private CopilotWindow? _copilotWindow;

    private void OnOpenCopilot(object sender, RoutedEventArgs e)
    {
        if (_copilotWindow is { IsLoaded: true }) { _copilotWindow.Activate(); return; }
        _copilotWindow = new CopilotWindow(_vm) { Owner = this };
        _copilotWindow.Closed += (_, _) => _copilotWindow = null;
        _copilotWindow.Show();
    }

    private async void OnDraftSummary(object sender, RoutedEventArgs e)
    {
        if (_vm.CopilotUnavailable is { } why) { MessageBox.Show(this, why, "Copilot", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var button = sender as Button;
        if (button is not null) { button.IsEnabled = false; button.Content = "Drafting…"; }
        string draft;
        try { draft = await _vm.DraftShowSummaryAsync(); }
        finally { if (button is not null) { button.IsEnabled = true; button.Content = "\u2728 Draft the show summary…"; } }
        if (draft.Length == 0) { MessageBox.Show(this, "The copilot couldn't draft a summary (offline, or nothing in the journal yet).", "Copilot"); return; }
        var kept = Help.TextReview.Show(this, "Show summary", "Edit the draft. Only what you keep goes in the report.", draft);
        if (kept is not null) _vm.KeepShowSummary(kept);
    }

    private ScreensWindow? _screens;

    private void OnOpenScreens(object sender, RoutedEventArgs e)
    {
        if (_screens is { IsLoaded: true }) { _screens.Activate(); return; }
        _screens = new ScreensWindow(_vm) { Owner = this };
        _screens.Closed += (_, _) => _screens = null;
        _screens.Show();
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(App.SettingsStore, App.License, _vm.BuildDiagnostics, _vm.IntegrationStatus, _vm.PresenterPcs, () => _vm.CertificateFingerprint)
        {
            Owner = this,
            Sounds = new SettingsDialog.SoundHooks(_vm.AddSound, _vm.RemoveSound, _vm.PreviewChime),
            Keys = new SettingsDialog.KeyHooks(_vm.CreateApiKey, _vm.RevokeApiKey, _vm.ApiKeyLastUsed, _vm.ApiKeyBindingLine, _vm.ApiKeyNodeChoices, _vm.RebindApiKey),
        };
        if (dlg.ShowDialog() == true)
        {
            _vm.ApplySettings();
            _vm.BumpConfig();
        }
    }

    // ================================================================== help

    private void OnOpenHelp(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var item in HelpMenu()) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    private IEnumerable<Control> HelpMenu() => new Control[]
    {
        ContextHelp.Item("How to use Stagewatch", () => HelpWindow.ShowTopic(this, "start"), gesture: "F1"),
        ContextHelp.Item("Keyboard and mouse shortcuts", () => HelpWindow.ShowTopic(this, "shortcuts")),
        ContextHelp.Item("Troubleshooting", () => HelpWindow.ShowTopic(this, "trouble")),
        new Separator(),
        ContextHelp.Item("License and plans…", OpenLicense),
    };

    private void OpenLicense() => new LicenseDialog(App.LicenseManager) { Owner = this }.ShowDialog();

    private void ShowTab(TabItem tab) { if (_dock.IdOf(tab) is { } id) _dock.Show(id); }

    /// <summary>Shortcuts pressed in a torn-out panel window act as they would here.</summary>
    internal void HandleShortcut(KeyEventArgs e) => OnKeyDown(this, e);
    internal void HandleShortcutUp(KeyEventArgs e) => OnKeyUp(this, e);

    /// <summary>Right-click actions for each tagged element (see ContextHelp). Every action goes through the
    /// same commands as the buttons, so remotes, Companion's recorder and the journal see it the same way.</summary>
    public IEnumerable<Control> ActionsFor(string tip, FrameworkElement element)
    {
        static MenuItem I(string header, Action run, bool enabled = true, string? gesture = null) => ContextHelp.Item(header, run, enabled, gesture);
        var running = _vm.Engine.IsCounting;
        var hasRundown = _vm.HasRundown;

        switch (tip)
        {
            case "timer":
            case "timer-state":
                yield return I(running ? "Pause" : "Start", () => _vm.ToggleCommand.Execute(null), gesture: "Space");
                yield return I("Reset", () => _vm.ResetCommand.Execute(null), gesture: "R");
                yield return new Separator();
                yield return I("Add 1 minute", () => _vm.NudgeCommand.Execute("+1m"));
                yield return I("Remove 1 minute", () => _vm.NudgeCommand.Execute("-1m"));
                yield return I("Add 10 seconds", () => _vm.NudgeCommand.Execute("+10s"));
                yield return I("Remove 10 seconds", () => _vm.NudgeCommand.Execute("-10s"));
                yield return new Separator();
                yield return I("Flash the timer", () => _vm.SetFlash(_vm.Engine.State == TimerState.Expired ? FlashRate.Off : FlashRate.Fast), gesture: "F");
                yield return I(_vm.Blackout ? "Blackout off" : "Blackout", () => _vm.ToggleBlackoutCommand.Execute(null), gesture: "B");
                yield return I("Open the output", () => _vm.OpenOutputCommand.Execute(null), gesture: "O");
                break;

            case "mode":
                yield return I("Countdown", () => _vm.SetModeCountdownCommand.Execute(null));
                yield return I("Count up", () => _vm.SetModeCountUpCommand.Execute(null));
                yield return I("Time of day", () => _vm.SetModeTimeOfDayCommand.Execute(null));
                break;

            case "end-time":
            case "end-at":
            case "stretch":
                yield return I("Clear end-at", () => _vm.ClearEndAtCommand.Execute(null));
                break;

            case "next-title":
            case "schedule":
            case "next":
                yield return I("Load the next item", () => _vm.NextItemCommand.Execute(null), hasRundown, "N");
                yield return I("Previous item", () => _vm.PreviousItemCommand.Execute(null), hasRundown, "P");
                yield return I("Show the running order", () => ShowTab(TabRunning));
                break;

            case "preset" when element.Tag is TimerPreset p:
                yield return I("Load", () => _vm.LoadPresetCommand.Execute(p), gesture: p.Slot switch { <= 9 => p.Slot.ToString(), 10 => "0", 11 => "−", _ => "=" });
                yield return I("Load and start", () => _vm.StartPresetCommand.Execute(p));
                yield return new Separator();
                yield return I("Edit this preset…", () => EditPreset(p));
                yield return I("Edit all presets…", () => OnEditAllPresets(this, new RoutedEventArgs()));
                break;

            case "rundown-row" when element.Tag is RundownRow row:
                yield return I("Load", () => _vm.LoadItemCommand.Execute(row));
                yield return I("Load and start", () => _vm.StartItem(row));
                yield return new Separator();
                yield return I("Trim 1 minute", () => _vm.RunOperatorLine($"trim {row.Index} -60"));
                yield return I("Extend 1 minute", () => _vm.RunOperatorLine($"trim {row.Index} 60"));
                if (row.AdjustSeconds != 0)
                    yield return I("Undo trims", () => _vm.RunOperatorLine($"trim {row.Index} {-row.AdjustSeconds}"));
                yield return new Separator();
                yield return I("Edit running order…", () => OnEditRundown(this, new RoutedEventArgs()));
                break;

            case "rundown-start":
            case "rundown-edit":
            case "rundown-nav":
                yield return I("Edit running order…", () => OnEditRundown(this, new RoutedEventArgs()));
                yield return I("Back to the top", () => _vm.ResetShowCommand.Execute(null), hasRundown);
                break;

            case "presets-edit":
                yield return I("Edit all presets…", () => OnEditAllPresets(this, new RoutedEventArgs()));
                break;

            case "department" when element.DataContext is DepartmentRow row:
                yield return I($"Standby {row.Name}", () => _vm.DepartmentStandbyCommand.Execute(row.Name));
                yield return I($"GO {row.Name}", () => _vm.DepartmentGoCommand.Execute(row.Name));
                yield return I($"Clear {row.Name}'s cue", () => _vm.RunOperatorLine($"signal clear \"{row.Name}\""), row.State.Length > 0);
                yield return I($"ALL GO ({_vm.StandingByCount} standing by)", () => _vm.AllGoCommand.Execute(null), _vm.HasStandingBy);
                break;

            case "panel" when element is FrameworkElement header && PanelDock.IdOf(header) is { Length: > 0 } panel:
                yield return I($"Pop out \"{_dock.Title(panel)}\" into its own window", () => _dock.PopOut(panel));
                yield return I("Move left", () => _dock.Nudge(panel, -1), SideTabs.Items.IndexOf(TabFor(panel)) > 0);
                yield return I("Move right", () => _dock.Nudge(panel, +1), SideTabs.Items.IndexOf(TabFor(panel)) < SideTabs.Items.Count - 1);
                yield return I("Reset panel layout", () => _dock.Reset());
                break;

            case "panel-splitter":
                yield return I("Standard width", () => { App.SettingsStore.Update(s => s.Panels.SideWidth = PanelLayout.DefaultSideWidth); SideCol.Width = new GridLength(PanelLayout.DefaultSideWidth); });
                yield return I("Reset panel layout", () => _dock.Reset());
                break;

            case "all-go":
                yield return I("ALL GO", () => _vm.AllGoCommand.Execute(null), _vm.HasStandingBy);
                yield return I("Clear all cues", () => _vm.RunOperatorLine("signal clear"), _vm.HasStandingBy);
                break;

            case "signal" when element.DataContext is SignalView sig:
                yield return I("Send again", () => _vm.RunOperatorLine($"signal resend {sig.Id}"));
                yield return I("Clear this cue", () => _vm.RunOperatorLine($"signal clear {sig.Id}"));
                yield return I($"Clear all cues to {sig.Target}", () => _vm.RunOperatorLine($"signal clear \"{sig.Target}\""));
                break;

            case "question" when element.DataContext is QuestionView q:
                yield return I("Take live", () => _vm.RunOperatorLine($"q {q.Id} live"));
                yield return I("Approve", () => _vm.RunOperatorLine($"q {q.Id} approve"));
                yield return I("Star (ask first)", () => _vm.RunOperatorLine($"q {q.Id} star"));
                yield return I("Answered", () => _vm.RunOperatorLine($"q {q.Id} answered"));
                yield return I("Dismiss", () => _vm.RunOperatorLine($"q {q.Id} dismiss"));
                break;

            case "qa-next":
                yield return I("Next question", () => _vm.QuestionNextCommand.Execute(null));
                yield return I("Clear the queue", () => _vm.RunOperatorLine("q clear"));
                break;

            case "advice" when element.DataContext is Advice a && a.CommandLabel is { Length: > 0 } label:
                yield return I(label, () => _vm.ApplyAdviceCommand.Execute(a));
                break;

            case "chip-curfew":
            case "chip-forecast":
                yield return I("Show the assistant", () => ShowTab(TabProducer));
                yield return I("Catch up", () => _vm.CatchUpNowCommand.Execute(null));
                break;

            case "chip-delay":
            case "delay":
                yield return I("Delay +1 minute", () => _vm.AddDelayCommand.Execute("60"));
                yield return I("Delay +5 minutes", () => _vm.AddDelayCommand.Execute("300"));
                yield return I("Clear the delay", () => _vm.RunOperatorLine("delay clear"));
                break;

            case "chip-cue":
            case "ladder":
            case "preview":
                yield return I(_vm.PreviewActive ? "Stop briefing preview" : "Briefing preview (10×)", () => _vm.TogglePreviewCommand.Execute(null));
                yield return I("Show speaker cues", () => ShowTab(TabProducer));
                break;

            case "blackout":
                yield return I(_vm.Blackout ? "Blackout off" : "Blackout on", () => _vm.ToggleBlackoutCommand.Execute(null), gesture: "B");
                break;

            case "holo":
            {
                var d = _vm.Settings.Display;
                yield return I((_vm.Holographic && !d.EggTimer ? "● " : "") + "Hologram", () => _vm.SetThreeDScene(egg: false));
                yield return I((_vm.Holographic && d.EggTimer ? "● " : "") + "3D egg timer", () => _vm.SetThreeDScene(egg: true));
                yield return I((!_vm.Holographic ? "● " : "") + "Standard display", () => _vm.SetHolographic(false));
                yield return new Separator();
                yield return Toggle("Animated transitions", d.Animations3D, on => _vm.Set3DOption("animations", on));
                yield return Toggle("Particles", d.Particles3D, on => _vm.Set3DOption("particles", on));
                yield return Toggle("Projector base and reflection", d.HoloFloor, on => _vm.Set3DOption("floor", on));
                yield return new Separator();
            }
                yield return I("Open a holographic browser screen", () => OpenInBrowser("/holo"), _vm.RemoteBaseUrl is not null);
                yield return I("Display settings…", () => OnOpenSettings(this, new RoutedEventArgs()));
                break;

            case "output-mode":
            case "key-colour":
            case "ndi":
                yield return I("Full screen", () => _vm.SelectedOutputMode = OutputMode.FullScreen);
                yield return I("Keyed full screen", () => _vm.SelectedOutputMode = OutputMode.KeyedFullScreen);
                yield return I("Floating mini", () => _vm.SelectedOutputMode = OutputMode.FloatingMinimal);
                yield return I("Close the output", () => _vm.SelectedOutputMode = OutputMode.Closed);
                yield return new Separator();
                yield return I("Output settings…", () => OnOpenSettings(this, new RoutedEventArgs()));
                break;

            case "message":
            case "send":
                var typed = _vm.MessageInput.Trim().Length > 0;
                yield return I("Send", () => _vm.ApplyMessageCommand.Execute(null), typed);
                yield return I("Send and ask to acknowledge", () => { _vm.AckRequested = true; _vm.ApplyMessageCommand.Execute(null); },
                               typed && FeatureGate.AllowsCrewCues(_vm.License.Tier));
                yield return I("Save as quick message", _vm.AddQuickMessage, typed);
                yield return I("Clear the message", () => _vm.ClearMessageCommand.Execute(null));
                break;

            case "quick" when element.DataContext is MessageBankEntry entry:
                yield return I("Send", () => _vm.SendBankMessageCommand.Execute(entry));
                yield return I("Edit before sending", () =>
                {
                    _vm.MessageInput = entry.Text;
                    _vm.SelectedUrgency = entry.Urgency;
                    _vm.SelectedFlash = entry.Flash;
                });
                yield return new Separator();
                yield return I("Remove quick message", () => _vm.RemoveQuickMessage(entry));
                break;

            case "status":
                yield return I("Copy", () => CopyText(_vm.StatusLine), _vm.StatusLine.Length > 0);
                // Every playout link's certificate that waits for a person, whether or not its node has a Show network row: the same
                // entries, bound to the certificate each shows now.
                foreach (var waiting in _vm.CertificatesWaiting()) yield return I(waiting.Label, () => TrustCertificate(waiting));
                break;

            case "show-network" when element.DataContext is PeerRow peer:
                // The row's button (Make a key) and Settings → Remote's "Let it speak for…" and Revoke run the same code as these.
                yield return I("Copy node id", () => CopyText(peer.NodeId), peer.NodeId.Length > 0);
                if (peer.MakeKeyLabel.Length > 0) yield return I(peer.MakeKeyLabel, () => _vm.MakeKeyForPeerCommand.Execute(peer));
                yield return I("Let a key speak for this node…", () => LetKeySpeakFor(peer), peer.NodeId.Length > 0);
                yield return I("Revoke its key…", () => RevokePeerKey(peer), peer.KeyId.Length > 0);
                // A playout link's certificate of this node that waits for a person: the row's button and these run the same code.
                foreach (var waiting in _vm.CertificatesWaitingFor(peer)) yield return I(waiting.Label, () => TrustCertificate(waiting));
                break;

            case "show-hello":
                // The hello's line and the network's counts on the Producer tab: Copy takes the line right-clicked; Remote's bind is the
                // hello's cause.
                var shown = element is TextBlock { Text.Length: > 0 } line ? line.Text : _vm.HelloLine;
                yield return I("Copy", () => CopyText(shown), shown.Length > 0);
                yield return I("Remote settings…", () => OnOpenSettings(this, new RoutedEventArgs()));
                break;

            case "remote-line":
                yield return I("Copy", () => CopyText(_vm.RemoteLine), _vm.RemoteLine.Length > 0);
                yield return I("Remote settings…", () => OnOpenSettings(this, new RoutedEventArgs()));
                break;

            case "license":
                yield return I("License and plans…", OpenLicense);
                break;

            case "help":
                foreach (var item in HelpMenu()) yield return item;
                break;

            case "report":
            case "report-csv":
                yield return I("Show report", () => _vm.OpenReportCommand.Execute(null));
                yield return I("Export CSV", () => _vm.ExportReportCsvCommand.Execute(null));
                break;

            case "settings":
                yield return I("Settings…", () => OnOpenSettings(this, new RoutedEventArgs()));
                break;

            case "holding-item" when element.Tag is HoldingItem h:
                yield return I(h.IsLive ? "Take off air" : "Show on every screen", () => _vm.ToggleHoldingItem(h));
                yield return new Separator();
                yield return I("Rename…", () => RenameHolding(h));
                yield return I("Remove…", () => RemoveHolding(h));
                yield return new Separator();
                yield return I("Add images…", () => OnAddHolding(this, new RoutedEventArgs()));
                break;

            case "chip-holding":
            case "holding-clear":
                yield return I("Take the holding image off air", _vm.ClearHolding, _vm.HoldingOnAir, "H");
                yield return I("Show the Holding tab", () => ShowTab(TabHolding));
                break;

            case "holding-add":
            case "holding-fill":
                yield return I("Add images…", () => OnAddHolding(this, new RoutedEventArgs()));
                yield return I(_vm.HoldingFill ? "Fit the whole image" : "Fill the screen (crop)", () => _vm.HoldingFill = !_vm.HoldingFill);
                break;
        }
    }

    private static MenuItem Toggle(string header, bool on, Action<bool> set)
    {
        var item = ContextHelp.Item(header, () => set(!on));
        item.IsChecked = on;
        return item;
    }

    private void OpenInBrowser(string path)
    {
        if (_vm.RemoteBaseUrl is not { } baseUrl) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(baseUrl + path) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    /// <summary>
    /// Revoke the node key a Show network row is verified by, once the operator confirms: the same revocation as Settings →
    /// Remote → API keys, so the app's context and proposals are refused and its link on /ws drops to the public frame at once.
    /// </summary>
    private void RevokePeerKey(PeerRow peer)
    {
        var key = _vm.ApiKeys.FirstOrDefault(k => k.Id == peer.KeyId);
        if (key is null) return;
        if (MessageBox.Show(this, $"Revoke API key '{key.Name}'? {peer.Name} stops speaking for {NodeBindings.Describe(peer.NodeKind, peer.NodeId)} at once: " +
                "its context and proposals are refused, and its link on /ws keeps only the public frame until it has a key again.",
                "Show network", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        _vm.RevokeApiKey(key.Id);
    }

    /// <summary>
    /// The Show network row's trust button: the certificate its words and label were drawn from (<see cref="PeerRow.TrustWaiting"/>),
    /// never one looked up again at the press — a certificate pinned in its place since is not trusted by it.
    /// </summary>
    private void OnTrustCertificate(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PeerRow { TrustWaiting: { } waiting })
            TrustCertificate(waiting);
    }

    /// <summary>
    /// A person trusts a playout link's certificate (Show Bus 13.8.2–3). One pinned first is one press, the press bound to the
    /// fingerprint it showed. A changed one opens an entry that is never filled in: the person compares the certificate on the node's
    /// own screen and types at least 8 hex of it, the short form beside the box; nothing else trusts it, and no pad, line or assistant can.
    /// </summary>
    private void TrustCertificate(CertificateWaiting waiting)
    {
        if (!waiting.Wait.Changed)
        {
            _vm.TrustPlayoutCertificate(waiting.Link, waiting.Wait.Fingerprint, typed: false);
            return;
        }
        var pinned = waiting.Wait.Pinned.Length > 0 ? $" (the one pinned for it began {Stagewatch.Integrations.PlayoutWire.Short(waiting.Wait.Pinned)} …)" : "";
        var label = $"{waiting.Peer} now shows a certificate that begins {waiting.Short} …, not the one it was pinned with{pinned}. Nothing is sent to it "
                    + $"until you trust this one. Compare it on {waiting.Peer}'s own screen, then type at least {PlayoutRules.TrustHexMin.ToString(System.Globalization.CultureInfo.InvariantCulture)} hex of it:";
        if (Help.Prompt.Ask(this, "Certificate changed", label, "") is { } typed)
            _vm.TrustPlayoutCertificate(waiting.Link, typed, typed: true);
    }

    /// <summary>
    /// Let a node key speak for a Show network row's node: the keys refused for it first, then free keys, then keys that
    /// would move off the node they speak for (each says so). The same rebind as Settings → Remote → "Let it speak for…".
    /// </summary>
    private void LetKeySpeakFor(PeerRow peer)
    {
        var choices = _vm.KeyChoicesFor(peer);
        if (choices.Count == 0)
        {
            MessageBox.Show(this, $"No other node key to choose. Make one for {peer.Name}: Make a key for… on its row, or Settings → Remote → New key… (another show app).",
                "Show network", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var label = $"Which node key speaks for {peer.Name} ({NodeBindings.Describe(peer.NodeKind, peer.NodeId)})? From now on its context, proposals and " +
                    "results are taken only with that key, and a key that spoke for another node stops.";
        if (Help.Pick.One(this, "Let a key speak for this node", label, choices, c => c.Label, "Let it speak") is { } chosen)
            _vm.LetKeySpeakForPeer(peer, chosen.KeyId);
    }

    /// <summary>A node key made for a refused app: shown once and put on the clipboard; Stagewatch keeps only its fingerprint.</summary>
    private void ShowNewNodeKey(string words, string secret)
    {
        var panel = new StackPanel { Margin = new Thickness(20), MaxWidth = 460 };
        panel.Children.Add(new TextBlock { Text = words, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Copy this key now — Stagewatch keeps only a fingerprint of it and can't show it again.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new TextBox { Text = secret, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 8) });
        try
        {
            Clipboard.SetText(secret);
            panel.Children.Add(new TextBlock { Text = "(Already copied to the clipboard.)", Opacity = 0.6 });
        }
        catch (System.Runtime.InteropServices.ExternalException) { panel.Children.Add(new TextBlock { Text = "(The clipboard is busy: copy it from the box.)", Opacity = 0.6 }); }
        var win = new Window { Title = "A key of its own", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Background };
        var done = new Button { Content = "Done", IsDefault = true, IsCancel = true, MinWidth = 90, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) };
        done.Click += (_, _) => win.Close();
        var root = new StackPanel();
        root.Children.Add(panel);
        root.Children.Add(done);
        win.Content = root;
        win.ShowDialog();
    }

    private static void CopyText(string text)
    {
        try { Clipboard.SetText(text); } catch (Exception) { }
    }
}
