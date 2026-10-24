using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Stagewatch.Core;

/// <summary>
/// A browser window Stagewatch controls through an address of its own (/w/&lt;token&gt;). The token is the window's
/// identity and its pass: whoever opens the address sees what the operator puts on that window and controls nothing.
/// Identity lives here, not in the browser's storage, so two windows of one browser (OBS, vMix, a signage player) are
/// still two windows, and a browser that clears its storage is still the same window.
/// </summary>
public sealed class ScreenWindow
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    /// <summary>Letters, digits, - and _ (it is typed in commands: "window show lobby /clock").</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>The secret in the window's address. A new one (revoke) shuts the old address at once.</summary>
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    /// <summary>What it shows: a page on this Stagewatch ("/clock?h=12"), "holding" or "black".</summary>
    [JsonPropertyName("content")] public string Content { get; set; } = ScreenWindows.Holding;
    /// <summary>The holding page over the content. The content stays loaded underneath, so releasing is instant.</summary>
    [JsonPropertyName("hold")] public bool Hold { get; set; }
    /// <summary>Words on the holding page ("" = none).</summary>
    [JsonPropertyName("holdText")] public string HoldText { get; set; } = "";
    /// <summary>A holding-library image on the holding page, by id ("" = the show's own holding image when one is on air).</summary>
    [JsonPropertyName("holdImage")] public string HoldImage { get; set; } = "";
    /// <summary>When the link to Stagewatch drops: "hold" (the holding page, for audience screens) or "warn" (dimmed with
    /// NOT CONNECTED, for crew screens). Either way a stale clock is never shown as current.</summary>
    [JsonPropertyName("onLoss")] public string OnLoss { get; set; } = ScreenWindows.LossHold;
    /// <summary>Crossfade between contents, in milliseconds (0 = cut).</summary>
    [JsonPropertyName("fadeMs")] public int FadeMs { get; set; }
    /// <summary>Rises with every change the window must act on; the window reports the revision it shows.</summary>
    [JsonPropertyName("rev")] public int Revision { get; set; }

    public ScreenWindow Clone() => (ScreenWindow)MemberwiseClone();
}

/// <summary>What a window says it shows (it reports after each swap), as the server last heard it.</summary>
public sealed record WindowObserved(
    string WindowId,
    int Connected,
    int Revision,
    string Content,
    bool Hold,
    int SwapMs,
    DateTime? ShownUtc,
    string Error,
    string Device,
    string Address);

/// <summary>A window action, the window(s) it is for ("all", a name or an id) and its argument.</summary>
public sealed record WindowArgs(string Action, string Who, string Argument) : ShowArgs;

/// <summary>What a window command needs from outside the window list (owner thread).</summary>
public sealed record WindowContext(
    Func<string, string?> SetupPath,
    IReadOnlyList<HoldingImage> Images,
    Func<string> NewToken,
    Func<string> NewId);

/// <summary>What a window command does once applied.</summary>
public enum WindowEffect { None, Changed, Identify, Reload }

/// <summary>The result of a window command: the new list (when it changed), the words for the operator and the sender, the
/// windows it touched and what the server must do. <see cref="Ok"/> false = refused, nothing changed.</summary>
public sealed record WindowChange(bool Ok, string Words, IReadOnlyList<ScreenWindow> Windows, IReadOnlyList<string> Touched, WindowEffect Effect)
{
    public static WindowChange Refused(IReadOnlyList<ScreenWindow> current, string why) => new(false, why, current, Array.Empty<string>(), WindowEffect.None);
}

/// <summary>
/// The rules for controlled windows: their addresses, what they may show, and what each "window …" command does. Pure:
/// the host applies the result to the settings, the web server pushes it, the tests drive it directly.
/// </summary>
public static class ScreenWindows
{
    public const string Holding = "holding", Black = "black";
    public const string LossHold = "hold", LossWarn = "warn";
    public const int Max = 64, MaxHoldText = 200, MaxFadeMs = 5000;

    /// <summary>No 0/o or 1/l: a token read aloud or off a phone is typed right.</summary>
    private const string Alphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>26 characters × 5 bits = 130 random bits: an address nobody can guess or count through.</summary>
    public const int TokenLength = 26;

    public static string NewToken() => Random(TokenLength);

    /// <summary>A short id for lists, logs and commands ("wk3m9x2").</summary>
    public static string NewId() => "w" + Random(6);

