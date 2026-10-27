using System.Collections.Immutable;
using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// The playout links running now, by name: one immutable map swapped whole, so any thread reads a set that never changes under it.
/// One writer — the owner thread, as its supervisor starts and retires each link — and any number of readers.
/// </summary>
public sealed class PlayoutLinks
{
    private volatile ImmutableDictionary<string, PlayoutLink> _links = ImmutableDictionary<string, PlayoutLink>.Empty.WithComparers(StringComparer.Ordinal);
    private readonly Lock _write = new();

    /// <summary>The link running under <paramref name="name"/>, or null.</summary>
    public PlayoutLink? this[string name] => _links.TryGetValue(name, out var link) ? link : null;

    /// <summary>The links running now, in no order (readers order them by Settings).</summary>
    public IReadOnlyCollection<PlayoutLink> All => _links.Values.ToList();

    /// <summary>How many run now.</summary>
    public int Count => _links.Count;

    /// <summary>A link started under <paramref name="name"/> (or, with null, one that stopped running).</summary>
    public void Set(string name, PlayoutLink? link)
    {
        lock (_write) _links = link is null ? _links.Remove(name) : _links.SetItem(name, link);
    }

    /// <summary>
    /// The contexts <paramref name="links"/> pulled, as the show network takes them beside <paramref name="held"/> (Show Bus §6): each
    /// link's <see cref="PlayoutLink.PulledContext"/> — from a peer of the kind its settings name, for the id its STATE gives — at the
    /// link's address, proved as <paramref name="proof"/> says for its id (<see cref="PeerContexts.LinkedProof"/>: while no key speaks
    /// for that id, this Stagewatch's own link to the address the operator set proves it), and named by the link it came over. One per
    /// node id: an id <paramref name="held"/> or an earlier link in <paramref name="links"/>' order gives is left to that one.
    /// </summary>
    public static IReadOnlyList<HeldContext> Pulled(IEnumerable<PlayoutLink> links, IEnumerable<HeldContext> held, Func<string, NodeProof> proof, DateTime nowUtc)
    {
        var ids = held.Select(c => c.Entry.Id).ToHashSet(StringComparer.Ordinal);
        var pulled = new List<HeldContext>();
        foreach (var link in links)
            if (link.PulledContext is { } pc && PeerContexts.Parse(pc, link.Host, nowUtc) is { } pe && ids.Add(pe.Id))
                pulled.Add(new HeldContext(pe, proof(pe.Id), Pulled: true) { Via = link.LinkName });
        return pulled;
    }

    /// <summary>
    /// The context a link pulled for <paramref name="nodeId"/> (<see cref="PlayoutLink.PulledContext"/>), as a proposal's risk may take
    /// it (Show Bus §11: a context read over the desk's own link), from the first link in <paramref name="links"/>' order whose context
    /// names that id; null when none does.
    /// </summary>
    public static string? PulledFor(IEnumerable<PlayoutLink> links, string nodeId)
    {
        foreach (var link in links)
            if (link.PulledContext is { } pc && PeerContexts.Parse(pc, link.Host, DateTime.UtcNow) is { } pe && pe.Id == nodeId) return pc;
        return null;
    }

    /// <summary>
    /// The link a playout link's settings start, made and started: its token proved (never sent in clear off this PC), its request ids
    /// ending with this process's hello <paramref name="inst"/>, its time <paramref name="time"/>, and what it kept across a restart in
    /// <paramref name="kept"/> under the tag of its token as Settings stores it (13.15.3) — a hold before it writes or dials again, and
    /// the pin of the node Settings set it to (or, with none set, the one kept at its address) with the TLS door kept beside that pin —
    /// its transport as Settings set it, the hello the show network hears for its node (<paramref name="hello"/>, null for none: the
    /// TLS door it may dial, 13.4.2), and <paramref name="wire"/>'s listeners on it before it starts.
    /// </summary>
    public static PlayoutLink Make(PlayoutLinkSettings s, string token, string inst, TimeProvider time, PlayoutKeptStore? kept, string tag,
        Action<PlayoutLink>? wire = null, Func<PlayoutHeard?>? hello = null)
    {
        var name = s.Name;
        // The address it dials, as the pin it starts from is kept beside: with no node set, a pin is the one kept at this address.
        var address = s.Host.Trim() + ":" + s.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var link = new PlayoutLink(new PlayoutLinkOptions
        {
            Host = s.Host, Port = s.Port, Token = token, Inst = inst, Name = name, Kind = s.Kind, Time = time,
            Transport = s.Transport, NodeId = s.Node, Hello = hello,
            Kept = kept?.For(name, tag, time.GetUtcNow().ToUnixTimeMilliseconds(), s.Node, address),
            Keep = kept is null ? null : k => kept.Keep(name, tag, k, time.GetUtcNow().ToUnixTimeMilliseconds(), address),
            Door = kept?.DoorFor(name, s.Node, address) ?? 0,
            KeepDoor = kept is null ? null : d => kept.KeepDoor(name, d, time.GetUtcNow().ToUnixTimeMilliseconds()),
        });
        // Its listeners hear it from its first change of state on: wired before it starts.
        wire?.Invoke(link);
        link.Start();
        return link;
    }
}

