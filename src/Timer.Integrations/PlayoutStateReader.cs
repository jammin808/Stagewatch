using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// The clip a playout peer has on air, as its STATE's <c>video</c> says it (Patterns, and PlayMap's focused playlist). Newer desks
/// (Patterns Round 86+, PlayMap) send milliseconds, <c>paused</c>, <c>rate</c> and a <c>seq</c> that changes only on jumps; older ones
/// whole seconds. Length 0 = not known yet. <paramref name="Transport"/>: the peer understands VIDEO PLAY / PAUSE / STOP / SEEK.
/// <paramref name="Role"/> is what the clip is to its peer (Patterns: <c>program</c>, <c>playlist</c>, <c>stinger</c>, <c>layer</c>;
/// PlayMap: <c>playlist</c>), <paramref name="Key"/> the peer's own name for its source as it wrote it, sent back to name it, so ""
/// when a line cannot carry it (never cut or cleaned into another), and <paramref name="Pausable"/> and
/// <paramref name="Stoppable"/> the peer's own word on whether VIDEO PAUSE and VIDEO STOP hold it, null where it gives none.
/// </summary>
public sealed record PlayoutVideo(string File, string Tag, double LengthSeconds, double PositionSeconds, bool Playing, bool Ended, bool Loops,
    bool Paused = false, double Rate = 1, long Seq = -1, bool Transport = false, string Role = "", string Key = "", bool? Pausable = null, bool? Stoppable = null)
{
    /// <summary>A countdown only makes sense for a clip with a known length that doesn't loop.</summary>
    public bool CountsDown => LengthSeconds > 0 && !Loops;
    public string Title => Tag.Length > 0 && !File.StartsWith(Tag, StringComparison.OrdinalIgnoreCase) ? $"{Tag} {File}".Trim() : File;
}

/// <summary>One of a PlayMap node's playlists, as its STATE's <c>playlists</c> says it: ids, never guessed; "" and null where it gives none.</summary>
public sealed record PlayoutPlaylist(string Id, string Name, string State, string ItemId, string ItemLabel, long? PositionMs, string SyncGroup,
    string NextId, string NextLabel, long Seq);

/// <summary>A PlayMap node's place in its sync group (STATE <c>sync</c>): <c>leader</c>, <c>follower</c> or <c>solo</c>, and the leader it names.</summary>
public sealed record PlayoutSync(string Role, string Leader);

/// <summary>
/// A Patterns desk's cue stack (STATE <c>cuestack</c>): armed, held, the list's revision (it moves when the list changes) and the cue
/// standing by, "" when none.
/// </summary>
public sealed record PlayoutCuestack(bool Armed, bool Hold, string ListRev, string StandbyId, string StandbyNumber, string StandbyName, bool StandbyConfirms);

/// <summary>
/// What a playout peer's STATE says, as far as a link reads it. A section is set only when its whole value was read and is of the
/// JSON type the section is written as (an object, or null for none; <c>playlists</c> an array): a STATE longer than a link reads
/// (13.14) is read for the sections that lie wholly within its first part, and a section past that part, or of another type, is not
/// known, never taken as empty (its <c>Has…</c> is false; one of another type is named in <see cref="Unreadable"/>).
/// </summary>
public sealed record PlayoutPeerState
{
    /// <summary>The whole STATE's size in UTF-8 bytes (its JSON).</summary>
    public long Bytes { get; init; }

    /// <summary>Read to its end: false for a STATE read in part.</summary>
    public bool Whole { get; init; }

    /// <summary>
    /// The kind it says: <see cref="PlayoutRules.PlayMap"/> for <c>"t":"playmap"</c>; <see cref="PlayoutRules.Patterns"/> for
    /// <c>"t":"patterns"</c>, or for a STATE read to its end with no <c>t</c> (Patterns sends none); "" for any other <c>t</c>; null
    /// when the part read reached no <c>t</c> and not the STATE's end.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>The node's Show Bus id when the STATE gives one of that shape (PlayMap's <c>id</c>), else "".</summary>
    public string Id { get; init; } = "";

