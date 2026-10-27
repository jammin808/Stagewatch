using System.Globalization;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// One playout link (Settings → Integrations): a Patterns desk or a PlayMap node whose clip on air may feed the VT countdown
/// and takes the <c>playout</c> verbs. <see cref="Name"/> is how the operator, the wire and the running order name it
/// (<c>playout stage-left go …</c>), and the only thing its context ids are built from (<c>link:&lt;name&gt;</c>,
/// <c>clock:&lt;name&gt;</c>), so it keeps <see cref="PlayoutRules.ValidName"/>. Its peer is the node <see cref="Node"/> names,
/// of the <see cref="Kind"/> set here or observed. The token is DPAPI-protected at rest, as every secret in the settings.
/// </summary>
public sealed class PlayoutLinkSettings
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    /// <summary><see cref="PlayoutRules.Auto"/> (its hello and STATE say), <see cref="PlayoutRules.Patterns"/> or <see cref="PlayoutRules.PlayMap"/>.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = PlayoutRules.Auto;
    [JsonPropertyName("host")] public string Host { get; set; } = PlayoutRules.DefaultHost;
    [JsonPropertyName("port")] public int Port { get; set; } = PlayoutRules.PatternsPort;
    /// <summary>The peer's pairing token, DPAPI-protected. Empty = the peer asks for none.</summary>
    [JsonPropertyName("token")] public string TokenProtected { get; set; } = "";
    /// <summary>The Show Bus id of the node this link is to, "" until one is chosen or heard: the id its certificate's pin is kept under.</summary>
    [JsonPropertyName("node")] public string Node { get; set; } = "";
    /// <summary><see cref="PlayoutRules.Auto"/>, <see cref="PlayoutRules.Tcp"/> or <see cref="PlayoutRules.Wss"/>.</summary>
    [JsonPropertyName("transport")] public string Transport { get; set; } = PlayoutRules.Auto;
    /// <summary>Its clip on air may feed the VT countdown.</summary>
    [JsonPropertyName("followVt")] public bool FollowVt { get; set; } = true;
    /// <summary>When its clip ends on a video item (kind Video, or labelled VT), the running order goes to the next item.</summary>
    [JsonPropertyName("nextOnEnd")] public bool NextOnEnd { get; set; }

    public PlayoutLinkSettings Clone() => (PlayoutLinkSettings)MemberwiseClone();

    /// <summary>This link as Settings' dialog starts an edit of it, its token kept (null) unless a person types another.</summary>
    public PlayoutLinkEdit Edit() => new(Name, Enabled, Kind, Host, Port, null, Node, Transport, FollowVt, NextOnEnd);
}

/// <summary>
/// A link as Settings' Add and Edit dialog gives it (<see cref="PlayoutRules.EditLink"/>): every field a person sets, the token as
/// protected text — null keeps the one kept, "" keeps none.
/// </summary>
public sealed record PlayoutLinkEdit(string Name, bool Enabled, string Kind, string Host, int Port, string? TokenProtected, string Node,
    string Transport, bool FollowVt, bool NextOnEnd);

