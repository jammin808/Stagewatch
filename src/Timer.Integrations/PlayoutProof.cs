using System.Globalization;
using System.Security.Cryptography;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>What a playout link proves to its peer with (Show Bus 13.5): nothing, the pairing token as a code, an issued app key, or a key a code enrolled.</summary>
public enum PlayoutProofKind { None, Code, Key, Enrolled }

/// <summary>
/// A key a peer's verdict enrolled for this link (Show Bus 13.5.7): its kid, its K and the tag of the token it was enrolled from as
/// Settings stores it (<see cref="PlayoutKeptStore.Tag"/>), so a token changed since starts without it. K is a secret: it is kept
/// with the machine as the link's token is (sealed for this Windows user) and never reaches a frame, a log, a journal row, the
/// context or the settings' clear text; this record's text names its kid alone.
/// </summary>
public sealed record PlayoutEnrolment(string Kid, byte[] K, string Tag)
{
    /// <summary>The form the desk seals and keeps: kid, K as hex and the token's tag, one line.</summary>
    public string Sealable() => Kid + " " + Convert.ToHexStringLower(K) + " " + Tag;

    /// <summary>A kept enrolment read back, or null for one of any other shape.</summary>
    public static PlayoutEnrolment? Read(string? kept)
    {
        var parts = (kept ?? "").Split(' ');
        if (parts.Length != 3 || !PeerLink.IsKid(parts[0]) || parts[0] == "code" || !PeerLink.IsHex(parts[1], 64) || parts[2].Length == 0) return null;
        var k = Convert.FromHexString(parts[1]);
        return PeerLink.Kid(k) == parts[0] ? new PlayoutEnrolment(parts[0], k, parts[2]) : null;
    }

    public override string ToString() => "enrolled key " + Kid;

    public bool Equals(PlayoutEnrolment? other)
        => other is not null && Kid == other.Kid && Tag == other.Tag && CryptographicOperations.FixedTimeEquals(K, other.K);

    public override int GetHashCode() => HashCode.Combine(Kid, Tag);
}

/// <summary>
/// The rule a playout link decides LINK by, in one place (Show Bus 13.4.2, 13.6, 13.9): never by a hello's cap — a cap is a hint, and
/// Patterns holds <c>peerlink</c> back from its hello while its own conformance rows are open — but by what the operator gave the link
/// and the connection it is on. An issued app key (13.5.1's shape, Patterns' <c>ptk_</c>, PlayMap's <c>pmk_</c>) is only ever proved
/// by LINK, over plain or TLS, and never sent; a key a verdict enrolled likewise; the pairing token is proved by LINK as a code only
/// inside TLS, where the acceptor may enrol a key for this link (13.5.7), and on a plain socket stays 13.6.8's level-0 proof, as a
/// 2.22 link proved it. A peer that answers LINK as an older build stays at level 0, amber, <c>legacy</c>. PlayMap offers no LINK yet
/// (Mapper's PM-9b): a PlayMap link is never sent one, on any carrier, and <see cref="PlayMapSpeaksLink"/> is the seam that ends it.
/// </summary>
public static class PlayoutProof
{
    /// <summary>
    /// The seam LINK to PlayMap goes through once its line channel answers LINK (Mapper's PM-9b, wp4-peerlink): false until then, so a
    /// PlayMap link greets at level 0 and says so, whatever key it holds.
    /// </summary>
    public const bool PlayMapSpeaksLink = false;

    /// <summary>Show Bus §2's kind this Stagewatch names itself in LINK (13.6.4): the connector a peer binds an app key to is <c>stagewatch:&lt;its id&gt;</c>.</summary>
    public const string SelfKind = "stagewatch";

    /// <summary>A token typed as an app key: 13.5.1's shape (a lowercase prefix of 2–8 letters, <c>_</c>, and 32 symbols of the key alphabet).</summary>
    public static bool IsKey(string token)
    {
        if (token.IndexOf('_', StringComparison.Ordinal) is not (var at and >= 2 and <= 8)) return false;
        return token[..at].All(c => c is >= 'a' and <= 'z') && token.Length == at + 33
            && token[(at + 1)..].All(c => KeyAlphabet.Contains(c, StringComparison.Ordinal));
    }

    private const string KeyAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>
    /// What the link proves with: an enrolled key held for the token it was enrolled from comes first (13.5.7: the link adopted it), then
    /// an app key typed in the token's place, then the token as a code when 13.5.1 gives it a canonical form; else nothing.
    /// </summary>
    public static PlayoutProofKind KindOf(string token, PlayoutEnrolment? enrolled)
    {
        if (enrolled is not null) return PlayoutProofKind.Enrolled;
        if (IsKey(token)) return PlayoutProofKind.Key;
        return token.Length > 0 && PeerLink.Canon(token) is { Length: > 0 } ? PlayoutProofKind.Code : PlayoutProofKind.None;
    }

