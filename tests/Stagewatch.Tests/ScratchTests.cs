using System.Text.RegularExpressions;

/// <summary>
/// The fence on the suite's temporary folders and files (<see cref="Scratch"/>): it counts an entry of this run's own that its
/// maker left (a folder made and never disposed is the mutation it must see), and never an earlier run's, another run's or one
/// with no tag; a disposed folder or file goes with its siblings; a removal a process holds is retried, then said; and the
/// suite, the browser workflows and the interop lane name the temp folder in one place each and the code they run names it in
/// none, so no maker sits outside them.
/// </summary>
static class ScratchTests
{
    public static void Run(Action<bool, string> Check)
    {
        Classified(Check);
        LeftOneBehind(Check);
        Retried(Check);
        HeldOnWindows(Check);
        OneMaker(Check);
    }

    /// <summary>The split the fence counts by, on names alone.</summary>
    static void Classified(Action<bool, string> Check)
    {
        const string run = "0123abcd";
        var atStart = new HashSet<string>(StringComparer.Ordinal)
        {
            "sw-panels-001c58a2.json.bak",                         // an earlier run's, before the tag
            "sw-hist-0123abcd-aaaaaaaaaaaa.json",                  // an earlier run's that drew the same tag: there before this one began
            "sw-settings-89abcdef-bbbbbbbbbbbb",                   // an earlier tagged run's
        };
        var atEnd = atStart.Concat(new[]
        {
            "sw-settings-0123abcd-cccccccccccc",                   // this run's folder, left
            "sw-panels-0123abcd-dddddddddddd.json.bak",            // this run's file's sibling, left
            "sw-holding-test-0123abcd-eeeeeeeeeeee",               // a kind of several words
            "sw-reh-89abcdef-ffffffffffff.json",                   // another run's, made while this one ran
            "sw-rig-AbC123",                                       // the browser workflows' rig folder (another process)
            "sw-0123abcd-cccccccccccc",                            // no kind: not a name the suite makes
        }).ToList();
        var (ours, others) = Scratch.Since(atStart, atEnd, run);
        Check(ours.SequenceEqual(new[] { "sw-holding-test-0123abcd-eeeeeeeeeeee", "sw-panels-0123abcd-dddddddddddd.json.bak", "sw-settings-0123abcd-cccccccccccc" }),
            "temp fence: an entry the run made and left is counted, a sibling and a kind of several words with it (" + string.Join(", ", ours) + ")");
        Check(others.SequenceEqual(new[] { "sw-0123abcd-cccccccccccc", "sw-reh-89abcdef-ffffffffffff.json", "sw-rig-AbC123" }),
            "temp fence: what came during the run under another run's tag or none is never counted (" + string.Join(", ", others) + ")");
        Check(Scratch.Since(atStart, atStart, run) is { Ours.Count: 0, Others.Count: 0 }, "temp fence: what was there at the start, an earlier run's, is never counted");
    }

    /// <summary>The mutation the fence must see: a folder made and not disposed, then the same folder disposed.</summary>
    static void LeftOneBehind(Action<bool, string> Check)
    {
        var atStart = Scratch.Listing();
        var left = Scratch.Folder("fence");
        File.WriteAllText(Path.Combine(left.Path, "inside.json"), "{}");
        var name = Path.GetFileName(left.Path);
        Check(Scratch.Since(atStart, Scratch.Listing(), Scratch.Run).Ours.SequenceEqual(new[] { name }),
            "temp fence: a folder made and never disposed is counted as this run's leftover (" + name + ")");
        left.Dispose();
        Check(!Directory.Exists(left.Path) && Scratch.Since(atStart, Scratch.Listing(), Scratch.Run).Ours.Count == 0,
            "temp fence: the folder disposed is gone with what it held, and nothing of this run's is counted");

        var file = Scratch.File("fence", ".json");
        File.WriteAllText(file.Path, "{}");
        File.WriteAllText(file.Path + ".bak", "{}");
        File.WriteAllText(file.Path + "2", "{}");
        Check(Scratch.Since(atStart, Scratch.Listing(), Scratch.Run).Ours.Count == 3, "temp fence: a file and the siblings written beside it are each counted");
        file.Dispose();
        Check(!File.Exists(file.Path) && !File.Exists(file.Path + ".bak") && !File.Exists(file.Path + "2")
              && Scratch.Since(atStart, Scratch.Listing(), Scratch.Run).Ours.Count == 0,
            "temp fence: a file disposed goes with every sibling named after it (.bak, a second copy)");

        var unmade = Scratch.Folder("fence", make: false);
        Check(!Directory.Exists(unmade.Path), "temp fence: a folder for code that makes its own is named, not made");
        unmade.Dispose();
        Check(!Directory.Exists(unmade.Path), "temp fence: disposing a folder never made is quiet");
    }

