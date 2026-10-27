using System.Text.RegularExpressions;

namespace Stagewatch.Core;

/// <summary>
/// Running orders from a shared spreadsheet (Google Sheets, or any link that returns CSV). The sheet is the plan:
/// its rows set the order and the planned details. "Update" keeps what was done in Stagewatch on items it can match
/// by title — live trims, operator notes, cue ladders, department notes, ids — so re-importing a revised sheet during
/// the day doesn't lose the day's work. "Replace" takes the sheet as it is. Pure: no network here.
/// </summary>
public static class SheetImport
{
    /// <summary>
    /// A link someone pasted → a CSV download link. Google Sheets edit/share links become their CSV export (the sheet
    /// must be shared "anyone with the link can view", or published to the web). Null when it isn't a usable https link.
    /// </summary>
    public static string? CsvUrl(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        link = link.Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        if (uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            // Published to the web: …/pub?output=csv (or …/pub?…&output=csv) — use as is, forcing CSV.
            if (uri.AbsolutePath.Contains("/pub", StringComparison.Ordinal))
                return link.Contains("output=csv", StringComparison.OrdinalIgnoreCase) ? link : link + (uri.Query.Length > 0 ? "&" : "?") + "output=csv";
            var m = Regex.Match(uri.AbsolutePath, @"/spreadsheets/d/([A-Za-z0-9_-]{10,})");
            if (!m.Success) return null;
            var gid = Regex.Match(link, @"[#&?]gid=(\d+)");
            return $"https://docs.google.com/spreadsheets/d/{m.Groups[1].Value}/export?format=csv" + (gid.Success ? "&gid=" + gid.Groups[1].Value : "");
        }
        return link;
    }

    public sealed record MergeResult(List<RundownItem> Items, int Updated, int Added, IReadOnlyList<string> Dropped);

    /// <summary>
    /// The sheet's rows, in the sheet's order. Matched items (same title, first unused match) keep their id, live
    /// trims, operator notes, cue ladder and the department notes the sheet doesn't have columns for.
    /// Items not in the sheet are dropped and listed, so the operator can see what went.
    /// </summary>
    /// <summary>
    /// Three-way merge of one item's department notes when the editor saves. Per department: if the editor's note differs
    /// from what the editor opened with (<paramref name="baseline"/>), it was changed here — typed, or brought in by a sheet —
    /// and wins; otherwise the stored note wins, so a crew member's edit made while the editor was open isn't lost.
    /// No baseline (a new row) or no stored item: the editor's notes as they are.
    /// </summary>
    public static Dictionary<string, string> MergeDeptNotes(IReadOnlyDictionary<string, string>? baseline, IReadOnlyDictionary<string, string> editor,
        IReadOnlyDictionary<string, string>? stored)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (baseline is null || stored is null)
        {
            foreach (var (k, v) in editor) if (!string.IsNullOrWhiteSpace(v)) result[k] = v;
            return result;
        }
        static string Get(IReadOnlyDictionary<string, string> d, string k)
            => d.FirstOrDefault(kv => string.Equals(kv.Key, k, StringComparison.OrdinalIgnoreCase)).Value?.Trim() ?? "";
        var depts = baseline.Keys.Concat(editor.Keys).Concat(stored.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var d in depts)
        {
            var mine = Get(editor, d);
            var value = mine != Get(baseline, d) ? mine : Get(stored, d);
            if (value.Length > 0) result[d] = value;
        }
        return result;
    }

    public static MergeResult Update(IReadOnlyList<RundownItem> current, IReadOnlyList<RundownItem> sheet)
    {
        var used = new bool[current.Count];
        var result = new List<RundownItem>(sheet.Count);
        int updated = 0, added = 0;
        foreach (var row in sheet)
        {
            var match = -1;
            for (int i = 0; i < current.Count; i++)
                if (!used[i] && string.Equals(current[i].Title.Trim(), row.Title.Trim(), StringComparison.OrdinalIgnoreCase)) { match = i; break; }
            if (match < 0) { result.Add(row.Clone()); added++; continue; }
            used[match] = true;
            var old = current[match];
            var merged = row.Clone();
            merged.Id = old.Id;
            merged.AdjustSeconds = old.AdjustSeconds;
            merged.Notes = old.Notes;
            merged.CueLadderId = old.CueLadderId;
            merged.Playout = old.Playout.Select(c => c.Clone()).ToList();   // a sheet has no playout cues: the item keeps its own
            merged.Message ??= old.Message;
            merged.Group ??= old.Group;
            foreach (var (dept, note) in old.DeptNotes)
                if (merged.DeptNote(dept).Length == 0) merged.SetDeptNote(dept, note);
            result.Add(merged);
            updated++;
        }
        var dropped = current.Where((_, i) => !used[i]).Select(i => i.Title).ToList();
        return new MergeResult(result, updated, added, dropped);
    }
}
