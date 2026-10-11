using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>A panel torn out of the main window into its own window, and where it sits (device-independent pixels).</summary>
public sealed class FloatingPanel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("left")] public double Left { get; set; }
    [JsonPropertyName("top")] public double Top { get; set; }
    [JsonPropertyName("width")] public double Width { get; set; } = 420;
    [JsonPropertyName("height")] public double Height { get; set; } = 640;
}

/// <summary>
/// How the operator arranged the main window's panels: their order in the side column, which are torn out into their
/// own windows (another monitor, beside the timer), and how wide the side column is.
/// </summary>
public sealed class PanelLayoutSettings
{
    [JsonPropertyName("order")] public List<string> Order { get; set; } = new(PanelLayout.Default);
    [JsonPropertyName("floating")] public List<FloatingPanel> Floating { get; set; } = new();
    [JsonPropertyName("sideWidth")] public double SideWidth { get; set; } = PanelLayout.DefaultSideWidth;
}

/// <summary>
/// The rules of the panel layout, with no UI: every panel is always somewhere (docked or floating, never lost), the
/// order holds each panel once, and sizes stay usable. The window code only moves controls to match.
/// </summary>
public static class PanelLayout
{
    /// <summary>The panels, in their default order.</summary>
    public static readonly IReadOnlyList<string> Default = new[] { "presets", "running", "cues", "producer", "holding", "timers" };

    public const double DefaultSideWidth = 360, MinSideWidth = 300, MaxSideWidth = 760;
    public const double MinPanelWidth = 280, MinPanelHeight = 240;

    /// <summary>Repair a layout read from disk (or edited by hand): known panels only, each once, sizes in range.</summary>
    public static void Normalize(PanelLayoutSettings p)
    {
        p.Order ??= new List<string>();
        p.Floating ??= new List<FloatingPanel>();
        var order = p.Order.Where(id => id is not null && Default.Contains(id)).Distinct().ToList();
        // A missing panel (new in this version, or lost from a damaged file) goes at the end: the operator's order stays.
        foreach (var id in Default)
            if (!order.Contains(id)) order.Add(id);
        p.Order = order;
        p.Floating = p.Floating.Where(f => f is not null && Default.Contains(f.Id)).GroupBy(f => f.Id).Select(g => g.Last()).ToList();
        foreach (var f in p.Floating)
        {
            f.Width = Math.Max(MinPanelWidth, double.IsFinite(f.Width) ? f.Width : 420);
            f.Height = Math.Max(MinPanelHeight, double.IsFinite(f.Height) ? f.Height : 640);
            if (!double.IsFinite(f.Left)) f.Left = 0;
            if (!double.IsFinite(f.Top)) f.Top = 0;
        }
        p.SideWidth = double.IsFinite(p.SideWidth) ? Math.Clamp(p.SideWidth, MinSideWidth, MaxSideWidth) : DefaultSideWidth;
    }

    public static bool IsFloating(PanelLayoutSettings p, string id) => p.Floating.Any(f => f.Id == id);

    /// <summary>The panels in the side column, in order.</summary>
    public static IReadOnlyList<string> Docked(PanelLayoutSettings p) => p.Order.Where(id => !IsFloating(p, id)).ToList();

    /// <summary>
    /// Move a docked panel to <paramref name="dockedIndex"/> among the docked panels (floating ones keep their place in
    /// the overall order, so docking them again puts them back where they were).
    /// </summary>
    public static void Move(PanelLayoutSettings p, string id, int dockedIndex)
    {
        if (!p.Order.Contains(id)) return;
        var docked = Docked(p).Where(x => x != id).ToList();
        dockedIndex = Math.Clamp(dockedIndex, 0, docked.Count);
        p.Order.Remove(id);
        if (dockedIndex >= docked.Count)
        {
            var last = docked.LastOrDefault();
            p.Order.Insert(last is null ? p.Order.Count : p.Order.IndexOf(last) + 1, id);
        }
        else p.Order.Insert(p.Order.IndexOf(docked[dockedIndex]), id);
    }

    /// <summary>Tear a panel out into its own window at the given bounds.</summary>
    public static void Float(PanelLayoutSettings p, string id, double left, double top, double width, double height)
    {
        if (!Default.Contains(id)) return;
        p.Floating.RemoveAll(f => f.Id == id);
        p.Floating.Add(new FloatingPanel
        {
            Id = id, Left = left, Top = top,
            Width = Math.Max(MinPanelWidth, width), Height = Math.Max(MinPanelHeight, height),
        });
    }

    /// <summary>A floating panel moved or was resized.</summary>
    public static void Moved(PanelLayoutSettings p, string id, double left, double top, double width, double height)
    {
        if (p.Floating.FirstOrDefault(f => f.Id == id) is not { } f) return;
        f.Left = left; f.Top = top;
        f.Width = Math.Max(MinPanelWidth, width); f.Height = Math.Max(MinPanelHeight, height);
    }

    /// <summary>Put a floating panel back in the side column, at <paramref name="dockedIndex"/> (null = where it was).</summary>
    public static void Dock(PanelLayoutSettings p, string id, int? dockedIndex = null)
    {
        p.Floating.RemoveAll(f => f.Id == id);
        if (dockedIndex is { } i) Move(p, id, i);
    }

    /// <summary>Everything back where it started.</summary>
    public static void Reset(PanelLayoutSettings p)
    {
        p.Order = new List<string>(Default);
        p.Floating.Clear();
        p.SideWidth = DefaultSideWidth;
    }
}
