using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stagewatch.Core;

/// <summary>A light (Show Bus §6): worst wins — red, amber, green, grey (unknown, never guessed).</summary>
public static class Light
{
    public const string Red = "red", Amber = "amber", Green = "green", Grey = "grey";

    public static int Rank(string? light) => light switch { Red => 3, Amber => 2, Green => 1, _ => 0 };

    public static string Worst(IEnumerable<string> lights)
    {
        var worst = Grey;
        foreach (var l in lights) if (Rank(l) > Rank(worst)) worst = l;
        return worst;
    }

    public static string Clean(string? light) => light is Red or Amber or Green ? light : Grey;
}

/// <summary>
/// How a Show Bus body from another app is read: JSON whose objects name each key once. A repeated key, at any depth, is
/// malformed (a <see cref="JsonException"/> from the parse itself, before anything reads the body), so a server's gate and
/// the store behind it can never read two different values for one key, and no lookup meets the duplicate-key
/// <see cref="ArgumentException"/> a lenient parse defers to the first read.
/// </summary>
public static class BusJson
{
    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };

    /// <summary>The body as a node, or a <see cref="JsonException"/> when it isn't JSON or repeats a key.</summary>
    public static JsonNode? Parse(string json) => JsonNode.Parse(json, documentOptions: Strict);
}

/// <summary>One part of a node, as the Eye and an AI read it (Show Bus §6 "items").</summary>
public sealed record ContextItem(string Id, string Kind, string Label, string Light, string State);

/// <summary>A link between two parts (Show Bus §6 "links").</summary>
public sealed record ContextLink(string From, string To, string Kind, string Light);

/// <summary>A peer as one line: its light and headline, one hop only.</summary>
public sealed record PeerSummary(string Kind, string Id, string Name, string Light, string Headline, string Address, double AgeSeconds, bool HasContext)
{
    /// <summary>Show Bus liveness: live (heard within 6 s), stale (6–12 s), gone (over 12 s).</summary>
    public string Liveness => AgeSeconds <= 6 ? "live" : AgeSeconds <= 12 ? "stale" : "gone";

    /// <summary>"ws" while a node link on /ws proves this id (by a key or the operator's credential); "" otherwise.</summary>
    public string Link { get; init; } = "";

    /// <summary>
    /// What proves the facts this row shows, as the wire says it (key, operator or none): its context's proof when the
    /// light and headline are a context's (one this Stagewatch pulled over its own Patterns link counts as key while no
    /// key speaks for the id), else its proven link's; none when only links that prove nothing name it; "" when nothing
    /// spoke for it (a hello alone). A context verified "none" is held and shown, but sets no risk.
    /// </summary>
    public string Verified { get; init; } = "";
}

