using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// The interop lane's papers held to the lane. docs/BUILD.md's section on the lane lists exactly the rows its drivers run, each
/// on the topologies whose driver runs it (a row a driver adds or drops fails here until the table says so: a driver's rows are
/// its top-level `test(…, row({ id: '…', … }))` calls, one id each, and a driver that skips, marks as to do or singles out a
/// test is refused), names every topology a driver runs, and names every variable the lane's scripts and drivers read.
/// tools/test.sh's header is its help: it names every lane the script runs — the same lanes its refusal of an unknown lane
/// lists — its default lanes as the script sets them, and every variable a person sets for the lane (every one the lane reads
/// but those its own shell scripts set for the processes they start); and off Windows, `tools/test.sh help` (or -h, --help,
/// anywhere among the lanes) prints that header and runs nothing, and an unknown lane is refused with its words.
/// </summary>
static class InteropDocTests
{
    const string LaneHeading = "### The interop lane (`tools/test.sh interop`)";

    /// <summary>A variable the lane reads: its own, and where it finds the siblings' repositories.</summary>
    static readonly Regex Variable = new(@"\bSW_(?:INTEROP_[A-Z]+(?:_[A-Z]+)*|(?:PATTERNS|MAPPER|AGMESSAGE)_DIR)\b");

    /// <summary>A variable a shell script of the lane sets for what it starts (`NAME=… command`, or `export NAME=…`).</summary>
    static readonly Regex SetForChild = new(@"^\s*(?:export\s+)?(SW_[A-Z0-9_]+)=", RegexOptions.Multiline);

    public static async Task Run(Action<bool, string> Check)
    {
        var master = ShowBusSpecTests.Master();
        Check(master is not null, "interop doc: docs/ found above the suite");
        if (master is null) return;
        var root = Path.GetDirectoryName(Path.GetDirectoryName(master)!)!;
        var build = Read(Path.Combine(root, "docs", "BUILD.md"));
        var script = Read(Path.Combine(root, "tools", "test.sh"));
        var lane = Read(Path.Combine(root, "tools", "interop", "build.sh"));
        var drivers = Path.Combine(root, "tests", "interop");
        Check(build is not null && script is not null && lane is not null && Directory.Exists(drivers),
            "interop doc: docs/BUILD.md, tools/test.sh, tools/interop/build.sh and tests/interop are there");
        if (build is null || script is null || lane is null || !Directory.Exists(drivers)) return;

        var section = Section(build, LaneHeading);
        Check(section.Length > 0, $"interop doc: BUILD.md has its section '{LaneHeading}'");
        Rows(Check, section, drivers);
        var read = Variables(Check, section, root, drivers);
        var help = Help(script);
        Lanes(Check, script, help, read, OwnVariables(root));
        await Printed(Check, root, help);
    }

