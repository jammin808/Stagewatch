using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Stagewatch.Core;

/// <summary>The door a command line came in by, as <see cref="PlayoutGate"/> names it in its words.</summary>
public enum CommandDoor { Osc, Companion, Http, WebSocket, Mcp }

/// <summary>
/// Who sent a command line, as the server that took it judged them: the address it came from, whether that is this PC (loopback,
/// by the server's local-address seam; over HTTP, a WebSocket or MCP also under a Host that names this PC there,
/// <see cref="PlayoutGate.NamesThisPc"/>), whether the request proved a credential (the PIN this Stagewatch asks for, or a key), and
/// whether a browser page of another site sent it (<see cref="PlayoutGate.OtherSite"/>), which neither this PC nor a listed address
/// vouches for.
/// </summary>
public readonly record struct CommandSender(IPAddress Address, bool ThisPc, bool Credentialed, bool OtherSite = false);

/// <summary>
/// Who may drive another node's air through this Stagewatch, pure. A <c>playout</c> or <c>patterns</c> line runs on a Patterns
/// desk's or a PlayMap node's air over the link this Stagewatch paired, so it is never relayed for a sender that proved nothing:
/// it comes from this PC, with a credential (the PIN this Stagewatch asks for, or a control or MCP key; only HTTP, WebSocket and
/// MCP carry one, OSC and Companion TCP never), or from an address the operator listed (<see cref="RemoteSettings.PlayoutFrom"/>,
/// at most <see cref="MaxFrom"/>). A browser page of another site proves nothing wherever its browser runs: without a credential it
/// is refused even from this PC or a listed address. Anything else is refused in words naming the door, and nothing is sent. No
/// other verb is this gate's. A listed address is trusted by its source address alone, which any machine on that network can forge
/// over OSC (UDP): the operator lists addresses only on a show network they trust.
/// </summary>
public static class PlayoutGate
{
    /// <summary>The most addresses the operator lists.</summary>
    public const int MaxFrom = 16;

    /// <summary>Where the operator lists them, as every refusal names it.</summary>
    public const string SettingWords = "Settings → Remote → May drive playout";

    /// <summary>A command that drives another node's air: the lines this gate decides by their sender.</summary>
    public static bool Drives(ShowCommandKind kind) => kind is ShowCommandKind.Playout or ShowCommandKind.Patterns;

    /// <summary>
    /// A command no door takes from anyone, credential or not: trusting a playout link's certificate is a person's choice on this
    /// desk, beside its fingerprint (Show Bus 13.8.2–3), so a line of it is refused at every door, as its executors refuse one too.
    /// </summary>
    public static bool DeskOnly(ShowCommandKind kind) => kind == ShowCommandKind.PlayoutTrust;

    /// <summary>The refusal of a desk-only line that came in by <paramref name="door"/>.</summary>
    public static string DeskOnlyWords(CommandDoor door)
    {
        var from = door switch
        {
            CommandDoor.Osc => "OSC",
            CommandDoor.Companion => "Companion TCP",
            CommandDoor.Mcp => "MCP",
            CommandDoor.WebSocket => "a WebSocket",
            _ => "HTTP",
        };
        return $"{PlayoutRules.TrustDeskOnly} ({PlayoutRules.TrustWhere}) — refused from {from}, whoever sends it";
    }

