using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

/// <summary>One show app this Stagewatch can hear (Producer → Show network).</summary>
public sealed partial class PeerRow : ObservableObject
{
    public PeerRow(string id) => Id = id;
    public string Id { get; }
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _kind = "";
    [ObservableProperty] private string _light = Stagewatch.Core.Light.Grey;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _problems = "";
}

// Show Bus 1.1 (docs/SHOWBUS.md): this Stagewatch's context for other apps and their AI, and what it hears from them —
// Patterns over its wire, AGMessage and others by posting their context, every node by its hello. Lights and headlines
// are computed from facts, so all of this works with no internet and no AI; an AI, when there is one, reads the same.
public sealed partial class MainViewModel
{
    private readonly PeerContexts _peerContexts = new();
    private volatile string? _contextJson;
    private int _contextTick;

    public ObservableCollection<PeerRow> PeerRows { get; } = new();
    [ObservableProperty] private string _showNetworkLine = "";

    /// <summary>IShowContext: this node's context, rebuilt once a second on the show thread (any thread may read it).</summary>
    public string? ContextJson() => _contextJson;

    /// <summary>IShowContext: another app's context (network thread). Validated and kept 30 s.</summary>
    public string? AcceptPeerContext(string json, string address) => _peerContexts.Accept(json, address, DateTime.UtcNow, _store.Snapshot.NodeId);

    /// <summary>4 Hz (owner thread): once a second, rebuild the context and the Show network rows.</summary>
    private void RefreshShowBus()
    {
        if (++_contextTick % 4 != 0) return;
        ExpireShared();
        var now = DateTime.UtcNow;
        var peers = PeerSummaries(now);
        var input = new ContextInput(_nodeId, Settings.Integrations.RoomName.Length > 0 ? Settings.Integrations.RoomName : Environment.MachineName,
            now, _hub.Latest, _hub.ProducerStuck, LinkItems(), peers, _journal.Entries, ShowClock, _goLatency.Percentile(0.95));
        _contextJson = ShowContext.Build(input);
        SyncPeerRows(now, peers);
    }

    /// <summary>Every link this Stagewatch runs, as context items (off ones left out), plus the Patterns clock check.</summary>
    private List<ContextItem> LinkItems()
    {
        var list = new List<ContextItem>();
        foreach (var (name, health, status) in IntegrationStatus())
        {
            if (health == LinkHealth.Off) continue;
            var light = health switch { LinkHealth.Ok => Light.Green, LinkHealth.Starting => Light.Amber, _ => Light.Red };
            list.Add(new ContextItem("link:" + name.ToLowerInvariant().Replace(' ', '-'), name is "vMix" or "Patterns" ? "playout" : "link", name, light, status));
        }
        list.AddRange(WindowItems());
        if (_patterns?.ClockOffset is { } off && off.Duration() > ClockOffsetEstimator.ApartAfter)
            list.Add(new ContextItem("clock:patterns", "health", "Clock vs Patterns", Light.Amber,
                $"CLOCKS APART: Patterns' clock is {Math.Abs(off.TotalSeconds):0.0} s {(off > TimeSpan.Zero ? "ahead" : "behind")}"));
        return list;
    }

