using System.Text.Json;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>A Stagewatch this presenter PC sends to.</summary>
public sealed record AgentTarget(string Host, int Port, string Pin)
{
    public string Key => $"{Host.Trim().ToLowerInvariant()}:{Port}";
}

/// <summary>How sending to one Stagewatch is going.</summary>
public sealed record AgentTargetStatus(string Key, bool Ok, string Text);

/// <summary>
/// Stagewatch Slides — the presenter-PC role (Stagewatch.exe --slides). Reads this PC's PowerPoint (the same COM reader
/// the main app uses), announces itself on the LAN, and sends the slideshow to one or more Stagewatch machines (main and
/// backup). Netcode rules from games: whole state every time (no deltas to lose), a sequence number (late packets are
/// ignored), durations instead of clock times (no clock sync needed), latest-wins per target (a slow or dead Stagewatch
/// never delays the others or queues stale slides), and the big part (the deck's script) sent once, only when asked.
/// </summary>
public sealed class SlidesAgent : IDisposable
{
    private readonly string _id;
    private readonly Func<string> _name;
    private readonly Func<IReadOnlyList<AgentTarget>> _targets;
    private readonly PowerPointSensor _ppt = new();
    private readonly SlidesBeacon _beacon;
    private readonly HttpClient _http;
    /// <summary>
    /// Pinned certificates of the Stagewatches this PC sends to (Show Bus §10): a Stagewatch that says it has one is
    /// reached over HTTPS, and its certificate must be the one it had the first time it took our feed.
    /// </summary>
    private readonly TlsPins _pins;
    private readonly string? _pinsPath;
    /// <summary>What each Stagewatch's hello says its certificate is, by host:port (for the request in flight).</summary>
    private readonly ConcurrentDictionary<string, (string NodeId, string Fingerprint)> _expected = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, TargetState> _state = new();
    private long _seq;

    private sealed class TargetState
    {
        public int InFlight;
        public string LastSent = "";
        public long LastSentMs;
        public long ScriptSentMs = long.MinValue / 2;
        public volatile AgentTargetStatus Status = new("", false, "connecting…");
    }

    private readonly Func<DeckState?> _deck;

