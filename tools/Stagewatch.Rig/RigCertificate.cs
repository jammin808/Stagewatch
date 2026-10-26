using System.Security.Cryptography.X509Certificates;
using Stagewatch.Core;
using Stagewatch.Remote;

/// <summary>
/// --tls FOLDER: the rig's certificate, the app's kind (Show Bus §10), made by <see cref="CertificateMaker"/> as the desk makes its
/// own and kept, replaced and saved only through <see cref="CertificateFiles"/>, so a restarted rig serves the certificate its peers
/// pinned. Off Windows the password is not sealed (there is no DPAPI), so FOLDER is one of the rig's own: a folder that is not
/// there yet is made readable by this user alone (on Linux and macOS; on Windows the folder's own permissions hold it), and one
/// that is there is taken only as the rig leaves it: holding nothing but node.pfx and node.key (and the half-written copies of them
/// a write cut short leaves), and readable by no other user. Any other is refused, never narrowed or written in: --tls never
/// changes a folder the rig did not make. Each file it writes is readable by this user alone off Windows.
/// </summary>
static class RigCertificate
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerFolder = OwnerOnly | UnixFileMode.UserExecute;
    private const UnixFileMode Others = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                                        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

    /// <summary>
    /// The certificate kept in <paramref name="folder"/>, or made and saved there; null, said on standard error, when there is none
    /// to serve or the folder is not one the rig may keep it in. What the read had to say (a kept one replaced, one made for this run
    /// only) goes to standard error; nothing said holds the password or the key.
    /// </summary>
    public static X509Certificate2? Load(string folder, string room)
    {
        string full;
        try
        {
            full = Path.GetFullPath(folder);
            if (Directory.Exists(full))
            {
                if (Refused(full) is { } why)
                {
                    Console.Error.WriteLine($"Stagewatch.Rig: --tls: {full} {why}: name a folder of the rig's own (one that is not there yet, which it makes)");
                    return null;
                }
            }
            else if (OperatingSystem.IsWindows()) Directory.CreateDirectory(full);
            else Directory.CreateDirectory(full, OwnerFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine("Stagewatch.Rig: --tls: couldn't make the folder: " + ex.Message);
            return null;
        }
        var read = new CertificateFiles(full, secret => secret, secret => secret, now => CertificateMaker.Create("Stagewatch rig", room, now)) { Save = SaveOwnerOnly }.Load();
        if (read.Note is { } note) Console.Error.WriteLine("Stagewatch.Rig: --tls: " + note);
        if (read.Certificate is null) Console.Error.WriteLine("Stagewatch.Rig: --tls: no certificate to serve");
        return read.Certificate;
    }

    /// <summary>
    /// Why an existing folder is not the rig's to keep its certificate in, or null when it is: it holds something other than the
    /// certificate's two files (or a half-written copy of one), or, off Windows, another user may read, write or enter it. Names
    /// only, never what a file holds.
    /// </summary>
    private static string? Refused(string folder)
    {
        var stray = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).Where(n => !Ours(n!)).Order(StringComparer.Ordinal).ToList();
        if (stray.Count > 0) return $"holds what is not its certificate ({string.Join(", ", stray.Take(3))}{(stray.Count > 3 ? ", …" : "")})";
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(folder) & Others) != 0)
            return $"is open to other users (mode {Convert.ToString((int)File.GetUnixFileMode(folder), 8)})";
        return null;

        // CertificateFiles' two files, and AtomicFile's temporary copies of them (node.pfx.<thread>.<guid>.tmp).
        static bool Ours(string name) => name is "node.pfx" or "node.key"
                                         || ((name.StartsWith("node.pfx.", StringComparison.Ordinal) || name.StartsWith("node.key.", StringComparison.Ordinal))
                                             && name.EndsWith(".tmp", StringComparison.Ordinal));
    }

    /// <summary>
    /// A file of the certificate's replaced whole (<see cref="AtomicFile.Write(string, byte[])"/>), then readable by this user alone
    /// off Windows: inside a folder no other user may enter, so it is never another user's to read between the two.
    /// </summary>
    private static void SaveOwnerOnly(string path, byte[] bytes)
    {
        AtomicFile.Write(path, bytes);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, OwnerOnly);
    }
}
