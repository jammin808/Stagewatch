using System.Text;
using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>
/// Crash-safe file writes: the new text goes to a temporary file that is flushed to the disk, then swapped in; the
/// previous file is kept as <c>.bak</c> only when it was whole, so a torn file never replaces a good backup. A power
/// cut or a pulled USB stick leaves the old file, the new one, or the backup — never half of one.
/// </summary>
public static class AtomicFile
{
    /// <summary>Write <paramref name="text"/> to <paramref name="path"/>. With <paramref name="isWhole"/>, the old file
    /// is kept as <c>path.bak</c> when it passes. Throws <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>.</summary>
    public static void Write(string path, string text, Func<string, bool>? isWhole = null)
        => Commit(path, new UTF8Encoding(false).GetBytes(text), isWhole is null ? null : () =>
        {
            if (!File.Exists(path)) return;
            try
            {
                var old = File.ReadAllText(path);
                if (old.Length > 0 && old != text && isWhole(old)) File.Copy(path, path + ".bak", overwrite: true);
            }
            catch (IOException) { }   // the backup is a bonus; the write must still happen
        });

    /// <summary>Write <paramref name="bytes"/> to <paramref name="path"/>: the old file or the new one, never half of one, and no
    /// backup. Throws <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/>.</summary>
    public static void Write(string path, byte[] bytes) => Commit(path, bytes, null);

