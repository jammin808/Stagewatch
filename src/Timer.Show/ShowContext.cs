using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>What Stagewatch knows about the room when it builds its context (gathered on the owner thread).</summary>
public sealed record ContextInput(
    string NodeId,
    string Name,
    DateTime NowUtc,
    ShowSnapshot Frame,
    bool ShowThreadStuck,
    IReadOnlyList<ContextItem> Links,
    IReadOnlyList<PeerSummary> Peers,
    IReadOnlyList<JournalEntry> Journal,
    ShowClock Clock,
    double GoLatencyP95Ms,
    IReadOnlyList<PlayoutRow>? Playout = null);

/// <summary>
/// A playout link a peer or an AI may propose lines for: its name, as people read it, the peer's kind once known
/// (<see cref="PlayoutRules.Patterns"/> or <see cref="PlayoutRules.PlayMap"/>, else ""), and whether this Stagewatch can cue and
/// start something on it as well as move its clip on air.
/// </summary>
public sealed record PlayoutRow(string Name, string Label, string Kind, bool Starts);

/// <summary>
/// A playout link as its context items take it (Show Bus 13.16.4): its name (the items' ids are built from it alone), its peer as
/// people read it, the light of its record (13.16.3) and the words that go with it, and its clock as its matched pongs believe it.
/// </summary>
public sealed record PlayoutLinkFact(string Name, string Peer, string Light, string Words, IReadOnlyList<string>? Flags = null, double? ClockOffsetMs = null,
    long? ClockErrMs = null, bool ClockApart = false);

/// <summary>
/// Stagewatch's Show Bus context (§6): its light, a one-line headline and its parts — the timer, the schedule, each
/// department's cue, the speaker's message, every link — plus recent events, the verbs a peer or an AI may propose, and
/// the peers it hears. Computed from facts by rules (no model), so the Eye, a dashboard, a Stream Deck and an AI all read
/// the same truth, online or not. It is always within §14's context sizes: every text within its field's characters, and the
/// whole within its counts and bytes, trimmed in §6's order with one grey <see cref="TrimmedId"/> item saying what it left
/// out. Pure; tested headless.
/// </summary>
public static class ShowContext
{
    /// <summary>§14 <c>context.bytes</c>: the most a context takes as sent, counted in UTF-8 bytes.</summary>
    public const int MaxBytes = 65536;

    /// <summary>§14 <c>context.items</c>: the most items, the <see cref="TrimmedId"/> item among them.</summary>
    public const int MaxItems = 200;

    /// <summary>§14 <c>context.links</c>.</summary>
    public const int MaxLinks = 400;

    /// <summary>§14 <c>context.recent</c>: the journal's last lines, no more.</summary>
    public const int MaxRecent = 20;

    /// <summary>§14 <c>context.commands</c>.</summary>
    public const int MaxCommands = 256;

    /// <summary>§14 <c>context.peers</c>.</summary>
    public const int MaxPeers = 32;

    /// <summary>§14 <c>context.headline.chars</c>, the context's headline and each peer's.</summary>
    public const int HeadlineChars = 200;

    /// <summary>§14 <c>context.item.id.chars</c>, <c>context.item.kind.chars</c>, <c>context.item.label.chars</c> and <c>context.item.state.chars</c>.</summary>
    public const int IdChars = 60, KindChars = 30, LabelChars = 80, StateChars = 160;

    /// <summary>§14 <c>context.recent.chars</c>.</summary>
    public const int RecentChars = 200;

    /// <summary>
    /// How short §6's last resort cuts an amber or a red item's state, its "…" included: enough to name what is wrong. §6 leaves the
    /// length to the sender and §14 has no row for it, so this is Stagewatch's own choice as a sender, and the context:trimmed item's
    /// words keep to §6's "left out: …" alone (a cut state says it was cut by its "…"). A state no longer than this is never cut.
    /// </summary>
    public const int LastResortChars = 40;

