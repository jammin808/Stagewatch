using System.Globalization;
using System.Text.Json;
using Stagewatch.Core;

/// <summary>
/// The verb table (Show Bus §6's notation, §11's risk): a line rates by the most specific whole form of a verified table, in bounded
/// steps, its carried lines failing closed; the reader keeps a peer's context within §14's numbers; one selector gives the Show network
/// and a proposal's risk the same context; a context refused as too big is named on the Show network; and PlayMap's busy golden context
/// replays — every form's example rated as its own row or higher, never lower — against the probes the design and the review measured.
/// Pure, on fixed time.
/// </summary>
static class VerbTableTests
{
    static readonly DateTime T0 = new(2026, 10, 27, 19, 0, 0, DateTimeKind.Utc);

    public static void Run(Action<bool, string> Check)
    {
        Words(Check);
        Forms(Check);
        Notation(Check);
        Specificity(Check);
        Carried(Check);
        Readings(Check);
        Bounded(Check);
        Secrets(Check);
        TableCaps(Check);
        ReaderCaps(Check);
        Links(Check);
        Verified(Check);
        Readers(Check);
        TooBig(Check);
        Golden(Check);
    }

    static RiskVerdict Rate(string line, params (string Verb, string Risk)[] rows) => VerbTable.Compile(rows).Rate(line);

    /// <summary>A line's words as the line wires read them.</summary>
    static void Words(Action<bool, string> Check)
    {
        Check(VerbSyntax.Words("SET out1 name \"Left wall\"") is [{ Text: "SET" }, { Text: "out1" }, { Text: "name" }, { Text: "Left wall", Raw: "\"Left wall\"", Quoted: true }],
            "verbs: a quoted phrase is one word, its quotes off its text");
        Check(VerbSyntax.Words("ADD media {\"path\": \"a b.mp4\"}") is [_, _, { Json: true, Text: "{\"path\": \"a b.mp4\"}" }],
            "verbs: a word that opens with { runs to the end of the line");
        Check(VerbSyntax.Words("note \"a \\\"b\\\" c\"") is [_, { Text: "a \"b\" c" }], "verbs: a backslash in a phrase takes the mark after it");
        Check(VerbSyntax.Words("note \"open") is null && Rate("note \"open", ("note <text>", "low")) == RiskVerdict.Unlisted,
            "verbs: a quote that does not close reads as no line, and rates high");
        Check(VerbSyntax.Words("  ") is { Count: 0 } && Rate("", ("note <text>", "low")) == RiskVerdict.Unlisted, "verbs: an empty line is high");
    }

    /// <summary>A row's whole forms: " | " outside brackets, carried leading literals, a row whose brackets do not close has none.</summary>
    static void Forms(Action<bool, string> Check)
    {
        Check(VerbSyntax.Forms("start | pause").SequenceEqual(["start", "pause"]), "verbs: ' | ' separates whole forms");
        Check(VerbSyntax.Forms("MACHINE SET TIMER HIGH|DEFAULT | GPU-PRIORITY NORMAL|HIGH|REALTIME")
                .SequenceEqual(["MACHINE SET TIMER HIGH|DEFAULT", "MACHINE SET GPU-PRIORITY NORMAL|HIGH|REALTIME"]),
            "verbs: a form that does not start with the verb's first word carries on from the first form's leading literals, all but the last");
        Check(VerbSyntax.Forms("UNLOCK <x> | UNLOCK AREA <a> | unlock ALL").SequenceEqual(["UNLOCK <x>", "UNLOCK AREA <a>", "unlock ALL"]),
            "verbs: a form that starts with the verb's first word, whatever its case, stands as written");
        Check(VerbSyntax.Forms("PANIC [ON <a> | <b>]").Count == 1 && VerbSyntax.Forms("SEND ENABLE <send> ON|OFF").Count == 1,
            "verbs: '|' inside brackets or a word is not a new form");
        Check(VerbSyntax.Forms("FOO <bar").Count == 0 && VerbSyntax.Forms("FOO [bar").Count == 0 && Rate("FOO x", ("FOO <bar", "low")) == RiskVerdict.Unlisted,
            "verbs: a row whose bracket never closes has no form, so no line rates by it");
        Check(VerbSyntax.Forms("GO -> NEXT").Count == 1 && Rate("GO -> NEXT", ("GO -> NEXT", "low")).Risk == "low", "verbs: a closing mark with nothing open is a plain mark");
        Check(VerbSyntax.Example("PANIC [BLACK|RUN] [ON <output>…|ALL]", false) == "PANIC" && VerbSyntax.Example("PANIC [BLACK|RUN] [ON <output>…|ALL]", true) == "PANIC BLACK ON x1",
            "verbs: a form's example takes its literals, x1 for each value, and its options only when asked");
    }

