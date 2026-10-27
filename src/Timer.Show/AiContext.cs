using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// A browser screen as the copilot sees it. <see cref="Claim"/> is what a socket that only claims to be a show app claims
/// ("patterns 9f8e7d6c"; "" for a browser): listed so the operator can drop it, never reached by "all".
/// </summary>
public sealed record ScreenFact(string Id, string Name, string Page, string Device, bool Control, string Claim = "");

/// <summary>
/// Another show app as an AI sees it (Show Bus §6, one hop): light, headline, parts, recent lines; <see cref="Link"/> is
/// "ws" while it follows this Stagewatch over a proven node link, and <see cref="Verified"/> what proves those facts now
/// (key, operator, none, or "" when nothing spoke for it), so a context only claimed is read as a claim.
/// </summary>
public sealed record PeerFact(string Kind, string Name, string Light, string Headline, IReadOnlyList<string> Parts, IReadOnlyList<string> Recent,
    string Link = "", string Verified = "");

/// <summary>An integration's link as the copilot sees it ("why isn't vMix switching?").</summary>
public sealed record IntegrationFact(string Name, string Health, string Status);

/// <summary>
/// A playout link as the copilot sees it, so a line it proposes names a real link and a real playlist: its name, the peer's kind once
/// heard ("" while not), its peer as people read it, whether it is paired, whether its clock may time a GO (13.11.6), its record's light
/// and words, what a PlayMap node's WHOAMI says, and its playlists (at most <see cref="AiContext.PlayoutPlaylistsMax"/>, each with the
/// item on it and the next) or a Patterns desk's standing-by cue; what the peer lists for running-order cues (<paramref name="Listed"/>:
/// a node's playlists' items or a desk's cues, at most <see cref="AiContext.PlayoutListedMax"/>), and the running order's cues on the
/// link (<paramref name="Cues"/>, at most as many). Ids, as the peer gave them; no token, proof or nonce.
/// </summary>
public sealed record PlayoutFact(string Name, string Kind, string Peer, bool Paired, bool Timed, string Light, string Words, string Whoami,
    IReadOnlyList<PlayoutPlaylistFact> Playlists, string Standby, IReadOnlyList<string>? Listed = null, IReadOnlyList<string>? Cues = null);

