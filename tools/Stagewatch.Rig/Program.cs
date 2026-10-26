using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stagewatch.Companion;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

// The headless rig: the real web server, Companion server and OSC server around a simulated show, on any OS.
//   dotnet run --project tools/Stagewatch.Rig -- [--port 19940] [--companion 19941] [--osc 19942] [--pin 4321]
//       [--bind local|all] [--state rig-windows.json] [--view CODE] [--room "Rig Hall"] [--node-keys N] [--exit-after SECONDS]
//       [--bus PORT [--bus-to ADDRESS:PORT]]
// The first line on standard output is one JSON object: {"rig":"ready","http":…,"companion":…,"osc":…,"pin":…,"node":…,
// "windows":[…],"nodeKeys":[{"id","secret"}],"bus":…}; nodeKeys lists only the node keys made in this run (--node-keys N makes
// them on a first start; --state keeps their hashes and the node each speaks for, never a secret).
// --bus PORT puts the rig on the Show Bus (off by default): it hears hellos on that port, joins the nodes it hears into its
// context as the app does, and says the app's own hello (ShowBusHello.Main over its servers' doors and --bind) — to §2's targets
// on that port, or only to --bus-to. As the app, it says nothing while --bind is local, and its context's bus:hello is amber while
// its hello cannot go out or another machine says its id. "bus" in the ready line is the port, or 0.
// The rig simulates the timer, messages, blackout, holding images, the running order's Next/Previous/GoTo, windows,
// screen commands (browsers only, never a show app's node link) and the contexts show apps post; any other command is
// refused with words (in its receipt and its log), never pretended.
// Control lines on standard input, answered in JSON lines on standard output; a blank line is ignored:
//   stall <ms>   1-30000: the show thread is blocked that long, so every link hears the real stuck beat; answered twice,
//                → {"rig":"stalled","ms":…,"at":<unix ms>} as it stops, {"rig":"running","at":<unix ms>} as it turns over
//                (stopping the rig ends a stall early: its running line comes as the rig stops)
//   anything else is refused, never guessed → {"rig":"refused","line":"<first word>","error":"…"}
// The end of standard input ends the reader, never the rig.
var options = RigOptions.Parse(args);
if (options is null) return 2;
using var rig = new Rig(options);
return await rig.RunAsync();

sealed record RigOptions(int Http, int Companion, int Osc, string Pin, bool BindAll, string? StateFile, string ViewCode, string Room, int ExitAfterSeconds, int NodeKeys,
    int Bus = 0, IPEndPoint? BusTo = null)
{
    public static RigOptions? Parse(string[] args)
    {
        var o = new RigOptions(19940, 19941, 19942, "4321", false, null, "", "Rig Hall", 0, 0);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            int Port() => int.Parse(Next(), CultureInfo.InvariantCulture) is var p and >= 0 and <= 65535 ? p : throw new ArgumentException("ports are 0-65535");
            int Keys() => int.Parse(Next(), CultureInfo.InvariantCulture) is var n and >= 0 and <= ApiKeys.Max ? n : throw new ArgumentException($"--node-keys is 0-{ApiKeys.Max}");
            try
            {
                o = args[i] switch
                {
                    "--port" => o with { Http = Port() },
                    "--companion" => o with { Companion = Port() },
                    "--osc" => o with { Osc = Port() },
                    "--pin" => o with { Pin = Next() },
                    "--bind" => o with { BindAll = Next() == "all" },
                    "--state" => o with { StateFile = Next() },
                    "--view" => o with { ViewCode = Next() },
                    "--room" => o with { Room = Next() },
                    "--exit-after" => o with { ExitAfterSeconds = int.Parse(Next(), CultureInfo.InvariantCulture) },
                    "--node-keys" => o with { NodeKeys = Keys() },
                    "--bus" => o with { Bus = Port() is var b and > 0 ? b : throw new ArgumentException("--bus is 1-65535") },
                    "--bus-to" => o with
                    {
                        BusTo = IPEndPoint.TryParse(Next(), out var to) && to.Port > 0 && to.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                            ? to : throw new ArgumentException("--bus-to takes an IPv4 address and a port"),
                    },
                    _ => throw new ArgumentException($"unknown option {args[i]}"),
                };
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
            {
                Console.Error.WriteLine("Stagewatch.Rig: " + ex.Message);
                return null;
            }
        }
        if (o.BusTo is not null && o.Bus == 0)
        {
            Console.Error.WriteLine("Stagewatch.Rig: --bus-to needs --bus");
            return null;
        }
        return o;
    }
}

