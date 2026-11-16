using System.Globalization;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// What Stagewatch knows about the room when it builds its context (gathered on the owner thread): <see cref="Links"/> are its link
/// items — each integration's, each playout link's (<see cref="ShowContext.PlayoutItems"/>) and each link it accepted on <c>/ws</c>
/// (<see cref="ShowContext.Accepted"/>) — <see cref="Windows"/> its windows (the timer feeds each one while it shows a page that draws the
/// timer, and each serves the holders its grants prove), <see cref="Health"/> its own health items, which no row names (D11), and
/// <see cref="Rows"/> the one row each PeerLink item has to the node at its other end (Show Bus §6 P2), the connector's and the acceptor's.
/// </summary>
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
    IReadOnlyList<PlayoutRow>? Playout = null,
    IReadOnlyList<WindowFact>? Windows = null,
    IReadOnlyList<ContextItem>? Health = null,
    IReadOnlyList<ContextLink>? Rows = null);

/// <summary>
/// A playout link a peer or an AI may propose lines for: its name, as people read it, the peer's kind once known
/// (<see cref="PlayoutRules.Patterns"/> or <see cref="PlayoutRules.PlayMap"/>, else ""), and whether this Stagewatch can cue and
/// start something on it as well as move its clip on air.
/// </summary>
public sealed record PlayoutRow(string Name, string Label, string Kind, bool Starts);

