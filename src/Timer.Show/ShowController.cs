using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>Features the controller asks the host about (licensing lives with the host).</summary>
public enum ShowFeature { RunningOrder, Recovery, Roll }

/// <summary>
/// What the show core needs from its host (the operator UI, or a test): permission, words for the operator, and the
/// side effects that belong at the edges — screens, triggers, chimes, the overrun history. Called on the owner thread.
/// </summary>
public interface IShowHost
{
    /// <summary>Whether a feature may be used now. When not, the host tells the operator why.</summary>
    bool Allow(ShowFeature feature);

    /// <summary>A line for the operator's status bar.</summary>
    void Status(string line);

    /// <summary>A running-order item was loaded (not necessarily started): clear end-at, show its own message.</summary>
    void ItemLoaded(int index, RundownItem item);

    /// <summary>A running-order item started for the first time this run.</summary>
    void ItemStarted(int index, RundownItem item, bool firstOfShow);

    /// <summary>A running-order item that ran has ended.</summary>
    void ItemEnded(int index, RundownItem item, TimeSpan ran);

    /// <summary>The running order's plan or state changed: rebuild the rows.</summary>
    void RundownChanged();

    /// <summary>The running order's content changed (lengths, items): remotes and Companion refetch their config.</summary>
    void ConfigChanged();

    /// <summary>Department notes changed (crew pages refetch).</summary>
    void DeptNotesChanged();

    /// <summary>Undo / redo labels or Roll's state changed.</summary>
    void ControlsChanged();

    /// <summary>
    /// What was asked can't be done (no such running order, nothing to undo, a bad time…): tell the operator, and tell
    /// whoever sent the command — a status line alone can't be mistaken for success.
    /// </summary>
    void Refused(string why);

    /// <summary>Something visible changed: build and publish a frame (now, or with the next one).</summary>
    void Changed(bool publishNow);
}

/// <summary>
/// What the controller writes into a session checkpoint and restores from it. Items are named by identity
/// (<paramref name="CurrentItemId"/>, <paramref name="ActualIds"/>): positions are only a fallback for checkpoints written
/// before identities, because the running order may have been edited between the checkpoint and the restore.
/// </summary>
public sealed record ShowCursor(
    string Title, int CurrentItem, bool RundownActive, bool ItemStarted, DateTime? ShowAnchorUtc, DateTime? PinnedStartUtc,
    IReadOnlyDictionary<int, ItemActual> Actuals, double PendingDelaySeconds, string? RundownId, string? Execution,
    string? CurrentItemId = null, IReadOnlyDictionary<int, string>? ActualIds = null);

/// <summary>
/// The show's core: which running-order item is live and every transition around it — load, start, end, expiry and
/// auto-advance, loops, Roll, switching running orders, the schedule's anchors, trims, delays, department notes and
/// undo. No UI: the host supplies permission, words and edge effects (<see cref="IShowHost"/>), and time comes from a
/// <see cref="TimeProvider"/>, so a test drives whole workflows deterministically on a manual clock.
///
/// Single writer: every method runs on the owner thread (the UI thread in the app, the test thread in tests). Nothing
/// here locks; readers on other threads see only the immutable frames the host publishes.
/// </summary>
public sealed class ShowController
{
    private readonly SettingsStore _store;
    private readonly TimerEngine _engine;
    private readonly IShowHost _host;
    private readonly TimeProvider _time;

    private ShowJournal _journal = new(null);
    private readonly Dictionary<int, ItemActual> _actuals = new();
    private readonly Dictionary<int, string> _actualIds = new();   // which item each actual belongs to, so edits can't move it
    private readonly Dictionary<string, LiveEdits> _editsByOrder = new(StringComparer.OrdinalIgnoreCase);
    private LiveEdits? _batchEdits;
    private bool _replaying;
    private TimerState _prevState = TimerState.Idle;

    // Roll
    private int _rollItem = -1;
    private bool _rollWaiting;
    private bool _rollApplying;
    private DateTime _rollStartUtc;
    private DateTime? _rollEndUtc;

    public ShowController(SettingsStore store, TimerEngine engine, IShowHost host, TimeProvider? time = null)
    {
        _store = store;
        _engine = engine;
        _host = host;
        _time = time ?? TimeProvider.System;
        Journal = _journal;
    }

    private AppSettings Settings => _store.Settings;
    private DateTime Now => _time.GetUtcNow().UtcDateTime;
    public ShowClock ShowClock => ShowClock.For(Settings.Rundown.TimeZoneId);

    // ------------------------------------------------------------------ state (read by the host)