    /// <summary>The kid LINK names for what the link proves with (13.5.1): an app key's or an enrolled key's, <c>code</c> for the token; null for none.</summary>
    public static string? Kid(string token, PlayoutEnrolment? enrolled) => KindOf(token, enrolled) switch
    {
        PlayoutProofKind.Enrolled => enrolled!.Kid,
        PlayoutProofKind.Key => PeerLink.Kid(PeerLink.KeyK(token)),
        PlayoutProofKind.Code => "code",
        _ => null,
    };

    /// <summary>K for what the link proves with (13.5.1): never sent, never published; null for none.</summary>
    public static byte[]? K(string token, PlayoutEnrolment? enrolled) => KindOf(token, enrolled) switch
    {
        PlayoutProofKind.Enrolled => enrolled!.K,
        PlayoutProofKind.Key => PeerLink.KeyK(token),
        PlayoutProofKind.Code => PeerLink.CodeK(PeerLink.Canon(token)!),
        _ => null,
    };

    /// <summary>
    /// Whether a connection offers LINK: a link that names itself (a Show Bus id of its own), to a peer that answers LINK (never a
    /// PlayMap node, on its line channel or its TCP wire alike, until <see cref="PlayMapSpeaksLink"/>), with a key always, and with the
    /// token only inside TLS.
    /// </summary>
    public static bool OffersLink(PlayoutProofKind kind, bool tls, bool playMap, string? self)
    {
        if (self is null || !PeerLink.IsId(self)) return false;
        if (playMap && !PlayMapSpeaksLink) return false;
        return kind switch
        {
            PlayoutProofKind.Key or PlayoutProofKind.Enrolled => true,
            PlayoutProofKind.Code => tls,
            _ => false,
        };
    }

    /// <summary>
    /// 13.5.3 with Patterns' map (its AppKeyScopes): whether a link stood at <paramref name="scope"/> may send a playout command to a peer
    /// of <paramref name="kind"/>. Every command a playout link writes to Patterns — <c>VIDEO …</c>, <c>CUE GO</c>, <c>CUE STANDBY</c> —
    /// is <c>drive</c>'s; <c>follow</c> and <c>post</c> read only. A link with no verdict's scope (level 0) and a peer of another kind are
    /// not held here: the peer decides, and its refusal is said in its words.
    /// </summary>
    public static bool ScopeAllows(string? kind, string? scope)
        => kind != PlayoutRules.Patterns || scope is null or "none" or "drive" or "operate";

    /// <summary>What a link at <paramref name="scope"/> may do to <paramref name="peer"/>, in the words the desk shows before a press; "" when it is not held by scope.</summary>
    public static string ScopeWords(string? kind, string? scope, string peer)
        => ScopeAllows(kind, scope) ? ""
            : $"{peer} stands this link at scope {scope} — it may read, not drive; {peer}'s operator can raise its key on its Nodes page";

    /// <summary>
    /// A link's standing in a reader's words, from its record (13.16.3): its level and how it was proved, the key it proved with (by
    /// kid, never the key; <paramref name="enrolled"/>: one a peer enrolled for it, 13.5.7) and the scope it stands at. "" before
    /// anything was proved.
    /// </summary>
    public static string StandingWords(PeerLinkRecord r, bool enrolled = false)
    {
        if (r.Proof == "mutual")
        {
            var kid = r.Kid == "code" ? "the pairing code" : (enrolled ? "enrolled key " : "key ") + (r.Kid.Length > 8 ? r.Kid[..8] : r.Kid);
            return "PeerLink " + r.Level.ToString(CultureInfo.InvariantCulture) + " · proved both ways with " + kid + " · scope " + r.Scope
                + (r.Tls == "pinned" ? " · pinned " + r.Pin[..Math.Min(8, r.Pin.Length)] : "");
        }
        return r.Proof switch
        {
            "legacy" => "level 0 — proved by this link alone; the peer does not prove itself",
            "open" => "the peer asks for no key",
            _ => "",
        };
    }

    /// <summary>
    /// 13.5.7: the key a verdict enrolled, checked — derived from the code's K over this link's T (<c>HMAC-SHA256(K_code, "enrol\n" ‖ T)</c>)
    /// and taken only when its kid is the one the verdict named and the link proved the code inside TLS; null otherwise.
    /// </summary>
    public static PlayoutEnrolment? Enrolled(string? named, byte[] codeK, string t, string bind, string tag)
    {
        if (named is null || !PeerLink.MayEnrol("code", bind)) return null;
        var k = PeerLink.EnrolK(codeK, t);
        return PeerLink.Kid(k) == named ? new PlayoutEnrolment(named, k, tag) : null;
    }
}
