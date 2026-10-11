using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Stagewatch.App.ViewModels;

/// <summary>
/// Undo / redo of live edits, as the operator sees it (Ctrl+Z / Ctrl+Y, the Undo button, or the <c>undo</c> / <c>redo</c>
/// commands). The history lives in <see cref="Show.ShowController"/>: one per running order, with each edit's targets
/// checked by identity before it is replayed.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private string _undoLabel = "";
    [ObservableProperty] private string _redoLabel = "";

    private void RefreshUndo()
    {
        UndoLabel = _show.UndoLabel is { } u ? "Undo: " + u : "";
        RedoLabel = _show.RedoLabel is { } r ? "Redo: " + r : "";
    }

    private void BeginEditBatch(string label) => _show.BeginBatch(label);
    private void EndEditBatch() => _show.EndBatch();

    /// <summary>Mouse wheel on the timer: nudge, and remember it (quick turns merge into one undo step).</summary>
    public void NudgeFromWheel(TimeSpan delta) => _show.Nudge(delta);

    [RelayCommand] private void Undo() { RecordUi("undo"); _show.Undo(); }
    [RelayCommand] private void Redo() { RecordUi("redo"); _show.Redo(); }
}
