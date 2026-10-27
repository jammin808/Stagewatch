using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;

namespace Stagewatch.Integrations;

/// <summary>
/// A clip playing out, as a playout server reports it: <see cref="Source"/> the app it plays on, and for a playout link's peer the
/// link's name (<see cref="Link"/>) and the peer's Show Bus id (<see cref="Node"/>, "" while not known); both "" for vMix.
/// </summary>
public sealed record PlayoutState(string Source, string Title, double DurationSeconds, double PositionSeconds, bool Running)
{
    public double RemainingSeconds => Math.Max(0, DurationSeconds - PositionSeconds);

    public string Link { get; init; } = "";

    public string Node { get; init; } = "";
}

/// <summary>Anything that can tell us how long the current video has left (vMix today; CasparCG / OBS later).</summary>
public interface IPlayoutSource : IIntegration
{
    PlayoutState? Value { get; }
}

/// <summary>
/// vMix over its HTTP API (<c>http://host:8088/api</c>, XML). We pick the clip in Program (else
/// Preview) that has a duration, and report its remaining time — the "VT countdown" vMix users
/// otherwise build from chained XAML clocks and scripts.
/// </summary>
public sealed class VmixSource : PollingSensor<PlayoutState>, IPlayoutSource
{
    private readonly HttpClient _http;
    private readonly Uri _api;

    public VmixSource(string host, int port, HttpMessageHandler? handler = null)
        : base("vMix", TimeSpan.FromMilliseconds(250))
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMilliseconds(800);
        _api = new Uri($"http://{host}:{port}/api");
    }

    protected override PlayoutState? Poll(CancellationToken ct)
    {
        string xml;
        try { xml = _http.GetStringAsync(_api, ct).GetAwaiter().GetResult(); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Report(LinkHealth.NotFound, $"vMix not reachable at {_api.Host}:{_api.Port}");
            return null;
        }
        var state = Parse(xml);
        Report(LinkHealth.Ok, state is null ? "connected — no clip in Program" : $"{state.Title}: {TimeSpan.FromSeconds(state.RemainingSeconds):mm\\:ss} left");
        return state;
    }

    /// <summary>Pure: vMix API XML → the clip in Program (else Preview) with a duration.</summary>
    public static PlayoutState? Parse(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }
        var root = doc.Root;
        if (root is null) return null;
        var active = (string?)root.Element("active");
        var preview = (string?)root.Element("preview");
        var inputs = root.Element("inputs")?.Elements("input").ToList() ?? new();

        XElement? Pick(string? number) => number is null ? null : inputs.FirstOrDefault(i => (string?)i.Attribute("number") == number);
        foreach (var candidate in new[] { Pick(active), Pick(preview) })
        {
            if (candidate is null) continue;
            var dur = Ms(candidate.Attribute("duration"));
            if (dur <= 0) continue;
            var pos = Ms(candidate.Attribute("position"));
            var title = (string?)candidate.Attribute("title") ?? (string?)candidate.Value ?? "";
            var running = ((string?)candidate.Attribute("state"))?.Equals("Running", StringComparison.OrdinalIgnoreCase) == true;
            return new PlayoutState("vMix", title.Trim(), dur / 1000, pos / 1000, running);
        }
        return null;
    }

    private static double Ms(XAttribute? a)
        => a is not null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public override void Dispose()
    {
        base.Dispose();
        _http.Dispose();
    }
}
