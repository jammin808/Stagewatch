using System.Globalization;

namespace Stagewatch.Core;

/// <summary>
/// One row for the journal about another node's own journal (<see cref="JournalKind.Peer"/>): a line it told, with the line's time
/// on that node's clock when it told one, or (<see cref="Note"/>) this desk's own words about what it took — the node joined the
/// report, lines may be missing. <see cref="HeardUtc"/> is when this Stagewatch first heard it; <see cref="Via"/> the road the
/// node's context came by — a name, which every reader of the row may print.
/// </summary>
public sealed record HeardLine(DateTime HeardUtc, NodeRef Node, string Text, long? AtMs, string Via, bool Note)
{
    /// <summary>
    /// The address the node's context was posted from, when only the operator's credential proves its node ("" otherwise): anyone
    /// who holds that credential may speak for an id no key is bound to, so the journal file keeps where such a context came from.
    /// For the journal file alone, apart from <see cref="Via"/>: no report, CSV, context or model prints it.
    /// </summary>
    public string From { get; init; } = "";

    /// <summary>The row as every host journals it: the peer kind, the node's id, name and kind, the line's own time and the road, each apart from its text; where an operator-proved context was posted from, apart from all of them.</summary>
    public JournalEntry Entry(int item = -1) => new(HeardUtc, JournalKind.Peer, item, Text, Note ? 1 : 0)
    {
        Node = Node.Id,
        NodeName = Node.Name.Length > 0 ? Node.Name : null,
        NodeKind = Node.Kind.Length > 0 ? Node.Kind : null,
        PeerAtMs = AtMs,
        Via = Via.Length > 0 ? Via : null,
        From = From.Length > 0 ? From : null,
    };
}

/// <summary>
/// Which of a node's <c>recent</c> lines are new to this Stagewatch (Show Bus §6), so each is journalled once: a sample of what the
/// node's own journal holds, never its record. Per node it keeps the latest time a line told (the high-water), the texts told at
/// exactly that time, and the list as last taken. A line is fresh when its time is above the high-water, or at it with a text not
/// yet taken; a line with no time, when it comes after the longest run that ends the last list and begins this one. So the same
/// list again gives nothing, however long after, and a line at or below the high-water is never taken twice. A node seen for the
/// first time gives no backlog: one row says it joins. A list that shares nothing with the one before — no line of the last list,
/// and not the line taken at the high-water — gives one row saying lines may be missing, then its lines.
/// <para>A node whose clock reads earlier than it did (set back, or started again on an earlier one) never drops out of the report
/// unsaid. A list that shares nothing and is timed wholly before the high-water is that node on an earlier clock: one row says so,
/// the high-water comes down to the list's latest time and its lines are taken. A line told after the lines the lists share, yet
/// timed before the high-water, may have been heard before, so it is left out — and one row says lines are being left out, once
/// until the node's clock passes the high-water again. A line listed again ahead of the shared ones (a list that grew back) is
/// neither: it was taken, or was the backlog, and says nothing.</para>
/// At most <see cref="MaxNodes"/> nodes are remembered; past that the one heard longest ago is forgotten, and joins again if it
/// returns. Pure but for what it remembers; one thread at a time.
/// </summary>
public sealed class PeerLines
{
    /// <summary>The most nodes remembered.</summary>
    public const int MaxNodes = PeerContexts.MaxPeers;

    private sealed class Seen
    {
        public long High;
        public HashSet<string> AtHigh = new(StringComparer.Ordinal);
        public List<string> Untimed = [];
        public DateTime TookUtc;
        public DateTime SeenUtc;
        public IReadOnlyList<PeerLine>? Last;
        /// <summary>A row already says this node's lines timed before the high-water are left out: not said again until its clock passes it.</summary>
        public bool Behind;
    }

    private readonly Dictionary<string, Seen> _nodes = new(StringComparer.Ordinal);
    private readonly Func<DateTime, string> _time;

    /// <summary>Lines taken with times worded by <paramref name="time"/> (a moment as the show reads it); UTC's time of day when none is given.</summary>
    public PeerLines(Func<DateTime, string>? time = null)
        => _time = time ?? (utc => utc.ToString("HH:mm:ss", CultureInfo.InvariantCulture));

    /// <summary>How many nodes are remembered now.</summary>
    public int Count => _nodes.Count;

    /// <summary>What a node's first sight says, in place of every line it already held.</summary>
    public static string Joins(string name) => $"{name} joins the report; its earlier lines are in its own journal";

    /// <summary>What a list that shares nothing with the one before says, ahead of its lines.</summary>
    public static string Gap(string name, string from, string to) => $"lines from {name} may be missing between {from} and {to}";

    /// <summary>What a list wholly timed before the lines already heard, and sharing none of them, says ahead of its lines.</summary>
    public static string Earlier(string name) => $"{name}'s clock reads earlier than lines already heard; lines may be missing";

    /// <summary>What new lines timed before ones already heard say, once, in place of themselves.</summary>
    public static string LeftOut(string name)
        => $"lines from {name} timed before ones already heard are left out until its clock passes them: its clock may have been set back";

