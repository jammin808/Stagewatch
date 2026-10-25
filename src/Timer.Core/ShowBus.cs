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
/// </summary>
public sealed class PeerContexts
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(30);
    public const int MaxPeers = 32, MaxBytes = 64 * 1024;

    public sealed record Entry(string Kind, string Id, string Name, string Light, string Headline, IReadOnlyList<ContextItem> Items,
        IReadOnlyList<string> Recent, string Json, string Address, DateTime AtUtc)
    {
        /// <summary>The verbs this node accepts with their risk (§6 "commands") — the approval desk shows its proposals with them.</summary>
        public IReadOnlyList<(string Verb, string Risk)> Commands { get; init; } = Array.Empty<(string, string)>();

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
        if (json.Length is 0 or > MaxBytes) return (400, "a context is 1 byte to 64 KB", NodeProof.None);
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
    /// How risky a line is for a node, from the verbs its context lists: high without a context, and high when its context
    /// proves nothing now (<see cref="ProofOf"/>), so an unverified context never lowers a risk. <paramref name="linked"/>
    /// is a context this Stagewatch pulled over its own link to the node's address, which the operator set; it counts
    /// only when no held context proves anything and it proves something itself (<see cref="LinkedProof"/>).
    /// </summary>
    public string RiskFor(string nodeId, string line, DateTime nowUtc, string? linked = null)
    {
        if (Find(nodeId, nowUtc) is { } held && ProofOf(held) != NodeProof.None) return ProposalDesk.RiskFromCommands(held.Commands, line);
        if (linked is not null && Parse(linked, "", nowUtc) is { } pulled && pulled.Id == nodeId && LinkedProof(nodeId) != NodeProof.None)
            return ProposalDesk.RiskFromCommands(pulled.Commands, line);
        return "high";
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

    /// <summary>Pure: a §6 context → an entry (strings capped), or null when it isn't one (a repeated key included).</summary>
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
            var items = (root["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(64)
                .Select(i => new ContextItem(Str(i["id"], 60), Str(i["kind"], 30), Str(i["label"], 80), Light.Clean(Str(i["light"], 8)), Str(i["state"], 160)))
                .ToList();
            var recent = (root["recent"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().TakeLast(20).Select(r => Str(r["text"], 200)).Where(t => t.Length > 0).ToList();
            var commands = (root["commands"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(64)
                .Select(c => (Str(c["verb"], 80), Str(c["risk"], 8))).Where(c => c.Item1.Length > 0).ToList();
            return new Entry(kind, id, Str(node["name"], 60), Light.Clean(Str(root["light"], 8)), Str(root["headline"], 200), items, recent, json, address, nowUtc)
            { Commands = commands };
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }   // a value of the wrong type
        catch (FormatException) { return null; }
    }

    private static string Str(JsonNode? n, int max)
    {
        if (n is null || n.GetValueKind() != JsonValueKind.String) return "";
        var s = n.GetValue<string>().Trim();
        s = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return s.Length > max ? s[..max] : s;
    }
}