/// <summary>
/// One owner thread runs every command and builds every frame (as the app's show thread does); the servers run on the
/// thread pool and reach it only through <see cref="Execute"/>, <see cref="ExecuteWithReceiptAsync"/> and immutable reads.
/// </summary>
sealed class Rig : ICompanionBridge, IDisposable
{
    private readonly RigOptions _o;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _owner;
    private readonly WindowDirector _windows;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly string _nodeId = "rig-" + ScreenWindows.NewId()[1..];
    private Timer? _ticker;
    private WebServer? _web;
    /// <summary>The rig on the Show Bus (--bus): its hello and what it hears; null when it is off.</summary>
    private SlidesBeacon? _beacon;
    private readonly string _inst = ShowBusHello.NewInst();
    private HelloSaid _helloSaid;   // owner thread: the last beat's outcome it logged
    private bool _helloTwin;        // owner thread: whether it last logged another machine saying its id
    private CompanionServer? _companion;
    private OscServer? _osc;
    private int _item;
    private string _message = "";
    private MessageUrgency _urgency;
    private FlashRate _flash;
    private bool _blackout;
    private readonly HoldingSelection _holding = new();
    private volatile string? _context;

    // Node keys and what they speak for (Show Bus §11): the keys as saved, owner thread only; an immutable copy for the
    // servers, swapped whole; the bindings every node route and /ws speak through; the contexts show apps post.
    private readonly List<ApiKey> _keys;
    private volatile IReadOnlyList<ApiKey> _apiKeys;
    private readonly NodeBindings _nodes;
    private readonly PeerContexts _peerContexts;
    /// <summary>The node keys made in this run, with their secrets: printed once in the ready line, then forgotten.</summary>
    private List<(string Id, string Secret)>? _madeKeys;
    /// <summary>The show network as last joined (owner thread, once a second), for any thread to read.</summary>
    private volatile NetworkView _network = NetworkView.Empty;

    // Screens: the names the operator gave them and the saved setups, immutable copies swapped whole on the owner thread.
    private volatile IReadOnlyDictionary<string, string> _screenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private volatile IReadOnlyList<(string Name, string Path)> _presets = new[] { ("lobby", "/audience?holding=0") };

    // The control reader's stall: 1 from the line's acceptance until the owner thread runs again, so stalls never overlap.
    public const int MaxStallMs = 30_000;
    private int _stalling;
    private readonly CancellationTokenSource _stopping = new();
    private int _tickQueued;
    private readonly object _stdout = new();

    private static readonly (string Title, string Speaker, SegmentKind Kind, int Seconds, string Group)[] Agenda =
    {
        ("Welcome", "Host", SegmentKind.Talk, 600, "Opening"),
        ("Keynote", "Ana Ruiz", SegmentKind.Talk, 1800, "Opening"),
        ("Panel: The next decade", "Moderated by Sam", SegmentKind.Panel, 2700, "Opening"),
        ("Coffee break", "", SegmentKind.Break, 1200, ""),
        ("Product demo", "Lee", SegmentKind.Talk, 1500, "Afternoon"),
        ("Awards", "", SegmentKind.Award, 1800, "Afternoon"),
    };

    /// <summary>One holding image, made here (a 2×2 PNG), so holding commands and window holding cards have a picture.</summary>
    private static readonly HoldingImage Sponsors = new() { Id = "a1b2c3d4e5f6", Name = "Sponsors", File = "a1b2c3d4e5f6.png" };
    private static readonly byte[] SponsorsPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP4z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==");