/// <summary>
/// One playout cue on a running-order item (at most <see cref="PlayoutRules.MaxCuesPerItem"/>): what goes on <see cref="Link"/>
/// when the item starts and, with <see cref="Standby"/>, what stands by when it loads. A PlayMap cue names a playlist
/// (<see cref="List"/>) and its item; a Patterns cue names one cue in <see cref="Item"/>, <see cref="List"/> empty. Ids, never names:
/// they are picked from the peer's own lists, and <see cref="Label"/> is how people read them.
/// </summary>
public sealed class PlayoutCue
{
    [JsonPropertyName("link")] public string Link { get; set; } = "";
    [JsonPropertyName("list")] public string List { get; set; } = "";
    [JsonPropertyName("item")] public string Item { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    /// <summary>
    /// Stand it by when the item loads. Off unless the operator turns it on: a PlayMap standby shows the item's first frame on air
    /// at load, and a Patterns standby moves the Patterns desk's own standby.
    /// </summary>
    [JsonPropertyName("standby")] public bool Standby { get; set; }
    /// <summary>GO at one instant with the item's other cues marked so, where each link's clock allows it.</summary>
    [JsonPropertyName("together")] public bool Together { get; set; }

    public PlayoutCue Clone() => (PlayoutCue)MemberwiseClone();
}

/// <summary>
/// A playout command's arguments: the link it names ("" = <see cref="PlayoutRules.Target"/> picks one), its action, and for cue and
/// go what to cue or start. Two words are a PlayMap playlist (<see cref="List"/>) and its item; one word is in <see cref="Item"/>
/// alone, a Patterns cue or a PlayMap playlist whose next item goes. <see cref="Together"/> asks go to start at one instant with the
/// other links' GOs where their clocks allow. The seconds of end and seek ride in the command's Number.
/// </summary>
public sealed record PlayoutArgs(string Link, string Action, string List, string Item, bool Together) : ShowArgs;

/// <summary>
/// <c>playout &lt;link&gt; trust &lt;hex&gt;</c>: the link whose certificate a person trusts, and the hex they gave of it — 8 to 64 lowercase
/// hex (<see cref="PlayoutRules.TrustHex"/>). Only this desk's own entry beside the fingerprint runs it; a line of it is refused
/// wherever it comes from (<see cref="PlayoutRules.TrustLineWords"/>).
/// </summary>
public sealed record PlayoutTrustArgs(string Link, string Hex) : ShowArgs;

/// <summary>
/// The rules of playout links, pure: their names; their settings as loaded and saved (schema 2, the 2.17 Patterns keys written as
/// link "patterns"'s mirror); the running-order cues; which link a playout command reaches and what this Stagewatch does with it;
/// and the words a refusal uses. Every reader of the links and cues takes them through here.
/// </summary>
public static class PlayoutRules
{
    /// <summary>The most links one Stagewatch keeps.</summary>
    public const int Max = 8;
    public const string Auto = "auto", Patterns = "patterns", PlayMap = "playmap", Tcp = "tcp", Wss = "wss";
    public const string DefaultHost = "127.0.0.1";
    public const int PatternsPort = 9697, PlayMapPort = 9941;
    /// <summary>The link a 2.17 Patterns setup becomes, and the one <c>patterns …</c> means: its items stay link:patterns and clock:patterns.</summary>
    public const string PatternsName = "patterns";
    public const int MaxNameChars = 24;
    /// <summary>A timed GO is set this far ahead (ms) unless the operator says otherwise, within <see cref="MinLeadMs"/>–<see cref="MaxLeadMs"/>.</summary>
    public const int DefaultLeadMs = 300, MinLeadMs = 100, MaxLeadMs = 2000;
    public const int MaxCuesPerItem = 4, MaxRefChars = 64, MaxLabelChars = 80;

    /// <summary>
    /// What a playout command does, the first word after the link's name. No link is ever named one, so <c>playout pause</c> and
    /// <c>playout stage-left pause</c> read apart without a separator.
    /// </summary>
    public static readonly IReadOnlyList<string> Actions = ["end", "restart", "play", "pause", "stop", "seek", "cue", "go"];

    /// <summary>The actions that move the clip already on air; cue and go put something new there.</summary>
    public static readonly IReadOnlyList<string> TransportActions = ["end", "restart", "play", "pause", "stop", "seek"];

    /// <summary>
    /// Names no link may take: the other integrations' context slugs (their items are <c>link:&lt;slug&gt;</c>, as a link's are) and
    /// the assistant's, so a link never shares an id with them.
    /// </summary>
    public static readonly IReadOnlyList<string> Reserved =
        ["powerpoint", "presenter-pcs", "vmix", "speech", "time-server", "ltc", "ltc-in", "cue-lights", "triggers", "rooms", "assistant"];

    /// <summary>"playout links need Stagewatch Production": every refusal for the licence says it in these words.</summary>
    public static string LicenceWords => "playout links need Stagewatch " + Pricing.TierName(FeatureTier.Production);

    public static bool IsAction(string? word) => word is not null && Actions.Contains(word.ToLowerInvariant());

    /// <summary>
    /// The word after a link's name that trusts its certificate: <c>playout stage-left trust 03e9b152</c>. No action of the clip's,
    /// and no link is named it, so it reads apart from both.
    /// </summary>
    public const string TrustWord = "trust";

    /// <summary>The fewest hex of a certificate a person types to trust it after CERTIFICATE CHANGED (Show Bus 13.8.3).</summary>
    public const int TrustHexMin = 8;

    /// <summary>
    /// What every surface but this desk's own entry hears, and the rest of the refusal names where the line came from: trusting a
    /// link's certificate is a person's choice beside its fingerprint, never a line's — not the wire's, a deck's, a trigger's or an
    /// assistant's.
    /// </summary>
    public const string TrustDeskOnly = "trusting a playout link's certificate is a person's choice on the Stagewatch desk, beside its fingerprint";

    /// <summary>
    /// Where on the desk the choice is made, as every refusal of a line says it: the node's Show network row, or the status line's
    /// right-click, which lists every link's certificate that waits — a node with no row (its hello not heard) among them.
    /// </summary>
    public const string TrustWhere = "right-click its node on the Show network, or the status line";

    /// <summary>The refusal of a <c>playout … trust</c> line that reached the show thread (a trigger, an approved proposal): never run.</summary>
    public static string TrustLineWords => TrustDeskOnly + " (" + TrustWhere + "), never a line's — not run";

