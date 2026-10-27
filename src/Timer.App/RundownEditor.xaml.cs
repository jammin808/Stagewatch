using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Win32;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Stagewatch.Core;

namespace Stagewatch.App;

/// <summary>Edit the running order: grid editing, reorder, spreadsheet paste, CSV import/export.</summary>
public partial class RundownEditor : Window
{
    private readonly SettingsStore _store;
    private readonly ObservableCollection<Row> _rows = new();

    public IReadOnlyList<SegmentKind> Kinds { get; } = Enum.GetValues<SegmentKind>();
    public IReadOnlyList<string> LadderIds { get; }

    private readonly Func<string, Task<Stagewatch.Integrations.AgendaDraft>>? _draftAgenda;

    private readonly string? _rundownName;
    private readonly Dictionary<string, Dictionary<string, string>> _baselineNotes = new();

    /// <summary>The running order being edited: the live one, or another of the show's running orders by name.</summary>
    private Rundown Target(AppSettings s)
        => _rundownName is null || string.Equals(_rundownName, s.RundownName, StringComparison.OrdinalIgnoreCase)
            ? s.Rundown
            : s.OtherRundowns.FirstOrDefault(r => string.Equals(r.Name, _rundownName, StringComparison.OrdinalIgnoreCase))?.Rundown ?? s.Rundown;

