namespace Stagewatch.Integrations;

/// <summary>
/// A latest-wins mailbox: writers replace, readers take whatever is newest. No locks, no queue —
/// the show loop reads it once per frame like a game reads input state.
/// </summary>
public sealed class Latest<T> where T : class
{
    private T? _value;
    public T? Value => Volatile.Read(ref _value);
    public void Set(T? value) => Volatile.Write(ref _value, value);
}

/// <summary>Health of a sensor or output, shown in Settings → Integrations.</summary>
public enum LinkHealth { Off, Starting, Ok, NotFound, Error }

public interface IIntegration : IDisposable
{
    string Name { get; }
    LinkHealth Health { get; }
    string Status { get; }
}

/// <summary>
/// A sensor polled on its own background thread at a fixed rate. Exceptions are contained and
/// reported as status; the thread backs off when the source is missing so an absent PowerPoint or
/// vMix costs almost nothing.
/// </summary>
public abstract class PollingSensor<T> : IIntegration where T : class
{
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private readonly Latest<T> _latest = new();
    private volatile LinkHealth _health = LinkHealth.Starting;
    private volatile string _status = "starting";

    protected PollingSensor(string name, TimeSpan interval, ApartmentState apartment = ApartmentState.MTA)
    {
        Name = name;
        Interval = interval;
        _thread = new Thread(Run) { IsBackground = true, Name = "Stagewatch " + name, Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(apartment);
    }

    public string Name { get; }
    public TimeSpan Interval { get; }
    public LinkHealth Health => _health;
    public string Status => _status;
    public T? Value => _latest.Value;

    /// <summary>When the source is missing, poll this much less often.</summary>
    protected virtual TimeSpan IdleInterval => TimeSpan.FromSeconds(2);

    public void Start() => _thread.Start();

    /// <summary>Read the source once. Return null when there is nothing (source closed / not running).</summary>
    protected abstract T? Poll(CancellationToken ct);

    protected void Report(LinkHealth health, string status)
    {
        _health = health;
        _status = status;
    }

    private void Run()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            var wait = Interval;
            try
            {
                var v = Poll(ct);
                _latest.Set(v);
                if (v is null) wait = IdleInterval;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _latest.Set(null);
                Report(LinkHealth.Error, ex.Message);
                wait = IdleInterval;
            }
            if (ct.WaitHandle.WaitOne(wait)) break;
        }
        OnStopped();
    }

    protected virtual void OnStopped() { }

    public virtual void Dispose()
    {
        _cts.Cancel();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(TimeSpan.FromSeconds(2));
        _health = LinkHealth.Off;
        _status = "off";
    }
}