    /// <summary>
    /// The hex a person gave of a certificate, as it is compared, or null: <c>sha256:</c> left off, spaces dropped, either case, and
    /// <see cref="TrustHexMin"/> to 64 hex digits — the short form a desk shows ("03e9 b152") typed as it reads.
    /// </summary>
    public static string? TrustHex(string? typed)
    {
        var t = (typed ?? "").Trim();
        if (t.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) t = t[7..];
        var hex = new string(t.Where(c => !char.IsWhiteSpace(c)).Select(char.ToLowerInvariant).ToArray());
        return hex.Length is >= TrustHexMin and <= 64 && hex.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? hex : null;
    }

    /// <summary>Null when <paramref name="name"/> may name a link; else why not, in words.</summary>
    public static string? NameProblem(string? name)
    {
        if (name is not { Length: > 0 and <= MaxNameChars } || !IsNameStart(name[0]) || !name.All(c => IsNameStart(c) || c == '-'))
            return $"a playout link is named with 1 to {MaxNameChars.ToString(CultureInfo.InvariantCulture)} of a-z, 0-9 and -, starting with a letter or digit (stage-left)";
        if (IsAction(name) || name == TrustWord) return $"a playout link can't be called {name}: that is one of its commands";
        if (Reserved.Contains(name)) return $"a playout link can't be called {name}: another integration has that name";
        return null;

        static bool IsNameStart(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9');
    }

    public static bool ValidName(string? name) => NameProblem(name) is null;

    /// <summary>A link's name as people read it: "stage-left" is "Stage left", "patterns" is "Patterns".</summary>
    public static string Label(string name)
        => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..].Replace('-', ' ');

    /// <summary>A playlist, item or cue id as a running-order cue keeps it: 1–64 of letters, digits, '-' and '_' (a Patterns cue's id among them).</summary>
    public static bool ValidRef(string? id) => id is { Length: > 0 and <= MaxRefChars } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// A playlist, item or cue as a playout command names it: 1–64 characters and none a control character. An id, a name or
    /// PlayMap's <c>#4</c>; the link quotes it on its line.
    /// </summary>
    public static bool ValidWord(string? word) => word is { Length: > 0 and <= MaxRefChars } && !word.Any(char.IsControl);

    /// <summary>The link called <paramref name="name"/> (names are lower case), or null.</summary>
    public static PlayoutLinkSettings? Find(IEnumerable<PlayoutLinkSettings>? links, string name)
        => links?.FirstOrDefault(l => l.Name == name);

    /// <summary>Link "patterns", or null. What the Patterns link, <c>patterns …</c> and the 2.17 keys are.</summary>
    public static PlayoutLinkSettings? PatternsLink(IntegrationSettings i) => Find(i.PlayoutLinks, PatternsName);

    /// <summary>The port a kind listens on for its line wire unless told otherwise.</summary>
    public static int DefaultPort(string kind) => kind == PlayMap ? PlayMapPort : PatternsPort;

    // ------------------------------------------------------------------ settings, as loaded and saved

    /// <summary>
    /// The links as a settings file may keep them, each change or loss said in <paramref name="notes"/> (by name, never a token):
    /// <list type="bullet">
    /// <item>a link left out whole: no name by the name rule, a name another link has, or past the first <see cref="Max"/>;</item>
    /// <item>kind <see cref="Auto"/> when the file gives none; a kind word this Stagewatch does not know leaves the link switched off
    /// (a later Stagewatch's kind is never dialled as one this one guesses);</item>
    /// <item>transport <see cref="Auto"/> when the file gives none; a word this Stagewatch does not know is <see cref="Wss"/>, the
    /// strictest, never <see cref="Auto"/>, which may dial plain;</item>
    /// <item>the address by <see cref="Settle"/>, the one rule every link and the 2.17 keys keep;</item>
    /// <item>the node a Show Bus id, and no node two links' (the first keeps it; a later one finds its node again).</item>
    /// </list>
    /// </summary>
    public static List<PlayoutLinkSettings> Sanitise(IEnumerable<PlayoutLinkSettings?>? links, LoadNotes? notes = null)
    {
        var kept = new List<PlayoutLinkSettings>();
        foreach (var l in links ?? [])
        {
            if (l is null) continue;
            var name = (l.Name ?? "").Trim().ToLowerInvariant();
            if (NameProblem(name) is { } why) { notes?.Add($"playout link \"{LoadNotes.Shown(l.Name)}\" left out: {why}"); continue; }
            if (kept.Any(k => k.Name == name)) { notes?.Add($"a second playout link called {name} left out: a name names one link"); continue; }
            if (kept.Count == Max) { notes?.Add($"playout link {name} left out: Stagewatch keeps {Max.ToString(CultureInfo.InvariantCulture)} playout links"); continue; }
            var link = new PlayoutLinkSettings
            {
                Name = name,
                Enabled = l.Enabled,
                Host = l.Host ?? "",
                Port = l.Port,
                TokenProtected = l.TokenProtected ?? "",
                FollowVt = l.FollowVt,
                NextOnEnd = l.NextOnEnd,
            };
            var kind = Word(l.Kind);
            link.Kind = kind.Length == 0 ? Auto : kind is Auto or Patterns or PlayMap ? kind : Auto;
            if (kind.Length > 0 && link.Kind != kind)
            {
                link.Enabled = false;
                notes?.Add($"playout link {name} is switched off: its kind \"{LoadNotes.Shown(kind)}\" isn't one this Stagewatch knows (auto, patterns or playmap)");
            }
            var transport = Word(l.Transport);
            link.Transport = transport.Length == 0 ? Auto : transport is Auto or Tcp or Wss ? transport : Wss;
            if (transport.Length > 0 && link.Transport != transport)
                notes?.Add($"playout link {name} dials wss only: its transport \"{LoadNotes.Shown(transport)}\" isn't one this Stagewatch knows (auto, tcp or wss)");
            Settle(link, notes);
            var node = (l.Node ?? "").Trim();
            if (node.Length > 0 && !NodeBindings.ValidId(node))
            {
                notes?.Add($"playout link {name} will find its node again: \"{LoadNotes.Shown(node)}\" isn't a Show Bus id");
                node = "";
            }
            if (node.Length > 0 && kept.FirstOrDefault(k => k.Node == node) is { } first)
            {
                notes?.Add($"playout link {name} will find its node again: node {node} is link {first.Name}'s, and one node has one link");
                node = "";
            }
            link.Node = node;
            kept.Add(link);
        }
        return kept;
    }

