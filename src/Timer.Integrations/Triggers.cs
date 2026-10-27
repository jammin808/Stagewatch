using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Stagewatch.Core;
using Stagewatch.Remote;

namespace Stagewatch.Integrations;

/// <summary>Values a trigger payload can use: {ITEM} {TITLE} {PHASE} {REMAINING} {DETAIL} {ROOM}.</summary>
public sealed record TriggerContext(int Item, string Title, string Phase, string Remaining, string Detail, string Room);

/// <summary>
/// What an HTTP trigger's fire came to (Show Bus 13.12.3): the rule, the kind of its auth profile (<see cref="TriggerAuthRules.None"/>
/// for none), where it went (<c>host:port</c>), the outcome and its words, and the request id it carried (null for none). Its words
/// never hold the secret, an address's query or the payload.
/// </summary>
public sealed record TriggerSent(DateTimeOffset At, string Rule, string Kind, string Where, PeerOutcome Outcome, string Words, string? RequestId)
{
    public bool Authenticated => Kind != TriggerAuthRules.None;

    /// <summary>The fire as the show log and the status line say it: <c>Trigger 'GO VT' → 10.0.0.12:9940 (playmap): applied — …</c>.</summary>
    public string Row => $"Trigger '{LoadNotes.Shown(Rule)}' → {Where}{(Authenticated ? " (" + Kind + ")" : "")}: {Outcome.Word()}{(Words.Length > 0 ? " — " + Words : "")}";
}

/// <summary>
/// What a <see cref="TriggerRunner"/> is set to: how an auth profile's secret is resolved (the desk's DPAPI; nothing by default), the
/// inst its request ids end with (Show Bus 13.12.1), where its targets' pins are kept, which node a hello heard at a host and port
/// names, whether an address is this PC, its clock, the HTTP deadline, and who hears each HTTP fire's outcome.
/// </summary>
public sealed record TriggerRunnerOptions
{
    /// <summary>A profile's protected secret resolved for this fire; "" when it cannot be (kept for another Windows user or machine).</summary>
    public Func<string, string> Unprotect { get; init; } = _ => "";

    /// <summary>This process's hello <c>inst</c>: every request id a trigger sends ends with it.</summary>
    public string Inst { get; init; } = ShowBusHello.NewInst();

    /// <summary>The pins of HTTPS targets: kept across a restart by the desk, in memory alone by default.</summary>
    public TriggerPinStore Pins { get; init; } = new(null);

    /// <summary>The node id of the hello heard at a host and port (<see cref="TriggerPinStore.NodeAt"/>), or null; none by default.</summary>
    public Func<string, int, string?>? NodeAt { get; init; }

    /// <summary>Whether an address is this PC (13.1's local); a test plays another machine from loopback by answering false.</summary>
    public Func<IPAddress, bool> IsLoopback { get; init; } = PlayoutGate.IsLoopback;

    /// <summary>Resolves a target's host name (the system's resolver by default); a test plays a name that resolves elsewhere.</summary>
    public Func<string, CancellationToken, Task<IPAddress[]>> Resolve { get; init; } = Dns.GetHostAddressesAsync;

    /// <summary>The clock a hold (its end in wall time) and an HTTP deadline count on.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>How long an HTTP fire may take to reach its target — the connection and, over HTTPS, the handshake — before anything is sent.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long an HTTP fire waits for its answer once its request may have gone: an acceptor's own deadline (13.12's
    /// <c>receipt.deadline.ms</c>) and a margin, so an <c>accepted</c> said at that deadline is read, never taken for silence.
    /// </summary>
    public TimeSpan AnswerWait { get; init; } = TimeSpan.FromMilliseconds(PeerLink.ReceiptDeadlineMs) + TimeSpan.FromSeconds(2);

    /// <summary>Hears each HTTP fire's outcome once it settles, on the trigger worker: a row for the show log, never a secret.</summary>
    public Action<TriggerSent>? Sent { get; init; }
}

/// <summary>
/// Outbound triggers: "when X happens, do Y" — Stagewatch driving lights, playback, Companion
/// pages or other Patterns tools rather than only being driven. The show thread only matches rules
/// and enqueues; a worker does the network I/O with short timeouts, so a dead target never delays
/// the timer. Each rule fires at most once a second (no feedback storms).
/// An HTTP trigger with an auth profile sends its secret only over a channel that keeps it (<see cref="TriggerAuthRules"/>): to this
/// PC — the socket's address checked as it connects — or over HTTPS to a certificate a person pinned on this desk; its first certificate
/// is pinned <c>first</c> and sent nothing, and a changed one is refused until a person types it (Show Bus 13.8). Its address is fixed
/// text (show text fills its path and query alone, escaped), it follows no redirect and goes through no proxy, it carries a request id
/// (<c>tr&lt;n&gt;-&lt;inst&gt;</c>: <c>X-Request-Id</c>, or before a Patterns line), and its answer is read by 13.12.3 (<see cref="Read"/>).
/// A secret its target refuses (401) or that is rate limited (429) is held — every rule that resolves to it, kept across a new runner
/// and a restart (<see cref="TriggerPinStore.Hold"/>) — and not sent again until its hold ends or a new secret is set. Every fire with
/// a profile settles as one <see cref="TriggerSent"/>, whatever ends it.
/// </summary>
public sealed class TriggerRunner : IIntegration
{
    /// <summary>The most of an answer's body read: an answer is a receipt, never more.</summary>
    public const int MaxReplyBytes = 64 * 1024;

    /// <summary>The most characters of a target's words an outcome keeps.</summary>
    public const int WordsMax = 200;

