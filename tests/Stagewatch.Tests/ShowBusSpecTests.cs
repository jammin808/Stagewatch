using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// The Show Bus master (docs/SHOWBUS.md) read as data. Its body is held by a pinned hash; §13 PeerLink keeps its numbered
/// parts; the ```peerlink block sits inside the hashed body and every row parses. Each row's case is one 13.20 names; a
/// machine row's config, events and expected fields are the words 13.20's tables define, and the values it expects for a
/// state, light, show, refusal, tls, flag or outcome are in the closed sets §13's text names. The crypto rows hold with the
/// runtime's SHA-256 and HMAC alone — the labels, the transcript's lines, the code canon and the legacy key taken from the
/// text itself — so text and vectors agree before any tree copies them.
/// §14's ```showbus block is read as data too: every row is one §14.1 names, given once, with §14.2's units; every number §13
/// names in code is one of its rows, and every number of §13's own that §13 does not name is given its meaning in §14.4; the
/// vectors, the examples and the words §13 states with a number (13.10.3, 13.12, 13.14, 13.15, 13.16.1, 13.11.3) hold with
/// §14's numbers; the caps and kinds agree with each other and with §14.6's table. The header's Landed line, outside the body,
/// names the trees whose copies the hashes hold, and its list of rows that bind nothing yet is <see cref="Unworded"/>.
/// §12 Windows keeps its numbered parts, names every window and view row of §14 and no number that is not a row, and §1 rule 5
/// gives a view pass its two carriers as an address; what §12 says a window hears and reports, what its item is, and what a host
/// browser reads off it, is what Stagewatch's server, context and window shell do; the header names what of §12 2.19 brings, and §8's
/// Stagewatch row what no unit of 2.19 brings, each exactly while the code lacks it. Every part the body cites is there, or the header
/// names it as cited ahead of its words.
/// §6 keeps the Eye's rules, P1–P8, by their names; its link kinds and its kind table, and §14's kinds, are ShowBusKinds' tables; every
/// link row and link end the master writes is one §6 allows; §6's example keeps §6's own rules; posting a context answers in 13.5's
/// words; and 13.16 and 13.18 publish what P1 and P2 say. Stagewatch's own context keeps P1, P5 for its timer, P6 for its own parts but
/// its vMix link, and P8's no-secret rule; the desk keeps a posted context as §6 says; and §8's Stagewatch row names what of §6's rules
/// Stagewatch does not keep yet (<see cref="RulesNotKeptYet"/>), each while its code still does that.
/// §1–§11 hold 1.3's words. Each section names in code every row §14.4 gives it, and no dotted name but a row or one of the fields it
/// names; §2's example hello keeps §2's table and §14's kinds, ports, caps and sizes, and Stagewatch's hello sends only §2's fields; §3's
/// receipt is Stagewatch's, with its last four fields in order; §6's peers row and trimmed item are what Stagewatch writes and §6 allows;
/// §7.1's refusals and verdicts are NodeLinks' and NodeBindings' own words; §11's proposal and its states are ProposalDesk's, a field
/// appended after them allowed. §2's conflict holds a connector until its mark clears, in 13.15.3's words and 13.20's row. §8 gives each
/// §14 kind its row, with the parts §8 names; Stagewatch's row names its doors, its two exemptions and its stuckMs as its code has it,
/// and what it does not keep yet that a release after 2.19 ends or no unit is scheduled to end — every such row of
/// <see cref="NotKeptYet"/>, every entry of <see cref="WordsNotKeptYet"/> and <see cref="RulesNotKeptYet"/>, each in its words, and while
/// 1.3 is a draft nothing else — and the header names what 2.19 brings, each exactly while the code still lacks it.
/// Parse, pure and machine rows are otherwise checked for shape only here: <see cref="PeerLinkVectors"/> runs every row against Stagewatch's
/// core (src/Timer.Core/PeerLink.cs), and holds the §14 rows that core keeps. Of Stagewatch's own code:
/// a made key has 13.5.1's shape and its stored hash is K, and its level-0 proof makes every legacy row's proof and refuses
/// every nonce a row refuses (the tokens a row does not prove are not held here); of the rows of §2, §6, §10, §11 and §12 and those
/// 1.2's §3–§5 words, the ones its code keeps are held to their values where this suite reaches that code, the ones it does not keep
/// yet are <see cref="NotKeptYet"/>, and every other is named in <see cref="Unreached"/> or <see cref="NotStagewatchs"/>.
/// </summary>
static class ShowBusSpecTests
{
    /// <summary>
    /// SHA-256 of the master's body: from the line <see cref="BodyStart"/> to the end, UTF-8 without a BOM, every CRLF read as
    /// LF, one final LF. Any edit to the body changes it, so an edit is pinned here on purpose; the copies in Patterns,
    /// PlayMap and AGMessage hold the same hash, printed in the header, once 1.3 is final.
    /// </summary>
    const string BodySha256 = "93f043905d48f5159f9f5aa4df40ed8a1dc46c7cf4bac61531880e8926bf69f1";

    const string BodyStart = "## 1. Rules every node follows";
    const string ContextHeading = "## 6. Context — what an AI (or the Eye) needs to understand a node";

    /// <summary>§6's rules for the Eye, P1–P8 in order, by their names. Readers in four trees cite them by number (§6's P2), so none moves.</summary>
    static readonly string[] EyeRules =
    [
        "Items are the node's own", "Every PeerLink is one `link` item and one row", "What a node shows from another node is its claim", "Grants",
        "Show liveness", "Kinds", "Order", "Content",
    ];

    /// <summary>The planes §6's kind table may name: the Eye's bands, each one of its lenses.</summary>
    static readonly string[] Planes = ["control", "video", "audio", "room"];

    /// <summary>Dotted names in §6 that are a context's fields, not numbers.</summary>
    static readonly string[] ContextFields = ["node.t", "node.id"];

    /// <summary>
    /// The context rows §6 does not name yet: none, now that 1.3's words for §6 give its sizes. A row added to §14 for §6 is named in §6's
    /// words before it binds, and 1.3 final has none here.
    /// </summary>
    static readonly string[] SizesLater = [];

    /// <summary>
    /// What of §6's rules for the Eye Stagewatch's own context does not keep yet: the rule, what of it, the unit that ends it, what
    /// the code does today in §8's Stagewatch row's words, and a check that holds exactly while the code still does that. Once the code
    /// changes the check fails, and the entry leaves this table for a check of the rule kept (the row's words, frozen with 1.3, still
    /// say what 2.19 did).
    /// </summary>
    static readonly (string Rule, string Of, string Unit, string Today, Func<bool> Differs)[] RulesNotKeptYet =
    [
        ("P2", "for its links", "SW24.1", "its only rows run from its timer, its Patterns link is an item of kind `playout`, and no row names a peer",
            OwnRowsAsToday),
        ("P2", "for the node links it accepts on `/ws`", "SW24.1", "it lists them only as `peers` rows, with `link` and `verified`", AcceptedLinksAsToday),
        ("P3", "for the clip time it shows from Patterns", "SW24.1", "its screens show Patterns' clip time, and no row names Patterns' item",
            ClipTimeAsToday),
        ("P5", "for its schedule", "SW24.1", "a stuck show thread turns its timer red and leaves its schedule's light as the frame gives it",
            StuckScheduleAsToday),
        ("P6", "for its vMix link", "SW24.1", "an item of kind `playout`", VmixAsToday),
        ("P7", "for its order", "SW24.1", "its timer is its first item whatever its light", TimerFirstAsToday),
        ("P8", "for its messages", "SW24.1",
            "a cue item carries the text of a message it sent, a speaker message sent with an ack among them, and `recent` carries the text "
            + "of every message and audience question its journal holds",
            MessagesAsToday),
    ];

    const string WindowsHeading = "## 12. Windows (1.3)";

    /// <summary>§12's parts in order. Patterns and PlayMap cite them by number (12.5's path rule, 12.9's black), so none moves.</summary>
    static readonly string[] WindowParts =
    [
        "What a window is", "View passes", "The window link", "Grants", "Where a grant's address travels", "Messages on a node link",
        "The black end", "What a holder publishes", "Drawing a held window", "Placement claims and the granting node's readers",
    ];

    /// <summary>The rows §14.4 gives §12.</summary>
    static readonly string[] WindowRows = ["window.", "view."];

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

    const string NumbersHeading = "## 14. The numbers (1.3)";

    /// <summary>Dotted names in §13 that are names, not numbers: PlayMap's WebSocket subprotocol.</summary>
    static readonly string[] NotNumbers = ["playmap.lines.v1"];

    /// <summary>The rows §14.1 names, by the first word of each row of its table (a number's is <c>&lt;name&gt;</c>).</summary>
    static readonly string[] RowWords = ["revision", "proto", "<name>", "kind", "port", "cap", "reserved", "peerlink.feature"];

    /// <summary>The numbers whose rows §14.4 gives to §2, §6, §10, §11 and §12; every other number is §3–§5's and §13's own.</summary>
    static readonly string[] OtherSections = ["hello.", "peers.", "peer.", "context.", "cert.", "proposal.", "window.", "view."];

    /// <summary>The numbers this test computes with: without each of them, it stops after saying which are missing.</summary>
    static readonly string[] Computed =
    [
        "peerlink.version", "peerlink.nonce.bytes", "peerlink.kid.bytes", "peerlink.lid.bytes", "peerlink.can.words", "peerlink.key.bits.min",
        "peerlink.hold.refused.ms", "peerlink.redial.jitter.pct", "peerlink.stable.ms", "link.stale.ms", "link.drop.ms", "link.redial.first.ms",
        "link.redial.max.ms", "link.inflight.max", "link.read.min.bytes", "clock.window", "clock.apart.ms", "clock.rtt.max.ms", "clock.fresh.ms",
        "receipt.final.ms", "request.id.chars", "hello.port", "hello.caps", "hello.cap.chars", "hello.id.min", "hello.id.max", "peers.max",
        "context.bytes", "context.kept.ms", "context.items", "context.links", "context.commands", "context.peers", "context.recent", "context.recent.chars",
        "context.headline.chars", "context.item.id.chars", "context.item.kind.chars", "context.item.label.chars", "context.item.state.chars",
        "context.verb.chars", "proposal.undelivered.ms", "proposal.result.ms", "window.token.chars", "window.max", "hello.bytes", "hello.ports",
        "hello.name.chars", "hello.inst.chars", "hello.kind.chars", "show.stuck.min.ms", "show.stuck.max.ms", "peer.stale.ms", "peer.conflict.clear.ms",
        "peer.addresses",
    ];

    /// <summary>
    /// The rows the header says bind nothing until their section's words land: none, now that §1–§11 have 1.3's words. A row added to
    /// §14 before its section's words is named here and in the header until they land, and 1.3 final has none.
    /// </summary>
    static readonly string[] Unworded = [];

    /// <summary>The dotted names §1–§11 write in code that are fields of a message, not §14 rows.</summary>
    static readonly string[] Fields = ["ports.https", "video.seq", "clip.seq", "node.t", "node.id"];

    /// <summary>The §14 rows each of §2–§11 names in its words, by its section's number (§6's and §12's have checks of their own).</summary>
    static readonly (int Section, string[] Rows)[] SectionRows =
    [
        (2, ["hello.", "peers.", "peer."]),
        (3, ["request.id.chars", "receipt.deadline.ms", "receipt.final.ms", "receipt.late.ms"]),
        (4, ["link.beat.ms", "link.stale.ms", "link.drop.ms", "link.redial.first.ms", "link.redial.max.ms", "ping.min.ms", "ping.max.ms"]),
        (5, ["clock.window", "clock.apart.ms"]),
        (10, ["cert."]),
        (11, ["proposal."]),
    ];

    /// <summary>What each of §8's parts (one per §14 kind) says of its node, by its bold labels, in this order.</summary>
    static readonly string[] RowParts = ["Scopes", "Show writer", "Doors that are not PeerLinks", "Named exemptions", "Not kept yet"];

    /// <summary>The fields §3 appends to a receipt, in their order.</summary>
    static readonly string[] ReceiptTail = ["final", "repeat", "lateMs", "frame"];

    /// <summary>The rows of §13's own numbers that 1.2's §3–§5 already state in words (§4's liveness and redial, §5's clock, §3's ids).</summary>
    static readonly string[] Before13 =
    [
        "link.beat.ms", "link.stale.ms", "link.drop.ms", "link.redial.first.ms", "link.redial.max.ms", "ping.min.ms", "ping.max.ms",
        "clock.window", "clock.apart.ms", "receipt.deadline.ms", "request.id.chars",
    ];

    /// <summary>
    /// The rows Stagewatch's code does not keep yet, of those §14.4 gives §2, §6, §10, §11 and §12 and those 1.2's §3–§5 words
    /// (<see cref="Before13"/>): the row, the unit that ends it, what the code does today, and a check that holds while the code still
    /// differs (null where the suite reaches no code that could keep the row). Once the code keeps a row its check fails, and the row
    /// leaves this table for a check in <see cref="TodaysCode"/>. The header names the rows a unit of 2.19 ends (<see cref="In219"/>),
    /// which 2.19 keeps before it ships 1.3; §8's Stagewatch row names the rest, each in its Today words.
    /// </summary>
    static readonly (string Row, string Unit, string Today, Func<Numbers, bool>? Differs)[] NotKeptYet =
    [
        ("link.stale.ms", "SW20.4a", "PatternsLink marks no link stale: a clip shows no time 3 s after its last frame, and the link drops after 12 s", null),
        ("link.redial.first.ms", "SW20.4a", $"PatternsLink waits {PatternsLink.RedialFirstMs} ms before its first redial, and never jitters",
            n => PatternsLink.RedialFirstMs != n.Of("link.redial.first.ms")),
        ("context.items.first", "SW24.1", "its timer is its first item whatever its light, ahead of a red or amber one", _ => TimerFirstAsToday()),
        ("context.items", "SW21.1", "PeerContexts keeps a context's first 64 items",
            n => Peer(Many("items", "{\"id\":\"a\",\"kind\":\"b\",\"label\":\"c\",\"light\":\"green\",\"state\":\"d\"}", n.Of("context.items")))?.Items.Count < n.Of("context.items")),
        ("context.commands", "SW21.1", "PeerContexts keeps a context's first 64 commands",
            n => Peer(Many("commands", "{\"verb\":\"go\",\"risk\":\"low\"}", n.Of("context.commands")))?.Commands.Count < n.Of("context.commands")),
        ("context.verb.chars", "SW21.1", "PeerContexts cuts a verb at 80 characters, where a reader keeps a verb whole or drops it",
            n => Peer(Many("commands", "{\"verb\":\"" + new string('v', (int)n.Of("context.verb.chars")) + "\",\"risk\":\"low\"}", 1)) is { } c
                 && c.Commands.Count == 1 && c.Commands[0].Verb.Length < n.Of("context.verb.chars")),
        ("context.links", "SW21.1", "PeerContexts reads no links", _ => typeof(PeerContexts.Entry).GetProperty("Links") is null),
        ("context.link.end.chars", "SW21.1", "PeerContexts reads no links", _ => typeof(PeerContexts.Entry).GetProperty("Links") is null),
        ("context.words.read.chars", "SW21.1", "PeerContexts reads no command words", null),
        ("proposal.line.chars", "SW21.2", $"ProposalDesk takes a line of {ProposalDesk.MaxLine} characters and cuts a longer one, which can run another command",
            n => ProposalDesk.MaxLine < n.Of("proposal.line.chars")),
        ("proposal.collect.ms", "SW21.2", "the desk sends and approves for a target whatever its last collect",
            _ => typeof(ProposalDesk).GetMethods().All(m => !m.Name.Contains("Collected", StringComparison.Ordinal))),
        ("receipt.final.ms", "SW22.1c", "its receipts carry no `final`, `repeat`, `lateMs` or `frame`", _ => ReceiptTailAsToday()),
        ("window.grants", "SW22.3", "it grants no window, so no grant's address travels", null),
    ];

    /// <summary>Whether a unit ends its entry in 2.19, the release that carries 1.3: then the header names it, else §8's Stagewatch row.</summary>
    static bool In219(string unit) => unit.StartsWith("SW19.", StringComparison.Ordinal);