    public Rig(RigOptions o)
    {
        _o = o;
        var state = LoadState();
        _keys = state.Keys;
        MakeNodeKeys(o.NodeKeys);
        _apiKeys = CopyKeys(_keys);
        _nodes = new NodeBindings(_keys);   // a pair saved on a key is put back: a restart keeps what each key speaks for
        _peerContexts = new PeerContexts(_nodes);
        _windows = new WindowDirector(state.Windows, SaveWindows);
        _nodes.Bound += OnBound;   // raised on any thread; logged and saved on the owner thread
        if (_madeKeys is { Count: > 0 }) SaveState(state.Windows);
        Settings.Remote.Pin = o.Pin;
        Settings.Integrations.RoomName = o.Room;
        Settings.Holding.Images.Add(Sponsors);
        Engine.SetDuration(TimeSpan.FromSeconds(Agenda[_item].Seconds));
        _owner = new Thread(OwnerLoop) { IsBackground = true, Name = "rig owner" };
    }

    public SnapshotHub Snapshots { get; } = new();
    public TimerEngine Engine { get; } = new();
    public AppSettings Settings { get; } = new();
    public bool CompanionAllowed => true;
    public event Action<MessageState>? MessageChanged;
    public event Action<string>? CommandRecorded;

    public async Task<int> RunAsync()
    {
        _owner.Start();
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
        try
        {
            _web = new WebServer(this, () => _o.Pin.Length == 0 ? null : _o.Pin);
            _web.Start(_o.Http, _o.BindAll);
            _windows.Web = _web;
            if (_o.Companion > 0) { _companion = new CompanionServer(this); _companion.Start(_o.Companion, _o.BindAll); }
            if (_o.Osc > 0) { _osc = new OscServer(this); _osc.Start(_o.Osc, _o.BindAll, null, 0); }
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Console.Error.WriteLine("Stagewatch.Rig: couldn't listen: " + ex.Message);
            return 3;
        }
        if (_o.Bus > 0) _beacon = new SlidesBeacon(SayHello, _o.Bus, _o.BusTo is { } to ? [to] : null);
        _ticker = new Timer(_ => QueueTick(), null, 0, 250);
        Say(new
        {
            rig = "ready", http = _o.Http, companion = _o.Companion, osc = _o.Osc, pin = _o.Pin, node = _nodeId,
            windows = _windows.Windows.Select(w => new { name = w.Name, path = "/w/" + w.Token }),
            nodeKeys = (_madeKeys ?? new()).Select(k => new { id = k.Id, secret = k.Secret }),
            bus = _o.Bus,
        });
        _madeKeys = null;
        // After the ready line, so it stays the first line on standard output.
        new Thread(ReadControl) { IsBackground = true, Name = "rig control" }.Start();
        if (_o.ExitAfterSeconds > 0) _ = Task.Delay(TimeSpan.FromSeconds(_o.ExitAfterSeconds)).ContinueWith(_ => stop.TrySetResult(), TaskScheduler.Default);
        await stop.Task;
        await StopAsync();
        return 0;
    }

    private async Task StopAsync()
    {
        _ticker?.Dispose();
        _beacon?.Dispose();
        _stopping.Cancel();   // a stall in progress ends now
        if (_web is not null) await _web.StopAsync();
        if (_companion is not null) await _companion.StopAsync();
        _osc?.Stop();
        // No route is open now, so no key is used for the first time from here on. A restart never leaves a used key free:
        // the owner thread runs what it was handed (each binding's save among it) before the rig ends, and a binding whose
        // save never reached it is saved here, once this thread is the one writer.
        if (!EndOwner())
        {
            Log("stop: the show thread did not end in time; a binding made since its last save is not kept");
            return;
        }
        if (_nodes.Unsaved(_keys))
        {
            _nodes.SaveTo(_keys);
            SaveState(_windows.Windows);
        }
    }

    public void Dispose()
    {
        _ticker?.Dispose();
        _beacon?.Dispose();
        _stopping.Cancel();
        var ended = EndOwner();
        _nodes.Bound -= OnBound;
        Engine.Dispose();
        // The queue and the stall's token belong to the owner thread until it has ended.
        if (!ended) return;
        _queue.Dispose();
        _stopping.Dispose();
    }