    /// <summary>Each piece of §6's notation.</summary>
    static void Notation(Action<bool, string> Check)
    {
        Check(Rate("Go", ("GO", "low")).Risk == "low" && Rate("go now", ("GO", "low")) == RiskVerdict.Unlisted,
            "verbs: a literal matches whatever its case, and a form takes no word it does not name");
        Check(Rate("eye prev", ("EYE NEXT|PREV|RESET", "low")).Risk == "low" && !Rate("eye up", ("EYE NEXT|PREV|RESET", "low")).Listed,
            "verbs: A|B is one word, either of those");
        (string, string) panic = ("PANIC [BLACK [FADE <ms>]]", "review");
        Check(new[] { "PANIC", "PANIC BLACK", "panic black fade 300" }.All(l => Rate(l, panic).Risk == "review") && !Rate("PANIC FADE 300", panic).Listed,
            "verbs: [...] is optional and nests");
        Check(Rate("SHOW ARM", ("SHOW ARM …", "review")).Risk == "review" && Rate("SHOW ARM ALL rehearsal done", ("SHOW ARM …", "review")).Risk == "review",
            "verbs: a bare … is any further words, none included");
        (string, string) take = ("SCREEN <n> TAKE|CUT", "high"), stage = ("SCREEN <n> PVW LOOK|PRESET|PATTERN <name>", "review");
        Check(Rate("SCREEN 2 PVW LOOK Main look", take, stage) is { Risk: "review", Form: "SCREEN <n> PVW LOOK|PRESET|PATTERN <name>" }
              && Rate("SCREEN 2 TAKE PVW LOOK x", take, stage) == RiskVerdict.Unlisted && Rate("SCREEN two 2 TAKE", take, stage) == RiskVerdict.Unlisted,
            "verbs: a value is one word, but a form's last element takes one word or more");
        (string, string) bind = ("BIND ADD <input words> TO <target line>", "review"), cue = ("PLAYLIST CUE <playlist> <item>", "review");
        Check(Rate("BIND ADD note 60 TO PLAYLIST CUE show #3", bind, cue).Risk == "review" && Rate("BIND ADD note 60", bind, cue) == RiskVerdict.Unlisted,
            "verbs: a value named '… words' takes one word or more where it stands");
        Check(Rate("SET out1 name \"Left wall\"", ("SET <id> <field.path> <json>", "review")).Risk == "review"
              && Rate("ADD media {\"path\": \"a b.mp4\"}", ("ADD media|playlist [<owner>] <json>", "review")).Risk == "review",
            "verbs: a quoted phrase and a JSON run are one word each");
        var words = string.Join(' ', Enumerable.Repeat("w", VerbSyntax.MaxWords - 1));
        Check(Rate("SAY " + words, ("SAY <words>", "low")).Risk == "low" && Rate("SAY w " + words, ("SAY <words>", "low")) == RiskVerdict.Unlisted,
            $"verbs: a line of {VerbSyntax.MaxWords} words is read, one of {VerbSyntax.MaxWords + 1} is high");
        Check(Rate("PLAYLIST CUE Main show #3", ("PLAYLIST CUE <playlist> <item>|#<n>", "review")) is { Risk: "review", Listed: true, Words: "" }
              && VerbTable.Compile([new PeerCommand("CUE GO", "high", "fire the standby cue")]).Rate("cue go") is { Words: "fire the standby cue", Form: "CUE GO" },
            "verbs: the verdict names the form that decided it and its row's words");
    }

    /// <summary>The most literal words win, then the most single literals; an exact tie rates higher; a risk that is no word is high.</summary>
    static void Specificity(Action<bool, string> Check)
    {
        (string, string) eye = ("EYE <thing>", "review"), next = ("EYE NEXT", "low");
        Check(Rate("EYE NEXT", eye, next).Risk == "low" && Rate("EYE screen2", eye, next).Risk == "review",
            "verbs: EYE NEXT is EYE NEXT's row, more literal words than EYE <thing>'s");
        (string, string) both = ("SEND ENABLE <send> ON|OFF", "high"), on = ("SEND ENABLE <send> ON", "review");
        Check(Rate("SEND ENABLE s1 ON", both, on).Risk == "review" && Rate("SEND ENABLE s1 OFF", both, on).Risk == "high",
            "verbs: a word written in (a risk that depends on it, its own row) beats the alternatives that name it");
        Check(Rate("NOTE hi", ("NOTE <text>", "low"), ("NOTE <words>", "review")).Risk == "review"
              && Rate("NOTE hi", ("NOTE <words>", "review"), ("NOTE <text>", "low")).Risk == "review",
            "verbs: an exact tie gives the higher risk, in either order");
        Check(Rate("GO", ("GO", "HIGH ")).Risk == "high" && Rate("GO", ("GO", "Low")).Risk == "low" && Rate("GO", ("GO", "medium")).Risk == "high"
              && Rate("GO", ("GO", "")).Risk == "high", "verbs: a risk is low, review or high in any case; anything else is high");
        Check(Rate("pause every screen for good", ("pause", "low")) == RiskVerdict.Unlisted
              && ProposalDesk.RiskFromCommands([("pause", "low")], "pause every screen for good") == "high",
            "verbs: a form's leading words alone never rate a longer line (the whole form must take it)");
    }

    /// <summary>A value named line holds a line of the same node: it raises the risk, four lines deep, and JSON in such a row is high.</summary>
    static void Carried(Action<bool, string> Check)
    {
        (string, string)[] rows = [("BIND ADD <input words> TO <target line> | BIND ADD <json>", "review"), ("BLACKOUT ON", "high"), ("PLAYLIST CUE <playlist> <item>", "review")];
        Check(Rate("BIND ADD note 60 TO BLACKOUT ON", rows).Risk == "high" && Rate("BIND ADD note 60 TO PLAYLIST CUE show #3", rows).Risk == "review",
            "verbs: a carried line raises the risk to its own");
        Check(Rate("BIND ADD note 60 TO nothing listed here", rows) is { Risk: "high", Listed: true, Form: "BIND ADD <input words> TO <target line>" }
              && Rate("BIND ADD note 60 TO BLACKOUT", rows).Risk == "high",
            "verbs: a carried line no form takes is high, as it is on its own: the node may run it by a rule no table states (another name, its first words alone)");
        Check(Rate("BIND ADD note 60 TO \"BLACKOUT ON\"", rows).Risk == "high", "verbs: a quoted carried line is read as its text");
        string Nested(int n) => string.Concat(Enumerable.Range(0, n).Select(i => $"BIND ADD k{i.ToString(CultureInfo.InvariantCulture)} TO ")) + "PLAYLIST CUE show 3";
        Check(Rate(Nested(VerbSyntax.MaxDepth), rows).Risk == "review" && Rate(Nested(VerbSyntax.MaxDepth + 1), rows).Risk == "high",
            $"verbs: lines carried {VerbSyntax.MaxDepth} deep are read; one deeper is high");
        Check(Rate("BIND ADD {\"input\":\"note 60\",\"target\":\"BLACKOUT ON\"}", rows).Risk == "high" && Rate("BIND ADD x1", rows).Risk == "high"
              && Rate("ADD media {\"path\":\"BLACKOUT ON\"}", ("ADD media <json>", "review"), ("BLACKOUT ON", "high")).Risk == "review",
            "verbs: JSON in a row that carries lines is high (the desk does not read JSON); JSON in any other row is its row's");
        Check(Rate("MOVE it1 3 BLACKOUT ON", ("MOVE <id> <index>", "low"), ("BLACKOUT ON", "high")).Risk == "low",
            "verbs: words a form's last value takes are that value's, not a line (PlayMap's own RiskOf rates MOVE so)");
    }