    /// <summary>The live (or loaded) running-order item, or -1.</summary>
    public int CurrentItem { get; private set; } = -1;
    /// <summary>The timer is showing a running-order item (not a preset, a typed duration or the clock).</summary>
    public bool RundownActive { get; private set; }
    /// <summary>The loaded item has started this run.</summary>
    public bool ItemStarted { get; private set; }
    /// <summary>What the timer is showing, for screens ("Keynote — Ana", a preset's name, "Count up").</summary>
    public string ActiveTitle { get; private set; } = "";
    /// <summary>Plan start when no "Starts" time is set: the moment the first item ran.</summary>
    public DateTime? ShowAnchorUtc { get; private set; }
    /// <summary>The "Starts HH:MM" occurrence, pinned once the show is live (so midnight can't move it).</summary>
    public DateTime? PinnedStartUtc { get; private set; }
    /// <summary>The announced delay before the next item.</summary>
    public TimeSpan PendingDelay { get; private set; }
    /// <summary>When each item actually started and ended, by position in the live running order.</summary>
    public IReadOnlyDictionary<int, ItemActual> Actuals => _actuals;
    /// <summary>Which pass of a looping running order is on (0 = the first).</summary>
    public int LoopPass { get; private set; }
    /// <summary>The current run of the live item: new each time an item is loaded, so history tells runs apart.</summary>
    public string Execution { get; private set; } = "";
    public bool RollMode { get; private set; }
    public string RollLine { get; private set; } = "";

    public string? UndoLabel => Edits.NextUndoLabel;
    public string? RedoLabel => Edits.NextRedoLabel;

    /// <summary>The show journal. Setting it (a new show, a restored one) stamps its entries with identities.</summary>
    public ShowJournal Journal
    {
        get => _journal;
        set { _journal = value; _journal.Identify = IdentityOf; }
    }

    // ------------------------------------------------------------------ the frame loop

    /// <summary>Every engine tick, on the owner thread. A countdown that just reached zero moves the show on.</summary>
    public void OnTick(TimerTick t)
    {
        var justExpired = _prevState == TimerState.Running && t.State == TimerState.Expired;
        _prevState = t.State;
        if (justExpired) OnExpired();
    }

    /// <summary>Housekeeping (4 Hz in the app): Roll follows the clock.</summary>
    public void Housekeeping() => UpdateRoll();

    // ------------------------------------------------------------------ transport

    /// <summary>Start (or resume) the timer; a running-order item that hadn't started is marked started.</summary>
    public void Start()
    {
        _engine.Start();
        if (RundownActive && CurrentItem >= 0 && _engine.Mode == TimerMode.Countdown && !ItemStarted)
        {
            ItemStarted = true;
            MarkItemStarted(CurrentItem);
            if (PinnedStartUtc is null && ResolveScheduledStartUtc() is { } planned) PinnedStartUtc = planned;
            if (ShowAnchorUtc is null && PinnedStartUtc is null)
            {
                var before = Settings.Rundown.Items.Take(CurrentItem).Sum(i => (long)Math.Max(0, i.DurationSeconds));
                ShowAnchorUtc = Now.AddSeconds(-before);
            }
            _host.RundownChanged();
        }
        _host.Changed(false);
    }

    /// <summary>Nudge the clock, remembered for undo (quick turns of the wheel merge into one step).</summary>
    public void Nudge(TimeSpan delta)
    {
        _engine.Nudge(delta);
        RecordEdit(NudgeLabel(new[] { new ShowCommand(ShowCommandKind.Nudge, Number: delta.TotalSeconds) }),
            new ShowCommand(ShowCommandKind.Nudge, Number: -delta.TotalSeconds),
            new ShowCommand(ShowCommandKind.Nudge, Number: delta.TotalSeconds), "nudge", LiveTarget());
    }

    /// <summary>The timer now shows something that isn't a running-order item (a preset, a typed duration, a mode).</summary>
    public void LeaveRundown(string title)
    {
        RundownActive = false;
        ActiveTitle = title;
        _host.Changed(false);
    }

    /// <summary>Set what screens call the timer, without leaving the running order (Roll's "Next: …").</summary>
    public void SetTitle(string title) => ActiveTitle = title;

    // ------------------------------------------------------------------ running order

    public void Next()
    {
        var items = Settings.Rundown.Items;
        if (items.Count == 0) { _host.Refused("Running order is empty — add items with Edit…"); return; }
        var target = CurrentItem + 1;
        if (target >= items.Count) { _host.Refused("End of running order"); return; }
        // While the show is live, Next rolls straight into the next item.
        GoTo(target, start: _engine.IsCounting || _engine.State == TimerState.Expired);
    }

