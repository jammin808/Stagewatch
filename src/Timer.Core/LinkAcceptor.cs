using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>
/// What the /ws acceptor made of a <c>link=</c> socket's one link message (Show Bus 13.6.3, 13.6.5): linked by a key in force, with the
/// acceptor's proof, the link's id and its session credential; or refused with 13.6.4's class and its words. <see cref="CountKid"/> names
/// the kid of a key in force whose proof did not verify — the one refusal the wrong-proof table counts (13.5.6); an unknown kid, a
/// malformed message, a locked pair and a refusal before the proof never name one.
/// </summary>
public sealed record LinkDecision(bool Ok, string? Class, string Words, int RetrySeconds, ApiKey? Key, string? Proof, string? Lid, string? Session, string? CountKid)
{
    /// <summary>A refusal of <paramref name="cls"/> in <paramref name="words"/>, held off <paramref name="retrySeconds"/>; counts nothing.</summary>
    public static LinkDecision Refused(string cls, string words, int retrySeconds = NodeLinks.RetrySeconds, string? countKid = null)
        => new(false, cls, words, retrySeconds, null, null, null, null, countKid);

    /// <summary>What a log line or an exception may say of a decision: never its session credential or the acceptor's proof (13.19).</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Ok = ").Append(Ok ? "True" : "False").Append(", Class = ").Append(Class).Append(", Words = ").Append(Words)
            .Append(", RetrySeconds = ").Append(RetrySeconds.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(", Key = ").Append(Key?.Id).Append(", Lid = ").Append(Lid).Append(", CountKid = ").Append(CountKid);
        return true;
    }
}

/// <summary>
/// Stagewatch's <c>/ws</c> acceptor of PeerLink (Show Bus 13.6), pure: the offer a <c>link=</c> socket hears first, the verdict and the
/// refusal it hears once its link message is read, and the decision between them. Keys are the ones in force; each key's K is the hash
/// Stagewatch already keeps for it (13.5.1, SHA-256 of the key), so nothing new is stored. An unknown kid is treated exactly like a known
/// one until the proof — a K of its own, the same transcript and MAC, the same constant-time compare, the same words — and is never
/// counted. A pair that the wrong-proof table locks is refused before its proof is checked. Each socket answers one proof: the socket's
/// one link message is the only one read as LINK.
/// </summary>
public static class LinkAcceptor
{
    /// <summary>Stagewatch's node kind (§2), the <c>t</c> its offer and its transcript name it by.</summary>
    public const string Kind = "main";

    public const string MalformedWords = "a link message this Stagewatch cannot take";
    public const string VersionWords = "this Stagewatch speaks PeerLink 1";
    public const string ProofsLocked = "too many wrong proofs for this key from this device — wait a minute";

    /// <summary>
    /// The offer a <c>link=</c> socket hears before anything else (13.6.5): <c>{"type":"link","v":1,"speaks":1,"server":{"t","id"},"snonce","bind"}</c>.
    /// </summary>
    public static byte[] Offer(string ownId, string snonce, string bind) => Json(w =>
    {
        w.WriteString("type", "link");
        w.WriteNumber("v", 1);
        w.WriteNumber("speaks", PeerLink.Version);
        w.WriteStartObject("server");
        w.WriteString("t", Kind);
        w.WriteString("id", ownId);
        w.WriteEndObject();
        w.WriteString("snonce", snonce);
        w.WriteString("bind", bind);
    });

    /// <summary>
    /// The verdict of a socket LINK verified (13.6.5, 13.6.6): §7.1's verdict for its link — its scope, its node and how that id is
    /// verified, with words when it is not — and then the fields LINK appends, in order: the acceptor's proof, the lid, the level, the
    /// features it uses on this link, its inst and its beat.
    /// </summary>
    public static byte[] Verdict(WsLink link, string proof, string lid, IReadOnlyList<string> can, string inst, int beatMs) => Json(w =>
    {
        w.WriteString("type", "auth");
        w.WriteBoolean("ok", true);
        w.WriteString("scope", link.Scope);
        w.WriteStartObject("node");
        w.WriteString("t", link.Kind ?? "");
        w.WriteString("id", link.Node ?? "");
        w.WriteString("verified", NodeBindings.Word(link.Verified));
        w.WriteEndObject();
        if (link.Verified == NodeProof.None && link.Words is { } words) w.WriteString("words", words);
        w.WriteString("proof", proof);
        w.WriteString("lid", lid);
        w.WriteNumber("level", 1);
        w.WriteStartArray("can");
        foreach (var c in can) w.WriteStringValue(c);
        w.WriteEndArray();
        w.WriteString("inst", inst);
        w.WriteNumber("beatMs", beatMs);
    });

