using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;

namespace Stagewatch.Core;

/// <summary>An API key seen arriving in clear from another machine (Show Bus 13.6.9): which key, from which address, and when first.</summary>
public readonly record struct KeySighting(string KeyId, string Address, DateTime AtUtc);

/// <summary>
/// Which API keys this Stagewatch has seen arrive in clear from another machine (Show Bus 13.6.9): a key, from where and when it was
/// first seen. Anyone on the path between may have read such a key, so it is marked until the operator replaces it: amber on its key
/// row and in the context, "replace this key", and never given a window grant (§12; <see cref="MayGrant"/>). The first sighting is kept;
/// a later one adds nothing. Only keys in force are marked once <see cref="Keep"/> has named them, and a revoked key's mark goes with it,
/// so the table holds at most the keys in force (<see cref="ApiKeys.Max"/>). A mark saved on a key is put back on <see cref="Keep"/>.
/// Thread-safe: one lock, <see cref="Seen"/> raised outside it on the caller's thread. Holds no secret: a key's id, an address, a time.
/// </summary>
public sealed class KeySightings
{
    private static readonly ConditionalWeakTable<object, KeySightings> Owned = new();

    /// <summary>The most marks a table that was never told the keys in force holds (a surface that keeps no keys of its own).</summary>
    public const int MaxUnkept = 256;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, KeySighting> _seen = new(StringComparer.Ordinal);
    private HashSet<string>? _live;

    /// <summary>An empty table, or one seeded from the keys in force and the marks saved on them.</summary>
    public KeySightings(IEnumerable<ApiKey>? keys = null)
    {
        if (keys is not null) Keep(keys);
    }

    /// <summary>The default for a surface that keeps no table of its own: one per owner, so its marks still hold for its own life.</summary>
    public static KeySightings For(object owner) => Owned.GetValue(owner, _ => new KeySightings());

    /// <summary>Raised once per key, when it is first seen in clear (never for a mark put back by <see cref="Keep"/>).</summary>
    public event Action<KeySighting>? Seen;

    /// <summary>
    /// The keys in force: a key no longer among them loses its mark, and a mark saved on a key (<see cref="ApiKey.ClearSeen"/>,
    /// <see cref="ApiKey.ClearFrom"/>) is put back when this table holds none for it. A mark this table holds stands over the saved one.
    /// </summary>
    public void Keep(IEnumerable<ApiKey> keys)
    {
        lock (_lock)
        {
            var list = keys.ToList();
            _live = list.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var gone in _seen.Keys.Where(id => !_live.Contains(id)).ToList()) _seen.Remove(gone);
            foreach (var k in list)
                if (!_seen.ContainsKey(k.Id) && Saved(k) is { } saved) _seen[k.Id] = saved;
        }
    }

    /// <summary>
    /// Marks a key seen in clear from <paramref name="address"/> at <paramref name="atUtc"/>: true, and <see cref="Seen"/> raised, the
    /// first time; false when it is marked already, is not a key in force, or the table is full.
    /// </summary>
    public bool Mark(string keyId, string address, DateTime atUtc)
    {
        if (string.IsNullOrEmpty(keyId)) return false;
        KeySighting sighting;
        lock (_lock)
        {
            if (_seen.ContainsKey(keyId)) return false;
            if (_live is not null ? !_live.Contains(keyId) : _seen.Count >= MaxUnkept) return false;
            sighting = new KeySighting(keyId, address, DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));
            _seen[keyId] = sighting;
        }
        Seen?.Invoke(sighting);
        return true;
    }

    /// <summary>The key's mark, or null when it was never seen in clear.</summary>
    public KeySighting? Of(string keyId)
    {
        lock (_lock) return _seen.TryGetValue(keyId, out var s) ? s : null;
    }

    /// <summary>
    /// Whether a window may be granted to <paramref name="keyId"/>'s link (§12, 13.6.9): never once the key was seen in clear, until the
    /// operator replaces it with a key that never crossed the network in clear.
    /// </summary>
    public bool MayGrant(string keyId) => Of(keyId) is null;

    /// <summary>Every mark, the oldest first.</summary>
    public IReadOnlyList<KeySighting> All()
    {
        lock (_lock) return _seen.Values.OrderBy(s => s.AtUtc).ThenBy(s => s.KeyId, StringComparer.Ordinal).ToList();
    }

    /// <summary>Whether a key as saved does not carry the mark this table holds for it.</summary>
    public bool Unsaved(IEnumerable<ApiKey> keys) => Save(keys, write: false);

    /// <summary>Writes each mark onto its key as saved (<see cref="ApiKey.ClearSeen"/>, <see cref="ApiKey.ClearFrom"/>); whether any changed.</summary>
    public bool SaveTo(IEnumerable<ApiKey> keys) => Save(keys, write: true);

    private bool Save(IEnumerable<ApiKey> keys, bool write)
    {
        var changed = false;
        lock (_lock)
        {
            foreach (var k in keys)
            {
                if (k is null || !_seen.TryGetValue(k.Id, out var s)) continue;
                var at = Iso(s.AtUtc);
                if (k.ClearSeen == at && k.ClearFrom == s.Address) continue;
                changed = true;
                if (!write) return true;
                k.ClearSeen = at;
                k.ClearFrom = s.Address;
            }
        }
        return changed;
    }

    /// <summary>A time as a key keeps it: ISO 8601 in UTC to the second.</summary>
    public static string Iso(DateTime atUtc) => atUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>The mark saved on a key, or null when it carries none that reads as one (a time and an address).</summary>
    public static KeySighting? Saved(ApiKey key)
        => ParseSaved(key.ClearSeen, key.ClearFrom) is { } at ? new KeySighting(key.Id, key.ClearFrom!, at) : null;

    /// <summary>Whether a saved mark reads as one: <see cref="Iso"/>'s shape and an IP address.</summary>
    public static bool ValidSaved(string? seen, string? from) => ParseSaved(seen, from) is not null;

    private static DateTime? ParseSaved(string? seen, string? from)
        => seen is { Length: 20 } && from is { Length: > 0 and <= 45 } && IPAddress.TryParse(from, out _)
           && DateTime.TryParseExact(seen, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
               DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null;
}