    /// <summary>The id of the one grey item a trimmed context adds, saying what it left out (§6).</summary>
    public const string TrimmedId = "context:trimmed";

    /// <summary>
    /// The verbs a peer or an AI may propose, with their risk (high is never pre-ticked by any assistant). <c>Air</c> marks §6's panic,
    /// air and transport commands, listed first and left out last: what blacks out every screen, what puts a page on a screen at once,
    /// and what starts, pauses or moves the show or Patterns' clip — a department's GO among them, the call that moves a called show.
    /// The rest follow by what an operator needs most on a failing night, a standby before messages, time and notes, because trimming
    /// leaves commands out from the end of the list.
    /// </summary>
    public static readonly IReadOnlyList<(string Verb, string Risk, string Words, bool Air)> Commands = new[]
    {
        ("blackout", "high", "Black out every screen", true),
        ("window hold <window>", "high", "Put a window on its holding page", true),
        ("window show <window> <page>", "high", "Put a page on a controlled browser window (the audience sees it at once)", true),
        ("window revoke <window>", "high", "Give a window a new address (the screen on the old one goes dark)", true),
        ("start / pause", "high", "Start or pause the timer", true),
        ("next", "high", "Go to the next item", true),
        ("patterns end 10", "high", "Jump Patterns' clip to its last 10 seconds", true),
        ("signal go all", "high", "GO every department standing by", true),
        ("signal go <dept> <cue>", "high", "GO one department", true),
        ("signal standby <dept> <cue>", "review", "Stand a department by", false),
        ("message <text> warning", "review", "Put a message on the speaker's screen", false),
        ("nudge <seconds>", "review", "Add or take time from the live item", false),
        ("delay <seconds>", "review", "Announce a delay before the next item", false),
        ("trim <item> <seconds>", "review", "Lengthen or shorten a coming item", false),
        ("note <text>", "low", "Add a line to the show log", false),
    };

    /// <summary>
    /// <see cref="Commands"/> with each playout link's rows among the panic, air and transport commands (all high: what they do is on
    /// another node's air): moving its clip for every link it runs, and cueing and starting for a link that can, in its kind's
    /// words. With no link, <see cref="Commands"/> as it is.
    /// </summary>
    public static IReadOnlyList<(string Verb, string Risk, string Words, bool Air)> CommandsFor(IReadOnlyList<PlayoutRow>? links)
    {
        if (links is not { Count: > 0 }) return Commands;
        var rows = new List<(string, string, string, bool)>();
        foreach (var l in links)
        {
            var on = "playout " + l.Name;
            rows.Add(($"{on} play|pause|stop|restart", "high", $"Play, pause, stop (back to the top, held) or restart the clip on air on {l.Label}", true));
            rows.Add(($"{on} end [<seconds>]", "high", $"Jump {l.Label}'s clip to its last seconds (10 unless said)", true));
            rows.Add(($"{on} seek <m:ss>", "high", $"Move {l.Label}'s clip to a point", true));
            if (!l.Starts) continue;
            if (l.Kind == PlayoutRules.PlayMap)
            {
                // together: at one instant on the node's clock where that clock is fit for it (Show Bus 13.11.6), else at once and said.
                rows.Add(($"{on} go <playlist> [<item>] [together]", "high", $"Play a playlist's item on {l.Label} (its next item when none is named); together: at an instant on its clock, where that clock is fit for it", true));
                rows.Add(($"{on} cue <playlist> <item>", "high", $"Load an item on {l.Label}: its first frame shows on air at once", true));
            }
            else if (l.Kind == PlayoutRules.Patterns)
            {
                rows.Add(($"{on} go <cue>", "high", $"GO a cue on {l.Label} (its own standby decides)", true));
                rows.Add(($"{on} cue <cue>", "high", $"Stand a cue by on {l.Label}: it moves that desk's own standby", true));
            }
        }
        return [.. Commands.Where(c => c.Air), .. rows, .. Commands.Where(c => !c.Air)];
    }

