using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stagewatch.Core;
using Stagewatch.Integrations;

/// <summary>
/// The Show Bus master (docs/SHOWBUS.md) read as data. Its body is held by a pinned hash; §13 PeerLink keeps its numbered
/// parts; the ```peerlink block sits inside the hashed body and every row parses. Each row's case is one 13.20 names; a
/// machine row's config, events and expected fields are the words 13.20's tables define, and the values it expects for a
/// state, light, show, refusal, tls, flag or outcome are in the closed sets §13's text names. The crypto rows hold with the
/// runtime's SHA-256 and HMAC alone — the labels, the transcript's lines, the code canon and the legacy key taken from the
/// text itself — so text and vectors agree before any tree copies them. Parse, pure and machine rows are checked for shape
/// only. Of Stagewatch's own code: a made key has 13.5.1's shape and its stored hash is K, and its level-0 proof makes every
/// legacy row's proof and refuses every nonce a row refuses (the tokens a row does not prove are not held here).
/// </summary>
static class ShowBusSpecTests
{
    /// <summary>
    /// SHA-256 of the master's body: from the line <see cref="BodyStart"/> to the end, UTF-8 without a BOM, every CRLF read as
    /// LF, one final LF. Any edit to the body changes it, so an edit is pinned here on purpose; the copies in Patterns,
    /// PlayMap and AGMessage hold the same hash, printed in the header, once 1.3 is final.
    /// </summary>
    const string BodySha256 = "e1430143ac1499800443b7cba1fd64f4bbf074d9c113ca3193c16055b3357f6b";

    const string BodyStart = "## 1. Rules every node follows";
    const string PeerLinkHeading = "## 13. PeerLink — one authenticated link between two nodes (1.3)";

    /// <summary>§13's parts in order. Readers in four trees cite them by number (13.12.3, 13.16.5, 13.18), so none moves.</summary>
    static readonly string[] PeerLinkParts =
    [
        "Words", "What the venue network can and cannot do", "Carriers", "Identity and the hello", "Keys", "The handshake: LINK",
        "Side calls", "TLS and pinning", "Versions, features and floors", "Liveness — the link and the show",
        "Clock offset and its quality", "Commands — request ids, receipts and outcomes", "Order and latest wins",
        "Bounds and back-pressure", "Reconnect and holds", "States, and what each side shows", "Safety on loss",
        "Doors that are not PeerLinks", "Secrets and logging", "Conformance",
    ];

    /// <summary>
    /// Every number §13 names in <c>code</c>, each a §14 row with its value: a name added to §13 is a row added to §14.
    /// </summary>
    static readonly string[] PeerLinkNumbers =
    [
        "clock.apart.ms", "clock.fresh.ms", "clock.rtt.max.ms", "clock.window", "cmd.expire.max.ms", "link.beat.ms",
        "link.check.max.ms", "link.command.bytes", "link.drop.ms", "link.inflight.max", "link.read.min.bytes",
        "link.redial.first.ms", "link.redial.max.ms", "link.replies.max", "link.selfgap.ms", "link.sidecall.inflight",
        "link.sidecall.timeout.ms", "link.stale.ms", "link.write.deadline.max.ms", "link.ws.in.bytes", "peerlink.cert.probe.ms",
        "peerlink.handshake.ms", "peerlink.hold.disconnect.ms", "peerlink.hold.refused.ms", "peerlink.key.bits.min",
        "peerlink.kid.bytes", "peerlink.lid.bytes", "peerlink.peer.sha256", "peerlink.pings.matched", "peerlink.stable.ms",
        "peerlink.unpaired.per.address", "peerlink.wrong.proofs.per.minute", "ping.max.ms", "ping.min.ms",
        "receipt.deadline.ms", "receipt.final.ms", "receipt.late.ms", "receipts.ask.max", "receipts.kept", "receipts.kept.ms",
        "report.retry.first.ms", "report.retry.for.ms", "report.retry.max.ms", "request.id.chars", "show.stuck.max.ms",
        "show.stuck.min.ms",
    ];

    /// <summary>Dotted names in §13 that are names, not numbers: PlayMap's WebSocket subprotocol.</summary>
    static readonly string[] NotNumbers = ["playmap.lines.v1"];

    /// <summary>The bytes the vectors are cut to (§14's <c>peerlink.kid.bytes</c> and <c>peerlink.lid.bytes</c>).</summary>
    const int KidBytes = 8, LidBytes = 8;

    public static void Run(Action<bool, string> Check)
    {
        var path = Master();
        Check(path is not null, "showbus spec: docs/SHOWBUS.md found above the suite");
        if (path is null) return;
        if (Body(Check, File.ReadAllBytes(path)) is not { } read) return;
        var (text, body) = read;

        Sections(Check, body);
        var s13 = Between(body, "\n" + PeerLinkHeading + "\n", "\n## ");
        Check(s13 is not null, "showbus spec: §13 PeerLink is in the hashed body");
        if (s13 is null) return;
        Parts(Check, s13);
        Numbers(Check, s13);
        Doors(Check, s13);
        if (Block(Check, text, body, s13) is not { } rows) return;
        Rows(Check, s13, rows);
        Crypto(Check, s13, rows);
        TodaysCode(Check, s13, rows);
    }

    // ------------------------------------------------------------------ the body and its hash

