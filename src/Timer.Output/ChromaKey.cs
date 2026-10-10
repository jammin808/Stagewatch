using System.Windows.Media;
using Stagewatch.Core;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;
using Brush = System.Windows.Media.Brush;

namespace Stagewatch.Output;

public static class ChromaKey
{
    /// <summary>Standard chroma key colors. Pure RGB to maximise key cleanliness.</summary>
    public static Color ToColor(KeyColor key) => key switch
    {
        KeyColor.Blue    => Color.FromRgb(0x00, 0x00, 0xFF),
        KeyColor.Green   => Color.FromRgb(0x00, 0xFF, 0x00),
        KeyColor.Magenta => Color.FromRgb(0xFF, 0x00, 0xFF),
        KeyColor.Black   => Color.FromRgb(0x00, 0x00, 0x00),
        _ => Colors.Black
    };

    public static Brush ToBrush(KeyColor key) => new SolidColorBrush(ToColor(key));
}