    /// <summary>
    /// Each playout link's items, pure: <c>link:&lt;name&gt;</c> (kind <c>playout</c>), labelled with its peer, in its record's light
    /// (13.16.4: no reader derives a light of its own — a level-0 link is amber, <c>legacy</c>) and its words; and while its clock is
    /// apart (13.11.5: the offset less its error past <c>clock.apart.ms</c>) one amber <c>clock:&lt;name&gt;</c> (kind <c>health</c>). The
    /// ids come from the link's name alone, which no other integration's slug may take (<see cref="PlayoutRules.Reserved"/>).
    /// </summary>
    public static IReadOnlyList<ContextItem> PlayoutItems(IEnumerable<PlayoutLinkFact> links)
    {
        var items = new List<ContextItem>();
        foreach (var l in links)
        {
            var light = l.Light is Light.Green or Light.Amber or Light.Red ? l.Light : Light.Grey;
            var flags = (l.Flags ?? []).Select(FlagWords).Where(w => w.Length > 0).ToList();
            items.Add(new ContextItem("link:" + l.Name, "playout", l.Peer, light, flags.Count == 0 ? l.Words : l.Words + " · " + string.Join(" · ", flags)));
            if (l is { ClockApart: true, ClockOffsetMs: { } offset })
                items.Add(new ContextItem("clock:" + l.Name, "health", "Clock vs " + l.Peer, Light.Amber,
                    FormattableString.Invariant($"CLOCKS APART: {l.Peer}'s clock is {Math.Abs(offset) / 1000:0.0} s {(offset > 0 ? "ahead" : "behind")} (± {l.ClockErrMs ?? 0} ms)")));
        }
        return items;
    }

    /// <summary>
    /// A link record's flag (13.16.2) in a reader's words: what an amber light stands for, so a link that is up and amber says why.
    /// A flag no row names is left out.
    /// </summary>
    public static string FlagWords(string flag) => flag switch
    {
        "plain" => "plain TCP — its wire offers no TLS",
        "keyless" => "public view only",
        "legacy" => "proved at level 0 — the peer does not prove itself to this link",
        "open" => "it asks for no key",
        "code-plain" => "proved with a pairing code over plain TCP",
        "scope" => "its key's scope is below what the link is used for",
        "first" => "pinned at first use — compare the fingerprint",
        "note" => "a hello contradicts this link",
        "apart" => "CLOCKS APART",
        "unfit" => "its clock is not fit for timed commands",
        "oversize" => "an over-long line read in part",
        "malformed" => "lines it could not read",
        _ => "",
    };