    /// <summary>
    /// What else of §1–§11 and §12 Stagewatch does not keep yet, past what a §14 row names: §8's Stagewatch row's words for it, the unit
    /// that ends it (each after 2.19, or "not scheduled"), what the code does today in the row's words, and a check that holds exactly
    /// while the code still does that. Once the code changes its check fails, and the entry leaves this table for a check of the words
    /// kept. Nothing no unit of 2.19 ends is named in the header, which says only what 2.19 brings.
    /// </summary>
    static readonly (string What, string Unit, string Today, Func<bool> Differs)[] WordsNotKeptYet =
    [
        ("13.11's clock quality", "SW20.4a", "CLOCKS APART reads the offset alone, with no error, freshness or fitness",
            () => typeof(ClockOffsetEstimator).GetMembers().All(m => !Regex.IsMatch(m.Name, "Err|Fresh|Fit"))),
        ("its Patterns link proves at level 0 only", "SW23.1", "it answers NONCE with AUTH-PROOF and runs no LINK",
            () => Source("src/Timer.Integrations/PatternsLink.cs") is { } link && link.Contains("\"AUTH-PROOF \"", StringComparison.Ordinal)
                  && !Regex.IsMatch(link, @"""(?:@l\d+ )?LINK ")),
        ("no playout link to PlayMap", "SW20.4a", "its Patterns link is its only line connector",
            () => typeof(PatternsLink).Assembly.GetTypes().Where(t => t.Name.EndsWith("Link", StringComparison.Ordinal)).Select(t => t.Name)
                  .SequenceEqual(["PatternsLink"])),
        ("its `/ws` speaks no LINK", "SW22.1a", "a node link proves a bearer key or the operator's credential, §7.1",
            () => Source("src/Timer.Core/NodeLinks.cs") is { } links && !links.Contains("snonce", StringComparison.Ordinal)
                  && Source("src/Timer.Remote/WebServer.cs") is { } server && !server.Contains("snonce", StringComparison.Ordinal)),
        ("13.6.9's mark on a key seen in clear", "SW22.1b", "it takes a key sent in clear from another machine as it takes any other, and marks nothing",
            SeenInClearAsToday),
        ("its risk by whole forms", "SW21.1", "it rates a line by the longest run of a listed verb's leading words",
            () => ProposalDesk.RiskFromCommands([("pause", "low")], "pause every screen for good") == "low"),
        ("its proposal ids", "SW21.2", "they restart with each desk run, `p<n>`, so a target pairs an id with `created`", ProposalIdsAsToday),
        ("a `from` of kind `main` or `mcp`", "SW21.2", "the desk takes one from the network unless it names the desk's own id",
            () => Proposed("{\"from\":{\"t\":\"main\",\"id\":\"b2c3d4e5f6a7\",\"name\":\"Hall B\"},\"target\":{\"t\":\"main\"},\"line\":\"note hi\"}", "low") is not null),
        ("a proposal sent with an `mcp` key", "SW21.2", "the desk queues one for a node no key is bound to, proved by nothing", McpProposalAsToday),
        ("§11's line refusals", "SW21.2", "the desk takes a line that holds a key, a window's address or a filled secret placeholder, and drops its control characters",
            () => Proposed("{\"from\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"FOH\"},\"target\":{\"t\":\"main\"},\"line\":\"note swk_abcdefghijkmnpqrstuvwxyz23456789\"}", "low") is not null),
        ("a target's raise of its own proposal's risk", "SW21.2", "the desk takes a proposal's risk from the target's table alone",
            () => Proposed("{\"from\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"FOH\"},\"target\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\"},\"line\":\"CUE GO\",\"risk\":\"high\"}", "low") is { Risk: "low" }),
        ("its commands rows in §6's notation", "SW21.2", "it lists `start / pause` and `patterns end 10`",
            () => ShowContext.Commands.Any(c => c.Verb == "start / pause") && ShowContext.Commands.Any(c => c.Verb == "patterns end 10")),
        ("a line a person sends to another app", "SW21.4", "it has no `peer` or `desk` verb",
            () => !Enum.GetNames<ShowCommandKind>().Any(k => k is "Peer" or "Desk")),
        ("verified peers' `recent` in its report", "SW21.7", "its report holds its own journal alone",
            () => Source("src/Timer.Show/ShowReports.cs") is { } reports && !reports.Contains("Peer", StringComparison.Ordinal)),
        ("an over-long context's `413`", "SW20.9", "a context over `context.bytes` closes its connection with no answer",
            () => Source("src/Timer.Remote/WebServer.cs") is { } server && server.Contains("if (length > MaxBody) return null;", StringComparison.Ordinal)),
        ("12.2's page rule for a page opened with a viewing code or a crew link", "not scheduled",
            "it serves those pages without the window page's no-referrer, noindex and no-framing headers", PageRuleAtWindowsOnly),
        ("12.2's revocation at once for a viewing code or a crew link", "not scheduled", "a screen already open on a changed one keeps its link until it reconnects",
            ViewersNeverRechecked),
    ];

    /// <summary>
    /// What else of §1–§11 Stagewatch does not keep yet past a row, which 2.19 brings: the header's words for it, the unit of 2.19 that
    /// ends it, what the code does today, and a check that holds exactly while the code still does that. The header names each exactly
    /// while its check holds. Empty: every rule of §1–§11 that 2.19 brings is kept (§6's order of commands is held in
    /// <see cref="ContextCode"/>, §6's sizes and trimming in <see cref="OwnContextSizes"/>).
    /// </summary>
    static readonly (string What, string Unit, string Today, Func<bool> Differs)[] NotKept219 = [];

    /// <summary>
    /// Rows of those sections that Stagewatch's code bears on but this suite does not reach yet, with where that code is and the unit
    /// that brings it in: neither kept nor not kept is claimed here. None, now that the hello is built by ShowBusHello, which the suite
    /// builds, and the desk only calls it.
    /// </summary>
    static readonly (string Row, string Where)[] Unreached = [];

    /// <summary>Rows of those sections that bind no code of Stagewatch's: a role it does not play.</summary>
    static readonly (string Row, string Why)[] NotStagewatchs =
    [
        ("context.post.min.ms", "Stagewatch posts its context to no desk: it is the desk"),
        ("context.post.max.ms", "Stagewatch posts its context to no desk: it is the desk"),
    ];

    public static void Run(Action<bool, string> Check)
    {
        var path = Master();
        Check(path is not null, "showbus spec: docs/SHOWBUS.md found above the suite");
        if (path is null) return;
        if (Body(Check, File.ReadAllBytes(path)) is not { } read) return;
        var (text, body) = read;

        Sections(Check, body);
        var s6 = Between(body, "\n" + ContextHeading + "\n", "\n## ");
        var s12 = Between(body, "\n" + WindowsHeading + "\n", "\n## ");
        var s13 = Between(body, "\n" + PeerLinkHeading + "\n", "\n## ");
        var s14 = Between(body, "\n" + NumbersHeading + "\n", "\n## ");
        Check(s6 is not null && s12 is not null && s13 is not null && s14 is not null,
            "showbus spec: §6 Context, §12 Windows, §13 PeerLink and §14 The numbers are in the hashed body");
        if (s6 is null || s12 is null || s13 is null || s14 is null) return;
        var draft = text.Split('\n')[0].Contains("(draft)", StringComparison.Ordinal);
        Landed(Check, text, body, s14, draft);
        Parts(Check, s13);
        WindowsText(Check, body, s12);
        References(Check, text[..^body.Length], body, draft);
        Doors(Check, s13);
        var rows = Block(Check, text, body, s13);
        if (rows is not null) Rows(Check, s13, rows);
        if (ReadNumbers(Check, text, body, s14, draft) is not { } n) return;
        var missing = Computed.Where(c => n.Of(c) < 0).ToList();
        Check(missing.Count == 0, "showbus spec: §14 gives every number this test computes with (" + string.Join(", ", missing) + ")");
        if (missing.Count > 0) return;
        Vocabulary(Check, text, s13, s14, n);
        Named(Check, s13, s14, n);
        WindowsNamed(Check, s12, n);
        ContextNamed(Check, s6, n, draft);
        ContextText(Check, body, s6, n);
        if (rows is not null)
        {
            Crypto(Check, s13, rows, n);
            Agreement(Check, s13, rows, n);
        }
        TodaysCode(Check, s13, rows ?? [], n);
        WindowsCode(Check, text[..^body.Length], s12, n);
        ContextCode(Check, s6, n);
        Words(Check, body, n);
        WordsCode(Check, body, n);
        NodeRows(Check, body, s13, n, draft);
        Brings(Check, text[..^body.Length]);
    }

    // ------------------------------------------------------------------ the body and its hash

    internal static string? Master()
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
        Check(Enumerable.Range(1, 14).All(numbers.Contains) && numbers[^1] == 14, "showbus spec: §1–§14 are in the body, §14 last (" + string.Join(", ", numbers) + ")");
        Check(Regex.Matches(body, "^" + Regex.Escape(WindowsHeading) + "$", RegexOptions.Multiline).Count == 1, "showbus spec: §12 is Windows, once");
        Check(Regex.Matches(body, "^" + Regex.Escape(PeerLinkHeading) + "$", RegexOptions.Multiline).Count == 1, "showbus spec: §13 is PeerLink, once");
        Check(Regex.Matches(body, "^" + Regex.Escape(NumbersHeading) + "$", RegexOptions.Multiline).Count == 1, "showbus spec: §14 is The numbers, once");
    }

    /// <summary>
    /// The header's one Landed line names, from §14.5's repository names, the trees whose copies the hashes hold, each once, the
    /// master's own first; it stays outside the body, so it grows without changing the hash. The header's rows that bind nothing yet
    /// are <see cref="Unworded"/>, and none once 1.3 is final.
    /// </summary>
    static void Landed(Action<bool, string> Check, string text, string body, string s14, bool draft)
    {
        var header = text[..^body.Length];
        var copies = Between(s14, "5. **Copies.**", "\n6. **") ?? "";
        var trees = Regex.Match(copies, @"by repository name, from\s+\(([^)]+)\)") is { Success: true } named ? Regex.Split(named.Groups[1].Value, @",\s+") : [];
        Check(trees.Length > 0 && trees[0] == "Stagewatch" && trees.Distinct(StringComparer.Ordinal).Count() == trees.Length,
            "showbus spec: §14.5 names the trees by repository name, each once, Stagewatch first (" + string.Join(", ", trees) + ")");
        var lines = Regex.Matches(header, @"^\*Landed \(§14\): (.+)\.\*$", RegexOptions.Multiline);
        Check(lines.Count == 1, $"showbus spec: the header has one '*Landed (§14): …*' line ({lines.Count})");
        if (lines.Count == 1)
        {
            var landed = lines[0].Groups[1].Value.Split(", ");
            Check(landed[0] == "Stagewatch" && landed.All(trees.Contains) && landed.Distinct(StringComparer.Ordinal).Count() == landed.Length,
                "showbus spec: the Landed line names §14.5's trees, each once and comma-separated, Stagewatch first (" + lines[0].Groups[1].Value + ")");
        }
        Check(!Regex.IsMatch(body, @"^\*Landed", RegexOptions.Multiline) && copies.Contains("`*Landed (§14): …*`", StringComparison.Ordinal),
            "showbus spec: the Landed line is the header's, outside the body, and §14.5 names it");

        var unworded = Regex.Match(header, @"binds nothing until its section's words land: (.+?\))\.", RegexOptions.Singleline) is { Success: true } u
            ? Regex.Matches(u.Groups[1].Value, "`([a-z][a-z0-9.]*)`").Select(m => m.Groups[1].Value).ToList() : [];
        Check(unworded.Order(StringComparer.Ordinal).SequenceEqual(Unworded.Order(StringComparer.Ordinal)),
            "showbus spec: the header's rows that bind nothing yet are ShowBusSpecTests.Unworded (header: " + string.Join(", ", unworded.Except(Unworded))
            + "; pinned: " + string.Join(", ", Unworded.Except(unworded)) + ")");
        Check(draft || Unworded.Length == 0, "showbus spec: 1.3 final leaves no row that binds nothing (" + string.Join(", ", Unworded) + ")");
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

    // ------------------------------------------------------------------ §14's block

    /// <summary>§14's block, read: its revision and proto, the numbers by name, and the rows that list words.</summary>
    sealed record Numbers(string Revision, long Proto, Dictionary<string, string> Values, List<(string T, string Name)> Kinds,
        List<string> Ports, List<(string Word, string[] Kinds)> Caps, List<(string Word, string Kind)> Reserved, List<string> Features)
    {
        /// <summary>A whole number's value; -1 when the block gives none (a failure already says so).</summary>
        public long Of(string name)
            => Values.TryGetValue(name, out var v) && long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
    }

    /// <summary>
    /// The one ```showbus block, inside §14 and so inside the hashed body. Each row is one §14.1 names: printable ASCII, words one
    /// space apart, no empty line; a number named once, with a whole value but for the three §14.1 names (the peer's hash may read
    /// <c>pending</c> only while the title says draft); a kind, port, cap, reserved word and feature each named once.
    /// </summary>
    static Numbers? ReadNumbers(Action<bool, string> Check, string text, string body, string s14, bool draft)
    {
        var fences = Regex.Matches(text, "^```showbus$", RegexOptions.Multiline);
        Check(fences.Count == 1, "showbus spec: one ```showbus block in the master");
        var inner = Between(s14, "\n```showbus\n", "\n```");
        Check(inner is not null && fences.Count == 1 && fences[0].Index >= text.Length - body.Length, "showbus spec: the ```showbus block is in §14, inside the hashed body");
        if (inner is null) return null;
        var lines = inner["\n```showbus\n".Length..].Split('\n');
        var unclean = lines.Where(l => l.Length == 0 || l.Any(c => c is < ' ' or > '~') || l.StartsWith(' ') || l.EndsWith(' ') || l.Contains("  ", StringComparison.Ordinal)).ToList();
        Check(unclean.Count == 0, "showbus spec: every §14 row is printable ASCII with its words one space apart, and no line is empty (" + string.Join(" · ", unclean) + ")");

        string? revision = null;
        long proto = -1;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var kinds = new List<(string, string)>();
        var ports = new List<string>();
        var caps = new List<(string, string[])>();
        var reserved = new List<(string, string)>();
        var features = new List<string>();
        var bad = new List<string>();
        const string Kind = "^[a-z0-9_-]{1,16}$", Word = @"^[a-z0-9-]+(?:\.[a-z0-9-]+)*$";
        foreach (var line in lines)
        {
            var w = line.Split(' ');
            switch (w[0])
            {
                case "revision" when w.Length == 2 && revision is null && Regex.IsMatch(w[1], @"^[1-9][0-9]*\.[0-9]+$"):
                    revision = w[1];
                    break;
                case "proto" when w.Length == 2 && proto < 0 && Whole(w[1]) is { } p:
                    proto = p;
                    break;
                case "kind" when w.Length >= 3 && Regex.IsMatch(w[1], Kind):
                    kinds.Add((w[1], string.Join(' ', w[2..])));
                    break;
                case "port" when w.Length == 2 && Regex.IsMatch(w[1], Kind):
                    ports.Add(w[1]);
                    break;
                case "cap" when w.Length >= 3 && Regex.IsMatch(w[1], Word) && w[2..].All(k => Regex.IsMatch(k, Kind)):
                    caps.Add((w[1], w[2..]));
                    break;
                case "reserved" when w.Length == 3 && Regex.IsMatch(w[1], Word) && Regex.IsMatch(w[2], Kind):
                    reserved.Add((w[1], w[2]));
                    break;
                case "peerlink.feature" when w.Length == 2 && Regex.IsMatch(w[1], "^[a-z-]+$"):
                    features.Add(w[1]);
                    break;
                default:
                    if (!(w.Length == 2 && w[0] != "peerlink.feature" && Regex.IsMatch(w[0], @"^[a-z][a-z0-9]*(?:\.[a-z0-9]+)+$")
                          && NumberValue(w[0], w[1], draft) && values.TryAdd(w[0], w[1])))
                        bad.Add(line);
                    break;
            }
        }
        Check(bad.Count == 0, "showbus spec: every §14 row is one §14.1 names, well formed, a number given once (" + string.Join(" · ", bad) + ")");
        Check(revision is not null && proto >= 0, "showbus spec: §14 gives its revision and proto, once each");
        static bool Once(IEnumerable<string> words) => words.GroupBy(x => x, StringComparer.Ordinal).All(g => g.Count() == 1);
        Check(Once(kinds.Select(k => k.Item1)) && Once(ports) && Once(caps.Select(c => c.Item1).Concat(reserved.Select(r => r.Item1))) && Once(features),
            "showbus spec: §14 names each kind, port, cap or reserved word, and feature once");
        return revision is null ? null : new Numbers(revision, proto, values, kinds, ports, caps, reserved, features);
    }

    /// <summary>A number's value as §14.1 has it: a whole number, but for the label, the window alphabet and the peer's hash.</summary>
    static bool NumberValue(string name, string value, bool draft) => name switch
    {
        "peerlink.label" => Regex.IsMatch(value, "^[a-z0-9-]+$"),
        "window.token.alphabet" => Regex.IsMatch(value, "^[a-z0-9]+$") && value.Distinct().Count() == value.Length,
        "peerlink.peer.sha256" => Hex64(value) || (draft && value == "pending"),
        _ => Whole(value) is not null,
    };

    static long? Whole(string s)
        => Regex.IsMatch(s, "^(?:0|[1-9][0-9]{0,17})$") ? long.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture) : null;

    /// <summary>
    /// The block against the rest of the text: §14.1's table names the block's rows; the revision is the title's and proto its minor
    /// number; caps name only kinds the block gives, a dotted word stands under the word before its dot, each kind sends some cap, and
    /// every cap a kind may send fits one hello; §14.6 gives each cap word its meaning; the features are 13.1's; a name holds §14.2's
    /// units; and the bounds hold in the order their sections need (a reader takes what a sender may send, a beat comes before a link
    /// goes stale, a link goes stale before it goes down, a posted context outlives the wait for the next post).
    /// </summary>
    static void Vocabulary(Action<bool, string> Check, string text, string s13, string s14, Numbers n)
    {
        var rowWords = FirstCells(s14, "row").Select(c => c.Split(' ')[0]).ToList();
        Check(rowWords.SequenceEqual(RowWords), "showbus spec: §14.1's table names the rows the block holds (" + string.Join(", ", rowWords) + ")");
        var title = text.Split('\n')[0];
        var minor = n.Revision.Split('.')[1];
        Check(title.StartsWith("# Show Bus " + n.Revision + " ", StringComparison.Ordinal) && n.Proto.ToString(CultureInfo.InvariantCulture) == minor,
            $"showbus spec: §14's revision is the title's and its proto that revision's (revision {n.Revision}, proto {n.Proto})");

        var kinds = n.Kinds.Select(k => k.T).ToHashSet(StringComparer.Ordinal);
        var words = n.Caps.Select(c => c.Word).ToHashSet(StringComparer.Ordinal);
        var strays = n.Caps.SelectMany(c => c.Kinds).Concat(n.Reserved.Select(r => r.Kind)).Where(k => !kinds.Contains(k)).Distinct().ToList();
        Check(strays.Count == 0, "showbus spec: a cap or reserved row names only kinds §14 gives (" + string.Join(", ", strays) + ")");
        var orphans = n.Caps.Select(c => c.Word).Concat(n.Reserved.Select(r => r.Word)).Where(w => w.Contains('.') && !words.Contains(w[..w.IndexOf('.')])).ToList();
        Check(orphans.Count == 0, "showbus spec: a dotted cap is a finer ability of a cap word (" + string.Join(", ", orphans) + ")");
        var mute = kinds.Where(k => !n.Caps.Any(c => c.Kinds.Contains(k))).ToList();
        Check(mute.Count == 0, "showbus spec: every kind sends some cap (" + string.Join(", ", mute) + ")");
        var longWords = n.Caps.Select(c => c.Word).Concat(n.Reserved.Select(r => r.Word)).Where(w => w.Length > n.Of("hello.cap.chars")).ToList();
        Check(longWords.Count == 0, "showbus spec: every cap word is within hello.cap.chars (" + string.Join(", ", longWords) + ")");
        var crowded = kinds.Where(k => n.Caps.Count(c => c.Kinds.Contains(k)) + n.Reserved.Count(r => r.Kind == k) > n.Of("hello.caps")).ToList();
        Check(crowded.Count == 0, "showbus spec: every cap a kind may send fits in one hello's hello.caps (" + string.Join(", ", crowded) + ")");
        var meant = FirstCells(s14, "cap").Select(c => c.Split(' ')[0]).ToList();
        var vocabulary = n.Caps.Select(c => c.Word).Concat(n.Reserved.Select(r => r.Word)).ToList();
        Check(meant.Order(StringComparer.Ordinal).SequenceEqual(vocabulary.Order(StringComparer.Ordinal)),
            "showbus spec: §14.6 gives a meaning to each cap and reserved word, and to no other (unmeant: " + string.Join(", ", vocabulary.Except(meant))
            + "; unknown: " + string.Join(", ", meant.Except(vocabulary)) + ")");
        Check(n.Ports.Contains("http") && n.Ports.Contains("ws") && n.Ports.Contains("wire") && kinds.SetEquals(["main", "agent", "patterns", "playmap", "messenger"]),
            "showbus spec: §14's ports hold the doors §2 names, and its kinds are the five nodes of this contract");

        var featureLine = Line(Part(s13, 1) ?? "", "**Feature**") ?? "";
        var named = Ticked(Between(featureLine, "list:", ".") ?? "").ToList();
        Check(n.Features.SequenceEqual(named), "showbus spec: §14's features are 13.1's, in its order (" + string.Join(", ", n.Features) + " / " + string.Join(", ", named) + ")");
        Check(n.Features.Count <= n.Of("peerlink.can.words"), "showbus spec: every feature fits one verdict's can (peerlink.can.words)");

        var units = Between(s14, "2. **Units.**", "\n3. **") ?? "";
        var unitWords = Regex.Matches(units, "`([a-z0-9]+)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var otherUnits = Regex.Matches(units, @"`([a-z][a-z0-9]*(?:\.[a-z0-9]+)+)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Check(unitWords.SetEquals(["ms", "hours", "days", "bytes", "chars", "bits", "pct"]), "showbus spec: §14.2 names seven unit words (" + string.Join(", ", unitWords) + ")");
        int Units(string name) => name.Split('.').Count(unitWords.Contains);
        static bool PerMinute(string name) => name.EndsWith(".per.minute", StringComparison.Ordinal);
        var doubled = n.Values.Keys.Where(k => Units(k) > 1 || (PerMinute(k) && Units(k) > 0)).ToList();
        Check(doubled.Count == 0, "showbus spec: a §14 name holds one unit word at most, and a count in a minute none (" + string.Join(", ", doubled) + ")");
        var notNumbers = n.Values.Where(kv => Whole(kv.Value) is null).Select(kv => kv.Key).ToList();
        var misnamed = otherUnits.Where(x => !n.Values.ContainsKey(x) || Units(x) > 0 || PerMinute(x)).Concat(notNumbers.Where(x => !otherUnits.Contains(x))).ToList();
        Check(misnamed.Count == 0,
            "showbus spec: §14.2 names a unit for each row that is a word, and every name it gives another unit is a row without a unit word (" + string.Join(", ", misnamed) + ")");

        (string Low, string High, bool Strict)[] order =
        [
            ("hello.bytes", "hello.read.bytes", false), ("hello.id.min", "hello.id.max", false), ("hello.every.ms", "peer.stale.ms", true),
            ("peer.stale.ms", "peer.gone.ms", true), ("peer.gone.ms", "peer.forget.ms", false), ("link.beat.ms", "link.stale.ms", true),
            ("link.stale.ms", "link.drop.ms", true), ("ping.min.ms", "ping.max.ms", false), ("ping.max.ms", "link.stale.ms", true),
            ("link.check.max.ms", "link.stale.ms", true), ("link.check.max.ms", "link.selfgap.ms", true), ("link.redial.first.ms", "link.redial.max.ms", false),
            ("show.stuck.min.ms", "show.stuck.max.ms", false), ("receipt.deadline.ms", "receipt.final.ms", true),
            ("context.post.min.ms", "context.post.max.ms", false), ("context.post.max.ms", "context.kept.ms", true), ("context.items.first", "context.items", false),
            ("context.words.read.chars", "context.words.chars", false), ("report.retry.first.ms", "report.retry.max.ms", false),
            ("report.retry.max.ms", "report.retry.for.ms", false), ("cert.renew.days", "cert.days", true),
            ("proposal.collect.ms", "proposal.undelivered.ms", true), ("context.peers", "peers.max", false),
        ];
        var broken = order.Where(o => n.Of(o.Low) < 0 || n.Of(o.High) < 0 || (o.Strict ? n.Of(o.Low) >= n.Of(o.High) : n.Of(o.Low) > n.Of(o.High)))
            .Select(o => o.Low + (o.Strict ? " < " : " ≤ ") + o.High).ToList();
        Check(broken.Count == 0, "showbus spec: §14's bounds hold in the order their sections need (" + string.Join(", ", broken) + ")");
    }

    /// <summary>
    /// Every dotted name §13 writes in code is a number §14 gives; every number of §13's own (§14.4: not §2's, §6's, §10's, §11's or
    /// §12's) that §13 does not name is one §14.4 gives its meaning; and every dotted name §14's prose writes in code is a row's.
    /// </summary>
    static void Named(Action<bool, string> Check, string s13, string s14, Numbers n)
    {
        var named = CodeNames(s13);
        named.ExceptWith(NotNumbers);
        var unknown = named.Where(x => !n.Values.ContainsKey(x)).Order(StringComparer.Ordinal).ToList();
        Check(unknown.Count == 0, "showbus spec: every number §13 names in code is a §14 row (" + string.Join(", ", unknown) + ")");

        var own = n.Values.Keys.Where(k => !OtherSections.Any(p => k.StartsWith(p, StringComparison.Ordinal))).ToHashSet(StringComparer.Ordinal);
        var unnamed = own.Except(named).ToHashSet(StringComparer.Ordinal);
        var whose = Between(s14, "4. **Whose rows.**", "\n5. **") ?? "";
        var meant = Regex.Matches(whose, @"^\s+- `([a-z][a-z0-9.]*)`:", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Check(unnamed.SetEquals(meant),
            "showbus spec: §14.4 gives the meaning of exactly the numbers of §13's own that §13 does not name (unmeant: "
            + string.Join(", ", unnamed.Except(meant)) + "; named or not §13's: " + string.Join(", ", meant.Except(unnamed)) + ")");

        var prose = s14[..s14.IndexOf("\n```showbus\n", StringComparison.Ordinal)];
        var words = n.Values.Keys.Concat(n.Caps.Select(c => c.Word)).Concat(n.Reserved.Select(r => r.Word)).Append("peerlink.feature").ToHashSet(StringComparer.Ordinal);
        var strays = CodeNames(prose).Where(x => !words.Contains(x)).ToList();
        Check(strays.Count == 0, "showbus spec: every dotted name §14's text writes in code is one of its rows (" + string.Join(", ", strays) + ")");
        Check(Unworded.All(n.Values.ContainsKey), "showbus spec: every row the header says binds nothing yet is a §14 row (" + string.Join(", ", Unworded.Where(x => !n.Values.ContainsKey(x))) + ")");
    }

    /// <summary>The dotted names a text writes in code: the §14 rows it names, and anything else written like one.</summary>
    static HashSet<string> CodeNames(string text)
        => Regex.Matches(text, @"`([a-z][a-z0-9]*(?:\.[a-z0-9-]+)+)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    // ------------------------------------------------------------------ §6's text

    /// <summary>
    /// Every dotted name §6 writes in code is a §14 row, but a context's own fields; the context rows §6 does not name yet are exactly
    /// <see cref="SizesLater"/>, and none once 1.3 is final.
    /// </summary>
    static void ContextNamed(Action<bool, string> Check, string s6, Numbers n, bool draft)
    {
        var named = CodeNames(s6);
        named.ExceptWith(ContextFields);
        var unknown = named.Where(x => !n.Values.ContainsKey(x)).Order(StringComparer.Ordinal).ToList();
        Check(unknown.Count == 0, "showbus spec: every number §6 names in code is a §14 row (" + string.Join(", ", unknown) + ")");
        var unnamed = n.Values.Keys.Where(k => k.StartsWith("context.", StringComparison.Ordinal) && !named.Contains(k)).ToHashSet(StringComparer.Ordinal);
        Check(unnamed.SetEquals(SizesLater),
            "showbus spec: the context rows §6 does not name yet are ShowBusSpecTests.SizesLater (unnamed: " + string.Join(", ", unnamed.Except(SizesLater))
            + "; named now: " + string.Join(", ", SizesLater.Except(unnamed)) + ")");
        Check(draft || SizesLater.Length == 0, "showbus spec: 1.3 final names every context row in §6 (" + string.Join(", ", SizesLater) + ")");
    }

    /// <summary>
    /// §6's rules for the Eye are P1–P8, in order, by their names. Its link kinds are <see cref="ShowBusKinds.Links"/> and every link
    /// row the master writes uses one; P2's one row runs from the connector's end to the acceptor's, its K the connector's use, one of
    /// three link kinds, and a reader that holds both ends draws the worse light; an acceptor's items stand on a proof or a bound key,
    /// and its unproved links fold into one item with no row; P3 feeds (a clip's time among what a node shows) and P4 serves; P5's words
    /// put the show stuck in place of a time; P8 keeps a message's text private whatever screen shows it. A link end is @ and a §2 id,
    /// with an item id after a slash, within its §14 rows, and every end the master writes is one. §6's example keeps P1–P7: its light
    /// is its items' worst, its kinds the table's, its links its own items' or a peer's it lists one hop, its link item with its one P2
    /// row, its items in P7's order (its peers row has §6's fields: <see cref="Words"/>). The kind table is <see cref="ShowBusKinds.Items"/> row for row, its last row ShowBusKinds' default, its planes the Eye's
    /// bands, its words binding and its placement advice; P5's counting kinds are its words. Posting keeps the latest, is no node's
    /// command, and on a line is LINK's, a bound key's of scope post or above, and that key's own node's — never a code's — answered in
    /// 13.5.2's and 13.5.3's words. 13.16.4 and 13.16.5 publish P2's items and rows; 13.16.5 lights an accepted link as its connector
    /// would, amber over green, grey for peer.forget.ms once ended, never red; P1's one exception is 13.18's twin item, which 13.18 names.
    /// </summary>
    static void ContextText(Action<bool, string> Check, string body, string s6, Numbers n)
    {
        var rules = Regex.Matches(s6, @"^(\d)\. \*\*P(\d) — (.+?)\.\*\*", RegexOptions.Multiline)
            .Select(m => (N: m.Groups[1].Value, P: m.Groups[2].Value, Name: m.Groups[3].Value)).ToList();
        Check(rules.Select(r => r.N).SequenceEqual(Enumerable.Range(1, EyeRules.Length).Select(i => i.ToString(CultureInfo.InvariantCulture)))
              && rules.All(r => r.N == r.P) && rules.Select(r => r.Name).SequenceEqual(EyeRules),
            "showbus spec: §6's rules for the Eye are P1–P8, in order, by their names (" + string.Join(" · ", rules.Select(r => "P" + r.P + " " + r.Name)) + ")");

        // The link kinds, and every row the master writes.
        var flat = Flat(s6);
        var linkKinds = Ticked(Inside(flat, "A link joins two ends with a `kind` — ", " — and a `light`") ?? "").ToList();
        Check(linkKinds.SequenceEqual(ShowBusKinds.Links), "showbus spec: §6's link kinds are ShowBusKinds.Links, in order (" + string.Join(", ", linkKinds) + ")");
        var rows = Regex.Matches(body, @"\{""from"":""[^""]*"",""to"":""[^""]*"",""kind"":(?:""([a-z]+)""|K)(?:,""[a-z]+"":""[^""]*"")*\}").ToList();
        var strays = rows.Where(m => m.Groups[1].Success && !ShowBusKinds.Links.Contains(m.Groups[1].Value)).Select(m => m.Groups[1].Value).ToList();
        Check(rows.Count >= 6 && strays.Count == 0, $"showbus spec: every link row the master writes uses one of §6's link kinds ({rows.Count} rows; " + string.Join(", ", strays) + ")");
        var p2 = EyeRule(s6, 2);
        Check(p2.Contains("a connector publishes `{\"from\":\"<its link item>\",\"to\":\"@<peer id>\",\"kind\":K}`, an acceptor `{\"from\":\"@<peer id>\",\"to\":\"<its link item>\",\"kind\":K}`", StringComparison.Ordinal)
              && p2.Contains("A link has this one row, whatever else it carries", StringComparison.Ordinal),
            "showbus spec: P2's one row runs from the connector's end to the acceptor's, whichever end publishes it");
        var uses = Ticked(Inside(p2, "**K** is", ". An acceptor") ?? "").ToList();
        Check(uses.SequenceEqual(["controls", "follows", "sends"]) && uses.All(ShowBusKinds.Links.Contains)
              && p2.Contains("An acceptor names K by the same order from what the connector has done on the link within its key's scope", StringComparison.Ordinal)
              && p2.Contains("**Both ends.** A reader that holds both ends' items for one link draws one edge, in the worse of their lights, with the connector's K", StringComparison.Ordinal),
            "showbus spec: P2's K is the connector's primary use — controls, else follows, else sends — each a link kind, an acceptor names it by the same "
            + "order from what it sees, and a reader draws one edge in the worse light with the connector's K (" + string.Join(", ", uses) + ")");
        Check(p2.Contains("of kind `link`, its id `link:` followed by a name of its choosing, the key's scope among its words", StringComparison.Ordinal)
              && p2.Contains("as connector, for each PeerLink it holds, with its 13.16.3 record's light and state words", StringComparison.Ordinal)
              && p2.Contains("as acceptor, for each link it accepted that LINK verified (13.6), or, until that link's connector speaks LINK, that a key bound to the connector's node proves (13.5.2;", StringComparison.Ordinal)
              && p2.Contains("with its 13.16.5 row's light and words", StringComparison.Ordinal)
              && p2.Contains("names the id its hello or its answer gave, and its words say \"not proved\"", StringComparison.Ordinal),
            "showbus spec: P2's item takes its record's or its acceptor row's light and words, the key's scope among them; an acceptor's stands on a proof "
            + "or a bound key; and a connector's link not yet proved says so");
        Check(p2.Contains("The links it took that neither proves — a level-0 link a pairing code proved, a `keyless` one — it folds into one item of kind `link`, id `link:unproved`, whose words count and name them", StringComparison.Ordinal)
              && p2.Contains("with no row: an id nothing proved draws no edge from the node that took it", StringComparison.Ordinal),
            "showbus spec: P2 bounds an acceptor's unproved links: one link:unproved item counts them, with no row to an id nothing proved");
        Check(EyeRule(s6, 3).Contains("a timer or a clip's time left it shows", StringComparison.Ordinal)
              && EyeRule(s6, 3).Contains("`{\"from\":\"@<node id>/<item id>\",\"to\":\"<its own item>\",\"kind\":\"feeds\"}`", StringComparison.Ordinal)
              && EyeRule(s6, 4).Contains("`{\"from\":\"<its item>\",\"to\":\"@<holder id>\",\"kind\":\"serves\"}`", StringComparison.Ordinal),
            "showbus spec: P3's claim feeds the shower's own item from the other node's, a clip's time it shows among them, and P4's grant serves its holder");

        // Link ends.
        Check(flat.Contains("`@<node id>/<item id>` names that node's item", StringComparison.Ordinal) && flat.Contains("`@<node id>` names the node itself", StringComparison.Ordinal)
              && flat.Contains("The node id is a §2 id, `hello.id.min` to `hello.id.max` letters, digits, `-` and `_`; the item id is at most `context.item.id.chars`, and the whole end at most `context.link.end.chars`", StringComparison.Ordinal)
              && flat.Contains("A reader refuses a link whose end breaks these", StringComparison.Ordinal),
            "showbus spec: §6's link ends name a node, or one of its items, by a §2 id within context.link.end.chars, and a reader refuses one that breaks them");
        Check(2 + n.Of("hello.id.max") + n.Of("context.item.id.chars") <= n.Of("context.link.end.chars"),
            "showbus spec: the longest end §6 allows — @, a longest id, /, a longest item id — is within context.link.end.chars");
        var ends = Regex.Matches(body, "\"(@[^\"<>]*)\"").Select(m => m.Groups[1].Value).ToList();
        var bad = ends.Where(e => !EndOk(e, n)).ToList();
        Check(ends.Count >= 2 && bad.Count == 0, $"showbus spec: every link end the master writes is one §6 allows ({ends.Count} ends; " + string.Join(", ", bad) + ")");

        // The example keeps §6's own rules.
        JsonElement? example = null;
        var json = Inside(s6, "\n```json\n", "\n```");
        Check(json is not null, "showbus spec: §6 shows a context");
        if (json is not null)
        {
            try { example = JsonDocument.Parse(json).RootElement.Clone(); }
            catch (JsonException e) { Check(false, "showbus spec: §6's example is JSON (" + e.Message + ")"); }
        }
        if (example is { } c)
        {
            var items = Items(c, "items");
            var ids = items.Select(i => Str(i, "id") ?? "").ToList();
            var lights = items.Select(i => Str(i, "light") ?? "").ToList();
            var links = Items(c, "links");
            var peers = Items(c, "peers").Select(p => Str(p, "id") ?? "").ToHashSet(StringComparer.Ordinal);
            bool Own(string? end) => end is not null && ids.Contains(end);
            bool Hop(string? end) => end is not null && EndOk(end, n) && peers.Contains(end[1..].Split('/')[0]);
            Check(Str(c, "light") == Light.Worst(lights), "showbus spec: §6's example's light is its items' worst");
            Check(items.All(i => ShowBusKinds.Known(Str(i, "kind"))), "showbus spec: §6's example's items use the kind table's words (P6)");
            Check(links.Count > 0 && links.All(l => ShowBusKinds.Links.Contains(Str(l, "kind") ?? "")
                                                     && (Own(Str(l, "from")) || Hop(Str(l, "from"))) && (Own(Str(l, "to")) || Hop(Str(l, "to")))),
                "showbus spec: §6's example's links join its own items, or one to a peer it lists one hop, by a link kind");
            var linkItems = items.Where(i => Str(i, "kind") == "link").Select(i => Str(i, "id")).ToList();
            Check(linkItems.Count > 0 && linkItems.All(id => links.Count(l => Str(l, "from") == id || Str(l, "to") == id) == 1
                                                             && links.Any(l => Str(l, "from") == id && Hop(Str(l, "to")))),
                "showbus spec: §6's example's link item has its one P2 row, from it to its peer");
            var crossing = ids.Select((id, k) => (id, k)).Where(x => links.Any(l => (Str(l, "from") == x.id && Hop(Str(l, "to"))) || (Str(l, "to") == x.id && Hop(Str(l, "from")))));
            Check(lights.Select(Light.Rank).SkipWhile(r => r >= Light.Rank(Light.Amber)).All(r => r < Light.Rank(Light.Amber))
                  && crossing.All(x => x.k < n.Of("context.items.first")),
                "showbus spec: §6's example lists its reds and ambers first, and its items with a row to another node among its first context.items.first (P7)");
        }

        // The kind table.
        var table = KindTable(s6);
        Check(table.Count > 1 && table[^1].First == "anything else" && table[^1].Kinds.Length == 0 && table[^1].Plane == ShowBusKinds.ElsePlane
              && table[^1].Column == ShowBusKinds.ElseColumn,
            "showbus spec: the kind table's last row places anything else as ShowBusKinds does (control, column 1)");
        var rowsOf = table.Take(Math.Max(0, table.Count - 1)).ToList();
        Check(rowsOf.Count == ShowBusKinds.Items.Count
              && rowsOf.Zip(ShowBusKinds.Items).All(p => p.First.Kinds.SequenceEqual(p.Second.Kinds) && p.First.Plane == p.Second.Plane && p.First.Column == p.Second.Column),
            "showbus spec: the kind table is ShowBusKinds.Items, row for row (" + string.Join(" · ", rowsOf.Select(r => r.Plane + " " + r.Column + ": " + string.Join(" ", r.Kinds))) + ")");
        var words = rowsOf.SelectMany(r => r.Kinds).ToList();
        Check(words.Count > 0 && words.Distinct(StringComparer.Ordinal).Count() == words.Count && words.All(w => w.Length <= n.Of("context.item.kind.chars"))
              && table.All(r => Planes.Contains(r.Plane)),
            "showbus spec: the kind table names each kind once, each within context.item.kind.chars, on one of the Eye's planes");
        var counting = Ticked(Inside(EyeRule(s6, 5), "counting items — kinds", "— red") ?? "").ToList();
        Check(counting.Count > 0 && counting.All(ShowBusKinds.Known), "showbus spec: P5's counting kinds are the kind table's words (" + string.Join(", ", counting) + ")");
        Check(EyeRule(s6, 5).Contains("red, its state words saying the show is stuck in place of a time", StringComparison.Ordinal),
            "showbus spec: P5 turns a stuck node's counting items red, their words the show stuck in place of a time");
        Check(EyeRule(s6, 6).Contains("a reader places any other word as the table's last row says", StringComparison.Ordinal)
              && flat.Contains("**The kind table.** The words P6 asks for", StringComparison.Ordinal)
              && flat.Contains("The words bind; the placement is advice to an Eye", StringComparison.Ordinal),
            "showbus spec: P6 places a word the kind table does not name by its last row; the table's words bind and its placement advises");
        var p8 = EyeRule(s6, 8);
        Check(p8.Contains("what one person writes for another — a message's text, a speaker's message and an audience question among them, whatever screen shows it", StringComparison.Ordinal)
              && p8.Contains("The show's own words are not private: a running order's titles and the people they name, a cue's words (\"LX 12\"), and who ran a verb, in `recent`", StringComparison.Ordinal)
              && p8.Contains("A node may keep more out than this", StringComparison.Ordinal),
            "showbus spec: P8 keeps a message's text private whatever screen shows it, a speaker's and an audience question's among them, and says titles, "
            + "cue words and who ran a verb are the show's");

        // Posting.
        Check(flat.Contains("**Posting.** A node that gives its context to another (a desk) posts it on change and at least every `context.post.max.ms` while linked, never more often than once a `context.post.min.ms`", StringComparison.Ordinal)
              && flat.Contains("keeps the latest it took for each node id for `context.kept.ms` after it came, then drops it", StringComparison.Ordinal)
              && flat.Contains("no node lists it among its `commands`, and no assistant proposes it", StringComparison.Ordinal),
            "showbus spec: §6 posts a context on change, within context.post.min.ms and context.post.max.ms, keeps the latest context.kept.ms, and lists it as no command");
        Check(flat.Contains("the line `CONTEXT POST <json>`. It is taken only on a connection LINK verified (13.6) with a key bound to one node — made for it or enrolled (13.5.2, 13.5.7) — of scope `post` or above (13.5.3), and only for the node that key speaks for: the context's `node.t` and `node.id` are the key's bound ones", StringComparison.Ordinal)
              && flat.Contains("A pairing code speaks for no one node (13.5.2), so a connection a code proved posts no context until it enrols a key", StringComparison.Ordinal)
              && flat.Contains("The whole line is within `link.command.bytes` (13.14)", StringComparison.Ordinal),
            "showbus spec: CONTEXT POST is taken only on a LINK-verified connection, with a key bound to one node of scope post or above, for that node, "
            + "never on a code's proof, within link.command.bytes");
        var exchange = Inside(s6, "\n```\n→ ", "\n```") is { } said ? ("→ " + said).Split('\n') : Array.Empty<string>();
        var answers = exchange.Where(l => l.StartsWith("← ", StringComparison.Ordinal)).Select(l => l[2..]).ToList();
        var keys = Flat(Part(body, "13.5"));
        var binding = Regex.Match(keys, "\"(this key speaks for) <kind> <id>\"");
        var scope = Regex.Match(keys, "A refusal names it: \"([^\"]+)\"");
        var bound = answers.Select(a => Regex.Match(a, "^ERR @c7 " + Regex.Escape(binding.Groups[1].Value) + @" ([a-z]+) [A-Za-z0-9_-]+ — not for \1 [A-Za-z0-9_-]+$"))
            .FirstOrDefault(m => m.Success);
        Check(exchange.FirstOrDefault()?.StartsWith("→ @c7 CONTEXT POST {\"v\":1,\"node\":{\"t\":\"messenger\",", StringComparison.Ordinal) == true
              && answers.Count == 6 && answers[0] == "OK @c7 kept" && answers.Skip(1).All(a => a.StartsWith("ERR @c7 ", StringComparison.Ordinal))
              && binding.Success && bound is not null && n.Kinds.Any(k => k.T == bound.Groups[1].Value)
              && answers.Contains("ERR @c7 a pairing code speaks for no one node — posting a context needs a key made or enrolled for this node")
              && keys.Contains("A code belongs to its node and may be proved by several connectors, each named by its own id", StringComparison.Ordinal)
              && scope.Success && answers.Contains("ERR @c7 " + scope.Groups[1].Value),
            "showbus spec: CONTEXT POST answers OK kept, or ERR in 13.5.2's binding words, a code's own words (13.5.2: a code names no one node) and "
            + "13.5.3's scope words (" + string.Join(" · ", answers) + ")");

        // 13.16 and 13.18 publish what P1 and P2 say.
        var states = Flat(Part(body, "13.16"));
        Check(states.Contains("with its one `links` row to the peer node (§6's P2)", StringComparison.Ordinal)
              && states.Contains("in its context (one `link` item and its row per peer LINK or a bound key proves, and one item that counts the rest, §6's P2)", StringComparison.Ordinal),
            "showbus spec: 13.16.4's connector and 13.16.5's acceptor publish P2's items and rows");
        Check(states.Contains("✓ green when it is verified (13.1), or when it is `keyless`, with i \"public view\" beside it", StringComparison.Ordinal)
              && states.Contains("It is i \"public view\" when no key is set", StringComparison.Ordinal)
              && states.Contains("△ amber, over green, when it is `legacy` (level 0: the proof shows it knows the token, never who dialled) or `plain` (from another machine with no TLS; i on a local link), or its key was seen in clear (13.6.9)", StringComparison.Ordinal)
              && states.Contains("○ grey once its link has ended, for `peer.forget.ms`, and then it leaves the list; never red, since an acceptor does not redial", StringComparison.Ordinal),
            "showbus spec: 13.16.5 lights an accepted link — green verified, or keyless with the connector's i public view; amber over green for legacy, "
            + "plain or seen in clear; grey for peer.forget.ms once ended — and never red");
        Check(EyeRule(s6, 1).Contains("The one exception is Patterns' twin (13.18): a twin node names its desk in one item of kind `twin`", StringComparison.Ordinal)
              && ShowBusKinds.Known("twin")
              && Flat(Part(body, "13.18")).Contains("one item of kind `twin`, with the twin link's own light and words: the one item §6's P1 lets name another node", StringComparison.Ordinal),
            "showbus spec: P1's one exception is the twin's desk item, of kind twin, and 13.18 names it");
    }

    /// <summary>The text of §6's rule P<paramref name="p"/>, flattened, up to the next rule, or for the last up to the kind table.</summary>
    static string EyeRule(string s6, int p)
    {
        string Head(int k) => "\n" + k.ToString(CultureInfo.InvariantCulture) + ". **P" + k.ToString(CultureInfo.InvariantCulture) + " — ";
        var start = s6.IndexOf(Head(p), StringComparison.Ordinal);
        if (start < 0) return "";
        var next = s6.IndexOf(Head(p + 1), start + 1, StringComparison.Ordinal);
        var end = next > 0 ? next : s6.IndexOf("\n**The kind table**", start, StringComparison.Ordinal);
        return Flat(s6[(start + 1)..(end > 0 ? end : s6.Length)]);
    }

    /// <summary>Whether <paramref name="end"/> is a link end §6 allows: @ and a §2 id, then an item id after a slash, each within its rows.</summary>
    static bool EndOk(string end, Numbers n)
    {
        var m = Regex.Match(end, "^@([A-Za-z0-9_-]+)(?:/(.+))?$");
        return m.Success && end.Length <= n.Of("context.link.end.chars") && m.Groups[1].Length >= n.Of("hello.id.min") && m.Groups[1].Length <= n.Of("hello.id.max")
               && (!m.Groups[2].Success || m.Groups[2].Length <= n.Of("context.item.id.chars"));
    }

    /// <summary>
    /// §6's kind table: each row's first cell as written, its ticked kind words, its plane, and the number its column cell starts with
    /// (-1 when it starts with none).
    /// </summary>
    static List<(string First, string[] Kinds, string Plane, int Column)> KindTable(string s6)
    {
        var rows = new List<(string, string[], string, int)>();
        var inTable = false;
        foreach (var line in s6.Split('\n'))
        {
            if (line.StartsWith("| kind | plane | column |", StringComparison.Ordinal)) { inTable = true; continue; }
            if (!inTable) continue;
            if (!line.StartsWith('|')) break;
            if (line.StartsWith("|---", StringComparison.Ordinal)) continue;
            var cells = line.Split('|');
            if (cells.Length < 5) { rows.Add((line, Array.Empty<string>(), "", -1)); continue; }
            var column = Regex.Match(cells[3].Trim(), @"^(\d+)(?: — .+)?$") is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            rows.Add((cells[1].Trim(), Regex.Matches(cells[1], "`([a-z]+)`").Select(x => x.Groups[1].Value).ToArray(), cells[2].Trim(), column));
        }
        return rows;
    }

    /// <summary>The text between <paramref name="from"/> and the next <paramref name="to"/> after it, both left out; null when there is no <paramref name="from"/>.</summary>
    static string? Inside(string text, string from, string to) => Between(text, from, to)?[from.Length..];

    // ------------------------------------------------------------------ §12's text

    /// <summary>
    /// §12 keeps its ten parts in order, with their titles and nothing below them. §1 rule 5 makes a window's address, a viewing code
    /// and a crew link view passes and gives a view pass exactly two carriers as an address — a browser's address bar, and the one
    /// verified PeerLink (or key-verified node link) to the grant's own app — the second one 12.5's, whose four conditions all hold
    /// before a grant's address is sent; anywhere else a holder keeps it as its own secret, which 13.18's twin may carry and a grant's
    /// address never. 12.5 and 13.2 say where it travels in the same words. 12.6 sends the grants on every link of the app's node, an
    /// empty list when it holds none, sends a draw grant's window only where a grant's own node may hear it, and reads an app's
    /// abilities failing closed; 12.9 draws a granted window's content only from its current link's assignment.
    /// </summary>
    static void WindowsText(Action<bool, string> Check, string body, string s12)
    {
        var parts = Regex.Matches(s12, @"^### 12\.(\d+) (.+)$", RegexOptions.Multiline)
            .Select(m => (N: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Title: m.Groups[2].Value)).ToList();
        Check(parts.Select(p => p.N).SequenceEqual(Enumerable.Range(1, WindowParts.Length)),
            "showbus spec: §12's parts are 12.1–12.10 in order (" + string.Join(", ", parts.Select(p => p.N)) + ")");
        Check(parts.Select(p => p.Title).SequenceEqual(WindowParts),
            "showbus spec: §12's parts keep their titles (" + string.Join(" · ", parts.Select(p => p.Title).Except(WindowParts)) + ")");
        Check(Regex.Matches(s12, "^#### ", RegexOptions.Multiline).Count == 0, "showbus spec: §12 has no level below its parts");

        var rule5 = Flat(Between(body, "\n5. **Secrets ride", "\n6. **") ?? "");
        Check(rule5.Contains("*view pass* (§12.2) — a window's address, a viewing code, a crew link", StringComparison.Ordinal),
            "showbus spec: §1 rule 5 makes a window's address, a viewing code and a crew link view passes (§12.2)");
        Check(rule5.Contains("a view pass rides as an address in exactly two carriers: a browser's address bar, and the one verified PeerLink (or key-verified node link) to the grant's own app (12.5)", StringComparison.Ordinal),
            "showbus spec: §1 rule 5 gives a view pass exactly two carriers as an address, the second one 12.5's");
        Check(rule5.Contains("keeps it as its own secret, which travels only where that node's secrets may (13.18's twin, under its switch); a grant's address travels nowhere else (12.5, 12.8)", StringComparison.Ordinal)
              && Flat(Part(body, "13.18")).Contains("send device secrets — a view pass the node keeps among them (12.8) — only under their own switch", StringComparison.Ordinal)
              && Flat(Part(body, "13.18")).Contains("and never a grant's address (12.4)", StringComparison.Ordinal),
            "showbus spec: §1 rule 5 and 13.18 agree: a view pass a node keeps is its own secret, which its twin carries only under its switch, and a grant's address never");

        var path = WindowPart(s12, 5) ?? "";
        var conditions = Regex.Matches(path, @"^- \*\*([^*]+)\*\*", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Check(path.Contains("is sent only on a link that is all of these:", StringComparison.Ordinal)
              && conditions.SequenceEqual(["the grant's own node's", "pinned TLS, or local", "on a key never seen in clear", "offering `pages`"]),
            "showbus spec: 12.5 sends a grant's address only on the grant's own node's link, pinned TLS or local, on a key never seen in clear, offering pages ("
            + string.Join(" · ", conditions) + ")");
        Check(Flat(path).Contains("**pinned TLS, or local**: TLS to the certificate the app pinned", StringComparison.Ordinal)
              && Flat(path).Contains("or local (13.1): a loopback address, never one the node owns", StringComparison.Ordinal)
              && Flat(Part(body, "13.2")).Contains("a view pass on the one link §1 rule 5 lets it ride, travel only inside pinned TLS or on a local link (13.1,", StringComparison.Ordinal)
              && Flat(Part(body, "13.1")).Contains("**Local**: the socket's remote address is a loopback address", StringComparison.Ordinal),
            "showbus spec: 12.5 and 13.2 say alike where a grant's address travels — pinned TLS, or a local link, which 13.1 makes loopback only");

        var messages = Flat(WindowPart(s12, 6) ?? "");
        Check(messages.Contains("**The grants**, node → app, on every link of 12.5's first point, whether or not that node holds a grant", StringComparison.Ordinal)
              && messages.Contains("A node that holds none hears an empty list", StringComparison.Ordinal),
            "showbus spec: 12.6 sends the grants on every link of 12.5's first point, an empty list when that node holds none, so a grant withdrawn while its app was away ends");
        Check(messages.Contains("**A draw grant's window**, node → app, only on a link of 12.5's first point, on a key never seen in clear (13.6.9), that offers `window-draw`", StringComparison.Ordinal),
            "showbus spec: 12.6 sends a draw grant's window only on the grant's own node's link, on a key never seen in clear, offering window-draw");
        Check(messages.Contains("The node reads the first `hello.caps` words and ignores the rest, a word it does not know and a word whose cap row does not name the app's kind: an ability it ignores is not offered", StringComparison.Ordinal),
            "showbus spec: 12.6 reads an app's can= failing closed: past hello.caps, an unknown word or one whose cap row names another kind is not offered");
        var drawing = Flat(WindowPart(s12, 9) ?? "");
        Check(drawing.Contains("its content, from the last `window-assign` for it on its current link to the granting node", StringComparison.Ordinal)
              && drawing.Contains("black for a window it was never granted, or whose grant ended: a `window-end`, or a `grants` that leaves it out, a new link's first included", StringComparison.Ordinal),
            "showbus spec: 12.9 draws a granted window's content only from its current link's assignment, and black once a grants leaves it out");
    }

    /// <summary>The text of part 12.<paramref name="n"/>, up to the next part.</summary>
    static string? WindowPart(string s12, int n) => Between(s12, $"\n### 12.{n.ToString(CultureInfo.InvariantCulture)} ", "\n### ");

    /// <summary>The text of the body's part <paramref name="number"/> (13.2), up to the next part or section; empty when there is none.</summary>
    static string Part(string body, string number)
    {
        var start = body.IndexOf("\n### " + number + " ", StringComparison.Ordinal);
        if (start < 0) return "";
        var ends = new[] { body.IndexOf("\n### ", start + 1, StringComparison.Ordinal), body.IndexOf("\n## ", start + 1, StringComparison.Ordinal) }.Where(i => i > 0).ToList();
        return body[(start + 1)..(ends.Count > 0 ? ends.Min() : body.Length)];
    }

    /// <summary>The text with every run of white space one space, so a sentence reads the same however it is wrapped.</summary>
    static string Flat(string text) => Regex.Replace(text, @"\s+", " ");

    /// <summary>
    /// Every dotted name §12 writes in code is a §14 row, and every row §14.4 gives §12 is named there, so §12's words hold each of
    /// them; the caps §12 names are §14's cap words, and what 12.6's can= offers is exactly the caps an app holds a window with.
    /// </summary>
    static void WindowsNamed(Action<bool, string> Check, string s12, Numbers n)
    {
        var named = CodeNames(s12);
        var unknown = named.Where(x => !n.Values.ContainsKey(x)).Order(StringComparer.Ordinal).ToList();
        Check(unknown.Count == 0, "showbus spec: every number §12 names in code is a §14 row (" + string.Join(", ", unknown) + ")");
        var unworded = n.Values.Keys.Where(k => WindowRows.Any(p => k.StartsWith(p, StringComparison.Ordinal)) && !named.Contains(k)).Order(StringComparer.Ordinal).ToList();
        Check(unworded.Count == 0, "showbus spec: §12 names every window and view row §14 gives it (" + string.Join(", ", unworded) + ")");
        var intro = s12[..s12.IndexOf("\n### ", StringComparison.Ordinal)];
        var caps = Regex.Matches(intro, "`([a-z-]+)`").Select(m => m.Groups[1].Value).ToList();
        Check(caps.Order(StringComparer.Ordinal).SequenceEqual(["pages", "window-draw", "windows"]) && caps.All(c => n.Caps.Any(x => x.Word == c)),
            "showbus spec: §12 grants with cap windows and holds with pages or window-draw, each a §14 cap (" + string.Join(", ", caps) + ")");
        var offers = Regex.Matches(WindowPart(s12, 6) ?? "", "`can=([a-z-]+)`").Select(m => m.Groups[1].Value).ToList();
        var holders = caps.Where(c => c != "windows").ToList();
        Check(offers.Order(StringComparer.Ordinal).SequenceEqual(holders.Order(StringComparer.Ordinal)) && offers.All(w => n.Caps.Any(x => x.Word == w && x.Kinds.Length > 0)),
            "showbus spec: 12.6's can= offers exactly the caps an app holds a window with, each a §14 cap row that names the kinds that may offer it ("
            + string.Join(", ", offers) + ")");
    }

    /// <summary>
    /// Every part the body cites is there: §N.M is a part's heading; a bare 12.x, 13.x or 14.x (outside code) is one of §12's or §13's
    /// parts, or one of §14's numbered items, and with a third number one of that part's numbered items. The header names the parts
    /// the body cites ahead of their words, and only those, and 1.3 final has none.
    /// </summary>
    static void References(Action<bool, string> Check, string header, string body, bool draft)
    {
        var prose = Regex.Replace(body, "^```[^\n]*\n.*?^```$", "", RegexOptions.Multiline | RegexOptions.Singleline);
        var heads = Regex.Matches(body, @"^### (\d+\.\d+) ", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var s14 = Between(body, "\n" + NumbersHeading + "\n", "\n## ") ?? "";
        bool There(string section, string part, string item) => section == "14"
            ? item.Length == 0 && Regex.IsMatch(s14, "^" + part + @"\. ", RegexOptions.Multiline)
            : heads.Contains(section + "." + part) && (item.Length == 0 || Regex.IsMatch(Part(body, section + "." + part), @"^\s*" + item + @"\. ", RegexOptions.Multiline));
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(prose, @"(?<![\w.])(§?)(\d+)\.(\d+)(?:\.(\d+))?(?![\d])(?!\.\d)"))
        {
            var (cited, section) = (m.Groups[1].Length > 0, m.Groups[2].Value);
            if ((cited || section is "12" or "13" or "14") && !There(section, m.Groups[3].Value, m.Groups[4].Value)) missing.Add(m.Value);
        }
        var ahead = Regex.Match(Flat(header), @"cite (.+?) ahead of their words") is { Success: true } a
            ? Regex.Matches(a.Groups[1].Value, @"§\d+\.\d+").Select(x => x.Value).ToHashSet(StringComparer.Ordinal) : [];
        Check(missing.SetEquals(ahead), "showbus spec: every part the body cites is there, or the header names it as cited ahead of its words (not there: "
            + string.Join(", ", missing) + "; the header names: " + string.Join(", ", ahead.Order(StringComparer.Ordinal)) + ")");
        Check(draft || ahead.Count == 0, "showbus spec: 1.3 final cites no part ahead of its words (" + string.Join(", ", ahead) + ")");
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
        var fences = Regex.Matches(text, "^```peerlink$", RegexOptions.Multiline);
        Check(fences.Count == 1, "showbus spec: one ```peerlink block in the master");
        var conformance = Part(s13, 20);
        var inner = conformance is null ? null : Between(conformance, "\n```peerlink\n", "\n```");
        Check(inner is not null && fences.Count == 1 && fences[0].Index >= text.Length - body.Length, "showbus spec: the ```peerlink block is in 13.20, inside the hashed body");
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
        Check(words.Config.SetEquals(["carrier", "can", "key", "pin", "floor", "needs"]) && words.Events.Count >= 20 && words.Fields.Count >= 15
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

    /// <summary>The bytes §14 cuts a kid, a lid and a nonce to (<c>peerlink.kid.bytes</c>, <c>peerlink.lid.bytes</c>, <c>peerlink.nonce.bytes</c>).</summary>
    readonly record struct Cut(int Kid, int Lid, int Nonce);

    static void Crypto(Action<bool, string> Check, string s13, List<JsonElement> rows, Numbers n)
    {
        var cut = new Cut((int)n.Of("peerlink.kid.bytes"), (int)n.Of("peerlink.lid.bytes"), (int)n.Of("peerlink.nonce.bytes"));
        Check(cut.Kid is > 0 and <= 32 && cut.Lid is > 0 and <= 32 && cut.Nonce > 0, $"showbus spec: §14 cuts a kid and a lid to 1–32 bytes of a hash, and a nonce is bytes ({cut})");
        if (cut.Kid is <= 0 or > 32 || cut.Lid is <= 0 or > 32 || cut.Nonce <= 0) return;
        var label = n.Values.GetValueOrDefault("peerlink.label", "");
        Check(Between(Part(s13, 6) ?? "", "\n```\n", "\n") == "\n```\n" + label,
            "showbus spec: §14's peerlink.label is the first line of 13.6.1's transcript (" + label + ")");
        var handshake = Part(s13, 6) ?? "";
        Check(handshake.Contains($"<{cut.Nonce * 2} lowercase hex: {cut.Nonce} bytes from", StringComparison.Ordinal)
              && handshake.Contains($"kid {cut.Kid * 2} lowercase hex or `code`", StringComparison.Ordinal)
              && (Part(s13, 7) ?? "").Contains($"lid=<{cut.Lid * 2} hex>", StringComparison.Ordinal),
            "showbus spec: 13.6.1's nonces and kid, and 13.7.1's lid, have the lengths §14 gives");
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
            Check(Hex(k) == Str(r, "K") && Kid(k, cut.Kid) == Str(r, "kid"), "showbus spec: a key's K is SHA-256 of the key and its kid the labelled hash's first bytes: " + key[..4]);
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
        foreach (var r in proofs) Proof(Check, r, fields, canon, cut);
        var version = n.Of("peerlink.version");
        Check(proofs.All(r => Int(r, "offer") == version && Int(r, "speaks") == version), $"showbus spec: every proof row offers and speaks §14's peerlink.version ({version})");
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

    static void Proof(Action<bool, string> Check, JsonElement r, List<string> fields, Func<string, string?> canon, Cut cut)
    {
        var bind = Str(r, "bind") ?? "";
        var name = $"proof {Str(r, "carrier")} {Str(r, "kid")} {bind[..Math.Min(5, bind.Length)]}";
        Check(Str(r, "carrier") is "line" or "ws"
              && Int(r, "offer") is >= 0 && Int(r, "speaks") is >= 0
              && Regex.IsMatch(Str(r, "server") ?? "", "^[a-z0-9_-]{1,16}:[A-Za-z0-9_-]{6,40}$")
              && Regex.IsMatch(Str(r, "client") ?? "", "^[a-z0-9_-]{1,16}:[A-Za-z0-9_-]{6,40}$")
              && Regex.IsMatch(Str(r, "kid") ?? "", "^(?:[0-9a-f]{" + cut.Kid * 2 + "}|code)$")
              && Regex.IsMatch(Str(r, "snonce") ?? "", "^[0-9a-f]{" + cut.Nonce * 2 + "}$") && Regex.IsMatch(Str(r, "cnonce") ?? "", "^[0-9a-f]{" + cut.Nonce * 2 + "}$")
              && Regex.IsMatch(Str(r, "bind") ?? "", "^(?:plain|tls:sha256:[0-9a-f]{64})$"),
            "showbus spec: a proof row's fields have 13.6.1's shapes: " + name);
        Check(Hex64(Str(r, "K")) && Hex64(Str(r, "clientProof")) && Hex64(Str(r, "serverProof")) && Regex.IsMatch(Str(r, "lid") ?? "", "^[0-9a-f]{" + cut.Lid * 2 + "}$"),
            "showbus spec: a proof row's K, proofs and lid are lowercase hex: " + name);
        if (!fields.All(f => r.TryGetProperty(f, out _)) || !Hex64(Str(r, "K"))) return;
        var t = Transcript(fields, f => Value(r.GetProperty(f)));
        var k = FromHex(Str(r, "K")!);
        var plain = Str(r, "bind") == "plain";
        if (Str(r, "Tsha256") is { } th) Check(Hex(Sha(Utf8(t))) == th, "showbus spec: the transcript hashes to Tsha256: " + name);
        Check(Hex(HMACSHA256.HashData(k, Utf8("client\n" + t))) == Str(r, "clientProof"), "showbus spec: client proof = HMAC(K, client ‖ T): " + name);
        Check(Hex(HMACSHA256.HashData(k, Utf8("server\n" + t))) == Str(r, "serverProof"), "showbus spec: server proof = HMAC(K, server ‖ T): " + name);
        Check(Hex(Sha(Utf8("showbus-peerlink-1 lid\n" + t)))[..(cut.Lid * 2)] == Str(r, "lid"), "showbus spec: lid = the first bytes of SHA-256(lid ‖ T): " + name);
        Check(Str(r, "kid") == "code" || Kid(k, cut.Kid) == Str(r, "kid"), "showbus spec: a key's kid is its K's: " + name);
        if (Str(r, "code") is { } code) Check(canon(code) is { } c && Hex(Sha(Utf8("showbus-peerlink-1 code\n" + c))) == Str(r, "K"), "showbus spec: a code proof's K is its code's: " + name);
        if (Str(r, "session") is { } s)
            Check(Str(r, "carrier") == "ws" && Hex(HMACSHA256.HashData(k, Utf8("session\n" + t))) == s, "showbus spec: S = HMAC(K, session ‖ T), on ws only: " + name);
        if (Str(r, "enrolK") is { } ek)
        {
            var link = HMACSHA256.HashData(k, Utf8("enrol\n" + t));
            Check(!plain && Str(r, "kid") == "code", "showbus spec: only a code over TLS enrols: " + name);
            Check(Hex(link) == ek && Kid(link, cut.Kid) == Str(r, "enrolKid"), "showbus spec: K_link = HMAC(K_code, enrol ‖ T), with its own kid: " + name);
        }
        if (r.TryGetProperty("enrol", out var enrol)) Check(enrol.ValueKind == JsonValueKind.False && plain, "showbus spec: a code over plain does not enrol: " + name);
        var flags = Strings(r, "flags");
        Check(plain == flags.Contains("plain") && (plain && Str(r, "kid") == "code") == flags.Contains("code-plain"),
            "showbus spec: a plain proof is flagged plain, and a code's code-plain: " + name);
    }

    // ------------------------------------------------------------------ §13's vectors and words against §14's numbers

    /// <summary>
    /// What §13's vectors, examples and words state holds with §14's numbers: liveness by <c>link.stale.ms</c> and
    /// <c>link.drop.ms</c> (§13's own example, 13.10.3's, 13.16.1's states and a machine row that only verifies too); each redial
    /// wait, 13.15.1's stated bounds and each waitMs range by its formula over <c>link.redial.first.ms</c>, <c>link.redial.max.ms</c>
    /// and <c>peerlink.redial.jitter.pct</c>, a range's n counted from its row's events with <c>peerlink.stable.ms</c>; clock and
    /// errms rows by 13.11.3's formulas with <c>clock.window</c>, <c>clock.rtt.max.ms</c>, <c>clock.apart.ms</c> and
    /// <c>clock.fresh.ms</c>; the in-flight row and words by <c>link.inflight.max</c>; the final answer by <c>receipt.final.ms</c>;
    /// a hold's end, and no dial before it, by <c>peerlink.hold.refused.ms</c> and the refusal's retry; the conflict row's mark by
    /// <c>peer.stale.ms</c> and its end by <c>peer.conflict.clear.ms</c>; and 13.14's bounds are rows.
    /// </summary>
    static void Agreement(Action<bool, string> Check, string s13, List<JsonElement> rows, Numbers n)
    {
        long stale = n.Of("link.stale.ms"), drop = n.Of("link.drop.ms");
        string Liveness(long silence) => silence <= stale ? "live" : silence <= drop ? "stale" : "down";
        foreach (var r in rows.Where(r => Str(r, "case") == "liveness"))
            Check(Int(r, "silenceMs") is { } s && Str(r, "state") == Liveness(s), "showbus spec: a liveness row holds with link.stale.ms and link.drop.ms: " + Short(r));
        var within = Regex.Match(s13, @"\((\d+) ms of silence is within `link\.stale\.ms`\)");
        Check(within.Success && within.Groups[1].Value == Ms(stale), "showbus spec: §13's example of a silence within link.stale.ms is link.stale.ms");
        var worked = Regex.Match(Part(s13, 10) ?? "", @"So (\d+) ms is live, (\d+) stale, (\d+) stale, (\d+) down");
        Check(worked.Success && worked.Groups.Values.Skip(1).Select(g => g.Value).SequenceEqual(new[] { stale, stale + 1, drop, drop + 1 }.Select(x => Ms(x))),
            "showbus spec: 13.10.3's example is link.stale.ms and link.drop.ms, each and one past");
        var cells = Regex.Matches(Part(s13, 16) ?? "", @"^\| `([a-z-]+)` \| ([^|]+) \| ([^|]+) \|", RegexOptions.Multiline)
            .ToDictionary(m => m.Groups[1].Value, m => (When: m.Groups[2].Value, Light: m.Groups[3].Value), StringComparer.Ordinal);
        bool Cites(string state, string column, params string[] names)
            => cells.TryGetValue(state, out var c) && names.All(x => (column == "when" ? c.When : c.Light).Contains("`" + x + "`", StringComparison.Ordinal));
        Check(cells.Count > 0 && cells.Values.All(c => !Regex.IsMatch(c.When + " " + c.Light, @"\d ?m?s\b"))
              && Cites("live", "when", "link.stale.ms") && Cites("stale", "when", "link.stale.ms", "link.drop.ms") && Cites("down", "when", "link.drop.ms")
              && Cites("connecting", "light", "peerlink.handshake.ms"),
            "showbus spec: 13.16.1's states are timed by link.stale.ms, link.drop.ms and peerlink.handshake.ms, with no time of their own");
        var plain = Regex.Matches(Part(s13, 14) ?? "", @"^\| [^|\n]+ \| ([^\n]+)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value)
            .Where(v => Regex.IsMatch(v, @"\d ?(?:KiB|KB|MB|bytes|ms|s)\b")).ToList();
        Check(plain.Count == 0, "showbus spec: 13.14's bounds are §14 rows, with no size or time of their own (" + string.Join(" · ", plain) + ")");

        long first = n.Of("link.redial.first.ms"), max = n.Of("link.redial.max.ms");
        var p = n.Of("peerlink.redial.jitter.pct") / 100.0;
        double Base(long failed) => Math.Min(max, first * Math.Pow(2, Math.Min(failed, 62)));
        var reconnect = Part(s13, 15) ?? "";
        var formula = Regex.Match(reconnect, @"base × \(([0-9.]+) \+ ([0-9.]+) j\)");
        Check(formula.Success && Math.Abs(double.Parse(formula.Groups[1].Value, CultureInfo.InvariantCulture) - (1 - p)) < 1e-9
                              && Math.Abs(double.Parse(formula.Groups[2].Value, CultureInfo.InvariantCulture) - 2 * p) < 1e-9,
            "showbus spec: 13.15.1's wait moves by peerlink.redial.jitter.pct either way");
        var bounds = Regex.Match(reconnect, @"A first redial waits (\d+)–(\d+) ms, and none waits more than (\d+) ms");
        Check(bounds.Success && bounds.Groups[1].Value == Ms(Base(0) * (1 - p)) && bounds.Groups[2].Value == Ms(Base(0) * (1 + p))
                             && bounds.Groups[3].Value == Ms(max * (1 + p)),
            "showbus spec: 13.15.1's first and longest waits are link.redial.first.ms and link.redial.max.ms with the jitter");
        Check(reconnect.Contains("for `peerlink.stable.ms`", StringComparison.Ordinal), "showbus spec: 13.15.2 resets n after peerlink.stable.ms");
        foreach (var r in rows.Where(r => Str(r, "case") == "redial"))
            Check(Int(r, "n") is { } failed && Num(r, "j") is { } j && Int(r, "ms") is { } ms && Num(r, "tol") is { } tol
                  && Math.Abs(Math.Round(Base(failed) * (1 - p + 2 * p * j), MidpointRounding.AwayFromZero) - ms) <= tol,
                "showbus spec: a redial row is 13.15.1's wait with §14's numbers: " + Short(r));

        long window = n.Of("clock.window"), rttMax = n.Of("clock.rtt.max.ms"), apart = n.Of("clock.apart.ms"), fresh = n.Of("clock.fresh.ms");
        static long Rtt(long[] s) => (s[3] - s[0]) - (s[2] - s[1]);
        bool Refused(long[] s) => s.Any(x => x <= 0) || s[3] < s[0] || s[2] < s[1] || Rtt(s) < 0 || Rtt(s) > rttMax;
        static long Err(long rtt) => (rtt + 1) / 2 + 1;
        Check((Part(s13, 11) ?? "").Contains("`errMs` = ⌊(rtt + 1) / 2⌋ + 1", StringComparison.Ordinal), "showbus spec: 13.11.3's errMs is ⌊(rtt + 1) / 2⌋ + 1");
        foreach (var r in rows.Where(r => Str(r, "case") == "errms"))
            Check(Int(r, "rttMs") is { } rtt && Int(r, "errMs") == Err(rtt), "showbus spec: an errms row is 13.11.3's errMs: " + Short(r));
        foreach (var r in rows.Where(r => Str(r, "case") == "clock"))
        {
            var samples = Items(r, "samples").Where(x => x.ValueKind == JsonValueKind.Array && x.GetArrayLength() == 4)
                .Select(x => x.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0).ToArray()).ToList();
            var best = r.TryGetProperty("best", out var b) ? b : default;
            var tol = Num(r, "tol") ?? 0;
            if (samples.Count > 0)
            {
                Check(samples.Count <= window, "showbus spec: a clock row holds no more samples than clock.window: " + Short(r));
                var kept = samples.Where(s => !Refused(s)).ToList();
                if (Int(r, "refused") is { } refused) Check(samples.Count - kept.Count == refused, "showbus spec: a clock row's refused samples are 13.11.2's with clock.rtt.max.ms: " + Short(r));
                if (kept.Count == 0) continue;
                var believed = kept.MinBy(Rtt)!;
                var offset = ((believed[1] - believed[0]) + (believed[2] - believed[3])) / 2.0;
                var err = Err(Rtt(believed));
                Check((Int(r, "rttMs") is not { } rtt || rtt == Rtt(believed)) && (Num(r, "offsetMs") is not { } o || Math.Abs(o - offset) <= tol)
                      && (Int(r, "errMs") is not { } e || Math.Abs(e - err) <= tol),
                    "showbus spec: a clock row believes its smallest rtt, with 13.11.3's offsetMs and errMs: " + Short(r));
                if (Bool(r, "apart") is { } isApart)
                    Check(isApart == Math.Abs(offset) - err > apart, "showbus spec: a clock row is apart past clock.apart.ms: " + Short(r));
            }
            else if (Num(best, "offsetMs") is { } offset && Int(best, "errMs") is { } err)
            {
                if (Bool(r, "apart") is { } isApart)
                    Check(isApart == Math.Abs(offset) - err > apart, "showbus spec: a clock row is apart past clock.apart.ms: " + Short(r));
                if (Int(best, "ageMs") is { } age && Bool(r, "fresh") is { } isFresh)
                    Check(isFresh == age <= fresh, "showbus spec: a clock row is fresh within clock.fresh.ms: " + Short(r));
                foreach (var fit in r.EnumerateObject().Where(x => Regex.IsMatch(x.Name, "^fitFor[1-9][0-9]*$")))
                    Check(Int(best, "ageMs") is { } a && fit.Value.ValueKind is JsonValueKind.True or JsonValueKind.False
                          && (fit.Value.ValueKind == JsonValueKind.True) == (a <= fresh && err <= long.Parse(fit.Name["fitFor".Length..], CultureInfo.InvariantCulture)),
                        "showbus spec: a clock row is fit for a tolerance while fresh within clock.fresh.ms and errMs within it: " + Short(r));
            }
        }

        var final = n.Of("receipt.final.ms");
        foreach (var r in rows.Where(r => Str(r, "case") == "outcome" && Int(Field(r, "answer"), "silenceMs") is not null))
            Check(Int(Field(r, "answer"), "silenceMs") == final + 1 && Str(r, "outcome") == "unconfirmed",
                "showbus spec: the outcome row one past receipt.final.ms is unconfirmed: " + Short(r));
        var commands = Part(s13, 12) ?? "";
        Check(commands.Contains($"an answer at {final} ms still counts", StringComparison.Ordinal) && commands.Contains($"\"no answer in {final / 1000} s", StringComparison.Ordinal),
            "showbus spec: 13.12's words on the final answer are receipt.final.ms's");
        var inflight = n.Of("link.inflight.max");
        Check(commands.Contains($"(\"{inflight} commands are waiting", StringComparison.Ordinal), "showbus spec: 13.12.8's words name link.inflight.max");

        var hold = n.Of("peerlink.hold.refused.ms");
        var stable = n.Of("peerlink.stable.ms");
        foreach (var r in rows.Where(r => Str(r, "case") == "machine"))
        {
            var events = Items(r, "events");
            var expect = Items(r, "expect");
            var name = "machine " + Str(r, "name");
            if (events.Count == 1 && At(events[0]) == 0 && Word(events[0]) == "verified" && events[0].GetArrayLength() == 2)
                foreach (var e in expect.Where(e => Str(Arg(e, 1), "state") is not null))
                    Check(Str(Arg(e, 1), "state") == Liveness(At(e)), "showbus spec: a row that only verifies holds with the liveness bounds: " + name);
            var closes = Closes(events, stable, drop);
            foreach (var x in expect.Where(e => Field(Arg(e, 1), "waitMs").ValueKind == JsonValueKind.Array))
            {
                var range = Field(Arg(x, 1), "waitMs");
                var close = closes.LastOrDefault(c => c.At <= At(x), (-1, 0));
                Check(close.At >= 0 && range.GetArrayLength() == 2 && range.EnumerateArray().All(v => v.ValueKind == JsonValueKind.Number)
                      && range[0].GetDouble().Equals(Math.Round(Base(close.Failed) * (1 - p), MidpointRounding.AwayFromZero))
                      && range[1].GetDouble().Equals(Math.Round(Base(close.Failed) * (1 + p), MidpointRounding.AwayFromZero)),
                    $"showbus spec: a waitMs range is 13.15.1's for the dials failed since the link was last stable (n = {close.Failed}, by peerlink.stable.ms): " + name);
            }
            foreach (var count in events.Where(e => Word(e) == "send").Select(e => Int(Arg(e, 2), "count")))
                if (expect.Select(e => Arg(e, 1)).FirstOrDefault(f => Int(f, "inFlight") is not null) is { ValueKind: JsonValueKind.Object } f)
                    Check(count == inflight + 1 && Int(f, "inFlight") == inflight && Str(Field(f, "outcome"), (inflight + 1).ToString(CultureInfo.InvariantCulture)) == "not-sent",
                        "showbus spec: the command past link.inflight.max is not-sent: " + name);
            if (expect.FirstOrDefault(e => Int(Arg(e, 1), "heldUntil") is not null) is not { ValueKind: JsonValueKind.Array } held) continue;
            var heldAt = At(held);
            var refusal = events.LastOrDefault(e => At(e) <= heldAt
                && (Word(e) == "refused" || (Word(e) == "side-call" && Int(Arg(e, 2), "http") == 401 && Bool(Field(Arg(e, 2), "body"), "relink") != true)));
            Check(refusal.ValueKind == JsonValueKind.Array, "showbus spec: a row with heldUntil holds a refusal before it: " + name);
            if (refusal.ValueKind != JsonValueKind.Array) continue;
            var end = At(refusal) + Math.Max(hold, (Int(Arg(refusal, 2), "retry") ?? 0) * 1000);
            Check(Int(Arg(held, 1), "heldUntil") == end, $"showbus spec: a hold ends peerlink.hold.refused.ms, or the retry if longer, after its refusal ({end}): " + name);
            foreach (var e in expect.Where(e => At(e) >= heldAt && Field(Arg(e, 1), "dial").ValueKind != JsonValueKind.Undefined))
                Check((Field(Arg(e, 1), "dial").ValueKind == JsonValueKind.Null) == (At(e) < end), "showbus spec: nothing is dialled before a hold ends, and a dial goes at its end: " + name);
        }

        // §2 and 13.15.3 over the conflict row: its hellos keep the peer live and alternate (A, B, A) within peer.stale.ms, then say A alone;
        // nothing is dialled while the mark stands, and the connector dials again once peer.conflict.clear.ms pass without the alternation.
        var conflict = rows.FirstOrDefault(r => Str(r, "case") == "machine" && Str(r, "name") == "a conflict holds until its mark clears");
        Check(conflict.ValueKind == JsonValueKind.Object, "showbus spec: 13.20's block holds §2's conflict row (a conflict holds until its mark clears)");
        if (conflict.ValueKind == JsonValueKind.Object)
        {
            var heard = Items(conflict, "events").Where(e => Word(e) == "hello").Select(e => (At: At(e), Inst: Str(Arg(e, 2), "inst") ?? "")).ToList();
            var heardFor = n.Of("peer.stale.ms");
            var marked = Enumerable.Range(2, Math.Max(0, heard.Count - 2))
                .Where(i => heard[i].Inst == heard[i - 2].Inst && heard[i].Inst != heard[i - 1].Inst)
                .Select(i => heard[i]).FirstOrDefault((-1L, ""));
            var clears = marked.Item1 + n.Of("peer.conflict.clear.ms");
            var dials = Items(conflict, "expect").Where(e => Field(Arg(e, 1), "dial").ValueKind != JsonValueKind.Undefined).ToList();
            Check(marked.Item1 >= 0 && heard.Zip(heard.Skip(1)).All(p => p.Second.At - p.First.At <= heardFor)
                  && heard.Where(h => h.At > marked.Item1).All(h => h.Inst == marked.Item2)
                  && dials.Any(e => At(e) < clears) && dials.Any(e => At(e) > clears)
                  && dials.All(e => At(e) == clears || (Field(Arg(e, 1), "dial").ValueKind == JsonValueKind.Null) == (At(e) < clears)),
                $"showbus spec: the conflict row marks its id when the hellos alternate within peer.stale.ms (at {marked.Item1}), dials nothing while the mark "
                + $"stands, and dials again once peer.conflict.clear.ms pass without the alternation (after {clears})");
        }
    }

    /// <summary>
    /// 13.15.1–2 over a machine row's events: each <c>closed</c> with n, the dials failed since the link was last stable, that the
    /// wait before the next dial takes. A close counts as a failed dial, but for one that ends a link verified at least
    /// <paramref name="stable"/> before it and never silent past <paramref name="drop"/> meanwhile, which starts n again from 0.
    /// </summary>
    static List<(long At, long Failed)> Closes(List<JsonElement> events, long stable, long drop)
    {
        var closes = new List<(long, long)>();
        long failed = 0, verifiedAt = -1, heardAt = -1;
        var wentDown = false;
        foreach (var e in events)
        {
            var t = At(e);
            switch (Word(e))
            {
                case "verified":
                    verifiedAt = heardAt = t;
                    wentDown = false;
                    break;
                case "heard" or "frame" or "beat" or "beat-stuck" or "pong" or "pong-stuck" when verifiedAt >= 0:
                    wentDown |= t - heardAt > drop;
                    heardAt = t;
                    break;
                case "closed":
                    if (verifiedAt >= 0 && !wentDown && t - heardAt <= drop && t - verifiedAt >= stable) failed = 0;
                    closes.Add((t, failed));
                    failed++;
                    verifiedAt = -1;
                    break;
            }
        }
        return closes;
    }

    /// <summary>A machine event's or expectation's time; -1 when it has none.</summary>
    static long At(JsonElement e)
        => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 0 && e[0].ValueKind == JsonValueKind.Number && e[0].TryGetInt64(out var ms) ? ms : -1;

    /// <summary>A machine event's word; null when it has none.</summary>
    static string? Word(JsonElement e)
        => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > 1 && e[1].ValueKind == JsonValueKind.String ? e[1].GetString() : null;

    static JsonElement Arg(JsonElement e, int i) => e.ValueKind == JsonValueKind.Array && e.GetArrayLength() > i ? e[i] : default;

    static JsonElement Field(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    static bool? Bool(JsonElement e, string name) => Field(e, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    static string Ms(double ms) => Math.Round(ms, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ what §13 and §14 say Stagewatch already does

    static readonly DateTime Tonight = new(2026, 10, 25, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Stagewatch's code against §13 and §14: its keys and level-0 proofs against the vectors; each row of §2, §6, §10, §11 and §12,
    /// and of 1.2's §3–§5 words, its code keeps, held to its value where this suite reaches that code, in the role each check's words
    /// name; each row of
    /// <see cref="NotKeptYet"/> still apart from its code; and every row of those sections in one of these or in
    /// <see cref="Unreached"/> or <see cref="NotStagewatchs"/>. §13's other rows bind a node once it offers LINK (13.20), which
    /// Stagewatch does not yet: <see cref="PeerLinkVectors"/> holds its PeerLink core, which its links do not run on yet, to them.
    /// </summary>
    static void TodaysCode(Action<bool, string> Check, string s13, List<JsonElement> rows, Numbers n)
    {
        // 13.5.1: Stagewatch's swk_ keys are 13.5.1 keys, and what it stores for one is K.
        var (key, secret) = ApiKeys.Create("Hall B's playout", ApiKeyScope.Node, Tonight);
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

        var kept = new HashSet<string>(StringComparer.Ordinal);
        void Keeps(bool ok, string words, params string[] names)
        {
            kept.UnionWith(names);
            Check(ok, "showbus spec: Stagewatch keeps " + string.Join(", ", names) + ": " + words);
        }

        Keeps(alphabet.Length == 32 && 32 * Math.Log2(alphabet.Length) >= n.Of("peerlink.key.bits.min"), "a swk_ key holds at least that many random bits", "peerlink.key.bits.min");

        // §2: the hello and its reader.
        Keeps(SlidesBeacon.Port == n.Of("hello.port") && PatternsLink.HelloPort == n.Of("hello.port"), "it says and hears hellos on that port", "hello.port");
        Keeps((long)SlidesBeacon.Every.TotalMilliseconds == n.Of("hello.every.ms"), "it says a hello that often", "hello.every.ms");
        Keeps(SlidesBeacon.ReadBytes == n.Of("hello.read.bytes") && SlidesBeacon.KeptPorts == n.Of("hello.ports") && SlidesBeacon.KindChars == n.Of("hello.kind.chars"),
            "its hello reader takes a datagram that long, keeps that many doors and a kind that long", "hello.read.bytes", "hello.ports", "hello.kind.chars");
        var capWord = new string('a', (int)n.Of("hello.cap.chars"));
        Keeps(ShowBusHello.CapChars == n.Of("hello.cap.chars") && ShowBusHello.CapShape(capWord) && !ShowBusHello.CapShape(capWord + "a")
              && ShowBusHello.Read(HelloDatagram("FOH", "FOH-PC", caps: [capWord, capWord + "a", "Video"]))?.Caps?.SequenceEqual([capWord]) == true,
            "its hello reader keeps a cap word that long of §2's shape and ignores any other", "hello.cap.chars");
        Keeps(SlidesBeacon.IdChars == n.Of("hello.id.max") && SlidesBeacon.ValidToken(new string('a', (int)n.Of("hello.id.min")), SlidesBeacon.IdChars),
            "its hello reader takes an id that short and that long", "hello.id.min", "hello.id.max");
        static string Liveness(double ageSeconds) => new PeerSummary("patterns", "9f8e7d6c", "FOH", Light.Green, "", "", ageSeconds, false).Liveness;
        double Seconds(string name) => n.Of(name) / 1000.0;
        Keeps(Liveness(Seconds("peer.stale.ms")) == "live" && Liveness(Seconds("peer.stale.ms") + 0.001) == "stale"
              && Liveness(Seconds("peer.gone.ms")) == "stale" && Liveness(Seconds("peer.gone.ms") + 0.001) == "gone",
            "a peer it hears is live, stale, then gone past those ages", "peer.stale.ms", "peer.gone.ms");
        Keeps((long)SlidesBeacon.ForgetAfter.TotalMilliseconds == n.Of("peer.forget.ms"), "its hello reader forgets a peer unheard that long", "peer.forget.ms");
        HelloCode(Check, Keeps, n);

        // §3–§5: request ids and receipts, /ws's beat and messages, the Patterns link's pings, drop and redial, the clock.
        var rid = (int)n.Of("request.id.chars");
        Keeps(CommandReceipt.CleanId(new string('a', rid)) is not null && CommandReceipt.CleanId(new string('a', rid + 1)) is null,
            "it takes a request id that long and no longer", "request.id.chars");
        Keeps((long)WebServer.ReceiptWait.TotalMilliseconds == n.Of("receipt.deadline.ms"), "a receipt says accepted when the command is not applied within it", "receipt.deadline.ms");
        Keeps((long)WebServer.HeartbeatEvery.TotalMilliseconds == n.Of("link.beat.ms"), "/ws sends a frame or a beat at least that often", "link.beat.ms");
        Keeps(WebServer.MaxWsMessageBytes == n.Of("link.ws.in.bytes"), "/ws takes a message that long", "link.ws.in.bytes");
        var ping = (long)PatternsLink.PingEvery.TotalMilliseconds;
        Keeps(ping >= n.Of("ping.min.ms") && ping <= n.Of("ping.max.ms"), "PatternsLink pings within that span", "ping.min.ms", "ping.max.ms");
        Keeps((long)PatternsLink.DeadAfter.TotalMilliseconds == n.Of("link.drop.ms"), "PatternsLink drops a link silent past it", "link.drop.ms");
        Keeps(PatternsLink.RedialMaxMs == n.Of("link.redial.max.ms"), "PatternsLink waits no longer before a redial", "link.redial.max.ms");
        Keeps(PatternsLink.MaxLine == n.Of("link.read.min.bytes"), "PatternsLink reads a line that long", "link.read.min.bytes");
        Keeps(ClockOffsetEstimator.Window == n.Of("clock.window") && (long)ClockOffsetEstimator.ApartAfter.TotalMilliseconds == n.Of("clock.apart.ms"),
            "its clock keeps that many samples and is apart past that offset", "clock.window", "clock.apart.ms");
        var rttMax = n.Of("clock.rtt.max.ms");
        var within = new ClockOffsetEstimator();
        within.Add(1000, 1000, 1000, 1000 + rttMax);
        var beyond = new ClockOffsetEstimator();
        beyond.Add(1000, 1000, 1000, 1000 + rttMax + 1);
        Keeps(within.Offset is not null && beyond.Offset is null, "its clock takes a sample of that rtt and refuses one past it", "clock.rtt.max.ms");

        // §6: a peer's context as PeerContexts reads it, and Stagewatch's own.
        Keeps((long)PeerContexts.KeepFor.TotalMilliseconds == n.Of("context.kept.ms") && PeerContexts.MaxBytes == n.Of("context.bytes"),
            "PeerContexts keeps a posted context that long and takes one that big", "context.kept.ms", "context.bytes");
        string Xs(string name) => new('x', (int)n.Of(name));
        var item = $"{{\"id\":\"{Xs("context.item.id.chars")}\",\"kind\":\"{Xs("context.item.kind.chars")}\",\"label\":\"{Xs("context.item.label.chars")}\",\"light\":\"green\",\"state\":\"{Xs("context.item.state.chars")}\"}}";
        var context = Peer($",\"light\":\"green\",\"headline\":\"{Xs("context.headline.chars")}\",\"items\":[{item}]" + Many("recent", $"{{\"text\":\"{Xs("context.recent.chars")}\"}}", n.Of("context.recent")));
        Keeps(context is { Items.Count: 1 } c && c.Headline.Length == n.Of("context.headline.chars") && c.Items[0].Id.Length == n.Of("context.item.id.chars")
              && c.Items[0].Kind.Length == n.Of("context.item.kind.chars") && c.Items[0].Label.Length == n.Of("context.item.label.chars")
              && c.Items[0].State.Length == n.Of("context.item.state.chars") && c.Recent.Count == n.Of("context.recent")
              && c.Recent.All(x => x.Length == n.Of("context.recent.chars")),
            "PeerContexts reads a context's headline, items and recent lines whole at those sizes", "context.headline.chars", "context.item.id.chars",
            "context.item.kind.chars", "context.item.label.chars", "context.item.state.chars", "context.recent", "context.recent.chars");
        var hellos = Enumerable.Range(0, (int)n.Of("context.peers") + 1).Select(i => new HeardNode("patterns", "pat" + i.ToString("D4", CultureInfo.InvariantCulture), "P", "10.0.1.1", 1));
        Keeps(ShowNetwork.Summaries(hellos, Array.Empty<HeldContext>(), Array.Empty<NodeLinkFact>(), "desk01", Tonight).Rows.Count == n.Of("context.peers"),
            "its own context lists that many peers at most", "context.peers");
        Keeps(ShowContext.Commands.Count <= n.Of("context.commands")
              && ShowContext.Commands.All(x => x.Verb.Length <= n.Of("context.verb.chars") && x.Words.Length <= n.Of("context.words.chars")),
            "its own context lists no more commands, with no longer verbs or words", "context.commands", "context.verb.chars", "context.words.chars");
        OwnContextSizes(Keeps, n);

        // §10: CertificateLife holds the certificate's numbers; NodeCertificate (Timer.App, not built beside this suite on every
        // platform) is held to making and keeping one only through it, from its source.
        CertificateCode(Keeps, n);

        // §11 and §12.
        Keeps((long)ProposalDesk.CollectWithin.TotalMilliseconds == n.Of("proposal.undelivered.ms") && (long)ProposalDesk.ResultWithin.TotalMilliseconds == n.Of("proposal.result.ms"),
            "its queue marks a proposal undelivered after that long uncollected, or that long with no result", "proposal.undelivered.ms", "proposal.result.ms");
        var tokens = Enumerable.Range(0, 64).Select(_ => ScreenWindows.NewToken()).ToList();
        var symbols = n.Values.GetValueOrDefault("window.token.alphabet", "");
        Keeps(ScreenWindows.TokenLength == n.Of("window.token.chars") && ScreenWindows.Max == n.Of("window.max")
              && tokens.All(x => x.Length == n.Of("window.token.chars") && x.All(symbols.Contains)),
            "its window addresses are that many of those symbols, and it keeps that many windows at most", "window.token.chars", "window.token.alphabet", "window.max");
        ViewPassCode(Keeps, n);

        foreach (var (row, unit, today, differs) in NotKeptYet)
            Check(differs is null || differs(n),
                $"showbus spec: Stagewatch does not keep {row} yet ({unit}): {today} — once its code keeps the row, the row leaves ShowBusSpecTests.NotKeptYet for a check of its own");

        // Every row §14.4 gives §2, §6, §10, §11 or §12, and every row 1.2's §3–§5 words, is held here, not kept yet, not reached yet,
        // or not Stagewatch's.
        var later = NotKeptYet.Select(x => x.Row).ToHashSet(StringComparer.Ordinal);
        var unreached = Unreached.Select(x => x.Row).ToHashSet(StringComparer.Ordinal);
        var others = NotStagewatchs.Select(x => x.Row).ToHashSet(StringComparer.Ordinal);
        var named = kept.Concat(later).Concat(unreached).Concat(others).Concat(Before13).ToList();
        var strays = named.Where(x => x != "proto" && !n.Values.ContainsKey(x)).Distinct().ToList();
        Check(strays.Count == 0, "showbus spec: every row Stagewatch's code is held to here is a §14 row (" + string.Join(", ", strays) + ")");
        var clashes = unreached.Where(x => kept.Contains(x) || later.Contains(x)).Concat(others.Where(x => kept.Contains(x) || later.Contains(x) || unreached.Contains(x))).ToList();
        Check(clashes.Count == 0, "showbus spec: a row Stagewatch's code is not reached for, or not bound by, is held nowhere else here (" + string.Join(", ", clashes) + ")");
        var sections = n.Values.Keys.Where(k => OtherSections.Any(x => k.StartsWith(x, StringComparison.Ordinal))).Concat(Before13).Append("proto");
        var loose = sections.Where(x => !kept.Contains(x) && !later.Contains(x) && !unreached.Contains(x) && !others.Contains(x)).Order(StringComparer.Ordinal).ToList();
        Check(loose.Count == 0,
            "showbus spec: every row of §2, §6, §10, §11 and §12, and of §3–§5's 1.2 words, is one Stagewatch's code keeps (held here), does not keep yet (NotKeptYet), "
            + "is not reached here yet (Unreached) or does not bind (NotStagewatchs) (" + string.Join(", ", loose) + ")");
    }

    /// <summary>
    /// What §12 says a window is told, reports and shows a host browser, against Stagewatch's server and its window shell (the shell
    /// is a page, read from source): the alphabet is exactly the window alphabet, five bits a symbol, and a page that needs the PIN or
    /// another site is never content; a window hears 12.3's window message and 12.3's end, its link is never a node link, and it reports
    /// 12.3's shown fields; the shell's root carries 12.9's data attributes with 12.9's values, and its state names what is loading. The
    /// draw messages of 12.6 carry 12.3's fields, and 12.7's black end is 12.3's end with black. A window asked to identify or reload
    /// hears 12.3's messages; a locked device is never told its address is gone (12.2); a window's item is 12.10's. The header names cap
    /// windows in the hello exactly while the code lacks it, among what 2.19 brings (<see cref="Brings"/> holds its rows), and nothing of
    /// 12.2, whose page rule and revocation for viewing codes and crew links no unit of 2.19 ends: §8's Stagewatch row names them
    /// (<see cref="WordsNotKeptYet"/>).
    /// </summary>
    static void WindowsCode(Action<bool, string> Check, string header, string s12, Numbers n)
    {
        // 12.1: the token's symbols, and what a window may show.
        var symbols = n.Values.GetValueOrDefault("window.token.alphabet", "");
        var length = (int)n.Of("window.token.chars");
        var strangers = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".Where(c => !symbols.Contains(c)).ToList();
        Check(symbols.Length == 32 && symbols.All(c => ScreenWindows.ValidToken(new string(c, length))) && strangers.All(c => !ScreenWindows.ValidToken(new string(c, length))),
            "showbus spec: Stagewatch keeps 12.1: a window's address takes exactly window.token.alphabet's symbols, 32 of them, five bits each (outside it: "
            + string.Join("", strangers.Where(c => ScreenWindows.ValidToken(new string(c, length)))) + ")");
        Check(ScreenWindows.CanShow("/clock", out _) && !ScreenWindows.CanShow("/control", out _) && !ScreenWindows.CanShow("https://example.com/clock", out _),
            "showbus spec: Stagewatch keeps 12.1: a window shows a page that needs no credential, never one that needs the PIN or another site");

        // 12.3: what a window hears, and how its link ends.
        var link = WindowPart(s12, 3) ?? "";
        var window = Shape(link, "window");
        var lobby = new ScreenWindow { Id = "wk3m9x2", Name = "lobby", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 3 };
        var told = JsonDocument.Parse(WebServer.WindowJson(lobby)).RootElement;
        var toldFields = told.EnumerateObject().Select(p => p.Name).ToList();
        Check(window is not null && Str(told, "type") == "window" && toldFields.SequenceEqual(window.Prepend("type")) && !told.GetRawText().Contains(lobby.Token, StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps 12.3: a window hears its window message's fields, in its order, and never its address (" + string.Join(", ", toldFields) + ")");
        Check(link.Contains("`" + WebServer.WindowGoneJson + "`", StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps 12.3: a window whose address stopped working hears " + WebServer.WindowGoneJson);
        var nodeAndWindow = new Dictionary<string, string>(StringComparer.Ordinal) { ["node"] = "9f8e7d6c", ["t"] = "patterns", ["win"] = ScreenWindows.NewToken() };
        Check(NodeLinks.BadRequest(nodeAndWindow, null, "3f9c2a1b7d0e") == NodeLinks.WindowNotNode && Flat(link).Contains(NodeLinks.WindowNotNode, StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps 12.3: win with node is refused before the upgrade, in 12.3's words");
        var identify = JsonDocument.Parse(WebServer.IdentifyJson("lobby", "wk3m9x2")).RootElement;
        var identifyFields = identify.EnumerateObject().Select(p => p.Name).ToList();
        Check(Shape(link, "identify") is { } asked && Str(identify, "type") == "identify" && identifyFields.SequenceEqual(asked.Prepend("type")),
            "showbus spec: Stagewatch keeps 12.3: a window asked to identify hears 12.3's identify message, in its order (" + string.Join(", ", identifyFields) + ")");
        Check(link.Contains("`" + WebServer.ReloadJson + "`", StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps 12.3: a window asked to reload hears " + WebServer.ReloadJson);

        // 12.2: a device locked out of view passes is never told its address is gone: its window link closes, its page says wait.
        // WindowFor reads the lock once and says locked apart from unknown, and both callers take its answer, so a device that
        // reaches the limit as it asks is told to wait, never that a right address is gone.
        var server = Source("src/Timer.Remote/WebServer.cs") ?? "";
        var windowPage = Method(server, "private async Task HandleWindowPageAsync(") ?? "";
        var windowFor = Method(server, "private ScreenWindow? WindowFor(") ?? "";
        Check(Regex.IsMatch(windowFor, @"locked = PassesLockedOut\(remote\);\s*if \(locked\) return null;")
              && Regex.IsMatch(server, @"WindowFor\(winToken, remote, out lockedWindow\)") && Regex.IsMatch(server, @"if \(!lockedWindow\) await ws\.SendAsync\(WindowGone\b")
              && Regex.IsMatch(windowPage, @"WindowFor\(token, remote, out var locked\);\s*if \(locked\)\s*\{\s*await WriteAsync\(stream, 429,")
              && Flat(WindowPart(s12, 2) ?? "").Contains("A locked device is never told its address is gone: a page it asks for says to wait, and a window's link is closed and tries again", StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps 12.2: a locked device's window link is closed without the gone message, and its window page says wait (429)");

        // 12.6 and 12.7 against 12.3: a draw grant's messages carry a window's fields, and a page grant's end is a window's end, black.
        var messages = WindowPart(s12, 6) ?? "";
        var shown = Shape(link, "shown");
        Check(Shape(messages, "window-assign") is { } assign && window is not null && assign.SequenceEqual(window.Select(f => f == "id" ? "window" : f)),
            "showbus spec: 12.6's window-assign is 12.3's window message, the window named by window");
        Check(Shape(messages, "window-shown") is { } drawn && shown is not null && drawn.SequenceEqual(shown.Prepend("window")),
            "showbus spec: 12.6's window-shown is 12.3's report, the window named by window");
        Check((WindowPart(s12, 7) ?? "").Contains("`" + WebServer.WindowGoneJson[..^1] + ",\"black\":true}`", StringComparison.Ordinal),
            "showbus spec: 12.7's black end is 12.3's end with black");

        // 12.3 and 12.9 against the window shell.
        var shell = Source("src/Timer.Remote/wwwroot/window.js");
        Check(shell is not null, "showbus spec: the window shell (src/Timer.Remote/wwwroot/window.js) is in the tree");
        if (shell is null) return;
        var reported = Regex.Match(shell, @"type: 'shown', ([^}]+)\}") is { Success: true } r ? Regex.Matches(r.Groups[1].Value, @"(\w+):").Select(m => m.Groups[1].Value).ToList() : [];
        Check(shown is not null && reported.SequenceEqual(shown), "showbus spec: Stagewatch keeps 12.3: a window reports the shown fields, in their order (" + string.Join(", ", reported) + ")");
        var drawing = WindowPart(s12, 9) ?? "";
        var attributes = Regex.Matches(shell, @"setAttribute\('data-([a-z]+)'").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var named = Regex.Matches(drawing, "`data-([a-z]+)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Check(attributes.SetEquals(named), "showbus spec: Stagewatch keeps 12.9: the shell's root carries exactly the data attributes 12.9 names ("
            + string.Join(", ", attributes.Order(StringComparer.Ordinal)) + " / " + string.Join(", ", named.Order(StringComparer.Ordinal)) + ")");
        foreach (var attribute in new[] { "link", "hold" })
        {
            var values = Regex.Matches(shell, @"setAttribute\('data-" + attribute + @"', ([^)]*)\)").SelectMany(m => Regex.Matches(m.Groups[1].Value, "'([^']*)'"))
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var said = Regex.Match(Flat(drawing), "`data-" + attribute + @"` \(([^)]*)\)") is { Success: true } v
                ? Regex.Matches(v.Groups[1].Value, "`([^`]*)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal) : [];
            Check(values.Count > 0 && values.SetEquals(said), $"showbus spec: Stagewatch keeps 12.9: the shell's data-{attribute} takes exactly 12.9's values ("
                + string.Join(", ", values.Order(StringComparer.Ordinal)) + " / " + string.Join(", ", said.Order(StringComparer.Ordinal)) + ")");
        }
        var state = Regex.Match(shell, @"state: function \(\) \{\s*return \{([^}]*)\}") is { Success: true } st
            ? Regex.Matches(st.Groups[1].Value, @"(\w+):").Select(m => m.Groups[1].Value).ToList() : [];
        Check(drawing.Contains("`SWShell.state()`, whose `loading`", StringComparison.Ordinal) && shell.Contains("window.SWShell = {", StringComparison.Ordinal) && state.Contains("loading"),
            "showbus spec: Stagewatch keeps 12.9: SWShell.state() says what is loading (" + string.Join(", ", state) + ")");

        // 12.10: a window's item is window: and its name in lower case, a screen, grey until opened, amber when its link is lost, never red.
        var hall = new ScreenWindow { Id = "wq8r4t1", Name = "Hall-A", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 2 };
        var foyer = new ScreenWindow { Id = "wz5n6p7", Name = "Foyer", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 4 };
        var items = ScreenWindows.ContextItems([hall, foyer], [new WindowObserved(foyer.Id, 0, 4, "/clock", false, 40, Tonight, "", "", "")]);
        var placement = Flat(WindowPart(s12, 10) ?? "");
        var claimed = Regex.Match(placement, @"""from"":""@[0-9a-z]+/window:([^""]+)""") is { Success: true } c ? c.Groups[1].Value : "";
        Check(items.Select(i => i.Id).SequenceEqual(["window:hall-a", "window:foyer"]) && items.All(i => i.Kind == "screen")
              && items.Select(i => i.Light).SequenceEqual([Light.Grey, Light.Amber])
              && placement.Contains("Its context has one item per window, `window:` and the window's name in lower case (kind `screen`;", StringComparison.Ordinal)
              && placement.Contains("grey while none has been opened", StringComparison.Ordinal) && claimed.Length > 0 && claimed == claimed.ToLowerInvariant(),
            "showbus spec: Stagewatch keeps 12.10: a window's item is window: and its name in lower case, kind screen, grey until opened and amber once its link is lost ("
            + string.Join(", ", items.Select(i => i.Id + " " + i.Kind + " " + i.Light)) + ")");

        // The header: what of §12 past its rows 2.19 brings, named exactly while the code still lacks it (its rows: Brings). What of 12.2 no
        // unit of 2.19 brings — its page rule and its revocation for viewing codes and crew links — is §8's Stagewatch row's
        // (WordsNotKeptYet), never the header's.
        var sentence = Brought(header);
        Check(!sentence.Contains("12.2's", StringComparison.Ordinal),
            "showbus spec: the header names nothing of 12.2 among what 2.19 brings: no unit of 2.19 ends its page rule or its revocation for viewing codes and crew links");
        // The desk and the rig say the hello ShowBusHello.Main builds (HelloCode holds them to it), so its caps are the builder's.
        var sends = ShowBusHello.Main("a1b2c3d4e5f6", ShowBusHello.NewInst(), "Hall A", "FOH-PC", new HelloDoors(9924, true, null, 0, false, 0, false), false, 0)
            .Hello?.Caps?.Contains("windows") == true;
        Check(sentence.Contains("cap `windows` in its hello", StringComparison.Ordinal) != sends,
            "showbus spec: the header says Stagewatch's hello lacks cap windows exactly while its hello builder (ShowBusHello.Main) lacks it"
            + (sends ? " — it sends it now: take it out of the header's list" : ""));
    }

    /// <summary>
    /// The text of the method of <paramref name="source"/> that starts with <paramref name="signature"/>, up to its closing brace at a
    /// class member's indent; null when there is none.
    /// </summary>
    static string? Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        return end < 0 ? null : source[start..end];
    }

    /// <summary>
    /// Whether WebServer still sends no-referrer, noindex and no-framing only with the window page (12.2's page rule, kept for windows and
    /// not for the pages a viewing code or a crew link opens): each header is written in the window page's handler and nowhere else.
    /// </summary>
    static bool PageRuleAtWindowsOnly()
    {
        var server = Source("src/Timer.Remote/WebServer.cs") ?? "";
        var windowPage = Method(server, "private async Task HandleWindowPageAsync(") ?? "";
        int Times(string text, string word) => Regex.Matches(text, Regex.Escape(word)).Count;
        return windowPage.Length > 0 && Times(server, "frame-ancestors") == 1
               && new[] { "Referrer-Policy", "X-Robots-Tag" }.All(x => Times(windowPage, x) > 0 && Times(server, x) == Times(windowPage, x));
    }

    /// <summary>
    /// Whether WebServer still rechecks no socket that only a viewing code or a crew link let in (12.2's revocation at once): a changed
    /// code reaches a screen already open only when it reconnects.
    /// </summary>
    static bool ViewersNeverRechecked()
        => (Method(Source("src/Timer.Remote/WebServer.cs") ?? "", "private void Recheck(WsClient client)") ?? "")
            .Contains("if (link.KeyId is null && link.PinHash is null) return;", StringComparison.Ordinal);

    /// <summary>
    /// Whether Stagewatch still marks no key seen in clear (13.6.9): a stored key has no field for it and the server names no such mark.
    /// </summary>
    static bool SeenInClearAsToday()
        => typeof(ApiKey).GetProperties().All(p => !p.Name.Contains("Clear", StringComparison.Ordinal))
           && Source("src/Timer.Remote/WebServer.cs") is { } server && !Regex.IsMatch(server, "(?i)seen ?in ?clear|InClear");

    /// <summary>
    /// Whether the desk still takes a proposal an mcp key sends: WebServer lets an mcp key post one, reads it as a credential that proves
    /// nothing, and the desk queues a proposal proved by nothing for a node no key is bound to.
    /// </summary>
    static bool McpProposalAsToday()
    {
        var server = Source("src/Timer.Remote/WebServer.cs") ?? "";
        return server.Contains("return Authorize(req, remote, ApiKeyScope.Mcp);", StringComparison.Ordinal)
               && Regex.IsMatch(Method(server, "private NodeCredential CredentialOf(") ?? "", @"_ => NodeCredential\.None")
               && Proposed("{\"from\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"FOH\"},\"target\":{\"t\":\"main\"},\"line\":\"note hi\"}", "low",
                   NodeCredential.None) is not null;
    }

    /// <summary>A check that names the rows it holds Stagewatch's code to (<see cref="TodaysCode"/>'s own).</summary>
    delegate void KeepsRows(bool ok, string words, params string[] names);

    /// <summary>
    /// §2 against the hello Stagewatch says and hears (ShowBusHello, SlidesBeacon, HelloHearing; the desk and the rig only call the
    /// builder): within hello.bytes whatever its name, proto 1.3's, an inst of §2's shape, caps of §14's rows for its kind and doors of
    /// its port rows, only those other machines can open, and no hello while its doors are on this machine only; its reader keeps
    /// hello.caps caps and names of hello.name.chars, at most peers.max ids, every address of an id heard within peer.stale.ms (at most
    /// peer.addresses) with the dialled one kept while it is heard, and marks one id said by two running nodes in conflict until
    /// peer.conflict.clear.ms passes without the alternation — never a restart.
    /// </summary>
    static void HelloCode(Action<bool, string> Check, KeepsRows Keeps, Numbers n)
    {
        // The builder, with every door, every cap and the worst names: one that takes three bytes a character, emoji cut at a pair's
        // edge, quotes, an unassigned code point and a line separator (escaped by any encoder).
        var id = new string('a', (int)n.Of("hello.id.max"));
        var inst = ShowBusHello.NewInst();
        var every = new HelloDoors(65535, true, "sha256:" + new string('f', 64), 65534, true, 65533, true);
        string[] names = [new string('€', 80), string.Concat(Enumerable.Repeat("😀", 40)), new string('"', 80), new string('\u0378', 80), new string('\u2028', 80)];
        var built = names.Select(w => ShowBusHello.Main(id, inst, w, w, every, true, long.MaxValue).Hello).ToList();
        var agents = names.Select(w => ShowBusHello.Agent("lectern-" + new string('a', 24), inst, w, w, true, long.MaxValue)).ToList();
        var sizes = built.Concat(agents).Select(h => h is null ? int.MaxValue : ShowBusHello.Bytes(h).Length).ToList();
        Keeps(ShowBusHello.HelloBytes == n.Of("hello.bytes") && sizes.All(b => b <= n.Of("hello.bytes")),
            "its hello and its Slides agents' are that many bytes at most, whatever their names (" + string.Join(", ", sizes) + ")", "hello.bytes");
        Keeps(ShowBusHello.Proto == n.Proto && built.Concat(agents).All(h => h?.Proto == n.Proto),
            "its hello and its Slides agents' say the revision whose shapes they speak", "proto");
        var plain = ShowBusHello.Main("a1b2c3d4e5f6", inst, "Hall A", "FOH-PC", new HelloDoors(9924, true, null, 0, false, 0, false), false, 0).Hello;
        var wire = plain is null ? "" : Encoding.UTF8.GetString(ShowBusHello.Bytes(plain));
        Keeps(ShowBusHello.InstChars == n.Of("hello.inst.chars") && Regex.IsMatch(ShowBusHello.NewInst(), "^[0-9a-f]{" + n.Of("hello.inst.chars").ToString(CultureInfo.InvariantCulture) + "}$")
              && wire.Contains("\"inst\":\"" + inst + "\"", StringComparison.Ordinal) && built.Concat(agents).All(h => h?.Inst == inst)
              && ShowBusHello.Read(HelloDatagram("FOH", "FOH-PC", inst: "a1b2c3d4"))?.Inst == "a1b2c3d4"
              && new[] { "A1B2C3D4", "a1b2c3d", "a1b2c3d4e", "g1b2c3d4" }.All(x => ShowBusHello.Read(HelloDatagram("FOH", "FOH-PC", inst: x)) is { Inst: null }),
            "its hello says an inst of that many lowercase hex, new each start, and its reader keeps only one of that shape", "hello.inst.chars");
        var mainCaps = built.SelectMany(h => h?.Caps ?? []).Distinct().ToList();
        var agentCaps = agents.SelectMany(h => h.Caps ?? []).Distinct().ToList();
        Check(mainCaps.Count > 0 && mainCaps.All(c => n.Caps.Any(x => x.Word == c && x.Kinds.Contains("main")))
              && agentCaps.SequenceEqual(["slides"]) && agentCaps.All(c => n.Caps.Any(x => x.Word == c && x.Kinds.Contains("agent")))
              && built.All(h => h is { Caps: { } caps, Ports: { } ports } && caps.Length <= n.Of("hello.caps") && caps.Contains("windows")
                                && ports.Keys.All(n.Ports.Contains) && ports.Count <= n.Of("hello.ports")),
            "showbus spec: Stagewatch's hello says only §14's cap rows for main (windows among them) and its port rows, and its Slides agents' only cap slides ("
            + string.Join(", ", mainCaps.Concat(agentCaps).Where(c => !n.Caps.Any(x => x.Word == c))) + ")");

        // §2's silence on loopback, and ports only every receiver can open.
        var here = new HelloDoors(9924, false, null, 9923, false, 9925, false);
        var lanWeb = new HelloDoors(9924, true, null, 9923, false, 9925, false);
        var said = ShowBusHello.Main("a1b2c3d4e5f6", inst, "Hall A", "FOH-PC", here, false, 0);
        var lan = ShowBusHello.Main("a1b2c3d4e5f6", inst, "Hall A", "FOH-PC", lanWeb, false, 0);
        var desk = Source("src/Timer.App/ViewModels/MainViewModel.Slides.cs") ?? "";
        var rig = Source("tools/Stagewatch.Rig/Program.cs") ?? "";
        Check(said.Scope == HelloScope.None && ShowBusHello.Targets(said.Scope, (int)n.Of("hello.port")).Count == 0
              && ShowBusHello.Main("a1b2c3d4e5f6", inst, "Hall A", "FOH-PC", new HelloDoors(0, true, null, 0, false, 0, false), false, 0).Hello is null
              && lan.Scope == HelloScope.Network && lan.Hello?.Ports?.Keys.Order(StringComparer.Ordinal).SequenceEqual(["http", "ws"]) == true
              && built[0]?.Ports?.Keys.Order(StringComparer.Ordinal).SequenceEqual(["companion", "http", "https", "osc", "ws"]) == true
              && ShowBusHello.Targets(HelloScope.Network, 9928).Select(t => t.ToString()).SequenceEqual(["255.255.255.255:9928", "127.0.0.1:9928"])
              && ShowBusHello.Doors(new BoundDoor(9924, false), null, new BoundDoor(9923, true), null) is { WebBindAll: false } bound
              && ShowBusHello.Main("a1b2c3d4e5f6", inst, "Hall A", "FOH-PC", bound, false, 0).Scope == HelloScope.None
              && new[] { desk, rig }.All(src => src.Contains("ShowBusHello.Main(", StringComparison.Ordinal) && src.Contains("ShowBusHello.Doors(", StringComparison.Ordinal)
                                               && src.Contains(".Door", StringComparison.Ordinal) && !src.Contains("new HelloDoors(", StringComparison.Ordinal))
              && desk.Contains("_store.Snapshot", StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps §2's silence on loopback: with its web server bound to this machine, or none, it says no hello; else it "
            + "broadcasts and copies to this machine, naming only the doors other machines can open — as the desk and the rig both build it, "
            + "each from its servers' doors as they bound them (ShowBusHello.Doors), never a door of their own making");

        // The reader: caps and names.
        var many = Enumerable.Range(0, (int)n.Of("hello.caps") + 8).Select(i => "c" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
        Keeps(SlidesBeacon.KeptCaps == n.Of("hello.caps") && ShowBusHello.Read(HelloDatagram("FOH", "FOH-PC", caps: many))?.Caps?.SequenceEqual(many.Take((int)n.Of("hello.caps"))) == true,
            "its hello reader keeps that many caps", "hello.caps");
        var name = new string('n', (int)n.Of("hello.name.chars"));
        var heardName = ShowBusHello.Read(HelloDatagram(name, name + "h"));
        Keeps(SlidesBeacon.NameChars == n.Of("hello.name.chars") && heardName is { } hn && hn.Name == name && hn.Host == name
              && ShowBusHello.Main("a1b2c3d4e5f6", inst, name + "x", name + "y", lanWeb, false, 0).Hello is { } mine && mine.Name == name && mine.Host == name,
            "its hello says a name and a host that long at most, and its reader keeps them whole to that length", "hello.name.chars");

        // The hearing, on injected time.
        long now = 0;
        SlidesBeacon.Hello From(string node, string? inst = null, string host = "FOH-PC") => new("patterns", 1, node, "FOH", host, 0, false, Host: host, Inst: inst);
        var full = new HelloHearing(() => now);
        var max = (int)n.Of("peers.max");
        var taken = Enumerable.Range(0, max + 1).Count(i => full.Hear(From("pat" + i.ToString("D4", CultureInfo.InvariantCulture)), "10.0.0.5"));
        Keeps(SlidesBeacon.MaxPeers == max && taken == max && full.Peers().Count == max && !SlidesBeacon.HasRoom(max) && SlidesBeacon.HasRoom(max - 1),
            "its hello reader holds that many ids at most and drops a new one past them", "peers.max");

        var stale = n.Of("peer.stale.ms");
        var kept = (int)n.Of("peer.addresses");
        var addresses = new HelloHearing(() => now);
        now = 0;
        for (var i = 1; i <= kept + 2; i++) addresses.Hear(From("9f8e7d6c"), "10.0.0." + i.ToString(CultureInfo.InvariantCulture));
        var one = addresses.Peers().Single();
        now = stale;
        addresses.Hear(From("9f8e7d6c"), "10.0.0.9");
        var held = addresses.Peers().Single().Address;
        now = stale + 1;
        addresses.Hear(From("9f8e7d6c"), "10.0.0.9");
        var moved = addresses.Peers().Single();
        Keeps(SlidesBeacon.KeptAddresses == kept && one.Addresses.Count == kept && one.Address == "10.0.0.1" && one.Addresses.Contains("10.0.0.1")
              && (long)SlidesBeacon.StaleAfter.TotalMilliseconds == stale && held == "10.0.0.1" && moved.Address == "10.0.0.9" && moved.Addresses.SequenceEqual(["10.0.0.9"]),
            "its hello reader keeps an id's addresses heard within that long, that many at most, and dials the first while it is heard",
            "peer.addresses", "peer.stale.ms");
        Check(one.Addresses.Count == kept && held == "10.0.0.1",
            "showbus spec: Stagewatch keeps §2's every address per id: one id heard from many addresses is one peer, dialled where it was first heard while that is heard");

        var clear = n.Of("peer.conflict.clear.ms");
        var two = new HelloHearing(() => now);
        now = 0;
        two.Hear(From("3f9c2a1b7d0e", "aaaaaaaa", "FOH-PC"), "10.0.0.5");
        now = 1000;
        two.Hear(From("3f9c2a1b7d0e", "bbbbbbbb", "FOH-PC2"), "10.0.0.7");
        var oneSwitch = two.Peers().Single().Conflict is null;   // A then B: a restart, until A comes back
        now = 2000;
        two.Hear(From("3f9c2a1b7d0e", "aaaaaaaa", "FOH-PC"), "10.0.0.5");
        var marked = two.Peers().Single().Conflict;
        // Only A from here (the clone stopped), still heard, so the id is never forgotten while its mark runs out.
        for (now = 3000; now <= 2000 + clear; now += 1000) two.Hear(From("3f9c2a1b7d0e", "aaaaaaaa", "FOH-PC"), "10.0.0.5");
        now = 2000 + clear;
        var stands = two.Peers().Single().Conflict is not null;
        now = 2000 + clear + 1;
        var cleared = two.Peers().Single().Conflict is null;
        Keeps((long)SlidesBeacon.ConflictClearsAfter.TotalMilliseconds == clear && oneSwitch && stands && cleared
              && marked?.Words == "two machines say they are 3f9c2a1b7d0e: FOH-PC2 at 10.0.0.7 and FOH-PC at 10.0.0.5",
            "its hello reader marks an id two running nodes say in conflict, naming both, and clears the mark that long after the alternation stops",
            "peer.conflict.clear.ms");
    }

    /// <summary>A hello datagram of kind patterns, as another node sends it: its name and host, and its caps and inst when given.</summary>
    static byte[] HelloDatagram(string name, string host, string[]? caps = null, string? inst = null)
        => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["t"] = "patterns", ["v"] = 1, ["proto"] = 3, ["id"] = "9f8e7d6c", ["name"] = name, ["host"] = host, ["caps"] = caps, ["inst"] = inst,
        });

    /// <summary>
    /// How long Stagewatch's own context sends a title, a message and a journal line, each longer than its field: a running order item's
    /// 300-character title, a message of 500 characters (the most the desk keeps of one) sent with an ack and journalled, and a note of
    /// 1000 (the most it keeps of one) — the headline's length, the timer's label's, the speaker cue's state's and the longest recent
    /// line's, as sent.
    /// </summary>
    static (int Headline, int Label, int State, int Recent) OwnSizes()
    {
        var message = new string('m', 500);
        var frame = Running() with
        {
            Title = new string('t', 300),
            Signals = new[] { new SignalView(2, SignalKind.Message, "speaker", message, SignalState.Sent, false, 5, "") }.ToEquatableArray(),
        };
        var input = Own(frame, false, [], [], journal: [new(Tonight, JournalKind.Message, 0, message), new(Tonight, JournalKind.Note, 0, new string('n', 1000))]);
        var sent = ShowBusTests.Read(ShowContext.Build(input));
        return (sent.Headline.Length, sent.Items.FirstOrDefault(i => i.Id == "timer")?.Label.Length ?? 0,
            sent.Items.FirstOrDefault(i => i.Id == "cue:speaker")?.State.Length ?? 0, sent.Recent.Select(r => r.Length).DefaultIfEmpty(0).Max());
    }

    /// <summary>
    /// §6's sizes and trimming against Stagewatch's own context (ShowBusTests.ContextSizes walks the order step by step). Its sizes are
    /// §14's rows, its name within a hello's; a title, a message and a journal line longer than their fields are sent cut to exactly
    /// their sizes (<see cref="OwnSizes"/>); a crowded room — 64 windows, 8 links, 30 departments standing by, 20 long recent lines and
    /// 32 peers — is sent within context.bytes, context.items, context.links and context.recent, trimmed in §6's order with one grey
    /// context:trimmed item of a table kind saying what it left out in §6's words alone, and every red and amber item sent whole; and
    /// past context.links links it sends context.items items, the context:trimmed item among them, and no more than context.links links.
    /// </summary>
    static void OwnContextSizes(KeepsRows Keeps, Numbers n)
    {
        Keeps(ShowContext.MaxBytes == n.Of("context.bytes") && ShowContext.MaxItems == n.Of("context.items") && ShowContext.MaxLinks == n.Of("context.links")
              && ShowContext.MaxRecent == n.Of("context.recent") && ShowContext.MaxCommands == n.Of("context.commands") && ShowContext.MaxPeers == n.Of("context.peers")
              && ShowContext.HeadlineChars == n.Of("context.headline.chars") && ShowContext.IdChars == n.Of("context.item.id.chars")
              && ShowContext.KindChars == n.Of("context.item.kind.chars") && ShowContext.LabelChars == n.Of("context.item.label.chars")
              && ShowContext.StateChars == n.Of("context.item.state.chars") && ShowContext.RecentChars == n.Of("context.recent.chars")
              && ShowBusText.NameChars == n.Of("hello.name.chars"),
            "its own context's sizes are those rows, its name and its peers' names a hello's", "context.bytes", "context.items", "context.links", "context.recent",
            "context.commands", "context.peers", "context.headline.chars", "context.item.id.chars", "context.item.kind.chars", "context.item.label.chars",
            "context.item.state.chars", "context.recent.chars", "hello.name.chars");
        var (headline, label, state, recent) = OwnSizes();
        Keeps(headline == n.Of("context.headline.chars") && label == n.Of("context.item.label.chars") && state == n.Of("context.item.state.chars")
              && recent == n.Of("context.recent.chars"),
            $"its own context sends a 300-character title, a 500-character message and a 1000-character note cut to those sizes ({headline}, {label}, {state}, {recent})",
            "context.headline.chars", "context.item.label.chars", "context.item.state.chars", "context.recent.chars");

        var room = ShowBusTests.Crowded(Tonight, windows: 64, links: 8, departments: 30, recent: 20, peers: 32, words: 160, letter: '€', name: '€');
        var sent = ShowBusTests.Read(ShowContext.Build(room));
        var (steps, words) = ShowBusTests.LeftOut(room, sent);
        var trimmed = sent.Items.Where(i => i.Id == ShowContext.TrimmedId).ToList();
        var urgent = ShowContext.Assess(room).Items.Where(i => i.Light is Light.Red or Light.Amber).ToList();
        Keeps(sent.Bytes <= n.Of("context.bytes") && sent.Items.Count <= n.Of("context.items") && sent.Links.Count <= n.Of("context.links")
              && sent.Recent.Count <= n.Of("context.recent") && ShowBusTests.OutOfOrder(steps) is null
              && trimmed is [{ } one] && one.Light == Light.Grey && ShowBusKinds.Known(one.Kind) && one.State == words && one.State.Length <= n.Of("context.item.state.chars")
              && Regex.IsMatch(one.State, @"^left out: \d+ (?:recent|links?|commands?|items?)(?:, \d+ (?:recent|links?|commands?|items?))*$")
              && urgent.Count >= 30 && urgent.All(sent.Items.Contains),
            $"its own context in a crowded room is sent within them, trimmed in §6's order, says what it left out in §6's words and sends every red and amber item whole "
            + $"({sent.Bytes} bytes, {sent.Items.Count} items, {sent.Links.Count} links, {sent.Recent.Count} recent; {words}{ShowBusTests.OutOfOrder(steps)})",
            "context.bytes", "context.items", "context.links", "context.recent");
        var many = ShowBusTests.Crowded(Tonight, windows: 0, links: (int)n.Of("context.links") + 50, departments: 0, recent: 0, peers: 0, words: 10, letter: 'x');
        var counted = ShowBusTests.Read(ShowContext.Build(many));
        Keeps(counted.Items.Count == n.Of("context.items") && counted.Links.Count <= n.Of("context.links") && counted.Items[^1].Id == ShowContext.TrimmedId,
            $"its own context with more links than that sends that many items, the context:trimmed item among them, and no more links ({counted.Items.Count}, {counted.Links.Count})",
            "context.items", "context.links");
    }

    /// <summary>
    /// 12.2's count against Stagewatch's code. WrongGuesses, which holds it, is driven on an injected clock: view.wrong.per.minute
    /// wrong view passes in a device's minute lock that device until the minute ends, one fewer locks nothing, another device is
    /// apart. WebServer (read from source, the one door it has) counts them in a table of their own, which nothing but its minute
    /// clears: window addresses (WindowFor), viewing codes (ViewPass) and crew links (CrewLinkFor) count there and never in the
    /// PIN's table, the PIN's door (CheckPin, AuthorizeSecret) neither reads nor clears it, and every view pass's path refuses a
    /// locked device. WebV2Tests.ViewPassCounts holds the same on a real server, by request.
    /// </summary>
    static void ViewPassCode(KeepsRows Keeps, Numbers n)
    {
        var limit = (int)n.Of("view.wrong.per.minute");
        var device = System.Net.IPAddress.Parse("10.0.0.5");
        var other = System.Net.IPAddress.Parse("10.0.0.6");
        var table = new WrongGuesses(WebServer.WrongViewPassesPerMinute);
        for (var i = 0; i < limit - 1; i++) table.Count(device, 1_000 + i);
        var below = table.Locked(device, 2_000);
        table.Count(device, 59_000);
        Keeps(WebServer.WrongViewPassesPerMinute == limit && limit > 0 && !below && table.Locked(device, 59_000) && table.Locked(device, 60_999)
              && !table.Locked(device, 61_000) && !table.Locked(other, 59_000),
            "that many wrong view passes in a device's minute lock it out of view passes until that minute ends, one fewer locks nothing", "view.wrong.per.minute");

        var server = Source("src/Timer.Remote/WebServer.cs") ?? "";
        string Of(string signature) => Method(server, signature) ?? "";
        var passPaths = new[] { "private ScreenWindow? WindowFor(", "private string? ViewPass(", "private string? CrewLinkFor(" }.Select(Of).ToList();
        var pinDoor = new[] { "private string? CheckPin(", "private Denied? AuthorizeSecret(" }.Select(Of).ToList();
        var counted = Regex.IsMatch(Of("private void CountWrongPass("), @"=>\s*_passGuesses\.Count\(")
                      && Regex.IsMatch(Of("private bool PassesLockedOut("), @"=>\s*_passGuesses\.Locked\(")
                      // At the view door, a key that is no crew link counts in a local function of its own, which counts it.
                      && Regex.IsMatch(Of("private string? ViewPass("), @"void CountCrewKey\(\)\s*\{[^{}]*CountWrongPass\(remote\);\s*\}");
        Keeps(counted && passPaths.All(b => b.Contains("CountWrongPass(remote)", StringComparison.Ordinal) && b.Contains("PassesLockedOut(remote)", StringComparison.Ordinal)
                                            && !b.Contains("_pinGuesses", StringComparison.Ordinal))
              && pinDoor.All(b => b.Length > 0 && b.Contains("_pinGuesses.", StringComparison.Ordinal) && !Regex.IsMatch(b, "_passGuesses|CountWrongPass|PassesLockedOut"))
              && !server.Contains("_passGuesses.Clear", StringComparison.Ordinal)
              && Regex.Matches(server, @"_passGuesses\.").Count == 2,
            "wrong window addresses, viewing codes and crew links count in a table of their own, apart from wrong PINs and keys, which no right "
            + "PIN or key clears, and a locked device is refused every view pass (WebV2Tests.ViewPassCounts holds it by request)", "view.wrong.per.minute");
    }

    /// <summary>
    /// A message's text no context may carry (P8), as the desk sends it: found in Stagewatch's context, the text leaked. ASCII, so a
    /// serializer's escaping cannot hide it from a check that reads the JSON as written.
    /// </summary>
    const string Canary = "meet me backstage at nine, a canary";

    /// <summary>An audience question's text no context may carry (P8), as the Q&amp;A page submits it.</summary>
    const string QuestionCanary = "will the slides be shared afterwards, a canary";

    /// <summary>
    /// What §6 says against Stagewatch's code. ShowBusKinds' node kinds are §14's kind rows, and the desk names each kind by its row.
    /// Its own context keeps P1 (a peer's red stays in its peers row), P5 for its timer (red, with no time in its words), P6 for its
    /// own parts but its vMix link (its Patterns link is P2's) and P8's no-secret rule (no window's address, with a message's text in
    /// the fixture as the desk sends it), and its rows use §6's link kinds. As the desk it keeps the latest context it took for a node,
    /// context.kept.ms after it came; no command of its own posts one. Each entry of <see cref="RulesNotKeptYet"/> holds while the code
    /// still does what it says (§8's Stagewatch row names them: <see cref="NodeRows"/>).
    /// </summary>
    static void ContextCode(Action<bool, string> Check, string s6, Numbers n)
    {
        Check(ShowBusKinds.Nodes.SequenceEqual(n.Kinds) && n.Kinds.All(k => ShowBusKinds.Label(k.T) == k.Name && ShowNetwork.AppName(k.T) == k.Name)
              && ShowBusKinds.Label("eye") is null && ShowNetwork.AppName("eye") == "eye",
            "showbus spec: Stagewatch keeps §14's kinds: ShowBusKinds.Nodes are its kind rows, in order, and the desk names each kind by its row ("
            + string.Join(", ", ShowBusKinds.Nodes.Select(k => k.Kind + " " + k.Name)) + ")");

        var lobby = new ScreenWindow { Id = "wk3m9x2", Name = "Lobby", Token = ScreenWindows.NewToken(), Content = "/clock", Revision = 3 };
        var windows = ScreenWindows.ContextItems([lobby], [new WindowObserved(lobby.Id, 1, 3, "/clock", true, 40, Tonight, "", "", "")]);
        var patterns = new PeerSummary("patterns", "9f8e7d6c", "Patterns FOH", Light.Red, "Projector 1 mismatch", "10.0.0.5", 1, true);
        var input = Own(Running(), false,
            [new ContextItem("link:patterns", "playout", "Patterns", Light.Green, "VT1: 0:42 left"), new ContextItem("link:osc", "link", "OSC", Light.Green, "listening"),
             new ContextItem("clock:patterns", "health", "Clock vs Patterns", Light.Amber, "CLOCKS APART"), .. windows],
            [patterns], 120);
        var (light, headline, items) = ShowContext.Assess(input);
        var json = ShowContext.Build(input);

        // P6, for its own parts but its vMix link (P6's entry): the kinds the context builder and the desk's link items write. Its
        // Patterns link's kind is P2's entry.
        var linkItems = Method(Source("src/Timer.App/ViewModels/MainViewModel.ShowBus.cs") ?? "", "private List<ContextItem> LinkItems()") ?? "";
        var own = items.Where(i => i.Id != "link:patterns").Select(i => i.Kind).Distinct().ToList();
        Check(own.Count >= 7 && own.All(ShowBusKinds.Known)
              && linkItems.Contains("? \"playout\" : \"link\"", StringComparison.Ordinal) && linkItems.Contains("new ContextItem(\"clock:patterns\", \"health\",", StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps P6 for its own parts but its vMix link: its timer, schedule, cues, message, links, health and windows are the kind "
            + "table's words, and every integration but vMix and Patterns is a link (" + string.Join(", ", own) + ")");

        // P1, P5 for its timer, P8's no-secret rule, and its rows' kinds.
        Check(light != Light.Red && !headline.Contains(patterns.Headline, StringComparison.Ordinal)
              && items.All(i => i.Light != Light.Red && !i.Id.StartsWith('@') && !i.Id.Contains(patterns.Id, StringComparison.Ordinal)),
            "showbus spec: Stagewatch keeps P1: a peer's red stays in its peers row, never in an item or the headline (" + light + ": " + headline + ")");
        var stuck = ShowContext.Assess(Own(Running(), true, [], [])).Items.FirstOrDefault(i => i.Id == "timer");
        Check(stuck?.Light == Light.Red && !stuck.State.Contains(Running().Text, StringComparison.Ordinal) && !Regex.IsMatch(stuck.State, @"\d:\d\d"),
            "showbus spec: Stagewatch keeps P5 for its timer: a stuck show thread turns it red, its words the show stuck in place of a time (" + stuck?.State + ")");
        Check(windows.Count > 0 && !json.Contains(lobby.Token, StringComparison.Ordinal),
            "showbus spec: Stagewatch keeps P8's no-secret rule: its context carries no window's address (its messages are P8's entry in RulesNotKeptYet)");
        using (var doc = JsonDocument.Parse(json))
        {
            var kinds = Items(doc.RootElement, "links").Select(l => Str(l, "kind") ?? "").Distinct().ToList();
            Check(kinds.Count > 0 && kinds.All(ShowBusKinds.Links.Contains), "showbus spec: Stagewatch's own rows use §6's link kinds (" + string.Join(", ", kinds) + ")");
        }

        // Posting, as the desk: the latest context taken for an id, kept context.kept.ms after it came and no longer; no command posts one.
        var store = new PeerContexts(new NodeBindings());
        static string Posted(string words) => "{\"v\":1,\"node\":{\"t\":\"messenger\",\"id\":\"77aa77aa\",\"name\":\"AGMessage\"},\"headline\":\"" + words + "\"}";
        var came = Tonight.AddSeconds(1);
        var first = store.Accept(Posted("first"), "10.0.0.7", Tonight, "abc123abc123", NodeCredential.Operator).Status;
        var second = store.Accept(Posted("second"), "10.0.0.7", came, "abc123abc123", NodeCredential.Operator).Status;
        var kept = TimeSpan.FromMilliseconds(n.Of("context.kept.ms"));
        Check(first == 200 && second == 200 && store.Find("77aa77aa", came)?.Headline == "second" && store.Find("77aa77aa", came + kept) is not null
              && store.Find("77aa77aa", came + kept + TimeSpan.FromMilliseconds(1)) is null,
            "showbus spec: Stagewatch keeps §6's posting as the desk: the latest context it took for an id, context.kept.ms after it came and no longer");
        Check(ShowContext.Commands.All(c => !c.Verb.StartsWith("context", StringComparison.OrdinalIgnoreCase)),
            "showbus spec: Stagewatch lists no context post among the commands its context offers");

        // §6's order of commands: panic, air and transport first.
        var airFirst = ShowContext.Commands.TakeWhile(c => c.Air).Select(c => c.Verb).ToList();
        Check(Flat(s6).Contains("A sender lists its panic, air and transport commands first.", StringComparison.Ordinal)
              && ShowContext.Commands.SkipWhile(c => c.Air).All(c => !c.Air) && new[] { "blackout", "next", "start / pause" }.All(airFirst.Contains),
            "showbus spec: Stagewatch keeps §6's order of commands: its panic, air and transport commands first (" + string.Join(", ", airFirst) + ")");

        // What of §6's rules Stagewatch does not keep yet: each entry holds exactly while its code still does what it says.
        foreach (var (rule, of, unit, today, differs) in RulesNotKeptYet)
            Check(differs(), $"showbus spec: Stagewatch does not keep {rule} {of} yet ({unit}): today {today} — once its code changes, the entry leaves "
                + "ShowBusSpecTests.RulesNotKeptYet for a check of the rule kept");
    }

    /// <summary>
    /// A show under way: a countdown running on time in a running order, a department standing by, and a speaker message sent with an
    /// ack and not yet seen — a message signal to "speaker" carrying the message's text, as the desk's ApplyMessage sends it.
    /// </summary>
    static ShowSnapshot Running() => new()
    {
        Mode = TimerMode.Countdown, State = TimerState.Running, Seconds = 300, Text = "05:00", Phase = TimerPhase.Normal, Title = "Keynote",
        ItemIndex = 0, ItemCount = 3, OffsetSeconds = 0, ScheduleText = "on time", Message = Canary, MessageAck = "sent",
        Signals = new[]
        {
            new SignalView(1, SignalKind.Standby, "Lighting", "LX 12", SignalState.Seen, false, 5, ""),
            new SignalView(2, SignalKind.Message, "speaker", Canary, SignalState.Sent, false, 5, ""),
        }.ToEquatableArray(),
    };

    /// <summary>
    /// The journal lines the desk writes for tonight's messages and question, in its own words: a message sent with an ack (a Signal
    /// line), one sent without (a Message line) and an audience question as the Q&amp;A page submits it.
    /// </summary>
    static JournalEntry[] Journal() =>
    [
        new(Tonight.AddSeconds(-30), JournalKind.Signal, 0, "Message speaker: " + Canary),
        new(Tonight.AddSeconds(-20), JournalKind.Message, 0, Canary),
        new(Tonight.AddSeconds(-10), JournalKind.Question, 0, QuestionCanary),
    ];

    /// <summary>Stagewatch's context input for <paramref name="frame"/>, with these links, peers and journal, at <see cref="Tonight"/>.</summary>
    static ContextInput Own(ShowSnapshot frame, bool stuck, IReadOnlyList<ContextItem> links, IReadOnlyList<PeerSummary> peers, double goLatencyMs = 0,
        IReadOnlyList<JournalEntry>? journal = null)
        => new("abc123abc123", "Hall A", Tonight, frame, stuck, links, peers, journal ?? Array.Empty<JournalEntry>(), ShowClock.For("UTC"), goLatencyMs);

    /// <summary>
    /// Whether Stagewatch's own rows are still as the header says (P2, for its links): its only rows run from its timer, none names a
    /// peer, and the desk's link items still give its Patterns link the kind playout.
    /// </summary>
    static bool OwnRowsAsToday()
    {
        var json = ShowContext.Build(Own(Running(), false, [new ContextItem("link:patterns", "playout", "Patterns", Light.Green, "VT1: 0:42 left")],
            [new PeerSummary("patterns", "9f8e7d6c", "Patterns FOH", Light.Green, "", "10.0.0.5", 1, true)]));
        using var doc = JsonDocument.Parse(json);
        var links = Items(doc.RootElement, "links");
        var desk = Method(Source("src/Timer.App/ViewModels/MainViewModel.ShowBus.cs") ?? "", "private List<ContextItem> LinkItems()") ?? "";
        return links.Count > 0 && links.All(l => Str(l, "from") == "timer" && Str(l, "to") is { } to && !to.StartsWith('@'))
               && Regex.IsMatch(desk, @"""Patterns""[^?;]*\? ""playout""");
    }

    /// <summary>Whether Stagewatch still lists a node link it accepted only as a peers row, with link and verified (P2, as acceptor).</summary>
    static bool AcceptedLinksAsToday()
    {
        var node = new PeerSummary("playmap", "pm5e6f7a", "PlayMap Stage left", Light.Green, "Out 3 · 60 fps", "10.0.0.12", 1, true) { Link = "ws", Verified = "key" };
        var json = ShowContext.Build(Own(Running(), false, [], [node]));
        using var doc = JsonDocument.Parse(json);
        var row = Items(doc.RootElement, "peers").FirstOrDefault(p => Str(p, "id") == node.Id);
        return row.ValueKind == JsonValueKind.Object && Str(row, "link") == "ws" && Str(row, "verified") == "key"
               && Items(doc.RootElement, "items").All(i => Str(i, "kind") != "link" && !(Str(i, "id") ?? "").Contains(node.Id, StringComparison.Ordinal))
               && !json.Contains("\"@" + node.Id, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether Stagewatch still shows Patterns' clip time with no row naming Patterns' item (P3): its frames carry the clip's time
    /// left, from Patterns, to every screen, its speaker screen's elements include it, and its context writes no row with an end on
    /// another node.
    /// </summary>
    static bool ClipTimeAsToday()
    {
        var frame = Running() with { Elements = DisplayElements.All, Playout = new PlayoutView("VT1", 42, 90, true, "Patterns") };
        using var shown = JsonDocument.Parse(SnapshotJson.Serialize(frame));
        var root = shown.RootElement;
        var clip = root.TryGetProperty("playout", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
        var drawn = root.TryGetProperty("show", out var show) && show.TryGetProperty("playout", out var on) && on.ValueKind == JsonValueKind.True;
        var json = ShowContext.Build(Own(frame, false, [new ContextItem("link:patterns", "playout", "Patterns", Light.Green, "VT1: 0:42 left")],
            [new PeerSummary("patterns", "9f8e7d6c", "Patterns FOH", Light.Green, "", "10.0.0.5", 1, true)]));
        using var doc = JsonDocument.Parse(json);
        return clip.ValueKind == JsonValueKind.Object && Str(clip, "source") == "Patterns" && clip.GetProperty("remaining").GetInt64() == 42 && drawn
               && Items(doc.RootElement, "links").All(l => !(Str(l, "from") ?? "").StartsWith('@') && !(Str(l, "to") ?? "").StartsWith('@'));
    }

    /// <summary>Whether the desk's link items still give its vMix link the kind playout, a word the kind table does not hold (P6).</summary>
    static bool VmixAsToday()
    {
        var desk = Method(Source("src/Timer.App/ViewModels/MainViewModel.ShowBus.cs") ?? "", "private List<ContextItem> LinkItems()") ?? "";
        return Regex.IsMatch(desk, @"name is ""vMix""[^?;]*\? ""playout""") && !ShowBusKinds.Known("playout");
    }

    /// <summary>
    /// Whether a message's text still reaches Stagewatch's context (P8). The desk still sends a speaker message with an ack as a
    /// message signal carrying its text and journals every message's and question's text in the words <see cref="Journal"/> copies;
    /// and the context still writes a message signal's text into its cue item and every journal line into recent.
    /// </summary>
    static bool MessagesAsToday()
    {
        var desk = Source("src/Timer.App/ViewModels/MainViewModel.cs") ?? "";
        var systems = Source("src/Timer.App/ViewModels/MainViewModel.Systems.cs") ?? "";
        var writes = desk.Contains("_signals.Send(SignalKind.Message, \"speaker\", MessageInput, DateTime.UtcNow)", StringComparison.Ordinal)
                     && desk.Contains("JournalKind.Signal, _show.RundownActive ? _show.CurrentItem : -1, $\"Message speaker: {MessageInput}\")", StringComparison.Ordinal)
                     && desk.Contains("_journal.Add(DateTime.UtcNow, JournalKind.Message, _show.RundownActive ? _show.CurrentItem : -1, text)", StringComparison.Ordinal)
                     && systems.Contains("_journal.Add(now, JournalKind.Question, item, q.Text)", StringComparison.Ordinal);
        var input = Own(Running(), false, [], [], journal: Journal());
        var cue = ShowContext.Assess(input).Items.FirstOrDefault(i => i.Id == "cue:speaker");
        using var doc = JsonDocument.Parse(ShowContext.Build(input));
        var recent = Items(doc.RootElement, "recent").Select(r => Str(r, "text") ?? "").ToList();
        return writes && cue is not null && cue.State.Contains(Canary, StringComparison.Ordinal)
               && recent.Count(t => t.Contains(Canary, StringComparison.Ordinal)) == 2 && recent.Any(t => t.Contains(QuestionCanary, StringComparison.Ordinal));
    }

    /// <summary>Whether a stuck show thread still turns Stagewatch's timer red and leaves its schedule's light as the frame gives it (P5).</summary>
    static bool StuckScheduleAsToday()
    {
        var items = ShowContext.Assess(Own(Running(), true, [], [])).Items;
        return items.FirstOrDefault(i => i.Id == "timer")?.Light == Light.Red && items.FirstOrDefault(i => i.Id == "schedule")?.Light == Light.Green;
    }

    /// <summary>Whether Stagewatch's timer is still its first item whatever its light, ahead of a red one (P7).</summary>
    static bool TimerFirstAsToday()
    {
        var items = ShowContext.Assess(Own(new ShowSnapshot { Title = "Keynote" }, false, [new ContextItem("link:osc", "link", "OSC", Light.Red, "not listening")], [])).Items;
        return items.Count > 1 && items[0].Id == "timer" && items[0].Light == Light.Grey && items.Skip(1).Any(i => i.Light == Light.Red);
    }

    /// <summary>
    /// The fields of the message of <paramref name="type"/> a text writes in code as <c>{"type":"…","a","b"}</c>, after its type;
    /// null when it writes none.
    /// </summary>
    static List<string>? Shape(string text, string type)
        => Regex.Match(Flat(text), "`\\{\"type\":\"" + Regex.Escape(type) + "\",((?:\"[A-Za-z]+\",?)+)\\}`") is { Success: true } m
            ? Regex.Matches(m.Groups[1].Value, "\"([A-Za-z]+)\"").Select(f => f.Groups[1].Value).ToList() : null;

    /// <summary>
    /// A file of this tree, read as text with LF line ends whatever the checkout wrote — a page, or Timer.App, which is not built beside
    /// this suite on every platform; null when it is not there.
    /// </summary>
    static string? Source(string relative)
    {
        if (Master() is not { } master || Path.GetDirectoryName(Path.GetDirectoryName(master)) is not { } root) return null;
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) : null;
    }

