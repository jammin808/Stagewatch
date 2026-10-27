using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// An HTTP trigger's auth profile: the kind of door its secret opens (<see cref="TriggerAuthRules.Kinds"/>) and the secret itself,
/// DPAPI-protected for this Windows user (base64) — never in clear in the settings file. The desk protects what a person types and
/// resolves it as the trigger goes; the secret rides in no status, journal row, log line, frame or context.
/// </summary>
public sealed class TriggerAuth
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = TriggerAuthRules.None;

    /// <summary>The secret, DPAPI-protected (base64); "" for none.</summary>
    [JsonPropertyName("secret")] public string SecretProtected { get; set; } = "";
}

/// <summary>
/// An HTTP trigger's target with its authority fixed: the scheme, host and port as typed — no placeholder among them — and the rest
/// (path, query, fragment), the only part show text may fill in. <see cref="Where"/> is <c>host:port</c> as words say it.
/// </summary>
public sealed record TriggerTarget(string Scheme, string Host, int Port, string Authority, string Rest)
{
    public bool Https => Scheme == "https";

    /// <summary><c>host:port</c>, an IPv6 host in brackets; the default port named.</summary>
    public string Where => (Host.Contains(':') ? "[" + Host + "]" : Host) + ":" + Port.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The rules an HTTP trigger's secret goes by, pure. A secret goes only over a channel that keeps it (Show Bus 13.6.8, 13.7, 13.8): to
/// this PC (a loopback host — <c>localhost</c> or a loopback address), or over <c>https://</c> to a certificate a person pinned on this
/// desk; never over plain HTTP to another machine. With a secret, the scheme, host and port are fixed text — a placeholder there is
/// refused — so no running-order text can send it elsewhere, and the client that carries it follows no redirect. A rule that breaks
/// them is refused at save and, should a file hold one, at fire. A rule with no auth profile is never refused by them: one whose
/// address carries a secret in its query (<c>?pin=</c>) fires as before, with a warning offering to move the secret into a profile.
/// </summary>
public static class TriggerAuthRules
{
    public const string None = "none";
    public const string PlayMap = "playmap";
    public const string Patterns = "patterns";
    public const string Stagewatch = "stagewatch";
    public const string Bearer = "bearer";

    /// <summary>Every kind an auth profile may name.</summary>
    public static readonly IReadOnlyList<string> Kinds = [None, PlayMap, Patterns, Stagewatch, Bearer];

    /// <summary>The prefix of a trigger's request id (Show Bus 13.12.1): <c>tr&lt;n&gt;-&lt;inst&gt;</c>.</summary>
    public const string RequestIdPrefix = "tr";

    /// <summary>What a Patterns desk is told the sender is (<c>X-Patterns-Client</c>): a deliberate client, never another site's page.</summary>
    public const string PatternsClient = "Stagewatch";

    /// <summary>The longest secret a profile carries.</summary>
    public const int SecretMaxChars = 256;

    /// <summary>The words a query parameter is named by when it carries a secret: <c>pin</c>, <c>token</c>, <c>key</c>, <c>secret</c>.</summary>
    public static readonly IReadOnlyList<string> SecretNames = ["pin", "token", "key", "secret"];

    /// <summary>Where a person sets a trigger's profile, as every refusal names it.</summary>
    public const string SettingWords = "Settings → Triggers";

    /// <summary>The profile's kind as the rules read it: lower case, trimmed; <see cref="None"/> for none.</summary>
    public static string KindOf(TriggerRule rule)
        => (rule.Auth?.Kind ?? "").Trim().ToLowerInvariant() is { Length: > 0 } kind ? kind : None;

    /// <summary>Whether the rule has an auth profile (any kind but <see cref="None"/>, a kind no rule may name among them).</summary>
    public static bool Carries(TriggerRule rule) => KindOf(rule) != None;