/// <summary>Which of a link's verbs a plan calls (<see cref="PlayoutLink.Clip"/>, <see cref="PlayoutLink.Playlist"/>, <see cref="PlayoutLink.Cue"/>).</summary>
public enum PlayoutCall { Clip, Playlist, Cue }

/// <summary>
/// What the link's STATE is looked at for once a command was taken, for <see cref="Ms"/> at most (the operator's lead and 3 s): a
/// playlist playing or cued on an item, or the clip on air paused or playing. "Seen" is seen in STATE, never on the outputs.
/// </summary>
public sealed record PlayoutWatch(string Words, Func<PlayoutPeerState, bool> Seen, long Ms);

/// <summary>
/// A playout command as its link will carry it, pure: the verb it calls and that verb's words, what was asked in people's words, and
/// what its effect is looked for as; or why nothing is sent (<see cref="Error"/>).
/// </summary>
public sealed record PlayoutPlan(string? Error, PlayoutCall Call = PlayoutCall.Clip, string What = "", int Seconds = 0, string List = "",
    string? Item = null, string? Expect = null, string? OnKey = null, string Asked = "", PlayoutWatch? Watch = null)
{
    public static PlayoutPlan Refused(string why) => new(why);

    /// <summary>The command on <paramref name="link"/>, with the asker's <paramref name="mark"/>: its ticket, or why it was not sent.</summary>
    public PlayoutSent Run(PlayoutLink link, int mark) => Call switch
    {
        PlayoutCall.Playlist => link.Playlist(What, List, Item, Expect, mark),
        PlayoutCall.Cue => link.Cue(What, Item ?? "", mark),
        _ => link.Clip(What, Seconds, mark, OnKey),
    };
}

/// <summary>What a link's peer has said of itself, as a plan reads it: its kind, its STATE and that STATE's age, and its context.</summary>
public sealed record PlayoutFacts(string? Kind, PlayoutPeerState? State, long StateAgeMs, string? Context)
{
    public static PlayoutFacts Of(PlayoutLink link) => link.Reading is { } r
        ? new(link.Kind, r.State, r.AgeMs, link.Context)
        : new(link.Kind, null, long.MaxValue, link.Context);
}

/// <summary>
/// What a playout command became: sent on a link with its ticket, or refused with why (nothing sent). <see cref="Note"/> is what the
/// sender is told beside a command that went: "" unless its effect is not looked for in the peer's STATE, and why.
/// </summary>
public sealed record PlayoutStep(string? Error, string Link = "", string Peer = "", string Asked = "", PlayoutTicket? Ticket = null)
{
    public static PlayoutStep Refused(string why) => new(why);

    public string Note { get; init; } = "";
}

/// <summary>
/// The executor of <c>playout</c> and <c>patterns</c> commands, for the desk and the rig alike: which link a command reaches
/// (<see cref="PlayoutRules.Route"/>), what it becomes there or why it is refused from the peer's own facts (<see cref="Plan"/>),
/// the command handed to the link with its ticket, the effect looked for in the peer's STATE, and every row the links give the show
/// log — each command's outcome when it settles, each change of a link's state, the link's own lines — through the host's sinks,
/// which take them onto its show thread. It holds no link of its own: <see cref="PlayoutLinks"/> does.
/// </summary>
public sealed class PlayoutDirector
{
    /// <summary>How fresh a STATE must be for a PlayMap playlist to be called idle before a CUE, which shows its first frame on air.</summary>
    public const long IdleFreshMs = 3_000;

    /// <summary>How long past the lead the effect of a command is looked for in its peer's STATE.</summary>
    public const long WatchMs = 3_000;

