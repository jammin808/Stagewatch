using System.Text.Json;
using Stagewatch.Core;

/// <summary>
/// Which of a node's recent lines are new to this Stagewatch (Show Bus §6), pure and on times a test gives: the reader keeps each
/// line's own time; a node's first sight is one joining row and no backlog; then exactly the new lines, once — the same list again
/// gives nothing, however long after; the same words at another time are another line; lines that tell no time go by where the new
/// list overlaps the last; a list that shares nothing with the one before says lines may be missing; a node whose clock reads
/// earlier than it did is said, never silently dropped — its lines taken when they are all new, left out with one row when they
/// come among lines already heard; and past 32 nodes the one heard longest ago is forgotten, and joins again, with no backlog,
/// when it returns.
/// </summary>
static class PeerLinesTests
{
    static readonly DateTime T0 = new(2026, 10, 25, 3, 20, 0, DateTimeKind.Utc);
    static readonly NodeRef PlayMap = new("playmap", "9f8e7d6c", "Stage left");
    const string Via = "node key 'Stage left'";
    const long At = 1792898411000;   // 2026-10-25T03:20:11Z

    static IReadOnlyList<PeerLine> Lines(params (long At, string Text)[] lines) => lines.Select(l => new PeerLine(l.At, l.Text)).ToList();

    static string Said(IEnumerable<HeardLine> rows) => string.Join(" | ", rows.Select(r => (r.Note ? "note: " : "") + r.Text + (r.AtMs is { } at ? "@" + (at - At) : "")));

