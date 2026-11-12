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
/// Who may act on another show app through this Stagewatch, pure. A <c>playout</c> or <c>patterns</c> line runs on a Patterns
/// desk's or a PlayMap node's air over the link this Stagewatch paired, so it is never relayed for a sender that proved nothing:
/// it comes from this PC, with a credential (the PIN this Stagewatch asks for, or a control or MCP key; only HTTP, WebSocket and
/// MCP carry one, OSC and Companion TCP never), or from an address the operator listed (<see cref="RemoteSettings.PlayoutFrom"/>,
/// at most <see cref="MaxFrom"/>). A browser page of another site proves nothing wherever its browser runs: without a credential it
/// is refused even from this PC or a listed address. Anything else is refused in words naming the door, and nothing is sent.
/// A <c>peer</c> or <c>desk</c> line acts on another app with a person's authority (<see cref="Person"/>): over HTTP and a WebSocket
/// it passes by the same rule — the PIN this Stagewatch asks for, or a control key; with no PIN set, this PC or a listed address,
/// never another site's page — over Companion TCP and OSC, which carry no credential, from this PC or a listed address alone, as a
/// person at a deck (<see cref="DeskOrigin.Deck"/>), and never over MCP, whatever key comes with it (an agent is not a person). A
/// window grant from Companion TCP or OSC is asked, never made (<see cref="Asks"/>): it waits on the desk for the operator. A line in
/// another app's own grammar that an agent proposes over MCP (run_command's target) passes by the rule of a line that drives another
/// node's air (<see cref="Proposes"/>), and then waits for a person. No other verb is this gate's. A listed address is trusted by its source address alone, which any machine on that network can forge
/// over OSC (UDP): the operator lists addresses only on a show network they trust.
/// </summary>
public static class PlayoutGate
{
    /// <summary>The most addresses the operator lists.</summary>
    public const int MaxFrom = 16;

    /// <summary>Where the operator lists them, as every refusal names it.</summary>
    public const string SettingWords = "Settings → Remote → " + SettingName;

    /// <summary>The list's name, as Settings draws it and every note of it says it.</summary>
    public const string SettingName = "May drive other show apps";

    /// <summary>A command that drives another node's air: the lines this gate decides by their sender.</summary>
    public static bool Drives(ShowCommandKind kind) => kind is ShowCommandKind.Playout or ShowCommandKind.Patterns;

    /// <summary>
    /// A command that acts on another show app with a person's authority — a line sent to it born approved, a decision on a proposal:
    /// decided by its sender as a line that drives another node's air is, and never an agent's, the copilot's or a proposal's.
    /// </summary>
    public static bool Person(ShowCommandKind kind) => kind is ShowCommandKind.Peer or ShowCommandKind.Desk;

    /// <summary>
    /// The person's kind a line's first word names (<see cref="ProposalDesk.PersonVerbs"/>), whatever follows it and whether or not
    /// the rest parses; else null. A door that hands no model these verbs asks this before it parses, so a mis-shaped line is
    /// answered as a well-shaped one is — never with the verb's grammar, and never quoted.
    /// </summary>
    public static ShowCommandKind? PersonKind(string? verb)
    {
        foreach (var kind in new[] { ShowCommandKind.Peer, ShowCommandKind.Desk })
            if (string.Equals(Verb(kind), verb?.Trim(), StringComparison.OrdinalIgnoreCase)) return kind;
        return null;
    }

    /// <summary>Why a person's line (<see cref="Person"/>) is refused from what is not a person: an agent, the advisor, a copilot step, a proposal.</summary>
    public static string PersonWords(ShowCommandKind kind) => kind == ShowCommandKind.Desk ? DeskOrigin.PersonDecides : DeskOrigin.PersonSends;

    /// <summary>A door that carries no credential (Companion TCP, OSC): it passes a line by its sender's address alone.</summary>
    public static bool Deck(CommandDoor door) => door is CommandDoor.Osc or CommandDoor.Companion;

    /// <summary>A deck's door as words name it: "OSC" or "Companion TCP"; "" for a door that is no deck's.</summary>
    public static string DeckName(CommandDoor door) => door switch { CommandDoor.Osc => "OSC", CommandDoor.Companion => "Companion TCP", _ => "" };

    /// <summary>The verb a refusal names a line by.</summary>
    private static string Verb(ShowCommandKind kind) => kind switch
    {
        ShowCommandKind.Patterns => "patterns",
        ShowCommandKind.Peer => "peer",
        ShowCommandKind.Desk => "desk",
        _ => "playout",
    };

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
    /// line (<see cref="DeskOnly"/>) never passes, whoever sends it; any other line that neither drives another node's air nor acts
    /// with a person's authority (<see cref="Person"/>) always passes. A person's line never passes over MCP. A credential counts only on
    /// a door that carries one: OSC and Companion TCP pass only this PC and the listed addresses, whatever the server says — the door
    /// then names its sender a deck (<see cref="DeskOrigin.Deck"/>). On a door that carries one, a page of another site passes only
    /// with it.
    /// </summary>
    public static string? Check(ShowCommandKind kind, CommandDoor door, CommandSender sender, IReadOnlyList<IPAddress> from)
    {
        if (DeskOnly(kind)) return DeskOnlyWords(door);
        if (Person(kind))
        {
            if (door == CommandDoor.Mcp) return PersonWords(kind);
        }
        else if (!Drives(kind)) return null;
        var carries = door is CommandDoor.Http or CommandDoor.WebSocket or CommandDoor.Mcp;
        if (sender.Credentialed && carries) return null;
        if (sender.OtherSite && carries) return OtherSiteWords(kind, door);
        if (sender.ThisPc) return null;
        return Listed(sender.Address, from) ? null : Words(kind, door);
    }

