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
//   dotnet run --project tools/Stagewatch.Rig -- [--port 19940] [--companion 19941] [--osc 19942] [--pin 4321 | --pin-file FILE]
//       [--bind local|all] [--state rig-windows.json] [--view CODE] [--room "Rig Hall"] [--node-keys N] [--exit-after SECONDS]
//       [--bus PORT [--bus-to ADDRESS:PORT]] [--node-id ID] [--journal FILE|-] [--secrets-out FILE] [--exit-on-eof]
// The first line on standard output is one JSON object: {"rig":"ready","http":…,"companion":…,"osc":…,"pin":…,"node":…,
// "windows":[…],"nodeKeys":[…],"bus":…,"bind":"local|all","journal":true|false,"secrets":"stdout|file"}; nodeKeys lists only the
// node keys made in this run (--node-keys N makes them on a first start; --state keeps their hashes and the node each speaks for,
// never a secret).
// Secrets: "pin" is "none" for no PIN, "set" when it came from --pin-file or --secrets-out is given, and the PIN itself only
// when it came from --pin (or is the default) with no --secrets-out. With --secrets-out FILE the rig writes every secret it would
// print to FILE (owner-only on Linux and macOS, written whole before the ready line): {"pin":"…","windows":[{"name","path"}],
// "nodeKeys":[{"id","secret"}]}, and the ready line names windows and keys only ({"name"}, {"id"}): nothing secret reaches
// standard output or standard error. The file holds this start's secrets, as the ready line would: each start rewrites it, and
// a key made on an earlier start is not in it (its secret went out once, on that start), so a reader reads it after each start.
// --pin-file FILE reads the PIN from FILE's first line (an empty line is no PIN); never with --pin. Without --secrets-out,
// windows carry their "path" (/w/<token>) and keys their "secret", as before.
// --node-id ID (6-40 letters, digits, - or _) is the id it says in its hello, context and node links, the same on every start;
// without it each start draws "rig-" and a random id.
// --journal FILE keeps the show journal as the app does (JSON lines, appended; a restart reads the file back and goes on), "-"
// keeps it in memory: item starts and ends, phase changes, blackout, messages, notes, window changes and key bindings. A FILE
// the rig cannot open for appending is refused (exit 2) before any ready line says it keeps a journal. /report and
// /api/report.csv answer from it and the rig's running order; the show thread builds the report, so while it is stalled (or
// with no --journal) both answer 404 "no report", as the app's do. The context's recent lines are its rows; without it, none.
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
//   auth         the lockout tables as they stand (WebServer.AuthFailures, ViewPassFailures), no secret in them →
//                {"rig":"auth","at":<unix ms>,"addresses":[{"address","failures","windowAgeMs"}],"viewPasses":[…the same]}:
//                each device whose one-minute window is still open, the PIN's (wrong PINs) and the view passes' (wrong window
//                addresses, viewing codes and crew links)
//   exit         stops the rig as Ctrl+C does → {"rig":"exiting"}
//   anything else is refused, never guessed → {"rig":"refused","line":"<first word>","error":"…"}
// The end of standard input ends the reader; with --exit-on-eof it also stops the rig as Ctrl+C does (so does input that can no
// longer be read), and never otherwise.
// Exit codes: 0 stopped (Ctrl+C, the process ending, --exit-after, exit, --exit-on-eof); 2 an option it cannot take, said on
// standard error; 3 a server could not listen; 4 --secrets-out could not be written. On 3 and 4 no secret went out, so the
// node keys made in that start are taken out of --state again and the next start makes them afresh.
var options = RigOptions.Parse(args);
if (options is null) return 2;
using var rig = new Rig(options);
return await rig.RunAsync();

/// <summary>Where the rig's PIN came from: what decides whether the ready line may say it.</summary>
enum PinSource { Default, Flag, File }