    /// <summary>
    /// The section's rows table is the drivers' rows: each `id: '…'` of tests/interop/&lt;topology&gt;.interop.cjs is a row of the table
    /// on that topology, each row of the table runs in every driver it names, no row is listed twice, and every topology a driver
    /// runs is a row of the topologies table. A driver's every test is a row the table can see: as many top-level `test(` calls
    /// (at the line's start, so none inside a loop or a condition), `test(` calls in all and `row({` specs as `id: '…'` lines,
    /// and no test skipped, marked as to do or singled out (`.skip(`, `.todo(`, `.only(`, or a `skip:`, `todo:` or `only:`
    /// option), in its code outside comments.
    /// </summary>
    static void Rows(Action<bool, string> Check, string section, string drivers)
    {
        var run = new SortedSet<string>(StringComparer.Ordinal);
        var topologies = new List<string>();
        foreach (var file in Directory.GetFiles(drivers, "*.interop.cjs").Order(StringComparer.Ordinal))
        {
            var topology = Path.GetFileName(file)[..^".interop.cjs".Length];
            topologies.Add(topology);
            var code = Code(Read(file)!);
            var ids = Regex.Matches(code, @"^\s*id: '([^']+)',$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
            Check(ids.Count > 0, $"interop doc: the {topology} driver has rows (its tests' `id: '…'`)");
            var topLevel = Regex.Matches(code, @"^test\(", RegexOptions.Multiline).Count;
            var tests = Regex.Matches(code, @"(?<![\w.$])test\(").Count;
            var specs = Regex.Matches(code, @"(?<![\w.$])row\(\{").Count;
            Check(topLevel == ids.Count && tests == ids.Count && specs == ids.Count,
                $"interop doc: the {topology} driver's every test is a row with its id on a line of its own (ids {ids.Count}; top-level tests {topLevel}; tests {tests}; row specs {specs})");
            var unseen = Regex.Matches(code, @"\.(?:skip|todo|only)\s*\(|\b(?:skip|todo|only)\s*:").Select(m => m.Value.Trim()).ToList();
            Check(unseen.Count == 0, $"interop doc: the {topology} driver skips, marks as to do or singles out no test: " + string.Join(", ", unseen));
            foreach (var id in ids) run.Add(topology + " " + id);
        }
        Check(topologies.Count > 0, "interop doc: tests/interop has a driver per topology (*.interop.cjs)");

        var listed = new SortedSet<string>(StringComparer.Ordinal);
        var twice = new List<string>();
        foreach (Match m in Regex.Matches(section, @"^\| (S[0-9][0-9a-z.]*) \| ([a-z]+(?:, [a-z]+)*) \| .+ \|$", RegexOptions.Multiline))
            foreach (var topology in m.Groups[2].Value.Split(", "))
                if (!listed.Add(topology + " " + m.Groups[1].Value)) twice.Add(topology + " " + m.Groups[1].Value);
        Check(listed.Count > 0, "interop doc: BUILD.md's lane section has its rows table (| Row | On | What it holds |)");
        Check(twice.Count == 0, "interop doc: no row is listed twice on one topology: " + string.Join(", ", twice));
        var unlisted = run.Except(listed, StringComparer.Ordinal).ToList();
        var notRun = listed.Except(run, StringComparer.Ordinal).ToList();
        Check(unlisted.Count == 0, "interop doc: every row a driver runs is in BUILD.md's rows table (topology row): " + string.Join(", ", unlisted));
        Check(notRun.Count == 0, "interop doc: every row of BUILD.md's rows table is run by the driver it names (topology row): " + string.Join(", ", notRun));
        var undescribed = topologies.Where(t => !Regex.IsMatch(section, @"^\| `" + Regex.Escape(t) + @"` \| .+ \| `tests/interop/" + Regex.Escape(t) + @"\.interop\.cjs` \|$", RegexOptions.Multiline)).ToList();
        Check(undescribed.Count == 0, "interop doc: every topology a driver runs is described with its driver in the topologies table: " + string.Join(", ", undescribed));
    }

    /// <summary>
    /// Every variable tools/test.sh, the lane's shell and Python scripts and its drivers name is named in the section; the
    /// variables read, for the help's check.
    /// </summary>
    static SortedSet<string> Variables(Action<bool, string> Check, string section, string root, string drivers)
    {
        var interop = Path.Combine(root, "tools", "interop");
        var sources = new[] { Path.Combine(root, "tools", "test.sh") }
            .Concat(Directory.GetFiles(interop, "*.sh"))
            .Concat(Directory.GetFiles(interop, "*.py"))
            .Concat(Directory.EnumerateFiles(drivers, "*.cjs", SearchOption.AllDirectories)
                .Where(f => !f.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("node_modules", StringComparer.Ordinal)));
        var read = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in sources) foreach (Match m in Variable.Matches(Read(file)!)) read.Add(m.Value);
        Check(read.Contains("SW_INTEROP_REQUIRED") && read.Contains("SW_INTEROP_HEADS"),
            "interop doc: the lane's variables are found in its scripts (SW_INTEROP_REQUIRED, SW_INTEROP_HEADS among them)");
        var unnamed = read.Where(v => !Regex.IsMatch(section, @"\b" + v + @"\b")).ToList();
        Check(unnamed.Count == 0, "interop doc: BUILD.md's lane section names every variable the lane reads: " + string.Join(", ", unnamed));
        return read;
    }

    /// <summary>The variables tools/test.sh and the lane's shell scripts set, in their code, for the processes they start.</summary>
    static SortedSet<string> OwnVariables(string root)
    {
        var own = new SortedSet<string>(StringComparer.Ordinal);
        var scripts = new[] { Path.Combine(root, "tools", "test.sh") }.Concat(Directory.GetFiles(Path.Combine(root, "tools", "interop"), "*.sh"));
        foreach (var file in scripts)
        {
            var code = string.Join('\n', Read(file)!.Split('\n').Where(l => !l.TrimStart().StartsWith('#')));
            foreach (Match m in SetForChild.Matches(code)) own.Add(m.Groups[1].Value);
        }
        return own;
    }

