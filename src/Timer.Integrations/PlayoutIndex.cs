using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>One item of a PlayMap node's playlist as its <c>INDEX</c> lists it: its id, as a line can carry it, and its label.</summary>
public sealed record PlayoutIndexItem(string Id, string Label);

/// <summary>One of a PlayMap node's playlists as its <c>INDEX</c> lists it: id, name, sync group ("" for none) and items, in its order.</summary>
public sealed record PlayoutIndexPlaylist(string Id, string Name, string SyncGroup, IReadOnlyList<PlayoutIndexItem> Items);

/// <summary>One cue of a Patterns desk's stack as its <c>CUE LIST</c> lists it: id, number, name and whether it is switched on.</summary>
public sealed record PlayoutIndexCue(string Id, string Number, string Name, bool Enabled);

/// <summary>
/// What a playout peer lists of itself for running-order cues, read once per revision: a PlayMap node's playlists and their items
/// (its <c>INDEX</c>, whose <c>sig</c> its STATE's <c>project.index</c> repeats) or a Patterns desk's cues (its <c>CUE LIST</c>, whose
/// <c>listRev</c> its STATE's <c>cuestack.listRev</c> repeats). Ids are taken only as a line can carry them (never cut or cleaned
/// into another); names and labels are cut for people. Bounded: at most <see cref="MaxPlaylists"/> playlists of <see cref="MaxItems"/>
/// items, or <see cref="MaxCues"/> cues — a list past a bound is <see cref="Cut"/>, and an id it does not hold is then not known,
/// never called missing.
/// </summary>
public sealed record PlayoutIndex(string Kind, string Revision, IReadOnlyList<PlayoutIndexPlaylist> Playlists, IReadOnlyList<PlayoutIndexCue> Cues, bool Cut)
{
    /// <summary>The most playlists one INDEX is read for (STATE's own bound).</summary>
    public const int MaxPlaylists = PlayoutStateReader.MaxPlaylists;

    /// <summary>The most items of one playlist read from an INDEX.</summary>
    public const int MaxItems = 256;

    /// <summary>The most cues read from one CUE LIST.</summary>
    public const int MaxCues = 512;

