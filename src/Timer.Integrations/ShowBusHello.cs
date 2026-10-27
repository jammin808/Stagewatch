using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// Where a hello goes (Show Bus §2): <see cref="Network"/> when the node's doors are bound to the network, so it broadcasts. Every
/// other value says no hello at all, and why: <see cref="None"/> its doors are bound to this machine only (the default, so a hello
/// nothing placed goes nowhere); <see cref="Off"/> it runs no web server to name; <see cref="Refused"/> its id or inst is not one §2
/// allows.
/// </summary>
public enum HelloScope { None, Network, Off, Refused }

/// <summary>
/// The doors a Stagewatch runs, as its servers bound them (never as the settings say they should be): a port of 0 is a server
/// that is not running; <c>Tls</c> is the web server's certificate fingerprint ("sha256:…"), null when it speaks plain HTTP only.
/// </summary>
public sealed record HelloDoors(int WebPort, bool WebBindAll, string? Tls, int CompanionPort, bool CompanionBindAll, int OscPort, bool OscBindAll);

/// <summary>
/// What the last beat did with the hello: <see cref="Said"/> a target off this machine took it (the broadcast went);
/// <see cref="ThisPcOnly"/> only a target on this machine did (the loopback copy); <see cref="NotSaid"/> every target failed;
/// <see cref="Silent"/> the doors are bound to this machine only, so none was sent (§2); <see cref="NotYet"/> no beat has run.
/// </summary>
public enum HelloSaid { NotYet, Said, ThisPcOnly, NotSaid, Silent }

/// <summary>One target of the hello: how many sends went and failed, and the words of the last failure ("" when none).</summary>
public sealed record HelloTarget(string Target, bool ThisPc, long Sent, long Failed, string LastFailure);

/// <summary>
/// The hello's state after its last beat, published whole: what that beat did, for how many beats in a row, the words of the first
/// failure it met ("" when none), and each target's counts. Attempts are not facts: a hello counts as said only when a socket took it.
/// A silent beat keeps why it was silent (<see cref="Scope"/>) and the web port its hello named, so its words are that beat's, never
/// the servers' state read later.
/// </summary>
public sealed record HelloStatus(HelloSaid Said, int Beats, string Words, IReadOnlyList<HelloTarget> Targets)
{
    public static readonly HelloStatus NotYet = new(HelloSaid.NotYet, 0, "", Array.Empty<HelloTarget>());

    /// <summary>Where the beat's hello was to go; for a <see cref="HelloSaid.Silent"/> beat, why it went nowhere.</summary>
    public HelloScope Scope { get; init; }

    /// <summary>The web port the beat's hello named, 0 when it built none.</summary>
    public int WebPort { get; init; }

    /// <summary>
    /// Every target has failed for <see cref="ShowBusHello.FailedBeats"/> beats in a row: no other app can find this node. A broadcast
    /// that fails while the copy on this machine goes is not this: with no network no other machine is reachable either.
    /// </summary>
    public bool CannotGoOut => Said == HelloSaid.NotSaid && Beats >= ShowBusHello.FailedBeats;
}

/// <summary>
/// One id heard from two running nodes (Show Bus §2): the host and address of the hello before the switch, and of the one after.
/// Ids are not authenticated and travel with copied settings, so this is shown, never merged.
/// </summary>
public sealed record HelloConflict(string Id, string HostA, string AddressA, string HostB, string AddressB)
{
    /// <summary>The words a reader shows amber, naming both hosts and both addresses.</summary>
    public string Words => $"two machines say they are {Id}: {Who(HostA, AddressA)} and {Who(HostB, AddressB)}";

    /// <summary>The second of the two, as words ("FOH-PC2 at 10.0.0.7"): for this node's own id, the other machine.</summary>
    public string Other => Who(HostB, AddressB);

    private static string Who(string host, string address) => host.Length > 0 ? $"{host} at {address}" : address;
}

/// <summary>
/// Stagewatch's hello as Show Bus 1.3 §2 has it, pure: built from what the servers bound and the settings snapshot, never the live
/// settings; sent only where its doors are bound to the network; at most <see cref="HelloBytes"/> as sent; and read back within §2's
/// limits. <see cref="SlidesBeacon"/> sends and hears it; <see cref="HelloHearing"/> keeps what it hears.
/// </summary>
public static class ShowBusHello
{
    /// <summary>The Show Bus revision whose shapes this node speaks: 1.3 (§14 <c>proto</c>).</summary>
    public const int Proto = 3;