    /// <summary>A peer's context as PeerContexts reads it: a patterns node, then <paramref name="extra"/>.</summary>
    static PeerContexts.Entry? Peer(string extra)
        => PeerContexts.Parse("{\"v\":1,\"node\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"FOH\"}" + extra + "}", "10.0.0.5", Tonight);

    /// <summary>A context's array <paramref name="key"/> of <paramref name="count"/> copies of <paramref name="item"/>, after a comma.</summary>
    static string Many(string key, string item, long count) => ",\"" + key + "\":[" + string.Join(",", Enumerable.Repeat(item, (int)count)) + "]";

    /// <summary>
    /// §10's numbers as CertificateLife keeps them, at several moments of making (a fraction of a second included, which X.509 does
    /// not keep), and NodeCertificate held to them from its source: Timer.App is not built beside this suite on every platform, so
    /// its source must make a certificate only over <see cref="CertificateLife.Window"/>, keep, replace and save one only through
    /// <see cref="CertificateFiles"/> (which ShowBusTests runs on every platform), and name no day count of its own.
    /// </summary>
    static void CertificateCode(KeepsRows Keeps, Numbers n)
    {
        var days = TimeSpan.FromDays(n.Of("cert.days"));
        DateTimeOffset[] moments = [Tonight, new DateTimeOffset(Tonight).AddTicks(7_654_321), new(2028, 2, 29, 23, 59, 59, 999, TimeSpan.FromHours(-8)),
            new(2030, 3, 31, 1, 30, 0, TimeSpan.FromHours(2))];
        var windows = moments.Select(m => (Made: m, Window: CertificateLife.Window(m))).ToList();
        Keeps(CertificateLife.Span == days && windows.All(w => w.Window.NotAfter - w.Window.NotBefore == days && w.Window.NotBefore.Ticks % TimeSpan.TicksPerSecond == 0)
              && windows.All(w => !CertificateLife.NeedsRenewal(w.Window.NotBefore, w.Window.NotAfter, w.Made))
              && CertificateLife.NeedsRenewal(Tonight.AddDays(-1), Tonight.AddDays(-1) + days + TimeSpan.FromSeconds(1), Tonight),
            "a certificate it makes spans exactly that long, NotBefore to NotAfter, and one a second longer is replaced at a start", "cert.days");
        var back = TimeSpan.FromHours(n.Of("cert.backdate.hours"));
        Keeps(CertificateLife.Backdate <= back && windows.All(w => w.Window.NotBefore >= w.Made - back && w.Window.NotBefore <= w.Made),
            "a certificate's NotBefore lies no further before its making", "cert.backdate.hours");
        var renew = TimeSpan.FromDays(n.Of("cert.renew.days"));
        var (from, to) = CertificateLife.Window(Tonight);
        Keeps(CertificateLife.RenewDays == n.Of("cert.renew.days") && !CertificateLife.NeedsRenewal(from, to, to - renew)
              && CertificateLife.NeedsRenewal(from, to, to - renew + TimeSpan.FromSeconds(1)),
            "it makes a new certificate at a start once fewer days than that remain", "cert.renew.days");
        // Its code, its comments left out: they may name the numbers, the code may not.
        var source = Regex.Replace(Source("src/Timer.App/NodeCertificate.cs") ?? "", @"//.*$", "", RegexOptions.Multiline);
        Keeps(Regex.Matches(source, @"CreateSelfSigned\(").Count == 1 && source.Contains("var (notBefore, notAfter) = CertificateLife.Window(now);", StringComparison.Ordinal)
              && source.Contains("req.CreateSelfSigned(notBefore, notAfter)", StringComparison.Ordinal)
              && Regex.Matches(source, @"new CertificateFiles\(").Count == 1 && source.Contains("now => Create(name, now)).Load();", StringComparison.Ordinal)
              && !Regex.IsMatch(source, @"\b(?:NotAfter|NotBefore|LoadPkcs12|WriteAllBytes|ReadAllBytes|UtcNow|Now)\b|Add(?:Days|Hours|Years|Months)\(|\b(?:825|826|30)\b"),
            "NodeCertificate makes a certificate over CertificateLife's window from CertificateFiles' clock, and keeps, replaces and saves one only "
            + "through CertificateFiles, with no number or file of its own",
            "cert.days", "cert.backdate.hours", "cert.renew.days");
    }