    /// <summary>
    /// A line's carried lines are counted at every depth together, a line carried twice each time (PlayMap's own count): past
    /// <see cref="VerbTable.MaxCarried"/> the line is high, at or under it it rates by its lines.
    /// </summary>
    static void Readings(Action<bool, string> Check)
    {
        var table = VerbTable.Compile([("BIND ADD <input words> TO <target line>", "review"), ("TO …", "low"), ("PLAYLIST CUE <playlist> <item>", "review")]);
        static string Line(int tos) => string.Concat(Enumerable.Repeat("BIND ADD x TO ", 4)) + string.Concat(Enumerable.Repeat("TO ", tos)) + "PLAYLIST CUE a b";
        var counted = Enumerable.Range(0, VerbSyntax.MaxWords - 19).Select(m => (M: m, Measured: table.Measure(Line(m)))).ToList();
        var over = counted.FirstOrDefault(c => c.Measured.Carried > VerbTable.MaxCarried);
        var under = counted.Where(c => c.Measured.Carried <= VerbTable.MaxCarried).ToList();
        Check(over.M > 0 && over.Measured.Verdict is { Risk: "high", Listed: true } && under.Count == over.M
              && under.All(c => c.Measured.Verdict is { Risk: "review", Listed: true }) && counted.Where(c => c.M >= over.M).All(c => c.Measured.Verdict.Risk == "high"),
            $"verbs: a line whose carried lines number more than {VerbTable.MaxCarried} at every depth is high; at or under, it rates by them "
            + $"(high from {over.M} more TOs, {over.Measured.Carried} read; {under.LastOrDefault().Measured.Carried} read below)");
        Check(Line(over.M).Split(' ').Length <= VerbSyntax.MaxWords, $"verbs: such a line fits a line's {VerbSyntax.MaxWords} words ({Line(over.M).Split(' ').Length})");
    }

    /// <summary>
    /// Every rating is bounded: a form is a flat run of steps, each step tried once against each word, so optional parts cost their
    /// count, not two to that power; past <see cref="VerbTable.MaxSteps"/> a line is high and unlisted; and a proposal is rated before
    /// the approval desk's lock is taken.
    /// </summary>
    static void Bounded(Action<bool, string> Check)
    {
        var line = "X " + string.Join(' ', Enumerable.Repeat("w", VerbSyntax.MaxWords - 1));
        // The review's row: optional values, as many as a verb's 512 characters hold, and optional literals, about 120 of them.
        var values = "X" + string.Concat(Enumerable.Repeat(" [<a>]", (PeerContexts.MaxVerbChars - 1) / 6));
        var literals = "X" + string.Concat(Enumerable.Repeat(" [w]", (PeerContexts.MaxVerbChars - 1) / 4));
        var a = VerbTable.Compile([(values, "low")]).Measure(line);
        var b = VerbTable.Compile([(literals, "low")]).Measure(line);
        // Each step of the form is tried at most once for each place in the line, and each trial takes at most three steps.
        static int Bound(string verb) => 3 * (1 + 2 * verb.Count(c => c == '[')) * (VerbSyntax.MaxWords + 1);
        Check(values.Length <= PeerContexts.MaxVerbChars && literals.Length <= PeerContexts.MaxVerbChars && literals.Count(c => c == '[') >= 120
              && a.Verdict is { Risk: "low", Listed: true } && b.Verdict is { Risk: "low", Listed: true } && a.Steps <= Bound(values) && b.Steps <= Bound(literals),
            $"verbs: a 512-character row of optional parts against a {VerbSyntax.MaxWords}-word line is rated in steps its places count bounds "
            + $"({a.Steps} ≤ {Bound(values)}, {b.Steps} ≤ {Bound(literals)})");
        // A row of values that each take one word or more: every way of cutting the line is a step, so the bound speaks.
        var many = "X" + string.Concat(Enumerable.Repeat(" <a words>", (PeerContexts.MaxVerbChars - 1) / 10));
        var slow = VerbTable.Compile(Enumerable.Repeat((many, "low"), PeerContexts.MaxCommands)).Measure(line);
        var most = VerbTable.MaxSteps + 2 * (VerbSyntax.MaxWords + 1) * (VerbSyntax.MaxWords + 1);
        Check(slow.Verdict == RiskVerdict.Unlisted && slow.Steps > VerbTable.MaxSteps && slow.Steps <= most,
            $"verbs: a table of {PeerContexts.MaxCommands} rows that would take more than {VerbTable.MaxSteps} steps stops at the bound and rates the line high ({slow.Steps} ≤ {most})");

        var desk = new ProposalDesk(() => "desk01", new NodeBindings());
        var free = false;
        var (proposal, error, _) = desk.Submit("{\"from\":{\"t\":\"patterns\",\"id\":\"pat9\",\"name\":\"FOH\"},\"target\":{\"t\":\"playmap\",\"id\":\"pm1\",\"name\":\"Hall\"},\"line\":\"GO\"}",
            NodeCredential.Operator, (_, _) =>
            {
                // Another thread reads the desk while the line is rated: it waits on nothing.
                free = Task.Run(() => desk.All().Count).Wait(TimeSpan.FromSeconds(10));
                return "low";
            }, T0);
        Check(proposal is { Risk: "low" } && error is null && free, "verbs: a proposal is rated before the desk's lock is taken, so the desk answers while a line is rated");
    }

    /// <summary>A secret placeholder, by its name or a row's "secret": n, filled with anything but the marker.</summary>
    static void Secrets(Action<bool, string> Check)
    {
        (string, string) link = ("LINK STAGEWATCH <host[:port]>|\"\"|OFF [<key>]", "review");
        Check(Rate("LINK STAGEWATCH host swk_abc", link) is { Risk: "review", Secret: true } && Rate("LINK STAGEWATCH host " + VerbSyntax.SecretMarker, link) is { Secret: false }
              && Rate("LINK STAGEWATCH host", link) is { Secret: false, Listed: true },
            "verbs: a value named key filled with anything but ‹secret› is a filled secret; the marker or nothing is not");
        Check(new[] { "token", "KEY", "Password", "passcode", "secret" }.All(n => Rate("AUTH x", ($"AUTH <{n}>", "low")).Secret)
              && !Rate("PIN ADD 3 4", ("PIN ADD <pin> <x>", "review")).Secret && !Rate("WEB KEY F5", ("WEB KEY <key name>", "high")).Secret,
            "verbs: a secret placeholder is one whose whole name is token, key, password, passcode or secret; pin and 'key name' are not");
        var table = VerbTable.Compile([new PeerCommand("AUTH <code>", "low", "", 1), new PeerCommand("CREDENTIAL SET <ref> <value>", "high", "", 3)]);
        Check(table.Rate("AUTH abc").Secret && !table.Rate("AUTH " + VerbSyntax.SecretMarker).Secret && !table.Rate("CREDENTIAL SET r").Listed
              && !table.Rate("CREDENTIAL SET r " + VerbSyntax.SecretMarker).Secret && table.Rate("CREDENTIAL SET r v").Secret,
            "verbs: a row's \"secret\": n makes every word from the n-th on (the first word 0) a secret");
        Check(Rate("BIND ADD note 60 TO LINK STAGEWATCH h swk_x", ("BIND ADD <input words> TO <target line>", "review"), link) is { Secret: true, Risk: "review" },
            "verbs: a secret filled in a carried line is the line's");
    }

