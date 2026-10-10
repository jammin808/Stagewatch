using System.Text.Json;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>How one finished item actually ran — the forecaster's training data.</summary>
public sealed record HistoryRecord(
    [property: JsonPropertyName("kind")] SegmentKind Kind,
    [property: JsonPropertyName("speaker")] string Speaker,
    [property: JsonPropertyName("planned")] int PlannedSeconds,
    [property: JsonPropertyName("actual")] int ActualSeconds,
    [property: JsonPropertyName("at")] DateTime Utc);

/// <summary>Bounded on-disk history of item overruns (newest kept).</summary>
public sealed class OverrunHistory
{
    public const int MaxRecords = 2000;
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private readonly string? _path;
    private readonly object _lock = new();
    private List<HistoryRecord> _records = new();

    public OverrunHistory(string? path)
    {
        _path = path;
        if (path is null) return;
        try
        {
            if (File.Exists(path))
                _records = JsonSerializer.Deserialize<List<HistoryRecord>>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { _records = new(); }
    }

    public IReadOnlyList<HistoryRecord> Records { get { lock (_lock) return _records.ToArray(); } }

    /// <summary>Record a finished item. Ignores nonsense (zero plans, runs under 10 s). Saves on a worker.</summary>
    public void Add(HistoryRecord r)
    {
        if (r.PlannedSeconds < 30 || r.ActualSeconds < 10 || r.ActualSeconds > r.PlannedSeconds * 6) return;
        List<HistoryRecord> copy;
        lock (_lock)
        {
            _records.Add(r);
            if (_records.Count > MaxRecords) _records.RemoveRange(0, _records.Count - MaxRecords);
            copy = _records.ToList();
        }
        if (_path is null) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(copy, Json));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }
}

/// <summary>
/// Overrun model: for each segment kind, the distribution of log(actual / planned). Starts from
/// industry-shaped priors (panels and Q&amp;A run long, videos don't) and learns from this
/// operator's own shows as history accumulates.
/// </summary>
public sealed class OverrunModel
{
    private readonly Dictionary<SegmentKind, (double Mu, double Sigma)> _byKind = new();
    private readonly Dictionary<string, (double Mu, double Sigma)> _bySpeaker = new(StringComparer.OrdinalIgnoreCase);

    public static (double Mu, double Sigma) Prior(SegmentKind k) => k switch
    {
        SegmentKind.Panel => (Math.Log(1.10), 0.14),
        SegmentKind.QA => (Math.Log(1.12), 0.18),
        SegmentKind.Talk => (Math.Log(1.05), 0.09),
        SegmentKind.Award => (Math.Log(1.08), 0.20),
        SegmentKind.Break => (Math.Log(1.02), 0.08),
        SegmentKind.Video => (0, 0.005),
        SegmentKind.Buffer => (0, 0.0),
        _ => (Math.Log(1.05), 0.12)
    };

    public static OverrunModel Learn(IEnumerable<HistoryRecord> history)
    {
        var m = new OverrunModel();
        var list = history.ToList();
        foreach (var g in list.GroupBy(r => r.Kind))
            m._byKind[g.Key] = Blend(Prior(g.Key), g.Select(LogRatio).ToList());
        foreach (var g in list.Where(r => !string.IsNullOrWhiteSpace(r.Speaker)).GroupBy(r => r.Speaker.Trim()))
            if (g.Count() >= 3) m._bySpeaker[g.Key] = Blend(Prior(g.First().Kind), g.Select(LogRatio).ToList());
        return m;
    }

    public (double Mu, double Sigma) For(SegmentKind kind, string? speaker)
    {
        if (!string.IsNullOrWhiteSpace(speaker) && _bySpeaker.TryGetValue(speaker.Trim(), out var s)) return s;
        return _byKind.TryGetValue(kind, out var k) ? k : Prior(kind);
    }

    private static double LogRatio(HistoryRecord r) => Math.Log((double)r.ActualSeconds / r.PlannedSeconds);