    public void Previous()
    {
        if (Settings.Rundown.Items.Count == 0) return;
        GoTo(Math.Max(0, CurrentItem - 1), start: false);
    }

    /// <summary>Load item <paramref name="index"/> (and start it). Ends the item that was running.</summary>
    public void GoTo(int index, bool start)
    {
        if (!_host.Allow(ShowFeature.RunningOrder)) return;
        var items = Settings.Rundown.Items;
        if (index < 0 || index >= items.Count) { _host.Refused(items.Count == 0 ? "Running order is empty" : $"No item {index + 1} — the running order has {items.Count}"); return; }
        var item = items[index];
        if (RundownActive && ItemStarted) MarkItemEnded(CurrentItem);
        CurrentItem = index;
        _currentItemId = item.Id;
        RundownActive = true;
        ItemStarted = false;
        Execution = "x" + Guid.NewGuid().ToString("N")[..10];
        _engine.SetMode(TimerMode.Countdown);
        _engine.SetDuration(TimeSpan.FromSeconds(Math.Max(0, item.EffectiveSeconds)));
        ActiveTitle = item.DisplayTitle;
        _host.ItemLoaded(index, item);
        if (start) Start();
        RefreshEdits();   // a delay or nudge on the item that just ended can no longer be undone: say so now
        _host.Status($"{index + 1}/{items.Count}: {item.DisplayTitle}");
        _host.Changed(true);
    }

    /// <summary>The countdown reached zero: a looping running order starts again; a linked item rolls on.</summary>
    private void OnExpired()
    {
        if (!RundownActive || CurrentItem < 0 || RollMode) return;   // Roll decides what comes next by the clock
        var items = Settings.Rundown.Items;
        if (CurrentItem == items.Count - 1 && (Settings.Rundown.Loop < 0 || LoopPass < Settings.Rundown.Loop))
        {
            // End this pass's last item before its record is cleared, or its run would vanish from the report.
            if (ItemStarted) MarkItemEnded(CurrentItem);
            ItemStarted = false;
            LoopPass++;
            _actuals.Clear();
            _actualIds.Clear();
            PinnedStartUtc = Now;     // this pass is planned from now
            _journal.Add(Now, JournalKind.Note, -1, $"Loop: pass {LoopPass + 1}");
            GoTo(0, start: true);
            return;
        }
        if (CurrentItem < items.Count && items[CurrentItem].AutoAdvance && CurrentItem + 1 < items.Count)
            GoTo(CurrentItem + 1, start: true);
    }

    /// <summary>Back to before the first item: history so far stays in the journal.</summary>
    public void ResetShow()
    {
        LoopPass = 0;
        if (RundownActive && ItemStarted) MarkItemEnded(CurrentItem);
        _actuals.Clear();
        _actualIds.Clear();
        PendingDelay = TimeSpan.Zero;
        _journal.Add(Now, JournalKind.Note, -1, "Running order reset");
        CurrentItem = -1;
        RundownActive = false;
        ItemStarted = false;
        ShowAnchorUtc = null;
        PinnedStartUtc = null;
        _host.RundownChanged();
        _host.Status("Running order reset — Next loads item 1");
        _host.Changed(true);
    }

    /// <summary>A different show was opened: start at the top of its running order, with a fresh undo history.</summary>
    public void NewShow()
    {
        if (RollMode) SetRoll(false);
        CurrentItem = -1;
        RundownActive = false;
        ItemStarted = false;
        ShowAnchorUtc = null;
        PinnedStartUtc = null;
        _actuals.Clear();
        _actualIds.Clear();
        PendingDelay = TimeSpan.Zero;
        LoopPass = 0;
        Execution = "";
        _currentItemId = null;
        _editsByOrder.Clear();
        Settings.Rundown.EnsureIds();
        _host.ControlsChanged();
    }

    /// <summary>
    /// The running order was edited. The cursor and the record of what ran follow their items by id, so deleting or
    /// moving an item above the live one never makes the cursor point at a different item. If the live item itself was
    /// deleted, the cursor stays where it was (clamped) but nothing is live.
    /// </summary>
    public void RundownEdited()
    {
        var items = Settings.Rundown.Items;
        var indexOf = new Dictionary<string, int>();
        for (int i = 0; i < items.Count; i++) indexOf.TryAdd(items[i].Id, i);
        if (CurrentItem >= 0 && _currentItemId is { } liveId)
        {
            if (indexOf.TryGetValue(liveId, out var moved)) CurrentItem = moved;
            else { RundownActive = false; ItemStarted = false; }
        }
        var actuals = _actuals.ToList();
        var ids = new Dictionary<int, string>(_actualIds);
        _actuals.Clear();
        _actualIds.Clear();
        foreach (var (oldIndex, actual) in actuals)
            if (ids.TryGetValue(oldIndex, out var id) && indexOf.TryGetValue(id, out var newIndex)) { _actuals[newIndex] = actual; _actualIds[newIndex] = id; }
        if (CurrentItem >= items.Count) CurrentItem = items.Count - 1;
        if (items.Count == 0) { CurrentItem = -1; RundownActive = false; ShowAnchorUtc = null; PinnedStartUtc = null; }
        RefreshEdits();
    }

