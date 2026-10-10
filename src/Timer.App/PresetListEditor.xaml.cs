using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Stagewatch.Core;

namespace Stagewatch.App;

public partial class PresetListEditor : Window
{
    private readonly SettingsStore _store;
    private readonly ObservableCollection<EditableRow> _rows = new();

    public PresetListEditor(SettingsStore store)
    {
        InitializeComponent();
        Help.ContextHelp.SetTopic(this, "presets");
        Help.ContextHelp.Attach(this);
        _store = store;
        foreach (var p in store.Settings.Presets)
            _rows.Add(EditableRow.From(p));
        Grid.ItemsSource = _rows;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        Grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        var updated = _rows.Select(r => r.ToPreset()).OrderBy(p => p.Slot).ToList();
        _store.Update(s => s.Presets = updated);
        _store.SaveNow();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnReset(object sender, RoutedEventArgs e)
    {
        _rows.Clear();
        foreach (var p in TimerPreset.DefaultDeck())
            _rows.Add(EditableRow.From(p));
    }

    /// <summary>Mutable row VM so DataGrid two-way bindings work cleanly.</summary>
    public sealed class EditableRow : INotifyPropertyChanged
    {
        public int Slot { get; set; }
        public string Label { get; set; } = "";
        public string? Message { get; set; }
        public string? ColorHex { get; set; }

        private string _durationText = "00:00";
        public string DurationText
        {
            get => _durationText;
            set
            {
                if (_durationText == value) return;
                _durationText = value;
                Notify();
                Notify(nameof(DurationSeconds)); // the read-only cell shows this
            }
        }

        public int DurationSeconds
        {
            get => IsCountUp(DurationText) ? -1 : TryParseTime(DurationText, out var ts) ? (int)ts.TotalSeconds : 0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public static EditableRow From(TimerPreset p) => new()
        {
            Slot = p.Slot,
            Label = p.Label,
            DurationText = p.DurationSeconds < 0 ? "count up" : FormatDuration(p.DurationSeconds),
            Message = p.Message,
            ColorHex = p.ColorHex
        };

        public TimerPreset ToPreset() => new()
        {
            Slot = Slot,
            Label = string.IsNullOrWhiteSpace(Label) ? $"Preset {Slot}" : Label.Trim(),
            DurationSeconds = DurationSeconds,
            Message = string.IsNullOrWhiteSpace(Message) ? null : Message.Trim(),
            ColorHex = string.IsNullOrWhiteSpace(ColorHex) ? null : ColorHex
        };

        /// <summary>Negative preset duration = count up; type "count up" (or "up") in the grid.</summary>
        private static bool IsCountUp(string s) => s.Trim().ToLowerInvariant() is "count up" or "countup" or "up";

        private static string FormatDuration(int seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private static bool TryParseTime(string s, out TimeSpan ts)
        {
            ts = default;
            var parts = s.Split(':');
            try
            {
                if (parts.Length == 3) { ts = new TimeSpan(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])); return true; }
                if (parts.Length == 2) { ts = new TimeSpan(0, int.Parse(parts[0]), int.Parse(parts[1])); return true; }
                if (int.TryParse(s, out var secs)) { ts = TimeSpan.FromSeconds(secs); return true; }
            }
            catch { }
            return false;
        }
    }
}
