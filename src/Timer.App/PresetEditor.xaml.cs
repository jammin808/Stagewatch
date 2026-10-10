using System.Windows;
using System.Windows.Controls;
using Stagewatch.Core;

namespace Stagewatch.App;

public partial class PresetEditor : Window
{
    public TimerPreset? Result { get; private set; }
    private readonly TimerPreset _original;

    public PresetEditor(TimerPreset preset)
    {
        InitializeComponent();
        Help.ContextHelp.SetTopic(this, "presets");
        Help.ContextHelp.Attach(this);
        _original = preset;
        SlotText.Text = $"Slot {preset.Slot}  ·  shortcut key: {ShortcutFor(preset.Slot)}";
        LabelBox.Text = preset.Label;
        // Negative duration = count up (see TimerPreset).
        CountUpBox.IsChecked = preset.DurationSeconds < 0;
        DurationBox.Text = preset.DurationSeconds < 0 ? "" : FormatDuration(preset.DurationSeconds);
        DurationBox.IsEnabled = preset.DurationSeconds >= 0;
        MessageInputBox.Text = preset.Message ?? "";

        foreach (ComboBoxItem item in ColorBox.Items)
            if ((item.Tag as string) == (preset.ColorHex ?? "")) ColorBox.SelectedItem = item;
        if (ColorBox.SelectedItem is null) ColorBox.SelectedIndex = 0;

        LabelBox.Focus();
        LabelBox.SelectAll();
    }

    private static string FormatDuration(int seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    private static string ShortcutFor(int slot) => slot switch
    {
        >= 1 and <= 9 => slot.ToString(),
        10 => "0",
        11 => "-",
        12 => "=",
        _ => "—"
    };

    private void OnCountUpChanged(object sender, RoutedEventArgs e)
    {
        DurationBox.IsEnabled = CountUpBox.IsChecked != true;
        DurationError.Visibility = Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var countUp = CountUpBox.IsChecked == true;
        var ts = TimeSpan.Zero;
        if (!countUp && (!TryParseTime(DurationBox.Text.Trim(), out ts) || ts <= TimeSpan.Zero))
        {
            DurationError.Text = "Enter a duration such as 5:00, 1:30:00 or 90 — or tick Count up.";
            DurationError.Visibility = Visibility.Visible;
            DurationBox.Focus();
            DurationBox.SelectAll();
            return;
        }

        var color = (ColorBox.SelectedItem as ComboBoxItem)?.Tag as string;
        Result = new TimerPreset
        {
            Slot = _original.Slot,
            Label = string.IsNullOrWhiteSpace(LabelBox.Text) ? _original.Label : LabelBox.Text.Trim(),
            DurationSeconds = countUp ? -1 : (int)ts.TotalSeconds,
            Message = string.IsNullOrWhiteSpace(MessageInputBox.Text) ? null : MessageInputBox.Text.Trim(),
            ColorHex = string.IsNullOrEmpty(color) ? null : color
        };
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

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