    /// <summary>A table keeps 256 rows, each verb whole within 512 characters or dropped, and counts what it does not keep.</summary>
    static void TableCaps(Action<bool, string> Check)
    {
        var rows = Enumerable.Range(0, 300).Select(i => ("V" + i.ToString(CultureInfo.InvariantCulture), "low")).ToList();
        var table = VerbTable.Compile(rows);
        Check(table.Commands.Count == PeerContexts.MaxCommands && table.Dropped == 300 - PeerContexts.MaxCommands && table.Rate("V255").Risk == "low" && !table.Rate("V256").Listed,
            $"verbs: 300 rows keep {PeerContexts.MaxCommands} and count {300 - PeerContexts.MaxCommands}");
        var whole = "GO " + new string('x', PeerContexts.MaxVerbChars - 3);
        var over = whole + "y";
        var two = VerbTable.Compile([(over, "low"), (whole, "low"), ("", "low")]);
        Check(two.Commands.Count == 1 && two.Commands[0].Verb == whole && two.Dropped == 2 && !two.Rate(over).Listed,
            $"verbs: a verb of {PeerContexts.MaxVerbChars} characters is kept whole, a longer or empty one dropped and counted, never cut");
        Check(VerbTable.Empty.Rate("anything") == RiskVerdict.Unlisted && VerbTable.Rank("low") < VerbTable.Rank("review") && VerbTable.Rank("review") < VerbTable.Rank("bogus"),
            "verbs: an empty table rates every line high");
        Check(VerbTable.Compile([("FOO [bar", "low"), ("GO", "low")]) is { Dropped: 1, Commands: [{ Verb: "GO" }] },
            "verbs: a row with no form (a bracket it opens never closes) is dropped and counted, as one too long is");
    }

    static string Context(string extra) => "{\"v\":1,\"node\":{\"t\":\"playmap\",\"id\":\"pm0badc0de\",\"name\":\"Hall\"}" + extra + "}";

    static string Many(string key, IEnumerable<string> items) => ",\"" + key + "\":[" + string.Join(",", items) + "]";

    /// <summary>What PeerContexts keeps of a context: 200 items, 256 commands each whole, words to 512, the secret field read.</summary>
    static void ReaderCaps(Action<bool, string> Check)
    {
        var items = PeerContexts.Parse(Context(Many("items", Enumerable.Range(0, 250).Select(i => "{\"id\":\"i" + i.ToString(CultureInfo.InvariantCulture) + "\",\"light\":\"green\"}"))), "", T0);
        Check(items is { Items.Count: PeerContexts.MaxItems } && items.Items[^1].Id == "i199", $"reader: a context's first {PeerContexts.MaxItems} items are kept");
        var commands = PeerContexts.Parse(Context(Many("commands", Enumerable.Range(0, 300).Select(i => "{\"verb\":\"V" + i.ToString(CultureInfo.InvariantCulture) + "\",\"risk\":\"low\"}"))), "", T0);
        Check(commands is { Commands.Count: PeerContexts.MaxCommands, CommandsDropped: 44 } && commands.Commands[^1].Verb == "V255",
            $"reader: a context's first {PeerContexts.MaxCommands} commands are kept and the rest counted");
        var verb = "GO " + new string('x', PeerContexts.MaxVerbChars - 3);
        var words = new string('w', PeerContexts.MaxWordsChars + 40);
        var one = PeerContexts.Parse(Context(Many("commands", [
            $"{{\"verb\":\"{verb}y\",\"risk\":\"low\"}}", $"{{\"verb\":\"{verb}\",\"risk\":\"low\",\"words\":\"{words}\"}}",
            "{\"verb\":\"A\\u0007B\",\"risk\":\"low\"}", "{\"verb\":\"\",\"risk\":\"low\"}", "42", "{\"verb\":\"FOO [bar\",\"risk\":\"low\"}",
            "{\"verb\":\"AUTH <code>\",\"risk\":\"Low\",\"secret\":1}", "{\"verb\":\"X1\",\"risk\":\"meh\",\"secret\":\"1\"}",
            "{\"verb\":\"X2\",\"risk\":\"review\",\"secret\":-1}", "{\"verb\":\"X3\",\"risk\":\"review\",\"secret\":65}", "{\"verb\":\"X4\",\"secret\":2.5}"])), "", T0);
        Check(one is { Commands.Count: 6, CommandsDropped: 4 } && one.Commands[0].Verb == verb && one.Commands[0].Words.Length == PeerContexts.MaxWordsChars,
            $"reader: a verb of {PeerContexts.MaxVerbChars} characters is kept whole, a longer, empty, control-bearing or formless one dropped and counted; words kept to {PeerContexts.MaxWordsChars}");
        Check(one is { Commands: [_, { Risk: "low", SecretFrom: 1 }, { Risk: "high", SecretFrom: 0 }, { SecretFrom: 0 }, { SecretFrom: 0 }, { Risk: "high", SecretFrom: 0 }] },
            "reader: a risk that is no word reads high, and a secret field that is not a whole number from 0 to 64 marks the whole row secret");
        Check(one!.Verbs.Rate("AUTH k").Secret && one.Verbs.Rate("X1").Secret && ReferenceEquals(one.Verbs, one.Verbs) && ReferenceEquals((one with { Proof = NodeProof.Key }).Verbs, one.Verbs)
              && !ReferenceEquals((one with { Commands = [new PeerCommand("GO", "low")] }).Verbs, one.Verbs),
            "reader: an entry's table is compiled once for its commands, shared by a copy with the same commands");
    }

