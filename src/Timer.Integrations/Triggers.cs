using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Stagewatch.Core;
using Stagewatch.Remote;

namespace Stagewatch.Integrations;

/// <summary>Values a trigger payload can use: {ITEM} {TITLE} {PHASE} {REMAINING} {DETAIL} {ROOM}.</summary>
public sealed record TriggerContext(int Item, string Title, string Phase, string Remaining, string Detail, string Room);

/// <summary>
/// Outbound triggers: "when X happens, do Y" — Stagewatch driving lights, playback, Companion
/// pages or other Patterns tools rather than only being driven. The show thread only matches rules
/// and enqueues; a worker does the network I/O with short timeouts, so a dead target never delays
/// the timer. Each rule fires at most once a second (no feedback storms).
/// </summary>
public sealed class TriggerRunner : IIntegration
{
    private readonly Func<IReadOnlyList<TriggerRule>> _rules;
    private readonly Action<string> _runCommand;
    private readonly Channel<(TriggerRule Rule, TriggerContext Ctx)> _queue =
        Channel.CreateBounded<(TriggerRule, TriggerContext)>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };
    private readonly UdpClient _udp = new();
    private readonly Dictionary<TriggerRule, long> _lastFired = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;

    /// <param name="runCommand">Runs a Stagewatch command line through the app's normal input path.</param>
    public TriggerRunner(Func<IReadOnlyList<TriggerRule>> rules, Action<string> runCommand)
    {
        _rules = rules;
        _runCommand = runCommand;
        _worker = Task.Run(WorkAsync);
    }

    public string Name => "Triggers";
    public LinkHealth Health => LinkHealth.Ok;
    public string Status { get; private set; } = "ready";
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

    public static string Expand(string text, TriggerContext c) => text
        .Replace("{ITEM}", c.Item.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
        .Replace("{TITLE}", c.Title, StringComparison.OrdinalIgnoreCase)
        .Replace("{PHASE}", c.Phase, StringComparison.OrdinalIgnoreCase)
        .Replace("{REMAINING}", c.Remaining, StringComparison.OrdinalIgnoreCase)
        .Replace("{DETAIL}", c.Detail, StringComparison.OrdinalIgnoreCase)
        .Replace("{ROOM}", c.Room, StringComparison.OrdinalIgnoreCase);

    private async Task WorkAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_queue.Reader.TryRead(out var job))
                {
                    try
                    {
                        await RunAsync(job.Rule, job.Ctx);
                        Fired++;
                        Status = $"last: {job.Rule.Name} ({job.Rule.Action}) OK";
                    }
                    catch (Exception ex)
                    {
                        Status = $"last: {job.Rule.Name} failed — {ex.Message}";
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
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
            case TriggerAction.Http:
            {
                var url = Expand(r.Target, ctx);
                using var resp = payload.Length == 0
                    ? await _http.GetAsync(url, _cts.Token)
                    : await _http.PostAsync(url, new StringContent(payload, Encoding.UTF8, payload.TrimStart().StartsWith('{') ? "application/json" : "text/plain"), _cts.Token);
                resp.EnsureSuccessStatusCode();
                break;
            }
            case TriggerAction.CompanionPress:
            {
                var ep = ParseEndPoint(r.Target, 8000);
                var loc = payload.Trim().Trim('/');
                if (loc.Split('/').Length != 3) throw new FormatException("Companion button must be page/row/column");
                using var resp = await _http.PostAsync($"http://{ep.Address}:{ep.Port}/api/location/{loc}/press", new StringContent(""), _cts.Token);
                resp.EnsureSuccessStatusCode();
                break;
            }
        }
    }

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
