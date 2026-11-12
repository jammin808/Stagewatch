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

    /// <summary>How many whole seconds, rounded up, the device stays locked out at <paramref name="nowMs"/>; 0 when it is not.</summary>
    public int LockedFor(IPAddress device, long nowMs)
        => _devices.TryGetValue(device, out var d) && nowMs - d.StartMs < MinuteMs && d.Count >= Limit
            ? (int)Math.Max(1, (MinuteMs - (nowMs - d.StartMs) + 999) / 1000) : 0;

    /// <summary>
    /// The table as it stands at <paramref name="nowMs"/>: every device whose minute is still open, with its count and how long
    /// ago that minute began, the oldest minute first (then by address, so one moment always reads the same). A device whose
    /// minute has ended is absent, as it is to <see cref="Of"/>. A copy: reading it changes nothing, and it holds no secret.
    /// </summary>
    public IReadOnlyList<(IPAddress Device, int Count, long AgeMs)> Open(long nowMs)
        => _devices.Where(d => nowMs - d.Value.StartMs < MinuteMs)
            .Select(d => (Device: d.Key, d.Value.Count, AgeMs: Math.Max(0, nowMs - d.Value.StartMs)))
            .OrderByDescending(d => d.AgeMs).ThenBy(d => d.Device.ToString(), StringComparer.Ordinal)
            .ToList();

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

/// <summary>
/// One device in a lockout table as it stands: its address, its wrong guesses in its open one-minute window, and how long ago
/// that window began (ms). No secret: never the guess itself.
/// </summary>
public readonly record struct AuthFailureCount(string Address, int Failures, long WindowAgeMs);

/// <summary>
/// Wrong PeerLink proofs (Show Bus 13.5.6), counted in a table of their own per (address, kid), each pair in a minute of its own: its
/// first wrong proof starts its minute, and once <see cref="Limit"/> are counted in it the pair is locked — refused before its proof is
/// checked — until the minute ends. Only a kid of a key in force is ever counted (an unknown kid and a binding refusal never are), and
/// nothing here locks out a PIN, a viewing code, a view pass, another key or a browser. Only the minute's end frees a pair: a right
/// proof vouches for itself alone, never for whoever else proves that kid from its address. A proof about to be checked holds a place
/// (<see cref="Reserve"/>) until it is settled, so proofs checked together never pass the limit between them: wrong proofs counted
/// and proofs in flight together never exceed <see cref="Limit"/> in a minute. Times are milliseconds on one monotonic clock, passed in.
/// Thread-safe; past <see cref="WrongGuesses.SweepAbove"/> pairs, those whose minute has ended and that hold no proof in flight are
/// dropped, at most once a minute, and no outcome changes.
/// </summary>
public sealed class WrongProofs
{
    private readonly ConcurrentDictionary<(IPAddress Device, string Kid), (int Count, int Pending, long StartMs)> _pairs = new();
    private long _nextSweepMs = long.MinValue;

    public WrongProofs(int limit, long minuteMs = 60_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(minuteMs, 1L);
        Limit = limit;
        MinuteMs = minuteMs;
    }

    /// <summary>Wrong proofs in a pair's minute that lock it for the rest of that minute.</summary>
    public int Limit { get; }

    /// <summary>The length of a pair's minute, in ms.</summary>
    public long MinuteMs { get; }

    /// <summary>The pair's wrong proofs counted in its minute at <paramref name="nowMs"/>; 0 once that minute has ended.</summary>
    public int Of(IPAddress device, string kid, long nowMs)
        => _pairs.TryGetValue((device, kid), out var p) ? Counted(p, nowMs) : 0;

    /// <summary>How many whole seconds, rounded up, the pair stays locked at <paramref name="nowMs"/>; 0 when it is not locked.</summary>
    public int LockedFor(IPAddress device, string kid, long nowMs)
        => _pairs.TryGetValue((device, kid), out var p) && Counted(p, nowMs) >= Limit ? Left(p, nowMs) : 0;