    /// <summary>The links PeerContexts keeps: §6's ends and kinds, the first 400, the rest counted.</summary>
    static void Links(Action<bool, string> Check)
    {
        string Link(string from, string to, string kind = "feeds", string light = "green") => JsonSerializer.Serialize(new { from, to, kind, light });
        var many = PeerContexts.Parse(Context(Many("links", Enumerable.Range(0, 450).Select(i => Link("a" + i.ToString(CultureInfo.InvariantCulture), "@pat9f8e7d/screen:1")))), "", T0);
        Check(many is { Links.Count: PeerContexts.MaxLinks, LinksDropped: 50 } && many.Links[0] == new ContextLink("a0", "@pat9f8e7d/screen:1", "feeds", Light.Green),
            $"reader: a context's first {PeerContexts.MaxLinks} links are kept and the rest counted");
        var item = new string('i', PeerContexts.ItemIdChars);
        var node = new string('n', 40);
        string[] good = ["output:1", "@abcdef", "@abcdef/screen:1", "@" + node, "@" + node + "/" + item, item];
        string[] bad = ["", "@abcde", "@" + node + "n", item + "i", "@" + node + "/" + item + "j", "@abcdef/", "@abc def", "a b", "a\u0007", "@ab$def",
            "@abcdef/" + new string('j', PeerContexts.LinkEndChars)];
        Check(good.All(PeerContexts.ValidEnd) && bad.All(e => !PeerContexts.ValidEnd(e)),
            $"reader: a link end is an own item id of 1 to {PeerContexts.ItemIdChars} characters, or @ a node id of {PeerContexts.EndIdMin} to 40 and an item id, at most {PeerContexts.LinkEndChars} in all");
        var mixed = PeerContexts.Parse(Context(Many("links", [Link("a", "b", "controls", "purple"), Link("a", "b", "eats"), Link("a", "@ab"), "{\"from\":\"a\",\"to\":3,\"kind\":\"feeds\"}",
            "\"a\"", Link("x", "@pm0badc0de", "follows", "red")])), "", T0);
        Check(mixed is { Links: [{ Kind: "controls", Light: Light.Grey }, { From: "x", Kind: "follows", Light: Light.Red }], LinksDropped: 3 },
            "reader: a link whose kind §6 does not name, or whose end breaks §6's rules, is left out and counted; its light read as §6's");
        Check(ShowBusKinds.Links.All(k => PeerContexts.Parse(Context(Many("links", [Link("a", "b", k)])), "", T0) is { Links.Count: 1 }),
            "reader: every link kind §6 names is kept");
    }

    /// <summary>A §6 context of <paramref name="kind"/> for <paramref name="id"/> listing <paramref name="commands"/> (each a JSON object).</summary>
    static string Ctx(string kind, string id, string name, params string[] commands)
        => "{\"v\":1,\"node\":{\"t\":\"" + kind + "\",\"id\":\"" + id + "\",\"name\":\"" + name + "\"},\"commands\":[" + string.Join(",", commands) + "]}";

    static string Cmd(string verb, string risk, string words = "") => JsonSerializer.Serialize(new { verb, risk, words });

    /// <summary>
    /// Risk comes only from a verified table of the target's kind, chosen as the Show network's row chooses it: the context this desk's
    /// own link pulled first while it proves something, never one of another kind and never one that proves nothing.
    /// </summary>
    static void Verified(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var peers = new PeerContexts(nodes);
        const string line = "EYE FOCUS screen 2";
        var pat9 = new NodeRef("patterns", "pat9", "FOH");
        static string Posted(string id, string risk, string kind = "patterns") => Ctx(kind, id, "FOH", Cmd("EYE FOCUS <thing>", risk, "move the Eye"));
        static PeerContexts.Entry Pulled(string json) => PeerContexts.Parse(json, "10.0.0.30", T0)!;
        Check(peers.Accept(Posted("pat9", "low"), "10.0.0.5", T0, "desk01", NodeCredential.Operator).Status == 200
              && peers.VerdictFor(pat9, line, T0) is { Risk: "low", Form: "EYE FOCUS <thing>", Words: "move the Eye", Listed: true },
            "verified: the operator's posted context rates by its whole forms");
        Check(peers.VerdictFor(pat9, line, T0, Pulled(Posted("pat9", "review"))) is { Risk: "review", Listed: true },
            "verified: the context this desk's own link pulled comes before a posted one");
        Check(peers.VerdictFor(pat9, line, T0, Pulled(Posted("pat8", "review"))).Risk == "low",
            "verified: a pulled context for another id leaves the posted one");
        Check(peers.VerdictFor(pat9 with { Kind = "playmap" }, line, T0) == RiskVerdict.Unlisted
              && peers.VerdictFor(pat9, line, T0, Pulled(Posted("pat9", "review", "playmap"))).Risk == "low",
            "verified: a context speaks for its own kind of node alone — a target of another kind finds none, and a pulled one of another kind leaves the posted one");

        // A Patterns peer at the operator's link address whose context names a PlayMap node's id never rates that node's lines.
        var pm = new NodeRef("playmap", "pm5e6f7a", "Stage left");
        Check(peers.Accept(Ctx("playmap", "pm5e6f7a", "Stage left", Cmd("PLAYLIST GO <playlist>", "high")), "10.0.0.40", T0, "desk01", NodeCredential.Operator).Status == 200
              && peers.VerdictFor(pm, "PLAYLIST GO show", T0, Pulled(Ctx("patterns", "pm5e6f7a", "FOH", Cmd("PLAYLIST GO <playlist>", "low")))).Risk == "high"
              && peers.Chosen("pm5e6f7a", new HeldContext(Pulled(Ctx("patterns", "pm5e6f7a", "FOH")), NodeProof.None, Pulled: true), T0, "playmap") is { Pulled: false, Entry.Kind: "playmap" },
            "verified: a Patterns link's context that names a PlayMap node's id leaves that node's own posted table to decide");

        // One selector: with the operator's post and a pulled context for one id, the Show network's row and the risk read the pulled one.
        var pulledJson = Ctx("patterns", "pat9", "Linked", Cmd("EYE FOCUS <thing>", "review"), Cmd(new string('v', PeerContexts.MaxVerbChars + 1), "low"));
        var pulled = new HeldContext(Pulled(pulledJson), NodeProof.None, Pulled: true) { Via = "patterns" };
        var row = ShowNetwork.Summaries([], peers.Speaking([pulled], T0), [], "desk01", T0).Rows.Single(r => r.Peer.Id == "pat9");
        var said = ShowNetwork.LinkLine(row, _ => null);
        Check(row is { Pulled: true, ContextProof: NodeProof.Key, Peer: { Verified: "key", Name: "Linked" } } && ShowNetwork.Mark(row.Peer.Verified) == "✓"
              && said.Contains("of its context, 1 verb not kept", StringComparison.Ordinal) && peers.VerdictFor(pat9, line, T0, pulled.Entry).Risk == "review",
            $"verified: the row shows the pulled context — its mark, what of it was not kept — and the risk reads the same table ({said})");
        Check(peers.Accept(Posted("pat6", "low"), "10.0.0.31", T0, "desk01", NodeCredential.None).Status == 200
              && peers.Speaking([new HeldContext(Pulled(Posted("pat6", "review")), NodeProof.None, Pulled: true)], T0).Single(c => c.Entry.Id == "pat6") is { Pulled: true, Proof: NodeProof.Key }
              && peers.VerdictFor(new NodeRef("patterns", "pat6", ""), line, T0, Pulled(Posted("pat6", "review"))).Risk == "review",
            "verified: a post that proves nothing never stands in front of a pulled context that proves its id");

        nodes.Keep([new ApiKey { Id = "kP", Name = "FOH", Scope = ApiKeyScope.Node, Hash = new string('a', 64) }]);
        Check(nodes.Speak(NodeCredential.Key("kP"), "patterns", "pat9", T0).Ok && peers.VerdictFor(pat9, line, T0, Pulled(Posted("pat9", "low"))) == RiskVerdict.Unlisted,
            "verified: once a key speaks for the id, neither the pulled context nor the operator's posted one counts");
        Check(peers.Accept(Posted("pat9", "review"), "10.0.0.5", T0, "desk01", NodeCredential.Key("kP")).Status == 200
              && peers.VerdictFor(pat9, line, T0, Pulled(Posted("pat9", "low"))).Risk == "review",
            "verified: the key's own context then sets the risk");
        Check(peers.Accept(Posted("pat7", "low"), "10.0.0.8", T0, "desk01", NodeCredential.None).Status == 200
              && peers.VerdictFor(new NodeRef("patterns", "pat7", ""), line, T0) == RiskVerdict.Unlisted && peers.VerdictFor(new NodeRef("patterns", "nobody1", ""), "GO", T0) == RiskVerdict.Unlisted,
            "verified: an unverified context, or none, rates every line high and lists none");
    }

