using System.Text.RegularExpressions;

/// <summary>
/// The suite's temporary folders and files, and the fence that holds a run to removing them. Each is named in
/// <see cref="Root"/> as sw-&lt;kind&gt;-&lt;run&gt;-&lt;unique&gt;, &lt;run&gt; being this run's tag, and each goes when its owner is
/// disposed, with every entry beside it whose name begins with its own (the .tmp, .bak and second copies the stores write next
/// to a file). A removal a process still holds is retried, each wait longer than the last; what still stands is tried again at
/// the run's end and said in the summary, never passed over. <see cref="Begin"/> lists the temp folder's sw-* entries as the
/// run starts and <see cref="End"/> lists them again: an entry that was not there at the start and carries this run's tag is one
/// the run made and did not remove, and fails the run (it then goes, being the run's own). An entry there at the start (an
/// earlier run's) or one carrying another run's tag or none is never counted and never touched.
/// </summary>
static class Scratch
{
    /// <summary>What every name the suite makes in the temp folder starts with, and what the fence lists.</summary>
    public const string Prefix = "sw-";

    /// <summary>How many times a removal is tried before it is said; the waits between are <see cref="Pause"/>, twice it, and so on.</summary>
    public const int Attempts = 5;

    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(50);

    /// <summary>This run's tag: in the name of everything the suite makes in the temp folder, and in no other run's.</summary>
    public static readonly string Run = Guid.NewGuid().ToString("N")[..8];

    /// <summary>A name the suite makes: sw-&lt;kind&gt;-&lt;run&gt;-&lt;unique&gt;, then anything (an extension, a sibling's suffix).</summary>
    static readonly Regex Tagged = new(@"^sw-[a-z]+(?:-[a-z]+)*-(?<run>[0-9a-f]{8})-[0-9a-f]{12}", RegexOptions.CultureInvariant);

    static readonly Regex Kind = new("^[a-z]+(?:-[a-z]+)*$", RegexOptions.CultureInvariant);

    static readonly object Gate = new();
    static HashSet<string>? _atStart;
    static int _named;
    static readonly List<string> Held = [];

    /// <summary>The temp folder, read when used: TMPDIR off Windows, TMP or TEMP on it.</summary>
    public static string Root => Path.GetTempPath();

    /// <summary>The fence's first listing: the sw-* entries there before the run made anything.</summary>
    public static void Begin()
    {
        var listing = Listing();
        lock (Gate) _atStart = listing;
    }

    /// <summary>A folder of the run's own, made unless <paramref name="make"/> is false (for code that makes its folder itself).</summary>
    public static ScratchFolder Folder(string kind, bool make = true)
    {
        var path = Path.Combine(Root, Name(kind));
        if (make) Directory.CreateDirectory(path);
        return new ScratchFolder(path);
    }

    /// <summary>A file's path of the run's own (not made: the code under test writes it).</summary>
    public static ScratchFile File(string kind, string extension) => new(Path.Combine(Root, Name(kind) + extension));

    static string Name(string kind)
    {
        if (!Kind.IsMatch(kind)) throw new ArgumentException("a kind is lowercase words joined by '-': \"" + kind + "\"", nameof(kind));
        Interlocked.Increment(ref _named);
        return Prefix + kind + "-" + Run + "-" + Guid.NewGuid().ToString("N")[..12];
    }

    /// <summary>What a disposed owner calls: the entry goes now, or is tried again and said at the run's end.</summary>
    internal static void Release(string path)
    {
        if (Remove(path) is null) return;
        lock (Gate) Held.Add(path);
    }

    /// <summary>
    /// Remove <paramref name="path"/> and every entry beside it whose name begins with its own, a folder with all it holds: null
    /// once none is left, else why one still stands.
    /// </summary>
    public static string? Remove(string path) => Retry(() => Delete(path), Thread.Sleep);

