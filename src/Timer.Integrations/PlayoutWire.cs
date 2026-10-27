using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// A Patterns desk or a PlayMap node heard by Find: where its line wire is (<see cref="Address"/>, <see cref="WirePort"/>) and its
/// <see cref="Kind"/>, <see cref="PlayoutRules.Patterns"/> or <see cref="PlayoutRules.PlayMap"/>. A Show Bus hello (§2) also says its
/// id, its https door and the certificate it names beside that door; Patterns' own 9700 beacon says none of those. Nothing here is
/// proved: a hello says what a node is, and the pin, not the hello, is the trust (§10, 13.8).
/// </summary>
public sealed record PlayoutPeer(string Name, string Address, int WirePort, string Kind)
{
    /// <summary>Its Show Bus id (§2), or "" when the datagram gave none of that shape.</summary>
    public string Id { get; init; } = "";

    /// <summary>Its <c>ports.https</c>, 0 when it names none.</summary>
    public int HttpsPort { get; init; }

    /// <summary>The certificate its hello names (<c>tls</c>: <c>sha256:</c> and 64 lowercase hex), or null when it names none of that shape.</summary>
    public string? Tls { get; init; }

    /// <summary>What it says it is within its kind — the beacon's <c>kind</c>, the hello's <c>role</c> ("desk") — or "".</summary>
    public string Role { get; init; } = "";

    /// <summary>Heard as a Show Bus hello (UDP 9928), not as Patterns' own beacon.</summary>
    public bool FromHello { get; init; }
}

/// <summary>
/// The primitives every playout link's line wire is made of, pure but for <see cref="DiscoverAsync"/>: Find, which hears Patterns'
/// and PlayMap's hellos and Patterns' beacon; the level-0 proof (13.6.8, the core's own); a word and a command line as the peer's
/// wire reads them; and the bound a link reads a line within (13.14), with the words that say a STATE was longer.
/// </summary>
public static class PlayoutWire
{
    /// <summary>The show network's hello (§2), where Patterns and PlayMap say hello.</summary>
    public const int HelloPort = SlidesBeacon.Port;

    /// <summary>Patterns' own beacon, kept in Find until Patterns' hello has been heard on a real network.</summary>
    public const int BeaconPort = 9700;

    /// <summary>The UDP ports Find (<see cref="DiscoverAsync"/>) listens on, and the only ones: the show network's hello and Patterns' own beacon.</summary>
    public static IReadOnlyList<int> FindPorts { get; } = [HelloPort, BeaconPort];

    /// <summary>How long Settings' Find listens on <see cref="FindPorts"/>.</summary>
    public static readonly TimeSpan FindListen = TimeSpan.FromSeconds(3);

    /// <summary>The largest datagram Find reads; a longer one is dropped unread.</summary>
    public const int FindReadBytes = 8192;

    /// <summary>The most machines one Find lists (<see cref="PlayoutFindList"/>), counted among the kinds it was asked for.</summary>
    public const int FindMax = 32;

    /// <summary>
    /// §14 <c>link.read.min.bytes</c>: the most of one line a playout link keeps. A line up to this long is read whole; a longer one
    /// keeps this much and is read only for what lies within it (<see cref="PlayoutLineReader"/>, 13.14).
    /// </summary>
    public const int LineBytes = 256 * 1024;

    /// <summary>
    /// §14 <c>link.command.bytes</c>: the longest command line a peer reads; it refuses a longer one and closes, so <see cref="Line"/>
    /// builds none longer, counted in UTF-8 bytes without its LF.
    /// </summary>
    public const int CommandBytes = 64 * 1024;

    /// <summary>The name a link says in <c>HELLO</c>, and the one its level-0 proof is made over.</summary>
    public const string HelloName = "Stagewatch";

    // ------------------------------------------------------------------ Find