    /// <summary>The most bytes a hello is as sent: one datagram that needs no fragmenting (§14 <c>hello.bytes</c>).</summary>
    public const int HelloBytes = 1200;

    /// <summary>An <c>inst</c>'s lowercase hex digits (§14 <c>hello.inst.chars</c>).</summary>
    public const int InstChars = 8;

    /// <summary>The longest cap word, of lowercase letters, digits, '.' and '-' (§14 <c>hello.cap.chars</c>).</summary>
    public const int CapChars = 24;

    /// <summary>The fewest characters an id has (§14 <c>hello.id.min</c>); <see cref="SlidesBeacon.IdChars"/> is the most.</summary>
    public const int IdMin = 6;

    /// <summary>Beats in a row with every target failed before the context says so amber (<see cref="HelloStatus.CannotGoOut"/>).</summary>
    public const int FailedBeats = 3;

    /// <summary>
    /// What a Stagewatch (kind <c>main</c>) can do, in the order its hello lists them, each a §14 <c>cap … main</c> row: <c>windows</c>
    /// because its web server serves windows (§12); <c>mcp</c> follows, only while MCP is allowed.
    /// </summary>
    public static readonly IReadOnlyList<string> MainCaps =
        ["timer", "cues", "messages", "context", "receipts", "clock", "screens", "windows", "aux", "slides-in", "copilot", "approvals"];

    /// <summary>What a Slides agent (kind <c>agent</c>) can do: send a slide feed.</summary>
    public static readonly IReadOnlyList<string> AgentCaps = ["slides"];

    private static readonly JsonSerializerOptions Wire = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Names in any script stay as UTF-8, a few bytes each, rather than six-byte escapes; a hello is never HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Stagewatch's hello and where it goes. No hello without a web server to name (<see cref="HelloScope.Off"/>) or with an id or
    /// inst §2 refuses (<see cref="HelloScope.Refused"/>); scope <see cref="HelloScope.None"/> while the web server is bound to this
    /// machine only (its doors are, so it says nothing), and <see cref="HelloScope.Network"/> otherwise. <c>ports</c> names only doors
    /// every receiver can open: <c>http</c> and <c>ws</c>, <c>https</c> with a certificate, and the Companion and OSC servers only
    /// while they run bound to the network. Never null for a valid id while the web server runs.
    /// </summary>
    public static (SlidesBeacon.Hello? Hello, HelloScope Scope) Main(string nodeId, string inst, string name, string host, HelloDoors doors, bool mcp, long clockMs)
    {
        if (doors.WebPort is <= 0 or > 65535) return (null, HelloScope.Off);
        if (!ValidId(nodeId) || !InstShape(inst)) return (null, HelloScope.Refused);
        var ports = new Dictionary<string, int> { ["http"] = doors.WebPort, ["ws"] = doors.WebPort };
        if (doors.Tls is not null) ports["https"] = doors.WebPort;   // the same port: TLS is recognised by its first byte
        if (doors.CompanionPort is > 0 and < 65536 && doors.CompanionBindAll) ports["companion"] = doors.CompanionPort;
        if (doors.OscPort is > 0 and < 65536 && doors.OscBindAll) ports["osc"] = doors.OscPort;
        var caps = mcp ? MainCaps.Append("mcp").ToArray() : MainCaps.ToArray();
        var hello = Fit(new SlidesBeacon.Hello("main", 1, nodeId, Name(name), Name(host), doors.WebPort, false, caps, Proto, ports, clockMs, Name(host), doors.Tls, inst));
        return (hello, doors.WebBindAll ? HelloScope.Network : HelloScope.None);
    }

    /// <summary>
    /// The doors a hello may name, from each server's door as it bound it (<see cref="BoundDoor"/>, null while it does not run) and the
    /// web server's certificate: never from the settings, so a hello names only what is open. A certificate with no web server is no door.
    /// </summary>
    public static HelloDoors Doors(BoundDoor? web, string? tls, BoundDoor? companion, BoundDoor? osc)
        => new(web?.Port ?? 0, web?.BindAll ?? false, web is null ? null : tls, companion?.Port ?? 0, companion?.BindAll ?? false, osc?.Port ?? 0, osc?.BindAll ?? false);