    /// <summary>
    /// The target as these rules read it, or null with why: <c>http://</c> or <c>https://</c> typed out, then a host and optional port
    /// with no placeholder, no user or password, no space, backslash or control character — the rest starts with <c>/</c>, <c>?</c> or
    /// <c>#</c> and is the only part show text fills in.
    /// </summary>
    public static TriggerTarget? ParseTarget(string? target, out string? problem)
    {
        problem = null;
        var t = (target ?? "").Trim();
        string scheme;
        if (t.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) scheme = "https";
        else if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) scheme = "http";
        else
        {
            problem = "its target is not an http:// or https:// address typed out (no placeholder in its scheme)";
            return null;
        }
        var after = t[(scheme.Length + 3)..];
        var end = after.IndexOfAny(['/', '?', '#']);
        var authority = end < 0 ? after : after[..end];
        var rest = end < 0 ? "" : after[end..];
        if (authority.Length == 0) problem = "its target names no host";
        else if (authority.IndexOfAny(['{', '}']) >= 0)
            problem = "a placeholder in its host or port — with a secret, where it goes is fixed text; placeholders go in the path or query";
        else if (authority.Contains('@')) problem = "a user or password in its address — a secret goes in the rule's auth profile, never in an address";
        else if (authority.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')) problem = "its host holds a space, a backslash or a control character";
        if (problem is not null) return null;
        if (!Uri.TryCreate(scheme + "://" + authority + "/", UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown
            || uri.Host.Length == 0 || uri.UserInfo.Length > 0)
        {
            problem = "its target is not an address Stagewatch can read";
            return null;
        }
        var host = uri.Host.StartsWith('[') && uri.Host.EndsWith(']') ? uri.Host[1..^1] : uri.Host;
        return new TriggerTarget(scheme, host.ToLowerInvariant(), uri.Port, authority, rest);
    }

    /// <summary>
    /// Whether a target's host is this PC (Show Bus 13.1's local): a loopback address, or <c>localhost</c>, as the seam
    /// <paramref name="isLoopback"/> judges an address (a test plays another machine from loopback by answering false). Never a name
    /// that is not <c>localhost</c>: a host name resolves wherever its resolver says.
    /// </summary>
    public static bool IsLocalHost(string host, Func<IPAddress, bool>? isLoopback = null)
    {
        var check = isLoopback ?? PlayoutGate.IsLoopback;
        if (IPAddress.TryParse(host, out var ip)) return check(ip);
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && check(IPAddress.Loopback);
    }

    /// <summary>
    /// The name of a query parameter of <paramref name="target"/> that carries a secret, or null: one named by one of
    /// <see cref="SecretNames"/>, or with a part so named (<c>api_key</c>, <c>access-token</c>), or run together after <c>api</c>,
    /// <c>access</c> or <c>auth</c> (<c>apikey</c>, <c>accessToken</c>).
    /// </summary>
    public static string? QuerySecret(string? target)
    {
        foreach (var (name, _) in Query(target))
        {
            var parts = name.ToLowerInvariant().Split(['_', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => SecretNames.Contains(p) || RunTogether(p))) return name;
        }
        return null;

        static bool RunTogether(string part)
            => SecretNames.Any(s => s != "pin" && part.EndsWith(s, StringComparison.Ordinal)
                                    && part[..^s.Length] is "api" or "access" or "auth");
    }

    /// <summary>The query's parameters as typed (template text, before any <c>#</c>), each name and value still escaped.</summary>
    private static IEnumerable<(string Name, string Value)> Query(string? target)
    {
        var t = target ?? "";
        if (t.IndexOf('#') is var hash and >= 0) t = t[..hash];   // a '?' in the fragment is no query
        var q = t.IndexOf('?');
        if (q < 0) yield break;
        var query = t[(q + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            yield return eq < 0 ? (pair, "") : (pair[..eq], pair[(eq + 1)..]);
        }
    }

    /// <summary>
    /// Null when <paramref name="rule"/> may be saved and fired as it stands; else why not, in words (the rule's name is the caller's to
    /// add). A rule with no auth profile always may. One with a profile: a kind these rules know, an HTTP action, a secret, a target
    /// whose scheme, host and port are fixed text, plain HTTP only to this PC (<see cref="IsLocalHost"/>, by
    /// <paramref name="isLoopback"/>), and no secret in its query besides.
    /// </summary>
    public static string? Problem(TriggerRule rule, Func<IPAddress, bool>? isLoopback = null)
    {
        var kind = KindOf(rule);
        if (kind == None) return null;
        if (!Kinds.Contains(kind))
            return $"its auth kind '{LoadNotes.Shown(kind)}' is not one Stagewatch knows ({string.Join(", ", Kinds)})";
        if (rule.Action != TriggerAction.Http) return "only an HTTP trigger carries a secret";
        if (string.IsNullOrEmpty(rule.Auth!.SecretProtected)) return "its auth profile has no secret — type it";
        if (ParseTarget(rule.Target, out var problem) is not { } target) return problem;
        if (!target.Https && !IsLocalHost(target.Host, isLoopback))
            return $"a secret goes over plain HTTP only to this PC (localhost, 127.0.0.1), and {target.Where} is another machine — use https://, "
                   + "whose certificate is pinned on first use and trusted on this desk";
        if (QuerySecret(rule.Target) is { } name) return $"its address carries {name}= besides its auth profile — take it out of the address";
        return null;
    }

    /// <summary>
    /// A warning to say at save, never a refusal, or null: an HTTP rule with no auth profile whose address carries a secret in its
    /// query, in clear wherever the address goes — offering to move it into a profile (<see cref="QueryPin"/> for Stagewatch's PIN).
    /// </summary>
    public static string? Warning(TriggerRule rule)
    {
        if (rule.Action != TriggerAction.Http || Carries(rule) || QuerySecret(rule.Target) is not { } name) return null;
        return $"its address carries {name}= in clear — readable on the network and in the target's logs; move it into the rule's auth profile"
               + (QueryPin(rule) is not null ? " (kind stagewatch: the PIN goes in a header, and only to this PC or a pinned HTTPS server)" : "");
    }

    /// <summary>
    /// The move a <see cref="Warning"/> offers for a Stagewatch PIN: the target without its <c>pin=</c> parameter and the PIN, unescaped,
    /// for a profile of kind <see cref="Stagewatch"/>; null when the query names no <c>pin</c>, or names it by a placeholder or empty.
    /// The desk protects the PIN before it is kept.
    /// </summary>
    public static (string Target, string Pin)? QueryPin(TriggerRule rule)
    {
        var t = rule.Target ?? "";
        var q = t.IndexOf('?');
        if (q < 0 || t.IndexOf('#') is >= 0 and var fragment && fragment < q) return null;
        var hash = t.IndexOf('#', q);
        var query = hash < 0 ? t[(q + 1)..] : t[(q + 1)..hash];
        var pairs = query.Split('&').ToList();
        var at = pairs.FindIndex(p => p.Split('=')[0].Equals("pin", StringComparison.OrdinalIgnoreCase));
        if (at < 0) return null;
        var value = pairs[at].IndexOf('=') is var eq and >= 0 ? pairs[at][(eq + 1)..] : "";
        if (value.Length == 0 || value.IndexOfAny(['{', '}']) >= 0) return null;
        pairs.RemoveAt(at);
        var kept = string.Join('&', pairs.Where(p => p.Length > 0));
        var target = t[..q] + (kept.Length > 0 ? "?" + kept : "") + (hash < 0 ? "" : t[hash..]);
        return (target, Uri.UnescapeDataString(value));
    }

    /// <summary>
    /// Why a secret as typed (or as resolved) cannot be sent, or null: none, longer than <see cref="SecretMaxChars"/>, a control
    /// character in it (a header would break), or any character outside printable ASCII (a header carries none: the client would refuse
    /// it with the connection already made, so it is refused here, before anything is dialled).
    /// </summary>
    public static string? SecretProblem(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return "its auth profile has no secret — type it";
        if (secret.Length > SecretMaxChars) return $"its secret is longer than {SecretMaxChars.ToString(CultureInfo.InvariantCulture)} characters";
        if (secret.Any(char.IsControl)) return "its secret holds a control character — type it again";
        if (secret.Any(c => c is < ' ' or > '~')) return "its secret holds a character outside printable ASCII, which no header carries — type it again";
        return null;
    }

    /// <summary>
    /// The headers a profile of <paramref name="kind"/> sends: PlayMap's <c>X-PlayMap-Token</c> and <c>X-Request-Id</c>; Patterns'
    /// <c>X-Patterns-Token</c> and <c>X-Patterns-Client</c> (its id rides in the body, <see cref="Body"/>); Stagewatch's
    /// <c>X-Stagewatch-Pin</c> (a PIN or a key) and <c>X-Request-Id</c>; a bearer's <c>Authorization</c>. None for no profile.
    /// </summary>
    public static IReadOnlyList<(string Name, string Value)> Headers(string kind, string secret, string requestId) => kind switch
    {
        PlayMap => [("X-PlayMap-Token", secret), ("X-Request-Id", requestId)],
        Patterns => [("X-Patterns-Token", secret), ("X-Patterns-Client", PatternsClient)],
        Stagewatch => [("X-Stagewatch-Pin", secret), ("X-Request-Id", requestId)],
        Bearer => [("Authorization", "Bearer " + secret)],
        _ => [],
    };

    /// <summary>The body a profile of <paramref name="kind"/> posts: Patterns' <c>/api/cmd</c> takes the request id before its line (<c>@tr3-1a2b3c4d CUE GO 12</c>); the rest post the payload as it is.</summary>
    public static string Body(string kind, string requestId, string payload)
        => kind == Patterns && payload.Length > 0 ? "@" + requestId + " " + payload : payload;

    /// <summary>What each kind's secret is and the header it rides in, as Settings offers the kinds.</summary>
    public static string KindWords(string kind) => kind switch
    {
        PlayMap => "PlayMap token (X-PlayMap-Token)",
        Patterns => "Patterns token (X-Patterns-Token)",
        Stagewatch => "Stagewatch PIN or key (X-Stagewatch-Pin)",
        Bearer => "Bearer token (Authorization)",
        _ => "none",
    };

    /// <summary>
    /// A rule's auth profile as Settings' grid shows it, never its secret: "none", "none — a secret in its address" (the warning's case),
    /// or the kind with "secret set" or "no secret — type it".
    /// </summary>
    public static string Label(TriggerRule rule)
    {
        if (!Carries(rule)) return Warning(rule) is not null ? "none — a secret in its address" : "none";
        var kind = KindOf(rule);
        var name = Kinds.Contains(kind) ? KindWords(kind) : $"'{LoadNotes.Shown(kind)}' (not a kind Stagewatch knows)";
        return name + (string.IsNullOrEmpty(rule.Auth!.SecretProtected) ? " · no secret — type it" : " · secret set");
    }

    /// <summary>
    /// Settings' profile box for one rule: <paramref name="kind"/> one of <see cref="Kinds"/> — <see cref="None"/> takes the profile
    /// away, secret and all — and the secret as protected text, null to keep the one kept (a kind changed keeps it too). Null when
    /// set; else why not, and nothing changed. Whether the rule may then be saved is <see cref="Problem"/>'s, said at Save.
    /// </summary>
    public static string? SetProfile(TriggerRule rule, string? kind, string? secretProtected)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        if (k.Length == 0) k = None;
        if (!Kinds.Contains(k)) return $"'{LoadNotes.Shown(kind)}' isn't a kind of secret Stagewatch knows ({string.Join(", ", Kinds)})";
        if (k == None)
        {
            rule.Auth = null;
            return null;
        }
        rule.Auth = new TriggerAuth { Kind = k, SecretProtected = secretProtected ?? rule.Auth?.SecretProtected ?? "" };
        return null;
    }

    /// <summary>
    /// The move a <see cref="Warning"/> offers for a Stagewatch PIN in an address (<see cref="QueryPin"/>): the PIN out of the address and
    /// into a profile of kind <see cref="Stagewatch"/>, protected by <paramref name="protect"/>. Made only when the rule would then be
    /// sent as it stands (<see cref="SecretProblem"/>, <see cref="Problem"/>): null when moved; else why not — plain HTTP to another
    /// machine among them — and the rule is left as it was, firing as before with its warning.
    /// </summary>
    public static string? MovePin(TriggerRule rule, Func<string, string> protect, Func<IPAddress, bool>? isLoopback = null)
    {
        if (rule.Action != TriggerAction.Http || Carries(rule)) return "only an HTTP trigger with no secret of its own moves a PIN out of its address";
        if (QueryPin(rule) is not { } found) return "its address names no PIN to move (pin=, typed out, not a placeholder)";
        var (target, pin) = found;
        if (SecretProblem(pin) is { } bad) return "its PIN can't be sent in a header: " + bad;
        var moved = rule.Clone();
        moved.Target = target;
        moved.Auth = new TriggerAuth { Kind = Stagewatch, SecretProtected = "set" };
        if (Problem(moved, isLoopback) is { } why) return "it would then be refused: " + why;
        rule.Target = target;
        rule.Auth = new TriggerAuth { Kind = Stagewatch, SecretProtected = protect(pin) };
        return null;
    }

    /// <summary>A rule's name as a refusal or note quotes it.</summary>
    public static string Named(TriggerRule rule) => $"trigger '{LoadNotes.Shown(rule.Name.Length > 0 ? rule.Name : "(no name)")}'";

    /// <summary>
    /// What a load says of the rules a file holds: each with an auth profile these rules refuse is refused as it fires until it is
    /// fixed. A note never quotes a secret, an address's query or the file's text past <see cref="LoadNotes.QuoteChars"/>.
    /// </summary>
    public static void Note(IEnumerable<TriggerRule> rules, LoadNotes notes)
    {
        foreach (var rule in rules)
            if (Problem(rule) is { } problem)
                notes.Add($"{Named(rule)} is refused as it fires until it is fixed in {SettingWords}: {problem}");
    }
}
