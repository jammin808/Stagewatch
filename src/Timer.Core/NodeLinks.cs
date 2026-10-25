using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>What a socket on /ws is to this Stagewatch (Show Bus §7.1).</summary>
public enum WsClass
{
    /// <summary>A browser: listed in Screens, and reached by "all" unless it is a control page.</summary>
    Screen,
    /// <summary>
    /// Said it is a node (<c>node=</c>) and proved nothing that speaks for one: listed in Screens, flagged, never reached by
    /// "all" or an empty target, only by its own screen id or a name the operator gave it.
    /// </summary>
    Claim,
    /// <summary>
    /// A show app that proved a node key or the operator's credential: never listed as a screen, never sent a screen
    /// command. Its id may still be verified "none" (a key shared by two installs): the frames flow, the node routes refuse.
    /// </summary>
    Node,
}

/// <summary>What a /ws request's credential came to, as the server checked it.</summary>
public enum WsProofKind
{
    /// <summary>Nothing, or a PIN this Stagewatch doesn't ask for.</summary>
    None,
    /// <summary>The PIN this Stagewatch asks for, given right.</summary>
    Pin,
    /// <summary>A wrong PIN, a missing one on a control page, or an address locked out for wrong guesses.</summary>
    PinRefused,
    /// <summary>An API key in force (any scope).</summary>
    Key,
    /// <summary>A key that is unknown, revoked, locked out or over its rate.</summary>
    KeyRefused,
}

/// <summary>
/// A /ws request's credential as the server checked it. The server checks keys and PINs and counts the guesses; nothing
/// here counts or looks anything up, so the admission rules are the same for every server and every test.
/// </summary>
public readonly record struct WsCredential(WsProofKind Kind, string? KeyId, ApiKeyScope Scope, string? Error, int RetrySeconds)
{
    public static WsCredential None => default;
    public static WsCredential Pin => new(WsProofKind.Pin, null, ApiKeyScope.Read, null, 0);
    public static WsCredential PinRefused(string error) => new(WsProofKind.PinRefused, null, ApiKeyScope.Read, error, NodeLinks.RetrySeconds);
    public static WsCredential Key(string keyId, ApiKeyScope scope) => new(WsProofKind.Key, keyId, scope, null, 0);

    /// <summary>A refused key, held off for <paramref name="retrySeconds"/> (a rate limit's Retry-After), else the default.</summary>
    public static WsCredential KeyRefused(string error, int retrySeconds = 0)
        => new(WsProofKind.KeyRefused, null, ApiKeyScope.Read, error, retrySeconds > 0 ? retrySeconds : NodeLinks.RetrySeconds);
}

/// <summary>
/// One socket on /ws as this Stagewatch holds it: what it is, what it named, what proved it, and what it may read and do.
/// Immutable; a credential change swaps in a new one (<see cref="NodeLinks.Recheck"/>), latest wins.
/// </summary>
public sealed record WsLink
{
    public WsClass Class { get; init; }
    /// <summary>The node id and kind the socket named (<c>node=</c>, <c>t=</c>); null on a socket that named none.</summary>
    public string? Node { get; init; }
    public string? Kind { get; init; }
    /// <summary>What speaks for its id: a node key, the operator's credential, or nothing (a claim, a conflict, a key that names no id).</summary>
    public NodeProof Verified { get; init; }
    /// <summary>Why a link's id is verified "none", for people; null when it is verified, or a refusal said why.</summary>
    public string? Words { get; init; }
    /// <summary>The credential as the wire names it in a verdict: node, control, read, mcp, pin or none.</summary>
    public string Scope { get; init; } = "none";
    /// <summary>The key in force that admitted it (any scope); null when no key did, or it was revoked since.</summary>
    public string? KeyId { get; init; }
    public ApiKeyScope KeyScope { get; init; }
    /// <summary>
    /// <see cref="NodeLinks.PinHash"/> of the PIN in force when the PIN admitted it ("" when it was admitted to control with no
    /// PIN asked for); null when no PIN admitted it. A keyed mark under a key that never leaves this process: never the PIN,
    /// nor anything the PIN can be read back from.
    /// </summary>
    public string? PinHash { get; init; }
    /// <summary>It asked to control (<c>role=control</c>).</summary>
    public bool Control { get; init; }
    public bool CanControl { get; init; }
    /// <summary>It reads the operator frame (a key in force, or the PIN on a node link), read-only unless it controls.</summary>
    public bool OperatorRead { get; init; }
    /// <summary>The id a browser chose for itself (<c>cid</c>), or one the server made: its screen id while it is a screen.</summary>
    public string BrowserId { get; init; } = "";

