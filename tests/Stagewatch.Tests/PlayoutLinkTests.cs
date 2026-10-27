using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// PlayoutLink, the desk's link to a Patterns desk (or a PlayMap node's line wire), on Stagewatch's PeerLink core at level 0, against
/// desks played on loopback. Its pairing keeps SW18.7's rules — plain AUTH only to a socket on this PC and never on one that heard a
/// NONCE, a proof only for 32 lowercase hex, nothing taken before the desk pairs — and a refused proof now holds the token. On the
/// core: request ids <c>s&lt;n&gt;-&lt;inst&gt;</c> unique for the process, at most 32 commands in flight and none evicted, a command
/// written before a drop or unanswered for 30 s unconfirmed and never failed, nothing queued while the link is down, only matched pongs
/// as clock samples, the link stale past 6 s and down past 12 s of silence, and the redial's wait 13.15.1's, its count started over only
/// by a link that paired and stayed up 10 s; a link not live never green; no plain socket taken for TLS; the clock emptied by a wall
/// step and on each connection; one log line for the evaluator's own pause; a busy desk's words. The core's rules run on the injected
/// clock of <see cref="LinkClock"/>, as does every check of a reading; the pairing rules that wait out the desk's NONCE or the probe run
/// on the system's. Then the link's own: each connection writes on a lane of its own, so no caller and not the evaluator waits on a
/// write, a command still on the lane at a drop is not-sent, and one the core settled not-sent while it waited there is never written;
/// a refused proof's hold is kept with its end, so a restart — a store read back from its file — keeps it, for the token it was for
/// (<see cref="PlayoutKeptStore"/>, which writes on a lane of its own); and the show log's rows — one per change of state with the
/// peer's address, repeats (a flapping link's among them) folded into a summary a minute, one per command's outcome as it first settles
/// and one more as an unconfirmed one is resolved. The scripted peer's level-0 scenarios against it are PeerLinkScenarios.Playout.cs.
/// </summary>
static class PlayoutLinkTests
{
    const string Token = "AB12CD34";
    const string Nonce = "00112233445566778899aabbccddeeff";

    public static async Task Run(Action<bool, string> Check)
    {
        await PairingRules(Check);
        await Desk11(Check);
        await OlderDeskOnThisPc(Check);
        await Round86Desk(Check);
        await Oversize(Check);
        await Ids(Check);
        await InFlight(Check);
        await Drops(Check);
        await Pongs(Check);
        await SilenceAndRedial(Check);
        await NotGreenWhileDown(Check);
        await Scheme(Check);
        await WallClock(Check);
        await SelfGap(Check);
        await Busy(Check);
        Words(Check);
        await WriterLane(Check);
        await LaneSettled(Check);
        await Holds(Check);
        await KeptStore(Check);
        await Journal(Check);
        await Flapping(Check);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A link to a desk on this PC's loopback at <paramref name="port"/>, a desk on another PC when the seam says so.</summary>
    internal static PlayoutLink Link(int port, string token, Func<IPAddress, bool>? loopback = null, TimeProvider? time = null, string inst = "5e6f7a8b",
        Func<double>? jitter = null)
        => new(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = port, Token = token, Inst = inst, IsLoopback = loopback ?? PlayoutLink.IsLoopbackAddress,
            Time = time ?? TimeProvider.System, Jitter = jitter,
        });

    /// <summary>The seam playing a desk on another PC from loopback.</summary>
    internal static bool Remote(IPAddress _) => false;

    /// <summary>A fresh inst, so a test's request ids count from 1 in the process's counter.</summary>
    static string NewInst() => Guid.NewGuid().ToString("N")[..8];

