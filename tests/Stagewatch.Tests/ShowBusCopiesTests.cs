using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// tools/showbus-copies.sh, §14.5's comparison of the trees, run as the interop lane runs it. On this tree it holds the master's
/// header and §14 to its body and its scripted peer, and compares nothing else while the Landed line names Stagewatch alone. On
/// trees made for it from this tree's master and peer it holds each tree the Landed line names to the master — its body (read
/// with CRLF line ends too), the hash its header's Body line prints and its peer, byte for byte — and names each stale copy and
/// why; an absent tree is "not run", and a failure only with SW_INTEROP_REQUIRED=1; a tree the Landed line does not name is not
/// compared, however stale; a master whose own header, peer or Landed line does not hold fails; and a word it does not take is
/// refused before anything is compared. The script is the Linux bench's (bash): on Windows this group is named, never run.
/// </summary>
static class ShowBusCopiesTests
{
    const string Start = "## 1. Rules every node follows";

    /// <summary>The trees that copy the master, by repository name (§14.5).</summary>
    static readonly string[] Copies = ["Patterns", "Mapper", "AGMessage"];

    /// <summary>The variables the script reads: cleared for every run, so the machine's own never reach a check.</summary>
    static readonly string[] Vars = ["SW_PATTERNS_DIR", "SW_MAPPER_DIR", "SW_AGMESSAGE_DIR", "SW_INTEROP_REQUIRED"];