sealed record RigOptions(int Http, int Companion, int Osc, string Pin, bool BindAll, string? StateFile, string ViewCode, string Room, int ExitAfterSeconds, int NodeKeys,
    int Bus = 0, IPEndPoint? BusTo = null)
{
    /// <summary>The longest first line --pin-file reads as a PIN.</summary>
    public const int PinFileMax = 256;

    /// <summary>--node-id: the id it says in its hello, context and node links on every start; null draws "rig-" and a random id.</summary>
    public string? NodeId { get; init; }

    /// <summary>--journal: the journal's file, <see cref="InMemory"/> for one kept in memory only, or null for none.</summary>
    public string? Journal { get; init; }

    /// <summary>The --journal word for a journal kept in memory.</summary>
    public const string InMemory = "-";

    /// <summary>--pin, --pin-file or neither (the default PIN).</summary>
    public PinSource PinFrom { get; init; }

    /// <summary>--pin-file: the file whose first line is the PIN, or null.</summary>
    public string? PinFile { get; init; }

    /// <summary>--secrets-out: the file every secret goes to instead of standard output, or null.</summary>
    public string? SecretsOut { get; init; }

    /// <summary>--exit-on-eof: standard input reaching its end stops the rig as Ctrl+C does.</summary>
    public bool ExitOnEof { get; init; }

    /// <summary>Whether the ready line may carry the PIN itself: only one given in argv (or the default), never with --secrets-out.</summary>
    public bool PinOnStdout => PinFrom != PinSource.File && SecretsOut is null;

    public static RigOptions? Parse(string[] args)
    {
        var o = new RigOptions(19940, 19941, 19942, "4321", false, null, "", "Rig Hall", 0, 0);
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            int Port() => int.Parse(Next(), CultureInfo.InvariantCulture) is var p and >= 0 and <= 65535 ? p : throw new ArgumentException("ports are 0-65535");
            int Keys() => int.Parse(Next(), CultureInfo.InvariantCulture) is var n and >= 0 and <= ApiKeys.Max ? n : throw new ArgumentException($"--node-keys is 0-{ApiKeys.Max}");
            string File(string option) => Next() is { Length: > 0 } f ? f : throw new ArgumentException($"{option} needs a file");
            string Id(string id) => ShowBusHello.ValidId(id) ? id : throw new ArgumentException($"--node-id is {ShowBusHello.IdMin}-{SlidesBeacon.IdChars} letters, digits, - or _");
            try
            {
                o = args[i] switch
                {
                    "--port" => o with { Http = Port() },
                    "--companion" => o with { Companion = Port() },
                    "--osc" => o with { Osc = Port() },
                    "--pin" => o.PinFrom == PinSource.Default ? o with { Pin = Next(), PinFrom = PinSource.Flag } : throw new ArgumentException("--pin once, and never with --pin-file"),
                    "--pin-file" => o.PinFrom == PinSource.Default ? o with { PinFrom = PinSource.File, PinFile = File("--pin-file") } : throw new ArgumentException("--pin-file once, and never with --pin"),
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
                    "--node-id" => o.NodeId is null ? o with { NodeId = Id(Next()) } : throw new ArgumentException("--node-id once"),
                    "--journal" => o.Journal is null ? o with { Journal = File("--journal") } : throw new ArgumentException("--journal once"),
                    "--secrets-out" => o.SecretsOut is null ? o with { SecretsOut = File("--secrets-out") } : throw new ArgumentException("--secrets-out once"),
                    "--exit-on-eof" => o with { ExitOnEof = true },
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
        if (o.PinFile is { } pinFile)
        {
            if (ReadPin(pinFile, out var error) is not { } pin)
            {
                Console.Error.WriteLine("Stagewatch.Rig: " + error);
                return null;
            }
            o = o with { Pin = pin };
        }
        if (o.Journal is { } journal && journal != InMemory && CheckJournal(journal) is { } cannot)
        {
            Console.Error.WriteLine("Stagewatch.Rig: " + cannot);
            return null;
        }
        return o;
    }

    /// <summary>
    /// --journal FILE: opened for appending once, its folder made first, as the journal's own writer opens it, so a file the rig
    /// could never write is refused before a ready line says it keeps a journal. Null when it opens; else words naming the file.
    /// </summary>
    private static string? CheckJournal(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.ReadWrite).Dispose();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return "--journal: couldn't open it for appending: " + ex.Message;
        }
    }

    /// <summary>
    /// The PIN from a file's first line, its line end taken off (an empty line is no PIN), read no further than
    /// <see cref="PinFileMax"/> characters; null with words when it cannot be read or the line is longer. The words never
    /// carry what the file holds.
    /// </summary>
    private static string? ReadPin(string path, out string error)
    {
        error = "";
        try
        {
            using var reader = new StreamReader(path, new System.Text.UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            var buffer = new char[PinFileMax + 2];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            var text = new string(buffer, 0, read);
            var line = text.IndexOf('\n') is var end and >= 0 ? text[..end] : text;
            if (line.EndsWith('\r')) line = line[..^1];
            if (line.Length <= PinFileMax) return line;
            error = $"--pin-file: the first line is over {PinFileMax.ToString(CultureInfo.InvariantCulture)} characters";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = "--pin-file: " + ex.Message;
        }
        return null;
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
    /// <summary>The id it says in its hello, context and node links: --node-id's on every start, or drawn for this start alone.</summary>
    private readonly string _nodeId;
    /// <summary>
    /// The show journal (--journal), or null: the owner thread is its one writer and its one reader (the context, the report),
    /// as the app's show thread is; its file is appended to off the owner thread by the journal's own writer.
    /// </summary>
    private readonly ShowJournal? _journal;
    /// <summary>Set once to stop the rig, as Ctrl+C does: by Ctrl+C, the process ending, --exit-after, <c>exit</c> or --exit-on-eof.</summary>
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        _nodeId = o.NodeId ?? "rig-" + ScreenWindows.NewId()[1..];
        if (o.Journal is { } journal)
        {
            // A file is appended to, and read back first: a restarted rig goes on with the show's journal, as the app does. It is
            // read before the journal's writer opens it: on Windows that writer's handle (shared for reading only) refuses a
            // reader that shares only reading, and the earlier rows would be lost to the report and the context.
            var path = journal == RigOptions.InMemory ? null : Path.GetFullPath(journal);
            var earlier = path is not null && File.Exists(path) ? ShowJournal.Load(path) : null;
            _journal = new ShowJournal(path);
            if (earlier is not null) _journal.Seed(earlier);
        }
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
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _stop.TrySetResult(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _stop.TrySetResult();
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
            ForgetMadeKeys(_madeKeys ?? new(), EndOwner());
            return 3;
        }
        if (_o.Bus > 0) _beacon = new SlidesBeacon(SayHello, _o.Bus, _o.BusTo is { } to ? [to] : null);
        _ticker = new Timer(_ => QueueTick(), null, 0, 250);
        // The windows as they stand at the ready line: the constructor's, before any command can change them.
        var windows = _windows.Windows;
        var keys = _madeKeys ?? new();
        _madeKeys = null;
        // The secrets go to their file whole before the ready line, so a reader of the ready line finds them there.
        if (_o.SecretsOut is { } secretsOut && !WriteSecrets(secretsOut, windows, keys, _o.Pin))
        {
            await StopAsync();
            ForgetMadeKeys(keys, EndOwner());
            return 4;
        }
        Say(Ready(windows, keys));
        // After the ready line, so it stays the first line on standard output.
        new Thread(ReadControl) { IsBackground = true, Name = "rig control" }.Start();
        if (_o.ExitAfterSeconds > 0) _ = Task.Delay(TimeSpan.FromSeconds(_o.ExitAfterSeconds)).ContinueWith(_ => _stop.TrySetResult(), TaskScheduler.Default);
        await _stop.Task;
        await StopAsync();
        return 0;
    }

    /// <summary>
    /// The ready line: ports, the node id, the windows and the keys made in this run, and its fields appended after
    /// <c>bus</c>. A secret is in it only without --secrets-out: the PIN only when it came from argv (or is the default),
    /// a window's path and a key's secret only when no file takes them.
    /// </summary>
    private object Ready(IReadOnlyList<ScreenWindow> windows, List<(string Id, string Secret)> keys)
    {
        var file = _o.SecretsOut is not null;
        return new
        {
            rig = "ready", http = _o.Http, companion = _o.Companion, osc = _o.Osc,
            pin = _o.Pin.Length == 0 ? "none" : _o.PinOnStdout ? _o.Pin : "set",
            node = _nodeId,
            windows = windows.Select(w => file ? (object)new { name = w.Name } : new { name = w.Name, path = "/w/" + w.Token }),
            nodeKeys = keys.Select(k => file ? (object)new { id = k.Id } : new { id = k.Id, secret = k.Secret }),
            bus = _o.Bus,
            bind = _o.BindAll ? "all" : "local",
            journal = _journal is not null,
            secrets = file ? "file" : "stdout",
        };
    }

    /// <summary>
    /// --secrets-out: every secret the ready line would carry — the PIN, each window's path, each key made in this run with its
    /// secret — written whole (a file of its own, then moved over the old one), readable by this user alone on Linux and macOS
    /// (on Windows the folder's own permissions hold it). False with words on standard error, naming the file, never a secret.
    /// </summary>
    private static bool WriteSecrets(string path, IReadOnlyList<ScreenWindow> windows, List<(string Id, string Secret)> keys, string pin)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            pin,
            windows = windows.Select(w => new { name = w.Name, path = "/w/" + w.Token }),
            nodeKeys = keys.Select(k => new { id = k.Id, secret = k.Secret }),
        });
        var full = Path.GetFullPath(path);
        var tmp = full + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) create.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.Delete(tmp);
            using (var fs = new FileStream(tmp, create))
            {
                fs.Write(json);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, full, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Stagewatch.Rig: couldn't write --secrets-out {full}: {ex.Message}");
            try { File.Delete(tmp); } catch (Exception gone) when (gone is IOException or UnauthorizedAccessException) { Log("secrets: " + gone.Message); }
            return false;
        }
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
            Log("stop: the show thread did not end in time; a binding made since its last save is not kept");
        else if (_nodes.Unsaved(_keys))
        {
            _nodes.SaveTo(_keys);
            SaveState(_windows.Windows);
        }
        // The journal's last rows reach its file before the rig ends, within the journal's own bounded wait.
        if (_journal is not null) await _journal.DisposeAsync();
    }

    public void Dispose()
    {
        _ticker?.Dispose();
        _beacon?.Dispose();
        _stopping.Cancel();
        var ended = EndOwner();
        _nodes.Bound -= OnBound;
        Engine.Dispose();
        // A rig that never ran its stop (it could not listen) still writes what its journal holds, within a bounded wait.
        if (_journal is not null && !_journal.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3))) Log("journal: its last rows did not reach the file in time");
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
    /// <summary>The words a control line that is none of the reader's is refused with: the lines there are.</summary>
    public const string UnknownControl = "unknown (stall <ms>, auth, exit)";

    /// <summary>
    /// One line at a time from standard input, each answered in JSON lines (a stall twice); a blank line is ignored, and an
    /// unknown line, or a known one with words it does not take, is refused with the lines there are, never guessed. <c>exit</c>
    /// stops the rig and ends the reader. The end of input (or input that can no longer be read) ends the reader, and with
    /// --exit-on-eof stops the rig as Ctrl+C does.
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
                    case "auth" when words.Length == 1: Say(Lockout()); break;
                    case "exit" when words.Length == 1:
                        Say(new { rig = "exiting" });
                        Log("exit: stopping (control line)");
                        _stop.TrySetResult();
                        return;
                    case "auth" or "exit": Say(new { rig = "refused", line = words[0], error = words[0] + " takes no words" }); break;
                    default: Say(new { rig = "refused", line = Clip(words[0]), error = UnknownControl }); break;
                }
            }
            Log("control: standard input ended");
        }
        catch (IOException ex) { Log("control: standard input could not be read: " + ex.Message); }
        // Input that ended and input that can no longer be read are both its end: --exit-on-eof stops the rig on either.
        if (_o.ExitOnEof)
        {
            Log("control: stopping (--exit-on-eof)");
            _stop.TrySetResult();
        }

        static string Clip(string word) => word.Length > 32 ? word[..32] : word;
    }

    /// <summary>
    /// <c>auth</c>: the web server's lockout tables as they stand, read off the show thread (a stalled show still answers): each
    /// device whose one-minute window is open, with its count and the window's age — wrong PINs (<c>addresses</c>) and wrong view
    /// passes (<c>viewPasses</c>). Addresses and counts only, never what was guessed.
    /// </summary>
    private object Lockout()
    {
        static IEnumerable<object> Rows(IReadOnlyList<AuthFailureCount> table)
            => table.Select(f => new { address = f.Address, failures = f.Failures, windowAgeMs = f.WindowAgeMs });
        var web = _web;
        return new
        {
            rig = "auth",
            at = UnixMs(),
            addresses = Rows(web?.AuthFailures() ?? Array.Empty<AuthFailureCount>()),
            viewPasses = Rows(web?.ViewPassFailures() ?? Array.Empty<AuthFailureCount>()),
        };
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
            ShowCommandKind.Note => Did(() => Journal(JournalKind.Note, _item, c.Text ?? ""), "noted"),
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
        // Leaving an item that started ends it in the journal, with how long it ran; the next starts when its timer counts.
        if (index != _item) EndItem(DateTime.UtcNow);
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
        if (change.Effect == WindowEffect.Changed) Journal(JournalKind.Note, -1, "Window: " + change.Words);
        return (change.Ok, change.Words);
    }

    /// <summary>Owner thread, 4 Hz and after every command: the frame (published only when something visible changed) and,
    /// once a second, the context. Alive is marked before the frame is published, so a link the frame wakes never reads
    /// the show as stuck.</summary>
    private int _tick;
    private void Tick()
    {
        Snapshots.MarkAlive();
        var frame = Frame();
        JournalEdges(frame);
        Snapshots.Publish(frame);
        if (++_tick % 4 == 0 || _context is null) _context = Context();
    }

    // ---------------------------------------------------------------- the journal (--journal), owner thread
    // What the app's show thread writes, for what the rig simulates: an item's start (its timer first counting) and end (the
    // running order moving on, with the seconds it ran), the phase as it changes while a countdown runs, blackout on and off, a
    // message put up, a note, a window changed, and a key coming to speak for a node. Positions identify items: the rig has one
    // running order, and its report matches rows to items by position.
    private ShowSnapshot? _journalled;   // the last frame the journal's edges were read from
    private DateTime? _itemStartUtc;     // when the item in hand started, or null when it has not

    private void Journal(JournalKind kind, int item, string text = "", double value = 0) => _journal?.Add(DateTime.UtcNow, kind, item, text, value);

    /// <summary>The edges between the last frame and this one, as the app journals a published frame's.</summary>
    private void JournalEdges(ShowSnapshot frame)
    {
        if (_journal is null) return;
        var prev = _journalled;
        _journalled = frame;
        var now = DateTime.UtcNow;
        if (_itemStartUtc is null && Engine.IsCounting)
        {
            _itemStartUtc = now;
            _journal.Add(now, JournalKind.ItemStart, _item, Agenda[_item].Title);
        }
        if (prev is null) return;
        if (frame.Phase != prev.Phase && frame.Mode == TimerMode.Countdown && frame.State != TimerState.Idle)
            _journal.Add(now, JournalKind.Phase, _item, frame.Phase.ToString());
        if (frame.Blackout != prev.Blackout) _journal.Add(now, JournalKind.Blackout, _item, frame.Blackout ? "on" : "off");
    }

    /// <summary>The item in hand, if it started, ends now: its ItemEnd with the seconds it ran.</summary>
    private void EndItem(DateTime now)
    {
        if (_itemStartUtc is not { } start) return;
        _itemStartUtc = null;
        _journal?.Add(now, JournalKind.ItemEnd, _item, Agenda[_item].Title, (now - start).TotalSeconds);
    }

    /// <summary>The running order the report reads the journal against: the rig's agenda, its plan anchored where the rig started.</summary>
    private static readonly Rundown RigRundown = new()
    {
        Id = "rig",
        Items = Agenda.Select((a, i) => new RundownItem
        {
            Id = "rig-item-" + (i + 1).ToString(CultureInfo.InvariantCulture), Title = a.Title, Speaker = a.Speaker.Length > 0 ? a.Speaker : null,
            DurationSeconds = a.Seconds, Kind = a.Kind,
        }).ToList(),
    };

    /// <summary>
    /// IShowReports: the planned-vs-actual report ("csv", else HTML) from the journal and the rig's running order, built on the
    /// owner thread (the journal's one reader and writer), waited for as long as a receipt waits; null without --journal, or
    /// while the show thread does not turn over in that time (a stall) — the route then answers 404 "no report", as the app's
    /// does; telling a stalled show from no report is the app's and the rig's together (docs/WHATS_NEXT.md).
    /// </summary>
    public string? Report(string format)
    {
        if (_journal is null) return null;
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            // A report that cannot be built is no report, at once: the owner loop logs why.
            try
            {
                var clock = ShowClock.For("UTC");
                var report = ShowReport.Build(_o.Room, RigRundown, _journal.Entries, _startedUtc, clock, DateTime.UtcNow);
                done.TrySetResult(ShowReports.Render(report, format, clock));
            }
            finally { done.TrySetResult(null); }
        });
        return done.Task.Wait(WebServer.ReceiptWait) ? done.Task.Result : null;
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
            view.Rows.Select(r => r.Peer).ToList(), (IReadOnlyList<JournalEntry>?)_journal?.Entries ?? Array.Empty<JournalEntry>(), ShowClock.For("UTC"), 0));
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
        if (text.Length > 0) Journal(JournalKind.Message, _item, text);
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

    /// <summary>
    /// The rig ends before the secrets of the keys made in this start went out (it could not listen, or --secrets-out could not
    /// be written): those keys come out of --state again, so no saved key is one whose secret nobody holds, and the next start
    /// makes them afresh. Only once the owner thread has ended (this thread is then the one writer); otherwise said, and kept.
    /// </summary>
    private void ForgetMadeKeys(List<(string Id, string Secret)> made, bool ownerEnded)
    {
        if (made.Count == 0) return;
        if (!ownerEnded)
        {
            Log("keys: the show thread did not end in time; the keys made in this start stay in --state, their secrets never given");
            return;
        }
        var ids = made.Select(k => k.Id).ToHashSet(StringComparer.Ordinal);
        _keys.RemoveAll(k => ids.Contains(k.Id));
        _apiKeys = CopyKeys(_keys);
        SaveState(_windows.Windows);
        Log($"keys: the {made.Count.ToString(CultureInfo.InvariantCulture)} made in this start are taken out of --state again; their secrets never went out");
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
        var words = KeyBindingWords.JournalLine(bound, id => _apiKeys.FirstOrDefault(k => k.Id == id)?.Name);
        Log(words);
        Journal(JournalKind.Note, _item, words);
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
