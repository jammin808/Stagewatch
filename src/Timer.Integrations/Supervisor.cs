namespace Stagewatch.Integrations;

/// <summary>
/// Starts, restarts and stops the integrations (sensors and outputs) as their settings change, without the show loop
/// ever waiting on one. Owner thread only: every call, and every callback, happens on the thread that owns the show.
/// </summary>
/// <remarks>
/// Stopping an integration can take a while (worker threads, devices, sockets closing), so the old instance is disposed
/// on a background task and its replacement starts only once the old one has let go of its port or device. Settings
/// that change again meanwhile are picked up when the retirement finishes: <c>reapply</c> is posted back to the owner
/// thread, which runs the reconcile pass again.
/// </remarks>
public sealed class IntegrationSupervisor
{
    private readonly Action<Action> _post;
    private readonly Action _reapply;
    private readonly Action<string> _report;
    private readonly Dictionary<string, string> _signatures = new();
    private readonly Dictionary<string, (IDisposable Instance, Action Clear)> _running = new();
    private readonly HashSet<string> _retiring = new();
    private bool _stopped;

    /// <param name="post">Queues work onto the owner thread (the dispatcher).</param>
    /// <param name="reapply">Runs the reconcile pass again (on the owner thread) after an integration has retired.</param>
    /// <param name="report">Tells the operator an integration couldn't start.</param>
    public IntegrationSupervisor(Action<Action> post, Action reapply, Action<string> report)
    {
        _post = post;
        _reapply = reapply;
        _report = report;
    }

    /// <summary>Names of integrations still letting go of their devices.</summary>
    public IReadOnlyCollection<string> Retiring => _retiring;

    /// <summary>
    /// Bring one integration in line with its settings. <paramref name="signature"/> captures everything it was started
    /// with; its first field is "True" when it should be running. <paramref name="set"/> mirrors the live instance (or
    /// null) to its owner.
    /// </summary>
    public void Reconcile<T>(string name, string signature, Action<T?> set, Func<T> create) where T : class, IDisposable
    {
        if (_stopped) return;
        var on = signature.StartsWith("True", StringComparison.Ordinal);
        if (_retiring.Contains(name)) { _signatures[name] = signature; return; }
        var running = _running.ContainsKey(name);
        if (_signatures.TryGetValue(name, out var old) && old == signature && running == on) return;
        _signatures[name] = signature;
        if (running)
        {
            Retire(name);
            return;
        }
        if (!on) return;
        try
        {
            var instance = create();
            _running[name] = (instance, () => set(null));
            set(instance);
        }
        catch (Exception ex) { _report($"{name}: couldn't start — {ex.Message}"); }
    }

    private void Retire(string name)
    {
        var (instance, clear) = _running[name];
        _running.Remove(name);
        clear();
        _retiring.Add(name);
        Task.Run(() => { try { instance.Dispose(); } catch (Exception) { } })
            .ContinueWith(_ => _post(() =>
            {
                _retiring.Remove(name);
                if (!_stopped) _reapply();
            }), TaskScheduler.Default);
    }

    /// <summary>
    /// Stop everything in parallel, waiting at most <paramref name="cap"/>: closing the app never waits on one slow
    /// device for long. <paramref name="others"/> are stopped alongside (integrations the owner runs itself).
    /// </summary>
    public void StopAll(TimeSpan cap, params IDisposable?[] others)
    {
        _stopped = true;
        var all = _running.Values.ToList();
        _running.Clear();
        foreach (var (_, clear) in all) clear();
        var stopping = all.Select(r => r.Instance).Concat(others.Where(o => o is not null).Select(o => o!))
            .Select(d => Task.Run(() => { try { d.Dispose(); } catch (Exception) { } }))
            .ToArray();
        try { Task.WaitAll(stopping, cap); } catch (AggregateException) { }
    }
}