    /// <summary>
    /// A PlayMap node's <c>INDEX</c> answer, read for its playlists and their items, with its <c>sig</c> as the revision; null for one
    /// it cannot read. Never an exception, whatever the peer sent.
    /// </summary>
    public static PlayoutIndex? ReadIndex(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("playlists", out var lists) || lists.ValueKind != JsonValueKind.Array) return null;
            var cut = false;
            var kept = new List<PlayoutIndexPlaylist>();
            foreach (var p in lists.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object || Id(p, "id") is not { } id) continue;
                if (kept.Count == MaxPlaylists) { cut = true; break; }
                var items = new List<PlayoutIndexItem>();
                if (p.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Array)
                    foreach (var i in its.EnumerateArray())
                    {
                        if (i.ValueKind != JsonValueKind.Object || Id(i, "id") is not { } itemId) continue;
                        if (items.Count == MaxItems) { cut = true; break; }
                        items.Add(new PlayoutIndexItem(itemId, Label(i, "label")));
                    }
                kept.Add(new PlayoutIndexPlaylist(id, Label(p, "name"), Id(p, "syncGroup") ?? "", items));
            }
            return new PlayoutIndex(PlayoutRules.PlayMap, Plain(root, "sig"), kept, [], cut);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// A Patterns desk's <c>CUE LIST</c> answer, read for its cues, with its <c>listRev</c> as the revision; null for one it cannot read.
    /// Never an exception, whatever the desk sent.
    /// </summary>
    public static PlayoutIndex? ReadCues(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cues", out var cues) || cues.ValueKind != JsonValueKind.Array) return null;
            var cut = false;
            var kept = new List<PlayoutIndexCue>();
            foreach (var c in cues.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object || Id(c, "id") is not { } id) continue;
                if (kept.Count == MaxCues) { cut = true; break; }
                var enabled = !c.TryGetProperty("enabled", out var e) || e.ValueKind != JsonValueKind.False;
                kept.Add(new PlayoutIndexCue(id, Plain(c, "number"), Label(c, "name"), enabled));
            }
            return new PlayoutIndex(PlayoutRules.Patterns, Plain(root, "listRev"), [], kept, cut);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>A playlist by its id, else by its name (any case), as a cue or a person names it; null when it lists none.</summary>
    public PlayoutIndexPlaylist? Playlist(string word)
        => Playlists.FirstOrDefault(p => p.Id == word) ?? Playlists.FirstOrDefault(p => p.Name.Equals(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>A cue by its id; null when it lists none.</summary>
    public PlayoutIndexCue? Cue(string id) => Cues.FirstOrDefault(c => c.Id == id);

    /// <summary>
    /// Why a running-order cue names what this list does not hold, in words naming <paramref name="peer"/>; null when it holds it, and
    /// null when it cannot tell — a list cut at its bound, or a cue of the other kind's shape (the plan refuses that one in its own
    /// words). A PlayMap cue names a playlist and an item of it (an item by its id, or <c>#n</c> by its place); a Patterns cue one cue.
    /// </summary>
    public string? Lacks(PlayoutCue cue, string peer)
    {
        if (Kind == PlayoutRules.PlayMap && cue.List.Length > 0)
        {
            if (Playlist(cue.List) is not { } pl) return Cut ? null : $"{peer} lists no playlist {cue.List}";
            if (cue.Item.StartsWith('#'))
                return int.TryParse(cue.Item.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= 1 && (n <= pl.Items.Count || Cut)
                    ? null : $"{peer}'s {Named(pl)} has no item {cue.Item}";
            return pl.Items.Any(i => i.Id == cue.Item) || Cut ? null : $"{peer}'s {Named(pl)} lists no item {cue.Item}";
        }
        if (Kind == PlayoutRules.Patterns && cue.List.Length == 0)
        {
            if (Cue(cue.Item) is not { } c) return Cut ? null : $"{peer} lists no cue {cue.Item}";
            return c.Enabled ? null : $"{peer}'s cue {Words(c)} is switched off";
        }
        return null;
    }

    /// <summary>A cue as people read it: its number and name, else its id.</summary>
    public static string Words(PlayoutIndexCue c)
        => (c.Number.Length > 0 ? c.Number + " " : "") + (c.Name.Length > 0 ? c.Name : c.Number.Length > 0 ? "" : c.Id);

    /// <summary>
    /// What the copilot sees of this list, at most <paramref name="max"/> lines: each playlist's items as <c>playlist item · label</c>,
    /// or each cue as <c>id · number name</c> (switched-off ones said so), ids a <c>playout</c> line can name.
    /// </summary>
    public IReadOnlyList<string> Lines(int max)
    {
        var lines = new List<string>();
        foreach (var pl in Playlists)
            foreach (var i in pl.Items)
            {
                if (lines.Count == max) return lines;
                lines.Add($"{pl.Id} {i.Id} · {(pl.Name.Length > 0 ? pl.Name + " · " : "")}{i.Label}");
            }
        foreach (var c in Cues)
        {
            if (lines.Count == max) return lines;
            lines.Add($"{c.Id} · {Words(c).Trim()}" + (c.Enabled ? "" : " (switched off)"));
        }
        return lines;
    }

    private static string Named(PlayoutIndexPlaylist pl) => pl.Name.Length > 0 ? pl.Name : pl.Id;

    /// <summary>An id as the peer wrote it when a line can carry it (<see cref="PlayoutRules.ValidWord"/>), else null.</summary>
    private static string? Id(JsonElement e, string name)
        => e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { } id && PlayoutRules.ValidWord(id) ? id : null;

    /// <summary>A name or label as people read it, no control character, cut at <see cref="PlayoutRules.MaxLabelChars"/> with "…".</summary>
    private static string Label(JsonElement e, string name)
        => e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String && s.GetString() is { } text
            ? ShowBusText.Cut(new string(text.Where(c => !char.IsControl(c)).ToArray()), PlayoutRules.MaxLabelChars)
            : "";

    /// <summary>A string or a whole number as plain text (a revision, a cue's number), at most <see cref="PlayoutRules.MaxRefChars"/>; "" otherwise.</summary>
    private static string Plain(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        var text = v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number when v.TryGetInt64(out var n) => n.ToString(CultureInfo.InvariantCulture),
            _ => "",
        };
        return text.Length <= PlayoutRules.MaxRefChars && !text.Any(char.IsControl) ? text : "";
    }
}
