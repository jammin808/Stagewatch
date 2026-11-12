using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// A node this Stagewatch was set up to work with, so it is expected in the room: a playout link's peer, or the node a node key is
/// bound to. <see cref="Why"/> names the relationship as people read it ("playout link stage-left", "node key 'AGM'"). A name, never
/// a secret.
/// </summary>
public sealed record ConfiguredNode(string Id, string Kind, string Name, string Why)
{
    /// <summary>The relationship is a node key bound to the id: the node is expected for as long as that key speaks for it.</summary>
    public bool ByKey { get; init; }
}

/// <summary>A node this Stagewatch heard, and when it last did (<see cref="SeenNodes"/>): what the preflight says of it once it is gone.</summary>
public sealed record SeenNode(string Id, string Kind, string Name, DateTime HeardUtc);

/// <summary>
/// What the preflight is judged from, gathered on the owner thread at <see cref="NowUtc"/>: the show network as every other reader
/// takes it (the heard table and the contexts, joined, each context with what it proves), the nodes heard before
/// (<see cref="SeenNodes"/>), the configured relationships, this Stagewatch's own link and window items, and the node keys refused
/// within the time a context is kept. <see cref="Clock"/> tells the times a row names ("went away at 21:40").
/// </summary>
public sealed record PreflightInput(DateTime NowUtc, NetworkView Network, IReadOnlyList<SeenNode> Seen, IReadOnlyList<ConfiguredNode> Configured,
    IReadOnlyList<ContextItem> Links, IReadOnlyList<ContextItem> Windows, ShowClock Clock)
{
    /// <summary>
    /// The keyed requests refused, each under the id the request named (<see cref="NodeBindings.RefusedSince"/>) — which the refused
    /// key does not speak for, so a refusal says nothing of that node by itself: it is judged beside what the node proves.
    /// </summary>
    public IReadOnlyList<NodeRefusal> Refused { get; init; } = Array.Empty<NodeRefusal>();

    /// <summary>The keys in force by id, with the names Settings gives them: how a row names the key that was refused. Names, never a secret.</summary>
    public IReadOnlyDictionary<string, string> KeyNames { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// The room's verdict at one moment: ready while no row is red (amber is counted, listed and never blocks), the counts over every
/// row, the verdict in words, and the rows — red, amber, green, then by name — at most <see cref="PreflightWire.MaxRows"/>.
/// </summary>
public sealed record PreflightResult(long AtMs, bool Ready, int Red, int Amber, int Green, string Words, IReadOnlyList<PreflightRow> Rows)
{
    /// <summary>The verdict as judged: ready, the counts, never stuck (stuck is what a door or a frame says in its place).</summary>
    public NetworkVerdict Verdict => new(Ready, Red, Amber, Stuck: false);

    /// <summary>The verdict as an operator frame built at <paramref name="nowMs"/> carries it (<see cref="PreflightWire.Carried"/>): the stuck one once this has gone stale.</summary>
    public NetworkVerdict VerdictAt(long nowMs) => PreflightWire.Carried(Verdict, AtMs, nowMs)!;

    /// <summary>The route's JSON (<see cref="PreflightWire.Write"/>).</summary>
    public string ToJson() => PreflightWire.Write(AtMs, Ready, stuck: false, Red, Amber, Green, Words, Rows);
}

/// <summary>
/// The nodes this Stagewatch heard and when it last heard each, so one that goes away is still named: for
/// <see cref="KeepFor"/> after its last word when nothing configured names it, and for as long as something does. At most
/// <see cref="Max"/>, the one heard longest ago forgotten first, a configured one last. Owner thread.
/// </summary>
public sealed class SeenNodes
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(10);
    public const int Max = 32;

    private readonly Dictionary<string, SeenNode> _byId = new(StringComparer.Ordinal);

    /// <summary>
    /// Take this second's join: every row's node is heard at <paramref name="nowUtc"/> less its age. An id two machines say is not
    /// noted (its hello speaks for neither). Returns what is kept now.
    /// </summary>
    public IReadOnlyList<SeenNode> Note(NetworkView view, IReadOnlyCollection<string> configured, DateTime nowUtc)
    {
        foreach (var r in view.Rows)
        {
            if (!NetworkPreflight.Heard(r, nowUtc, out var heard)) continue;
            if (_byId.TryGetValue(r.Peer.Id, out var had) && had.HeardUtc > heard) heard = had.HeardUtc;
            _byId[r.Peer.Id] = new SeenNode(r.Peer.Id, r.Peer.Kind, r.Peer.Name, heard);
        }
        bool Kept(string id) => configured.Contains(id);
        foreach (var id in _byId.Values.Where(s => !Kept(s.Id) && nowUtc - s.HeardUtc > KeepFor).Select(s => s.Id).ToList()) _byId.Remove(id);
        while (_byId.Count > Max)
            _byId.Remove(_byId.Values.OrderBy(s => Kept(s.Id)).ThenBy(s => s.HeardUtc).ThenBy(s => s.Id, StringComparer.Ordinal).First().Id);
        return _byId.Values.ToList();
    }
}

/// <summary>
/// One preflight for the room: 'ready for doors', with a reason per node. Each node configured or seen is checked for whether it is
/// heard, whether its context is held and fresh, what its own parts say and what proves its posts; each of this Stagewatch's links
/// and windows by its own light. What a person configured and is gone, or was never heard, is red; what was only seen and went
/// away is amber, and after <see cref="SeenNodes.KeepFor"/> not listed: nothing a person did not configure turns the verdict red by
/// leaving. A context that proves nothing is never read for its node's parts, and a key refused for an id is red only for a node
/// that is expected or heard and has nothing of its own taken, so neither a claim nor another node's key closes the doors on a
/// node that proves itself. What cannot be judged is amber, never guessed green or red. Read-only, and never an item of this
/// Stagewatch's own context. Pure, on the time it is given.
/// </summary>
public static class NetworkPreflight
{
    /// <summary>The most nodes checked; the rest are counted in one amber row.</summary>
    public const int MaxNodes = 32;

    /// <summary>Show Bus liveness: live within <see cref="LiveSeconds"/>, stale to <see cref="StaleSeconds"/>, gone after.</summary>
    public const double LiveSeconds = 6, StaleSeconds = 12;

    /// <summary>A context is fresh to this age; older, until it is no longer kept (<see cref="PeerContexts.KeepFor"/>), it is amber.</summary>
    public const double ContextFreshSeconds = 15;

    /// <summary>A row's words are at most this long, and its name this long.</summary>
    public const int WordsChars = 240, NameChars = 80;

    /// <summary>The red and amber rows the copilot is given.</summary>
    public const int AiRows = 12;

    public const string HeardCheck = "heard", ContextCheck = "context", OwnCheck = "own", KeyCheck = "key", LinkCheck = "link", ClockCheck = "clock", WindowCheck = "window";

    /// <summary>The id of the one row that counts the nodes past <see cref="MaxNodes"/>.</summary>
    public const string MoreId = "network:more";

    /// <summary>
    /// A row's id is <c>node:&lt;id&gt;:&lt;check&gt;</c>, one per node and check; the row for an id two machines say is a
    /// <see cref="HeardCheck"/> row of its own, <c>node:&lt;id&gt;:conflict</c>, beside the node's heard row when a context or a
    /// link still gives it one. Its words quote both machines' hellos.
    /// </summary>
    public static bool IsConflict(PreflightRow row) => row.Node.Length > 0 && row.Id == $"node:{row.Node}:conflict";

    /// <summary>When a row's node was last heard, by its hello, its context or an open link; false for a row that tells no age.</summary>
    internal static bool Heard(NetworkRow row, DateTime nowUtc, out DateTime heardUtc)
    {
        var age = row.Peer.AgeSeconds;
        var told = double.IsFinite(age) && age is >= 0 and < 1e9;
        heardUtc = told ? nowUtc - TimeSpan.FromSeconds(age) : default;
        return told;
    }

    /// <summary>
    /// The configured relationships, pure: each playout link whose peer's id is known (its settings', else what its peer said), and
    /// each node key bound to an id, by the names Settings gives them. This Stagewatch's own id is never one.
    /// </summary>
    public static IReadOnlyList<ConfiguredNode> Configured(IEnumerable<(string Name, string? Kind, string? NodeId, string Peer)> playoutLinks,
        IEnumerable<ApiKey> keys, NodeBindings nodes, string? ownId)
    {
        var list = new List<ConfiguredNode>();
        foreach (var l in playoutLinks)
            if (NodeBindings.ValidId(l.NodeId) && l.NodeId != ownId)
                list.Add(new ConfiguredNode(l.NodeId!, l.Kind ?? "", l.Peer, "playout link " + l.Name));
        foreach (var k in keys)
            if (k.Scope == ApiKeyScope.Node && nodes.NodeOf(k.Id) is (var kind, var id) && nodes.KeyOf(id) == k.Id && id != ownId)
                list.Add(new ConfiguredNode(id, kind ?? "", "", $"node key '{LineFence.Shown(k.Name, 60)}'") { ByKey = true });
        return list;
    }

    /// <summary>The keys' names by id, as <see cref="PreflightInput.KeyNames"/> takes them; an id listed twice keeps its last name.</summary>
    public static IReadOnlyDictionary<string, string> KeyNames(IEnumerable<ApiKey> keys)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var k in keys) names[k.Id] = k.Name;
        return names;
    }

    private sealed class Node(string id)
    {
        public string Id { get; } = id;
        public NetworkRow? Row;
        public SeenNode? Seen;
        public HeardNode? Conflict;
        public NodeRefusal? Refused;
        public List<ConfiguredNode> Configured { get; } = [];
        public bool IsConfigured => Configured.Count > 0;
    }

    public static PreflightResult Run(PreflightInput input)
    {
        var now = input.NowUtc;
        var nodes = new List<Node>();
        var byId = new Dictionary<string, Node>(StringComparer.Ordinal);
        Node Of(string id)
        {
            if (!byId.TryGetValue(id, out var n))
            {
                n = new Node(id);
                byId[id] = n;
                nodes.Add(n);
            }
            return n;
        }
        // Configured first: past this cap it is the nodes nobody asked for that go unchecked. A configured node the join itself left
        // out (NetworkView.More) has no row to judge: it is amber, not checked, never red by a guess (NodeRows).
        foreach (var c in input.Configured) Of(c.Id).Configured.Add(c);
        foreach (var r in input.Network.Rows) Of(r.Peer.Id).Row = r;
        foreach (var c in input.Network.Conflicts) Of(c.Id).Conflict = c;
        foreach (var r in input.Refused.Where(r => NodeBindings.ValidId(r.Id)).GroupBy(r => r.Id, StringComparer.Ordinal).Select(g => g.MaxBy(r => r.AtUtc))) Of(r.Id).Refused = r;
        foreach (var s in input.Seen.OrderByDescending(s => s.HeardUtc))
        {
            // Only seen, and gone longer than a seen node is kept: not listed at all.
            if (!byId.ContainsKey(s.Id) && now - s.HeardUtc > SeenNodes.KeepFor) continue;
            Of(s.Id).Seen = s;
        }

        var rows = new List<PreflightRow>();
        foreach (var n in nodes.Take(MaxNodes)) NodeRows(n, input, rows);
        var more = Math.Max(0, nodes.Count - MaxNodes) + input.Network.More;
        if (more > 0)
            rows.Add(new PreflightRow(MoreId, "", "Show network", "", HeardCheck, Light.Amber, "",
                $"{Number(more)} more node{(more == 1 ? "" : "s")} not checked", $"the preflight checks {Number(MaxNodes)} nodes"));
        foreach (var item in input.Links) rows.Add(Own(item, item.Id.StartsWith("clock:", StringComparison.Ordinal) ? ClockCheck : LinkCheck));
        foreach (var item in input.Windows) rows.Add(Own(item, WindowCheck));

        var sorted = rows.OrderByDescending(r => Light.Rank(r.Light)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        int red = sorted.Count(r => r.Light == Light.Red), amber = sorted.Count(r => r.Light == Light.Amber);
        return new PreflightResult(new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)).ToUnixTimeMilliseconds(), red == 0, red, amber,
            sorted.Count - red - amber, Verdict(sorted, red, amber), sorted.Take(PreflightWire.MaxRows).ToList());
    }

    /// <summary>
    /// The verdict in words: "ready for doors"; "ready · 2 to look at"; "not ready: Stage left not heard for 14 s (+1 more) · 2 to
    /// look at" — the first red row (they sort first), how many more are red, and how many are amber.
    /// </summary>
    private static string Verdict(IReadOnlyList<PreflightRow> sorted, int red, int amber)
    {
        var look = amber > 0 ? $" · {Number(amber)} to look at" : "";
        if (red == 0) return amber == 0 ? "ready for doors" : "ready" + look;
        var first = sorted[0];
        return $"not ready: {(first.Name.Length > 0 ? first.Name : first.Id)} {first.Words}" + (red > 1 ? $" (+{Number(red - 1)} more)" : "") + look;
    }

    /// <summary>
    /// The desk's Show network header with the verdict: the network's own line (<see cref="ShowNetwork.NetworkLine"/>) and the
    /// verdict's words; with no app listed, the verdict alone. A verdict is always said, so the desk is in step with the page's chip
    /// and the deck's key and its Copy the preflight is always there; before the first is judged, the network's line alone.
    /// </summary>
    public static string Line(string networkLine, PreflightResult? result)
    {
        if (result is null) return networkLine;
        return (networkLine.Length > 0 ? networkLine + " · " : "Show network: ") + result.Words;
    }

    /// <summary>The rows as a tooltip and the clipboard take them: the verdict, then one line per row, its light in words first.</summary>
    public static string Text(PreflightResult result)
    {
        var lines = new List<string>(result.Rows.Count + 1) { result.Words };
        foreach (var r in result.Rows)
            lines.Add($"{Mark(r.Light)} {r.Light}  {(r.Name.Length > 0 ? r.Name : r.Id)} · {r.Check}: {r.Words}"
                      + (r.Configured.Length > 0 ? $" ({r.Configured})" : "") + (r.Fix.Length > 0 ? $" — {r.Fix}" : ""));
        return string.Join("\n", lines);
    }

    /// <summary>A light's shape, drawn beside its word so it is never colour alone: ✕ red, △ amber, ✓ green.</summary>
    public static string Mark(string light) => light switch { Light.Red => "✕", Light.Amber => "△", _ => "✓" };

    private static void NodeRows(Node n, PreflightInput input, List<PreflightRow> rows)
    {
        var now = input.NowUtc;
        var row = n.Row;
        var first = n.Configured.FirstOrDefault();
        var kind = row?.Peer.Kind is { Length: > 0 } rk ? rk : n.Seen?.Kind is { Length: > 0 } sk ? sk : n.Conflict?.Kind is { Length: > 0 } ck ? ck
            : first?.Kind is { Length: > 0 } fk ? fk : n.Refused?.Kind ?? "";
        var app = ShowNetwork.AppName(kind);
        var name = LineFence.Shown(row?.Peer.Name ?? n.Seen?.Name ?? (n.Conflict is { Name.Length: > 0 } c ? c.Name : null)
                                   ?? n.Configured.Select(x => x.Name).FirstOrDefault(x => x.Length > 0) ?? "", NameChars);
        if (name.Length == 0) name = $"{app} {n.Id}";
        var configured = string.Join(" · ", n.Configured.Select(x => x.Why).Distinct(StringComparer.Ordinal));
        // One row per node and check: no two rows share an id.
        PreflightRow Row(string check, string light, string words, string fix = "", string? id = null)
            => new($"node:{n.Id}:{id ?? check}", n.Id, name, kind, check, light, configured, Clip(words), fix);
        var running = $"is {app} running and on this network?";
        // A node only a node key makes expected may be one that no longer exists (a rental returned, an id from before a reinstall):
        // its red says the other way out too.
        var missing = n.IsConfigured && n.Configured.All(x => x.ByKey)
            ? running + " If it was retired or reinstalled: revoke its key, or let that key speak for the new node (Settings → Remote)"
            : running;

        // heard: by its hello, its context or an open link, whichever is newest. An id two machines say is heard from neither.
        var gone = true;
        if (n.Conflict is { } both)
            rows.Add(Row(HeardCheck, Light.Amber, both.Conflict, "a copied install keeps the id it was copied with: give one of them a new id", "conflict"));
        if (row is not null && Heard(row, now, out var heardUtc))
        {
            var age = (now - heardUtc).TotalSeconds;
            gone = age > StaleSeconds;
            if (age <= LiveSeconds) rows.Add(Row(HeardCheck, Light.Green, "live"));
            else if (!gone) rows.Add(Row(HeardCheck, Light.Amber, $"not heard for {Seconds(age)} s", running));
            else rows.Add(Gone(heardUtc));
        }
        // The join lists only so many apps: a node it left out has no row to judge, so whether it is there is not known.
        else if (row is null && input.Network.More > 0 && n.Conflict is null && (n.IsConfigured || n.Seen is not null))
            rows.Add(Row(HeardCheck, Light.Amber, $"not checked: the show network lists {Number(input.Network.Rows.Count)} apps and it is not among them"));
        else if (n.Seen is { } seen) rows.Add(Gone(seen.HeardUtc));
        else if (n.IsConfigured && n.Conflict is null) rows.Add(Row(HeardCheck, Light.Red, "never heard", missing));

        // A node no longer listed, by when it was last heard: one only seen went away (amber); a configured one is stale until the Show
        // Bus calls it gone, then red — whether its row is still listed or left the join with its link or its last word.
        PreflightRow Gone(DateTime lastUtc)
        {
            var at = input.Clock.ToShow(lastUtc).ToString("HH:mm", CultureInfo.InvariantCulture);
            if (!n.IsConfigured) return Row(HeardCheck, Light.Amber, $"went away at {at}");
            var quiet = Math.Max(0, (now - lastUtc).TotalSeconds);
            if (quiet <= StaleSeconds) return Row(HeardCheck, Light.Amber, $"not heard for {Seconds(quiet)} s", running);
            return Row(HeardCheck, Light.Red, quiet <= 120 ? $"not heard for {Seconds(quiet)} s" : $"not heard since {at}", missing);
        }

        // key: one row. What proves the node's posts; and a keyed request refused for its id, judged beside that — the refusal is
        // recorded under the id the request named, which any node's key can name, so it is red only where nothing of the node's
        // own is taken.
        var live = row is not null && !gone;
        var proves = !live ? null
            : row!.Context is not null && row.ContextProof != NodeProof.None
                ? row.Pulled ? "read over this Stagewatch's own link"
                    : row.ContextProof == NodeProof.Key ? "posts with its own node key" : "posts with the operator's credential (the PIN or a control key), not a key of its own"
                : row.Link is { } link ? link.Verified == NodeProof.Key ? "follows over /ws with its own node key" : "follows over /ws with the operator's credential"
                : null;
        const string makeKey = "give it a node key of its own (Make a key for… on its Show network row)";
        PreflightRow? key = null;
        if (n.Refused is { } refused)
        {
            var which = input.KeyNames.TryGetValue(refused.KeyId, out var keyName) && keyName.Length > 0 ? $"the key '{LineFence.Shown(keyName, 60)}'" : "a key";
            var hasKey = n.Configured.Any(x => x.ByKey);
            if (proves is not null)
                key = Row(KeyCheck, Light.Amber, $"{proves}; {which} was refused for this id: {refused.Words}",
                    hasKey || row!.ContextProof == NodeProof.Key ? "another app names this id with a key that is not this node's: give that app a key of its own" : makeKey);
            else if (live || n.IsConfigured)
                key = Row(KeyCheck, Light.Red, $"{which} was refused for it, and nothing of its own is taken: {refused.Words}",
                    hasKey ? "it has a node key: paste that key into it, or let the refused key speak for it (Let a key speak for this node… on its Show network row)" : makeKey);
            else
                key = Row(KeyCheck, Light.Amber, $"{which} was refused for this id, which is neither set up nor heard: {refused.Words}");
        }
        // A context this Stagewatch pulled over its own link is proved by the link, and says so there.
        else if (live && row is { Context: not null, Pulled: false } && row.ContextProof != NodeProof.None)
            key = row.ContextProof == NodeProof.Key
                ? Row(KeyCheck, Light.Green, proves!)
                : Row(KeyCheck, Light.Amber, proves!, "make it a node key (Settings → Remote → New key…)");
        if (key is not null) rows.Add(key);

        // A node that is gone says one thing more: what it last told is no longer what it is.
        if (row is null || gone) return;

        if (row.ClaimSetAside)
            rows.Add(Row(ContextCheck, Light.Amber, "a context that names its id proves nothing: set aside, not read"));
        else if (row.Context is not { } context)
            rows.Add(Row(ContextCheck, Light.Amber, row.TooBig is { } big ? ShowNetwork.TooBigWords(big, held: false) : "no context held"));
        else if (row.ContextProof == NodeProof.None)
            rows.Add(Row(ContextCheck, Light.Amber, "its context is unverified: its own parts are not read", makeKey));
        else
        {
            var age = Math.Max(0, (now - context.AtUtc).TotalSeconds);
            rows.Add(age <= ContextFreshSeconds
                ? Row(ContextCheck, Light.Green, "context up to date")
                : Row(ContextCheck, Light.Amber, $"context {Seconds(age)} s old"));
            rows.Add(OwnParts(context, (check, light, words, fix) => Row(check, light, words, fix)));
        }
    }

    /// <summary>
    /// A node's own worst part: the worst light among its context's light and its items', with its two worst parts in its own words.
    /// Red is red and amber amber (a PlayMap blacked out before doors is amber, and normal). Green only when the context itself
    /// says green: one that tells no light of its own is amber whatever its items say — unknown, never guessed green.
    /// </summary>
    private static PreflightRow OwnParts(PeerContexts.Entry context, Func<string, string, string, string, PreflightRow> row)
    {
        var worst = Light.Worst(context.Items.Select(i => i.Light).Append(context.Light));
        if (worst is not (Light.Red or Light.Amber))
            return Light.Clean(context.Light) == Light.Green
                ? row(OwnCheck, Light.Green, "its own parts are green", "")
                : row(OwnCheck, Light.Amber, "it tells no light of its own", "");
        var parts = context.Items.Where(i => i.Light == worst).Take(2).Select(i => $"{i.Label}: {i.State}").ToList();
        return row(OwnCheck, worst, parts.Count > 0 ? string.Join(" · ", parts) : context.Headline.Length > 0 ? context.Headline : $"its own light is {worst}", "");
    }

    /// <summary>One of this Stagewatch's own links or windows, by its item's own light; grey (unknown, a window nobody opened) is amber.</summary>
    private static PreflightRow Own(ContextItem item, string check)
        => new(item.Id, "", LineFence.Shown(item.Label, NameChars), item.Kind, check, item.Light is Light.Red or Light.Amber or Light.Green ? item.Light : Light.Amber, "", Clip(item.State), "");

    /// <summary>Words as a row shows them: nothing a line would be refused for (<see cref="LineFence.Shown"/>), at most <see cref="WordsChars"/>.</summary>
    private static string Clip(string words) => LineFence.Shown(words, WordsChars);

    private static string Seconds(double seconds) => Math.Floor(seconds).ToString("0", CultureInfo.InvariantCulture);

    private static string Number(int n) => n.ToString(CultureInfo.InvariantCulture);
}