    /// <summary>A Slides agent's hello: no doors, cap <c>slides</c>, and whether it is showing a slide — the one show fact a hello carries.</summary>
    public static SlidesBeacon.Hello Agent(string agentId, string inst, string name, string host, bool presenting, long clockMs)
        => Fit(new SlidesBeacon.Hello("agent", 1, agentId, Name(name), Name(host), 0, presenting, AgentCaps.ToArray(), Proto, null, clockMs, Name(host), null, inst));

    /// <summary>
    /// The targets of a hello: for <see cref="HelloScope.Network"/> the broadcast, then the copy to this machine (which no reader
    /// relies on: on a shared port it reaches one listener of several); for <see cref="HelloScope.None"/>, none.
    /// </summary>
    public static IReadOnlyList<IPEndPoint> Targets(HelloScope scope, int port) => scope == HelloScope.Network
        ? [new IPEndPoint(IPAddress.Broadcast, port), new IPEndPoint(IPAddress.Loopback, port)]
        : [];

    /// <summary>What a beat did, from which of its targets took the hello: a target off this machine says it, one on it only here.</summary>
    public static HelloSaid Said(IEnumerable<(IPEndPoint Target, bool Went)> sends)
    {
        bool any = false, outward = false;
        foreach (var (target, went) in sends)
        {
            if (!went) continue;
            any = true;
            if (!IPAddress.IsLoopback(target.Address)) outward = true;
        }
        return outward ? HelloSaid.Said : any ? HelloSaid.ThisPcOnly : HelloSaid.NotSaid;
    }

    /// <summary>A cap word as §2 allows it: 1 to <see cref="CapChars"/> lowercase letters, digits, '.' and '-'.</summary>
    public static bool CapShape(string? cap)
        => cap is { Length: > 0 and <= CapChars } && cap.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-');

    /// <summary>An <c>inst</c> as §2 has it: exactly <see cref="InstChars"/> lowercase hex digits.</summary>
    public static bool InstShape(string? inst) => inst is { Length: InstChars } && inst.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>An id a hello may say: <see cref="IdMin"/> to <see cref="SlidesBeacon.IdChars"/> letters, digits, '-' and '_'.</summary>
    public static bool ValidId(string? id) => id is { Length: >= IdMin } && SlidesBeacon.ValidToken(id, SlidesBeacon.IdChars);

    /// <summary>A new <c>inst</c>: <see cref="InstChars"/> lowercase hex digits from the system's random generator, once per process start.</summary>
    public static string NewInst() => RandomNumberGenerator.GetHexString(InstChars, lowercase: true);

    /// <summary>A hello as it goes on the wire: compact UTF-8 JSON, with no field that is null.</summary>
    public static byte[] Bytes(SlidesBeacon.Hello hello) => JsonSerializer.SerializeToUtf8Bytes(hello, Wire);

    /// <summary>
    /// A name or a host as a hello says it, by the one rule its context says its name by too (<see cref="ShowBusText.Name"/>): no control
    /// characters, and at most <see cref="SlidesBeacon.NameChars"/> characters, never ending inside a character a surrogate pair makes.
    /// </summary>
    public static string Name(string? text) => ShowBusText.Name(text);

    /// <summary>
    /// The hello within <see cref="HelloBytes"/>: whole when it fits; else its name, host and older <c>machine</c> cut shorter until it
    /// does (a name of unassigned or escaped characters is the only thing that can need it).
    /// </summary>
    private static SlidesBeacon.Hello Fit(SlidesBeacon.Hello hello)
    {
        foreach (var max in new[] { 32, 16, 8, 0 })
        {
            if (Bytes(hello).Length <= HelloBytes) return hello;
            hello = hello with { Name = ShowBusText.Clip(hello.Name, max), Machine = ShowBusText.Clip(hello.Machine, max), Host = hello.Host is null ? null : ShowBusText.Clip(hello.Host, max) };
        }
        return hello;
    }