    /// <summary>
    /// The most commands whose effect is looked for at once on one link: as many as may wait for their answers on it (§14
    /// <c>link.inflight.max</c>). A command past it still goes; its effect is not looked for, and its sender is told so.
    /// </summary>
    public const int WatchesMax = PeerLink.InflightMax;

    private readonly PlayoutLinks _links;
    private readonly Func<IReadOnlyList<PlayoutLinkSettings>> _settings;
    private readonly Func<bool> _licensed;
    private readonly Func<int> _leadMs;
    private readonly Action<DateTimeOffset, int, string> _journal;
    private readonly Action<string> _status;
    private readonly TimeProvider _time;
    // How many effects are looked for now, per link name: each look takes one and gives it back when it ends.
    private readonly Dictionary<string, int> _watching = new(StringComparer.Ordinal);

    /// <param name="links">The links running now.</param>
    /// <param name="settings">The links as Settings has them now, in their order.</param>
    /// <param name="licensed">Whether the licence runs playout links now.</param>
    /// <param name="leadMs">The operator's lead for a GO (its effect is looked for that long and <see cref="WatchMs"/>).</param>
    /// <param name="journal">A row for the show log: when, the running-order item it is filed against (-1 none), its words. Any thread.</param>
    /// <param name="status">A line for the desk's status line. Any thread.</param>
    /// <param name="time">The clock its looks at the peers' STATE run on.</param>
    public PlayoutDirector(PlayoutLinks links, Func<IReadOnlyList<PlayoutLinkSettings>> settings, Func<bool> licensed, Func<int> leadMs,
        Action<DateTimeOffset, int, string> journal, Action<string> status, TimeProvider? time = null)
    {
        _links = links;
        _settings = settings;
        _licensed = licensed;
        _leadMs = leadMs;
        _journal = journal;
        _status = status;
        _time = time ?? TimeProvider.System;
    }

    public PlayoutLinks Links => _links;

    /// <summary>
    /// The link's rows for the show log and its status line, in the order the link made them (its own threads): each command's outcome
    /// when it first settles and once more when an unconfirmed one is resolved (13.12.10), against the item it was asked for; each change
    /// of its state (13.16.7), repeats folded (13.19.4); its own lines (13.10.4). Its peer named as people read it, kind and all.
    /// </summary>
    public void Wire(PlayoutLink link)
    {
        link.Answered += a =>
        {
            var peer = link.Peer;
            _status(a.StatusLine(peer));
            if (a.JournalLine(peer) is { } row) _journal(a.At, a.Mark, row);
        };
        link.Changed += c => _journal(c.At, -1, c.Line(link.Peer));
        link.Logged += line => _journal(_time.GetUtcNow(), -1, line);
    }

    /// <summary>
    /// <c>playout [&lt;link&gt;] &lt;action&gt; …</c>: the link it reaches, what it becomes there, and the command handed to it with its ticket;
    /// or why not, nothing sent. <paramref name="mark"/> is the running-order item it is asked for (-1 none), <paramref name="vtLink"/> the
    /// link feeding the VT countdown now, the one a transport action with no link named reaches among two or more.
    /// </summary>
    public PlayoutStep Playout(PlayoutArgs a, double number, int mark, string? vtLink)
    {
        var (target, why) = PlayoutRules.Route(_settings(), a, _licensed(), name => _links[name] is not null, vtLink);
        if (target is null) return PlayoutStep.Refused(why!);
        var link = _links[target.Name]!;
        var plan = Plan(target, PlayoutFacts.Of(link), a.Action, a.List, a.Item, a.Together, (int)number, _leadMs());
        return Send(link, plan, mark);
    }

    /// <summary>
    /// <c>patterns &lt;end|restart|play|pause|stop|seek&gt; …</c>, as 2.17 had it: the clip on air on link "patterns", else on the only
    /// link switched on.
    /// </summary>
    public PlayoutStep Patterns(string what, int seconds, int mark, string? vtLink)
        => Playout(new PlayoutArgs(PlayoutRules.PatternsTarget(_settings()), what, "", "", false), seconds, mark, vtLink);

