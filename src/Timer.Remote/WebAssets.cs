using System.Security.Cryptography;

namespace Stagewatch.Remote;

/// <summary>The built-in web pages, for hosts other than the web server (the output window's 3D layer).</summary>
public static class WebAssets
{
    private static readonly string[] HoloFiles = { "holo.html", "holo-page.js", "holo.bundle.js", "common.js" };
    private static readonly object Gate = new();
    private static string? _holoFolder;

    /// <summary>
    /// Write the holographic page into a folder under <paramref name="root"/> named after its content hash, so a new
    /// version never mixes with an old one and an existing copy is reused. Returns the folder.
    /// </summary>
    public static string ExtractHolo(string root)
    {
        lock (Gate)
        {
            if (_holoFolder is not null && Directory.Exists(_holoFolder)) return _holoFolder;
            var files = HoloFiles.Select(n => (Name: n, Data: Assets.Get(n) ?? throw new InvalidOperationException("missing built-in page " + n))).ToList();
            using var sha = SHA256.Create();
            foreach (var f in files) sha.TransformBlock(f.Data, 0, f.Data.Length, null, 0);
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var folder = Path.Combine(root, Convert.ToHexString(sha.Hash!)[..12].ToLowerInvariant());
            Directory.CreateDirectory(folder);
            foreach (var f in files)
            {
                var path = Path.Combine(folder, f.Name);
                if (File.Exists(path) && new FileInfo(path).Length == f.Data.Length) continue;
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, f.Data);
                File.Move(tmp, path, overwrite: true);
            }
            _holoFolder = folder;
            return folder;
        }
    }
}