    /// <summary>How a refusal of its sender names a line in another show app's own grammar (<see cref="Proposes"/>).</summary>
    public const string TargetLine = "a line for another show app";

    /// <summary>
    /// Null when <paramref name="sender"/> may propose a line in another show app's own grammar by <paramref name="door"/> — MCP's
    /// run_command with a target, which waits for a person at this desk whoever sends it — else why not. It asks for another app's air,
    /// so its sender is decided as the sender of a line that drives another node's air is (<see cref="Drives"/>): a credential on a door
    /// that carries one; else this PC or a listed address, and never a page of another site without one. A person still decides the
    /// line; this keeps an unlisted machine or a web page from filling the desk with lines nobody there may send.
    /// </summary>
    public static string? Proposes(CommandDoor door, CommandSender sender, IReadOnlyList<IPAddress> from)
    {
        var carries = door is CommandDoor.Http or CommandDoor.WebSocket or CommandDoor.Mcp;
        if (sender.Credentialed && carries) return null;
        if (sender.OtherSite && carries) return FromOtherSite(TargetLine, door);
        if (sender.ThisPc) return null;
        return Listed(sender.Address, from) ? null : Unlisted(TargetLine, door);
    }

    /// <summary>
    /// Whether a line grants a window to a show app (<c>window grant &lt;window&gt; &lt;app&gt;</c>): a person's act (Show Bus §12.4), which
    /// a door takes only where it authenticates the operator.
    /// </summary>
    public static bool Grants(ShowCommand cmd) => cmd.Kind == ShowCommandKind.Window && cmd.Get<WindowArgs>() is { Action: "grant" };

    /// <summary>
    /// Null when <paramref name="sender"/> may send <paramref name="cmd"/> by <paramref name="door"/>; else why not. A window grant is a
    /// person's act (Show Bus §12.4), made only from a door that authenticates the operator: over HTTP and a WebSocket it passes only on a
    /// credential proved with it — the PIN this Stagewatch asks for, or a control key — never on an address alone (this PC and a listed
    /// address authenticate nobody); OSC and Companion TCP, which authenticate nobody, pass it from this PC or a listed address only to be
    /// asked (<see cref="Asks"/>): it waits on the desk for the operator, and is refused from any other address; over MCP it passes the
    /// door only to be queued as a proposal a person decides (the endpoint never runs it). Every other line is
    /// <see cref="Check(ShowCommandKind, CommandDoor, CommandSender, IReadOnlyList{IPAddress})"/>'s.
    /// </summary>
    public static string? Check(ShowCommand cmd, CommandDoor door, CommandSender sender, IReadOnlyList<IPAddress> from)
    {
        if (!Grants(cmd)) return Check(cmd.Kind, door, sender, from);
        if (Deck(door)) return sender.ThisPc || Listed(sender.Address, from) ? null : GrantWords(door);
        if (door == CommandDoor.Mcp) return null;
        if (sender.Credentialed) return null;
        return GrantWords(door, sender.OtherSite);
    }

    /// <summary>
    /// Whether a line that passed <paramref name="door"/> is asked of the operator rather than run: a window grant from a deck (Companion
    /// TCP, OSC), which proves no person (Show Bus §12.4 lets such a door queue it). The door names its sender a deck
    /// (<see cref="DeskOrigin.Deck"/>), and the host queues the grant on the desk (<see cref="DeskOrigin.Asks"/>), never makes it.
    /// </summary>
    public static bool Asks(ShowCommand cmd, CommandDoor door) => Deck(door) && Grants(cmd);

    /// <summary>
    /// The sender a deck's door names on a line it passes that acts with a person's authority or is asked of the operator, or null for
    /// any other line: a deck by its door's name, with its address for the desk's journal alone.
    /// </summary>
    public static DeskOrigin? DeckSender(ShowCommand cmd, CommandDoor door, IPAddress address)
        => Deck(door) && (Person(cmd.Kind) || Grants(cmd))
            ? DeskOrigin.Deck($"{DeckName(door)}, {Plain(address)}", DeckName(door))
            : null;

