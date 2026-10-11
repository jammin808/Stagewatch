using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>A presenter PC as Stagewatch sees it.</summary>
public sealed record PresenterPc(string Agent, string Name, string Machine, string Address, bool Presenting, string Presentation,
    int Slide, int Count, bool Connected, double AgeSeconds);

/// <summary>
/// Stagewatch's side of remote slides: the latest feed per presenter PC, their scripts (by hash), and which deck the
/// show follows — this PC's PowerPoint, a chosen presenter PC, or automatically whichever one is presenting.
/// Feeds arrive on web-server threads; reads come from the show thread. Latest-wins, lock-free reads.
/// </summary>
public sealed class RemoteDecks
{
    /// <summary>A presenter PC counts as gone when it hasn't reported for this long.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(4);

    private sealed record Entry(SlideFeed Feed, string Address, long AtTicks);

    private readonly ConcurrentDictionary<string, Entry> _feeds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<long> _now;

    public RemoteDecks(Func<long>? now = null) => _now = now ?? Stopwatch.GetTimestamp;

    /// <summary>Take a feed. Returns true when Stagewatch wants this deck's script (it hasn't seen that hash).</summary>
    public bool Accept(SlideFeed feed, string address)
    {
        if (_feeds.Count > 64 && !_feeds.ContainsKey(feed.Agent)) return false;   // bounded
        var entry = new Entry(feed, address, _now());
        _feeds.AddOrUpdate(feed.Agent, entry, (_, old) => feed.Seq != 0 && feed.Seq < old.Feed.Seq && feed.Seq > old.Feed.Seq - 1_000_000 ? old : entry);
        return feed.ScriptHash.Length > 0 && !_scripts.ContainsKey(feed.ScriptHash);
    }

    /// <summary>Drop every stored script (what a restart does) — presenter PCs send them again when asked.</summary>
    public void ForgetScripts() => _scripts.Clear();

    public void AcceptScript(string hash, string script)
    {
        if (hash.Length == 0 || SlideFeed.HashScript(script) != hash) return;   // only the script that was announced
        if (_scripts.Count > 32) _scripts.Clear();
        _scripts[hash] = script;
    }

    public IReadOnlyList<PresenterPc> List()
    {
        var now = _now();
        return _feeds.Values
            .Select(e =>
            {
                var age = Stopwatch.GetElapsedTime(e.AtTicks, now);
                var f = e.Feed;
                return new PresenterPc(f.Agent, f.Name.Length > 0 ? f.Name : f.Machine, f.Machine, e.Address, f.Presenting, f.Presentation,
                    f.Slide, f.Count, age < StaleAfter, age.TotalSeconds);
            })
            .Where(p => p.AgeSeconds < 600)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The deck to follow. <paramref name="source"/>: "local" (or empty) = this PC; "auto" = a presenter PC that is
    /// presenting, the most recent one if several (else this PC); otherwise a presenter PC's agent id.
    /// </summary>
    public DeckState? Pick(string source, DeckState? local, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(source) || source == "local") return local;
        var now = _now();
        Entry? chosen = null;
        if (source == "auto")
        {
            chosen = _feeds.Values
                .Where(e => e.Feed.Presenting && Stopwatch.GetElapsedTime(e.AtTicks, now) < StaleAfter)
                .OrderBy(e => e.Feed.ShowMs)                      // the one that started presenting most recently
                .FirstOrDefault();
            if (chosen is null) return local;
        }
        else if (_feeds.TryGetValue(source, out var e) && Stopwatch.GetElapsedTime(e.AtTicks, now) < StaleAfter && e.Feed.Presenting)
            chosen = e;
        if (chosen is null) return null;

        var f = chosen.Feed;
        var since = Stopwatch.GetElapsedTime(chosen.AtTicks, now);   // time since the packet arrived
        _scripts.TryGetValue(f.ScriptHash, out var script);
        return new DeckState(f.Presentation, f.Slide, f.Count, f.NextTitle, f.Notes, f.Budgets,
            nowUtc - since - TimeSpan.FromMilliseconds(f.SlideMs), nowUtc - since - TimeSpan.FromMilliseconds(f.ShowMs), script ?? "");
    }
}

/// <summary>
/// "Who's here?" for Stagewatch and its presenter PCs: each announces itself on UDP 9928 every two seconds and
/// listens for the others. No configuration; nothing but names and ports goes on the wire (no PINs, no slides).
/// </summary>
public sealed class SlidesBeacon : IDisposable
{
    public const int Port = 9928;

