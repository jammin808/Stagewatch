using System.Collections.Concurrent;
using System.Net;

namespace Stagewatch.Remote;

/// <summary>
/// One kind of wrong guess, counted per device (its address), each device in a minute of its own: its first wrong guess starts
/// its minute, and once <see cref="Limit"/> are counted in that minute the device is locked out of that kind until the minute
/// ends. Only the minute's end resets a device, or <see cref="Clear"/>, which a table's owner calls only for a right answer of
/// the same kind, never another's. Times are milliseconds on one monotonic clock, passed in, so a test drives the minute.
/// Thread-safe. A device whose minute has ended is as good as absent: once more than <see cref="SweepAbove"/> devices are held,
/// such devices are dropped, at most once a minute, so the table holds about the devices that guessed in the last two minutes
/// and no outcome changes.
/// </summary>
public sealed class WrongGuesses
{
    /// <summary>How many devices are held before those whose minute has ended are dropped.</summary>
    public const int SweepAbove = 4096;

    private readonly ConcurrentDictionary<IPAddress, (int Count, long StartMs)> _devices = new();
    private long _nextSweepMs = long.MinValue;

    public WrongGuesses(int limit, long minuteMs = 60_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minuteMs, 1L);
        Limit = limit;
        MinuteMs = minuteMs;
    }

    /// <summary>Wrong guesses in a device's minute that lock it out for the rest of that minute.</summary>
    public int Limit { get; }

    /// <summary>The length of a device's minute, in ms.</summary>
    public long MinuteMs { get; }

    /// <summary>How many devices the table holds now, their minute ended or not.</summary>
    public int Held => _devices.Count;

    /// <summary>The device's wrong guesses counted in its minute at <paramref name="nowMs"/>; 0 once that minute has ended.</summary>
    public int Of(IPAddress device, long nowMs)
        => _devices.TryGetValue(device, out var d) && nowMs - d.StartMs < MinuteMs ? d.Count : 0;

    /// <summary>Whether the device is locked out at <paramref name="nowMs"/>: <see cref="Limit"/> wrong guesses in its minute.</summary>
    public bool Locked(IPAddress device, long nowMs) => Of(device, nowMs) >= Limit;

    /// <summary>One wrong guess from the device: the first after its minute ended starts a new minute. The count stops at the limit.</summary>
    public void Count(IPAddress device, long nowMs)
    {
        _devices.AddOrUpdate(device,
            _ => (1, nowMs),
            (_, d) => nowMs - d.StartMs >= MinuteMs ? (1, nowMs) : (Math.Min(d.Count + 1, Limit), d.StartMs));
        if (_devices.Count > SweepAbove && nowMs >= Interlocked.Read(ref _nextSweepMs))
        {
            Interlocked.Exchange(ref _nextSweepMs, nowMs + MinuteMs);
            foreach (var (held, d) in _devices)
                if (nowMs - d.StartMs >= MinuteMs) _devices.TryRemove(new KeyValuePair<IPAddress, (int, long)>(held, d));
        }
    }

    /// <summary>Forget the device's count: only for a right answer of this table's own kind.</summary>
    public void Clear(IPAddress device) => _devices.TryRemove(device, out _);
}