    /// <summary>
    /// A link's address by the one rule a settings file's links and the 2.17 keys both keep: a blank host is this PC; a host it cannot
    /// dial (<see cref="ValidHost"/>: a port, a path or a space in it) or a port out of 1–65535 is never replaced by an address the
    /// operator did not type and then dialled — the link is switched off at its kind's default address, and the note quotes what the
    /// file said.
    /// </summary>
    private static void Settle(PlayoutLinkSettings link, LoadNotes? notes)
    {
        var host = (link.Host ?? "").Trim();
        if (host.Length == 0) host = DefaultHost;
        if (!ValidHost(host))
        {
            notes?.Add($"playout link {link.Name} is switched off at {DefaultHost}: \"{LoadNotes.Shown(host)}\" isn't an address it can dial (a name or an address; the port has its own box)");
            link.Enabled = false;
            host = DefaultHost;
        }
        link.Host = host;
        if (link.Port is not (> 0 and < 65536))
        {
            notes?.Add($"playout link {link.Name} is switched off at port {DefaultPort(link.Kind).ToString(CultureInfo.InvariantCulture)}: {link.Port.ToString(CultureInfo.InvariantCulture)} isn't a port (1-65535)");
            link.Enabled = false;
            link.Port = DefaultPort(link.Kind);
        }
    }