    /// <summary>The show apps heard (hello) joined with what they told us (context). One hop; presenter PCs are links, not peers.</summary>
    private List<PeerSummary> PeerSummaries(DateTime now)
    {
        var byId = new Dictionary<string, PeerSummary>(StringComparer.Ordinal);
        foreach (var p in NetworkPeers())
        {
            if (p.Hello.Kind == "agent" || p.Hello.Id == _nodeId) continue;
            byId[p.Hello.Id] = new PeerSummary(p.Hello.Kind, p.Hello.Id, p.Hello.Name, Light.Grey, "heard on the network — no context yet", p.Address, p.AgeSeconds, false);
        }
        var contexts = _peerContexts.All(now).ToList();
        if (_patterns?.Context is { } pc && PeerContexts.Parse(pc, Settings.Integrations.PatternsHost, now) is { } pe) contexts.Add(pe);
        foreach (var e in contexts)
        {
            if (e.Id == _nodeId) continue;
            var heardAge = byId.TryGetValue(e.Id, out var h) ? h.AgeSeconds : (now - e.AtUtc).TotalSeconds;
            byId[e.Id] = new PeerSummary(e.Kind, e.Id, e.Name.Length > 0 ? e.Name : e.Kind, e.Light, e.Headline, e.Address, Math.Min(heardAge, (now - e.AtUtc).TotalSeconds), true);
        }
        return byId.Values.OrderBy(p => p.Kind).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void SyncPeerRows(DateTime now, IReadOnlyList<PeerSummary> peers)
    {
        var problems = _peerContexts.All(now).ToDictionary(e => e.Id, e => e.Items);
        if (_patterns?.Context is { } pc && PeerContexts.Parse(pc, "", now) is { } pe) problems[pe.Id] = pe.Items;
        for (var i = PeerRows.Count - 1; i >= 0; i--)
            if (!peers.Any(p => p.Id == PeerRows[i].Id)) PeerRows.RemoveAt(i);
        foreach (var p in peers)
        {
            var row = PeerRows.FirstOrDefault(r => r.Id == p.Id);
            if (row is null) PeerRows.Add(row = new PeerRow(p.Id));
            row.Name = p.Name;
            row.Kind = p.Kind switch { "patterns" => "Patterns", "messenger" => "AGMessage", "main" => "Stagewatch", _ => p.Kind };
            row.Light = p.Liveness == "gone" ? Light.Grey : p.Light;
            row.Headline = p.Liveness == "live" ? p.Headline : $"not heard for {p.AgeSeconds:0} s — " + p.Headline;
            row.Problems = problems.TryGetValue(p.Id, out var items)
                ? string.Join("\n", items.Where(x => x.Light is Light.Red or Light.Amber).OrderByDescending(x => Light.Rank(x.Light)).Take(4).Select(x => $"{x.Label}: {x.State}"))
                : "";
        }
        var reds = peers.Count(p => p.Light == Light.Red);
        ShowNetworkLine = peers.Count == 0 ? "" : $"Show network: {peers.Count} app{(peers.Count == 1 ? "" : "s")}" + (reds > 0 ? $" · {reds} red" : "");
    }

    /// <summary>The peers' red and amber parts as advice (they need the producer even with no AI).</summary>
    private IEnumerable<Advice> PeerAdvice(DateTime now)
    {
        var entries = _peerContexts.All(now).ToList();
        if (_patterns?.Context is { } pc && PeerContexts.Parse(pc, "", now) is { } pe && entries.All(x => x.Id != pe.Id)) entries.Add(pe);   // Patterns may also post it
        foreach (var e in entries)
            foreach (var item in e.Items.Where(x => x.Light is Light.Red or Light.Amber).OrderByDescending(x => Light.Rank(x.Light)).Take(2))
                yield return new Advice($"peer:{e.Id}:{item.Id}", item.Light == Light.Red ? AdviceLevel.Urgent : AdviceLevel.Warn,
                    $"{(e.Name.Length > 0 ? e.Name : e.Kind)} — {item.Label}: {item.State}", null, null);
    }

    /// <summary>What the AI sees of the other apps: each peer's light, headline, problem parts and recent lines.</summary>
    private IReadOnlyList<PeerFact> PeerFacts(DateTime now)
    {
        var entries = _peerContexts.All(now).ToList();
        if (_patterns?.Context is { } pc && PeerContexts.Parse(pc, Settings.Integrations.PatternsHost, now) is { } pe && entries.All(x => x.Id != pe.Id)) entries.Add(pe);
        return entries.Select(e => new PeerFact(e.Kind, e.Name, e.Light, e.Headline,
            e.Items.Select(i => $"{i.Label} [{i.Light}]: {i.State}").Take(24).ToList(), e.Recent.TakeLast(8).ToList())).ToList();
    }
}