    /// <summary>
    /// The id Screens and the screen pushes use: a browser's own id; <c>claim-</c> and 12 hex for a claim; <c>node:&lt;id&gt;</c>
    /// or <c>key:&lt;key id&gt;</c> for a node link, which no browser id can be (<c>Screens.ValidId</c> refuses ':').
    /// </summary>
    public string ScreenId => Class switch
    {
        WsClass.Node => Node is not null ? "node:" + Node : "key:" + KeyId,
        WsClass.Claim => NodeLinks.ClaimId(Kind ?? "", Node ?? ""),
        _ => BrowserId,
    };

    /// <summary>"patterns 9f8e7d6c" for a claim (what it claims to be), "" for anything else.</summary>
    public string Claim => Class == WsClass.Claim ? NodeBindings.Describe(Kind, Node ?? "") : "";
}

/// <summary>What admission made of a socket: its link, the one message it hears before any frame (or none), and whether it closes after it.</summary>
public sealed record WsAdmission(WsLink Link, byte[]? First, bool Close);

/// <summary>What a credential change made of an open socket: its link now, the message it hears (latest wins), and whether it closes.</summary>
public sealed record WsRecheck(WsLink Link, byte[]? Message, bool Close);

/// <summary>
/// One show app's link on /ws, as Screens' neighbours read it: grouped by the id it named (or by its key when it named
/// none). <see cref="Id"/> is null for a key that names no node id; <see cref="Verified"/> is what speaks for the id.
/// </summary>
public sealed record NodeLinkFact(string Kind, string? Id, NodeProof Verified, string? KeyId, string KeyName, string Address, DateTime ConnectedUtc, int Links, string? Words);

/// <summary>
/// Who is on /ws (Show Bus §7.1): a browser screen, a claim, or a show app's node link. A socket that names a node
/// (<c>/ws?node=&lt;id&gt;&amp;t=&lt;kind&gt;</c>, its credential in the Authorization header) hears what this Stagewatch made of
/// it before any frame; a node key or the operator's credential makes it a node link, which never hears a screen
/// command; anything less is a claim, kept out of "all". A refused key is told once and keeps the public frame: no socket
/// is closed for a key reason (only the viewing code closes), so an app redialling with a bad key never loops. Pure apart
/// from the bindings it is handed.
/// </summary>
public static class NodeLinks
{
    /// <summary>How long a refused credential waits before it tries again, in seconds (unless a rate limit says otherwise).</summary>
    public const int RetrySeconds = 60;

    public const string WindowNotNode = "a window's link is not a node link";
    public const string PinInUrl = "a node link carries its key in the Authorization header, never in the URL";
    public const string NodeDoesNotControl = "a node link reads; it does not control";
    public const string BadId = "a node id is 1-40 letters, digits, - or _";
    public const string BadKind = "a node link names its kind (t=patterns, playmap, messenger…)";
    public const string OwnId = "that's this Stagewatch";

    public const string NoKey = "no key — the id is only claimed";
    public const string ReadKey = "a read key reads; only a node key speaks for a node";
    public const string NamesNoId = "names no node id";
    public const string KeyRevoked = "this key was revoked";
    public const string PinChanged = "the PIN changed — enter the new one";

    /// <summary>What every claim's screen id starts with; a browser's own id never does (<c>Screens.ValidBrowserId</c>).</summary>
    public const string ClaimPrefix = "claim-";

