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

    private void InitSlides()
    {
        _slidesBeacon = new SlidesBeacon(() => _web is not { } web ? null : new SlidesBeacon.Hello(
            "main", 1, _nodeId,
            Settings.Integrations.RoomName.Length > 0 ? Settings.Integrations.RoomName : Environment.MachineName,
            Environment.MachineName, web.Port, false,
            HelloCaps(),
            Proto: 1,
            Ports: HelloPorts(web.Port),
            Clock: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Host: Environment.MachineName,
            Tls: _web?.Fingerprint));
    }

    /// <summary>What this Stagewatch offers on the Show Bus (only what is switched on).</summary>
    private string[] HelloCaps()
    {
        var caps = new List<string> { "timer", "cues", "messages", "context", "receipts", "clock", "screens", "aux", "slides-in", "copilot", "approvals" };
        if (McpAllowed) caps.Add("mcp");
        return caps.ToArray();
    }

    private Dictionary<string, int> HelloPorts(int web)
    {
        var ports = new Dictionary<string, int> { ["http"] = web, ["ws"] = web };
        if (_web?.Certificate is not null) ports["https"] = web;   // the same port: TLS is recognised by its first byte
        if (_companion.IsRunning) ports["companion"] = _companion.Port;
        if (_osc is not null) ports["osc"] = _osc.Port;
        return ports;
    }

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
