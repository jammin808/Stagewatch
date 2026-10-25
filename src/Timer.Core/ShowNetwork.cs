using System.Globalization;

namespace Stagewatch.Core;

/// <summary>A node heard by its hello (Show Bus §4), as the show network reads it.</summary>
public sealed record HeardNode(string Kind, string Id, string Name, string Address, double AgeSeconds);

/// <summary>
/// A context held for a node, with what it proves now: <see cref="PeerContexts.ProofOf"/> for one posted here,
/// <see cref="PeerContexts.LinkedProof"/> for one this Stagewatch pulled over its own Patterns link (<see cref="Pulled"/>),
/// which proves the id by the address the operator set, never by a key the node holds.
/// </summary>
public sealed record HeldContext(PeerContexts.Entry Entry, NodeProof Proof, bool Pulled = false);

/// <summary>
/// One show app in the show network: the summary every reader takes (the context's peers row, the AI's facts, the desk's
/// row), the context its light, headline, items and recent lines come from and what that proves now (null and none when
/// it has none), the /ws link that proves its id (null when none does), and the links that name its id and prove nothing
/// (a key two installs share). <see cref="Named"/> is true when the app told its name (hello or context); otherwise the row
/// is named after its kind and id.
/// </summary>
public sealed record NetworkRow(PeerSummary Peer, PeerContexts.Entry? Context, NodeProof ContextProof, NodeLinkFact? Link, IReadOnlyList<NodeLinkFact> Unproven, bool Named)
{
    /// <summary><see cref="Context"/> came over this Stagewatch's own Patterns link, not from a post by the node.</summary>
    public bool Pulled { get; init; }

    /// <summary>
    /// A context held for this id proves nothing while a link proves the id: it is set aside, so none of its light,
    /// headline, items or recent lines reach a reader under the proven id (<see cref="Context"/> is null).
    /// </summary>
    public bool ClaimSetAside { get; init; }
}

/// <summary>
/// The show network at one moment: its rows (at most the cap), how many more there were, and the node links that add no
/// row: a key that names no node id, and a link whose id proves nothing and that no hello, context or proven link lists.
/// The desk shows those apart; the context and the AI never list them.
/// </summary>
public sealed record NetworkView(IReadOnlyList<NetworkRow> Rows, int More, IReadOnlyList<NodeLinkFact> Apart)
{
    public static readonly NetworkView Empty = new(Array.Empty<NetworkRow>(), 0, Array.Empty<NodeLinkFact>());
}

/// <summary>
/// A row the desk shows apart from the show network, one per id a conflict named or per key that names no id
/// (<see cref="Key"/> is that id, or "key:" and the key's id, which no node id can be): grey, marked unverified.
/// </summary>
public sealed record ApartRow(string Key, string Kind, string? NodeId, string Name, string Headline, string LinkLine);

/// <summary>
/// Who this Stagewatch's show network is (Show Bus §6 peers, one hop): the apps heard by their hello, the contexts held
/// for them, and the show apps that follow this Stagewatch over /ws, joined by node id. One join for every reader — the
/// context, the copilot and the assistant, the desk's rows and its advice — so they never disagree about who is there or
/// what proves it. A row's <c>link</c> is "ws" only for a node link whose id is verified by a key or the operator's
/// credential. Its <c>verified</c> is what proves the facts the row shows: the context's proof when a context supplies
/// them, else the proven link's, "none" when only links that prove nothing name it, and "" when nothing spoke for it (a
/// hello alone). A context that proves nothing never rides under an id a link proves: it is set aside. A claim (in
/// Screens), a conflict and a key that names no id never add a row. Pure.
/// </summary>
public static class ShowNetwork
{
    /// <summary>A row's <c>link</c> when a node link on /ws proves its id.</summary>
    public const string LinkWs = "ws";

    public const string HelloOnly = "heard on the network — no context yet";
    public const string LinkOnly = "follows this Stagewatch over /ws — no context yet";

    /// <summary>The link line's words for a context set aside under an id a link proves.</summary>
    public const string SetAside = "a context that names its id proves nothing: set aside, so it shows nothing here and sets no risk";

    public const string ApartNoId = "follows the timer over /ws with a node key that names no node id";
    public const string ApartUnproven = "follows the timer over /ws, but nothing proves its id: its context and proposals are refused";

