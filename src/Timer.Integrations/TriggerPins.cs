using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// The certificate an HTTP trigger's target showed, pinned (Show Bus 13.8): its fingerprint (64 lowercase hex), how it was pinned —
/// <see cref="PinTrust.First"/>, kept for comparison only and sent nothing, or <see cref="PinTrust.Person"/>, trusted by a person on
/// this desk — and when (wall ms). It carries no secret: a fingerprint is public.
/// </summary>
public sealed record TriggerPin(string Fingerprint, PinTrust Trust, long At);

/// <summary>
/// A trigger target's certificate that waits for a person, as the desk offers it: the key of its address's pin
/// (<see cref="TriggerPinStore.AddrKey"/>), where it is (<c>host:port</c>) and the rule that last went there, the certificate shown,
/// whether it is a changed one — CERTIFICATE CHANGED, trusted only by typing at least <see cref="PlayoutRules.TrustHexMin"/> hex of it —
/// or one pinned first, trusted by one press beside its short form; the pin it would replace (64 hex, else ""), and the node the hello
/// heard there named at its last fire (null for none), whose key the trust is kept under too.
/// </summary>
public sealed record TriggerCertificateWait(string Key, string Where, string Rule, string Fingerprint, bool Changed, string Pinned, string? NodeId);

/// <summary>
/// A trigger's secret held after its target refused it (401) or rate limited it (429), Show Bus 13.15.3: not sent to
/// <see cref="Where"/> as <see cref="Kind"/> again until <see cref="UntilWall"/> (Unix ms), and never longer than <see cref="Ms"/> from
/// any moment it is read, so a wall clock stepped back never lengthens it. The secret is named two ways and neither is it: by
/// <see cref="Mark"/>, a keyed hash whose key this process drew and never wrote (null once read back from the file), which holds every
/// rule that resolves to the same secret; and by <see cref="Tags"/>, the tags of the protected forms that resolved to it when it was held
/// (<see cref="PlayoutKeptStore.Tag"/>), all the file keeps of it. <see cref="Why"/> is the refusal's words, which never quote it.
/// </summary>
public sealed record TriggerHold(string Where, string Kind, string? Mark, IReadOnlyList<string> Tags, long UntilWall, long Ms, string Why);

/// <summary>
/// What HTTP triggers keep across a restart, in one file of their own: their targets' pins and their held secrets' ends. A pin is kept
/// by the address (<c>addr:&lt;host&gt;:&lt;port&gt;</c>) and, when the hello heard there names a node, by its node id
/// (<c>node:&lt;id&gt;</c>) too, so neither a hello lost nor one forged moves a target off the pin a person made. What is kept is the
/// store's at once and the file is written whole on the store's own lane (<see cref="AtomicFile"/>), never on the caller's thread. A file
/// that cannot be read is no pins and no holds, said (13.8.4: <see cref="Lost"/>) until a write lands; an entry it cannot read is
/// dropped, never guessed. At most <see cref="MaxEntries"/> pins — a first pin makes room before any a person made, and never takes a
/// person's place — and <see cref="MaxHolds"/> holds, the one ending first making room.
/// </summary>
public sealed class TriggerPinStore
{
    /// <summary>The most pins the file holds: 64 targets, each by its address and its node.</summary>
    public const int MaxEntries = 128;

    /// <summary>The longest key kept: <c>addr:</c> and a host name with its port.</summary>
    public const int KeyMax = 300;

    /// <summary>The most holds the file keeps.</summary>
    public const int MaxHolds = 64;

    /// <summary>The longest hold: a 429's <c>Retry-After</c> is honoured up to ten times 13.15.3's <c>peerlink.hold.refused.ms</c>.</summary>
    public const long HoldMaxMs = 10 * PeerLink.HoldRefusedMs;

    /// <summary>The most tags a hold keeps: the rules that resolved to its secret when it was held.</summary>
    public const int TagsMax = 16;

