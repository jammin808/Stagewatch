namespace Stagewatch.Show;

/// <summary>
/// The context every door serves (<c>GET /api/context</c>, MCP's <c>get_context</c>, the context a peer pulls), so that §6 P5 holds on
/// the wire as it does in the builder: the show thread publishes, at each build, the context and the same context built as a stuck show
/// thread's (its timer, schedule and VT countdown red with <see cref="ShowContext.StuckWords"/>) as one pair, and a reader on any thread
/// takes the stuck one while the show thread is stuck. A context is built on the show thread, so a stalled thread builds nothing: what a
/// door would otherwise serve is the last time it drew, never confident while the thread has stopped turning over.
/// </summary>
public sealed class ServedContext
{
    private sealed record Pair(string Live, string Stuck);

    private volatile Pair? _pair;

    /// <summary>Builds and publishes both forms of the context from one input (show thread). An input already stuck is its own stuck form.</summary>
    public void Publish(ContextInput x)
    {
        var live = ShowContext.Build(x);
        _pair = new Pair(live, x.ShowThreadStuck ? live : ShowContext.Build(x with { ShowThreadStuck = true }));
    }

    /// <summary>
    /// The context to serve now (any thread): the stuck form while <paramref name="showThreadStuck"/> (the snapshot hub's
    /// <c>ProducerStuck</c>, read by the caller at serve time), else the last one built; null before the first build.
    /// </summary>
    public string? Json(bool showThreadStuck) => _pair is { } p ? (showThreadStuck ? p.Stuck : p.Live) : null;
}
