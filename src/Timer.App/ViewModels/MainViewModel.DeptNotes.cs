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
}