    /// <summary>Its frame counter (§4: PlayMap's <c>seq</c>, Patterns' <c>rev</c>), or null.</summary>
    public long? Seq { get; init; }

    /// <summary>PlayMap's <c>at</c>: its wall clock when it wrote the STATE, Unix ms, or null.</summary>
    public long? At { get; init; }

    /// <summary>Its <c>video</c> was read; <see cref="Video"/> is then the clip on air, or null for none.</summary>
    public bool HasVideo { get; init; }
    public PlayoutVideo? Video { get; init; }

    /// <summary>Its <c>playlists</c> were read: the first <see cref="PlayoutStateReader.MaxPlaylists"/> with an id a line can carry.</summary>
    public bool HasPlaylists { get; init; }
    public IReadOnlyList<PlayoutPlaylist> Playlists { get; init; } = [];

    /// <summary>Its <c>sync</c> was read; <see cref="Sync"/> is then where it stands, or null for none.</summary>
    public bool HasSync { get; init; }
    public PlayoutSync? Sync { get; init; }

    /// <summary>Its <c>project</c> was read; <see cref="ProjectIndex"/> is then its <c>index</c> (it moves when the INDEX changes), or null.</summary>
    public bool HasProject { get; init; }
    public string? ProjectIndex { get; init; }

    /// <summary>Its <c>cuestack</c> was read; <see cref="Cuestack"/> is then the stack, or null for none.</summary>
    public bool HasCuestack { get; init; }
    public PlayoutCuestack? Cuestack { get; init; }

    /// <summary>The sections it gave as another JSON type than they are written as (<c>"video":"x"</c>), by name, in the order read: not known.</summary>
    public IReadOnlyList<string> Unreadable { get; init; } = [];
}

/// <summary>
/// A playout peer's STATE read forward, once (13.14): its top-level members are walked with a <see cref="Utf8JsonReader"/>, and only
/// the sections a link uses are taken, each once its whole value is in hand. So a STATE longer than a link reads is read for what lies
/// within the part it keeps — PlayMap writes <c>video</c> and <c>playlists</c> right after <c>air</c>, so its VT survives — and a
/// section cut at the end of that part is not known. A STATE read whole is checked to its end: one that does not parse, or is not an
/// object, is not read at all. Whatever bytes it is handed, it answers a reading or null and never throws: text that is not UTF-8,
/// or an escape that names half a character (<c>\ud800</c>), within a name or a string it reads, makes the STATE one it does not read.
/// </summary>
public static class PlayoutStateReader
{
    /// <summary>The most playlists kept of one STATE.</summary>
    public const int MaxPlaylists = 64;

    /// <summary>A STATE's JSON, whole (a test, a STATE asked for): <see cref="Read(ReadOnlySpan{byte}, long)"/> of its UTF-8.</summary>
    public static PlayoutPeerState? Read(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Read(bytes, bytes.Length);
    }