    /// <summary>
    /// Listen for <paramref name="listen"/> on each of <see cref="FindPorts"/>: the nodes of <paramref name="kinds"/> on this network,
    /// each with its line wire's port, one per address and port, kept by <see cref="PlayoutFindList"/>. It listens the whole time, so a
    /// node already listed is still updated once the list is full.
    /// </summary>
    public static async Task<IReadOnlyList<PlayoutPeer>> DiscoverAsync(TimeSpan listen, IReadOnlyCollection<string> kinds, CancellationToken ct = default)
    {
        var found = new PlayoutFindList(kinds);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(listen);
        async Task ListenOn(int port)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                while (!timeout.IsCancellationRequested)
                {
                    UdpReceiveResult r;
                    try { r = await udp.ReceiveAsync(timeout.Token); }
                    catch (OperationCanceledException) { break; }
                    // ParseBeacon answers any datagram with a peer or null, never an exception: nothing heard ends Find.
                    if (ParseBeacon(r.Buffer, r.RemoteEndPoint.Address.ToString()) is not { } heard) continue;
                    lock (found) found.Hear(heard);
                }
            }
            catch (SocketException)
            {
                // A port another program holds without sharing it: Find hears what the other port brings.
            }
        }
        await Task.WhenAll(FindPorts.Select(ListenOn));
        lock (found) return found.Peers;
    }

    /// <summary>
    /// Pure: a datagram heard at <paramref name="address"/> → the playout peer it names, or null. It names one when it is Patterns'
    /// beacon (<c>{"patterns":1,…}</c>), or a §2 hello of format <c>v</c> 1 and kind <c>patterns</c> or <c>playmap</c>, and gives a line
    /// wire: <c>ports.wire</c> (§2's), else the top-level <c>wire</c> the older senders give. Anything on the network can send anything:
    /// a field of the wrong shape is never read (an id not a Show Bus id, a port out of range, a <c>tls</c> that is not a SHA-256
    /// fingerprint), a name keeps no control character, and a datagram that is not UTF-8, or that escapes half a character
    /// (<c>\ud800</c>) in a field it reads, names none: whatever it is sent, it answers a peer or null and never throws.
    /// </summary>
    public static PlayoutPeer? ParseBeacon(byte[] data, string address)
    {
        // JSON on a network is UTF-8 (RFC 8259 §8.1): a datagram that is not names nothing.
        if (data.Length > FindReadBytes || !System.Text.Unicode.Utf8.IsValid(data)) return null;
        try { return Heard(data, address); }
        catch (JsonException) { return null; }
        // GetString's answer to bytes that are not UTF-8, or to an escape of half a surrogate pair.
        catch (InvalidOperationException) { return null; }
    }

    private static PlayoutPeer? Heard(byte[] data, string address)
    {
        using var doc = JsonDocument.Parse(data);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var beacon = root.TryGetProperty("patterns", out var proto) && proto.ValueKind == JsonValueKind.Number;
        var t = Text(root, "t");
        var hello = !beacon && t is PlayoutRules.Patterns or PlayoutRules.PlayMap
                    && root.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var format) && format == 1;
        if (!beacon && !hello) return null;
        var ports = root.TryGetProperty("ports", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        if ((Port(ports, "wire") ?? Port(root, "wire")) is not { } wire) return null;
        var name = new[] { Text(root, "machine"), Text(root, "name"), Text(root, "host") }.Select(ShowBusText.Name).FirstOrDefault(n => n.Length > 0) ?? "";
        var id = hello ? Text(root, "id") : "";
        var tls = hello ? Text(root, "tls") : "";
        return new PlayoutPeer(name, address, wire, beacon ? PlayoutRules.Patterns : t)
        {
            Id = ShowBusHello.ValidId(id) ? id : "",
            HttpsPort = hello ? Port(ports, "https") ?? 0 : 0,
            Tls = IsFingerprint(tls) ? tls : null,
            Role = ShowBusText.Clip(new string(Text(root, beacon ? "kind" : "role").Where(c => !char.IsControl(c)).ToArray()), 24),
            FromHello = hello,
        };

        static string Text(JsonElement e, string name)
            => e.TryGetProperty(name, out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";

        static int? Port(JsonElement e, string name)
            => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number
               && n.TryGetInt32(out var port) && port is > 0 and < 65536 ? port : null;
    }

    /// <summary>Pure: a certificate's fingerprint as a hello names it (§2's <c>tls</c>): <c>sha256:</c> and 64 lowercase hex.</summary>
    public static bool IsFingerprint(string? tls)
        => tls is { Length: 71 } && tls.StartsWith("sha256:", StringComparison.Ordinal) && PeerLink.IsHex(tls[7..], 64);

    // ------------------------------------------------------------------ the level-0 proof (13.6.8)

    /// <summary>13.6.8: a NONCE a link answers — exactly 32 lowercase hex, as Show Bus 1.1 desks make them.</summary>
    public static bool IsProvableNonce(string nonce) => PeerLink.IsProvableNonce(nonce);

    /// <summary>
    /// 13.6.8's <c>AUTH-PROOF</c>, the core's own (<see cref="PeerLink.LegacyProof"/>): HMAC-SHA256 keyed with the token as 1.1 desks
    /// compare it, over the nonce and the HELLO name, lowercase hex. Null when the nonce is not one a link answers, or the token one
    /// 13.6.8 does not prove (<see cref="TokenProblem"/>): then no proof is sent at all.
    /// </summary>
    public static string? Proof(string token, string nonce, string name = HelloName) => PeerLink.LegacyProof(token, nonce, name);

    /// <summary>13.6.8's words, as the contract quotes them, for a token holding a character 1.1 desks key apart.</summary>
    public const string InvisibleCharacter = "this token holds an invisible character — retype it";

    /// <summary>
    /// Null when 13.6.8 proves <paramref name="token"/>; else why not, in words a person can act on. 1.1 desks compare a token with its
    /// dashes, spaces, tabs, CRs and LFs removed and the rest upper-cased, and differ over any other character 13.5.1's canonical form
    /// removes (another space, a line or paragraph separator, a byte order mark), so a token holding one is never proved (it would be
    /// refused, and each refusal counts toward the desk's lockout) and is told in 13.6.8's words, <see cref="InvisibleCharacter"/>.
    /// </summary>
    public static string? TokenProblem(string token)
    {
        if (PeerLink.LegacyKey(token) is not null) return null;
        if (token.Length == 0) return "there is no pairing token";
        return token.All(c => c is '-' or ' ' or '\t' or '\r' or '\n') ? "this token is only dashes and spaces — retype it" : InvisibleCharacter;
    }

    // ------------------------------------------------------------------ words and lines

    /// <summary>
    /// One word as PlayMap's line rule carries it (its <c>WireLine.Quote</c>, Show Bus §6's notation): bare when it reads back as itself
    /// — not empty, no white space or quote mark in it, not opening with <c>{</c> or <c>[</c> (read as JSON to the end of the line) — else
    /// between quotes, each <c>\</c> and <c>"</c> in it escaped. Patterns reads no quotes (it takes a verb's last word raw, quote marks and
    /// all), so a word for Patterns goes only bare (<see cref="Word"/>). Null for a word no line can carry: a control character, a line
    /// break among them, would end or bend the line, and half a surrogate pair would reach the peer as U+FFFD, another word.
    /// </summary>
    public static string? Quote(string word)
    {
        if (word.Any(char.IsControl) || !WholeText(word)) return null;
        if (word.Length > 0 && word[0] is not ('{' or '[') && !word.Any(c => char.IsWhiteSpace(c) || c == '"')) return word;
        return "\"" + word.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// One word as a peer of <paramref name="kind"/> reads it back as itself: to PlayMap, which reads quotes, as <see cref="Quote"/>
    /// carries it; to any other — Patterns, which takes words raw, or a peer whose kind is not known yet — bare or not at all, so a word
    /// that would need quotes (an id is bare) is refused here rather than read there as another.
    /// </summary>
    public static string? Word(string kind, string word)
        => Quote(word) is { } said && (kind == PlayoutRules.PlayMap || said == word) ? said : null;

    /// <summary>
    /// A command line as a link to a peer of <paramref name="kind"/> writes it, without its LF: <c>@</c> and the request id when there is
    /// one (§3), the verb, then each other word as <see cref="Word"/> carries it to that peer. Null — nothing is sent, and never a line cut
    /// short — when the id is not a §3 request id, the verb is not upper-case letters and dashes (<c>AUTH-PROOF</c>), a word cannot reach
    /// that peer as itself, or the line would be longer than <see cref="CommandBytes"/>.
    /// </summary>
    public static string? Line(string kind, string? id, string verb, params IReadOnlyList<string> words)
    {
        if (id is not null && CommandReceipt.CleanId(id) is null) return null;
        if (verb.Length == 0 || verb[0] == '-' || !verb.All(c => c is (>= 'A' and <= 'Z') or '-')) return null;
        var line = new StringBuilder();
        if (id is not null) line.Append('@').Append(id).Append(' ');
        line.Append(verb);
        foreach (var word in words)
        {
            if (Word(kind, word) is not { } said) return null;
            line.Append(' ').Append(said);
        }
        var text = line.ToString();
        return Encoding.UTF8.GetByteCount(text) <= CommandBytes ? text : null;
    }

    /// <summary>
    /// "STATE 293 KB — over this link's 256 KB": a STATE longer than a link reads, by its whole size in UTF-8 bytes, each size in
    /// whole KB rounded up, so a STATE one byte over never reads as the bound itself.
    /// </summary>
    public static string OversizeWords(long bytes) => $"STATE {Kb(bytes)} KB — over this link's {Kb(LineBytes)} KB";

    private static string Kb(long bytes) => ((Math.Max(0, bytes) + 1023) / 1024).ToString(CultureInfo.InvariantCulture);

    /// <summary>Text with every surrogate in a pair: UTF-8 carries it as written, never as U+FFFD.</summary>
    private static bool WholeText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
            i++;
        }
        return true;
    }
}