    /// <summary>
    /// The help names every lane the script runs and nothing else, as its refusal of an unknown lane does; its default lanes are
    /// the ones the script sets; it points at BUILD.md's section; and it names every variable a person sets for the lane: every
    /// one the lane reads but those its own scripts set for what they start.
    /// </summary>
    static void Lanes(Action<bool, string> Check, string script, string help, SortedSet<string> read, SortedSet<string> own)
    {
        var lines = help.Split('\n');
        var from = Array.IndexOf(lines, "Lanes:");
        var described = lines.Skip(from + 1).TakeWhile(l => l.StartsWith(' ')).Select(l => Regex.Match(l, "^  ([a-z][a-z0-9]*) +\\S"))
            .Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();
        var code = string.Join('\n', script.Split('\n').Where(l => !l.StartsWith('#')));
        var arms = Regex.Matches(code, @"^    ([a-z][a-z0-9]*)\)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        var refusal = Regex.Match(code, @"unknown lane: \$l \(([a-z0-9 ]+);");
        var defaults = Regex.Match(code, @"lanes=\(([a-z0-9 ]+)\)");
        Check(from >= 0 && described.Count > 0 && arms.Count > 0 && refusal.Success && defaults.Success,
            "interop doc: tools/test.sh has its help's lanes, its lanes' arms, its unknown lane's words and its default lanes");
        Check(described.Order(StringComparer.Ordinal).SequenceEqual(arms.Order(StringComparer.Ordinal), StringComparer.Ordinal),
            $"interop doc: the help describes exactly the lanes tools/test.sh runs (help: {string.Join(" ", described)}; runs: {string.Join(" ", arms)})");
        Check(refusal.Groups[1].Value.Split(' ').SequenceEqual(described, StringComparer.Ordinal),
            $"interop doc: an unknown lane's words list the lanes as the help does, in its order ({refusal.Groups[1].Value})");
        Check(help.Contains("the default lanes: " + defaults.Groups[1].Value + "\n", StringComparison.Ordinal),
            $"interop doc: the help names the default lanes the script sets ({defaults.Groups[1].Value})");
        Check(help.Contains("tools/test.sh help", StringComparison.Ordinal) && help.Contains("docs/BUILD.md, \"The interop lane\"", StringComparison.Ordinal),
            "interop doc: the help says how to print it and points at BUILD.md's section on the interop lane");
        var set = read.Except(own, StringComparer.Ordinal).ToList();
        var unnamed = set.Where(v => !Regex.IsMatch(help, @"\b" + v + @"\b")).ToList();
        Check(set.Count > 0 && unnamed.Count == 0,
            $"interop doc: the help names every variable a person sets for the lane (all it reads but {string.Join(", ", own)}): " + string.Join(", ", unnamed));
    }

    /// <summary>A script's code: its lines but those that are only a comment (`//`, or a block comment's `/*`, `*` and `*/`).</summary>
    static string Code(string text) =>
        string.Join('\n', text.Split('\n').Where(l => !Regex.IsMatch(l, @"^\s*(?://|/\*|\*)")));

    /// <summary>
    /// Off Windows, bash runs the script: help, -h and --help, alone or among lanes, print the header and nothing else and exit 0
    /// before any lane runs; an unknown lane is refused, exit 1, with words that point at the help.
    /// </summary>
    static async Task Printed(Action<bool, string> Check, string root, string help)
    {
        if (!Bench.OffWindows("interop doc: tools/test.sh help printed by bash (the Linux job runs it)")) return;
        var script = Path.Combine(root, "tools", "test.sh");
        foreach (var args in new[] { new[] { "help" }, ["-h"], ["--help"], ["unit", "help"] })
        {
            var (code, output, errors) = await Bash(script, root, args);
            Check(code == 0 && output == help && errors.Length == 0,
                $"interop doc: tools/test.sh {string.Join(' ', args)} prints its header and runs nothing (exit {code}; {errors.Trim()})");
        }
        var refused = await Bash(script, root, "no-such-lane");
        Check(refused.Code == 1 && refused.Errors.Contains("unknown lane: no-such-lane (", StringComparison.Ordinal)
              && refused.Errors.Contains("tools/test.sh help says what each does", StringComparison.Ordinal) && !refused.Output.Contains("==>", StringComparison.Ordinal),
            $"interop doc: an unknown lane is refused with words that point at the help, and nothing runs (exit {refused.Code}): {refused.Errors.Trim()}");
    }

    /// <summary>The script's header as its help prints it: each comment line from the second on, up to the first that is not one, without its "# ".</summary>
    static string Help(string script)
    {
        var lines = script.Split('\n').Skip(1).TakeWhile(l => l.StartsWith('#'));
        return string.Concat(lines.Select(l => (l.StartsWith("# ", StringComparison.Ordinal) ? l[2..] : l[1..]) + "\n"));
    }

    static async Task<(int Code, string Output, string Errors)> Bash(string script, string cwd, params string[] args)
    {
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        foreach (var a in args) start.ArgumentList.Add(a);
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception e)
        {
            return (-1, "", "bash did not start: " + e.Message);
        }
        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                return (-1, await output, "tools/test.sh did not end within 30 s: " + await errors);
            }
            return (process.ExitCode, await output, await errors);
        }
    }

    static string? Read(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal) : null;

    /// <summary>From a heading's line to the next heading of its level or higher, outside code fences; "" when it is not there.</summary>
    static string Section(string text, string heading)
    {
        var lines = text.Split('\n');
        var level = heading.TakeWhile(ch => ch == '#').Count();
        var start = Array.IndexOf(lines, heading);
        if (start < 0) return "";
        var fenced = false;
        var end = lines.Length;
        for (var i = start + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            if (fenced) continue;
            var m = Regex.Match(lines[i], "^(#+) ");
            if (m.Success && m.Groups[1].Length <= level) { end = i; break; }
        }
        return string.Join('\n', lines[start..end]);
    }
}
