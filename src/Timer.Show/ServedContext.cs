using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// The context every door serves (<c>GET /api/context</c>, MCP's <c>get_context</c>, the context a peer pulls), so that §6 P5 holds on
/// the wire as it does in the builder, and the one rule every door a reader asks serves by (<see cref="Stuck"/>: the state doors and the
/// preflight read it too, through <c>IShowControl.ServedStuck</c>). The show thread publishes, at each build, the context and the same
/// context built as a stuck show thread's (its timer, schedule and VT countdown red with <see cref="ShowContext.StuckWords"/>, its
/// headline with no counting value) as one pair stamped with the hub's clock; a reader on any thread takes the stuck one while
/// <see cref="Stuck"/> holds: the hub's frame is stale (<see cref="SnapshotHub.FrameStale"/>: the show thread stuck, or turned over
/// since a stall and not yet published), the context is resuming (the thread turned over since a stall and has not yet built a context
/// from a frame made after it), or the pair is older than <see cref="MaxAge"/>. The bound is the preflight's (<see cref="PreflightWire.MaxAge"/>);
/// the context's age is read on the hub's injected clock, the preflight's own on its own clock against the time it was built.
/// So no context older than the bound is served live, none from before a stall is served live after it (the resuming mark holds from
/// the turn that ends the stall until a build from that turn's frame or later), and a build that stops while the thread still turns over
/// fails closed by age. The turn after a stall builds at once (<see cref="MarkAlive"/>, <see cref="Due"/>). Everything in the stuck form
/// but its counting items and its headline is as it stood at the context's <c>at</c>, from before the stall: links, windows still
/// "shown", the timer's <c>feeds</c> rows and the <c>serves</c> rows to holders, health — P5 reddens counting items and no more, and
/// <c>at</c> dates the rest.
/// </summary>
public sealed class ServedContext(SnapshotHub hub)
{
    /// <summary>How old a built context may be and still be served live: the preflight's bound.</summary>
    public static TimeSpan MaxAge => PreflightWire.MaxAge;

    private sealed record Pair(string Live, string Stuck, long AtMs);

    private volatile Pair? _pair;
    private volatile bool _resuming;   // written on the show thread
    private bool _due = true;          // show thread

    /// <summary>
    /// The show thread's first act each turn, in place of the hub's own: a turn that ends a stall marks the context resuming; this turn
    /// must build — none built yet, resuming, or the last build older than <see cref="MaxAge"/> — then the hub is marked alive.
    /// </summary>
    public void MarkAlive()
    {
        if (hub.ProducerStuck) _resuming = true;   // before the hub is alive: a reader that sees it alive sees it resuming
        if (_resuming || _pair is not { } p || Old(p, hub.NowMs)) _due = true;
        hub.MarkAlive();
    }

    /// <summary>
    /// Whether this turn builds the context whatever its cadence says (show thread): before the first build, and after a stall until a
    /// build from a frame made after it.
    /// </summary>
    public bool Due => _due;

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
        if (!hub.Resuming) _resuming = false;   // after the pair: a reader that sees it resumed sees the new build
        _due = _resuming;
    }

    /// <summary>
    /// The rule every door a reader asks serves by (any thread, read at serve time): the hub's frame is stale, the context is resuming
    /// after a stall, or the last build is older than <see cref="MaxAge"/>. Before the first build only the hub's word.
    /// </summary>
    public bool Stuck => StuckWith(_pair);

    /// <summary>
    /// The context to serve now (any thread): the stuck form while <see cref="Stuck"/>, else the last one built; null before the first build.
    /// </summary>
    public string? Json() => _pair is not { } p ? null : StuckWith(p) ? p.Stuck : p.Live;

    private bool StuckWith(Pair? p) => hub.FrameStale || _resuming || (p is not null && Old(p, hub.NowMs));

    private static bool Old(Pair p, long nowMs) => nowMs - p.AtMs > (long)MaxAge.TotalMilliseconds;
}