    /// <summary>The most characters of a hold's words the file keeps.</summary>
    public const int WhyMax = 300;

    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, TriggerPin> _pins;
    private readonly List<TriggerHold> _holds;
    private TaskCompletionSource<bool>? _pending;
    private Task<bool> _last = Task.FromResult(true);
    private bool _writing;
    private volatile bool _lost;

    /// <param name="path">The file; null keeps nothing on disk (a test).</param>
    public TriggerPinStore(string? path)
    {
        _path = path;
        string? text = null;
        var lost = false;
        try
        {
            if (path is not null && File.Exists(path)) text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lost = true;   // a file it cannot read is no pins, said: every target is new here
        }
        _pins = Parse(text, out var unreadable);
        _holds = unreadable ? [] : ParseHolds(text);
        _lost = lost || unreadable;
    }

    /// <summary>13.8.4: the file was there and could not be read whole — every target is new here; compare fingerprints. Cleared once a write lands.</summary>
    public bool Lost => _lost;

    /// <summary>The words <see cref="Lost"/> is said in.</summary>
    public const string LostWords = "trigger pins lost — every HTTPS target is new here; compare fingerprints";

    /// <summary>The pin kept under <paramref name="key"/>, or null.</summary>
    public TriggerPin? Get(string key)
    {
        lock (_gate) return _pins.GetValueOrDefault(key);
    }

    /// <summary>Every pin, by key.</summary>
    public IReadOnlyDictionary<string, TriggerPin> Pins
    {
        get { lock (_gate) return new Dictionary<string, TriggerPin>(_pins, StringComparer.Ordinal); }
    }

    /// <summary>
    /// Keeps <paramref name="pin"/> under <paramref name="key"/>: the store's at once, written on its lane. The task completes once a
    /// write holding it has landed, false when it could not be written. A key or fingerprint of the wrong shape is kept nowhere, and so
    /// is a first pin when every pin kept is a person's: a first pin never takes a person's place (<see cref="Room"/>).
    /// </summary>
    public Task<bool> Put(string key, TriggerPin pin)
    {
        if (!ValidKey(key) || !PlayoutWire.IsFingerprint("sha256:" + pin.Fingerprint)) return Task.FromResult(false);
        lock (_gate)
        {
            if (!_pins.ContainsKey(key) && _pins.Count >= MaxEntries)
            {
                if (Room(_pins, pin.Trust) is not { } room) return Task.FromResult(false);
                _pins.Remove(room);
            }
            _pins[key] = pin;
            return Write();
        }
    }

    /// <summary>
    /// Pure: the key whose pin makes room for a new pin of <paramref name="trust"/> in a full store — the first pin made longest ago,
    /// else, for a person's pin alone, the person's pin made longest ago; null when a first pin would take a person's place.
    /// </summary>
    public static string? Room(IReadOnlyDictionary<string, TriggerPin> pins, PinTrust trust)
    {
        string? oldest = null;
        long at = long.MaxValue;
        foreach (var (key, pin) in pins)
            if (pin.Trust == PinTrust.First && (oldest is null || pin.At < at || pin.At == at && string.CompareOrdinal(key, oldest) < 0))
                (oldest, at) = (key, pin.At);
        if (oldest is not null || trust == PinTrust.First) return oldest;
        foreach (var (key, pin) in pins)
            if (oldest is null || pin.At < at || pin.At == at && string.CompareOrdinal(key, oldest) < 0)
                (oldest, at) = (key, pin.At);
        return oldest;
    }

    /// <summary>
    /// Holds a secret (<paramref name="hold"/>): kept at once and written on the lane, in place of the holds it ends or replaces — any
    /// of the same target and kind that names the same secret (its mark, or a tag in common) — and of any already over at
    /// <paramref name="nowWall"/>. At most <see cref="MaxHolds"/>, the one ending first making room.
    /// </summary>
    public Task<bool> Hold(TriggerHold hold, long nowWall)
    {
        lock (_gate)
        {
            _holds.RemoveAll(h => Left(h, nowWall) <= 0 || h.Where == hold.Where && h.Kind == hold.Kind
                                  && (h.Mark is not null && h.Mark == hold.Mark || h.Tags.Intersect(hold.Tags, StringComparer.Ordinal).Any()));
            _holds.Add(hold);
            while (_holds.Count > MaxHolds) _holds.Remove(_holds.MinBy(h => h.UntilWall)!);
            return Write();
        }
    }

