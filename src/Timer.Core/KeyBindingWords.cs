using System.Globalization;

namespace Stagewatch.Core;

/// <summary>
/// A node the operator may let a key speak for: its kind (empty while not known, then the node's own first words name
/// it), its id, and why it is offered, in words.
/// </summary>
public sealed record NodeChoice(string Kind, string Id, string Label);

/// <summary>A node key the operator may let speak for a node, and what choosing it changes, in words.</summary>
public sealed record KeyChoice(string KeyId, string Label);

/// <summary>
/// The desk's words for which node each node key speaks for (Settings → Remote, the Show network, the journal) and the
/// choices "Let it speak for…" offers, from the bindings. Key names, node kinds and ids only, never a secret. Pure.
/// </summary>
public static class KeyBindingWords
{
    /// <summary>How long ago, for people: "12 s ago", "4 min ago", "2 h ago".</summary>
    public static string Ago(DateTime thenUtc, DateTime nowUtc)
    {
        var s = (long)Math.Max(0, (nowUtc - thenUtc).TotalSeconds);
        return s < 60 ? s.ToString(CultureInfo.InvariantCulture) + " s ago"
            : s < 3600 ? (s / 60).ToString(CultureInfo.InvariantCulture) + " min ago"
            : (s / 3600).ToString(CultureInfo.InvariantCulture) + " h ago";
    }

    private static string Named(Func<string, string?> keyName, string keyId) => keyName(keyId) is { } n ? $"'{n}'" : "(a revoked key)";

    /// <summary>
    /// The journal row for a key that came to speak for a node: its first proven use, its kind first named, or the
    /// operator's choice, saying what the key spoke for before and which key the id moved off.
    /// <paramref name="keyName"/> names a key by its id (null once it is gone).
    /// </summary>
    public static string JournalLine(NodeBound bound, Func<string, string?> keyName)
    {
        var key = "API key " + Named(keyName, bound.KeyId);
        var node = NodeBindings.Describe(bound.Kind, bound.Id);
        if (!bound.ByOperator)
            return bound.PreviousId == bound.Id ? $"{key} speaks for {node} (its kind, first named)" : $"{key} now speaks for {node} (first use)";
        var before = bound.PreviousId is { } p ? "it spoke for " + NodeBindings.Describe(bound.PreviousKind, p) : "it spoke for no node";
        var taken = bound.TakenFromKeyId is { } t ? $"; key {Named(keyName, t)} speaks for no node now" : "";
        return $"{key} now speaks for {node} (the operator's choice; {before}{taken})";
    }

    /// <summary>
    /// A key's words in Settings → Remote: what a node key speaks for ("speaks for patterns 9f8e7d6c", "no node yet") and,
    /// when it was refused for another node, which and when. "" for a key of any other scope, which never speaks for a node.
    /// </summary>
    public static string KeyLine(ApiKey key, (string Kind, string Id)? node, NodeRefusal? refused, DateTime nowUtc)
    {
        if (key.Scope != ApiKeyScope.Node) return "";
        var line = node is { } n ? "speaks for " + NodeBindings.Describe(n.Kind, n.Id) : "no node yet";
        if (refused is { } r && (node is null || node.Value.Id != r.Id))
            line += $" · refused for {NodeBindings.Describe(r.Kind, r.Id)} {Ago(r.AtUtc, nowUtc)}";
        return line;
    }

    /// <summary>
    /// The nodes "Let it speak for…" offers a key (decision: the ids that key was refused for, the ids heard, or a typed
    /// one): first the node this key was last refused for, then the show network's rows, the ids its unverified links named
    /// and the nodes refused here, each once, never the node the key already speaks for nor this Stagewatch's own id. A
    /// node's kind is one a credential named (<see cref="KindFor"/>): a row known only by its hello or an unverified context
    /// is offered with none, and the node's first keyed words name it. Each label says which key speaks for the node now,
    /// since choosing it moves the node off that key.
    /// </summary>
    public static IReadOnlyList<NodeChoice> NodeChoices(string keyId, NodeBindings nodes, NetworkView view, IReadOnlyList<NodeRefusal> refusedHere,
        string? ownId, Func<string, string?> keyName, DateTime nowUtc)
    {
        var mine = nodes.NodeOf(keyId);
        var list = new List<NodeChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (mine is { } m) seen.Add(m.Id);
        if (!string.IsNullOrEmpty(ownId)) seen.Add(ownId);

        string Holder(string id) => nodes.KeyOf(id) is { } holder ? $"speaks with key {Named(keyName, holder)}, which would speak for no node" : "no key of its own yet";
        void Add(string kind, string id, string label)
        {
            if (NodeBindings.ValidId(id) && seen.Add(id)) list.Add(new NodeChoice(NodeBindings.ValidKind(kind) ? kind : "", id, label));
        }

        if (nodes.LastRefused(keyId) is { } r)
        {
            // This key's own refused request: the kind its app will name when it speaks with this key.
            var kind = r.Kind.Length > 0 ? r.Kind : KindFor("", r.Id, nodes);
            Add(kind, r.Id, $"{NodeBindings.Describe(kind, r.Id)} — this key was refused for it {Ago(r.AtUtc, nowUtc)} · {Holder(r.Id)}");
        }
        foreach (var row in view.Rows)
        {
            var p = row.Peer;
            var kind = KindFor(CredentialKind(row), p.Id, nodes);
            Add(kind, p.Id, $"{p.Name} — {NodeBindings.Describe(kind, p.Id)} · {Holder(p.Id)}");
        }
        // A node link on /ws came with a credential (a node key in force or the operator's), even when its id is refused.
        foreach (var link in view.Apart)
            if (link.Id is { } id)
            {
                var kind = KindFor(link.Kind, id, nodes);
                Add(kind, id, $"{NodeBindings.Describe(kind, id)} — on /ws, unverified · {Holder(id)}");
            }
        foreach (var refusal in refusedHere)
        {
            var kind = refusal.Kind.Length > 0 ? refusal.Kind : KindFor("", refusal.Id, nodes);
            Add(kind, refusal.Id, $"{NodeBindings.Describe(kind, refusal.Id)} — a key was refused for it {Ago(refusal.AtUtc, nowUtc)} · {Holder(refusal.Id)}");
        }
        return list;
    }