    /// <summary>How long a stopping rig waits for the owner thread to run what it was handed (a stall ends at once).</summary>
    private static readonly TimeSpan OwnerEnds = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The owner queue takes nothing more; the owner thread runs what it already holds and ends. True once it has ended (or
    /// never started): from then on the calling thread is the one writer.
    /// </summary>
    private bool EndOwner()
    {
        _queue.CompleteAdding();
        return (_owner.ThreadState & ThreadState.Unstarted) != 0 || _owner.Join(OwnerEnds);
    }

    // ---------------------------------------------------------------- the owner thread
    private void Post(Action a)
    {
        try { _queue.Add(a); }
        catch (InvalidOperationException) { /* stopping: nothing more runs */ }
    }

    private void OwnerLoop()
    {
        foreach (var a in _queue.GetConsumingEnumerable())
        {
            try { a(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Log("owner: " + ex.GetType().Name + ": " + ex.Message); }
        }
    }

    private static void Log(string line) => Console.Error.WriteLine(DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line);

    /// <summary>
    /// One JSON line on standard output, whole and flushed (the ready line and the control reader's answers). Anything
    /// outside ASCII is escaped, so a line reads the same through a pipe in any console code page.
    /// </summary>
    private void Say(object line)
    {
        var json = JsonSerializer.Serialize(line);
        lock (_stdout)
        {
            Console.Out.WriteLine(json);
            Console.Out.Flush();
        }
    }

    private static long UnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// The ticker's turn: at most one tick waits in the owner's queue, so a show thread that stops (a stall) comes back to
    /// one tick, never to a backlog of them.
    /// </summary>
    private void QueueTick()
    {
        if (Interlocked.Exchange(ref _tickQueued, 1) == 1) return;
        Post(() =>
        {
            Volatile.Write(ref _tickQueued, 0);
            Tick();
        });
    }

    // ---------------------------------------------------------------- the control reader (standard input)
    /// <summary>
    /// One line at a time from standard input, each answered in JSON lines (a stall twice); a blank line is ignored, and an
    /// unknown line is refused with the lines there are, never guessed. The end of input ends the reader, never the rig.
    /// </summary>
    private void ReadControl()
    {
        try
        {
            while (Console.In.ReadLine() is { } line)
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (words.Length == 0) continue;
                switch (words[0])
                {
                    case "stall": Stall(words); break;
                    default: Say(new { rig = "refused", line = Clip(words[0]), error = "unknown (stall <ms>)" }); break;
                }
            }
        }
        catch (IOException ex) { Log("control: " + ex.Message); }

        static string Clip(string word) => word.Length > 32 ? word[..32] : word;
    }

    /// <summary>
    /// <c>stall &lt;ms&gt;</c>: the owner thread is blocked for that long, as a show thread that stops turning over is: no tick
    /// marks the show alive, no command runs (a receipt says only accepted), and after <see cref="SnapshotHub.StuckAfter"/>
    /// every link hears the server's own stuck beat instead of a frame. The owner thread itself says "stalled" as it stops and
    /// "running" as it turns over again. One stall at a time; stopping the rig ends one.
    /// </summary>
    private void Stall(string[] words)
    {
        if (words.Length != 2 || !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ms) || ms is < 1 or > MaxStallMs)
        {
            Say(new { rig = "refused", line = "stall", error = $"stall takes a time of 1-{MaxStallMs.ToString(CultureInfo.InvariantCulture)} ms" });
            return;
        }
        if (Interlocked.CompareExchange(ref _stalling, 1, 0) != 0)
        {
            Say(new { rig = "refused", line = "stall", error = "already stalled" });
            return;
        }
        Post(() =>
        {
            Say(new { rig = "stalled", ms, at = UnixMs() });
            Log($"stalled the show thread for {ms.ToString(CultureInfo.InvariantCulture)} ms (control line)");
            _stopping.Token.WaitHandle.WaitOne(ms);
            Volatile.Write(ref _stalling, 0);
            Say(new { rig = "running", at = UnixMs() });
            Log("the show thread runs again");
        });
    }

    public void Execute(ShowCommand command) => Post(() =>
    {
        var (ok, words) = Run(command);
        Log((ok ? "ran " : "refused ") + command.Kind + ": " + words);
    });