/// <summary>
/// What one Find has heard, pure (<see cref="PlayoutWire.DiscoverAsync"/> holds it under one lock): one peer per address and line
/// wire, of the kinds it was asked for only, at most <see cref="PlayoutWire.FindMax"/> of them, in the order first heard. A node heard
/// again replaces its entry — a hello a beacon, never a beacon a hello — even once the list is full, so what a hello says is never lost
/// to the bound, and a node of another kind never takes a place.
/// </summary>
public sealed class PlayoutFindList(IEnumerable<string> kinds)
{
    private readonly HashSet<string> _kinds = new(kinds, StringComparer.Ordinal);
    private readonly Dictionary<string, int> _at = new(StringComparer.Ordinal);
    private readonly List<PlayoutPeer> _peers = [];

    /// <summary>The peers listed, as a copy.</summary>
    public IReadOnlyList<PlayoutPeer> Peers => [.. _peers];

    /// <summary>Take a peer heard; true when the list changed.</summary>
    public bool Hear(PlayoutPeer heard)
    {
        if (!_kinds.Contains(heard.Kind)) return false;
        var key = heard.Address + ":" + heard.WirePort.ToString(CultureInfo.InvariantCulture);
        if (_at.TryGetValue(key, out var at))
        {
            if (_peers[at].FromHello && !heard.FromHello) return false;
            _peers[at] = heard;
            return true;
        }
        if (_peers.Count >= PlayoutWire.FindMax) return false;
        _at[key] = _peers.Count;
        _peers.Add(heard);
        return true;
    }
}