    /// <summary>
    /// Why a line aimed at this Stagewatch is never a proposal (Show Bus §11), or null: a desk-only line — trusting a playout link's
    /// certificate — is a person's choice on this desk beside its fingerprint, so it is refused when it is proposed, never queued for
    /// an approval that could not run it. <paramref name="tokens"/> is the line as the desk splits it (its verb first).
    /// </summary>
    public static string? NeverProposed(IReadOnlyList<string> tokens)
        => tokens.Count > 0 && ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out _) && DeskOnly(cmd.Kind)
            ? $"{PlayoutRules.TrustDeskOnly} ({PlayoutRules.TrustWhere}), never a proposal"
            : null;

    /// <summary>A sender on this PC: a loopback address, an IPv4-mapped one included.</summary>
    public static bool IsLoopback(IPAddress address) => IPAddress.IsLoopback(Plain(address));

    /// <summary>
    /// Null when <paramref name="sender"/> may send a <paramref name="kind"/> line by <paramref name="door"/>; else why not. A desk-only
    /// line (<see cref="DeskOnly"/>) never passes, whoever sends it; any other line that drives no other node's air always passes. A
    /// credential counts only on a door that carries one: OSC and Companion TCP pass
    /// only this PC and the listed addresses, whatever the server says. On a door that carries one, a page of another site passes
    /// only with it.
    /// </summary>
    public static string? Check(ShowCommandKind kind, CommandDoor door, CommandSender sender, IReadOnlyList<IPAddress> from)
    {
        if (DeskOnly(kind)) return DeskOnlyWords(door);
        if (!Drives(kind)) return null;
        var carries = door is CommandDoor.Http or CommandDoor.WebSocket or CommandDoor.Mcp;
        if (sender.Credentialed && carries) return null;
        if (sender.OtherSite && carries) return OtherSiteWords(kind, door);
        if (sender.ThisPc) return null;
        return Listed(sender.Address, from) ? null : Words(kind, door);
    }

    /// <summary>The refusal for a <paramref name="kind"/> line a browser page of another site sent by <paramref name="door"/>, with no credential.</summary>
    public static string OtherSiteWords(ShowCommandKind kind, CommandDoor door)
    {
        var verb = kind == ShowCommandKind.Patterns ? "patterns" : "playout";
        return door switch
        {
            CommandDoor.Mcp => $"{verb} from another site's web page over MCP needs an MCP key while no PIN is set",
            CommandDoor.WebSocket => $"{verb} from another site's web page over a WebSocket needs a control key while no PIN is set",
            _ => $"{verb} from another site's web page over HTTP needs a control key while no PIN is set",
        };
    }

    /// <summary>
    /// Whether a browser page of another site sent a web request: a Sec-Fetch-Site of <c>cross-site</c> or <c>same-site</c> (another origin
    /// on the same host: another port, another app), or an Origin that is not the request's own Host (<c>null</c>, a sandboxed or file
    /// page, included). A browser sets both headers itself and a page's script can neither set nor remove them. With neither, as a device,
    /// a script or Companion sends, it is not; Stagewatch's own pages are same-origin and never are.
    /// </summary>
    public static bool OtherSite(string? origin, string? host, string? secFetchSite)
    {
        var site = (secFetchSite ?? "").Trim().ToLowerInvariant();
        if (site is "cross-site" or "same-site") return true;
        if (site == "same-origin") return false;
        var o = (origin ?? "").Trim();
        if (o.Length == 0) return false;
        if (o.Equals("null", StringComparison.OrdinalIgnoreCase)) return true;
        var scheme = o.IndexOf("://", StringComparison.Ordinal);
        var authority = scheme >= 0 ? o[(scheme + 3)..] : o;
        var slash = authority.IndexOf('/');
        if (slash >= 0) authority = authority[..slash];
        return !authority.Equals((host ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a web request that came in over loopback names this PC's server the way a program on this PC names it: no Host (a device or
    /// a script that sends none), or <c>localhost</c> or a loopback address written as a browser writes it, at <paramref name="port"/> (no
    /// port: 80 or 443). A page whose name a resolver rebinds to 127.0.0.1 keeps its own name there, and a forwarder that carries another
    /// machine's traffic in over loopback its own port: neither is this PC, and its line is judged as another machine's.
    /// </summary>
    public static bool NamesThisPc(string? host, int port)
    {
        var t = (host ?? "").Trim();
        if (t.Length == 0) return true;
        string rest;
        if (t[0] == '[')
        {
            var close = t.IndexOf(']');
            if (close < 0 || !IPAddress.TryParse(t[1..close], out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6 || !IPAddress.IsLoopback(Plain(v6)))
                return false;
            rest = t[(close + 1)..];
        }
        else
        {
            var colon = t.IndexOf(':');
            if (colon >= 0 && t.IndexOf(':', colon + 1) >= 0) return false;   // an IPv6 address unbracketed: no browser sends it
            var name = colon < 0 ? t : t[..colon];
            rest = colon < 0 ? "" : t[colon..];
            var loopbackName = name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(name, out var v4) && v4.AddressFamily == AddressFamily.InterNetwork
                    && v4.ToString().Equals(name, StringComparison.Ordinal) && IPAddress.IsLoopback(v4));
            if (!loopbackName) return false;
        }
        if (rest.Length == 0) return port is 80 or 443;
        var digits = rest[0] == ':' ? rest[1..] : "";
        if (digits.Length is 0 or > 5 || !digits.All(char.IsAsciiDigit) || digits[0] == '0') return false;
        return int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture) == port;
    }

    /// <summary>The refusal for a <paramref name="kind"/> line from an unlisted machine by <paramref name="door"/>.</summary>
    public static string Words(ShowCommandKind kind, CommandDoor door)
    {
        var verb = kind == ShowCommandKind.Patterns ? "patterns" : "playout";
        return door switch
        {
            CommandDoor.Osc => $"{verb} from another machine over OSC needs its address in {SettingWords} (OSC carries no PIN)",
            CommandDoor.Companion => $"{verb} from another machine over Companion TCP needs its address in {SettingWords} (Companion TCP carries no PIN)",
            CommandDoor.Mcp => $"{verb} from another machine over MCP needs an MCP key while no PIN is set, or its address in {SettingWords}",
            CommandDoor.WebSocket => $"{verb} from another machine over a WebSocket needs a control key while no PIN is set, or its address in {SettingWords}",
            _ => $"{verb} from another machine over HTTP needs a control key while no PIN is set, or its address in {SettingWords}",
        };
    }

    /// <summary>Whether <paramref name="sender"/> is one of <paramref name="from"/>, by its address alone (an IPv6 scope is not compared).</summary>
    public static bool Listed(IPAddress sender, IReadOnlyList<IPAddress> from)
    {
        var s = Plain(sender);
        var bytes = s.GetAddressBytes();
        foreach (var listed in from)
        {
            var a = Plain(listed);
            if (a.AddressFamily == s.AddressFamily && a.GetAddressBytes().AsSpan().SequenceEqual(bytes)) return true;
        }
        return false;
    }

    /// <summary>What the operator may list, in words: a note and the desk's box say it.</summary>
    public const string AddressWords = "one IPv4 or IPv6 address each, written out (no names, ranges, ports or loopback)";

    /// <summary>
    /// One address as the list keeps it, or null: an IPv4 address as four numbers (10.0.0.40, nothing .NET would read as octal, hex or
    /// short), or an IPv6 address without brackets or a port; never a name (a sender is known by its address, and a name is a lookup
    /// someone else answers), a range, an address no sender has (0.0.0.0, ::, 255.255.255.255), or a loopback address (this PC is
    /// trusted by its own rule, which a listed loopback address would only widen past: to a page of another site, or a forwarder that
    /// carries another machine in over loopback). An IPv4-mapped IPv6 address is its IPv4 address.
    /// </summary>
    public static IPAddress? Parse(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 64 || text.IndexOfAny(['[', ']', '/', ' ']) >= 0) return null;
        if (!IPAddress.TryParse(text, out var address)) return null;
        if (address.AddressFamily == AddressFamily.InterNetwork && address.ToString() != text) return null;
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !text.Contains(':')) return null;
        address = Plain(address);
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast) || IPAddress.IsLoopback(address)) return null;
        return address;
    }

    /// <summary>
    /// The list as a settings file may keep it, each loss said in <paramref name="notes"/>: every entry an address by <see cref="Parse"/>,
    /// written as .NET writes it; a repeat kept once; the first <see cref="MaxFrom"/>. Fails closed: an entry it can't read is left out,
    /// never widened to something the operator did not write.
    /// </summary>
    public static List<string> Sanitise(IEnumerable<string?>? from, LoadNotes? notes = null)
    {
        var kept = new List<string>();
        var over = 0;
        foreach (var raw in from ?? [])
        {
            if (Parse(raw) is not { } address)
            {
                notes?.Add($"May drive playout: \"{LoadNotes.Shown(raw)}\" left out: {AddressWords}");
                continue;
            }
            var written = address.ToString();
            if (kept.Contains(written)) continue;
            if (kept.Count == MaxFrom) { over++; continue; }
            kept.Add(written);
        }
        if (over > 0)
            notes?.Add($"May drive playout keeps {MaxFrom.ToString(CultureInfo.InvariantCulture)} addresses: {over.ToString(CultureInfo.InvariantCulture)} more left out");
        return kept;
    }

    /// <summary>The listed addresses as the gate compares them: the first <see cref="MaxFrom"/> readable ones, an immutable copy.</summary>
    public static IReadOnlyList<IPAddress> Addresses(IEnumerable<string?>? from)
        => Sanitise(from).Select(a => Parse(a)!).ToArray();

    private static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