    static string? Master()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "docs", "SHOWBUS.md")) && File.Exists(Path.Combine(dir.FullName, "Timer.slnf")))
                return Path.Combine(dir.FullName, "docs", "SHOWBUS.md");
        return null;
    }

    /// <summary>The whole text and the body, both with LF line ends; null (and a failure) when the file breaks the body rule.</summary>
    static (string Text, string Body)? Body(Action<bool, string> Check, byte[] bytes)
    {
        Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "showbus spec: the master has no BOM");
        string raw;
        try { raw = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException e)
        {
            Check(false, "showbus spec: the master is UTF-8 (" + e.Message + ")");
            return null;
        }
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal);
        Check(!text.Contains('\r'), "showbus spec: no carriage return but in CRLF line ends");
        var starts = Regex.Matches(text, "^" + Regex.Escape(BodyStart) + "$", RegexOptions.Multiline);
        Check(starts.Count == 1, $"showbus spec: the body starts once, at '{BodyStart}' ({starts.Count})");
        if (starts.Count != 1) return null;
        var body = text[starts[0].Index..];
        Check(body.EndsWith('\n') && !body.EndsWith("\n\n", StringComparison.Ordinal), "showbus spec: the body ends in exactly one LF");
        var hash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        Check(hash == BodySha256,
            $"showbus spec: the body hashes to the pinned {BodySha256} — it now hashes to {hash}; pin it in ShowBusSpecTests.BodySha256 when the edit is meant (Patterns, PlayMap and AGMessage follow the final hash)");
        return (text, body);
    }

    /// <summary>The body's sections, outside code fences: numbered from 1, in increasing order, §13 PeerLink among them.</summary>
    static void Sections(Action<bool, string> Check, string body)
    {
        var numbers = new List<int>();
        var fenced = false;
        foreach (var line in body.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
            if (fenced) continue;
            var m = Regex.Match(line, @"^## (\d+)\. ");
            if (m.Success) numbers.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        Check(!fenced, "showbus spec: every code fence in the body is closed");
        Check(numbers.Count > 0 && numbers[0] == 1 && numbers.Zip(numbers.Skip(1)).All(p => p.Second > p.First),
            "showbus spec: sections are numbered from 1 in increasing order (" + string.Join(", ", numbers) + ")");
        Check(Enumerable.Range(1, 11).All(numbers.Contains) && numbers.Contains(13), "showbus spec: §1–§11 and §13 are in the body");
        Check(Regex.Matches(body, "^" + Regex.Escape(PeerLinkHeading) + "$", RegexOptions.Multiline).Count == 1, "showbus spec: §13 is PeerLink, once");
    }

    // ------------------------------------------------------------------ §13's text

    static void Parts(Action<bool, string> Check, string s13)
    {
        var parts = Regex.Matches(s13, @"^### 13\.(\d+) (.+)$", RegexOptions.Multiline)
            .Select(m => (N: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Title: m.Groups[2].Value)).ToList();
        Check(parts.Select(p => p.N).SequenceEqual(Enumerable.Range(1, PeerLinkParts.Length)),
            "showbus spec: §13's parts are 13.1–13.20 in order (" + string.Join(", ", parts.Select(p => p.N)) + ")");
        Check(parts.Select(p => p.Title).SequenceEqual(PeerLinkParts),
            "showbus spec: §13's parts keep their titles (" + string.Join(" · ", parts.Select(p => p.Title).Except(PeerLinkParts)) + ")");
        Check(Regex.Matches(s13, "^#### ", RegexOptions.Multiline).Count == 0, "showbus spec: §13 has no level below its parts");
    }

    /// <summary>Every dotted name §13 writes in code is a number §14 gives, or one of the few names that are not numbers.</summary>
    static void Numbers(Action<bool, string> Check, string s13)
    {
        var named = Regex.Matches(s13, @"`([a-z][a-z0-9]*(?:\.[a-z0-9]+)+)`").Select(m => m.Groups[1].Value)
            .Distinct().Except(NotNumbers).Order(StringComparer.Ordinal).ToList();
        var pinned = PeerLinkNumbers.Order(StringComparer.Ordinal).ToList();
        Check(named.SequenceEqual(pinned),
            "showbus spec: §13 names exactly the numbers §14 gives (new: " + string.Join(", ", named.Except(pinned)) + "; gone: " + string.Join(", ", pinned.Except(named)) + ")");
    }

    /// <summary>13.18 keeps the two doors §8 names as exemptions in 1.3: browser pages with ?pin=, and Companion TCP 9923.</summary>
    static void Doors(Action<bool, string> Check, string s13)
    {
        var doors = Part(s13, 18) ?? "";
        Check(doors.Contains("Stagewatch's Companion TCP (9923)", StringComparison.Ordinal) && doors.Contains("`?pin=`", StringComparison.Ordinal),
            "showbus spec: 13.18 names Companion TCP 9923 and ?pin= as doors that are not PeerLinks");
        var secrets = Part(s13, 19) ?? "";
        Check(secrets.Contains("A view pass (§12.2)", StringComparison.Ordinal) && secrets.Contains("§1 rule 5", StringComparison.Ordinal),
            "showbus spec: 13.19 sends a view pass only where §1 rule 5 lets it ride");
    }

    // ------------------------------------------------------------------ the block

    /// <summary>The one ```peerlink block, inside §13.20 and so inside the hashed body; its rows, each a JSON object.</summary>
    static List<JsonElement>? Block(Action<bool, string> Check, string text, string body, string s13)
    {
        Check(Regex.Matches(text, "^```peerlink$", RegexOptions.Multiline).Count == 1, "showbus spec: one ```peerlink block in the master");
        var conformance = Part(s13, 20);
        var inner = conformance is null ? null : Between(conformance, "\n```peerlink\n", "\n```");
        Check(inner is not null && body.Contains(inner, StringComparison.Ordinal), "showbus spec: the ```peerlink block is in 13.20, inside the hashed body");
        if (inner is null) return null;
        var lines = inner["\n```peerlink\n".Length..].Split('\n');
        Check(lines.All(l => l.All(c => c < 128)), "showbus spec: the ```peerlink block is ASCII (" + string.Join(", ", lines.Where(l => l.Any(c => c >= 128)).Select(l => l[..Math.Min(40, l.Length)])) + ")");
        var rows = new List<JsonElement>();
        foreach (var line in lines)
        {
            try
            {
                var row = JsonDocument.Parse(line).RootElement.Clone();
                Check(row.ValueKind == JsonValueKind.Object && Str(row, "case") is not null, "showbus spec: a peerlink row is an object with a case: " + line);
                if (row.ValueKind == JsonValueKind.Object) rows.Add(row);
            }
            catch (JsonException e)
            {
                Check(false, $"showbus spec: a peerlink row parses ({e.Message}): {line}");
            }
        }
        Check(rows.Count == lines.Length && rows.Count > 0, $"showbus spec: every line of the block is a row ({rows.Count} of {lines.Length})");
        return rows;
    }

    /// <summary>Each row's case is one 13.20 names, every case has rows, and every word a row expects is in §13's closed sets.</summary>
    static void Rows(Action<bool, string> Check, string s13, List<JsonElement> rows)
    {
        var conformance = Part(s13, 20) ?? "";
        var kinds = new HashSet<string>(StringComparer.Ordinal) { "parse", "machine" };
        foreach (var label in new[] { "crypto rows:", "pure rows:" })
            if (Line(conformance, label) is { } l) kinds.UnionWith(Ticked(l));
        Check(kinds.SetEquals(["canon", "key", "proof", "refuse", "legacy", "parse", "liveness", "redial", "errms", "clock", "outcome", "machine"]),
            "showbus spec: 13.20 names the twelve kinds of row (" + string.Join(", ", kinds) + ")");
        var cases = rows.Select(r => Str(r, "case") ?? "").ToList();
        Check(cases.All(kinds.Contains), "showbus spec: every row's case is one 13.20 names (" + string.Join(", ", cases.Where(c => !kinds.Contains(c)).Distinct()) + ")");
        Check(kinds.All(cases.Contains), "showbus spec: every kind 13.20 names has rows (" + string.Join(", ", kinds.Where(k => !cases.Contains(k))) + ")");

        var commands = Part(s13, 12) ?? "";
        var states = Part(s13, 16) ?? "";
        var outcomes = Bullets(Between(commands, "2. **Outcomes.**", "3. **") ?? "");
        var linkStates = Regex.Matches(states, @"^\| `([a-z-]+)` \|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var flags = Bullets(Between(states, "2. **Flags.**", "3. **") ?? "");
        var record = Between(states, "\n```\n{\"peer\"", "\n```") ?? "";
        HashSet<string> Alternatives(string field)
            => Regex.Match(record, "\"" + field + "\":\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value.Split('|').ToHashSet(StringComparer.Ordinal) : [];
        var lights = Alternatives("light");
        var shows = Alternatives("show");
        var refusals = Alternatives("refusal");
        var tls = Alternatives("tls");
        var classes = Ticked(Between(Part(s13, 6) ?? "", "`ERR LINK <class> — <words>`, with class", ".\n") ?? "").ToHashSet(StringComparer.Ordinal);
        var words = new MachineWords(linkStates, flags, outcomes, lights, shows, refusals, tls, classes,
            Table(conformance, "config"), Table(conformance, "event"), Table(conformance, "field"));
        Check(outcomes.SetEquals(["not-sent", "refused", "accepted", "applied", "unconfirmed"]), "showbus spec: 13.12.2 names five outcomes (" + string.Join(", ", outcomes) + ")");
        Check(linkStates.SetEquals(["off", "finding", "connecting", "live", "stale", "down", "held", "refused"]), "showbus spec: 13.16.1's closed set of link states (" + string.Join(", ", linkStates) + ")");
        Check(flags.Count >= 10 && flags.Contains("plain") && flags.Contains("code-plain") && flags.Contains("malformed"), "showbus spec: 13.16.2's flags (" + string.Join(", ", flags) + ")");
        Check(lights.SetEquals(["green", "amber", "red", "grey"]) && shows.SetEquals(["live", "stuck", "unknown"]) && refusals.Contains("floor")
              && tls.SetEquals(["pinned", "first", "none", "local"]),
            "showbus spec: the link record names its lights, shows, refusals and tls");
        Check(classes.SetEquals(["malformed", "version", "refused", "locked", "binding", "scope", "busy", "open"]),
            "showbus spec: 13.6.4 names a LINK verdict's classes (" + string.Join(", ", classes) + ")");
        Check(refusals.Contains("key") && classes.Except(["refused", "open"]).All(refusals.Contains),
            "showbus spec: every refusing verdict class is a refusal the link record carries, refused as key (" + string.Join(", ", classes.Except(["refused", "open"]).Where(c => !refusals.Contains(c))) + ")");
        Check(words.Config.SetEquals(["carrier", "can", "key", "pin", "floor"]) && words.Events.Count >= 20 && words.Fields.Count >= 15
              && words.Events.Contains("evaluate") && words.Fields.Contains("sent") && words.Fields.Contains("credentials"),
            $"showbus spec: 13.20 defines a machine row's config, events and fields ({words.Config.Count}, {words.Events.Count}, {words.Fields.Count})");

        foreach (var r in rows)
        {
            var c = Str(r, "case");
            var name = c + " " + (Str(r, "name") ?? Short(r));
            switch (c)
            {
                case "outcome":
                    Check(Str(r, "outcome") is { } o && outcomes.Contains(o) && r.TryGetProperty("answer", out var a) && a.ValueKind == JsonValueKind.Object,
                        "showbus spec: an outcome row reads an answer to one of the five outcomes: " + name);
                    break;
                case "liveness":
                    Check(Int(r, "silenceMs") is >= 0 && Str(r, "state") is "live" or "stale" or "down", "showbus spec: a liveness row is a silence and live, stale or down: " + name);
                    break;
                case "redial":
                    Check(Int(r, "n") is >= 0 && Num(r, "j") is >= 0.0 and < 1.0 && Int(r, "ms") is > 0 && Num(r, "tol") is >= 0.0, "showbus spec: a redial row is n, j in [0, 1), ms and tol: " + name);
                    break;
                case "errms":
                    Check(Int(r, "rttMs") is >= 0 && Int(r, "errMs") is > 0, "showbus spec: an errms row is rttMs and errMs: " + name);
                    break;
                case "clock":
                    var samples = r.TryGetProperty("samples", out var s) && s.ValueKind == JsonValueKind.Array
                        && s.EnumerateArray().All(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 4 && x.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number));
                    var best = r.TryGetProperty("best", out var b) && b.ValueKind == JsonValueKind.Object;
                    Check(samples ^ best, "showbus spec: a clock row holds samples of four stamps or one best sample: " + name);
                    break;
                case "parse":
                    Check(Str(r, "carrier") is "line" or "ws" or "http" && r.TryGetProperty("expect", out _), "showbus spec: a parse row names its carrier and what it expects: " + name);
                    break;
                case "machine":
                    Machine(Check, r, name, words);
                    break;
            }
        }
        var machines = rows.Where(r => Str(r, "case") == "machine").Select(r => Str(r, "name")).ToList();
        Check(machines.Distinct().Count() == machines.Count, "showbus spec: machine rows have names of their own");
    }

    /// <summary>The closed sets a machine row's words come from: §13's states, flags, outcomes and record values, and 13.20's tables.</summary>
    sealed record MachineWords(HashSet<string> States, HashSet<string> Flags, HashSet<string> Outcomes, HashSet<string> Lights,
        HashSet<string> Shows, HashSet<string> Refusals, HashSet<string> Tls, HashSet<string> Classes,
        HashSet<string> Config, HashSet<string> Events, HashSet<string> Fields);

    /// <summary>
    /// A machine row: a carrier in its config and nothing 13.20 does not define; events and expectations in time order, each event
    /// one 13.20 names and each expected field one it defines; every value a closed set names is in it.
    /// </summary>
    static void Machine(Action<bool, string> Check, JsonElement r, string name, MachineWords w)
    {
        var events = Items(r, "events");
        var expect = Items(r, "expect");
        Check(Str(r, "name") is { Length: > 0 } && events.Count > 0 && expect.Count > 0, "showbus spec: a machine row has a name, events and expectations: " + name);
        Check(r.EnumerateObject().All(p => p.Name is "case" or "name" or "role" or "config" or "events" or "expect"), "showbus spec: a machine row has only case, name, role, config, events and expect: " + name);
        var config = r.TryGetProperty("config", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        Check(config.ValueKind == JsonValueKind.Object && Str(config, "carrier") is "line" or "ws", "showbus spec: a machine row's config names its carrier, line or ws: " + name);
        Check(config.ValueKind != JsonValueKind.Object || config.EnumerateObject().All(p => w.Config.Contains(p.Name)),
            "showbus spec: a machine row's config sets only what 13.20 defines: " + name);
        Check(events.All(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() is 2 or 3 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.String
                              && (e.GetArrayLength() == 2 || e[2].ValueKind == JsonValueKind.Object)),
            "showbus spec: a machine event is [ms, event, args?]: " + name);
        var unknown = events.Where(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 1 && e[1].ValueKind == JsonValueKind.String)
            .Select(e => e[1].GetString()!).Where(n => !w.Events.Contains(n)).ToList();
        Check(unknown.Count == 0, "showbus spec: a machine event is one 13.20 names (" + string.Join(", ", unknown) + "): " + name);
        foreach (var e in events.Where(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3 && e[1].ValueKind == JsonValueKind.String && e[1].GetString() == "refused"))
            Check(Str(e[2], "class") is { } cls && w.Classes.Contains(cls) && cls != "open", "showbus spec: a refused event carries a refusing class of 13.6.4's: " + name);
        Check(expect.All(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 && e[0].ValueKind == JsonValueKind.Number && e[1].ValueKind == JsonValueKind.Object),
            "showbus spec: a machine expectation is [ms, fields]: " + name);
        static bool Ordered(List<JsonElement> list) => list.Select(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].ValueKind == JsonValueKind.Number ? e[0].GetDouble() : double.NaN)
            .Zip(list.Skip(1).Select(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].ValueKind == JsonValueKind.Number ? e[0].GetDouble() : double.NaN))
            .All(p => p.Second >= p.First);
        Check(Ordered(events) && Ordered(expect), "showbus spec: a machine row's events and expectations are in time order: " + name);
        Check(Str(r, "role") is null or "acceptor", "showbus spec: a machine row plays the connector unless it names the acceptor: " + name);
        foreach (var e in expect.Where(e => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 2 && e[1].ValueKind == JsonValueKind.Object))
        {
            var f = e[1];
            var undefined = f.EnumerateObject().Select(p => p.Name).Where(n => !w.Fields.Contains(n)).ToList();
            Check(undefined.Count == 0, "showbus spec: a machine expectation names only fields 13.20 defines (" + string.Join(", ", undefined) + "): " + name);
            Check(Str(f, "state") is not { } st || w.States.Contains(st), $"showbus spec: a machine state is one of 13.16.1's ({Str(f, "state")}): " + name);
            Check(Str(f, "light") is not { } li || w.Lights.Contains(li), $"showbus spec: a machine light is the record's ({Str(f, "light")}): " + name);
            Check(Str(f, "show") is not { } sh || w.Shows.Contains(sh), $"showbus spec: a machine show is live, stuck or unknown ({Str(f, "show")}): " + name);
            Check(Str(f, "refusal") is not { } re || w.Refusals.Contains(re), $"showbus spec: a machine refusal is the record's ({Str(f, "refusal")}): " + name);
            Check(Str(f, "tls") is not { } tl || w.Tls.Contains(tl), $"showbus spec: a machine tls is the record's ({Str(f, "tls")}): " + name);
            Check(Strings(f, "flags").All(w.Flags.Contains), "showbus spec: a machine flag is one of 13.16.2's: " + name);
            Check(Strings(f, "credentials").All(x => x is "proof" or "auth-proof" or "bearer" or "session"), "showbus spec: a machine credential is one 13.20 names: " + name);
            Check(!f.TryGetProperty("waitMs", out var wait) || (wait.ValueKind == JsonValueKind.Array && wait.GetArrayLength() == 2 && wait.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number)
                                                    && wait[0].GetDouble() <= wait[1].GetDouble()),
                "showbus spec: a machine waitMs is a range: " + name);
            Check(!f.TryGetProperty("outcome", out var o) || (o.ValueKind == JsonValueKind.Object && o.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.String && w.Outcomes.Contains(p.Value.GetString()!))),
                "showbus spec: a machine outcome is one of 13.12.2's: " + name);
        }
    }

    // ------------------------------------------------------------------ the crypto rows, from the text alone

    static void Crypto(Action<bool, string> Check, string s13, List<JsonElement> rows)
    {
        // The labels the text writes into each hash and MAC.
        string[] labels = ["\"showbus-peerlink-1 code\\n\"", "\"showbus-peerlink-1 kid\\n\"", "\"showbus-peerlink-1 lid\\n\"",
            "\"client\\n\"", "\"server\\n\"", "\"session\\n\"", "\"enrol\\n\""];
        Check(labels.All(l => s13.Contains(l, StringComparison.Ordinal)), "showbus spec: §13 writes every label the vectors use (" + string.Join(" ", labels.Where(l => !s13.Contains(l, StringComparison.Ordinal))) + ")");

        var (canon, canonRemoves, canonOk) = Canon(s13);
        Check(canonOk, "showbus spec: 13.5.1 says what a code's canonical form removes and what it must be");
        var fields = TranscriptFields(Part(s13, 6) ?? "");
        Check(fields.SequenceEqual(["carrier", "offer", "speaks", "server", "client", "kid", "snonce", "cnonce", "bind"]),
            "showbus spec: 13.6.1's transcript is showbus-peerlink-1 then nine fields (" + string.Join(", ", fields) + ")");

        foreach (var r in rows.Where(r => Str(r, "case") == "canon"))
        {
            var code = Str(r, "code") ?? "";
            Check(canon(code) == Str(r, "canon"), $"showbus spec: canon {Short(r)} → {Str(r, "canon") ?? "refused"} (by 13.5.1's text: {canon(code) ?? "refused"})");
            if (Str(r, "K") is { } k)
            {
                Check(canon(code) is { } c && Hex(Sha(Utf8("showbus-peerlink-1 code\n" + c))) == k, "showbus spec: a code's K is SHA-256 of its label and canonical form: " + Short(r));
                Check(Str(r, "kid") == "code", "showbus spec: a code's kid is the word code: " + Short(r));
            }
        }
        var alphabet = KeyAlphabet(s13);
        Check(alphabet.Length == 32 && alphabet.Distinct().Count() == 32, "showbus spec: 13.5.1 names a key's 32 symbols (" + alphabet + ")");
        foreach (var r in rows.Where(r => Str(r, "case") == "key"))
        {
            var key = Str(r, "key") ?? "";
            var k = Sha(Utf8(key));
            Check(Regex.IsMatch(key, "^[a-z]{2,4}_[" + alphabet + "]{32}$"), "showbus spec: a key is a prefix and 32 symbols of 13.5.1's alphabet: " + key[..Math.Min(4, key.Length)]);
            Check(Hex(k) == Str(r, "K") && Kid(k) == Str(r, "kid"), "showbus spec: a key's K is SHA-256 of the key and its kid the labelled hash's first bytes: " + key[..4]);
        }

        // 13.6.8: the legacy rows, keyed as the text says, and proved only for a nonce of 32 lowercase hex.
        var (legacy, legacyOk) = Legacy(s13, canonRemoves);
        Check(legacyOk && (Part(s13, 6) ?? "").Contains("exactly 32 lowercase hex", StringComparison.Ordinal),
            "showbus spec: 13.6.8 says how a legacy proof is keyed, which tokens it does not prove, and which nonces it answers");
        foreach (var r in rows.Where(r => Str(r, "case") == "legacy"))
        {
            var nonce = Str(r, "nonce") ?? "";
            var provable = Regex.IsMatch(nonce, "^[0-9a-f]{32}$");
            if (Str(r, "proof") is { } proof)
                Check(provable && Str(r, "code") is { } code && legacy(code) is { } k && Hex64(proof)
                      && Hex(HMACSHA256.HashData(Utf8(k), Utf8(nonce + (Str(r, "name") ?? "")))) == proof,
                    "showbus spec: a legacy proof is §7's AUTH-PROOF keyed with the token as 1.1 desks compare it: " + Short(r));
            else
                Check(r.TryGetProperty("prove", out var p) && p.ValueKind == JsonValueKind.False
                      && (Str(r, "code") is { } code ? provable && legacy(code) is null : !provable),
                    "showbus spec: a legacy row with prove false names a token 13.6.8 does not prove, or a nonce it does not answer: " + Short(r));
        }

        var proofs = rows.Where(r => Str(r, "case") == "proof").ToList();
        Check(proofs.Count > 0 && Str(proofs[0], "carrier") == "line" && Str(proofs[0], "kid") == "code", "showbus spec: the first proof row, which refuse rows reuse, is a code on line");
        foreach (var r in proofs) Proof(Check, r, fields, canon);
        if (proofs.Count == 0 || !fields.All(f => proofs[0].TryGetProperty(f, out _)) || !Hex64(Str(proofs[0], "K"))) return;
        var first = proofs[0];
        foreach (var r in rows.Where(r => Str(r, "case") == "refuse"))
        {
            var why = Str(r, "why") ?? "";
            Check(Str(r, "expect") == "refused" && Hex64(Str(r, "clientProof")), "showbus spec: a refuse row is a proof that is refused: " + why);
            if (why == "role")
            {
                Check(Str(r, "clientProof") == Str(first, "serverProof"), "showbus spec: refuse role offers the server's proof as the client's");
                continue;
            }
            Check(fields.Contains(why) && r.TryGetProperty(why, out _), "showbus spec: a refuse row changes one transcript field and gives its value: " + why);
            if (!fields.Contains(why) || !r.TryGetProperty(why, out var changed)) continue;
            var t = Transcript(fields, f => f == why ? Value(changed) : Value(first.GetProperty(f)));
            var proof = Hex(HMACSHA256.HashData(FromHex(Str(first, "K")!), Utf8("client\n" + t)));
            Check(proof == Str(r, "clientProof") && proof != Str(first, "clientProof"), "showbus spec: changing " + why + " changes the client proof to the row's");
        }

        // 13.6.4's line example is the first proof row on the wire.
        var example = Between(Part(s13, 6) ?? "", "\n```\n← NONCE", "\n```") ?? "";
        Check(new[] { "snonce", "cnonce", "bind", "clientProof", "serverProof", "lid", "enrolKid" }.All(f => Str(first, f) is { } v && example.Contains(v, StringComparison.Ordinal))
              && example.Contains("LINK 1 main 3f9c2a1b7d0e", StringComparison.Ordinal) && example.Contains("LINK 1 patterns 9f8e7d6c", StringComparison.Ordinal),
            "showbus spec: 13.6.4's example carries the first proof row's nonces, certificate, proofs, lid and enrolled kid");
    }

    static void Proof(Action<bool, string> Check, JsonElement r, List<string> fields, Func<string, string?> canon)
    {
        var bind = Str(r, "bind") ?? "";
        var name = $"proof {Str(r, "carrier")} {Str(r, "kid")} {bind[..Math.Min(5, bind.Length)]}";
        Check(Str(r, "carrier") is "line" or "ws"
              && Int(r, "offer") is >= 0 && Int(r, "speaks") is >= 0
              && Regex.IsMatch(Str(r, "server") ?? "", "^[a-z0-9_-]{1,16}:[A-Za-z0-9_-]{6,40}$")
              && Regex.IsMatch(Str(r, "client") ?? "", "^[a-z0-9_-]{1,16}:[A-Za-z0-9_-]{6,40}$")
              && Regex.IsMatch(Str(r, "kid") ?? "", "^(?:[0-9a-f]{16}|code)$")
              && Regex.IsMatch(Str(r, "snonce") ?? "", "^[0-9a-f]{32}$") && Regex.IsMatch(Str(r, "cnonce") ?? "", "^[0-9a-f]{32}$")
              && Regex.IsMatch(Str(r, "bind") ?? "", "^(?:plain|tls:sha256:[0-9a-f]{64})$"),
            "showbus spec: a proof row's fields have 13.6.1's shapes: " + name);
        Check(Hex64(Str(r, "K")) && Hex64(Str(r, "clientProof")) && Hex64(Str(r, "serverProof")) && Regex.IsMatch(Str(r, "lid") ?? "", "^[0-9a-f]{16}$"),
            "showbus spec: a proof row's K, proofs and lid are lowercase hex: " + name);
        if (!fields.All(f => r.TryGetProperty(f, out _)) || !Hex64(Str(r, "K"))) return;
        var t = Transcript(fields, f => Value(r.GetProperty(f)));
        var k = FromHex(Str(r, "K")!);
        var plain = Str(r, "bind") == "plain";
        if (Str(r, "Tsha256") is { } th) Check(Hex(Sha(Utf8(t))) == th, "showbus spec: the transcript hashes to Tsha256: " + name);
        Check(Hex(HMACSHA256.HashData(k, Utf8("client\n" + t))) == Str(r, "clientProof"), "showbus spec: client proof = HMAC(K, client ‖ T): " + name);
        Check(Hex(HMACSHA256.HashData(k, Utf8("server\n" + t))) == Str(r, "serverProof"), "showbus spec: server proof = HMAC(K, server ‖ T): " + name);
        Check(Hex(Sha(Utf8("showbus-peerlink-1 lid\n" + t)))[..(LidBytes * 2)] == Str(r, "lid"), "showbus spec: lid = the first bytes of SHA-256(lid ‖ T): " + name);
        Check(Str(r, "kid") == "code" || Kid(k) == Str(r, "kid"), "showbus spec: a key's kid is its K's: " + name);
        if (Str(r, "code") is { } code) Check(canon(code) is { } c && Hex(Sha(Utf8("showbus-peerlink-1 code\n" + c))) == Str(r, "K"), "showbus spec: a code proof's K is its code's: " + name);
        if (Str(r, "session") is { } s)
            Check(Str(r, "carrier") == "ws" && Hex(HMACSHA256.HashData(k, Utf8("session\n" + t))) == s, "showbus spec: S = HMAC(K, session ‖ T), on ws only: " + name);
        if (Str(r, "enrolK") is { } ek)
        {
            var link = HMACSHA256.HashData(k, Utf8("enrol\n" + t));
            Check(!plain && Str(r, "kid") == "code", "showbus spec: only a code over TLS enrols: " + name);
            Check(Hex(link) == ek && Kid(link) == Str(r, "enrolKid"), "showbus spec: K_link = HMAC(K_code, enrol ‖ T), with its own kid: " + name);
        }
        if (r.TryGetProperty("enrol", out var enrol)) Check(enrol.ValueKind == JsonValueKind.False && plain, "showbus spec: a code over plain does not enrol: " + name);
        var flags = Strings(r, "flags");
        Check(plain == flags.Contains("plain") && (plain && Str(r, "kid") == "code") == flags.Contains("code-plain"),
            "showbus spec: a plain proof is flagged plain, and a code's code-plain: " + name);
    }

    // ------------------------------------------------------------------ what §13 says Stagewatch already does

    static void TodaysCode(Action<bool, string> Check, string s13, List<JsonElement> rows)
    {
        // 13.5.1: Stagewatch's swk_ keys are 13.5.1 keys, and what it stores for one is K.
        var (key, secret) = ApiKeys.Create("Hall B's playout", ApiKeyScope.Node, new DateTime(2026, 10, 25, 20, 0, 0, DateTimeKind.Utc));
        var alphabet = KeyAlphabet(s13);
        Check(alphabet.Length == 32 && Regex.IsMatch(secret, "^" + Regex.Escape(ApiKeys.Prefix) + "[" + alphabet + "]{32}$"), "showbus spec: a swk_ key is 13.5.1's prefix and 32 symbols");
        Check(key.Hash == Hex(Sha(Utf8(secret))), "showbus spec: what Stagewatch stores for a key is 13.5.1's K");

        // 13.6.8: Stagewatch's level-0 proof makes each legacy row's proof, and proves no nonce a row refuses.
        foreach (var r in rows.Where(r => Str(r, "case") == "legacy"))
        {
            var nonce = Str(r, "nonce") ?? "";
            if (Str(r, "proof") is { } proof)
                Check(PatternsLink.IsProvableNonce(nonce) && PatternsLink.Proof(Str(r, "code") ?? "", nonce, Str(r, "name") ?? "") == proof,
                    "showbus spec: Stagewatch's level-0 proof makes the legacy row's proof: " + Short(r));
            else if (Str(r, "code") is null)
                Check(!PatternsLink.IsProvableNonce(nonce), "showbus spec: Stagewatch proves no nonce but 32 lowercase hex: " + nonce);
        }
    }

    // ------------------------------------------------------------------ reading the text

    /// <summary>
    /// 13.5.1's canonical form, read from its own sentence: the characters it removes (<c>-</c> and each U+ range), a–z upper-cased,
    /// and the shape the result must have; null for a code it refuses.
    /// </summary>
    static (Func<string, string?> Canon, HashSet<int> Removed, bool Ok) Canon(string s13)
    {
        var keys = Part(s13, 5) ?? "";
        var sentence = Between(keys, "Its canonical form removes", "upper-cases") ?? "";
        var removed = new HashSet<int>();
        foreach (Match m in Regex.Matches(sentence, @"U\+([0-9A-F]{4,6})(?:–U\+([0-9A-F]{4,6}))?"))
        {
            var from = int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var to = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : from;
            for (var c = from; c <= to; c++) removed.Add(c);
        }
        if (sentence.Contains("`-`", StringComparison.Ordinal)) removed.Add('-');
        var shape = Regex.Match(keys, @"The result MUST be `([^`]+)`") is { Success: true } s ? s.Groups[1].Value : "";
        var ok = removed.Contains('-') && removed.Contains(0xFEFF) && removed.Contains(0x2009) && sentence.Contains("U+0009–U+000D", StringComparison.Ordinal) && shape.Length > 0;
        string? Canonical(string code)
        {
            var sb = new StringBuilder(code.Length);
            foreach (var rune in code.EnumerateRunes())
            {
                if (removed.Contains(rune.Value)) continue;
                sb.Append(rune.Value is >= 'a' and <= 'z' ? (char)(rune.Value - 32) : rune.ToString());
            }
            var result = sb.ToString();
            return shape.Length > 0 && Regex.IsMatch(result, "^(?:" + shape + ")$") ? result : null;
        }
        return (Canonical, removed, ok);
    }

    /// <summary>
    /// 13.6.8's legacy key: the token as 1.1 desks compare it — <c>-</c>, space, tab, CR and LF removed, every other UTF-16
    /// unit kept and upper-cased alone — or null when what is left still holds a character 13.5.1's canon removes, or nothing.
    /// </summary>
    static (Func<string, string?> Legacy, bool Ok) Legacy(string s13, HashSet<int> canonRemoves)
    {
        var handshake = Part(s13, 6) ?? "";
        var ok = handshake.Contains("`-`, space, tab, CR and LF removed", StringComparison.Ordinal)
                 && handshake.Contains("upper-cased alone, one UTF-16 unit at a time", StringComparison.Ordinal)
                 && handshake.Contains("still holds a character 13.5.1's canonical form removes", StringComparison.Ordinal);
        string? Key(string token)
        {
            var sb = new StringBuilder(token.Length);
            foreach (var c in token)
            {
                if (c is '-' or ' ' or '\t' or '\r' or '\n') continue;
                if (canonRemoves.Contains(c)) return null;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }
        return (Key, ok);
    }

    /// <summary>The symbols 13.5.1 makes a key of ("32 symbols of `…`"), or empty.</summary>
    static string KeyAlphabet(string s13)
        => Regex.Match(Part(s13, 5) ?? "", @"32\s+symbols of `([a-z0-9]+)`").Groups[1].Value;

    /// <summary>13.6.1's transcript fence: its first line is the label, then one <c>name=&lt;…&gt;</c> per field.</summary>
    static List<string> TranscriptFields(string handshake)
    {
        var fence = Between(handshake, "\n```\nshowbus-peerlink-1\n", "\n```") ?? "";
        return fence.Split('\n').Skip(3).Select(l => l.Split('=')[0]).Where(f => f.Length > 0).ToList();
    }

    static string Transcript(List<string> fields, Func<string, string> value)
        => "showbus-peerlink-1\n" + string.Join("\n", fields.Select(f => f + "=" + value(f)));

    /// <summary>The text of part 13.<paramref name="n"/>, up to the next part.</summary>
    static string? Part(string s13, int n) => Between(s13, $"\n### 13.{n.ToString(CultureInfo.InvariantCulture)} ", "\n### ");

    /// <summary>From the first <paramref name="from"/> up to the next <paramref name="to"/> after it (or the end).</summary>
    static string? Between(string text, string from, string to)
    {
        var i = text.IndexOf(from, StringComparison.Ordinal);
        if (i < 0) return null;
        var j = text.IndexOf(to, i + from.Length, StringComparison.Ordinal);
        return j < 0 ? text[i..] : text[i..j];
    }

    static string? Line(string text, string label)
        => text.Split('\n').FirstOrDefault(l => l.Contains(label, StringComparison.Ordinal));

    static IEnumerable<string> Ticked(string line) => Regex.Matches(line, "`([a-z-]+)`").Select(m => m.Groups[1].Value);

    /// <summary>The words that open a list's bullets (<c>- `word`</c>, and <c>`word`, and `word`</c> on one bullet).</summary>
    static HashSet<string> Bullets(string list)
        => Regex.Matches(list, @"^\s+- `([a-z-]+)`(?:, and `([a-z-]+)`)?", RegexOptions.Multiline)
            .SelectMany(m => m.Groups.Values.Skip(1).Where(g => g.Success).Select(g => g.Value)).ToHashSet(StringComparer.Ordinal);

    /// <summary>The ticked words in the first cell of each row of the table whose header starts <c>| header |</c>.</summary>
    static HashSet<string> Table(string text, string header)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        var inTable = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("| " + header + " |", StringComparison.Ordinal)) { inTable = true; continue; }
            if (!inTable) continue;
            if (!line.StartsWith('|')) break;
            if (line.StartsWith("|---", StringComparison.Ordinal)) continue;
            var cell = line.Split('|')[1];
            words.UnionWith(Regex.Matches(cell, "`([A-Za-z-]+)`").Select(m => m.Groups[1].Value));
        }
        return words;
    }

    // ------------------------------------------------------------------ JSON and bytes

    static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static long? Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    static double? Num(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static List<JsonElement> Items(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().ToList() : [];

    static List<string> Strings(JsonElement e, string name)
        => Items(e, name).Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : "").ToList();

    /// <summary>A transcript field as T writes it: a string as it is, a number in invariant digits.</summary>
    static string Value(JsonElement v)
        => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ValueKind == JsonValueKind.Number ? v.GetInt64().ToString(CultureInfo.InvariantCulture) : v.GetRawText();

    static string Short(JsonElement r) => r.GetRawText() is var t && t.Length > 60 ? t[..60] : r.GetRawText();

    static bool Hex64(string? s) => s is not null && Regex.IsMatch(s, "^[0-9a-f]{64}$");

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    static byte[] Sha(byte[] b) => SHA256.HashData(b);

    static string Hex(byte[] b) => Convert.ToHexString(b).ToLowerInvariant();

    static byte[] FromHex(string hex) => Convert.FromHexString(hex);

    /// <summary>A key's kid: the first bytes of SHA-256 of the kid label and K, as lowercase hex.</summary>
    static string Kid(byte[] k) => Hex(Sha([.. Utf8("showbus-peerlink-1 kid\n"), .. k]))[..(KidBytes * 2)];
}