    public static void Run(Action<bool, string> Check)
    {
        // The reader: each recent line keeps its node's own time, beside the text every other reader takes.
        var json = JsonSerializer.Serialize(new
        {
            v = 1,
            node = new { t = "playmap", id = "9f8e7d6c", name = "Stage left" },
            recent = new object[]
            {
                new { at = At, text = "PLAYLIST GO show — Intro" }, new { text = "no time told" }, new { at = "1792898411000", text = "a time in a string" },
                new { at = -5, text = "a time before any" }, new { at = 1e40, text = "a time past any calendar" }, new { at = 1792898412500.5, text = "half a millisecond" },
                new { at = At + 1000, text = "" },
            },
        });
        var entry = PeerContexts.Parse(json, "10.0.0.9", T0);
        Check(entry is not null && entry.Recent.SequenceEqual(entry.RecentLines.Select(l => l.Text)) && entry.RecentLines.Count == 6
              && entry.RecentLines[0] == new PeerLine(At, "PLAYLIST GO show — Intro") && entry.RecentLines.Skip(1).All(l => l.AtMs == 0),
            $"peer lines: Parse keeps each line's at beside its text — whole milliseconds a calendar holds, else none; a line with no text is no line ({string.Join(",", entry?.RecentLines.Select(l => l.AtMs) ?? [])})");

        // First sight: no backlog, one row that says so.
        var lines = new PeerLines();
        var first = lines.Take(PlayMap, Lines((At, "PLAYLIST CUE show #1"), (At + 1000, "PLAYLIST GO show — Intro")), Via, T0);
        Check(first.Count == 1 && first[0] is { Note: true, AtMs: null, Via: Via, HeardUtc: var heard, Text: "Stage left joins the report; its earlier lines are in its own journal" } && heard == T0
              && first[0].Node == PlayMap,
            $"peer lines: a node's first post gives none of its lines and one row that it joins ({Said(first)})");

        // The next post: exactly the new lines, each with its own time and the road.
        var second = lines.Take(PlayMap, Lines((At, "PLAYLIST CUE show #1"), (At + 1000, "PLAYLIST GO show — Intro"), (At + 4000, "BLACKOUT ON"), (At + 9000, "refused: PANIC")), Via, T0.AddSeconds(5));
        Check(Said(second) == "BLACKOUT ON@4000 | refused: PANIC@9000" && second.All(r => r is { Note: false, Via: Via } && r.HeardUtc == T0.AddSeconds(5)),
            $"peer lines: the next post with two new lines gives those two, in its order, each with its own time ({Said(second)})");

        // The same again: nothing — at once, as another list of the same lines, and after the context was dropped (31 s).
        var same = Lines((At, "PLAYLIST CUE show #1"), (At + 1000, "PLAYLIST GO show — Intro"), (At + 4000, "BLACKOUT ON"), (At + 9000, "refused: PANIC"));
        Check(lines.Take(PlayMap, same, Via, T0.AddSeconds(6)).Count == 0 && lines.Take(PlayMap, same, Via, T0.AddSeconds(7)).Count == 0
              && lines.Take(PlayMap, Lines(same.Select(l => (l.AtMs, l.Text)).ToArray()), Via, T0.AddSeconds(38)).Count == 0,
            "peer lines: the same post again gives nothing — the same list, an equal one, or the same lines 31 s later, once the context was dropped");

        // The same words at another time are another line; at the same time, the same line.
        var again = lines.Take(PlayMap, Lines((At + 4000, "BLACKOUT ON"), (At + 9000, "refused: PANIC"), (At + 9000, "BLACKOUT OFF"), (At + 12000, "BLACKOUT ON")), Via, T0.AddSeconds(40));
        Check(Said(again) == "BLACKOUT OFF@9000 | BLACKOUT ON@12000",
            $"peer lines: a text already taken is taken again at a later time, and a new text at the latest time taken is new ({Said(again)})");
        // A line told after the shared ones yet timed before the latest taken may have been heard: never taken, and one row says so —
        // once, however many such lines follow, until the node's clock passes the latest time taken.
        var late = lines.Take(PlayMap, Lines((At + 9000, "refused: PANIC"), (At + 9000, "BLACKOUT OFF"), (At + 12000, "BLACKOUT ON"), (At + 3000, "an older line, late")), Via, T0.AddSeconds(41));
        var later = lines.Take(PlayMap, Lines((At + 9000, "BLACKOUT OFF"), (At + 12000, "BLACKOUT ON"), (At + 3000, "an older line, late"), (At + 3500, "another, late")), Via, T0.AddSeconds(42));
        Check(Said(late) == "note: lines from Stage left timed before ones already heard are left out until its clock passes them: its clock may have been set back"
              && later.Count == 0,
            $"peer lines: a new line timed below the latest taken is never taken, and one row says lines are left out — once ({Said(late)} / {Said(later)})");
        // A list that grew back at its front (older lines listed again ahead of the shared ones) says nothing: they were taken, or were the backlog.
        var grown = new PeerLines();
        grown.Take(PlayMap, Lines((At + 1000, "a"), (At + 2000, "b"), (At + 3000, "c")), Via, T0);
        var shrunk = grown.Take(PlayMap, Lines((At + 3000, "c"), (At + 4000, "d")), Via, T0.AddSeconds(1));
        var regrown = grown.Take(PlayMap, Lines((At + 1000, "a"), (At + 2000, "b"), (At + 3000, "c"), (At + 4000, "d")), Via, T0.AddSeconds(2));
        // And a list emptied and then listed again shares the line taken at the latest time: nothing is missing, nothing is new.
        var emptied = grown.Take(PlayMap, Lines(), Via, T0.AddSeconds(3));
        var relisted = grown.Take(PlayMap, Lines((At + 3000, "c"), (At + 4000, "d")), Via, T0.AddSeconds(4));
        Check(Said(shrunk) == "d@4000" && regrown.Count == 0 && emptied.Count == 0 && relisted.Count == 0,
            $"peer lines: older lines listed again ahead of the shared ones, or a list emptied and listed again, give no row ({Said(regrown)} / {Said(relisted)})");

        // Lines that tell no time: the part of the new list after its longest overlap with the last.
        var plain = new PeerLines();
        var agm = new NodeRef("messenger", "a1b2c3d4", "AGM");
        plain.Take(agm, Lines((0, "hall opened"), (0, "screen joined"), (0, "screen joined")), "node key 'AGM'", T0);
        var next = plain.Take(agm, Lines((0, "screen joined"), (0, "screen joined"), (0, "help asked"), (0, "screen joined")), "node key 'AGM'", T0.AddSeconds(2));
        Check(Said(next) == "help asked | screen joined" && next.All(r => r.AtMs is null)
              && plain.Take(agm, Lines((0, "screen joined"), (0, "screen joined"), (0, "help asked"), (0, "screen joined")), "node key 'AGM'", T0.AddSeconds(3)).Count == 0,
            $"peer lines: lines without a time are new after the longest run that ends the last list and begins this one — repeats of one text counted ({Said(next)})");

        // A post that shares nothing with the last: one row that lines may be missing, then its lines.
        var apart = plain.Take(agm, Lines((0, "hall closed"), (0, "screen left")), "node key 'AGM'", T0.AddSeconds(63));
        Check(Said(apart) == "note: lines from AGM may be missing between 03:20:03 and 03:21:03 | hall closed | screen left",
            $"peer lines: a list that shares no line with the last gives one row saying lines may be missing, between the two posts' times here ({Said(apart)})");
        var timedApart = lines.Take(PlayMap, Lines((At + 60000, "PLAYLIST GO show — Outro"), (At + 61000, "UNDO")), Via, T0.AddSeconds(90));
        Check(timedApart.Count == 3 && timedApart[0] is { Note: true, Text: "lines from Stage left may be missing between 03:20:42 and 03:21:30" } && Said(timedApart.Skip(1)) == "PLAYLIST GO show — Outro@60000 | UNDO@61000",
            $"peer lines: timed lines all past the latest taken, none shared, say the same ({Said(timedApart)})");
        // A clock set back. The node's lines are all new and all timed before the latest taken: one row says its clock reads earlier,
        // its lines are taken, and the same list again gives nothing; lines after them are taken as they come.
        var clock = new PeerLines();
        clock.Take(PlayMap, Lines((At + 8000, "PLAYLIST CUE show #1")), Via, T0);
        clock.Take(PlayMap, Lines((At + 8000, "PLAYLIST CUE show #1"), (At + 9000, "PLAYLIST GO show — Intro")), Via, T0.AddSeconds(1));
        var setBack = Lines((At + 1000, "started"), (At + 2000, "PLAYLIST CUE show #2"), (At + 3000, "PLAYLIST GO show — Outro"));
        var stepped = clock.Take(PlayMap, setBack, Via, T0.AddSeconds(30));
        var steppedAgain = clock.Take(PlayMap, Lines(setBack.Select(l => (l.AtMs, l.Text)).ToArray()), Via, T0.AddSeconds(31));
        var onward = clock.Take(PlayMap, Lines((At + 2000, "PLAYLIST CUE show #2"), (At + 3000, "PLAYLIST GO show — Outro"), (At + 3000, "BLACKOUT ON"), (At + 5000, "UNDO")), Via, T0.AddSeconds(32));
        Check(Said(stepped) == "note: Stage left's clock reads earlier than lines already heard; lines may be missing | started@1000 | PLAYLIST CUE show #2@2000 | PLAYLIST GO show — Outro@3000"
              && stepped[0] is { AtMs: null, Via: Via } && steppedAgain.Count == 0 && Said(onward) == "BLACKOUT ON@3000 | UNDO@5000",
            $"peer lines: a node whose lines are all new and all timed before the latest taken is on an earlier clock — one row says so, its lines are taken once, and the next are taken from there ({Said(stepped)} / {Said(steppedAgain)} / {Said(onward)})");
        // A clock set back while the node goes on listing lines already heard: the new ones are left out and one row says so, once;
        // when its clock passes the latest time taken its lines are taken again, and a later step back is said again.
        var held = new PeerLines();
        held.Take(PlayMap, Lines((At + 8000, "a"), (At + 9000, "b")), Via, T0);
        var first1 = held.Take(PlayMap, Lines((At + 8000, "a"), (At + 9000, "b"), (At + 1000, "c, after the clock went back")), Via, T0.AddSeconds(1));
        var second1 = held.Take(PlayMap, Lines((At + 9000, "b"), (At + 1000, "c, after the clock went back"), (At + 2000, "d")), Via, T0.AddSeconds(2));
        var caughtUp = held.Take(PlayMap, Lines((At + 1000, "c, after the clock went back"), (At + 2000, "d"), (At + 9500, "e")), Via, T0.AddSeconds(3));
        var again1 = held.Take(PlayMap, Lines((At + 2000, "d"), (At + 9500, "e"), (At + 4000, "f, after another step back")), Via, T0.AddSeconds(4));
        Check(first1.Count == 1 && first1[0] is { Note: true, AtMs: null } && first1[0].Text == PeerLines.LeftOut("Stage left") && second1.Count == 0
              && Said(caughtUp) == "e@9500" && again1.Count == 1 && again1[0].Text == PeerLines.LeftOut("Stage left"),
            $"peer lines: new lines timed before ones heard, among lines already heard, are left out with one row; past the latest time taken lines are taken again, and the next step back is said again ({Said(first1)} / {Said(second1)} / {Said(caughtUp)} / {Said(again1)})");

        // A node with no lines yet, and then its first: nothing was there to miss.
        var quiet = new PeerLines(utc => "show " + utc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
        var pat = new NodeRef("patterns", "pat1", "");
        var joined = quiet.Take(pat, Lines(), "the link", T0);
        var firstLine = quiet.Take(pat, Lines((At, "take wall — done")), "the link", T0.AddSeconds(1));
        var noneThen = quiet.Take(pat, Lines(), "the link", T0.AddSeconds(2));
        var missing = quiet.Take(pat, Lines((At + 5000, "cut wall — done")), "the link", T0.AddMinutes(2));
        Check(joined.Single().Text == "patterns pat1 joins the report; its earlier lines are in its own journal" && Said(firstLine) == "take wall — done@0" && noneThen.Count == 0
              && missing[0].Text == "lines from patterns pat1 may be missing between show 03:20 and show 03:22" && missing.Count == 2,
            $"peer lines: a node that had no lines gives its first with no warning; one with no name is named by its kind and id; times are worded by the host's clock ({Said(joined.Concat(firstLine).Concat(missing))})");

        // 32 nodes: the 33rd evicts the one heard longest ago, which joins again when it returns — never a backlog.
        var many = new PeerLines();
        for (var i = 0; i < PeerLines.MaxNodes; i++)
            many.Take(new NodeRef("playmap", "node" + i, "Node " + i), Lines((At + i, "line of " + i)), "", T0.AddSeconds(i));
        many.Take(new NodeRef("playmap", "node0", "Node 0"), Lines((At, "line of 0")), "", T0.AddSeconds(40));   // node0 heard again: node1 is now the oldest
        var newcomer = many.Take(new NodeRef("playmap", "node32", "Node 32"), Lines((At, "x")), "", T0.AddSeconds(41));
        var kept = many.Take(new NodeRef("playmap", "node0", "Node 0"), Lines((At, "line of 0"), (At + 50, "a new line of 0")), "", T0.AddSeconds(42));
        var back = many.Take(new NodeRef("playmap", "node1", "Node 1"), Lines((At + 1, "line of 1"), (At + 70, "a line while it was forgotten")), "", T0.AddSeconds(43));
        Check(PeerLines.MaxNodes == 32 && many.Count == 32 && newcomer.Single().Note && Said(kept) == "a new line of 0@50"
              && back.Count == 1 && back[0] is { Note: true, Text: "Node 1 joins the report; its earlier lines are in its own journal" },
            $"peer lines: 32 nodes are remembered; a 33rd evicts the one heard longest ago, and that node's return is a joining row, not a backlog ({Said(back)})");
    }
}