    public async Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
    {
        var done = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            var (ok, words) = Run(command);
            Log((ok ? "ran " : "refused ") + command.Kind + ": " + words);
            done.TrySetResult(ok ? new CommandReceipt(id, true, "applied", words, null, null) : CommandReceipt.Refused(id, words));
        });
        var first = await Task.WhenAny(done.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return first == done.Task ? await done.Task.ConfigureAwait(false) : CommandReceipt.Accepted(id);
    }

    /// <summary>Owner thread: one command, and what it did (or why not).</summary>
    private (bool Ok, string Words) Run(ShowCommand c)
    {
        (bool Ok, string Words) result = c.Kind switch
        {
            ShowCommandKind.Start => Did(Engine.Start, "started"),
            ShowCommandKind.Pause => Did(Engine.Pause, "paused"),
            ShowCommandKind.Toggle => Did(Engine.Toggle, "toggled"),
            ShowCommandKind.Reset => Did(Engine.Reset, "reset"),
            ShowCommandKind.Nudge => Did(() => Engine.Nudge(TimeSpan.FromSeconds(c.Number)), $"nudged {c.Number.ToString(CultureInfo.InvariantCulture)} s"),
            ShowCommandKind.SetDuration => Did(() => Engine.SetDuration(TimeSpan.FromSeconds(c.Number)), "duration set"),
            ShowCommandKind.Message => Did(() => SetMessage(c.Text ?? "", c.Urgency, c.Flash), "message on"),
            ShowCommandKind.ClearMessage => Did(() => SetMessage("", MessageUrgency.Info, FlashRate.Off), "message cleared"),
            ShowCommandKind.Blackout => Did(() => _blackout = c.Number < 0 ? !_blackout : c.Number > 0, "blackout"),
            ShowCommandKind.Next => Item(_item + 1),
            ShowCommandKind.Previous => Item(_item - 1),
            ShowCommandKind.GoTo => Item((int)c.Number - 1),
            ShowCommandKind.Holding => HoldingImage(c.Text ?? "toggle"),
            ShowCommandKind.Note => (true, "noted"),
            ShowCommandKind.Window => WindowCommand(c.Get<WindowArgs>()),
            ShowCommandKind.Screen => ScreenDirector.Run(c.Get<ScreenArgs>(), _web, PeerName, RenameScreen, SaveScreenPreset, ResolveScreenTarget),
            _ => (false, $"the rig doesn't simulate {c.Kind.ToString().ToLowerInvariant()} (the app does)"),
        };
        if (result.Ok) { CommandRecorded?.Invoke(c.Kind.ToString().ToLowerInvariant()); Tick(); }
        return result;

        static (bool, string) Did(Action a, string words) { a(); return (true, words); }
    }

    private (bool, string) Item(int index)
    {
        if (index < 0 || index >= Agenda.Length) return (false, "no such item in the rig's running order");
        _item = index;
        var running = Engine.State == TimerState.Running;
        Engine.SetDuration(TimeSpan.FromSeconds(Agenda[index].Seconds));
        if (running) Engine.Start();
        return (true, $"item {index + 1}: {Agenda[index].Title}");
    }

    private (bool, string) HoldingImage(string key)
    {
        var image = _holding.Choose(Settings.Holding.Images, key, out var error);
        if (error is not null) return (false, "Holding: " + error);
        return _holding.Set(image) ? (true, image is null ? "holding image off" : "holding " + image.Name) : (false, "Holding: nothing changes");
    }

    private (bool, string) WindowCommand(WindowArgs a)
    {
        var change = _windows.Run(a, ScreenPresetPath, Settings.Holding.Images);
        return (change.Ok, change.Words);
    }

    /// <summary>Owner thread, 4 Hz and after every command: the frame (published only when something visible changed) and,
    /// once a second, the context. Alive is marked before the frame is published, so a link the frame wakes never reads
    /// the show as stuck.</summary>
    private int _tick;
    private void Tick()
    {
        Snapshots.MarkAlive();
        Snapshots.Publish(Frame());
        if (++_tick % 4 == 0 || _context is null) _context = Context();
    }

    private ShowSnapshot Frame()
    {
        var now = DateTime.UtcNow;
        var tick = new TimerTick(Engine.Mode, Engine.State, Engine.Current, now, null, Engine.BaseDuration);
        var secs = tick.DisplaySeconds;
        var phase = Engine.Mode != TimerMode.Countdown || Engine.State == TimerState.Idle ? TimerPhase.Normal
            : secs < 0 ? TimerPhase.Overtime : secs <= 60 ? TimerPhase.WrapUp : secs <= 300 ? TimerPhase.Warning : TimerPhase.Normal;
        var color = phase switch { TimerPhase.Overtime => "#DC2626", TimerPhase.WrapUp => "#F97316", TimerPhase.Warning => "#F59E0B", _ => "#FFFFFF" };
        var local = now;
        var clock = (long)local.TimeOfDay.TotalSeconds;
        // The plan starts when the rig did; each item's times are relative to now, as the app sends them.
        var startIn = (long)(_startedUtc - now).TotalSeconds;
        var timings = new List<ItemTimingView>();
        var at = startIn;
        for (var i = 0; i < Agenda.Length; i++)
        {
            var (title, speaker, kind, seconds, group) = Agenda[i];
            var status = i < _item ? ItemStatus.Done : i == _item ? ItemStatus.Live : ItemStatus.Upcoming;
            var planned = local.AddSeconds(at).ToString("HH:mm", CultureInfo.InvariantCulture);
            var end = local.AddSeconds(at + seconds).ToString("HH:mm", CultureInfo.InvariantCulture);
            timings.Add(new ItemTimingView(i, title, speaker, kind, seconds, 0, planned, planned, end, "", "", status, 0, false, false, "", group, at, at + seconds, at));
            at += seconds;
        }
        var image = Settings.Holding.Images.FirstOrDefault(h => h.Id == _holding.OnAirId);
        return new ShowSnapshot
        {
            Mode = Engine.Mode, State = Engine.State, Seconds = secs, Text = tick.Format(), SpeakerText = tick.Format(), TotalSeconds = (long)Engine.BaseDuration.TotalSeconds,
            Phase = phase, ColorHex = color, Title = Agenda[_item].Title + (Agenda[_item].Speaker.Length > 0 ? " — " + Agenda[_item].Speaker : ""),
            NextTitle = _item + 1 < Agenda.Length ? Agenda[_item + 1].Title : "", ItemIndex = _item, ItemCount = Agenda.Length,
            TimeOfDay = local.ToString("HH:mm:ss", CultureInfo.InvariantCulture), ClockSeconds = clock, UtcSeconds = new DateTimeOffset(now).ToUnixTimeSeconds(),
            DateText = local.ToString("dddd d MMMM", CultureInfo.InvariantCulture), Room = _o.Room,
            Message = _message, Urgency = _urgency, Flash = _flash, Blackout = _blackout,
            HoldingId = image?.Id ?? "", HoldingName = image?.Name ?? "",
            Elements = DisplayElements.Timer | DisplayElements.Message | DisplayElements.NextUp | DisplayElements.ProgressBar | DisplayElements.TimeOfDay,
            Timings = timings.ToEquatableArray(),
        };
    }

    /// <summary>
    /// Owner thread: the context, with the show network joined as the app joins it (<see cref="ShowNetwork.Summaries"/>): the
    /// nodes it hears on the Show Bus (with --bus), the contexts show apps posted and their node links on /ws, by node id; and,
    /// as the app, its own amber <c>bus:hello</c> while its hello cannot go out.
    /// </summary>
    private string Context()
    {
        var now = DateTime.UtcNow;
        var links = ScreenWindows.ContextItems(_windows.Windows, _web?.WindowStatus() ?? Array.Empty<WindowObserved>()).ToList();
        var hello = _beacon?.Status ?? HelloStatus.NotYet;
        var twin = _beacon?.OwnConflict;
        if (ShowBusHello.Item(hello, twin) is { } cannot) links.Add(cannot);
        var line = ShowBusHello.Line(hello, _o.Room, _nodeId, twin);
        if (hello.Said != _helloSaid || (twin is not null) != _helloTwin)
        {
            _helloSaid = hello.Said;
            _helloTwin = twin is not null;
            Log("hello: " + line);
        }
        var heard = (_beacon?.Peers() ?? Array.Empty<SlidesBeacon.Peer>())
            .Select(p => new HeardNode(p.Hello.Kind, p.Hello.Id, p.Hello.Name, p.Address, p.AgeSeconds) { Conflict = p.Conflict?.Words ?? "" });
        var view = ShowNetwork.Summaries(heard, _peerContexts.Held(now), _web?.ListNodes() ?? Array.Empty<NodeLinkFact>(), _nodeId, now);
        foreach (var c in view.Conflicts.Where(c => !_conflictsLogged.Contains(c.Id))) Log($"hello: {c.Conflict} — its hello adds no peers row while the mark stands");
        _conflictsLogged = view.Conflicts.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        _network = view;
        return ShowContext.Build(new ContextInput(_nodeId, _o.Room, now, Snapshots.Latest, Snapshots.ProducerStuck, links,
            view.Rows.Select(r => r.Peer).ToList(), Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 0));
    }

    private HashSet<string> _conflictsLogged = new(StringComparer.Ordinal);   // owner thread: the ids in conflict it logged

    /// <summary>
    /// The beacon's timer thread, every beat: the app's hello over the rig's own servers and --bind (<see cref="ShowBusHello.Main"/>):
    /// none at all while --bind is local, and only the doors other machines can open.
    /// </summary>
    private (SlidesBeacon.Hello? Hello, HelloScope Scope) SayHello()
    {
        var web = _web;
        var doors = ShowBusHello.Doors(web?.Door, web?.Fingerprint, _companion?.Door, _osc?.Door);
        return ShowBusHello.Main(_nodeId, _inst, _o.Room, Environment.MachineName, doors, McpAllowed, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>A show app's name as it told this rig (its context), or null: what a screen command naming it is refused with.</summary>
    private string? PeerName(string id) => _network.Rows.FirstOrDefault(r => r.Peer.Id == id && r.Named)?.Peer.Name;

    // ---------------------------------------------------------------- what the servers read (any thread)
    public RemoteConfig GetRemoteConfig() => new(
        new[] { (1, "Keynote", 1800), (2, "Panel", 2700) },
        new[] { ("Wrap", "Please wrap up", MessageUrgency.Warning, FlashRate.Slow) },
        Agenda.Select(a => (a.Title, (string?)a.Speaker, a.Seconds)).ToArray(),
        Array.Empty<(string, string)>(), new[] { "Lighting", "Sound" }, _o.Room, "");
    public IReadOnlyList<ScreenWindow> Windows => _windows.Windows;
    public string? ViewCode => _o.ViewCode.Length == 0 ? null : _o.ViewCode;
    public bool McpAllowed => true;
    public string? ContextJson() => _context;
    public string? OwnNodeId => _nodeId;
    public IReadOnlyList<ApiKey> ApiKeys => _apiKeys;
    public NodeBindings Nodes => _nodes;

    /// <summary>A show app's context, taken only from a credential that may speak for its node, and held with that proof.</summary>
    public (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
        => _peerContexts.Accept(json, address, DateTime.UtcNow, _nodeId, credential);

    public string? ScreenName(string screenId) => _screenNames.TryGetValue(screenId, out var n) ? n : null;
    public string? ScreenPresetPath(string name)
        => _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } hit ? hit.Path : null;
    public IReadOnlyList<(string Name, string Path)> ScreenPresets => _presets;
    public string? LayoutJson(string name) => name is "lobby" or "foyer" ? ScreenLayouts.ToJson(ScreenLayouts.Starter(name)) : null;
    public IReadOnlyList<string> LayoutNames => new[] { "foyer", "lobby" };
    public (byte[] Data, string ContentType)? HoldingImageData(string id) => id == Sponsors.Id ? (SponsorsPng, "image/png") : null;

    // ---------------------------------------------------------------- Companion's direct calls
    public void LoadPreset(int slot, bool autoStart) => Post(() => { Item(Math.Clamp(slot - 1, 0, Agenda.Length - 1)); if (autoStart) Engine.Start(); Tick(); });
    public void SetMessage(string text, MessageUrgency urgency, FlashRate flash)
    {
        if (Thread.CurrentThread != _owner) { Post(() => SetMessage(text, urgency, flash)); return; }
        _message = text; _urgency = urgency; _flash = flash;
        MessageChanged?.Invoke(new MessageState { Text = text, Urgency = urgency, Flash = flash });
        Tick();
    }
    public void SetFlash(FlashRate rate) => Post(() => { _flash = rate; Tick(); });
    public void SetTimerFlash(bool flashTimer) => Post(Tick);

    // ---------------------------------------------------------------- screens (owner thread)
    /// <summary>As the app names a screen: trimmed, at most 40 characters, and an empty name takes the name away.</summary>
    private void RenameScreen(string id, string name)
    {
        name = name.Trim();
        if (!Screens.ValidId(id)) return;
        var names = new Dictionary<string, string>(_screenNames, StringComparer.OrdinalIgnoreCase);
        if (name.Length == 0) names.Remove(id); else names[id] = name.Length > 40 ? name[..40] : name;
        _screenNames = names;
    }

    private void SaveScreenPreset(string name, string path)
    {
        if (!ScreenPreset.ValidName(name) || !ScreenPreset.ValidPath(path)) return;
        _presets = _presets.Where(p => !p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Append((name, path)).ToList();
    }

    /// <summary>A saved setup's name or a page → the page to open, or null.</summary>
    private string? ResolveScreenTarget(string target) => ScreenPreset.ValidPath(target) ? target : ScreenPresetPath(target);

    // ---------------------------------------------------------------- node keys
    /// <summary>Make node keys until there are <paramref name="count"/>: their secrets go out once, in the ready line.</summary>
    private void MakeNodeKeys(int count)
    {
        _madeKeys = new();
        for (var i = _keys.Count; i < count; i++)
        {
            var (key, secret) = Stagewatch.Core.ApiKeys.Create($"Rig node {(i + 1).ToString(CultureInfo.InvariantCulture)}", ApiKeyScope.Node, DateTime.UtcNow);
            _keys.Add(key);
            _madeKeys.Add((key.Id, secret));
        }
    }

    /// <summary>The keys copied whole for other threads: the servers never read an object the owner thread writes.</summary>
    private static IReadOnlyList<ApiKey> CopyKeys(IEnumerable<ApiKey> keys)
        => keys.Select(k => new ApiKey { Id = k.Id, Name = k.Name, Scope = k.Scope, Hash = k.Hash, Hint = k.Hint, CreatedUtc = k.CreatedUtc, Node = k.Node, NodeKind = k.NodeKind }).ToArray();

    /// <summary>
    /// A key came to speak for a node, on whichever thread it happened: the owner thread logs it (names and ids, never a
    /// secret) and saves what the bindings hold, so a restarted rig keeps the pair.
    /// </summary>
    private void OnBound(NodeBound bound) => Post(() =>
    {
        Log(KeyBindingWords.JournalLine(bound, id => _apiKeys.FirstOrDefault(k => k.Id == id)?.Name));
        if (!_nodes.Unsaved(_keys)) return;
        _nodes.SaveTo(_keys);
        _apiKeys = CopyKeys(_keys);
        SaveState(_windows.Windows);
    });

    // ---------------------------------------------------------------- what --state keeps between runs
    /// <summary>The windows, and the node keys with the node each speaks for: hashes, ids and names, never a secret.</summary>
    private sealed class RigState
    {
        [JsonPropertyName("windows")] public List<ScreenWindow> Windows { get; set; } = new();
        [JsonPropertyName("keys")] public List<ApiKey> Keys { get; set; } = new();
    }

    /// <summary>The state as saved; a list alone is windows (a state file from before the rig kept keys).</summary>
    private RigState LoadState()
    {
        if (_o.StateFile is not { } path || !File.Exists(path)) return new RigState();
        try
        {
            var text = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(text);
            var state = doc.RootElement.ValueKind == JsonValueKind.Array
                ? new RigState { Windows = JsonSerializer.Deserialize<List<ScreenWindow>>(text) ?? new() }
                : JsonSerializer.Deserialize<RigState>(text) ?? new RigState();
            state.Windows ??= new();
            state.Keys = Stagewatch.Core.ApiKeys.Sanitise(state.Keys).Where(k => k.Scope == ApiKeyScope.Node).ToList();
            return state;
        }
        catch (JsonException ex) { Log("state: " + ex.Message + " — starting with no windows and no keys"); return new RigState(); }
    }

    private void SaveWindows(IReadOnlyList<ScreenWindow> windows) => SaveState(windows);

    /// <summary>Owner thread (or the constructor, before it starts): the whole state, written atomically.</summary>
    private void SaveState(IReadOnlyList<ScreenWindow> windows)
    {
        if (_o.StateFile is not { } path) return;
        try { AtomicFile.Write(path, JsonSerializer.Serialize(new RigState { Windows = windows.ToList(), Keys = _keys })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("state: " + ex.Message); }
    }
}