    /// <summary>The desk's Show network row says what of a context this Stagewatch did not keep, beside how the context came.</summary>
    static void Readers(Action<bool, string> Check)
    {
        var commands = Enumerable.Range(0, PeerContexts.MaxCommands + 3).Select(i => "{\"verb\":\"V" + i.ToString(CultureInfo.InvariantCulture) + "\",\"risk\":\"low\"}")
            .Append("{\"verb\":\"" + new string('v', PeerContexts.MaxVerbChars + 1) + "\",\"risk\":\"low\"}").Prepend("{\"verb\":\"FOO [bar\",\"risk\":\"low\"}");
        var entry = PeerContexts.Parse(Context(Many("commands", commands) + Many("links", ["{\"from\":\"a\",\"to\":\"@ab\",\"kind\":\"feeds\"}"])), "10.0.0.9", T0)!
            with { Proof = NodeProof.Key, KeyId = "kPM" };
        var view = ShowNetwork.Summaries([], [new HeldContext(entry, NodeProof.Key)], [], "desk01", T0);
        var line = view.Rows is [var row] ? ShowNetwork.LinkLine(row, _ => "Hall") : "";
        Check(entry is { CommandsDropped: 5, LinksDropped: 1 }
              && line.Contains("posts its context with key 'Hall' · not linked on /ws · of its context, 5 verbs not kept (too long, malformed or past 256) — a line no kept verb takes reads high and 1 link not kept", StringComparison.Ordinal),
            $"readers: the Show network row says what of a context was not kept ({line})");
        var whole = PeerContexts.Parse(Context(Many("commands", ["{\"verb\":\"GO\",\"risk\":\"low\"}"])), "", T0)!;
        Check(ShowNetwork.NotKept(whole) == "" && ShowNetwork.NotKept(whole with { CommandsDropped = 1 }).Contains("1 verb not kept", StringComparison.Ordinal),
            "readers: a context kept whole says nothing more, and one verb is said as one");
    }

    /// <summary>
    /// A context refused as too big to read (413) is named on the Show network: on the row of the node its key is bound to, else on the
    /// one row at its address, while nothing it posted since was taken; any other is a row of its own apart, and the headline counts it.
    /// </summary>
    static void TooBig(Action<bool, string> Check)
    {
        var entry = PeerContexts.Parse(Context(Many("commands", ["{\"verb\":\"GO\",\"risk\":\"low\"}"])), "10.0.0.9", T0)!;
        var keyed = new TooBigContext("10.0.0.9", "playmap", "pm0badc0de", 70_211, T0.AddSeconds(1));
        var held = ShowNetwork.Summaries([], [new HeldContext(entry, NodeProof.Key)], [], "desk01", T0.AddSeconds(2), tooBig: [keyed]).Rows.Single();
        Check(held.TooBig == keyed && ShowNetwork.LinkLine(held, _ => "Hall").EndsWith(
                "posted a context too big to read (70211 bytes, over 65536) — not kept; the context shown here still sets its risk", StringComparison.Ordinal),
            $"too big: on the row of the node its key is bound to, beside the context it posted before ({ShowNetwork.LinkLine(held, _ => "Hall")})");
        var since = ShowNetwork.Summaries([], [new HeldContext(entry with { AtUtc = T0.AddSeconds(3) }, NodeProof.Key)], [], "desk01", T0.AddSeconds(4), tooBig: [keyed]);
        Check(since is { Rows: [{ TooBig: null }], TooBig.Count: 0 }, "too big: a context taken since says nothing more");

        var hello = new HeardNode("patterns", "pat9", "FOH", "10.0.0.5", 1);
        var byAddress = new TooBigContext("10.0.0.5", null, null, 70_000, T0);
        var one = ShowNetwork.Summaries([hello], [], [], "desk01", T0, tooBig: [byAddress]);
        Check(one.Rows is [{ TooBig: { Address: "10.0.0.5" } } r] && ShowNetwork.LinkLine(r, _ => null).EndsWith("— not kept, so its verbs read high", StringComparison.Ordinal)
              && ShowNetwork.NetworkLine(one, 0, 0).Contains("1 context too big to read", StringComparison.Ordinal),
            $"too big: with no key, on the one row at its address, its verbs high with no context held ({ShowNetwork.NetworkLine(one, 0, 0)})");
        var two = ShowNetwork.Summaries([hello, new HeardNode("playmap", "pm1abcde", "Hall", "10.0.0.5", 1)], [], [], "desk01", T0, tooBig: [byAddress, keyed]);
        Check(two.Rows.All(r => r.TooBig is null) && two.TooBig.Count == 2
              && ShowNetwork.TooBigRows(two.TooBig) is [{ Key: "post:10.0.0.5", NodeId: null, Name: "A show app at 10.0.0.5", Headline: ShowNetwork.TooBigHeadline },
                  { Key: "post:pm0badc0de", Kind: "playmap", Name: "PlayMap pm0badc0de", LinkLine: "10.0.0.9 · 70211 bytes, over 65536" }]
              && ShowNetwork.NetworkLine(two, 0, 0).Contains("2 contexts too big to read", StringComparison.Ordinal),
            "too big: an address two rows share, or a key's node with no row, is a row of its own apart, named by its node or its address");
        Check(ShowNetwork.Summaries([], [], [], "pm0badc0de", T0, tooBig: [keyed]).TooBig.Count == 0, "too big: nothing under this Stagewatch's own id");
    }

