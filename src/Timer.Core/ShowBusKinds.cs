namespace Stagewatch.Core;

/// <summary>
/// The Show Bus's kind words as its master (docs/SHOWBUS.md) names them: a node's kind (§2's <c>t</c>, §14's <c>kind</c> rows)
/// with the name a reader shows for it; the kinds of link a context may draw between two ends (§6); and an item's kind (§6's
/// kind table) with where an Eye places it — the plane its lenses show it on and its column in its node's cluster.
/// ShowBusSpecTests holds each table to the master, row for row, so a word added on one side alone fails the suite.
/// </summary>
public static class ShowBusKinds
{
    /// <summary>§14's kind rows: every node kind of the contract, with its name for people, in the master's order.</summary>
    public static readonly IReadOnlyList<(string Kind, string Name)> Nodes =
    [
        ("main", "Stagewatch"), ("agent", "Stagewatch Slides"), ("patterns", "Patterns"), ("playmap", "PlayMap"), ("messenger", "AGMessage"),
    ];

    /// <summary>The name a reader shows for a node kind; null for a word that is not one of <see cref="Nodes"/>.</summary>
    public static string? Label(string? kind)
    {
        foreach (var (k, name) in Nodes)
            if (k == kind) return name;
        return null;
    }

    /// <summary>§6's link kinds: the Eye's words for what joins two ends, in the master's order.</summary>
    public static readonly IReadOnlyList<string> Links =
        ["feeds", "shows", "drives", "carries", "controls", "mirrors", "follows", "hears", "routes", "serves", "sends"];

    /// <summary>The plane of the kind table's last row, where a kind no row names is placed.</summary>
    public const string ElsePlane = "control";

    /// <summary>The column of the kind table's last row.</summary>
    public const int ElseColumn = 1;

    /// <summary>§6's kind table, its rows in the master's order without its last ("anything else": <see cref="ElsePlane"/>, <see cref="ElseColumn"/>).</summary>
    public static readonly IReadOnlyList<(IReadOnlyList<string> Kinds, string Plane, int Column)> Items =
    [
        (["link", "deck", "client", "desk", "node", "twin", "assistant", "ai", "approvals", "health", "machine", "preflight", "fault", "clock",
          "sync", "mirror", "fleet", "decode", "optimise", "scan", "calibration", "align", "site", "survey"], "control", 0),
        (["timer", "schedule", "department", "cue", "message", "stack", "playlist", "playlists", "chase", "timecode", "show"], "control", 1),
        (["air", "media", "content", "input", "videoio"], "video", 1),
        (["screen", "output", "outputs", "send", "stage"], "video", 2),
        (["projector", "surface", "led"], "video", 3),
        (["room", "meetings", "prompt", "help", "phone", "audience"], "room", 1),
        (["audio"], "audio", 1),
    ];

    /// <summary>Whether a row of the kind table names <paramref name="kind"/> (P6: a node uses these words where one fits).</summary>
    public static bool Known(string? kind) => kind is not null && Items.Any(row => row.Kinds.Contains(kind, StringComparer.Ordinal));

    /// <summary>Where an Eye places an item of <paramref name="kind"/>: its row's plane and column, or the last row's.</summary>
    public static (string Plane, int Column) Place(string? kind)
    {
        if (kind is null) return (ElsePlane, ElseColumn);
        foreach (var (kinds, plane, column) in Items)
            if (kinds.Contains(kind, StringComparer.Ordinal)) return (plane, column);
        return (ElsePlane, ElseColumn);
    }
}