    private PlayoutStep Send(PlayoutLink link, PlayoutPlan plan, int mark)
    {
        var peer = link.Peer;
        if (plan.Error is { } refused) return PlayoutStep.Refused(refused);
        // The STATE as the command goes: what a look for its effect must see past.
        var before = link.Reading;
        var sent = plan.Run(link, mark);
        if (sent.Ticket is not { } ticket)
        {
            var why = sent.Error ?? "not sent";
            return PlayoutStep.Refused(why.StartsWith(PlayoutRules.Label(link.LinkName), StringComparison.Ordinal) || why.StartsWith(peer, StringComparison.Ordinal) ? why : peer + ": " + why);
        }
        var step = new PlayoutStep(null, link.LinkName, peer, plan.Asked, ticket);
        if (plan.Watch is not { } watch) return step;
        if (!TakeWatch(link.LinkName))
            return step with { Note = $"its effect is not looked for in {peer}'s STATE: {WatchesMax.ToString(CultureInfo.InvariantCulture)} are looked for on it already" };
        _ = WatchAsync(link, ticket, watch, mark, before);
        return step;
    }

    /// <summary>One more look at <paramref name="link"/>'s STATE, unless <see cref="WatchesMax"/> run on it now.</summary>
    private bool TakeWatch(string link)
    {
        lock (_watching)
        {
            var n = _watching.GetValueOrDefault(link);
            if (n >= WatchesMax) return false;
            _watching[link] = n + 1;
            return true;
        }
    }

    private void GiveWatch(string link)
    {
        lock (_watching)
        {
            if (_watching.GetValueOrDefault(link) <= 1) _watching.Remove(link);
            else _watching[link]--;
        }
    }

    /// <summary>How many effects are looked for on <paramref name="link"/> now.</summary>
    public int Watching(string link)
    {
        lock (_watching) return _watching.GetValueOrDefault(link);
    }

    /// <summary>
    /// Once the peer took the command (applied or accepted), its STATE is looked at until the effect is seen or <see cref="PlayoutWatch.Ms"/>
    /// has passed: one row either way, "seen: …" or "not seen in … STATE: …". Seen is seen in the peer's STATE, never on its outputs, and
    /// only in a STATE the link read after the command went (<paramref name="before"/> was the last read before it) — and when that
    /// last one already showed the effect, only in one read after the peer's answer: a STATE from before the command proves nothing of
    /// it, while a peer that pushes its new STATE ahead of its answer is still seen.
    /// </summary>
    private async Task WatchAsync(PlayoutLink link, PlayoutTicket ticket, PlayoutWatch watch, int mark, PlayoutReading? before)
    {
        try
        {
            var first = await ticket.First.ConfigureAwait(false);
            if (first.Outcome is not (PeerOutcome.Applied or PeerOutcome.Accepted)) return;
            var sentAfter = before?.Number ?? 0;
            var showedAlready = before is { } b && watch.Seen(b.State);
            var started = _time.GetTimestamp();
            while (true)
            {
                if (link.Reading is { } r && (r.Number > first.Read || (!showedAlready && r.Number > sentAfter)) && watch.Seen(r.State))
                {
                    _journal(_time.GetUtcNow(), mark, $"seen: {watch.Words} on {link.Peer}");
                    return;
                }
                if (_time.GetElapsedTime(started).TotalMilliseconds >= watch.Ms) break;
                await Task.Delay(TimeSpan.FromMilliseconds(100), _time).ConfigureAwait(false);
            }
            _journal(_time.GetUtcNow(), mark, $"not seen in {link.Peer}'s STATE within {(watch.Ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s: {watch.Words}");
        }
        finally
        {
            GiveWatch(link.LinkName);
        }
    }

    // ------------------------------------------------------------------ the plan, pure

