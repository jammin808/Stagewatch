using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// What each playout link keeps across a restart (Show Bus 13.15.3, 13.9.3, 13.8, 13.4.3), by the link's name: a hold with its end in
/// wall time and the refusal it holds for, a pin, a floor and the node it is bound to, the address it was kept at, and beside the pin
/// the TLS door its certificate last answered at (where a pinned link dials while no hello names one). It never holds a token, proof
/// or nonce —
/// <see cref="PeerKept"/> carries none. A hold holds the token it was for: it is kept beside <see cref="Tag"/> of that token as
/// Settings stores it, and a link whose token has changed since starts without it (13.15.3: "the key changes"); a restored hold ends
/// within <see cref="HoldMaxMs"/>, whatever the file or a wall clock stepped back says. One file, read when the store is made; a keep
/// changes the store in memory at once and the file is written whole on the store's own lane (<see cref="AtomicFile"/>) — one writer,
/// the newest of everything kept, never on a keeper's thread or under its lock, so a link's lock and the show thread never wait on the
/// disk. A file it cannot read is no holds, and a field it cannot read is dropped, never guessed: a refusal outside 13.16.3's set holds
/// as <c>key</c>, a kind outside the hello's is no kind. At most <see cref="MaxEntries"/> links, the one kept longest ago making room
/// for a new one.
/// </summary>
public sealed class PlayoutKeptStore
{
    /// <summary>
    /// A link's entry: the tag of the token its hold is for, what it keeps, when it was kept (wall ms, for room), the TLS door kept
    /// beside its pin (0 for none) and the address (host:port) the link dialled when it kept it (null when not known).
    /// </summary>
    public sealed record Entry(string Tag, PeerKept Kept, long At, int Door = 0, string? Address = null);

    /// <summary>The longest address an entry keeps (a host name and its port); a longer one is not kept.</summary>
    public const int AddressMax = 300;

    /// <summary>The most links the file holds: every link Settings may hold (<see cref="PlayoutRules.Max"/>), and as many renamed since.</summary>
    public const int MaxEntries = PlayoutRules.Max * 2;

    /// <summary>The hex digits of <see cref="Tag"/>.</summary>
    public const int TagHex = 16;

    /// <summary>
    /// The longest hold a playout link starts (13.15.3): <c>peerlink.hold.refused.ms</c>, since a level-0 link honours no acceptor's
    /// retry or <c>Retry-After</c> (13.6.8). A restored hold that would end later ends this long from the restart.
    /// </summary>
    public const long HoldMaxMs = PeerLink.HoldRefusedMs;

    /// <summary>13.16.3's refusal words: a kept hold's refusal is one of them, or the hold is for <c>key</c>.</summary>
    public static readonly IReadOnlyList<string> Refusals =
        ["key", "binding", "scope", "locked", "version", "certificate", "floor", "other-id", "conflict", "busy", "malformed"];

    /// <summary>13.16.3's proof words, a kept floor's among them.</summary>
    private static readonly string[] Proofs = ["mutual", "legacy", "open", "none"];

    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _links;

    // The store's lane (under the lock): the write every keep since the last one waits on, the last write made or under way, and
    // whether the lane runs.
    private TaskCompletionSource<bool>? _pending;
    private Task<bool> _last = Task.FromResult(true);
    private bool _writing;

    /// <param name="path">The file; null keeps nothing on disk (a test, or a link the rig runs).</param>
    public PlayoutKeptStore(string? path)
    {
        _path = path;
        string? text = null;
        try
        {
            if (path is not null && File.Exists(path)) text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            text = null;   // a file it cannot read is no holds: each link starts as a fresh one
        }
        _links = Parse(text);
    }

