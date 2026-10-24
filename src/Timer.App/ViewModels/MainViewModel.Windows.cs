using Stagewatch.Core;
using Stagewatch.Remote;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Windows: browser windows Stagewatch controls through addresses of their own (/w/&lt;token&gt;). Every change is a
/// <c>window …</c> command (the Screens window, its right-click menu, Companion, HTTP, OSC, MCP and the copilot all send
/// the same line); <see cref="WindowDirector"/> applies it, saves it to the settings and tells the windows. What a window
/// says it shows comes back from the web server and reaches the Screens window, the context (the Eye, AI) and the copilot.
/// </summary>
public sealed partial class MainViewModel
{
    private WindowDirector? _windowDirector;

    /// <summary>Made once, on the owner thread, from the saved windows (before any server can ask for them).</summary>
    private WindowDirector Windowing => _windowDirector ??= new WindowDirector(Settings.Remote.Windows,
        list => _store.Update(s => s.Remote.Windows = list.ToList())) { Web = _web };

    IReadOnlyList<ScreenWindow> IShowScreens.Windows => _windowDirector?.Windows ?? Array.Empty<ScreenWindow>();

    /// <summary>The windows now (any thread).</summary>
    public IReadOnlyList<ScreenWindow> ControlledWindows => _windowDirector?.Windows ?? Array.Empty<ScreenWindow>();

    /// <summary>What each window last said it shows (empty while the web server is off).</summary>
    public IReadOnlyList<WindowObserved> WindowStatus() => _web?.WindowStatus() ?? Array.Empty<WindowObserved>();

    /// <summary>The address to open on the window's screen, on this PC's network; null while the web server is off.</summary>
    public string? WindowAddress(ScreenWindow w)
    {
        if (_web is null) return null;
        var host = Settings.Remote.WebBindAll ? LanAddresses().FirstOrDefault() ?? "localhost" : "localhost";
        return $"{(_web.RequireHttps ? "https" : "http")}://{host}:{_web.Port}/w/{w.Token}";
    }

    /// <summary>The <c>window …</c> command, from anywhere. Refused with the reason when it would change nothing.</summary>
    private void ApplyWindowCommand(WindowArgs args)
    {
        var director = Windowing;
        director.Web = _web;
        var change = director.Run(args, ScreenPresetPath, Settings.Holding.Images);
        if (!change.Ok) { Refuse(change.Words); return; }
        StatusLine = change.Words;
        // The show log says what the room's screens were given (never their addresses).
        if (change.Effect == WindowEffect.Changed) _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, "Window: " + change.Words);
    }

    /// <summary>The windows as context items (Eye, AI): the same lights the Screens window shows.</summary>
    private IReadOnlyList<ContextItem> WindowItems() => ScreenWindows.ContextItems(ControlledWindows, WindowStatus());
}
