using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// Runs "window …" commands: applies the rules (<see cref="ScreenWindows.Apply"/>), keeps the list readers see, has it
/// saved, and tells the windows. One owner thread calls <see cref="Run"/> (the show thread in the app, the rig's loop,
/// a test); any thread reads <see cref="Windows"/>, an immutable list swapped whole on each change.
/// </summary>
public sealed class WindowDirector
{
    private volatile IReadOnlyList<ScreenWindow> _windows;
    private readonly Action<IReadOnlyList<ScreenWindow>> _save;
    private readonly Func<string> _newToken;
    private readonly Func<string> _newId;

    /// <param name="initial">The windows as saved (copied: the caller's objects are never shared with readers).</param>
    /// <param name="save">Owner thread: keep the new list (the app writes it to its settings).</param>
    public WindowDirector(IEnumerable<ScreenWindow> initial, Action<IReadOnlyList<ScreenWindow>> save, Func<string>? newToken = null, Func<string>? newId = null)
    {
        _windows = initial.Where(w => ScreenWindows.ValidName(w.Name) && ScreenWindows.ValidToken(w.Token) && w.Id.Length > 0)
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First().Clone()).ToList();
        _save = save;
        _newToken = newToken ?? ScreenWindows.NewToken;
        _newId = newId ?? ScreenWindows.NewId;
    }

    /// <summary>The web server the windows connect to (null while it is off: commands still edit the windows).</summary>
    public WebServer? Web { get; set; }

    /// <summary>Thread-safe: the windows now.</summary>
    public IReadOnlyList<ScreenWindow> Windows => _windows;

    /// <summary>Owner thread. Refused (with the reason) when it would change nothing; otherwise applied, saved and pushed.</summary>
    public WindowChange Run(WindowArgs args, Func<string, string?> setupPath, IReadOnlyList<HoldingImage> images)
    {
        var current = _windows;
        var change = ScreenWindows.Apply(current, args, new WindowContext(setupPath, images, _newToken, _newId));
        if (!change.Ok) return change;
        switch (change.Effect)
        {
            case WindowEffect.Changed:
                _windows = change.Windows;
                _save(change.Windows.Select(w => w.Clone()).ToList());
                Web?.PushWindows();
                return change;
            case WindowEffect.Identify or WindowEffect.Reload:
            {
                // Attempts are not facts: a window nobody has open can't be found or reloaded, and the sender hears so.
                if (Web is null) return WindowChange.Refused(current, "Windows: turn on Settings → Remote first");
                var open = Web.WindowStatus().Where(o => o.Connected > 0).Select(o => o.WindowId).ToHashSet(StringComparer.Ordinal);
                var reachable = change.Touched.Where(open.Contains).ToList();
                if (reachable.Count == 0)
                    return WindowChange.Refused(current, change.Touched.Count == 1 ? $"{Name(change.Touched[0])} isn't open on any screen" : "None of those windows is open on a screen");
                var told = change.Effect == WindowEffect.Identify ? Web.IdentifyWindows(reachable) : Web.ReloadWindows(reachable);
                var missing = change.Touched.Count - reachable.Count;
                return change with { Touched = reachable, Words = change.Words + (missing > 0 ? $" ({missing} not open on a screen)" : "") + (told > reachable.Count ? $" on {told} screens" : "") };
            }
        }
        return change;
    }

    private string Name(string id) => _windows.FirstOrDefault(w => w.Id == id)?.Name ?? id;
}