    /// <summary>
    /// A node's <c>recent</c> as its context holds it now: the rows to journal, in order — nothing for a list already taken, one
    /// joining row for a node not remembered, else the fresh lines, after one row when lines may be missing before them, when the
    /// node's clock reads earlier than it did, or when lines are left out for their times. The node is named in this desk's words
    /// by <paramref name="node"/>'s name.
    /// </summary>
    public IReadOnlyList<HeardLine> Take(NodeRef node, IReadOnlyList<PeerLine> lines, string via, DateTime nowUtc)
    {
        var name = node.Name.Length > 0 ? node.Name : NodeBindings.Describe(node.Kind, node.Id);
        if (!_nodes.TryGetValue(node.Id, out var seen))
        {
            if (_nodes.Count >= MaxNodes) _nodes.Remove(_nodes.MinBy(kv => kv.Value.SeenUtc).Key);
            _nodes[node.Id] = seen = new Seen { TookUtc = nowUtc, SeenUtc = nowUtc };
            Keep(seen, lines, Untimed(lines));
            return [new HeardLine(nowUtc, node, Joins(name), null, via, Note: true)];
        }
        seen.SeenUtc = nowUtc;
        // The list as last taken, the same object: nothing to read. A list equal in content gives nothing either, by the rules below.
        if (ReferenceEquals(seen.Last, lines)) return [];

        var untimed = Untimed(lines);
        var overlap = Overlap(seen.Untimed, untimed);
        // What ties this list to what was taken before: a timed line the last list held, or the line taken at the high-water. A line
        // merely timed at or below the high-water ties nothing — a clock set back tells only such lines.
        var last = new HashSet<PeerLine>((seen.Last ?? []).Where(l => l.AtMs > 0));
        var timed = 0;
        var lastShared = -1;
        var below = true;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.AtMs <= 0) continue;
            timed++;
            if (line.AtMs >= seen.High) below = false;
            if (last.Contains(line) || (line.AtMs == seen.High && seen.AtHigh.Contains(line.Text))) lastShared = i;
        }
        var apart = timed > 0 && seen.High > 0 && lastShared < 0;
        // Nothing shared and every time before the high-water: the node on an earlier clock. Its lines are new, and the high-water follows it down.
        var earlier = apart && below;
        var fresh = new List<PeerLine>();
        var plain = 0;
        var passed = false;
        var behind = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.AtMs <= 0)
            {
                if (plain++ >= overlap) fresh.Add(line);
            }
            else if (earlier) fresh.Add(line);
            else if (line.AtMs > seen.High || (line.AtMs == seen.High && !seen.AtHigh.Contains(line.Text)))
            {
                fresh.Add(line);
                passed |= line.AtMs > seen.High;
            }
            // Told after every line the lists share, yet timed before the high-water: it may have been heard, so it is never taken.
            else if (line.AtMs < seen.High && i > lastShared) behind = true;
        }
        // Nothing shared with the list before, where that list held lines of the same sort: what came between may never have been seen.
        var gap = (apart && !earlier) || (untimed.Count > 0 && seen.Untimed.Count > 0 && overlap == 0);
        var rows = new List<HeardLine>(fresh.Count + 2);
        HeardLine Says(string words) => new(nowUtc, node, words, null, via, Note: true);
        if (gap) rows.Add(Says(Gap(name, _time(seen.TookUtc), _time(nowUtc))));
        if (earlier) rows.Add(Says(Earlier(name)));
        if (behind && !seen.Behind) rows.Add(Says(LeftOut(name)));
        if (behind) seen.Behind = true;
        else if (passed || earlier) seen.Behind = false;
        rows.AddRange(fresh.Select(l => new HeardLine(nowUtc, node, l.Text, l.AtMs > 0 ? l.AtMs : null, via, Note: false)));
        Keep(seen, lines, untimed, lower: earlier);
        seen.TookUtc = nowUtc;
        return rows;
    }

    /// <summary>
    /// The node's memory after a list: the high-water and the texts at it, the untimed lines as listed, and the list itself. The
    /// high-water only rises, but for a node on an earlier clock (<paramref name="lower"/>), where it becomes the list's latest time.
    /// </summary>
    private static void Keep(Seen seen, IReadOnlyList<PeerLine> lines, List<string> untimed, bool lower = false)
    {
        var high = lines.Count == 0 ? 0 : lines.Max(l => l.AtMs);
        if (high > seen.High || (lower && high > 0))
        {
            seen.High = high;
            seen.AtHigh = new HashSet<string>(StringComparer.Ordinal);
        }
        if (seen.High > 0) foreach (var l in lines) if (l.AtMs == seen.High) seen.AtHigh.Add(l.Text);
        seen.Untimed = untimed;
        seen.Last = lines;
    }

    /// <summary>The lines of a list that told no time, in its order.</summary>
    private static List<string> Untimed(IReadOnlyList<PeerLine> lines) => lines.Where(l => l.AtMs <= 0).Select(l => l.Text).ToList();

    /// <summary>The longest run that ends <paramref name="last"/> and begins <paramref name="next"/>: how many of the new list's first lines were already listed.</summary>
    private static int Overlap(List<string> last, List<string> next)
    {
        for (var k = Math.Min(last.Count, next.Count); k > 0; k--)
        {
            var same = true;
            for (var i = 0; i < k && same; i++) same = last[last.Count - k + i] == next[i];
            if (same) return k;
        }
        return 0;
    }
}