    public sealed record Hello(
        [property: JsonPropertyName("t")] string Kind,      // "main" (a Stagewatch), "agent" (a presenter PC), "patterns", "messenger"…
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("machine")] string Machine,
        [property: JsonPropertyName("web")] int WebPort,
        [property: JsonPropertyName("presenting")] bool Presenting,
        /// <summary>What this node offers, for anything listening (Patterns, other tools): e.g. "slides", "timer", "screens".</summary>
        [property: JsonPropertyName("caps")] string[]? Caps = null,
        /// <summary>Protocol version of the slide feed this node speaks.</summary>
        [property: JsonPropertyName("proto")] int Proto = 1,
        /// <summary>Show Bus 1.1: where to connect ("http", "ws", "wire", "osc", "companion").</summary>
        [property: JsonPropertyName("ports")] Dictionary<string, int>? Ports = null,
        /// <summary>Show Bus 1.1: the sender's Unix time in ms (a first, rough clock offset).</summary>
        [property: JsonPropertyName("clock")] long Clock = 0,
        [property: JsonPropertyName("host")] string? Host = null,
        /// <summary>Show Bus 1.2: "sha256:&lt;hex&gt;" of the node's TLS certificate (pinned by peers on first authenticated use).</summary>
        [property: JsonPropertyName("tls")] string? Tls = null)
    {
        /// <summary>The node's web port: "ports.http", or the older "web".</summary>
        public int HttpPort => Ports is not null && Ports.TryGetValue("http", out var p) && p is > 0 and < 65536 ? p : WebPort;
    }

    /// <summary>A node kind or id as the Show Bus allows it: short, letters, digits, '-' and '_'.</summary>
    public static bool ValidToken(string? s, int max) => s is { Length: > 0 } && s.Length <= max && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public sealed record Peer(Hello Hello, string Address, double AgeSeconds);

    private readonly Func<Hello?> _hello;
    private readonly UdpClient? _rx;
    private readonly UdpClient _tx = new() { EnableBroadcast = true };
    private readonly Timer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, (Hello Hello, string Address, long SeenMs)> _peers = new();

    public SlidesBeacon(Func<Hello?> hello, int port = Port)
    {
        _hello = hello;
        try
        {
            var rx = new UdpClient(AddressFamily.InterNetwork);
            rx.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            rx.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _rx = rx;
            _ = Task.Run(() => ListenAsync(rx, port, _cts.Token));
            Listening = true;
        }
        catch (SocketException) { Listening = false; }
        _timer = new Timer(_ => Send(port), null, 200, 2000);
    }

    public bool Listening { get; }

    private void Send(int port)
    {
        try
        {
            if (_hello() is not { } h) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(h);
            _tx.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, port));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
    }

    private async Task ListenAsync(UdpClient rx, int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await rx.ReceiveAsync(ct); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            if (r.Buffer.Length > 2048) continue;
            try
            {
                var h = JsonSerializer.Deserialize<Hello>(r.Buffer);
                // Every kind of show node is heard (Show Bus 1.1 discovery); presenter PCs still need a valid feed id.
                if (h is null || h.Version != 1 || !ValidToken(h.Kind, 16) || !ValidToken(h.Id, 40)) continue;
                if (h.Kind == "agent" && !SlideFeed.Valid(h.Id)) continue;
                h = h with
                {
                    Name = h.Name ?? "", Machine = h.Machine ?? h.Host ?? "", Caps = h.Caps?.Where(c => c is not null).Take(16).Select(Cap).ToArray(),
                    Ports = h.Ports?.Where(kv => ValidToken(kv.Key, 16) && kv.Value is > 0 and < 65536).Take(8).ToDictionary(kv => kv.Key, kv => kv.Value),
                    Host = h.Host is null ? null : Cap(h.Host),
                };
                if (_hello() is { } me && me.Id == h.Id) continue;
                if (_peers.Count > 128 && !_peers.ContainsKey(h.Id)) continue;
                var clean = h with { Name = Cap(h.Name), Machine = Cap(h.Machine), WebPort = Math.Clamp(h.WebPort, 0, 65535) };
                _peers[h.Id] = (clean, r.RemoteEndPoint.Address.ToString(), Environment.TickCount64);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Anything on the network can send us anything: a bad packet is dropped, and we keep listening.
            }
        }
    }

    /// <summary>Everyone heard in the last minute.</summary>
    public IReadOnlyList<Peer> Peers()
    {
        var now = Environment.TickCount64;
        var list = new List<Peer>();
        foreach (var (id, p) in _peers)
        {
            var age = (now - p.SeenMs) / 1000.0;
            if (age > 60) { _peers.TryRemove(id, out _); continue; }
            list.Add(new Peer(p.Hello, p.Address, age));
        }
        return list.OrderBy(p => p.Hello.Kind).ThenBy(p => p.Hello.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Cap(string s) => s.Length > 60 ? s[..60] : s;

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _rx?.Dispose();
        _tx.Dispose();
    }
}