    /// <summary>
    /// The hold on a secret at <paramref name="nowWall"/> and the ms it has left, or null: one of <paramref name="where"/> and
    /// <paramref name="kind"/> that names it by its <paramref name="mark"/> or by the <paramref name="tag"/> of the protected form it was
    /// resolved from.
    /// </summary>
    public (TriggerHold Hold, long LeftMs)? Held(string where, string kind, string mark, string tag, long nowWall)
    {
        lock (_gate)
        {
            for (var i = 0; i < _holds.Count; i++)
            {
                var h = _holds[i];
                if (h.Where != where || h.Kind != kind || h.Mark != mark && (tag.Length == 0 || !h.Tags.Contains(tag, StringComparer.Ordinal))) continue;
                if (Clamped(i, nowWall) is var left and > 0) return (_holds[i], left);
            }
            return null;
        }
    }

    /// <summary>The holds not over at <paramref name="nowWall"/>, each with the ms it has left.</summary>
    public IReadOnlyList<(TriggerHold Hold, long LeftMs)> Holds(long nowWall)
    {
        lock (_gate)
        {
            var live = new List<(TriggerHold, long)>();
            for (var i = 0; i < _holds.Count; i++)
                if (Clamped(i, nowWall) is var left and > 0) live.Add((_holds[i], left));
            return live;
        }
    }

    /// <summary>The ms a hold has left at <paramref name="nowWall"/>, never more than its length (a wall clock stepped back).</summary>
    public static long Left(TriggerHold hold, long nowWall) => Math.Min(hold.UntilWall - nowWall, hold.Ms);

    /// <summary>
    /// <see cref="Left"/>, and a hold whose end lies further off than its length — the wall clock stepped back since it was set, or the
    /// file was written on a clock ahead — is ended its length from now, so it never outlasts it (under the lock).
    /// </summary>
    private long Clamped(int at, long nowWall)
    {
        var hold = _holds[at];
        if (hold.UntilWall - nowWall <= hold.Ms) return hold.UntilWall - nowWall;
        _holds[at] = hold with { UntilWall = nowWall + hold.Ms };
        return hold.Ms;
    }

    /// <summary>A write of everything kept by now, started on the lane unless one waits already (under the lock).</summary>
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

    /// <summary>A task that completes once everything kept before it is on disk, or could not be: true when the last write landed.</summary>
    public Task<bool> Flushed
    {
        get { lock (_gate) return _pending?.Task ?? _last; }
    }