    /// <summary>
    /// The join, capped at <paramref name="max"/> rows: rows a hello or a context gives are kept first, then rows only a
    /// proven link gives; <see cref="NetworkView.More"/> counts the rest. <paramref name="contexts"/> are in the caller's
    /// order of trust (held before pulled): a node's row takes the first that proves something, else the first, and sets
    /// it aside when it proves nothing while a link proves the id. A row is live while a proven link is open, else by its
    /// hello or context. Presenter PCs (kind <c>agent</c>) are links, not peers, and nothing under <paramref name="ownId"/>
    /// is a peer.
    /// </summary>
    public static NetworkView Summaries(IEnumerable<HeardNode> hellos, IEnumerable<HeldContext> contexts, IReadOnlyList<NodeLinkFact> links,
        string? ownId, DateTime nowUtc, int max = PeerContexts.MaxPeers)
    {
        bool Own(string id) => !string.IsNullOrEmpty(ownId) && string.Equals(id, ownId, StringComparison.Ordinal);

        var heard = new Dictionary<string, HeardNode>(StringComparer.Ordinal);
        foreach (var h in hellos)
            if (h.Kind != "agent" && NodeBindings.ValidId(h.Id) && !Own(h.Id)) heard[h.Id] = h;

        var held = new Dictionary<string, HeldContext>(StringComparer.Ordinal);
        foreach (var c in contexts)
        {
            if (Own(c.Entry.Id)) continue;
            if (!held.TryGetValue(c.Entry.Id, out var first) || (first.Proof == NodeProof.None && c.Proof != NodeProof.None)) held[c.Entry.Id] = c;
        }

        var byId = links.Where(l => l.Id is { } id && !Own(id)).GroupBy(l => l.Id!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        NetworkRow Row(string id)
        {
            heard.TryGetValue(id, out var h);
            held.TryGetValue(id, out var c);
            var facts = byId.TryGetValue(id, out var f) ? f : new List<NodeLinkFact>();
            var link = facts.Where(l => l.Verified != NodeProof.None).OrderByDescending(l => l.Verified).ThenBy(l => l.ConnectedUtc).FirstOrDefault();
            var unproven = facts.Where(l => l.Verified == NodeProof.None).OrderBy(l => l.ConnectedUtc).ToList();
            // A claim never speaks under an id a link proves: its light, headline, items and name stay out of every reader.
            var setAside = c is not null && c.Proof == NodeProof.None && link is not null;
            var e = setAside ? null : c?.Entry;
            var kind = e?.Kind ?? h?.Kind ?? link?.Kind ?? "";
            var toldName = e is { Name.Length: > 0 } ? e.Name : h is { Name.Length: > 0 } ? h.Name : null;
            var name = toldName ?? $"{AppName(kind)} {id}";
            var (light, headline) = e is not null ? (e.Light, e.Headline) : (Light.Grey, h is not null && link is null ? HelloOnly : LinkOnly);
            var address = e is { Address.Length: > 0 } ? e.Address : h?.Address ?? link?.Address ?? "";
            var age = double.MaxValue;
            if (h is not null) age = Math.Min(age, h.AgeSeconds);
            if (e is not null) age = Math.Min(age, Math.Max(0, (nowUtc - e.AtUtc).TotalSeconds));
            if (link is not null) age = 0;   // open now: a live link is heard
            var contextProof = e is not null ? c!.Proof : NodeProof.None;
            // What proves the facts shown: the context's proof when they are its, else the proven link's.
            var proof = e is not null ? contextProof : link?.Verified ?? NodeProof.None;
            var spoke = link is not null || e is not null || unproven.Count > 0;
            var peer = new PeerSummary(kind, id, name, light, headline, address, age, e is not null)
            {
                Link = link is not null ? LinkWs : "",
                Verified = spoke ? NodeBindings.Word(proof) : "",
            };
            return new NetworkRow(peer, e, contextProof, link, unproven, toldName is not null)
            {
                Pulled = e is not null && c!.Pulled,
                ClaimSetAside = setAside,
            };
        }

        static IOrderedEnumerable<NetworkRow> Sorted(IEnumerable<NetworkRow> rows)
            => rows.OrderBy(r => r.Peer.Kind, StringComparer.Ordinal).ThenBy(r => r.Peer.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Peer.Id, StringComparer.Ordinal);

        var told = Sorted(heard.Keys.Union(held.Keys, StringComparer.Ordinal).Select(Row)).ToList();
        var linkOnly = Sorted(byId.Where(kv => !heard.ContainsKey(kv.Key) && !held.ContainsKey(kv.Key) && kv.Value.Any(l => l.Verified != NodeProof.None))
            .Select(kv => Row(kv.Key))).ToList();
        var kept = told.Concat(linkOnly).Take(Math.Max(0, max)).ToList();
        var more = told.Count + linkOnly.Count - kept.Count;

        var listed = told.Concat(linkOnly).Select(r => r.Peer.Id).ToHashSet(StringComparer.Ordinal);
        var apart = links.Where(l => l.Verified == NodeProof.None && (l.Id is null || (!Own(l.Id) && !listed.Contains(l.Id))))
            .OrderBy(l => l.Kind, StringComparer.Ordinal).ThenBy(l => l.Id ?? "", StringComparer.Ordinal).ThenBy(l => l.ConnectedUtc).ToList();
        return new NetworkView(kept, more, apart);
    }

    /// <summary>The app a Show Bus kind names, for people: "Patterns", "AGMessage", "PlayMap", "Stagewatch", or the kind itself.</summary>
    public static string AppName(string? kind) => kind switch
    {
        "patterns" => "Patterns",
        "messenger" => "AGMessage",
        "playmap" => "PlayMap",
        "main" => "Stagewatch",
        null or "" => "show app",
        _ => kind,
    };

    /// <summary>
    /// The shape that says what proves a row's facts, drawn beside its light so it is never colour alone: ✓ a key (its own,
    /// or this Stagewatch's own Patterns link), △ the operator's credential, ○ nothing (unverified, or nothing spoke for it).
    /// </summary>
    public static string Mark(string verified) => verified switch { "key" => "✓", "operator" => "△", _ => "○" };

    /// <summary>What <see cref="Mark"/> means, for a tooltip and a screen reader.</summary>
    public static string MarkWords(string verified) => verified switch
    {
        "key" => "verified: it speaks with a node key of its own",
        "operator" => "verified by the operator's credential (the PIN or a control key), not a key of its own",
        "none" => "unverified: nothing it sent proves its id",
        _ => "unverified: heard on the network, nothing spoke for it",
    };

    /// <summary>What a row's mark means: <see cref="MarkWords"/>, but for a context pulled over this Stagewatch's own link, what proves it.</summary>
    public static string MarkTip(NetworkRow row) => row.Pulled && row.ContextProof == NodeProof.Key
        ? "verified: its context comes over this Stagewatch's own Patterns link, to the address the operator set — not a key of its own"
        : MarkWords(row.Peer.Verified);

    /// <summary>
    /// How a row's app reaches this Stagewatch, for the desk: its /ws link and the credential that proves it, else how its
    /// context came, else that it was only heard; how its context came when that is not what its link says; a context set
    /// aside; then any link that names its id and proves nothing, with why. <paramref name="keyName"/> names a key by its id
    /// (null when it is gone). Names and addresses only, never a secret.
    /// </summary>
    public static string LinkLine(NetworkRow row, Func<string, string?> keyName)
    {
        var parts = new List<string>();
        if (row.Link is { } l)
        {
            parts.Add("follows over /ws");
            parts.Add(Credential(l));
            if (l.Address.Length > 0) parts.Add(l.Address);
            if (l.Links > 1) parts.Add(l.Links.ToString(CultureInfo.InvariantCulture) + " links");
            if (row.Context is { } c && (row.Pulled || row.ContextProof != l.Verified)) parts.Add(ContextWords(row, c, keyName));
            if (row.ClaimSetAside) parts.Add(SetAside);
        }
        else
        {
            parts.Add(row.Context is { } c ? ContextWords(row, c, keyName) : "heard on the network");
            // A link that names its id and proves nothing says so below; "not linked" would contradict it.
            if (row.Unproven.Count == 0) parts.Add("not linked on /ws");
        }
        foreach (var u in row.Unproven) parts.Add(ApartLine(u));
        return string.Join(" · ", parts);
    }

    private static string ContextWords(NetworkRow row, PeerContexts.Entry c, Func<string, string?> keyName)
    {
        if (row.Pulled)
        {
            var over = "its context comes over this Stagewatch's Patterns link" + (c.Address.Length > 0 ? $" ({c.Address})" : "");
            return row.ContextProof == NodeProof.None ? over + ", but a key speaks for its id: it proves nothing, so it sets no risk" : over;
        }
        return row.ContextProof switch
        {
            NodeProof.Key => c.KeyId is { } k && keyName(k) is { } n ? $"posts its context with key '{n}'" : "posts its context with its node key",
            NodeProof.Operator => "posts its context with the operator's credential",
            _ => "posts its context unverified, so it sets no risk",
        };
    }

    /// <summary>A node link that proves nothing for its id, or names none, for the desk.</summary>
    public static string ApartLine(NodeLinkFact link)
    {
        var from = link.Address.Length > 0 ? " from " + link.Address : "";
        return link.Id is null
            ? $"on /ws{from} with key '{link.KeyName}', which names no node id: it follows the timer and speaks for no node"
            : $"on /ws{from}, unverified: {link.Words ?? NodeLinks.NoKey}";
    }

    /// <summary>The desk's row an apart link goes under: the id a conflict named, or "key:" and the key's id when it names none.</summary>
    public static string ApartKey(NodeLinkFact link) => link.Id ?? "key:" + link.KeyId;

    /// <summary>The desk's rows for <see cref="NetworkView.Apart"/>: one per id a conflict named, one per key that names no id.</summary>
    public static IReadOnlyList<ApartRow> ApartRows(IReadOnlyList<NodeLinkFact> apart)
        => apart.GroupBy(ApartKey, StringComparer.Ordinal).Select(g =>
        {
            var first = g.First();
            var name = first.Id is { } id ? $"{AppName(first.Kind)} {id}"
                : first.KeyName.Length > 0 ? $"Key '{first.KeyName}'" : "A revoked key";
            return new ApartRow(g.Key, first.Kind, first.Id, name, first.Id is null ? ApartNoId : ApartUnproven, string.Join(" · ", g.Select(ApartLine)));
        }).ToList();

    /// <summary>
    /// The Show network's headline on the desk: the apps listed, the red ones, the keys refused (<paramref name="refused"/>),
    /// the rows apart (unverified on /ws) and how many more than the cap; "" when there is nothing at all.
    /// <paramref name="refusalOnly"/> counts the desk's rows for refused apps no longer heard.
    /// </summary>
    public static string NetworkLine(NetworkView view, int refused, int refusalOnly)
    {
        var apps = view.Rows.Count;
        var reds = view.Rows.Count(r => r.Peer.Light == Light.Red);
        var apart = view.Apart.Select(ApartKey).Distinct(StringComparer.Ordinal).Count();
        if (apps + apart + refusalOnly + view.More == 0) return "";
        return $"Show network: {apps.ToString(CultureInfo.InvariantCulture)} app{(apps == 1 ? "" : "s")}"
               + (reds > 0 ? $" · {reds.ToString(CultureInfo.InvariantCulture)} red" : "")
               + (refused > 0 ? $" · {refused.ToString(CultureInfo.InvariantCulture)} key refused" : "")
               + (apart > 0 ? $" · {apart.ToString(CultureInfo.InvariantCulture)} unverified on /ws" : "")
               + (view.More > 0 ? $" · +{view.More.ToString(CultureInfo.InvariantCulture)} more" : "");
    }

    /// <summary>
    /// The rows' red and amber parts as advice for the producer (they need it even with no AI), two per app, from the
    /// same rows every other reader takes. A context that proves nothing is labelled "unverified:" and one level lower, so
    /// a claim never raises an urgent alarm.
    /// </summary>
    public static IReadOnlyList<Advice> PeerAdvice(NetworkView view)
    {
        var list = new List<Advice>();
        foreach (var r in view.Rows)
        {
            if (r.Context is not { } e) continue;
            var claim = r.ContextProof == NodeProof.None;
            foreach (var item in e.Items.Where(x => x.Light is Light.Red or Light.Amber).OrderByDescending(x => Light.Rank(x.Light)).Take(2))
            {
                var level = item.Light == Light.Red ? AdviceLevel.Urgent : AdviceLevel.Warn;
                if (claim) level = level == AdviceLevel.Urgent ? AdviceLevel.Warn : AdviceLevel.Info;
                list.Add(new Advice($"peer:{e.Id}:{item.Id}", level, $"{(claim ? "unverified: " : "")}{r.Peer.Name} — {item.Label}: {item.State}", null, null));
            }
        }
        return list;
    }

    private static string Credential(NodeLinkFact l) => l.Verified switch
    {
        NodeProof.Key => $"key '{l.KeyName}'",
        NodeProof.Operator when l.KeyId is not null => $"control key '{l.KeyName}' (the operator's, not its own)",
        NodeProof.Operator => "the PIN (the operator's, not a key of its own)",
        _ => "unverified",
    };
}
