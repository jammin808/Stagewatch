using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Undo / redo of live edits (Ctrl+Z / Ctrl+Y, the Undo button, or the <c>undo</c> / <c>redo</c> commands). Each running
/// order keeps its own history (switching to Day 2 never undoes something in Day 1), and every edit remembers the ids of
/// the items it touched: an edit whose item was deleted, moved or is no longer live is dropped, never replayed onto
/// whatever now sits in that position.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, LiveEdits> _editsByOrder = new(StringComparer.OrdinalIgnoreCase);
    private LiveEdits? _batchEdits;
    private bool _replaying;

    /// <summary>The live running order's history (or the one a batch began in, until it ends).</summary>
    private LiveEdits _edits
    {
        get
        {
            if (_batchEdits is { InBatch: true } b) return b;
            var key = Settings.RundownName ?? "";
            if (!_editsByOrder.TryGetValue(key, out var e)) _editsByOrder[key] = e = new LiveEdits();
            return e;
        }
    }

    private void BeginEditBatch(string label) { _batchEdits = _edits; _batchEdits.BeginBatch(label); }
    private void EndEditBatch() { var b = _batchEdits; _batchEdits = null; b?.EndBatch(); RefreshUndo(); }

    /// <summary>A running order was renamed: its history goes with it.</summary>
    private void RenameEditHistory(string oldName, string newName)
    {
        if (_editsByOrder.Remove(oldName, out var e)) _editsByOrder[newName] = e;
    }

    /// <summary>The item at <paramref name="index"/> as an undo target (null when there's no such item).</summary>
    private LiveEdits.EditTarget? ItemTarget(int index, bool mustBeLive = false)
        => index >= 0 && index < Settings.Rundown.Items.Count ? new LiveEdits.EditTarget(index, Settings.Rundown.Items[index].Id, mustBeLive) : null;

    /// <summary>The live item's clock, when a running-order item is live (a free timer has no target).</summary>
    private LiveEdits.EditTarget[] LiveTarget()
        => _rundownActive && ItemTarget(_currentItem, mustBeLive: true) is { } t ? new[] { t } : Array.Empty<LiveEdits.EditTarget>();

    private bool TargetStillValid(LiveEdits.EditTarget t)
        => t.Index >= 0 && t.Index < Settings.Rundown.Items.Count && Settings.Rundown.Items[t.Index].Id == t.ItemId
           && (!t.MustBeLive || (_rundownActive && _currentItem == t.Index));

    [ObservableProperty] private string _undoLabel = "";
    [ObservableProperty] private string _redoLabel = "";

    /// <summary>Called where an edit happens. Edits made while undoing or redoing aren't recorded again.</summary>
    private void RecordEdit(string label, ShowCommand undo, ShowCommand redo, string? mergeKey = null, IReadOnlyList<LiveEdits.EditTarget>? targets = null)
    {
        if (_replaying) return;
        _edits.Record(label, undo, redo, mergeKey, mergeKey == "nudge" ? NudgeLabel : null, targets: targets);
        RefreshUndo();
    }

    /// <summary>Several quick nudges read as one: "Nudge +1:30".</summary>
    private static string NudgeLabel(IReadOnlyList<ShowCommand> redo)
    {
        var total = (long)redo.Sum(c => c.Number);
        return $"Nudge {(total >= 0 ? "+" : "−")}{TimerTick.FormatSeconds(Math.Abs(total))}";
    }

    /// <summary>A nudge from the operator's buttons or mouse wheel (they act on the engine directly).</summary>
    private void RecordNudge(TimeSpan delta)
        => RecordEdit(NudgeLabel(new[] { new ShowCommand(ShowCommandKind.Nudge, Number: delta.TotalSeconds) }),
            new ShowCommand(ShowCommandKind.Nudge, Number: -delta.TotalSeconds),
            new ShowCommand(ShowCommandKind.Nudge, Number: delta.TotalSeconds), "nudge", LiveTarget());

    private void RefreshUndo()
    {
        _edits.DropInvalid(TargetStillValid);
        UndoLabel = _edits.NextUndoLabel is { } u ? "Undo: " + u : "";
        RedoLabel = _edits.NextRedoLabel is { } r ? "Redo: " + r : "";
    }

    private void UndoEdit()
    {
        var dropped = _edits.DropInvalid(TargetStillValid);
        if (_edits.TakeUndo() is not { } e) { StatusLine = dropped > 0 ? "Nothing to undo — the items those edits changed have moved or gone" : "Nothing to undo"; RefreshUndo(); return; }
        Replay(e.Undo);
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, "Undid: " + e.Label);
        StatusLine = "Undid: " + e.Label;
        RefreshUndo();
    }

    private void RedoEdit()
    {
        var dropped = _edits.DropInvalid(TargetStillValid);
        if (_edits.TakeRedo() is not { } e) { StatusLine = dropped > 0 ? "Nothing to redo — the items those edits changed have moved or gone" : "Nothing to redo"; RefreshUndo(); return; }
        Replay(e.Redo);
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, "Redid: " + e.Label);
        StatusLine = "Redid: " + e.Label;
        RefreshUndo();
    }

    private void Replay(IReadOnlyList<ShowCommand> commands)
    {
        _replaying = true;
        try { foreach (var c in commands) ExecuteNow(c); }
        finally { _replaying = false; }
        MarkDirty();
        Publish();
    }

    /// <summary>Mouse wheel on the timer: nudge, and remember it (quick turns merge into one undo step).</summary>
    public void NudgeFromWheel(TimeSpan delta)
    {
        _engine.Nudge(delta);
        RecordNudge(delta);
    }

    [RelayCommand] private void Undo() { RecordUi("undo"); UndoEdit(); }
    [RelayCommand] private void Redo() { RecordUi("redo"); RedoEdit(); }
}
