using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// The context every door serves (<c>GET /api/context</c>, MCP's <c>get_context</c>, the context a peer pulls), so that §6 P5 holds on
/// the wire as it does in the builder, and the one rule every door a reader asks serves by (<see cref="Stuck"/>: the state doors and the
/// preflight read it too, through <c>IShowControl.ServedStuck</c>). The show thread publishes, at each build, the context and the same
/// context built as a stuck show thread's (its timer, schedule and VT countdown red with <see cref="ShowContext.StuckWords"/>, its
/// headline with no counting value) as one pair stamped with the hub's clock; a reader on any thread takes the stuck one while
/// <see cref="Stuck"/> holds. The invariant: a door says stuck only when the show thread has been silent past the hub's bound
/// (<see cref="SnapshotHub.StuckAfter"/>), is resuming from that (the hub's frame not yet published since, or no context built since
/// from a frame made after it), or has turned over without building for <see cref="MaxAge"/> — and then it says so at every asked door
/// at once. The build is due by time on the hub's clock (<see cref="Due"/>: at a turn that finds the last build
/// <see cref="BuildEvery"/> old), never by a count of turns, so a live thread's build is never older than its own last gap plus
/// <see cref="BuildEvery"/>; <see cref="MaxAge"/> is the hub's bound plus that, so the age term fires only where a build was missed for
/// longer than the hub's own bound allows a live thread. So no context from before a stall is served live after it (the resuming mark
/// holds from the turn that ends the stall until a build from that turn's frame or later), and a build that stops while the thread
/// still turns over fails closed by age. Everything in the stuck form but its counting items and its headline is as it stood at the
/// context's <c>at</c>, from before the stall: links, windows still "shown", the timer's <c>feeds</c> rows and the <c>serves</c> rows
/// to holders, health — P5 reddens counting items and no more, and <c>at</c> dates the rest.
/// </summary>
public sealed class ServedContext(SnapshotHub hub)
{
    /// <summary>How often a live show thread builds the context: the preflight's judging cadence (<see cref="PreflightWire.BuildEvery"/>).</summary>
    public static TimeSpan BuildEvery => PreflightWire.BuildEvery;

    /// <summary>How old a built context may be and still be served live: the preflight's bound, the hub's plus one build period.</summary>
    public static TimeSpan MaxAge => PreflightWire.MaxAge;

    private sealed record Pair(string Live, string Stuck, long AtMs);

    private volatile Pair? _pair;
    private volatile bool _resuming;   // written on the show thread

    /// <summary>
    /// The show thread's first act each turn, in place of the hub's own: the hub is marked alive, and a turn the hub says ended a stall
    /// (its own verdict, <see cref="SnapshotHub.MarkAlive"/>'s answer, never a second read of the clock) marks the context resuming. The
    /// mark lands after the hub's alive and before any publish of this turn: until the hub's next publish its <see cref="SnapshotHub.Resuming"/>
    /// holds every door stuck, and that publish comes after this write on the same thread.
    /// </summary>
    public void MarkAlive()
    {
        if (hub.MarkAlive()) _resuming = true;
    }

    /// <summary>
    /// Whether this turn builds the context (show thread, read when the turn would build): before the first build, while resuming after
    /// a stall, and once the last build is <see cref="BuildEvery"/> old on the hub's clock. A build that threw leaves it due.
    /// </summary>
    public bool Due => _resuming || _pair is not { } p || hub.NowMs - p.AtMs >= (long)BuildEvery.TotalMilliseconds;

    /// <summary>
    /// Builds and publishes both forms of the context from one input (show thread). An input already stuck is its own stuck form. A build
    /// while the hub is still <see cref="SnapshotHub.Resuming"/> was made from the frame from before the stall, so it leaves the context
    /// resuming (served stuck) and the next turn builds again.
    /// </summary>
    public void Publish(ContextInput x)
    {
        var at = hub.NowMs;
        var live = ShowContext.Build(x);
        _pair = new Pair(live, x.ShowThreadStuck ? live : ShowContext.Build(x with { ShowThreadStuck = true }), at);
        if (!hub.Resuming) _resuming = false;   // after the pair: a reader that read it resumed reads the new build after it
    }

    /// <summary>
    /// The rule every door a reader asks serves by (any thread, read at serve time): the hub's frame is stale, the context is resuming
    /// after a stall, or the last build is older than <see cref="MaxAge"/>. Before the first build only the hub's word. The flags are
    /// read before the pair: a pair read after a cleared flag is at least the build that cleared it.
    /// </summary>
    public bool Stuck
    {
        get
        {
            var stale = Stale;
            return stale || (_pair is { } p && Old(p, hub.NowMs));
        }
    }

    /// <summary>
    /// The context to serve now (any thread): the stuck form while <see cref="Stuck"/>, else the last one built; null before the first
    /// build. The flags are read before the pair, as <see cref="Stuck"/> reads them.
    /// </summary>
    public string? Json()
    {
        var stale = Stale;
        if (_pair is not { } p) return null;
        return stale || Old(p, hub.NowMs) ? p.Stuck : p.Live;
    }

    private bool Stale => hub.FrameStale || _resuming;

    private static bool Old(Pair p, long nowMs) => nowMs - p.AtMs > (long)MaxAge.TotalMilliseconds;
}
