using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// A playout link as the running order's cue editor offers it: its name, its peer as people read it, its kind (heard, else set, else
/// null) and what its peer lists for cues (<see cref="PlayoutIndex"/>, null while not heard: the link not running, not paired, or its
/// peer answering as another kind or node), with the link's own status beside it.
/// </summary>
public sealed record PlayoutCueSource(string Name, string Peer, string? Kind, PlayoutIndex? Index, string Status);

/// <summary>
/// One thing a running-order cue can name, as the editor's picker offers it: a PlayMap playlist (<see cref="List"/>) and its item, or a
/// Patterns cue (<see cref="Item"/>, <see cref="List"/> empty); its label as people read it; whether the peer lists it switched off.
/// </summary>
public sealed record PlayoutCueChoice(string List, string Item, string Label, bool Off)
{
    public override string ToString() => Label + (Off ? " (switched off)" : "");
}

/// <summary>
/// What the cue editor offers and keeps, pure: the picker's choices from a peer's own list, the words of the standby and together
/// options for each kind, and a cue made by the rules a running order keeps (<see cref="PlayoutRules.SanitiseCues"/>), each refusal
/// said rather than mended.
/// </summary>
public static class PlayoutCueChoices
{
    /// <summary>
    /// The picker's choices from <paramref name="index"/>: each PlayMap playlist's items in its order ("Main show · Intro"), or each
    /// Patterns cue ("12 Walk-in", one switched off marked). Only ids a running-order cue can keep (<see cref="PlayoutRules.ValidRef"/>)
    /// are offered; <c>Left</c> counts those that are not, so the editor says some were left out and why.
    /// </summary>
    public static (IReadOnlyList<PlayoutCueChoice> Choices, int Left) Of(PlayoutIndex? index)
    {
        if (index is null) return ([], 0);
        var choices = new List<PlayoutCueChoice>();
        var left = 0;
        foreach (var pl in index.Playlists)
            foreach (var item in pl.Items)
            {
                if (!PlayoutRules.ValidRef(pl.Id) || !PlayoutRules.ValidRef(item.Id)) { left++; continue; }
                var name = pl.Name.Length > 0 ? pl.Name : pl.Id;
                choices.Add(new PlayoutCueChoice(pl.Id, item.Id, Cut(name + " · " + (item.Label.Length > 0 ? item.Label : item.Id)), false));
            }
        foreach (var c in index.Cues)
        {
            if (!PlayoutRules.ValidRef(c.Id)) { left++; continue; }
            choices.Add(new PlayoutCueChoice("", c.Id, Cut(PlayoutIndex.Words(c).Trim()), !c.Enabled));
        }
        return (choices, left);
    }

    /// <summary>Why a listed thing is not offered, as the editor says it beside the picker.</summary>
    public static string LeftWords(int left) => left == 0 ? "" : left.ToString(CultureInfo.InvariantCulture)
        + $" listed {(left == 1 ? "id isn't" : "ids aren't")} offered: a running-order cue keeps ids of 1-{PlayoutRules.MaxRefChars.ToString(CultureInfo.InvariantCulture)} letters, digits, - and _";

    /// <summary>
    /// The standby option's words for a link of <paramref name="kind"/>: off unless set, and what it does to the peer when the item
    /// loads — a PlayMap standby shows the item's first frame on air (on an idle playlist only), a Patterns one moves that desk's own
    /// standby; both said while the kind is not known.
    /// </summary>
    public static string StandbyWords(string? kind) => kind switch
    {
        PlayoutRules.PlayMap => "Stand by when the item loads — PlayMap shows the item's first frame on air at load (only on a playlist that is idle)",
        PlayoutRules.Patterns => "Stand by when the item loads — moves the Patterns desk's own standby",
        _ => "Stand by when the item loads — a PlayMap node shows the item's first frame on air at load; a Patterns desk's own standby moves",
    };