    /// <summary>
    /// A heard datagram as a hello within §2's limits, or null when it is not one: at most <see cref="SlidesBeacon.ReadBytes"/>, JSON
    /// of format <c>v</c> 1, a kind and an id of letters, digits, '-' and '_' (a Slides agent's id a feed's id); its first
    /// <see cref="SlidesBeacon.KeptCaps"/> caps of §2's shape (other words ignored), its first <see cref="SlidesBeacon.KeptPorts"/>
    /// doors, its name and host with no control characters and cut at <see cref="SlidesBeacon.NameChars"/> (as <see cref="Name"/> says
    /// them: they reach the desk's words and a log line), and its <c>inst</c> only when it has §2's shape. Anything on the network can
    /// send anything: whatever else it carries is never trusted.
    /// </summary>
    public static SlidesBeacon.Hello? Read(byte[] datagram)
    {
        if (datagram.Length > SlidesBeacon.ReadBytes) return null;
        SlidesBeacon.Hello? h;
        try { h = JsonSerializer.Deserialize<SlidesBeacon.Hello>(datagram); }
        catch (JsonException) { return null; }
        if (h is null || h.Version != 1 || !SlidesBeacon.ValidToken(h.Kind, SlidesBeacon.KindChars) || !SlidesBeacon.ValidToken(h.Id, SlidesBeacon.IdChars)) return null;
        if (h.Kind == "agent" && !SlideFeed.Valid(h.Id)) return null;
        return h with
        {
            Name = Name(h.Name),
            Machine = Name(h.Machine ?? h.Host),
            Host = h.Host is null ? null : Name(h.Host),
            WebPort = Math.Clamp(h.WebPort, 0, 65535),
            Caps = h.Caps?.Where(CapShape).Take(SlidesBeacon.KeptCaps).ToArray(),
            Ports = h.Ports?.Where(kv => SlidesBeacon.ValidToken(kv.Key, 16) && kv.Value is > 0 and < 65536).Take(SlidesBeacon.KeptPorts)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
            Inst = InstShape(h.Inst) ? h.Inst : null,
        };
    }

    /// <summary>
    /// The Show network's first line on the desk: whether this Stagewatch's hello is out, and if not, why — the last beat's own reason
    /// (<see cref="HelloStatus.Scope"/>), never the servers' state read since; then, while another machine says this Stagewatch's id
    /// (<paramref name="own"/>, §2's conflict seen from one of its two), that machine.
    /// </summary>
    public static string Line(HelloStatus status, string name, string id, HelloConflict? own = null)
    {
        var line = status.Said switch
        {
            HelloSaid.Said => $"Said on the network as {name} ({(id.Length > 4 ? id[..4] + "…" : id)})",
            HelloSaid.ThisPcOnly => "Said on this PC only — no network: " + Words(status),
            HelloSaid.NotSaid => "Not said: " + Words(status),
            HelloSaid.Silent => status.Scope switch
            {
                HelloScope.Off => "Not said — Remote is off, so no other app can reach this Stagewatch",
                HelloScope.None => $"Not said — Remote is set to this PC; other apps reach it at 127.0.0.1:{status.WebPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                _ => $"Not said — this install's id ({id}) is not one the Show Bus allows",
            },
            _ => "Saying hello on the network…",
        };
        return own is null ? line : $"{line} — {Twin(own)}";
    }

    /// <summary>
    /// The context's amber item while the hello cannot go out (<see cref="HelloStatus.CannotGoOut"/>) or another machine says this
    /// Stagewatch's id (<paramref name="own"/>): this node's own health (P1), so other apps reading it, and its AI, know why they cannot
    /// find it, or cannot tell it from the other. Null otherwise.
    /// </summary>
    public static ContextItem? Item(HelloStatus status, HelloConflict? own = null)
    {
        var parts = new List<string>(2);
        if (status.CannotGoOut) parts.Add($"this Stagewatch's hello can't go out: {Words(status)} — other apps can't find it");
        if (own is not null) parts.Add(Twin(own));
        return parts.Count == 0 ? null
            : new ContextItem("bus:hello", "health", "Show network hello", Light.Amber, ShowBusText.Clip(string.Join("; ", parts), ItemStateChars));
    }

    /// <summary>The producer's warning while another machine says this Stagewatch's id; null otherwise. Never urgent: nothing is merged.</summary>
    public static Advice? OwnAdvice(HelloConflict? own) => own is null ? null
        : new Advice("bus:hello:conflict", AdviceLevel.Warn, Twin(own) + " (likely a copied install, which keeps the id it was copied with)", null, null);

    /// <summary>The most characters <see cref="Item"/>'s state takes, §6's <c>context.item.state.chars</c>.</summary>
    private const int ItemStateChars = 160;

    /// <summary>Another machine saying this node's own id, in words.</summary>
    private static string Twin(HelloConflict own) => $"another machine says it is this Stagewatch: {own.Other} — give one of them a new id";

    /// <summary>The first failure's words, cut so the item's state stays within §6's size.</summary>
    private static string Words(HelloStatus status) => status.Words.Length == 0 ? "no reason given" : ShowBusText.Clip(status.Words, 80);
}