    /// <summary>
    /// What a deck (Companion TCP, OSC) at <paramref name="sender"/> may do with a person's lines now, as Companion's <c>ACCESS</c> line
    /// says it to each connection: <c>{"peer":…,"desk":…,"why":"…","grant":…}</c> — whether a <c>peer</c> line and a <c>desk</c> decision
    /// pass, why not (empty when they pass), and whether a window grant is taken to be asked of the operator. One rule decides all
    /// three, this PC or a listed address (<see cref="Check(ShowCommandKind, CommandDoor, CommandSender, IReadOnlyList{IPAddress})"/>), so a
    /// deck greys its buttons by the server's own answer.
    /// </summary>
    public static string AccessJson(CommandDoor door, CommandSender sender, IReadOnlyList<IPAddress> from)
    {
        var peer = Check(ShowCommandKind.Peer, door, sender, from) is null;
        var desk = Check(ShowCommandKind.Desk, door, sender, from) is null;
        var grant = Deck(door) && (sender.ThisPc || Listed(sender.Address, from));
        var why = peer && desk ? "" : AccessWords(door);
        return System.Text.Json.JsonSerializer.Serialize(new { peer, desk, why, grant });
    }

    /// <summary>Why a deck at an unlisted address may send no person's line: what Companion's <c>ACCESS</c> line says.</summary>
    public static string AccessWords(CommandDoor door)
        => $"peer, desk and window grants from another machine over {DeckName(door)} need its address in {SettingWords} ({DeckName(door)} carries no PIN)";

    /// <summary>Why a window grant is refused at <paramref name="door"/> (Show Bus §12.4: a person's act, made only where the operator is authenticated).</summary>
    public static string GrantWords(CommandDoor door, bool otherSite = false) => door switch
    {
        CommandDoor.Osc or CommandDoor.Companion =>
            $"window grant from another machine over {DeckName(door)} needs its address in {SettingWords} — and then it waits for the operator's approval at the desk ({DeckName(door)} carries no PIN)",
        CommandDoor.Mcp => "window grant is a person's act — an agent proposes it, and a person decides",
        _ when otherSite =>
            $"window grant from another site's web page over {(door == CommandDoor.WebSocket ? "a WebSocket" : "HTTP")} needs a control key while no PIN is set",
        _ => $"window grant is a person's act — over {(door == CommandDoor.WebSocket ? "a WebSocket" : "HTTP")} it needs the PIN given (set one in Settings → Remote) "
             + "or a control key, from this PC or any other: grant it at the desk (Screens → Windows) otherwise",
    };

    /// <summary>The refusal for a <paramref name="kind"/> line a browser page of another site sent by <paramref name="door"/>, with no credential.</summary>
    public static string OtherSiteWords(ShowCommandKind kind, CommandDoor door) => FromOtherSite(Verb(kind), door);

    /// <summary>The refusal of a line named <paramref name="verb"/> that a browser page of another site sent by <paramref name="door"/>, with no credential.</summary>
    private static string FromOtherSite(string verb, CommandDoor door)
    {
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
        var verb = Verb(kind);
        if (Person(kind) && Deck(door))
            return $"{verb} acts on another show app with a person's authority — from another machine over {DeckName(door)} it needs its address in {SettingWords} "
                   + $"({DeckName(door)} carries no PIN)";
        if (Person(kind))
            return $"{verb} acts on another show app with a person's authority — from another machine over {(door == CommandDoor.WebSocket ? "a WebSocket" : "HTTP")} "
                   + $"it needs a PIN set and given, a control key, or its address in {SettingWords}";
        return Unlisted(verb, door);
    }

    /// <summary>The refusal of a line named <paramref name="verb"/> from an unlisted machine by <paramref name="door"/>, where no person's rule applies.</summary>
    private static string Unlisted(string verb, CommandDoor door)
    {
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
                notes?.Add($"{SettingName}: \"{LoadNotes.Shown(raw)}\" left out: {AddressWords}");
                continue;
            }
            var written = address.ToString();
            if (kept.Contains(written)) continue;
            if (kept.Count == MaxFrom) { over++; continue; }
            kept.Add(written);
        }
        if (over > 0)
            notes?.Add($"{SettingName} keeps {MaxFrom.ToString(CultureInfo.InvariantCulture)} addresses: {over.ToString(CultureInfo.InvariantCulture)} more left out");
        return kept;
    }

    /// <summary>
    /// The list as Settings' box gives it — addresses separated by commas, semicolons, spaces or lines — kept by the rules a settings
    /// file's list keeps (<see cref="Sanitise"/>), with each entry left out said in <c>Notes</c>: a person sees what was not kept before
    /// anything relies on it. Fails closed: nothing is widened to an address the person did not write.
    /// </summary>
    public static (List<string> Kept, IReadOnlyList<string> Notes) Typed(string? text)
    {
        var notes = new LoadNotes();
        var kept = Sanitise((text ?? "").Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries), notes);
        return (kept, notes.Words);
    }

    /// <summary>The list as Settings' box shows it: one address a line.</summary>
    public static string Shown(IEnumerable<string>? from) => string.Join(Environment.NewLine, from ?? []);

    /// <summary>The listed addresses as the gate compares them: the first <see cref="MaxFrom"/> readable ones, an immutable copy.</summary>
    public static IReadOnlyList<IPAddress> Addresses(IEnumerable<string?>? from)
        => Sanitise(from).Select(a => Parse(a)!).ToArray();

    private static IPAddress Plain(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
