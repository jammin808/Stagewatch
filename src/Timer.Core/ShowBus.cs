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

/// <summary>One part of a node, as the Eye and an AI read it (Show Bus §6 "items").</summary>
public sealed record ContextItem(string Id, string Kind, string Label, string Light, string State);

/// <summary>A link between two parts (Show Bus §6 "links").</summary>
public sealed record ContextLink(string From, string To, string Kind, string Light);

/// <summary>A peer as one line: its light and headline, one hop only.</summary>
public sealed record PeerSummary(string Kind, string Id, string Name, string Light, string Headline, string Address, double AgeSeconds, bool HasContext)
{
    /// <summary>Show Bus liveness: live (heard within 6 s), stale (6–12 s), gone (over 12 s).</summary>
    public string Liveness => AgeSeconds <= 6 ? "live" : AgeSeconds <= 12 ? "stale" : "gone";
}

/// <summary>
/// The contexts other show apps posted here or that we fetched (Show Bus §6), kept for 30 s, validated and size-capped:
/// anything on the network can post anything. Thread-safe.
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
    }

    /// <summary>The newest context from a node, by id (null when none is held).</summary>
    public Entry? Find(string nodeId, DateTime nowUtc) => All(nowUtc).FirstOrDefault(e => e.Id == nodeId);

    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);

    /// <summary>Take a context. Null = taken, else why not.</summary>
    public string? Accept(string json, string address, DateTime nowUtc, string? ownId = null)
    {
        if (json.Length is 0 or > MaxBytes) return "a context is 1 byte to 64 KB";
        if (Parse(json, address, nowUtc) is not { } e) return "not a Show Bus context (v 1, node.t, node.id)";
        if (e.Id == ownId) return "that's this Stagewatch";
        lock (_byId)
        {
            Prune(nowUtc);
            if (_byId.Count >= MaxPeers && !_byId.ContainsKey(e.Id)) return "too many peers";
            _byId[e.Id] = e;
        }
        return null;
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

    /// <summary>Pure: a §6 context → an entry (strings capped), or null when it isn't one.</summary>
    public static Entry? Parse(string json, string address, DateTime nowUtc)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return null;
            if (root["v"]?.GetValueKind() != JsonValueKind.Number || root["v"]!.GetValue<double>() < 1) return null;
            if (root["node"] is not JsonObject node) return null;
            var kind = Str(node["t"], 16);
            var id = Str(node["id"], 40);
            if (kind.Length == 0 || id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
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
