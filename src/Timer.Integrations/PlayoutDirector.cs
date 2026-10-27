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
/// playlist playing or cued on an item, or the clip on air paused or playing. "Seen" is seen in STATE, never on the outputs. A timed
/// GO's look also reads, from the STATE that shows it, whether it started late (<see cref="Late"/>: words, or null when on time).
/// </summary>
public sealed record PlayoutWatch(string Words, Func<PlayoutPeerState, bool> Seen, long Ms, Func<PlayoutPeerState, string?>? Late = null);

/// <summary>
/// A playout command as its link will carry it, pure: the verb it calls and that verb's words, what was asked in people's words, and
/// what its effect is looked for as; or why nothing is sent (<see cref="Error"/>).
/// </summary>
public sealed record PlayoutPlan(string? Error, PlayoutCall Call = PlayoutCall.Clip, string What = "", int Seconds = 0, string List = "",
    string? Item = null, string? Expect = null, string? OnKey = null, string Asked = "", PlayoutWatch? Watch = null)
{
    public static PlayoutPlan Refused(string why) => new(why);

    /// <summary>A GO asked to start at one instant with others (<c>together</c>): timed only where its link's clock allows (13.11.6).</summary>
    public bool Together { get; init; }

    /// <summary>A PlayMap GO's instant on its node's own wall clock (Unix ms), set only from a clock fit for it; null sends it untimed.</summary>
    public long? At { get; init; }

    /// <summary>The command on <paramref name="link"/>, with the asker's <paramref name="mark"/>: its ticket, or why it was not sent.</summary>
    public PlayoutSent Run(PlayoutLink link, int mark) => Call switch
    {
        PlayoutCall.Playlist => link.Playlist(What, List, Item, Expect, mark, At),
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
/// sender is told beside a command that went, true whatever the peer answers: "" unless it was asked together (sent with AT, or not
/// timed and why) or its effect is not looked for in the peer's STATE, and why. <see cref="Taken"/> is said in its place once the
/// peer took the command (applied or accepted) — a GO sent with AT is "scheduled for …" only then — and "" when the note stands.
/// </summary>
public sealed record PlayoutStep(string? Error, string Link = "", string Peer = "", string Asked = "", PlayoutTicket? Ticket = null)
{
    public static PlayoutStep Refused(string why) => new(why);

    public string Note { get; init; } = "";

    public string Taken { get; init; } = "";
}

/// <summary>How one GO of a start goes (13.11.6): at once, at an instant on its node's own clock (<c>AT</c>), or held on this PC.</summary>
public enum PlayoutWhen { Now, At, Hold }

/// <summary>
/// One GO's timing, pure: how it goes; <see cref="InstantMs"/>, the instant its start was set for on this PC's wall clock (Unix ms);
/// <see cref="AtPeerMs"/>, that instant on its node's clock (<see cref="PlayoutWhen.At"/>); <see cref="SendAtMs"/>, when this PC writes
/// it (<see cref="PlayoutWhen.Hold"/>); the round trip of the clock sample it was judged by (0 for none); its words for the rows as it
/// goes, an attempt's (a GO "sent with AT for …"); and <see cref="Taken"/>, the words once its node took it ("scheduled for …"; "" when
/// there are none: nothing of its timing waits on the node's answer).
/// </summary>
public sealed record PlayoutTiming(PlayoutWhen When, long InstantMs, long AtPeerMs = 0, long SendAtMs = 0, long RttMs = 0, string Words = "", string Taken = "");

/// <summary>
/// A link's clock as a timing reads it: the peer as people read it, its kind, the believed sample (13.11.3; null for none), whether it
/// is fit for a GO's tolerance on a channel that may time a command (<see cref="PlayoutLink.CanSchedule"/>), whether that channel may
/// time one at all (<see cref="PlayoutLink.ClockMayTime"/>: TLS or a local link), and whether the link is up and paired — one that is
/// not is sent nothing, so its missing clock takes no timing from the others.
/// </summary>
public sealed record PlayoutClockFacts(string Peer, string? Kind, PeerClock.Belief? Clock, bool Fit, bool MayTime, bool Up = true)
{
    public static PlayoutClockFacts Of(PlayoutLink link, long toleranceMs)
        => new(link.Peer, link.Kind, link.Clock, link.CanSchedule(toleranceMs), link.ClockMayTime, link.Connected && link.Pairing != PlayoutPairing.None);
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
    private readonly Func<ShowClock> _clock;
    // How many effects are looked for now, per link name: each look takes one and gives it back when it ends.
    private readonly Dictionary<string, int> _watching = new(StringComparer.Ordinal);
    // The running-order item loaded in this turn of the owner thread, with its cues as they were then, until the turn ends (owner thread).
    private Loaded? _loaded;

    /// <summary>An item loaded in the owner thread's turn: its place, its id, its cues as loaded, and whether it started in that turn.</summary>
    private sealed record Loaded(int Index, string Id, IReadOnlyList<PlayoutCue> Cues)
    {
        public bool Started { get; set; }
    }

    /// <param name="links">The links running now.</param>
    /// <param name="settings">The links as Settings has them now, in their order.</param>
    /// <param name="licensed">Whether the licence runs playout links now.</param>
    /// <param name="leadMs">The operator's lead for a GO (its effect is looked for that long and <see cref="WatchMs"/>).</param>
    /// <param name="journal">A row for the show log: when, the running-order item it is filed against (-1 none), its words. Any thread.</param>
    /// <param name="status">A line for the desk's status line. Any thread.</param>
    /// <param name="time">
    /// The clock its looks at the peers' STATE, a timed GO's instant and a held send run on: this PC's wall clock, the one the links'
    /// clock samples are taken on.
    /// </param>
    /// <param name="clock">The show's clock, which its rows say an instant in (UTC when none is given).</param>
    public PlayoutDirector(PlayoutLinks links, Func<IReadOnlyList<PlayoutLinkSettings>> settings, Func<bool> licensed, Func<int> leadMs,
        Action<DateTimeOffset, int, string> journal, Action<string> status, TimeProvider? time = null, Func<ShowClock>? clock = null)
    {
        _links = links;
        _settings = settings;
        _licensed = licensed;
        _leadMs = leadMs;
        _journal = journal;
        _status = status;
        _time = time ?? TimeProvider.System;
        _clock = clock ?? (() => Utc);
    }

    private static readonly ShowClock Utc = new(TimeZoneInfo.Utc);

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
        var lead = _leadMs();
        var plan = Plan(target, PlayoutFacts.Of(link), a.Action, a.List, a.Item, a.Together, (int)number, lead);
        if (plan is not { Error: null, Together: true }) return Send(link, plan, mark);
        // A lone GO asked together: at an instant on its node's clock where that clock is fit for it, else at once — said either way;
        // "scheduled" only once the node took it.
        var timing = Together([PlayoutClockFacts.Of(link, ToleranceMs(lead))], NowMs(), lead, When)[0];
        var step = Send(link, timing.When == PlayoutWhen.At ? Timed(plan, timing) : plan, mark);
        if (step.Error is not null) return step;
        _journal(_time.GetUtcNow(), mark, $"{link.Peer}: {plan.Asked} {timing.Words}");
        if (timing.Taken.Length > 0) _ = WhenTaken(step.Ticket!, () => _journal(_time.GetUtcNow(), mark, $"{link.Peer}: {plan.Asked} {timing.Taken}"));
        static string With(string words, string note) => note.Length > 0 ? words + " · " + note : words;
        return step with { Note = With(timing.Words, step.Note), Taken = timing.Taken.Length > 0 ? With(timing.Taken, step.Note) : "" };
    }

    /// <summary>
    /// <paramref name="then"/> once the peer took the command (applied or accepted) — never before, and never for a refusal, a command
    /// never sent or one never answered: a timed GO is said to be scheduled only from its node's own answer.
    /// </summary>
    private static async Task WhenTaken(PlayoutTicket ticket, Action then)
    {
        var first = await ticket.First.ConfigureAwait(false);
        if (first.Outcome is PeerOutcome.Applied or PeerOutcome.Accepted) then();
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
                    if (watch.Late?.Invoke(r.State) is { } late) _journal(_time.GetUtcNow(), mark, $"{link.Peer}: {late}");
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

    // ------------------------------------------------------------------ timed GO, pure

    /// <summary>How far past its instant a timed GO may be seen to start, beyond half its round trip, before a row says it started late.</summary>
    public const long LateMarginMs = 20;

    /// <summary>
    /// A timed GO's tolerance (13.11.4's "fit for"): the error a round trip as long as the operator's lead gives, so a GO goes at an
    /// instant on its node's clock only when the clock's believed sample came back within the lead it is set ahead by.
    /// </summary>
    public static long ToleranceMs(int leadMs) => PeerClock.ErrMs(leadMs);

    /// <summary>
    /// The GOs of one start asked to go together, timed at one instant <c>T</c> = now + the lead on this PC's clock (pure), each by its own
    /// link's clock (13.11.6):
    /// <list type="bullet">
    /// <item>a PlayMap node whose clock is fit for <see cref="ToleranceMs"/> on a channel that may time a command (TLS, or a local link)
    /// gets <c>AT</c> = T plus its offset, sent now;</item>
    /// <item>any other member is held on this PC and sent untimed: half its round trip before T when its clock is measured on such a
    /// channel (a Patterns desk takes no <c>AT</c>; a node's clock not fit), and at T itself by this PC's clock alone when it is measured
    /// over a plain link off this PC — such a clock may be shown and never schedules anything, its round trip included;</item>
    /// <item>when any member that is up has no clock sample at all, nothing is together: every GO goes at once, untimed, and the words
    /// say whose clock is missing;</item>
    /// <item>a member whose link is not up and paired is sent at once, untimed — its link refuses it, and its own row says why — and
    /// takes no timing from the others;</item>
    /// <item>a lone GO (the one member up) is never held: at its instant on its node's clock when that is fit for it, else at once.</item>
    /// </list>
    /// A GO with <c>AT</c> is said to be sent with it as it goes and scheduled only once its node took it (<see cref="PlayoutTiming.Taken"/>).
    /// <paramref name="when"/> says an instant (Unix ms on this PC) in the rows' words.
    /// </summary>
    public static IReadOnlyList<PlayoutTiming> Together(IReadOnlyList<PlayoutClockFacts> members, long nowMs, int leadMs, Func<long, string> when)
    {
        var t = nowMs + leadMs;
        var tolerance = ToleranceMs(leadMs);
        var up = members.Where(m => m.Up).ToList();
        var lone = up.Count == 1;
        var clockless = lone ? null : up.FirstOrDefault(m => m.Clock is null);
        return members.Select(m => !m.Up ? new PlayoutTiming(PlayoutWhen.Now, t, Words: $"not timed: the link to {m.Peer} wasn't up as the start was timed — sent at once")
            : clockless is not null ? new PlayoutTiming(PlayoutWhen.Now, t, Words: $"not together: {clockless.Peer} has no clock yet — every GO sent at once, untimed")
            : One(m)).ToList();

        PlayoutTiming One(PlayoutClockFacts m)
        {
            if (m.Clock is not { } c) return new(PlayoutWhen.Now, t, Words: $"not timed: {m.Peer} has no clock yet — sent at once");
            var rtt = c.RttMs.ToString(CultureInfo.InvariantCulture);
            var err = c.ErrMs.ToString(CultureInfo.InvariantCulture);
            if (m.Kind == PlayoutRules.PlayMap && m.Fit)
                return new(PlayoutWhen.At, t, AtPeerMs: t + (long)Math.Round(c.OffsetMs), RttMs: c.RttMs,
                    Words: $"sent with AT for {when(t)} (± {err} ms)", Taken: $"scheduled for {when(t)} (± {err} ms)");
            var why = Why(m);
            if (lone) return new(PlayoutWhen.Now, t, RttMs: c.RttMs, Words: $"not timed: {why} — sent at once");
            if (!m.MayTime) return new(PlayoutWhen.Hold, t, SendAtMs: t, RttMs: c.RttMs, Words: $"held here, to be sent at {when(t)} by this PC's clock alone: {why}");
            var send = t - (c.RttMs + 1) / 2;
            return new(PlayoutWhen.Hold, t, SendAtMs: send, RttMs: c.RttMs,
                Words: $"held here, to be sent at {when(send)}, half its round trip ({rtt} ms) before {when(t)}: {why}");
        }

        string Why(PlayoutClockFacts m)
        {
            var lead = leadMs.ToString(CultureInfo.InvariantCulture);
            if (m.Kind != PlayoutRules.PlayMap)
                return m.MayTime ? $"{m.Peer} takes no AT"
                    : $"{m.Peer} takes no AT, and its clock, measured over plain TCP off this PC, schedules nothing (Show Bus 13.11.6) until Patterns offers TLS on its wire (its Round 97)";
            return m.MayTime
                ? $"{m.Peer}'s clock is not fit for a GO {lead} ms ahead (a sample at most {(PeerLink.ClockFreshMs / 1000).ToString(CultureInfo.InvariantCulture)} s old, off by at most {tolerance.ToString(CultureInfo.InvariantCulture)} ms)"
                : $"{m.Peer}'s clock is measured over plain TCP off this PC and schedules nothing (Show Bus 13.11.6) until the link reaches the node's TLS door";
        }
    }

    /// <summary>
    /// For each GO of one start, the GO that moves it (pure): its own index, or — for a PlayMap playlist on a sync group whose node
    /// follows it — that of a GO among them on the same group to the node that leads it (STATE <c>sync.role</c> <c>leader</c>), since a
    /// follower takes its leader's anchors and a GO of its own would be overruled. With no leader among them, each stands alone (and
    /// the plan refuses a follower's GO in words naming its leader).
    /// </summary>
    public static IReadOnlyList<int> Leaders(IReadOnlyList<(string Group, string Role)> members)
    {
        var moved = new int[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            moved[i] = i;
            var (group, role) = members[i];
            if (group.Length == 0 || role != "follower") continue;
            for (var j = 0; j < members.Count; j++)
                if (members[j].Group == group && members[j].Role == "leader") { moved[i] = j; break; }
        }
        return moved;
    }

    /// <summary>
    /// Words when the STATE that shows a timed GO's playlist playing says it started late (pure): its start on the node's clock — the
    /// STATE's <c>at</c> less the playlist's <c>positionMs</c>, which repeats the <c>AT</c> instant unless the node clamped an <c>AT</c>
    /// that reached it after its instant — more than half the round trip and <see cref="LateMarginMs"/> past <paramref name="atPeerMs"/>.
    /// Null when on time, or when the STATE gives no <c>at</c> or position to tell by.
    /// </summary>
    public static string? Late(PlayoutPeerState s, string playlist, long atPeerMs, long rttMs)
    {
        if (s.At is not { } at || !s.HasPlaylists || FindPlaylist(s.Playlists, playlist) is not { PositionMs: { } position } pl) return null;
        var late = at - position - atPeerMs;
        return late > (rttMs + 1) / 2 + LateMarginMs
            ? $"{(pl.Name.Length > 0 ? pl.Name : pl.Id)} started late by {late.ToString(CultureInfo.InvariantCulture)} ms (AT arrived after its instant)"
            : null;
    }

    /// <summary>A GO's plan at <paramref name="timing"/>'s instant on its node's clock, its look also reading whether it started late.</summary>
    private static PlayoutPlan Timed(PlayoutPlan plan, PlayoutTiming timing)
        => plan with
        {
            At = timing.AtPeerMs,
            Watch = plan.Watch is { } w ? w with { Late = s => Late(s, plan.List, timing.AtPeerMs, timing.RttMs) } : null,
        };

    private long NowMs() => _time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>An instant (Unix ms on this PC) in the show's clock, as the rows say it.</summary>
    private string When(long ms)
        => _clock().ToShow(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ the running order's cues

    /// <summary>
    /// A running-order item was loaded (owner thread): its cues are kept for the end of this turn of the owner thread, when those the
    /// operator set to stand by (each off unless set: a PlayMap standby shows the item's first frame on air, a Patterns one moves that
    /// desk's own standby) stand by — unless the item also started in the turn, when its GOs alone go. The host calls
    /// <see cref="EndOfTurn"/> once the turn is over (posted behind it).
    /// </summary>
    public void ItemLoaded(int index, RundownItem item)
        => _loaded = item.Playout is { Count: > 0 } cues ? new Loaded(index, item.Id, cues.Select(c => c.Clone()).ToList()) : null;

    /// <summary>
    /// A running-order item started for the first time in its run (owner thread): its cues GO — a PlayMap playlist's item, a Patterns
    /// cue by <c>CUE GO</c> alone (its own standby fence decides; never STANDBY and GO together) — one GO per sync group, those marked
    /// together at one instant where each link's clock allows (<see cref="Together"/>), each cue a link cannot take skipped with a row.
    /// </summary>
    public void ItemStarted(int index, RundownItem item)
    {
        if (_loaded is { } loaded && loaded.Index == index && loaded.Id == item.Id) loaded.Started = true;
        if (item.Playout is { Count: > 0 } cues) Start(index, cues.Select(c => c.Clone()).ToList());
    }

    /// <summary>
    /// A person's "GO playout now" on a running-order row (owner thread): the item's cues GO as they do when it starts — one GO per sync
    /// group, those marked together at one instant, each a link cannot take skipped with its row — whether or not the item is loaded.
    /// The running order does not move. An item with no cues says so on the status line and sends nothing.
    /// </summary>
    public void GoNow(int index, RundownItem item)
    {
        if (item.Playout is { Count: > 0 } cues) Start(index, cues.Select(c => c.Clone()).ToList());
        else Row(index, "no playout cues to GO — nothing sent");
    }

    /// <summary>
    /// A person's "Cue playout now" on a running-order row (owner thread): every cue of the item stands by — set to stand by at load or
    /// not, since a person asked — by the same guards a load's standby keeps: a PlayMap CUE only on a playlist seen idle a moment ago
    /// (a CUE shows its first frame on air), a Patterns STANDBY moving that desk's own standby. Nothing GOes.
    /// </summary>
    public void CueNow(int index, RundownItem item)
    {
        if (item.Playout is { Count: > 0 } cues) Standby(index, cues.Select(c => { var k = c.Clone(); k.Standby = true; return k; }).ToList());
        else Row(index, "no playout cues to stand by — nothing sent");
    }

    /// <summary>The owner thread's turn that loaded an item is over: its standby cues stand by, unless it started in that turn.</summary>
    public void EndOfTurn()
    {
        if (_loaded is not { } loaded) return;
        _loaded = null;
        if (!loaded.Started) Standby(loaded.Index, loaded.Cues);
    }

    /// <summary>
    /// The amber advice before <paramref name="item"/> (at <paramref name="index"/>) starts: each of its cues that would be skipped, in
    /// the words its row would have — no such link, one switched off or not running, a peer answering as another kind or node than its
    /// settings name, or one whose own list (INDEX, CUE LIST) does not hold what it names — and, without the licence, one line for
    /// them all. The planner's own facts, as a refusal is: a desk shows them whether or not its assistant is on.
    /// </summary>
    public IReadOnlyList<Advice> CueAdvice(RundownItem item, int index)
    {
        if (item.Playout is not { Count: > 0 } cues) return [];
        var number = (index + 1).ToString(CultureInfo.InvariantCulture);
        if (!_licensed())
            return [new Advice($"playout-cue:{item.Id}", AdviceLevel.Warn, $"Item {number} ({item.Title}): its playout cues won't go — {PlayoutRules.LicenceWords}", null, null)];
        var advice = new List<Advice>();
        for (var n = 0; n < cues.Count; n++)
            if (Resolve(cues[n]).Skip is { } skip)
                advice.Add(new Advice($"playout-cue:{item.Id}:{n.ToString(CultureInfo.InvariantCulture)}", AdviceLevel.Warn,
                    $"Item {number} ({item.Title}): {CueName(cues[n])} will be skipped — {skip}", null, null));
        return advice;
    }

    /// <summary>
    /// The link a cue names, running, and whether its peer lists what it names; or why the cue is skipped. A peer that answers as
    /// another kind or node than its settings name is said so first: nothing of its list is a reason (13.4.3).
    /// </summary>
    private (PlayoutLinkSettings? Settings, PlayoutLink? Link, string? Skip) Resolve(PlayoutCue cue)
    {
        var label = PlayoutRules.Label(cue.Link);
        if (PlayoutRules.Find(_settings(), cue.Link) is not { } s)
            return (null, null, $"no playout link called {cue.Link} on this PC");
        if (!s.Enabled) return (null, null, $"{label} is switched off (Settings → Integrations)");
        if (_links[s.Name] is not { } link) return (null, null, $"{label} isn't running");
        if (link.WrongKind is { } wrong) return (null, null, wrong);
        if (link.Index?.Lacks(cue, link.Peer) is { } lacks) return (null, null, lacks);
        return (s, link, null);
    }

    /// <summary>A cue as the rows name it: its label, else what it names, and the link it is on.</summary>
    private static string CueName(PlayoutCue c)
        => $"playout cue {(c.Label.Length > 0 ? c.Label : c.List.Length > 0 ? c.List + " " + c.Item : c.Item)} on {PlayoutRules.Label(c.Link)}";

    /// <summary>
    /// One of the planner's rows: in the show log against the item, and on the desk's status line with the item's number — a cue
    /// skipped, not sent or not stood by reaches the person running the show, not only the log, as a link's answers do.
    /// </summary>
    private void Row(int index, string words)
    {
        _journal(_time.GetUtcNow(), index, words);
        _status($"Item {(index + 1).ToString(CultureInfo.InvariantCulture)}: {words}");
    }

    /// <summary>The cues of an item loaded and not started in its turn, set to stand by: a CUE (PlayMap, an idle playlist's only) or a STANDBY (Patterns).</summary>
    private void Standby(int index, IReadOnlyList<PlayoutCue> cues)
    {
        var standing = cues.Where(c => c.Standby).ToList();
        if (standing.Count == 0) return;
        if (!_licensed())
        {
            Row(index, $"{Cues(standing.Count)} not stood by: {PlayoutRules.LicenceWords}");
            return;
        }
        var lead = _leadMs();
        foreach (var cue in standing)
        {
            var (s, link, skip) = Resolve(cue);
            if (skip is not null)
            {
                Row(index, $"{CueName(cue)}: not stood by — {skip}");
                continue;
            }
            var plan = Plan(s!, PlayoutFacts.Of(link!), "cue", cue.List, cue.Item, false, 0, lead);
            if (plan.Error is { } why) Row(index, $"{CueName(cue)}: not stood by — {why}");
            else if (Send(link!, plan, index).Error is { } refused) Row(index, $"{CueName(cue)}: not stood by — {refused}");
        }
    }

    /// <summary>
    /// A GO of a start as it goes: its cue, link and plan, and what it says once its send is known — to the cues of the same sync group
    /// left to it (<see cref="Leaders"/>), whose rows say whether their leader's GO went (null) or why it did not.
    /// </summary>
    private sealed record Going(PlayoutCue Cue, PlayoutLink Link, PlayoutPlan Plan, Action<string?> Sent);

    /// <summary>
    /// The cues of an item that started: one GO each, but one per sync group (<see cref="Leaders"/>, among the GOs whose plans pass);
    /// those marked together timed at one instant (<see cref="Together"/>) — sent with <c>AT</c>, held on this PC until their moment, or
    /// sent at once — the rest at once; each cue that cannot go has a row saying why, and so has each timed or held GO, a GO with
    /// <c>AT</c> scheduled only once its node took it. A send held for one connection of its link never goes on another, nor on a link
    /// Settings replaced or stopped, nor without the licence: each leaves it not sent, with a row.
    /// </summary>
    private void Start(int index, IReadOnlyList<PlayoutCue> cues)
    {
        if (!_licensed())
        {
            Row(index, $"{Cues(cues.Count)} not sent: {PlayoutRules.LicenceWords}");
            return;
        }
        var lead = _leadMs();
        var members = new List<(PlayoutCue Cue, PlayoutLink Link, PlayoutFacts Facts, PlayoutPlan Plan)>();
        foreach (var cue in cues)
        {
            var (s, link, skip) = Resolve(cue);
            if (skip is not null) Row(index, $"{CueName(cue)}: skipped — {skip}");
            else
            {
                var facts = PlayoutFacts.Of(link!);
                members.Add((cue, link!, facts, Plan(s!, facts, "go", cue.List, cue.Item, cue.Together, 0, lead)));
            }
        }
        // A follower's own plan refuses it; it is left to its group's leader only where that leader's GO is planned among them.
        var leaders = Leaders(members.Select(m => SyncOf(m.Facts, m.Cue) is var (group, role) && (role != "leader" || m.Plan.Error is null) ? (group, role) : (group, "")).ToList());
        var going = new List<Going>();
        for (var i = 0; i < members.Count; i++)
        {
            if (leaders[i] != i) continue;
            var (cue, link, _, plan) = members[i];
            if (plan.Error is { } why)
            {
                Row(index, $"{CueName(cue)}: not sent — {why}");
                continue;
            }
            var follow = Enumerable.Range(0, members.Count).Where(j => j != i && leaders[j] == i).ToList();
            going.Add(new Going(cue, link, plan, refused =>
            {
                foreach (var j in follow)
                {
                    var (fc, fl, ff, _) = members[j];
                    var group = SyncOf(ff, fc).Group;
                    Row(index, refused is null
                        ? $"{CueName(fc)}: not sent — {fl.Peer} follows sync group {group}, which {link.Peer}'s GO moves"
                        : $"{CueName(fc)}: not sent — {fl.Peer} follows sync group {group}, and its leader's GO on {link.Peer} was not sent: {refused}");
                }
            }));
        }
        var together = going.Where(g => g.Plan.Together).ToList();
        var timings = Together(together.Select(g => PlayoutClockFacts.Of(g.Link, ToleranceMs(lead))).ToList(), NowMs(), lead, When);
        // The GOs at an instant first, so each reaches its node ahead of that instant; then those at once; then the ones held here.
        for (var i = 0; i < together.Count; i++)
            if (timings[i].When == PlayoutWhen.At) Go(index, together[i] with { Plan = Timed(together[i].Plan, timings[i]) }, timings[i]);
        foreach (var g in going.Where(g => !g.Plan.Together)) Go(index, g, null);
        for (var i = 0; i < together.Count; i++)
        {
            if (timings[i].When == PlayoutWhen.Now) Go(index, together[i], timings[i]);
            else if (timings[i].When == PlayoutWhen.Hold)
            {
                var g = together[i];
                Row(index, $"{CueName(g.Cue)}: {g.Plan.Asked} {timings[i].Words}");
                _ = HoldAsync(index, g, timings[i].SendAtMs, g.Link.ConnectionSerial);
            }
        }
    }

    /// <summary>
    /// A cue's GO sent now, with a row for its timing's words when it has some — and, for a GO with <c>AT</c>, one more once its node
    /// took it — or a row saying why it was not sent; then what its send means to the cues left to it.
    /// </summary>
    private void Go(int index, Going g, PlayoutTiming? timing)
    {
        var step = Send(g.Link, g.Plan, index);
        if (step.Error is { } refused) Row(index, $"{CueName(g.Cue)}: not sent — {refused}");
        else if (timing is not null)
        {
            Row(index, $"{CueName(g.Cue)}: {g.Plan.Asked} {timing.Words}");
            if (timing.Taken.Length > 0) _ = WhenTaken(step.Ticket!, () => Row(index, $"{CueName(g.Cue)}: {g.Plan.Asked} {timing.Taken}"));
        }
        g.Sent(step.Error);
    }

    /// <summary>
    /// A GO held on this PC until <paramref name="sendAtMs"/>, then written on the link and connection it was held for — never on
    /// another, never once Settings replaced or stopped that link, and never without the licence; whatever stops it, a row says it was
    /// not sent and why, and the cues left to it hear the same.
    /// </summary>
    private async Task HoldAsync(int index, Going g, long sendAtMs, int connection)
    {
        try
        {
            var wait = sendAtMs - NowMs();
            if (wait > 0) await Task.Delay(TimeSpan.FromMilliseconds(wait), _time).ConfigureAwait(false);
            var stopped = !_licensed() ? PlayoutRules.LicenceWords
                : !ReferenceEquals(_links[g.Link.LinkName], g.Link) ? $"the link to {g.Link.Peer} was stopped or replaced while it was held"
                : connection != 0 && g.Link.ConnectionSerial != connection ? $"the link to {g.Link.Peer} dropped while it was held"
                : null;
            if (stopped is not null)
            {
                Row(index, $"{CueName(g.Cue)}: not sent — {stopped}");
                g.Sent(stopped);
                return;
            }
            Go(index, g, null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A held GO that cannot be written (its link disposed under it, say) is said not sent, never lost without a row.
            var why = $"its send failed: {e.Message}";
            Row(index, $"{CueName(g.Cue)}: not sent — {why}");
            g.Sent(why);
        }
    }

    /// <summary>The sync group of the PlayMap playlist a cue names, and its node's place in it, from the node's STATE ("" when none).</summary>
    private static (string Group, string Role) SyncOf(PlayoutFacts facts, PlayoutCue cue)
    {
        if (facts.Kind != PlayoutRules.PlayMap || cue.List.Length == 0 || facts.State is not { HasPlaylists: true } state) return ("", "");
        var group = FindPlaylist(state.Playlists, cue.List)?.SyncGroup ?? "";
        return group.Length == 0 ? ("", "") : (group, state is { HasSync: true, Sync.Role: var role } ? role : "");
    }

    private static string Cues(int n) => n == 1 ? "1 playout cue" : n.ToString(CultureInfo.InvariantCulture) + " playout cues";

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
    /// <item>a GO asked <c>together</c> is marked so (<see cref="PlayoutPlan.Together"/>); when it goes, and whether at an instant on its
    /// node's clock, is <see cref="Together"/>'s to say from each link's clock (13.11.6) — never this plan's.</item>
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
        var plan = facts.Kind == PlayoutRules.PlayMap ? PlayMapStart(link.Name, peer, facts, action, list, item, leadMs) : PatternsStart(link.Name, action, list, item);
        return together && action == "go" && plan.Error is null ? plan with { Together = true } : plan;
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
