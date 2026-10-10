using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>
/// Captures a rehearsal: how long the speaker spent on each slide. Feed it the slide that's up on
/// every frame while rehearsing; <see cref="Finish"/> gives the per-slide budgets the ghost run uses.
/// </summary>
public sealed class RehearsalRecorder
{
    private readonly Dictionary<int, double> _seconds = new();
    private int _slide;
    private DateTime _since;
    private int _count;

    public bool Active { get; private set; }
    public string Presentation { get; private set; } = "";

    public void Begin(string presentation, int slideCount, DateTime utc)
    {
        _seconds.Clear();
        _slide = 0;
        _count = slideCount;
        Presentation = presentation;
        Active = true;
        _since = utc;
    }

    public void Observe(int slide, int slideCount, DateTime utc)
    {
        if (!Active || slide <= 0) return;
        _count = Math.Max(_count, slideCount);
        if (slide == _slide) return;
        Close(utc);
        _slide = slide;
        _since = utc;
    }

    /// <summary>Stop and return seconds per slide (0 for slides never shown). Null if nothing was captured.</summary>
    public double[]? Finish(DateTime utc)
    {
        if (!Active) return null;
        Close(utc);
        Active = false;
        if (_seconds.Count == 0 || _count <= 0) return null;
        var budgets = new double[_count];
        foreach (var (slide, secs) in _seconds)
            if (slide >= 1 && slide <= _count) budgets[slide - 1] = Math.Round(secs, 1);
        return budgets;
    }

    private void Close(DateTime utc)
    {
        if (_slide <= 0) return;
        var secs = (utc - _since).TotalSeconds;
        if (secs > 0) _seconds[_slide] = _seconds.GetValueOrDefault(_slide) + secs; // revisits add up
    }
}

/// <summary>Saved rehearsals, by presentation file name (%APPDATA%\Stagewatch\rehearsals.json).</summary>
public sealed class RehearsalStore
{
    private readonly string? _path;
    private Dictionary<string, double[]> _byDeck = new(StringComparer.OrdinalIgnoreCase);

    public RehearsalStore(string? path)
    {
        _path = path;
        try
        {
            if (path is not null && File.Exists(path))
                _byDeck = new(JsonSerializer.Deserialize<Dictionary<string, double[]>>(File.ReadAllText(path)) ?? new(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public double[]? For(string presentation) => _byDeck.TryGetValue(presentation, out var b) ? b : null;

    public void Save(string presentation, double[] budgets)
    {
        _byDeck[presentation] = budgets;
        if (_path is null) return;
        var copy = new Dictionary<string, double[]>(_byDeck);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(copy));
                File.Move(tmp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }
}
