using System.Globalization;
using System.Windows.Data;
using Stagewatch.Core;

namespace Stagewatch.App;

public sealed class ModeRadio : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
        => value is TimerMode m && parameter is string s && Enum.TryParse<TimerMode>(s, out var target)
            ? m == target : false;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class SecondsToHms : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not int s) return "";
        if (s < 0) return "count up";
        var ts = TimeSpan.FromSeconds(s);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class HexToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var hex = value as string;
        if (string.IsNullOrEmpty(hex)) return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x9C, 0xA3, 0xAF));
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            return new System.Windows.Media.SolidColorBrush(color);
        }
        catch { return System.Windows.Media.Brushes.Gray; }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Visible when the bound text isn't empty: status chips that only appear when they have something to say.</summary>
public sealed class NonEmptyToVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is string s && s.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>True when the bound text isn't empty (enable a button only when there's something to act on).</summary>
public sealed class NonEmptyToBool : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is string s && s.Length > 0;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class InverseBoolToVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class OutputModeLabel : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is OutputMode m ? m switch
        {
            OutputMode.Closed          => "Closed",
            OutputMode.FullScreen      => "Full screen",
            OutputMode.KeyedFullScreen => "Keyed full screen",
            OutputMode.FloatingMinimal => "Floating mini",
            _ => m.ToString()
        } : "";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