/// <summary>
/// The hellos one listener heard (Show Bus §2), one entry per id, on injected time. A hello's address is kept with the others of its
/// id heard within <see cref="SlidesBeacon.StaleAfter"/>, at most <see cref="SlidesBeacon.KeptAddresses"/>, each with the certificate
/// its own last hello named: a new address past that takes the place of the one heard longest ago, never of the one dialled. The
/// dialled address stays while it is still heard and moves to the newest only once it goes stale, so a node heard from two addresses
/// (its broadcast and its copy here) never flips between them. One id from two or more running nodes is marked in conflict: a hello
/// whose <c>inst</c> was heard within <see cref="SlidesBeacon.StaleAfter"/> comes back after a different one (A, B, A — and A, B, C,
/// A for three copies in turn), or, from nodes that send no <c>inst</c>, a second host within that time. The mark stands until
/// <see cref="SlidesBeacon.ConflictClearsAfter"/> passes without such a return. A restart (A, then only B) is not a conflict, nor is a
/// run of restarts (A, B, C, each once); one <c>inst</c> from two addresses is one node. A hello that says this node's own id from
/// another process (<see cref="HearOwn"/>) marks this node itself, as one of two (<see cref="OwnConflict"/>). At most
/// <see cref="SlidesBeacon.MaxPeers"/> ids; one unheard for <see cref="SlidesBeacon.ForgetAfter"/> is forgotten. Thread-safe: the
/// listener writes, any thread reads.
/// </summary>
public sealed class HelloHearing
{
    /// <summary>
    /// The senders kept per id: one per running node, as many as the addresses kept. Past that a new sender is not kept (it is still
    /// the last heard), so the ones kept are the ones that can come back: N copies in turn are seen whatever N is.
    /// </summary>
    private const int KeptSenders = SlidesBeacon.KeptAddresses;

    private sealed class Entry(SlidesBeacon.Hello hello)
    {
        public SlidesBeacon.Hello Hello = hello;
        public long SeenMs;
        // Each address heard within the stale time: when, and the certificate its last hello named.
        public readonly Dictionary<string, (long Ms, string? Tls)> Addresses = new(StringComparer.Ordinal);
        public string Dial = "";
        // Who sent each hello (its inst, or "host:" and its host when it sends none) heard within the stale time, and when last.
        public readonly Dictionary<string, long> Senders = new(StringComparer.Ordinal);
        // Who sent the last hello, when, and from where.
        public string Who = "";
        public long WhoMs;
        public string WhoHost = "", WhoAddress = "";
        public long ConflictMs;
        public HelloConflict? Conflict;
    }

    private const string HostKey = "host:";
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _peers = new(StringComparer.Ordinal);
    private readonly Func<long> _nowMs;
    private readonly long _staleMs = (long)SlidesBeacon.StaleAfter.TotalMilliseconds;
    private readonly long _forgetMs = (long)SlidesBeacon.ForgetAfter.TotalMilliseconds;
    private readonly long _clearMs = (long)SlidesBeacon.ConflictClearsAfter.TotalMilliseconds;
    private (HelloConflict Conflict, long Ms)? _own;

    /// <param name="nowMs">A monotonic clock in milliseconds (tests inject one); the machine's tick count by default.</param>
    public HelloHearing(Func<long>? nowMs = null) => _nowMs = nowMs ?? (() => Environment.TickCount64);

    /// <summary>
    /// Take a hello (already read within §2's limits) heard from <paramref name="address"/>. False when it is a new id and
    /// <see cref="SlidesBeacon.MaxPeers"/> ids are held: it is dropped.
    /// </summary>
    public bool Hear(SlidesBeacon.Hello hello, string address)
    {
        var now = _nowMs();
        lock (_lock)
        {
            if (!_peers.TryGetValue(hello.Id, out var e))
            {
                if (!SlidesBeacon.HasRoom(_peers.Count)) Forget(now);
                if (!SlidesBeacon.HasRoom(_peers.Count)) return false;
                _peers[hello.Id] = e = new Entry(hello);
            }
            e.Hello = hello;
            e.SeenMs = now;
            Address(e, address, hello.Tls, now);
            Alternation(e, hello, address, now);
            return true;
        }
    }

