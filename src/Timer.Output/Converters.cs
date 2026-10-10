using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Stagewatch.Core;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Stagewatch.Output;

public sealed class UrgencyToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is MessageUrgency u ? u switch
        {
            MessageUrgency.Info     => new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
            MessageUrgency.Warning  => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            MessageUrgency.Critical => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            _ => Brushes.White
        } : Brushes.White;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class StateToTimerBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is TimerState s && s == TimerState.Expired
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))
            : Brushes.White;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class BoolToVis : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
