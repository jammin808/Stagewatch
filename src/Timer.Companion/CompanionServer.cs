using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Stagewatch.Core;

namespace Stagewatch.Companion;

/// <summary>
/// TCP server that exposes the Stagewatch Companion protocol. Default port 9923.
///
/// Two event styles share one socket:
///   v1 (default, used by the PowerPoint add-in): TICK / STATE / MODE / MESSAGE lines.
///   v2 (<c>SUBSCRIBE JSON</c>, used by the Companion module): SNAPSHOT {json} whenever anything
///      visible changes (~1/s while running) and CONFIG {json} whenever presets, quick messages
///      or the running order change. Same frames the browser viewer gets.
///
/// Stability: every client has its own bounded outbound queue drained by its own writer task.
/// Publishing never touches a socket, so a stalled client can't block the engine ticker or the
/// UI thread; it loses its oldest queued lines, and is dropped if a write stalls for 10 s.
/// </summary>
public sealed class CompanionServer : IAsyncDisposable
{
    public const int MaxClients = 32;
    private static readonly TimeSpan WriteStallLimit = TimeSpan.FromSeconds(10);

    private readonly ICompanionBridge _bridge;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<Client> _clients = new();
    private readonly object _clientsLock = new();
    private (int Version, string Line)? _configLine;

