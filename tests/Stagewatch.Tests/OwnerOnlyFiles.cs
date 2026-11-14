using Stagewatch.Core;

/// <summary>
/// An owner-only write (<see cref="AtomicFile.Write(string, string, Func{string, bool}?, bool)"/> with <c>ownerOnly</c>, what the rig's
/// --state takes, whose windows and page grants carry view passes): off Windows the file, its temporary copy and its backup are each
/// readable and writable by this user alone from the moment each is made, whatever mode an older file or backup had. A rename keeps a
/// file's mode, so the file and the backup read after the write carry the modes their temporary copies were made with; the main
/// temporary copy is read while it waits to be swapped in (the whole check runs then).
/// </summary>
static class OwnerOnlyFiles
{
    const UnixFileMode Wide = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static void Run(Action<bool, string> Check)
    {
        using var folder = Scratch.Folder("owner-only");
        var path = Path.Combine(folder.Path, "state.json");
        if (Bench.OnWindows("files: owner-only on Windows writes the file and its backup whole, no temporary copy left (the folder's own permissions hold them)"))
        {
            AtomicFile.Write(path, "{\"a\":1}", AtomicFile.IsJson, ownerOnly: true);
            AtomicFile.Write(path, "{\"a\":2}", AtomicFile.IsJson, ownerOnly: true);
            Check(File.ReadAllText(path) == "{\"a\":2}" && File.ReadAllText(path + ".bak") == "{\"a\":1}" && Directory.GetFiles(folder.Path, "*.tmp").Length == 0,
                "files: owner-only on Windows: written whole, the previous one kept as .bak, no temp left");
            return;
        }
        if (!OperatingSystem.IsWindows()) OffWindows(Check, folder.Path, path);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    static void OffWindows(Action<bool, string> Check, string dir, string path)
    {
        // A first write: the new file is this user's alone.
        AtomicFile.Write(path, "{\"a\":1}", AtomicFile.IsJson, ownerOnly: true);
        Check(File.GetUnixFileMode(path) == AtomicFile.OwnerOnly, $"files: an owner-only first write is 0600 ({Mode(path)})");

        // An older file and an older backup in the mode a new file gets: neither mode survives the next write.
        File.SetUnixFileMode(path, Wide);
        File.WriteAllText(path + ".bak", "{\"old\":0}");
        File.SetUnixFileMode(path + ".bak", Wide);
        var temps = new List<(string Name, UnixFileMode Mode)>();
        AtomicFile.Write(path, "{\"a\":2}", text =>
        {
            foreach (var t in Directory.GetFiles(dir, "*.tmp")) temps.Add((Path.GetFileName(t), File.GetUnixFileMode(t)));
            return AtomicFile.IsJson(text);
        }, ownerOnly: true);
        Check(temps.Count == 1 && temps[0].Mode == AtomicFile.OwnerOnly && temps[0].Name.StartsWith("state.json.", StringComparison.Ordinal),
            $"files: an owner-only write's temporary copy is 0600 while it waits to be swapped in ({string.Join(", ", temps.Select(t => t.Name + " " + Octal(t.Mode)))})");
        Check(File.ReadAllText(path) == "{\"a\":2}" && File.GetUnixFileMode(path) == AtomicFile.OwnerOnly,
            $"files: an owner-only write over a 0644 file leaves it 0600 ({Mode(path)})");
        Check(File.ReadAllText(path + ".bak") == "{\"a\":1}" && File.GetUnixFileMode(path + ".bak") == AtomicFile.OwnerOnly,
            $"files: and its backup, written over a 0644 backup, is 0600 too ({Mode(path + ".bak")})");
        Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "files: an owner-only write leaves no temporary copy behind");

        // The control: the same write without the flag comes out wider, so the checks above prove the flag and not this process's
        // umask. Under a umask of 077 a new file is 0600 whatever is asked, and this check fails by name rather than pass a proof of
        // nothing.
        var plain = Path.Combine(dir, "plain.json");
        File.WriteAllText(plain, "{}");
        File.SetUnixFileMode(plain, Wide);
        AtomicFile.Write(plain, "{\"b\":1}");
        Check((File.GetUnixFileMode(plain) & ~AtomicFile.OwnerOnly) != 0,
            $"files: control — a plain write over a 0644 file is wider than 0600 here ({Mode(plain)}), so the owner-only checks prove the flag (a umask of 077 fails this by name)");
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    static string Mode(string path) => Octal(File.GetUnixFileMode(path));

    static string Octal(UnixFileMode mode) => Convert.ToString((int)mode, 8);
}
