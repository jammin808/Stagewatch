using System.Windows.Threading;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Designed screens: layouts made in the browser designer (/designer), shown at /layout?name=…. The web server reads
/// them from an immutable copy; saves are checked on the web thread and stored on the UI thread. The copilot can draft
/// a layout from a sentence — the designer previews it and nothing is kept until the operator saves.
/// </summary>
public sealed partial class MainViewModel
{
    private volatile IReadOnlyDictionary<string, string> _layouts = new Dictionary<string, string>();

    /// <summary>The designer on this PC, logged in with the PIN (null when browser pages are off).</summary>
    public string? DesignerAddress()
        => _web is null ? null : $"http://localhost:{_web.Port}/designer" + (string.IsNullOrEmpty(Settings.Remote.Pin) ? "" : "?pin=" + Uri.EscapeDataString(Settings.Remote.Pin));

    public string? LayoutJson(string name) => _layouts.TryGetValue(name, out var j) ? j : null;

    public IReadOnlyList<string> LayoutNames => _layouts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

    public string? SaveLayout(string json)
    {
        if (ScreenLayouts.FromJson(json, out var error) is not { } layout) return error;
        if (!_layouts.ContainsKey(layout.Name) && _layouts.Count >= ScreenLayouts.MaxLayouts)
            return $"There are already {ScreenLayouts.MaxLayouts} layouts — delete one first.";
        _ui.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            var isNew = Settings.Remote.Layouts.All(l => !l.Name.Equals(layout.Name, StringComparison.OrdinalIgnoreCase));
            _store.Update(s =>
            {
                s.Remote.Layouts.RemoveAll(l => l.Name.Equals(layout.Name, StringComparison.OrdinalIgnoreCase));
                s.Remote.Layouts.Add(layout);
            });
            RefreshScreenCopies();
            _configVersion++;   // screens showing it reload it
            MarkDirty();
            if (isNew) _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, $"Screen layout made: {layout.Name}");
        });
        // Visible to the next request at once, before the UI thread stores it.
        _layouts = new Dictionary<string, string>(_layouts, StringComparer.OrdinalIgnoreCase) { [layout.Name] = ScreenLayouts.ToJson(layout) };
        return null;
    }

    public void DeleteLayout(string name)
    {
        _ui.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            _store.Update(s => s.Remote.Layouts.RemoveAll(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            RefreshScreenCopies();
            _configVersion++;
            MarkDirty();
        });
    }

    public async Task<(string? Json, IReadOnlyList<string> Notes)> DesignLayoutAsync(string request, string? currentJson)
    {
        // The copilot client and the holding image list belong to the UI thread.
        var (copilot, images) = await _ui.InvokeAsync(() => (CopilotClient(),
            (IReadOnlyList<(string Id, string Name)>)Settings.Holding.Images.Select(i => (i.Id, i.Name)).ToList()));
        if (copilot is null) return (null, new[] { CopilotUnavailable ?? "The copilot isn't available." });
        var current = currentJson is null ? null : ScreenLayouts.FromJson(currentJson, out _);
        var (layout, notes) = await Task.Run(() => copilot.DesignLayoutAsync(request, current, images));
        _ = _ui.BeginInvoke(() => _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, "Copilot drafted a screen layout: " + Trunc(request, 160)));
        return (layout is null ? null : ScreenLayouts.ToJson(layout), notes);
    }
}