    /// <param name="deckSource">Where the slideshow comes from; default this PC's PowerPoint (tests pass their own).</param>
    public SlidesAgent(string agentId, Func<string> name, Func<IReadOnlyList<AgentTarget>> targets, Func<DeckState?>? deckSource = null, int beaconPort = SlidesBeacon.Port,
        string? pinsPath = null)
    {
        _pinsPath = pinsPath;
        _pins = new TlsPins(LoadPins(pinsPath));
        _http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (req, cert, _, _) => CertificateOk(req, cert) })
        { Timeout = TimeSpan.FromSeconds(3) };
        _id = agentId;
        _name = name;
        _targets = targets;
        if (deckSource is null) _ppt.Start();
        _deck = deckSource ?? (() => _ppt.Value);
        var inst = ShowBusHello.NewInst();   // one per agent start: two agents saying one id are a conflict (Show Bus §2)
        _beacon = new SlidesBeacon(() => ShowBusHello.Agent(_id, inst, _name(), Environment.MachineName, _deck() is { Slide: > 0 },
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), beaconPort);
        _ = Task.Run(() => LoopAsync(_cts.Token));
    }

    public DeckState? Deck => _deck();
    public string PowerPointStatus => _ppt.Status;
    public IReadOnlyList<SlidesBeacon.Peer> Peers() => _beacon.Peers();

    /// <summary>Another presenter PC saying this one's feed id (Show Bus §2), while its mark stands; null otherwise.</summary>
    public HelloConflict? OwnConflict => _beacon.OwnConflict;

    public AgentTargetStatus StatusOf(AgentTarget t)
        => _state.TryGetValue(t.Key, out var s) ? s.Status : new AgentTargetStatus(t.Key, false, "connecting…");

    /// <summary>Pure: what this PC reports right now (presenting = a slideshow is running).</summary>
    public static SlideFeed BuildFeed(DeckState? deck, string agentId, string name, long seq, DateTime nowUtc)
    {
        if (deck is null || deck.Slide <= 0)
            return new SlideFeed { Agent = agentId, Name = name, Machine = Environment.MachineName, Presenting = false, Seq = seq };
        var notes = deck.Notes.Length > SlideFeed.MaxNotes ? deck.Notes[..SlideFeed.MaxNotes] : deck.Notes;
        return new SlideFeed
        {
            Agent = agentId, Name = name, Machine = Environment.MachineName, Presenting = true,
            Presentation = deck.Presentation, Slide = deck.Slide, Count = deck.Count, NextTitle = deck.NextTitle, Notes = notes,
            Budgets = deck.RehearsalBudgets,
            SlideMs = (long)Math.Max(0, (nowUtc - deck.SlideSinceUtc).TotalMilliseconds),
            ShowMs = (long)Math.Max(0, (nowUtc - deck.ShowSinceUtc).TotalMilliseconds),
            ScriptHash = deck.Script.Length <= SlideFeed.MaxScript ? SlideFeed.HashScript(deck.Script) : "",
            Seq = seq,
        };
    }

    /// <summary>What changed enough to send now (slide, deck, presenting) — durations alone wait for the 1 s refresh.</summary>
    private static string Identity(SlideFeed f) => $"{f.Presenting}|{f.Presentation}|{f.Slide}|{f.Count}|{f.ScriptHash}|{f.Name}";

    private async Task LoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(150));
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var deck = _deck();
            var targets = _targets();
            if (targets.Count == 0) continue;
            var feed = BuildFeed(deck, _id, _name(), Interlocked.Increment(ref _seq), DateTime.UtcNow);
            var identity = Identity(feed);
            var now = Environment.TickCount64;
            foreach (var t in targets)
            {
                var st = _state.GetOrAdd(t.Key, _ => new TargetState());
                if (identity == st.LastSent && now - st.LastSentMs < 1000) continue;     // nothing new, and refreshed recently
                if (Interlocked.Exchange(ref st.InFlight, 1) == 1) continue;           // latest wins: never queue behind a slow target
                st.LastSent = identity;
                st.LastSentMs = now;
                _ = SendAsync(t, st, feed, deck, ct);
            }
            foreach (var key in _state.Keys)
                if (!targets.Any(t => t.Key == key)) _state.TryRemove(key, out _);
        }
    }

    private async Task SendAsync(AgentTarget t, TargetState st, SlideFeed feed, DeckState? deck, CancellationToken ct)
    {
        try
        {
            var scheme = Scheme(t);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{scheme}://{t.Host.Trim()}:{t.Port}/api/slides/feed")
            {
                Content = new ByteArrayContent(feed.ToJson()),
            };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            if (t.Pin.Length > 0) req.Headers.Add("X-Stagewatch-Pin", t.Pin);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                st.Status = new AgentTargetStatus(t.Key, false, Reason((int)res.StatusCode, body));
                st.LastSent = "";   // retry promptly once fixed
                return;
            }
            if (scheme == "https" && _expected.TryGetValue(t.Key, out var exp) && _pins.Check(exp.NodeId, exp.Fingerprint) == TlsPins.Verdict.FirstUse)
            {
                _pins.Pin(exp.NodeId, exp.Fingerprint);   // first authenticated success: this is the certificate from now on
                SavePins();
            }
            st.Status = new AgentTargetStatus(t.Key, true, (scheme == "https" ? "🔒 " : "") + (feed.Presenting ? $"sending slide {feed.Slide}/{feed.Count}" : "connected — no slideshow running"));
            // Whenever Stagewatch says it needs the script, it gets it — it may have restarted and lost it. Rate-limited so a
            // failing upload can't turn into one per second.
            if (body.Contains("\"needScript\":true", StringComparison.Ordinal) && deck is not null && feed.ScriptHash.Length > 0
                && Environment.TickCount64 - st.ScriptSentMs > 5000)
            {
                using var sreq = new HttpRequestMessage(HttpMethod.Post, $"{scheme}://{t.Host.Trim()}:{t.Port}/api/slides/script?hash={feed.ScriptHash}")
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes(deck.Script)),
                };
                if (t.Pin.Length > 0) sreq.Headers.Add("X-Stagewatch-Pin", t.Pin);
                using var sres = await _http.SendAsync(sreq, ct).ConfigureAwait(false);
                st.ScriptSentMs = Environment.TickCount64;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            st.Status = new AgentTargetStatus(t.Key, false, "no answer (is Stagewatch running, with Settings → Remote on?)");
            st.LastSent = "";
        }
        catch (HttpRequestException ex)
        {
            var changed = _expected.TryGetValue(t.Key, out var e) && _pins.Check(e.NodeId, e.Fingerprint) == TlsPins.Verdict.Changed;
            st.Status = new AgentTargetStatus(t.Key, false, changed
                ? "CERTIFICATE CHANGED — this Stagewatch's certificate isn't the one it had before. If it was reinstalled, press Trust new certificate."
                : "can't reach it: " + ex.Message);
            st.LastSent = "";
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        finally { Volatile.Write(ref st.InFlight, 0); }
    }

    /// <summary>
    /// https when the target's hello names a certificate (Show Bus §10), else http. The certificate is the one the hello heard at the
    /// typed address named (by a host name, the last hello's, when it says it is that host), never another address's. While two
    /// machines say the target's id (§2) no hello says whose certificate the typed address presents: a certificate already expected or
    /// pinned for that id stays, and none is taken from a hello.
    /// </summary>
    private string Scheme(AgentTarget t)
    {
        var host = t.Host.Trim();
        var here = host is "127.0.0.1" || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        bool OnThisPc(SlidesBeacon.Peer p) => here && p.Hello.Machine.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        // A typed address matches a heard Stagewatch by any address its id was heard from, or its host (Show Bus §2).
        var peer = _beacon.Peers().FirstOrDefault(p => p.Hello.Kind == "main" && p.Hello.HttpPort == t.Port && (p.HeardAt(host) || OnThisPc(p)));
        if (peer?.Conflict is not null)
        {
            if (_expected.ContainsKey(t.Key)) return "https";
            if (_pins.Snapshot().TryGetValue(peer.Hello.Id, out var pinned))
            {
                _expected[t.Key] = (peer.Hello.Id, pinned);
                return "https";
            }
            return "http";
        }
        var named = peer is not null && (string.Equals(peer.Hello.Host, host, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(peer.Hello.Machine, host, StringComparison.OrdinalIgnoreCase) || OnThisPc(peer));
        var fp = peer?.TlsAt(here ? "127.0.0.1" : host) ?? (named ? peer!.Hello.Tls : null);
        if (peer is not null && fp is not null && TlsPins.IsFingerprint(fp))
        {
            _expected[t.Key] = (peer.Hello.Id, fp);
            return "https";
        }
        _expected.TryRemove(t.Key, out _);
        return "http";
    }

    /// <summary>The server's certificate is the pinned one (or, before the first success, the one its hello names).</summary>
    private bool CertificateOk(HttpRequestMessage req, System.Security.Cryptography.X509Certificates.X509Certificate2? cert)
    {
        if (cert is null || req.RequestUri is not { } uri) return false;
        var key = $"{uri.Host.ToLowerInvariant()}:{uri.Port}";
        if (!_expected.TryGetValue(key, out var exp)) return false;
        var actual = TlsPins.Fingerprint(cert.RawData);
        return _pins.Check(exp.NodeId, actual) switch
        {
            TlsPins.Verdict.Trusted => true,
            TlsPins.Verdict.FirstUse => actual == exp.Fingerprint,
            _ => false,   // CERTIFICATE CHANGED: refused until the operator accepts it
        };
    }

    /// <summary>The operator accepts a Stagewatch's new certificate (after a reinstall): the next one it presents is pinned.</summary>
    public void TrustNewCertificate(AgentTarget t)
    {
        if (!_expected.TryGetValue(t.Key, out var exp)) return;
        _pins.Pin(exp.NodeId, exp.Fingerprint);
        SavePins();
    }

    private static Dictionary<string, string>? LoadPins(string? path)
    {
        try { return path is not null && File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private void SavePins()
    {
        if (_pinsPath is null) return;
        try { AtomicFile.Write(_pinsPath, JsonSerializer.Serialize(_pins.Snapshot())); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string Reason(int status, string body)
    {
        var msg = System.Text.RegularExpressions.Regex.Match(body, "\"error\"\\s*:\\s*\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : "";
        return status switch
        {
            401 => msg.Length > 0 ? $"PIN: {msg}" : "wrong or missing PIN",
            403 => msg.Length > 0 ? msg : "refused",
            404 => "this Stagewatch is too old for presenter PCs (update it)",
            _ => $"error {status} {msg}".Trim(),
        };
    }

    public void Dispose()
    {
        _cts.Cancel();
        _beacon.Dispose();
        _ppt.Dispose();
        _http.Dispose();
    }
}
