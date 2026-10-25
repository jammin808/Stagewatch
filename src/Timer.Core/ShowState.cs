using System.Globalization;

namespace Stagewatch.Core;

/// <summary>
/// One immutable frame of everything an output shows. Built once per visible change by
/// the app (the single writer) and read by every consumer — WPF output, browser viewers,
/// OSC feedback — so they can never disagree. Value equality is the "did anything
/// visible change" test, which keeps network traffic to roughly one frame per second.
/// </summary>
/// <summary>
/// A frame's place in the stream (Show Bus <c>seq</c>). It compares equal to every other stamp, so a frame's equality —
/// "did anything visible change?" — never depends on its number.
/// </summary>
public readonly struct FrameSeq : IEquatable<FrameSeq>
{
    public FrameSeq(long value) => Value = value;
    public long Value { get; }
    public bool Equals(FrameSeq other) => true;
    public override bool Equals(object? obj) => obj is FrameSeq;
    public override int GetHashCode() => 0;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record ShowSnapshot
{
    /// <summary>Show Bus frame format version.</summary>
    public const int FormatVersion = 1;

    /// <summary>Stamped by <see cref="SnapshotHub.Publish"/>: increases with every published frame.</summary>
    public FrameSeq Seq { get; init; }

    public TimerMode Mode { get; init; }
    public TimerState State { get; init; }
    /// <summary>Whole seconds on the display (negative in overtime).</summary>
    public long Seconds { get; init; }
    public string Text { get; init; } = "00:00";
    public long TotalSeconds { get; init; }
    public TimerPhase Phase { get; init; }
    public string ColorHex { get; init; } = "#FFFFFF";

    public string Title { get; init; } = "";
    public string NextTitle { get; init; } = "";
    public int ItemIndex { get; init; } = -1;
    public int ItemCount { get; init; }
    /// <summary>Loaded preset slot, or 0 when the timer wasn't loaded from a preset.</summary>
    public int PresetSlot { get; init; }

    public string EndTimeText { get; init; } = "";
    public string TimeOfDay { get; init; } = "";
    /// <summary>Schedule offset in seconds (positive = late). Null when there is no plan to compare to.</summary>
    public long? OffsetSeconds { get; init; }
    public string ScheduleText { get; init; } = "";

    public string Message { get; init; } = "";
    public MessageUrgency Urgency { get; init; }
    public FlashRate Flash { get; init; }
    public bool FlashTimer { get; init; }

    public bool Blackout { get; init; }
    /// <summary>The output window's mode (Closed when no output is open).</summary>
    public OutputMode Output { get; init; }
    /// <summary>NDI is sending.</summary>
    public bool Ndi { get; init; }
    /// <summary>The holding image on air ("" = none). Screens fetch it from /api/holding/&lt;id&gt;.</summary>
    public string HoldingId { get; init; } = "";
    public string HoldingName { get; init; } = "";
    /// <summary>Fill the screen (crop) rather than fit (black bars).</summary>
    public bool HoldingFill { get; init; }
    /// <summary>LTC in: the incoming timecode ("" = none), whether it is rolling, and whether the timer follows it.</summary>
    public string LtcText { get; init; } = "";
    public bool LtcRunning { get; init; }
    public bool LtcChase { get; init; }
    /// <summary>Roll mode is on: the running order follows the clock.</summary>
    public bool Roll { get; init; }
    /// <summary>The live running order's name, and the show's other running orders.</summary>
    public string RundownName { get; init; } = "";
    public EquatableArray<string> RundownNames { get; init; } = EquatableArray<string>.Empty;
    /// <summary>Auxiliary timers, and the one on stage as a ready-made line ("VIDEO  02:13"; "" = none).</summary>
    public EquatableArray<AuxView> Aux { get; init; } = EquatableArray<AuxView>.Empty;
    public string AuxLine { get; init; } = "";
    /// <summary>Non-empty in Free mode: every output must show it.</summary>
    public string Watermark { get; init; } = "";
    public DisplayElements Elements { get; init; } = DisplayElements.Timer | DisplayElements.Message;

    /// <summary>Bumped when presets / rundown / message bank change so remotes refetch them.</summary>
    public int ConfigVersion { get; init; }

    // ---------------------------------------------------------------- speaker face (2.0)

    /// <summary>The timer as the speaker sees it (calm / progress-only / honest formats). Empty = progress only.</summary>
    public string SpeakerText { get; init; } = "00:00";
    /// <summary>The cue-ladder label in force ("5 MIN", "WRAP UP") — the non-colour cue.</summary>
    public string CueLabel { get; init; } = "";
    /// <summary>"FINISH BY 10:49".</summary>
    public string FinishBy { get; init; } = "";
    /// <summary>A briefing preview is playing (10× speed run through the cue ladder).</summary>
    public bool Preview { get; init; }
    /// <summary>Elastic end-at is stretching time right now (operator badge; speaker views may hide it).</summary>
    public bool Stretching { get; init; }
    /// <summary>Speaker outputs reserve space for the message so nothing jumps when one appears.</summary>
    public bool StableLayout { get; init; }
    /// <summary>Holographic 3D style on displays that support it.</summary>
    public bool Holographic { get; init; }
    public bool HoloFloor { get; init; } = true;
    public bool EggTimer { get; init; }
    public bool Animations3D { get; init; } = true;
    public bool Particles3D { get; init; } = true;

    // ---------------------------------------------------------------- acknowledged cues

    /// <summary>Id of the signal behind the current message (0 = no acknowledgement asked).</summary>
    public int MessageSignalId { get; init; }
    /// <summary>"" (no ack asked) | "sent" | "seen" | "done".</summary>
    public string MessageAck { get; init; } = "";
    public string MessageAckBy { get; init; } = "";
    public EquatableArray<SignalView> Signals { get; init; } = EquatableArray<SignalView>.Empty;

    // ---------------------------------------------------------------- presentation awareness

    public SlideView? Slides { get; init; }
    public SpeechView? Speech { get; init; }
    public PlayoutView? Playout { get; init; }

    // ---------------------------------------------------------------- producer (detail frames)

    public string Room { get; init; } = "";
    public EquatableArray<ItemTimingView> Timings { get; init; } = EquatableArray<ItemTimingView>.Empty;
    /// <summary>Groups of items (sessions, acts) with their targets and timing.</summary>
    public EquatableArray<GroupView> Groups { get; init; } = EquatableArray<GroupView>.Empty;
    /// <summary>The show's label colours as "VT=#3B82F6;GFX=#F59E0B" (labels in use only).</summary>
    public string LabelColors { get; init; } = "";
    /// <summary>Seconds since midnight in the show's time zone (for studio clocks; screens count on locally between frames).</summary>
    public long ClockSeconds { get; init; } = -1;
    /// <summary>Seconds since 1970 (UTC), for screens that show another time zone (counted on locally between frames).</summary>
    public long UtcSeconds { get; init; }
    /// <summary>The show's date, e.g. "Friday 9 October".</summary>
    public string DateText { get; init; } = "";
    /// <summary>A chime just played (screens with ?sound=1 play it too): its sequence number and what it was.</summary>
    public int ChimeSeq { get; init; }
    public string Chime { get; init; } = "";

    /// <summary>Goes up whenever a department note changes, so crew pages refetch theirs.</summary>
    public int NotesVersion { get; init; }
    public string HardOutText { get; init; } = "";
    public long? HardOutMarginSeconds { get; init; }
    public string ForecastText { get; init; } = "";
    public long PendingDelaySeconds { get; init; }
    public EquatableArray<Advice> Advice { get; init; } = EquatableArray<Advice>.Empty;
    public EquatableArray<RoomView> Rooms { get; init; } = EquatableArray<RoomView>.Empty;
    public int? ClockOffsetMs { get; init; }

    // ---------------------------------------------------------------- Q&A

    public string QaLive { get; init; } = "";
    public int QaPending { get; init; }
    public int QaQueued { get; init; }
    public int QaFit { get; init; }
    public EquatableArray<QuestionView> Questions { get; init; } = EquatableArray<QuestionView>.Empty;
}

/// <summary>Where the presenter is in their deck (PowerPoint), plus the rehearsal ghost.</summary>
public readonly record struct SlideView(int Slide, int Count, string NextTitle, string Notes, long GhostDeltaSeconds, long ProjectedSeconds, PaceState Pace, bool Rehearsing);

/// <summary>Live speech-to-script tracking (operator-facing).</summary>
public readonly record struct SpeechView(double Percent, double WordsPerMinute, long? ProjectedRemainingSeconds, bool Listening);

/// <summary>A video playing out of vMix (or another playout source).</summary>
public readonly record struct PlayoutView(string Title, long RemainingSeconds, long DurationSeconds, bool Running, string Source);

/// <summary>
/// One running-order line for producer views. Times are pre-formatted in the show's time zone. StartIn / EndIn are
/// seconds from this frame to the item's expected (or actual) start and end — relative, so a screen counts on from
/// when the frame arrived and never depends on its own clock being right.
/// </summary>
public readonly record struct ItemTimingView(
    int Index, string Title, string Speaker, SegmentKind Kind, int Seconds, int AdjustSeconds,
    string Planned, string Expected, string ExpectedEnd, string Actual, string ActualEnd,
    ItemStatus Status, long DelaySeconds, bool Conflict, bool Fixed, string HardOut,
    string Group = "", long? StartIn = null, long? EndIn = null, long? PlannedIn = null, string Labels = "");

/// <summary>A group of consecutive items (an act, a session) with its planned length, optional target and timing.</summary>
public readonly record struct GroupView(
    string Name, int First, int Last, long PlannedSeconds, long? TargetSeconds, long? StartIn, long? EndIn,
    string Planned, string ExpectedEnd, ItemStatus Status);

/// <summary>Another Stagewatch on the network (multi-room dashboard).</summary>
public readonly record struct RoomView(
    string Room, string Host, int WebPort, string Title, string Text, string State, string Phase,
    long? OffsetSeconds, string HardOut, long? HardOutMarginSeconds, int OpenSignals, bool Stale, bool Self);

/// <summary>
/// Latest-wins snapshot mailbox. Publish is called on the owner thread; subscribers must
/// return quickly (signal a worker, never block). A throwing subscriber is isolated — it
/// can't take down the timer or starve the others.
/// </summary>
public sealed class SnapshotHub
{
    private ShowSnapshot _latest = new();
    private readonly object _subLock = new();

    public ShowSnapshot Latest => Volatile.Read(ref _latest);

    private readonly Func<long> _clockMs;
    private long _aliveAt = long.MinValue;          // MinValue = the producer has never reported (liveness not in use)

    public SnapshotHub(Func<long>? clockMs = null) => _clockMs = clockMs ?? (() => Environment.TickCount64);

    /// <summary>
    /// The producer (the show thread) says it is still turning over, whether or not anything changed. A paused or
    /// idle show publishes nothing for minutes, so frames alone can't tell "quiet" from "frozen"; this can.
    /// The first call switches the check on.
    /// </summary>
    public void MarkAlive() => Volatile.Write(ref _aliveAt, _clockMs());

    /// <summary>After this long without a sign of life the producer counts as stuck: screens say so instead of showing the last frame.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(3);

    /// <summary>The producer reported before but has been silent longer than <see cref="StuckAfter"/>.</summary>
    public bool ProducerStuck
    {
        get
        {
            var at = Volatile.Read(ref _aliveAt);
            return at != long.MinValue && _clockMs() - at > (long)StuckAfter.TotalMilliseconds;
        }
    }

    /// <summary>Reports a subscriber failure (for logging). Never throws back into Publish.</summary>
    public event Action<Exception>? SubscriberFailed;

    /// <summary>A subscriber took longer than <see cref="SlowAfter"/> to handle one frame (diagnostics; the show didn't wait).</summary>
    public event Action<string, TimeSpan>? SubscriberSlow;

    public static readonly TimeSpan SlowAfter = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// One subscriber's mailbox: a single slot holding the newest frame, drained by at most one worker at a time. The
    /// publisher only swaps a reference and, if the subscriber is idle, queues one work item — it never runs subscriber
    /// code, so a slow consumer (a stalled socket, a busy encoder) can't delay the show loop or the other consumers.
    /// A consumer that falls behind skips straight to the newest frame (latest-wins); each consumer sees frames in order.
    /// </summary>
    private sealed class Mailbox(Action<ShowSnapshot> handler, string name)
    {
        public readonly Action<ShowSnapshot> Handler = handler;
        public readonly string Name = name;
        public ShowSnapshot? Pending;
        public int Scheduled;
        public int Busy;
    }

    private Mailbox[] _mailboxes = Array.Empty<Mailbox>();

    /// <summary>Subscribe to published frames. Handlers run on a worker, never on the publishing (show) thread.</summary>
    public void Subscribe(Action<ShowSnapshot> onPublished, string? name = null)
    {
        lock (_subLock)
        {
            _mailboxes = _mailboxes.Append(new Mailbox(onPublished, name ?? onPublished.Method.DeclaringType?.Name ?? "subscriber")).ToArray();
        }
    }

    public void Unsubscribe(Action<ShowSnapshot> onPublished)
    {
        lock (_subLock)
        {
            _mailboxes = _mailboxes.Where(m => m.Handler != onPublished).ToArray();
        }
    }

    /// <returns>True if the snapshot differed from the previous one and was published.</returns>
    public bool Publish(ShowSnapshot snapshot)
    {
        var latest = Volatile.Read(ref _latest);
        if (snapshot.Equals(latest)) return false;
        snapshot = snapshot with { Seq = new FrameSeq(latest.Seq.Value + 1) };   // one owner thread publishes: no race on the count
        Volatile.Write(ref _latest, snapshot);
        foreach (var m in Volatile.Read(ref _mailboxes))
        {
            Volatile.Write(ref m.Pending, snapshot);
            if (Interlocked.Exchange(ref m.Scheduled, 1) == 0)
                ThreadPool.UnsafeQueueUserWorkItem(Drain, m, preferLocal: false);
        }
        return true;
    }

    private void Drain(Mailbox m)
    {
        Interlocked.Exchange(ref m.Busy, 1);
        try
        {
            while (true)
            {
                var frame = Interlocked.Exchange(ref m.Pending, null);
                if (frame is null)
                {
                    Volatile.Write(ref m.Scheduled, 0);
                    // A frame may have landed after the read above and before Scheduled was cleared: take it now.
                    if (Volatile.Read(ref m.Pending) is null || Interlocked.Exchange(ref m.Scheduled, 1) != 0) return;
                    continue;
                }
                var started = Environment.TickCount64;
                try { m.Handler(frame); }
                catch (Exception ex) { try { SubscriberFailed?.Invoke(ex); } catch { } }
                var took = Environment.TickCount64 - started;
                if (took > (long)SlowAfter.TotalMilliseconds) { try { SubscriberSlow?.Invoke(m.Name, TimeSpan.FromMilliseconds(took)); } catch { } }
            }
        }
        finally { Interlocked.Exchange(ref m.Busy, 0); }
    }

    /// <summary>Wait until every subscriber has handled the newest frame (tests, orderly shutdown). False on timeout.</summary>
    public bool WaitIdle(TimeSpan timeout)
    {
        return SpinWait.SpinUntil(() => Volatile.Read(ref _mailboxes).All(m => Volatile.Read(ref m.Scheduled) == 0 && Volatile.Read(ref m.Busy) == 0), timeout);
    }
}

public enum ShowCommandKind
{
    Start, Pause, Toggle, Reset,
    Next, Previous, GoTo,
    Preset, Nudge, SetDuration, EndAt,
    Message, ClearMessage,
    Blackout, Mode,
    // ---- 2.0 ----
    /// <summary>Number = SignalKind, Text = target + "\u0001" + text, Flag = ack required.</summary>
    Signal,
    /// <summary>Number = signal id, Text = "by|stage".</summary>
    Ack,
    /// <summary>Number = signal id, or 0 with Text = target ("*" = all).</summary>
    SignalClear,
    /// <summary>Number = signal id to send again.</summary>
    SignalResend,
    /// <summary>Number = item (1-based), Text = signed seconds (invariant).</summary>
    Trim,
    /// <summary>Number = seconds of announced delay before the next item (0 clears).</summary>
    Delay,
    /// <summary>Re-time the live item (elastic) so it lands on its planned end — "reach schedule".</summary>
    CatchUp,
    /// <summary>Number = 1 start / 0 stop / -1 toggle the 10× briefing preview.</summary>
    Preview,
    /// <summary>Text = cue ladder id ("" = default).</summary>
    Ladder,
    /// <summary>Number = SpeakerTimerFormat.</summary>
    Format,
    /// <summary>Text = question + "\u0001" + name.</summary>
    Question,
    /// <summary>Number = question id (0 for next/clear), Text = action.</summary>
    QuestionSet,
    /// <summary>Number = 1 / 0 / -1: rehearsal capture of slide timings.</summary>
    Rehearse,
    /// <summary>Number = 1 / 0 / -1: speech-to-script tracking.</summary>
    Listen,
    /// <summary>Text = "html" | "csv": write the show report.</summary>
    Report,
    /// <summary>Text = a show-caller note for the journal.</summary>
    Note,
    // ---- 2.2 ----
    /// <summary>Number = (int)OutputMode, or -1 = toggle full screen / closed.</summary>
    Output,
    /// <summary>Number = 1 on / 0 off / -1 toggle NDI.</summary>
    Ndi,
    /// <summary>Text = image number, id or name; "off" clears; "toggle" brings the last one back or clears.</summary>
    Holding,
    /// <summary>Number = 1 hologram / 2 egg timer / 0 standard / -1 toggle the 3D display.</summary>
    Style,
    /// <summary>LTC in. Text = "chase" (Number 1 on / 0 off / -1 toggle) or "end" (Text2 in Text after \u0001: timecode or "").</summary>
    Ltc,
    /// <summary>Browser screens. Text = action \u0001 argument \u0001 who ("all", a screen id or its name).</summary>
    Screen,
    /// <summary>Auxiliary timer. Number = slot (1..3), Text = action \u0001 argument.</summary>
    Aux,
    /// <summary>A department's note on an item. Number = item (1-based), Text = department \u0001 note ("" clears).</summary>
    DeptNote,
    /// <summary>Roll mode (follow the clock): Number 1 on / 0 off / -1 toggle.</summary>
    Roll,
    /// <summary>Make another of the show's running orders live. Text = its name.</summary>
    Rundown,
    /// <summary>Undo (Number 1) or redo (Number -1) the last live edit.</summary>
    Undo,
    /// <summary>The show's end time. Text = "HH:mm" (or dated), "" clears.</summary>
    EndOfShow,
    /// <summary>Loop the running order. Number = extra passes (0 off, -1 forever).</summary>
    Loop,
    /// <summary>The clip on air in Patterns: Text "end" (Number = seconds before its end) or "restart".</summary>
    Patterns,
    /// <summary>GO every department standing by, at once ("signal go all").</summary>
    SignalGoAll,
    /// <summary>Controlled browser windows (their own addresses). Text = action \u0001 who \u0001 argument.</summary>
    Window
}

/// <summary>
/// A transport-neutral operator command. For EndAt, Number carries the pace:
/// 0 = the operator's setting, 1 = elastic, 2 = real time. HTTP, WebSocket, OSC and Companion all parse
/// into this, and the app executes it on its own thread — one input path, one writer.
/// </summary>
public readonly record struct ShowCommand(
    ShowCommandKind Kind,
    double Number = 0,
    string? Text = null,
    bool Flag = false,
    MessageUrgency Urgency = MessageUrgency.Info,
    FlashRate Flash = FlashRate.Off,
    TimerMode Mode = TimerMode.Countdown)
{
    /// <summary>
    /// The arguments of a command that takes several (a signal's target and text, a department and its note...), typed.
    /// Set by the parser and the factories below; read through <see cref="Get{T}"/>. <see cref="Text"/> still carries
    /// the older packed form for compatibility, but nothing should split it by hand.
    /// </summary>
    public ShowArgs? Args { get; init; }

    /// <summary>This command's typed arguments (parsed from the packed text if it was built without them).</summary>
    public T Get<T>() where T : ShowArgs
        => Args as T ?? ShowArgs.From(this) as T ?? throw new InvalidOperationException($"{Kind} has no {typeof(T).Name}");

    public static ShowCommand Signal(SignalKind kind, string target, string text, bool ackRequired)
        => new ShowCommand(ShowCommandKind.Signal, Number: (int)kind, Text: ShowArgs.Pack(target, text), Flag: ackRequired) { Args = new SignalArgs(target, text) };

    public static ShowCommand DeptNote(int item, string department, string note)
        => new ShowCommand(ShowCommandKind.DeptNote, Number: item, Text: ShowArgs.Pack(department, note)) { Args = new DeptNoteArgs(department, note) };

    public static ShowCommand Question(string text, string name)
        => new ShowCommand(ShowCommandKind.Question, Text: ShowArgs.Pack(text, name)) { Args = new QuestionArgs(text, name) };
}

/// <summary>Typed arguments for the commands that take more than one value.</summary>
public abstract record ShowArgs
{
    internal const char Separator = '\u0001';

    /// <summary>The older packed text form (still on <see cref="ShowCommand.Text"/> for compatibility).</summary>
    public static string Pack(params string[] parts) => string.Join(Separator, parts.Select(p => (p ?? "").Replace(Separator, ' ')));

    /// <summary>The one place the packed form is read.</summary>
    public static ShowArgs? From(ShowCommand c)
    {
        var p = (c.Text ?? "").Split(Separator);
        var bar = (c.Text ?? "|seen").Split('|');   // acknowledgements use "who|state"
        string At(int i) => i < p.Length ? p[i] : "";
        return c.Kind switch
        {
            ShowCommandKind.Signal => new SignalArgs(At(0), At(1)),
            ShowCommandKind.Question => new QuestionArgs(At(0), At(1)),
            ShowCommandKind.Screen => new ScreenArgs(At(0), At(1), At(2).Length == 0 ? "all" : At(2)),
            ShowCommandKind.Aux => new AuxArgs(At(0), At(1)),
            ShowCommandKind.DeptNote => new DeptNoteArgs(At(0).Trim(), At(1)),
            ShowCommandKind.Ltc => new LtcArgs(At(0), At(1)),
            ShowCommandKind.Ack => new AckArgs(bar[0], bar.Length > 1 && bar[1].Length > 0 ? bar[1] : "seen"),
            ShowCommandKind.Window => new WindowArgs(At(0), At(1), At(2)),
            _ => null,
        };
    }
}

/// <summary>A cue to a target (a department, "speaker", "backstage") with its words.</summary>
public sealed record SignalArgs(string Target, string Text) : ShowArgs;
/// <summary>An audience question and who asked.</summary>
public sealed record QuestionArgs(string Text, string Name) : ShowArgs;
/// <summary>A screen action ("go", "identify", "name", "save", "reload", "disconnect"), its argument, and which screens.</summary>
public sealed record ScreenArgs(string Action, string Argument, string Who) : ShowArgs;
/// <summary>An extra timer action ("toggle", "set", "nudge", "name"...) and its argument.</summary>
public sealed record AuxArgs(string Action, string Argument) : ShowArgs;
/// <summary>A department and its note ("" clears it).</summary>
public sealed record DeptNoteArgs(string Department, string Note) : ShowArgs;
/// <summary>An LTC-in action ("chase" or "end") and, for "end", the timecode ("" clears).</summary>
public sealed record LtcArgs(string Action, string Timecode) : ShowArgs;
/// <summary>Who acknowledged ("" = unknown) and how ("seen" or "done").</summary>
public sealed record AckArgs(string By, string State) : ShowArgs;

/// <summary>The show's core contract: run commands, publish frames, and the static data remotes build buttons from.</summary>
public interface IShowCommands
{
    SnapshotHub Snapshots { get; }

    /// <summary>Thread-safe. Queues the command onto the owner thread and returns immediately.</summary>
    void Execute(ShowCommand command);

    /// <summary>
    /// Thread-safe (never call it on the owner thread and wait). Runs the command on the owner thread and says what
    /// happened (Show Bus receipt): applied with its effect, refused with why, or only accepted when the owner thread
    /// didn't get to it within <paramref name="timeout"/>.
    /// </summary>
    Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
    {
        Execute(command);
        return Task.FromResult(CommandReceipt.Accepted(id));
    }

    /// <summary>Static-ish data remotes need for their buttons: presets, quick messages, running order.</summary>
    RemoteConfig GetRemoteConfig();
}

/// <summary>Who may do what: API keys, the viewing code, and whether AI agents may connect. Thread-safe.</summary>
public interface IShowAccess
{
    /// <summary>Whether the MCP endpoint may answer (enabled in settings and licensed).</summary>
    bool McpAllowed => true;

    /// <summary>The API keys in force (an immutable copy; revoking swaps in a new one).</summary>
    IReadOnlyList<ApiKey> ApiKeys => Array.Empty<ApiKey>();

    /// <summary>The code browser screens need to view the show, or null/empty when screens are open.</summary>
    string? ViewCode => null;

    /// <summary>
    /// Which node each node key speaks for (Show Bus §11). A surface that keeps none still gets bindings of its own, one
    /// per surface and first-bind-wins, so no surface lets one key speak for two nodes.
    /// </summary>
    NodeBindings Nodes => NodeBindings.For(this);
}

/// <summary>Crew pages: crew links and each department's notes. Thread-safe.</summary>
public interface IShowCrew
{
    /// <summary>Crew link policy: the secret behind crew keys, and whether public acknowledgements need one.</summary>
    (string? Secret, bool Required) CrewKeyPolicy => (null, false);

    /// <summary>Departments, for the crew link list.</summary>
    IReadOnlyList<string> CrewDepartments => Array.Empty<string>();

    /// <summary>A department's notes for every item (index, title, note), or null when there's no running order.</summary>
    IReadOnlyList<(int Index, string Title, string Note)>? DeptNotesFor(string dept) => null;
}

/// <summary>What browser screens show besides the frame: names, setups, styling, designs, images and sounds. Thread-safe.</summary>
public interface IShowScreens
{
    /// <summary>The name the operator gave a browser screen, by its id, or null.</summary>
    string? ScreenName(string screenId) => null;

    /// <summary>A saved screen setup's page ("/viewer?…") by its name, or null.</summary>
    string? ScreenPresetPath(string name) => null;

    /// <summary>All saved screen setups, for the operator's lists.</summary>
    IReadOnlyList<(string Name, string Path)> ScreenPresets => Array.Empty<(string, string)>();

    /// <summary>The operator's custom CSS for browser screens ("" = none).</summary>
    string CustomCss => "";

    /// <summary>A designed screen's JSON, or null.</summary>
    string? LayoutJson(string name) => null;

    /// <summary>The designed screens' names.</summary>
    IReadOnlyList<string> LayoutNames => Array.Empty<string>();

    /// <summary>Save a designed screen (from the designer page). Null = saved, else why not.</summary>
    string? SaveLayout(string json) => "Layouts aren't available here.";

    /// <summary>Delete a designed screen.</summary>
    void DeleteLayout(string name) { }

    /// <summary>The copilot designs (or changes) a layout from a sentence. Nothing is saved: the designer previews it.</summary>
    Task<(string? Json, IReadOnlyList<string> Notes)> DesignLayoutAsync(string request, string? currentJson)
        => Task.FromResult<(string?, IReadOnlyList<string>)>((null, new[] { "The copilot isn't available here." }));

    /// <summary>A holding image's bytes for screens to show.</summary>
    (byte[] Data, string ContentType)? HoldingImageData(string id) => null;

    /// <summary>Add an uploaded holding image. Returns its id, or an error for the uploader.</summary>
    (string? Id, string? Error) AddHoldingImage(string name, byte[] data) => (null, "not supported");

    /// <summary>A chime library sound as WAV (for screens that play sound), or null.</summary>
    byte[]? SoundData(string id) => null;

    /// <summary>The controlled windows: an immutable copy, swapped whole on every change.</summary>
    IReadOnlyList<ScreenWindow> Windows => Array.Empty<ScreenWindow>();
}

/// <summary>
/// What this node tells other apps and their AI about itself, and what it hears from them (Show Bus §6). Thread-safe.
/// </summary>
public interface IShowContext
{
    /// <summary>This node's context (the §6 JSON), or null when there isn't one.</summary>
    string? ContextJson() => null;

    /// <summary>This node's own Show Bus id (its hello's), or null when it has none: no node link may name it.</summary>
    string? OwnNodeId => null;

    /// <summary>
    /// A peer posted its context with <paramref name="credential"/>, what the request proved (Show Bus §11). Status 200
    /// with the proof the context is held with; 400 when it isn't a context this node can hold; 403 when the credential
    /// may not speak for its node. A refusal stores nothing.
    /// </summary>
    (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
        => (400, "not supported", NodeProof.None);
}

/// <summary>
/// The shared approval queue (Show Bus §11): this node is the desk. Thread-safe. Each returns an HTTP status and a JSON
/// body. <c>credential</c> is what the request proved: a node key (by its id), the operator (a control key, or the PIN
/// this node asks for), or nothing (a read or MCP key, or no credential on an open node).
/// </summary>
public interface IShowApprovals
{
    (int Status, string Json) SubmitProposal(string body, NodeCredential credential) => (404, "{\"ok\":false,\"error\":\"no approval desk here\"}");
    (int Status, string Json) GetProposal(string id) => (404, "{\"ok\":false,\"error\":\"no such proposal\"}");
    (int Status, string Json) ProposalsFrom(string nodeId) => (200, "{\"proposals\":[]}");
    (int Status, string Json) CollectApproved(string targetId, NodeCredential credential) => (200, "{\"proposals\":[]}");
    (int Status, string Json) ProposalResult(string id, string body, NodeCredential credential) => (404, "{\"ok\":false,\"error\":\"no such proposal\"}");
}

/// <summary>The planned-vs-actual show report. Thread-safe.</summary>
public interface IShowReports
{
    /// <summary>The report ("html" | "csv"), or null when there's nothing to report.</summary>
    string? Report(string format) => null;
}

/// <summary>AI agents (MCP): their commands can wait for the operator's approval. Thread-safe.</summary>
public interface IShowAgents
{
    /// <summary>
    /// An agent wants to run a command. Returns what to tell the agent when it was queued for the operator's
    /// approval (or refused), or null when agents may act directly.
    /// </summary>
    string? ProposeFromAgent(string commandLine, string reason) => null;

    /// <summary>A proposal's fate as JSON (state, and each step's command and result), or null when there's no such proposal.</summary>
    string? ProposalJson(int id) => null;
}

/// <summary>Presenter PCs (Stagewatch Slides) reporting their slideshows. Thread-safe.</summary>
public interface ISlideFeeds
{
    /// <summary>
    /// A presenter PC reports its slideshow. Returns an error for the agent, or null when taken;
    /// <paramref name="needScript"/> asks the agent to send this deck's script once.
    /// </summary>
    string? AcceptSlideFeed(SlideFeed feed, string address, out bool needScript) { needScript = false; return "not supported"; }

    /// <summary>A presenter PC's whole-deck script (speaker notes), sent once per deck.</summary>
    void AcceptSlideScript(string hash, string script) { }
}

/// <summary>
/// Everything a full remote surface (the web server) needs: the union of the focused contracts above. Consumers that
/// need less depend on less — OSC on <see cref="IShowCommands"/>, MCP on commands, reports and agents.
/// </summary>
public interface IShowControl : IShowCommands, IShowAccess, IShowCrew, IShowScreens, IShowReports, IShowAgents, ISlideFeeds, IShowContext, IShowApprovals
{
    /// <summary>A full surface has the remote config, so its departments come from there unless the host says otherwise.</summary>
    IReadOnlyList<string> IShowCrew.CrewDepartments => GetRemoteConfig().Departments ?? Array.Empty<string>();
}

public sealed record RemoteConfig(
    IReadOnlyList<(int Slot, string Label, int DurationSeconds)> Presets,
    IReadOnlyList<(string Label, string Text, MessageUrgency Urgency, FlashRate Flash)> Messages,
    IReadOnlyList<(string Title, string? Speaker, int DurationSeconds)> Rundown,
    IReadOnlyList<(string Id, string Name)>? Ladders = null,
    IReadOnlyList<string>? Departments = null,
    string? Room = null,
    string? ActiveLadder = null,
    /// <summary>Licence name ("Pro", "Production"…) and the feature keys it unlocks, so remotes can dim what isn't available.</summary>
    string? Tier = null,
    IReadOnlyList<string>? Features = null,
    /// <summary>The holding-image library (id, name), for remotes' buttons.</summary>
    IReadOnlyList<(string Id, string Name)>? Holding = null);

/// <summary>
/// The one command grammar. Verbs and arguments are plain strings so every transport
/// maps onto it trivially:
///   HTTP   GET /api/nudge?value=60          → ("nudge", ["60"])
///   OSC    /stagewatch/preset/3 "start"      → ("preset", ["3", "start"])
///   WS     {"cmd":"message","args":["Hi"]}   → ("message", ["Hi"])
/// </summary>
public static class ShowCommandParser
{
    /// <summary>How many auxiliary timers there are (aux 1..AuxSlots).</summary>
    public const int AuxSlots = 3;

    public const string Help =
        "start | pause | stop | toggle | reset | next | previous | goto <n> | preset <n> [start] | " +
        "nudge <±seconds> | duration <secs|mm:ss|hh:mm:ss> | endat <hh:mm|clear> [elastic|realtime] | " +
        "message <text> [info|warn|crit] [off|slow|fast] [ack] | clearmessage | blackout [on|off|toggle] | " +
        "mode <countdown|countup|tod> | output <full|keyed|mini|closed|toggle> | ndi [on|off|toggle] | " +
        "holding <n|name|off|toggle> | style <holo|egg|standard|toggle> | ltc chase [on|off|toggle] | ltc end <HH:MM:SS:FF|clear> | " +
        "aux <1-3> start|pause|toggle|reset|stage|unstage|up|down | aux <n> set <time> | aux <n> nudge <secs> | aux <n> name <text> | " +
        "deptnote <item> <department> [text] | roll [on|off|toggle] | rundown <name> | undo | redo | endofshow <HH:MM|clear> | loop <off|n|forever> | " +
        "screen reload [screen] | screen disconnect [screen] | patterns end [seconds] | patterns restart | patterns play | patterns pause | patterns stop | patterns seek <m:ss> | " +
        "screen identify [screen] | screen go <preset|/page> [screen] | screen name <id> <name> | screen save <preset> <screen> | " +
        "window new <name> [page] | window show <window|all> <page|setup|holding|black> | window cue <window|all> <page> | " +
        "window hold <window|all> [toggle|text <words>|image <n|name|off>] | window release <window|all> | window identify|reload <window|all> | " +
        "window loss <window|all> <hold|warn> | window fade <window|all> <ms> | window rename <window> <name> | window revoke <window|all> | window delete <window> | " +
        "signal <message|standby|go|call|nudge> <target> [text] [noack] | signal clear <target|id> | signal resend <id> | " +
        "ack <id> [seen|done] [name] | trim <item> <±seconds> | delay <seconds|mm:ss|clear> | catchup | preview [on|off] | " +
        "ladder <id|default> | format <standard|calm|progress> | question <text> [name] | " +
        "q <id|next|clear> [approve|star|live|answered|dismiss] | rehearse [on|off] | listen [on|off] | report [html|csv] | note <text>";

    public static bool TryParse(string verb, IReadOnlyList<string> args, out ShowCommand cmd, out string error)
    {
        if (!TryParseCore(verb, args, out cmd, out error)) return false;
        // Commands with several arguments carry them typed (read with Get<T>), not just packed in Text.
        if (ShowArgs.From(cmd) is { } typed) cmd = cmd with { Args = typed };
        return true;
    }

    private static bool TryParseCore(string verb, IReadOnlyList<string> args, out ShowCommand cmd, out string error)
    {
        cmd = default;
        error = "";
        var inv = CultureInfo.InvariantCulture;
        string Arg(int i) => i < args.Count ? args[i].Trim() : "";

        try
        {
            switch (verb.Trim().ToLowerInvariant())
            {
                case "start": case "play": case "go":
                    cmd = new(ShowCommandKind.Start); return true;
                case "pause": case "stop":
                    cmd = new(ShowCommandKind.Pause); return true;
                case "toggle":
                    cmd = new(ShowCommandKind.Toggle); return true;
                case "reset":
                    cmd = new(ShowCommandKind.Reset); return true;
                case "next":
                    cmd = new(ShowCommandKind.Next); return true;
                case "previous": case "prev": case "back":
                    cmd = new(ShowCommandKind.Previous); return true;

                case "goto":
                    cmd = new(ShowCommandKind.GoTo, Number: int.Parse(Arg(0), inv),
                        Flag: Arg(1).Equals("start", StringComparison.OrdinalIgnoreCase));
                    return true;

                case "preset":
                    cmd = new(ShowCommandKind.Preset, Number: int.Parse(Arg(0), inv),
                        Flag: Arg(1).Equals("start", StringComparison.OrdinalIgnoreCase));
                    return true;

                case "nudge": case "add":
                    var secs = double.Parse(Arg(0), NumberStyles.Float, inv);
                    if (!double.IsFinite(secs) || Math.Abs(secs) > 86400) { error = "nudge out of range"; return false; }
                    cmd = new(ShowCommandKind.Nudge, Number: secs);
                    return true;

                case "duration": case "set":
                    if (!TryParseSpan(Arg(0), out var span)) { error = "bad duration"; return false; }
                    cmd = new(ShowCommandKind.SetDuration, Number: span.TotalSeconds);
                    return true;

                case "endat":
                    var a = Arg(0);
                    if (a is "" or "-" || a.Equals("clear", StringComparison.OrdinalIgnoreCase))
                    { cmd = new(ShowCommandKind.EndAt, Text: null); return true; }
                    if (!Schedule.TryParseClock(a, out _)) { error = "bad time, use HH:MM"; return false; }
                    var pace = Arg(1).ToLowerInvariant() switch
                    {
                        "" => 0,
                        "elastic" or "stretch" => 1,
                        "realtime" or "real" => 2,
                        _ => -1
                    };
                    if (pace < 0) { error = "bad pace, use elastic or realtime"; return false; }
                    cmd = new(ShowCommandKind.EndAt, Number: pace, Text: a);
                    return true;

                case "message": case "msg":
                    var text = Arg(0);
                    if (text.Length > 500) text = text[..500];
                    cmd = new(ShowCommandKind.Message, Text: text,
                        Urgency: args.Count > 1 ? ParseUrgency(Arg(1)) : MessageUrgency.Info,
                        Flash: args.Count > 2 ? ParseFlash(Arg(2)) : FlashRate.Off,
                        Flag: args.Skip(3).Any(x => x.Trim().Equals("ack", StringComparison.OrdinalIgnoreCase)));
                    return true;

                case "signal": case "cue":
                {
                    var sub = Arg(0).ToLowerInvariant();
                    if (sub == "clear")
                    {
                        var target = Arg(1);
                        cmd = int.TryParse(target, NumberStyles.Integer, inv, out var cid)
                            ? new(ShowCommandKind.SignalClear, Number: cid)
                            : new(ShowCommandKind.SignalClear, Text: target.Length == 0 ? "*" : target);
                        return true;
                    }
                    if (sub == "resend")
                    {
                        cmd = new(ShowCommandKind.SignalResend, Number: int.Parse(Arg(1), inv));
                        return true;
                    }
                    var kind = sub switch
                    {
                        "message" or "msg" => SignalKind.Message,
                        "standby" or "sb" => SignalKind.Standby,
                        "go" => SignalKind.Go,
                        "call" => SignalKind.Call,
                        "nudge" or "haptic" or "buzz" => SignalKind.Nudge,
                        _ => throw new FormatException("signal takes message|standby|go|call|nudge|clear|resend")
                    };
                    var who = Arg(1);
                    // "signal go all": one GO to every department standing by — a cue that spans departments.
                    if (kind == SignalKind.Go && who.Trim().ToLowerInvariant() is "all" or "*")
                    {
                        cmd = new(ShowCommandKind.SignalGoAll);
                        return true;
                    }
                    if (who.Length == 0) who = kind == SignalKind.Call ? "backstage" : "speaker";
                    var body = Arg(2);
                    if (body.Length > 200) body = body[..200];
                    cmd = new(ShowCommandKind.Signal, Number: (int)kind, Text: who + "\u0001" + body,
                        Flag: !args.Skip(3).Any(x => x.Trim().Equals("noack", StringComparison.OrdinalIgnoreCase)));
                    return true;
                }

                case "ack":
                {
                    var id = int.Parse(Arg(0), inv);
                    var a1 = Arg(1).ToLowerInvariant();
                    var isStage = a1 is "" or "seen" or "done" or "ok" or "found" or "inposition" or "in-position";
                    var stage = a1 is "done" or "ok" or "found" or "inposition" or "in-position" ? "done" : "seen";
                    var by = isStage ? Arg(2) : Arg(1);
                    if (by.Length > 40) by = by[..40];
                    cmd = new(ShowCommandKind.Ack, Number: id, Text: by.Replace("|", "/") + "|" + stage);
                    return true;
                }

                case "trim": case "adjust":
                {
                    var item = int.Parse(Arg(0), inv);
                    var delta = double.Parse(Arg(1), NumberStyles.Float, inv);
                    if (!double.IsFinite(delta) || Math.Abs(delta) > 86400) { error = "trim out of range"; return false; }
                    cmd = new(ShowCommandKind.Trim, Number: item, Text: ((int)Math.Round(delta)).ToString(inv));
                    return true;
                }

                case "delay":
                {
                    var d0 = Arg(0);
                    double dsecs = d0.Length == 0 || d0.Equals("clear", StringComparison.OrdinalIgnoreCase) ? 0
                        : d0.Contains(':') && TryParseSpan(d0, out var dspan) ? dspan.TotalSeconds
                        : double.Parse(d0, NumberStyles.Float, inv);
                    if (!double.IsFinite(dsecs) || dsecs < 0 || dsecs > 86400) { error = "delay out of range"; return false; }
                    cmd = new(ShowCommandKind.Delay, Number: dsecs);
                    return true;
                }

                case "catchup": case "reachschedule":
                    cmd = new(ShowCommandKind.CatchUp); return true;

                case "preview":
                    cmd = new(ShowCommandKind.Preview, Number: OnOff(Arg(0))); return true;

                case "ladder":
                    cmd = new(ShowCommandKind.Ladder, Text: Arg(0).Equals("default", StringComparison.OrdinalIgnoreCase) ? "" : Arg(0)); return true;

                case "format":
                    cmd = new(ShowCommandKind.Format, Number: (int)(Arg(0).ToLowerInvariant() switch
                    {
                        "" or "standard" or "normal" => SpeakerTimerFormat.Standard,
                        "calm" or "minutes" => SpeakerTimerFormat.Calm,
                        "progress" or "bar" or "progressonly" => SpeakerTimerFormat.ProgressOnly,
                        _ => throw new FormatException("format takes standard|calm|progress")
                    }));
                    return true;

                case "question": case "ask":
                {
                    var qtext = Arg(0);
                    if (qtext.Length == 0) { error = "empty question"; return false; }
                    if (qtext.Length > QaBoard.MaxLength) qtext = qtext[..QaBoard.MaxLength];
                    var qname = Arg(1);
                    if (qname.Length > 40) qname = qname[..40];
                    cmd = new(ShowCommandKind.Question, Text: qtext + "\u0001" + qname);
                    return true;
                }

                case "q": case "qa":
                {
                    var target = Arg(0).ToLowerInvariant();
                    if (target is "next" or "clear") { cmd = new(ShowCommandKind.QuestionSet, Number: 0, Text: target); return true; }
                    var state = Arg(1).ToLowerInvariant();
                    if (state is not ("approve" or "star" or "live" or "answered" or "dismiss" or "new"))
                        throw new FormatException("q takes <id> approve|star|live|answered|dismiss, or q next|clear");
                    cmd = new(ShowCommandKind.QuestionSet, Number: int.Parse(target, inv), Text: state);
                    return true;
                }

                case "rehearse":
                    cmd = new(ShowCommandKind.Rehearse, Number: OnOff(Arg(0))); return true;

                case "listen":
                    cmd = new(ShowCommandKind.Listen, Number: OnOff(Arg(0))); return true;

                case "report":
                    cmd = new(ShowCommandKind.Report, Text: Arg(0).ToLowerInvariant() is "csv" ? "csv" : "html"); return true;

                case "screen": case "screens":
                {
                    // screen identify [who] | screen go <preset|/path> [who] | screen name <id> <name> | screen save <preset> <who>
                    var action = Arg(0).ToLowerInvariant();
                    string Rest(int from) => string.Join(' ', args.Skip(from)).Trim();
                    static string Who(string w) => w.Length == 0 ? "all" : (w.Length > 60 ? w[..60] : w).Replace('\u0001', ' ');
                    switch (action)
                    {
                        case "reload": case "refresh":
                            cmd = new(ShowCommandKind.Screen, Text: "reload\u0001\u0001" + Who(Rest(1)));
                            return true;
                        case "disconnect": case "kick":
                            cmd = new(ShowCommandKind.Screen, Text: "disconnect\u0001\u0001" + Who(Rest(1)));
                            return true;
                        case "identify": case "id": case "find":
                            cmd = new(ShowCommandKind.Screen, Text: "identify\u0001\u0001" + Who(Rest(1)));
                            return true;
                        case "go": case "send": case "show":
                        {
                            var target = Arg(1);
                            if (!ScreenPreset.ValidName(target) && !ScreenPreset.ValidPath(target))
                                throw new FormatException("screen go takes a preset name or a page like /viewer");
                            cmd = new(ShowCommandKind.Screen, Text: "go\u0001" + target + "\u0001" + Who(Rest(2)));
                            return true;
                        }
                        case "name": case "rename":
                        {
                            var id = Arg(1); var name = Rest(2);
                            if (id.Length == 0 || name.Length > 40) throw new FormatException("screen name takes a screen id and a name (up to 40 characters)");
                            cmd = new(ShowCommandKind.Screen, Text: "name\u0001" + name.Replace('\u0001', ' ') + "\u0001" + id);
                            return true;
                        }
                        case "save":
                        {
                            var preset = Arg(1);
                            if (!ScreenPreset.ValidName(preset)) throw new FormatException("preset names use letters, digits, - and _ (e.g. lobby)");
                            var who = Rest(2);
                            if (who.Length == 0) throw new FormatException("screen save takes a preset name and the screen to copy");
                            cmd = new(ShowCommandKind.Screen, Text: "save\u0001" + preset + "\u0001" + Who(who));
                            return true;
                        }
                    }
                    throw new FormatException("screen takes identify [screen], go <preset|/page> [screen], name <id> <name>, or save <preset> <screen>");
                }

                case "window": case "windows": case "win":
                {
                    // window <action> <who> [argument…]: who is one word (a window name, an id or all), so the rest is
                    // the argument whole: a page, a setup, words for the holding page.
                    var action = Arg(0).ToLowerInvariant();
                    var who = Arg(1);
                    var rest = string.Join(' ', args.Skip(2)).Trim();
                    if (action.Length == 0) throw new FormatException("window takes new, show, cue, hold, release, identify, reload, loss, fade, rename, revoke or delete");
                    if (action is not ("new" or "show" or "cue" or "hold" or "release" or "identify" or "reload" or "loss" or "fade" or "rename" or "revoke" or "delete"))
                        throw new FormatException($"window {action}? It takes new, show, cue, hold, release, identify, reload, loss, fade, rename, revoke or delete");
                    if (who.Length == 0 && action is "new" or "rename" or "delete") throw new FormatException($"window {action} takes a window name");
                    if (who.Length > 40 || (who.Length > 0 && !ScreenWindows.ValidName(who))) throw new FormatException("a window is named with letters, digits, - and _ (or all)");
                    if (action is "show" or "cue" && rest.Length == 0) throw new FormatException($"window {action} takes a window and what to show: a page (/clock), a setup, holding or black");
                    if (rest.Length > 400) throw new FormatException("that's longer than a window command takes");
                    cmd = new(ShowCommandKind.Window, Text: ShowArgs.Pack(action, who.Length == 0 ? "all" : who, rest));
                    return true;
                }

                case "ltc":
                {
                    var sub = Arg(0).ToLowerInvariant();
                    if (sub == "chase") { cmd = new(ShowCommandKind.Ltc, Text: "chase", Number: OnOff(Arg(1))); return true; }
                    if (sub == "end")
                    {
                        var tc = Arg(1);
                        if (tc.Length == 0 || tc.Equals("clear", StringComparison.OrdinalIgnoreCase) || tc.Equals("off", StringComparison.OrdinalIgnoreCase)) tc = "";
                        else if (!System.Text.RegularExpressions.Regex.IsMatch(tc, @"^\d{1,2}[:;.]\d{2}[:;.]\d{2}([:;.]\d{2})?$"))
                            throw new FormatException("ltc end takes a timecode HH:MM:SS:FF, or clear");
                        cmd = new(ShowCommandKind.Ltc, Text: "end\u0001" + tc);
                        return true;
                    }
                    throw new FormatException("ltc takes chase on|off|toggle, or end <HH:MM:SS:FF|clear>");
                }

                case "style":
                    cmd = new(ShowCommandKind.Style, Number: Arg(0).ToLowerInvariant() switch
                    {
                        "" or "toggle" => -1,
                        "holo" or "holographic" or "hologram" or "3d" or "on" => 1,
                        "egg" or "eggtimer" or "egg-timer" or "hourglass" => 2,
                        "standard" or "flat" or "normal" or "off" => 0,
                        _ => throw new FormatException("style takes holo|egg|standard|toggle")
                    });
                    return true;

                case "aux": case "timer2":
                {
                    // aux <1-3> start|pause|toggle|reset|stage|unstage|up|down | aux <n> set <duration> | aux <n> nudge <±secs> | aux <n> name <text>
                    if (!int.TryParse(Arg(0), out var slot) || slot is < 1 or > AuxSlots) throw new FormatException($"aux takes a timer number 1-{AuxSlots}");
                    var action = Arg(1).ToLowerInvariant();
                    var rest = string.Join(' ', args.Skip(2)).Trim();
                    switch (action)
                    {
                        case "start" or "pause" or "stop" or "toggle" or "reset" or "stage" or "unstage" or "up" or "down" or "go" or "":
                            cmd = new(ShowCommandKind.Aux, Number: slot, Text: (action is "" or "go" ? "toggle" : action == "stop" ? "pause" : action) + "\u0001");
                            return true;
                        case "set" or "load":
                            if (!TryParseSpan(rest, out var d) || d < TimeSpan.Zero || d > TimeSpan.FromHours(24)) throw new FormatException("aux set takes a duration like 5:00");
                            cmd = new(ShowCommandKind.Aux, Number: slot, Text: "set\u0001" + ((long)d.TotalSeconds).ToString(CultureInfo.InvariantCulture));
                            return true;
                        case "nudge" or "add":
                            if (!double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out var auxSecs) || Math.Abs(auxSecs) > 86400) throw new FormatException("aux nudge takes seconds, e.g. 30 or -30");
                            cmd = new(ShowCommandKind.Aux, Number: slot, Text: "nudge\u0001" + auxSecs.ToString(CultureInfo.InvariantCulture));
                            return true;
                        case "name" or "rename":
                            cmd = new(ShowCommandKind.Aux, Number: slot, Text: "name\u0001" + (rest.Length > 30 ? rest[..30] : rest).Replace('\u0001', ' '));
                            return true;
                    }
                    throw new FormatException("aux takes start, pause, toggle, reset, stage, unstage, up, down, set <time>, nudge <secs> or name <text>");
                }

                case "holding": case "hold":
                {
                    var key = string.Join(' ', args).Trim();
                    if (key.Length == 0) key = "toggle";
                    if (key.Length > 100) throw new FormatException("holding takes an image number, name, off or toggle");
                    cmd = new(ShowCommandKind.Holding, Text: key.ToLowerInvariant() is "clear" or "none" or "0" ? "off" : key);
                    return true;
                }

                case "rundown": case "runningorder":
                {
                    var name = string.Join(' ', args).Trim();
                    if (name.Length is 0 or > 40) throw new FormatException("rundown takes the name of one of the show's running orders");
                    cmd = new(ShowCommandKind.Rundown, Text: name);
                    return true;
                }

                case "endofshow": case "showend": case "ends":
                {
                    var t = string.Join(' ', args).Trim();
                    if (t.Equals("clear", StringComparison.OrdinalIgnoreCase) || t.Equals("off", StringComparison.OrdinalIgnoreCase)) t = "";
                    if (t.Length > 0 && !ShowClock.IsValidTimeText(t)) throw new FormatException("endofshow takes a time (17:30 or 2026-10-24 17:30), or clear");
                    cmd = new(ShowCommandKind.EndOfShow, Text: t);
                    return true;
                }
                case "loop":
                {
                    var a0 = Arg(0).ToLowerInvariant();
                    var n = a0 is "" or "on" or "forever" or "always" or "infinite" ? -1 : a0 is "off" or "no" or "0" ? 0
                        : int.TryParse(a0, out var k) && k is > 0 and <= 99 ? k : throw new FormatException("loop takes off, a number of extra passes (1-99) or forever");
                    cmd = new(ShowCommandKind.Loop, Number: n);
                    return true;
                }

                case "patterns": case "pvideo":
                {
                    // patterns end [seconds]  — the clip on air in Patterns jumps to its last seconds (default 10)
                    // patterns restart        — plays it again from the top. No raw lines: only these two reach Patterns.
                    var sub = Arg(0).ToLowerInvariant();
                    if (sub is "end" or "last" or "out" or "tail")
                    {
                        var a1 = Arg(1);
                        var pSecs = a1.Length == 0 ? 10 : int.TryParse(a1, out var k) && k is >= 1 and <= 3600 ? k
                            : throw new FormatException("patterns end takes the seconds before the clip's end (1-3600, default 10)");
                        cmd = new(ShowCommandKind.Patterns, Number: pSecs, Text: "end");
                        return true;
                    }
                    if (sub is "restart" or "top" or "rewind")
                    {
                        cmd = new(ShowCommandKind.Patterns, Text: "restart");
                        return true;
                    }
                    // Patterns Round 86+: play on, pause, stop the clip (back to the top, held — never outputs off), seek.
                    if (sub is "play" or "resume") { cmd = new(ShowCommandKind.Patterns, Text: "play"); return true; }
                    if (sub is "pause" or "hold") { cmd = new(ShowCommandKind.Patterns, Text: "pause"); return true; }
                    if (sub is "stop") { cmd = new(ShowCommandKind.Patterns, Text: "stop"); return true; }
                    if (sub is "seek" or "goto" or "at")
                    {
                        var at = Arg(1);
                        if (!TryParseSpan(at, out var seekAt) || seekAt < TimeSpan.Zero || seekAt > TimeSpan.FromHours(24))
                            throw new FormatException("patterns seek takes a point in the clip: seconds, m:ss or h:mm:ss");
                        cmd = new(ShowCommandKind.Patterns, Number: Math.Floor(seekAt.TotalSeconds), Text: "seek");
                        return true;
                    }
                    throw new FormatException("patterns takes end [seconds], restart, play, pause, stop or seek <m:ss>");
                }

                case "undo":
                    cmd = new(ShowCommandKind.Undo, Number: 1); return true;
                case "redo":
                    cmd = new(ShowCommandKind.Undo, Number: -1); return true;

                case "roll":
                    cmd = new(ShowCommandKind.Roll, Number: OnOff(Arg(0))); return true;

                case "deptnote": case "dnote":
                {
                    // deptnote <item> <department> [text…]  — empty text clears that department's note
                    if (!int.TryParse(Arg(0), out var item) || item < 1) throw new FormatException("deptnote takes an item number, a department and the note");
                    var dept = Arg(1);
                    if (dept.Length is 0 or > 40 || dept.Contains('\u0001')) throw new FormatException("deptnote needs a department name (up to 40 characters)");
                    var noteText = string.Join(' ', args.Skip(2)).Trim();
                    if (noteText.Length > 1000) noteText = noteText[..1000];
                    cmd = new(ShowCommandKind.DeptNote, Number: item, Text: dept + "\u0001" + noteText.Replace('\u0001', ' '));
                    return true;
                }

                case "note": case "log":
                {
                    var note = Arg(0);
                    if (note.Length > 300) note = note[..300];
                    cmd = new(ShowCommandKind.Note, Text: note);
                    return true;
                }

                case "clearmessage": case "messageclear": case "clear":
                    cmd = new(ShowCommandKind.ClearMessage); return true;

                case "blackout":
                    var b = Arg(0).ToLowerInvariant();
                    // Number: 1 = on, 0 = off, -1 = toggle
                    cmd = new(ShowCommandKind.Blackout, Number: b switch
                    {
                        "" or "toggle" => -1,
                        "on" or "1" or "true" => 1,
                        "off" or "0" or "false" => 0,
                        _ => throw new FormatException("blackout takes on|off|toggle")
                    });
                    return true;

                case "output":
                    cmd = new(ShowCommandKind.Output, Number: Arg(0).ToLowerInvariant() switch
                    {
                        "" or "toggle" => -1,
                        "closed" or "close" or "off" => (int)OutputMode.Closed,
                        "full" or "fullscreen" or "on" or "open" => (int)OutputMode.FullScreen,
                        "keyed" or "key" => (int)OutputMode.KeyedFullScreen,
                        "mini" or "floating" => (int)OutputMode.FloatingMinimal,
                        _ => throw new FormatException("output takes closed|full|keyed|mini|toggle")
                    });
                    return true;

                case "ndi":
                    cmd = new(ShowCommandKind.Ndi, Number: OnOff(Arg(0))); return true;

                case "mode":
                    cmd = new(ShowCommandKind.Mode, Mode: Arg(0).ToLowerInvariant() switch
                    {
                        "countdown" or "down" => TimerMode.Countdown,
                        "countup" or "up" or "elapsed" => TimerMode.CountUp,
                        "tod" or "timeofday" or "clock" => TimerMode.TimeOfDay,
                        _ => throw new FormatException("mode takes countdown|countup|tod")
                    });
                    return true;

                default:
                    error = "unknown command: " + verb;
                    return false;
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            error = $"{verb}: {ex.Message}";
            return false;
        }
    }

    /// <summary>on / off / toggle (or empty) → 1 / 0 / -1.</summary>
    private static int OnOff(string s) => s.ToLowerInvariant() switch
    {
        "" or "toggle" => -1,
        "on" or "1" or "true" or "start" => 1,
        "off" or "0" or "false" or "stop" => 0,
        _ => throw new FormatException("expected on|off|toggle")
    };

    /// <summary>
    /// Verbs a read-only client (speaker view, audience phone) may send without the PIN: acknowledging
    /// a cue and asking a question. Nothing that changes the clock or what the audience sees.
    /// </summary>
    /// <summary>Verbs a page may send without the PIN (each is checked further: crew keys, rate limits).</summary>
    public static bool IsPublicVerb(string verb) => verb.Trim().ToLowerInvariant() is "ack" or "question" or "ask" or "deptnote" or "dnote";

    public static bool TryParseSpan(string s, out TimeSpan ts)
    {
        ts = default;
        var inv = CultureInfo.InvariantCulture;
        var parts = s.Split(':');
        try
        {
            ts = parts.Length switch
            {
                3 => new TimeSpan(int.Parse(parts[0], inv), int.Parse(parts[1], inv), int.Parse(parts[2], inv)),
                2 => new TimeSpan(0, int.Parse(parts[0], inv), int.Parse(parts[1], inv)),
                1 => TimeSpan.FromSeconds(double.Parse(parts[0], NumberStyles.Float, inv)),
                _ => throw new FormatException()
            };
            return ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(4);
        }
        catch { return false; }
    }

    public static MessageUrgency ParseUrgency(string s) => s.ToLowerInvariant() switch
    {
        "" or "info" => MessageUrgency.Info,
        "warn" or "warning" => MessageUrgency.Warning,
        "crit" or "critical" => MessageUrgency.Critical,
        _ => throw new FormatException("urgency must be info|warn|crit")
    };

    public static FlashRate ParseFlash(string s) => s.ToLowerInvariant() switch
    {
        "" or "off" => FlashRate.Off,
        "slow" => FlashRate.Slow,
        "fast" => FlashRate.Fast,
        _ => throw new FormatException("flash must be off|slow|fast")
    };
}

/// <summary>
/// What became of a command (Show Bus §3): <c>accepted</c> (queued — the owner thread didn't confirm in time) or
/// <c>applied</c> (run; <see cref="Effect"/> says what changed), or refused (<see cref="Ok"/> false, <see cref="Error"/>).
/// <see cref="Refs"/> names what it made: a message's signal id, an AI agent's proposal id.
/// </summary>
public sealed record CommandReceipt(string Id, bool Ok, string Stage, string Effect, string? Error, IReadOnlyDictionary<string, long>? Refs = null)
{
    public static CommandReceipt Accepted(string id) => new(id, true, "accepted", "", null);
    public static CommandReceipt Refused(string id, string error) => new(id, false, "applied", "", error);

    /// <summary>A request id as the Show Bus allows it: 1–32 of letters, digits, '-' and '_'. Null when not.</summary>
    public static string? CleanId(string? id)
        => id is { Length: > 0 and <= 32 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;

    public byte[] ToJson() => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
    {
        ["type"] = "receipt", ["id"] = Id, ["ok"] = Ok, ["stage"] = Stage, ["effect"] = Effect,
        ["error"] = Error, ["refs"] = Refs ?? new Dictionary<string, long>(),
    });
}