    /// <summary>
    /// <paramref name="json"/>: a STATE's JSON, or its first part when <paramref name="size"/>, the whole STATE's size in bytes, is larger.
    /// Null when it is not a STATE it reads: not an object, not JSON within what was read, or a name or string it reads that is not
    /// whole UTF-8 text.
    /// </summary>
    public static PlayoutPeerState? Read(ReadOnlySpan<byte> json, long size)
    {
        var whole = size <= json.Length;
        var reader = new Utf8JsonReader(json, isFinalBlock: whole, state: default);
        var st = new PlayoutPeerState { Bytes = Math.Max(size, json.Length), Whole = whole };
        string? kind = null;
        var ended = false;
        try
        {
            if (!reader.Read()) return whole ? null : st;
            if (reader.TokenType != JsonTokenType.StartObject) return null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) { ended = true; break; }
                var name = reader.GetString() ?? "";
                if (!Taken(name))
                {
                    // Skipped whole, or the part ends inside it: nothing after it is in hand.
                    if (!reader.Read() || !reader.TrySkip()) break;
                    continue;
                }
                if (!JsonDocument.TryParseValue(ref reader, out var doc)) break;
                using (doc) st = Take(st, name, doc.RootElement, ref kind);
            }
            // A STATE read whole is one value to its end.
            if (whole && (!ended || reader.Read())) return null;
        }
        catch (JsonException) { return null; }
        // GetString's answer to bytes that are not UTF-8, or to an escape of half a surrogate pair: the text is not one it reads.
        catch (InvalidOperationException) { return null; }
        return st with { Kind = kind ?? (ended ? PlayoutRules.Patterns : null) };
    }

    private static readonly string[] Sections = ["t", "id", "seq", "rev", "at", "video", "playlists", "sync", "project", "cuestack"];

    private static bool Taken(string name) => Sections.Contains(name, StringComparer.Ordinal);

    private static PlayoutPeerState Take(PlayoutPeerState st, string name, JsonElement v, ref string? kind)
    {
        switch (name)
        {
            case "t":
                kind = v.ValueKind == JsonValueKind.String && v.GetString() is PlayoutRules.Patterns or PlayoutRules.PlayMap ? v.GetString() : "";
                return st;
            case "id":
                return st with { Id = v.ValueKind == JsonValueKind.String && v.GetString() is { } id && ShowBusHello.ValidId(id) ? id : "" };
            case "seq":
            case "rev":
                return st with { Seq = Long(v) ?? st.Seq };
            case "at":
                return st with { At = Long(v) };
            case "playlists":
                return v.ValueKind == JsonValueKind.Array ? st with { HasPlaylists = true, Playlists = Playlists(v) } : Unread(st, name);
            case "video" or "sync" or "project" or "cuestack" when v.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null):
                return Unread(st, name);
            case "video":
                return st with { HasVideo = true, Video = v.ValueKind == JsonValueKind.Object ? Video(v) : null };
            case "sync":
                return st with { HasSync = true, Sync = v.ValueKind == JsonValueKind.Object ? new PlayoutSync(Word(v, "role", 16).ToLowerInvariant(), Word(v, "leader", PlayoutRules.MaxRefChars)) : null };
            case "project":
                return st with { HasProject = true, ProjectIndex = v.ValueKind == JsonValueKind.Object && v.TryGetProperty("index", out var ix) && Plain(ix) is { } index ? index : null };
            case "cuestack":
                return st with { HasCuestack = true, Cuestack = v.ValueKind == JsonValueKind.Object ? Cuestack(v) : null };
            default:
                return st;
        }
    }

    /// <summary>A section given as another JSON type than it is written as: not known, never taken as empty, and named.</summary>
    private static PlayoutPeerState Unread(PlayoutPeerState st, string name) => st with { Unreadable = [.. st.Unreadable, name] };

    /// <summary>The clip on air, read as the VT countdown has always read it, with the peer's own words on its role and transport.</summary>
    private static PlayoutVideo Video(JsonElement v)
    {
        string S(string n) => v.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? (p.GetString() ?? "").Trim() : "";
        double D(string n) => v.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d) && double.IsFinite(d) ? Math.Max(0, d) : 0;
        bool B(string n) => v.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;
        bool? Said(string n) => v.TryGetProperty(n, out var p) ? p.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null } : null;
        var file = S("file");
        // Round 86+ and PlayMap: milliseconds (null length = not known yet), paused, rate, seq. Older desks: whole seconds.
        var precise = v.TryGetProperty("positionMs", out var pm) && pm.ValueKind == JsonValueKind.Number;
        var length = D("length");
        var position = D("position");
        if (precise)
        {
            position = pm.TryGetDouble(out var p0) && double.IsFinite(p0) ? Math.Max(0, p0 / 1000) : position;
            length = v.TryGetProperty("lengthMs", out var lm) && lm.ValueKind == JsonValueKind.Number && lm.TryGetDouble(out var l0) && double.IsFinite(l0) ? Math.Max(0, l0 / 1000) : 0;
        }
        var rate = v.TryGetProperty("rate", out var rt) && rt.ValueKind == JsonValueKind.Number && rt.TryGetDouble(out var r0) && r0 is > 0.05 and < 20 ? r0 : 1;
        var seq = v.TryGetProperty("seq", out var sq) && sq.ValueKind == JsonValueKind.Number && sq.TryGetInt64(out var s0) ? s0 : -1;
        return new PlayoutVideo(file.Length > 120 ? file[..120] : file, S("tag") is var t && t.Length <= 30 ? t : "", length, position, B("playing"), B("ended"), B("loops"),
            Paused: B("paused"), Rate: rate, Seq: seq, Transport: precise, Role: Word(v, "role", 24).ToLowerInvariant(), Key: Ref(v, "key"),
            Pausable: Said("pausable"), Stoppable: Said("stoppable"));
    }

    private static List<PlayoutPlaylist> Playlists(JsonElement list)
    {
        var kept = new List<PlayoutPlaylist>();
        foreach (var p in list.EnumerateArray())
        {
            if (kept.Count == MaxPlaylists) break;
            if (p.ValueKind != JsonValueKind.Object || Ref(p, "id") is not { Length: > 0 } id) continue;
            var item = p.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
            var next = p.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.Object ? n : default;
            kept.Add(new PlayoutPlaylist(id, Label(p, "name"), Word(p, "state", 16).ToLowerInvariant(), Ref(item, "id"), Label(item, "label"),
                p.TryGetProperty("positionMs", out var pos) ? Long(pos) : null, Ref(p, "syncGroup"), Ref(next, "id"), Label(next, "label"),
                p.TryGetProperty("seq", out var s) ? Long(s) ?? 0 : 0));
        }
        return kept;
    }

    private static PlayoutCuestack Cuestack(JsonElement c)
    {
        var standby = c.TryGetProperty("standby", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
        return new PlayoutCuestack(Is(c, "armed"), Is(c, "hold"), c.TryGetProperty("listRev", out var rev) ? Plain(rev) ?? "" : "",
            Ref(standby, "id"), standby.ValueKind == JsonValueKind.Object && standby.TryGetProperty("number", out var num) ? Plain(num) ?? "" : "",
            Label(standby, "name"), Is(standby, "requireConfirm"));
    }

    private static bool Is(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var b) && b.ValueKind == JsonValueKind.True;

    private static long? Long(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) ? n : null;

    /// <summary>A string or a whole number as plain text (a revision, an index, a cue's number), at most <see cref="PlayoutRules.MaxRefChars"/>; null for anything else.</summary>
    private static string? Plain(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => Clean(e.GetString() ?? "", PlayoutRules.MaxRefChars),
        JsonValueKind.Number when e.TryGetInt64(out var n) => n.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>A string member, no control character in it, clipped at <paramref name="max"/>; "" when absent or not a string.</summary>
    private static string Word(JsonElement e, string name, int max)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String ? Clean(s.GetString() ?? "", max) : "";

    /// <summary>An id as the peer wrote it when a line can carry it (<see cref="PlayoutRules.ValidWord"/>), else "": an id is never cut or cleaned into another one.</summary>
    private static string Ref(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { } id && PlayoutRules.ValidWord(id) ? id : "";

    /// <summary>A name or label as people read it, cut at <see cref="PlayoutRules.MaxLabelChars"/> with "…".</summary>
    private static string Label(JsonElement e, string name)
        => ShowBusText.Cut(Word(e, name, int.MaxValue), PlayoutRules.MaxLabelChars);

    private static string Clean(string text, int max)
        => ShowBusText.Clip(text.Any(char.IsControl) ? new string(text.Where(c => !char.IsControl(c)).ToArray()) : text, max);
}