    /// <summary>
    /// What link <paramref name="name"/> starts from at <paramref name="nowWall"/> (Unix ms), for the token whose tag is
    /// <paramref name="tag"/>, the node Settings set it to (<paramref name="node"/>, null or "" for none) and the address it dials
    /// (<paramref name="address"/>, host:port; null when not said): what it kept, its hold only when it was for that token, and ending
    /// within <see cref="HoldMaxMs"/>; null when it kept nothing. A pin is for its node (13.8, 13.4.1): a link set to another node
    /// since — or, while no node is set, to another address, where another node may answer — starts with none of the old one's pin,
    /// floor or binding; and a link whose own entry has no pin for it (renamed, made again, or moved back) starts from the pin and floor
    /// another entry keeps for its node — or, while no node is set, for the address it dials — never that entry's hold, which is its
    /// token's. Its door is <see cref="DoorFor"/>'s.
    /// </summary>
    public PeerKept? For(string name, string tag, long nowWall, string? node = null, string? address = null)
    {
        lock (_gate)
        {
            var (kept, _) = Resolve(name, node, address);
            if (kept is null) return null;
            if (!_links.TryGetValue(name, out var e) || e.Tag != tag) return kept with { HeldUntilWall = null, HeldRefusal = null };
            return kept is { HeldUntilWall: { } until } && until > nowWall + HoldMaxMs ? kept with { HeldUntilWall = nowWall + HoldMaxMs } : kept;
        }
    }

    /// <summary>
    /// The TLS door kept beside the pin <see cref="For"/> gives link <paramref name="name"/> for <paramref name="node"/> and
    /// <paramref name="address"/>: where that pinned node's certificate last answered; 0 for none.
    /// </summary>
    public int DoorFor(string name, string? node = null, string? address = null)
    {
        lock (_gate) return Resolve(name, node, address).Door;
    }

    /// <summary>
    /// What a link's entry gives it (under the lock): its own pin when it is still for the node and address the link is set to, else
    /// the newest other entry's for them; with the door kept beside that pin. A hold and a binding are only ever its own entry's.
    /// </summary>
    private (PeerKept? Kept, int Door) Resolve(string name, string? node, string? address)
    {
        var meant = node is { Length: > 0 } n ? n : null;
        var where = address is { Length: > 0 } a ? a : null;
        _links.TryGetValue(name, out var e);
        var kept = e?.Kept;
        var door = e?.Door ?? 0;
        if (kept is not null && e is not null && !Fits(e, meant, where))
        {
            kept = kept with { Pin = null, Floor = null, NodeId = null, NodeKind = null };
            door = 0;
        }
        if (kept?.Pin is null)
        {
            var other = _links.Where(kv => kv.Key != name && kv.Value.Kept.Pin is not null
                                           && (meant is not null ? kv.Value.Kept.Pin!.NodeId == meant : where is not null && kv.Value.Address == where))
                .Select(kv => kv.Value).MaxBy(x => x.At);
            if (other is not null)
            {
                kept = (kept ?? new PeerKept(null, null, null, null)) with { Pin = other.Kept.Pin, Floor = other.Kept.Floor };
                door = other.Door;
            }
        }
        if (kept is null || Empty(kept)) return (null, 0);
        return (kept, kept.Pin is null ? 0 : door);
    }

    /// <summary>
    /// Whether an entry's pin is for the link as it is set now: for the node Settings set (by its pin's node, else its binding), or,
    /// with none set, kept at the address the link dials (an entry that never said its address fits any).
    /// </summary>
    private static bool Fits(Entry e, string? meant, string? where)
    {
        if (meant is not null) return (e.Kept.Pin?.NodeId ?? e.Kept.NodeId) is not { } was || was == meant;
        return where is null || e.Address is null || e.Address == where;
    }

    /// <summary>
    /// Keeps what link <paramref name="name"/> holds now, for the token whose tag is <paramref name="tag"/>, at <paramref name="nowWall"/>
    /// (Unix ms); nothing kept removes its entry. The store holds it at once — <see cref="For"/> reads it — and its lane writes the file:
    /// cheap, and never a wait on the disk for the caller. The task completes once a write holding this keep has landed, false when it
    /// could not be written.
    /// </summary>
    public Task<bool> Keep(string name, string tag, PeerKept kept, long nowWall, string? address = null)
    {
        lock (_gate)
        {
            if (Empty(kept)) _links.Remove(name);
            else
            {
                _links.TryGetValue(name, out var was);
                if (was is null && _links.Count >= MaxEntries)
                    _links.Remove(_links.MinBy(kv => kv.Value.At).Key);
                // The door stays while there is a pin (where the node answers TLS, whichever certificate it shows); the address is
                // where the link dials now.
                var door = kept.Pin is not null ? was?.Door ?? 0 : 0;
                _links[name] = new Entry(Valid(tag) ? tag : "", kept, nowWall, door, Address(address) ?? was?.Address);
            }
            return Write();
        }
    }