    /// <summary>
    /// What <paramref name="action"/> becomes on <paramref name="link"/>, pure, from the peer's own facts; or why nothing is sent:
    /// <list type="bullet">
    /// <item>a peer that answers as another kind than its settings name is sent nothing;</item>
    /// <item>the clip's transport (end, restart, play, pause, stop, seek) goes to either kind; to Patterns, pause and stop are refused
    /// for a playlist item or a stinger's clip in Patterns' own words until its <c>video.pausable</c> / <c>video.stoppable</c> say
    /// otherwise, which then decide; PlayMap is never refused by role; a Patterns desk whose context lists <c>VIDEO … ON &lt;key&gt;</c>
    /// is sent the clip's own key with it;</item>
    /// <item>go and cue need the peer's kind heard (its STATE); to PlayMap a playlist's item — never on a sync group's follower, a
    /// CUE only to a playlist seen idle or ended in a STATE at most <see cref="IdleFreshMs"/> old, a GO with no item only with the next
    /// item its STATE names (<c>EXPECT</c>); to Patterns one cue by its id, GO or STANDBY, never both;</item>
    /// <item><c>together</c> is not carried: a GO at one instant needs a clock fit for it on every link (13.11.6).</item>
    /// </list>
    /// </summary>
    public static PlayoutPlan Plan(PlayoutLinkSettings link, PlayoutFacts facts, string action, string list, string item, bool together, int seconds, int leadMs)
    {
        var label = PlayoutRules.Label(link.Name);
        var peer = PlayoutRules.Peer(facts.Kind ?? (link.Kind == PlayoutRules.Auto ? null : link.Kind), link.Name);
        if (facts.Kind is { } said && link.Kind != PlayoutRules.Auto && said != link.Kind)
            return PlayoutPlan.Refused($"{label} answers as {ShowBusKinds.Label(said)}, but its settings say {ShowBusKinds.Label(link.Kind)} — nothing sent; set its kind (Settings → Integrations)");
        if (PlayoutRules.TransportActions.Contains(action)) return Transport(peer, facts, action, seconds, leadMs);
        if (facts.Kind is null) return PlayoutPlan.Refused($"{label}'s kind is not known yet — no STATE heard; nothing sent");
        if (together)
            return PlayoutPlan.Refused($"playout {action} … together isn't carried yet: a GO at one instant needs a clock fit for it on every link (Show Bus 13.11.6) — nothing sent; send it without together");
        return facts.Kind == PlayoutRules.PlayMap ? PlayMapStart(link.Name, peer, facts, action, list, item, leadMs) : PatternsStart(link.Name, action, list, item);
    }

    private static PlayoutPlan Transport(string peer, PlayoutFacts facts, string action, int seconds, int leadMs)
    {
        var what = action.ToUpperInvariant();
        var v = facts.State?.Video;
        if (facts.Kind != PlayoutRules.PlayMap && v is not null && action is "pause" or "stop")
        {
            var says = action == "pause" ? v.Pausable : v.Stoppable;
            if (says == false || (says is null && v.Role is "playlist" or "stinger"))
                return PlayoutPlan.Refused(OwnClockWords(peer, v, action));
        }
        // The clip by the peer's own key, when the peer lists that form and the key reads back as itself on its wire; else the clip on air.
        var onKey = facts.Kind == PlayoutRules.Patterns && v is { Key.Length: > 0 } && OffersOnKey(facts.Context, what)
                    && PlayoutWire.Word(PlayoutRules.Patterns, v.Key) is not null ? v.Key : null;
        var asked = action switch
        {
            "end" => $"jump the clip to its last {seconds.ToString(CultureInfo.InvariantCulture)} s",
            "restart" => "restart the clip",
            "play" => "play the clip",
            "pause" => "pause the clip",
            "stop" => "stop the clip at the top",
            _ => $"move the clip to {TimerTick.FormatSeconds(seconds)}",
        };
        var watch = action switch
        {
            "pause" => new PlayoutWatch("the clip on air paused", s => s.Video is { Paused: true }, leadMs + WatchMs),
            "play" => new PlayoutWatch("the clip on air playing", s => s.Video is { Playing: true, Paused: false }, leadMs + WatchMs),
            _ => null,
        };
        return new PlayoutPlan(null, PlayoutCall.Clip, what, seconds, OnKey: onKey, Asked: asked, Watch: watch);
    }

    /// <summary>
    /// Patterns' own words for a clip on a clock of its own (its ShowActions refusal), the Stagewatch verbs that do move it in the
    /// parenthesis; or the peer's word alone where it says no with no role given.
    /// </summary>
    private static string OwnClockWords(string peer, PlayoutVideo v, string action)
    {
        var what = v.Role switch { "playlist" => "a playlist item", "stinger" => "a stinger's clip", _ => null };
        var name = v.File.Length > 0 ? $"'{v.File}'" : "the clip on air";
        if (what is null) return $"{peer} says {name} can't be {(action == "pause" ? "paused" : "stopped")} now — nothing sent";
        return action == "pause"
            ? $"{peer}: {name} is {what} — its own clock keeps running, so it is not paused (end or seek move it)"
            : $"{peer}: {name} is {what} — its own clock keeps running, so it is not stopped (restart puts it back to the top)";
    }