    public RundownEditor(SettingsStore store, Func<string, Task<Stagewatch.Integrations.AgendaDraft>>? draftAgenda = null, string? rundownName = null)
    {
        _store = store;
        _draftAgenda = draftAgenda;
        _rundownName = rundownName;
        LadderIds = new[] { "" }.Concat(store.Settings.Display.AllLadders().Select(l => l.Id)).ToList();
        InitializeComponent();
        Help.ContextHelp.SetTopic(this, "rundown");
        Help.ContextHelp.Attach(this);
        DataContext = this;
        // Show time zone: blank = this PC. Fixed starts and curfews are read in this zone.
        ZoneCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "This PC's time zone", Tag = "" });
        foreach (var z in TimeZoneInfo.GetSystemTimeZones())
            ZoneCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = z.DisplayName, Tag = z.Id });
        var target = Target(store.Settings);
        if (_rundownName is not null && !string.Equals(_rundownName, store.Settings.RundownName, StringComparison.OrdinalIgnoreCase))
            Title = $"Edit running order — {_rundownName} (not live)";
        var current = target.TimeZoneId ?? "";
        ZoneCombo.SelectedItem = ZoneCombo.Items.Cast<System.Windows.Controls.ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Tag, current, StringComparison.OrdinalIgnoreCase)) ?? ZoneCombo.Items[0];
        foreach (var (k, v) in target.GroupTargets) _groupTargets[k] = v;
        foreach (var (k, v) in target.LabelColors) _labelColors[k] = v;
        _sheetUrl = target.SheetUrl;
        LoopCombo.SelectedItem = LoopCombo.Items.Cast<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == target.Loop.ToString())
                                 ?? LoopCombo.Items[0];
        // What the department notes were when the editor opened: the base for merging on save.
        foreach (var item in target.Items)
            _baselineNotes[item.Id] = new Dictionary<string, string>(item.DeptNotes, StringComparer.OrdinalIgnoreCase);
        foreach (var item in target.Items)
        {
            var row = Row.From(item);
            row.PropertyChanged += (_, _) => UpdateSummary();
            _rows.Add(row);
        }
        _rows.CollectionChanged += (_, _) => Renumber();
        Grid.ItemsSource = _rows;
        Renumber();
    }

    private void Renumber()
    {
        for (int i = 0; i < _rows.Count; i++) _rows[i].Number = i + 1;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var total = _rows.Sum(r => (long)r.Seconds);
        var bad = _rows.Count(r => !r.DurationValid);
        var badTimes = _rows.Count(r => !r.TimesValid);
        Summary.Text = $"{_rows.Count} item{(_rows.Count == 1 ? "" : "s")} · total {TimerTick.FormatSeconds(total, forceHours: true)}"
                       + (bad > 0 ? $" · {bad} duration{(bad == 1 ? "" : "s")} not understood (shown as 0)" : "")
                       + (badTimes > 0 ? $" · {badTimes} start/curfew time{(badTimes == 1 ? "" : "s")} not understood (use 14:30 or 2026-10-24 14:30)" : "")
                       + GroupSummary();
    }

    /// <summary>" · Morning 1:55:00 / 2:00:00 target · Panels over by 05:00" for groups in the editor.</summary>
    private string GroupSummary()
    {
        var items = _rows.Select(r => new RundownItem { Group = r.Group, DurationSeconds = r.Seconds }).ToList();
        var parts = RundownGroups.Spans(items).Select(g =>
        {
            var planned = RundownGroups.PlannedSeconds(items, g.First, g.Last);
            var target = _groupTargets.FirstOrDefault(t => string.Equals(t.Key, g.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (target <= 0) return $"{g.Name} {TimerTick.FormatSeconds(planned)}";
            var diff = planned - target;
            return $"{g.Name} {TimerTick.FormatSeconds(planned)} / {TimerTick.FormatSeconds(target)}"
                   + (Math.Abs(diff) < 30 ? "" : diff > 0 ? $" (over by {TimerTick.FormatSeconds(diff)})" : $" ({TimerTick.FormatSeconds(-diff)} spare)");
        }).ToList();
        return parts.Count == 0 ? "" : "\nGroups: " + string.Join(" · ", parts);
    }

    private readonly Dictionary<string, int> _groupTargets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _labelColors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rename or recolour labels across every item at once.</summary>
    private void OnLabels(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var labels = Stagewatch.Core.Labels.InUse(_rows.Select(r => new RundownItem { Labels = r.Labels }));
        if (labels.Count == 0)
        {
            MessageBox.Show(this, "Type labels in the Labels column first, separated by commas (e.g. VT, GFX, CAM 2).", "Labels");
            return;
        }
        var probe = new Rundown { LabelColors = new Dictionary<string, string>(_labelColors, StringComparer.OrdinalIgnoreCase) };
        var edits = new List<(string Old, System.Windows.Controls.TextBox Name, System.Windows.Controls.ComboBox Color)>();
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Rename or recolour — every item with the label changes. Empty name = remove it.", Opacity = 0.7, Margin = new Thickness(0, 0, 0, 10) });
        for (int i = 0; i < labels.Count; i++)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var name = new System.Windows.Controls.TextBox { Text = labels[i], Width = 160, MaxLength = 20 };
            var color = new System.Windows.Controls.ComboBox { Width = 120, Margin = new Thickness(8, 0, 0, 0) };
            var current = Stagewatch.Core.Labels.ColorFor(probe, labels[i], i);
            foreach (var c in Stagewatch.Core.Labels.Palette)
            {
                var swatch = new System.Windows.Controls.Border
                {
                    Width = 90, Height = 16, CornerRadius = new CornerRadius(3),
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(c)!
                };
                color.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = swatch, Tag = c });
            }
            color.SelectedItem = color.Items.Cast<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(x => string.Equals((string)x.Tag, current, StringComparison.OrdinalIgnoreCase)) ?? color.Items[0];
            row.Children.Add(name);
            row.Children.Add(color);
            panel.Children.Add(row);
            edits.Add((labels[i], name, color));
        }
        if (!ShowDialogWith(panel, "Labels")) return;
        foreach (var (old, nameBox, colorBox) in edits)
        {
            var newName = Stagewatch.Core.Labels.Parse(nameBox.Text).FirstOrDefault() ?? "";
            foreach (var row in _rows)
            {
                var idx = row.Labels.FindIndex(l => string.Equals(l, old, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) continue;
                if (newName.Length == 0) row.Labels.RemoveAt(idx); else row.Labels[idx] = newName;
                row.LabelsText = string.Join(", ", row.Labels);
            }
            foreach (var k in _labelColors.Keys.Where(k => string.Equals(k, old, StringComparison.OrdinalIgnoreCase)).ToList()) _labelColors.Remove(k);
            if (newName.Length > 0) _labelColors[newName] = (string)((System.Windows.Controls.ComboBoxItem)colorBox.SelectedItem).Tag;
        }
    }

    private void OnGroupTargets(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var names = RundownGroups.Spans(_rows.Select(r => new RundownItem { Group = r.Group }).ToList()).Select(g => g.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0)
        {
            MessageBox.Show(this, "Give items a Group first (e.g. Morning, Panels, Awards). Consecutive items with the same group form one.", "Group targets");
            return;
        }
        var boxes = new Dictionary<string, System.Windows.Controls.TextBox>();
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Target length per group (blank = none). E.g. 2:00:00 or 45:00.", Opacity = 0.7, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var n in names)
        {
            var row = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var box = new System.Windows.Controls.TextBox { Width = 110, Text = _groupTargets.TryGetValue(n, out var t) && t > 0 ? TimerTick.FormatSeconds(t) : "" };
            System.Windows.Controls.DockPanel.SetDock(box, System.Windows.Controls.Dock.Right);
            row.Children.Add(box);
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = n, VerticalAlignment = VerticalAlignment.Center, MinWidth = 220 });
            panel.Children.Add(row);
            boxes[n] = box;
        }
        if (!ShowDialogWith(panel, "Group targets")) return;
        foreach (var (n, box) in boxes)
        {
            if (string.IsNullOrWhiteSpace(box.Text)) _groupTargets.Remove(n);
            else if (RundownCsv.TryParseDuration(box.Text, out var ts) && ts.TotalSeconds > 0) _groupTargets[n] = (int)ts.TotalSeconds;
        }
        UpdateSummary();
    }

    /// <summary>Plays a chime on the operator's device (the editor's Test buttons).</summary>
    public Action<string>? PreviewChime { get; init; }

    private void OnChimes(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Row row }) return;
        var choices = new List<(string Spec, string Label)>
        {
            ("", "Show's alert"), ("off", "None"), ("builtin:start", "Built-in: start"), ("builtin:warning", "Built-in: warning (2 beeps)"),
            ("builtin:wrapup", "Built-in: wrap-up (3 beeps)"), ("builtin:zero", "Built-in: time's up"),
        };
        choices.AddRange(_store.Settings.Sounds.Select(s => ("sound:" + s.Id, "♪ " + s.Name)));
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20), MinWidth = 420 };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{row.Number}. {row.Title}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var combos = new List<System.Windows.Controls.ComboBox>();
        foreach (var (label, current) in new[] { ("When it starts", row.Chimes.Start), ("At warning", row.Chimes.Warning), ("At wrap-up", row.Chimes.WrapUp), ("At zero", row.Chimes.Zero) })
        {
            var line = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var test = new System.Windows.Controls.Button { Content = "Test", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0), MinWidth = 0 };
            System.Windows.Controls.DockPanel.SetDock(test, System.Windows.Controls.Dock.Right);
            var combo = new System.Windows.Controls.ComboBox { Width = 230 };
            foreach (var (spec, text) in choices) combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = text, Tag = spec });
            combo.SelectedItem = combo.Items.Cast<System.Windows.Controls.ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == current) ?? combo.Items[0];
            test.Click += (_, _) => { if (combo.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string spec } && spec.Length > 0) PreviewChime?.Invoke(spec); };
            System.Windows.Controls.DockPanel.SetDock(combo, System.Windows.Controls.Dock.Right);
            line.Children.Add(test);
            line.Children.Add(combo);
            line.Children.Add(new System.Windows.Controls.TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(line);
            combos.Add(combo);
        }
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Add your own sounds in Settings → Timer → Chime library. Screens opened with ?sound=1 play them too.", Opacity = 0.5, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        if (!ShowDialogWith(panel, "Chimes")) return;
        string Spec(int i) => (combos[i].SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "";
        row.SetChimes(new ItemChimes { Start = Spec(0), Warning = Spec(1), WrapUp = Spec(2), Zero = Spec(3) });
    }

    private void OnDeptNotes(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: Row row }) return;
        var depts = _store.Settings.Signals.Departments.Concat(row.DeptNotes.Keys)
            .Select(d => d.Trim()).Where(d => d.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var boxes = new Dictionary<string, System.Windows.Controls.TextBox>();
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20), MinWidth = 460 };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{row.Number}. {row.Title}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var d in depts)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = d, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 2) });
            var box = new System.Windows.Controls.TextBox
            {
                Text = row.DeptNotes.FirstOrDefault(kv => string.Equals(kv.Key.Trim(), d, StringComparison.OrdinalIgnoreCase)).Value ?? "",
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 44, MaxLength = 1000
            };
            panel.Children.Add(box);
            boxes[d] = box;
        }
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Departments come from Settings → Cues. Each department can also edit its own notes from its crew page.",
            Opacity = 0.5, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 460
        });
        if (!ShowDialogWith(new System.Windows.Controls.ScrollViewer { Content = panel, MaxHeight = 560, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto }, "Department notes")) return;
        var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (d, box) in boxes) if (!string.IsNullOrWhiteSpace(box.Text)) notes[d] = box.Text.Trim();
        row.SetDeptNotes(notes);
    }

    /// <summary>A small OK / Cancel dialog around some content, in the app's style.</summary>
    private bool ShowDialogWith(UIElement content, string title)
    {
        var ok = new System.Windows.Controls.Button { Content = "OK", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", MinWidth = 90, IsCancel = true };
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(buttons, System.Windows.Controls.Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(content);
        var win = new Window
        {
            Title = title, Owner = this, Content = root, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = (System.Windows.Media.Brush)FindResource("BgDeepBrush"),
            Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush")
        };
        ok.Click += (_, _) => win.DialogResult = true;
        return win.ShowDialog() == true;
    }

    private int SelectedIndex => Grid.SelectedItem is Row r ? _rows.IndexOf(r) : -1;

    private void Select(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        Grid.SelectedItem = _rows[index];
        Grid.ScrollIntoView(_rows[index]);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var at = SelectedIndex >= 0 ? SelectedIndex + 1 : _rows.Count;
        var row = new Row { Title = $"Item {_rows.Count + 1}", DurationText = "10:00" };
        row.PropertyChanged += (_, _) => UpdateSummary();
        _rows.Insert(at, row);
        Select(at);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        var i = SelectedIndex;
        if (i < 0) return;
        Grid.CommitEdit();
        _rows.RemoveAt(i);
        Select(Math.Min(i, _rows.Count - 1));
    }

    private void OnUp(object sender, RoutedEventArgs e) => MoveSelected(-1);
    private void OnDown(object sender, RoutedEventArgs e) => MoveSelected(+1);

    private void MoveSelected(int delta)
    {
        var i = SelectedIndex;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _rows.Count) return;
        Grid.CommitEdit();
        _rows.Move(i, j);
        Select(j);
    }

    private void OnPaste(object sender, RoutedEventArgs e)
    {
        string text;
        try { text = Clipboard.GetText(); }
        catch { text = ""; }
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "The clipboard is empty. Copy rows from your run sheet first (Title, Duration, Speaker, Message).",
                "Paste", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        AddParsed(RundownCsv.Parse(text, _store.Settings.Signals.Departments), "Pasted");
    }

    /// <summary>AI import: any agenda text → draft rows, added to the grid for review (nothing is kept until OK).</summary>
    private async void OnAiImport(object sender, RoutedEventArgs e)
    {
        if (_draftAgenda is null)
        {
            MessageBox.Show(this, "The copilot needs Production and an API key (Settings → Integrations → Producer assistant → Also ask Claude).",
                "From agenda text", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string initial = "";
        try { initial = Clipboard.GetText(); } catch { }
        var text = Help.TextReview.Show(this, "Agenda text", "Paste the agenda (email, PDF text, notes). It is sent to Anthropic to draft the rows.", initial);
        if (string.IsNullOrWhiteSpace(text)) return;
        AiImport.IsEnabled = false;
        AiImport.Content = "Drafting…";
        try
        {
            var draft = await _draftAgenda(text);
            var items = draft.Items.Select(Stagewatch.Integrations.Copilot.ToRundownItem).ToList();
            var warnings = draft.Notes.ToList();
            if (items.Count > 0) warnings.Insert(0, "Drafted by the copilot — check every row before pressing OK.");
            AddParsed(new RundownCsv.Result(items, warnings), "From agenda text");
        }
        finally
        {
            AiImport.IsEnabled = true;
            AiImport.Content = "\u2728 From any agenda text\u2026";
        }
    }

    private static readonly System.Net.Http.HttpClient SheetHttp = new() { Timeout = TimeSpan.FromSeconds(20) };
    private string? _sheetUrl;

    /// <summary>Google Sheets / CSV link → rows in the grid (reviewed before OK). Update keeps the day's work on matched items.</summary>
    private async void OnSheet(object sender, RoutedEventArgs e)
    {
        var link = new System.Windows.Controls.TextBox { Text = _sheetUrl ?? "", Width = 520, Margin = new Thickness(0, 4, 0, 10) };
        var update = new System.Windows.Controls.RadioButton { Content = "Update — keep live trims, notes and department notes on items with the same title", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
        var replace = new System.Windows.Controls.RadioButton { Content = "Replace — take the sheet exactly as it is", Margin = new Thickness(0, 2, 0, 2) };
        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Link to the sheet", Opacity = 0.75 });
        panel.Children.Add(link);
        panel.Children.Add(update);
        panel.Children.Add(replace);
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = "Share the sheet as \"Anyone with the link can view\" (or File → Share → Publish to web). Columns: Title, Duration, Speaker, Message, Kind, Fixed start, Min, Curfew, Group, and one per department (e.g. Lighting).",
            Opacity = 0.55, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 10, 0, 0)
        });
        if (!ShowDialogWith(panel, "From Google Sheet")) return;
        var url = SheetImport.CsvUrl(link.Text);
        if (url is null) { MessageBox.Show(this, "That isn't an https link to a sheet or a CSV file.", "From Google Sheet"); return; }

        SheetButton.IsEnabled = false;
        SheetButton.Content = "Fetching…";
        string text;
        try
        {
            using var res = await SheetHttp.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"the sheet answered {(int)res.StatusCode}");
            if (res.Content.Headers.ContentLength is > 5_000_000) throw new InvalidOperationException("the sheet is too big (over 5 MB)");
            text = await res.Content.ReadAsStringAsync();
            if (text.Length > 5_000_000) throw new InvalidOperationException("the sheet is too big (over 5 MB)");
            if (text.TrimStart().StartsWith("<", StringComparison.Ordinal))
                throw new InvalidOperationException("Google returned a sign-in page — share the sheet as \"Anyone with the link can view\", or publish it to the web");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            MessageBox.Show(this, "Couldn't read the sheet: " + ex.Message, "From Google Sheet", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            SheetButton.IsEnabled = true;
            SheetButton.Content = "From Google Sheet\u2026";
        }

        var parsed = RundownCsv.Parse(text, _store.Settings.Signals.Departments);
        if (parsed.Items.Count == 0) { MessageBox.Show(this, "No rows found in the sheet.", "From Google Sheet"); return; }
        _sheetUrl = link.Text.Trim();
        Grid.CommitEdit();
        List<RundownItem> items;
        string summary;
        if (update.IsChecked == true)
        {
            var merged = SheetImport.Update(_rows.Select(r => r.ToItem()).ToList(), parsed.Items);
            items = merged.Items;
            summary = $"{merged.Updated} updated, {merged.Added} added"
                      + (merged.Dropped.Count > 0 ? $", {merged.Dropped.Count} not in the sheet (removed): {string.Join(", ", merged.Dropped.Take(8))}{(merged.Dropped.Count > 8 ? "…" : "")}" : "");
        }
        else
        {
            items = parsed.Items;
            summary = $"{items.Count} items from the sheet";
        }
        _rows.Clear();
        foreach (var item in items)
        {
            var row = Row.From(item);
            row.PropertyChanged += (_, _) => UpdateSummary();
            _rows.Add(row);
        }
        UpdateSummary();
        var warn = parsed.Warnings.Count > 0 ? "\n\n" + string.Join("\n", parsed.Warnings.Take(10)) : "";
        MessageBox.Show(this, summary + ". Check the rows, then press OK to keep them." + warn, "From Google Sheet", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "CSV or text (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            AddParsed(RundownCsv.Parse(File.ReadAllText(dlg.FileName), _store.Settings.Signals.Departments), "Imported");
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddParsed(RundownCsv.Result result, string verb)
    {
        if (result.Items.Count == 0)
        {
            MessageBox.Show(this, "No rows found.", verb, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var replace = _rows.Count > 0 && MessageBox.Show(this,
            $"{result.Items.Count} items found.\n\nYes = replace the current running order\nNo = add them to the end",
            verb, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        Grid.CommitEdit();
        if (replace) _rows.Clear();
        foreach (var item in result.Items)
        {
            var row = Row.From(item);
            row.PropertyChanged += (_, _) => UpdateSummary();
            _rows.Add(row);
        }
        if (result.Warnings.Count > 0)
            MessageBox.Show(this, string.Join("\n", result.Warnings.Take(12)) + (result.Warnings.Count > 12 ? "\n…" : ""),
                $"{verb} with warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit();
        var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "running-order.csv" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, RundownCsv.Export(_rows.Select(r => r.ToItem())), new System.Text.UTF8Encoding(true));
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0) return;
        if (MessageBox.Show(this, "Remove every item from the running order?", "Clear all",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _rows.Clear();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        var items = _rows.Select(r => r.ToItem()).ToList();
        var zone = (ZoneCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
        _store.Update(s =>
        {
            // Department notes: a three-way merge per department against what the editor opened with. Notes changed here
            // (by hand or by a sheet) win; notes crew edited live while the editor was open win where this editor didn't
            // touch them. Rows rebuilt by a sheet update keep their ids, so this holds after re-importing too.
            var r = Target(s);
            var live = r.Items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First().DeptNotes);
            foreach (var it in items)
                it.DeptNotes = SheetImport.MergeDeptNotes(_baselineNotes.GetValueOrDefault(it.Id), it.DeptNotes, live.GetValueOrDefault(it.Id));
            r.Items = items;
            r.TimeZoneId = string.IsNullOrEmpty(zone) ? null : zone;
            r.GroupTargets = new Dictionary<string, int>(_groupTargets, StringComparer.OrdinalIgnoreCase);
            r.SheetUrl = string.IsNullOrWhiteSpace(_sheetUrl) ? null : _sheetUrl;
            r.LabelColors = new Dictionary<string, string>(_labelColors, StringComparer.OrdinalIgnoreCase);
            Stagewatch.Core.Labels.AssignColors(r);
            r.Loop = int.TryParse((LoopCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string, out var loop) ? loop : 0;
        });
        _store.SaveNow();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Mutable row so DataGrid two-way bindings work; converted to RundownItem on save.</summary>
    public sealed class Row : INotifyPropertyChanged
    {
        private int _number;
        private string _durationText = "00:00";

        public int Number { get => _number; set { _number = value; Notify(); } }
        public string Title { get; set; } = "";
        public string? Speaker { get; set; }
        public string? Message { get; set; }
        public bool AutoAdvance { get; set; }
        public SegmentKind Kind { get; set; } = SegmentKind.Talk;
        /// <summary>Published start: blank = follows the previous item; a time holds it to that time.</summary>
        public string FixedStart { get => _fixedStart; set { _fixedStart = value; Notify(); } }
        /// <summary>Shortest it may be cut to (blank = not compressible).</summary>
        public string MinText { get; set; } = "";
        public string HardOut { get => _hardOut; set { _hardOut = value; Notify(); } }
        public string CueLadderId { get; set; } = "";
        public string? Notes { get; set; }
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string? Group { get; set; }
        public List<string> Labels { get; set; } = new();
        public ItemChimes Chimes { get; set; } = new();
        public string ChimesLabel => Chimes.IsDefault ? "Show" : "Own ♪";
        public void SetChimes(ItemChimes c) { Chimes = c; Notify(nameof(ChimesLabel)); }
        /// <summary>"VT, GFX" in the grid; tidied into a list.</summary>
        public string LabelsText
        {
            get => string.Join(", ", Labels);
            set { Labels = Stagewatch.Core.Labels.Parse(value); Notify(); }
        }
        public Dictionary<string, string> DeptNotes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>The item's playout cues, carried through the editor whole (a row keeps them until it is deleted).</summary>
        public List<PlayoutCue> Playout { get; private set; } = new();
        /// <summary>True once notes were changed in this editor (otherwise notes made live on crew pages are kept on save).</summary>
        public bool DeptNotesEdited { get; private set; }
        public string DeptNotesLabel => DeptNotes.Count == 0 ? "Add…" : $"{DeptNotes.Count} note{(DeptNotes.Count == 1 ? "" : "s")}";
        public void SetDeptNotes(Dictionary<string, string> notes) { DeptNotes = notes; DeptNotesEdited = true; Notify(nameof(DeptNotesLabel)); }
        /// <summary>Live trims made during the show — kept so the report can show plan vs adjusted vs actual.</summary>
        public int AdjustSeconds { get; set; }
        private string _fixedStart = "";
        private string _hardOut = "";

        public bool TimesValid => ShowClock.IsValidTimeText(FixedStart) && ShowClock.IsValidTimeText(HardOut);

        public string DurationText
        {
            get => _durationText;
            set { _durationText = value; Notify(); }
        }

        public bool DurationValid => RundownCsv.TryParseDuration(DurationText, out _);
        public int Seconds => RundownCsv.TryParseDuration(DurationText, out var ts) ? (int)Math.Min(ts.TotalSeconds, int.MaxValue) : 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public static Row From(RundownItem i) => new()
        {
            Title = i.Title,
            Speaker = i.Speaker,
            Message = i.Message,
            AutoAdvance = i.AutoAdvance,
            DurationText = TimerTick.FormatSeconds(i.DurationSeconds),
            Kind = i.Kind,
            FixedStart = i.StartMode == StartMode.Fixed ? i.FixedStart ?? "" : "",
            MinText = i.MinDurationSeconds is { } m ? TimerTick.FormatSeconds(m) : "",
            HardOut = i.HardOut ?? "",
            CueLadderId = i.CueLadderId ?? "",
            Notes = i.Notes,
            AdjustSeconds = i.AdjustSeconds,
            Id = i.Id,
            Group = i.Group,
            Labels = i.Labels.ToList(),
            Chimes = i.Chimes.Clone(),
            DeptNotes = new Dictionary<string, string>(i.DeptNotes, StringComparer.OrdinalIgnoreCase),
            Playout = i.Playout.Select(c => c.Clone()).ToList(),
        };

        public RundownItem ToItem() => new()
        {
            Title = string.IsNullOrWhiteSpace(Title) ? $"Item {Number}" : Title.Trim(),
            Speaker = string.IsNullOrWhiteSpace(Speaker) ? null : Speaker.Trim(),
            Message = string.IsNullOrWhiteSpace(Message) ? null : Message.Trim(),
            AutoAdvance = AutoAdvance,
            DurationSeconds = Seconds,
            Kind = Kind,
            StartMode = string.IsNullOrWhiteSpace(FixedStart) || !ShowClock.IsValidTimeText(FixedStart) ? StartMode.Sequential : StartMode.Fixed,
            FixedStart = string.IsNullOrWhiteSpace(FixedStart) || !ShowClock.IsValidTimeText(FixedStart) ? null : FixedStart.Trim(),
            MinDurationSeconds = RundownCsv.TryParseDuration(MinText, out var min) && !string.IsNullOrWhiteSpace(MinText) ? (int)min.TotalSeconds : null,
            HardOut = string.IsNullOrWhiteSpace(HardOut) || !ShowClock.IsValidTimeText(HardOut) ? null : HardOut.Trim(),
            CueLadderId = string.IsNullOrWhiteSpace(CueLadderId) ? null : CueLadderId,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            AdjustSeconds = AdjustSeconds,
            Id = Id,
            Group = string.IsNullOrWhiteSpace(Group) ? null : Group.Trim(),
            Labels = Labels.ToList(),
            Chimes = Chimes.Clone(),
            DeptNotes = new Dictionary<string, string>(DeptNotes, StringComparer.OrdinalIgnoreCase),
            Playout = Playout.Select(c => c.Clone()).ToList(),
        };
    }
}
