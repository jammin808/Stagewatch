using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// <summary>The node's kind as Show Bus names it (patterns, messenger…); <see cref="Kind"/> is the app's name.</summary>
    [ObservableProperty] private string _nodeKind = "";
    /// <summary>Why a key this node used was refused, and what to do about it; "" when nothing was.</summary>
    [ObservableProperty] private string _keyNote = "";
    /// <summary>"Make a key for AGMessage" when the node was refused and has no key of its own; "" otherwise.</summary>
    [ObservableProperty] private string _makeKeyLabel = "";
    /// <summary>Refused here and not heard (or no longer heard): the row is the desk's own, grey, and never reaches the context.</summary>
    public bool RefusalOnly { get; set; }
}

// Show Bus 1.1 (docs/SHOWBUS.md): this Stagewatch's context for other apps and their AI, and what it hears from them —
// Patterns over its wire, AGMessage and others by posting their context, every node by its hello. Lights and headlines
// are computed from facts, so all of this works with no internet and no AI; an AI, when there is one, reads the same.
public sealed partial class MainViewModel
{
    // One node key speaks for one node id on every node route: the posted context, proposals, collects and results.
    private readonly NodeBindings _nodes = new();
    private readonly PeerContexts _peerContexts;
    private readonly HashSet<(string KeyId, string Id)> _refusalsJournalled = new();   // show thread
    private volatile string? _contextJson;
    private int _contextTick;

    /// <summary>IShowAccess: which node each node key speaks for.</summary>
    public NodeBindings Nodes => _nodes;

    /// <summary>A node key was made for a refused node: its secret, shown once by the desk, never journalled or kept.</summary>
    public event Action<string, string>? NodeKeyMade;

    public ObservableCollection<PeerRow> PeerRows { get; } = new();
    [ObservableProperty] private string _showNetworkLine = "";

    /// <summary>IShowContext: this node's context, rebuilt once a second on the show thread (any thread may read it).</summary>
    public string? ContextJson() => _contextJson;

    /// <summary>IShowContext: another app's context with no credential that names a node (network thread). Kept 30 s, unverified.</summary>
    public string? AcceptPeerContext(string json, string address)
        => _peerContexts.Accept(json, address, DateTime.UtcNow, _store.Snapshot.NodeId, NodeCredential.None).Error;

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

    /// <summary>The app a Show Bus kind names, for people.</summary>
    private static string AppName(string kind) => kind switch { "patterns" => "Patterns", "messenger" => "AGMessage", "playmap" => "PlayMap", "main" => "Stagewatch", "" => "show app", _ => kind };

    private string KeyName(string keyId) => _apiKeys.FirstOrDefault(k => k.Id == keyId)?.Name ?? "a revoked key";

