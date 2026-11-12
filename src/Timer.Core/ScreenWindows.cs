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
    /// <summary>
    /// The show apps this window is granted to, at most <see cref="ScreenWindows.MaxGrants"/>, each by its own node id. Kept with this
    /// PC's settings like the window itself, never in a show file; a page grant's token is a pass of its own (Show Bus §12.4).
    /// </summary>
    [JsonPropertyName("grants")] public List<WindowGrant> Grants { get; set; } = new();

    /// <summary>
    /// A copy that shares nothing with this one, its grants included (a missing grant, as a hand-edited file can hold, is not
    /// copied): readers hold copies while the owner edits.
    /// </summary>
    public ScreenWindow Clone()
    {
        var copy = (ScreenWindow)MemberwiseClone();
        copy.Grants = (Grants ?? new()).OfType<WindowGrant>().Select(g => g.Clone()).ToList();
        return copy;
    }
}

/// <summary>
/// One window granted to one show app (Show Bus §12.4): the app by its Show Bus id, kind and name as heard when the grant was made,
/// and how it holds the window, fixed then. A page grant (<see cref="ScreenWindows.HowPage"/>) has an address of its own, its
/// <see cref="Token"/>, made as a window's is: withdrawing that app stops only that address. A draw grant
/// (<see cref="ScreenWindows.HowDraw"/>) holds no address, so its token is empty.
/// </summary>
public sealed class WindowGrant
{
    /// <summary>The app's Show Bus id: the only node whose key-verified link may take this grant.</summary>
    [JsonPropertyName("node")] public string Node { get; set; } = "";
    /// <summary>The app's kind (its hello's <c>t</c>), as heard when the grant was made.</summary>
    [JsonPropertyName("t")] public string Kind { get; set; } = "";
    /// <summary>The app's name as heard when the grant was made, for the operator's words.</summary>
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary><see cref="ScreenWindows.HowPage"/> or <see cref="ScreenWindows.HowDraw"/>.</summary>
    [JsonPropertyName("how")] public string How { get; set; } = "";
    /// <summary>A page grant's own pass (its address is /w/&lt;token&gt;); "" for a draw grant.</summary>
    [JsonPropertyName("token")] public string Token { get; set; } = "";
    [JsonPropertyName("made")] public DateTime MadeUtc { get; set; }

    public WindowGrant Clone() => (WindowGrant)MemberwiseClone();
}

/// <summary>
/// A show app a window may be granted to, as this Stagewatch hears it: its kind and Show Bus id, its name, what it says it can do
/// (<see cref="Can"/>: the abilities its node link offers, its <c>can=</c>, when it is linked; else its hello's caps), and whether a
/// key bound to its id proves the link those abilities came from (<see cref="Verified"/>). A grant made on an app that is not verified
/// is worded as its claim, checked when it links.
/// </summary>
public sealed record ShowApp(string Kind, string Id, string Name, IReadOnlyList<string> Can, bool Verified)
{
    /// <summary>How the operator's words name it: its name, else its kind and id.</summary>
    public string Label => Name.Length > 0 ? Name : NodeBindings.Describe(Kind, Id);
}

/// <summary>An app looked up by the id or name a <c>window grant</c> line gives: the app, or why none is meant (words for the operator).</summary>
public sealed record AppLookup(ShowApp? App, string Error)
{
    public static AppLookup Found(ShowApp app) => new(app, "");
    public static AppLookup Missing(string error) => new(null, error);
}

/// <summary>
/// The window an address opens, and by which of its passes: <see cref="Via"/> is "" for the window's own address, else the node id of
/// the page grant whose address it is.
/// </summary>
public readonly record struct WindowHit(ScreenWindow Window, string Via);

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