    /// <summary>
    /// A host a link may dial: a name or an address, no space, control character, path or user in it, and a ':' only inside an
    /// IPv6 address (the port is a field of its own).
    /// </summary>
    public static bool ValidHost(string? host)
        => host is { Length: > 0 and <= 253 } && !host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '\\' or '@' or '?' or '#')
           && (!host.Contains(':') || host.Trim('[', ']') is var v6 && !v6.Contains('[') && !v6.Contains(']')
               && System.Net.IPAddress.TryParse(v6, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);

    /// <summary>
    /// The settings as every load leaves them (schema 2): the links made sound (<see cref="Sanitise"/>), the lead within its range,
    /// and link "patterns" in step with the 2.17 keys. Those keys are what <see cref="WriteMirror"/> writes; when one says something
    /// the mirror would not — a 2.17 file, which has no links, or keys an older Stagewatch changed after a rollback — the keys win for
    /// link "patterns", made first in the list when there is none (the last link then gives way if the list was full), and its
    /// address then kept by the same rule as every link's (<see cref="Settle"/>). Keys absent from the file say nothing and change
    /// nothing. Each change a person did not make is said in <paramref name="notes"/>.
    /// </summary>
    public static void Upgrade(IntegrationSettings i, LoadNotes? notes = null)
    {
        var links = Sanitise(i.PlayoutLinks, notes);
        var mirrored = Mirrored(links);
        var legacy = Legacy.Read(i);
        if (legacy.Differs(Mirror.Of(mirrored)))
        {
            var own = Find(links, PatternsName);
            // A key the file does not hold leaves the link as it was; a link made here starts from what 2.17 reads then.
            var was = Mirror.Of(own);
            if (own is null)
            {
                own = new PlayoutLinkSettings { Name = PatternsName };
                links.Insert(0, own);
                if (links.Count > Max)
                {
                    notes?.Add($"playout link {links[^1].Name} left out to make room for link patterns, which the 2.17 Patterns settings name: Stagewatch keeps {Max.ToString(CultureInfo.InvariantCulture)} playout links");
                    links.RemoveAt(links.Count - 1);
                }
            }
            else if (own.Kind == PlayMap)
            {
                // The keys only ever described a Patterns desk: the PlayMap link that held the name gives it up to it.
                notes?.Add("playout link patterns is a Patterns desk's again, not a PlayMap node's: the 2.17 Patterns settings, changed by an older Stagewatch, name it");
                own.Kind = Auto;
                own.Node = "";
                own.Transport = Auto;
            }
            own.Enabled = legacy.On ?? was.On;
            own.Host = legacy.Host ?? was.Host;
            own.Port = legacy.Port ?? was.Port;
            own.TokenProtected = legacy.Token ?? was.Token;
            own.NextOnEnd = legacy.NextOnEnd ?? was.NextOnEnd;
            Settle(own, notes);
        }
        i.PlayoutLinks = links;
        i.PlayoutLeadMs = Math.Clamp(i.PlayoutLeadMs, MinLeadMs, MaxLeadMs);
        WriteMirror(i);
    }

    /// <summary>
    /// The 2.17 Patterns keys written as the mirror of link "patterns" (of a Patterns desk or a kind not yet known), so a Stagewatch
    /// rolled back to 2.17 runs that link as its Patterns link: switched off, with nothing else said, when there is none. Every save
    /// writes it (2.17 keeps no <c>playoutLinks</c>, so after a rollback only link "patterns" survives).
    /// </summary>
    public static void WriteMirror(IntegrationSettings i)
    {
        var own = Mirrored(i.PlayoutLinks);
        i.LegacyPatterns = own?.Enabled ?? false;
        i.LegacyPatternsHost = own?.Host;
        i.LegacyPatternsPort = own?.Port;
        i.LegacyPatternsToken = own?.TokenProtected;
        i.LegacyPatternsNextOnEnd = own?.NextOnEnd;
    }

    // ------------------------------------------------------------------ Settings' own edits

    /// <summary>
    /// Add a link (<paramref name="was"/> null) to <paramref name="links"/>, Settings' copy, or change link <paramref name="was"/> there:
    /// null when written; else why not, in words naming the box, and nothing changed. Each rule a settings file's links keep
    /// (<see cref="Sanitise"/>) is said here rather than mended: a name by the name rule that no other link has; at most
    /// <see cref="Max"/> links; a kind and a transport this Stagewatch knows, and no TLS asked of a Patterns desk, whose wire offers
    /// none; an address it can dial (<see cref="ValidHost"/>, a blank one being this PC) and a port of 1–65535; a node a Show Bus id
    /// that no other link has. A token of null keeps the one kept, "" keeps none. A link added goes last; one changed keeps its place.
    /// A renamed link's running-order cues are the caller's to move (<see cref="RenameCues"/>).
    /// </summary>
    public static string? EditLink(List<PlayoutLinkSettings> links, string? was, PlayoutLinkEdit e)
    {
        var name = (e.Name ?? "").Trim().ToLowerInvariant();
        var own = was is null ? null : Find(links, was);
        if (was is not null && own is null) return $"no playout link called {was} to change";
        if (NameProblem(name) is { } why) return Capital(why);
        if (links.Any(l => l.Name == name && !ReferenceEquals(l, own))) return $"Another playout link is called {name}: a name names one link";
        if (own is null && links.Count >= Max)
            return $"Stagewatch keeps {Max.ToString(CultureInfo.InvariantCulture)} playout links and has {Max.ToString(CultureInfo.InvariantCulture)} already — remove one first";
        var kind = Word(e.Kind);
        if (kind.Length == 0) kind = Auto;
        if (kind is not (Auto or Patterns or PlayMap)) return $"\"{LoadNotes.Shown(e.Kind)}\" isn't a kind of link this Stagewatch knows (auto, patterns or playmap)";
        var transport = Word(e.Transport);
        if (transport.Length == 0) transport = Auto;
        if (transport is not (Auto or Tcp or Wss)) return $"\"{LoadNotes.Shown(e.Transport)}\" isn't a transport this Stagewatch knows (auto, tcp or wss)";
        if (kind == Patterns && transport == Wss)
            return "A Patterns desk offers no TLS on its wire: its link runs over TCP, paired by proof (transport auto or tcp)";
        var host = (e.Host ?? "").Trim();
        if (host.Length == 0) host = DefaultHost;
        if (!ValidHost(host)) return $"\"{LoadNotes.Shown(host)}\" isn't an address a link can dial (a name or an address; the port has its own box)";
        if (e.Port is not (> 0 and < 65536)) return $"{e.Port.ToString(CultureInfo.InvariantCulture)} isn't a port (1-65535)";
        var node = (e.Node ?? "").Trim();
        if (node.Length > 0 && !NodeBindings.ValidId(node))
            return $"\"{LoadNotes.Shown(node)}\" isn't a Show Bus node id (1-40 letters, digits, - or _) — leave it empty and the link finds its node";
        if (node.Length > 0 && links.FirstOrDefault(l => l.Node == node && !ReferenceEquals(l, own)) is { } other)
            return $"Node {node} is link {other.Name}'s, and one node has one link";
        if (own is null)
        {
            own = new PlayoutLinkSettings();
            links.Add(own);
        }
        own.Name = name;
        own.Enabled = e.Enabled;
        own.Kind = kind;
        own.Host = host;
        own.Port = e.Port;
        if (e.TokenProtected is not null) own.TokenProtected = e.TokenProtected;
        own.Node = node;
        own.Transport = transport;
        own.FollowVt = e.FollowVt;
        own.NextOnEnd = e.NextOnEnd;
        return null;

        static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>
    /// A name for a new link to a peer heard as <paramref name="heard"/> (its own name, as its hello gives it): lower case, each run of
    /// other characters one '-', cut to fit; else the name of its <paramref name="kind"/> (or "link"); then one no name in
    /// <paramref name="taken"/> has, by a number after it ("stage-left-2"). Always a name <see cref="NameProblem"/> takes.
    /// </summary>
    public static string SuggestName(string? heard, string? kind, IEnumerable<string> taken)
    {
        var names = taken.ToHashSet(StringComparer.Ordinal);
        var b = new System.Text.StringBuilder();
        foreach (var c in (heard ?? "").ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9')) b.Append(c);
            else if (b.Length > 0 && b[^1] != '-') b.Append('-');
        }
        var stem = b.ToString().Trim('-');
        if (stem.Length > MaxNameChars - 3) stem = stem[..(MaxNameChars - 3)].TrimEnd('-');
        if (!ValidName(stem)) stem = kind is Patterns or PlayMap ? kind : "link";
        if (!names.Contains(stem)) return stem;
        for (var n = 2; ; n++)
            if (stem + "-" + n.ToString(CultureInfo.InvariantCulture) is var name && !names.Contains(name)) return name;
    }

    /// <summary>The running orders this Stagewatch keeps: the live one and every other one of the show.</summary>
    private static IEnumerable<Rundown> Rundowns(AppSettings s)
        => new[] { s.Rundown }.Concat(s.OtherRundowns.Select(r => r.Rundown)).OfType<Rundown>();

    /// <summary>How many running-order cues of the show's running orders name link <paramref name="name"/>.</summary>
    public static int CuesNaming(AppSettings s, string name)
        => Rundowns(s).Sum(r => r.Items.Sum(i => i?.Playout?.Count(c => c?.Link == name) ?? 0));

    /// <summary>
    /// How many of the show's running-order cues will name link <paramref name="name"/> once <see cref="RenameCues"/> has moved them
    /// through <paramref name="renames"/>, in their order, as Save does: walked back from the last rename, a name renamed away no longer
    /// names it, one renamed onto it does, and cues that already named a rename's new name keep it (and are counted).
    /// </summary>
    public static int CuesNaming(AppSettings s, string name, IReadOnlyList<(string From, string To)> renames)
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { name };
        for (var i = renames.Count - 1; i >= 0; i--)
        {
            var (from, to) = renames[i];
            var landsHere = names.Contains(to);
            names.Remove(from);
            if (landsHere) names.Add(from);
        }
        return names.Sum(n => CuesNaming(s, n));
    }

    /// <summary>
    /// The link of <paramref name="links"/> that already reaches a peer found at <paramref name="host"/>:<paramref name="port"/> with Show
    /// Bus id <paramref name="nodeId"/> ("" for none): one set to that node (one node has one link, as <see cref="EditLink"/> keeps), else
    /// one dialling that address and port; null for none.
    /// </summary>
    public static PlayoutLinkSettings? Reaching(IEnumerable<PlayoutLinkSettings> links, string nodeId, string host, int port)
    {
        var all = links.ToList();
        return (nodeId.Length > 0 ? all.FirstOrDefault(l => l.Node == nodeId) : null)
               ?? all.FirstOrDefault(l => l.Port == port && string.Equals(l.Host.Trim(), host.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A link renamed in Settings takes its running-order cues with it, in every running order of the show (one in a show file not
    /// open moves only when that file is): the cues naming <paramref name="from"/> name <paramref name="to"/>. How many moved.
    /// </summary>
    public static int RenameCues(AppSettings s, string from, string to)
    {
        var moved = 0;
        foreach (var cue in Rundowns(s).SelectMany(r => r.Items).SelectMany(i => i?.Playout ?? []).Where(c => c?.Link == from))
        {
            cue.Link = to;
            moved++;
        }
        return moved;
    }

    /// <summary>
    /// The lead of a GO at one instant as Settings' box gives it, in ms: null when it is a whole number within
    /// <see cref="MinLeadMs"/>–<see cref="MaxLeadMs"/>, else why not (and the lead kept is unchanged).
    /// </summary>
    public static string? LeadProblem(string? typed, out int ms)
    {
        ms = 0;
        if (int.TryParse((typed ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= MinLeadMs and <= MaxLeadMs)
        {
            ms = n;
            return null;
        }
        return $"The lead of a GO at one instant is {MinLeadMs.ToString(CultureInfo.InvariantCulture)} to {MaxLeadMs.ToString(CultureInfo.InvariantCulture)} ms — \"{LoadNotes.Shown(typed)}\" isn't";
    }

    /// <summary>
    /// How Settings' list shows a link, beside its own status: its name and peer, where it dials, its kind and transport as set, and
    /// what it feeds — "stage-left · PlayMap Stage left at 10.0.0.12:9941 · wss · VT, next on end" — "(off)" when switched off.
    /// </summary>
    public static string Describe(PlayoutLinkSettings l)
    {
        var host = l.Host.Contains(':') ? "[" + l.Host + "]" : l.Host;
        var peer = Peer(l.Kind == Auto ? null : l.Kind, l.Name);
        var feeds = (l.FollowVt ? "VT" : "") + (l.NextOnEnd ? (l.FollowVt ? ", next on end" : "next on end") : "");
        return $"{l.Name} · {peer} at {host}:{l.Port.ToString(CultureInfo.InvariantCulture)}"
               + (l.Kind == Auto ? " · kind heard" : "") + (l.Transport != Auto ? " · " + l.Transport : "")
               + (feeds.Length > 0 ? " · " + feeds : "") + (l.Node.Length > 0 ? " · node " + l.Node : "") + (l.Enabled ? "" : " (off)");
    }

    /// <summary>Link "patterns" when the 2.17 keys can describe it: a Patterns desk, or a peer whose kind is not set.</summary>
    private static PlayoutLinkSettings? Mirrored(IEnumerable<PlayoutLinkSettings>? links)
        => Find(links, PatternsName) is { } own && own.Kind != PlayMap ? own : null;

    /// <summary>A settings word as it is compared: trimmed and lower case, "" for none.</summary>
    private static string Word(string? word) => (word ?? "").Trim().ToLowerInvariant();

    /// <summary>What the 2.17 keys say of a Patterns link, with the defaults 2.17 reads when a key is absent.</summary>
    private readonly record struct Mirror(bool On, string Host, int Port, string Token, bool NextOnEnd)
    {
        public static Mirror Of(PlayoutLinkSettings? link)
            => link is null ? new(false, DefaultHost, PatternsPort, "", false) : new(link.Enabled, link.Host, link.Port, link.TokenProtected, link.NextOnEnd);
    }

    /// <summary>
    /// The 2.17 keys a file holds, each null when the file has none, as the file says them: a blank host is this PC (as 2.17 dials
    /// it); a host or port no link can dial stays as written, so it differs from every mirror and <see cref="Settle"/> keeps the link
    /// switched off and says so.
    /// </summary>
    private readonly record struct Legacy(bool? On, string? Host, int? Port, string? Token, bool? NextOnEnd)
    {
        public static Legacy Read(IntegrationSettings i) => new(
            i.LegacyPatterns,
            i.LegacyPatternsHost is { } h ? (h.Trim().Length > 0 ? h.Trim() : DefaultHost) : null,
            i.LegacyPatternsPort,
            i.LegacyPatternsToken,
            i.LegacyPatternsNextOnEnd);

        /// <summary>Whether a key the file holds says something <paramref name="m"/> does not.</summary>
        public bool Differs(Mirror m)
            => On is { } on && on != m.On || Host is { } h && h != m.Host || Port is { } p && p != m.Port
               || Token is { } t && t != m.Token || NextOnEnd is { } n && n != m.NextOnEnd;
    }

    // ------------------------------------------------------------------ running-order cues

    /// <summary>
    /// A running order's playout cues as a show or settings file may keep them (both are input): per item, the first
    /// <see cref="MaxCuesPerItem"/> sound cues — a link's name by the name rule, a playlist "" or an id, an item an id, a label of at
    /// most <see cref="MaxLabelChars"/> with no control character. A cue that breaks one is left out whole, and each item that loses
    /// one is said in <paramref name="notes"/> by its number and title in running order <paramref name="where"/>.
    /// </summary>
    public static void SanitiseCues(Rundown? rundown, LoadNotes? notes = null, string where = "")
    {
        if (rundown?.Items is null) return;
        for (var n = 0; n < rundown.Items.Count; n++)
        {
            if (rundown.Items[n] is not { } item) continue;
            var had = item.Playout?.Count(c => c is not null) ?? 0;
            item.Playout = SanitiseCues(item.Playout);
            if (had > item.Playout.Count)
                notes?.Add($"{Cues(had - item.Playout.Count)} left out of item {(n + 1).ToString(CultureInfo.InvariantCulture)} ({LoadNotes.Shown(item.Title)}) in running order {LoadNotes.Shown(where)}: unreadable, or past {MaxCuesPerItem.ToString(CultureInfo.InvariantCulture)} an item");
        }

        static string Cues(int n) => n == 1 ? "1 playout cue" : n.ToString(CultureInfo.InvariantCulture) + " playout cues";
    }

    public static List<PlayoutCue> SanitiseCues(IEnumerable<PlayoutCue?>? cues)
        => (cues ?? [])
            .OfType<PlayoutCue>()
            .Select(c => new PlayoutCue
            {
                Link = (c.Link ?? "").Trim().ToLowerInvariant(), List = c.List ?? "", Item = c.Item ?? "", Label = c.Label ?? "",
                Standby = c.Standby, Together = c.Together,
            })
            .Where(c => ValidName(c.Link) && (c.List.Length == 0 || ValidRef(c.List)) && ValidRef(c.Item)
                        && c.Label.Length <= MaxLabelChars && !c.Label.Any(char.IsControl))
            .Take(MaxCuesPerItem)
            .ToList();

    // ------------------------------------------------------------------ which link a command reaches, and what this Stagewatch does with it

    /// <summary>
    /// The link a playout command reaches, pure, or why none, asked in this order — the licence; the link (<see cref="Target"/>);
    /// whether this Stagewatch runs it now (<paramref name="runs"/>, by name: a link started under the licence, switched on, its
    /// kind one this Stagewatch speaks). What the link then sends, or refuses with the peer's own facts, is the director's. A refusal
    /// sends nothing.
    /// </summary>
    public static (PlayoutLinkSettings? Link, string? Error) Route(IReadOnlyList<PlayoutLinkSettings> links, PlayoutArgs a, bool licensed, Func<string, bool> runs, string? vtLink)
    {
        if (!licensed) return (null, LicenceWords + ". Help → License.");
        var (link, why) = Target(links, a.Link, a.Action, vtLink);
        if (link is null) return (null, why);
        return runs(link.Name) ? (link, null) : (null, $"{Label(link.Name)} isn't running yet — its link starts with Settings → Integrations, and dials once started");
    }

    /// <summary>
    /// The link <c>patterns …</c> reaches: link "patterns" when there is one, else the only link switched on — the verb 2.17's
    /// Patterns link answered, unchanged.
    /// </summary>
    public static string PatternsTarget(IReadOnlyList<PlayoutLinkSettings> links) => Find(links, PatternsName) is not null ? PatternsName : "";

    /// <summary>
    /// How people read a link's peer: the app its kind names and the link's label — "PlayMap Stage left" — or the app alone when the
    /// label already says it ("Patterns" for link patterns), or the label alone while the kind is not known.
    /// </summary>
    public static string Peer(string? kind, string name)
    {
        var label = Label(name);
        if (ShowBusKinds.Label(kind) is not { } app) return label;
        return label.Equals(app, StringComparison.OrdinalIgnoreCase) ? app : app + " " + label;
    }

    /// <summary>
    /// The link a playout command reaches, or why none. A named link must be one of <paramref name="links"/> and switched on. With
    /// no name: the only link switched on; else, for end, restart, play, pause, stop and seek, the link feeding the VT countdown
    /// (<paramref name="vtLink"/>); go and cue, which put something new on a peer's air, never guess and are refused naming one.
    /// </summary>
    public static (PlayoutLinkSettings? Link, string? Error) Target(IReadOnlyList<PlayoutLinkSettings> links, string name, string action, string? vtLink)
    {
        if (name.Length > 0)
        {
            if (Find(links, name) is not { } named)
                return (null, links.Count == 0 ? $"no playout link called {name} — none is set up (Settings → Integrations)" : $"no playout link called {name} — links: {Names(links)}");
            return named.Enabled ? (named, null) : (null, $"{Label(name)} is switched off (Settings → Integrations)");
        }
        var on = links.Where(l => l.Enabled).ToList();
        if (on.Count == 1) return (on[0], null);
        if (on.Count == 0)
            return (null, links.Count == 0 ? "no playout link is set up (Settings → Integrations)" : $"every playout link is switched off — links: {Names(links)}");
        if (TransportActions.Contains(action) && vtLink is not null && on.FirstOrDefault(l => l.Name == vtLink) is { } vt) return (vt, null);
        var why = TransportActions.Contains(action) ? " and none feeds the VT" : "";
        return (null, $"{Count(on.Count)} playout links{why} — name one: playout {on[0].Name} {action} …");
    }

    private static string Names(IEnumerable<PlayoutLinkSettings> links) => string.Join(", ", links.Select(l => l.Name));

    private static string Count(int n) => n switch
    {
        2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };
}
