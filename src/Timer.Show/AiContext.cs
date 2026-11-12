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
/// (key, operator, none, or "" when nothing spoke for it), so a context only claimed is read as a claim. <see cref="Id"/> is its
/// Show Bus id (what <c>window grant &lt;window&gt; &lt;app&gt;</c> names it by), and <see cref="Can"/> what it can do with a window
/// (12.6's abilities, <c>pages</c> or <c>window-draw</c>): its key-verified node link's <c>can=</c> while it has one, else its
/// hello's caps, each kept only for the kinds its cap row names. For a copilot plan (<see cref="AiContext.Apps"/>): whether it
/// <see cref="Collects"/> this desk's approved lines now, whether its verbs are verified (<see cref="VerbsVerified"/>: only then are
/// any of them given), the words its verbs open with (<see cref="VerbHeads"/>), and its whole table (<see cref="Verbs"/>) only when
/// the request names it or the plan asked for it, as much of it as the budgets allow, with how many rows were left out
/// (<see cref="MoreVerbs"/>).
/// </summary>
public sealed record PeerFact(string Kind, string Name, string Light, string Headline, IReadOnlyList<string> Parts, IReadOnlyList<string> Recent,
    string Link = "", string Verified = "", string Id = "", IReadOnlyList<string>? Can = null, bool Collects = false, bool VerbsVerified = false,
    IReadOnlyList<string>? VerbHeads = null, IReadOnlyList<PeerCommand>? Verbs = null, int MoreVerbs = 0);

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
    IReadOnlyList<WindowFact>? Windows = null,
    IReadOnlyList<PlayoutFact>? Playout = null,
    PreflightResult? Preflight = null);

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

    /// <summary>A JSON object's fields but <paramref name="field"/>, in their order; any other JSON value as it is.</summary>
    private static void WriteWithout(Utf8JsonWriter w, byte[] json, string field)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            doc.RootElement.WriteTo(w);
            return;
        }
        w.WriteStartObject();
        foreach (var p in doc.RootElement.EnumerateObject())
            if (!p.NameEquals(field)) p.WriteTo(w);
        w.WriteEndObject();
    }

    /// <summary>
    /// The copilot's context: the operator frame and the config (each without what other apps wrote into it: the frame's desk, the
    /// config's peers), presets and messages, screens, links, history, recent log.
    /// </summary>
    public static string Copilot(CopilotFacts f)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("now", f.Clock.ToShow(f.NowUtc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            // The frame and the config as the operator's readers get them, less what other apps wrote: the desk block (a waiting
            // proposal's line, the apps' names) and the config's peers (names from hellos, which prove nothing). Another app's words
            // reach the model only as that app's data (other_apps), never as this Stagewatch's own state.
            w.WritePropertyName("show");
            WriteWithout(w, f.FrameUtf8, "desk");
            w.WritePropertyName("config");
            WriteWithout(w, f.ConfigUtf8, "peers");
            w.WritePropertyName("screens");
            // Browsers and claims; a show app is never a screen (other_apps lists it, with its link).
            JsonSerializer.Serialize(w, f.Screens.Select(s => new { id = s.Id, name = s.Name, page = s.Page, device = s.Device, control = s.Control, claim = s.Claim }));
            w.WritePropertyName("screen_setups");
            JsonSerializer.Serialize(w, f.ScreenSetups.Select(p => new { name = p.Name, page = p.Path }));
            // The windows Stagewatch controls ("window show <name> <page>"): what each is given (a page, holding or black), its light and
            // what it says it shows, the show apps it is granted to and what they claim about where they show it (their words, never a
            // fact this Stagewatch saw). Never a token or a path to one.
            w.WritePropertyName("windows");
            JsonSerializer.Serialize(w, (f.Windows ?? Array.Empty<WindowFact>()).Select(x => new
            {
                name = x.Name, showing = x.Showing, light = x.Item.Light, granted = x.Granted, on = x.On, state = x.Item.State,
            }));
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
            // This show's own rows: a peer's lines are another node's words, read from its facts above, never as what happened here.
            JsonSerializer.Serialize(w, ShowJournal.LastOwn(f.Journal, RecentLines)
                .Select(e => string.Create(CultureInfo.InvariantCulture, $"{f.Clock.ToShow(e.Utc):HH:mm:ss} {e.Kind}: {e.Text}")));
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
            // 'Ready for doors' as this Stagewatch judges the room: ready while nothing is red, and the rows that are red or amber
            // (amber never blocks). Left out by a host that keeps no preflight.
            if (f.Preflight is { } preflight)
            {
                w.WritePropertyName("doors");
                JsonSerializer.Serialize(w, Doors(preflight));
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// The room's verdict as the copilot sees it: ready, the counts, and the rows that are red or amber, reds first, at most
    /// <see cref="NetworkPreflight.AiRows"/> — each with what it is about, its check, its light, why, what makes the app expected and
    /// what to do. These are this Stagewatch's own judgement, so nothing another app chose is in them: a node is named by its kind
    /// and id (<c>node</c> joins it to <c>other_apps</c>), never by the name its hello or context told; its <c>own</c> row says its
    /// light and an id two machines say says that much, each pointing to <c>other_apps</c>, where an app's words are that app's data.
    /// </summary>
    public static object Doors(PreflightResult preflight) => new
    {
        ready = preflight.Ready,
        red = preflight.Red,
        amber = preflight.Amber,
        rows = preflight.Rows.Where(r => r.Light is Light.Red or Light.Amber).Take(NetworkPreflight.AiRows).Select(r => new
        {
            about = r.Node.Length > 0 ? $"{ShowNetwork.AppName(r.Kind)} {r.Node}" : r.Name.Length > 0 ? r.Name : r.Id,
            node = r.Node,
            check = r.Check,
            light = r.Light,
            words = DoorWords(r),
            configured = r.Configured,
            fix = r.Fix,
        }).ToList(),
    };

    /// <summary>A row's words for the model: Stagewatch's own, with a row that quotes another app said in fixed words instead.</summary>
    private static string DoorWords(PreflightRow r)
    {
        if (r.Node.Length == 0) return r.Words;
        if (r.Check == NetworkPreflight.OwnCheck) return $"its own parts are {r.Light} (what they say is under other_apps)";
        if (NetworkPreflight.IsConflict(r)) return "two machines say this id (who they are is under other_apps)";
        return r.Words;
    }

    /// <summary>
    /// What the AI sees of the other apps, from one join of the show network (<see cref="ShowNetwork.Summaries"/>): the rows the
    /// context lists, each with its light, headline, problem parts and recent lines, whether it follows the timer over /ws and what
    /// proves those facts; and each id two running machines say (§2), as the desk shows it — a row of that id amber with the
    /// conflict's words before its headline, and an id only its hello names as a fact of its own, which the context does not list.
    /// </summary>
    public static IReadOnlyList<PeerFact> PeerFacts(NetworkView view, IReadOnlyDictionary<string, IReadOnlyList<string>>? helloCaps = null)
    {
        var conflicts = view.Conflicts.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var facts = new List<PeerFact>(view.Rows.Count + view.Conflicts.Count);
        foreach (var r in view.Rows)
        {
            var fact = new PeerFact(r.Peer.Kind, r.Peer.Name, r.Peer.Light, r.Peer.Headline,
                r.Context?.Items.Select(i => $"{i.Label} [{i.Light}]: {i.State}").Take(24).ToList() ?? new List<string>(),
                r.Context?.Recent.TakeLast(8).ToList() ?? new List<string>(), r.Peer.Link, r.Peer.Verified, r.Peer.Id, Can(r, helloCaps));
            facts.Add(conflicts.TryGetValue(r.Peer.Id, out var c) ? fact with { Light = Light.Amber, Headline = c.Conflict + " — " + r.Peer.Headline } : fact);
        }
        var listed = view.Rows.Select(r => r.Peer.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in view.Conflicts.Where(c => !listed.Contains(c.Id)))
            facts.Add(new PeerFact(c.Kind, c.Name.Length > 0 ? c.Name : $"{ShowNetwork.AppName(c.Kind)} {c.Id}", Light.Amber, c.Conflict,
                Array.Empty<string>(), Array.Empty<string>(), Id: c.Id));
        return facts;
    }

    /// <summary>
    /// What a row's app can do with a window (12.6): its node link's abilities while a key bound to its id proves that link — none when
    /// that link offers none, whatever a hello says, since delivery reads that link's <c>can=</c> alone (D3) — else its hello's caps read
    /// as <c>can=</c> is (<see cref="WindowGrants.Offered"/>: only an ability whose cap row names its kind), none when neither says.
    /// </summary>
    private static IReadOnlyList<string> Can(NetworkRow r, IReadOnlyDictionary<string, IReadOnlyList<string>>? helloCaps)
        => ShowNetwork.WindowAbilities(r, helloCaps);

    /// <summary>
    /// An app as the copilot reads it. Its verbs come last: whether it collects, whether its verbs are verified, their heads, and —
    /// only when given — the whole rows (verb, risk, words) and how many were left out; no row carries more.
    /// </summary>
    private static Dictionary<string, object?> PeerJson(PeerFact p)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["app"] = p.Kind, ["name"] = p.Name, ["light"] = p.Light, ["headline"] = p.Headline, ["parts"] = p.Parts, ["recent"] = p.Recent,
            ["link"] = p.Link, ["verified"] = p.Verified, ["id"] = p.Id, ["can"] = p.Can ?? [],
            ["collects"] = p.Collects, ["verbs_verified"] = p.VerbsVerified, ["verb_heads"] = p.VerbHeads ?? [],
        };
        if (p.Verbs is { } verbs)
        {
            row["verbs"] = verbs.Select(VerbJson);
            row["more_verbs"] = p.MoreVerbs;
        }
        return row;
    }

    private static object VerbJson(PeerCommand c) => new { verb = c.Verb, risk = c.Risk, words = c.Words };

    /// <summary>The most bytes of one app's whole verbs (its rows, as JSON) the copilot is given.</summary>
    public const int VerbsPerAppBytes = 32 * 1024;

    /// <summary>The most bytes of every app's whole verbs together the copilot is given.</summary>
    public const int VerbsTotalBytes = 64 * 1024;

    /// <summary>
    /// The other apps as a copilot plan reads them: each fact (by its id) joined to the desk's app of that id — whether it collects and
    /// whether its verbs are verified — and, only for a verified app, the distinct words its forms open with, in its table's order
    /// (<see cref="VerbSyntax.Lead"/>). The apps in <paramref name="full"/> also get their whole rows, in order, while each app's rows
    /// stay within <see cref="VerbsPerAppBytes"/> and all apps' within <see cref="VerbsTotalBytes"/> (counted as the UTF-8 JSON of the
    /// rows, in the facts' order), with the rows left out counted. An unverified table, or a fact that is no app the desk lists, gives
    /// no verbs: what proves nothing never reaches the model.
    /// </summary>
    public static IReadOnlyList<PeerFact> Apps(IReadOnlyList<PeerFact> facts, IReadOnlyList<DeskApp> apps, Func<string, IReadOnlyList<PeerCommand>> table,
        IReadOnlyCollection<string> full)
    {
        var left = VerbsTotalBytes;
        var joined = new List<PeerFact>(facts.Count);
        foreach (var f in facts)
        {
            if (apps.FirstOrDefault(a => a.Id == f.Id && f.Id.Length > 0) is not { } app)
            {
                joined.Add(f with { Collects = false, VerbsVerified = false, VerbHeads = [], Verbs = null, MoreVerbs = 0 });
                continue;
            }
            if (!app.Verified)
            {
                joined.Add(f with { Collects = app.Collects, VerbsVerified = false, VerbHeads = [], Verbs = null, MoreVerbs = 0 });
                continue;
            }
            var rows = table(app.Id);
            var heads = rows.SelectMany(r => VerbSyntax.Forms(r.Verb)).Select(VerbSyntax.Lead).Where(h => h.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<PeerCommand>? verbs = null;
            var more = 0;
            if (full.Contains(app.Id))
            {
                verbs = [];
                var mine = 0;
                foreach (var r in rows)
                {
                    // A row and the comma before it, as the copilot's context writes it. The rows given are the table's first ones:
                    // once one does not fit, it and every row after it are counted, never skipped over for a shorter one.
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(VerbJson(r)).Length + 1;
                    if (more == 0 && mine + bytes <= VerbsPerAppBytes && bytes <= left)
                    {
                        verbs.Add(r);
                        mine += bytes;
                        left -= bytes;
                    }
                    else more++;
                }
            }
            joined.Add(f with { Collects = app.Collects, VerbsVerified = true, VerbHeads = heads, Verbs = verbs, MoreVerbs = more });
        }
        return joined;
    }

    /// <summary>The cloud assistant's compact view: titles, plan, expected/actual, curfews, live remaining, the other apps.</summary>
    public static string Assistant(IReadOnlyList<RundownItem> items, ScheduleProjection p, ShowClock clock, DateTime nowUtc,
        int liveItem, TimeSpan liveRemaining, TimeSpan pendingDelay, int qaQueued, IReadOnlyList<PeerFact>? peers = null)
    {
        var rows = items.Select((it, i) => new
        {
            n = i + 1, title = it.Title, kind = it.Kind.ToString(), planned_min = it.DurationSeconds / 60.0, adjust_s = it.AdjustSeconds,
            min_s = it.MinDurationSeconds, start_mode = it.StartMode.ToString(), fixed_start = it.FixedStart, hard_out = it.HardOut,
            planned_start = i < p.Items.Length ? clock.ToShow(p.Items[i].PlannedStartUtc).ToString("HH:mm", CultureInfo.InvariantCulture) : null,
            expected_start = i < p.Items.Length ? clock.ToShow(p.Items[i].ExpectedStartUtc).ToString("HH:mm", CultureInfo.InvariantCulture) : null,
            status = i < p.Items.Length ? p.Items[i].Status.ToString() : null
        });
        return JsonSerializer.Serialize(new
        {
            now = clock.ToShow(nowUtc).ToString("HH:mm:ss", CultureInfo.InvariantCulture),
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
        // This show's own rows: the summary is of what happened here, and another node's words are never a model's input through it.
        var entries = ShowJournal.LastOwn(journal, SummaryEntries).Select(e => new
        {
            t = clock.ToShow(e.Utc).ToString("HH:mm:ss", CultureInfo.InvariantCulture), k = e.Kind.ToString(),
            item = e.ItemId is { } id ? byId.GetValueOrDefault(id, "") : "",
            x = e.Text, v = e.Value
        });
        var plan = settings.Rundown.Items.Select((it, i) => new { n = i + 1, title = it.Title, planned_min = it.DurationSeconds / 60.0, kind = it.Kind.ToString() });
        return JsonSerializer.Serialize(new { running_order = plan, journal = entries });
    }
}