    /// <summary>
    /// Why a request that names a node is refused before the upgrade (HTTP 400), or null. Checked before any credential,
    /// window address or viewing code is, so a refusal here is never counted toward a lockout.
    /// </summary>
    public static string? BadRequest(IReadOnlyDictionary<string, string> query, string? authorization, string? ownId)
    {
        if (!query.TryGetValue("node", out var node)) return null;
        if (query.ContainsKey("win") || IsWindowScheme(authorization)) return WindowNotNode;
        if (query.ContainsKey("pin")) return PinInUrl;
        if (query.GetValueOrDefault("role") == "control") return NodeDoesNotControl;
        if (!NodeBindings.ValidId(node)) return BadId;
        if (!NodeBindings.ValidKind(query.GetValueOrDefault("t"))) return BadKind;
        if (!string.IsNullOrEmpty(ownId) && string.Equals(node, ownId, StringComparison.Ordinal)) return OwnId;
        return null;
    }

    private static bool IsWindowScheme(string? authorization)
    {
        var a = (authorization ?? "").TrimStart();
        var space = a.IndexOf(' ');
        return (space < 0 ? a : a[..space]).Equals("Window", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What a socket that passed <see cref="BadRequest"/> is, and what it hears first. <paramref name="checkView"/> is asked
    /// only when the credential doesn't open the viewing code itself (a key in force, the PIN, a control page let in);
    /// it says why the socket may not view, or null. A node key or the operator's credential on a socket that names a
    /// node speaks for it through <paramref name="bindings"/> (a node key bound to nothing is bound now, when the id is
    /// free); a conflict keeps the frames with the binding's words. A socket that names no node keeps today's rules: a
    /// control page hears its auth, a refused key hears one refusal and stays a viewer, anything else hears nothing new.
    /// </summary>
    public static WsAdmission Admit(IReadOnlyDictionary<string, string> query, WsCredential credential, bool pinAsked, string pinHash,
        Func<string?> checkView, string browserId, NodeBindings bindings, DateTime nowUtc)
    {
        var nodeAsked = query.TryGetValue("node", out var node);
        var control = !nodeAsked && query.GetValueOrDefault("role") == "control";
        var keyOk = credential.Kind == WsProofKind.Key && credential.KeyId is not null;
        var refused = credential.Kind is WsProofKind.KeyRefused or WsProofKind.PinRefused;

        var controlOk = false;
        string? controlError = null;
        if (control)
        {
            (controlOk, controlError) = credential.Kind switch
            {
                WsProofKind.Key when keyOk => ApiKeys.Allows(credential.Scope, ApiKeyScope.Control)
                    ? (true, (string?)null) : (false, $"this API key is {ApiKeys.Describe(credential.Scope)}"),
                WsProofKind.Pin => (true, null),
                WsProofKind.None => pinAsked ? (false, "PIN required") : (true, null),
                _ => (false, credential.Error ?? "refused"),
            };
        }

        var link = new WsLink
        {
            KeyId = keyOk ? credential.KeyId : null,
            KeyScope = credential.Scope,
            Scope = ScopeWord(credential),
            Control = control,
            CanControl = control && controlOk,
            OperatorRead = keyOk || credential.Kind == WsProofKind.Pin,
            PinHash = (control && controlOk && credential.Kind is WsProofKind.Pin or WsProofKind.None)
                      || (nodeAsked && credential.Kind == WsProofKind.Pin) ? pinHash : null,
            BrowserId = browserId,
        };

        // The viewing code: a credential that proves something opens it; anything else must pass it on its own.
        var passes = keyOk || credential.Kind == WsProofKind.Pin || (control && controlOk);
        var viewError = passes ? null : checkView();

        if (nodeAsked)
        {
            var kind = query.GetValueOrDefault("t") ?? "";
            link = link with { Node = node, Kind = kind };
            var speaker = credential.Kind switch
            {
                WsProofKind.Key when keyOk && credential.Scope == ApiKeyScope.Node => NodeCredential.Key(credential.KeyId),
                WsProofKind.Key when keyOk && credential.Scope == ApiKeyScope.Control => NodeCredential.Operator,
                WsProofKind.Pin => NodeCredential.Operator,
                _ => NodeCredential.None,
            };
            if (speaker.Proof == NodeProof.None)
            {
                link = link with { Class = WsClass.Claim, Verified = NodeProof.None, Words = refused ? null : keyOk ? ReadKey : NoKey };
            }
            else
            {
                // A proven credential always opens the viewing code, so this socket stays: its first proven use may bind.
                var verdict = bindings.Speak(speaker, kind, node!, nowUtc);
                link = link with { Class = WsClass.Node, Verified = verdict.Ok ? verdict.Proof : NodeProof.None, Words = verdict.Ok ? null : verdict.Error };
            }
        }
        else
        {
            // A node key that names no node is still a show app: off the screens, listed under its key.
            link = link with
            {
                Class = keyOk && credential.Scope == ApiKeyScope.Node ? WsClass.Node : WsClass.Screen,
                Words = keyOk && credential.Scope == ApiKeyScope.Node ? NamesNoId : null,
            };
        }

        if (viewError is not null)
        {
            // One message, then the socket closes. Its error is what the viewing code says, as screens have always heard it
            // (a page asks for the code under it); a refused credential adds why it was refused and when to try again.
            var first = refused && !control ? ViewRefusal(viewError, credential.Error ?? "refused", credential.RetrySeconds) : ViewRefusal(viewError);
            return new WsAdmission(link, first, Close: true);
        }

        byte[]? message;
        if (control) message = controlOk ? ControlOk : Refusal(controlError ?? "refused", retrySeconds: 0);
        else if (refused) message = Refusal(credential.Error ?? "refused", credential.RetrySeconds);
        else if (nodeAsked) message = Verdict(link);
        else message = null;   // an accepted key, or none: the first thing it hears is a frame, as always
        return new WsAdmission(link, message, Close: false);
    }

    /// <summary>
    /// The keys, the PIN or the bindings changed: what an open socket is now. What admitted it is checked again: a key no
    /// longer in force drops it to the public frame (a node link becomes a claim) with "this key was revoked"; a PIN changed
    /// (or set where there was none) drops a socket the PIN admitted to a viewer with "the PIN changed". A node link still
    /// proven speaks for its id again (<see cref="NodeBindings.SpeakAgain"/>): a key bound to nothing binds to a free id as
    /// its admission would have, and a verdict that moved is sent again, so a link is verified by key only while its key's
    /// binding holds its id, and by the operator only while no key does. Nothing closes unless the viewing code now refuses
    /// it: <paramref name="viewRefusal"/> is why it may not view without its credential, or null when it may.
    /// </summary>
    public static WsRecheck Recheck(WsLink link, IReadOnlyList<ApiKey> keys, NodeBindings bindings, string pinHashNow, string? viewRefusal)
    {
        var same = new WsRecheck(link, null, false);
        if (link.KeyId is { } keyId && !keys.Any(k => k.Id == keyId))
        {
            var dropped = Demote(link) with { KeyId = null };
            return new WsRecheck(dropped, Dropped(KeyRevoked, RetrySeconds, viewRefusal), Close: viewRefusal is not null);
        }
        if (link.KeyId is null && link.PinHash is { } admitted && admitted != pinHashNow)
        {
            if (pinHashNow.Length == 0)
            {
                // The PIN was taken away: every socket may control now, so a control page keeps on; on a node link the
                // PIN proves nothing any more, and it is a claim.
                if (link.Control) return new WsRecheck(link with { PinHash = "" }, null, false);
                var claim = link with { Class = WsClass.Claim, Verified = NodeProof.None, Words = NoKey, Scope = "none", OperatorRead = false, PinHash = null };
                return new WsRecheck(claim, Verdict(claim), Close: false);
            }
            var viewer = Demote(link) with { PinHash = null };
            var retry = link.Node is not null ? RetrySeconds : 0;
            return new WsRecheck(viewer, Dropped(PinChanged, retry, viewRefusal), Close: viewRefusal is not null);
        }
        if (link.Class != WsClass.Node || link.Node is null) return same;
        var speaker = link.KeyId is { } id
            ? link.KeyScope == ApiKeyScope.Node ? NodeCredential.Key(id) : NodeCredential.Operator
            : link.PinHash is not null ? NodeCredential.Operator : NodeCredential.None;
        if (speaker.Proof == NodeProof.None) return same;
        var verdict = bindings.SpeakAgain(speaker, link.Kind, link.Node);
        var (verified, words) = verdict.Ok ? (verdict.Proof, (string?)null) : (NodeProof.None, verdict.Error);
        if (verified == link.Verified && words == link.Words) return same;
        var next = link with { Verified = verified, Words = words };
        return new WsRecheck(next, Verdict(next), Close: false);
    }

    /// <summary>A credential gone from an open socket: one refusal, or, when the viewing code now refuses it, the one view message before it closes.</summary>
    private static byte[] Dropped(string error, int retrySeconds, string? viewRefusal)
        => viewRefusal is null ? Refusal(error, retrySeconds) : ViewRefusal(viewRefusal, error, retrySeconds);

    /// <summary>A socket whose credential is gone: no control, no operator frame; a node link is a claim, a key's node link a screen.</summary>
    private static WsLink Demote(WsLink link) => link with
    {
        CanControl = false,
        OperatorRead = false,
        Class = link.Node is not null ? WsClass.Claim : WsClass.Screen,
        Verified = NodeProof.None,
        Words = null,
        Scope = "none",
    };

    /// <summary>The key a PIN's mark is made under: random for this process, never written, logged or sent.</summary>
    private static readonly byte[] PinMarkKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// What a socket remembers of the PIN that admitted it: HMAC-SHA-256 of the PIN under a key made at random for this
    /// process, hex. A short PIN can't be read back from it (a bare hash of a 4-digit PIN could be), and equal PINs give
    /// equal marks within one run, which is all a recheck compares. "" when no PIN is asked for.
    /// </summary>
    public static string PinHash(string? pin)
        => string.IsNullOrEmpty(pin) ? "" : Convert.ToHexString(HMACSHA256.HashData(PinMarkKey, Encoding.UTF8.GetBytes(pin))).ToLowerInvariant();

    /// <summary>A claim's screen id: <c>claim-</c> and 12 hex of what it claims, the same for every socket that claims it.</summary>
    public static string ClaimId(string kind, string id)
        => ClaimPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kind + ":" + id))).ToLowerInvariant()[..12];

    private static string ScopeWord(WsCredential c) => c.Kind switch
    {
        WsProofKind.Key => c.Scope switch
        {
            ApiKeyScope.Node => "node",
            ApiKeyScope.Control => "control",
            ApiKeyScope.Mcp => "mcp",
            _ => "read",
        },
        WsProofKind.Pin => "pin",
        _ => "none",
    };

    private static readonly byte[] ControlOk = "{\"type\":\"auth\",\"ok\":true}"u8.ToArray();

    /// <summary>The verdict a socket that named a node hears: <c>{"type":"auth","ok":true,"scope",…,"node":{"t","id","verified"}}</c>, with words when its id is not verified.</summary>
    public static byte[] Verdict(WsLink link) => Json(w =>
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
    });

    /// <summary><c>{"type":"auth","ok":false,"error",…[,"retry":s]}</c>: one refusal; the socket keeps the public frame.</summary>
    public static byte[] Refusal(string error, int retrySeconds) => Json(w =>
    {
        w.WriteString("type", "auth");
        w.WriteBoolean("ok", false);
        w.WriteString("error", error);
        if (retrySeconds > 0) w.WriteNumber("retry", retrySeconds);
    });

    /// <summary>
    /// The one message before a socket closes for the viewing code: <c>{"type":"auth","ok":false,"view":true,"error",…}</c>,
    /// whose error is always the viewing code's words (a page asks for the code under them). When a key or the PIN was
    /// refused too, its words and when to try again follow: <c>…,"retry":s,"refused":"…"}</c>.
    /// </summary>
    public static byte[] ViewRefusal(string viewError, string? refused = null, int retrySeconds = 0) => Json(w =>
    {
        w.WriteString("type", "auth");
        w.WriteBoolean("ok", false);
        w.WriteBoolean("view", true);
        w.WriteString("error", viewError);
        if (refused is null) return;
        if (retrySeconds > 0) w.WriteNumber("retry", retrySeconds);
        w.WriteString("refused", refused);
    });

    private static byte[] Json(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(160);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}