    /// <summary>
    /// Take a hello that says this node's own id from another process (an <c>inst</c> not this one's, or none): this node is one of
    /// two that say it (§2), marked until <see cref="SlidesBeacon.ConflictClearsAfter"/> passes without another. Never a peer.
    /// </summary>
    public void HearOwn(SlidesBeacon.Hello hello, string address)
    {
        var now = _nowMs();
        lock (_lock) _own = (new HelloConflict(hello.Id, "", "", hello.Host ?? hello.Machine ?? "", address), now);
    }

    /// <summary>The other machine saying this node's own id, while its mark stands; null otherwise.</summary>
    public HelloConflict? OwnConflict()
    {
        var now = _nowMs();
        lock (_lock) return _own is { } o && now - o.Ms <= _clearMs ? o.Conflict : null;
    }

    /// <summary>Everyone heard and not yet forgotten, by kind and then name.</summary>
    public IReadOnlyList<SlidesBeacon.Peer> Peers()
    {
        var now = _nowMs();
        var list = new List<SlidesBeacon.Peer>();
        lock (_lock)
        {
            Forget(now);
            foreach (var e in _peers.Values)
            {
                var fresh = e.Addresses.Where(a => now - a.Value.Ms <= _staleMs).OrderByDescending(a => a.Value.Ms).ThenBy(a => a.Key, StringComparer.Ordinal).ToList();
                var heard = fresh.Select(a => a.Key).ToList();
                if (heard.Count == 0) heard.Add(e.Dial);
                var tls = fresh.Where(a => a.Value.Tls is not null).ToDictionary(a => a.Key, a => a.Value.Tls!, StringComparer.OrdinalIgnoreCase);
                if (e.Conflict is not null && now - e.ConflictMs > _clearMs) e.Conflict = null;
                list.Add(new SlidesBeacon.Peer(e.Hello, e.Dial, (now - e.SeenMs) / 1000.0)
                {
                    Addresses = heard, Conflict = e.Conflict, HeardTls = tls,
                    ConflictLeftMs = e.Conflict is null ? 0 : Math.Max(1, _clearMs - (now - e.ConflictMs)),
                });
            }
        }
        return list.OrderBy(p => p.Hello.Kind, StringComparer.Ordinal).ThenBy(p => p.Hello.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Hello.Id, StringComparer.Ordinal).ToList();
    }

    private void Forget(long now)
    {
        foreach (var id in _peers.Where(p => now - p.Value.SeenMs > _forgetMs).Select(p => p.Key).ToList()) _peers.Remove(id);
    }

    /// <summary>Every address heard within the stale time, at most the kept number, and the dialled one kept while it is heard.</summary>
    private void Address(Entry e, string address, string? tls, long now)
    {
        foreach (var stale in e.Addresses.Where(a => now - a.Value.Ms > _staleMs).Select(a => a.Key).ToList()) e.Addresses.Remove(stale);
        e.Addresses[address] = (now, tls);
        while (e.Addresses.Count > SlidesBeacon.KeptAddresses)
        {
            var oldest = e.Addresses.Where(a => a.Key != e.Dial && a.Key != address).OrderBy(a => a.Value.Ms).ThenBy(a => a.Key, StringComparer.Ordinal).First().Key;
            e.Addresses.Remove(oldest);
        }
        if (!e.Addresses.ContainsKey(e.Dial)) e.Dial = address;
    }

    /// <summary>
    /// §2's two nodes with one id: a sender heard within the stale time comes back after another, however many take turns; or, with
    /// no inst, a second host within that time.
    /// </summary>
    private void Alternation(Entry e, SlidesBeacon.Hello hello, string address, long now)
    {
        var host = hello.Host ?? hello.Machine ?? "";
        var who = hello.Inst ?? HostKey + host;
        foreach (var stale in e.Senders.Where(s => now - s.Value > _staleMs).Select(s => s.Key).ToList()) e.Senders.Remove(stale);
        if (e.Who.Length > 0 && who != e.Who)
        {
            var back = e.Senders.ContainsKey(who);
            var twoHosts = who.StartsWith(HostKey, StringComparison.Ordinal) && e.Who.StartsWith(HostKey, StringComparison.Ordinal) && now - e.WhoMs <= _staleMs;
            if (back || twoHosts)
            {
                e.ConflictMs = now;
                e.Conflict = new HelloConflict(hello.Id, e.WhoHost, e.WhoAddress, host, address);
            }
        }
        if (e.Senders.ContainsKey(who) || e.Senders.Count < KeptSenders) e.Senders[who] = now;
        e.Who = who;
        e.WhoMs = now;
        e.WhoHost = host;
        e.WhoAddress = address;
    }
}
