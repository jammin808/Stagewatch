using System.Net;

namespace Stagewatch.Remote;

/// <summary>
/// One connected browser screen, as the operator sees it. A screen's <see cref="Id"/> is chosen by the browser and
/// kept there (localStorage), so a name given to "the lobby TV" survives reloads, reboots and Stagewatch restarts.
/// Several tabs in one browser share an id and are managed together.
/// </summary>
public sealed record ScreenInfo(
    string Id,
    string Name,
    string Page,
    string Address,
    string Device,
    DateTime ConnectedUtc,
    bool Control,
    int Tabs);

public static class Screens
{
    /// <summary>Ids come from browsers: short, plain, and nothing that could break a log line or a settings file.</summary>
    public static bool ValidId(string? id)
        => !string.IsNullOrEmpty(id) && id.Length is >= 6 and <= 32 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>"iPad · Safari", "Windows · Edge"… from a User-Agent, for telling screens apart in a list.</summary>
    public static string Device(string? userAgent)
    {
        var ua = userAgent ?? "";
        string os = ua.Contains("iPad", StringComparison.Ordinal) ? "iPad"
            : ua.Contains("iPhone", StringComparison.Ordinal) ? "iPhone"
            : ua.Contains("Android", StringComparison.Ordinal) ? (ua.Contains("Mobile", StringComparison.Ordinal) ? "Android phone" : "Android tablet")
            : ua.Contains("CrOS", StringComparison.Ordinal) ? "Chromebook"
            : ua.Contains("Windows", StringComparison.Ordinal) ? "Windows"
            : ua.Contains("Macintosh", StringComparison.Ordinal) ? "Mac"
            : ua.Contains("SMART-TV", StringComparison.OrdinalIgnoreCase) || ua.Contains("Tizen", StringComparison.Ordinal) || ua.Contains("Web0S", StringComparison.Ordinal) ? "Smart TV"
            : ua.Contains("Linux", StringComparison.Ordinal) ? "Linux"
            : "Browser";
        string browser = ua.Contains("Edg/", StringComparison.Ordinal) ? "Edge"
            : ua.Contains("OPR/", StringComparison.Ordinal) ? "Opera"
            : ua.Contains("Firefox/", StringComparison.Ordinal) ? "Firefox"
            : ua.Contains("Chrome/", StringComparison.Ordinal) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.Ordinal) ? "Safari"
            : "";
        return browser.Length > 0 ? $"{os} · {browser}" : os;
    }

    /// <summary>The page a screen says it shows, cleaned: a path on this server, never a PIN.</summary>
    public static string CleanPage(string? page)
    {
        if (string.IsNullOrEmpty(page) || page.Length > 400 || page[0] != '/' || page.StartsWith("//", StringComparison.Ordinal)) return "/";
        var q = page.IndexOf('?');
        if (q < 0) return page;
        var kept = page[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("pin=", StringComparison.OrdinalIgnoreCase));
        var query = string.Join('&', kept);
        return query.Length == 0 ? page[..q] : page[..q] + "?" + query;
    }

    /// <summary>Who does "Stage left", "all" or an id mean?</summary>
    public static IReadOnlyList<ScreenInfo> Match(IReadOnlyList<ScreenInfo> screens, string who)
    {
        who = who.Trim();
        if (who.Length == 0 || who.Equals("all", StringComparison.OrdinalIgnoreCase))
            return screens.Where(s => !s.Control).ToList();
        var byId = screens.Where(s => s.Id.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
        return byId.Count > 0 ? byId : screens.Where(s => s.Name.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    internal static string AddressOf(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4().ToString() : a.ToString();
}