    /// <summary>PlayMap's written-in rows (its RiskWhen and HighFor), as its context will list them once it emits them: the design's table.</summary>
    static readonly (string Verb, string Risk)[] PlayMapDerived =
    [
        ("ASSISTANT SET enabled|provider <value>", "high"),
        ("DECODE POLICY AUTO|DIRECT|GPU-COPY|COPY-BACK|SOFTWARE FORCE", "high"),
        ("MEDIA OPTIMISE CANCEL [<job>|ALL]", "low"), ("MEDIA OPTIMISE PAUSE [<job>|ALL]", "low"), ("MEDIA OPTIMISE RESUME [<job>|ALL]", "low"),
        ("SEND ENABLE <send> ON", "review"),
        ("BIND LEARN OFF", "low"),
        ("TIMECODE SOURCE RESET <source>", "low"),
        ("MACHINE SET GPU-PRIORITY REALTIME", "high"),
    ];

    /// <summary>The design's probes on PlayMap's busy context with its written-in rows: the line, its risk, and whether it fills a secret.</summary>
    static readonly (string Line, string Risk, bool Secret)[] Probes =
    [
        ("MACHINE SET GPU-PRIORITY REALTIME", "high", false), ("MACHINE SET GPU-PRIORITY HIGH", "review", false),
        ("DECODE POLICY DIRECT FORCE", "high", false), ("DECODE POLICY DIRECT", "review", false),
        ("ASSISTANT SET enabled on", "high", false), ("ASSISTANT SET model x", "review", false),
        ("SEND ENABLE s1 ON", "review", false), ("SEND ENABLE s1 OFF", "high", false),
        ("MEDIA OPTIMISE CANCEL", "low", false), ("MEDIA OPTIMISE clip1", "review", false),
        ("BIND ADD note 60 TO BLACKOUT ON", "high", false), ("BIND ADD note 60 TO PLAYLIST CUE show #3", "review", false),
        ("BIND LEARN BLACKOUT ON", "high", false), ("BIND LEARN OFF", "review", false),
        ("MOVE it1 3", "low", false), ("MOVE it1 3 BLACKOUT ON", "low", false), ("UNDO TO 5 BLACKOUT ON", "review", false),
        ("PROJECT SAVE x BLACKOUT ON", "low", false),
        ("LINK STAGEWATCH host swk_abc", "review", true), ("LINK STAGEWATCH host ‹secret›", "review", false), ("LINK STAGEWATCH \"\" OFF", "review", true),
        ("LINK PATTERNS 10.0.0.5 tok123", "review", true),
        ("SET out1 name \"Left wall\"", "review", false), ("ADD media {\"path\": \"a b.mp4\"}", "review", false),
        ("SHOW SET REMOTE-VIEW-ONLY ON", "review", false), ("VIDEOIO SET NDI-GROUPS a,b", "review", false), ("MACHINE SET TIMER HIGH", "review", false),
        ("PANIC", "high", false), ("FOO BAR", "high", false), ("BLACKOUT", "high", false),
        ("BIND ADD {\"input\":\"note 60\",\"target\":\"BLACKOUT ON\"}", "high", false),
        ("PLAYLIST CUE show #3", "review", false), ("SCREEN 2 TAKE PVW LOOK x", "high", false), ("PLAYLIST CUE \"Main show\" #3", "review", false),
        ("PLAYLIST CUE Main show #3", "review", false),
        // The review's lines, each high by PlayMap's own RiskOf: a carried line PlayMap names by another name (VT, CLIP and TC are
        // VIDEO and TIMECODE) or by its first words alone, and a line of bindings whose carried lines PlayMap stops reading at 256.
        ("BIND ADD note 60 TO VT STOP", "high", false), ("BIND ADD note 60 TO CLIP PLAY", "high", false),
        ("BIND ADD note 60 TO TC ENGAGE pl1 ON", "high", false), ("BIND LEARN VT STOP", "high", false),
        ("BIND ADD note 60 TO BLACKOUT", "high", false), ("BIND ADD note 60 TO PANIC NOW", "high", false),
        (string.Concat(Enumerable.Repeat("BIND ADD x TO ", 4)) + string.Concat(Enumerable.Repeat("TO ", 32)) + "PLAYLIST CUE a b", "high", false),
    ];

