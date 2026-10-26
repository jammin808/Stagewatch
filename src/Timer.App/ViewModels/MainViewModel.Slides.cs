using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Slides from presenter PCs (Stagewatch Slides): they send their slideshow here, so slide awareness, rehearsal
/// ghosts and speech pace work when PowerPoint runs on another computer. The show follows this PC's PowerPoint, a
/// chosen presenter PC, or automatically whichever presenter PC is presenting. A small UDP beacon lets presenter
/// PCs (and anything else on the network, e.g. Patterns) find this Stagewatch with no setup.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly RemoteDecks _remoteDecks = new();
    private SlidesBeacon? _slidesBeacon;
    /// <summary>This install's stable Show Bus id (a renamed Stagewatch is still the same node).</summary>
    private string _nodeId => Settings.NodeId;

    /// <summary>This process's <c>inst</c> (Show Bus §2): new at every start, so one id said by two running Stagewatches is seen as a conflict.</summary>
    private readonly string _inst = ShowBusHello.NewInst();

    private void InitSlides() => _slidesBeacon = new SlidesBeacon(SayHello);

    /// <summary>
    /// The beacon's timer thread, every beat: this Stagewatch's hello as the settings snapshot and the servers as they bound say it,
    /// never the live settings — scope None (no hello at all) while Remote is off or set to this PC, and only the doors other
    /// machines can open (<see cref="ShowBusHello.Main"/>).
    /// </summary>
    private (SlidesBeacon.Hello? Hello, HelloScope Scope) SayHello()
    {
        var s = _store.Snapshot;
        var web = Volatile.Read(ref _web);
        var doors = ShowBusHello.Doors(web?.Door, web?.Fingerprint, _companion.Door, Volatile.Read(ref _osc)?.Door);
        return ShowBusHello.Main(s.NodeId, _inst, s.Integrations.RoomName.Length > 0 ? s.Integrations.RoomName : Environment.MachineName,
            Environment.MachineName, doors, s.Integrations.Mcp && FeatureGate.AllowsMcp(_license.Tier), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>What this Stagewatch's hello did at its last beat (<see cref="SlidesBeacon.Status"/>); any thread reads it.</summary>
    private HelloStatus OwnHello => _slidesBeacon?.Status ?? HelloStatus.NotYet;

    /// <summary>Another machine saying this Stagewatch's id (§2), while its mark stands (<see cref="SlidesBeacon.OwnConflict"/>).</summary>
    private HelloConflict? OwnTwin => _slidesBeacon?.OwnConflict;

    /// <summary>The deck the show follows right now (this PC's PowerPoint or a presenter PC), or null.</summary>
    private DeckState? CurrentDeck() => _remoteDecks.Pick(Settings.Integrations.SlidesSource, _ppt?.Value, DateTime.UtcNow);

    public IReadOnlyList<PresenterPc> PresenterPcs() => _remoteDecks.List();

    /// <summary>Presenter PCs and other Stagewatch machines heard on the network (whether or not they send here).</summary>
    public IReadOnlyList<SlidesBeacon.Peer> NetworkPeers() => _slidesBeacon?.Peers() ?? Array.Empty<SlidesBeacon.Peer>();

    public string? AcceptSlideFeed(SlideFeed feed, string address, out bool needScript)
    {
        needScript = false;
        if (!FeatureGate.AllowsPowerPointAddIn(_license.Tier)) return "Slides from presenter PCs need Stagewatch Pro";
        needScript = _remoteDecks.Accept(feed, address);
        return null;
    }

    public void AcceptSlideScript(string hash, string script) => _remoteDecks.AcceptScript(hash, script);

    /// <summary>One line for Settings → Integrations → Status.</summary>
    private (LinkHealth Health, string Status) PresenterStatus()
    {
        var pcs = PresenterPcs();
        if (pcs.Count == 0) return (LinkHealth.Off, Settings.Integrations.SlidesSource == "local" ? "this PC only" : "no presenter PCs yet");
        var live = pcs.Where(p => p.Connected).ToList();
        var text = string.Join(" · ", pcs.Select(p => $"{p.Name}: " + (!p.Connected ? "not heard" : p.Presenting ? $"slide {p.Slide}/{p.Count}" : "not presenting")));
        return (live.Count > 0 ? LinkHealth.Ok : LinkHealth.Starting, text);
    }
}
