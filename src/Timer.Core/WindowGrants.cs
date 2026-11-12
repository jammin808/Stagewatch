using System.Buffers;
using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>
/// One grant as one node link carries it (Show Bus 12.6's <c>grants</c> row): the window, how its app holds it, the page grant's address
/// when 12.5 lets this link carry it (else ""), and why not (<see cref="Cannot"/>, "" when the link was given the window).
/// </summary>
public sealed record GrantRow(string Window, string Name, string How, string Path, string Cannot);

/// <summary>
/// What a window grant's app hears on its node link and what the operator reads of it (Show Bus 12.5–12.6). Pure: the web server reads
/// the windows, the link and the key's mark, and sends what this says.
/// </summary>
public static class WindowGrants
{
    /// <summary>The most <c>can=</c> words a link is read for (Show Bus §14 <c>hello.caps</c>): the rest are ignored.</summary>
    public const int MaxCanWords = 32;

    /// <summary>
    /// The abilities a node link may offer for windows, each with the kinds its §14.6 cap row names: only an app of one of those kinds
    /// offers it (12.6). Any other word is not an ability this Stagewatch reads.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> AbilityKinds = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [ScreenWindows.CanPages] = ["patterns"],
        [ScreenWindows.CanDraw] = ["playmap"],
    };

    /// <summary>What a node link is carried by (12.6's <c>link</c>): TLS, local (loopback, 13.1) or neither.</summary>
    public const string LinkTls = "tls", LinkLocal = "local", LinkPlain = "plain";

    /// <summary>Why a page grant's address is not on a link (12.5's <c>cannot</c>), each worded for the app's operator.</summary>
    public const string NotEncrypted = "this link is not encrypted — link over wss://";
    public const string KeyInClear = "this node's key crossed the network unencrypted — the Stagewatch operator makes it a new key";
    public const string NoPages = "this link doesn't offer pages";
    public const string NoDraw = "this link doesn't offer window-draw";

    /// <summary>What an app hears for a <c>window-shown</c> it may not send (12.6).</summary>
    public const string DrawOnly = "only a show app a window is granted to draw may say what it draws";

    /// <summary>What a socket hears for a <c>grants-seen</c> on any link but a show app's own key-verified one.</summary>
    public const string SeenOnlyOnLink = "only a show app's key-verified link says which grants it saw";

    /// <summary>The longest <c>cannot</c> an app's <c>grants-seen</c> keeps.</summary>
    public const int MaxSeenCannot = 200;

    /// <summary>A <c>window-end</c>'s <c>why</c>: the grant ended while the window stays, or the window is gone.</summary>
    public const string EndWithdrawn = "withdrawn", EndDeleted = "deleted";

    /// <summary>
    /// The states a grant's row is in for the operator (<see cref="GrantObserved.State"/>), from nothing linked to shown: no key-verified
    /// link of its app (<c>waiting</c>); a link that may not carry it (<c>plain</c>, <c>key-in-clear</c>, <c>cannot</c>); given but not
    /// acknowledged (<c>sent</c>); acknowledged with no viewer (<c>held</c>); a viewer on it (<c>open</c>); a viewer that says it shows the
    /// window's revision (<c>shown</c>) or another, or could not (<c>stale</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> States = ["waiting", "plain", "key-in-clear", "cannot", "sent", "held", "open", "shown", "stale"];

    /// <summary>
    /// The abilities a link's <c>can=</c> offers (12.6): its first <see cref="MaxCanWords"/> comma-separated words, of which only an ability
    /// whose cap row names <paramref name="kind"/> is kept, once each. Fails closed: a word it does not know, or one for another kind, is not
    /// offered.
    /// </summary>
    public static IReadOnlyList<string> Offered(string? can, string? kind)
    {
        if (string.IsNullOrEmpty(can) || string.IsNullOrEmpty(kind)) return [];
        var offered = new List<string>(2);
        foreach (var word in can.Split(',').Take(MaxCanWords))
        {
            var w = word.Trim();
            if (AbilityKinds.TryGetValue(w, out var kinds) && kinds.Contains(kind, StringComparer.Ordinal) && !offered.Contains(w, StringComparer.Ordinal))
                offered.Add(w);
        }
        return offered;
    }

    /// <summary>
    /// The grants rows one link of <paramref name="node"/> hears (12.5–12.6), in the windows' order: every grant to that node. A page grant's
    /// path is set only on a TLS or local link that offers pages, on a key never seen in clear; a draw grant is given (an empty
    /// <c>cannot</c>) only on a link that offers window-draw, on a key never seen in clear. Otherwise <c>cannot</c> says why, the link's
    /// carrier first, then the key, then the ability.
    /// </summary>
    public static IReadOnlyList<GrantRow> For(IReadOnlyList<ScreenWindow> windows, string node, string link, IReadOnlyList<string> can, bool keyInClear)
    {
        var rows = new List<GrantRow>();
        foreach (var w in windows)
        {
            foreach (var g in w.Grants ?? [])
            {
                if (g is null || !string.Equals(g.Node, node, StringComparison.Ordinal)) continue;
                string cannot;
                if (g.How == ScreenWindows.HowPage)
                    cannot = link is not (LinkTls or LinkLocal) ? NotEncrypted
                        : keyInClear ? KeyInClear
                        : !can.Contains(ScreenWindows.CanPages, StringComparer.Ordinal) ? NoPages : "";
                else
                    cannot = keyInClear ? KeyInClear : !can.Contains(ScreenWindows.CanDraw, StringComparer.Ordinal) ? NoDraw : "";
                var path = g.How == ScreenWindows.HowPage && cannot.Length == 0 ? "/w/" + g.Token : "";
                rows.Add(new GrantRow(w.Id, w.Name, g.How, path, cannot));
            }
        }
        return rows;
    }

    /// <summary>The rows as JSON, alone: what a link compares to tell whether its grants changed.</summary>
    public static byte[] RowsJson(IReadOnlyList<GrantRow> rows) => Json(w => Rows(w, rows), array: true);

    /// <summary><c>{"type":"grants","from","rev","link","grants":[…]}</c> (12.6).</summary>
    public static byte[] GrantsJson(string from, int rev, string link, IReadOnlyList<GrantRow> rows) => Json(w =>
    {
        w.WriteString("type", "grants");
        w.WriteString("from", from);
        w.WriteNumber("rev", rev);
        w.WriteString("link", link);
        w.WritePropertyName("grants");
        Rows(w, rows);
    });

    private static void Rows(Utf8JsonWriter w, IReadOnlyList<GrantRow> rows)
    {
        w.WriteStartArray();
        foreach (var r in rows)
        {
            w.WriteStartObject();
            w.WriteString("window", r.Window);
            w.WriteString("name", r.Name);
            w.WriteString("how", r.How);
            w.WriteString("path", r.Path);
            w.WriteString("cannot", r.Cannot);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    /// <summary>A draw grant's window, <c>{"type":"window-assign","window",…,"rev"}</c>: 12.3's window message for it, the window named by <c>window</c>.</summary>
    public static byte[] AssignJson(ScreenWindow s) => Json(w =>
    {
        w.WriteString("type", "window-assign");
        w.WriteString("window", s.Id);
        w.WriteString("name", s.Name);
        w.WriteString("content", s.Content);
        w.WriteBoolean("hold", s.Hold);
        w.WriteString("holdText", s.HoldText);
        w.WriteString("holdImage", s.HoldImage);
        w.WriteString("onLoss", s.OnLoss);
        w.WriteNumber("fadeMs", s.FadeMs);
        w.WriteNumber("rev", s.Revision);
    });

    /// <summary><c>{"type":"window-end","window","why"}</c>, <c>why</c> <see cref="EndWithdrawn"/> or <see cref="EndDeleted"/>.</summary>
    public static byte[] EndJson(string windowId, string why) => Json(w =>
    {
        w.WriteString("type", "window-end");
        w.WriteString("window", windowId);
        w.WriteString("why", why);
    });

    /// <summary><c>{"type":"window-identify","window","name"}</c>: a drawing app shows the window's name, as a window does on <c>identify</c>.</summary>
    public static byte[] IdentifyJson(string windowId, string name) => Json(w =>
    {
        w.WriteString("type", "window-identify");
        w.WriteString("window", windowId);
        w.WriteString("name", name);
    });

    /// <summary>
    /// Where one grant stands for the operator (<see cref="States"/>), with words naming its app: a viewer on it first — its report, on the
    /// window's revision and with no error, is <c>shown</c>, any other report <c>stale</c>, none yet <c>open</c> — then no linked app
    /// <c>waiting</c>, then why its link may not carry it, then whether its app acknowledged what it was sent, and, held, the app's own
    /// reason for showing no page when it gave one.
    /// </summary>
    /// <param name="row">The row its app's newest key-verified link was last sent for it, or null when no such link is up.</param>
    /// <param name="acknowledged">That link's <c>grants-seen</c> names the revision of the grants it was last sent.</param>
    /// <param name="open">Its viewers now: links on a page grant's address, or the drawing link that reported.</param>
    /// <param name="shown">What its viewer last said it shows, while one is on it.</param>
    /// <param name="appCannot">Why the app says it shows no page at all (its <c>grants-seen</c>'s <c>cannot</c>), "" when it gave no reason:
    /// an acknowledged grant with no viewer says it in the app's words.</param>
    public static (string State, string Words) State(ScreenWindow window, WindowGrant grant, GrantRow? row, bool acknowledged, int open, ViewerObserved? shown,
        string appCannot = "")
    {
        var app = ScreenWindows.GrantLabel(grant);
        if (open > 0)
        {
            if (shown?.ShownUtc is null) return ("open", grant.How == ScreenWindows.HowPage ? $"{app} has it open" : $"{app} draws it");
            if (shown.Error.Length > 0) return ("stale", $"{app} couldn't show {ScreenWindows.Describe(window.Content)}: {shown.Error}");
            return shown.Revision == window.Revision ? ("shown", $"{app} shows it") : ("stale", $"{app} shows an older change (sent, not yet shown)");
        }
        if (row is null) return ("waiting", $"{app} hasn't linked with its node key yet");
        if (row.Cannot == NotEncrypted) return ("plain", $"{app}: {row.Cannot}");
        if (row.Cannot == KeyInClear) return ("key-in-clear", $"{app}: {row.Cannot}");
        if (row.Cannot.Length > 0) return ("cannot", $"{app}: {row.Cannot}");
        if (!acknowledged) return ("sent", $"sent to {app}, not yet acknowledged");
        if (appCannot.Length > 0)
            return ("held", grant.How == ScreenWindows.HowPage ? $"{app} has its address and shows no page: {appCannot}" : $"{app} has it and draws nothing: {appCannot}");
        return ("held", grant.How == ScreenWindows.HowPage ? $"{app} has its address and no page open" : $"{app} has it and draws nothing yet");
    }

    /// <summary>
    /// A grant's state (<see cref="States"/>) in the few words a window's item and the frame's <c>granted</c> carry after the app's name:
    /// "shows it", "has it open", "not linked yet"… An unknown state reads as itself.
    /// </summary>
    public static string Short(string state, string how) => state switch
    {
        "shown" => "shows it",
        "open" => how == ScreenWindows.HowDraw ? "draws it" : "has it open",
        "stale" => "behind",
        "held" => how == ScreenWindows.HowDraw ? "has it, draws nothing" : "has its address, no page open",
        "sent" => "sent, not yet acknowledged",
        "waiting" => "not linked yet",
        "plain" => "link not encrypted",
        "key-in-clear" => "key seen in clear",
        "cannot" => "cannot take it",
        _ => state,
    };

    /// <summary>
    /// A grant's state as a shape beside its words, never a colour alone (the desk's lights): ✓ shown; ■ the app has it, as it acknowledged
    /// or a viewer of it says (open, held); ○ not yet observed there (sent and not acknowledged, not linked yet); △ anything that needs the
    /// operator (behind, plain, key in clear, cannot).
    /// </summary>
    public static string Mark(string state) => state switch
    {
        "shown" => "✓",
        "open" or "held" => "■",
        "sent" or "waiting" => "○",
        _ => "△",
    };

    /// <summary>A grant its app has taken (Show Bus 12.6): it acknowledged what it was sent, or a viewer of it is or was on it.</summary>
    public static bool Taken(string state) => state is "held" or "open" or "shown" or "stale";

    private static byte[] Json(Action<Utf8JsonWriter> body, bool array = false)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
        {
            if (array) body(w);
            else
            {
                w.WriteStartObject();
                body(w);
                w.WriteEndObject();
            }
        }
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
/// The journal's row for a grant its app took (Show Bus 12.6, the operator's record beside "granted" and "withdrawn"): once per grant in
/// this desk's run, the first time its state says the app took it (<see cref="WindowGrants.Taken"/>), never an address. A grant is known by
/// its window, its app and when it was made, so a grant made again — even within one tick of its withdraw — is told again; one that ends is
/// forgotten. Nothing is kept across a restart: an app that takes its grants again after one is told again. Owner thread.
/// </summary>
public sealed class GrantsTaken
{
    private readonly HashSet<(string Window, string Node, DateTime Made)> _told = new();

    /// <summary>The rows to journal now, in the grants' order: "Patterns FOH took lobby's address", "PlayMap Stage left took stage to draw".</summary>
    public IReadOnlyList<string> Note(IReadOnlyList<ScreenWindow> windows, IReadOnlyList<GrantObserved> grants)
    {
        var held = new HashSet<(string, string, DateTime)>();
        foreach (var w in windows)
            foreach (var g in w.Grants ?? [])
                if (g is not null) held.Add((w.Id, g.Node, g.MadeUtc));
        _told.RemoveWhere(k => !held.Contains(k));
        var rows = new List<string>();
        foreach (var o in grants)
        {
            if (!WindowGrants.Taken(o.State)) continue;
            if (windows.FirstOrDefault(w => w.Id == o.WindowId) is not { } w || w.Grants?.FirstOrDefault(g => g?.Node == o.Node) is not { } g) continue;
            if (!_told.Add((w.Id, g.Node, g.MadeUtc))) continue;
            rows.Add(g.How == ScreenWindows.HowPage ? $"{ScreenWindows.GrantLabel(g)} took {w.Name}'s address" : $"{ScreenWindows.GrantLabel(g)} took {w.Name} to draw");
        }
        return rows;
    }
}