    /// <summary>
    /// A place for one proof about to be checked: 0 when it is taken (the caller then settles it once, <see cref="Settle"/>), otherwise
    /// the seconds, at least 1, until the pair's minute ends — its wrong proofs and the proofs in flight already reach the limit, so this
    /// one is refused unchecked.
    /// </summary>
    public int Reserve(IPAddress device, string kid, long nowMs)
    {
        var key = (device, kid);
        while (true)
        {
            if (!_pairs.TryGetValue(key, out var p))
            {
                if (_pairs.TryAdd(key, (0, 1, nowMs))) return 0;
                continue;
            }
            var counted = Counted(p, nowMs);
            if (counted + p.Pending >= Limit) return Left(p, nowMs);
            var next = counted > 0 ? (p.Count, p.Pending + 1, p.StartMs) : (0, p.Pending + 1, nowMs);
            if (_pairs.TryUpdate(key, next, p)) return 0;
        }
    }

    /// <summary>
    /// A reserved proof checked: <paramref name="wrong"/> counts it as a wrong proof (the first in a new minute starts that minute); a
    /// right one only gives its place back and clears nothing.
    /// </summary>
    public void Settle(IPAddress device, string kid, long nowMs, bool wrong)
    {
        var key = (device, kid);
        while (true)
        {
            if (!_pairs.TryGetValue(key, out var p)) return;
            var pending = Math.Max(0, p.Pending - 1);
            var next = wrong ? Counting(p, nowMs) with { Pending = pending } : p with { Pending = pending };
            var done = next.Count == 0 && next.Pending == 0
                ? _pairs.TryRemove(new KeyValuePair<(IPAddress, string), (int, int, long)>(key, p))
                : _pairs.TryUpdate(key, next, p);
            if (done) break;
        }
        Sweep(nowMs);
    }

    /// <summary>One wrong proof for the pair, held by no place: the first after its minute ended starts a new minute. The count stops at the limit.</summary>
    public void Count(IPAddress device, string kid, long nowMs)
    {
        _pairs.AddOrUpdate((device, kid), _ => (1, 0, nowMs), (_, p) => Counting(p, nowMs));
        Sweep(nowMs);
    }

    /// <summary>
    /// Every pair with wrong proofs in a minute still open at <paramref name="nowMs"/>: its address, its kid (not a secret), its count and
    /// how long ago its minute began, the oldest first. A copy that holds no secret; a place in flight alone is not listed.
    /// </summary>
    public IReadOnlyList<(IPAddress Device, string Kid, int Count, long AgeMs)> Open(long nowMs)
        => _pairs.Where(p => Counted(p.Value, nowMs) > 0)
            .Select(p => (p.Key.Device, p.Key.Kid, p.Value.Count, AgeMs: Math.Max(0, nowMs - p.Value.StartMs)))
            .OrderByDescending(p => p.AgeMs).ThenBy(p => p.Device.ToString(), StringComparer.Ordinal).ThenBy(p => p.Kid, StringComparer.Ordinal)
            .ToList();

    private int Counted((int Count, int Pending, long StartMs) p, long nowMs) => nowMs - p.StartMs < MinuteMs ? p.Count : 0;

    private int Left((int Count, int Pending, long StartMs) p, long nowMs)
        => (int)Math.Max(1, (MinuteMs - Math.Clamp(nowMs - p.StartMs, 0, MinuteMs) + 999) / 1000);

    private (int Count, int Pending, long StartMs) Counting((int Count, int Pending, long StartMs) p, long nowMs)
        => Counted(p, nowMs) > 0 ? (Math.Min(p.Count + 1, Limit), p.Pending, p.StartMs) : (1, p.Pending, nowMs);

    private void Sweep(long nowMs)
    {
        if (_pairs.Count <= WrongGuesses.SweepAbove || nowMs < Interlocked.Read(ref _nextSweepMs)) return;
        Interlocked.Exchange(ref _nextSweepMs, nowMs + MinuteMs);
        foreach (var (held, p) in _pairs)
            if (p.Pending == 0 && Counted(p, nowMs) == 0) _pairs.TryRemove(new KeyValuePair<(IPAddress, string), (int, int, long)>(held, p));
    }
}

/// <summary>
/// One (device, kid) pair in the wrong-proof table as it stands (Show Bus 13.5.6): its address, the kid it named (not a secret), its wrong
/// proofs in its open one-minute window and how long ago that window began (ms). Never a proof, a key or K.
/// </summary>
public readonly record struct ProofFailureCount(string Address, string Kid, int Count, long WindowAgeMs);