/// <summary>
/// A playout link as its context items take it (Show Bus 13.16.4): its name (the link item's id is built from it alone), its peer as
/// people read it, the light of its record (13.16.3) and the words that go with it, and its clock as its matched pongs believe it.
/// <see cref="PeerId"/> is the node at its other end — the id its LINK proved (<see cref="Proved"/>), else the id the peer's own answer
/// named on the link (its STATE or its context), null while neither did: never one the settings name or a guess. <see cref="Use"/> is the
/// link's row kind, its primary use (<c>PlayoutProof.Use</c>, the one rule): <c>controls</c>, or <c>follows</c> where its scope only reads.
/// </summary>
public sealed record PlayoutLinkFact(string Name, string Peer, string Light, string Words, IReadOnlyList<string>? Flags = null, double? ClockOffsetMs = null,
    long? ClockErrMs = null, bool ClockApart = false, string Standing = "", string? PeerId = null, bool Proved = false, string Use = "controls");

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

    /// <summary>§14 <c>context.items.first</c>: the items an Eye draws first, among which P7 puts every item with a row to another node, as far as reds and ambers leave room.</summary>
    public const int ItemsFirst = 12;

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
    /// The verbs a peer or an AI may propose, with their risk (high is never pre-ticked by any assistant): thirteen rows, each in Show Bus
    /// §6's notation, so a reader rates a line by its whole form: every form of every row — a word or a quoted phrase in each value, each
    /// optional part taken or left out, each alternative — is a line this desk's own parser takes, at a risk no lower than the desk's own
    /// rating of it (<see cref="CommandRisk"/>). A value the parser reads as one word is one in the notation too, but a form's last, and an
    /// option the parser reads only after another nests in it. Every window verb and Patterns' transport is a row: a window grant among
    /// them, which a show app may propose only for itself. <c>Air</c> marks §6's panic, air and transport commands, listed first and left
    /// out last: what blacks out every screen, what changes or ends what a window's screen shows, and what starts, pauses or moves the show
    /// or Patterns' clip — a department's GO among them, the call that moves a called show. The rest follow by what an operator needs most
    /// on a failing night, a standby before messages, time, a window's settings and notes, because trimming leaves commands out from the
    /// end of the list.
    /// </summary>
    public static readonly IReadOnlyList<(string Verb, string Risk, string Words, bool Air)> Commands = new[]
    {
        ("blackout", "high", "Black out every screen", true),
        ("window hold|release <window or all>", "high", "Put a window on its holding page, or take it off to show what is under it", true),
        ("window show|cue <window or all> <page or setup or holding or black>", "high",
            "Put a page, a saved setup, the holding page or black on a controlled browser window: show puts it up at once, cue loads it behind the holding page", true),
        ("window identify|reload|revoke|delete <window>", "high",
            "Flash a window's name on its screen, reload it, give it a new address (the screen on the old one goes black) or delete it", true),
        ("window grant <window> <app> | window withdraw <window or all> [<app>]", "high",
            "Grant a window to a show app by its id (an app asks only for itself), or withdraw it (that app's screen of it goes black)", true),
        ("next | start | pause", "high", "Go to the next item, or start or pause the timer", true),
        ("patterns end [<seconds>] | patterns restart|play|pause|stop | patterns seek <m:ss>", "high",
            "Patterns' clip on air: jump to its last seconds (10 unless said), restart, play, pause, stop (back to the top, held) or move to a point", true),
        ("signal go <dept> <cue> | signal go all", "high", "GO one department, or every department standing by", true),
        ("signal standby <dept> <cue>", "review", "Stand a department by", false),
        ("message <phrase> [info|warn|crit [off|slow|fast [ack]]]", "review", "Put a message on the speaker's screen: one word, or words in double quotes (info and steady unless said; ack asks the speaker for Got it)", false),
        ("nudge <seconds> | trim <item> <seconds> | delay <seconds>", "review",
            "Add or take time from the live item, lengthen or shorten a coming item, or announce a delay before the next item", false),
        ("window new <window> [<page>] | window rename <window> <name> | window loss <window or all> hold|warn | window fade <window or all> <ms>", "low",
            "Make a window, rename one, choose what its screen shows when its link drops, or set its crossfade: nothing on a screen changes", false),
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
    /// Each playout link's items, pure (Show Bus §6 P2, 13.16.4): <c>link:&lt;name&gt;</c> of kind <c>link</c>, labelled with its peer, in its
    /// record's light (no reader derives a light of its own — a level-0 link is amber, <c>legacy</c>) and its words: the link's own, the
    /// node at its other end (<see cref="PeerWords"/>: proved, named and not proved, or none yet), its flags' and what it stands on, its
    /// scope among them. Then, for each link whose clock its matched pongs measured and whose node is known, one <c>clock:&lt;node id&gt;</c>
    /// (kind <c>health</c>): green with its offset and error, amber while apart (13.11.5: the offset less its error past
    /// <c>clock.apart.ms</c>). No item for a clock nobody measured, none for a link that names no node, and none under an id another link's
    /// clock took first: one node's clock is never said under another's id. The link item's id comes from the link's name alone, which no
    /// other integration's slug may take (<see cref="PlayoutRules.Reserved"/>).
    /// </summary>
    public static IReadOnlyList<ContextItem> PlayoutItems(IEnumerable<PlayoutLinkFact> links)
    {
        var items = new List<ContextItem>();
        var clocks = new List<ContextItem>();
        var clocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in links)
        {
            var light = l.Light is Light.Green or Light.Amber or Light.Red ? l.Light : Light.Grey;
            var flags = (l.Flags ?? []).Select(FlagWords).Where(w => w.Length > 0).ToList();
            // The link's own words, the node at its other end, then what it stands on (13.16.3: its level, proof, key by kid and scope).
            var parts = new List<string> { l.Words, PeerWords(l) };
            parts.AddRange(flags);
            if (l.Standing.Length > 0) parts.Add(l.Standing);
            items.Add(new ContextItem("link:" + l.Name, "link", l.Peer, light, string.Join(" · ", parts.Where(p => p.Length > 0))));
            if (l.ClockOffsetMs is not { } offset || NodeOf(l) is not { } id || !clocked.Add(id)) continue;
            var err = l.ClockErrMs ?? 0;
            clocks.Add(l.ClockApart
                ? new ContextItem("clock:" + id, "health", "Clock vs " + l.Peer, Light.Amber,
                    FormattableString.Invariant($"CLOCKS APART: {l.Peer}'s clock is {Math.Abs(offset) / 1000:0.0} s {(offset > 0 ? "ahead" : "behind")} (± {err} ms)"))
                : new ContextItem("clock:" + id, "health", "Clock vs " + l.Peer, Light.Green,
                    FormattableString.Invariant($"{l.Peer}'s clock is {offset:+0;-0;0} ms from this PC's (± {err} ms)")));
        }
        return [.. items, .. clocks];
    }

    /// <summary>
    /// The one row each playout link has to the node at its other end (Show Bus §6 P2: the connector's, from its link item to
    /// <c>@&lt;node id&gt;</c>, of the link's primary use, in its record's light), pure. A link that names no node has none: its words say so.
    /// </summary>
    public static IReadOnlyList<ContextLink> PlayoutRows(IEnumerable<PlayoutLinkFact> links)
        => links.Where(l => NodeOf(l) is not null)
            .Select(l => new ContextLink("link:" + l.Name, "@" + NodeOf(l), l.Use is "controls" or "follows" or "sends" ? l.Use : "follows",
                l.Light is Light.Green or Light.Amber or Light.Red ? l.Light : Light.Grey))
            .ToList();

    /// <summary>The node a playout link's row names: its <see cref="PlayoutLinkFact.PeerId"/> while that is a §2 id an end may carry, else null.</summary>
    private static string? NodeOf(PlayoutLinkFact l) => l.PeerId is { } id && PeerContexts.ValidEnd("@" + id) ? id : null;

    /// <summary>
    /// The node at a playout link's other end in words (§6 P2): "node 9f8e7d6c" once its LINK proved it, "node 9f8e7d6c, not proved" for an
    /// id only the peer's answer named (a level-0 link), and "no node id proved or named yet" while neither did — then the link has no row.
    /// </summary>
    public static string PeerWords(PlayoutLinkFact l)
        => NodeOf(l) is not { } id ? "no node id proved or named yet" : l.Proved ? "node " + id : "node " + id + ", not proved";

    /// <summary>The id of the one item that counts and names the links this Stagewatch accepted on <c>/ws</c> that nothing proves (§6 P2).</summary>
    public const string UnprovedId = "link:unproved";

    /// <summary>The words every accepted link's item starts with while a bound node key proves it and it runs no LINK (13.16.5's <c>legacy</c>).</summary>
    public const string AcceptedLevel0 = "level 0: a bound node key proves it, no LINK";

    /// <summary>Whether an item is one of the links this Stagewatch accepted on <c>/ws</c> (<see cref="Accepted"/>): <c>link:ws:&lt;id&gt;</c> or <see cref="UnprovedId"/>.</summary>
    public static bool IsAccepted(ContextItem i) => i.Id.StartsWith("link:ws:", StringComparison.Ordinal) || i.Id == UnprovedId;

    /// <summary>
    /// A context's link items as the network preflight checks them (<see cref="NetworkPreflight"/>): each integration's and each playout
    /// link's, and a clock while it is apart. A link this Stagewatch accepted is not among them — the preflight's node rows already judge
    /// each node it hears — and nor is a clock that is not apart, which has nothing to look at.
    /// </summary>
    public static IReadOnlyList<ContextItem> PreflightItems(IEnumerable<ContextItem> links)
        => links.Where(i => !IsAccepted(i) && !(i.Id.StartsWith("clock:", StringComparison.Ordinal) && i.Light == Light.Green)).ToList();

    /// <summary>
    /// The links this Stagewatch accepted on <c>/ws</c> as its context's items and rows (Show Bus §6 P2 as acceptor, 13.16.5), pure: one
    /// <c>link:ws:&lt;node id&gt;</c> item of kind <c>link</c> for each node whose link LINK verified, or whose bound node key proves it, with
    /// its one row <c>@&lt;node id&gt; -follows-&gt; link:ws:&lt;node id&gt;</c>. P2 has an acceptor name K from what the connector has done
    /// on the link; this desk keeps no record of that per link yet, so it names every accepted link by what a node link always does —
    /// it reads this desk's frames and context — and never <c>controls</c>, a right it would be guessing at. A link whose connector has
    /// also run a show verb (AGMessage putting a message on the stage under its node key) is still said <c>follows</c> until the acceptor
    /// records use.
    /// Its light is 13.16.5's: green when LINK verified it, amber over green when it is level 0 (a bound key proves it and it runs no LINK),
    /// plain from another machine, or its key was seen in clear (<paramref name="seenInClear"/>, by key id); its words its kind and name,
    /// how it was proved, by which key (by name, never the key), its scope, its carrier, its sockets and since when. Every other link it took
    /// — the operator's credential at level 0, one that proves no id, a key two installs share — is folded into one amber
    /// <see cref="UnprovedId"/> item that counts and names them, with no row: an id nothing proved draws no edge. Never an address.
    /// </summary>
    public static (IReadOnlyList<ContextItem> Items, IReadOnlyList<ContextLink> Rows) Accepted(NetworkView view, ShowClock clock,
        IReadOnlySet<string>? seenInClear = null)
    {
        var items = new List<ContextItem>();
        var rows = new List<ContextLink>();
        var unproved = new List<string>();
        foreach (var r in view.Rows)
        {
            foreach (var u in r.Unproven) unproved.Add(Who(u, r.Peer.Name));
            if (r.Link is not { Id: { } id } l) continue;
            var proved = l.Linked > 0 || l.Verified == NodeProof.Key;
            if (!proved || !PeerContexts.ValidEnd("@" + id))
            {
                unproved.Add(Who(l, r.Peer.Name));
                continue;
            }
            var seen = l.KeyId is { } kid && seenInClear?.Contains(kid) == true;
            var plain = l.Carrier == WindowGrants.LinkPlain;
            var light = l.Linked == 0 || plain || seen ? Light.Amber : Light.Green;
            // What proves it first (the headline takes a link's words up to their first " · "), its name in the label.
            var parts = new List<string> { l.Linked > 0 ? "PeerLink 1, LINK verified" : AcceptedLevel0 };
            parts.Add(l.Verified == NodeProof.Key ? (l.KeyName.Length > 0 ? $"key '{l.KeyName}'" : "a node key") + " · scope node" : "the operator's credential");
            parts.Add(l.Carrier switch { WindowGrants.LinkTls => "over TLS", WindowGrants.LinkLocal => "on this PC", WindowGrants.LinkPlain => "plain TCP from another machine", _ => "" });
            if (seen) parts.Add("its key was seen in clear — replace this key");
            if (l.Links > 1) parts.Add(l.Links.ToString(CultureInfo.InvariantCulture) + " links");
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"since {clock.ToShow(l.ConnectedUtc):HH:mm:ss}"));
            var item = "link:ws:" + id;
            items.Add(new ContextItem(item, "link", r.Peer.Name.Length > 0 ? ShowBusText.Name(r.Peer.Name) : ShowNetwork.AppName(l.Kind) + " " + id, light,
                string.Join(" · ", parts.Where(p => p.Length > 0))));
            rows.Add(new ContextLink("@" + id, item, "follows", light));
        }
        foreach (var a in view.Apart) unproved.Add(Who(a, ""));
        if (unproved.Count > 0)
            items.Add(new ContextItem(UnprovedId, "link", "Links not proved", Light.Amber,
                $"{unproved.Count.ToString(CultureInfo.InvariantCulture)} connection{(unproved.Count == 1 ? "" : "s")} not proved: " + string.Join(", ", unproved)));
        return (items, rows);

        // A link that proves nothing, named by what it said, never by an address or a key.
        static string Who(NodeLinkFact l, string name) => name.Length > 0 ? ShowBusText.Name(name)
            : l.Id is { } id ? NodeBindings.Describe(l.Kind, id) : l.KeyName.Length > 0 ? $"a link under key '{l.KeyName}'" : "a link that names no node";
    }

    /// <summary>
    /// Each API key seen arriving in clear from another machine (13.6.9, 13.16.5), pure: one amber <c>key:&lt;key id&gt;</c> (kind
    /// <c>health</c>) labelled with the key's name (never its secret; "a revoked key" once it is gone), saying from where and when, and
    /// "replace this key". The oldest first; none for a key never seen in clear.
    /// </summary>
    public static IReadOnlyList<ContextItem> ClearSeenItems(IEnumerable<KeySighting> seen, Func<string, string?> keyName)
        => seen.OrderBy(s => s.AtUtc).ThenBy(s => s.KeyId, StringComparer.Ordinal)
            .Select(s => new ContextItem("key:" + s.KeyId, "health", "API key " + (keyName(s.KeyId) is { } n ? "'" + n + "'" : "(a revoked key)"), Light.Amber,
                $"seen in clear from {s.Address} at {KeySightings.Iso(s.AtUtc)} — {KeyBindingWords.ReplaceKey}"))
            .ToList();

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
        var timerState = x.ShowThreadStuck ? StuckWords
            : f.Mode == TimerMode.TimeOfDay ? "showing the time of day"
            : f.State == TimerState.Idle ? "idle"
            : $"{(f.State == TimerState.Paused ? "paused" : counting ? "running" : f.State.ToString().ToLowerInvariant())}, " +
              (f.Seconds < 0 ? $"{TimerTick.FormatSeconds(-f.Seconds)} over" : $"{f.Text} left");
        items.Add(new ContextItem("timer", "timer", f.Title.Length > 0 ? f.Title : "Timer", timerLight, timerState));

        // The schedule. P5: a stuck show thread turns every counting item red, its words the show stuck in place of a time.
        if (f.ItemCount > 0)
        {
            var schedLight = x.ShowThreadStuck ? Light.Red
                : f.HardOutMarginSeconds is < 0 ? Light.Red
                : f.OffsetSeconds is > 120 ? Light.Amber
                : f.OffsetSeconds is null ? Light.Grey : Light.Green;
            var parts = new List<string>();
            if (f.ScheduleText.Length > 0) parts.Add(f.ScheduleText);
            if (f.HardOutText.Length > 0) parts.Add(f.HardOutText);
            if (f.ForecastText.Length > 0) parts.Add(f.ForecastText);
            items.Add(new ContextItem("schedule", "schedule", $"Running order {(f.ItemIndex >= 0 ? $"item {f.ItemIndex + 1} of {f.ItemCount}" : $"{f.ItemCount} items")}",
                schedLight, x.ShowThreadStuck ? StuckWords : parts.Count > 0 ? string.Join(" · ", parts) : "not started"));
        }

        // The VT countdown its screens show (P3: a clip's time left is the peer's; this item is the countdown Stagewatch draws of it, and
        // its feeds row names the node it comes from). A counting item: P5 holds for it too.
        if (VtShown(f) is { } vt)
            items.Add(new ContextItem(VtId, "timer", "VT countdown", x.ShowThreadStuck ? Light.Red : vt.Running ? Light.Green : Light.Grey,
                x.ShowThreadStuck ? StuckWords
                : $"{(vt.Title.Length > 0 ? vt.Title : "the clip")} on {vt.Source}: {TimerTick.FormatSeconds(Math.Max(0, vt.RemainingSeconds))} left{(vt.Running ? "" : ", not running")}"));

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
            // P8: a department's cue words are the show's own ("LX 12"); what a message or a nudge says is written for a person, never sent.
            var words = g.Kind is SignalKind.Message or SignalKind.Nudge || g.Text.Length == 0 ? "" : ": " + g.Text;
            items.Add(new ContextItem("cue:" + g.Target.ToLowerInvariant(), g.Kind is SignalKind.Standby or SignalKind.Go ? "department" : "cue", g.Target, cueLight,
                what + words + (g.Escalated ? " — NO ACKNOWLEDGEMENT" : "") + (g.By.Length > 0 ? $" ({g.By})" : "")));
        }

        // The message on the speaker's screen.
        if (f.Message.Length > 0)
            items.Add(new ContextItem("message", "message", "Speaker message", f.MessageAck == "sent" ? Light.Amber : Light.Green,
                f.MessageAck switch { "sent" => "waiting for the speaker to see it", "seen" => "seen", "done" => "acknowledged", _ => "on screen" }));

        // Links (integrations, presenter PCs, other apps) as the app reports them, then its windows, then its own health.
        items.AddRange(x.Links);
        items.AddRange((x.Windows ?? []).Select(w => w.Item));
        items.AddRange(x.Health ?? []);
        if (x.GoLatencyP95Ms > GoLatency.AmberMs)
            items.Add(new ContextItem("go-latency", "health", "GO to screen", Light.Amber, $"95% of GOs take {x.GoLatencyP95Ms:0} ms or less — slower than it should be"));

        // P7: reds, then ambers, then the items with a row to another node as far as ItemsFirst leaves room, then the rest, each in the
        // order above: what an Eye draws of this node before it is opened holds its problems and its chain ends, and its timer stays near
        // the top however many links it has.
        var crossing = Links(x).SelectMany(l => l.To.StartsWith('@') ? new[] { l.From } : l.From.StartsWith('@') ? new[] { l.To } : Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        var urgent = items.Where(i => i.Light is Light.Red).Concat(items.Where(i => i.Light is Light.Amber)).ToList();
        var ends = items.Where(i => i.Light is not (Light.Red or Light.Amber) && crossing.Contains(i.Id)).Take(Math.Max(0, ItemsFirst - urgent.Count)).ToList();
        items = [.. urgent, .. ends, .. items.Where(i => !urgent.Contains(i) && !ends.Contains(i))];
        var light = Light.Worst(items.Select(i => i.Light));
        var standing = f.Signals.Where(s => s.Kind == SignalKind.Standby).Select(s => s.Target).ToList();
        var headline = new List<string>
        {
            f.Title.Length > 0 ? f.Title : "No item",
            timerState,
        };
        // P5: a stuck show's headline carries no counting value — the offset is the frozen frame's, as stale as its time.
        if (!x.ShowThreadStuck && f.ItemCount > 0 && f.OffsetSeconds is { } off && Math.Abs(off) >= 30)
            headline.Add(off > 0 ? $"{TimerTick.FormatSeconds(off)} behind" : $"{TimerTick.FormatSeconds(-off)} ahead");
        if (standing.Count > 0) headline.Add(string.Join(", ", standing) + " standing by");
        // A link this desk accepted on /ws is amber over green while 13.16.5 says so (level 0, plain, a key seen in clear), and so is
        // the one that counts the unproved ones: their light stands on their items, which P7 puts first, and never in the headline. The
        // headline is the line peers quote from a context they pull on a beat (Patterns every 5 s), so it says the show's own state
        // and does not change each time another app's link joins or leaves this desk.
        var worst = items.Where(i => i.Light == Light.Red).Concat(items.Where(i => i.Light == Light.Amber && i.Id is not "timer" and not "schedule" && !i.Id.StartsWith("cue:", StringComparison.Ordinal)
            && !IsAccepted(i))).FirstOrDefault();
        // A link's words run from what it is to what it stands on; the headline takes the first part alone, so it stays one line.
        if (worst is not null && worst.Id != "timer")
            headline.Add($"{worst.Label}: {(worst.Kind == "link" && worst.State.IndexOf(" · ", StringComparison.Ordinal) is var cut and > 0 ? worst.State[..cut] : worst.State)}");
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
        // headline now ("" when nothing spoke for it); collects is whether the node asks this desk for its approved lines now, so a
        // line proposed for it would reach it. Each after the fields before it: readers match the earlier fields as written.
        var peers = x.Peers.Take(MaxPeers).Select(p => new
        {
            t = p.Kind, id = p.Id, name = ShowBusText.Name(p.Name), light = p.Light, headline = ShowBusText.Cut(p.Headline, HeadlineChars), heard = p.Liveness,
            link = p.Link, verified = p.Verified, collects = p.Collects,
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
            Links(x).Select(Part.Of).ToList(),
            // This show's own rows alone: what another node's journal told this desk (a peer row) is never said again as Stagewatch's.
            ShowJournal.LastOwn(x.Journal, MaxRecent).Select(e => Part.Of(new DateTimeOffset(DateTime.SpecifyKind(e.Utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
                ShowBusText.Cut(string.Create(CultureInfo.InvariantCulture, $"{x.Clock.ToShow(e.Utc):HH:mm:ss} {e.Kind}: {PublicText(e)}"), RecentChars))).ToList(),
            CommandsFor(x.Playout).Select(Part.Of).ToList());
        // The usual context fits: one serialization, as it is sent. Only one over a count or the bytes is measured part by part.
        var json = Json(whole);
        if (whole.Items.Count <= MaxItems && whole.Links.Count <= MaxLinks && whole.Commands.Count <= MaxCommands
            && Encoding.UTF8.GetByteCount(json) <= MaxBytes)
            return json;
        return Json(Fit(whole, Encoding.UTF8.GetByteCount(Json(Shape.Empty))));
    }

    /// <summary>
    /// The context's rows (Show Bus §6 P2–P4), pure. Each PeerLink item's one row to the node at its other end (<see cref="ContextInput.Rows"/>:
    /// the connector's and the acceptor's), and nothing else from a link: no row runs from the timer to a link. The timer feeds a window
    /// only while the window says it shows the page it was given and that page draws the timer (<see cref="WindowFact.Fed"/>,
    /// <see cref="ScreenWindows.DrawsTimer"/>); a window serves each holder a grant on its key-verified link proves (P4, its light the
    /// grant's: green shown, amber while it opens); and the VT countdown, while a screen draws it, is fed by the node of the playout link
    /// its clip comes over (P3), the row's end and light that link's own row's. No row names a health item (<c>clock:*</c>, <c>go-latency</c>, a key seen in clear, the hello). A row
    /// whose own end is no item of this context, or whose other end breaks §6's rules, is left out.
    /// </summary>
    public static IReadOnlyList<ContextLink> Links(ContextInput x)
    {
        var own = x.Links.Select(l => Sized(l).Id).ToHashSet(StringComparer.Ordinal);
        var links = new List<ContextLink>();
        foreach (var r in x.Rows ?? [])
        {
            var inward = r.From.StartsWith('@');
            var (mine, other) = inward ? (ShowBusText.Clip(r.To, IdChars), r.From) : (ShowBusText.Clip(r.From, IdChars), r.To);
            if (own.Contains(mine) && other.StartsWith('@') && PeerContexts.ValidEnd(other) && ShowBusKinds.Links.Contains(r.Kind))
                links.Add(inward ? new ContextLink(other, mine, r.Kind, Light.Clean(r.Light)) : new ContextLink(mine, other, r.Kind, Light.Clean(r.Light)));
        }
        foreach (var w in x.Windows ?? [])
        {
            var id = Sized(w.Item).Id;
            if (w.Fed) links.Add(new ContextLink("timer", id, "feeds", Light.Green));
            foreach (var h in w.Holders ?? [])
                if (PeerContexts.ValidEnd("@" + h.Node)) links.Add(new ContextLink(id, "@" + h.Node, "serves", Light.Clean(h.Light)));
        }
        if (VtShown(x.Frame) is { Link.Length: > 0 } vt
            && links.FirstOrDefault(l => l.From == "link:" + vt.Link && l.To.StartsWith('@')) is { } via)
            links.Add(new ContextLink(via.To, VtId, "feeds", via.Light));
        return links;
    }

    /// <summary>The VT countdown's item id: what Stagewatch's screens draw of the clip a playout link or vMix plays out.</summary>
    public const string VtId = "vt";

    /// <summary>
    /// The clip the VT countdown shows while Stagewatch's screens draw it — a clip feeds the VT and the show's display elements put the
    /// countdown on the speaker's screens (<see cref="DisplayElements.Playout"/>) — else null: no <see cref="VtId"/> item and no row.
    /// </summary>
    private static PlayoutView? VtShown(ShowSnapshot f) => f.Playout is { } vt && f.Elements.HasFlag(DisplayElements.Playout) ? vt : null;

    /// <summary>What a counting item says while the show thread is stuck (§6 P5), in place of a time.</summary>
    public const string StuckWords = "the show thread has stopped turning over — no time shown";

    /// <summary>
    /// An answer's own words after the signal's (" (Done by Patterns FOH (127.0.0.1))"): the parenthesis that closes the line, read back to
    /// the one that opens it, past <paramref name="after"/> and starting with a signal state's word; "" when the line ends in none.
    /// </summary>
    private static string AckSuffix(string text, int after)
    {
        if (!text.EndsWith(')')) return "";
        var depth = 0;
        for (var i = text.Length - 1; i > after; i--)
        {
            depth += text[i] == ')' ? 1 : text[i] == '(' ? -1 : 0;
            if (depth != 0) continue;
            var inside = text[(i + 1)..^1];
            return text[i - 1] == ' ' && Enum.GetNames<SignalState>().Any(st => inside == st || inside.StartsWith(st + " by ", StringComparison.Ordinal)) ? text[(i - 1)..] : "";
        }
        return "";
    }

    /// <summary>
    /// A journal line as the context's <c>recent</c> may carry it (§6 P8): its own words, but for what one person writes for another — a
    /// message's text and an audience question's, a message or nudge signal's words, and the same words inside a line the approval desk
    /// or the assistant wrote down (<see cref="WithoutMessageWords"/>) — which stay on this desk.
    /// </summary>
    public static string PublicText(JournalEntry e)
    {
        switch (e.Kind)
        {
            case JournalKind.Desk or JournalKind.Note: return WithoutMessageWords(e.Text);
            case JournalKind.Message: return "a message went to the speaker's screen (its words stay on this desk)";
            case JournalKind.Question: return "an audience question came in (its words stay on this desk)";
            case JournalKind.Signal or JournalKind.Ack or JournalKind.Escalated
                when (e.Text.StartsWith(nameof(SignalKind.Message) + " ", StringComparison.Ordinal) || e.Text.StartsWith(nameof(SignalKind.Nudge) + " ", StringComparison.Ordinal))
                     && e.Text.IndexOf(':', StringComparison.Ordinal) is var colon and > 0:
            {
                // An answer keeps who gave it and how ("(Done by Sam)"), the show's own words, after the words it leaves out.
                var by = e.Kind == JournalKind.Ack ? AckSuffix(e.Text, colon) : "";
                return e.Text[..colon] + " (its words stay on this desk)" + by;
            }
            default: return e.Text;
        }
    }

    /// <summary>The verbs of a line that puts a person's words on a screen (Stagewatch's <c>message</c>, AGMessage's <c>notice</c>), and the signals whose words are a person's (<c>signal message|nudge</c>).</summary>
    private static readonly string[] MessageVerbs = ["message", "msg", "notice"], SignalVerbs = ["signal", "cue"], PersonalSignals = ["message", "msg", "nudge", "haptic", "buzz"];

    /// <summary>
    /// A row that quotes a command line (the approval desk's "Proposed ab12: AGMessage → Stagewatch: message "Wrap up"", "Sent …: …",
    /// "Outcome …: Stagewatch: … — applied (…)", the assistant's "AI agent proposed: …") with a message's or a personal signal's words left
    /// out (§6 P8): the line from its verb on becomes the verb alone — and a signal's kind — and "(its words stay on this desk)", and an
    /// outcome keeps its state word but not its result, which may echo the words. A line found after any ": " counts, so words quoted
    /// in a proposer's name cannot hide one; a row with no such line is its own words.
    /// </summary>
    public static string WithoutMessageWords(string text)
    {
        for (var at = text.IndexOf(": ", StringComparison.Ordinal); at >= 0; at = text.IndexOf(": ", at + 2, StringComparison.Ordinal))
        {
            var words = text[(at + 2)..].TrimStart().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            var verb = words[0].ToLowerInvariant();
            var kept = MessageVerbs.Contains(verb) ? words[0]
                : SignalVerbs.Contains(verb) && words.Length > 1 && PersonalSignals.Contains(words[1].ToLowerInvariant()) ? words[0] + " " + words[1]
                : null;
            if (kept is null) continue;
            var state = "";
            if (text.StartsWith("Outcome ", StringComparison.Ordinal) && text.LastIndexOf(" — ", StringComparison.Ordinal) is var dash and > 0)
            {
                var word = text[(dash + 3)..].Split(' ', 2)[0];
                if (Enum.GetNames<SharedProposalState>().Any(n => n.Equals(word, StringComparison.OrdinalIgnoreCase))) state = " — " + word;
            }
            return text[..(at + 2)] + kept + " (its words stay on this desk)" + state;
        }
        return text;
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