/// <summary>
/// One connection's lines off a line wire (13.14), split at LF: a line up to <see cref="PlayoutWire.LineBytes"/> bytes is read whole,
/// its trailing CRs dropped; a longer one keeps its first <see cref="PlayoutWire.LineBytes"/> bytes, is counted to its LF and handed on
/// once, not whole, so its tail is never read as a line of its own. Fed in order, on one thread; it holds at most one line's bound.
/// </summary>
public sealed class PlayoutLineReader
{
    /// <summary>A line: what is kept of it, its whole size in bytes before its LF, and whether it was kept whole.</summary>
    public delegate void LineRead(ReadOnlySpan<byte> line, long size, bool whole);

    private readonly int _bound;
    private readonly MemoryStream _kept = new();
    private long _size;

    /// <param name="bound">The most of one line kept (a test gives a small one); <see cref="PlayoutWire.LineBytes"/> on a link.</param>
    public PlayoutLineReader(int bound = PlayoutWire.LineBytes)
        => _bound = bound > 0 ? bound : throw new ArgumentOutOfRangeException(nameof(bound), "a line reader keeps at least one byte of a line");

    /// <summary>Bytes as they came off the wire: each line they end is handed to <paramref name="onLine"/> before the next is read.</summary>
    public void Feed(ReadOnlySpan<byte> data, LineRead onLine)
    {
        while (data.Length > 0)
        {
            var lf = data.IndexOf((byte)'\n');
            var part = lf < 0 ? data : data[..lf];
            var room = _bound - (int)_kept.Length;
            if (room > 0) _kept.Write(part[..Math.Min(room, part.Length)]);
            _size += part.Length;
            if (lf < 0) return;
            var whole = _size <= _bound;
            var line = _kept.GetBuffer().AsSpan(0, (int)_kept.Length);
            if (whole) line = line.TrimEnd((byte)'\r');
            onLine(line, _size, whole);
            _kept.SetLength(0);
            _size = 0;
            data = data[(lf + 1)..];
        }
    }
}