    private void SyncPeerRows(DateTime now, IReadOnlyList<PeerSummary> peers)
    {
        var problems = _peerContexts.All(now).ToDictionary(e => e.Id, e => e.Items);
        if (_patterns?.Context is { } pc && PeerContexts.Parse(pc, "", now) is { } pe) problems[pe.Id] = pe.Items;
        // A node whose key was refused within the time a context is kept: the operator sees why and can make it a key.
        var since = now - PeerContexts.KeepFor;
        JournalRefusals(_nodes.Refusals().Where(r => r.AtUtc >= since && r.Id != _nodeId));
        var refused = _nodes.RefusedSince(since, _nodeId).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var heard = peers.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = PeerRows.Count - 1; i >= 0; i--)
            if (!heard.Contains(PeerRows[i].Id) && !refused.ContainsKey(PeerRows[i].Id)) PeerRows.RemoveAt(i);
        foreach (var p in peers)
        {
            var row = PeerRows.FirstOrDefault(r => r.Id == p.Id);
            if (row is null) PeerRows.Add(row = new PeerRow(p.Id));
            row.RefusalOnly = false;
            row.Name = p.Name;
            row.NodeKind = p.Kind;
            row.Kind = AppName(p.Kind);
            row.Light = p.Liveness == "gone" ? Light.Grey : p.Light;
            row.Headline = p.Liveness == "live" ? p.Headline : $"not heard for {p.AgeSeconds:0} s — " + p.Headline;
            row.Problems = problems.TryGetValue(p.Id, out var items)
                ? string.Join("\n", items.Where(x => x.Light is Light.Red or Light.Amber).OrderByDescending(x => Light.Rank(x.Light)).Take(4).Select(x => $"{x.Label}: {x.State}"))
                : "";
        }
        foreach (var r in refused.Values)
        {
            if (heard.Contains(r.Id)) continue;
            // Refused here and not heard, or no longer heard: grey with no light or headline left over from when it was.
            var row = PeerRows.FirstOrDefault(x => x.Id == r.Id);
            if (row is null) PeerRows.Add(row = new PeerRow(r.Id));
            var named = row.Name.Length > 0 && row.Name != $"{AppName(row.NodeKind)} {row.Id}";
            if (r.Kind.Length > 0 || row.NodeKind.Length == 0)
            {
                row.NodeKind = r.Kind;
                row.Kind = AppName(r.Kind);
            }
            if (!named) row.Name = $"{AppName(row.NodeKind)} {row.Id}";
            row.RefusalOnly = true;
            row.Light = Light.Grey;
            row.Headline = "not heard — its key was refused, so its context and proposals are not taken";
            row.Problems = "";
        }
        foreach (var row in PeerRows)
        {
            if (!refused.TryGetValue(row.Id, out var r)) { row.KeyNote = ""; row.MakeKeyLabel = ""; continue; }
            var holder = _nodes.KeyOf(r.Id);
            var ownKey = holder is not null && holder != r.KeyId;
            row.KeyNote = ownKey
                ? $"Key '{KeyName(r.KeyId)}' refused: {r.Words}. It has a key of its own now ('{KeyName(holder!)}'): paste that key into it."
                : $"Key '{KeyName(r.KeyId)}' refused: {r.Words}";
            row.MakeKeyLabel = ownKey ? "" : $"Make a key for {(row.RefusalOnly ? AppName(row.NodeKind) : row.Name.Length > 0 ? row.Name : row.Kind)}";
        }
        var reds = peers.Count(p => p.Light == Light.Red);
        var refusedOnly = PeerRows.Count(r => r.RefusalOnly);
        ShowNetworkLine = peers.Count + refusedOnly == 0 ? ""
            : $"Show network: {peers.Count} app{(peers.Count == 1 ? "" : "s")}" + (reds > 0 ? $" · {reds} red" : "") + (refused.Count > 0 ? $" · {refused.Count} key refused" : "");
    }

    /// <summary>Each (key, node) refusal once this session: the key's name and both nodes, never a secret.</summary>
    private void JournalRefusals(IEnumerable<NodeRefusal> refusals)
    {
        foreach (var r in refusals)
            if (_refusalsJournalled.Add((r.KeyId, r.Id)))
                _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1,
                    $"API key '{KeyName(r.KeyId)}' refused for {NodeBindings.Describe(r.Kind, r.Id)}: {r.Words}");
    }

    /// <summary>
    /// The operator's one press for a node whose key was refused (a key two apps shared): a node key of its own, named
    /// after it and speaking for it from now on. The secret goes to <see cref="NodeKeyMade"/> once; Stagewatch keeps only
    /// its fingerprint. Show thread.
    /// </summary>
    [RelayCommand]
    private void MakeKeyForPeer(PeerRow? row)
    {
        if (row is null) return;
        var (secret, words) = MakeNodeKeyFor(row.Id, row.RefusalOnly ? "" : row.Name);
        StatusLine = words;
        if (secret is not null) NodeKeyMade?.Invoke(words, secret);
    }

    /// <summary>
    /// Make a node key that speaks for <paramref name="id"/>, saved with the kind the node's own refused request named
    /// (<see cref="NodeBindings.KindToMakeFor"/>). The key is handed out only when the bindings took it for that id; when
    /// another key came to speak for the id first, the new key is removed unseen and the words say so. Returns the secret
    /// (or null) and what happened.
    /// </summary>
    public (string? Secret, string Words) MakeNodeKeyFor(string id, string name)
    {
        if (!NodeBindings.ValidId(id)) return (null, "That node has no id a key can speak for.");
        var kind = _nodes.KindToMakeFor(id);
        var who = NodeBindings.Describe(kind, id);
        var app = kind is null ? "that app" : AppName(kind);
        if (_nodes.KeyOf(id) is { } holder) return (null, $"{who} already has a key of its own ('{KeyName(holder)}').");
        if (Settings.Remote.ApiKeys.Count >= Stagewatch.Core.ApiKeys.Max) return (null, $"There are already {Stagewatch.Core.ApiKeys.Max} API keys — revoke one first (Settings → Remote).");
        var (key, secret) = Stagewatch.Core.ApiKeys.Create(name.Trim().Length > 0 ? name : $"{AppName(kind ?? "")} {id}", ApiKeyScope.Node, DateTime.UtcNow);
        key.Node = id;
        key.NodeKind = kind;
        _store.Update(s => s.Remote.ApiKeys.Add(key));
        RefreshScreenCopies();   // the bindings take the pair saved on the key, unless another key came to speak for the id first
        var bound = _nodes.KeyOf(id);
        if (bound != key.Id)
        {
            _store.Update(s => s.Remote.ApiKeys.RemoveAll(k => k.Id == key.Id));
            RefreshScreenCopies();
            return (null, bound is null ? $"No key made: {who} could not be given one." : $"No key made: {who} has a key of its own now ('{KeyName(bound)}').");
        }
        _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, $"API key made: {key.Name} ({Stagewatch.Core.ApiKeys.Describe(ApiKeyScope.Node)}) — it speaks for {who}");
        return (secret, $"API key '{key.Name}' made: it speaks for {who}. Paste it into {app} in place of the shared key.");
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
