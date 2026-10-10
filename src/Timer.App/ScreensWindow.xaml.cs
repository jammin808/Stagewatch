using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Stagewatch.App.ViewModels;
using Stagewatch.Core;
using Stagewatch.Remote;

namespace Stagewatch.App;

/// <summary>
/// Screens: connected browsers (name, page, device), identify / send / save, and saved setups (/p/name).
/// Refreshes twice a second in place — rows keep their identity, so a selection or an edit in progress survives.
/// Every action goes through the same <c>screen …</c> command remotes and Companion use.
/// </summary>
public partial class ScreensWindow : Window
{
    private static readonly string[] Pages = { "/viewer", "/audience", "/clock", "/countdown?to=next", "/timeline", "/holding", "/holo", "/backstage", "/rooms", "/viewer?style=flat" };

    private readonly MainViewModel _vm;
    private readonly ObservableCollection<ScreenRow> _rows = new();
    private readonly ObservableCollection<PresetRow> _presets = new();
    private readonly DispatcherTimer _refresh;
    private bool _editing;

    public ScreensWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        ScreenGrid.ItemsSource = _rows;
        PresetGrid.ItemsSource = _presets;
        ScreenGrid.BeginningEdit += (_, _) => _editing = true;
        _refresh = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); RefreshPresets(); _refresh.Start(); };
        Closed += (_, _) => _refresh.Stop();
    }

    private void Refresh()
    {
        if (!_vm.ScreensAvailable)
        {
            Subtitle.Text = "The web server is off — turn on Settings → Remote to use browser screens.";
            _rows.Clear();
            return;
        }
        if (_editing) return;
        var now = DateTime.UtcNow;
        var live = _vm.ListScreens();
        for (int i = _rows.Count - 1; i >= 0; i--)
            if (!live.Any(s => s.Id == _rows[i].Id)) _rows.RemoveAt(i);
        foreach (var s in live)
        {
            var row = _rows.FirstOrDefault(r => r.Id == s.Id);
            if (row is null) { row = new ScreenRow(s.Id); _rows.Add(row); }
            row.Update(s, now);
        }
        Subtitle.Text = live.Count == 0
            ? "No browser screens connected yet. Open http://<this PC>:9924/viewer on a TV or tablet."
            : $"{live.Count(s => !s.Control)} screen(s) and {live.Count(s => s.Control)} control page(s). Double-click a name to change it; select screens to find them or send them somewhere else.";
    }

    private void RefreshPresets()
    {
        _presets.Clear();
        foreach (var p in _vm.Settings.Remote.ScreenPresets)
            _presets.Add(new PresetRow(p.Name, p.Path, _vm.PresetAddress(p.Name) ?? ""));
        var keep = Target.Text;
        Target.Items.Clear();
        foreach (var p in _presets) Target.Items.Add(p.Name);
        foreach (var name in _vm.LayoutNames) Target.Items.Add("/layout?name=" + name);
        foreach (var page in Pages) Target.Items.Add(page);
        Target.Text = keep.Length > 0 ? keep : _presets.FirstOrDefault()?.Name ?? "/viewer";
    }

    /// <summary>The selected screens, or "all" when none is selected.</summary>
    private IEnumerable<string> Selected()
    {
        var picked = ScreenGrid.SelectedItems.Cast<ScreenRow>().Select(r => r.Id).ToList();
        return picked.Count > 0 ? picked : new[] { "all" };
    }

    private void Run(string line)
    {
        var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var cmd, out var error)) _vm.Execute(cmd);
        else MessageBox.Show(this, error, "Screens", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnIdentify(object sender, RoutedEventArgs e)
    {
        foreach (var who in Selected()) Run($"screen identify {who}");
    }

    private void OnReload(object sender, RoutedEventArgs e)
    {
        foreach (var who in Selected()) Run($"screen reload {who}");
    }

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        var who = Selected().ToList();
        var label = who is ["all"] ? "every screen" : who.Count == 1 ? "this screen" : $"{who.Count} screens";
        if (MessageBox.Show(this, $"Disconnect {label}? They stay off until someone reloads the page on the device.", "Screens",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        foreach (var w in who) Run($"screen disconnect {w}");
    }

    private void OnSend(object sender, RoutedEventArgs e)
    {
        var target = Target.Text.Trim();
        if (target.Length == 0) return;
        if (!ScreenPreset.ValidName(target) && !ScreenPreset.ValidPath(target))
        {
            MessageBox.Show(this, "Choose a saved setup, or type a page that starts with / (e.g. /viewer?cue=0).", "Screens");
            return;
        }
        var who = Selected().ToList();
        if (who is ["all"] && _rows.Count(r => !r.Control) > 1 &&
            MessageBox.Show(this, $"Send every screen to {target}?", "Screens", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;
        foreach (var w in who) Run($"screen go {target} {w}");
    }

    private void OnSaveFromScreen(object sender, RoutedEventArgs e)
    {
        if (ScreenGrid.SelectedItem is not ScreenRow row)
        {
            MessageBox.Show(this, "Select the screen whose page and options you want to save.", "Screens");
            return;
        }
        var name = Help.Prompt.Ask(this, "Save screen setup", $"Name for {row.Page} (letters, digits, - and _):",
            row.Name.Length > 0 ? Slug(row.Name) : "");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!ScreenPreset.ValidName(name.Trim())) { MessageBox.Show(this, "Use letters, digits, - and _ only (e.g. lobby).", "Screens"); return; }
        _vm.SaveScreenPreset(name.Trim(), row.Page);
        RefreshPresets();
    }

    private void OnAddPreset(object sender, RoutedEventArgs e)
    {
        var name = NewName.Text.Trim(); var path = NewPath.Text.Trim();
        if (!ScreenPreset.ValidName(name)) { MessageBox.Show(this, "Name: letters, digits, - and _ only (e.g. lobby).", "Screens"); return; }
        if (!ScreenPreset.ValidPath(path)) { MessageBox.Show(this, "Page must start with / (e.g. /audience?holding=0) and can't contain a PIN.", "Screens"); return; }
        _vm.SaveScreenPreset(name, path);
        NewName.Text = "";
        RefreshPresets();
    }

    /// <summary>The screen designer in this PC's browser, logged in.</summary>
    private void OnDesign(object sender, RoutedEventArgs e)
    {
        if (_vm.DesignerAddress() is not { } url) { MessageBox.Show(this, "Turn on browser pages first (Settings → Remote).", "Screen designer"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void OnCopyPreset(object sender, RoutedEventArgs e)
    {
        if (PresetGrid.SelectedItem is PresetRow p && p.Address.Length > 0)
        {
            try { Clipboard.SetText(p.Address); } catch { }
        }
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        if (PresetGrid.SelectedItem is not PresetRow p) return;
        _vm.DeleteScreenPreset(p.Name);
        RefreshPresets();
    }

    private void OnNameEdited(object? sender, DataGridCellEditEndingEventArgs e)
    {
        _editing = false;
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not ScreenRow row || e.EditingElement is not TextBox box) return;
        var name = box.Text.Trim();
        Run(name.Length == 0 ? $"screen name {row.Id}" : $"screen name {row.Id} {name}");
    }

    private void OnGridKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) _editing = false;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static string Slug(string s) => new(s.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray());

    public sealed class ScreenRow : INotifyPropertyChanged
    {
        public ScreenRow(string id) => Id = id;
        public string Id { get; }
        public string Name { get; set; } = "";
        public string Page { get; private set; } = "";
        public string Device { get; private set; } = "";
        public string Address { get; private set; } = "";
        public string Since { get; private set; } = "";
        public bool Control { get; private set; }

        public void Update(ScreenInfo s, DateTime now)
        {
            Set(nameof(Name), Name, s.Name, v => Name = v);
            var page = s.Control ? "🔒 " + s.Page : s.Page;
            Set(nameof(Page), Page, s.Tabs > 1 ? $"{page}  (+{s.Tabs - 1} tab)" : page, v => Page = v);
            Set(nameof(Device), Device, s.Device, v => Device = v);
            Set(nameof(Address), Address, s.Address, v => Address = v);
            var age = now - s.ConnectedUtc;
            Set(nameof(Since), Since, age.TotalMinutes < 1 ? "just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min" : $"{(int)age.TotalHours} h {age.Minutes} min", v => Since = v);
            Control = s.Control;
        }

        private void Set(string prop, string old, string value, Action<string> assign)
        {
            if (old == value) return;
            assign(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public sealed record PresetRow(string Name, string Path, string Address);
}