    /// <summary>
    /// The node keys "Let a key speak for this node…" offers for <paramref name="id"/>: first the keys refused for it
    /// (newest first: a reinstalled app's key, refused for its new id), then the keys that speak for no node, then those
    /// that speak for another, by name. Each says what it speaks for now, since choosing it moves it. Never the key that
    /// already speaks for the node, and never a key of another scope.
    /// </summary>
    public static IReadOnlyList<KeyChoice> KeyChoices(string id, IEnumerable<ApiKey> keys, NodeBindings nodes, DateTime nowUtc)
    {
        var holder = nodes.KeyOf(id);
        return keys.Where(k => k.Scope == ApiKeyScope.Node && k.Id != holder)
            .Select(k =>
            {
                var refused = nodes.LastRefused(k.Id) is { } r && r.Id == id ? r : (NodeRefusal?)null;
                var node = nodes.NodeOf(k.Id);
                // A key that speaks for another node always says so: choosing it leaves that node with no key.
                var speaks = node is { } n ? $"speaks for {NodeBindings.Describe(n.Kind, n.Id)} now, and would stop" : "speaks for no node yet";
                var (rank, state) = refused is { } rf ? (0, $"refused for this node {Ago(rf.AtUtc, nowUtc)} · {speaks}")
                    : (node is null ? 1 : 2, speaks);
                return (Rank: rank, At: refused?.AtUtc ?? DateTime.MinValue, Choice: new KeyChoice(k.Id, $"{k.Name} (…{k.Hint}) · {state}"), k.Name);
            })
            .OrderBy(x => x.Rank).ThenByDescending(x => x.At).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Choice).ToList();
    }

    /// <summary>
    /// The kind a key the operator lets speak for a node is bound with, saved on the key: what the node named with a key of
    /// its own — the kind its newest refused keyed request named (<see cref="NodeBindings.KindToMakeFor"/>), else the kind
    /// the key that speaks for it now is bound with — else <paramref name="credentialKind"/>, the kind a credential named
    /// for its row (<see cref="CredentialKind"/>, or a node link's on /ws), else none, and the node's first keyed words name
    /// it. Never a hello's word nor an unverified context's: a wrong kind saved on the key would refuse the real node's
    /// keyed requests until the operator moved it again.
    /// </summary>
    public static string KindFor(string credentialKind, string id, NodeBindings nodes)
    {
        if (nodes.KindToMakeFor(id) is { } asked) return asked;
        if (nodes.KeyOf(id) is { } holder && nodes.NodeOf(holder) is { } held && held.Id == id && held.Kind.Length > 0) return held.Kind;
        return NodeBindings.ValidKind(credentialKind) ? credentialKind : "";
    }

    /// <summary>
    /// The kind a credential named for a show network row: its context's when what proves the context is a key, the
    /// operator's credential or this Stagewatch's own Patterns link; else its proven /ws link's; else a link's on /ws that
    /// names its id and was refused, which still came with a node key in force or the operator's credential. "" when only a
    /// hello (unauthenticated, from anyone on the LAN) or a context that proves nothing names it, whatever the row shows.
    /// </summary>
    public static string CredentialKind(NetworkRow row)
    {
        if (row.Context is { } c && row.ContextProof != NodeProof.None && NodeBindings.ValidKind(c.Kind)) return c.Kind;
        if (row.Link is { Verified: not NodeProof.None } l && NodeBindings.ValidKind(l.Kind)) return l.Kind;
        return row.Unproven.FirstOrDefault(u => NodeBindings.ValidKind(u.Kind))?.Kind ?? "";
    }
}
