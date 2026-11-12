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
/// <see cref="MaxAge"/> (or tells no time this reader can read), the answer is the stuck one: not ready, one red row. Pure.
/// </summary>
public static class PreflightWire
{
    public const int Version = 1;

    /// <summary>The most rows a preflight lists, reds first.</summary>
    public const int MaxRows = 200;

    /// <summary>How old a built verdict may be and still be served as the verdict.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(3);

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
    /// What a door serves: null when the host keeps no preflight; the stuck answer while <paramref name="showStuck"/>, or when
    /// <paramref name="built"/> is older than <see cref="MaxAge"/> at <paramref name="nowMs"/> or tells no time; else
    /// <paramref name="built"/> as it is.
    /// </summary>
    public static string? Served(string? built, bool showStuck, long nowMs)
    {
        if (built is null) return null;
        if (showStuck || At(built) is not { } at || nowMs - at > (long)MaxAge.TotalMilliseconds) return Stuck(nowMs);
        return built;
    }

    /// <summary>The verdict a frame carries in place of one gone stale: not ready, one red row, stuck.</summary>
    public static NetworkVerdict StuckVerdict { get; } = new(Ready: false, Red: 1, Amber: 0, Stuck: true);

    /// <summary>
    /// What a frame carries, by the doors' own rule: null from a host that keeps no preflight; <paramref name="verdict"/> while it
    /// was judged within <see cref="MaxAge"/> of <paramref name="nowMs"/>; else <see cref="StuckVerdict"/> — a frame still being
    /// built while the judging has stopped never repeats the last verdict as if it stood.
    /// </summary>
    public static NetworkVerdict? Carried(NetworkVerdict? verdict, long atMs, long nowMs)
        => verdict is null ? null : nowMs - atMs > (long)MaxAge.TotalMilliseconds ? StuckVerdict : verdict;

    /// <summary><see cref="Served"/> at this moment on the machine's clock.</summary>
    public static string? ServedNow(string? built, bool showStuck) => Served(built, showStuck, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