    /// <summary>
    /// The context's light (the worst of every item, those trimming may leave out among them), its headline within
    /// <see cref="HeadlineChars"/> and its items, each within §14's item sizes.
    /// </summary>
    public static (string Light, string Headline, IReadOnlyList<ContextItem> Items) Assess(ContextInput x)
    {
        var f = x.Frame;
        var items = new List<ContextItem>();

        // The timer.
        var counting = f.State is TimerState.Running or TimerState.Expired;
        var timerLight = x.ShowThreadStuck ? Light.Red
            : f.Mode != TimerMode.Countdown || f.State == TimerState.Idle ? Light.Grey
            : f.Phase == TimerPhase.Overtime ? Light.Red
            : f.Phase is TimerPhase.WrapUp or TimerPhase.Warning ? Light.Amber
            : Light.Green;
        var timerState = x.ShowThreadStuck ? "the show thread has stopped turning over"
            : f.Mode == TimerMode.TimeOfDay ? "showing the time of day"
            : f.State == TimerState.Idle ? "idle"
            : $"{(f.State == TimerState.Paused ? "paused" : counting ? "running" : f.State.ToString().ToLowerInvariant())}, " +
              (f.Seconds < 0 ? $"{TimerTick.FormatSeconds(-f.Seconds)} over" : $"{f.Text} left");
        items.Add(new ContextItem("timer", "timer", f.Title.Length > 0 ? f.Title : "Timer", timerLight, timerState));

        // The schedule.
        if (f.ItemCount > 0)
        {
            var schedLight = f.HardOutMarginSeconds is < 0 ? Light.Red
                : f.OffsetSeconds is > 120 ? Light.Amber
                : f.OffsetSeconds is null ? Light.Grey : Light.Green;
            var parts = new List<string>();
            if (f.ScheduleText.Length > 0) parts.Add(f.ScheduleText);
            if (f.HardOutText.Length > 0) parts.Add(f.HardOutText);
            if (f.ForecastText.Length > 0) parts.Add(f.ForecastText);
            items.Add(new ContextItem("schedule", "schedule", $"Running order {(f.ItemIndex >= 0 ? $"item {f.ItemIndex + 1} of {f.ItemCount}" : $"{f.ItemCount} items")}",
                schedLight, parts.Count > 0 ? string.Join(" · ", parts) : "not started"));
        }

        // Departments and the speaker's cues: each target's newest cue.
        foreach (var g in f.Signals.GroupBy(s => s.Target).Select(grp => grp.Last()))
        {
            var cueLight = g.Escalated ? Light.Red
                : g.Kind == SignalKind.Standby ? Light.Amber
                : g.State == SignalState.Sent ? Light.Amber : Light.Green;
            var what = g.Kind switch
            {
                SignalKind.Standby => g.State switch { SignalState.Sent => "standby sent", SignalState.Seen => "standing by", _ => "in position" },
                SignalKind.Go => g.State == SignalState.Sent ? "GO sent" : "GO done",
                _ => g.State == SignalState.Sent ? $"{g.Kind.ToString().ToLowerInvariant()} sent" : $"{g.Kind.ToString().ToLowerInvariant()} {g.State.ToString().ToLowerInvariant()}",
            };
            items.Add(new ContextItem("cue:" + g.Target.ToLowerInvariant(), g.Kind is SignalKind.Standby or SignalKind.Go ? "department" : "cue", g.Target, cueLight,
                what + (g.Text.Length > 0 ? ": " + g.Text : "") + (g.Escalated ? " — NO ACKNOWLEDGEMENT" : "") + (g.By.Length > 0 ? $" ({g.By})" : "")));
        }

        // The message on the speaker's screen.
        if (f.Message.Length > 0)
            items.Add(new ContextItem("message", "message", "Speaker message", f.MessageAck == "sent" ? Light.Amber : Light.Green,
                f.MessageAck switch { "sent" => "waiting for the speaker to see it", "seen" => "seen", "done" => "acknowledged", _ => "on screen" }));

        // Links (integrations, presenter PCs, other apps) as the app reports them.
        items.AddRange(x.Links);
        if (x.GoLatencyP95Ms > GoLatency.AmberMs)
            items.Add(new ContextItem("go-latency", "health", "GO to screen", Light.Amber, $"95% of GOs take {x.GoLatencyP95Ms:0} ms or less — slower than it should be"));

        var light = Light.Worst(items.Select(i => i.Light));
        var standing = f.Signals.Where(s => s.Kind == SignalKind.Standby).Select(s => s.Target).ToList();
        var headline = new List<string>
        {
            f.Title.Length > 0 ? f.Title : "No item",
            timerState,
        };
        if (f.ItemCount > 0 && f.OffsetSeconds is { } off && Math.Abs(off) >= 30)
            headline.Add(off > 0 ? $"{TimerTick.FormatSeconds(off)} behind" : $"{TimerTick.FormatSeconds(-off)} ahead");
        if (standing.Count > 0) headline.Add(string.Join(", ", standing) + " standing by");
        var worst = items.Where(i => i.Light == Light.Red).Concat(items.Where(i => i.Light == Light.Amber && i.Id is not "timer" and not "schedule" && !i.Id.StartsWith("cue:", StringComparison.Ordinal))).FirstOrDefault();
        if (worst is not null && worst.Id != "timer") headline.Add($"{worst.Label}: {worst.State}");
        return (light, ShowBusText.Cut(string.Join(" · ", headline), HeadlineChars), items.Select(Sized).ToList());
    }