    private static string Random(int length)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        foreach (var b in bytes) sb.Append(Alphabet[b & 31]);
        return sb.ToString();
    }

    /// <summary>Only strings this class could have made reach a lookup.</summary>
    public static bool ValidToken(string? token)
        => token is { Length: TokenLength } && token.All(c => Alphabet.Contains(c, StringComparison.Ordinal));

    public static bool ValidName(string? name) => ScreenPreset.ValidName(name);

    /// <summary>The window an address belongs to, or null. Every window is compared in constant time, so the time taken
    /// says nothing about how close a guess was.</summary>
    public static ScreenWindow? ByToken(IReadOnlyList<ScreenWindow> windows, string? token)
    {
        if (!ValidToken(token)) return null;
        var given = Encoding.ASCII.GetBytes(token!);
        ScreenWindow? hit = null;
        foreach (var w in windows)
            if (w.Token.Length == TokenLength && CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(w.Token)))
                hit = w;
        return hit;
    }

    /// <summary>Pages that need the PIN, and the server's own routes, are not window content.</summary>
    private static readonly string[] NotContent = { "/w/", "/api/", "/mcp", "/tls", "/p/", "/ws", "/control", "/producer", "/showcaller", "/designer", "/report" };

    /// <summary>Whether a window may show <paramref name="path"/>; <paramref name="why"/> says why not.</summary>
    public static bool CanShow(string? path, out string why)
    {
        why = "";
        if (!ScreenPreset.ValidPath(path)) { why = "a window shows a page on this Stagewatch, like /clock (never another site or a PIN)"; return false; }
        var bare = path!.Split('?', '#')[0].ToLowerInvariant();
        foreach (var n in NotContent)
        {
            var root = n.TrimEnd('/');
            if (bare == root || bare == root + ".html" || bare.StartsWith(root + "/", StringComparison.Ordinal))
            {
                why = $"{bare} isn't something a window shows (operator pages need the PIN; use a public page such as /viewer or /clock)";
                return false;
            }
        }
        return true;
    }

    /// <summary>"all" (or nothing) is every window; otherwise a name or an id, case-insensitive.</summary>
    public static IReadOnlyList<ScreenWindow> Match(IReadOnlyList<ScreenWindow> windows, string who)
    {
        who = who.Trim();
        if (who.Length == 0 || who.Equals("all", StringComparison.OrdinalIgnoreCase)) return windows;
        return windows.Where(w => w.Name.Equals(who, StringComparison.OrdinalIgnoreCase) || w.Id.Equals(who, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>"/clock", "holding", "black" or a saved setup's name → what a window shows, or null with why.</summary>
    public static string? ResolveContent(string target, Func<string, string?> setupPath, out string why)
    {
        why = "";
        target = target.Trim();
        if (target.Equals(Holding, StringComparison.OrdinalIgnoreCase)) return Holding;
        if (target.Equals(Black, StringComparison.OrdinalIgnoreCase)) return Black;
        if (target.StartsWith('/')) return CanShow(target, out why) ? target : null;
        if (!ScreenPreset.ValidName(target)) { why = "show a page (/clock), a saved setup's name, holding or black"; return null; }
        if (setupPath(target) is not { } path) { why = $"no saved setup called \"{target}\""; return null; }
        return CanShow(path, out why) ? path : null;
    }

    /// <summary>"/clock" stays "/clock"; holding and black read as words.</summary>
    public static string Describe(string content) => content switch { Holding => "the holding page", Black => "black", _ => content };

    /// <summary>
    /// Apply one "window …" command to the list. Refused with the reason when it would change nothing, names nothing, or
    /// asks for something a window can't show; otherwise the new list (copies: the caller's list is never written) and
    /// what the server must do.
    /// </summary>
    public static WindowChange Apply(IReadOnlyList<ScreenWindow> current, WindowArgs a, WindowContext ctx)
    {
        var action = a.Action.ToLowerInvariant();
        var list = current.Select(w => w.Clone()).ToList();

        if (action == "new")
        {
            var name = a.Who.Trim();
            if (!ValidName(name)) return WindowChange.Refused(current, "Window names use letters, digits, - and _ (e.g. lobby), up to 40");
            if (list.Any(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return WindowChange.Refused(current, $"There is already a window called {name}");
            if (list.Count >= Max) return WindowChange.Refused(current, $"Stagewatch keeps at most {Max} windows — delete one first");
            var content = Holding;
            if (a.Argument.Trim().Length > 0)
            {
                if (ResolveContent(a.Argument, ctx.SetupPath, out var why) is not { } c) return WindowChange.Refused(current, "Window: " + why);
                content = c;
            }
            string id;
            do id = ctx.NewId(); while (list.Any(w => w.Id == id));
            var made = new ScreenWindow { Id = id, Name = name, Token = ctx.NewToken(), Content = content, Revision = 1 };
            list.Add(made);
            return new WindowChange(true, $"Window {name} made: open its address on the screen it is for", list, new[] { id }, WindowEffect.Changed);
        }

        var hits = Match(list, a.Who);
        if (list.Count == 0) return WindowChange.Refused(current, "There are no windows yet — make one with: window new <name>");
        if (hits.Count == 0) return WindowChange.Refused(current, $"No window called \"{a.Who}\"");
        var all = a.Who.Trim().Length == 0 || a.Who.Trim().Equals("all", StringComparison.OrdinalIgnoreCase);
        var label = all ? (hits.Count == 1 ? hits[0].Name : $"{hits.Count} windows") : hits[0].Name;
        var ids = hits.Select(h => h.Id).ToList();

        switch (action)
        {
            case "identify":
                return new WindowChange(true, $"Identifying {label}", current, ids, WindowEffect.Identify);
            case "reload":
                return new WindowChange(true, $"Reloading {label}", current, ids, WindowEffect.Reload);

            case "show": case "cue":
            {
                if (ResolveContent(a.Argument, ctx.SetupPath, out var why) is not { } content) return WindowChange.Refused(current, "Window: " + why);
                var hold = action == "cue";
                var changed = hits.Where(w => w.Content != content || w.Hold != hold).ToList();
                if (changed.Count == 0)
                    return WindowChange.Refused(current, hold ? $"{label} already has {Describe(content)} cued behind the holding page" : $"{label} already shows {Describe(content)}");
                foreach (var w in changed) { w.Content = content; w.Hold = hold; w.Revision++; }
                return Done(hold ? $"{Describe(content)} cued behind the holding page on {label}" : $"{label} → {Describe(content)}", changed);
            }

            case "hold":
            {
                // hold <who> [toggle | text <words> | image <n|name|off>]
                var rest = a.Argument.Trim();
                var (kind, value) = SplitFirst(rest);
                kind = kind.ToLowerInvariant();
                if (kind == "toggle")
                {
                    // Any held → release them all; none held → hold them all (one press, one predictable state).
                    var hold = !hits.Any(w => w.Hold);
                    var flipped = hits.Where(w => w.Hold != hold).ToList();
                    foreach (var w in flipped) { w.Hold = hold; w.Revision++; }
                    return Done(hold ? $"{label}: holding page on" : $"{label}: holding page off", flipped);
                }
                string? text = null, image = null;
                if (kind == "text") text = value.Length > MaxHoldText ? value[..MaxHoldText] : value;
                else if (kind == "image")
                {
                    if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Length == 0) image = "";
                    else if (HoldingLibrary.Resolve(ctx.Images, value) is { } img) image = img.Id;
                    else return WindowChange.Refused(current, $"No holding image \"{value}\"");
                }
                else if (rest.Length > 0) return WindowChange.Refused(current, "window hold takes a window, then optionally toggle, text <words> or image <number|name|off>");
                var changed = hits.Where(w => !w.Hold || (text is not null && w.HoldText != text) || (image is not null && w.HoldImage != image)).ToList();
                if (changed.Count == 0) return WindowChange.Refused(current, $"{label} is already on its holding page");
                foreach (var w in changed)
                {
                    w.Hold = true;
                    if (text is not null) { w.HoldText = text; if (text.Length > 0) w.HoldImage = ""; }
                    if (image is not null) { w.HoldImage = image; if (image.Length > 0) w.HoldText = ""; }
                    w.Revision++;
                }
                return Done($"{label}: holding page on", changed);
            }

            case "release":
            {
                var changed = hits.Where(w => w.Hold).ToList();
                if (changed.Count == 0) return WindowChange.Refused(current, $"{label} isn't on its holding page");
                foreach (var w in changed) { w.Hold = false; w.Revision++; }
                return Done($"{label} → {(changed.Count == 1 ? Describe(changed[0].Content) : "their content")}", changed);
            }

            case "loss":
            {
                var mode = a.Argument.Trim().ToLowerInvariant();
                if (mode is not (LossHold or LossWarn)) return WindowChange.Refused(current, "window loss takes hold (the holding page) or warn (dim with NOT CONNECTED)");
                var changed = hits.Where(w => w.OnLoss != mode).ToList();
                if (changed.Count == 0) return WindowChange.Refused(current, $"{label} already does that when the link drops");
                foreach (var w in changed) { w.OnLoss = mode; w.Revision++; }
                return Done(mode == LossHold ? $"{label}: holding page if the link drops" : $"{label}: NOT CONNECTED warning if the link drops", changed);
            }

            case "fade":
            {
                if (!int.TryParse(a.Argument.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ms) || ms is < 0 or > MaxFadeMs)
                    return WindowChange.Refused(current, $"window fade takes milliseconds, 0 (cut) to {MaxFadeMs}");
                var changed = hits.Where(w => w.FadeMs != ms).ToList();
                if (changed.Count == 0) return WindowChange.Refused(current, $"{label} already {(ms == 0 ? "cuts" : $"fades in {ms} ms")}");
                foreach (var w in changed) { w.FadeMs = ms; w.Revision++; }
                return Done(ms == 0 ? $"{label}: cuts between pages" : $"{label}: {ms} ms fades", changed);
            }

            case "rename":
            {
                if (all || hits.Count != 1) return WindowChange.Refused(current, "Rename one window at a time");
                var name = a.Argument.Trim();
                if (!ValidName(name)) return WindowChange.Refused(current, "Window names use letters, digits, - and _ (e.g. lobby), up to 40");
                if (list.Any(w => w != hits[0] && w.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return WindowChange.Refused(current, $"There is already a window called {name}");
                if (hits[0].Name == name) return WindowChange.Refused(current, $"That window is already called {name}");
                var old = hits[0].Name;
                hits[0].Name = name; hits[0].Revision++;
                return Done($"Window {old} is now {name}", hits);
            }

            case "revoke":
            {
                foreach (var w in hits) { w.Token = ctx.NewToken(); w.Revision++; }
                return Done($"New address for {label}: the old one stopped working", hits);
            }

            case "delete":
            {
                if (all) return WindowChange.Refused(current, "Delete windows one at a time");
                list.Remove(hits[0]);
                return new WindowChange(true, $"Window {hits[0].Name} deleted: its address stopped working", list, ids, WindowEffect.Changed);
            }
        }
        return WindowChange.Refused(current, "window takes new, show, cue, hold, release, identify, reload, loss, fade, rename, revoke or delete");

        WindowChange Done(string words, IReadOnlyList<ScreenWindow> touched) => new(true, words, list, touched.Select(t => t.Id).ToList(), WindowEffect.Changed);
    }

    private static (string Head, string Tail) SplitFirst(string s)
    {
        var i = s.IndexOf(' ');
        return i < 0 ? (s, "") : (s[..i], s[(i + 1)..].Trim());
    }

    /// <summary>
    /// The windows as the Eye and an AI read them (Show Bus §6 items). Green only when the window has said it shows the
    /// revision it was sent; amber while a change is sent but not yet shown, when it couldn't show it, or when a window
    /// that was showing has lost its link; grey for a window nobody has opened yet. A lobby screen going dark never turns
    /// the whole show red.
    /// </summary>
    public static IReadOnlyList<ContextItem> ContextItems(IReadOnlyList<ScreenWindow> windows, IReadOnlyList<WindowObserved> observed)
    {
        var items = new List<ContextItem>(windows.Count);
        foreach (var w in windows)
        {
            var o = observed.FirstOrDefault(x => x.WindowId == w.Id);
            var wants = w.Hold ? "the holding page" : Describe(w.Content);
            string light, state;
            if (o is null || o.Connected == 0)
            {
                (light, state) = o?.ShownUtc is null
                    ? (Light.Grey, $"not opened yet (should show {wants})")
                    : (Light.Amber, $"link lost — the screen shows its {(w.OnLoss == LossWarn ? "NOT CONNECTED warning" : "holding page")}");
            }
            else if (o.Error.Length > 0)
                (light, state) = (Light.Amber, $"couldn't show {Describe(w.Content)}: {o.Error}; still on {(o.Hold ? "the holding page" : Describe(o.Content))}");
            else if (o.Revision != w.Revision)
                (light, state) = (Light.Amber, $"switching to {wants} (sent, not yet shown)");
            else
                (light, state) = (Light.Green, o.Hold ? $"holding page up ({Describe(w.Content)} ready underneath)" : $"showing {Describe(o.Content)}");
            items.Add(new ContextItem("window:" + w.Name.ToLowerInvariant(), "screen", "Window " + w.Name, light, state));
        }
        return items;
    }
}