    /// <summary>
    /// Whether a Patterns desk's context lists its clip verb <paramref name="what"/> with <c>ON &lt;key&gt;</c>: a command whose verb starts
    /// with <c>VIDEO</c>, names <c>ON &lt;key&gt;</c> and names <paramref name="what"/> among its words (<c>VIDEO PAUSE ON &lt;key&gt;</c>,
    /// <c>VIDEO PLAY|PAUSE|STOP ON &lt;key&gt;</c>). Nothing else is guessed: a desk that lists no such form is sent the clip on air.
    /// </summary>
    public static bool OffersOnKey(string? context, string what)
    {
        if (context is null || PeerContexts.Parse(context, "", DateTime.UtcNow) is not { } entry) return false;
        return entry.Commands.Any(c => c.Verb.StartsWith("VIDEO ", StringComparison.Ordinal) && c.Verb.Contains("ON <key>", StringComparison.Ordinal)
                                       && c.Verb.Split(' ', '|', '[', ']').Contains(what, StringComparer.Ordinal));
    }

    private static PlayoutPlan PlayMapStart(string name, string peer, PlayoutFacts facts, string action, string list, string item, int leadMs)
    {
        // One word is a playlist (its next item goes); two are a playlist and its item.
        var playlist = list.Length > 0 ? list : item;
        string? named = list.Length > 0 ? item : null;
        if (action == "cue" && named is null)
            return PlayoutPlan.Refused($"PlayMap cues a playlist's item: playout {name} cue <playlist> <item> — nothing sent");
        var state = facts.State;
        var pl = state is { HasPlaylists: true } ? FindPlaylist(state.Playlists, playlist) : null;
        var plName = pl is { Name.Length: > 0 } ? pl.Name : playlist;
        if (pl is { SyncGroup.Length: > 0 } && state is { HasSync: true, Sync.Role: "follower" })
            return PlayoutPlan.Refused($"{peer}'s {plName} follows sync group {pl.SyncGroup}" + (state.Sync.Leader.Length > 0 ? $" (leader {state.Sync.Leader})" : "")
                + " — GO its leader; nothing sent");
        if (action == "cue")
        {
            // A cued item shows its first frame on air at once: only a playlist this PC saw idle or ended a moment ago is cued.
            if (pl is null)
                return PlayoutPlan.Refused($"not cued — {playlist} isn't in {peer}'s STATE, so this PC can't see it is idle (a CUE shows its first frame on air)");
            if (facts.StateAgeMs > IdleFreshMs)
                return PlayoutPlan.Refused($"not cued — {peer}'s STATE is {(facts.StateAgeMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)} s old, so this PC can't see {plName} is idle (a CUE shows its first frame on air)");
            if (pl.State is not ("idle" or "empty" or "ended"))
                return PlayoutPlan.Refused($"not cued — {plName} is {(pl.State.Length > 0 ? pl.State : "in use")} on {peer}; GO will play it");
            var cueItem = named!;
            return new PlayoutPlan(null, PlayoutCall.Playlist, "CUE", List: playlist, Item: cueItem, Asked: $"cue {plName} · {cueItem}",
                Watch: new PlayoutWatch($"{plName} cued on {cueItem}", s => Plays(s, playlist, cueItem, "cued"), leadMs + WatchMs));
        }
        if (named is { } goItem)
            return new PlayoutPlan(null, PlayoutCall.Playlist, "GO", List: playlist, Item: goItem, Asked: $"play {plName} · {goItem}",
                Watch: new PlayoutWatch($"{plName} plays {goItem}", s => Plays(s, playlist, goItem, "playing"), leadMs + WatchMs));
        if (pl is not { NextId.Length: > 0 })
            return PlayoutPlan.Refused($"{plName}'s next item isn't in {peer}'s STATE — name it: playout {name} go {playlist} <item>; nothing sent");
        var next = pl.NextLabel.Length > 0 ? pl.NextLabel : pl.NextId;
        return new PlayoutPlan(null, PlayoutCall.Playlist, "GO", List: playlist, Expect: pl.NextId, Asked: $"play {plName}'s next item, {next}",
            Watch: new PlayoutWatch($"{plName} plays {next}", s => Plays(s, playlist, pl.NextId, "playing"), leadMs + WatchMs));
    }