    /// <summary>The store's lane: while a keep waits, the file is written whole with every pin by then — latest wins, one writer.</summary>
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
                    json = ToJson(_pins, _holds);
                }
                bool landed;
                try
                {
                    AtomicFile.Write(_path!, json, AtomicFile.IsJson);
                    landed = true;
                    _lost = false;   // the file holds what is kept now: nothing is lost any more
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

    /// <summary>Pure: the key a target's pin is always kept under, its address — <c>addr:&lt;host&gt;:&lt;port&gt;</c> (<see cref="TriggerTarget.Where"/>).</summary>
    public static string AddrKey(TriggerTarget target) => "addr:" + target.Where;

    /// <summary>
    /// Pure: the key a target's pin is kept under besides, when the hello heard at its host and port names a node — <c>node:&lt;id&gt;</c>,
    /// which follows the node to another address — or null for none (or an id of the wrong shape).
    /// </summary>
    public static string? NodeKey(string? nodeId) => nodeId is { } id && NodeBindings.ValidId(id) ? "node:" + id : null;

    /// <summary>
    /// Pure: the pin a target's certificate (<paramref name="shown"/>) is judged by, of the one kept by its node
    /// (<paramref name="byNode"/>) and the one kept by its address (<paramref name="byAddr"/>): a person's pin under either is the pin —
    /// the one it matches, else the node's, else the address's — so a hello lost, heard again or forged never moves a target off the pin a
    /// person made; with no person's pin, a first pin (the one it matches first), else none.
    /// </summary>
    public static TriggerPin? Effective(TriggerPin? byNode, TriggerPin? byAddr, string shown)
    {
        TriggerPin?[] both = [byNode, byAddr];
        return both.FirstOrDefault(p => p is { Trust: not PinTrust.First } && p.Fingerprint == shown)
               ?? both.FirstOrDefault(p => p is { Trust: not PinTrust.First })
               ?? both.FirstOrDefault(p => p is not null && p.Fingerprint == shown)
               ?? both.FirstOrDefault(p => p is not null);
    }

    private static bool ValidKey(string key)
        => key.Length <= KeyMax && !key.Any(char.IsControl)
           && (key.StartsWith("node:", StringComparison.Ordinal) ? NodeBindings.ValidId(key[5..]) : key.StartsWith("addr:", StringComparison.Ordinal) && key.Length > 5);

    /// <summary>
    /// Pure: the node id of the one show app whose hello, heard within <see cref="SlidesBeacon.StaleAfter"/> and under no conflict mark
    /// (§2), was heard at <paramref name="host"/> with <paramref name="port"/> among its ports; null when none is, or two are.
    /// </summary>
    public static string? NodeAt(IEnumerable<SlidesBeacon.Peer> peers, string host, int port)
    {
        var ids = peers.Where(p => p.AgeSeconds <= SlidesBeacon.StaleAfter.TotalSeconds && p.Conflict is null && p.HeardAt(host)
                                   && (p.Hello.WebPort == port || p.Hello.Ports?.Values.Contains(port) == true)
                                   && NodeBindings.ValidId(p.Hello.Id))
            .Select(p => p.Hello.Id).Distinct(StringComparer.Ordinal).Take(2).ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>
    /// Pure, 13.8.2–3 for a trigger's target: what the certificate it showed (<paramref name="shown"/>, 64 hex) meets. A pin a person
    /// made that it matches: send. One it does not match: CERTIFICATE CHANGED, nothing sent, the pin kept until a person types the new
    /// one. A pin made <c>first</c> it matches: nothing sent — a first pin is kept for comparison, never trust. None, or a first pin of
    /// another certificate: this one pinned first in its place (<paramref name="nowWall"/>), nothing sent.
    /// </summary>
    public static (bool Send, TriggerPin? Keep, bool Changed) Judge(TriggerPin? pin, string shown, long nowWall)
    {
        if (pin is { Trust: not PinTrust.First } trusted) return trusted.Fingerprint == shown ? (true, null, false) : (false, null, true);
        if (pin is { } first && first.Fingerprint == shown) return (false, null, false);
        return (false, new TriggerPin(shown, PinTrust.First, nowWall), false);
    }

    /// <summary>Pure: the file's text for <paramref name="pins"/>, its keys in order, and <paramref name="holds"/> (none by default) — never a hold's mark.</summary>
    public static string ToJson(IReadOnlyDictionary<string, TriggerPin> pins, IEnumerable<TriggerHold>? holds = null)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, Options))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteStartObject("pins");
            foreach (var (key, pin) in pins.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(key);
                w.WriteString("fingerprint", pin.Fingerprint);
                w.WriteString("trust", pin.Trust.ToString().ToLowerInvariant());
                w.WriteNumber("at", pin.At);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartArray("holds");
            foreach (var hold in holds ?? [])
            {
                w.WriteStartObject();
                w.WriteString("where", hold.Where);
                w.WriteString("kind", hold.Kind);
                w.WriteStartArray("tags");
                foreach (var tag in hold.Tags) w.WriteStringValue(tag);
                w.WriteEndArray();
                w.WriteNumber("until", hold.UntilWall);
                w.WriteNumber("ms", hold.Ms);
                w.WriteString("why", hold.Why);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Pure: the holds a file's text keeps — each with a target and a kind of <see cref="TriggerAuthRules.Kinds"/>, 1 to
    /// <see cref="TagsMax"/> tags of <see cref="PlayoutKeptStore.TagHex"/> lowercase hex, an end, a length of 1 ms to
    /// <see cref="HoldMaxMs"/> and its words; anything else dropped, none for text that is not a v1 object. A hold read back has no mark:
    /// this process holds by its tags until it is held again. Never throws; at most <see cref="MaxHolds"/>.
    /// </summary>
    public static List<TriggerHold> ParseHolds(string? json)
    {
        var holds = new List<TriggerHold>();
        if (string.IsNullOrWhiteSpace(json)) return holds;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("holds", out var all) || all.ValueKind != JsonValueKind.Array)
                return holds;
            foreach (var e in all.EnumerateArray())
            {
                if (holds.Count >= MaxHolds) break;
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (Str(e, "where") is not { Length: > 0 and <= KeyMax } where || where.Any(char.IsControl)) continue;
                if (Str(e, "kind") is not { } kind || kind == TriggerAuthRules.None || !TriggerAuthRules.Kinds.Contains(kind)) continue;
                if (!e.TryGetProperty("tags", out var t) || t.ValueKind != JsonValueKind.Array) continue;
                var tags = t.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : null).ToList();
                if (tags.Count is 0 or > TagsMax || tags.Any(x => x is not { Length: PlayoutKeptStore.TagHex } || !x.All(char.IsAsciiHexDigitLower))) continue;
                if (Num(e, "until") is not { } until || Num(e, "ms") is not { } ms || ms > HoldMaxMs) continue;
                if (Str(e, "why") is not { } why) continue;
                holds.Add(new TriggerHold(where, kind, null, tags!, until, ms, ShowBusText.Cut(new string(why.Select(c => char.IsControl(c) ? ' ' : c).ToArray()), WhyMax)));
            }
        }
        catch (JsonException)
        {
            holds.Clear();
        }
        return holds;
    }

    /// <summary>
    /// Pure: the pins a file's text keeps — each a valid key with a 64-hex fingerprint and a trust word of 13.8's (<c>first</c>,
    /// <c>person</c>; a proof never pins a trigger's target); anything else dropped. <paramref name="unreadable"/> when there was text
    /// and it was not a v1 object. Never throws; at most <see cref="MaxEntries"/>.
    /// </summary>
    public static Dictionary<string, TriggerPin> Parse(string? json, out bool unreadable)
    {
        unreadable = false;
        var pins = new Dictionary<string, TriggerPin>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return pins;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || v.GetRawText() != "1"
                || !root.TryGetProperty("pins", out var all) || all.ValueKind != JsonValueKind.Object)
            {
                unreadable = true;
                return pins;
            }
            foreach (var entry in all.EnumerateObject())
            {
                if (pins.Count >= MaxEntries) break;
                if (!ValidKey(entry.Name) || entry.Value.ValueKind != JsonValueKind.Object || pins.ContainsKey(entry.Name)) continue;
                var e = entry.Value;
                if (Str(e, "fingerprint") is not { Length: 64 } fp || !fp.All(char.IsAsciiHexDigitLower)) continue;
                PinTrust? trust = Str(e, "trust") switch { "first" => PinTrust.First, "person" => PinTrust.Person, _ => null };
                if (trust is not { } t) continue;
                var at = e.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt64(out var n) && n > 0 ? n : 0;
                pins[entry.Name] = new TriggerPin(fp, t, at);
            }
        }
        catch (JsonException)
        {
            pins.Clear();
            unreadable = true;
        }
        return pins;
    }

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long? Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n) && n > 0 ? n : null;
}
