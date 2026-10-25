using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>What an API key may do. The PIN can do everything; a key only what it was made for.</summary>
public enum ApiKeyScope
{
    /// <summary>Read the full operator state, reports and the screen list — dashboards, vMix titles, logging.</summary>
    Read,
    /// <summary>Read, and run commands over HTTP (/api/...) — Stream Deck scripts, show control, automation.</summary>
    Control,
    /// <summary>Read, and the MCP endpoint for AI agents (whose changes can still wait for approval).</summary>
    Mcp,
    /// <summary>
    /// Another show app (a messenger such as AGMessage): read, put a message on the stage, and post its own context
    /// (Show Bus 1.1). Nothing else — it can't run the show.
    /// </summary>
    Node,
}

/// <summary>
/// An API key: a named, scoped, revocable credential that isn't the PIN. Only a hash of the secret is kept, so the
/// settings file never holds a usable key; the secret is shown once, when the key is made.
/// </summary>
public sealed class ApiKey
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("scope"), JsonConverter(typeof(JsonStringEnumConverter))] public ApiKeyScope Scope { get; set; }
    /// <summary>SHA-256 of the secret, hex.</summary>
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    /// <summary>The secret's last four characters, so the operator can tell keys apart.</summary>
    [JsonPropertyName("hint")] public string Hint { get; set; } = "";
    [JsonPropertyName("created")] public DateTime CreatedUtc { get; set; }
    /// <summary>
    /// The node id a node key speaks for (Show Bus §11), or null while it speaks for none. Kept only on node keys; the
    /// binding itself is <see cref="NodeBindings"/>, seeded from here.
    /// </summary>
    [JsonPropertyName("node")] public string? Node { get; set; }
    /// <summary>That node's kind (patterns, messenger, playmap…), or null while not yet named.</summary>
    [JsonPropertyName("nodeKind")] public string? NodeKind { get; set; }
}

public static class ApiKeys
{
    /// <summary>Every secret starts with this, so the server knows a key from a PIN without guessing.</summary>
    public const string Prefix = "swk_";
    public const int Max = 50;

    /// <summary>Requests a key may make per minute (headers say how many are left).</summary>
    public const int PerMinute = 600;

    /// <summary>Make a key. Returns the entry to store and the secret to show once.</summary>
    public static (ApiKey Key, string Secret) Create(string name, ApiKeyScope scope, DateTime nowUtc)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";   // no 0/o, 1/l: read aloud or typed from a screen
        var secret = new StringBuilder(Prefix, Prefix.Length + 32);
        for (var i = 0; i < 32; i++) secret.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        var s = secret.ToString();
        var key = new ApiKey
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Name = Clean(name),
            Scope = scope,
            Hash = HashOf(s),
            Hint = s[^4..],
            CreatedUtc = nowUtc,
        };
        return (key, s);
    }

    public static bool LooksLikeKey(string? s) => s is not null && s.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>The key a secret belongs to, or null (unknown or revoked). Constant-time per stored key.</summary>
    public static ApiKey? Find(IReadOnlyList<ApiKey> keys, string? secret)
    {
        if (!LooksLikeKey(secret) || secret!.Length > 80) return null;
        var hash = Encoding.ASCII.GetBytes(HashOf(secret));
        ApiKey? hit = null;
        foreach (var k in keys)
            if (k.Hash.Length == hash.Length && CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(k.Hash))) hit = k;
        return hit;
    }

    /// <summary>Whether a key's scope covers what's asked. Every scope may read.</summary>
    public static bool Allows(ApiKeyScope have, ApiKeyScope need)
        => need == ApiKeyScope.Read || have == need || (need == ApiKeyScope.Node && have == ApiKeyScope.Control);

    /// <summary>What a node key may run: stage messages only.</summary>
    public static bool NodeMayRun(ShowCommandKind kind) => kind is ShowCommandKind.Message or ShowCommandKind.ClearMessage;

    public static string Describe(ApiKeyScope s) => s switch
    {
        ApiKeyScope.Read => "read only",
        ApiKeyScope.Control => "read + commands",
        ApiKeyScope.Node => "read + stage messages + its context (another show app)",
        _ => "read + MCP (AI agents)",
    };

    /// <summary>Drop anything malformed from a loaded settings file.</summary>
    public static List<ApiKey> Sanitise(IEnumerable<ApiKey?>? keys)
        => (keys ?? Array.Empty<ApiKey?>())
            .Where(k => k is not null && k.Hash.Length == 64 && k.Hash.All(char.IsAsciiHexDigit) && Enum.IsDefined(k.Scope) && k.Id.Length is > 0 and <= 16)
            .Select(k =>
            {
                k!.Name = Clean(k.Name);
                // A node binding belongs on a node key only, with an id Show Bus can name; the kind only beside an id.
                if (k.Scope != ApiKeyScope.Node || !NodeBindings.ValidId(k.Node)) k.Node = null;
                if (k.Node is null || !NodeBindings.ValidKind(k.NodeKind)) k.NodeKind = null;
                return k;
            })
            .DistinctBy(k => k.Id).Take(Max).ToList();

    private static string Clean(string? name)
    {
        var n = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return n.Length == 0 ? "API key" : n.Length > 40 ? n[..40] : n;
    }

    private static string HashOf(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();
}