    /// <summary>
    /// The §6 JSON, within §14's sizes. A context over <see cref="MaxBytes"/> as sent, or over a count, is trimmed in §6's order — the
    /// oldest recent lines; grey links, then the other links; commands that are not panic, air or transport, from the end; grey items,
    /// then green items — and only while it is still over, the last resort: each amber item's state cut short, then each red item's,
    /// then amber items, red items and panic, air and transport commands left out, each from the end. One grey <see cref="TrimmedId"/>
    /// item then says what it left out in §6's words, within the same bytes and items. A link left without the item it names goes with
    /// it. The headline, the node and its peers are never trimmed: within their sizes they leave room for the rest. The node's name and
    /// each peer's are said by the hello's rule (<see cref="ShowBusText.Name"/>), so a peer hears one name for a node in both.
    /// </summary>
    public static string Build(ContextInput x)
    {
        var (light, headline, items) = Assess(x);
        var node = new { t = "main", id = x.NodeId, name = ShowBusText.Name(x.Name) };
        var at = new DateTimeOffset(x.NowUtc).ToUnixTimeMilliseconds();
        // One hop (ShowNetwork's join): link is "ws" while a node link proves the id; verified is what proves the row's light and
        // headline now ("" when nothing spoke for it). Both last in the row: readers match the earlier fields as written.
        var peers = x.Peers.Take(MaxPeers).Select(p => new
        {
            t = p.Kind, id = p.Id, name = ShowBusText.Name(p.Name), light = p.Light, headline = ShowBusText.Cut(p.Headline, HeadlineChars), heard = p.Liveness,
            link = p.Link, verified = p.Verified,
        }).ToList();
        string Json(Shape s) => JsonSerializer.Serialize(new
        {
            v = 1,
            node,
            at,
            light,
            headline,
            items = s.Items.Select(p => p.Json).ToList(),
            links = s.Links.Select(p => p.Json).ToList(),
            recent = s.Recent.Select(p => p.Json).ToList(),
            commands = s.Commands.Select(p => p.Json).ToList(),
            peers,
        });

        var whole = new Shape(
            items.Select(Part.Of).ToList(),
            x.Links.Select(Sized).Select(l => Part.Of(new ContextLink("timer", l.Id, l.Kind == "playout" ? "follows" : "hears", l.Light))).ToList(),
            x.Journal.TakeLast(MaxRecent).Select(e => Part.Of(new DateTimeOffset(DateTime.SpecifyKind(e.Utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                ShowBusText.Cut(string.Create(CultureInfo.InvariantCulture, $"{x.Clock.ToShow(e.Utc):HH:mm:ss} {e.Kind}: {e.Text}"), RecentChars))).ToList(),
            CommandsFor(x.Playout).Select(Part.Of).ToList());
        // The usual context fits: one serialization, as it is sent. Only one over a count or the bytes is measured part by part.
        var json = Json(whole);
        if (whole.Items.Count <= MaxItems && whole.Links.Count <= MaxLinks && whole.Commands.Count <= MaxCommands
            && Encoding.UTF8.GetByteCount(json) <= MaxBytes)
            return json;
        return Json(Fit(whole, Encoding.UTF8.GetByteCount(Json(Shape.Empty))));
    }

    /// <summary>An item within §14's item sizes — its id and kind clipped, a word each; its label and state cut — its light one of §6's four.</summary>
    private static ContextItem Sized(ContextItem i)
        => new(ShowBusText.Clip(i.Id, IdChars), ShowBusText.Clip(i.Kind, KindChars), ShowBusText.Cut(i.Label, LabelChars), Light.Clean(i.Light),
            ShowBusText.Cut(i.State, StateChars));

    /// <summary>
    /// <paramref name="whole"/>, over a count or <see cref="MaxBytes"/>, trimmed to make room for the <see cref="TrimmedId"/> item at its
    /// longest — the words with every count at its most — which it then adds. <paramref name="fixedBytes"/> is the context with its
    /// four trimmed arrays empty.
    /// </summary>
    private static Shape Fit(Shape whole, long fixedBytes)
    {
        var most = new Trimmed(whole.Recent.Count, whole.Links.Count, whole.Commands.Count, whole.Items.Count);
        var room = Part.Of(TrimmedItem(most)).Bytes + 1;
        var trimmer = new Trimmer(whole, MaxBytes - fixedBytes - room, MaxItems - 1);
        var done = trimmer.Run();
        trimmer.Items.Add(Part.Of(TrimmedItem(done)));
        return new Shape(trimmer.Items, trimmer.Links, trimmer.Recent, trimmer.Commands);
    }

    private static ContextItem TrimmedItem(Trimmed t) => Sized(new ContextItem(TrimmedId, "health", "Context", Light.Grey, t.Words));

    /// <summary>
    /// What §6's trimming left out of a context, by array. A trimmed context always left something out, so its words always name a count:
    /// one over a count leaves out what is past it, and one over the bytes leaves something out before its last resort cuts any state —
    /// at the latest a command that is not panic, air or transport, which the list always holds.
    /// </summary>
    private readonly record struct Trimmed(int Recent, int Links, int Commands, int Items)
    {
        /// <summary>§6's words: "left out: 12 recent, 40 links, 30 commands, 5 items", each part only when it left one out.</summary>
        public string Words
        {
            get
            {
                static string Count(int n, string one, string many) => n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many);
                var parts = new List<string>();
                if (Recent > 0) parts.Add(Count(Recent, "recent", "recent"));
                if (Links > 0) parts.Add(Count(Links, "link", "links"));
                if (Commands > 0) parts.Add(Count(Commands, "command", "commands"));
                if (Items > 0) parts.Add(Count(Items, "item", "items"));
                return "left out: " + string.Join(", ", parts);
            }
        }
    }

    /// <summary>
    /// One element of a context's array as it is sent, with its light and whether it is a panic, air or transport command; its UTF-8
    /// bytes are measured the first time trimming asks.
    /// </summary>
    private sealed record Part(object Json, string Light, bool Air, ContextItem? Item, ContextLink? Link)
    {
        private int _bytes = -1;

        /// <summary>Its bytes as sent, inside the context as on its own (the same writer and encoder).</summary>
        public int Bytes => _bytes >= 0 ? _bytes : _bytes = JsonSerializer.SerializeToUtf8Bytes(Json).Length;

        public static Part Of(ContextItem i)
        {
            var json = new { id = i.Id, kind = i.Kind, label = i.Label, light = i.Light, state = i.State };
            return new Part(json, i.Light, false, i, null);
        }

        public static Part Of(ContextLink l)
        {
            var json = new { from = l.From, to = l.To, kind = l.Kind, light = l.Light };
            return new Part(json, Core.Light.Clean(l.Light), false, null, l);
        }

        public static Part Of(long at, string text)
        {
            var json = new { at, text };
            return new Part(json, "", false, null, null);
        }

        public static Part Of((string Verb, string Risk, string Words, bool Air) c)
        {
            var json = new { verb = c.Verb, risk = c.Risk, words = c.Words };
            return new Part(json, "", c.Air, null, null);
        }
    }

    /// <summary>The four arrays trimming may shorten, in their order.</summary>
    private sealed record Shape(List<Part> Items, List<Part> Links, List<Part> Recent, List<Part> Commands)
    {
        public static readonly Shape Empty = new([], [], [], []);

        /// <summary>What the four arrays add to the context with them empty: each element's bytes and the comma between two.</summary>
        public long Bytes => Of(Items) + Of(Links) + Of(Recent) + Of(Commands);

        public static long Of(List<Part> parts) => parts.Count == 0 ? 0 : parts.Sum(p => (long)p.Bytes) + parts.Count - 1;
    }

    /// <summary>§6's trimming, step by step, each step only until the context fits: copies of the arrays, and what it left out.</summary>
    private sealed class Trimmer(Shape whole, long budget, int maxItems)
    {
        public readonly List<Part> Items = [.. whole.Items], Links = [.. whole.Links], Recent = [.. whole.Recent], Commands = [.. whole.Commands];
        private long _bytes = whole.Bytes;
        private int _recent, _links, _commands, _items;

        private bool Over => _bytes > budget;

        public Trimmed Run()
        {
            // The counts first, in the same order: a list over its count leaves out what the bytes would leave out first.
            _commands += Drop(Commands, c => !c.Air, () => Commands.Count > MaxCommands) + Drop(Commands, _ => true, () => Commands.Count > MaxCommands);
            _links += Drop(Links, l => l.Light == Light.Grey, () => Links.Count > MaxLinks) + Drop(Links, _ => true, () => Links.Count > MaxLinks);
            foreach (var light in new[] { Light.Grey, Light.Green, Light.Amber, Light.Red })
                _items += Drop(Items, i => i.Light == light, () => Items.Count > maxItems);
            // A link whose own end names an item left out goes with it (an end on another node starts with @).
            var kept = Items.Select(i => i.Item!.Id).ToHashSet(StringComparer.Ordinal);
            _links += Drop(Links, l => !Names(l.Link!.From) || !Names(l.Link!.To), () => true);
            bool Names(string end) => end.StartsWith('@') || kept.Contains(end);

            // Then the bytes, in §6's order.
            while (Over && Recent.Count > 0) { RemoveAt(Recent, 0); _recent++; }
            _links += Drop(Links, l => l.Light == Light.Grey, () => Over) + Drop(Links, _ => true, () => Over);
            _commands += Drop(Commands, c => !c.Air, () => Over);
            _items += Drop(Items, i => i.Light == Light.Grey, () => Over) + Drop(Items, i => i.Light == Light.Green, () => Over);
            // The last resort, only while it is still over: the states of ambers then reds cut short, then ambers, reds and the
            // panic, air and transport commands left out.
            foreach (var light in new[] { Light.Amber, Light.Red })
                for (var i = 0; i < Items.Count && Over; i++)
                    if (Items[i].Light == light && Items[i].Item is { } item && item.State.Length > LastResortChars)
                    {
                        var cut = Part.Of(item with { State = ShowBusText.Cut(item.State, LastResortChars) });
                        _bytes += cut.Bytes - Items[i].Bytes;
                        Items[i] = cut;
                    }
            _items += Drop(Items, i => i.Light == Light.Amber, () => Over) + Drop(Items, i => i.Light == Light.Red, () => Over);
            _commands += Drop(Commands, _ => true, () => Over);
            return new Trimmed(_recent, _links, _commands, _items);
        }

        /// <summary>Leaves out the elements <paramref name="which"/> takes, the last first, while <paramref name="more"/> holds; how many it left out.</summary>
        private int Drop(List<Part> parts, Func<Part, bool> which, Func<bool> more)
        {
            var n = 0;
            for (var i = parts.Count - 1; i >= 0 && more(); i--)
            {
                if (!which(parts[i])) continue;
                RemoveAt(parts, i);
                n++;
            }
            return n;
        }

        private void RemoveAt(List<Part> parts, int i)
        {
            _bytes -= parts[i].Bytes + (parts.Count > 1 ? 1 : 0);
            parts.RemoveAt(i);
        }
    }
}