    /// <summary>
    /// A refused link message's answer (13.6.5): <c>{"type":"auth","ok":false,"error","class","retry"}</c>. The socket keeps the public
    /// frame, a public viewer (§7.1) — unless the viewing code shuts it to a socket without a credential: then <c>"view":true</c> follows
    /// and the socket closes after it.
    /// </summary>
    public static byte[] Refusal(string cls, string error, int retrySeconds, bool view = false) => Json(w =>
    {
        w.WriteString("type", "auth");
        w.WriteBoolean("ok", false);
        w.WriteString("error", error);
        w.WriteString("class", cls);
        w.WriteNumber("retry", Math.Max(1, retrySeconds));
        if (view) w.WriteBoolean("view", true);
    });

    /// <summary>
    /// The decision for one link message. <paramref name="rawQuery"/> is the socket's query as it came, and <paramref name="node"/> and
    /// <paramref name="kind"/> its <c>node</c> and <c>t</c> as the server reads them: 13.6.5's <c>client</c> must equal both readings, so
    /// the id a verdict names and a binding takes is the one the proof covered; <paramref name="snonce"/> and <paramref name="bind"/> are the ones this socket's offer named;
    /// <paramref name="lockedFor"/> is asked once, for the kid the message names, before its proof is checked: how many seconds its pair
    /// is still locked by the wrong-proof table, or 0 (a caller may hold the pair's place for this proof then, and settle it by
    /// <see cref="LinkDecision.CountKid"/>). A message that is not one link message of 13.6.5's shape is <c>malformed</c>, an offer this core does not speak
    /// <c>version</c>, both uncounted. A proof that is not 64 hex is a wrong proof (13.6.4).
    /// </summary>
    public static LinkDecision Decide(string rawQuery, string? node, string? kind, JsonElement message, string ownId, string snonce, string bind,
        IReadOnlyList<ApiKey> keys, Func<string, int> lockedFor)
    {
        var parse = message.ValueKind == JsonValueKind.Object ? PeerLink.ParseLinkMessage(rawQuery, message) : PeerLink.LinkParse.Malformed;
        if (parse.Refused is "malformed") return LinkDecision.Refused("malformed", MalformedWords);
        if (parse.Refused is "version") return LinkDecision.Refused("version", VersionWords);
        if (!message.TryGetProperty("client", out var client) || client.ValueKind != JsonValueKind.Object
            || !Says(client, "id", node) || !Says(client, "t", kind))
            return LinkDecision.Refused("malformed", MalformedWords);

        // A proof that is not 64 hex: a wrong proof for the kid it names, which the table counts only for a key in force.
        if (parse.Ask is not { } ask || parse.Proof is not { } proof)
        {
            var named = message.TryGetProperty("kid", out var k) && k.ValueKind == JsonValueKind.String && PeerLink.IsKid(k.GetString()) ? k.GetString()! : null;
            if (named is not null && lockedFor(named) is var held and > 0) return LinkDecision.Refused("locked", ProofsLocked, held);
            return LinkDecision.Refused("refused", PeerLink.ProofWords, countKid: named is not null && KeyOf(keys, named) is not null ? named : null);
        }
        if (lockedFor(ask.Kid) is var locked and > 0) return LinkDecision.Refused("locked", ProofsLocked, locked);

        var key = KeyOf(keys, ask.Kid);
        var k32 = key is not null ? Convert.FromHexString(key.Hash) : RandomNumberGenerator.GetBytes(32);
        var t = PeerLink.Transcript("ws", ask.Offer, PeerLink.Speaks(ask.Offer) ?? PeerLink.Version, Kind + ":" + ownId, ask.T + ":" + ask.Id, ask.Kid,
            snonce, ask.CNonce, bind);
        if (t is null) return LinkDecision.Refused("malformed", MalformedWords);
        var verified = PeerLink.Verify(PeerLink.ClientProof(k32, t), proof);
        if (!verified || key is null) return LinkDecision.Refused("refused", PeerLink.ProofWords, countKid: key is not null ? ask.Kid : null);
        return new LinkDecision(true, null, "", 0, key, PeerLink.ServerProof(k32, t), PeerLink.Lid(t), PeerLink.Session(k32, t), null);
    }

    /// <summary>The key in force whose kid is <paramref name="kid"/>, or null (the word <c>code</c> included: Stagewatch holds no code).</summary>
    public static ApiKey? KeyOf(IReadOnlyList<ApiKey> keys, string kid)
    {
        if (kid == "code") return null;
        ApiKey? hit = null;
        foreach (var k in keys)
            if (k.Hash.Length == 64 && PeerLink.IsHex(k.Hash, 64) && PeerLink.Kid(Convert.FromHexString(k.Hash)) == kid) hit ??= k;
        return hit;
    }

    private static bool Says(JsonElement o, string name, string? value)
        => value is not null && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && string.Equals(v.GetString(), value, StringComparison.Ordinal);

    private static byte[] Json(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