    private string? _currentItemId;

    /// <summary>
    /// Make another running order live. The old item ends (journalled), its timer stops and its title comes off screens;
    /// the new running order's first item is loaded, ready to start. Each running order keeps its own undo history.
    /// </summary>
    public bool SwitchRundown(string name)
    {
        name = name.Trim();
        if (string.Equals(name, Settings.RundownName, StringComparison.OrdinalIgnoreCase)) return true;
        var target = Settings.OtherRundowns.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target is null) { _host.Refused($"No running order called \"{name}\""); return false; }
        if (RollMode) SetRoll(false);
        if (RundownActive && ItemStarted) MarkItemEnded(CurrentItem);
        var from = Settings.RundownName;
        _store.Update(s =>
        {
            s.OtherRundowns.RemoveAll(r => string.Equals(r.Name, target.Name, StringComparison.OrdinalIgnoreCase));
            s.OtherRundowns.Insert(0, new NamedRundown { Name = s.RundownName, Rundown = s.Rundown });
            s.Rundown = target.Rundown;
            s.RundownName = target.Name;
        });
        Settings.Rundown.EnsureIds();
        _actuals.Clear();
        _actualIds.Clear();
        PendingDelay = TimeSpan.Zero;
        CurrentItem = -1;
        RundownActive = false;
        ItemStarted = false;
        ShowAnchorUtc = null;
        PinnedStartUtc = null;
        LoopPass = 0;
        _journal.Add(Now, JournalKind.Note, -1, $"Running order switched: {from} → {target.Name}");
        _engine.SetMode(TimerMode.Countdown);
        _engine.SetDuration(TimeSpan.Zero);
        ActiveTitle = "";
        RundownEdited();
        _host.RundownChanged();
        _host.ControlsChanged();
        if (Settings.Rundown.Items.Count > 0)
        {
            GoTo(0, start: false);
            _host.Status($"Running order \"{target.Name}\" is live — item 1 loaded, Start when ready");
        }
        else _host.Status($"Running order \"{target.Name}\" is live — it has no items yet");
        _host.Changed(true);
        return true;
    }

    /// <summary>A running order was renamed (its undo history goes with it) or deleted (its history goes too).</summary>
    public void RundownRenamed(string oldName, string newName)
    {
        if (_editsByOrder.Remove(oldName, out var e)) _editsByOrder[newName] = e;
    }

    public void RundownDeleted(string name) => _editsByOrder.Remove(name);

    // ------------------------------------------------------------------ the schedule's anchors

    /// <summary>"Starts HH:MM" as the occurrence nearest to now (19:00 seen at 00:30 = last night), in UTC.</summary>
    public DateTime? ResolveScheduledStartUtc() => ShowClock.TryResolve(Settings.Rundown.ScheduledStart, Now, out var utc) ? utc : null;

    /// <summary>No planned start but a planned end: the plan is back-timed so it ends exactly then.</summary>
    public DateTime? BackTimedStartUtc()
        => ShowClock.TryResolve(Settings.Rundown.EndOfShow, Now, out var end) ? ScheduleEngine.BackTimedAnchor(Settings.Rundown.Items, end, ShowClock) : null;

    /// <summary>Planned start of item 1, in UTC.</summary>
    public DateTime? ScheduleAnchor() => PinnedStartUtc ?? ResolveScheduledStartUtc() ?? BackTimedStartUtc() ?? ShowAnchorUtc;

    public bool BackTimed => PinnedStartUtc is null && ResolveScheduledStartUtc() is null && BackTimedStartUtc() is not null;

    /// <summary>Set (or clear) the planned start. False with a status line when the text isn't a time.</summary>
    public bool SetScheduledStart(string text)
    {
        text = text.Trim();
        if (text.Length > 0 && !ShowClock.IsValidTimeText(text)) { _host.Refused("Bad start time. Try HH:MM (or yyyy-MM-dd HH:MM)"); return false; }
        _store.Update(s => s.Rundown.ScheduledStart = text.Length == 0 ? null : text);
        PinnedStartUtc = null;
        if (ItemStarted) PinnedStartUtc = ResolveScheduledStartUtc();
        _host.Status(text.Length == 0 ? "Show start cleared — plan starts when the first item runs" : $"Show planned to start at {text}");
        _host.RundownChanged();
        _host.Changed(true);
        return true;
    }

    /// <summary>Set (or clear) when the show must end.</summary>
    public bool SetEndOfShow(string text)
    {
        text = text.Trim();
        if (text.Length > 0 && !ShowClock.IsValidTimeText(text)) { _host.Refused("Bad end time. Try HH:MM (or yyyy-MM-dd HH:MM)"); return false; }
        _store.Update(s => s.Rundown.EndOfShow = text.Length == 0 ? null : text);
        _host.Status(text.Length == 0 ? "Show end cleared" : BackTimed ? $"Show ends at {text} — planned backwards from it" : $"Show ends at {text} — over/under and curfew aim at it");
        _host.RundownChanged();
        _host.Changed(true);
        return true;
    }

    public void SetLoop(int loops)
    {
        _store.Update(s => s.Rundown.Loop = loops);
        _host.Status(loops == 0 ? "Loop off" : loops < 0 ? "The running order loops until stopped" : $"The running order plays {loops + 1} times");
    }

    // ------------------------------------------------------------------ live edits

    /// <summary>Lengthen or shorten item <paramref name="index"/> by <paramref name="deltaSeconds"/> (the live item's clock moves with it).</summary>
    public void Trim(int index, int deltaSeconds)
    {
        var items = Settings.Rundown.Items;
        if (index < 0 || index >= items.Count) { _host.Refused($"No item {index + 1} to trim"); return; }
        if (deltaSeconds == 0) return;
        if (!_host.Allow(ShowFeature.Recovery)) return;
        var it = items[index];
        var newAdjust = it.AdjustSeconds + deltaSeconds;
        if (it.DurationSeconds + newAdjust < 0) newAdjust = -it.DurationSeconds;
        var applied = newAdjust - it.AdjustSeconds;
        _store.Update(_ => it.AdjustSeconds = newAdjust);
        RecordEdit($"{(applied < 0 ? "Cut" : "Extend")} \"{it.Title}\" {TimerTick.FormatSeconds(Math.Abs(applied))}",
            new ShowCommand(ShowCommandKind.Trim, Number: index + 1, Text: (-applied).ToString(CultureInfo.InvariantCulture)),
            new ShowCommand(ShowCommandKind.Trim, Number: index + 1, Text: applied.ToString(CultureInfo.InvariantCulture)),
            targets: ItemTarget(index) is { } t ? new[] { t } : null);
        if (index == CurrentItem && RundownActive && applied != 0) _engine.Nudge(TimeSpan.FromSeconds(applied));
        _journal.Add(Now, JournalKind.Trim, index, it.Title, applied);
        _host.Status($"{(applied < 0 ? "Cut" : "Extended")} \"{it.Title}\" by {TimerTick.FormatSeconds(Math.Abs(applied))}");
        _host.RundownChanged();
        _host.ConfigChanged();
    }

    /// <summary>Announce a delay before the next item (0 clears it).</summary>
    public void SetDelay(double seconds)
    {
        if (Math.Abs(PendingDelay.TotalSeconds - seconds) > 0.5)
            RecordEdit(seconds > 0 ? $"Delay {TimerTick.FormatSeconds((long)seconds)}" : "Clear the delay",
                new ShowCommand(ShowCommandKind.Delay, Number: PendingDelay.TotalSeconds), new ShowCommand(ShowCommandKind.Delay, Number: seconds), targets: LiveTarget());
        PendingDelay = TimeSpan.FromSeconds(seconds);
        _journal.Add(Now, JournalKind.Delay, RundownActive ? CurrentItem : -1, "", seconds);
        _host.Status(seconds > 0 ? $"Delay of {TimerTick.FormatSeconds((long)seconds)} before the next item" : "Delay cleared");
    }

    /// <summary>A department's note on item <paramref name="item"/> (1-based). Empty clears it.</summary>
    public void SetDeptNote(int item, string dept, string note)
    {
        var index = item - 1;
        if (index < 0 || index >= Settings.Rundown.Items.Count) { _host.Refused($"No item {item} for a {dept} note"); return; }
        var before = Settings.Rundown.Items[index].DeptNote(dept);
        if (before == note.Trim()) return;
        RecordEdit($"{dept} note on item {item}",
            ShowCommand.DeptNote(item, dept, before),
            ShowCommand.DeptNote(item, dept, note),
            targets: ItemTarget(index) is { } t ? new[] { t } : null);
        _store.Update(s => s.Rundown.Items[index].SetDeptNote(dept, note));
        _journal.Add(Now, JournalKind.Note, index, $"{dept} note: {(note.Length > 0 ? note : "(cleared)")}");
        _host.DeptNotesChanged();
        _host.Status($"{dept} updated their note on item {item}");
        _host.Changed(true);
    }

    /// <summary>Apply trims (item, ±seconds) as one undoable step, e.g. a recovery.</summary>
    public void ApplyTrims(IEnumerable<(int Item, int Delta)> trims, string label)
    {
        var list = trims.ToList();
        if (list.Count == 0) return;
        BeginBatch(label);
        try { foreach (var (item, delta) in list) Trim(item, delta); }
        finally { EndBatch(); }
        _journal.Add(Now, JournalKind.Note, RundownActive ? CurrentItem : -1, label);
        _host.Status(label + " — Undo (Ctrl+Z) puts it back");
        _host.Changed(true);
    }

    // ------------------------------------------------------------------ undo

    /// <summary>The live running order's history (or the one a batch began in, until it ends).</summary>
    private LiveEdits Edits
    {
        get
        {
            if (_batchEdits is { InBatch: true } b) return b;
            var key = Settings.RundownName ?? "";
            if (!_editsByOrder.TryGetValue(key, out var e)) _editsByOrder[key] = e = new LiveEdits();
            return e;
        }
    }

    /// <summary>Group the edits made until <see cref="EndBatch"/> into one undo step (a recovery, an approved plan).</summary>
    public void BeginBatch(string label) { _batchEdits = Edits; _batchEdits.BeginBatch(label); }

    public void EndBatch() { var b = _batchEdits; _batchEdits = null; b?.EndBatch(); RefreshEdits(); }

    public void Undo()
    {
        var dropped = Edits.DropInvalid(TargetStillValid);
        if (Edits.TakeUndo() is not { } e)
        {
            _host.Refused(dropped > 0 ? "Nothing to undo — the items those edits changed have moved or gone" : "Nothing to undo");
            RefreshEdits();
            return;
        }
        Replay(e.Undo);
        _journal.Add(Now, JournalKind.Note, RundownActive ? CurrentItem : -1, "Undid: " + e.Label);
        _host.Status("Undid: " + e.Label);
        RefreshEdits();
    }

    public void Redo()
    {
        var dropped = Edits.DropInvalid(TargetStillValid);
        if (Edits.TakeRedo() is not { } e)
        {
            _host.Refused(dropped > 0 ? "Nothing to redo — the items those edits changed have moved or gone" : "Nothing to redo");
            RefreshEdits();
            return;
        }
        Replay(e.Redo);
        _journal.Add(Now, JournalKind.Note, RundownActive ? CurrentItem : -1, "Redid: " + e.Label);
        _host.Status("Redid: " + e.Label);
        RefreshEdits();
    }

    /// <summary>Drop edits whose targets are gone and tell the host the labels may have changed.</summary>
    public void RefreshEdits()
    {
        Edits.DropInvalid(TargetStillValid);
        _host.ControlsChanged();
    }

    private void Replay(IReadOnlyList<ShowCommand> commands)
    {
        _replaying = true;
        try
        {
            foreach (var c in commands)
                switch (c.Kind)
                {
                    case ShowCommandKind.Trim: Trim((int)c.Number - 1, int.Parse(c.Text ?? "0", CultureInfo.InvariantCulture)); break;
                    case ShowCommandKind.Delay: SetDelay(c.Number); break;
                    case ShowCommandKind.Nudge: Nudge(TimeSpan.FromSeconds(c.Number)); break;
                    case ShowCommandKind.DeptNote:
                    {
                        var note = c.Get<DeptNoteArgs>();
                        SetDeptNote((int)c.Number, note.Department, note.Note);
                        break;
                    }
                }
        }
        finally { _replaying = false; }
        _host.Changed(true);
    }

    private void RecordEdit(string label, ShowCommand undo, ShowCommand redo, string? mergeKey = null, IReadOnlyList<LiveEdits.EditTarget>? targets = null)
    {
        if (_replaying) return;
        Edits.Record(label, undo, redo, mergeKey, mergeKey == "nudge" ? NudgeLabel : null, targets: targets);
        RefreshEdits();
    }

    private static string NudgeLabel(IReadOnlyList<ShowCommand> redo)
    {
        var total = (long)redo.Sum(c => c.Number);
        return $"Nudge {(total >= 0 ? "+" : "−")}{TimerTick.FormatSeconds(Math.Abs(total))}";
    }

    private LiveEdits.EditTarget? ItemTarget(int index, bool mustBeLive = false)
        => index >= 0 && index < Settings.Rundown.Items.Count ? new LiveEdits.EditTarget(index, Settings.Rundown.Items[index].Id, mustBeLive) : null;

    private LiveEdits.EditTarget[] LiveTarget()
        => RundownActive && ItemTarget(CurrentItem, mustBeLive: true) is { } t ? new[] { t } : Array.Empty<LiveEdits.EditTarget>();

    private bool TargetStillValid(LiveEdits.EditTarget t)
        => t.Index >= 0 && t.Index < Settings.Rundown.Items.Count && Settings.Rundown.Items[t.Index].Id == t.ItemId
           && (!t.MustBeLive || (RundownActive && CurrentItem == t.Index));

    // ------------------------------------------------------------------ Roll

    /// <summary>Roll on: the running order follows its planned times until the operator does anything else.</summary>
    public void SetRoll(bool on)
    {
        if (on == RollMode) return;
        if (on)
        {
            if (Settings.Rundown.Items.Count == 0) { _host.Refused("Roll needs a running order"); return; }
            if (ScheduleAnchor() is null) { _host.Refused("Roll follows the planned times: set Starts on the Running order tab first"); return; }
            if (!_host.Allow(ShowFeature.Roll)) return;
        }
        RollMode = on;
        _rollItem = -1;
        _rollWaiting = false;
        _rollEndUtc = null;
        _journal.Add(Now, JournalKind.Note, RundownActive ? CurrentItem : -1, on ? "Roll on" : "Roll off");
        _host.Status(on ? "Roll on — the running order follows the clock" : "Roll off");
        if (on) UpdateRoll();
        else RollLine = "";
        _host.ControlsChanged();
    }

    private void UpdateRoll()
    {
        if (!RollMode) return;
        var items = Settings.Rundown.Items;
        if (items.Count == 0 || ScheduleAnchor() is not { } anchor) { SetRoll(false); return; }

        // Did a person take over since Roll's last move? Then step aside.
        if (_rollItem >= 0 && !_rollApplying)
        {
            var moved = !RundownActive || CurrentItem != _rollItem;
            var stopped = !_engine.IsCounting && _engine.State != TimerState.Expired;
            // Retimed: the operator nudged, trimmed or set an end time, so the clock no longer lands where Roll put it.
            var retimed = _rollEndUtc is { } end && _engine.IsCounting && _engine.State != TimerState.Expired
                          && Math.Abs(((end - Now) - _engine.Current).TotalSeconds) > 2;
            if (moved || stopped || retimed)
            {
                SetRoll(false);
                _host.Status("Roll off — you took over");
                return;
            }
        }

        var now = Now;
        var plan = ScheduleEngine.Plan(items, anchor, ShowClock);
        if (Settings.Rundown.Loop != 0 && plan.Length > 0)
        {
            var cycle = plan[^1].End - plan[0].Start;
            var passAnchor = Roll.LoopAnchor(anchor, cycle, Settings.Rundown.Loop, now);
            if (passAnchor != anchor) plan = ScheduleEngine.Plan(items, passAnchor, ShowClock);
        }
        var d = Roll.Decide(plan, now);
        var line = RollLine;
        _rollApplying = true;
        try
        {
            switch (d.What)
            {
                case Roll.Kind.Over:
                    line = "ROLL · the running order is over";
                    break;

                case Roll.Kind.Wait:
                    if (_rollItem != d.Item || !_rollWaiting || Math.Abs((_rollStartUtc - plan[d.Item].Start).TotalSeconds) > 1)
                    {
                        GoTo(d.Item, start: false);
                        ActiveTitle = "Next: " + items[d.Item].DisplayTitle;
                        _engine.SetDuration(d.Remaining);
                        _engine.Start();                       // counts to the start — the item itself isn't marked started
                        _rollItem = d.Item;
                        _rollWaiting = true;
                        _rollStartUtc = plan[d.Item].Start;
                        _rollEndUtc = now + d.Remaining;
                        _host.Changed(true);
                    }
                    line = $"ROLL · {items[d.Item].Title} starts in {TimerTick.FormatSeconds((long)Math.Ceiling(d.Remaining.TotalSeconds))}";
                    break;

                case Roll.Kind.Run:
                    // A new pass through the same item (a one-item loop) has a new planned start: reload it.
                    if (_rollItem != d.Item || _rollWaiting || !ItemStarted || Math.Abs((_rollStartUtc - plan[d.Item].Start).TotalSeconds) > 1)
                    {
                        GoTo(d.Item, start: false);
                        _engine.SetDuration(d.Remaining);
                        Start();
                        _rollItem = d.Item;
                        _rollWaiting = false;
                        _rollStartUtc = plan[d.Item].Start;
                        _rollEndUtc = now + d.Remaining;
                        _host.Changed(true);
                    }
                    line = $"ROLL · {items[d.Item].Title} until {Clock.Time(ShowClock.ToShow(now + d.Remaining), Settings.Display.Use12HourClock, seconds: false)}";
                    break;
            }
        }
        finally { _rollApplying = false; }
        if (line != RollLine) { RollLine = line; _host.ControlsChanged(); }
    }

    // ------------------------------------------------------------------ bookkeeping

    private void MarkItemStarted(int index)
    {
        var now = Now;
        var first = _actuals.Count == 0;
        _actuals[index] = new ItemActual(now, null);
        var item = Settings.Rundown.Items[index];
        _actualIds[index] = item.Id;
        PendingDelay = TimeSpan.Zero;
        _journal.Add(now, JournalKind.ItemStart, index, item.Title);
        _host.ItemStarted(index, item, first);
    }

    private void MarkItemEnded(int index)
    {
        if (index < 0 || !_actuals.TryGetValue(index, out var a) || a.StartUtc is not { } start || a.EndUtc is not null) return;
        var now = Now;
        _actuals[index] = a with { EndUtc = now };
        var items = Settings.Rundown.Items;
        if (index >= items.Count) return;
        var it = items[index];
        _journal.Add(now, JournalKind.ItemEnd, index, it.Title, (now - start).TotalSeconds);
        _host.ItemEnded(index, it, now - start);
    }

    /// <summary>Who a journal entry about item <paramref name="index"/> of the live running order is about.</summary>
    private JournalIdentity IdentityOf(int index)
    {
        var r = Settings.Rundown;
        if (index < 0 || index >= r.Items.Count) return default;
        if (r.Id.Length == 0) r.EnsureIds();
        var live = RundownActive && index == CurrentItem && Execution.Length > 0;
        return new JournalIdentity(r.Id, r.Items[index].Id, live ? Execution : null);
    }

    // ------------------------------------------------------------------ session checkpoints

    public ShowCursor Capture() => new(ActiveTitle, CurrentItem, RundownActive, ItemStarted, ShowAnchorUtc, PinnedStartUtc,
        new Dictionary<int, ItemActual>(_actuals), PendingDelay.TotalSeconds, Settings.Rundown.Id, Execution,
        CurrentItem >= 0 ? _currentItemId : null, new Dictionary<int, string>(_actualIds));

    /// <summary>
    /// Bring the cursor back after a crash. Only inside the same running order (by id) — restoring Day 1's position into
    /// Day 2 would point at the wrong item — and then by item identity: an item inserted, deleted or moved since the
    /// checkpoint can't make the restore select a different item or credit its times to another. An item that has gone
    /// is not live after the restore, and its times are dropped.
    /// </summary>
    public void Restore(ShowCursor c)
    {
        var items = Settings.Rundown.Items;
        var sameOrder = c.RundownId is null || c.RundownId == Settings.Rundown.Id;
        int IndexOf(string id) => items.FindIndex(i => i.Id == id);
        CurrentItem = !sameOrder ? -1
            : c.CurrentItemId is { Length: > 0 } cid ? IndexOf(cid)
            : Math.Min(c.CurrentItem, items.Count - 1);   // a checkpoint from before identities
        RundownActive = sameOrder && c.RundownActive && CurrentItem >= 0;
        ItemStarted = RundownActive && c.ItemStarted;
        ActiveTitle = !c.RundownActive || RundownActive ? c.Title : "";
        ShowAnchorUtc = sameOrder ? c.ShowAnchorUtc : null;
        PinnedStartUtc = sameOrder ? c.PinnedStartUtc : null;
        _actuals.Clear();
        _actualIds.Clear();
        if (sameOrder)
            foreach (var (k, v) in c.Actuals)
            {
                var at = c.ActualIds is not null ? (c.ActualIds.TryGetValue(k, out var aid) ? IndexOf(aid) : -1) : k;
                if (at >= 0 && at < items.Count) { _actuals[at] = v; _actualIds[at] = items[at].Id; }
            }
        _currentItemId = CurrentItem >= 0 ? Settings.Rundown.Items[CurrentItem].Id : null;
        PendingDelay = TimeSpan.FromSeconds(Math.Max(0, c.PendingDelaySeconds));
        Execution = sameOrder && c.Execution is { Length: > 0 } ex ? ex : "";
        _host.RundownChanged();
    }
}
