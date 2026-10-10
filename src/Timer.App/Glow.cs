using System.Windows;

namespace Stagewatch.App;

/// <summary>The neon colour a button glows on hover (set in styles; <c>Tag</c> is left free for data).</summary>
public static class Glow
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.RegisterAttached(
        "Color", typeof(Color), typeof(Glow), new PropertyMetadata(Color.FromRgb(0x22, 0xD3, 0xEE)));
    public static Color GetColor(DependencyObject d) => (Color)d.GetValue(ColorProperty);
    public static void SetColor(DependencyObject d, Color value) => d.SetValue(ColorProperty, value);
}