/// <summary>One of a PlayMap node's playlists for the copilot: its id and name, its state, the item on it and the next (id · label).</summary>
public sealed record PlayoutPlaylistFact(string Id, string Name, string State, string Item, string Next);

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
    IReadOnlyList<PeerFact>? Peers = null,
    IReadOnlyList<ContextItem>? Windows = null,
    IReadOnlyList<PlayoutFact>? Playout = null);

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

    /// <summary>The most playlists of one playout link the copilot sees.</summary>
    public const int PlayoutPlaylistsMax = 50;

    /// <summary>The most items or cues a playout link's peer lists, and the most running-order cues on one link, the copilot sees.</summary>
    public const int PlayoutListedMax = 50;

    /// <summary>
    /// The running order's cues on the link called <paramref name="link"/>, as the copilot sees them, at most
    /// <see cref="PlayoutListedMax"/>: the item (number and title), what the cue names (its playlist and item, or its cue) and label,
    /// and whether it stands by at load or goes together with the item's others.
    /// </summary>
    public static IReadOnlyList<string> PlayoutCues(IReadOnlyList<RundownItem> items, string link)
    {
        var lines = new List<string>();
        for (var n = 0; n < items.Count && lines.Count < PlayoutListedMax; n++)
            foreach (var c in items[n].Playout ?? [])
            {
                if (c.Link != link) continue;
                if (lines.Count == PlayoutListedMax) break;
                var what = (c.List.Length > 0 ? c.List + " " + c.Item : c.Item) + (c.Label.Length > 0 ? " · " + c.Label : "");
                var how = (c.Standby ? ", stands by at load" : "") + (c.Together ? ", together" : "");
                lines.Add($"item {(n + 1).ToString(CultureInfo.InvariantCulture)} ({items[n].Title}): {what}{how}");
            }
        return lines;
    }

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
            // Browsers and claims; a show app is never a screen (other_apps lists it, with its link).
            JsonSerializer.Serialize(w, f.Screens.Select(s => new { id = s.Id, name = s.Name, page = s.Page, device = s.Device, control = s.Control, claim = s.Claim }));
            w.WritePropertyName("screen_setups");
            JsonSerializer.Serialize(w, f.ScreenSetups.Select(p => new { name = p.Name, page = p.Path }));
            // The windows Stagewatch controls ("window show <name> <page>"): what each should show and what it says it shows.
            w.WritePropertyName("windows");
            JsonSerializer.Serialize(w, (f.Windows ?? Array.Empty<ContextItem>()).Select(x => new { name = x.Label.StartsWith("Window ", StringComparison.Ordinal) ? x.Label[7..] : x.Label, light = x.Light, state = x.State }));
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
            // The other show apps in the room (Patterns, AGMessage…): their lights are evidence; grey means unknown. Which
            // follow the timer over /ws, and what proves each one's facts: a context verified "none" is only claimed.
            w.WritePropertyName("other_apps");
            JsonSerializer.Serialize(w, (f.Peers ?? Array.Empty<PeerFact>()).Select(PeerJson));
            // The playout links this Stagewatch runs: what "playout <link> go <playlist> <item>" can name, and how far each can be
            // trusted now (its light, its words, whether its clock may time a GO). Every playout line is high risk and a proposal.
            w.WritePropertyName("playout_links");
            JsonSerializer.Serialize(w, (f.Playout ?? Array.Empty<PlayoutFact>()).Take(PlayoutRules.Max).Select(p => new
            {
                name = p.Name, kind = p.Kind, peer = p.Peer, paired = p.Paired, timed = p.Timed, light = p.Light, words = p.Words, whoami = p.Whoami,
                playlists = p.Playlists.Take(PlayoutPlaylistsMax).Select(l => new { id = l.Id, name = l.Name, state = l.State, item = l.Item, next = l.Next }),
                standby = p.Standby,
                listed = (p.Listed ?? []).Take(PlayoutListedMax),
                cues = (p.Cues ?? []).Take(PlayoutListedMax),
            }));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// What the AI sees of the other apps, from one join of the show network (<see cref="ShowNetwork.Summaries"/>): the rows the
    /// context lists, each with its light, headline, problem parts and recent lines, whether it follows the timer over /ws and what
    /// proves those facts; and each id two running machines say (§2), as the desk shows it — a row of that id amber with the
    /// conflict's words before its headline, and an id only its hello names as a fact of its own, which the context does not list.
    /// </summary>
    public static IReadOnlyList<PeerFact> PeerFacts(NetworkView view)
    {
        var conflicts = view.Conflicts.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var facts = new List<PeerFact>(view.Rows.Count + view.Conflicts.Count);
        foreach (var r in view.Rows)
        {
            var fact = new PeerFact(r.Peer.Kind, r.Peer.Name, r.Peer.Light, r.Peer.Headline,
                r.Context?.Items.Select(i => $"{i.Label} [{i.Light}]: {i.State}").Take(24).ToList() ?? new List<string>(),
                r.Context?.Recent.TakeLast(8).ToList() ?? new List<string>(), r.Peer.Link, r.Peer.Verified);
            facts.Add(conflicts.TryGetValue(r.Peer.Id, out var c) ? fact with { Light = Light.Amber, Headline = c.Conflict + " — " + r.Peer.Headline } : fact);
        }
        var listed = view.Rows.Select(r => r.Peer.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in view.Conflicts.Where(c => !listed.Contains(c.Id)))
            facts.Add(new PeerFact(c.Kind, c.Name.Length > 0 ? c.Name : $"{ShowNetwork.AppName(c.Kind)} {c.Id}", Light.Amber, c.Conflict,
                Array.Empty<string>(), Array.Empty<string>()));
        return facts;
    }

    private static object PeerJson(PeerFact p) => new { app = p.Kind, name = p.Name, light = p.Light, headline = p.Headline, parts = p.Parts, recent = p.Recent, link = p.Link, verified = p.Verified };

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
            other_apps = (peers ?? Array.Empty<PeerFact>()).Select(x => new { app = x.Kind, name = x.Name, light = x.Light, headline = x.Headline, link = x.Link, verified = x.Verified })
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
