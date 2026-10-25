using System.Net;
using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// One connected browser screen, as the operator sees it. A screen's <see cref="Id"/> is chosen by the browser and
/// kept there (localStorage), so a name given to "the lobby TV" survives reloads, reboots and Stagewatch restarts.
/// Several tabs in one browser share an id and are managed together. A show app's proven node link is never a screen;
/// a socket that only claims to be one is listed with <see cref="Claim"/> ("patterns 9f8e7d6c"), never reached by "all".
/// </summary>
public sealed record ScreenInfo(
    string Id,
    string Name,
    string Page,
    string Address,
    string Device,
    DateTime ConnectedUtc,
    bool Control,
    int Tabs,
    string Claim = "");

public static class Screens
{
    /// <summary>Ids come from browsers: short, plain, and nothing that could break a log line or a settings file.</summary>
    public static bool ValidId(string? id)
        => !string.IsNullOrEmpty(id) && id.Length is >= 6 and <= 32 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// The id a browser chose for itself (<c>cid</c>), when Screens may keep it: a valid id that no id this Stagewatch makes
    /// can be (a claim's <c>claim-</c>, a window's <c>win-</c>), so no browser is grouped with, or pushed to as, either.
    /// </summary>
    public static bool ValidBrowserId(string? id)
        => ValidId(id) && !id!.StartsWith(NodeLinks.ClaimPrefix, StringComparison.OrdinalIgnoreCase) && !id.StartsWith(WindowPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a window's link is listed under in the socket table: <c>win-</c> and the window's id.</summary>
    public const string WindowPrefix = "win-";

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

    /// <summary>
    /// Who does "Stage left", "all" or an id mean? "all" (or nothing) is every browser that is neither a control page nor
    /// a claim; then a screen id (a claim's included); then a name the operator gave. Show apps are never in the list.
    /// </summary>
    public static IReadOnlyList<ScreenInfo> Match(IReadOnlyList<ScreenInfo> screens, string who)
    {
        who = who.Trim();
        if (IsAll(who))
            return screens.Where(s => !s.Control && s.Claim.Length == 0).ToList();
        var byId = screens.Where(s => s.Id.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
        return byId.Count > 0 ? byId : screens.Where(s => s.Name.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>"all", or no target at all: every browser screen.</summary>
    public static bool IsAll(string who) => who.Trim() is var w && (w.Length == 0 || w.Equals("all", StringComparison.OrdinalIgnoreCase));

    internal static string AddressOf(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4().ToString() : a.ToString();
}
