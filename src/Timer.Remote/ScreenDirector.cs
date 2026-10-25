using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>Whom a screen command reaches: the browser screens it names, or why none (with words the operator can act on).</summary>
public sealed record ScreenTarget(IReadOnlyList<ScreenInfo> Hits, string? Refusal);

/// <summary>
/// Runs "screen …" commands (from the desk, Companion, HTTP, OSC, MCP, triggers or the copilot): who they reach, then the
/// push. Screen commands reach browsers only; a show app's node link is never one of them, whatever it is called. The
/// order is fixed, so a name a peer chose can never shadow a screen: "all" (or nothing) is every browser that is neither
/// a control page nor a claim; then a screen id (a claim's included, so the operator can still drop a stray client); then
/// a name the operator gave a screen; only then a show app's id, or the name of a show app verified by its key, which is
/// refused as a show app; else nothing matches. Owner thread (the show thread in the app, the rig's loop, a test).
/// </summary>
public static class ScreenDirector
{
    /// <summary>
    /// Whom <paramref name="who"/> means among <paramref name="screens"/> (browsers and claims) and
    /// <paramref name="nodes"/> (show apps' links), in the fixed order. <paramref name="nodeName"/> is a show app's name as
    /// it told this Stagewatch (hello or context), or null.
    /// </summary>
    public static ScreenTarget Target(IReadOnlyList<ScreenInfo> screens, IReadOnlyList<NodeLinkFact> nodes, string who, Func<string, string?> nodeName)
    {
        who = who.Trim();
        if (Screens.IsAll(who))
        {
            var all = Screens.Match(screens, who);
            return all.Count > 0 ? new ScreenTarget(all, null) : new ScreenTarget(all, "Screens: no browser screen is connected");
        }
        var hits = Screens.Match(screens, who);
        if (hits.Count > 0) return new ScreenTarget(hits, null);
        return new ScreenTarget(Array.Empty<ScreenInfo>(), ShowApp(nodes, who, nodeName) ?? $"Screens: nothing matches \"{who}\"");
    }

    /// <summary>The refusal for a target that names a show app (its id, or the name of one verified by its key), or null.</summary>
    private static string? ShowApp(IReadOnlyList<NodeLinkFact> nodes, string who, Func<string, string?> nodeName)
    {
        foreach (var n in nodes)
        {
            if (n.Id is not { } id) continue;
            if (id.Equals(who, StringComparison.OrdinalIgnoreCase))
                return Words(who, nodeName(id) is { Length: > 0 } name ? name : n.Kind.Length > 0 ? n.Kind : "a show app");
        }
        foreach (var n in nodes)
        {
            if (n.Id is not { } id || n.Verified != NodeProof.Key) continue;
            if (nodeName(id) is { Length: > 0 } name && name.Equals(who, StringComparison.OrdinalIgnoreCase))
                return Words(who, NodeBindings.Describe(n.Kind, id));
        }
        return null;

        static string Words(string who, string what)
            => $"Screens: {who} is a show app ({what}), not a screen — screen commands reach browsers only (Producer → Show network)";
    }

    /// <summary>
    /// Run a screen command. Returns whether it reached anything, and the words for the operator (the status line when it
    /// did, the refusal when not). <paramref name="rename"/> and <paramref name="savePreset"/> keep a screen's name and a
    /// saved setup; <paramref name="resolvePreset"/> turns a setup's name or a page into the page to open.
    /// </summary>
    public static (bool Ok, string Words) Run(ScreenArgs args, WebServer? web, Func<string, string?> nodeName,
        Action<string, string> rename, Action<string, string> savePreset, Func<string, string?> resolvePreset)
    {
        var (action, arg, who) = (args.Action, args.Argument, args.Who.Trim());
        if (web is null) return (false, "Screens: turn on Settings → Remote first");
        var screens = web.ListScreens();
        var nodes = web.ListNodes();

        if (action == "name")
        {
            // A name is given to a screen id: a connected screen's (a claim's included), or one to come; never a show app's.
            var target = screens.FirstOrDefault(s => s.Id.Equals(who, StringComparison.OrdinalIgnoreCase));
            if (target is null && ShowApp(nodes, who, _ => null) is { } app) return (false, app);
            if (target is null && !Screens.ValidId(who)) return (false, $"Screens: no screen with id {who}");
            rename(target?.Id ?? who, arg);
            return (true, arg.Length > 0 ? $"Screen {who} is now \"{arg}\"" : $"Screen {who}: name cleared");
        }

        var (hits, refusal) = Target(screens, nodes, who, nodeName);
        if (refusal is not null) return (false, refusal);
        var ids = hits.Select(h => h.Id).ToList();
        switch (action)
        {
            case "identify":
                return Told(web.Identify(ids), $"Identifying {Count(hits.Count)}");
            case "reload":
                return Told(web.Reload(ids), $"Reloading {Count(hits.Count)}");
            case "disconnect":
                return Told(web.Disconnect(ids), $"Disconnected {Count(hits.Count)}");
            case "go":
                if (resolvePreset(arg) is not { } path) return (false, $"Screens: no saved setup called \"{arg}\"");
                return Told(web.Redirect(ids, path), $"Sent {Count(hits.Count)} to {arg}");
            case "save":
                savePreset(arg, hits[0].Page);
                return (true, $"Saved \"{arg}\" = {hits[0].Page}");
            default:
                return (false, $"Screens: no such action \"{action}\"");
        }

        // Attempts are not facts: a screen that left between the list and the push was told nothing.
        static (bool, string) Told(int tabs, string words) => tabs > 0 ? (true, words) : (false, "Screens: none of those screens is connected now");
        static string Count(int n) => n == 1 ? "1 screen" : $"{n} screens";
    }
}
