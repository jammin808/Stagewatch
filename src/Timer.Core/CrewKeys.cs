using System.Security.Cryptography;
using System.Text;

namespace Stagewatch.Core;

/// <summary>
/// Crew link keys: who may confirm ("Got it") a cue without the operator PIN.
///
/// Each crew page's link carries a short key derived from this install's secret and the page's target
/// (speaker, moderator, backstage, interpreter, a department). A cue for "Lighting" can only be confirmed by a
/// page opened with Lighting's key — not by any phone on the venue Wi-Fi that guesses a cue number. Keys are
/// stable (bookmark them; print them as QR codes) and can all be revoked at once with a new secret.
/// </summary>
public static class CrewKeys
{
    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));

    /// <summary>The key for one target: 10 hex characters of HMAC-SHA256(secret, target).</summary>
    public static string For(string secret, string target)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes("stagewatch-crew:" + Normal(target)));
        return Convert.ToHexString(mac, 0, 5).ToLowerInvariant();
    }

    public static bool Matches(string secret, string target, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var expected = Encoding.ASCII.GetBytes(For(secret, target));
        var supplied = Encoding.ASCII.GetBytes(key.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static string Normal(string target) => target.Trim().ToLowerInvariant();

    /// <summary>Is this key any crew page's key (the roles or a department)? A crew link is trusted to view the show.</summary>
    public static bool IsAnyCrewKey(string secret, IEnumerable<string> departments, string? key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        foreach (var t in new[] { "speaker", "moderator", "backstage", "interpreter" }.Concat(departments))
            if (Matches(secret, t, key)) return true;
        return false;
    }

    /// <summary>Every crew page with its key, as paths for this server ("/dept?name=Sound&amp;key=…").</summary>
    public static IReadOnlyList<(string Label, string Path)> Links(string secret, IEnumerable<string> departments)
    {
        string K(string t) => For(secret, t);
        var list = new List<(string, string)>
        {
            ("Speaker screen", $"/viewer?key={K("speaker")}"),
            ("Speaker's pocket buzz", $"/haptic?for=speaker&key={K("speaker")}"),
            ("Moderator", $"/moderator?key={K("moderator")}"),
            ("Moderator's pocket buzz", $"/haptic?for=moderator&key={K("moderator")}"),
            ("Green room / backstage", $"/backstage?key={K("backstage")}"),
            ("Interpreters", $"/interpreter?key={K("interpreter")}"),
        };
        foreach (var d in departments.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
            list.Add(($"Department: {d}", $"/dept?name={Uri.EscapeDataString(d)}&key={K(d)}"));
        return list;
    }
}