/// <summary>
/// What a window command needs from outside the window list (owner thread). <see cref="FindApp"/> finds the show app a grant names
/// (null: this Stagewatch hears none, so every grant is refused); <see cref="NowUtc"/> stamps a grant (null: the system clock).
/// </summary>
public sealed record WindowContext(
    Func<string, string?> SetupPath,
    IReadOnlyList<HoldingImage> Images,
    Func<string> NewToken,
    Func<string> NewId,
    Func<string, AppLookup>? FindApp = null,
    Func<DateTime>? NowUtc = null);

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

    /// <summary>The most show apps one window is granted to (Show Bus §14, <c>window.grants</c>).</summary>
    public const int MaxGrants = 4;

    /// <summary>How an app holds a granted window: it opens the window's page in a browser of its own (ability <see cref="CanPages"/>).</summary>
    public const string HowPage = "page";

    /// <summary>How an app holds a granted window: it draws the content itself and holds no address (ability <see cref="CanDraw"/>).</summary>
    public const string HowDraw = "draw";

    /// <summary>The abilities (Show Bus §2 caps, a node link's <c>can=</c>) that let an app hold a window.</summary>
    public const string CanPages = "pages", CanDraw = "window-draw";

    /// <summary>The longest app name a grant keeps.</summary>
    public const int MaxAppName = 60;

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

    /// <summary>
    /// The window an address belongs to, and by which pass (<see cref="WindowHit.Via"/>: "" for the window's own address, else the
    /// node id of the page grant whose address it is), or null. Every window's token and every page grant's are compared, each in
    /// constant time and none skipped once one matches, so the time taken says nothing about how close a guess was or which pass it hit.
    /// </summary>
    public static WindowHit? Find(IReadOnlyList<ScreenWindow> windows, string? token)
    {
        if (!ValidToken(token)) return null;
        var given = Encoding.ASCII.GetBytes(token!);
        WindowHit? hit = null;
        foreach (var w in windows)
        {
            if (Same(given, w.Token)) hit = new WindowHit(w, "");
            foreach (var g in w.Grants ?? [])
                if (g is { How: HowPage } && Same(given, g.Token)) hit = new WindowHit(w, g.Node);
        }
        return hit;

        static bool Same(byte[] given, string token)
            => token.Length == TokenLength && CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(token));
    }

    /// <summary>The window an address belongs to, by any of its passes (<see cref="Find"/>), or null.</summary>
    public static ScreenWindow? ByToken(IReadOnlyList<ScreenWindow> windows, string? token) => Find(windows, token)?.Window;

    /// <summary>Every pass the windows hold: each window's token and each of its page grants' — what a line must never carry.</summary>
    public static IEnumerable<string> Tokens(IReadOnlyList<ScreenWindow> windows)
        => windows.SelectMany(w => (w.Grants ?? []).Where(g => g is { Token.Length: > 0 }).Select(g => g.Token).Prepend(w.Token));

    /// <summary>
    /// The windows as this PC's settings keep them, made whole: a window with a bad name, token or id, or a name another window took
    /// first, is dropped; a window whose address another window holds first keeps everything but that address, and gets a new one
    /// (<paramref name="newToken"/>, else <see cref="NewToken"/>), so one pass opens exactly one window; and of its grants only those that
    /// hold everything a grant must are kept (<see cref="KeptGrants"/>), every token unique across them all. Copies: the caller's objects
    /// are never shared.
    /// </summary>
    public static List<ScreenWindow> Kept(IEnumerable<ScreenWindow?>? saved, Func<string>? newToken = null)
    {
        var windows = (saved ?? []).Where(w => w is not null && ValidName(w.Name) && ValidToken(w.Token) && !string.IsNullOrEmpty(w.Id))
            .GroupBy(w => w!.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()!.Clone()).ToList();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in windows)
        {
            if (taken.Add(w.Token)) continue;
            string token;
            do token = (newToken ?? NewToken)(); while (!ValidToken(token) || !taken.Add(token));
            w.Token = token;
            w.Revision++;
        }
        foreach (var w in windows) w.Grants = KeptGrants(w.Grants, taken);
        return windows;
    }

    /// <summary>
    /// A window's grants as loading keeps them: at most <see cref="MaxGrants"/>, one per node, each with a valid Show Bus id and a
    /// <c>how</c> of page or draw; a page grant with a valid token no other pass has (<paramref name="taken"/>, which gains it), a draw
    /// grant with none. Anything else is dropped, never repaired: a grant is the operator's act, made again by a person.
    /// </summary>
    private static List<WindowGrant> KeptGrants(List<WindowGrant>? grants, HashSet<string> taken)
    {
        var kept = new List<WindowGrant>();
        foreach (var g in grants ?? [])
        {
            if (kept.Count >= MaxGrants) break;
            if (g is null || !NodeBindings.ValidId(g.Node) || kept.Any(k => k.Node == g.Node)) continue;
            g.Kind ??= ""; g.Name ??= ""; g.Token ??= "";
            var ok = g.How switch
            {
                HowPage => ValidToken(g.Token) && taken.Add(g.Token),
                HowDraw => g.Token.Length == 0,
                _ => false,
            };
            if (!ok) continue;
            if (!NodeBindings.ValidId(g.Kind)) g.Kind = "";
            if (g.Name.Length > MaxAppName) g.Name = g.Name[..MaxAppName];
            kept.Add(g);
        }
        return kept;
    }

    /// <summary>
    /// The one show app <paramref name="idOrName"/> names among <paramref name="heard"/>: by its Show Bus id, else by a name no other app
    /// has (case aside). Refused with words naming what was heard: no app by that id or name, or a name two apps share.
    /// </summary>
    public static AppLookup FindApp(IReadOnlyList<ShowApp> heard, string idOrName)
    {
        var want = idOrName.Trim();
        if (want.Length == 0) return AppLookup.Missing("window grant takes a window and a show app (its id, or its name in quotes)");
        if (heard.FirstOrDefault(a => a.Id.Equals(want, StringComparison.Ordinal)) is { } byId) return AppLookup.Found(byId);
        var named = heard.Where(a => a.Name.Equals(want, StringComparison.OrdinalIgnoreCase) || a.Label.Equals(want, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count == 1) return AppLookup.Found(named[0]);
        if (named.Count > 1) return AppLookup.Missing($"\"{want}\" names {named.Count} show apps — use its id ({string.Join(", ", named.Select(a => a.Id))})");
        return AppLookup.Missing(NoApp(want, heard));
    }

    /// <summary>The words for an app nobody heard, naming those that were (the first few).</summary>
    public static string NoApp(string want, IReadOnlyList<ShowApp> heard)
        => $"No show app called \"{want}\" on the show network ("
           + (heard.Count == 0 ? "this Stagewatch hears none" : "heard: " + string.Join(", ", heard.Take(8).Select(a => a.Label + " " + a.Id)) + (heard.Count > 8 ? ", …" : ""))
           + ")";

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
            case "grant":
                return Grant(current, list, a, ctx, all ? null : hits[0]);
            case "withdraw":
                // Every window's grants end only when all is said: a withdraw that names no window is refused, whichever door built it.
                if (a.Who.Trim().Length == 0) return WindowChange.Refused(current, "window withdraw takes a window (or all)");
                return Withdraw(current, list, hits, label, all, a.Argument.Trim(), ctx);

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
                // Every pass of the window changes at once: its own address and each page grant's. A grant's app keeps its grant (its new
                // address is its link's to carry); nobody's copy of an old one opens anything.
                var pages = 0;
                foreach (var w in hits)
                {
                    w.Token = NewPass(list, ctx);
                    foreach (var g in w.Grants.Where(g => g.How == HowPage)) { g.Token = NewPass(list, ctx); pages++; }
                    w.Revision++;
                }
                return Done($"New address for {label}: the old one stopped working"
                            + (pages == 0 ? "" : pages == 1 ? ", and the one granted app's address too" : $", and the {pages} granted apps' addresses too"), hits);
            }

            case "delete":
            {
                if (all) return WindowChange.Refused(current, "Delete windows one at a time");
                list.Remove(hits[0]);
                var granted = hits[0].Grants.Count;
                return new WindowChange(true, $"Window {hits[0].Name} deleted: its address stopped working"
                                              + (granted == 0 ? "" : granted == 1 ? ", and its grant ended" : $", and its {granted} grants ended"), list, ids, WindowEffect.Changed);
            }
        }
        return WindowChange.Refused(current, Actions);

        WindowChange Done(string words, IReadOnlyList<ScreenWindow> touched) => new(true, words, list, touched.Select(t => t.Id).ToList(), WindowEffect.Changed);
    }

    /// <summary>Why an unknown <c>window</c> action is refused: the actions there are.</summary>
    public const string Actions = "window takes new, show, cue, hold, release, identify, reload, loss, fade, rename, revoke, delete, grant or withdraw";

    /// <summary>A token no window or grant in <paramref name="list"/> holds: a pass opens exactly one thing.</summary>
    private static string NewPass(IReadOnlyList<ScreenWindow> list, WindowContext ctx)
    {
        var taken = Tokens(list).ToHashSet(StringComparer.Ordinal);
        string token;
        do token = ctx.NewToken(); while (taken.Contains(token));
        return token;
    }

    /// <summary>
    /// <c>window grant &lt;window&gt; &lt;app&gt;</c>: one window to one show app (Show Bus §12.4). The app is found by its id or a name no
    /// other app has (<see cref="WindowContext.FindApp"/>); how it holds the window is chosen now from what it offers — a page when it
    /// shows pages, else a drawing when it draws windows — and an app that offers neither is refused. A page grant gets an address of its
    /// own; a draw grant none. Refused, nothing changed: all windows, an unknown or ambiguous app, one already granted the window, a
    /// window granted to <see cref="MaxGrants"/> apps already. The grant changes nothing the window shows, so its revision stays.
    /// </summary>
    private static WindowChange Grant(IReadOnlyList<ScreenWindow> current, List<ScreenWindow> list, WindowArgs a, WindowContext ctx, ScreenWindow? window)
    {
        if (window is null) return WindowChange.Refused(current, "Grant one window at a time");
        var want = a.Argument.Trim();
        if (want.Length == 0) return WindowChange.Refused(current, "window grant takes a window and a show app (its id, or its name in quotes)");
        var found = ctx.FindApp?.Invoke(want) ?? AppLookup.Missing(NoApp(want, []));
        if (found.App is not { } app) return WindowChange.Refused(current, found.Error);
        if (!NodeBindings.ValidId(app.Id)) return WindowChange.Refused(current, $"{app.Label} has no valid Show Bus id");
        if (window.Grants.FirstOrDefault(g => g.Node == app.Id) is { } held)
            return WindowChange.Refused(current, $"{window.Name} is already granted to {GrantLabel(held)}");
        if (window.Grants.Count >= MaxGrants)
            return WindowChange.Refused(current, $"{window.Name} is granted to {MaxGrants} show apps already — withdraw one first");
        var can = app.Can ?? [];
        var how = can.Contains(CanPages, StringComparer.Ordinal) ? HowPage : can.Contains(CanDraw, StringComparer.Ordinal) ? HowDraw : null;
        if (how is null) return WindowChange.Refused(current, $"{app.Label} can't show a window: it offers neither {CanPages} nor {CanDraw}");
        var name = app.Name.Length > MaxAppName ? app.Name[..MaxAppName] : app.Name;
        var grant = new WindowGrant
        {
            Node = app.Id, Kind = NodeBindings.ValidId(app.Kind) ? app.Kind : "", Name = name, How = how,
            Token = how == HowPage ? NewPass(list, ctx) : "", MadeUtc = (ctx.NowUtc ?? (() => DateTime.UtcNow))(),
        };
        window.Grants.Add(grant);
        var label = $"{app.Label} ({app.Id})";
        var words = how == HowPage
            ? $"{window.Name} granted to {label} as a page — it takes the address when it links with its node key"
            : $"{window.Name} granted to {label} to draw — it draws the window when it links with its node key";
        if (!app.Verified) words += how == HowPage ? " (it says it shows pages; checked when it links)" : " (it says it draws windows; checked when it links)";
        return new WindowChange(true, words, list, new[] { window.Id }, WindowEffect.Changed);
    }

    /// <summary>
    /// <c>window withdraw &lt;window|all&gt; [app]</c>: ends the named window's grants (or every window's), to every app or to the one
    /// named by its id or the name its grant was made with. A page grant's address stops at once. Refused, nothing changed, when no
    /// such grant is there, or when a name (not an id) matches grants to more than one app: the words never name one app while two
    /// grants end.
    /// </summary>
    private static WindowChange Withdraw(IReadOnlyList<ScreenWindow> current, List<ScreenWindow> list, IReadOnlyList<ScreenWindow> hits, string label, bool all, string app,
        WindowContext ctx)
    {
        bool ById(WindowGrant g) => g.Node.Equals(app, StringComparison.Ordinal);
        bool Named(WindowGrant g) => app.Length == 0 || ById(g) || g.Name.Equals(app, StringComparison.OrdinalIgnoreCase)
                                     || GrantLabel(g).Equals(app, StringComparison.OrdinalIgnoreCase);
        var ended = hits.SelectMany(w => w.Grants.Where(Named).Select(g => (Window: w, Grant: g))).ToList();
        if (ended.Count == 0)
        {
            // The app as the operator knows it: its heard name when this Stagewatch hears it, else the word typed.
            var whom = app.Length == 0 ? "anyone" : ctx.FindApp?.Invoke(app).App?.Label ?? app;
            return WindowChange.Refused(current, all && hits.Count != 1 ? $"No window is granted to {whom}" : $"{label} isn't granted to {whom}");
        }
        var nodes = ended.Select(e => e.Grant.Node).Distinct(StringComparer.Ordinal).ToList();
        if (app.Length > 0 && nodes.Count > 1 && !ended.Any(e => ById(e.Grant)))
            return WindowChange.Refused(current, $"\"{app}\" names {nodes.Count} show apps — use its id ({string.Join(", ", nodes)})");
        foreach (var (w, g) in ended) w.Grants.Remove(g);
        var touched = ended.Select(e => e.Window).Distinct().ToList();
        var from = nodes.Count == 1 ? GrantLabel(ended[0].Grant) : $"{nodes.Count} show apps";
        var what = touched.Count == 1 ? touched[0].Name : $"{touched.Count} windows";
        var pages = ended.Count(e => e.Grant.How == HowPage);
        return new WindowChange(true, $"{what} withdrawn from {from}" + (pages == 0 ? "" : pages == 1 ? ": its granted address stopped working" : ": their granted addresses stopped working"),
            list, touched.Select(t => t.Id).ToList(), WindowEffect.Changed);
    }

    /// <summary>A grant's app as the operator's words name it: the name it was granted under, else its kind and id.</summary>
    public static string GrantLabel(WindowGrant g) => g.Name.Length > 0 ? g.Name : NodeBindings.Describe(g.Kind, g.Node);

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