    /// <summary>The bytes go to a temporary file flushed to the disk, <paramref name="beforeSwap"/> runs, then the file is swapped in.</summary>
    private static void Commit(string path, byte[] bytes, Action? beforeSwap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = $"{path}.{Environment.CurrentManagedThreadId}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }
            beforeSwap?.Invoke();
            // Antivirus and backup tools sometimes hold the file for a moment: a few short retries.
            for (var attempt = 0; ; attempt++)
            {
                try { File.Move(tmp, path, overwrite: true); return; }
                catch (IOException) when (attempt < 4) { Pause(attempt); }
                catch (UnauthorizedAccessException) when (attempt < 4) { Pause(attempt); }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void Pause(int attempt) => Task.Delay(25 * (attempt + 1)).GetAwaiter().GetResult();

    /// <summary>A JSON document that parses (the usual "whole" check).</summary>
    public static bool IsJson(string text)
    {
        try { using var _ = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }

    /// <summary>The file's text, or its backup's when the file is missing or fails <paramref name="isWhole"/>. Null when neither.</summary>
    public static string? ReadWithBackup(string path, Func<string, bool> isWhole, out bool fromBackup)
    {
        fromBackup = false;
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var text = File.ReadAllText(candidate);
                if (text.Length == 0 || !isWhole(text)) continue;
                fromBackup = candidate != path;
                return text;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
}

/// <summary>What the watchdog does when the app it watches stops.</summary>
public enum WatchdogAction { Stop, Restart, GiveUp }

/// <summary>A decision, with how long to wait first and why (for the log and the app's next start).</summary>
public readonly record struct WatchdogDecision(WatchdogAction Action, TimeSpan Delay, string Reason);

/// <summary>
/// The watchdog's rules, with no process in sight (tested). The app runs as a child of a small supervisor that restarts
/// it when it crashes or stops answering; a stage timer must come back fast, so the first restart is immediate and the
/// waits grow only if it keeps failing. It gives up when the app fails over and over (a restart loop helps nobody), and
/// a run that lasted a while resets the count.
/// </summary>
public sealed class WatchdogPolicy
{
    /// <summary>Exit code the app uses to ask for a restart (after an update, a licence change…).</summary>
    public const int RestartCode = 82;

    public static readonly TimeSpan[] Backoff = { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) };
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    public const int MaxFailuresInWindow = 6;
    public static readonly TimeSpan GoodRun = TimeSpan.FromMinutes(5);

    /// <summary>No heartbeat for this long while a show is live: the app is hung. Short, because the audience is waiting.</summary>
    public static readonly TimeSpan HangWhileLive = TimeSpan.FromSeconds(10);
    /// <summary>No heartbeat for this long otherwise (a long file dialog or a big import is not a hang).</summary>
    public static readonly TimeSpan HangIdle = TimeSpan.FromSeconds(30);
    /// <summary>Time allowed before the first heartbeat (start-up, licence check, the restore question).</summary>
    public static readonly TimeSpan StartupDeadline = TimeSpan.FromSeconds(90);

    private readonly List<DateTime> _failures = new();
    private int _streak;

    public IReadOnlyList<DateTime> Failures => _failures;

    /// <summary>The app exited (or was killed for hanging) after running for <paramref name="ranFor"/>.</summary>
    public WatchdogDecision OnExit(int exitCode, TimeSpan ranFor, DateTime nowUtc, bool hung = false)
    {
        if (!hung && exitCode == 0) return new(WatchdogAction.Stop, TimeSpan.Zero, "closed normally");
        if (!hung && exitCode == RestartCode) { _streak = 0; return new(WatchdogAction.Restart, TimeSpan.Zero, "restart requested"); }

        if (ranFor >= GoodRun) _streak = 0;
        _failures.RemoveAll(t => nowUtc - t > Window);
        _failures.Add(nowUtc);
        var why = hung ? "stopped answering" : $"exited with code {exitCode}";
        if (_failures.Count > MaxFailuresInWindow)
            return new(WatchdogAction.GiveUp, TimeSpan.Zero, $"{why} — {_failures.Count} failures in {Window.TotalMinutes:0} minutes, giving up");
        var delay = Backoff[Math.Min(_streak, Backoff.Length - 1)];
        _streak++;
        return new(WatchdogAction.Restart, delay, why);
    }

    /// <summary>Whether a child that last beat <paramref name="sinceBeat"/> ago is hung.</summary>
    public static bool IsHung(TimeSpan sinceBeat, bool live, bool hadFirstBeat)
        => hadFirstBeat ? sinceBeat > (live ? HangWhileLive : HangIdle) : sinceBeat > StartupDeadline;
}

/// <summary>Mini-dumps of native crashes (kept beside the settings, newest few).</summary>
public static class CrashDumps
{
    public const int Keep = 3;

    public static string Folder(string dataFolder) => Path.Combine(dataFolder, "crashes");

    /// <summary>The environment the watched app runs with: the smallest useful dump on a native crash.</summary>
    public static IReadOnlyDictionary<string, string> Environment(string dataFolder) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET_DbgEnableMiniDump"] = "1",
        ["DOTNET_DbgMiniDumpType"] = "1",
        ["DOTNET_DbgMiniDumpName"] = Path.Combine(Folder(dataFolder), "stagewatch-%p-%t.dmp"),
        ["DOTNET_CreateDumpDiagnostics"] = "0",
    };

    /// <summary>Delete all but the newest <see cref="Keep"/> dumps.</summary>
    public static void Sweep(string dataFolder)
    {
        try
        {
            var dir = new DirectoryInfo(Folder(dataFolder));
            if (!dir.Exists) return;
            foreach (var old in dir.GetFiles("*.dmp").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Keep)) old.Delete();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// How long a GO takes to reach the screens: from the command arriving to the frame being published, and to the next
/// paint of the outputs. A fixed ring (no allocation once warm), owner thread only.
/// </summary>
public sealed class GoLatency
{
    public const int Size = 60;
    /// <summary>Above this the reading turns amber: a GO the room can feel.</summary>
    public const double AmberMs = 50;

    private readonly double[] _publish = new double[Size];
    private readonly double[] _paint = new double[Size];
    private int _count, _next;

    public int Count => _count;

    public void Add(double publishMs, double paintMs)
    {
        _publish[_next] = publishMs;
        _paint[_next] = paintMs;
        _next = (_next + 1) % Size;
        _count = Math.Min(Size, _count + 1);
    }

    public double Last => _count == 0 ? 0 : _paint[(_next - 1 + Size) % Size];

    /// <summary>The <paramref name="p"/>-th percentile (0–1) of command → paint, or 0 with no readings.</summary>
    public double Percentile(double p, bool paint = true)
    {
        if (_count == 0) return 0;
        var src = paint ? _paint : _publish;
        var copy = new double[_count];
        for (var i = 0; i < _count; i++) copy[i] = src[i];
        Array.Sort(copy);
        return copy[Math.Clamp((int)Math.Ceiling(p * _count) - 1, 0, _count - 1)];
    }

    public string Describe()
        => _count == 0 ? "GO → screen: no GO measured yet"
            : $"GO → screen: last {Last:0} ms, median {Percentile(0.5):0} ms, 95% {Percentile(0.95):0} ms (frame published: median {Percentile(0.5, paint: false):0} ms) over {_count} GOs"
              + (Percentile(0.95) > AmberMs ? " — slower than it should be" : "");
}

/// <summary>
/// Which UI faults are contained (the app carries on) and which end the process (for the watchdog to restart it):
/// fatal runtime faults never; a storm of faults — something is broken, not unlucky — ends it too. Thread-safe.
/// </summary>
public sealed class FaultCounter
{
    public const int StormCount = 10;
    public static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(30);

    private readonly Queue<DateTime> _recent = new();
    private int _total;

    public int Total => Volatile.Read(ref _total);

    public static bool IsFatal(Exception ex) => ex is OutOfMemoryException or StackOverflowException or AccessViolationException
        or InvalidProgramException or System.Runtime.InteropServices.SEHException or BadImageFormatException;

    /// <summary>True when <paramref name="ex"/> should be contained (counted); false when the process should end.</summary>
    public bool Contain(Exception ex, DateTime nowUtc)
    {
        if (IsFatal(ex) || ex.InnerException is { } inner && IsFatal(inner)) return false;
        lock (_recent)
        {
            while (_recent.Count > 0 && nowUtc - _recent.Peek() > StormWindow) _recent.Dequeue();
            _recent.Enqueue(nowUtc);
            Interlocked.Increment(ref _total);
            return _recent.Count < StormCount;
        }
    }
}

/// <summary>
/// A peer's clock offset, NTP-style (Show Bus §5): offset = ((t2 − t1) + (t3 − t4)) / 2, keeping the sample with the
/// smallest round trip of the last 8 (the least delayed is the most honest). Thread-safe.
/// </summary>
public sealed class ClockOffsetEstimator
{
    public const int Window = 8;
    /// <summary>Above this two machines disagree on what "14:30" is: say CLOCKS APART.</summary>
    public static readonly TimeSpan ApartAfter = TimeSpan.FromSeconds(2);

    private readonly Queue<(long Rtt, long Offset)> _samples = new();

    /// <summary>One exchange: our send time t1, their receive t2 and send t3, our receive t4 (Unix ms).</summary>
    public void Add(long t1, long t2, long t3, long t4)
    {
        var rtt = (t4 - t1) - (t3 - t2);
        if (rtt < 0 || rtt > 60_000) return;   // nonsense: ignore
        lock (_samples)
        {
            _samples.Enqueue((rtt, ((t2 - t1) + (t3 - t4)) / 2));
            while (_samples.Count > Window) _samples.Dequeue();
        }
    }

    /// <summary>The peer's clock minus ours, or null before any sample.</summary>
    public TimeSpan? Offset
    {
        get
        {
            lock (_samples)
                return _samples.Count == 0 ? null : TimeSpan.FromMilliseconds(_samples.MinBy(x => x.Rtt).Offset);
        }
    }

    public bool Apart => Offset is { } o && o.Duration() > ApartAfter;
}
