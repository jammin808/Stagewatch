using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Department notes: each department keeps its own notes on running-order items ("Q12 blue wash", "radio mic 3"),
/// from its crew page (its crew link is its write permission) or in the running-order editor. Crew pages read only
/// their own department's notes; the web server reads an immutable copy swapped in on every change.
/// </summary>
public sealed partial class MainViewModel
{
    private volatile IReadOnlyList<(string Title, IReadOnlyDictionary<string, string> Notes)> _notesCopy = Array.Empty<(string, IReadOnlyDictionary<string, string>)>();
    private int _notesVersion;

    /// <summary>Called whenever the running order changes (edits, show files, notes).</summary>
    private void RefreshNotesCopy()
    {
        _notesCopy = Settings.Rundown.Items
            .Select(i => (i.Title, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(i.DeptNotes, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        Interlocked.Increment(ref _notesVersion);
    }

    public IReadOnlyList<(int Index, string Title, string Note)>? DeptNotesFor(string dept)
    {
        var copy = _notesCopy;
        if (copy.Count == 0) return null;
        var list = new List<(int, string, string)>(copy.Count);
        for (int i = 0; i < copy.Count; i++)
        {
            var note = "";
            foreach (var (k, v) in copy[i].Notes)
                if (string.Equals(k.Trim(), dept.Trim(), StringComparison.OrdinalIgnoreCase)) { note = v; break; }
            list.Add((i, copy[i].Title, note));
        }
        return list;
    }

    private void ApplyDeptNote(int item, string text)
    {
        var parts = text.Split('\u0001');
        if (parts.Length < 2) return;
        var (dept, note) = (parts[0].Trim(), parts[1]);
        var index = item - 1;
        if (index < 0 || index >= Settings.Rundown.Items.Count) { StatusLine = $"No item {item} for a {dept} note"; return; }
        var before = Settings.Rundown.Items[index].DeptNote(dept);
        if (before == note.Trim()) return;
        RecordEdit($"{dept} note on item {item}",
            new ShowCommand(ShowCommandKind.DeptNote, Number: item, Text: dept + "\u0001" + before),
            new ShowCommand(ShowCommandKind.DeptNote, Number: item, Text: dept + "\u0001" + note),
            targets: ItemTarget(index) is { } t ? new[] { t } : null);
        _store.Update(s => s.Rundown.Items[index].SetDeptNote(dept, note));
        _journal.Add(DateTime.UtcNow, JournalKind.Note, index, $"{dept} note: {(note.Length > 0 ? note : "(cleared)")}");
        RefreshNotesCopy();
        StatusLine = $"{dept} updated their note on item {item}";
        MarkDirty();
        Publish();
    }
}