    internal static async Task<bool> Until(Func<bool> cond, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until && !cond()) await Task.Delay(15);
        return cond();
    }

    /// <summary>A line as a desk reads it: its request id (with its @) and its body.</summary>
    static (string Tag, string Body) Split(string line)
    {
        if (!line.StartsWith('@')) return ("", line);
        var sp = line.IndexOf(' ');
        return sp < 0 ? (line, "") : (line[..sp], line[(sp + 1)..]);
    }

    /// <summary>An answer as a 1.1 desk tags it: with the line's request id when it had one.</summary>
    static string Ok(string tag, string words = "") => "OK" + (tag.Length > 0 ? " " + tag : "") + (words.Length > 0 ? " " + words : "");

    static string Err(string tag, string words) => "ERR" + (tag.Length > 0 ? " " + tag : "") + " " + words;

    static string State(string file) => "STATE {\"video\":{\"file\":\"" + file + "\",\"positionMs\":1000,\"lengthMs\":60000,\"playing\":true,\"seq\":1}}";

    static string ProofLine(string token, string nonce) => "@pa AUTH-PROOF " + PlayoutWire.Proof(token, nonce, PlayoutWire.HelloName);

    /// <summary>
    /// A clock that moves only when told to, its wall time starting at <paramref name="unixMs"/>. Each timer due by the time it is
    /// moved to fires once in <see cref="Advance"/>, outside its lock — as a paused process's would when it runs again — so a jump
    /// longer than the link's evaluator period is the evaluator's own gap.
    /// </summary>
    internal sealed class LinkClock(long unixMs) : TimeProvider
    {
        readonly Lock _gate = new();
        readonly List<Timer> _timers = [];
        long _ticks, _wallShift;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return DateTimeOffset.FromUnixTimeMilliseconds(unixMs + _wallShift).AddTicks(_ticks);
        }

        /// <summary>The wall clock steps by <paramref name="ms"/>, either way, and the monotonic clock does not move: an NTP step, a person's.</summary>
        public void StepWall(long ms)
        {
            lock (_gate) _wallShift += ms;
        }

        public override long GetTimestamp()
        {
            lock (_gate) return _ticks;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <summary>The wall clock now, Unix ms.</summary>
        public long WallMs => GetUtcNow().ToUnixTimeMilliseconds();

        /// <summary>
        /// Whether a one-shot timer waits on this clock — a link's redial wait, or its dial's deadline: what a test waits for before it
        /// moves the clock on, so the wait counts from the moment the link set it.
        /// </summary>
        public bool OneShotWaiting
        {
            get { lock (_gate) return _timers.Any(t => t.Period == 0 && t.DueAt is not null); }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new Timer(this, callback, state);
            lock (_gate) _timers.Add(t);
            t.Change(dueTime, period);
            return t;
        }

        /// <summary>Moves the clock on by <paramref name="ms"/>; a monotonic clock never moves back, so a negative move is refused.</summary>
        public void Advance(long ms)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(ms);
            List<Timer> due;
            lock (_gate)
            {
                _ticks += ms * TimeSpan.TicksPerMillisecond;
                due = _timers.Where(t => t.DueAt is { } d && d <= _ticks).ToList();
                foreach (var t in due) t.DueAt = t.Period > 0 ? _ticks + t.Period : null;
            }
            foreach (var t in due) t.Fire();
        }

        internal sealed class Timer(LinkClock owner, TimerCallback callback, object? state) : ITimer
        {
            public long? DueAt;
            public long Period;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._ticks + dueTime.Ticks;
                    Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                }
                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._gate) owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>
    /// A desk on loopback that takes any number of connections, in turn: each greeted with <see cref="Greeting"/> and each line it hears
    /// answered with <see cref="Answer"/>'s lines, or — while <see cref="Closing"/> — closed as soon as it is taken. What every connection
    /// heard is kept, in order.
    /// </summary>
    internal sealed class FakeDesk : IAsyncDisposable
    {
        readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        readonly CancellationTokenSource _stop = new();
        readonly Task _accepting;
        readonly Lock _gate = new();
        readonly List<(TcpClient Client, StreamWriter Writer)> _conns = [];
        int _taken;

        public ConcurrentQueue<string> Heard { get; } = new();
        public int Port { get; }
        public volatile bool Closing;
        public Func<IReadOnlyList<string>> Greeting { get; set; } = () => [];
        public Func<string, IEnumerable<string>> Answer { get; set; } = _ => [];

        /// <summary>Connections taken so far, closed at once or not.</summary>
        public int Taken => Volatile.Read(ref _taken);

        public FakeDesk()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _accepting = Task.Run(Accept);
        }

        async Task Accept()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { return; }
                Interlocked.Increment(ref _taken);
                if (Closing)
                {
                    client.Dispose();
                    continue;
                }
                _ = Task.Run(() => Serve(client));
            }
        }

        async Task Serve(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                lock (_gate) _conns.Add((client, writer));
                foreach (var g in Greeting()) Say(writer, g);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                for (var l = await reader.ReadLineAsync(_stop.Token); l is not null; l = await reader.ReadLineAsync(_stop.Token))
                {
                    Heard.Enqueue(l);
                    foreach (var a in Answer(l)) Say(writer, a);
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                // The connection ended: the link closed it, or the test did.
            }
            finally
            {
                client.Dispose();
            }
        }

        void Say(StreamWriter writer, string line)
        {
            lock (_gate)
            {
                try { writer.WriteLine(line); }
                catch (Exception e) when (e is IOException or ObjectDisposedException) { _ = e; }
            }
        }

        /// <summary>A line to the newest connection.</summary>
        public void Say(string line)
        {
            StreamWriter? w;
            lock (_gate) w = _conns.Count > 0 ? _conns[^1].Writer : null;
            if (w is not null) Say(w, line);
        }

        /// <summary>The newest connection, closed by the desk.</summary>
        public void Drop()
        {
            lock (_gate)
            {
                if (_conns.Count > 0) _conns[^1].Client.Dispose();
            }
        }

        /// <summary>The newest connection, reset by the desk: its socket closes at once with an RST, as a desk that crashed or was cut off.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                if (_conns.Count == 0) return;
                _conns[^1].Client.Client.LingerState = new LingerOption(true, 0);
                _conns[^1].Client.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            lock (_gate)
            {
                foreach (var (c, _) in _conns) c.Dispose();
            }
            await _accepting.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(_ => { }, TaskScheduler.Default);
            _stop.Dispose();
        }
    }

    /// <summary>
    /// A connection's bytes passed through, but for a write whose line <see cref="HoldWhen"/> picks: the link's lane is held in it — as a
    /// socket whose desk stopped reading holds a write — until the test lets it go (<see cref="Release"/>) or the link ends the
    /// connection, and none of it reaches the desk before then.
    /// </summary>
    internal sealed class HeldStream(Stream inner) : Stream
    {
        TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int _held;
        public volatile Func<string, bool>? HoldWhen;
        public volatile bool Holding;

        /// <summary>How many writes it has held so far.</summary>
        public int Held => Volatile.Read(ref _held);

        /// <summary>The write it holds goes on to the desk; the next write <see cref="HoldWhen"/> picks is held again.</summary>
        public void Release()
        {
            var release = Interlocked.Exchange(ref _release, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            Holding = false;
            release.TrySetResult();
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (HoldWhen is { } hold && hold(Encoding.UTF8.GetString(buffer.Span)))
            {
                var release = Volatile.Read(ref _release);
                Holding = true;
                Interlocked.Increment(ref _held);
                await release.Task.WaitAsync(cancellationToken);
            }
            await inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// A Show Bus 1.1 desk as the core-rule tests play it: it greets with <see cref="Nonce"/>, pairs <paramref name="token"/>'s proof
    /// (then pushes a STATE of pushed.mp4), answers the probe, pongs a ping with the offset <paramref name="offset"/> gives (none while it
    /// gives null), answers a command with what <paramref name="commands"/> gives (none while it gives null), and answers the link's
    /// STATUS ask — the last thing it says unasked-for once paired — with a STATE of asked.mp4.
    /// </summary>
    static void Desk11(FakeDesk desk, string token, Func<long?> offset, Func<string, string?> commands)
    {
        desk.Greeting = () => ["NONCE " + Nonce];
        desk.Answer = l =>
        {
            var (tag, body) = Split(l);
            if (body.StartsWith("AUTH-PROOF ", StringComparison.Ordinal))
                return body == ProofLine(token, Nonce)[4..] ? [Ok(tag, "paired"), State("pushed.mp4")] : [Err(tag, "wrong token")];
            if (tag == "@hi") return [Ok(tag, "PONG " + body[5..] + " " + body[5..] + " " + body[5..])];
            if (body.StartsWith("PING ", StringComparison.Ordinal))
            {
                if (offset() is not { } off || !long.TryParse(body[5..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t1)) return [];
                var t2 = (t1 + off).ToString(CultureInfo.InvariantCulture);
                return [Ok(tag, "PONG " + body[5..] + " " + t2 + " " + t2)];
            }
            if (body == "STATUS") return [Ok(tag, State("asked.mp4")["STATE ".Length..])];
            if (body.StartsWith("VIDEO ", StringComparison.Ordinal) && commands(tag) is { } answer) return [answer];
            return [];
        };
    }

    // ------------------------------------------------------------------ SW18.7's pairing rules, on the core

    /// <summary>
    /// PlayoutLink's pairing rules against scripted desks: plain AUTH only to a loopback socket (the seam plays a desk on another PC
    /// from loopback), a proof only for a NONCE of 32 lowercase hex, nothing taken before the desk pairs, and a refused proof or token
    /// red, held and never tried again on the desk's next NONCE. The scenarios run side by side; their checks are reported in order.
    /// </summary>
    static async Task PairingRules(Action<bool, string> Check)
    {
        static string AheadBy5s()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return FormattableString.Invariant($"OK PONG {now} {now + 5000} {now + 5000}");
        }
        static string Pong(string probe) => "OK @hi PONG " + probe["@hi PING ".Length..] + " " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            + " " + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        // A desk older than Show Bus 1.1: no NONCE, "@hi" refused, the token taken as plain AUTH.
        static IEnumerable<string> Older(string l) => l.StartsWith("@hi ", StringComparison.Ordinal) ? ["ERR unknown command: @hi"]
            : l == "AUTH " + Token ? ["OK paired"] : l == "PING" ? ["OK", State("older.mp4")] : ["OK"];
        static bool Proof(string l) => Split(l).Body.StartsWith("AUTH-PROOF ", StringComparison.Ordinal);
        static int Auths(ShowBusTests.ScriptedDesk desk) => desk.Heard.Count(l => l.Contains("AUTH", StringComparison.Ordinal));
        // Past the link's wait for a NONCE (1.5 s from the connection), with margin.
        const int pastTheWait = 2300;

        async Task<List<(bool, string)>> OlderOffThisPc()
        {
            var r = new List<(bool, string)>();
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4")], Older);
            using var link = Link(desk.Port, Token, Remote);
            link.Start();
            await Task.Delay(pastTheWait);
            r.Add((!desk.HeardSecret(Token) && Auths(desk) == 0 && desk.Heard.Contains("HELLO Stagewatch"),
                $"playout pairing: an older desk on another PC (no NONCE) never hears AUTH or the token ({string.Join(" | ", desk.Heard)})"));
            r.Add((link.Pairing == PlayoutPairing.None && link.Health == LinkHealth.Starting
                   && link.Status == $"127.0.0.1:{desk.Port} is older than Show Bus 1.1 and pairs only by sending its token in clear — update Patterns; the token stays on this PC",
                $"playout pairing: that link is amber and names the desk and why ({link.Health}: {link.Status})"));
            r.Add((link.Video is null && link.Value is null && link.SetAsideBeforePairing >= 1 && link.Context is null && !desk.Heard.Contains("@ctx CONTEXT")
                   && link.Record.Counts.Malformed >= 1,
                $"playout pairing: nothing it says is taken — its STATE set aside ({link.SetAsideBeforePairing}) and counted by the core, no context asked"));
            r.Add((link.EndIn(10) is { } end && end.Contains("hasn't paired") && link.Play() is { } play && play.Contains("hasn't paired")
                   && !desk.Heard.Any(l => l.Contains("VIDEO", StringComparison.Ordinal)),
                $"playout pairing: commands to an unpaired desk are refused here with the reason, nothing written ({link.EndIn(10)})"));
            return r;
        }

        async Task<List<(bool, string)>> OlderOnThisPc()
        {
            var r = new List<(bool, string)>();
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4")], Older);
            using var link = Link(desk.Port, Token);
            link.Start();
            var paired = await Until(() => link.Pairing == PlayoutPairing.Paired);
            r.Add((paired && desk.Heard.Contains("AUTH " + Token) && link.Health == LinkHealth.Ok && !desk.Heard.Any(l => l.Contains("STATUS", StringComparison.Ordinal))
                   && link.Record is { State: "live", Proof: "legacy", Level: 0 } && link.Record.Flags.Contains("legacy"),
                $"playout pairing: the same older desk on this PC (a loopback socket) gets the token as AUTH and pairs, legacy on the core; it answers untagged, so it is not asked for its STATE ({link.Status})"));
            desk.Say(State("after.mp4"));
            r.Add((await Until(() => link.Video is { File: "after.mp4" }) && link.SetAsideBeforePairing >= 1,
                $"playout pairing: a STATE after OK paired is read; the greeting before it was not ({link.Video?.File})"));
            return r;
        }

        async Task<List<(bool, string)>> NonceShapes()
        {
            var r = new List<(bool, string)>();
            const string short31 = "00112233445566778899aabbccddeef";
            const string upper = "00112233445566778899AABBCCDDEEFF";
            await using var desk31 = new ShowBusTests.ScriptedDesk([State("greeting.mp4"), "NONCE " + short31], Older);
            await using var deskUp = new ShowBusTests.ScriptedDesk([State("greeting.mp4"), "NONCE " + upper], l => Proof(l) ? [Ok(Split(l).Tag, "paired")] : []);
            // The default seam: both desks are on this PC, where plain AUTH could go — but not on a connection that heard a NONCE.
            using var link31 = Link(desk31.Port, Token);
            using var linkUp = Link(deskUp.Port, Token);
            link31.Start();
            linkUp.Start();
            await Task.Delay(pastTheWait);
            r.Add((Auths(desk31) == 0 && !desk31.HeardSecret(Token) && link31.Pairing == PlayoutPairing.None
                   && link31.Status.Contains("not 32 lowercase hex") && link31.Health == LinkHealth.Starting,
                $"playout pairing: a 31-hex NONCE is not proved, and the connection that heard it never gets plain AUTH ({link31.Status})"));
            r.Add((Auths(deskUp) == 0 && linkUp.Pairing == PlayoutPairing.None && linkUp.Status.Contains("not 32 lowercase hex"),
                $"playout pairing: an upper-case NONCE is not proved ({string.Join(" | ", deskUp.Heard)})"));
            deskUp.Say("NONCE " + Nonce);
            r.Add((await Until(() => linkUp.Pairing == PlayoutPairing.Paired) && Auths(deskUp) == 1 && deskUp.Heard.Contains(ProofLine(Token, Nonce)) && !deskUp.HeardSecret(Token),
                $"playout pairing: the desk's next NONCE, 32 lowercase hex, gets the one proof (with its own request id) and pairs ({linkUp.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> NothingBeforePaired()
        {
            var r = new List<(bool, string)>();
            // A desk on another PC that answers the proof only after a STATE, a receipt, a clock sample and a context: all of them came
            // before OK paired.
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4"), "NONCE " + Nonce], l => Proof(l)
                ? [State("early.mp4"), "OK @s1-5e6f7a8b jumped to the last 10 s", AheadBy5s(),
                   "OK @ctx {\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"forged\"},\"light\":\"green\",\"headline\":\"forged\"}", Ok(Split(l).Tag, "paired")]
                : []);
            using var link = Link(desk.Port, Token, Remote);
            var answers = new ConcurrentQueue<PlayoutAnswer>();
            link.Answered += answers.Enqueue;
            link.Start();
            var paired = await Until(() => link.Pairing == PlayoutPairing.Paired);
            r.Add((paired && desk.Heard.Contains(ProofLine(Token, Nonce)) && !desk.HeardSecret(Token),
                $"playout pairing: a desk on another PC pairs by proof alone ({link.Status})"));
            r.Add((link.Video is null && link.ClockOffset is null && link.Context is null && answers.IsEmpty && link.SetAsideBeforePairing >= 5 && link.Record.Samples == 0,
                $"playout pairing: a STATE, a receipt, a clock sample and a context before OK paired move nothing ({link.SetAsideBeforePairing} set aside; clip {link.Video?.File ?? "none"}, offset {link.ClockOffset})"));
            desk.Say(State("late.mp4"));
            r.Add((await Until(() => link.Video is { File: "late.mp4" }) && link.Health == LinkHealth.Ok && link.Status.StartsWith("late.mp4", StringComparison.Ordinal),
                $"playout pairing: the first STATE after OK paired is the one read ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> OpenDeskOffThisPc(string held)
        {
            var r = new List<(bool, string)>();
            // A Show Bus 1.1 desk with no token: no NONCE, so its answer to the probe says it asks for none.
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4")], l =>
                l.StartsWith("@hi PING ", StringComparison.Ordinal) ? [Pong(l)]
                : l == "@ctx CONTEXT" ? ["OK @ctx {\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"open1\"},\"light\":\"amber\",\"headline\":\"open desk\"}"]
                : []);
            using var link = Link(desk.Port, held, Remote);
            link.Start();
            var open = await Until(() => link.Pairing == PlayoutPairing.Open);
            desk.Say(State("open.mp4"));
            var read = await Until(() => link.Video is { File: "open.mp4" } && link.Context is not null);
            var what = held.Length > 0 ? "holding a token" : "holding none";
            r.Add((open && read && Auths(desk) == 0 && (held.Length == 0 || !desk.HeardSecret(held)) && link.SetAsideBeforePairing >= 1
                   && link.Record is { Proof: "open" } && link.Record.Flags.Contains("open") && link.Record.Flags.Contains("legacy"),
                $"playout pairing: an open 1.1 desk on another PC ({what}) is followed once its answer to the probe shows it asks for no key, open and legacy on the core; no AUTH, the greeting set aside ({link.Status})"));
            r.Add((held.Length > 0
                    ? link.Health == LinkHealth.Starting && link.Status.EndsWith("· asks for no key — the token stays on this PC", StringComparison.Ordinal)
                    : link.Health == LinkHealth.Ok && link.Status.EndsWith("· asks this link for no key", StringComparison.Ordinal),
                $"playout pairing: it is {(held.Length > 0 ? "amber while this link holds a token it never asked for" : "green, and says it asks for no key")} ({link.Health}: {link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> TokenDeskNoTokenHere()
        {
            var r = new List<(bool, string)>();
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4"), "NONCE " + Nonce], l =>
                l.StartsWith("@hi PING ", StringComparison.Ordinal) ? [Pong(l)] : l.StartsWith("PING ", StringComparison.Ordinal) ? [State("still.mp4")] : []);
            using var link = Link(desk.Port, "");
            link.Start();
            await Task.Delay(pastTheWait);
            r.Add((link.Pairing == PlayoutPairing.None && link.Health == LinkHealth.Starting && link.Status.Contains("asks for its pairing token")
                   && link.Video is null && Auths(desk) == 0 && !desk.Heard.Contains("@ctx CONTEXT") && !desk.Heard.Any(l => l.StartsWith("PING", StringComparison.Ordinal)),
                $"playout pairing: a desk that asks for a proof, with no token here, stays unpaired and amber with the reason; nothing read, no ping or context asked ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> OlderOffThisPcNoTokenHere()
        {
            var r = new List<(bool, string)>();
            // Neither side holds a token: the older desk off this PC asks for nothing, and is still not followed.
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4")], Older);
            using var link = Link(desk.Port, "", Remote);
            link.Start();
            await Task.Delay(pastTheWait);
            desk.Say(State("pushed.mp4"));
            await Task.Delay(300);
            r.Add((link.Pairing == PlayoutPairing.None && link.Health == LinkHealth.Starting && link.Video is null && link.SetAsideBeforePairing >= 2 && Auths(desk) == 0
                   && link.Status == $"127.0.0.1:{desk.Port} is older than Show Bus 1.1 and pairs only on its own PC — update Patterns; nothing is taken from it until it pairs",
                $"playout pairing: an older desk on another PC is not followed even when neither side holds a token; amber, its STATEs set aside ({link.SetAsideBeforePairing}, {link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> SilentOffThisPc()
        {
            var r = new List<(bool, string)>();
            // A desk on another PC that sends no NONCE and never answers the probe: nothing secret goes, and it is called older only once
            // the probe has gone unanswered (3 s), not at the end of the wait for a NONCE (1.5 s).
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4")], _ => []);
            using var link = Link(desk.Port, Token, Remote);
            var since = System.Diagnostics.Stopwatch.StartNew();
            link.Start();
            await Task.Delay(2000);
            var early = link.Status;
            var earlyInTime = since.ElapsedMilliseconds < 3000;   // read before the probe could have timed out
            r.Add((!earlyInTime || (!early.Contains("older", StringComparison.Ordinal) && link.Health == LinkHealth.Starting && link.Pairing == PlayoutPairing.None),
                $"playout pairing: past the wait for a NONCE, a silent desk off this PC is still pairing, not yet called older ({early})"));
            var older = $"127.0.0.1:{desk.Port} is older than Show Bus 1.1 and pairs only by sending its token in clear — update Patterns; the token stays on this PC";
            var named = await Until(() => link.Status == older, 4000);
            r.Add((named && link.Health == LinkHealth.Starting && link.Pairing == PlayoutPairing.None && !desk.HeardSecret(Token) && Auths(desk) == 0 && link.Video is null,
                $"playout pairing: once the probe goes unanswered, the silent desk is amber, named and told to update; no AUTH, nothing read ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> WrongProof()
        {
            var r = new List<(bool, string)>();
            const string fresh = "ffeeddccbbaa99887766554433221100";
            // A 1.1 desk on another PC that refuses the proof as Patterns does: its words, then a fresh NONCE.
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4"), "NONCE " + Nonce], l => Proof(l)
                ? [Err(Split(l).Tag, "wrong token — the pairing token is on the desk's Remote page, TRUST"), "NONCE " + fresh, State("refused.mp4")]
                : []);
            using var link = Link(desk.Port, "ZZ99YY88", Remote);
            link.Start();
            var red = await Until(() => link.Health == LinkHealth.Error && link.State == PeerLinkState.Held);
            await Task.Delay(pastTheWait);
            r.Add((red && link.Health == LinkHealth.Error && link.Status.Contains("wrong token", StringComparison.Ordinal) && link.Status.Contains("the token is held; next try in", StringComparison.Ordinal)
                   && link.Pairing == PlayoutPairing.None && Auths(desk) == 1 && desk.Heard.Contains(ProofLine("ZZ99YY88", Nonce))
                   && link.Record is { State: "held", Refusal: "key" } && !link.Connected,
                $"playout pairing: a refused proof turns the link red and holds the token (13.15.3): the connection ends, and the desk's fresh NONCE gets no second proof ({link.Health}: {link.Status}; {Auths(desk)} proof)"));
            r.Add((link.Video is null && link.SetAsideBeforePairing >= 1 && !desk.Heard.Contains("@ctx CONTEXT") && !desk.Heard.Contains("@st STATUS") && !desk.HeardSecret("ZZ99YY88"),
                $"playout pairing: nothing a desk that refused the proof says is read, and nothing is asked of it ({link.SetAsideBeforePairing} set aside)"));
            return r;
        }

        async Task<List<(bool, string)>> WrongTokenOnThisPc()
        {
            var r = new List<(bool, string)>();
            // An older desk on this PC and a token that is not its own: the one plain AUTH is refused, and the link is held although the
            // desk would answer this PC's lines.
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4")], l => l.StartsWith("@hi ", StringComparison.Ordinal) ? ["ERR unknown command: @hi"]
                : l.StartsWith("AUTH ", StringComparison.Ordinal) ? ["ERR wrong token — the pairing token is on the desk's Remote page, TRUST"]
                : l == "PING" ? ["OK", State("older.mp4")] : ["OK"]);
            using var link = Link(desk.Port, "STALE1");
            link.Start();
            var red = await Until(() => link.Health == LinkHealth.Error && link.State == PeerLinkState.Held);
            desk.Say(State("pushed.mp4"));
            await Task.Delay(300);
            r.Add((red && link.Status.Contains("wrong token", StringComparison.Ordinal) && link.Pairing == PlayoutPairing.None && link.Video is null
                   && link.SetAsideBeforePairing >= 1 && Auths(desk) == 1 && link.EndIn(10) is { } why && why.Contains("nothing sent"),
                $"playout pairing: a wrong token on this PC is red, held and unread — one AUTH, its greeting set aside, commands refused here ({link.Status})"));
            return r;
        }

        async Task<List<(bool, string)>> AskedAfterPairing()
        {
            var r = new List<(bool, string)>();
            // A 1.1 desk that pushes nothing after its greeting (a still desk): the link asks for its STATE once it paired.
            await using var desk = new ShowBusTests.ScriptedDesk([State("greeting.mp4"), "NONCE " + Nonce], l =>
                l.StartsWith("@hi PING ", StringComparison.Ordinal) ? [Pong(l)]
                : Proof(l) ? [Ok(Split(l).Tag, "paired")]
                : l == "@st STATUS" ? ["OK @st " + State("asked.mp4")["STATE ".Length..]]
                : []);
            using var link = Link(desk.Port, Token, Remote);
            link.Start();
            var read = await Until(() => link.Video is { File: "asked.mp4" });
            var lines = desk.Heard.ToList();
            var proof = lines.FindIndex(Proof);
            r.Add((read && link.Pairing == PlayoutPairing.Paired && link.Health == LinkHealth.Ok && lines.Count(l => l == "@st STATUS") == 1
                   && proof >= 0 && lines.IndexOf("@st STATUS") > proof,
                $"playout pairing: once paired, a 1.1 desk is asked for its STATE once and the clip is read without waiting for a push ({link.Status})"));
            return r;
        }

        var scenarios = new[]
        {
            OlderOffThisPc(), OlderOnThisPc(), NonceShapes(), NothingBeforePaired(), OpenDeskOffThisPc(Token), OpenDeskOffThisPc(""), TokenDeskNoTokenHere(),
            OlderOffThisPcNoTokenHere(), SilentOffThisPc(), WrongProof(), WrongTokenOnThisPc(), AskedAfterPairing(),
        };
        await Task.WhenAll(scenarios);
        foreach (var scenario in scenarios)
            foreach (var (ok, name) in scenario.Result) Check(ok, name);
        Check(PlayoutLink.IsLoopbackAddress(IPAddress.Loopback) && PlayoutLink.IsLoopbackAddress(IPAddress.IPv6Loopback)
              && PlayoutLink.IsLoopbackAddress(IPAddress.Loopback.MapToIPv6()) && !PlayoutLink.IsLoopbackAddress(IPAddress.Parse("10.0.0.5"))
              && !PlayoutLink.IsLoopbackAddress(IPAddress.Parse("::ffff:10.0.0.5")),
              "playout pairing: the default seam calls a socket loopback by its address alone (IPv4, IPv6, IPv4-mapped)");
    }

    /// <summary>A Round 87 Patterns desk: greeting STATE, NONCE, the proof checked, the probe, receipts with ids, CONTEXT and a clock 5 s ahead.</summary>
    static async Task Desk11(Action<bool, string> Check)
    {
        var heard = new ConcurrentQueue<string>();
        var paired = false;
        await using var desk = new ShowBusTests.ScriptedDesk(
            ["STATE {\"video\":{\"file\":\"VT1.mp4\",\"positionMs\":1000,\"lengthMs\":60000,\"playing\":true,\"seq\":3}}", "NONCE " + Nonce], l =>
            {
                heard.Enqueue(l);
                var (tag, body) = Split(l);
                if (body.StartsWith("AUTH-PROOF ", StringComparison.Ordinal))
                {
                    paired = body["AUTH-PROOF ".Length..] == PlayoutWire.Proof("ab12-cd34", Nonce, "Stagewatch");
                    return [paired ? Ok(tag, "paired") : Err(tag, "wrong token")];
                }
                if (body.StartsWith("PING ", StringComparison.Ordinal))
                {
                    var ahead = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000).ToString(CultureInfo.InvariantCulture);
                    return [Ok(tag, "PONG " + body[5..] + " " + ahead + " " + ahead)];
                }
                if (body == "CONTEXT") return [Ok(tag, "{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"Patterns FOH\"},\"light\":\"green\",\"headline\":\"3 screens green\"}")];
                if (body.StartsWith("VIDEO END", StringComparison.Ordinal)) return [Ok(tag, "jumped to the last 10 s")];
                return [Err(tag, "unknown word")];
            });
        using var link = Link(desk.Port, "AB12CD34");
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        link.Start();
        await Until(() => link.Context is not null && link.ClockOffset is not null, 10_000);
        Check(paired && !heard.Any(l => l.Contains("AB12CD34") || l.StartsWith("AUTH ")), "playout link: pairs by proof — the token never crosses the wire");
        Check(link.Pairing == PlayoutPairing.Paired && link.Health == LinkHealth.Ok && link.SetAsideBeforePairing >= 1 && link.Video is null,
              $"playout link: the greeting STATE came before OK paired, so it is set aside and no clip is read from it ({link.SetAsideBeforePairing} set aside, {link.Status})");
        Check(link.Context is { } ctx && ctx.Contains("3 screens green"), "playout link: reads the desk's context");
        Check(link.ClockOffset is { } off && Math.Abs(off.TotalMilliseconds - 5000) < 1000 && link.ClockApart && link.Record.Samples >= 1,
              $"playout link: measures the desk's clock offset from matched pongs, apart past 2 s less its error ({link.ClockOffset}, apart {link.ClockApart})");
        Check(link.EndIn(10) is null, "playout link: sends a command");
        await Until(() => !answers.IsEmpty);
        var id = heard.FirstOrDefault(l => l.EndsWith(" VIDEO END 10", StringComparison.Ordinal)) is { } sent ? Split(sent).Tag[1..] : "";
        Check(answers.TryPeek(out var a) && a is { Outcome: PeerOutcome.Applied, Line: "VIDEO END 10", Words: "jumped to the last 10 s" } && a.Id == id
              && id.StartsWith('s') && id.EndsWith("-5e6f7a8b", StringComparison.Ordinal),
              $"playout link: the desk's answer comes back matched to the command by its request id, applied in its words ({a}; {id})");
        // Every reader takes the core's record (13.16.4), which at level 0 is amber (legacy): the context's link:<name> item is the
        // record's light with its flag's words beside the link's own, while Settings' line keeps the link's own green from its pairing.
        // NODES.md §5 says so.
        var nodes = ShowBusSpecTests.Master() is { } master ? File.ReadAllText(Path.Combine(Path.GetDirectoryName(master)!, "NODES.md")) : "";
        var items = Stagewatch.Show.ShowContext.PlayoutItems([new Stagewatch.Show.PlayoutLinkFact(link.LinkName, link.Peer, link.Record.Light, link.Status, link.Record.Flags)]);
        Check(link.Record is { Light: "amber", Proof: "legacy" } rec && rec.Flags.Contains("legacy") && link.Health == LinkHealth.Ok
              && items is [{ Id: "link:patterns", Kind: "playout", Light: "amber" } item] && item.State.Contains("proved at level 0", StringComparison.Ordinal)
              && Regex.Replace(nodes, @"\s+", " ").Contains("Companion that read that context, takes that record's light", StringComparison.Ordinal),
              $"playout light: a level-0 link's record is amber (legacy), and the context's item takes it while the desk's line is green from its pairing, as NODES.md §5 says ({link.Record.Light}, {link.Health})");
    }

    /// <summary>
    /// A desk older than Show Bus 1.1 on this PC: no NONCE, the token taken as plain AUTH and answered OK paired; STATE → the VT
    /// countdown; commands without request ids, each unconfirmed at once (it gives no receipts); a reconnect after the desk drops the link.
    /// </summary>
    static async Task OlderDeskOnThisPc(Action<bool, string> Check)
    {
        const string sponsor = """STATE {"video":{"file":"sponsor.mp4","tag":"VT","position":100,"length":210,"playing":true,"loops":false}}""";
        await using var desk = new FakeDesk { Greeting = () => [sponsor] };
        desk.Answer = l => l.StartsWith("PING", StringComparison.Ordinal) ? ["OK pong"] : l == "AUTH s3cret" ? ["OK paired", sponsor] : [];
        using var keep = new Timer(_ => desk.Say(sponsor), null, 500, 500);   // a frame every half second, as Patterns pushes while a clip plays
        using var link = Link(desk.Port, "s3cret");
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        Check(link.EndIn(10) is { } offline && offline.Contains("isn't connected") && desk.Heard.IsEmpty, "playout link: nothing queued while offline");
        link.Start();
        Check(await Until(() => link.Connected && link.Value is not null), $"playout link: connects ({link.Status})");
        Check(desk.Heard.Contains("HELLO Stagewatch") && desk.Heard.Contains("AUTH s3cret"), "playout link: HELLO, then AUTH with the pairing token to a desk on this PC");
        Check(link.Pairing == PlayoutPairing.Paired && link.SetAsideBeforePairing >= 1 && link.Health == LinkHealth.Ok,
              $"playout link: a desk on this PC pairs by its token; its greeting STATE before OK paired is set aside ({link.SetAsideBeforePairing}, {link.Status})");
        var v = link.Value!;
        Check(v.Source == "Patterns" && v.DurationSeconds == 210 && v.RemainingSeconds is > 100 and <= 110 && v.Title == "VT sponsor.mp4", $"playout link: STATE → VT countdown ({v.RemainingSeconds:0.0} left)");
        Check(link.EndIn(10) is null && await Until(() => desk.Heard.Contains("VIDEO END 10")), "playout link: Last 10 s sends VIDEO END 10, with no id to a desk that gives no receipts");
        Check(await Until(() => answers.Any(x => x is { Line: "VIDEO END 10", Outcome: PeerOutcome.Unconfirmed, Words: "sent — this node gives no receipts; look at its state" })),
              "playout link: a desk older than 1.1 echoes no id, so its command is unconfirmed at once (13.12.3), never applied on faith");
        Check(link.Restart() is null && await Until(() => desk.Heard.Contains("VIDEO RESTART")), "playout link: restart");
        var taken = desk.Taken;
        desk.Drop();
        Check(await Until(() => desk.Taken > taken && link.Pairing == PlayoutPairing.Paired && link.Value is not null, 8000), $"playout link: reconnects after a drop, pairs again and reads ({link.Status})");
        Check(!desk.Heard.Any(l => l.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)), "playout link: never a bare STOP");
        Check(link.Play() is { } old && old.Contains("older") && link.Seek(30) is not null && !desk.Heard.Any(l => l.StartsWith("VIDEO PLAY") || l.StartsWith("VIDEO SEEK")),
              "playout link: an older desk isn't sent transport commands it doesn't know");
    }

    /// <summary>A Round 86 desk on this PC, asked for no key: followed as open, green; the transport commands on the wire, VIDEO STOP never a bare STOP.</summary>
    static async Task Round86Desk(Action<bool, string> Check)
    {
        const string reel = """STATE {"video":{"file":"reel.mp4","position":10,"length":60,"playing":true,"positionMs":10000,"lengthMs":60000,"remainingMs":50000,"paused":false,"rate":1,"seq":3}}""";
        await using var desk = new FakeDesk { Greeting = () => [reel] };
        desk.Answer = l => [l.StartsWith("PING", StringComparison.Ordinal) ? "OK pong" : "OK"];
        using var keep = new Timer(_ => desk.Say(reel), null, 500, 500);
        using var link = Link(desk.Port, "");
        link.Start();
        Check(await Until(() => link.Connected && link.Video is { Transport: true }), "playout link: a Round 86 desk connects");
        Check(link.Pairing == PlayoutPairing.Open && link.Health == LinkHealth.Ok && !desk.Heard.Any(l => l.StartsWith("AUTH")),
              $"playout link: with no token, a desk on this PC that asks for no NONCE is followed as open, green ({link.Status})");
        Check(link.Pause() is null && link.Play() is null && link.StopClip() is null && link.Seek(42) is null && link.Seek(500) is { } tooFar && tooFar.Contains("only"),
              "playout link: transport commands written (seek past the end refused here)");
        Check(await Until(() => desk.Heard.Contains("VIDEO PAUSE") && desk.Heard.Contains("VIDEO PLAY") && desk.Heard.Contains("VIDEO STOP") && desk.Heard.Contains("VIDEO SEEK 42")),
              $"playout link: VIDEO PAUSE / PLAY / STOP / SEEK 42 on the wire ({string.Join(" | ", desk.Heard)})");
        Check(!desk.Heard.Any(l => l.Trim().Equals("STOP", StringComparison.OrdinalIgnoreCase)), "playout link: VIDEO STOP, never a bare STOP");
    }

    /// <summary>
    /// The reading through the primitives, against a desk on this PC: a STATE longer than the link reads is read in part — the clip when
    /// its video lies within the part, none when not — with the link amber and up, the STATE's size said and counted, by the core too;
    /// any other line that long is counted and not read; a whole STATE after them is green again. A token 13.6.8 does not prove gets no
    /// proof.
    /// </summary>
    static async Task Oversize(Action<bool, string> Check)
    {
        static IEnumerable<string> Older(string l) => l.StartsWith("@hi ", StringComparison.Ordinal) ? ["ERR unknown command: @hi"]
            : l == "AUTH " + Token ? ["OK paired"] : l == "PING" ? ["OK"] : [];
        static string Clip(string file, int padBefore = 0, int padAfter = 0)
            => "{" + (padBefore > 0 ? "\"decks\":\"" + new string('x', padBefore) + "\"," : "")
               + "\"video\":{\"file\":\"" + file + "\",\"positionMs\":1000,\"lengthMs\":60000,\"playing\":true,\"seq\":1}"
               + (padAfter > 0 ? ",\"history\":\"" + new string('x', padAfter) + "\"" : "") + "}";

        // On the injected clock, which moves only when told to: no frame goes stale and no ping is due while a check waits.
        await using (var desk = new ShowBusTests.ScriptedDesk([], Older))
        {
            using var link = Link(desk.Port, Token, time: new LinkClock(1_700_000_000_000));
            link.Start();
            var paired = await Until(() => link.Pairing == PlayoutPairing.Paired);
            var first = Clip("big.mp4", padAfter: 300_000);
            desk.Say("STATE " + first);
            var read = await Until(() => link.Video is { File: "big.mp4" });
            Check(paired && read && link.Oversize == 1 && link.Record.Counts.Oversize == 1 && link.Record.Flags.Contains("oversize") && link.Malformed == 0 && link.Connected
                  && link.Health == LinkHealth.Starting && link.Value is { Title: "big.mp4" }
                  && link.Status.EndsWith(" · " + PlayoutWire.OversizeWords(Encoding.UTF8.GetByteCount(first)) + ", read in part", StringComparison.Ordinal),
                  $"playout link: a STATE past 256 KB with its clip first is read in part — the clip followed, the link up and amber with the size, counted by the core ({link.Health}: {link.Status})");
            var second = Clip("late.mp4", padBefore: 300_000);
            desk.Say("STATE " + second);
            var gone = await Until(() => link.Oversize == 2 && link.Video is null);
            Check(gone && link.Value is null && link.Health == LinkHealth.Starting && link.Connected
                  && link.Status == PlayoutWire.OversizeWords(Encoding.UTF8.GetByteCount(second)) + ": its clip on air lies past the part read — no countdown",
                  $"playout link: one whose clip lies past the part read gives no countdown — never the last clip kept ({link.Status})");
            var before = link.Status;
            desk.Say("ERR " + new string('x', 300_000));
            Check(await Until(() => link.Oversize == 3) && link.Status == before && link.Health == LinkHealth.Starting,
                  $"playout link: any other line past 256 KB is counted and not read ({link.Status})");
            desk.Say("STATE " + Clip("small.mp4"));
            Check(await Until(() => link.Video is { File: "small.mp4" }) && link.Health == LinkHealth.Ok && link.Oversize == 3 && link.Status.StartsWith("small.mp4", StringComparison.Ordinal),
                  $"playout link: the next whole STATE is read and the link is green again ({link.Health}: {link.Status})");
            var green = link.Status;
            desk.Say("STATE " + Clip("\\ud800x"));
            var counted = await Until(() => link.Malformed == 1);
            counted = counted && desk.SayBytes([.. "STATE {\"video\":{\"file\":\""u8, 0xC3, 0x28, .. "\",\"positionMs\":1,\"lengthMs\":2,\"playing\":true}}"u8])
                      && await Until(() => link.Malformed == 2);
            Check(counted && link.Connected && link.Health == LinkHealth.Ok && link.Status == green && link.Oversize == 3,
                  $"playout link: a STATE escaping half a character, or not UTF-8, is counted malformed and the link stays up and green ({link.Malformed}: {link.Health}: {link.Status})");
            desk.Say("""STATE {"video":"x"}""");
            Check(await Until(() => link.Status.Contains("in a shape this link does not read", StringComparison.Ordinal)) && link.Health == LinkHealth.Starting
                  && link.Video is null && link.Value is null && link.Connected && link.Malformed == 2,
                  $"playout link: a STATE whose video is of another shape is amber with no countdown, never 'no clip on air' ({link.Status})");
        }

        await using (var desk = new ShowBusTests.ScriptedDesk(["NONCE " + Nonce], _ => []))
        {
            using var link = Link(desk.Port, "AB12 CD34", time: new LinkClock(1_700_000_000_000));
            link.Start();
            var said = await Until(() => link.Status.Contains(PlayoutWire.InvisibleCharacter, StringComparison.Ordinal));
            Check(said && link.Health == LinkHealth.Starting && link.Pairing == PlayoutPairing.None && !desk.Heard.Any(l => l.Contains("AUTH", StringComparison.Ordinal)),
                  $"playout link: a token Show Bus 1.1 desks read apart sends no proof and says so ({link.Status})");
        }
    }

    // ------------------------------------------------------------------ the core's rules, on an injected clock

    /// <summary>
    /// A 1.1 desk paired by a link on the injected clock: true once the link has read the desk's answer to its own STATUS ask, the last
    /// line the desk sends before the clock moves — so nothing the desk said at pairing lands after a test has moved the clock on.
    /// </summary>
    static async Task<bool> Paired(PlayoutLink link) => await Until(() => link.Pairing == PlayoutPairing.Paired && link.Video is { File: "asked.mp4" });

    /// <summary>
    /// The clock moved on by <paramref name="ms"/>, a ping's answer (if any) let in before the next move: the evaluator runs once at each
    /// step of <paramref name="step"/> ms, and the step waits until the link has heard the desk at that moment, or a short real while.
    /// </summary>
    static async Task Live(LinkClock clock, PlayoutLink link, long ms, long step = 2000)
    {
        for (var moved = 0L; moved < ms; moved += step)
        {
            clock.Advance(Math.Min(step, ms - moved));
            await Until(() => link.RecordNow().HeardMs == 0, 300);
        }
    }

    /// <summary>
    /// Request ids are <c>s&lt;n&gt;-&lt;inst&gt;</c>, counted for the process: a link made again with the same inst goes on counting, so no
    /// id is ever repeated (13.12.1; today's link counted <c>s&lt;n&gt;</c> from 1 each run).
    /// </summary>
    static async Task Ids(Action<bool, string> Check)
    {
        var inst = NewInst();
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, tag => Ok(tag));
        var ids = new List<string>();
        for (var run = 0; run < 2; run++)
        {
            using var link = Link(desk.Port, Token, inst: inst);
            var answers = new ConcurrentQueue<PlayoutAnswer>();
            link.Answered += answers.Enqueue;
            link.Start();
            await Paired(link);
            link.Restart();
            link.EndIn(10);
            await Until(() => answers.Count == 2);
            ids.AddRange(answers.Select(a => a.Id));
        }
        var heard = desk.Heard.Where(l => l.Contains(" VIDEO ", StringComparison.Ordinal)).Select(l => Split(l).Tag[1..]).ToList();
        Check(ids.SequenceEqual(["s1-" + inst, "s2-" + inst, "s3-" + inst, "s4-" + inst]) && heard.SequenceEqual(ids),
              $"playout ids: each command carries s<n>-<inst>, counted for the process across links made again: never one id twice ({string.Join(", ", heard)})");
    }

    /// <summary>
    /// At most 32 commands wait for answers on a link: the 33rd is not sent, in 13.12.8's words, and nothing waiting is evicted; with no
    /// answer each waits through 30 s — an answer at 30000 ms still counts — and is unconfirmed past it, never failed.
    /// </summary>
    static async Task InFlight(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst());
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        link.Start();
        var paired = await Paired(link);
        var sent = Enumerable.Range(0, 32).Select(_ => link.Restart()).ToList();
        var refused = link.Restart();
        var written = await Until(() => desk.Heard.Count(l => l.EndsWith(" VIDEO RESTART", StringComparison.Ordinal)) == 32);
        await Task.Delay(200);
        Check(paired && sent.All(x => x is null) && refused == "32 commands are waiting on Patterns's answers" && written && link.InFlight == 32
              && desk.Heard.Count(l => l.EndsWith(" VIDEO RESTART", StringComparison.Ordinal)) == 32 && answers.IsEmpty,
              $"playout in flight: 32 commands wait, the 33rd is not sent, with 13.12.8's words, and none is evicted ({refused}; {link.InFlight})");
        await Live(clock, link, 30_000);
        var waiting = link.InFlight;
        clock.Advance(1);
        var settled = link.InFlight;
        clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
        var told = await Until(() => answers.Count == 32);
        Check(waiting == 32 && settled == 0 && told && answers.All(a => a is { Outcome: PeerOutcome.Unconfirmed, Words: "no answer in 30 s — it may have run", Line: "VIDEO RESTART" })
              && link.State == PeerLinkState.Live && link.Record.Counts.Unconfirmed == 32,
              $"playout in flight: with no answer each waits through 30 s and is unconfirmed past it, it may have run — never failed; the link stays live ({answers.Count} told)");
        Check(link.Restart() is null && link.InFlight == 1, "playout in flight: once they stopped waiting, the next command goes");
    }

    /// <summary>
    /// Nothing crosses a drop (§4, 13.15.5): a command written before the desk dropped the link is unconfirmed, in 13.12.3's words; one
    /// asked for while the link is down is not sent and never queued, so the desk never hears it once the link is back.
    /// </summary>
    static async Task Drops(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        link.Start();
        var paired = await Paired(link);
        var written = link.Restart() is null && await Until(() => desk.Heard.Any(l => l.EndsWith(" VIDEO RESTART", StringComparison.Ordinal)));
        desk.Drop();
        var down = await Until(() => !link.Connected && answers.Count == 1);
        var a = answers.TryPeek(out var first) ? first : default;
        Check(paired && written && down && a is { Outcome: PeerOutcome.Unconfirmed, Words: "the link dropped before Patterns answered" }
              && link.State == PeerLinkState.Down && link.Record.Attempt == 1,
              $"playout drops: a command written before the link dropped is unconfirmed — it may have run — never failed ({a})");
        var refused = link.EndIn(10);
        clock.Advance(PeerLink.RedialWait(0, 0.5));
        var back = await Paired(link);
        await Task.Delay(200);
        Check(refused == "Patterns isn't connected — nothing sent" && back && !desk.Heard.Any(l => l.EndsWith(" VIDEO END 10", StringComparison.Ordinal)) && answers.Count == 1,
              $"playout drops: a command asked for while the link is down is not sent and not queued: the desk never hears it once the link is back ({refused})");
    }

    /// <summary>
    /// 13.10.2: a pong is a clock sample only when it answers one of the link's last pings: one that answers a t1 never sent, or one sent
    /// unasked, is counted unmatched and is no sample, however plausible its stamps; one that answers is believed — and a desk 5 s ahead
    /// is CLOCKS APART once its error is taken off. A clock over a link on this PC may schedule; over a plain link off it, never (13.11.6).
    /// </summary>
    static async Task Pongs(Action<bool, string> Check)
    {
        foreach (var local in new[] { true, false })
        {
            var clock = new LinkClock(1_700_000_000_000);
            await using var desk = new FakeDesk();
            var mode = "unmatched";
            Desk11(desk, Token, () => 0, _ => null);
            var desk11 = desk.Answer;
            desk.Answer = l =>
            {
                var (tag, body) = Split(l);
                if (!body.StartsWith("PING ", StringComparison.Ordinal) || tag.Length > 0) return desk11(l);
                var t1 = long.Parse(body[5..], CultureInfo.InvariantCulture);
                return mode == "unmatched" ? [FormattableString.Invariant($"OK PONG {t1 + 1} {t1 + 1} {t1 + 1}")] : [FormattableString.Invariant($"OK PONG {t1} {t1 + 5000} {t1 + 5000}")];
            };
            using var link = Link(desk.Port, Token, local ? null : Remote, clock, NewInst());
            link.Start();
            var paired = await Paired(link);
            clock.Advance(2000);
            var unmatched = await Until(() => link.RecordNow().Counts.Unmatched == 1);
            desk.Say(FormattableString.Invariant($"OK PONG {clock.WallMs - 5} {clock.WallMs} {clock.WallMs}"));
            var forged = await Until(() => link.RecordNow().Counts.Unmatched == 2);
            var none = link.RecordNow().Samples == 0 && link.ClockOffset is null && link.State == PeerLinkState.Live;
            mode = "honest";
            clock.Advance(2000);
            var sampled = await Until(() => link.RecordNow().Samples == 1);
            clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
            await Until(() => link.ClockOffset is not null);
            var where = local ? "on this PC" : "off this PC";
            Check(paired && unmatched && forged && none, $"playout pongs ({where}): a pong for a t1 never sent and one sent unasked are counted unmatched, and neither is a clock sample");
            Check(sampled && link.ClockOffset is { } off && Math.Abs(off.TotalMilliseconds - 5000) < 1 && link.ClockApart && link.Clock is { ErrMs: 1 },
                  $"playout pongs ({where}): a pong that answers a ping is a sample — the desk 5 s ahead, CLOCKS APART past 2 s less its error ({link.ClockOffset}, {link.Clock})");
            var record = link.RecordNow();
            Check(local ? link.CanSchedule(6) && record.Tls == "local" && !record.Flags.Contains("plain")
                        : !link.CanSchedule(6) && record.Tls == "none" && record.Flags.Contains("plain"),
                  $"playout pongs ({where}): " + (local ? "a fit clock over a link on this PC may schedule a timed command" : "a clock over a plain link off this PC never schedules a timed command, however fit (13.11.6)")
                  + $" ({record.ToJson()})");
        }
    }

    /// <summary>
    /// Silence (13.10.3): live through 6 s, stale past it — amber, no countdown — and down past 12 s, when the link closes and redials.
    /// The redial's wait (13.15.1) with j 0.5 is 500 ms, doubling to 5 s, and a desk that takes the connection and closes it is backed
    /// off; the count starts over only once a link that paired stayed up 10 s (13.15.2) — a pairing that ends sooner resets nothing.
    /// </summary>
    static async Task SilenceAndRedial(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        long? offset = 0;
        Desk11(desk, Token, () => offset, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        link.Start();
        var paired = await Paired(link);
        offset = null;   // the desk says nothing more
        var seen = new Dictionary<long, string>();
        for (var t = 1000L; t <= 6000; t += 1000) clock.Advance(1000);
        seen[6000] = link.RecordNow().State;
        clock.Advance(1);
        seen[6001] = link.RecordNow().State;
        clock.Advance(999);
        var stale = (link.Health, link.Status, link.Video);
        for (var t = 8000L; t <= 12_000; t += 1000) clock.Advance(1000);
        seen[12_000] = link.RecordNow().State;
        var taken = desk.Taken;
        clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
        var dropped = await Until(() => !link.Connected);
        var downRecord = link.RecordNow();
        Check(paired && seen[6000] == "live" && seen[6001] == "stale" && seen[12_000] == "stale",
              $"playout silence: live at link.stale.ms, stale past it and at link.drop.ms ({string.Join(", ", seen.Select(kv => kv.Key + " " + kv.Value))})");
        Check(stale.Health == LinkHealth.Starting && stale.Status.Contains(" quiet for 7 s — no countdown until it is heard again", StringComparison.Ordinal) && stale.Video is null,
              $"playout silence: stale is amber with the silence in words, and no countdown however fresh its last frame ({stale.Health}: {stale.Status})");
        Check(dropped && downRecord is { State: "down", Attempt: 1, NextMs: 500 } && link.Status.EndsWith("dialling again in 0.5 s", StringComparison.Ordinal),
              $"playout silence: down past link.drop.ms — the link closes the socket and waits 13.15.1's first wait ({downRecord.ToJson()}; {link.Status})");

        // The desk now takes each connection and closes it: each redial waits 13.15.1's next wait, never sooner.
        desk.Closing = true;
        var waits = new List<long>();
        var soon = true;
        for (var n = 1; n <= 5; n++)
        {
            // A wait is read only from a link that is down and waiting: one that is not has no wait to move the clock by.
            if (link.RecordNow().NextMs is not { } wait)
            {
                soon = false;
                break;
            }
            waits.Add(wait);
            clock.Advance(wait - 1);
            await Task.Delay(50);
            soon &= desk.Taken == taken;
            clock.Advance(1);
            soon &= await Until(() => desk.Taken == taken + 1 && link.RecordNow().Attempt == n + 1);
            taken = desk.Taken;
        }
        var last = link.RecordNow().NextMs;
        Check(soon && last is { } l && waits.Append(l).SequenceEqual([500L, 1000, 2000, 4000, 5000, 5000]),
              $"playout redial: each wait is 13.15.1's (j 0.5: from 500 ms, doubling to 5 s); a desk that takes the connection and closes it is backed off ({string.Join(", ", waits)}, {last})");

        // A pairing that ends within 10 s resets nothing; one that stays up 10 s starts the count over.
        desk.Closing = false;
        offset = 0;
        clock.Advance(last ?? 0);
        var again = await Paired(link);
        await Live(clock, link, 8000);
        desk.Drop();
        await Until(() => !link.Connected);
        var short8 = link.RecordNow();
        clock.Advance(Math.Max(0, short8.NextMs ?? 0));
        var again2 = await Paired(link);
        await Live(clock, link, 10_000);
        await Live(clock, link, 2000);
        desk.Drop();
        await Until(() => !link.Connected);
        var stable = link.RecordNow();
        Check(again && again2 && short8 is { Attempt: 7, NextMs: 5000 } && stable is { Attempt: 1, NextMs: 500 },
              $"playout redial: up 8 s after pairing the count goes on ({short8.Attempt}, {short8.NextMs} ms); up 10 s it starts over ({stable.Attempt}, {stable.NextMs} ms)");
    }

    /// <summary>
    /// A link that is not live is never green, the light every reader takes: the rule for each state, and a desk that resets the
    /// connection while a command goes to it — the reset found by the link's reader or by that write — leaves the link down with why it
    /// is, never under the live link's words or its countdown.
    /// </summary>
    static async Task NotGreenWhileDown(Action<bool, string> Check)
    {
        Check(Enum.GetValues<PeerLinkState>().Where(s => s != PeerLinkState.Live).All(s => PlayoutLink.HealthOf(s, LinkHealth.Ok) != LinkHealth.Ok)
              && PlayoutLink.HealthOf(PeerLinkState.Live, LinkHealth.Ok) == LinkHealth.Ok
              && PlayoutLink.HealthOf(PeerLinkState.Down, LinkHealth.Ok) == LinkHealth.NotFound
              && PlayoutLink.HealthOf(PeerLinkState.Down, LinkHealth.Error) == LinkHealth.Error
              && PlayoutLink.HealthOf(PeerLinkState.Down, LinkHealth.Starting) == LinkHealth.Starting
              && PlayoutLink.HealthOf(PeerLinkState.Connecting, LinkHealth.Ok) == LinkHealth.Starting
              && PlayoutLink.HealthOf(PeerLinkState.Stale, LinkHealth.Ok) == LinkHealth.Starting
              && PlayoutLink.HealthOf(PeerLinkState.Held, LinkHealth.Starting) == LinkHealth.Error
              && PlayoutLink.HealthOf(PeerLinkState.Refused, LinkHealth.Ok) == LinkHealth.Error,
              "playout light: green only while live — down, dialling, held or refused never takes the live link's green, and down keeps what was said of why");
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        link.Start();
        var paired = await Paired(link);
        var live = (link.Health, link.Status);
        desk.Reset();
        _ = link.Restart();   // written, unless the link has heard the reset already
        var down = await Until(() => !link.Connected);
        var seen = (link.Health, link.Status, link.State);
        Check(paired && live.Health == LinkHealth.Ok && live.Status.StartsWith("asked.mp4: ", StringComparison.Ordinal) && down && seen.State == PeerLinkState.Down
              && seen.Health == LinkHealth.NotFound && !seen.Status.Contains("asked.mp4", StringComparison.Ordinal)
              && seen.Status.EndsWith(" — dialling again in 0.5 s", StringComparison.Ordinal)
              && (seen.Status.StartsWith("Patterns closed the connection", StringComparison.Ordinal)
                  || seen.Status.StartsWith($"Patterns: the connection to 127.0.0.1:{desk.Port} broke", StringComparison.Ordinal)),
              $"playout light: a desk that resets the link leaves it down and not green, saying why — never its live words or countdown ({live.Status} → {seen.Health}: {seen.Status})");
    }

    /// <summary>
    /// A pinned node is dialled over TLS alone (13.4.2: for an id with a pin, the pin alone decides): its TLS carrier is the
    /// <c>playmap.lines.v1</c> channel on the door a hello names or the one kept beside its pin, and with neither — none here — the link
    /// dials nothing at all, amber, and says so, whether a person pinned it or it was pinned only on first sight. The line wire is
    /// never dialled for it, so nothing is sent on a plain socket (the plain flag on a link off this PC is held where a link has no
    /// pin, <see cref="PeerLinkScenarios"/>).
    /// </summary>
    static async Task Scheme(Action<bool, string> Check)
    {
        var fingerprint = string.Concat(Enumerable.Repeat("ab", 32));
        PlayoutLink Pinned(int port, PinTrust trust, LinkClock clock) => new(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = port, Token = Token, Inst = NewInst(), IsLoopback = Remote, Time = clock, Jitter = () => 0.5,
            Kept = new PeerKept(new PeerPin(null, fingerprint, trust), null, null, null),
        });
        {
            await using var desk = new FakeDesk();
            Desk11(desk, Token, () => 0, _ => null);
            using var link = Pinned(desk.Port, PinTrust.Person, new LinkClock(1_700_000_000_000));
            link.Start();
            var said = await Until(() => link.Status.Contains("never dialled plain", StringComparison.Ordinal));
            await Task.Delay(300);
            Check(said && desk.Taken == 0 && desk.Heard.IsEmpty && !link.Connected && link.Health == LinkHealth.Starting && link.State != PeerLinkState.Refused
                  && link.Pairing == PlayoutPairing.None && link.Status.Contains("no hello names its TLS door now", StringComparison.Ordinal),
                  $"playout scheme: a node a person pinned is never dialled plain — with no TLS door heard the link dials nothing, amber, and says so ({link.Health}: {link.Status}; {link.Record.ToJson()})");
        }
        {
            await using var desk = new FakeDesk();
            Desk11(desk, Token, () => 0, _ => null);
            using var link = Pinned(desk.Port, PinTrust.First, new LinkClock(1_700_000_000_000));
            link.Start();
            var said = await Until(() => link.Status.Contains("never dialled plain", StringComparison.Ordinal));
            await Task.Delay(300);
            Check(said && desk.Taken == 0 && desk.Heard.IsEmpty && !link.Connected && link.Health == LinkHealth.Starting && link.State != PeerLinkState.Refused
                  && link.Kept.Pin is { Trust: PinTrust.First } && link.Status.Contains("none is kept beside its pin", StringComparison.Ordinal),
                  $"playout scheme: a pin seen first decides the transport too — no TLS door heard or kept, nothing is dialled, amber, and it says so ({link.Health}: {link.Status})");
        }
    }

    /// <summary>
    /// 13.11.7: the clock's window empties when this PC's wall clock steps, either way — a sample measured before the step is believed no
    /// more — while a move within <see cref="PlayoutLink.WallStepMs"/> is no step; and each new connection starts with no sample, since a
    /// level-0 link never learns the desk's inst.
    /// </summary>
    static async Task WallClock(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        link.Start();
        var paired = await Paired(link);
        await Live(clock, link, 2000);
        var sampled = link.RecordNow().Samples == 1;
        clock.StepWall(PlayoutLink.WallStepMs);
        var within = link.RecordNow().Samples == 1;
        clock.StepWall(5000);
        var ahead = link.RecordNow().Samples == 0;
        clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
        var shown = link.Clock is null && link.ClockOffset is null && !link.ClockApart;
        await Live(clock, link, 2000);
        var again = link.RecordNow().Samples == 1;
        clock.StepWall(-5000);
        var back = link.RecordNow().Samples == 0;
        Check(paired && sampled && within && ahead && shown && again && back,
              $"playout clock: a step of this PC's wall clock either way empties the window, a move of {PlayoutLink.WallStepMs} ms does not (13.11.7) ({sampled}, {within}, {ahead}, {shown}, {again}, {back})");

        await Live(clock, link, 2000);
        var before = link.RecordNow().Samples;
        desk.Drop();
        await Until(() => !link.Connected);
        clock.Advance(PeerLink.RedialWait(0, 0.5));
        var relinked = await Paired(link);
        var fresh = link.RecordNow().Samples;
        Check(before == 1 && relinked && fresh == 0 && link.Clock is null,
              $"playout clock: a new connection starts with no sample — a level-0 link never learns the desk's inst, so it may be another desk ({before} → {fresh})");
    }

    /// <summary>
    /// 13.10.4: when the evaluator's own runs come further apart than link.selfgap.ms, the show's log gets one line naming the pause; at
    /// most one a minute, the next naming the pauses it folds in. A run link.selfgap.ms after the last is no pause.
    /// </summary>
    static async Task SelfGap(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        var lines = new ConcurrentQueue<string>();
        link.Logged += lines.Enqueue;
        link.Start();
        var paired = await Paired(link);
        await Live(clock, link, 4000);
        var none = lines.IsEmpty;
        clock.Advance(3000);
        var first = lines.ToList();
        await Until(() => link.RecordNow().HeardMs == 0, 300);
        clock.Advance(2500);
        var folded = lines.Count == 1 && link.State == PeerLinkState.Live;
        await Live(clock, link, 60_000);
        clock.Advance(2500);
        var all = lines.ToList();
        Check(paired && none && first is ["Patterns link: this PC paused for 3 s — Patterns pinged at once, and not called down for the pause"] && folded
              && all.Count == 2 && all[1] == "Patterns link: this PC paused for 2.5 s — Patterns pinged at once, and not called down for the pause (and 1 more pause since the last line)",
              $"playout self-gap: one line names the evaluator's own pause, at most one a minute, the next naming those it folds in ({string.Join(" | ", all)})");
    }

    /// <summary>
    /// A desk at its connection ceiling says so in words and closes (13.14) — Patterns' "ERR busy — …" before it reads a line. The link
    /// shows those words, takes them for no older desk and sends it nothing, not even a token to a desk on this PC; and it backs off.
    /// </summary>
    static async Task Busy(Action<bool, string> Check)
    {
        const string busy = "busy — 64 connections already open on this port; close one first";   // Patterns' WireLimits.BusyWords
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk { Greeting = () => ["ERR " + busy] };
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        link.Start();
        var shown = await Until(() => !link.Connected && link.State == PeerLinkState.Down && link.Status.Contains(busy, StringComparison.Ordinal));
        var first = (link.Health, link.Status, Record: link.RecordNow());
        clock.Advance(first.Record.NextMs ?? 0);
        var again = await Until(() => desk.Taken == 2 && !link.Connected && link.RecordNow().Attempt == 2);
        await Task.Delay(100);
        Check(shown && first.Health == LinkHealth.NotFound && first.Status == $"Patterns at 127.0.0.1:{desk.Port} is {busy} — dialling again in 0.5 s"
              && first.Record is { Attempt: 1, NextMs: 500 } && again && link.RecordNow().NextMs == 1000 && link.Status.Contains(busy, StringComparison.Ordinal)
              && !desk.Heard.Any(l => l.Contains("AUTH", StringComparison.Ordinal)),
              $"playout busy: a desk at its ceiling is shown in its own words, never called older, sent no token, and backed off ({first.Status}; {string.Join(" | ", desk.Heard)})");
    }

    /// <summary>The desk's words for a command's outcome: what the desk said, never a "done" it did not say.</summary>
    static void Words(Action<bool, string> Check)
    {
        static string Said(PeerOutcome o, string w) => new PlayoutAnswer("s1-5e6f7a8b", "VIDEO END 10", o, w).StatusLine("Patterns");
        Check(Said(PeerOutcome.Applied, "jumped") == "Patterns did it: VIDEO END 10 (jumped)"
              && Said(PeerOutcome.Refused, "no clip") == "Patterns refused VIDEO END 10: no clip"
              && Said(PeerOutcome.Unconfirmed, "no answer in 30 s — it may have run") == "Patterns: VIDEO END 10 unconfirmed — no answer in 30 s — it may have run"
              && Said(PeerOutcome.Accepted, "") == "Patterns took VIDEO END 10 and has not run it yet"
              && Said(PeerOutcome.NotSent, "32 commands are waiting on Patterns's answers") == "Patterns: VIDEO END 10 not sent — 32 commands are waiting on Patterns's answers",
              "playout words: the status line says the outcome the link read — applied, refused with the desk's words, unconfirmed, taken, not sent — never done on faith");

        // 13.12.10: a row when the outcome first settles, one more only when an unconfirmed one is resolved; none for a command never written.
        var accepted = new PlayoutAnswer("s1-5e6f7a8b", "CUE GO", PeerOutcome.Accepted, "requested");
        var thenApplied = accepted with { Outcome = PeerOutcome.Applied, Words = "cut", Before = PeerOutcome.Accepted };
        var unconfirmed = new PlayoutAnswer("s2-5e6f7a8b", "VIDEO RESTART", PeerOutcome.Unconfirmed, "no answer in 30 s — it may have run");
        var resolved = unconfirmed with { Outcome = PeerOutcome.Applied, Words = "restarted", Before = PeerOutcome.Unconfirmed };
        var acceptedRows = new[] { accepted, thenApplied }.Select(a => a.JournalLine("Patterns")).OfType<string>().ToList();
        var unconfirmedRows = new[] { unconfirmed, resolved }.Select(a => a.JournalLine("Patterns")).OfType<string>().ToList();
        Check(acceptedRows.SequenceEqual(["Patterns took CUE GO and has not run it yet (requested)"])
              && unconfirmedRows.SequenceEqual(["Patterns: VIDEO RESTART unconfirmed — no answer in 30 s — it may have run",
                  "Patterns did it: VIDEO RESTART (restarted) — it was unconfirmed until then"])
              && (accepted with { Outcome = PeerOutcome.Unconfirmed, Before = PeerOutcome.Accepted }).JournalLine("Patterns") is null
              && (unconfirmed with { Outcome = PeerOutcome.NotSent, Before = null }).JournalLine("Patterns") is null,
              $"playout words: the show log's rows — accepted then applied is one row, the status line saying the rest; unconfirmed then applied is two; a command never written none ({string.Join(" | ", acceptedRows.Concat(unconfirmedRows))})");
    }

    // ------------------------------------------------------------------ the writer lane, holds kept, the show log

    /// <summary>
    /// The writer lane: no caller and not the evaluator waits on a write. While the socket holds one write up, a command asked for comes
    /// back at once, queued behind it; the evaluator runs on, and its published state moves to stale on the desk's silence; past
    /// <see cref="PlayoutLink.WriteDeadline"/> of the held write, on the link's clock, it ends the connection and says why. The command
    /// being written is unconfirmed — a byte of it may have reached the desk — with a row for the show log; the one queued behind it is
    /// not-sent, since none of it did, with none; neither is sent again once the link is back.
    /// </summary>
    static async Task WriterLane(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);
        var streams = new ConcurrentQueue<HeldStream>();
        using var link = new PlayoutLink(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = NewInst(), Time = clock, Jitter = () => 0.5,
            Wrap = s =>
            {
                var held = new HeldStream(s);
                streams.Enqueue(held);
                return held;
            },
        });
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        link.Start();
        var paired = await Paired(link);
        var stream = streams.Last();
        stream.HoldWhen = line => line.Contains(" VIDEO RESTART", StringComparison.Ordinal);
        var first = link.Restart();
        var holding = await Until(() => stream.Holding);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var second = link.EndIn(10);
        var record = link.RecordNow();
        var asked = watch.ElapsedMilliseconds;
        var inFlight = link.InFlight;
        for (var t = 0; t < 25; t++) clock.Advance(250);   // 6.25 s on the link's clock: each step runs the evaluator once
        var stale = link.State;
        Check(paired && holding && first is null && second is null && asked < 1000 && record.State == "live" && inFlight == 2 && stale == PeerLinkState.Stale
              && link.Connected,
              $"playout lane: with a write held, a command asked for comes back at once ({asked} ms), queued behind it, and the evaluator runs on — the desk's silence makes the link stale ({stale}, {inFlight} in flight)");
        for (var t = 0; t < 16; t++) clock.Advance(250);   // 10.25 s since the held write began
        var ended = await Until(() => !link.Connected && answers.Count == 2);
        var restart = answers.FirstOrDefault(a => a.Line == "VIDEO RESTART");
        var end = answers.FirstOrDefault(a => a.Line == "VIDEO END 10");
        Check(ended && restart is { Outcome: PeerOutcome.Unconfirmed, Words: "the link dropped before Patterns answered" }
              && end is { Outcome: PeerOutcome.NotSent, Words: "the link dropped before it was written — it did not run; nothing is queued" }
              && restart.JournalLine("Patterns") == restart.StatusLine("Patterns") && end.JournalLine("Patterns") is null
              && link.Status.StartsWith($"Patterns: a write to 127.0.0.1:{desk.Port} did not finish in 10 s — dialling again in", StringComparison.Ordinal)
              && link.State == PeerLinkState.Down,
              $"playout lane: past the write deadline the evaluator ends the connection and says why; the command being written is unconfirmed, the one behind it not-sent, and only the first has a row for the show log ({restart}; {end}; {link.Status})");
        clock.Advance(PeerLink.RedialWait(0, 0.5));
        var back = await Paired(link);
        await Task.Delay(200);
        Check(back && !desk.Heard.Any(l => l.Contains(" VIDEO ", StringComparison.Ordinal)),
              $"playout lane: once the link is back, neither command is sent again — nothing is queued across a drop ({string.Join(" | ", desk.Heard)})");
    }

    /// <summary>
    /// A command the core settled while it waited on the lane is never written: four commands each hold the lane in a write of 8 s on
    /// the link's clock — under the write deadline — while the desk keeps the link live, so the fifth, asked for with them, waits on the
    /// lane past 30 s. The core settles it not-sent ("it did not run"), and when the lane reaches it, it is passed over: the desk never
    /// hears it, its outcome stays not-sent with no row for the show log, and the lane writes on.
    /// </summary>
    static async Task LaneSettled(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);   // it answers no command
        var streams = new ConcurrentQueue<HeldStream>();
        var inst = NewInst();
        using var link = new PlayoutLink(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = desk.Port, Token = Token, Inst = inst, Time = clock, Jitter = () => 0.5,
            Wrap = s =>
            {
                var held = new HeldStream(s);
                streams.Enqueue(held);
                return held;
            },
        });
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Answered += answers.Enqueue;
        link.Start();
        var paired = await Paired(link);
        var stream = streams.Last();
        stream.HoldWhen = line => line.Contains(" VIDEO RESTART", StringComparison.Ordinal);
        var asked = new List<string?>();
        for (var i = 0; i < 4; i++) asked.Add(link.Restart());
        asked.Add(link.EndIn(10));   // asked last: on the lane behind the four
        var fifth = PeerLink.RequestId(PlayoutLink.IdPrefix, 5, inst);
        var holds = 0;
        var live = true;
        var pings = 0;
        for (var n = 1; n <= 4; n++)
        {
            if (!await Until(() => stream.Held == n)) break;
            for (var t = 1; t <= 32; t++)
            {
                clock.Advance(250);
                if (t % 8 != 0) continue;
                desk.Say(State("pushed.mp4"));   // the desk is heard every 2 s, though the lane holds the link's pings
                live &= await Until(() => link.RecordNow().HeardMs == 0, 1000);
            }
            holds++;
            // The pings the desk heard before the last write is let go: those the lane holds behind the fifth command come after.
            if (n == 4) pings = desk.Heard.Count(l => l.StartsWith("PING ", StringComparison.Ordinal));
            stream.Release();
        }
        var settled = link.OutcomeOf(fifth);
        var wroteOn = await Until(() => desk.Heard.Count(l => l.StartsWith("PING ", StringComparison.Ordinal)) > pings);
        await Task.Delay(200);
        var told = answers.Where(a => a.Id == fifth).ToList();
        Check(paired && asked.All(a => a is null) && holds == 4 && live && link.State == PeerLinkState.Live && settled == PeerOutcome.NotSent && wroteOn
              && desk.Heard.Count(l => l.EndsWith(" VIDEO RESTART", StringComparison.Ordinal)) == 4 && !desk.Heard.Any(l => l.Contains(" VIDEO END ", StringComparison.Ordinal))
              && told is [{ Outcome: PeerOutcome.NotSent, Words: "not written in 30 s — it did not run", Line: "VIDEO END 10" }] && told[0].JournalLine("Patterns") is null
              && link.OutcomeOf(fifth) == PeerOutcome.NotSent,
              $"playout lane: a command the core settled not-sent while the lane held it past 30 s — it did not run — is passed over, never written: the desk never hears it, its outcome stays not-sent with no row, and the lane writes on ({string.Join(" | ", told)}; {holds} holds; {string.Join(" | ", desk.Heard.Where(l => l.Contains("VIDEO", StringComparison.Ordinal)))})");
    }

    /// <summary>
    /// 13.15.3's hold, kept: a refused proof holds the token a minute, kept with its end — the store takes it in the step that refused
    /// it, and its lane puts it on disk; a link made again from a store read back from that file — a restart — is held for what is left
    /// of the minute and dials nothing until it ends; the hold's end is kept too, and the desk's next refusal holds again. A token changed
    /// in Settings is proved at once: the hold was for the token it held.
    /// </summary>
    static async Task Holds(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);   // it pairs Token's proof and refuses any other: "wrong token"
        using var file = Scratch.File("kept", ".json");
        var first = PlayoutKeptStore.Tag("the token as Settings stores it");
        var kept = new ConcurrentQueue<PeerKept>();
        // Each run reads the file afresh, as the desk's store does when Stagewatch starts.
        PlayoutLink Make(PlayoutKeptStore store, string token, string tag) => new(new PlayoutLinkOptions
        {
            Host = "127.0.0.1", Port = desk.Port, Token = token, Inst = NewInst(), Time = clock, Jitter = () => 0.5,
            Kept = store.For(PlayoutRules.PatternsName, tag, clock.WallMs),
            Keep = k =>
            {
                kept.Enqueue(k);
                return store.Keep(PlayoutRules.PatternsName, tag, k, clock.WallMs);
            },
        });
        var start = clock.WallMs;
        var run1 = new PlayoutKeptStore(file.Path);
        using (var link = Make(run1, "ZZ99YY88", first))
        {
            link.Start();
            var held = await Until(() => link.State == PeerLinkState.Held && kept.Count == 1);
            var landed = await run1.Flushed;
            var onDisk = PlayoutKeptStore.Parse(File.Exists(file.Path) ? File.ReadAllText(file.Path) : null);
            var k1 = kept.TryPeek(out var k) ? k : null;
            Check(held && landed && k1 is { HeldUntilWall: { } until, HeldRefusal: "key", Pin: null, Floor: null } && until == start + PeerLink.HoldRefusedMs
                  && onDisk.TryGetValue(PlayoutRules.PatternsName, out var entry) && entry.Kept == k1 && entry.Tag == first && desk.Taken == 1
                  && link.Health == LinkHealth.Error && link.Status.Contains("wrong token — the token is held; next try in 60 s", StringComparison.Ordinal),
                  $"playout holds: a refused proof holds the token a minute, kept with its end in the step that refused it and on disk by the store's lane ({k1}; {link.Status})");
        }
        clock.Advance(20_000);
        var taken = desk.Taken;
        var run2 = new PlayoutKeptStore(file.Path);   // a restart: the file read back
        using (var again = Make(run2, "ZZ99YY88", first))
        {
            again.Start();
            await Task.Delay(200);
            var rec = again.RecordNow();
            var still = again.State == PeerLinkState.Held && again.Health == LinkHealth.Error && rec is { NextMs: 40_000, Refusal: "key" } && desk.Taken == taken;
            clock.Advance(40_000 - 1);
            await Task.Delay(200);
            var notYet = desk.Taken == taken && again.State == PeerLinkState.Held;
            clock.Advance(1);
            var redialled = await Until(() => desk.Taken == taken + 1 && kept.Count == 3);
            var list = kept.ToList();
            var landed = await run2.Flushed;
            Check(still && notYet && redialled && landed && list[1].HeldUntilWall is null && list[2].HeldUntilWall == clock.WallMs + PeerLink.HoldRefusedMs,
                  $"playout holds: a restart keeps the hold — held for what is left of the minute, no dial until it ends; its end is kept, and the desk's next refusal holds again ({rec.ToJson()}; {string.Join(" | ", list)})");
        }
        var run3 = new PlayoutKeptStore(file.Path);
        using (var changed = Make(run3, Token, PlayoutKeptStore.Tag("the new token as Settings stores it")))
        {
            changed.Start();
            var paired = await Paired(changed);
            Check(paired && desk.Taken == taken + 2 && run3.For(PlayoutRules.PatternsName, first, clock.WallMs)?.HeldUntilWall is not null,
                  "playout holds: a token changed in Settings is proved at once — the kept hold, read back after a restart, is the old token's, and stays its");
        }
    }

    /// <summary>
    /// The store of what playout links keep: a whole entry back as it was written; a hold only for the token it was for, while the pin,
    /// floor and node stay; a restored hold ends within <see cref="PlayoutKeptStore.HoldMaxMs"/>, whatever the file says; a keep is the
    /// store's at once and on disk by its lane, latest wins; nothing kept removes the entry; a file or a field it cannot read is dropped,
    /// never guessed — a refusal outside 13.16.3's set holds as key, a kind outside the hello's is none; a full store makes room by the
    /// entry kept longest ago; a file it cannot write is said; and the token, or the form Settings stores it in, is in no file.
    /// </summary>
    static async Task KeptStore(Action<bool, string> Check)
    {
        var fp = string.Concat(Enumerable.Repeat("0a", 32));
        var full = new PeerKept(new PeerPin("9f8e7d6c", fp, PinTrust.Person), new PeerFloor(true, 1, "mutual"), 1_700_000_060_000, "locked", "9f8e7d6c", "patterns");
        const string blob = "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA";
        var tag = PlayoutKeptStore.Tag(blob);
        var links = new Dictionary<string, PlayoutKeptStore.Entry>
        {
            ["patterns"] = new(tag, full, 5),
            ["stage-left"] = new("", new PeerKept(null, null, 1_700_000_060_000, "key"), 6),
        };
        var json = PlayoutKeptStore.ToJson(links);
        var back = PlayoutKeptStore.Parse(json);
        Check(back.Count == 2 && back["patterns"] == links["patterns"] && back["stage-left"] == links["stage-left"],
              "playout kept: each link's entry comes back as it was written — hold, refusal, pin, floor, node and the token's tag");
        Check(Regex.IsMatch(tag, "^[0-9a-f]{16}$") && tag != PlayoutKeptStore.Tag(blob + "x") && PlayoutKeptStore.Tag("") == "" && PlayoutKeptStore.Tag(null) == ""
              && !json.Contains(blob, StringComparison.Ordinal) && !json.Contains(blob[..12], StringComparison.Ordinal),
              "playout kept: a token's tag is 16 hex of the form Settings stores it in, another for another, and that form is in no file");

        const long now = 1_700_000_000_000;
        using (var file = Scratch.File("kept", ".json"))
        {
            var store = new PlayoutKeptStore(file.Path);
            var keeping = store.Keep("patterns", tag, full, 5);
            var at = store.For("patterns", tag, now);   // the store's at once, whether or not its lane has written it yet
            var wrote = await keeping;
            var reread = new PlayoutKeptStore(file.Path);
            var other = reread.For("patterns", PlayoutKeptStore.Tag("another"), now);
            Check(at == full && wrote && reread.For("patterns", tag, now) == full && other == full with { HeldUntilWall = null, HeldRefusal = null }
                  && reread.For("nobody", tag, now) is null,
                  "playout kept: a keep is the store's at once and on disk by its lane; read back after a restart, a link's hold is its own only for the token it was for; its pin, floor and node are kept whatever the token");
            var far = full with { HeldUntilWall = now + 365L * 24 * 3600 * 1000 };
            await reread.Keep("patterns", tag, far, 6);
            var capped = new PlayoutKeptStore(file.Path).For("patterns", tag, now);
            Check(capped is { HeldUntilWall: { } until } && until == now + PlayoutKeptStore.HoldMaxMs && PlayoutKeptStore.HoldMaxMs == PeerLink.HoldRefusedMs
                  && capped == far with { HeldUntilWall = now + PlayoutKeptStore.HoldMaxMs },
                  $"playout kept: a hold the file says ends a year on — a damaged file, a wall clock stepped back — ends within peerlink.hold.refused.ms of the restart, the longest hold a level-0 link starts ({capped})");
            var latest = Enumerable.Range(0, 20).Select(i => reread.Keep("patterns", tag, full with { HeldUntilWall = now + i }, 7 + i)).ToList();
            var all = await Task.WhenAll(latest);
            var last = PlayoutKeptStore.Parse(File.ReadAllText(file.Path));
            Check(all.All(x => x) && last["patterns"].Kept.HeldUntilWall == now + 19,
                  "playout kept: keeps made faster than the lane writes are written whole, the latest winning, each told it landed");
            var cleared = await reread.Keep("patterns", tag, new PeerKept(null, null, null, null), 30);
            Check(cleared && reread.Links.Count == 0 && File.Exists(file.Path) && PlayoutKeptStore.Parse(File.ReadAllText(file.Path)).Count == 0,
                  "playout kept: a link that keeps nothing more leaves the file");
        }

        const string bad = """
            {"v":1,"links":{"Bad Name":{"heldUntil":5},"stage-left":{"tag":"XYZ","heldUntil":5,"refusal":"Key!","pin":{"fingerprint":"ab","trust":"person"},
             "floor":{"tls":true,"level":2,"proof":"mutual"},"node":"a b"},"empty":{"tag":""},"patterns":{"heldUntil":"5","pin":{"fingerprint":"%FP%","trust":"nobody"}},
             "stage-right":{"heldUntil":7,"refusal":"forgiven","kind":"robot","node":"9f8e7d6c","floor":{"tls":false,"level":0,"proof":"trusted"}},
             "booth":{"heldUntil":8,"refusal":"locked","kind":"playmap"}}}
            """;
        var parsed = PlayoutKeptStore.Parse(bad.Replace("%FP%", fp, StringComparison.Ordinal));
        Check(parsed.Count == 3 && parsed["stage-left"] is { Tag: "", Kept: { HeldUntilWall: 5, HeldRefusal: "key", Pin: null, Floor: null, NodeId: null, NodeKind: null } }
              && parsed["stage-right"] is { Kept: { HeldUntilWall: 7, HeldRefusal: "key", NodeId: "9f8e7d6c", NodeKind: null, Floor: null } }
              && parsed["booth"] is { Kept: { HeldUntilWall: 8, HeldRefusal: "locked", NodeKind: "playmap" } }
              && PlayoutKeptStore.Parse("not json").Count == 0 && PlayoutKeptStore.Parse("{\"v\":2,\"links\":{\"patterns\":{\"heldUntil\":5}}}").Count == 0
              && PlayoutKeptStore.Parse(null).Count == 0 && PlayoutKeptStore.Parse("[1]").Count == 0,
              $"playout kept: a file or a field it cannot read is dropped, never guessed — a bad name, tag, pin, floor or node; a refusal outside 13.16.3's set holds as key, a kind outside the hello's is none; an entry keeping nothing; another version ({string.Join(", ", parsed.Keys)})");

        var room = new PlayoutKeptStore(null);
        var hold = new PeerKept(null, null, 1_700_000_060_000, "key");
        for (var i = 0; i < PlayoutKeptStore.MaxEntries; i++) await room.Keep("l" + i.ToString(CultureInfo.InvariantCulture), "", hold, 100 + i);
        await room.Keep("l3", "", hold, 500);
        await room.Keep("new", "", hold, 600);
        Check(room.Links.Count == PlayoutKeptStore.MaxEntries && !room.Links.ContainsKey("l0") && room.Links.ContainsKey("l3") && room.Links.ContainsKey("new"),
              "playout kept: a full store makes room for a new link by the entry kept longest ago");

        using var folder = Scratch.Folder("kept");
        var unwritable = new PlayoutKeptStore(folder.Path);
        Check(unwritable.Links.Count == 0 && !await unwritable.Keep("patterns", tag, full, 5) && !await unwritable.Flushed,
              "playout kept: a file it cannot read is no holds, and one it cannot write is said (false), never thrown");
    }

    /// <summary>
    /// The show log's rows (13.16.7, 13.19.4, 13.12.10): one per change of the link's state — the peer and its address, from, to, why,
    /// attempt — its start among them; a change said within the minute before is a repeat, folded into one summary row a minute — a
    /// redial going round, the link coming live again — never a row each. An outcome's row comes when it first settles, and one more
    /// when an unconfirmed one is resolved, at the link's time. No row carries the token.
    /// </summary>
    static async Task Journal(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        Desk11(desk, Token, () => 0, _ => null);   // it answers no command
        var inst = NewInst();
        using var link = Link(desk.Port, Token, time: clock, inst: inst, jitter: () => 0.5);
        var rows = new ConcurrentQueue<PlayoutLinkChange>();
        var answers = new ConcurrentQueue<PlayoutAnswer>();
        link.Changed += rows.Enqueue;
        link.Answered += answers.Enqueue;
        link.Start();
        var peer = $"Patterns link (127.0.0.1:{desk.Port.ToString(CultureInfo.InvariantCulture)})";
        var paired = await Paired(link) && await Until(() => rows.Count == 2);
        var start = rows.ToList();
        Check(paired && start is [{ From: PeerLinkState.Off, To: PeerLinkState.Connecting, Folded: 0 }, { From: PeerLinkState.Connecting, To: PeerLinkState.Live, Attempt: 0 }]
              && start[0].Line("Patterns").StartsWith(peer + ": off → connecting — ", StringComparison.Ordinal)
              && start[1].Line("Patterns").StartsWith(peer + ": connecting → live — paired with 127.0.0.1:", StringComparison.Ordinal)
              && start[1].Line("Patterns").EndsWith(" (attempt 0)", StringComparison.Ordinal) && start[1].At.ToUnixTimeMilliseconds() == clock.WallMs,
              $"playout log: the link's start and its pairing are a row each, at the link's time — the peer and its address, from, to, its words and the attempt ({string.Join(" | ", start.Select(r => r.Line("Patterns")))})");

        // A drop and the link back within the minute: the drop and the redial are new changes, a row each; the link coming live again
        // was said a moment before, so it is a repeat — folded into the summary a minute after it, never a row of its own.
        desk.Drop();
        var dropped = await Until(() => !link.Connected && clock.OneShotWaiting);
        clock.Advance(link.RecordNow().NextMs ?? 0);
        var again = await Paired(link) && await Until(() => rows.Count == 4);
        await Task.Delay(100);
        var quick = rows.Skip(2).ToList();
        Check(dropped && again && rows.Count == 4 && quick.Select(r => (r.From, r.To, r.Folded)).SequenceEqual([(PeerLinkState.Live, PeerLinkState.Down, 0), (PeerLinkState.Down, PeerLinkState.Connecting, 0)])
              && link.State == PeerLinkState.Live
              && quick[0].Line("Patterns") == peer + ": live → down — Patterns closed the connection — dialling again in 0.5 s (attempt 1)",
              $"playout log: a drop and the redial are a row each, the peer's address in each; the link live again, said a moment before, is no row of its own ({string.Join(" | ", quick.Select(r => r.Line("Patterns")))})");
        await Live(clock, link, 62_000);
        var folded = await Until(() => rows.Count == 5);
        var summary = rows.Last();
        Check(folded && summary is { Folded: 1, From: PeerLinkState.Connecting, To: PeerLinkState.Live } && summary.FoldedMs >= PlayoutLink.RepeatSummaryEvery.TotalMilliseconds
              && summary.Line("Patterns").StartsWith(peer + ": 1 more change of state in ", StringComparison.Ordinal)
              && summary.Line("Patterns").Contains(", each one said before — the last connecting → live — paired with 127.0.0.1:", StringComparison.Ordinal),
              $"playout log: the repeat is said in the minute's summary, which names the last change it folds ({summary.Line("Patterns")})");

        // An outcome's rows: unconfirmed with no answer in 30 s, then the desk's late answer resolves it.
        link.Restart();
        var id = PeerLink.RequestId(PlayoutLink.IdPrefix, 1, inst);
        await Until(() => desk.Heard.Any(l => l.StartsWith("@" + id + " ", StringComparison.Ordinal)));
        await Live(clock, link, 30_000);
        clock.Advance(1);
        clock.Advance(PlayoutLink.CheckEvery.Ticks / TimeSpan.TicksPerMillisecond);
        var unconfirmed = await Until(() => answers.Count == 1);
        desk.Say("OK @" + id + " restarted late");
        var moved = await Until(() => answers.Count == 2);
        var said = answers.ToList();
        Check(unconfirmed && moved && said[0] is { Outcome: PeerOutcome.Unconfirmed, Before: null } && said[0].JournalLine("Patterns") == said[0].StatusLine("Patterns")
              && said[1] is { Outcome: PeerOutcome.Applied, Before: PeerOutcome.Unconfirmed, Words: "restarted late" }
              && said[1].JournalLine("Patterns") == "Patterns did it: VIDEO RESTART (restarted late) — it was unconfirmed until then"
              && said[1].At.ToUnixTimeMilliseconds() == clock.WallMs && rows.Count == 5,
              $"playout log: an outcome's row when it first settles and one more when an unconfirmed one is resolved ({string.Join(" | ", said.Select(a => a.JournalLine("Patterns")))})");

        // The desk drops the link and closes each connection: a row for each new change, the repeats folded, a summary a minute.
        var mark = rows.Count;
        desk.Closing = true;
        desk.Drop();
        var down = await Until(() => !link.Connected && link.State == PeerLinkState.Down && clock.OneShotWaiting);
        var dials = 0;
        var went = 0L;
        while (went < PlayoutLink.RepeatSummaryEvery.TotalMilliseconds + 10_000 && down)
        {
            var wait = link.RecordNow().NextMs ?? 0;
            var attempt = link.RecordNow().Attempt;
            clock.Advance(wait);
            went += wait;
            dials++;
            down = await Until(() => link.RecordNow().Attempt == attempt + 1 && clock.OneShotWaiting);
        }
        var cycling = rows.Skip(mark).ToList();
        desk.Closing = false;
        clock.Advance(link.RecordNow().NextMs ?? 0);
        var back = await Paired(link) && await Until(() => rows.Last().To == PeerLinkState.Live);
        var after = rows.Skip(mark).ToList();
        var summaries = after.Where(r => r.Folded > 0).ToList();
        var changes = after.Sum(r => r.Folded > 0 ? r.Folded : 1);
        Check(down && back && dials >= 10 && cycling.Take(3).Select(r => (r.From, r.To)).SequenceEqual([(PeerLinkState.Live, PeerLinkState.Down), (PeerLinkState.Down, PeerLinkState.Connecting), (PeerLinkState.Connecting, PeerLinkState.Down)])
              && summaries.Count >= 1 && summaries[0].FoldedMs >= PlayoutLink.RepeatSummaryEvery.TotalMilliseconds
              && summaries[0].Line("Patterns").StartsWith(peer + ": ", StringComparison.Ordinal) && summaries[0].Line("Patterns").Contains(" more changes of state in ", StringComparison.Ordinal)
              && after.Count <= 3 + 3 + 2 && after.Count < dials && changes == 1 + 2 * dials + 2 && after[^1] is { From: PeerLinkState.Connecting, To: PeerLinkState.Live, Folded: 0 },
              $"playout log: a redial going round is a row for each new change and one summary a minute — {after.Count} rows for {changes} changes over {dials} dials — and the link coming live, not said within the minute, is said at once ({string.Join(" | ", after.Select(r => r.Line("Patterns")))})");
        Check(rows.Concat(after).All(r => !r.Line("Patterns").Contains(Token, StringComparison.Ordinal)) && said.All(a => a.JournalLine("Patterns")?.Contains(Token, StringComparison.Ordinal) != true),
              "playout log: no row carries the token");
    }

    /// <summary>
    /// 13.19.4 on a flapping link: a desk whose show stalls every few seconds takes the link live → stale → live ten times. The first
    /// of each change is a row; a repeat of a change said within the minute is folded into a summary a minute, and a change last said
    /// longer ago is said again — so twenty changes over a minute or more are a handful of rows, never a row or two per flap; the last
    /// names the link live again.
    /// </summary>
    static async Task Flapping(Action<bool, string> Check)
    {
        var clock = new LinkClock(1_700_000_000_000);
        await using var desk = new FakeDesk();
        long? offset = 0;
        Desk11(desk, Token, () => offset, _ => null);
        using var link = Link(desk.Port, Token, time: clock, inst: NewInst(), jitter: () => 0.5);
        var rows = new ConcurrentQueue<PlayoutLinkChange>();
        link.Changed += rows.Enqueue;
        link.Start();
        var paired = await Paired(link) && await Until(() => rows.Count == 2);
        var flaps = 0;
        var went = 0L;
        int Pings() => desk.Heard.Count(l => l.StartsWith("PING ", StringComparison.Ordinal));
        for (var n = 0; n < 10; n++)
        {
            offset = null;   // the desk's show stalls: it answers no ping
            var stale = false;
            for (var t = 0; t < 60 && !stale; t++)
            {
                clock.Advance(250);
                went += 250;
                stale = link.State == PeerLinkState.Stale;
            }
            offset = 0;      // and runs again: the next ping the desk hears is answered, and the clock waits for its pong
            var pings = Pings();
            var live = false;
            for (var t = 0; t < 16 && !live; t++)
            {
                clock.Advance(250);
                went += 250;
                if (await Until(() => Pings() > pings, 50)) live = await Until(() => link.State == PeerLinkState.Live, 3000);
            }
            if (stale && live) flaps++;
        }
        await Live(clock, link, 62_000);   // a quiet minute: the last summary is said
        var said = rows.Skip(2).ToList();
        var own = said.Where(r => r.Folded == 0).ToList();
        var summaries = said.Where(r => r.Folded > 0).ToList();
        var changes = said.Sum(r => r.Folded > 0 ? r.Folded : 1);
        // At least twenty changes: a pong the machine is slow to hand over can add a flap of its own, which only adds to the fold.
        Check(paired && flaps == 10 && went >= PlayoutLink.RepeatSummaryEvery.TotalMilliseconds && changes >= 20 && said.Count * 3 <= changes
              && own.Take(2).Select(r => (r.From, r.To)).SequenceEqual([(PeerLinkState.Live, PeerLinkState.Stale), (PeerLinkState.Stale, PeerLinkState.Live)])
              && summaries.Count >= 1 && said.Count <= 6 && said[^1] is { From: PeerLinkState.Stale, To: PeerLinkState.Live },
              $"playout log: a link flapping live and stale ten times is {said.Count} rows for {changes} changes over {(went / 1000).ToString(CultureInfo.InvariantCulture)} s — the first of each change and a summary a minute ({string.Join(" | ", said.Select(r => r.Line("Patterns")))})");
    }
}