    /// <summary>Shrink the sample mean/σ towards the prior (worth 5 observations), so 2 shows don't swing it.</summary>
    private static (double, double) Blend((double Mu, double Sigma) prior, List<double> xs)
    {
        const double priorWeight = 5;
        var n = xs.Count;
        var mean = xs.Average();
        var variance = n > 1 ? xs.Sum(x => (x - mean) * (x - mean)) / (n - 1) : prior.Sigma * prior.Sigma;
        var mu = (prior.Mu * priorWeight + mean * n) / (priorWeight + n);
        var sigma = Math.Sqrt((prior.Sigma * prior.Sigma * priorWeight + variance * n) / (priorWeight + n));
        return (mu, Math.Clamp(sigma, 0, 0.6));
    }
}

/// <summary>Probabilistic end of show: "50% by 17:24, 80% by 17:38".</summary>
public sealed record ForecastResult(DateTime P50Utc, DateTime P80Utc, DateTime P95Utc, double CurfewRisk);

/// <summary>
/// Monte Carlo over the rest of the running order. Deterministic projections assume everything
/// later runs to plan; panels and Q&amp;A systematically don't. Seeded RNG, so the same inputs give
/// the same answer (no flicker on the displays).
/// </summary>
public static class Forecaster
{
    public static ForecastResult? Run(IReadOnlyList<RundownItem> items, ScheduleProjection p, int current, DateTime nowUtc,
        OverrunModel model, DateTime? curfewUtc = null, int iterations = 2000, int seed = 12345)
    {
        if (items.Count == 0 || p.Items.Length != items.Count) return null;
        var rng = new Random(seed);
        var ends = new double[iterations];
        var baseTicks = nowUtc.Ticks;
        var curfewMisses = 0;
        var first = Math.Max(0, current);

        for (int n = 0; n < iterations; n++)
        {
            DateTime t;
            int from;
            if (current >= 0 && current < items.Count && p.Items[current].Status == ItemStatus.Live)
            {
                // The live item: it's planned to end at ExpectedEnd; add the kind's typical extra over-run on what's left.
                var it = items[current];
                var (mu, sigma) = model.For(it.Kind, it.Speaker);
                var remaining = Math.Max(0, (p.Items[current].ExpectedEndUtc - nowUtc).TotalSeconds);
                var ratio = Math.Exp(mu + sigma * Gaussian(rng));
                t = nowUtc.AddSeconds(remaining * ratio);
                from = current + 1;
            }
            else
            {
                t = first < items.Count ? p.Items[first].ExpectedStartUtc : nowUtc;
                from = first;
            }

            for (int i = from; i < items.Count; i++)
            {
                var it = items[i];
                var timing = p.Items[i];
                if (it.StartMode == StartMode.Fixed && it.FixedStart is not null && t < timing.PlannedStartUtc) t = timing.PlannedStartUtc;
                double dur = it.EffectiveSeconds;
                if (it.Kind == SegmentKind.Buffer) dur = Math.Max(0, dur - Math.Max(0, (t - timing.PlannedStartUtc).TotalSeconds));
                else
                {
                    var (mu, sigma) = model.For(it.Kind, it.Speaker);
                    dur *= Math.Exp(mu + sigma * Gaussian(rng));
                }
                t = t.AddSeconds(dur);
            }
            ends[n] = t.Ticks - baseTicks;
            if (curfewUtc is { } c && t > c) curfewMisses++;
        }
        Array.Sort(ends);
        DateTime Q(double q) => new DateTime(baseTicks + (long)ends[(int)Math.Min(iterations - 1, q * iterations)], DateTimeKind.Utc);
        // Quantise to whole minutes: forecasts are coarse by nature, and it keeps frames stable.
        static DateTime Minute(DateTime d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerMinute + (d.Ticks % TimeSpan.TicksPerMinute >= TimeSpan.TicksPerMinute / 2 ? TimeSpan.TicksPerMinute : 0), DateTimeKind.Utc);
        return new ForecastResult(Minute(Q(0.5)), Minute(Q(0.8)), Minute(Q(0.95)), curfewUtc is null ? 0 : Math.Round((double)curfewMisses / iterations, 2));
    }

    private static double Gaussian(Random r)
    {
        // Box–Muller
        var u1 = 1.0 - r.NextDouble();
        var u2 = r.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