    /// <summary>Where on the desk a person trusts a trigger target's certificate, as every refusal names it.</summary>
    public const string TrustWhere = "right-click the status line";

    /// <summary>The most targets met this run that the runner keeps in mind (the oldest making room); node ids come from hellos anyone can send.</summary>
    public const int MetMax = 64;

    /// <summary>The most rules whose last fire with a profile the light keeps (the oldest making room).</summary>
    public const int LastMax = 64;

    private static long s_requests;

    /// <summary>
    /// The key a held secret's mark is drawn with: this process's, random, never written — so equal secrets share a hold within the
    /// process, and nothing on disk is a hash a secret could be guessed against.
    /// </summary>
    private static readonly byte[] s_markKey = RandomNumberGenerator.GetBytes(32);

    private readonly Func<IReadOnlyList<TriggerRule>> _rules;
    private readonly Action<string> _runCommand;
    private readonly TriggerRunnerOptions _o;
    private readonly Channel<(TriggerRule Rule, TriggerContext Ctx)> _queue =
        Channel.CreateBounded<(TriggerRule, TriggerContext)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    // Deadlines ride on each call's token (the options' clock), never on the client's own timeout.
    private readonly HttpClient _http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private readonly UdpClient _udp = new();
    private readonly Dictionary<TriggerRule, long> _lastFired = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private readonly Lock _gate = new();
    // Under _gate: the HTTPS targets met this run whose pins may wait for a person (address key → the rule, where, the node the hello
    // there named at the last fire, and when it was met), the changed certificates that wait for a typed trust (address key → shown and
    // pinned), and each rule's last fire with a profile (rule → outcome, when). Holds are the pin store's, which outlives a runner.
    private readonly Dictionary<string, (string Rule, string Where, string? NodeId, long Seq)> _met = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Shown, string Pinned)> _changed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (PeerOutcome Outcome, long Seq)> _lastAuth = new(StringComparer.Ordinal);
    private long _seq;
    private string _last = "ready";

    /// <param name="runCommand">Runs a Stagewatch command line through the app's normal input path.</param>
    public TriggerRunner(Func<IReadOnlyList<TriggerRule>> rules, Action<string> runCommand, TriggerRunnerOptions? options = null)
    {
        _rules = rules;
        _runCommand = runCommand;
        _o = options ?? new TriggerRunnerOptions();
        _worker = Task.Run(WorkAsync);
    }

    public string Name => "Triggers";

    /// <summary>
    /// Red while a target's certificate changed or a secret is held after its target refused it; amber while a certificate is pinned
    /// first only, the pins were lost, or any rule's last fire with a profile was not sent or is unconfirmed (until its next answer).
    /// </summary>
    public LinkHealth Health
    {
        get
        {
            var held = _o.Pins.Holds(WallMs()).Count > 0;
            lock (_gate)
            {
                if (_changed.Count > 0 || held) return LinkHealth.Error;
                return _o.Pins.Lost || FirstWaiting().Any() || Unsettled().Any() ? LinkHealth.Starting : LinkHealth.Ok;
            }
        }
    }

    /// <summary>
    /// The last fire's words, then what waits for a person — a changed certificate, one pinned first only, a held secret, lost pins — and
    /// each rule whose last fire with a profile was not sent or is unconfirmed.
    /// </summary>
    public string Status
    {
        get
        {
            var holds = _o.Pins.Holds(WallMs());
            lock (_gate)
            {
                var parts = new List<string> { _last };
                foreach (var key in _changed.Keys)
                    parts.Add($"CERTIFICATE CHANGED at {WhereOf(key)} — nothing sent ({TrustWhere})");
                foreach (var key in FirstWaiting())
                    parts.Add($"{WhereOf(key)}'s certificate is pinned first only — trust it ({TrustWhere})");
                foreach (var (h, _) in holds) parts.Add("held: " + h.Why);
                if (_o.Pins.Lost) parts.Add(TriggerPinStore.LostWords);
                foreach (var (rule, outcome) in Unsettled())
                    parts.Add($"'{LoadNotes.Shown(rule)}' was {outcome.Word()} at its last fire");
                return string.Join(" · ", parts);
            }
        }
    }

    /// <summary>The rules whose last fire with a profile was not sent or is unconfirmed (under the lock).</summary>
    private IEnumerable<(string Rule, PeerOutcome Outcome)> Unsettled()
        => _lastAuth.Where(kv => kv.Value.Outcome is PeerOutcome.NotSent or PeerOutcome.Unconfirmed)
            .OrderBy(kv => kv.Value.Seq).Select(kv => (kv.Key, kv.Value.Outcome));

    private long WallMs() => _o.Time.GetUtcNow().ToUnixTimeMilliseconds();

    public int Fired { get; private set; }

    /// <summary>Owner thread. Matches and queues; never blocks.</summary>
    public void Fire(TriggerEvent ev, string filterValue, TriggerContext ctx)
    {
        var now = Environment.TickCount64;
        foreach (var r in _rules())
        {
            if (!r.Enabled || r.Event != ev) continue;
            if (!FilterMatches(r.Filter, filterValue)) continue;
            if (_lastFired.TryGetValue(r, out var last) && now - last < 1000) continue;
            _lastFired[r] = now;
            if (r.Action == TriggerAction.Command) { _runCommand(Expand(r.Payload, ctx)); Fired++; continue; }
            _queue.Writer.TryWrite((r, ctx));
        }
    }

    /// <summary>
    /// A rule's filter against what happened. The event may offer several things to match, separated by '|' (an item
    /// start offers "3|kind:talk|label:VT|group:Morning"), so one rule can follow an item, a label, a group or a kind.
    /// </summary>
    public static bool FilterMatches(string ruleFilter, string offered)
    {
        var f = ruleFilter.Trim();
        if (f.Length == 0) return true;
        foreach (var alt in offered.Split('|'))
            if (f.Equals(alt.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Timecode triggers: rules whose filter is a timecode fire once as rolling LTC passes it.</summary>
    public void FireTimecode(TimeSpan previous, TimeSpan now, int fps, TriggerContext ctx, bool dropFrame = false)
    {
        foreach (var r in _rules())
        {
            if (!r.Enabled || r.Event != TriggerEvent.Timecode) continue;
            if (Timecode.Parse(r.Filter, fps, dropFrame) is not { } point || !Timecode.Crossed(previous, now, point)) continue;
            if (r.Action == TriggerAction.Command) { _runCommand(Expand(r.Payload, ctx)); Fired++; continue; }
            _queue.Writer.TryWrite((r, ctx));
        }
    }

    public static string Expand(string text, TriggerContext c) => Expand(text, c, v => v);

    /// <summary>
    /// The placeholders filled in, each value through <paramref name="value"/>: an authenticated rule's path and query take them
    /// escaped (<see cref="Uri.EscapeDataString"/>), so show text never changes the address's shape.
    /// </summary>
    public static string Expand(string text, TriggerContext c, Func<string, string> value) => text
        .Replace("{ITEM}", value(c.Item.ToString(CultureInfo.InvariantCulture)), StringComparison.OrdinalIgnoreCase)
        .Replace("{TITLE}", value(c.Title), StringComparison.OrdinalIgnoreCase)
        .Replace("{PHASE}", value(c.Phase), StringComparison.OrdinalIgnoreCase)
        .Replace("{REMAINING}", value(c.Remaining), StringComparison.OrdinalIgnoreCase)
        .Replace("{DETAIL}", value(c.Detail), StringComparison.OrdinalIgnoreCase)
        .Replace("{ROOM}", value(c.Room), StringComparison.OrdinalIgnoreCase);

    private async Task WorkAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_queue.Reader.TryRead(out var job))
                {
                    if (job.Rule.Action == TriggerAction.Http)
                    {
                        var attempt = new Attempt(TriggerAuthRules.KindOf(job.Rule));
                        try
                        {
                            Settle(await HttpAsync(job.Rule, job.Ctx, attempt));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !_cts.IsCancellationRequested)
                        {
                            // Whatever ended it, the fire settles: unconfirmed once anything may have reached its target, else not sent.
                            // Never the exception's message: it may quote the address, and a secret with it.
                            var gone = attempt.MayHaveGone;
                            Settle(new TriggerSent(_o.Time.GetUtcNow(), job.Rule.Name, attempt.Kind, attempt.Where,
                                gone ? PeerOutcome.Unconfirmed : PeerOutcome.NotSent,
                                $"failed — {ex.GetType().Name}; {(gone ? "it may have run" : "not sent")}", attempt.Id));
                        }
                        continue;
                    }
                    try
                    {
                        await RunAsync(job.Rule, job.Ctx);
                        Fired++;
                        Said($"last: {job.Rule.Name} ({job.Rule.Action}) OK");
                    }
                    catch (Exception ex)
                    {
                        Said($"last: {job.Rule.Name} failed — {ex.Message}");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void Said(string words)
    {
        lock (_gate) _last = words;
    }

    /// <summary>An HTTP fire's outcome: the status's last words (with the rule's warning, if any), the light's record of a fire with a profile, the count, and its hearer.</summary>
    private void Settle(TriggerSent sent)
    {
        if (sent.Outcome != PeerOutcome.NotSent) Fired++;
        var rule = _rules().FirstOrDefault(r => r.Name == sent.Rule && r.Action == TriggerAction.Http);
        var warning = rule is null ? null : TriggerAuthRules.Warning(rule);
        lock (_gate)
        {
            _last = "last: " + sent.Row + (warning is null ? "" : " · " + warning);
            if (sent.Authenticated)
            {
                if (!_lastAuth.ContainsKey(sent.Rule) && _lastAuth.Count >= LastMax)
                    _lastAuth.Remove(_lastAuth.MinBy(kv => kv.Value.Seq).Key);
                _lastAuth[sent.Rule] = (sent.Outcome, ++_seq);
            }
        }
        _o.Sent?.Invoke(sent);
    }

    /// <summary>One HTTP fire as far as it went: what its row names, and whether anything may have reached its target.</summary>
    private sealed class Attempt(string kind)
    {
        public string Kind { get; } = kind;
        public string Where { get; set; } = "its target";
        public string? Id { get; set; }
        private volatile bool _gone;

        /// <summary>Set once the request may have been written: from then on, an end with no answer is unconfirmed, never not sent.</summary>
        public bool MayHaveGone { get => _gone; set => _gone = value; }
    }

    private async Task RunAsync(TriggerRule r, TriggerContext ctx)
    {
        var payload = Expand(r.Payload, ctx);
        switch (r.Action)
        {
            case TriggerAction.Osc:
            {
                var ep = ParseEndPoint(r.Target, 53000);
                var (address, args) = ParseOsc(payload);
                var packet = OscCodec.EncodeMessage(address, args);
                await _udp.SendAsync(packet, packet.Length, ep);
                break;
            }
            case TriggerAction.CompanionPress:
            {
                var ep = ParseEndPoint(r.Target, 8000);
                var loc = payload.Trim().Trim('/');
                if (loc.Split('/').Length != 3) throw new FormatException("Companion button must be page/row/column");
                using var deadline = new CancellationTokenSource(_o.Timeout, _o.Time);
                using var either = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, deadline.Token);
                using var resp = await _http.PostAsync($"http://{ep.Address}:{ep.Port}/api/location/{loc}/press", new StringContent(""), either.Token);
                resp.EnsureSuccessStatusCode();
                break;
            }
        }
    }

    // ------------------------------------------------------------------ HTTP

    /// <summary>An HTTP fire: one with an auth profile by <see cref="AuthenticatedAsync"/>, one with none as before, both read by 13.12.3.</summary>
    private Task<TriggerSent> HttpAsync(TriggerRule r, TriggerContext ctx, Attempt attempt)
        => TriggerAuthRules.Carries(r) ? AuthenticatedAsync(r, ctx, attempt) : PlainAsync(r, ctx, attempt);

    private static string Seconds(TimeSpan span) => span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// A rule with no auth profile, as it ran before: its whole address filled in, through the shared client (which follows
    /// redirects), its answer read by 13.12.3. Its words never quote the address, so a secret in its query stays out of them.
    /// </summary>
    private async Task<TriggerSent> PlainAsync(TriggerRule r, TriggerContext ctx, Attempt attempt)
    {
        var url = Expand(r.Target, ctx);
        var payload = Expand(r.Payload, ctx);
        var where = attempt.Where = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Where(uri) : "its target";
        var secrets = QueryValues(url);
        TriggerSent Out(PeerOutcome outcome, string words, bool theirs = false)
            => new(_o.Time.GetUtcNow(), r.Name, TriggerAuthRules.None, where, outcome, Clean(words, secrets, theirs), null);
        if (uri is null || uri.Scheme is not ("http" or "https")) return Out(PeerOutcome.NotSent, "its target is not an http:// or https:// address");
        var wait = _o.Timeout + _o.AnswerWait;
        using var deadline = new CancellationTokenSource(wait, _o.Time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, deadline.Token);
        try
        {
            using var request = new HttpRequestMessage(payload.Length == 0 ? HttpMethod.Get : HttpMethod.Post, uri);
            if (payload.Length > 0) request.Content = Content(payload);
            attempt.MayHaveGone = true;   // the shared client's connection is not seen here: from now on it may have gone
            using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, either.Token);
            var body = await ReadAtMostAsync(resp.Content, either.Token);
            var reading = Read((int)resp.StatusCode, body, RetryAfter(resp.Headers));
            return Out(reading.Outcome, reading.Words, theirs: true);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            return Out(PeerOutcome.Unconfirmed, $"no answer from {where} in {Seconds(wait)} s — it may have run");
        }
        catch (HttpRequestException ex)
        {
            return ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError
                ? Out(PeerOutcome.NotSent, $"could not reach {where} ({Describe(ex.HttpRequestError)})")
                : Out(PeerOutcome.Unconfirmed, $"the connection to {where} ended before it answered — it may have run");
        }
        catch (IOException)
        {
            // The answer's body cut off after its headers (HttpIOException): the request was written.
            return Out(PeerOutcome.Unconfirmed, $"the connection to {where} ended before it answered — it may have run");
        }
    }

    /// <summary>
    /// A rule with an auth profile. Refused before anything goes when the rules refuse it (<see cref="TriggerAuthRules.Problem"/>),
    /// when its secret cannot be read on this PC or cannot ride in a header, or while its secret is held. Then one connection of its own:
    /// to this PC only, the socket's address checked as it connects, for a target on this PC; else over HTTPS to the certificate a person
    /// pinned — by the target's address or by the node the hello heard there names (<see cref="TriggerPinStore.Effective"/>) — any other
    /// judged by <see cref="TriggerPinStore.Judge"/> in the handshake and sent nothing. No proxy, no redirect, no cookie. It has
    /// <see cref="TriggerRunnerOptions.Timeout"/> to reach its target and, once its request may have gone,
    /// <see cref="TriggerRunnerOptions.AnswerWait"/> for its answer.
    /// </summary>
    private async Task<TriggerSent> AuthenticatedAsync(TriggerRule r, TriggerContext ctx, Attempt attempt)
    {
        var kind = attempt.Kind;
        var parsed = TriggerAuthRules.ParseTarget(r.Target, out _);
        var where = attempt.Where = parsed?.Where ?? attempt.Where;
        var secret = "";
        TriggerSent Out(PeerOutcome outcome, string words, bool theirs = false)
            => new(_o.Time.GetUtcNow(), r.Name, kind, where, outcome, Clean(words, [secret], theirs), attempt.Id);

        if (TriggerAuthRules.Problem(r, _o.IsLoopback) is { } problem) return Out(PeerOutcome.NotSent, problem + " — not sent");
        var target = parsed!;
        secret = _o.Unprotect(r.Auth!.SecretProtected);
        if (secret.Length == 0)
            return Out(PeerOutcome.NotSent, $"its secret can't be read on this PC (kept for another Windows user or machine) — type it again in {TriggerAuthRules.SettingWords}; not sent");
        if (TriggerAuthRules.SecretProblem(secret) is { } bad) return Out(PeerOutcome.NotSent, bad + " — not sent");
        var mark = Mark(secret);
        if (HeldFor(target.Where, kind, mark, PlayoutKeptStore.Tag(r.Auth.SecretProtected)) is { } held) return Out(PeerOutcome.NotSent, held);

        var local = TriggerAuthRules.IsLocalHost(target.Host, _o.IsLoopback);
        var addrKey = TriggerPinStore.AddrKey(target);
        string? nodeKey = null;
        TriggerPin? byNode = null, byAddr = null;
        if (!local)
        {
            var nodeId = _o.NodeAt?.Invoke(target.Host, target.Port);
            nodeKey = TriggerPinStore.NodeKey(nodeId);
            byNode = nodeKey is null ? null : _o.Pins.Get(nodeKey);
            byAddr = _o.Pins.Get(addrKey);
            Met(addrKey, r.Name, target.Where, nodeKey is null ? null : nodeId);
        }
        // The address is fixed text and show text fills its path and query alone, escaped: what it reaches is checked against the rule.
        var url = target.Scheme + "://" + target.Authority + Expand(target.Rest, ctx, Uri.EscapeDataString);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Port != target.Port || !SameHost(uri, target))
            return Out(PeerOutcome.NotSent, "its address would change with show text — not sent");
        attempt.Id = PeerLink.RequestId(TriggerAuthRules.RequestIdPrefix, Interlocked.Increment(ref s_requests), _o.Inst);

        string? shown = null;
        TriggerPin? judged = null;
        var trusted = false;
        var notLocal = false;
        using var deadline = new CancellationTokenSource(_o.Timeout, _o.Time);
        using var either = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, deadline.Token);
        // From the moment the request may be written, the answer has AnswerWait: an acceptor's deadline and a margin.
        void Going()
        {
            attempt.MayHaveGone = true;
            deadline.CancelAfter(_o.AnswerWait);
        }
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = _o.Timeout,
            ConnectCallback = async (context, ct) =>
            {
                var addresses = IPAddress.TryParse(target.Host, out var ip) ? [ip] : await _o.Resolve(target.Host, ct);
                // A target on this PC is reached on this PC alone: a name that resolves elsewhere is sent nothing.
                if (local) addresses = addresses.Where(_o.IsLoopback).ToArray();
                if (addresses.Length == 0)
                {
                    notLocal = true;
                    throw new HttpRequestException(HttpRequestError.ConnectionError, "not this PC");
                }
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
                if (!target.Https) Going();
                return new NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                // 13.8.1: a certificate is judged by its fingerprint alone, never a name, chain or authority — on this PC any (13.1).
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert is null) return false;
                    shown = PlayoutWire.Fingerprint(cert.GetRawCertData());
                    judged = local ? null : TriggerPinStore.Effective(byNode, byAddr, shown);
                    trusted = local || judged is { Trust: not PinTrust.First } p && p.Fingerprint == shown;
                    if (trusted) Going();
                    return trusted;
                },
            },
        };
        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var payload = Expand(r.Payload, ctx);
        using var request = new HttpRequestMessage(payload.Length == 0 ? HttpMethod.Get : HttpMethod.Post, uri);
        foreach (var (name, value) in TriggerAuthRules.Headers(kind, secret, attempt.Id)) request.Headers.TryAddWithoutValidation(name, value);
        if (payload.Length > 0) request.Content = Content(TriggerAuthRules.Body(kind, attempt.Id, payload));
        try
        {
            using var resp = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, either.Token);
            // An answer over the certificate a person pinned: a change it was refused for before no longer waits.
            if (!local && trusted)
                lock (_gate) _changed.Remove(addrKey);
            var status = (int)resp.StatusCode;
            // A redirect after the request went may follow a command that ran (a 303 to its result): never followed, never called refused.
            if (status is >= 300 and < 400)
                return Out(PeerOutcome.Unconfirmed, $"{where} answered {status.ToString(CultureInfo.InvariantCulture)}, a redirect — not followed; it may have run; set its address to where it points");
            var body = await ReadAtMostAsync(resp.Content, either.Token);
            var reading = Read(status, body, RetryAfter(resp.Headers));
            if (status == 401) Hold(r, target.Where, kind, mark, PeerLink.HoldRefusedMs, $"{where} refused its secret (401) — set it again in {TriggerAuthRules.SettingWords}");
            else if (status == 429)
                Hold(r, target.Where, kind, mark, Math.Clamp(reading.RetryAfter * 1000L, PeerLink.HoldRefusedMs, TriggerPinStore.HoldMaxMs), $"{where} locked this PC out or rate limited it (429)");
            return Out(reading.Outcome, reading.Words, theirs: true);
        }
        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
        {
            return attempt.MayHaveGone
                ? Out(PeerOutcome.Unconfirmed, $"no answer from {where} in {Seconds(_o.AnswerWait)} s — it may have run")
                : Out(PeerOutcome.NotSent, $"no connection to {where} in {Seconds(_o.Timeout)} s");
        }
        catch (HttpRequestException ex)
        {
            if (notLocal) return Out(PeerOutcome.NotSent, $"{target.Host} is not this PC here — a secret goes over plain HTTP only to this PC; not sent");
            if (target.Https && !trusted && shown is not null && !local) return Out(PeerOutcome.NotSent, Judged(addrKey, nodeKey, target.Where, judged, shown));
            return !attempt.MayHaveGone || ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError
                ? Out(PeerOutcome.NotSent, $"could not reach {where} ({Describe(ex.HttpRequestError)})")
                : Out(PeerOutcome.Unconfirmed, $"the connection to {where} ended before it answered — it may have run");
        }
        catch (IOException)
        {
            // The answer's body cut off after its headers (HttpIOException): the request was written, so it may have run.
            return attempt.MayHaveGone
                ? Out(PeerOutcome.Unconfirmed, $"the connection to {where} ended before it answered — it may have run")
                : Out(PeerOutcome.NotSent, $"could not reach {where} (the connection ended)");
        }
    }

    /// <summary>
    /// 13.8.2–3 for a certificate the handshake refused: pinned first in place of none (or of another first pin), kept first, or
    /// CERTIFICATE CHANGED — each sent nothing — said in words that name the short form and where a person trusts it. A pin is kept by
    /// the target's address and, when a hello names its node, by the node too.
    /// </summary>
    private string Judged(string addrKey, string? nodeKey, string where, TriggerPin? pin, string shown)
    {
        var (_, keep, changed) = TriggerPinStore.Judge(pin, shown, WallMs());
        lock (_gate)
        {
            if (changed) _changed[addrKey] = (shown, pin!.Fingerprint);
            else _changed.Remove(addrKey);
        }
        if (keep is not null) Keep(addrKey, nodeKey, keep);
        var now = PlayoutWire.Short(shown);
        if (changed)
            return $"CERTIFICATE CHANGED: {where} now shows {now} …, not the certificate pinned for it ({PlayoutWire.Short(pin!.Fingerprint)} …) — nothing sent; "
                   + $"compare it on its own screen, then type at least {PlayoutRules.TrustHexMin.ToString(CultureInfo.InvariantCulture)} hex of it on this desk ({TrustWhere})";
        if (keep is not null && _o.Pins.Get(addrKey)?.Fingerprint != keep.Fingerprint)
            return $"{where} showed the certificate {now} … — not pinned: this PC keeps {TriggerPinStore.MaxEntries.ToString(CultureInfo.InvariantCulture)} certificates a person trusted already; nothing sent";
        if (pin is null)
            return $"{where} showed the certificate {now} … — pinned first; nothing sent. Compare it on its own screen, then trust it on this desk ({TrustWhere}) before its secret goes";
        if (keep is not null)
            return $"{where} now shows {now} …, not {PlayoutWire.Short(pin.Fingerprint)} … it showed first — pinned this one first; nothing sent. Compare it on its own screen, then trust it ({TrustWhere})";
        return $"{where}'s certificate {now} … is pinned first only — nothing sent until a person trusts it on this desk ({TrustWhere})";
    }

    /// <summary>Keeps a pin by the target's address and, when a hello named its node, by the node: a person's pin is found under either.</summary>
    private void Keep(string addrKey, string? nodeKey, TriggerPin pin)
    {
        _ = _o.Pins.Put(addrKey, pin);
        if (nodeKey is not null) _ = _o.Pins.Put(nodeKey, pin);
    }

    /// <summary>A target met over HTTPS this run, kept in mind for the desk's offer; at most <see cref="MetMax"/>, the one met longest ago making room.</summary>
    private void Met(string addrKey, string rule, string where, string? nodeId)
    {
        lock (_gate)
        {
            if (!_met.ContainsKey(addrKey) && _met.Count >= MetMax)
            {
                // The one met longest ago that waits on no typed trust, else the one met longest ago.
                var waiting = _met.Where(kv => !_changed.ContainsKey(kv.Key)).ToList();
                var room = (waiting.Count > 0 ? waiting : _met.ToList()).MinBy(kv => kv.Value.Seq).Key;
                _met.Remove(room);
                _changed.Remove(room);
            }
            _met[addrKey] = (rule, where, nodeId, ++_seq);
        }
    }

    /// <summary>The mark of a resolved secret: a keyed hash under this process's own key, never the secret and never written.</summary>
    private static string Mark(string secret) => Convert.ToHexStringLower(HMACSHA256.HashData(s_markKey, Encoding.UTF8.GetBytes(secret)));

    /// <summary>The words a held secret is refused with, or null when none is held for it at its target and kind now.</summary>
    private string? HeldFor(string where, string kind, string mark, string tag)
    {
        if (_o.Pins.Held(where, kind, mark, tag, WallMs()) is not { } held) return null;
        var left = (long)Math.Ceiling(held.LeftMs / 1000.0);
        return $"held: {held.Hold.Why}; {left.ToString(CultureInfo.InvariantCulture)} s more, or until a new secret is set — not sent";
    }

    /// <summary>
    /// Holds the secret <paramref name="firing"/> sent to <paramref name="where"/> as <paramref name="kind"/> for <paramref name="ms"/>:
    /// by its mark, and by the tag of every rule's protected form that resolves to it now (the firing rule's first), which is what holds
    /// it after a restart. Kept by the pin store, which outlives this runner, and written beside the pins with its end in wall time.
    /// </summary>
    private void Hold(TriggerRule firing, string where, string kind, string mark, long ms, string why)
    {
        var tags = new List<string> { PlayoutKeptStore.Tag(firing.Auth!.SecretProtected) };
        foreach (var rule in _rules())
        {
            if (tags.Count >= TriggerPinStore.TagsMax) break;
            if (!TriggerAuthRules.Carries(rule) || TriggerAuthRules.KindOf(rule) != kind || rule.Auth!.SecretProtected.Length == 0) continue;
            var tag = PlayoutKeptStore.Tag(rule.Auth.SecretProtected);
            if (tags.Contains(tag) || TriggerAuthRules.ParseTarget(rule.Target, out _)?.Where != where) continue;
            if (_o.Unprotect(rule.Auth.SecretProtected) is { Length: > 0 } other && Mark(other) == mark) tags.Add(tag);
        }
        _ = _o.Pins.Hold(new TriggerHold(where, kind, mark, tags, WallMs() + ms, ms, why), WallMs());
    }

    // ------------------------------------------------------------------ trust

    /// <summary>
    /// The certificates of HTTPS targets met this run that wait for a person: a changed one, which nothing goes to until a person types
    /// at least <see cref="PlayoutRules.TrustHexMin"/> hex of it, or one pinned first only, trusted by one press beside its short form.
    /// Each is offered by its address's key, with the node the hello there named at its last fire.
    /// </summary>
    public IReadOnlyList<TriggerCertificateWait> CertificatesWaiting()
    {
        lock (_gate)
        {
            var list = new List<TriggerCertificateWait>();
            foreach (var (key, met) in _met.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (_changed.TryGetValue(key, out var c))
                    list.Add(new TriggerCertificateWait(key, met.Where, met.Rule, c.Shown, true, c.Pinned, met.NodeId));
                else if (FirstOf(key, met.NodeId) is { } first)
                    list.Add(new TriggerCertificateWait(key, met.Where, met.Rule, first.Fingerprint, false, "", met.NodeId));
            }
            return list;
        }
    }

    /// <summary>
    /// A person on this desk trusts a target's certificate (13.8.2–3), and nothing else may: after CERTIFICATE CHANGED,
    /// <paramref name="hex"/> is what the person typed (<paramref name="typed"/>), at least <see cref="PlayoutRules.TrustHexMin"/> hex of
    /// the new certificate; for one pinned first, the whole fingerprint the press was drawn with, so a certificate pinned in its place
    /// since is never trusted by it. The pin is kept at once, by the target's address and by the node a hello named there, and written on
    /// the store's lane.
    /// </summary>
    public (bool Trusted, string Words) Trust(string key, string hex, bool typed)
    {
        var given = PlayoutRules.TrustHex(hex);
        string where;
        string? nodeId;
        TriggerPin? keep = null;
        (bool, string) result;
        lock (_gate)
        {
            where = WhereOf(key);
            nodeId = _met.TryGetValue(key, out var met) ? met.NodeId : null;
            if (_changed.TryGetValue(key, out var c))
            {
                if (!typed)
                    result = (false, $"{where}'s certificate changed: type at least {PlayoutRules.TrustHexMin.ToString(CultureInfo.InvariantCulture)} hex of the new one, compared on its own screen — nothing trusted");
                else if (given is null || !c.Shown.StartsWith(given, StringComparison.Ordinal))
                    result = (false, $"that is not how the certificate {where} shows starts ({PlayoutWire.Short(c.Shown)} …) — compare it on its own screen; nothing trusted");
                else
                {
                    keep = new TriggerPin(c.Shown, PinTrust.Person, WallMs());
                    _changed.Remove(key);
                    result = (true, $"{where}'s certificate {PlayoutWire.Short(c.Shown)} … trusted by a person on this desk in place of the one pinned before — its trigger's secret goes to it");
                }
            }
            else if (FirstOf(key, nodeId) is { } first)
            {
                if (given is null || given != first.Fingerprint)
                    result = (false, "a certificate pinned first is trusted by the press beside its short form, bound to its whole fingerprint — nothing trusted");
                else
                {
                    keep = new TriggerPin(first.Fingerprint, PinTrust.Person, WallMs());
                    result = (true, $"{where}'s certificate {PlayoutWire.Short(first.Fingerprint)} … trusted by a person on this desk — its trigger's secret goes to it, and a changed one is refused");
                }
            }
            else result = (false, $"no certificate of {where} waits for trust now — nothing changed");
        }
        if (keep is not null) Keep(key, TriggerPinStore.NodeKey(nodeId), keep);
        return result;
    }

    /// <summary>
    /// The first pin a target met waits on a person with — by its address, else by the node the hello there named — or null when a
    /// person's pin is kept under either (it is judged by that) or there is none.
    /// </summary>
    private TriggerPin? FirstOf(string addrKey, string? nodeId)
    {
        var byAddr = _o.Pins.Get(addrKey);
        var byNode = TriggerPinStore.NodeKey(nodeId) is { } nodeKey ? _o.Pins.Get(nodeKey) : null;
        if (byAddr is { Trust: not PinTrust.First } || byNode is { Trust: not PinTrust.First }) return null;
        return byAddr ?? byNode;
    }

    /// <summary>The targets met this run whose pin is first only and not changed (under the lock).</summary>
    private IEnumerable<string> FirstWaiting()
        => _met.Where(kv => !_changed.ContainsKey(kv.Key) && FirstOf(kv.Key, kv.Value.NodeId) is not null).Select(kv => kv.Key);

    private string WhereOf(string key) => _met.TryGetValue(key, out var met) ? met.Where : key.StartsWith("addr:", StringComparison.Ordinal) ? key[5..] : key;

    // ------------------------------------------------------------------ reading an answer

    /// <summary>
    /// 13.12.3 for an HTTP trigger's answer (<see cref="PeerLink.ReadHttp"/>), with one reading the contract's HTTP rows do not name: a
    /// 2xx whose <c>msg</c>, after its <c>OK</c> and any <c>@id</c>, is a JSON object — Patterns' <c>/api/cmd</c> answers CUE GO so,
    /// <c>{"ok":true,"msg":"OK {\"outcome\":\"Confirm\",…}"}</c> — is read by the line rows, as the wire reads <c>OK @id {…}</c>: Confirm
    /// accepted, asking for a person's second GO; Requested accepted; Done applied. Never the object's text as words: a reading with no
    /// words of its own names the cue Patterns' <c>last</c> row ran.
    /// </summary>
    public static PeerReading Read(int status, string? body, int retryAfterSeconds = 0)
    {
        if (status is >= 200 and < 300 && Embedded(body) is { } inner && PeerLink.ReadLine("OK @t " + inner.Text) is { } line)
            return line.Reading.Words.Length > 0 ? line.Reading : line.Reading with { Words = CueWords(inner.Object) };
        return PeerLink.ReadHttp(status, body, retryAfterSeconds);
    }

    /// <summary>The JSON object a 2xx body's <c>OK</c> msg carries after any <c>@id</c> (its text and the object), or null for any other body.</summary>
    private static (string Text, JsonElement Object)? Embedded(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var o = doc.RootElement;
            if (o.ValueKind != JsonValueKind.Object || o.TryGetProperty("stage", out _)
                || o.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False
                || !o.TryGetProperty("msg", out var m) || m.ValueKind != JsonValueKind.String || m.GetString() is not { } msg
                || !msg.StartsWith("OK ", StringComparison.Ordinal))
                return null;
            var rest = msg[3..].TrimStart(' ');
            if (rest.StartsWith('@')) rest = rest.IndexOf(' ') is var space and > 0 ? rest[(space + 1)..].TrimStart(' ') : "";
            if (!rest.StartsWith('{')) return null;
            using var inner = JsonDocument.Parse(rest);
            return inner.RootElement.ValueKind == JsonValueKind.Object ? (rest, inner.RootElement.Clone()) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The cue a Patterns answer's <c>last</c> row names (<c>cue 12 'Walk-in'</c>), or "".</summary>
    private static string CueWords(JsonElement o)
    {
        if (!o.TryGetProperty("last", out var last) || last.ValueKind != JsonValueKind.Object) return "";
        var number = last.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        var name = last.TryGetProperty("name", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
        if (number.Length == 0 && name.Length == 0) return "";
        var cue = number.Length > 0 ? "cue " + number : "cue";
        return name.Length > 0 ? cue + " '" + name + "'" : cue;
    }

    // ------------------------------------------------------------------ helpers

    private static StringContent Content(string body)
        => new(body, Encoding.UTF8, body.TrimStart().StartsWith('{') ? "application/json" : "text/plain");

    /// <summary>At most <see cref="MaxReplyBytes"/> of an answer's body, as text.</summary>
    private static async Task<string> ReadAtMostAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxReplyBytes];
        var got = 0;
        while (got < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(got), ct);
            if (n == 0) break;
            got += n;
        }
        return Encoding.UTF8.GetString(buffer, 0, got);
    }

    /// <summary>A 429's <c>Retry-After</c> in seconds (0 for none, or a date).</summary>
    private static int RetryAfter(HttpResponseHeaders headers)
        => headers.RetryAfter?.Delta is { } delta ? (int)Math.Min(int.MaxValue, Math.Max(0, delta.TotalSeconds)) : 0;

    private static bool SameHost(Uri uri, TriggerTarget target)
    {
        var host = uri.Host.StartsWith('[') && uri.Host.EndsWith(']') ? uri.Host[1..^1] : uri.Host;
        return host.Equals(target.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static string Where(Uri uri) => (uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.Host.ToLowerInvariant()) + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);

    /// <summary>The values of a filled-in address's query parameters that carry a secret (<see cref="TriggerAuthRules.QuerySecret"/>), unescaped.</summary>
    private static string[] QueryValues(string url)
    {
        if (url.IndexOf('#') is var hash and >= 0) url = url[..hash];
        var q = url.IndexOf('?');
        if (q < 0) return [];
        var query = url[(q + 1)..];
        return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => TriggerAuthRules.QuerySecret("?" + p) is not null && p.Contains('='))
            .Select(p => Uri.UnescapeDataString(p[(p.IndexOf('=') + 1)..]))
            .Where(v => v.Length > 0).ToArray();
    }

    /// <summary>
    /// An outcome's words as it keeps them: no control character, never a secret it was sent — a target that echoes one back has it
    /// masked — and a target's own words (<paramref name="theirs"/>) at most <see cref="WordsMax"/>.
    /// </summary>
    private static string Clean(string words, IEnumerable<string> secrets, bool theirs)
    {
        var w = new string((words ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        foreach (var s in secrets.Where(s => !string.IsNullOrEmpty(s)))
            w = w.Replace(s, "•••", StringComparison.OrdinalIgnoreCase);
        return theirs ? ShowBusText.Cut(w.Trim(), WordsMax) : w.Trim();
    }

    private static string Describe(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => "its name did not resolve",
        HttpRequestError.ConnectionError => "no connection",
        HttpRequestError.SecureConnectionError => "TLS failed",
        _ => "no answer",
    };

    /// <summary>"/cue/go 12 1.5 hello" → address + typed args (int, float, string).</summary>
    public static (string Address, object[] Args) ParseOsc(string payload)
    {
        var parts = payload.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !parts[0].StartsWith('/')) throw new FormatException("OSC payload must start with an address, e.g. /cue/go 1");
        var args = parts.Skip(1).Select(a =>
            int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? (object)i :
            float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f :
            a).ToArray();
        return (parts[0], args);
    }

    public static IPEndPoint ParseEndPoint(string target, int defaultPort)
    {
        var t = target.Trim();
        var colon = t.LastIndexOf(':');
        var host = colon > 0 ? t[..colon] : t;
        var port = colon > 0 && int.TryParse(t[(colon + 1)..], out var p) ? p : defaultPort;
        if (host.Length == 0) host = "127.0.0.1";
        var ip = IPAddress.TryParse(host, out var a) ? a
            : Dns.GetHostAddresses(host).First(x => x.AddressFamily == AddressFamily.InterNetwork);
        return new IPEndPoint(ip, port);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
        try { _worker.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _http.Dispose();
        _udp.Dispose();
    }
}
