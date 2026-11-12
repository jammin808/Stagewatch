using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Show;

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

    /// <summary>
    /// The <c>window …</c> command, from anywhere. Refused with the reason when it would change nothing. A proposal's line
    /// (<paramref name="by"/> <see cref="DeskOrigin.Proposal"/>) names its app by its Show Bus id alone (§12.4: an app asks only for itself),
    /// never by a name another app may have.
    /// </summary>
    private void ApplyWindowCommand(WindowArgs args, DeskOrigin? by = null)
    {
        var director = Windowing;
        director.Web = _web;
        // A deck's grant (Companion TCP, OSC) proves no person (Show Bus §12.4): it is asked of the operator on the desk, never made
        // here; tried first on the windows now, its app by its Show Bus id alone, as the approved proposal will find it.
        if (by is { Asks: true } && args.Action.Equals("grant", StringComparison.OrdinalIgnoreCase))
        {
            var asked = Desk.AskGrant(args, by, a => director.Check(a, ScreenPresetPath, Settings.Holding.Images, app => ScreenWindows.FindApp(HeardApps(), app, idOnly: true)));
            if (asked.Proposal is { } p)
            {
                ReceiptRefId(DeskReceipts.Ref, p.Id);
                OnSharedProposal(p);
            }
            if (!asked.Ok) { Refuse(asked.Words); return; }
            StatusLine = asked.Words;
            ReceiptAwait(asked.Proposal!.Id);
            return;
        }
        var idOnly = by == DeskOrigin.Proposal;
        var change = director.Run(args, ScreenPresetPath, Settings.Holding.Images, app => ScreenWindows.FindApp(HeardApps(), app, idOnly));
        if (!change.Ok) { Refuse(change.Words); return; }
        StatusLine = change.Words;
        // The show log says what the room's screens were given, and to whom a window is granted or withdrawn (never an address).
        if (change.Effect == WindowEffect.Changed) _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, "Window: " + change.Words);
    }

    /// <summary>IShowScreens: the show app a <c>window grant</c> names, by its id or a name no other app has, as this desk hears the network now.</summary>
    AppLookup IShowScreens.FindApp(string idOrName) => ScreenWindows.FindApp(HeardApps(), idOrName);

    /// <summary>
    /// The show apps a window may be granted to, from this second's join of the show network (node links, contexts, hellos): each with what
    /// it can do with a window — its key-verified link's <c>can=</c>, else its hello's caps — and whether such a link proves it.
    /// </summary>
    public IReadOnlyList<ShowApp> HeardApps() => ShowNetwork.Apps(_network, HelloCaps(), _nodeId);

    /// <summary>Each heard node's hello caps, by its id (the first hello heard for an id).</summary>
    private Dictionary<string, IReadOnlyList<string>> HelloCaps()
    {
        var caps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var p in NetworkPeers()) caps.TryAdd(p.Hello.Id, p.Hello.Caps ?? []);
        return caps;
    }

    /// <summary>The windows' items over every viewer, grant and claim (<see cref="ScreenWindows.ContextItems(IReadOnlyList{ScreenWindow}, IReadOnlyList{WindowObserved}, IReadOnlyList{ViewerObserved}?, IReadOnlyList{GrantObserved}?, IReadOnlyList{WindowPlacement}?)"/>): what the Screens window draws, the same the context says.</summary>
    public IReadOnlyList<ContextItem> WindowLights() => WindowFacts().Select(f => f.Item).ToList();

    /// <summary>Where each grant stands (its app's links, its viewers), for the Screens window's Granted to column; empty while the web server is off.</summary>
    public IReadOnlyList<GrantObserved> WindowGrantStatus() => _web?.GrantStatus() ?? Array.Empty<GrantObserved>();

    /// <summary>The windows as the operator's frame carries them, made once a second with the context (<see cref="RefreshWindowReaders"/>).</summary>
    private EquatableArray<WindowView> _windowViews = EquatableArray<WindowView>.Empty;

    /// <summary>Owner thread: who took a grant (once per grant), for the show log.</summary>
    private readonly GrantsTaken _grantsTaken = new();

    /// <summary>
    /// Owner thread, once a second with the context: the frame's windows from the facts the context was built from, and a show-log row for
    /// each grant its app took since the last second ("Patterns FOH took lobby's address"), never an address.
    /// </summary>
    private void RefreshWindowReaders(IReadOnlyList<WindowFact> facts, IReadOnlyList<GrantObserved> grants)
    {
        var windows = ControlledWindows;
        var views = ScreenWindows.Views(windows, facts);
        if (!views.Equals(_windowViews)) { _windowViews = views; MarkDirty(); }
        foreach (var row in _grantsTaken.Note(windows, grants)) _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, "Window: " + row);
    }

    /// <summary>
    /// The windows as the context and the copilot read them (<see cref="WindowFact"/>), from one read of the list: each window's item (the
    /// same light the Screens window shows), what it is given, to whom it is granted, and what its holders claim about where they show it
    /// (12.10: only from a context a key bound to the holder's id proves, while its grant has a viewer on it).
    /// </summary>
    private IReadOnlyList<WindowFact> WindowFacts() => WindowFacts(WindowGrantStatus());

    private IReadOnlyList<WindowFact> WindowFacts(IReadOnlyList<GrantObserved> grants)
    {
        var windows = ControlledWindows;
        var holders = WindowPlacements.Holders(windows, grants);
        var placements = WindowPlacements.From(_network.Rows.Where(r => r.Context is not null).Select(r => new HeldContext(r.Context!, r.ContextProof, r.Pulled)),
            _nodeId, holders);
        // Worst over every viewer (its own address, each grant's), with whom it is granted to and what its holders claim (12.10).
        var items = ScreenWindows.ContextItems(windows, WindowStatus(), _web?.ViewerStatus() ?? Array.Empty<ViewerObserved>(), grants, placements);
        return ScreenWindows.Facts(windows, items, placements);
    }
}