    public CompanionServer(ICompanionBridge bridge) { _bridge = bridge; }

    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }

    /// <summary>The door as <see cref="Start"/> bound it, published whole once the listener runs; null while it does not.</summary>
    public BoundDoor? Door => _door;
    private volatile BoundDoor? _door;

    public void Start(int port, bool bindAllInterfaces)
    {
        if (_listener is not null) return;

        var addr = bindAllInterfaces ? IPAddress.Any : IPAddress.Loopback;
        var listener = new TcpListener(addr, port);
        listener.Start();
        _listener = listener;
        Port = port;
        _door = new BoundDoor(port, bindAllInterfaces);
        _cts = new CancellationTokenSource();
        // Thread pool, not the caller's (UI) context: network I/O must never run on the show thread.
        _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
        _bridge.Engine.Tick += OnTick;
        _bridge.Engine.StateChanged += OnStateChanged;
        _bridge.Engine.ModeChanged += OnModeChanged;
        _bridge.MessageChanged += OnMessageChanged;
        _bridge.CommandRecorded += OnRecorded;
        _bridge.Snapshots.Subscribe(OnSnapshot);
    }

    public async Task StopAsync()
    {
        _door = null;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;

        _bridge.Engine.Tick -= OnTick;
        _bridge.Engine.StateChanged -= OnStateChanged;
        _bridge.Engine.ModeChanged -= OnModeChanged;
        _bridge.MessageChanged -= OnMessageChanged;
        _bridge.CommandRecorded -= OnRecorded;
        _bridge.Snapshots.Unsubscribe(OnSnapshot);

        Client[] snapshot;
        lock (_clientsLock) { snapshot = _clients.ToArray(); _clients.Clear(); }
        foreach (var c in snapshot) await c.DisposeAsync();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }

            int count;
            lock (_clientsLock) count = _clients.Count;
            if (count >= MaxClients) { tcp.Dispose(); continue; }

            tcp.NoDelay = true;
            try { tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
            var c = new Client(tcp, this);
            lock (_clientsLock) _clients.Add(c);
            _ = c.RunAsync(ct);
        }
    }

    private Client[] Snapshot()
    {
        lock (_clientsLock) return _clients.ToArray();
    }

    private void BroadcastV1(string line)
    {
        foreach (var c in Snapshot())
            if (c.Subscribed && !c.JsonMode) c.Enqueue(line);
    }

    private void OnTick(TimerTick t)
        => BroadcastV1($"TICK ms={(long)t.Remaining.TotalMilliseconds} formatted={t.Format()} state={t.State}");

    private void OnStateChanged(TimerState s) => BroadcastV1($"STATE state={s}");
    private void OnModeChanged(TimerMode m) => BroadcastV1($"MODE {m}");

    /// <summary>Operator actions in Stagewatch → Companion's action recorder (JSON clients only).</summary>
    private void OnRecorded(string line)
    {
        if (line.IndexOfAny(new[] { '\r', '\n' }) >= 0) line = line.Replace("\r", " ").Replace("\n", " ");
        foreach (var c in Snapshot())
            if (c.Subscribed && c.JsonMode) c.Enqueue("RECORD " + line);
    }

    private void OnMessageChanged(MessageState m)
        => BroadcastV1($"MESSAGE {CompanionProtocol.EscapeQuoted(m.Text)} urgency={m.Urgency} flash={m.Flash}");

    /// <summary>Owner thread, at most ~1/s. Serialize once, enqueue to every JSON subscriber.</summary>
    private void OnSnapshot(ShowSnapshot s)
    {
        var clients = Snapshot();
        if (!clients.Any(c => c.JsonMode && c.Subscribed)) return;
        var line = SnapshotLine(s);
        var config = ConfigLine(s.ConfigVersion);
        foreach (var c in clients)
        {
            if (!c.JsonMode || !c.Subscribed) continue;
            if (c.ConfigVersionSent != s.ConfigVersion)
            {
                c.ConfigVersionSent = s.ConfigVersion;
                c.Enqueue(config);
            }
            c.Enqueue(line);
        }
    }

    /// <summary>Companion gets the detail frame: projected running order, advice, Q&amp;A and rooms drive its keys.</summary>
    private static string SnapshotLine(ShowSnapshot s) => "SNAPSHOT " + Encoding.UTF8.GetString(SnapshotJson.Serialize(s, detail: true));

    private string ConfigLine(int version)
    {
        if (_configLine is { } cached && cached.Version == version) return cached.Line;
        var line = "CONFIG " + Encoding.UTF8.GetString(SnapshotJson.SerializeConfig(_bridge.GetRemoteConfig()));
        _configLine = (version, line);
        return line;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class Client : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private readonly CompanionServer _server;
        private readonly Stream _stream;
        private readonly StreamReader _reader;
        private readonly Channel<string> _outbound = Channel.CreateBounded<string>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
        private readonly CancellationTokenSource _life = new();
        private int _disposed;

        public bool Subscribed { get; private set; } = true; // default-on after connect
        public bool JsonMode { get; private set; }
        public int ConfigVersionSent { get; set; } = -1;

        public Client(TcpClient tcp, CompanionServer server)
        {
            _tcp = tcp;
            _server = server;
            _stream = tcp.GetStream();
            _reader = new StreamReader(_stream, Encoding.UTF8);
        }

        /// <summary>Never blocks. Called from the engine ticker, the UI thread, or this client's reader.</summary>
        public void Enqueue(string line) => _outbound.Writer.TryWrite(line);

        public async Task RunAsync(CancellationToken serverCt)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, _life.Token);
            var ct = linked.Token;
            var writer = WriteLoopAsync(ct);

            Enqueue($"READY {CompanionProtocol.Version}");
            if (!_server._bridge.CompanionAllowed)
            {
                Enqueue("DENIED license \"Companion control needs a Stagewatch Pro license — the trial has ended.\"");
                _outbound.Writer.TryComplete();
                try { await writer.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None); } catch { }
                await DisposeAsync();
                return;
            }
            SendStateV1();

            try
            {
                string? line;
                while ((line = await ReadLineBoundedAsync(ct)) is not null)
                {
                    var tokens = CompanionProtocol.Tokenize(line);
                    if (tokens.Count == 0) continue;
                    // Show Bus receipts: "@a1 NEXT" is answered "OK @a1 <effect>" or "ERROR @a1 <why>" once it has run.
                    string? requestId = null;
                    if (tokens[0].StartsWith('@') && CommandReceipt.CleanId(tokens[0][1..]) is { } rid && tokens.Count > 1)
                    {
                        requestId = rid;
                        tokens.RemoveAt(0);
                    }
                    try
                    {
                        if (requestId is null) Handle(tokens);
                        else HandleWithReceipt(tokens, requestId);
                    }
                    catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
                    {
                        Enqueue(requestId is null ? "ERROR " + ex.Message : $"ERROR @{requestId} {ex.Message}");
                    }
                }
            }
            catch (Exception) { /* client disconnected or misbehaved */ }
            finally
            {
                lock (_server._clientsLock) _server._clients.Remove(this);
                await DisposeAsync();
            }
        }

        /// <summary>Lines longer than 4 KB are a broken or hostile client — drop it.</summary>
        private async Task<string?> ReadLineBoundedAsync(CancellationToken ct)
        {
            var line = await _reader.ReadLineAsync(ct);
            if (line is { Length: > 4096 }) throw new InvalidDataException("line too long");
            return line;
        }

        private async Task WriteLoopAsync(CancellationToken ct)
        {
            var sb = new StringBuilder(1024);
            try
            {
                while (await _outbound.Reader.WaitToReadAsync(ct))
                {
                    // Batch whatever is queued into one write.
                    sb.Clear();
                    while (sb.Length < 64 * 1024 && _outbound.Reader.TryRead(out var l)) sb.Append(l).Append('\n');
                    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    stall.CancelAfter(WriteStallLimit);
                    await _stream.WriteAsync(bytes, stall.Token);
                }
            }
            catch (Exception) { _life.Cancel(); } // stalled or closed: tear the client down
        }

        private void SendStateV1()
        {
            var e = _server._bridge.Engine;
            Enqueue($"STATE mode={e.Mode} state={e.State} preset={e.CurrentPresetSlot?.ToString() ?? "-"}");
        }

        private void SendJson(bool includeConfig)
        {
            var snap = _server._bridge.Snapshots.Latest;
            if (includeConfig)
            {
                ConfigVersionSent = snap.ConfigVersion;
                Enqueue(_server.ConfigLine(snap.ConfigVersion));
            }
            Enqueue(SnapshotLine(snap));
        }

        private string? _rid;
        private bool _ridFailed;

        /// <summary>An error for the command being handled — with its request id when it came with one.</summary>
        private void Fail(string error)
        {
            if (_rid is { } id) { _ridFailed = true; Enqueue($"ERROR @{id} {error}"); }
            else Enqueue("ERROR " + error);
        }

        /// <summary>A command with a request id: through the shared grammar with a receipt, else as before ("accepted").</summary>
        private void HandleWithReceipt(List<string> t, string id)
        {
            if (ShowCommandParser.TryParse(t[0], t.Skip(1).ToList(), out var cmd, out _))
            {
                _ = ReplyAsync(_server._bridge.ExecuteWithReceiptAsync(cmd, id, TimeSpan.FromSeconds(3)));
                return;
            }
            _rid = id;
            _ridFailed = false;
            try { Handle(t); }
            finally { _rid = null; }
            if (!_ridFailed) Enqueue($"OK @{id}");

            async Task ReplyAsync(Task<CommandReceipt> receipt)
            {
                var r = await receipt.ConfigureAwait(false);
                var words = (r.Ok ? r.Effect : r.Error ?? "refused").Replace('\n', ' ').Replace('\r', ' ');
                Enqueue(r.Ok ? $"OK @{id} {r.Stage}{(words.Length > 0 ? " " + words : "")}" : $"ERROR @{id} {words}");
            }
        }

        private void Handle(List<string> t)
        {
            var verb = t[0].ToUpperInvariant();
            var bridge = _server._bridge;
            switch (verb)
            {
                // Every verb is executed by the app on its own thread (single writer).
                case "START":
                    bridge.Execute(new ShowCommand(ShowCommandKind.Start));
                    break;
                case "STOP":
                case "PAUSE":
                    bridge.Execute(new ShowCommand(ShowCommandKind.Pause));
                    break;
                case "RESET":
                    bridge.Execute(new ShowCommand(ShowCommandKind.Reset));
                    break;
                case "SET" when t.Count >= 2:
                    bridge.Execute(new ShowCommand(ShowCommandKind.SetDuration, Number: ParseTimeSpan(t[1]).TotalSeconds));
                    break;
                case "SETMS" when t.Count >= 2:
                    bridge.Execute(new ShowCommand(ShowCommandKind.SetDuration,
                        Number: long.Parse(t[1], CultureInfo.InvariantCulture) / 1000.0));
                    break;
                case "PRESET" when t.Count >= 2:
                {
                    var slot = int.Parse(t[1], CultureInfo.InvariantCulture);
                    var auto = t.Count >= 3 && t[2].Equals("START", StringComparison.OrdinalIgnoreCase);
                    bridge.LoadPreset(slot, auto);
                    break;
                }
                case "MODE" when t.Count >= 2:
                    bridge.Execute(new ShowCommand(ShowCommandKind.Mode, Mode: t[1].ToLowerInvariant() switch
                    {
                        "countdown" => TimerMode.Countdown,
                        "countup" or "up" => TimerMode.CountUp,
                        "tod" or "timeofday" => TimerMode.TimeOfDay,
                        _ => throw new ArgumentException("unknown mode")
                    }));
                    break;
                case "MESSAGE" when t.Count >= 2:
                {
                    var text = t[1];
                    var urg = t.Count >= 3 ? ShowCommandParser.ParseUrgency(t[2]) : MessageUrgency.Info;
                    var flash = t.Count >= 4 ? ShowCommandParser.ParseFlash(t[3]) : FlashRate.Off;
                    bridge.SetMessage(text, urg, flash);
                    break;
                }
                case "FLASH" when t.Count >= 3 && t[1].Equals("TIMER", StringComparison.OrdinalIgnoreCase):
                    bridge.SetTimerFlash(t[2].Equals("on", StringComparison.OrdinalIgnoreCase));
                    break;
                case "FLASH" when t.Count >= 2:
                    bridge.SetFlash(ShowCommandParser.ParseFlash(t[1]));
                    break;
                case "STATE":
                    if (JsonMode) SendJson(includeConfig: false); else SendStateV1();
                    break;
                case "SNAPSHOT":
                    SendJson(includeConfig: false);
                    break;
                case "CONFIG":
                    SendJson(includeConfig: true);
                    break;
                case "SUBSCRIBE":
                    Subscribed = true;
                    if (t.Count >= 2 && t[1].Equals("JSON", StringComparison.OrdinalIgnoreCase))
                    {
                        JsonMode = true;
                        SendJson(includeConfig: true);
                    }
                    break;
                case "UNSUBSCRIBE":
                    Subscribed = false;
                    break;
                case "PING":
                    // The link answering isn't proof the show is: if the show thread has stopped turning over, say so,
                    // so keys don't keep showing a frozen time as if it were live.
                    var pong = t.Count >= 2 ? "PONG " + t[1] : "PONG";
                    // Show Bus §5: "PING <unix ms>" also gets our receive and send times, for the clock offset.
                    if (t.Count >= 2 && long.TryParse(t[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var t1) && t1 > 1_000_000_000_000)
                    {
                        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        pong = FormattableString.Invariant($"PONG {t1} {now} {now}");
                    }
                    Enqueue(_server._bridge.Snapshots.ProducerStuck ? pong + " STUCK" : pong);
                    break;
                case "QUIT":
                    _life.Cancel();
                    break;
                default:
                    // Everything newer (NEXT, PREV, GOTO, BLACKOUT, TOGGLE, NUDGE, ENDAT, CLEARMESSAGE...)
                    // comes from the shared grammar so Companion, HTTP and OSC can never drift apart.
                    if (ShowCommandParser.TryParse(verb, t.Skip(1).ToList(), out var cmd, out var error))
                        bridge.Execute(cmd);
                    else
                        Fail(error);
                    break;
            }
        }

        private static TimeSpan ParseTimeSpan(string s)
        {
            if (!ShowCommandParser.TryParseSpan(s, out var ts)) throw new FormatException("bad duration: " + s);
            return ts;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
            _outbound.Writer.TryComplete();
            try { _life.Cancel(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _tcp.Close(); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}