    /// <summary>2.20's matcher, kept here to pin what it got wrong: a verb's words up to its first value, of the first 64 rows cut at 80.</summary>
    static string Legacy(IReadOnlyList<PeerCommand> commands, string line)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var best = (Len: 0, Risk: "high");
        foreach (var c in commands.Take(64))
        {
            var verb = c.Verb.Length > 80 ? c.Verb[..80] : c.Verb;
            var fixedWords = verb.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeWhile(w => !w.StartsWith('<') && !w.Contains('/')).ToArray();
            if (fixedWords.Length == 0 || fixedWords.Length > words.Length) continue;
            if (!fixedWords.Select((w, i) => w.Equals(words[i], StringComparison.OrdinalIgnoreCase)).All(x => x)) continue;
            if (fixedWords.Length > best.Len) best = (fixedWords.Length, c.Risk);
        }
        return best.Risk;
    }

    /// <summary>
    /// The golden replay: PlayMap's busy context (Mapper's golden, copied whole into fixtures/ with its source) is read whole, and each
    /// form of each row, written out with its options left out and taken, rates as its own row or higher, never lower — with x1 in
    /// every value (a carried x1 names no form, so a binding's example is high), and with each line value filled by a low line the
    /// table lists; with PlayMap's written-in rows, the only lines lower than their golden row are those rows' own words. The design's
    /// and the review's probes hold, the heaviest example is far inside the bound, and 2.20's matcher is pinned at what it got wrong.
    /// </summary>
    static void Golden(Action<bool, string> Check)
    {
        var master = ShowBusSpecTests.Master();
        var path = master is null ? null : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(master)!)!, "tests", "Stagewatch.Tests", "fixtures", "playmap-context-busy.json");
        Check(path is not null && File.Exists(path), "golden: PlayMap's busy context is in the suite's fixtures");
        if (path is null || !File.Exists(path)) return;
        var json = File.ReadAllText(path);
        Check(System.Text.Encoding.UTF8.GetByteCount(json) <= PeerContexts.MaxBytes, "golden: PlayMap's busy context fits a posted context's bytes");
        var entry = PeerContexts.Parse(json, "10.0.0.9", T0);
        Check(entry is { Kind: "playmap", Commands.Count: 199, CommandsDropped: 0, Items.Count: 33, Links.Count: 25, LinksDropped: 0 },
            $"golden: the reader keeps PlayMap's busy context whole: 199 commands, 33 items, 25 links ({entry?.Commands.Count}, {entry?.Items.Count}, {entry?.Links.Count}, dropped {entry?.CommandsDropped}/{entry?.LinksDropped})");
        if (entry is null) return;
        Check(entry.Commands.Max(c => c.Verb.Length) == 307 && entry.Commands.Max(c => c.Words.Length) == 403,
            "golden: its longest verb (307) and longest words (403) are kept whole, past 2.20's 80-character cut");

        var heaviest = 0;
        (int Total, int Same, List<string> Higher, List<string> Lower) Replay(VerbTable table, string? carried)
        {
            int total = 0, same = 0;
            List<string> higher = [], lower = [];
            foreach (var row in entry.Commands)
                foreach (var form in VerbSyntax.Forms(row.Verb))
                    foreach (var optional in new[] { false, true })
                    {
                        var line = VerbSyntax.Example(form, optional, carried);
                        var (verdict, steps, _) = table.Measure(line);
                        heaviest = Math.Max(heaviest, steps);
                        total++;
                        if (verdict.Risk == row.Risk) same++;
                        else (VerbTable.Rank(verdict.Risk) > VerbTable.Rank(row.Risk) ? higher : lower).Add(line);
                    }
            return (total, same, higher, lower);
        }

        // Every value x1: a carried x1 names no form of the table, so each form whose line value it fills rates high, and none lower.
        var golden = Replay(entry.Verbs, null);
        Check(golden is { Total: 504, Same: 496, Lower.Count: 0 } && golden.Higher.Count == 8 && golden.Higher.All(l => l.StartsWith("BIND ", StringComparison.Ordinal)),
            $"golden: of 504 form examples with x1 in every value, 496 rate as their row, 8 higher (a binding's carried x1, which names no form, and "
            + $"BIND ADD <json>) and none lower ({golden.Total}, {golden.Same}, higher {string.Join(" / ", golden.Higher)}, lower {string.Join(" / ", golden.Lower)})");
        // A line value filled with a low line the table lists: each form rates as its own row, but JSON in a row that carries lines.
        var low = entry.Commands.First(c => c.Risk == "low" && !c.Verb.Any(ch => ch is '<' or '[' or '|' or '…'));
        var filled = Replay(entry.Verbs, low.Verb);
        Check(filled is { Total: 504, Same: 502, Higher: ["BIND ADD x1", "BIND ADD x1"], Lower.Count: 0 },
            $"golden: with each line value filled by a listed low line ({low.Verb}), 502 rate as their row, 2 higher (BIND ADD <json>: JSON in a row "
            + $"that carries lines) and none lower ({filled.Total}, {filled.Same}, higher {string.Join(" / ", filled.Higher)}, lower {string.Join(" / ", filled.Lower)})");
        var derived = VerbTable.Compile(entry.Commands.Concat(PlayMapDerived.Select(d => new PeerCommand(d.Verb, d.Risk))));
        var withRows = Replay(derived, low.Verb);
        Check(withRows is { Total: 504, Same: 499, Higher.Count: 3, Lower.Count: 2 } && withRows.Lower.All(l => l.StartsWith("MEDIA OPTIMISE CANCEL", StringComparison.Ordinal))
              && withRows.Higher.Contains("DECODE POLICY AUTO FORCE"),
            "golden: with PlayMap's written-in rows, 499 rate as their row, 3 higher, and the 2 lower are MEDIA OPTIMISE CANCEL, a written-in row's own low "
            + $"({withRows.Same}, higher {string.Join(" / ", withRows.Higher)}, lower {string.Join(" / ", withRows.Lower)})");
        var wrong = Probes.Where(p => derived.Rate(p.Line) is var v && (v.Risk != p.Risk || v.Secret != p.Secret)).Select(p => $"{p.Line} → {derived.Rate(p.Line)}").ToList();
        Check(wrong.Count == 0, $"golden: the design's and the review's {Probes.Length} probes rate as measured on PlayMap's table with its written-in rows ({string.Join("; ", wrong)})");
        Check(heaviest <= VerbTable.MaxSteps / 100, $"golden: the heaviest form example takes under a hundredth of the bound ({heaviest} of {VerbTable.MaxSteps} steps)");
        Check(entry.Verbs.Rate("MACHINE SET GPU-PRIORITY REALTIME").Risk == "review" && entry.Verbs.Rate("DECODE POLICY DIRECT FORCE").Risk == "review",
            "golden: until PlayMap lists its written-in rows, a line whose risk PlayMap raises by a word rates by its row alone (named in WHATS_NEXT)");
        var firsts = entry.Commands.Where(c => c.Risk is "low" or "review").Select(c => (c.Risk, Line: VerbSyntax.Example(VerbSyntax.Forms(c.Verb)[0], false, low.Verb))).ToList();
        var legacyWrong = firsts.Count(f => Legacy(entry.Commands, f.Line) != f.Risk);
        var nowWrong = firsts.Count(f => entry.Verbs.Rate(f.Line).Risk != f.Risk);
        Check(firsts.Count == 144 && legacyWrong == 114 && nowWrong == 0,
            $"golden: of the 144 low and review rows' first forms, 2.20's matcher rated 114 wrong and the verb table none ({firsts.Count}, {legacyWrong}, {nowWrong})");
    }
}