    /// <summary>The together option's words: one instant for every cue of the item marked so, where each link's clock allows it.</summary>
    public const string TogetherWords = "GO together — at one instant with the item's other cues marked so, where each link's clock allows (else said)";

    /// <summary>
    /// A cue as the editor makes it, or why not: a link by the name rule (it need not exist here: a cue whose link is missing is
    /// skipped with advice), a playlist "" or an id, an item an id, a label of at most <see cref="PlayoutRules.MaxLabelChars"/> with no
    /// control character — the rules <see cref="PlayoutRules.SanitiseCues"/> keeps, so no cue made here is dropped by a load.
    /// </summary>
    public static (PlayoutCue? Cue, string? Why) Make(string link, string list, string item, string label, bool standby, bool together)
    {
        link = (link ?? "").Trim().ToLowerInvariant();
        list = (list ?? "").Trim();
        item = (item ?? "").Trim();
        label = (label ?? "").Trim();
        if (PlayoutRules.NameProblem(link) is { } bad) return (null, "Choose a playout link: " + bad);
        if (list.Length > 0 && !PlayoutRules.ValidRef(list))
            return (null, $"\"{LoadNotes.Shown(list)}\" isn't a playlist id a cue keeps (1-{PlayoutRules.MaxRefChars.ToString(CultureInfo.InvariantCulture)} letters, digits, - and _) — pick it from the list");
        if (!PlayoutRules.ValidRef(item))
            return (null, item.Length == 0 ? "Choose what it cues: a PlayMap playlist's item, or a Patterns cue"
                : $"\"{LoadNotes.Shown(item)}\" isn't an id a cue keeps (1-{PlayoutRules.MaxRefChars.ToString(CultureInfo.InvariantCulture)} letters, digits, - and _) — pick it from the list");
        if (label.Length > PlayoutRules.MaxLabelChars || label.Any(char.IsControl))
            return (null, $"A cue's label is at most {PlayoutRules.MaxLabelChars.ToString(CultureInfo.InvariantCulture)} characters, with no control character");
        return (new PlayoutCue { Link = link, List = list, Item = item, Label = label, Standby = standby, Together = together }, null);
    }

    /// <summary>
    /// A cue as the editor lists it: the link, what it cues by its label (else its ids), and its options — "Stage left: Main show · Intro
    /// · stands by at load · together".
    /// </summary>
    public static string Describe(PlayoutCue c)
        => $"{PlayoutRules.Label(c.Link)}: {(c.Label.Length > 0 ? c.Label : c.List.Length > 0 ? c.List + " " + c.Item : c.Item)}"
           + (c.Standby ? " · stands by at load" : "") + (c.Together ? " · together" : "");

    /// <summary>The running-order grid's button for an item's cues: "Add…" for none, else how many.</summary>
    public static string Summary(IReadOnlyCollection<PlayoutCue> cues)
        => cues.Count == 0 ? "Add…" : cues.Count == 1 ? "1 cue" : cues.Count.ToString(CultureInfo.InvariantCulture) + " cues";

    private static string Cut(string text) => ShowBusText.Cut(text, PlayoutRules.MaxLabelChars);
}

/// <summary>
/// An entry of a playout right-click menu as the desk draws it: the action it runs, its words, and — when it is greyed — why, in the
/// words the press itself would be refused in or the peer's own (WHOAMI).
/// </summary>
public sealed record PlayoutOffer(string Action, string Header, string? Why)
{
    public bool Enabled => Why is null;
}