    /// <summary>
    /// Keeps the TLS door beside link <paramref name="name"/>'s pin (0 drops it), at <paramref name="nowWall"/>; a link that keeps no
    /// pin keeps no door. As <see cref="Keep"/>: the store's at once, written on its lane.
    /// </summary>
    public Task<bool> KeepDoor(string name, int door, long nowWall)
    {
        lock (_gate)
        {
            if (!_links.TryGetValue(name, out var e) || e.Kept.Pin is null) return Task.FromResult(true);
            var d = door is > 0 and < 65536 ? door : 0;
            if (e.Door == d) return Task.FromResult(true);
            _links[name] = e with { Door = d, At = nowWall };
            return Write();
        }
    }

    /// <summary>The lane's next write, started when none runs (under the lock); what it hands back says whether it landed.</summary>
    private Task<bool> Write()
    {
        if (_path is null) return Task.FromResult(true);
        if (_pending is null)
        {
            _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_writing)
            {
                _writing = true;
                _ = Task.Run(WriteLane);
            }
        }
        return _pending.Task;
    }

    /// <summary>An address as an entry keeps it: 1 to <see cref="AddressMax"/> characters, none a control character; else null.</summary>
    private static string? Address(string? address)
        => address is { Length: > 0 and <= AddressMax } a && !a.Any(char.IsControl) ? a : null;

    /// <summary>A task that completes once everything kept before it is on disk, or could not be: true when the last write landed.</summary>
    public Task<bool> Flushed
    {
        get { lock (_gate) return _pending?.Task ?? _last; }
    }

    /// <summary>
    /// The store's lane: while a keep waits, the file is written whole with everything kept by then — latest wins, one writer — and
    /// every keep that waited on that write hears whether it landed.
    /// </summary>
    private void WriteLane()
    {
        try
        {
            while (true)
            {
                TaskCompletionSource<bool> done;
                string json;
                lock (_gate)
                {
                    if (_pending is null)
                    {
                        _writing = false;
                        return;
                    }
                    done = _pending;
                    _pending = null;
                    _last = done.Task;
                    json = ToJson(_links);
                }
                bool landed;
                try
                {
                    AtomicFile.Write(_path!, json, AtomicFile.IsJson);
                    landed = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    landed = false;
                }
                done.TrySetResult(landed);
            }
        }
        finally
        {
            // Should the lane end on a fault of its own, no keep waits on it for good, and the next keep starts it again.
            lock (_gate)
            {
                if (_writing)
                {
                    _writing = false;
                    _pending?.TrySetResult(false);
                    _pending = null;
                }
            }
        }
    }

    /// <summary>The links it holds now, by name.</summary>
    public IReadOnlyDictionary<string, Entry> Links
    {
        get { lock (_gate) return new Dictionary<string, Entry>(_links, StringComparer.Ordinal); }
    }

    /// <summary>
    /// Pure: the tag of a token as Settings stores it — the first <see cref="TagHex"/> hex of the SHA-256 of its protected form, never of
    /// the token itself, whose hash would be a guessing target — so a token changed in Settings changes its tag; "" for no token.
    /// </summary>
    public static string Tag(string? protectedToken)
        => string.IsNullOrEmpty(protectedToken) ? "" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(protectedToken)))[..TagHex];

    /// <summary>Whether a link keeps nothing: no hold, pin, floor or node.</summary>
    public static bool Empty(PeerKept kept)
        => kept is { HeldUntilWall: null, Pin: null, Floor: null, NodeId: null, NodeKind: null };

    /// <summary>Pure: the file's text for <paramref name="links"/>, its names in order.</summary>
    public static string ToJson(IReadOnlyDictionary<string, Entry> links)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Options))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteStartObject("links");
            foreach (var (name, e) in links.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                var k = e.Kept;
                w.WriteStartObject(name);
                w.WriteString("tag", e.Tag);
                w.WriteNumber("at", e.At);
                if (Address(e.Address) is { } address) w.WriteString("address", address);
                if (k.Pin is not null && e.Door is > 0 and < 65536) w.WriteNumber("door", e.Door);
                if (k.HeldUntilWall is { } until)
                {
                    w.WriteNumber("heldUntil", until);
                    w.WriteString("refusal", k.HeldRefusal ?? "key");
                }
                if (k.NodeId is { } node) w.WriteString("node", node);
                if (k.NodeKind is { } kind) w.WriteString("kind", kind);
                if (k.Pin is { } pin)
                {
                    w.WriteStartObject("pin");
                    if (pin.NodeId is { } pinNode) w.WriteString("node", pinNode);
                    w.WriteString("fingerprint", pin.Fingerprint);
                    w.WriteString("trust", pin.Trust.ToString().ToLowerInvariant());
                    w.WriteEndObject();
                }
                if (k.Floor is { } floor)
                {
                    w.WriteStartObject("floor");
                    w.WriteBoolean("tls", floor.Tls);
                    w.WriteNumber("level", floor.Level);
                    w.WriteString("proof", floor.Proof);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Pure: the links a file's text keeps — each one a valid link name with something kept, its fields of their shapes and its words
    /// of their closed sets; anything else dropped (a hold's refusal outside 13.16.3's set holds as <c>key</c>, fail closed), the whole
    /// when it is not a v1 object. Never throws; at most <see cref="MaxEntries"/>.
    /// </summary>
    public static Dictionary<string, Entry> Parse(string? json)
    {
        var links = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return links;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetRawText() != "1"
                || !root.TryGetProperty("links", out var all) || all.ValueKind != JsonValueKind.Object)
                return links;
            foreach (var link in all.EnumerateObject())
            {
                if (links.Count >= MaxEntries) break;
                if (!PlayoutRules.ValidName(link.Name) || link.Value.ValueKind != JsonValueKind.Object || links.ContainsKey(link.Name)) continue;
                var e = link.Value;
                long? until = Number(e, "heldUntil") is > 0 and var u ? u : null;
                var refusal = until is null ? null : Str(e, "refusal") is { } r && Refusals.Contains(r, StringComparer.Ordinal) ? r : "key";
                var kind = Str(e, "kind") is { } k && ShowBusKinds.Label(k) is not null ? k : null;
                var kept = new PeerKept(Pin(e), Floor(e), until, refusal, Id(Str(e, "node")), kind);
                if (Empty(kept)) continue;
                var tag = Str(e, "tag") is { } t && Valid(t) ? t : "";
                var door = kept.Pin is not null && Number(e, "door") is > 0 and < 65536 and var d ? (int)d : 0;
                links[link.Name] = new Entry(tag, kept, Number(e, "at") is > 0 and var at ? at : 0, door, Address(Str(e, "address")));
            }
        }
        catch (JsonException)
        {
            links.Clear();   // a file that is not JSON keeps nothing
        }
        return links;
    }

    private static bool Valid(string tag) => tag.Length == 0 || (tag.Length == TagHex && tag.All(char.IsAsciiHexDigitLower));

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long? Number(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) ? n : null;

    /// <summary>A node id as a hello carries one: 1–40 letters, digits, dots, dashes or underscores.</summary>
    private static string? Id(string? s) => s is { Length: > 0 and <= 40 } && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') ? s : null;

    private static PeerPin? Pin(JsonElement e)
    {
        if (!e.TryGetProperty("pin", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        if (Str(p, "fingerprint") is not { Length: 64 } fp || !fp.All(char.IsAsciiHexDigitLower)) return null;
        PinTrust? trust = Str(p, "trust") switch { "first" => PinTrust.First, "proof" => PinTrust.Proof, "person" => PinTrust.Person, _ => null };
        return trust is { } t ? new PeerPin(Id(Str(p, "node")), fp, t) : null;
    }

    private static PeerFloor? Floor(JsonElement e)
    {
        if (!e.TryGetProperty("floor", out var f) || f.ValueKind != JsonValueKind.Object) return null;
        if (!f.TryGetProperty("tls", out var tls) || tls.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return null;
        if (Number(f, "level") is not ({ } level and >= 0 and <= 1) || Str(f, "proof") is not { } proof || !Proofs.Contains(proof, StringComparer.Ordinal)) return null;
        return new PeerFloor(tls.ValueKind == JsonValueKind.True, (int)level, proof);
    }
}
