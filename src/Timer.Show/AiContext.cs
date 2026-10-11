using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>A browser screen as the copilot sees it.</summary>
public sealed record ScreenFact(string Id, string Name, string Page, string Device, bool Control);

/// <summary>Another show app as an AI sees it (Show Bus §6, one hop): light, headline, parts, recent lines.</summary>
public sealed record PeerFact(string Kind, string Name, string Light, string Headline, IReadOnlyList<string> Parts, IReadOnlyList<string> Recent);

/// <summary>An integration's link as the copilot sees it ("why isn't vMix switching?").</summary>
public sealed record IntegrationFact(string Name, string Health, string Status);

/// <summary>
/// Everything the copilot may reason about, gathered on the owner thread. The frame and remote config arrive already
/// serialized as UTF-8 JSON (operator access), so this layer needs nothing from the web server.
/// </summary>
public sealed record CopilotFacts(
    DateTime NowUtc,
    ShowClock Clock,
    byte[] FrameUtf8,
    byte[] ConfigUtf8,
    IReadOnlyList<ScreenFact> Screens,
    IReadOnlyList<ScreenPreset> ScreenSetups,
    IReadOnlyList<IntegrationFact> Integrations,
    int HistoryRecords,
    OverrunModel Model,
    IReadOnlyList<RundownItem> Items,
    IReadOnlyList<JournalEntry> Journal,
    IReadOnlyList<PeerFact>? Peers = null);

/// <summary>
/// What the AI sees, as JSON: the copilot's context, the cloud assistant's compact view of the show, and the journal
/// a show summary is drafted from. Pure functions of their inputs — no UI, no network — so what reaches the model is
/// tested headless and the view model only gathers the facts.
/// </summary>
public static class AiContext
{
    /// <summary>How many journal lines the copilot sees: refused commands, link drops, what just happened.</summary>
    public const int RecentLines = 30;

    /// <summary>How many journal entries a summary draft is written from.</summary>
    public const int SummaryEntries = 1500;

    /// <summary>The copilot's context: the operator frame, presets and messages, screens, links, history, recent log.</summary>
    public static string Copilot(CopilotFacts f)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("now", f.Clock.ToShow(f.NowUtc).ToString("yyyy-MM-dd HH:mm:ss"));
            w.WritePropertyName("show");
            w.WriteRawValue(f.FrameUtf8);
            w.WritePropertyName("config");
            w.WriteRawValue(f.ConfigUtf8);
            w.WritePropertyName("screens");
            JsonSerializer.Serialize(w, f.Screens.Select(s => new { id = s.Id, name = s.Name, page = s.Page, device = s.Device, control = s.Control }));
            w.WritePropertyName("screen_setups");
            JsonSerializer.Serialize(w, f.ScreenSetups.Select(p => new { name = p.Name, page = p.Path }));
            // How each link is doing, so "why isn't OBS switching?" has an answer.
            w.WritePropertyName("integrations");
            JsonSerializer.Serialize(w, f.Integrations.Select(x => new { name = x.Name, health = x.Health, status = x.Status }));
            // This operator's history: how much each item is likely to run over, learned from their past shows.
            w.WritePropertyName("history");
            JsonSerializer.Serialize(w, new
            {
                items_learned_from = f.HistoryRecords,
                items = f.Items.Select((it, i) =>
                {
                    var (mu, sigma) = f.Model.For(it.Kind, it.Speaker);
                    return new
                    {
                        n = i + 1, title = it.Title, kind = it.Kind.ToString(), speaker = it.Speaker ?? "",
                        planned_min = Math.Round(it.DurationSeconds / 60.0, 1),
                        expected_overrun_pct = Math.Round((Math.Exp(mu) - 1) * 100),
                        p90_overrun_pct = Math.Round((Math.Exp(mu + 1.2816 * sigma) - 1) * 100),
                    };
                }),
            });
            w.WritePropertyName("recent");
            JsonSerializer.Serialize(w, f.Journal.TakeLast(RecentLines).Select(e => $"{f.Clock.ToShow(e.Utc):HH:mm:ss} {e.Kind}: {e.Text}"));
            // The other show apps in the room (Patterns, AGMessage…): their lights are evidence; grey means unknown.
            w.WritePropertyName("other_apps");
            JsonSerializer.Serialize(w, (f.Peers ?? Array.Empty<PeerFact>()).Select(PeerJson));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static object PeerJson(PeerFact p) => new { app = p.Kind, name = p.Name, light = p.Light, headline = p.Headline, parts = p.Parts, recent = p.Recent };

    /// <summary>The cloud assistant's compact view: titles, plan, expected/actual, curfews, live remaining, the other apps.</summary>
    public static string Assistant(IReadOnlyList<RundownItem> items, ScheduleProjection p, ShowClock clock, DateTime nowUtc,
        int liveItem, TimeSpan liveRemaining, TimeSpan pendingDelay, int qaQueued, IReadOnlyList<PeerFact>? peers = null)
    {
        var rows = items.Select((it, i) => new
        {
            n = i + 1, title = it.Title, kind = it.Kind.ToString(), planned_min = it.DurationSeconds / 60.0, adjust_s = it.AdjustSeconds,
            min_s = it.MinDurationSeconds, start_mode = it.StartMode.ToString(), fixed_start = it.FixedStart, hard_out = it.HardOut,
            planned_start = i < p.Items.Length ? clock.ToShow(p.Items[i].PlannedStartUtc).ToString("HH:mm") : null,
            expected_start = i < p.Items.Length ? clock.ToShow(p.Items[i].ExpectedStartUtc).ToString("HH:mm") : null,
            status = i < p.Items.Length ? p.Items[i].Status.ToString() : null
        });
        return JsonSerializer.Serialize(new
        {
            now = clock.ToShow(nowUtc).ToString("HH:mm:ss"),
            live_item = liveItem >= 0 ? liveItem + 1 : 0,
            live_remaining_s = (long)liveRemaining.TotalSeconds,
            offset_s = p.OffsetSeconds,
            pending_delay_s = (long)pendingDelay.TotalSeconds,
            qa_queued = qaQueued,
            items = rows,
            other_apps = (peers ?? Array.Empty<PeerFact>()).Select(x => new { app = x.Kind, name = x.Name, light = x.Light, headline = x.Headline })
        });
    }

    /// <summary>
    /// What a show summary is drafted from: the live running order and the journal. Items are named by identity across
    /// every running order, never by position in whichever one is live now.
    /// </summary>
    public static string SummaryInput(AppSettings settings, IReadOnlyList<JournalEntry> journal, ShowClock clock)
    {
        var byId = new[] { (Name: settings.RundownName, Rundown: settings.Rundown) }
            .Concat(settings.OtherRundowns.Select(o => (o.Name, o.Rundown)))
            .SelectMany(o => o.Rundown.Items.Select((it, i) => (it.Id, Label: $"{o.Name} {i + 1}. {it.Title}")))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Label);
        var entries = journal.TakeLast(SummaryEntries).Select(e => new
        {
            t = clock.ToShow(e.Utc).ToString("HH:mm:ss"), k = e.Kind.ToString(),
            item = e.ItemId is { } id ? byId.GetValueOrDefault(id, "") : "",
            x = e.Text, v = e.Value
        });
        var plan = settings.Rundown.Items.Select((it, i) => new { n = i + 1, title = it.Title, planned_min = it.DurationSeconds / 60.0, kind = it.Kind.ToString() });
        return JsonSerializer.Serialize(new { running_order = plan, journal = entries });
    }
}