    /// <summary>The retry, on an injected removal and wait: a refusal that clears lands; one that stays is said after the last try.</summary>
    static void Retried(Action<bool, string> Check)
    {
        var waits = new List<TimeSpan>();
        var tries = 0;
        var landed = Scratch.Retry(() => { if (++tries < 3) throw new IOException("in use by another process"); }, waits.Add);
        Check(landed is null && tries == 3 && waits.SequenceEqual(new[] { Scratch.Pause, Scratch.Pause * 2 }),
            $"temp fence: a removal refused twice is retried, each wait longer, and lands ({tries} tries, waits {string.Join("/", waits.Select(w => w.TotalMilliseconds))} ms)");

        waits.Clear();
        tries = 0;
        var said = Scratch.Retry(() => { tries++; throw new UnauthorizedAccessException("held"); }, waits.Add);
        Check(said == $"UnauthorizedAccessException: held (tried {Scratch.Attempts} times)" && tries == Scratch.Attempts && waits.Count == Scratch.Attempts - 1,
            $"temp fence: a removal that stays refused is tried {Scratch.Attempts} times and then said, never passed over ({said})");

        var other = Thrown(() => Scratch.Retry(() => throw new InvalidOperationException("a bug"), waits.Add));
        Check(other is InvalidOperationException, "temp fence: only a removal's refusals (IO, access) are retried; anything else is not swallowed");
    }

    static Exception? Thrown(Action act)
    {
        try
        {
            act();
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e;
        }
    }

