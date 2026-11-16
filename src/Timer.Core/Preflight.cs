using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Stagewatch.Core;

/// <summary>
/// One line of the room's preflight: one check of one node, link or window, with its light (red, amber or green — what is unknown
/// is amber, never guessed green), what it found in words and, where a person can do something about it, what. <see cref="Node"/>
/// is the node's Show Bus id ("" for a row about this Stagewatch's own link or window), <see cref="Configured"/> the relationship
/// that makes the node expected ("playout link stage-left"; "" for one only seen). Names and words only: no row holds a secret.
/// </summary>
public sealed record PreflightRow(string Id, string Node, string Name, string Kind, string Check, string Light, string Configured, string Words, string Fix);

/// <summary>
/// The room's verdict as the operator frame carries it: ready while no row is red, how many rows are red and amber, and whether it
/// is the stuck answer. Amber is counted and never blocks.
/// </summary>
public sealed record NetworkVerdict(bool Ready, int Red, int Amber, bool Stuck);

/// <summary>
/// The preflight on the wire (<c>GET /api/preflight</c>, MCP <c>get_preflight</c>): its JSON, and the rule every door serves it by —
/// a verdict is never a confident stale one. While the show thread is stuck, or the verdict a host last built is older than
/// <see cref="MaxAge"/> (or tells no time this reader can read), the answer is the stuck one: not ready, one red row. A verdict's
/// age is read on the host's show-thread clock (its hub's, <c>SnapshotHub.NowMs</c>), the clock the hub's silence and the context's
/// build are read on, never on the wall clock: a step of the wall clock moves no stuck rule. Its <c>at</c> is the wall time a reader
/// sees, and only that. Pure.
/// </summary>
public static class PreflightWire
{
    public const int Version = 1;

    /// <summary>The most rows a preflight lists, reds first.</summary>
    public const int MaxRows = 200;

    /// <summary>
    /// How often a host whose show thread turns over judges the room and builds its context: at a turn that finds the last build this
    /// old on its show thread's clock, its hub's (the desk's and the rig's once a second; a context pair's build costs about 1 ms of the 250 ms turn).
    /// </summary>
    public static readonly TimeSpan BuildEvery = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How old a built verdict (or context) may be and still be served as the verdict: the hub's silence bound
    /// (<see cref="SnapshotHub.StuckAfter"/>) plus <see cref="BuildEvery"/>. A live show thread turns over within the hub's bound and builds
    /// at a turn that finds its last build <see cref="BuildEvery"/> old, so its build is never older than this; an older one is a build
    /// missed while the thread turned over, which a slow but live thread never makes.
    /// </summary>
    public static readonly TimeSpan MaxAge = SnapshotHub.StuckAfter + BuildEvery;

    public const string StuckId = "stagewatch:show-thread";
    public const string StuckCheck = "stagewatch";
    public const string StuckWords = "Stagewatch's show thread is stuck — no verdict until it turns over";

    /// <summary>What a host with no preflight is answered with (404).</summary>
    public const string None = "no preflight here";

    /// <summary>What a request with a query is refused with (400): the route takes none, so nothing rides in its address.</summary>
    public const string NoQuery = "/api/preflight takes no query";

    private const string Head = "{\"v\":1,\"at\":";

    /// <summary>The stuck answer's one row.</summary>
    public static PreflightRow StuckRow { get; } = new(StuckId, "", "Stagewatch", "main", StuckCheck, Light.Red, "", StuckWords, "");

    /// <summary>The preflight's JSON: <c>v</c> and <c>at</c> first, in that order, so <see cref="At"/> reads the time without parsing the rows.</summary>
    public static string Write(long atMs, bool ready, bool stuck, int red, int amber, int green, string words, IEnumerable<PreflightRow> rows)
    {
        var buffer = new ArrayBufferWriter<byte>(2048);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("v", Version);
            w.WriteNumber("at", atMs);
            w.WriteBoolean("ready", ready);
            w.WriteBoolean("stuck", stuck);
            w.WriteNumber("red", red);
            w.WriteNumber("amber", amber);
            w.WriteNumber("green", green);
            w.WriteString("words", words);
            w.WriteStartArray("rows");
            foreach (var r in rows.Take(MaxRows))
            {
                w.WriteStartObject();
                w.WriteString("id", r.Id);
                w.WriteString("node", r.Node);
                w.WriteString("name", r.Name);
                w.WriteString("kind", r.Kind);
                w.WriteString("check", r.Check);
                w.WriteString("light", r.Light);
                w.WriteString("configured", r.Configured);
                w.WriteString("words", r.Words);
                w.WriteString("fix", r.Fix);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>The stuck answer at <paramref name="atMs"/>: not ready, stuck, one red row that says why.</summary>
    public static string Stuck(long atMs) => Write(atMs, ready: false, stuck: true, red: 1, amber: 0, green: 0, "not ready: " + StuckWords, [StuckRow]);

    /// <summary>The Unix milliseconds a preflight written by <see cref="Write"/> was built at, or null when the text does not start as one does.</summary>
    public static long? At(string json)
    {
        if (!json.StartsWith(Head, StringComparison.Ordinal)) return null;
        var end = Head.Length;
        while (end < json.Length && char.IsAsciiDigit(json[end])) end++;
        return end > Head.Length && end < json.Length && json[end] == ','
               && long.TryParse(json.AsSpan(Head.Length, end - Head.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var at) ? at : null;
    }

    /// <summary>
    /// What a door serves: null when the host keeps no preflight; the stuck answer, dated <paramref name="wallMs"/>, while
    /// <paramref name="showStuck"/> (the host's one served rule), or when <paramref name="built"/> is <paramref name="ageMs"/> old on the
    /// host's show-thread clock and that is older than <see cref="MaxAge"/>, or it tells no time; else <paramref name="built"/> as it is.
    /// </summary>
    public static string? Served(string? built, bool showStuck, long ageMs, long wallMs)
    {
        if (built is null) return null;
        if (showStuck || ageMs > (long)MaxAge.TotalMilliseconds || At(built) is null) return Stuck(wallMs);
        return built;
    }

    /// <summary>The verdict a frame carries in place of one gone stale: not ready, one red row, stuck.</summary>
    public static NetworkVerdict StuckVerdict { get; } = new(Ready: false, Red: 1, Amber: 0, Stuck: true);

    /// <summary>
    /// What a frame carries, by the doors' own rule: null from a host that keeps no preflight; <see cref="StuckVerdict"/> when
    /// <paramref name="verdict"/> was judged before a stall the frame has not yet published past (<paramref name="beforeStall"/>: the
    /// hub's <c>MadeBeforeStall</c> at the judging's time), or more than <see cref="MaxAge"/> before <paramref name="nowMs"/> on the same
    /// show-thread clock as <paramref name="atMs"/>; else <paramref name="verdict"/> — a frame never repeats a verdict from before a stall,
    /// or from a judging that has stopped, as if it stood.
    /// </summary>
    public static NetworkVerdict? Carried(NetworkVerdict? verdict, long atMs, long nowMs, bool beforeStall)
        => verdict is null ? null : beforeStall || nowMs - atMs > (long)MaxAge.TotalMilliseconds ? StuckVerdict : verdict;
}
