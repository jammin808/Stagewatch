using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Stagewatch.Core;

public enum JournalKind
{
    ShowOpened, ItemLoaded, ItemStart, ItemEnd, Pause, Resume, Nudge, Expired,
    Phase, Message, Signal, Ack, Escalated, Trim, Delay, Question, Note, Preview, Blackout, ClockCheck,
    /// <summary>What a playout link was asked to do and what became of it, and what was seen of its clip.</summary>
    Playout
}

/// <summary>
/// One thing that happened, with its UTC time. Append-only: the show's flight recorder.
/// <see cref="Item"/> is the position at the time (kept for old journals and for reading); identity is
/// <see cref="RundownId"/> + <see cref="ItemId"/>, and <see cref="Execution"/> tells one run of an item from the next
/// (a loop pass, a second go after a restart). Entries written before identities existed have none of the three.
/// </summary>
public sealed record JournalEntry(
    [property: JsonPropertyName("t")] DateTime Utc,
    [property: JsonPropertyName("k")] JournalKind Kind,
    [property: JsonPropertyName("i")] int Item = -1,
    [property: JsonPropertyName("x")] string Text = "",
    [property: JsonPropertyName("v")] double Value = 0)
{
    [JsonPropertyName("ro"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RundownId { get; init; }
    [JsonPropertyName("ii"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ItemId { get; init; }
    [JsonPropertyName("ex"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Execution { get; init; }

    /// <summary>Whether this entry is about <paramref name="item"/> at <paramref name="index"/> of running order <paramref name="rundownId"/>.</summary>
    public bool IsAbout(string rundownId, RundownItem item, int index)
        => ItemId is not null
            ? ItemId == item.Id && (RundownId is null || RundownId == rundownId)
            : RundownId is null && Item == index;   // an old journal: position is all there is
}

/// <summary>Who an entry is about: the running order, the item, and which run of it.</summary>
public readonly record struct JournalIdentity(string? RundownId, string? ItemId, string? Execution);

/// <summary>
/// The show journal: an append-only audit log of what happened (with session checkpoints beside it, this is what
/// crash recovery and the report use). It records events, not every state change, so it explains a show rather than
/// rebuilding one. The owner thread appends; a background writer streams entries to a JSON-lines file so nothing is
/// lost in a crash and the show thread never waits on disk. Entries carry stable identities (see <see cref="Identify"/>).
/// </summary>
public sealed class ShowJournal : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly List<JournalEntry> _entries = new();
    private readonly Channel<JournalEntry> _pending = Channel.CreateUnbounded<JournalEntry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;
    private readonly string? _path;

    public ShowJournal(string? path)
    {
        _path = path;
        _writer = path is null ? Task.CompletedTask : Task.Run(WriteLoopAsync);
    }

    public string? Path => _path;
    public IReadOnlyList<JournalEntry> Entries => _entries;
    public int Count => _entries.Count;

    /// <summary>
    /// Stamps each entry about an item with who it is about (running order, item id, current run). Set by the owner of
    /// the live show; null leaves entries as given.
    /// </summary>
    public Func<int, JournalIdentity>? Identify { get; set; }

    /// <summary>Owner thread only. O(1), never blocks.</summary>
    public void Add(JournalEntry e)
    {
        if (e.ItemId is null && Identify is { } who)
        {
            var id = who(e.Item);
            if (id.RundownId is not null || id.ItemId is not null)
                e = e with { RundownId = id.RundownId, ItemId = id.ItemId, Execution = id.Execution };
        }
        _entries.Add(e);
        if (_path is not null) _pending.Writer.TryWrite(e);
    }

    /// <summary>Load earlier entries into memory (not re-written): continuing a journal after a restart.</summary>
    public void Seed(IEnumerable<JournalEntry> entries) => _entries.InsertRange(0, entries);

    public void Add(DateTime utc, JournalKind kind, int item = -1, string text = "", double value = 0)
        => Add(new JournalEntry(utc, kind, item, text, value));

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path!)!);
            await using var fs = new FileStream(_path!, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            var sb = new StringBuilder();
            while (await _pending.Reader.WaitToReadAsync())
            {
                sb.Clear();
                while (_pending.Reader.TryRead(out var e)) sb.Append(JsonSerializer.Serialize(e, Json)).Append('\n');
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                await fs.WriteAsync(bytes);
                await fs.FlushAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* journal is best-effort */ }
    }

    /// <summary>Read a journal file back (skipping damaged lines).</summary>
    public static List<JournalEntry> Load(string path)
    {
        var list = new List<JournalEntry>();
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { if (JsonSerializer.Deserialize<JournalEntry>(line, Json) is { } e) list.Add(e); }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }
}

/// <summary>One line of the post-show report.</summary>
public sealed record ReportRow(
    int Number, string Title, string Speaker, SegmentKind Kind,
    DateTime? PlannedStartUtc, int PlannedSeconds, int AdjustSeconds,
    DateTime? ActualStartUtc, DateTime? ActualEndUtc, int? ActualSeconds,
    int? OverrunSeconds, int? WrapUpToFinishSeconds, int Messages, int Acks, double? AvgAckSeconds);

/// <summary>
/// Planned vs actual, per item — the client deliverable no stage timer produces. Built from the
/// journal, so it is exactly what happened, including who acknowledged what and how long a
/// speaker took to finish after the wrap-up cue.
/// </summary>
public sealed class ShowReport
{
    public string ShowName { get; init; } = "";
    public DateTime GeneratedUtc { get; init; }
    public List<ReportRow> Rows { get; init; } = new();
    public string TimeZoneName { get; init; } = "";
    /// <summary>The narrative the operator kept (drafted by the copilot, reviewed by a person), or "".</summary>
    public string Summary { get; init; } = "";
    /// <summary>What AI proposed and what people decided, in order (time, text).</summary>
    public List<(DateTime Utc, string Text)> AiDecisions { get; init; } = new();

    /// <summary>Journal note prefixes the copilot writes — the report collects them.</summary>
    public const string SummaryPrefix = "Show summary (reviewed): ";
    public static readonly string[] AiPrefixes = { "Copilot asked: ", "Copilot step approved", "Copilot proposal discarded", "AI agent proposed: " };

    public int TotalPlannedSeconds => Rows.Sum(r => r.PlannedSeconds);
    public int TotalActualSeconds => Rows.Sum(r => r.ActualSeconds ?? 0);
    public int ItemsRun => Rows.Count(r => r.ActualSeconds is not null);
    public int ItemsOver => Rows.Count(r => r.OverrunSeconds > 30);

    /// <summary>
    /// The report for one running order. Entries are matched by identity (running order + item id), never by position,
    /// so another running order's history can't land on this one's items; each run of an item (a loop pass, a second go)
    /// is its own row. Old journals without identities fall back to position.
    /// </summary>
    public static ShowReport Build(string showName, Rundown rundown, IReadOnlyList<JournalEntry> journal,
        DateTime? anchorUtc, ShowClock clock, DateTime nowUtc)
    {
        var items = rundown.Items;
        var plan = anchorUtc is { } a ? ScheduleEngine.Plan(items, a, clock) : null;
        var rows = new List<ReportRow>();
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var mine = journal.Where(e => e.IsAbout(rundown.Id, it, i)).ToList();
            // One row per run: runs are told apart by their execution id, in the order they started.
            var runs = mine.Where(e => e.Kind == JournalKind.ItemStart).Select(e => e.Execution).Distinct().ToList();
            if (runs.Count == 0) runs.Add(null);
            for (int r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                var these = runs.Count == 1 ? mine : mine.Where(e => e.Execution == run).ToList();
                var start = these.FirstOrDefault(e => e.Kind == JournalKind.ItemStart)?.Utc;
                var end = these.LastOrDefault(e => e.Kind == JournalKind.ItemEnd)?.Utc;
                int? actual = start is { } s && end is { } en ? (int)Math.Round((en - s).TotalSeconds) : null;
                // Wrap-up cue → finish: how responsive the speaker was to the cue.
                var wrap = these.FirstOrDefault(e => e.Kind == JournalKind.Phase && e.Text is "WrapUp" or "wrapup")?.Utc;
                int? wrapToEnd = wrap is { } w && end is { } en2 && en2 > w ? (int)Math.Round((en2 - w).TotalSeconds) : null;
                var acks = these.Where(e => e.Kind == JournalKind.Ack).ToList();
                rows.Add(new ReportRow(
                    i + 1, runs.Count > 1 ? $"{it.Title} (run {r + 1})" : it.Title, it.Speaker ?? "", it.Kind,
                    r == 0 ? plan?[i].Start : null, it.DurationSeconds, it.AdjustSeconds,
                    start, end, actual,
                    actual is { } act ? act - it.EffectiveSeconds : null,
                    wrapToEnd,
                    these.Count(e => e.Kind is JournalKind.Message or JournalKind.Signal),
                    acks.Count,
                    acks.Count > 0 ? acks.Average(e => e.Value) : null));
            }
        }
        var notes = journal.Where(e => e.Kind == JournalKind.Note).ToList();
        var summary = notes.LastOrDefault(e => e.Text.StartsWith(SummaryPrefix, StringComparison.Ordinal))?.Text[SummaryPrefix.Length..] ?? "";
        var ai = notes.Where(e => AiPrefixes.Any(p => e.Text.StartsWith(p, StringComparison.Ordinal))).Select(e => (e.Utc, e.Text)).ToList();
        return new ShowReport { ShowName = showName, GeneratedUtc = nowUtc, Rows = rows, TimeZoneName = clock.Zone.DisplayName, Summary = summary, AiDecisions = ai };
    }

    public string ToCsv(ShowClock clock)
    {
        string T(DateTime? utc) => utc is { } u ? clock.ToShow(u).ToString("yyyy-MM-dd HH:mm:ss") : "";
        string D(int? s) => s is { } v ? TimerTick.FormatSeconds(v, forceHours: true) : "";
        string Q(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        var sb = new StringBuilder("#,Title,Speaker,Kind,Planned start,Planned,Adjusted,Actual start,Actual end,Actual,Over/under,Wrap-up to finish,Messages,Acks,Avg ack (s)\r\n");
        foreach (var r in Rows)
            sb.Append(r.Number).Append(',').Append(Q(r.Title)).Append(',').Append(Q(r.Speaker)).Append(',').Append(r.Kind).Append(',')
              .Append(T(r.PlannedStartUtc)).Append(',').Append(D(r.PlannedSeconds)).Append(',').Append(r.AdjustSeconds == 0 ? "" : D(r.AdjustSeconds)).Append(',')
              .Append(T(r.ActualStartUtc)).Append(',').Append(T(r.ActualEndUtc)).Append(',').Append(D(r.ActualSeconds)).Append(',')
              .Append(D(r.OverrunSeconds)).Append(',').Append(D(r.WrapUpToFinishSeconds)).Append(',')
              .Append(r.Messages).Append(',').Append(r.Acks).Append(',').Append(r.AvgAckSeconds is { } k ? k.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "")
              .Append("\r\n");
        return sb.ToString();
    }

    /// <summary>A self-contained printable page (Print → Save as PDF gives the client a PDF).</summary>
    public string ToHtml(ShowClock clock)
    {
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        string T(DateTime? utc) => utc is { } u ? clock.ToShow(u).ToString("HH:mm:ss") : "—";
        string D(int? s) => s is { } v ? TimerTick.FormatSeconds(v) : "—";
        string O(int? s) => s is not { } v ? "—" : Math.Abs(v) < 30 ? "on time" : (v > 0 ? "+" : "−") + TimerTick.FormatSeconds(Math.Abs(v));
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>")
          .Append(E(ShowName)).Append(" — show report</title><style>")
          .Append("body{font-family:Segoe UI,system-ui,sans-serif;margin:32px;color:#111}h1{margin:0 0 4px}p.sub{color:#555;margin:0 0 20px}")
          .Append("table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #ddd;padding:6px 8px;text-align:left;vertical-align:top}")
          .Append("th{background:#f3f4f6;font-weight:600}td.n{font-variant-numeric:tabular-nums;white-space:nowrap}.over{color:#b91c1c;font-weight:600}.under{color:#15803d}")
          .Append(".kpi{display:flex;gap:28px;margin:0 0 20px}.kpi div{font-size:13px;color:#555}.kpi b{display:block;font-size:22px;color:#111}")
          .Append("h2{font-size:16px;margin:28px 0 4px}.summary{background:#f8fafc;border-left:4px solid #0ea5e9;padding:8px 14px;margin:0 0 20px;font-size:14px}.summary p{margin:6px 0}.summary p.b{margin:3px 0 3px 8px}")
          .Append("@media print{body{margin:12mm}}</style></head><body>")
          .Append("<h1>").Append(E(ShowName)).Append("</h1><p class=\"sub\">Show report · generated ")
          .Append(E(clock.ToShow(GeneratedUtc).ToString("dddd d MMMM yyyy HH:mm"))).Append(" · ").Append(E(TimeZoneName)).Append("</p>")
          .Append("<div class=\"kpi\"><div><b>").Append(ItemsRun).Append('/').Append(Rows.Count).Append("</b>items run</div>")
          .Append("<div><b>").Append(D(TotalPlannedSeconds)).Append("</b>planned</div>")
          .Append("<div><b>").Append(D(TotalActualSeconds)).Append("</b>actual</div>")
          .Append("<div><b>").Append(ItemsOver).Append("</b>items over by 30 s+</div></div>");
        if (Summary.Length > 0)
        {
            sb.Append("<div class=\"summary\">");
            foreach (var line in Summary.Split('\n'))
            {
                var l = line.Trim();
                if (l.Length == 0) continue;
                sb.Append(l.StartsWith("- ", StringComparison.Ordinal) ? "<p class=\"b\">&bull; " + E(l[2..]) + "</p>" : "<p>" + E(l) + "</p>");
            }
            sb.Append("</div>");
        }
        sb
          .Append("<table><thead><tr><th>#</th><th>Item</th><th>Planned start</th><th>Planned</th><th>Actual start</th><th>Actual end</th><th>Actual</th><th>Over / under</th><th>Wrap-up → finish</th><th>Cues / acks</th></tr></thead><tbody>");
        foreach (var r in Rows)
        {
            var cls = r.OverrunSeconds is > 30 ? "over" : r.OverrunSeconds is < -30 ? "under" : "";
            sb.Append("<tr><td class=\"n\">").Append(r.Number).Append("</td><td>").Append(E(r.Title));
            if (r.Speaker.Length > 0) sb.Append("<br><small>").Append(E(r.Speaker)).Append("</small>");
            sb.Append("</td><td class=\"n\">").Append(T(r.PlannedStartUtc)).Append("</td><td class=\"n\">").Append(D(r.PlannedSeconds));
            if (r.AdjustSeconds != 0) sb.Append(" <small>(").Append(r.AdjustSeconds > 0 ? "+" : "−").Append(TimerTick.FormatSeconds(Math.Abs(r.AdjustSeconds))).Append(")</small>");
            sb.Append("</td><td class=\"n\">").Append(T(r.ActualStartUtc)).Append("</td><td class=\"n\">").Append(T(r.ActualEndUtc))
              .Append("</td><td class=\"n\">").Append(D(r.ActualSeconds)).Append("</td><td class=\"n ").Append(cls).Append("\">").Append(O(r.OverrunSeconds))
              .Append("</td><td class=\"n\">").Append(D(r.WrapUpToFinishSeconds)).Append("</td><td class=\"n\">").Append(r.Messages).Append(" / ").Append(r.Acks);
            if (r.AvgAckSeconds is { } k) sb.Append(" <small>(").Append(k.ToString("0", System.Globalization.CultureInfo.InvariantCulture)).Append(" s)</small>");
            sb.Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (AiDecisions.Count > 0)
        {
            sb.Append("<h2>AI suggestions and decisions</h2><p class=\"sub\">Everything the copilot or AI agents proposed, and what the operator did. Nothing ran without approval.</p><table><tbody>");
            foreach (var (utc, text) in AiDecisions)
                sb.Append("<tr><td class=\"n\">").Append(T(utc)).Append("</td><td>").Append(E(text)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("<p class=\"sub\" style=\"margin-top:18px\">Generated by Stagewatch.</p></body></html>");
        return sb.ToString();
    }
}
