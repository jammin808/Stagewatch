namespace Stagewatch.Core;

/// <summary>
/// Undo / redo for live edits (trims, delays, nudges, department notes, recoveries, approved copilot plans). The
/// command pattern from games: each edit is stored as the commands that reverse it and the commands that redo it, so
/// undoing goes through the same single command path as everything else and can never do what a person couldn't.
/// Several edits made as one action (a recovery, a copilot plan) form one batch and undo together. Show thread only.
/// </summary>
public sealed class LiveEdits
{
    public sealed record Edit(string Label, IReadOnlyList<ShowCommand> Undo, IReadOnlyList<ShowCommand> Redo, DateTime AtUtc, string? MergeKey = null,
        IReadOnlyList<EditTarget>? Targets = null);

    /// <summary>
    /// What an edit touched: running-order item <paramref name="Index"/> whose id was <paramref name="ItemId"/> (commands are
    /// positional, so the id proves the position still means the same item). <paramref name="MustBeLive"/>: the edit acted on
    /// the live item's clock (a nudge, a delay), so it only makes sense while that item is still the live one.
    /// </summary>
    public sealed record EditTarget(int Index, string ItemId, bool MustBeLive = false);

    private readonly List<Edit> _undo = new();
    private readonly List<Edit> _redo = new();
    private readonly int _capacity;
    private (string Label, List<ShowCommand> Undo, List<ShowCommand> Redo, List<EditTarget> Targets)? _batch;

    public LiveEdits(int capacity = 50) => _capacity = capacity;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;
    public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    /// <summary>
    /// Record one edit. Inside a batch it joins the batch. Edits with the same <paramref name="mergeKey"/> made within
    /// two seconds of each other (mouse-wheel nudges) merge into one step, labelled by <paramref name="mergedLabel"/>.
    /// </summary>
    public void Record(string label, ShowCommand undo, ShowCommand redo, string? mergeKey = null, Func<IReadOnlyList<ShowCommand>, string>? mergedLabel = null, DateTime? nowUtc = null,
        IReadOnlyList<EditTarget>? targets = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        targets ??= Array.Empty<EditTarget>();
        if (_batch is { } b)
        {
            b.Undo.Insert(0, undo);      // undone in reverse order
            b.Redo.Add(redo);
            b.Targets.AddRange(targets);
            return;
        }
        if (mergeKey is not null && _redo.Count == 0 && _undo.Count > 0 && _undo[^1] is { } top && top.MergeKey == mergeKey && now - top.AtUtc < TimeSpan.FromSeconds(2)
            && (top.Targets ?? Array.Empty<EditTarget>()).SequenceEqual(targets))
        {
            var redoAll = top.Redo.Append(redo).ToList();
            _undo[^1] = new Edit(mergedLabel?.Invoke(redoAll) ?? label, new[] { undo }.Concat(top.Undo).ToList(), redoAll, now, mergeKey, top.Targets);
            return;
        }
        Push(new Edit(label, new[] { undo }, new[] { redo }, now, mergeKey, targets.ToList()));
    }

    /// <summary>
    /// Forget edits whose targets are gone or have moved (an item deleted or reordered, a nudge whose item is no longer
    /// live). Replaying them would change something else. Returns how many were dropped.
    /// </summary>
    public int DropInvalid(Func<EditTarget, bool> stillValid)
    {
        bool Valid(Edit e) => (e.Targets ?? Array.Empty<EditTarget>()).All(stillValid);
        return _undo.RemoveAll(e => !Valid(e)) + _redo.RemoveAll(e => !Valid(e));
    }

    public bool InBatch => _batch is not null;

    /// <summary>Group the edits made until <see cref="EndBatch"/> into one undo step.</summary>
    public void BeginBatch(string label) => _batch ??= (label, new List<ShowCommand>(), new List<ShowCommand>(), new List<EditTarget>());

    public void EndBatch()
    {
        if (_batch is not { } b) return;
        _batch = null;
        if (b.Undo.Count > 0) Push(new Edit(b.Label, b.Undo, b.Redo, DateTime.UtcNow, Targets: b.Targets.Distinct().ToList()));
    }

    private void Push(Edit e)
    {
        _undo.Add(e);
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _redo.Clear();               // a new edit ends the redo history, as in every editor
    }

    /// <summary>The next edit to undo (moved to the redo stack), or null.</summary>
    public Edit? TakeUndo()
    {
        if (_undo.Count == 0) return null;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(e);
        return e;
    }

    public Edit? TakeRedo()
    {
        if (_redo.Count == 0) return null;
        var e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(e);
        return e;
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); _batch = null; }
}
