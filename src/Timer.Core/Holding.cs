using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>One image in the holding library (stored in the settings; the file lives in <see cref="HoldingLibrary.Folder"/>).</summary>
public sealed class HoldingImage
{
    /// <summary>Random, URL-safe, never reused: browsers may cache an image for ever by its id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>File name inside the library folder (id + extension).</summary>
    [JsonPropertyName("file")] public string File { get; set; } = "";
}

public sealed class HoldingSettings
{
    [JsonPropertyName("images")] public List<HoldingImage> Images { get; set; } = new();
    /// <summary>False = fit the whole image (black bars); true = fill the screen (crops the edges).</summary>
    [JsonPropertyName("fill")] public bool Fill { get; set; }
}

/// <summary>
/// The holding-image library on disk. Pure file work, no UI: thread-safe, so the web server can serve
/// and accept images on its own threads while the show thread only ever changes the settings list.
/// </summary>
public static class HoldingLibrary
{
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>Tests point this somewhere temporary.</summary>
    public static string? FolderOverride { get; set; }

    public static string Folder => FolderOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stagewatch", "Holding");

    public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp" };

    /// <summary>The image type from the file's first bytes (never trust the name), as an extension; null = not an image we show.</summary>
    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8') return ".gif";
        if (b.Length >= 2 && b[0] == 'B' && b[1] == 'M') return ".bmp";
        if (b.Length >= 4 && ((b[0] == 'I' && b[1] == 'I' && b[2] == 42 && b[3] == 0) || (b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 42))) return ".tif";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        return null;
    }

    public static string ContentType(string fileOrExt) => Path.GetExtension(fileOrExt).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    /// <summary>A display name from anything a user typed or a file was called.</summary>
    public static string CleanName(string? name)
    {
        var n = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (n.Length == 0) n = "Holding image";
        return n.Length > 60 ? n[..60].TrimEnd() : n;
    }

    /// <summary>Validate and store an image. Throws <see cref="InvalidDataException"/> with a message fit for the operator.</summary>
    public static HoldingImage Save(string name, byte[] data)
    {
        if (data.Length == 0) throw new InvalidDataException("The file is empty.");
        if (data.LongLength > MaxBytes) throw new InvalidDataException($"The image is larger than {MaxBytes / (1024 * 1024)} MB.");
        var ext = Sniff(data) ?? throw new InvalidDataException("That isn't a PNG, JPG, GIF, BMP, TIFF or WebP image.");
        Directory.CreateDirectory(Folder);
        var id = Guid.NewGuid().ToString("N")[..12];
        var file = id + ext;
        var tmp = Path.Combine(Folder, file + ".tmp");
        System.IO.File.WriteAllBytes(tmp, data);
        System.IO.File.Move(tmp, Path.Combine(Folder, file), overwrite: true); // atomic: never a half-written image
        return new HoldingImage { Id = id, Name = CleanName(name), File = file };
    }

    public static HoldingImage Import(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBytes) throw new InvalidDataException($"{info.Name} is larger than {MaxBytes / (1024 * 1024)} MB.");
        return Save(Path.GetFileNameWithoutExtension(path), System.IO.File.ReadAllBytes(path));
    }

    /// <summary>True for ids this library could have made (hex, 12 chars) — anything else never touches the disk.</summary>
    public static bool IsValidId(string? id) => id is { Length: 12 } && id.All(Uri.IsHexDigit);

    /// <summary>Full path of an image's file, or null when it is missing.</summary>
    public static string? PathOf(string id)
    {
        if (!IsValidId(id) || !Directory.Exists(Folder)) return null;
        foreach (var ext in Extensions)
        {
            var p = Path.Combine(Folder, id + ext);
            if (System.IO.File.Exists(p)) return p;
        }
        return null;
    }

    public static (byte[] Data, string ContentType)? Read(string id)
    {
        var p = PathOf(id);
        if (p is null) return null;
        try { return (System.IO.File.ReadAllBytes(p), ContentType(p)); }
        catch (IOException) { return null; }
    }

    public static void Delete(string id)
    {
        try { if (PathOf(id) is { } p) System.IO.File.Delete(p); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Which image a command means: "2" = the second, otherwise an id or a name (case-insensitive,
    /// then the first name that starts with it). Null = no match.
    /// </summary>
    public static HoldingImage? Resolve(IReadOnlyList<HoldingImage> images, string key)
    {
        key = key.Trim();
        if (key.Length == 0) return null;
        if (int.TryParse(key, out var n)) return n >= 1 && n <= images.Count ? images[n - 1] : null;
        return images.FirstOrDefault(i => i.Id.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? images.FirstOrDefault(i => i.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?? images.FirstOrDefault(i => i.Name.StartsWith(key, StringComparison.OrdinalIgnoreCase));
    }
}