    // ------------------------------------------------------------------ §1–§11's words

    /// <summary>The text of the body's section <paramref name="number"/>, from its heading up to the next section's; empty when there is none.</summary>
    static string Section(string body, int number)
    {
        var head = "## " + number.ToString(CultureInfo.InvariantCulture) + ". ";
        var start = body.StartsWith(head, StringComparison.Ordinal) ? 0 : body.IndexOf("\n" + head, StringComparison.Ordinal) + 1;
        if (start == 0 && !body.StartsWith(head, StringComparison.Ordinal)) return "";
        var end = body.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return body[start..(end < 0 ? body.Length : end)];
    }

    /// <summary>The ticked text of the first table row of <paramref name="section"/> whose first cell starts with <paramref name="cell"/>, and its other cells.</summary>
    static (string First, string Cells) TableRow(string section, string cell)
    {
        var line = section.Split('\n').FirstOrDefault(l => l.StartsWith("| " + cell + " |", StringComparison.Ordinal)) ?? "";
        var cells = line.Split('|');
        return cells.Length < 3 ? ("", "") : (cells[1], string.Join("|", cells[2..]));
    }

    /// <summary>A JSON text's value, cloned so nothing is left to dispose; null when there is no text or it is not JSON.</summary>
    static JsonElement? Json(string? text)
    {
        if (text is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The quoted names of a shape written <c>{"a","b":{…},"c"}</c>, at its top level, in order.</summary>
    static List<string> TopNames(string shape)
    {
        var flat = shape;
        for (var before = ""; before != flat;) { before = flat; flat = Regex.Replace(flat, @":\{[^{}]*\}", ""); }
        return Regex.Matches(flat, "\"([A-Za-z]+)\"").Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>
    /// §1–§11's words. Every dotted name they write in code is a §14 row or one of <see cref="Fields"/>, and each of §2–§5, §10 and §11
    /// names every row <see cref="SectionRows"/> gives it. §1's rules 3 and 4 hold 13.12.6 and the public view; §2's hello table names
    /// §14's kinds and ports, its example keeps that table and §14's numbers, and its heading's port is hello.port; §3 appends 13.12's
    /// four fields to a receipt, in their order, and §4 and §5 hand liveness and the clock's quality to §13; §6 words its sizes, its
    /// trimming (which touches reds, ambers and panic, air and transport commands only last, so every context has a form within its bytes)
    /// and its trimmed item, its peers row (the example's row has its fields; fields appended after them; LINK's verified mapped), its
    /// notation and its posting bound; §7 keeps a PIN, a viewing code and a view pass off PeerLinks and names a PIN-proved node link a
    /// level-0 door, §7.1 and §7.2 are there with LINK among what a connection may send before pairing; §9 names the language model by what
    /// it is; §10's span and line protocols are 1.3's; §11's states, ids, credentials and shape are worded. §2's conflict holds a connector,
    /// in 13.15.3's and 13.16's words, until its mark clears.
    /// </summary>
    static void Words(Action<bool, string> Check, string body, Numbers n)
    {
        var all = string.Concat(Enumerable.Range(1, 11).Select(i => Section(body, i)));
        var strays = CodeNames(all).Where(x => !n.Values.ContainsKey(x) && !Fields.Contains(x)).Order(StringComparer.Ordinal).ToList();
        Check(strays.Count == 0, "showbus spec: every dotted name §1–§11 write in code is a §14 row or a field they name (" + string.Join(", ", strays) + ")");
        foreach (var (section, of) in SectionRows)
        {
            var named = CodeNames(Section(body, section));
            var owed = n.Values.Keys.Where(k => of.Any(r => r.EndsWith('.') ? k.StartsWith(r, StringComparison.Ordinal) : k == r)).ToList();
            var missing = owed.Where(k => !named.Contains(k)).Order(StringComparer.Ordinal).ToList();
            Check(owed.Count > 0 && missing.Count == 0, $"showbus spec: §{section} names every row §14 gives it (" + string.Join(", ", missing) + ")");
        }

        // §1.
        var rules = Flat(Section(body, 1));
        Check(rules.Contains("and a command is never applied twice (13.12.6)", StringComparison.Ordinal),
            "showbus spec: §1 rule 3 says a command is never applied twice (13.12.6)");
        Check(rules.Contains("A node's *public view* — what any screen in the room may show, named for each node in §8 — is open by design", StringComparison.Ordinal)
              && rules.Contains("travels only on an authenticated link (a key, a proof, a pairing: §7), to this machine where the node trusts it, or from a node whose operator left it open (no PIN or token), which a connector says (13.6.7, 13.16.2)", StringComparison.Ordinal),
            "showbus spec: §1 rule 4 keeps show data off all but the public view, an authenticated link, this machine and an open node, which a connector says");

        // §2: its table, its example and its heading.
        var s2 = Section(body, 2);
        var kinds = n.Kinds.Select(k => k.T).ToHashSet(StringComparer.Ordinal);
        var named2 = Regex.Matches(TableRow(s2, "`t`").Cells, @"`([a-z]+)` \(").Select(m => m.Groups[1].Value).ToList();
        Check(named2.ToHashSet(StringComparer.Ordinal).SetEquals(kinds) && named2.Count == kinds.Count,
            "showbus spec: §2's t row names §14's kinds, each once (" + string.Join(", ", named2) + ")");
        var doors = Regex.Matches(TableRow(s2, "`ports`").Cells, "`([a-z]+)`").Select(m => m.Groups[1].Value).Where(w => w != "port").ToList();
        Check(doors.ToHashSet(StringComparer.Ordinal).SetEquals(n.Ports) && doors.Count == n.Ports.Count,
            "showbus spec: §2's ports row names §14's ports, each once (" + string.Join(", ", doors) + ")");
        Check(s2.StartsWith("## 2. Discovery — one channel: the hello on UDP " + n.Of("hello.port").ToString(CultureInfo.InvariantCulture) + "\n", StringComparison.Ordinal),
            "showbus spec: §2's heading names hello.port");
        var fields = Table(s2, "field");
        var ignored = Regex.Matches(TableRow(s2, "anything else").Cells, "`([a-z]+)`").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Check(fields.SetEquals(["t", "v", "proto", "id", "inst", "name", "host", "ports", "caps", "clock", "tls", "presenting"]) && ignored.SetEquals(["build", "role", "web", "wire", "machine"]),
            "showbus spec: §2's table names a hello's fields and the older ones a reader ignores (" + string.Join(", ", fields) + " / " + string.Join(", ", ignored) + ")");
        if (Inside(s2, "\n```json\n", "\n```") is { } helloJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(helloJson);
                var h = doc.RootElement;
                var t = Str(h, "t") ?? "";
                var id = Str(h, "id") ?? "";
                var ports = h.TryGetProperty("ports", out var p) && p.ValueKind == JsonValueKind.Object ? p.EnumerateObject().Select(x => x.Name).ToList() : [];
                var caps = Strings(h, "caps");
                var compact = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(h));
                Check(h.EnumerateObject().All(x => fields.Contains(x.Name)) && kinds.Contains(t) && Int(h, "v") == 1 && Int(h, "proto") == n.Proto
                      && id.Length >= n.Of("hello.id.min") && id.Length <= n.Of("hello.id.max") && Regex.IsMatch(id, "^[A-Za-z0-9_-]+$")
                      && Regex.IsMatch(Str(h, "inst") ?? "", "^[0-9a-f]{" + n.Of("hello.inst.chars").ToString(CultureInfo.InvariantCulture) + "}$")
                      && (Str(h, "name") ?? "").Length <= n.Of("hello.name.chars") && (Str(h, "host") ?? "").Length <= n.Of("hello.name.chars")
                      && ports.Count <= n.Of("hello.ports") && ports.All(n.Ports.Contains)
                      && caps.Count <= n.Of("hello.caps") && caps.All(c => n.Caps.Any(x => x.Word == c && x.Kinds.Contains(t)))
                      && Regex.IsMatch(Str(h, "tls") ?? "", "^sha256:[0-9a-f]{64}$") && compact <= n.Of("hello.bytes"),
                    $"showbus spec: §2's example hello keeps §2's table and §14's kinds, ports, caps for its kind and sizes ({compact} bytes)");
            }
            catch (JsonException e) { Check(false, "showbus spec: §2's example hello is JSON (" + e.Message + ")"); }
        }
        else Check(false, "showbus spec: §2 shows a hello");
        var hearing = Flat(s2);
        Check(hearing.Contains("A node whose doors are bound to this machine only says no hello", StringComparison.Ordinal)
              && hearing.Contains("**No reader relies on the copy**", StringComparison.Ordinal)
              && hearing.Contains("dials the address its link already uses while that address is still heard (13.15.4)", StringComparison.Ordinal)
              && hearing.Contains("every address it heard within `peer.stale.ms`, at most `peer.addresses` of them", StringComparison.Ordinal)
              && hearing.Contains("never of the one its link uses", StringComparison.Ordinal)
              && hearing.Contains("marks the id **in conflict**", StringComparison.Ordinal)
              && hearing.Contains("dials no new link to that id while the mark stands (13.4.4, 13.15.3), and clears the mark after `peer.conflict.clear.ms` without the alternation; a connector then dials it again", StringComparison.Ordinal),
            "showbus spec: §2 says where a hello goes, how one id is heard, within peer.addresses addresses, and how two nodes with one id are a "
            + "conflict that holds a connector until its mark clears, as 13.4.4, 13.15.3 and 13.15.4 cite it");
        var reconnect = Flat(Between(body, "\n### 13.15 ", "\n### 13.16 ") ?? "");
        var linkStates = Flat(Between(body, "\n### 13.16 ", "\n### 13.17 ") ?? "");
        Check(reconnect.Contains("An id in conflict (§2) is held while §2's mark stands", StringComparison.Ordinal)
              && reconnect.Contains("CERTIFICATE CHANGED, a floor and another id link nothing keyed until the operator acts", StringComparison.Ordinal)
              && linkStates.Contains("| `held` | a key refused, or an id in conflict (§2); its hold runs |", StringComparison.Ordinal)
              && linkStates.Contains("| `refused` | CERTIFICATE CHANGED, a floor, another id: a person must act |", StringComparison.Ordinal)
              && linkStates.Contains("`conflict` while §2's mark holds the link (13.15.3)", StringComparison.Ordinal),
            "showbus spec: 13.15.3 and 13.16 hold an id in conflict until §2's mark clears, never as a refusal a person must end");

        // §3–§5.
        var s3 = Section(body, 3);
        var tail = Regex.Matches(s3, @"^- `([A-Za-z]+)`:", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Check(tail.SequenceEqual(ReceiptTail) && Flat(s3).Contains("A receipt may end with four more fields, in this order", StringComparison.Ordinal)
              && Flat(s3).Contains("is 13.12.3's table", StringComparison.Ordinal),
            "showbus spec: §3 appends final, repeat, lateMs and frame to a receipt, in that order, and hands reading an answer to 13.12.3 (" + string.Join(", ", tail) + ")");
        Check(Flat(Section(body, 4)).Contains("Liveness, the same everywhere, is 13.10's", StringComparison.Ordinal)
              && Flat(Section(body, 4)).Contains("is stale — amber, its counting values shown as \"?\" (13.16.1)", StringComparison.Ordinal)
              && Flat(Section(body, 4)).Contains("Nothing is queued while a link is down (13.15.5)", StringComparison.Ordinal)
              && Flat(Section(body, 5)).Contains("— its quality — are 13.11's", StringComparison.Ordinal),
            "showbus spec: §4 hands liveness to 13.10 and 13.15.5, a stale link amber as 13.16.1 has it, and §5 a clock's quality to 13.11");

        // §6.
        var s6 = Section(body, 6);
        var six = Flat(s6);
        var peerFields = Regex.Match(six, @"A row is `\{((?:""[a-z]+"",?)+)\}`") is { Success: true } pf ? Regex.Matches(pf.Groups[1].Value, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToList() : [];
        var exampleRows = Json(Inside(s6, "\n```json\n", "\n```")) is { } example ? Items(example, "peers") : [];
        Check(peerFields.Count > 0 && exampleRows.Count > 0 && exampleRows.All(r => r.EnumerateObject().Select(x => x.Name).SequenceEqual(peerFields)),
            "showbus spec: §6's example's peers rows carry §6's peers fields, in order (" + string.Join(", ", peerFields) + ")");
        var trimmed = Json(Regex.Match(six, "`(\\{\"id\":\"context:trimmed\"[^`]*\\})`") is { Success: true } tm ? tm.Groups[1].Value : null);
        Check(trimmed is { } ti && Str(ti, "kind") is { } tk && ShowBusKinds.Known(tk)
              && Str(ti, "light") == Light.Grey && (Str(ti, "state") ?? "").Length <= n.Of("context.item.state.chars")
              && six.Contains("leaves out, in this order and only until it fits: the oldest `recent` lines; grey links, then the other links; commands other than panic, air and transport, from the end of its list; grey items, then green items", StringComparison.Ordinal)
              && six.Contains("Only while what is left is still over does it touch a red or amber item, or a panic, air or transport command: it cuts the `state` of each amber item, then of each red item, ending each cut with `…`; then it leaves out amber items, then red items, then panic, air and transport commands, each from the end of its list", StringComparison.Ordinal),
            "showbus spec: §6 trims an over-long context in its order, touches red and amber items and panic, air and transport commands only when "
            + "nothing else is left to trim, so every context has a form within context.bytes, and adds one grey context:trimmed item of a table kind");
        Check(six.Contains("A reader accepts at least these. It keeps a `verb` whole or drops it, and counts it, and keeps at least `context.words.read.chars` characters of `words`", StringComparison.Ordinal)
              && six.Contains("It keeps contexts for at least `context.peers` node ids and at most `peers.max`", StringComparison.Ordinal)
              && six.Contains("answers a context over `context.bytes` with `413` and its size", StringComparison.Ordinal),
            "showbus spec: §6's sizes bind a reader, and a desk keeps contexts for context.peers to peers.max nodes and answers an over-long one 413");
        Check(six.Contains("A `<…>` value is exactly one word, but for a form's last element and a value named `words`, `text` or `line`", StringComparison.Ordinal)
              && six.Contains("A value whose whole name is `token`, `key`, `password`, `passcode` or `secret`", StringComparison.Ordinal)
              && six.Contains("**A risk that depends on a word** is a row of its own", StringComparison.Ordinal)
              && six.Contains("may appear line by line in that desk's show report and its operator's AI summary", StringComparison.Ordinal),
            "showbus spec: §6 words the command notation, a word's risk, the secret placeholders, and that recent may reach a desk's report");
        Check(six.Contains("A sender may append fields to a row after these; a reader ignores a field it does not know", StringComparison.Ordinal)
              && six.Contains("A LINK verdict's `verified` (13.6.6) maps onto it: `key` and `link` are `key`, and `code`, the operator's credential (13.5.1), is `operator`", StringComparison.Ordinal),
            "showbus spec: §6's peers rows take fields appended after theirs, and map a LINK verdict's verified onto their own");

        // §7.
        var seven = Flat(Section(body, 7));
        Check(seven.Contains("a PIN, a viewing code or a view pass is never a PeerLink credential (13.5.1); a node link on Stagewatch's `/ws` that the PIN or a control key proves (§7.1) is level 0, a door that is not a PeerLink (13.18)", StringComparison.Ordinal)
              && seven.Contains("one the operator's credential proves is `verified` `operator`, and is never a PeerLink, which no PIN proves (13.5.1)", StringComparison.Ordinal)
              && seven.Contains("When the viewing code refuses the socket — its credential does not open the code, and its address does not carry it — that one message carries `\"view\":true`", StringComparison.Ordinal)
              && seven.Contains("### 7.1 A node on another node's `/ws`", StringComparison.Ordinal) && seven.Contains("### 7.2 Reading a node that asks for a token", StringComparison.Ordinal)
              && seven.Contains("`LINK` and `LINK PROOF` (13.6.4)", StringComparison.Ordinal)
              && seven.Contains("`ERR [@id] not paired — this node shows its state only to a paired connection`", StringComparison.Ordinal),
            "showbus spec: §7 keeps PINs and view passes off PeerLinks and names a PIN-proved node link a level-0 door; §7.1 and §7.2 are there, and LINK is "
            + "among what a connection sends before pairing");

        // §9–§11.
        var nine = Section(body, 9);
        Check(Regex.IsMatch(nine, @"^\| Discovery, links, commands, receipts, state, clock offset, liveness \| The language model: ", RegexOptions.Multiline),
            "showbus spec: §9's first need for the internet is the language model, by what it is");
        var ten = Flat(Section(body, 10));
        Check(ten.Contains("whose whole span — NotBefore to NotAfter, any backdating for a skewed clock included — is at most `cert.days`", StringComparison.Ordinal)
              && ten.Contains("or when the one it has spans more than `cert.days`", StringComparison.Ordinal)
              && ten.Contains("(13.8.5); 1.3 has no `STARTTLS`", StringComparison.Ordinal),
            "showbus spec: §10 bounds a certificate's whole span, replaces an over-long one, and serves TLS on a line wire as 13.8.5 does");
        var eleven = Flat(Section(body, 11));
        var states = Ticked(Line(Section(body, 11), "**States:**") + " " + (Between(eleven, "**States:**", "**Ids**") ?? "")).Distinct().ToList();
        Check(states.ToHashSet(StringComparer.Ordinal).SetEquals(Enum.GetNames<SharedProposalState>().Select(s => s.ToLowerInvariant())),
            "showbus spec: §11's states are ProposalDesk's (" + string.Join(", ", states) + ")");
        Check(eleven.Contains("**Ids** are opaque, and unique across a desk's runs: `p<n>-<run>`", StringComparison.Ordinal)
              && eleven.Contains("refused — never cut — when longer", StringComparison.Ordinal)
              && eleven.Contains("A target may raise the risk of its own proposal, never lower it", StringComparison.Ordinal)
              && eleven.Contains("A result is a report owed (13.12.9)", StringComparison.Ordinal),
            "showbus spec: §11 words ids unique across runs, lines refused not cut, a raise-only self risk and results as reports owed");
        Check(eleven.Contains("`POST /api/proposals` (a `node` or `control` key, or the PIN)", StringComparison.Ordinal)
              && eleven.Contains("an `mcp` key posts no proposal here", StringComparison.Ordinal)
              && eleven.Contains("A desk may append more fields after these, to a proposal and to its `201` answer; a reader ignores a field it does not know", StringComparison.Ordinal),
            "showbus spec: §11 takes a proposal with a credential that can speak for its from, never an mcp key, and lets a desk append fields to a proposal and its 201");
    }

    /// <summary>
    /// §1–§11 against Stagewatch's code: its hello sends only §2's fields or ones §2 says a reader ignores; §3's receipt is its receipt,
    /// field for field; §6's peers row is its own; as the desk it keeps contexts within §6's bound; §7.1's refusals are NodeLinks', and its
    /// verdicts are what NodeLinks and NodeBindings say; §11's proposal and its answer to one are ProposalDesk's and the desk's. Where §6 and
    /// §11 let a sender append fields, the spec's fields come first, in order, and the code may write more after them.
    /// </summary>
    static void WordsCode(Action<bool, string> Check, string body, Numbers n)
    {
        var s2 = Section(body, 2);
        var said = Table(s2, "field").Concat(Regex.Matches(TableRow(s2, "anything else").Cells, "`([a-z]+)`").Select(m => m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        var sent = typeof(SlidesBeacon.Hello).GetProperties().Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name).OfType<string>().ToList();
        Check(sent.Count > 0 && sent.All(said.Contains), "showbus spec: Stagewatch's hello sends only §2's fields or ones §2 says a reader ignores ("
            + string.Join(", ", sent.Where(x => !said.Contains(x))) + ")");

        var receipt = Inside(Section(body, 3), "\n```json\n", "\n```") ?? "";
        var made = new CommandReceipt("a1", true, "applied", "Message on the speaker screen", null, new Dictionary<string, long> { ["messageId"] = 12 }).ToJson();
        Check(SameJson(receipt, Encoding.UTF8.GetString(made)), "showbus spec: Stagewatch keeps §3's receipt: its receipt is §3's example, field for field");

        var six = Flat(Section(body, 6));
        var peerFields = Regex.Match(six, @"A row is `\{((?:""[a-z]+"",?)+)\}`") is { Success: true } pf ? Regex.Matches(pf.Groups[1].Value, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToList() : [];
        var node = new PeerSummary("playmap", "pm5e6f7a", "PlayMap Stage left", Light.Green, "Out 3 · 60 fps", "10.0.0.12", 1, true) { Link = "ws", Verified = "key" };
        using (var doc = JsonDocument.Parse(ShowContext.Build(Own(Running(), false, [], [node]))))
            Check(peerFields.Count > 0 && Items(doc.RootElement, "peers").FirstOrDefault() is { ValueKind: JsonValueKind.Object } row
                  && row.EnumerateObject().Select(x => x.Name).Take(peerFields.Count).SequenceEqual(peerFields),
                "showbus spec: Stagewatch keeps §6's peers row: its fields first, in order, any it appends after them (" + string.Join(", ", peerFields) + ")");
        Check(PeerContexts.MaxPeers >= n.Of("context.peers") && PeerContexts.MaxPeers <= n.Of("peers.max"),
            $"showbus spec: Stagewatch keeps §6's posting bound: as the desk it keeps contexts for {PeerContexts.MaxPeers} node ids, within context.peers and peers.max");

        var s71 = Between(body, "\n### 7.1 ", "\n### ") ?? "";
        var errors = new List<string>();
        var inTable = false;
        foreach (var line in s71.Split('\n'))
        {
            if (line.StartsWith("| request | error |", StringComparison.Ordinal)) { inTable = true; continue; }
            if (!inTable) continue;
            if (!line.StartsWith('|')) break;
            if (!line.StartsWith("|---", StringComparison.Ordinal)) errors.Add(line.Split('|')[2].Trim());
        }
        Check(errors.SequenceEqual([NodeLinks.WindowNotNode, NodeLinks.PinInUrl, NodeLinks.NodeDoesNotControl, NodeLinks.BadId, NodeLinks.BadKind, NodeLinks.OwnId]),
            "showbus spec: Stagewatch keeps §7.1's refusals before the upgrade: NodeLinks' words, in order (" + string.Join(" · ", errors) + ")");
        var verdicts = (Inside(s71, "\n```\n{", "\n```") is { } v ? "{" + v : "").Split('\n');
        var bindings = new NodeBindings();
        bindings.Speak(NodeCredential.Key("k1"), "patterns", "9f8e7d6c", Tonight);
        var moved = bindings.Speak(NodeCredential.Key("k1"), "patterns", "5e6f7a8b", Tonight);
        string Said(byte[] message) => Encoding.UTF8.GetString(message);
        var verified = new WsLink { Class = WsClass.Node, Node = "9f8e7d6c", Kind = "patterns", Verified = NodeProof.Key, Scope = "node" };
        var other = new WsLink { Class = WsClass.Node, Node = "5e6f7a8b", Kind = "patterns", Verified = NodeProof.None, Scope = "node", Words = moved.Error };
        Check(verdicts.Length == 3 && !moved.Ok && SameJson(verdicts[0], Said(NodeLinks.Verdict(verified))) && SameJson(verdicts[1], Said(NodeLinks.Verdict(other)))
              && SameJson(verdicts[2], Said(NodeLinks.Refusal("unknown or revoked API key", NodeLinks.RetrySeconds))),
            "showbus spec: Stagewatch keeps §7.1's verdicts: a key's node link, a key bound to another id in NodeBindings' words, and a refused key, as NodeLinks writes them");

        var eleven = Section(body, 11);
        var shape = Regex.Match(Flat(eleven), @"`(\{""id"",""from"":[^`]+\})`") is { Success: true } sm ? TopNames(sm.Groups[1].Value) : [];
        var proposal = new SharedProposal("p17", new NodeRef("patterns", "9f8e7d6c", "FOH"), new NodeRef("patterns", "9f8e7d6c", "FOH"), "EYE FOCUS screen 2", "", "review", false,
            SharedProposalState.Pending, Tonight);
        using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(proposal.ToJson())))
            Check(shape.Count > 0 && doc.RootElement.EnumerateObject().Select(x => x.Name).Take(shape.Count).SequenceEqual(shape),
                "showbus spec: Stagewatch keeps §11's proposal: ProposalDesk writes its fields first, in order, any it appends after them (" + string.Join(", ", shape) + ")");
        var desk = Source("src/Timer.App/ViewModels/MainViewModel.Desk.cs") ?? "";
        Check(Flat(eleven).Contains("→ `201 {\"ok\":true,\"id\":\"p17\",\"state\":\"pending\",\"risk\":\"review\"}`", StringComparison.Ordinal)
              && Regex.IsMatch(desk, @"\(201, Json\(new \{ ok = true, id = p\.Id, state = p\.StateName, risk = p\.Risk(?: \}|,)"),
            "showbus spec: Stagewatch keeps §11's answer to a proposal: 201 with ok, id, state and risk first (MainViewModel.Desk.cs)");
    }

    /// <summary>
    /// §8: its table gives each §14 kind one row and its parts follow the table, and each part names what §8 says a row does; every door
    /// 13.18 names is in a node's row. Stagewatch's row names its doors past 13.18's (OSC's feedback, scripts' bearer keys, the node links
    /// its operator's credential proves), its two exemptions — ?pin= with its redirect's echo — and its stuckMs as its code has it, within
    /// §14's bounds; and what it does not keep yet that a release after 2.19 ends or no unit is scheduled to end: every such row of
    /// <see cref="NotKeptYet"/>, and every entry of <see cref="WordsNotKeptYet"/> and <see cref="RulesNotKeptYet"/>, each on its own in its
    /// words (the rules in one list, in order, while 1.3 is a draft), and while 1.3 is a draft nothing else. Patterns' row names scope A's
    /// open queries, and PlayMap's its 9941 exemption and when it ends. Each entry of WordsNotKeptYet holds while the code does that.
    /// </summary>
    static void NodeRows(Action<bool, string> Check, string body, string s13, Numbers n, bool draft)
    {
        var s8 = Section(body, 8);
        var rows = Regex.Matches(s8, @"^\| \*\*[^*]+\*\* \(`([a-z]+)`\) \|", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        var parts = Regex.Matches(s8, @"^### 8\.(\d+) .+ \(`([a-z]+)`\)$", RegexOptions.Multiline)
            .Select(m => (N: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), T: m.Groups[2].Value)).ToList();
        var kinds = n.Kinds.Select(k => k.T).ToList();
        Check(rows.Count == kinds.Count && rows.ToHashSet(StringComparer.Ordinal).SetEquals(kinds) && parts.Select(p => p.T).SequenceEqual(rows)
              && parts.Select(p => p.N).SequenceEqual(Enumerable.Range(1, rows.Count)),
            "showbus spec: §8's table gives each §14 kind one row, and its parts 8.1 onward follow the table (" + string.Join(", ", rows) + " / "
            + string.Join(", ", parts.Select(p => p.T)) + ")");
        string PartOf(string t) => parts.FindIndex(p => p.T == t) is var i and >= 0 ? Part(body, "8." + (i + 1).ToString(CultureInfo.InvariantCulture)) : "";
        foreach (var t in kinds.Where(k => k != "agent"))
        {
            var at = RowParts.Select(label => PartOf(t).IndexOf("- **" + label + "**", StringComparison.Ordinal)).ToList();
            Check(at.All(i => i >= 0) && at.Zip(at.Skip(1)).All(p => p.First < p.Second),
                $"showbus spec: §8's row for {t} names, in order, " + string.Join(", ", RowParts));
        }
        var doors = Flat(Part(s13, 18) ?? "");
        (string In1318, string In8)[] named =
        [
            ("- OSC;", "OSC"), ("Stagewatch's Companion TCP (9923)", "Companion TCP (9923)"), ("Companion decks on the line wires", "Companion decks on its"),
            ("browser pages and `?pin=`", "browser pages and `?pin=`"), ("window links (§12)", "window links (§12)"), ("Slides agents", "Slides agents"),
            ("PlayMap's sync (9942) and fleet traffic", "(9942)"), ("PlayMap's desk↔engine link on one machine", "desk↔engine link"), ("Patterns' twin and its node relays", "twin and node relays"),
            ("Stagewatch's node links (§7.1) that the PIN or a control key proves", "node links (§7.1) the PIN or a control key proves"),
        ];
        var unnamed = named.Where(d => !doors.Contains(d.In1318, StringComparison.Ordinal) || !Flat(s8).Contains(d.In8, StringComparison.Ordinal)).Select(d => d.In1318).ToList();
        Check(unnamed.Count == 0, "showbus spec: every door 13.18 names is in a node's §8 row (" + string.Join(" · ", unnamed) + ")");

        // Stagewatch's row.
        var sw = Flat(PartOf("main"));
        var exemptions = Between(sw, "- **Named exemptions**", "- **Not kept yet**") ?? "";
        var swDoors = Between(sw, "- **Doors that are not PeerLinks**", "- **Named exemptions**") ?? "";
        Check(swDoors.Contains("its feedback, once set, sends the show's state to the host it names, unauthenticated and in clear", StringComparison.Ordinal)
              && swDoors.Contains("scripts and `/mcp` clients (13.6.9's bearer keys)", StringComparison.Ordinal)
              && swDoors.Contains("node links (§7.1) the PIN or a control key proves: the operator's credential as a bearer, level 0 and never a PeerLink", StringComparison.Ordinal),
            "showbus spec: §8's Stagewatch row names OSC's feedback, scripts' bearer keys and the node links its operator's credential proves among its doors");
        Check(exemptions.Contains("`?pin=`: a page or a script may carry the PIN or a key in its address", StringComparison.Ordinal)
              && exemptions.Contains("not scheduled to end", StringComparison.Ordinal)
              && exemptions.Contains("an `http://` address that carries the PIN or a key has crossed the network in clear by then, and the redirect's `Location` carries it back once more, in clear", StringComparison.Ordinal)
              && exemptions.Contains("Companion TCP (9923) takes commands with no authentication when its operator binds it to the network", StringComparison.Ordinal)
              && exemptions.Contains("its authentication comes with 1.4's default flip", StringComparison.Ordinal),
            "showbus spec: §8's Stagewatch row names ?pin= (not scheduled to end) and Companion TCP 9923 (until 1.4's default flip) as its exemptions");
        var stuck = (long)SnapshotHub.StuckAfter.TotalMilliseconds;
        Check(sw.Contains("- **Show writer**: its show thread. `stuckMs` " + stuck.ToString(CultureInfo.InvariantCulture) + ", ", StringComparison.Ordinal)
              && stuck >= n.Of("show.stuck.min.ms") && stuck <= n.Of("show.stuck.max.ms")
              && sw.Contains("It offers neither `stuck` nor `expire` as a feature: no measurement yet", StringComparison.Ordinal),
            $"showbus spec: §8's Stagewatch row names its show writer and its stuckMs as SnapshotHub has it ({stuck}), within show.stuck.min.ms and show.stuck.max.ms");
        var notKept = sw[(sw.IndexOf("- **Not kept yet**", StringComparison.Ordinal) is var k and >= 0 ? k : sw.Length)..];
        var later = NotKeptYet.Where(x => !In219(x.Unit)).ToList();
        foreach (var (row, unit, today, _) in later)
            Check(notKept.Contains("`" + row + "` (today " + today + ")", StringComparison.Ordinal),
                $"showbus spec: §8's Stagewatch row says it does not keep {row} yet, in NotKeptYet's words ({unit}): today {today}");
        foreach (var (what, unit, today, differs) in WordsNotKeptYet)
        {
            Check(!In219(unit) && notKept.Contains(what + " (today " + today + ")", StringComparison.Ordinal),
                $"showbus spec: §8's Stagewatch row says it does not keep {what} yet, in WordsNotKeptYet's words ({unit}): today {today}");
            Check(differs(), $"showbus spec: Stagewatch does not keep {what} yet ({unit}): today {today} — once its code changes, the entry leaves "
                + "ShowBusSpecTests.WordsNotKeptYet for a check of the words kept");
        }
        // Each rule's entry on its own, so one leaving RulesNotKeptYet after 1.3 is final leaves the frozen row's words standing; their
        // list, in order, only while 1.3 is a draft.
        var rules = RulesNotKeptYet.Select(r => r.Rule + " " + r.Of + " (today " + r.Today + ")").ToList();
        foreach (var (rule, of, unit, today, _) in RulesNotKeptYet)
            Check(!In219(unit) && notKept.Contains(rule + " " + of + " (today " + today + ")", StringComparison.Ordinal),
                $"showbus spec: §8's Stagewatch row says it does not keep {rule} {of} yet, in RulesNotKeptYet's words ({unit}): today {today}");
        Check(!draft || (rules.Count > 1 && notKept.Contains("of §6's rules for the Eye: " + string.Join("; ", rules.SkipLast(1)) + "; and " + rules[^1], StringComparison.Ordinal)),
            "showbus spec: while 1.3 is a draft, §8's Stagewatch row names what of §6's rules it does not keep yet in one list, in RulesNotKeptYet's order");
        var entries = Regex.Matches(notKept, @"\(today ").Count;
        var owed = later.Count + WordsNotKeptYet.Length + RulesNotKeptYet.Length;
        Check(entries == owed || (!draft && entries > owed),
            $"showbus spec: while 1.3 is a draft, §8's Stagewatch row says nothing is not kept yet but what NotKeptYet, WordsNotKeptYet and RulesNotKeptYet hold ({entries} entries, {owed} held)");

        // Patterns' and PlayMap's exemptions.
        Check(Flat(PartOf("patterns")).Contains("`NAV`, `MENU`, `CUE LIST`, `ARCADE …` and `ASSISTANT …` stay open before pairing (§7.2)", StringComparison.Ordinal),
            "showbus spec: §8's Patterns row names scope A's queries that stay open before pairing");
        Check(Flat(PartOf("playmap")).Contains("- **Named exemptions**: TCP 9941 sends `STATE` on `HELLO`", StringComparison.Ordinal)
              && Flat(PartOf("playmap")).Contains("its owner's decision 17; it ends after Patterns' Round 96, once Patterns' node devices pair by proof", StringComparison.Ordinal),
            "showbus spec: §8's PlayMap row names its 9941 greeting as an exemption under its owner's decision 17, ending after Patterns' Round 96");
    }

    /// <summary>The header's sentence of what this tree does not keep of 1.3 yet that 2.19 brings, flattened; empty when there is none.</summary>
    static string Brought(string header)
        => Regex.Match(Flat(header), @"this tree does not keep these of 1\.3 yet either, which Stagewatch 2\.19 brings before it ships: (.+?)\.\s") is { Success: true } m
            ? m.Groups[1].Value : "";

    /// <summary>
    /// The header's words once this tree keeps every rule of 1.3 that 2.19 brings, in place of the sentence <see cref="Brought"/> reads.
    /// They speak of rules alone: 1.3 final and 2.19's other units are not rules of 1.3 a tree keeps.
    /// </summary>
    const string BringsNothingMore = "and this tree already keeps every rule of 1.3 that 2.19 brings";

    /// <summary>
    /// The header names what 2.19 brings: every row of <see cref="NotKeptYet"/> a unit of 2.19 ends, in its words, and no other row; and
    /// each entry of <see cref="NotKept219"/> exactly while its code still does what it says. The rows each 2.19 unit ends are in
    /// the header, never in the body, which 1.3 final freezes. Once none is left, the header says this tree keeps every rule of 1.3
    /// that 2.19 brings instead, and only then.
    /// </summary>
    static void Brings(Action<bool, string> Check, string header)
    {
        var sentence = Brought(header);
        var now = NotKeptYet.Where(x => In219(x.Unit)).ToList();
        var owed = now.Count > 0 || NotKept219.Any(x => x.Differs());
        var nothingMore = Flat(header).Contains(BringsNothingMore, StringComparison.Ordinal);
        Check((sentence.Length > 0) == owed && nothingMore != owed,
            owed ? "showbus spec: the header says what this tree does not keep of 1.3 yet, which Stagewatch 2.19 brings, and not that it keeps every rule 2.19 brings"
                 : "showbus spec: this tree keeps every rule of 1.3 that Stagewatch 2.19 brings, and the header says so in place of a list (\"" + BringsNothingMore + "\")");
        // An entry names its rows in code, then what the code does today: "`a`, `b` and `c` (today …)".
        foreach (var (row, unit, today, _) in now)
            Check(Regex.IsMatch(sentence, "`" + Regex.Escape(row) + @"`[^()]*\(today " + Regex.Escape(today) + @"\)"),
                $"showbus spec: the header says this tree does not keep {row} yet, in NotKeptYet's words ({unit}): today {today}");
        var listed = Regex.Matches(sentence, @"`([a-z][a-z0-9.]*)`(?=[^()]*\(today )").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Check(listed.SetEquals(now.Select(x => x.Row)), "showbus spec: the header's rows that 2.19 brings are NotKeptYet's rows a 2.19 unit ends ("
            + string.Join(", ", listed.Order(StringComparer.Ordinal)) + " / " + string.Join(", ", now.Select(x => x.Row).Distinct().Order(StringComparer.Ordinal)) + ")");
        foreach (var (what, unit, today, differs) in NotKept219)
            Check(In219(unit) && sentence.Contains(what + " (today " + today + ")", StringComparison.Ordinal) == differs(),
                $"showbus spec: the header names {what} exactly while Stagewatch does that ({unit}): today {today}"
                + (differs() ? "" : " — it keeps it now: take it out of the header's list"));
    }

    /// <summary>Whether two JSON texts are one value, their top-level fields in one order; false when either is not JSON.</summary>
    static bool SameJson(string a, string b)
    {
        try
        {
            using var x = JsonDocument.Parse(a);
            using var y = JsonDocument.Parse(b);
            static IEnumerable<string> Names(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Select(p => p.Name) : [];
            return JsonElement.DeepEquals(x.RootElement, y.RootElement) && Names(x.RootElement).SequenceEqual(Names(y.RootElement));
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Whether Stagewatch's receipts still carry none of §3's last fields.</summary>
    static bool ReceiptTailAsToday()
    {
        using var doc = JsonDocument.Parse(new CommandReceipt("a1", true, "applied", "", null).ToJson());
        return ReceiptTail.All(f => !doc.RootElement.TryGetProperty(f, out _));
    }

    /// <summary>Whether ProposalDesk's ids still restart with each desk: a new desk's first proposal is p1.</summary>
    static bool ProposalIdsAsToday()
        => Proposed("{\"from\":{\"t\":\"patterns\",\"id\":\"9f8e7d6c\",\"name\":\"FOH\"},\"target\":{\"t\":\"main\"},\"line\":\"note hi\"}", "low") is { Id: "p1" };

    /// <summary>
    /// What a new desk makes of <paramref name="json"/>, proposed with <paramref name="credential"/> (the operator's when none is given),
    /// when its target's table rates every line <paramref name="tableRisk"/>: the proposal it queued, or null when it refused it.
    /// </summary>
    static SharedProposal? Proposed(string json, string tableRisk, NodeCredential? credential = null)
        => new ProposalDesk(() => "abc123abc123", new NodeBindings()).Submit(json, credential ?? NodeCredential.Operator, (_, _) => tableRisk, Tonight).Proposal;

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

    /// <summary>The first ticked text in the first cell of each row of the table whose header starts <c>| header |</c>.</summary>
    static List<string> FirstCells(string text, string header)
    {
        var cells = new List<string>();
        var inTable = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith("| " + header + " |", StringComparison.Ordinal)) { inTable = true; continue; }
            if (!inTable) continue;
            if (!line.StartsWith('|')) break;
            if (line.StartsWith("|---", StringComparison.Ordinal)) continue;
            if (Regex.Match(line.Split('|')[1], "`([^`]+)`") is { Success: true } m) cells.Add(m.Groups[1].Value);
        }
        return cells;
    }

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
    static string Kid(byte[] k, int bytes) => Hex(Sha([.. Utf8("showbus-peerlink-1 kid\n"), .. k]))[..(bytes * 2)];
}