/// <summary>
/// The playout entries of the desk's right-click menus, pure, so a menu offers what its press would do: the VT chip's transport for
/// the link feeding the VT, greyed by the same plan the press runs (<see cref="PlayoutDirector.Plan"/>: Patterns' own words for a
/// clip on a clock of its own, a kind not heard) or by the peer's own WHOAMI — which never refuses a press locally (the peer's ERR
/// does), but says the desk's entry would only be refused.
/// </summary>
public static class PlayoutMenus
{
    /// <summary>The VT chip's entries, in the menu's order: the action each runs (<c>playout &lt;link&gt; &lt;action&gt;</c>) and its words.</summary>
    public static readonly IReadOnlyList<(string Action, string Header)> VtActions =
    [
        ("play", "Play"),
        ("pause", "Pause"),
        ("stop", "Stop (back to the top)"),
        ("restart", "Restart"),
        ("end", "Last 10 s"),
        ("seek", "Seek to…"),
    ];

    /// <summary>The seconds "Last 10 s" asks for, the button's and the entry's.</summary>
    public const int LastSeconds = 10;

    /// <summary>
    /// Why <paramref name="action"/> on <paramref name="link"/> would go nowhere now, or null: the licence; the link not connected; the
    /// plan's own refusal from the peer's facts; the peer's WHOAMI letting this PC only read (<see cref="PlayoutLink.ReadOnlyWords"/>).
    /// </summary>
    public static string? Why(PlayoutLinkSettings link, PlayoutFacts facts, PlayoutWhoami? whoami, bool connected, bool licensed, string action, int leadMs)
    {
        if (!licensed) return PlayoutRules.LicenceWords;
        var peer = PlayoutRules.Peer(facts.Kind ?? (link.Kind == PlayoutRules.Auto ? null : link.Kind), link.Name);
        if (!connected) return $"{peer} isn't connected — nothing would be sent";
        if (PlayoutDirector.Plan(link, facts, action, "", "", false, action == "end" ? LastSeconds : 0, leadMs).Error is { } refused) return refused;
        return whoami is { MayDrive: false } ? PlayoutLink.ReadOnlyWords(peer) : null;
    }

    /// <summary>The VT chip's entries for <paramref name="link"/>, the link feeding the VT, each greyed by <see cref="Why"/>.</summary>
    public static IReadOnlyList<PlayoutOffer> Vt(PlayoutLinkSettings link, PlayoutFacts facts, PlayoutWhoami? whoami, bool connected, bool licensed, int leadMs)
        => VtActions.Select(a => new PlayoutOffer(a.Action, a.Header, Why(link, facts, whoami, connected, licensed, a.Action, leadMs))).ToList();

    /// <summary>
    /// The line an entry runs, the one a deck or the wire sends: <c>playout &lt;link&gt; &lt;action&gt;</c>, with the seconds of end and seek
    /// (seek's as typed, read by the grammar).
    /// </summary>
    public static string Line(string link, string action, string seconds = "")
        => $"playout {link} {action}" + (action == "end" ? " " + LastSeconds.ToString(CultureInfo.InvariantCulture) : seconds.Length > 0 ? " " + seconds : "");

    /// <summary>
    /// Why a running-order row's "Cue playout now" and "GO playout now" go nowhere, or null: no cues on its item, or no licence. A cue
    /// its link cannot take is skipped with its own row, as when the item starts.
    /// </summary>
    public static string? RowWhy(RundownItem item, bool licensed)
        => item.Playout is not { Count: > 0 } ? "this item has no playout cues — add them in Edit running order…"
            : !licensed ? PlayoutRules.LicenceWords : null;

    /// <summary>The words of a row's press whose running order changed under its menu: nothing is sent.</summary>
    public const string ChangedUnder = "the running order changed under this menu — nothing sent";

    /// <summary>
    /// The item a running-order row drew (at <paramref name="index"/>, 0-based, with id <paramref name="id"/>) in the live running order
    /// <paramref name="items"/> at the press, or null when it is no longer there at that place — another running order made live, an
    /// undo, an item moved or removed — so a press never follows moving state onto another item's cues.
    /// </summary>
    public static RundownItem? RowItem(IReadOnlyList<RundownItem> items, int index, string id)
        => index >= 0 && index < items.Count && items[index] is { } item && id.Length > 0 && item.Id == id ? item : null;
}
