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
    /// <summary>How it reaches this Stagewatch: its /ws link and what proves it, else how its context came (<see cref="ShowNetwork.LinkLine"/>).</summary>
    [ObservableProperty] private string _linkLine = "";
    /// <summary>✓ its own key, △ the operator's credential, ○ unverified (<see cref="ShowNetwork.Mark"/>): a shape beside the light, never colour alone.</summary>
    [ObservableProperty] private string _mark = ShowNetwork.Mark("");
    /// <summary>What <see cref="Mark"/> means, in words.</summary>
    [ObservableProperty] private string _markTip = ShowNetwork.MarkWords("");
    /// <summary>The node key that speaks for it, while it is verified by that key; "" otherwise. What "Revoke its key…" revokes.</summary>
    [ObservableProperty] private string _keyId = "";
    /// <summary>Its node id, or "" for a row listed under a key that names no node id. What "Copy node id" copies.</summary>
    public string NodeId => NodeBindings.ValidId(Id) ? Id : "";
    /// <summary>Refused here and not heard (or no longer heard): the row is the desk's own, grey, and never reaches the context.</summary>
    public bool RefusalOnly { get; set; }
    /// <summary>A link on /ws that proves no node id (a key two installs share, a key that names no id): the desk's own row, never in the context.</summary>
    public bool Apart { get; set; }
    /// <summary>Two running nodes say this id (Show Bus §2): amber, its words naming both; only the desk's own row unless a context or a proven link lists it.</summary>
    public bool Conflict { get; set; }
    /// <summary>
    /// The kind a credential named for this node (<see cref="KeyBindingWords.CredentialKind"/>, an apart link's, a keyed
    /// refusal's); "" when only a hello or an unverified context names it. What "Let a key speak for this node…" may save
    /// on the key, unlike <see cref="NodeKind"/>, which shows whatever named it.
    /// </summary>
    public string CredentialKind { get; set; } = "";
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
    /// <summary>The Show network's first line: whether this Stagewatch's hello is out, and if not why (<see cref="ShowBusHello.Line"/>).</summary>
    [ObservableProperty] private string _helloLine = "";
    /// <summary>
    /// The hello line needs the operator, in step with the context's amber <c>bus:hello</c>: every target has failed for
    /// <see cref="ShowBusHello.FailedBeats"/> beats, or another machine says this Stagewatch's id.
    /// </summary>
    [ObservableProperty] private bool _helloLineWarns;
    /// <summary>
    /// The show network as last joined, once a second on the show thread; published whole (an immutable view), so the
    /// context, the desk's rows, its advice, screen refusals and the AI's facts all read the same join on any thread.
    /// </summary>
    private volatile NetworkView _network = NetworkView.Empty;

    /// <summary>IShowContext: this node's context, rebuilt once a second on the show thread (any thread may read it).</summary>
    public string? ContextJson() => _contextJson;

    /// <summary>IShowContext: this Stagewatch's own Show Bus id, from the settings snapshot (any thread may read it).</summary>
    public string? OwnNodeId => _store.Snapshot.NodeId;

    /// <summary>A show app's name as it told this Stagewatch (hello or context), or null. Show thread.</summary>
    private string? PeerNameOf(string id) => _network.Rows.FirstOrDefault(r => r.Peer.Id == id && r.Named)?.Peer.Name;

    /// <summary>4 Hz (owner thread): once a second, rebuild the context and the Show network rows.</summary>
    private void RefreshShowBus()
    {
        if (++_contextTick % 4 != 0) return;
        ExpireShared();
        var now = DateTime.UtcNow;
        var view = Network(now);
        _network = view;
        var input = new ContextInput(_nodeId, Settings.Integrations.RoomName.Length > 0 ? Settings.Integrations.RoomName : Environment.MachineName,
            now, _hub.Latest, _hub.ProducerStuck, LinkItems(), view.Rows.Select(r => r.Peer).ToList(), _journal.Entries, ShowClock, _goLatency.Percentile(0.95),
            PlayoutRows());
        _contextJson = ShowContext.Build(input);
        SyncPeerRows(now, view);
        var hello = OwnHello;
        var twin = OwnTwin;
        HelloLine = ShowBusHello.Line(hello, input.Name, _nodeId, twin);
        HelloLineWarns = ShowBusHello.Item(hello, twin) is not null;   // in step with the context's amber bus:hello
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
        if (ShowBusHello.Item(OwnHello, OwnTwin) is { } hello) list.Add(hello);   // its own health: other apps can't find it, or can't tell it from a copy
        // 13.11.5: apart when the offset less its error is past clock.apart.ms, from the link's matched pongs alone (13.10.2).
        if (_patterns is { ClockApart: true, Clock: { } clock })
            list.Add(new ContextItem("clock:patterns", "health", "Clock vs Patterns", Light.Amber,
                FormattableString.Invariant($"CLOCKS APART: Patterns' clock is {Math.Abs(clock.OffsetMs) / 1000:0.0} s {(clock.OffsetMs > 0 ? "ahead" : "behind")} (± {clock.ErrMs} ms)")));
        return list;
    }

    /// <summary>
    /// The show apps heard (hello), what they told us (context, the one Patterns gave over our own link last) and the ones
    /// following over /ws, joined by node id (<see cref="ShowNetwork.Summaries"/>). One hop; presenter PCs are links, not peers.
    /// </summary>
    private NetworkView Network(DateTime now)
    {
        var hellos = NetworkPeers().Select(p => new HeardNode(p.Hello.Kind, p.Hello.Id, p.Hello.Name, p.Address, p.AgeSeconds) { Conflict = p.Conflict?.Words ?? "" });
        var contexts = _peerContexts.Held(now).ToList();
        if (_patterns?.Context is { } pc && PeerContexts.Parse(pc, PlayoutRules.PatternsLink(Settings.Integrations)?.Host ?? "", now) is { } pe)
            contexts.Add(new HeldContext(pe, _peerContexts.LinkedProof(pe.Id), Pulled: true));
        return ShowNetwork.Summaries(hellos, contexts, _web?.ListNodes() ?? Array.Empty<NodeLinkFact>(), _nodeId, now);
    }

    /// <summary>The app a Show Bus kind names, for people.</summary>
    private static string AppName(string kind) => ShowNetwork.AppName(kind);

    private string KeyName(string keyId) => _apiKeys.FirstOrDefault(k => k.Id == keyId)?.Name ?? "a revoked key";

    private void SyncPeerRows(DateTime now, NetworkView view)
    {
        // A node whose key was refused within the time a context is kept: the operator sees why and can make it a key.
        var since = now - PeerContexts.KeepFor;
        JournalRefusals(_nodes.Refusals().Where(r => r.AtUtc >= since && r.Id != _nodeId));
        var refused = _nodes.RefusedSince(since, _nodeId).ToDictionary(r => r.Id, StringComparer.Ordinal);
        var heard = view.Rows.Select(r => r.Peer.Id).ToHashSet(StringComparer.Ordinal);
        // Links on /ws that prove no id get rows of their own: a conflict under the id it named, a key that names none under the key.
        var apart = ShowNetwork.ApartRows(view.Apart).ToDictionary(a => a.Key, StringComparer.Ordinal);
        // An id two running nodes say (§2): amber, naming both, never merged; a row of its own unless a context or a proven link gives one.
        var conflicts = view.Conflicts.ToDictionary(c => c.Id, StringComparer.Ordinal);
        for (var i = PeerRows.Count - 1; i >= 0; i--)
        {
            var id = PeerRows[i].Id;
            if (!heard.Contains(id) && !apart.ContainsKey(id) && !refused.ContainsKey(id) && !conflicts.ContainsKey(id)) PeerRows.RemoveAt(i);
        }
        foreach (var r in view.Rows)
        {
            var p = r.Peer;
            var row = PeerRows.FirstOrDefault(x => x.Id == p.Id);
            if (row is null) PeerRows.Add(row = new PeerRow(p.Id));
            row.RefusalOnly = false;
            row.Apart = false;
            row.Name = p.Name;
            row.NodeKind = p.Kind;
            row.CredentialKind = KeyBindingWords.CredentialKind(r);
            row.Kind = AppName(p.Kind);
            row.Light = p.Liveness == "gone" ? Light.Grey : p.Light;
            row.Headline = p.Liveness == "live" ? p.Headline : $"not heard for {p.AgeSeconds:0} s — " + p.Headline;
            row.Problems = r.Context is { } c
                ? string.Join("\n", c.Items.Where(x => x.Light is Light.Red or Light.Amber).OrderByDescending(x => Light.Rank(x.Light)).Take(4).Select(x => $"{x.Label}: {x.State}"))
                : "";
            row.LinkLine = ShowNetwork.LinkLine(r, ApiKeyName);
            row.Mark = ShowNetwork.Mark(p.Verified);
            row.MarkTip = ShowNetwork.MarkTip(r);
            // Only a key the bindings hold for this id can be revoked from here; a pulled context proves by the link, not a key.
            row.KeyId = p.Verified == "key" ? _nodes.KeyOf(p.Id) ?? "" : "";
            row.Conflict = conflicts.TryGetValue(p.Id, out var both);
            if (both is not null)
            {
                row.Light = Light.Amber;
                row.Headline = both.Conflict + " — " + row.Headline;
            }
        }
        foreach (var c in conflicts.Values)
        {
            if (heard.Contains(c.Id)) continue;   // its row above, amber
            var row = PeerRows.FirstOrDefault(x => x.Id == c.Id);
            if (row is null) PeerRows.Add(row = new PeerRow(c.Id));
            row.RefusalOnly = false;
            row.Apart = false;
            row.Conflict = true;
            row.NodeKind = c.Kind;
            row.CredentialKind = "";   // a hello names it, never a credential
            row.Kind = AppName(c.Kind);
            row.Name = c.Name.Length > 0 ? c.Name : $"{AppName(c.Kind)} {c.Id}";
            row.Light = Light.Amber;
            row.Headline = c.Conflict;
            row.Problems = "";
            row.LinkLine = ShowNetwork.ConflictLine;
            row.Mark = ShowNetwork.Mark("");
            row.MarkTip = ShowNetwork.MarkWords("");
            row.KeyId = "";
        }
        foreach (var a in apart.Values)
        {
            var row = PeerRows.FirstOrDefault(x => x.Id == a.Key);
            if (row is null) PeerRows.Add(row = new PeerRow(a.Key));
            row.RefusalOnly = false;
            row.Apart = true;
            row.Conflict = false;
            row.NodeKind = a.Kind;
            row.CredentialKind = a.Kind;   // a node link on /ws came with a node key in force or the operator's credential
            row.Kind = AppName(a.Kind);
            row.Name = a.Name;
            row.Light = Light.Grey;
            row.Headline = a.Headline;
            row.Problems = "";
            row.LinkLine = a.LinkLine;
            row.Mark = ShowNetwork.Mark("none");
            row.MarkTip = ShowNetwork.MarkWords("none");
            row.KeyId = "";
        }
        foreach (var r in refused.Values)
        {
            if (heard.Contains(r.Id) || apart.ContainsKey(r.Id) || conflicts.ContainsKey(r.Id)) continue;
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
            row.CredentialKind = r.Kind;   // what the refused keyed request named
            row.RefusalOnly = true;
            row.Apart = false;
            row.Conflict = false;
            row.Light = Light.Grey;
            row.Headline = "not heard — its key was refused, so its context and proposals are not taken";
            row.Problems = "";
            row.LinkLine = "not linked on /ws";
            row.Mark = ShowNetwork.Mark("none");
            row.MarkTip = ShowNetwork.MarkWords("none");
            row.KeyId = "";
        }
        foreach (var row in PeerRows)
        {
            if (!refused.TryGetValue(row.Id, out var r)) { row.KeyNote = ""; row.MakeKeyLabel = ""; continue; }
            var holder = _nodes.KeyOf(r.Id);
            var ownKey = holder is not null && holder != r.KeyId;
            row.KeyNote = ownKey
                ? $"Key '{KeyName(r.KeyId)}' refused: {r.Words}. It has a key of its own now ('{KeyName(holder!)}'): paste that key into it."
                : $"Key '{KeyName(r.KeyId)}' refused: {r.Words}";
            row.MakeKeyLabel = ownKey ? "" : $"Make a key for {(row.RefusalOnly || row.Apart ? AppName(row.NodeKind) : row.Name.Length > 0 ? row.Name : row.Kind)}";
        }
        ShowNetworkLine = ShowNetwork.NetworkLine(view, refused.Count, PeerRows.Count(r => r.RefusalOnly));
    }

    /// <summary>A key's name by its id, or null once it is gone.</summary>
    private string? ApiKeyName(string keyId) => _apiKeys.FirstOrDefault(k => k.Id == keyId)?.Name;

    /// <summary>
    /// A key came to speak for a node, on whichever thread it happened (a node's first proven use on a request or a link,
    /// its kind first named, or the operator's choice): the show thread saves what the bindings hold and journals the change.
    /// </summary>
    private void OnNodeBound(NodeBound bound) => _ui.BeginInvoke(() =>
    {
        if (_disposed) return;
        SaveBindings();
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, KeyBindingWords.JournalLine(bound, ApiKeyName));
    });

    /// <summary>
    /// Show thread: the keys as saved follow the bindings (<see cref="NodeBindings.SaveTo"/>), so a restart puts back the
    /// pairs as they are now and never one the operator moved. Writes only when the saved keys are behind.
    /// </summary>
    private void SaveBindings()
    {
        if (!_nodes.Unsaved(Settings.Remote.ApiKeys)) return;
        _store.Update(s => _nodes.SaveTo(s.Remote.ApiKeys));
        CopyApiKeys();
    }

    /// <summary>The node keys "Let a key speak for this node…" offers for a Show network row's node (<see cref="KeyBindingWords.KeyChoices"/>).</summary>
    public IReadOnlyList<KeyChoice> KeyChoicesFor(PeerRow row) => KeyBindingWords.KeyChoices(row.NodeId, _apiKeys, _nodes, DateTime.UtcNow);

    /// <summary>
    /// "Let a key speak for this node…" on a Show network row: the chosen node key speaks for the row's node from now on,
    /// bound with a kind a credential named (<see cref="KeyBindingWords.KindFor"/>: never a hello's word nor an unverified
    /// context's, else none and the node's own first keyed words name it), through the same <see cref="RebindApiKey"/> as Settings.
    /// </summary>
    public (bool Ok, string Words) LetKeySpeakForPeer(PeerRow row, string keyId)
    {
        if (row.NodeId.Length == 0) return (false, StatusLine = "Not changed: that row names no node id.");
        return RebindApiKey(keyId, KeyBindingWords.KindFor(row.CredentialKind, row.NodeId, _nodes), row.NodeId);
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

    /// <summary>
    /// The peers' red and amber parts as advice (they need the producer even with no AI): the rows the desk shows
    /// (<see cref="ShowNetwork.PeerAdvice"/>), after another machine saying this Stagewatch's own id, while it does.
    /// </summary>
    private IReadOnlyList<Advice> PeerAdvice()
    {
        var peers = ShowNetwork.PeerAdvice(_network);
        return ShowBusHello.OwnAdvice(OwnTwin) is { } own ? [own, .. peers] : peers;
    }

    /// <summary>
    /// What the AI sees of the other apps, from the last join (<see cref="_network"/>): the rows the context lists and each id two
    /// machines say, as the desk shows it (<see cref="AiContext.PeerFacts"/>). Never joins again: any thread reads the published view.
    /// </summary>
    private IReadOnlyList<PeerFact> PeerFacts() => AiContext.PeerFacts(_network);
}