/// <summary>
/// The contexts other show apps posted here or that we fetched (Show Bus §6), kept for 30 s, validated and size-capped:
/// anything on the network can post anything. A context is taken only from a credential that may speak for its node
/// (<see cref="NodeBindings"/>), and keeps the proof it came with. Thread-safe.
/// This is the one home of the numbers Stagewatch reads another app's context by (§14's <c>context.</c> rows, as a reader):
/// every reader of a peer's context takes them from here.
/// </summary>
public sealed class PeerContexts
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(30);

    /// <summary>Each held list of commands with its compiled table, released with the list.</summary>
    private static readonly ConditionalWeakTable<IReadOnlyList<PeerCommand>, VerbTable> Compiled = new();

    /// <summary>The most peers held, and the most UTF-8 bytes of one context: the store and /api/peers/context refuse a longer one.</summary>
    public const int MaxPeers = 32, MaxBytes = 64 * 1024;

    /// <summary>The items kept of a context, its first (§14 <c>context.items</c>).</summary>
    public const int MaxItems = 200;

    /// <summary>An item's id, kind, label and state are kept to these many characters (§14 <c>context.item.*.chars</c>).</summary>
    public const int ItemIdChars = 60, ItemKindChars = 30, ItemLabelChars = 80, ItemStateChars = 160;

    /// <summary>A headline is kept to this many characters (§14 <c>context.headline.chars</c>).</summary>
    public const int HeadlineChars = 200;

    /// <summary>The recent lines kept, its last, each to <see cref="RecentChars"/> characters (§14 <c>context.recent</c>, <c>context.recent.chars</c>).</summary>
    public const int MaxRecent = 20, RecentChars = 200;

    /// <summary>
    /// The commands kept, in the order sent (§14 <c>context.commands</c>): a verb over <see cref="MaxVerbChars"/> is dropped whole,
    /// never cut, and counted, and so is every one past this many kept.
    /// </summary>
    public const int MaxCommands = 256;

    /// <summary>The longest verb kept (§14 <c>context.verb.chars</c>): a longer one is a different syntax cut, so it is dropped.</summary>
    public const int MaxVerbChars = 512;

    /// <summary>A command's words are kept whole to the most a sender may send (§14 <c>context.words.chars</c>), above the 200 a reader must keep.</summary>
    public const int MaxWordsChars = 512;

    /// <summary>The links kept, the first that keep §6's link rules (§14 <c>context.links</c>); a link past them is counted.</summary>
    public const int MaxLinks = 400;

    /// <summary>A link end is at most this many characters (§14 <c>context.link.end.chars</c>).</summary>
    public const int LinkEndChars = 120;

    /// <summary>The fewest characters of the node id an end names after its "@" (§2's <c>hello.id.min</c>).</summary>
    public const int EndIdMin = 6;

    public sealed record Entry(string Kind, string Id, string Name, string Light, string Headline, IReadOnlyList<ContextItem> Items,
        IReadOnlyList<string> Recent, string Json, string Address, DateTime AtUtc)
    {
        /// <summary>The verbs this node accepts (§6 "commands"), each whole, with its risk, its words and its secret's first word.</summary>
        public IReadOnlyList<PeerCommand> Commands { get; init; } = Array.Empty<PeerCommand>();

        /// <summary>
        /// The commands the context sent that are not kept: a verb that is empty, over <see cref="MaxVerbChars"/>, holds a control
        /// character or has no form (a bracket it opens never closes), or one past <see cref="MaxCommands"/>.
        /// </summary>
        public int CommandsDropped { get; init; }

        /// <summary>
        /// The links kept (§6 "links"), each end shaped as §6 says (<see cref="ValidEnd"/>): an item id of the node's own, or an end that
        /// names a node by its id. Ends are read for their shape only: an own end need not name an item the context lists, which may
        /// have been past <see cref="MaxItems"/>.
        /// </summary>
        public IReadOnlyList<ContextLink> Links { get; init; } = Array.Empty<ContextLink>();

        /// <summary>The links the context sent that are not kept: an end or a kind §6 does not allow, or one past <see cref="MaxLinks"/>.</summary>
        public int LinksDropped { get; init; }

        /// <summary>
        /// <see cref="Commands"/> compiled for rating a line (<see cref="VerbTable"/>): once per list of commands, so a copy of the
        /// entry with the same commands shares it and one with other commands compiles its own.
        /// </summary>
        public VerbTable Verbs => Compiled.GetValue(Commands, static c => VerbTable.Compile(c));

        /// <summary>
        /// How the poster proved it speaks for this node when it posted. What it still proves is <see cref="ProofOf"/>;
        /// <see cref="NodeProof.None"/> is held and shown, but sets no risk.
        /// </summary>
        public NodeProof Proof { get; init; }

        /// <summary>The node key it was posted with when <see cref="Proof"/> is <see cref="NodeProof.Key"/>, else null.</summary>
        public string? KeyId { get; init; }
    }

    /// <summary>Why a body is refused when <see cref="Parse"/> can't read a context from it (the server and the store say the same).</summary>
    public const string NotAContext = "not a Show Bus context (v 1, node.t, node.id; each key once)";

    private readonly NodeBindings _nodes;

    /// <summary>Contexts checked against <paramref name="nodes"/>: the same bindings every other node route speaks through.</summary>
    public PeerContexts(NodeBindings nodes) => _nodes = nodes;

    /// <summary>The newest context from a node, by id (null when none is held).</summary>
    public Entry? Find(string nodeId, DateTime nowUtc) => All(nowUtc).FirstOrDefault(e => e.Id == nodeId);

    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);

    /// <summary>
    /// Take a context from <paramref name="credential"/>: parsed, then spoken for (the bindings), then never over a stronger
    /// proof for the same node within <see cref="KeepFor"/>, then stored. Status 200 with the proof it is held with; 400 when
    /// it isn't a context this Stagewatch can hold; 403 when the credential may not speak for that node. A refusal stores
    /// nothing.
    /// </summary>
    public (int Status, string? Error, NodeProof Proof) Accept(string json, string address, DateTime nowUtc, string? ownId, NodeCredential credential)
    {
        // MaxBytes counts UTF-8 bytes, as the door does; a string longer in chars is over it without counting.
        if (json.Length is 0 or > MaxBytes || System.Text.Encoding.UTF8.GetByteCount(json) > MaxBytes) return (400, "a context is 1 byte to 64 KB", NodeProof.None);
        if (Parse(json, address, nowUtc) is not { } e) return (400, NotAContext, NodeProof.None);
        if (e.Id == ownId) return (400, "that's this Stagewatch", NodeProof.None);
        // Spoken for only when there is room for it, so a full store binds no key. The room is checked again when it is
        // stored: two posts racing for the last place can leave the loser's key bound to the node it named, which is that
        // key's own first proven use. A key's post is never refused after it spoke: no proof is stronger than a key.
        lock (_byId)
        {
            Prune(nowUtc);
            if (_byId.Count >= MaxPeers && !_byId.ContainsKey(e.Id)) return (400, "too many peers", NodeProof.None);
        }
        var said = _nodes.Speak(credential, e.Kind, e.Id, nowUtc);
        if (!said.Ok) return (403, said.Error, NodeProof.None);
        e = e with { Proof = said.Proof, KeyId = said.Proof == NodeProof.Key ? credential.KeyId : null };
        lock (_byId)
        {
            Prune(nowUtc);
            if (_byId.TryGetValue(e.Id, out var held) && ProofOf(held) > e.Proof)
                return (403, $"{NodeBindings.Describe(held.Kind, held.Id)} spoke with a stronger proof {NodeBindings.SecondsSince(held.AtUtc, nowUtc)} s ago", NodeProof.None);
            if (_byId.Count >= MaxPeers && !_byId.ContainsKey(e.Id)) return (400, "too many peers", NodeProof.None);
            _byId[e.Id] = e;
        }
        return (200, null, e.Proof);
    }

    /// <summary>
    /// What a held context still proves: a key's proof only while that key still speaks for the node (a revoked or moved
    /// key proves nothing from that moment), the operator's only while no key speaks for it (a key-bound id is spoken for
    /// with its key alone), and none stays none.
    /// </summary>
    public NodeProof ProofOf(Entry entry) => entry.Proof switch
    {
        NodeProof.Key => entry.KeyId is { } k && _nodes.KeyOf(entry.Id) == k ? NodeProof.Key : NodeProof.None,
        NodeProof.Operator => _nodes.KeyOf(entry.Id) is null ? NodeProof.Operator : NodeProof.None,
        _ => NodeProof.None,
    };

    /// <summary>
    /// What a context this Stagewatch pulled over its own link to an address the operator set proves for
    /// <paramref name="nodeId"/>: a key's proof while no key speaks for that id, nothing once one does (a key-bound id
    /// speaks with its key alone).
    /// </summary>
    public NodeProof LinkedProof(string nodeId) => _nodes.KeyOf(nodeId) is null ? NodeProof.Key : NodeProof.None;

    /// <summary>Every context held, each with what it proves now (<see cref="ProofOf"/>).</summary>
    public IReadOnlyList<HeldContext> Held(DateTime nowUtc) => All(nowUtc).Select(e => new HeldContext(e, ProofOf(e))).ToList();

    /// <summary>
    /// The context that speaks for a node now, as every reader takes it — the Show network's row, its light and words, and a proposal's
    /// risk (Show Bus §6, §11). Of <paramref name="pulled"/>, a context this Stagewatch pulled over its own link to the address the
    /// operator set (proved by <see cref="LinkedProof"/>), and the context held for the id (proved by <see cref="ProofOf"/>), the first
    /// that proves something now, the pulled one first; else the first of them, proving nothing; null with neither. A post that proves
    /// nothing never stands in front of a pulled context that proves something, and a pulled one proves nothing once a key speaks for
    /// the id. With <paramref name="kind"/>, only a context of that kind is taken: a context speaks for its own kind of node alone.
    /// </summary>
    public HeldContext? Chosen(string nodeId, HeldContext? pulled, DateTime nowUtc, string? kind = null)
    {
        var linked = pulled is { } p && p.Entry.Id == nodeId && (kind is null || p.Entry.Kind == kind) ? p with { Proof = LinkedProof(nodeId), Pulled = true } : null;
        var held = Find(nodeId, nowUtc) is { } e && (kind is null || e.Kind == kind) ? new HeldContext(e, ProofOf(e)) : null;
        if (linked is { Proof: not NodeProof.None }) return linked;
        if (held is { Proof: not NodeProof.None }) return held;
        return linked ?? held;
    }

    /// <summary>
    /// Every context that speaks for a node now, one per node id (<see cref="Chosen"/>): those held here and <paramref name="pulled"/>,
    /// the first pulled for an id standing for its link. What the Show network shows is what a proposal's risk reads.
    /// </summary>
    public IReadOnlyList<HeldContext> Speaking(IEnumerable<HeldContext> pulled, DateTime nowUtc)
    {
        var byId = new Dictionary<string, HeldContext>(StringComparer.Ordinal);
        foreach (var p in pulled) byId.TryAdd(p.Entry.Id, p);
        return All(nowUtc).Select(e => e.Id).Concat(byId.Keys).Distinct(StringComparer.Ordinal)
            .Select(id => Chosen(id, byId.GetValueOrDefault(id), nowUtc)).OfType<HeldContext>().ToList();
    }

    /// <summary>How risky a line is for a node: <see cref="VerdictFor"/>'s risk.</summary>
    public string RiskFor(NodeRef target, string line, DateTime nowUtc, Entry? pulled = null) => VerdictFor(target, line, nowUtc, pulled).Risk;

    /// <summary>
    /// How a line rates for the node <paramref name="target"/> names (Show Bus §11), by the whole forms of a verified table of that
    /// node's kind alone: the context <see cref="Chosen"/> takes for it — <paramref name="pulled"/>, a context this Stagewatch pulled
    /// over its own link to the node's address, first while it proves something — when it proves something now. Without one the line
    /// is high and unlisted, so an unverified context, or one of another kind, never lowers a risk; but a line that fills a secret
    /// placeholder of any table held for that id — pulled or posted, of any kind, proved or not — is still a secret (fail closed: a
    /// table no proof stands behind may refuse a line, never let one through).
    /// </summary>
    public RiskVerdict VerdictFor(NodeRef target, string line, DateTime nowUtc, Entry? pulled = null)
    {
        if (Chosen(target.Id, pulled is null ? null : new HeldContext(pulled, NodeProof.None, Pulled: true), nowUtc, target.Kind) is { Proof: not NodeProof.None } chosen)
            return chosen.Entry.Verbs.Rate(line);
        var unproved = new[] { pulled is { } p && p.Id == target.Id ? p : null, Find(target.Id, nowUtc) };
        return unproved.Any(e => e is not null && e.Verbs.Rate(line).Secret) ? RiskVerdict.Unlisted with { Secret = true } : RiskVerdict.Unlisted;
    }

    public IReadOnlyList<Entry> All(DateTime nowUtc)
    {
        lock (_byId)
        {
            Prune(nowUtc);
            return _byId.Values.OrderBy(e => e.Kind).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private void Prune(DateTime nowUtc)
    {
        foreach (var id in _byId.Where(kv => nowUtc - kv.Value.AtUtc > KeepFor).Select(kv => kv.Key).ToList()) _byId.Remove(id);
    }

    /// <summary>
    /// Pure: a §6 context → an entry, or null when it isn't one (a repeated key included). Its strings are capped; its first
    /// <see cref="MaxItems"/> items and last <see cref="MaxRecent"/> recent lines are kept; its commands as <see cref="ReadCommands"/>
    /// keeps them; its links as <see cref="ReadLinks"/> keeps them.
    /// </summary>
    public static Entry? Parse(string json, string address, DateTime nowUtc)
    {
        try
        {
            if (BusJson.Parse(json) is not JsonObject root) return null;
            if (root["v"]?.GetValueKind() != JsonValueKind.Number || root["v"]!.GetValue<double>() < 1) return null;
            if (root["node"] is not JsonObject node) return null;
            var kind = Str(node["t"], 16);
            var id = Str(node["id"], 40);
            if (!NodeBindings.ValidKind(kind) || !NodeBindings.ValidId(id)) return null;
            var items = (root["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(MaxItems)
                .Select(i => new ContextItem(Str(i["id"], ItemIdChars), Str(i["kind"], ItemKindChars), Str(i["label"], ItemLabelChars),
                    Light.Clean(Str(i["light"], 8)), Str(i["state"], ItemStateChars)))
                .ToList();
            var recent = (root["recent"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().TakeLast(MaxRecent).Select(r => Str(r["text"], RecentChars)).Where(t => t.Length > 0).ToList();
            var (commands, commandsDropped) = ReadCommands(root["commands"] as JsonArray);
            var (links, linksDropped) = ReadLinks(root["links"] as JsonArray);
            return new Entry(kind, id, Str(node["name"], 60), Light.Clean(Str(root["light"], 8)), Str(root["headline"], HeadlineChars), items, recent, json, address, nowUtc)
            { Commands = commands, CommandsDropped = commandsDropped, Links = links, LinksDropped = linksDropped };
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }   // a value of the wrong type
        catch (FormatException) { return null; }
    }

    /// <summary>
    /// A context's commands as this reader keeps them (§6): in the order sent, each verb whole or dropped — empty, over
    /// <see cref="MaxVerbChars"/>, holding a control character, or with no form (<see cref="VerbSyntax.Forms"/>: a bracket it opens
    /// never closes) — and the first <see cref="MaxCommands"/> of what is left kept, each counted when it is not; words
    /// kept to <see cref="MaxWordsChars"/>; a risk that is not low, review or high read as high; a <c>secret</c> that is not a whole
    /// number from 0 to <see cref="VerbSyntax.MaxWords"/> read as 0, so every word of a line by that row is its secret (fail closed).
    /// A command that is not an object is not one, and not counted.
    /// </summary>
    private static (List<PeerCommand> Kept, int Dropped) ReadCommands(JsonArray? sent)
    {
        var kept = new List<PeerCommand>();
        var dropped = 0;
        foreach (var c in (sent ?? new JsonArray()).OfType<JsonObject>())
        {
            var verb = Whole(c["verb"])?.Trim();
            if (verb is not { Length: > 0 and <= MaxVerbChars } || verb.Any(char.IsControl) || kept.Count >= MaxCommands || VerbSyntax.Forms(verb).Count == 0)
            {
                dropped++;
                continue;
            }
            int? secret = c["secret"] switch
            {
                null => null,
                JsonValue v when v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<int>(out var n) && n is >= 0 and <= VerbSyntax.MaxWords => n,
                _ => 0,
            };
            kept.Add(new PeerCommand(verb, VerbTable.Normal(Str(c["risk"], 8)), Str(c["words"], MaxWordsChars), secret));
        }
        return (kept, dropped);
    }

    /// <summary>
    /// A context's links as this reader keeps them (§6): the first <see cref="MaxLinks"/> whose ends keep §6's rules
    /// (<see cref="ValidEnd"/>) and whose kind is one of §6's (<see cref="ShowBusKinds.Links"/>), each counted when it is not kept.
    /// A link that is not an object is not one, and not counted.
    /// </summary>
    private static (List<ContextLink> Kept, int Dropped) ReadLinks(JsonArray? sent)
    {
        var kept = new List<ContextLink>();
        var dropped = 0;
        foreach (var l in (sent ?? new JsonArray()).OfType<JsonObject>())
        {
            var from = Whole(l["from"]);
            var to = Whole(l["to"]);
            var kind = Whole(l["kind"]);
            if (kept.Count >= MaxLinks || !ValidEnd(from) || !ValidEnd(to) || kind is null || !ShowBusKinds.Links.Contains(kind, StringComparer.Ordinal))
            {
                dropped++;
                continue;
            }
            kept.Add(new ContextLink(from!, to!, kind, Light.Clean(Str(l["light"], 8))));
        }
        return (kept, dropped);
    }

    /// <summary>
    /// Whether a link end keeps §6's rules: at most <see cref="LinkEndChars"/> characters, and either one of the node's own item ids
    /// (1 to <see cref="ItemIdChars"/> characters) or <c>@&lt;node id&gt;</c> or <c>@&lt;node id&gt;/&lt;item id&gt;</c>, the node id a §2
    /// id of <see cref="EndIdMin"/> to 40 letters, digits, '-' and '_', the item id 1 to <see cref="ItemIdChars"/> characters. No
    /// control character or white space is in any end.
    /// </summary>
    public static bool ValidEnd(string? end)
    {
        if (end is not { Length: > 0 and <= LinkEndChars } || end.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        if (end[0] != '@') return end.Length <= ItemIdChars;
        var slash = end.IndexOf('/', StringComparison.Ordinal);
        var node = slash < 0 ? end[1..] : end[1..slash];
        var item = slash < 0 ? null : end[(slash + 1)..];
        return node.Length >= EndIdMin && NodeBindings.ValidId(node) && item is null or { Length: > 0 and <= ItemIdChars };
    }

    /// <summary>A string value as sent, untrimmed and uncut, or null when it is not a string.</summary>
    private static string? Whole(JsonNode? n) => n is not null && n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null;

    private static string Str(JsonNode? n, int max)
    {
        if (n is null || n.GetValueKind() != JsonValueKind.String) return "";
        var s = n.GetValue<string>().Trim();
        s = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return s.Length > max ? s[..max] : s;
    }
}