    static void Delete(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null || !Directory.Exists(parent)) return;
        foreach (var entry in Directory.GetFileSystemEntries(parent, Path.GetFileName(path) + "*"))
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
            else System.IO.File.Delete(entry);
        }
    }

    /// <summary>
    /// <paramref name="delete"/> tried up to <see cref="Attempts"/> times, <paramref name="wait"/> called between with
    /// <see cref="Pause"/>, then twice it, and so on: null once it lands, else the last refusal's words.
    /// </summary>
    public static string? Retry(Action delete, Action<TimeSpan> wait)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                delete();
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts) return $"{e.GetType().Name}: {e.Message} (tried {Attempts} times)";
                wait(Pause * attempt);
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="landed"/> comes true within <paramref name="limit"/>, read every <see cref="Pause"/>: for a store that
    /// saves on a worker, whose file must be written before its owner removes it, or the write lands after the removal.
    /// </summary>
    public static bool Until(Func<bool> landed, TimeSpan limit)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!landed())
        {
            if (watch.Elapsed >= limit) return false;
            Thread.Sleep(Pause);
        }
        return true;
    }

    /// <summary>The sw-* entries in the temp folder now, by name.</summary>
    public static HashSet<string> Listing()
    {
        var root = Root;
        if (!Directory.Exists(root)) return new(StringComparer.Ordinal);
        return Directory.EnumerateFileSystemEntries(root, Prefix + "*").Select(e => Path.GetFileName(e)).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The entries at the end that were not there at the start, split by maker: <c>Ours</c> carry <paramref name="run"/>'s tag;
    /// <c>Others</c> carry another run's or none (another run's, or a maker outside this class). Each in ordinal order.
    /// </summary>
    public static (List<string> Ours, List<string> Others) Since(IReadOnlySet<string> atStart, IEnumerable<string> atEnd, string run)
    {
        var fresh = atEnd.Where(n => !atStart.Contains(n)).Order(StringComparer.Ordinal).ToList();
        bool Mine(string n) => Tagged.Match(n) is { Success: true } m && m.Groups["run"].Value == run;
        return (fresh.Where(Mine).ToList(), fresh.Where(n => !Mine(n)).ToList());
    }

    /// <summary>
    /// The fence, as the run ends: what a process held is tried once more, the temp folder is listed again, and an entry of this
    /// run's own still there fails the run through <paramref name="Check"/> and then goes. Returns the summary's lines.
    /// </summary>
    public static IReadOnlyList<string> End(Action<bool, string> Check)
    {
        List<string> held;
        HashSet<string> atStart;
        lock (Gate)
        {
            held = [.. Held];
            Held.Clear();
            atStart = _atStart ?? throw new InvalidOperationException("Scratch.End before Scratch.Begin: the fence has no first listing");
        }
        var lines = new List<string>();
        var stillHeld = new List<string>();
        foreach (var path in held)
        {
            if (Remove(path) is not { } why) continue;
            stillHeld.Add(Path.GetFileName(path));
            lines.Add($"temp: {Path.GetFileName(path)} could not be removed when its owner was done, nor at the run's end: {why}");
        }
        var (ours, others) = Since(atStart, Listing(), Run);
        Check(ours.Count == 0, $"temp: the run leaves none of the folders and files it made in {Root} behind ({ours.Count} left: {string.Join(", ", ours)})");
        lines.Insert(0, $"temp: {_named} folders and files named in {Root} under this run's tag {Run}; {ours.Count} left behind"
                        + (ours.Count == 0 ? "" : $" ({stillHeld.Count} of them held by a process): " + string.Join(", ", ours)));
        foreach (var name in ours)
            if (Remove(Path.Combine(Root, name)) is { } why) lines.Add($"temp: {name}, counted above, could not be removed after the count either: {why}");
        if (others.Count > 0)
            lines.Add($"temp: {others.Count} sw-* entries came during the run that are not this run's (another run's tag, or none), "
                      + "neither counted nor touched: " + string.Join(", ", others));
        return lines;
    }
}

/// <summary>A folder of the run's own in the temp folder: gone, with all it holds, when disposed.</summary>
sealed class ScratchFolder(string path) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose() => Scratch.Release(Path);
}

/// <summary>A file's path of the run's own in the temp folder: gone, with every sibling named after it, when disposed.</summary>
sealed class ScratchFile(string path) : IDisposable
{
    public string Path { get; } = path;

    public void Dispose() => Scratch.Release(Path);
}