    private static PlayoutPlan PatternsStart(string name, string action, string list, string item)
    {
        if (list.Length > 0) return PlayoutPlan.Refused($"Patterns takes one cue, no playlist: playout {name} {action} <cue> — nothing sent");
        if (PlayoutWire.Word(PlayoutRules.Patterns, item) is null)
            return PlayoutPlan.Refused($"a Patterns cue is named by its id, bare — \"{item}\" has a space or a quote mark in it, which Patterns would read as another word; nothing sent");
        // Its own standby fence decides a GO: a GO goes alone, never with a STANDBY, and a cue's STANDBY moves that desk's own standby.
        return action == "go"
            ? new PlayoutPlan(null, PlayoutCall.Cue, "GO", Item: item, Asked: $"GO cue {item}")
            : new PlayoutPlan(null, PlayoutCall.Cue, "STANDBY", Item: item, Asked: $"stand cue {item} by");
    }

    /// <summary>A playlist by its id, else by its name (any case): the words a person types name either.</summary>
    public static PlayoutPlaylist? FindPlaylist(IReadOnlyList<PlayoutPlaylist> lists, string word)
        => lists.FirstOrDefault(p => p.Id == word) ?? lists.FirstOrDefault(p => p.Name.Equals(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a STATE shows <paramref name="playlist"/> in <paramref name="state"/> on <paramref name="item"/> (its id or label; any item for <c>#n</c>).</summary>
    private static bool Plays(PlayoutPeerState s, string playlist, string item, string state)
        => s.HasPlaylists && FindPlaylist(s.Playlists, playlist) is { } p && p.State == state
           && (item.StartsWith('#') || p.ItemId == item || p.ItemLabel.Equals(item, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ the VT

    /// <summary>
    /// The clip feeding the VT countdown, pure: among the links that may feed it (followVt), in Settings' order, the first whose clip
    /// is running, else vMix's running clip, else the first link with a clip, else vMix's.
    /// </summary>
    public static PlayoutState? PickVt(IReadOnlyList<PlayoutLinkSettings> settings, Func<string, PlayoutState?> clipOf, PlayoutState? vmix)
    {
        var clips = settings.Where(s => s.FollowVt).Select(s => clipOf(s.Name)).OfType<PlayoutState>().ToList();
        return clips.FirstOrDefault(c => c.Running) ?? (vmix is { Running: true } ? vmix : null) ?? clips.FirstOrDefault() ?? vmix;
    }
}

/// <summary>
/// The clips' ends, per link, and the running order's one move per item execution (the show controller's <c>Execution</c>): a clip end
/// is said once per clip run, a clip rewound or restarted (another clip, or its position back before its last second) can end again,
/// and only the link feeding the VT moves the running order, at most once an execution — two links ending a moment apart never move it
/// twice. A look with no clip that counts down (the link stale or dropped a while, a clip gone stale, a looping one) changes nothing of
/// what it knows: the same ended clip seen again after such a gap is not a second end. A link is forgotten only once it stops running
/// (<see cref="Keep"/>). Owner thread.
/// </summary>
public sealed class PlayoutClipEnds
{
    private readonly Dictionary<string, (string Key, bool Handled)> _clips = new(StringComparer.Ordinal);
    private string _moved = "";

    /// <summary>Whether this look at <paramref name="link"/>'s clip <paramref name="v"/> is its end, said once a clip run.</summary>
    public bool Ended(string link, PlayoutVideo? v)
    {
        if (v is not { CountsDown: true }) return false;
        var key = v.File + "|" + v.Tag;
        var (was, handled) = _clips.TryGetValue(link, out var c) ? c : ("", false);
        if (key != was) handled = false;
        var left = v.LengthSeconds - v.PositionSeconds;
        if (left > 1) handled = false;
        var ends = !handled && left <= 1 && (v.Ended || (left <= 0.25 && !v.Paused));
        _clips[link] = (key, handled || ends);
        return ends;
    }

    /// <summary>True once for <paramref name="execution"/> (non-empty): the one move a clip end may make in it.</summary>
    public bool MayMoveOn(string execution)
    {
        if (execution.Length == 0 || execution == _moved) return false;
        _moved = execution;
        return true;
    }

    /// <summary>The links no longer running are forgotten.</summary>
    public void Keep(IEnumerable<string> running)
    {
        var keep = running.ToHashSet(StringComparer.Ordinal);
        foreach (var gone in _clips.Keys.Where(k => !keep.Contains(k)).ToList()) _clips.Remove(gone);
    }
}