    /// <summary>On Windows a file another handle holds open cannot go: it is retried and said, and goes once let go.</summary>
    static void HeldOnWindows(Action<bool, string> Check)
    {
        if (!Bench.OnWindows("temp fence: a file another handle holds is retried, said, and removed once let go (Windows file sharing)")) return;
        using var folder = Scratch.Folder("fence-held");
        var file = Path.Combine(folder.Path, "held.json");
        string? said;
        using (new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            said = Scratch.Remove(folder.Path);
        Check(said is not null && said.EndsWith($"(tried {Scratch.Attempts} times)", StringComparison.Ordinal) && Directory.Exists(folder.Path),
            "temp fence: a folder holding a file open elsewhere stands, and why is said (" + said + ")");
        Check(Scratch.Remove(folder.Path) is null && !Directory.Exists(folder.Path), "temp fence: once let go, it goes");
    }

    /// <summary>
    /// One maker of temporary entries per lane: the suite's in Scratch, the browser workflows' in tests/e2e/temp.cjs, none in the
    /// interop lane; and none in the code the suite and the lanes run (src/, the rig, the scripted peer and the copies script), which
    /// writes only at the paths it is given. An entry made outside these makers carries no run tag, so the end-of-run listing could
    /// only name it as another's and never fail on it: this fence is what keeps such a maker from landing.
    /// </summary>
    static void OneMaker(Action<bool, string> Check)
    {
        var root = Tree();
        Check(root is not null, "temp fence: the tree is found above the suite");
        if (root is null) return;

        // The runtime asked for the temp folder, or for its variables; in the suite, a literal /tmp too. Each alternative is written so
        // this file's own source never matches it.
        const string runtime = @"GetTempPath\(|GetTempFileName\(|CreateTempSubdirectory\(|GetEnvironmentVariable\(\s*""(?:TMPDIR|TMP|TEMP)""";
        var cs = new Regex(runtime, RegexOptions.CultureInvariant);
        var csSuite = new Regex(runtime + @"|[""]/tmp\b", RegexOptions.CultureInvariant);
        var suite = Sources(root, Path.Combine("tests", "Stagewatch.Tests"), "*.cs")
            .Where(f => Path.GetFileName(f) != "Scratch.cs" && csSuite.IsMatch(File.ReadAllText(f))).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).ToList();
        Check(suite.Count == 0, "temp fence: no file of the suite but Scratch.cs names the temp folder, its variables or /tmp (" + string.Join(", ", suite) + ")");

        var js = new Regex(@"mkdtemp|tmpdir\(|TMPDIR|mktemp", RegexOptions.CultureInvariant);
        var e2e = Path.Combine(root, "tests", "e2e");
        var scripts = Sources(root, Path.Combine("tests", "e2e"), "*.cjs").Concat(Sources(root, Path.Combine("tests", "interop"), "*.cjs")).ToList();
        var maker = Path.Combine(e2e, "temp.cjs");
        // The interop lane gives each app a TMPDIR of the run's own folder (proc.cjs, run.cjs) and makes none in the temp folder.
        var givers = new[] { Path.Combine(root, "tests", "interop", "lib", "proc.cjs"), Path.Combine(root, "tests", "interop", "lib", "run.cjs") };
        var makes = new Regex(@"mkdtemp|tmpdir\(|mktemp", RegexOptions.CultureInvariant);
        var outside = scripts.Where(f => f != maker && (givers.Contains(f) ? makes : js).IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Check(scripts.Contains(maker) && js.IsMatch(File.ReadAllText(maker)) && outside.Count == 0,
            "temp fence: the browser workflows make temporary folders through tests/e2e/temp.cjs alone, the interop lane none (" + string.Join(", ", outside) + ")");

        // What the suite and the lanes run: the product, the rig, the scripted peer (PeerLinkScenarios) and the copies script.
        var run = Sources(root, "src", "*.cs").Where(f => cs.IsMatch(File.ReadAllText(f)))
            .Concat(Sources(root, Path.Combine("tools", "Stagewatch.Rig"), "*.cs").Where(f => cs.IsMatch(File.ReadAllText(f))))
            .Concat(Sources(root, Path.Combine("tools", "showbus"), "*.mjs").Where(f => js.IsMatch(File.ReadAllText(f))))
            .Concat(new[] { Path.Combine(root, "tools", "showbus-copies.sh") }.Where(f => File.Exists(f) && js.IsMatch(File.ReadAllText(f))))
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Check(run.Count == 0, "temp fence: the code the suite and the lanes run (src/, the rig, the scripted peer, the copies script) names no temp "
                              + "folder of its own: it writes only at the paths it is given (" + string.Join(", ", run) + ")");
    }

    /// <summary>The files under <paramref name="folder"/> (relative to the tree) matching <paramref name="pattern"/>, at any depth, but none a build or a package manager put there.</summary>
    static IEnumerable<string> Sources(string root, string folder, string pattern)
    {
        var start = Path.Combine(root, folder);
        if (!Directory.Exists(start)) return [];
        return Directory.EnumerateFiles(start, pattern, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(start, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules"))
            .Order(StringComparer.Ordinal);
    }

    static string? Tree()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Timer.slnf")) && Directory.Exists(Path.Combine(dir.FullName, "tests", "e2e")))
                return dir.FullName;
        return null;
    }
}