    public static async Task Run(Action<bool, string> Check)
    {
        if (!Bench.OffWindows("showbus copies: tools/showbus-copies.sh against this tree and trees made for it (bash: the Linux job and the interop lane run it)"))
            return;
        var master = ShowBusSpecTests.Master();
        Check(master is not null, "showbus copies: docs/SHOWBUS.md found above the suite");
        if (master is null) return;
        var root = Path.GetDirectoryName(Path.GetDirectoryName(master)!)!;
        var script = Path.Combine(root, "tools", "showbus-copies.sh");
        var peer = File.ReadAllBytes(Path.Combine(root, "tools", "showbus", "peerlink-peer.mjs"));
        var text = File.ReadAllText(master, new UTF8Encoding(false)).Replace("\r\n", "\n", StringComparison.Ordinal);
        var at = text.IndexOf("\n" + Start + "\n", StringComparison.Ordinal) + 1;
        var (header, body) = (text[..at], text[at..]);
        var hash = Hex(Encoding.UTF8.GetBytes(body));

        var dir = Path.Combine(Path.GetTempPath(), "sw-copies-" + Guid.NewGuid().ToString("N"));
        try
        {
            // This tree, as the release's validate runs it: the master holds its own hashes. Its siblings are pointed at folders that are
            // not there, so a landed tree is "not run" whatever clones this machine holds, and a tree not landed is not compared.
            var landed = Regex.Match(header, @"^\*Landed \(§14\): (.+)\.\*$", RegexOptions.Multiline).Groups[1].Value.Split(", ");
            var away = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in Copies) away["SW_" + t.ToUpperInvariant() + "_DIR"] = Path.Combine(dir, "away", t);
            var own = await Script(script, root, away);
            Check(own.Code == 0 && Says(own.Out, "Stagewatch", "the master: its header's Body line prints its body's hash, and §14 pins its peer's")
                  && own.Out.Contains("  body " + hash + "\n", StringComparison.Ordinal) && own.Out.Contains("  peer " + Hex(peer) + "\n", StringComparison.Ordinal)
                  && Copies.All(t => Says(own.Out, t, landed.Contains(t) ? "not run: no tree at " + Path.Combine(dir, "away", t) : "not landed: not compared"))
                  && own.Out.Contains("every landed tree found holds the master (landed: " + string.Join(", ", landed) + ")", StringComparison.Ordinal),
                "showbus copies: this tree's master holds its body's hash and its peer's, and the script compares only the trees its Landed line "
                + "names (exit " + own.Code + "): " + own.Out);

            await Trees(Check, dir, script, header, body, hash, peer);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    static async Task Trees(Action<bool, string> Check, string dir, string script, string header, string body, string hash, byte[] peer)
    {
        const string All = "Stagewatch, Patterns, Mapper, AGMessage";
        // What the script says of a header with no line in §14.5's form: nothing printed, and the form it reads.
        const string NoBodyLine = "its header's Body line prints nothing (§14.5: once, on a line of its own, '*Body SHA-256 (§14): <hash>.*', the hash 64 lowercase hex)";
        var sw = Path.Combine(dir, "Stagewatch");
        var tool = Path.Combine(sw, "tools", "showbus-copies.sh");
        void Master(string landed, string? printed = null, byte[]? itsPeer = null)
        {
            // This tree's header with its Landed and Body lines set here, so the trees made from it hold whatever this tree's own lines say.
            var h = Regex.Replace(header, @"^\*Landed \(§14\): .+\.\*$", "*Landed (§14): " + landed + ".*", RegexOptions.Multiline);
            h = Regex.Replace(h, @"^\*Body SHA-256 \(§14\): .*\.\*$", "*Body SHA-256 (§14): " + (printed ?? hash) + ".*", RegexOptions.Multiline);
            Write(Path.Combine(sw, "docs", "SHOWBUS.md"), h + body);
            Write(Path.Combine(sw, "tools", "showbus", "peerlink-peer.mjs"), itsPeer ?? peer);
        }
        Directory.CreateDirectory(Path.Combine(sw, "tools"));
        File.Copy(script, tool);
        Master(All);

        // A copy as §14.5 has it: its own title and paragraph, the same Body line, the same body, the same peer.
        void Copy(string name, string? itsBody = null, string? printed = null, bool bodyLine = true, byte[]? itsPeer = null, bool crlf = false,
            string? folder = null, string? bodyLineAs = null)
        {
            var at = Path.Combine(dir, folder ?? name);
            var copy = "# " + name + "'s copy of Show Bus 1.3\n\n*" + name + "'s copy of the Show Bus contract, 1.3. The master is Stagewatch's "
                + "docs/SHOWBUS.md; where they differ, the master wins.*\n\n"
                + (bodyLine ? (bodyLineAs ?? "*Body SHA-256 (§14): " + (printed ?? hash) + ".*") + "\n\n" : "") + (itsBody ?? body);
            Write(Path.Combine(at, "docs", "SHOWBUS.md"), crlf ? copy.Replace("\n", "\r\n", StringComparison.Ordinal) : copy);
            Write(Path.Combine(at, "tools", "showbus", "peerlink-peer.mjs"), itsPeer ?? peer);
        }
        Copy("Patterns");
        Copy("Mapper", crlf: true);

        // Every landed tree found holds; the absent one is not run, and fails the run only when the lane requires every tree.
        var run = await Script(tool, dir, null);
        Check(run.Code == 0 && Says(run.Out, "Patterns", "holds the master (" + Path.Combine(dir, "Patterns") + ")")
              && Says(run.Out, "Mapper", "holds the master") && Says(run.Out, "AGMessage", "not run: no tree at " + Path.Combine(dir, "AGMessage")),
            "showbus copies: each landed tree beside the master that holds it is said to hold it — a copy checked out with CRLF line ends "
            + "among them — and an absent one is not run (exit " + run.Code + "): " + run.Out);
        run = await Script(tool, dir, new() { ["SW_INTEROP_REQUIRED"] = "1" });
        Check(run.Code == 1 && Says(run.Out, "AGMessage", "not run") && run.Out.Contains("does not hold: AGMessage\n", StringComparison.Ordinal),
            "showbus copies: with SW_INTEROP_REQUIRED=1 an absent landed tree fails the run (exit " + run.Code + "): " + run.Out);
        Copy("AGMessage");
        run = await Script(tool, dir, null);
        Check(run.Code == 0 && Copies.All(t => Says(run.Out, t, "holds the master"))
              && run.Out.Contains("every landed tree found holds the master (landed: " + All + ")", StringComparison.Ordinal),
            "showbus copies: every landed tree holds the master (exit " + run.Code + "): " + run.Out);

        // Each way a copy goes stale is named, with the tree; the trees that hold still say so.
        var edited = body.Replace("**Attempts are not facts.**", "**Attempts are not fact5.**", StringComparison.Ordinal);
        (string Why, Action Make, string Says)[] stale =
        [
            ("its body differs by one byte", () => Copy("Patterns", itsBody: edited, printed: Hex(Encoding.UTF8.GetBytes(edited))),
                "its body hashes to " + Hex(Encoding.UTF8.GetBytes(edited))),
            ("its header prints another hash", () => Copy("Patterns", printed: Flip(hash)), "its header's Body line prints " + Flip(hash)),
            ("its header prints no hash", () => Copy("Patterns", bodyLine: false), NoBodyLine),
            ("its header carries the hash in its own paragraph, not in §14.5's line",
                () => Copy("Patterns", bodyLineAs: "*Patterns's copy of the Show Bus contract, 1.3 (body sha256 " + hash + ").*"), NoBodyLine),
            ("its Body line has no full stop after the hash", () => Copy("Patterns", bodyLineAs: "*Body SHA-256 (§14): " + hash + "*"), NoBodyLine),
            ("its peer differs", () => Copy("Patterns", itsPeer: [.. peer, .. "// edited\n"u8.ToArray()]),
                "its scripted peer hashes to " + Hex([.. peer, .. "// edited\n"u8.ToArray()])),
            ("it has no master's copy", () => File.Delete(Path.Combine(dir, "Patterns", "docs", "SHOWBUS.md")), "no docs/SHOWBUS.md"),
            ("it has no scripted peer", () => File.Delete(Path.Combine(dir, "Patterns", "tools", "showbus", "peerlink-peer.mjs")),
                "no scripted peer at tools/showbus/peerlink-peer.mjs"),
        ];
        foreach (var (why, make, says) in stale)
        {
            Copy("Patterns");
            make();
            run = await Script(tool, dir, null);
            Check(run.Code == 1 && Says(run.Out, "Patterns", "stale (" + Path.Combine(dir, "Patterns") + "): ") && Line(run.Out, "Patterns").Contains(says, StringComparison.Ordinal)
                  && Says(run.Out, "Mapper", "holds the master") && Says(run.Out, "AGMessage", "holds the master")
                  && run.Out.Contains("does not hold: Patterns\n", StringComparison.Ordinal),
                $"showbus copies: a landed copy is named stale when {why} ({says}), and only it (exit {run.Code}): {run.Out}");
        }

        // A tree the Landed line does not name is not compared, however stale; a tree given by a folder of any name, or by a variable.
        Copy("Patterns", itsBody: edited);
        Master("Stagewatch, Mapper, AGMessage");
        run = await Script(tool, dir, null, "Patterns");
        Check(run.Code == 0 && Says(run.Out, "Patterns", "not landed: not compared") && !run.Out.Contains("stale", StringComparison.Ordinal),
            "showbus copies: a tree the Landed line does not name is not compared, though it is named and stale (exit " + run.Code + "): " + run.Out);
        Master(All);
        Copy("Patterns", folder: "patterns-at-its-pin");
        Copy("Mapper", folder: "mapper-clone");
        run = await Script(tool, dir, new() { ["SW_MAPPER_DIR"] = "mapper-clone" }, "Patterns=patterns-at-its-pin");
        Check(run.Code == 0 && Says(run.Out, "Patterns", "holds the master (" + Path.Combine(dir, "patterns-at-its-pin") + ")")
              && Says(run.Out, "Mapper", "holds the master (" + Path.Combine(dir, "mapper-clone") + ")"),
            "showbus copies: a tree is found at Patterns=<folder> or $SW_MAPPER_DIR, each read from where the script was run (exit " + run.Code + "): " + run.Out);
        run = await Script(tool, dir, new() { ["SW_PATTERNS_DIR"] = "patterns-at-its-pin" }, Path.Combine(dir, "Patterns"));
        Check(run.Code == 1 && Says(run.Out, "Patterns", "stale (" + Path.Combine(dir, "Patterns") + ")"),
            "showbus copies: a folder named for a tree is that tree, ahead of its variable (exit " + run.Code + "): " + run.Out);
        Copy("Patterns");

        // The master holds itself first: its Body line, its peer and its Landed line.
        (string Why, Action Make, string Says)[] master =
        [
            ("its header prints another hash", () => Master(All, printed: Flip(hash)), "its header's Body line prints " + Flip(hash)),
            ("its peer is not the one §14 pins", () => Master(All, itsPeer: [.. peer, .. "// edited\n"u8.ToArray()]), "its scripted peer hashes to"),
            ("its Landed line names Stagewatch after another tree", () => Master("Patterns, Stagewatch"), "names Stagewatch first"),
            ("its Landed line names a tree twice", () => Master("Stagewatch, Patterns, Patterns"), "names 'Patterns'"),
            ("its Landed line names a tree §14.5 does not", () => Master("Stagewatch, Slides"), "names 'Slides'"),
        ];
        foreach (var (why, make, says) in master)
        {
            make();
            run = await Script(tool, dir, null);
            Check(run.Code == 1 && Says(run.Out, "Stagewatch", "the master does not hold: ") && Line(run.Out, "Stagewatch").Contains(says, StringComparison.Ordinal)
                  && run.Out.Contains("does not hold: Stagewatch", StringComparison.Ordinal),
                $"showbus copies: the master does not hold when {why} ({says}) (exit {run.Code}): {run.Out}");
        }
        Master(All);

        // A word it does not take: refused, and nothing compared.
        string[][] refused = [["Slides=x"], ["Stagewatch=."], ["Patterns=x", "Patterns=y"], ["Patterns="], ["--all"], [Path.Combine(dir, "elsewhere")]];
        foreach (var words in refused)
        {
            run = await Script(tool, dir, null, words);
            Check(run.Code == 2 && run.Out.Contains("nothing compared", StringComparison.Ordinal) && !run.Out.Contains("holds", StringComparison.Ordinal),
                $"showbus copies: '{string.Join(" ", words)}' is refused and nothing is compared (exit {run.Code}): {run.Out}");
        }
    }

    /// <summary>Whether a tree's line in the script's output starts, after its name, with <paramref name="words"/>.</summary>
    static bool Says(string output, string tree, string words)
        => Regex.IsMatch(output, @"^  " + Regex.Escape(tree) + @" +" + Regex.Escape(words), RegexOptions.Multiline);

    /// <summary>A tree's lines in the script's output, joined; empty when it has none.</summary>
    static string Line(string output, string tree)
        => string.Join("\n", output.Split('\n').Where(l => Regex.IsMatch(l, @"^  " + Regex.Escape(tree) + " ")));

    static string Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Another hash of the same shape: the last digit moved on by one.</summary>
    static string Flip(string hash) => hash[..^1] + (hash[^1] == 'f' ? '0' : hash[^1] == '9' ? 'a' : (char)(hash[^1] + 1));

    static void Write(string path, string text) => Write(path, new UTF8Encoding(false).GetBytes(text));

    static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>The script run by bash from <paramref name="cwd"/>, the machine's own variables for it cleared: its exit code and what it wrote.</summary>
    static async Task<(int Code, string Out)> Script(string script, string cwd, Dictionary<string, string>? env, params string[] args)
    {
        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        start.ArgumentList.Add(script);
        foreach (var a in args) start.ArgumentList.Add(a);
        foreach (var v in Vars) start.Environment.Remove(v);
        if (env is not null) foreach (var (k, v) in env) start.Environment[k] = v;
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (Win32Exception e)
        {
            return (-1, "bash did not start: " + e.Message);
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
                return (-1, "still running after 30 s");
            }
            return (process.ExitCode, await output + await errors);
        }
    }
}
