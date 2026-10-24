using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Stagewatch.Companion;
using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Show;

// The headless rig: the real web server, Companion server and OSC server around a simulated show, on any OS.
//   dotnet run --project tools/Stagewatch.Rig -- [--port 19940] [--companion 19941] [--osc 19942] [--pin 4321]
//       [--bind local|all] [--state rig-windows.json] [--view CODE] [--room "Rig Hall"] [--exit-after SECONDS]
// The first line on standard output is one JSON object: {"rig":"ready","http":…,"companion":…,"osc":…,"pin":…,"windows":[…]}.
// The rig simulates the timer, messages, blackout, holding images, the running order's Next/Previous/GoTo and windows;
// any other command is refused with words (in its receipt and its log), never pretended.
var options = RigOptions.Parse(args);
if (options is null) return 2;
using var rig = new Rig(options);
return await rig.RunAsync();

sealed record RigOptions(int Http, int Companion, int Osc, string Pin, bool BindAll, string? StateFile, string ViewCode, string Room, int ExitAfterSeconds)
{
    public static RigOptions? Parse(string[] args)
    {
        var o = new RigOptions(19940, 19941, 19942, "4321", false, null, "", "Rig Hall", 0);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            int Port() => int.Parse(Next(), CultureInfo.InvariantCulture) is var p and >= 0 and <= 65535 ? p : throw new ArgumentException("ports are 0-65535");
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
                    _ => throw new ArgumentException($"unknown option {args[i]}"),
                };
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
            {
                Console.Error.WriteLine("Stagewatch.Rig: " + ex.Message);
                return null;
            }
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
    private CompanionServer? _companion;
    private OscServer? _osc;
    private int _item;
    private string _message = "";
    private MessageUrgency _urgency;
    private FlashRate _flash;
    private bool _blackout;
    private readonly HoldingSelection _holding = new();
    private volatile string? _context;

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
        _windows = new WindowDirector(LoadWindows(), SaveWindows);
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
        _ticker = new Timer(_ => Post(Tick), null, 0, 250);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            rig = "ready", http = _o.Http, companion = _o.Companion, osc = _o.Osc, pin = _o.Pin, node = _nodeId,
            windows = _windows.Windows.Select(w => new { name = w.Name, path = "/w/" + w.Token }),
        }));
        Console.Out.Flush();
        if (_o.ExitAfterSeconds > 0) _ = Task.Delay(TimeSpan.FromSeconds(_o.ExitAfterSeconds)).ContinueWith(_ => stop.TrySetResult(), TaskScheduler.Default);
        await stop.Task;
        await StopAsync();
        return 0;
    }

    private async Task StopAsync()
    {
        _ticker?.Dispose();
        if (_web is not null) await _web.StopAsync();
        if (_companion is not null) await _companion.StopAsync();
        _osc?.Stop();
        _queue.CompleteAdding();
    }

    public void Dispose()
    {
        _ticker?.Dispose();
        Engine.Dispose();
        _queue.Dispose();
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
        var change = _windows.Run(a, name => name.Equals("lobby", StringComparison.OrdinalIgnoreCase) ? "/audience?holding=0" : null, Settings.Holding.Images);
        return (change.Ok, change.Words);
    }

    /// <summary>Owner thread, 4 Hz and after every command: the frame (published only when something visible changed) and,
    /// once a second, the context.</summary>
    private int _tick;
    private void Tick()
    {
        Snapshots.Publish(Frame());
        Snapshots.MarkAlive();
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

    private string Context()
    {
        var links = ScreenWindows.ContextItems(_windows.Windows, _web?.WindowStatus() ?? Array.Empty<WindowObserved>()).ToList();
        return ShowContext.Build(new ContextInput(_nodeId, _o.Room, DateTime.UtcNow, Snapshots.Latest, Snapshots.ProducerStuck, links,
            Array.Empty<PeerSummary>(), Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 0));
    }

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
    public string? ScreenPresetPath(string name) => name.Equals("lobby", StringComparison.OrdinalIgnoreCase) ? "/audience?holding=0" : null;
    public IReadOnlyList<(string Name, string Path)> ScreenPresets => new[] { ("lobby", "/audience?holding=0") };
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

    // ---------------------------------------------------------------- windows kept between runs (--state)
    private IReadOnlyList<ScreenWindow> LoadWindows()
    {
        if (_o.StateFile is not { } path || !File.Exists(path)) return Array.Empty<ScreenWindow>();
        try { return JsonSerializer.Deserialize<List<ScreenWindow>>(File.ReadAllText(path)) ?? new List<ScreenWindow>(); }
        catch (JsonException ex) { Log("state: " + ex.Message + " — starting with no windows"); return Array.Empty<ScreenWindow>(); }
    }

    private void SaveWindows(IReadOnlyList<ScreenWindow> windows)
    {
        if (_o.StateFile is not { } path) return;
        try { AtomicFile.Write(path, JsonSerializer.Serialize(windows)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log("state: " + ex.Message); }
    }
}
