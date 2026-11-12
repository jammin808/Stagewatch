using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Remote;
using Stagewatch.Show;

/// <summary>
/// MCP proposes to other apps, and a person always decides (Show Bus §11, release B): run_command with a target is a pending proposal
/// on this desk from the agent, rated by the app's verified table, even where agents act directly, and nothing runs from the call;
/// what the desk refuses is refused unqueued, the line never quoted; get_proposal follows a string id as well as a number;
/// get_context reads another app's context only while it proves that app; and a surface with no desk refuses all three in words.
/// </summary>
static class McpTargetTests
{
    public static void Run(Action<bool, string> Check)
    {
        Tools(Check);
        Target(Check);
        Gates(Check);
        AgentQueue(Check);
        Refusals(Check);
        Proposals(Check);
        Contexts(Check);
        NoDesk(Check);
    }

    /// <summary>A surface whose agents act directly, hosting the desk as the app and the rig do: the three calls go to the service.</summary>
    sealed class AgentDesk(DeskTests.Bench b) : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new([], [], []);
        public IReadOnlyList<ApiKey> ApiKeys => [];
        public IReadOnlyList<IPAddress> PlayoutFrom => [];
        public string? ContextJson() => "{\"own\":true}";
        public AgentReply ProposeToApp(string app, string line, string reason) => b.Desk.ProposeToApp(app, line, reason);
        public (int Status, string Json) GetProposal(string id) => b.Desk.GetProposal(id);
        public (string? Json, string? Error) PeerContextJson(string app) => b.Desk.PeerContextJson(app);
    }

    /// <summary>A surface that keeps every default (no desk, no peers' contexts), whose agents act directly.</summary>
    sealed class Bare : IShowControl
    {
        public SnapshotHub Snapshots { get; } = new();
        public List<ShowCommand> Commands { get; } = new();
        public void Execute(ShowCommand command) { lock (Commands) Commands.Add(command); }
        public RemoteConfig GetRemoteConfig() => new([], [], []);
    }

    static McpEndpoint Endpoint(IShowControl control) => new(control, () => "{}"u8.ToArray());

    static JsonNode Rpc(McpEndpoint mcp, string method, object? @params = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = 1, method, @params });
        return JsonNode.Parse(mcp.Handle(body, _ => null)!)!["result"]!;
    }

    /// <summary>run_command through a sender's gates, as the web server hands them: a kind's, a line for another app's, and who hears the desk's refusals.</summary>
    static JsonNode Gated(McpEndpoint mcp, object args, Func<ShowCommandKind, string?> gate, Func<string?>? proposes = null, Action<string>? refused = null)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "run_command", arguments = args } });
        return JsonNode.Parse(mcp.Handle(body, gate, proposes, refused)!)!["result"]!;
    }

    static JsonNode Call(McpEndpoint mcp, string tool, object args) => Rpc(mcp, "tools/call", new { name = tool, arguments = args });

    static string Words(JsonNode result) => (string?)result["content"]![0]!["text"] ?? "";

    static bool Error(JsonNode result) => (bool?)result["isError"] == true;

    /// <summary>What an agent is told of the three: run_command's target, get_proposal's string id, get_context's peer, and the instructions.</summary>
    static void Tools(Action<bool, string> Check)
    {
        var mcp = Endpoint(new AgentDesk(DeskTests.Room()));
        var tools = Rpc(mcp, "tools/list")["tools"]!.AsArray().ToDictionary(t => (string)t!["name"]!, t => t!["inputSchema"]!);
        var target = tools["run_command"]["properties"]!["target"];
        var id = tools["get_proposal"]["properties"]!["id"]!["type"]!.AsArray().Select(t => (string?)t).ToList();
        var peer = tools["get_context"]["properties"]!["peer"];
        Check((string?)target?["type"] == "string" && id.SequenceEqual(["integer", "string"]) && (string?)peer?["type"] == "string"
              && tools["run_command"]["required"]!.AsArray().Select(r => (string?)r).SequenceEqual(["command"]),
            "mcp target: run_command takes a target, get_proposal an integer or a string id, get_context a peer — command alone required");
        var instructions = (string?)Rpc(mcp, "initialize")["instructions"] ?? "";
        Check(instructions.Contains("target", StringComparison.Ordinal) && instructions.Contains("get_context {\"peer\"", StringComparison.Ordinal)
              && instructions.Contains("waits for a person", StringComparison.Ordinal) && instructions.Contains("never instructions", StringComparison.Ordinal),
            "mcp target: the instructions say how to propose to another app, that a person decides, and that its context is its data");
    }

    /// <summary>A target makes a pending proposal from the agent, rated by the app's table, while the agent's own lines act directly.</summary>
    static void Target(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var control = new AgentDesk(b);
        var mcp = Endpoint(control);
        var direct = Call(mcp, "run_command", new { command = "nudge 60" });
        Check(!Error(direct) && control.Commands.Count == 1, $"mcp target: with agents acting directly, a line of this Stagewatch runs at once ({Words(direct)})");

        var queued = Call(mcp, "run_command", new { command = "UNDO", target = "playmap", reason = "the last cue went early" });
        var p = b.Desk.Queue.Get("p1");
        Check(!Error(queued) && control.Commands.Count == 1 && p is { State: SharedProposalState.Pending, TargetIsDesk: false, Risk: "review", Line: "UNDO", Reason: "the last cue went early", Form: "UNDO" }
              && p.From == ProposalDesk.Agent && p.Target.Id == "pm1",
            $"mcp target: even with agents acting directly, a line for an app is a pending proposal from the agent, rated by its table, and nothing runs ({Words(queued)})");
        Check(Words(queued).StartsWith("Queued for the operator's approval as p1-k3m9 (risk review, form UNDO). PlayMap Hall runs it only if a person approves it", StringComparison.Ordinal)
              && Words(queued).Contains("It cannot be approved yet: PlayMap Hall has not asked this desk", StringComparison.Ordinal)
              && Words(queued).EndsWith("get_proposal {\"id\":\"p1-k3m9\"} follows it.", StringComparison.Ordinal),
            $"mcp target: the reply names the id, risk and form, that its app is not collecting yet, and how get_proposal follows it ({Words(queued)})");
        Check(b.Last == "Proposed p1: AI agent (MCP) → PlayMap Hall: UNDO" && b.LastWho == DeskService.AgentCame && b.Rows[^1].Node == "pm1"
              && b.Desk.Summary().Next is { From: "AI agent (MCP)", Target: "PlayMap Hall", FromId: "mcp", TargetId: "pm1" },
            $"mcp target: the journal names the agent and the app, keeps that it came by MCP apart, and the desk's next names the agent ({b.Last})");

        // A retried call — the same line for the same app while the first waits — is that one: one decision, never the line run twice.
        var rows = b.Rows.Count;
        var again = Call(mcp, "run_command", new { command = "UNDO", target = "PlayMap Hall", reason = "retried" });
        Check(!Error(again) && Words(again).StartsWith("Already waiting for the operator's approval as p1-k3m9 — the same line for PlayMap Hall, asked again, is that one (risk review, form UNDO).", StringComparison.Ordinal)
              && b.Desk.Queue.All().Count == 1 && b.Rows.Count == rows && b.Desk.Queue.Get("p1") is { Reason: "the last cue went early" },
            $"mcp target: the same line for the same app while it waits names the waiting one, and nothing new is queued or journalled ({Words(again)})");
        var other = Call(mcp, "run_command", new { command = "undo", target = "pm1" });
        Check(!Error(other) && Words(other).StartsWith("Queued for the operator's approval as p2-k3m9", StringComparison.Ordinal) && b.Desk.Queue.All().Count == 2
              && b.Desk.Decide("p2", approve: false, DeskOrigin.Operator).Ok,
            "mcp target: a line that differs, even by its case alone, is a proposal of its own");

        // The app collects: a later proposal is told nothing about collecting; a high one is queued high, and next never approves it.
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        var panic = Call(mcp, "run_command", new { command = "PANIC", target = "pm1" });
        Check(!Error(panic) && Words(panic).StartsWith("Queued for the operator's approval as p3-k3m9 (risk high, form PANIC).", StringComparison.Ordinal)
              && !Words(panic).Contains("cannot be approved yet", StringComparison.Ordinal) && b.Desk.Queue.Get("p3") is { Risk: "high", State: SharedProposalState.Pending },
            $"mcp target: an app's high line is queued high, and a collecting app is not said to be silent ({Words(panic)})");
        var next = b.Desk.Decide("next", approve: true, DeskOrigin.Operator);
        var high = b.Desk.Decide("p3", approve: true, DeskOrigin.Operator, high: true);
        Check(next.Ok && b.Desk.Queue.Get("p1") is { State: SharedProposalState.Approved } && high.Ok && b.Desk.Queue.Get("p3") is { State: SharedProposalState.Approved },
            "mcp target: a person approves the agent's line, the high one by its id and the word");
    }

    /// <summary>
    /// A line for another app passes its sender's gate before the desk hears it — by default the gate of a line that drives another
    /// node's air — and the desk's refusals are handed to whoever shows them at the desk, in its words, never with the line.
    /// </summary>
    static void Gates(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var mcp = Endpoint(new AgentDesk(b));
        var asked = new List<ShowCommandKind>();
        var heard = new List<string>();
        var shut = Gated(mcp, new { command = "UNDO", target = "pm1" }, kind => { asked.Add(kind); return "refused at the door"; }, refused: heard.Add);
        Check(Error(shut) && Words(shut) == "Refused: refused at the door" && asked.SequenceEqual([ShowCommandKind.Playout]) && b.Desk.Queue.All().Count == 0 && heard.Count == 0,
            $"mcp target gates: with no gate of its own, a line for another app is refused by the gate of a line that drives another node's air, nothing queued ({Words(shut)})");
        var own = Gated(mcp, new { command = "UNDO", target = "pm1" }, _ => "refused at the door", () => "a line for another show app is refused here", heard.Add);
        Check(Error(own) && Words(own) == "Refused: a line for another show app is refused here" && b.Desk.Queue.All().Count == 0,
            "mcp target gates: its own gate, when given, decides it");
        var open = Gated(mcp, new { command = "UNDO", target = "pm1" }, _ => "refused at the door", () => null, heard.Add);
        var unlisted = Gated(mcp, new { command = "FROBNICATE the wall", target = "pm1" }, _ => null, () => null, heard.Add);
        var secret = Gated(mcp, new { command = "LINK STAGEWATCH 10.0.0.5 opensesame", target = "pm1" }, _ => null, () => null, heard.Add);
        Check(!Error(open) && b.Desk.Queue.All().Count == 1 && Error(unlisted) && Error(secret)
              && heard.SequenceEqual(["PlayMap Hall does not list that line — see its verbs", LineFence.SecretWords("PlayMap Hall")])
              && !heard.Exists(h => h.Contains("FROBNICATE", StringComparison.Ordinal) || h.Contains("opensesame", StringComparison.Ordinal)),
            $"mcp target gates: a sender it passes reaches the desk; each refusal the desk gives is handed on in its words, never the line, and a queued one is not ({string.Join(" / ", heard)})");
    }

    /// <summary>
    /// The queue keeps the agent's rules on its own, whoever calls it: never this desk; §11's line rules and secrets; a line its app does
    /// not list, in words that never quote it; the same line again while it waits is that one. And the agent is no node: a node that
    /// holds the id mcp follows its own proposals alone.
    /// </summary>
    static void AgentQueue(Action<bool, string> Check)
    {
        var nodes = new NodeBindings();
        var desk = new ProposalDesk(() => "desk01", nodes, "k3m9");
        var raised = 0;
        desk.Changed += _ => raised++;
        var t0 = new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc);
        var hall = new NodeRef("playmap", "pm1", "PlayMap Hall");
        var listed = new RiskVerdict("review", "UNDO", "", Listed: true, Secret: false);
        var unlisted = new RiskVerdict("high", null, "", Listed: false, Secret: false);
        var secret = new RiskVerdict("high", "LINK STAGEWATCH <host> <token>", "", Listed: true, Secret: true);
        const string Desk = "an agent proposes a line to this Stagewatch with no target — a target is another app";
        Check(desk.ProposeFromAgent(new NodeRef("main", "", ""), "note hi", "", listed, t0) is (null, Desk, false)
              && desk.ProposeFromAgent(new NodeRef("playmap", "desk01", "x"), "note hi", "", listed, t0) is (null, Desk, false)
              && desk.ProposeFromAgent(new NodeRef("main", "pm1", ""), "note hi", "", listed, t0) is (null, Desk, false),
            "mcp agent queue: a target of kind main, or with this desk's id, is this desk — refused");
        Check(desk.ProposeFromAgent(hall, "  ", "", listed, t0) is (null, "line is required", false)
              && desk.ProposeFromAgent(hall, "UNDO\tnow", "", listed, t0) is (null, LineFence.Control, false)
              && desk.ProposeFromAgent(hall, "LINK STAGEWATCH h opensesame", "", secret, t0) is (null, { } filled, false) && filled == LineFence.SecretWords("PlayMap Hall")
              && desk.ProposeFromAgent(hall, "FROBNICATE the wall", "", unlisted, t0) is (null, "PlayMap Hall does not list that line — see its verbs", false),
            "mcp agent queue: an empty line, §11's line rules, a filled secret and an unlisted line are refused, the line never quoted");
        Check(desk.All().Count == 0 && raised == 0, "mcp agent queue: a refusal queues and raises nothing");
        var first = desk.ProposeFromAgent(hall, " UNDO ", "why", listed, t0);
        var again = desk.ProposeFromAgent(hall, "UNDO", "again", listed, t0.AddSeconds(1));
        var elsewhere = desk.ProposeFromAgent(new NodeRef("playmap", "mcp", "PlayMap mcp"), "UNDO", "", listed, t0.AddSeconds(2));
        Check(first is ({ Id: "p1-k3m9", Line: "UNDO", Reason: "why", State: SharedProposalState.Pending }, null, false) && first.Proposal!.From == ProposalDesk.Agent
              && again is ({ Id: "p1-k3m9", Reason: "why" }, null, true) && elsewhere is ({ Id: "p2-k3m9" }, null, false) && raised == 2,
            "mcp agent queue: the same line for the same app while it waits is that one, nothing raised; for another app it is its own");
        // A node may hold the id mcp: what it follows as its own is its own, never the agent's.
        var body = JsonSerializer.Serialize(new { from = new { t = "playmap", id = "mcp" }, target = new { t = "patterns", id = "pat1" }, line = "note hi" });
        var mine = desk.Submit(body, NodeCredential.Key("kX"), (_, _) => "review", t0);
        Check(mine.Proposal is { Id: "p3-k3m9" } && desk.From("mcp").Select(p => p.Id).SequenceEqual(["p3-k3m9"]) && desk.All().Count == 3,
            "mcp agent queue: a node whose id is mcp follows its own proposals alone, never the agent's");
    }

    /// <summary>What the desk refuses is refused unqueued, in words that never quote the line.</summary>
    static void Refusals(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        b.Hear("playmap", "pm2", "PlayMap Side");
        b.Post("playmap", "pm2", "PlayMap Side", NodeCredential.None, DeskTests.PlayMapVerbs);
        var control = new AgentDesk(b);
        var mcp = Endpoint(control);
        var rows = b.Rows.Count;
        JsonNode Propose(object args) => Call(mcp, "run_command", args);
        bool Refused(JsonNode r, string words) => Error(r) && Words(r) == "Refused: " + words;

        var own = Propose(new { command = "UNDO", target = "stagewatch" });
        var none = Propose(new { command = "UNDO", target = "nosuch" });
        var unlisted = Propose(new { command = "FROBNICATE the wall", target = "pm1" });
        var keyed = Propose(new { command = "LINK STAGEWATCH 10.0.0.5 swk_abcdefghijkmnpqrstuvwxyz234567", target = "pm1" });
        var secret = Propose(new { command = "LINK STAGEWATCH 10.0.0.5 opensesame", target = "pm1" });
        var unverified = Propose(new { command = "UNDO", target = "pm2" });
        var longLine = Propose(new { command = "MESSAGE " + new string('a', ProposalDesk.MaxLine), target = "pm1" });
        var badReason = Propose(new { command = "UNDO", target = "pm1", reason = "swk_abcdefghijkmnpqrstuvwxyz234567" });
        var empty = Propose(new { command = "  ", target = "pm1" });
        var notText = Propose(new { command = "UNDO", target = 7 });
        Check(Error(own) && Words(own).Contains("is this Stagewatch", StringComparison.Ordinal)
              && Error(none) && Words(none).Contains("no app called 'nosuch'", StringComparison.Ordinal),
            $"mcp target refusals: this Stagewatch is no target, and a word that names no app is said so ({Words(own)} / {Words(none)})");
        Check(Refused(unlisted, "PlayMap Hall does not list that line — see its verbs") && !Words(unlisted).Contains("FROBNICATE", StringComparison.Ordinal),
            $"mcp target refusals: a line the app's table does not list is refused, never quoted ({Words(unlisted)})");
        Check(Refused(keyed, LineFence.Key) && Refused(secret, LineFence.SecretWords("PlayMap Hall")) && !Words(secret).Contains("opensesame", StringComparison.Ordinal),
            $"mcp target refusals: a key and a filled secret are refused in the desk's own words ({Words(keyed)} / {Words(secret)})");
        Check(Error(unverified) && Words(unverified).Contains("PlayMap Side's verbs are not verified", StringComparison.Ordinal),
            $"mcp target refusals: an app whose verbs prove nothing takes no line ({Words(unverified)})");
        Check(Error(longLine) && Words(longLine).Contains(ProposalDesk.MaxLine.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
              && Refused(badReason, "its reason: " + LineFence.Key) && Refused(empty, "line is required")
              && Error(notText) && Words(notText) == "Refused: target is another app's id or name, as a string.",
            $"mcp target refusals: over 2048 characters, a reason holding a key, an empty line and a target that is no string ({Words(longLine)})");
        Check(b.Desk.Queue.All().Count == 0 && b.Rows.Count == rows && control.Commands.Count == 0
              && !b.Rows.Exists(r => r.Words.Contains("swk_", StringComparison.Ordinal) || r.Words.Contains("opensesame", StringComparison.Ordinal)),
            "mcp target refusals: nothing is queued, journalled or run");

        // An empty target is this Stagewatch's own line, through its own grammar and rules as before.
        var ownLine = Propose(new { command = "peer playmap UNDO", target = "" });
        Check(Refused(ownLine, DeskOrigin.PersonSends) && b.Desk.Queue.All().Count == 0,
            $"mcp target refusals: an empty target is this Stagewatch's line, where peer is still a person's ({Words(ownLine)})");
    }

    /// <summary>get_proposal reads a shared proposal by its string id, whole or short, and a number as before.</summary>
    static void Proposals(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        var mcp = Endpoint(new AgentDesk(b));
        Call(mcp, "run_command", new { command = "UNDO", target = "pm1" });
        JsonNode? Read(JsonNode r) => Error(r) ? null : JsonNode.Parse(Words(r));
        var whole = Read(Call(mcp, "get_proposal", new { id = "p1-k3m9" }));
        var shortId = Read(Call(mcp, "get_proposal", new { id = " p1 " }));
        Check((string?)whole?["id"] == "p1-k3m9" && (string?)whole["state"] == "pending" && (string?)whole["from"]!["t"] == "mcp" && (string?)whole["target"]!["id"] == "pm1"
              && (string?)shortId?["id"] == "p1-k3m9",
            "mcp get_proposal: a string id, whole or as p<n>, reads the shared proposal");
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        b.Desk.Decide("p1", approve: true, DeskOrigin.Operator);
        b.Desk.CollectApproved("pm1", NodeCredential.Key("kM"));
        b.Desk.ProposalResult("p1-k3m9", "{\"ok\":true,\"words\":\"undone\"}", NodeCredential.Key("kM"));
        var done = Read(Call(mcp, "get_proposal", new { id = "p1" }));
        Check((string?)done?["state"] == "done" && (bool?)done["ok"] == true && (string?)done["words"] == "undone" && (string?)done["decidedBy"] == "operator",
            "mcp get_proposal: it follows the line to its app's own words once a person approved it");
        var old = Call(mcp, "get_proposal", new { id = "p1-zzzz" });
        var missing = Call(mcp, "get_proposal", new { id = "p9" });
        var number = Call(mcp, "get_proposal", new { id = 3 });
        var numeral = Call(mcp, "get_proposal", new { id = "3" });
        var wrong = Call(mcp, "get_proposal", new { id = true });
        Check(Error(old) && Words(old).Contains("restarted since p1-zzzz", StringComparison.Ordinal) && Error(missing) && Words(missing) == "no such proposal"
              && Error(number) && Words(number) == "No proposal 3." && Error(numeral) && Words(numeral) == "No proposal 3."
              && Error(wrong) && Words(wrong).StartsWith("get_proposal takes the id run_command gave", StringComparison.Ordinal),
            $"mcp get_proposal: another run's id says the desk restarted, a number (or a numeral) is this Stagewatch's own, and anything else is an error ({Words(old)})");
    }

    /// <summary>get_context gives another app's context only while it proves that app.</summary>
    static void Contexts(Action<bool, string> Check)
    {
        var b = DeskTests.Room();
        b.Hear("playmap", "pm2", "PlayMap Side");
        b.Post("playmap", "pm2", "PlayMap Side", NodeCredential.None, DeskTests.PlayMapVerbs);
        var mcp = Endpoint(new AgentDesk(b));
        var mine = Call(mcp, "get_context", new { });
        var byId = Call(mcp, "get_context", new { peer = "pat1" });
        var byName = Call(mcp, "get_context", new { peer = "AGMessage" });
        Check(!Error(mine) && Words(mine) == "{\"own\":true}"
              && !Error(byId) && (string?)JsonNode.Parse(Words(byId))!["node"]!["id"] == "pat1"
              && !Error(byName) && (string?)JsonNode.Parse(Words(byName))!["node"]!["id"] == "ag1",
            "mcp get_context: with no peer this Stagewatch's own; with one, that app's verified context, by id or by name");
        var unverified = Call(mcp, "get_context", new { peer = "pm2" });
        var ambiguous = Call(mcp, "get_context", new { peer = "playmap" });
        var own = Call(mcp, "get_context", new { peer = "stagewatch" });
        var never = Call(mcp, "get_context", new { peer = "pm9" });
        var notText = Call(mcp, "get_context", new { peer = 1 });
        Check(Error(unverified) && Words(unverified).StartsWith("PlayMap Side's context is not verified", StringComparison.Ordinal)
              && !Words(unverified).Contains("PLAYLIST", StringComparison.Ordinal)
              && Error(ambiguous) && Words(ambiguous).Contains("two apps answer 'playmap'", StringComparison.Ordinal)
              && Error(own) && Error(never) && Error(notText) && Words(notText) == "peer is another app's id or name, as a string.",
            $"mcp get_context: an unverified context is an error that carries none of its words; two apps of a kind, this Stagewatch, an unknown id and a non-string are errors ({Words(unverified)})");
        b.Hear("playmap", "pm3", "PlayMap Annex");
        var silent = Call(mcp, "get_context", new { peer = "pm3" });
        Check(Error(silent) && Words(silent) == "PlayMap Annex has told this desk no context", $"mcp get_context: an app heard but with no context says so ({Words(silent)})");
    }

    /// <summary>A surface with no desk and no peers' contexts refuses a target, a string id and a peer in words.</summary>
    static void NoDesk(Action<bool, string> Check)
    {
        var control = new Bare();
        var mcp = Endpoint(control);
        var target = Call(mcp, "run_command", new { command = "UNDO", target = "playmap" });
        var id = Call(mcp, "get_proposal", new { id = "p1" });
        var peer = Call(mcp, "get_context", new { peer = "playmap" });
        Check(Error(target) && Words(target) == IShowApprovals.NoDesk && control.Commands.Count == 0
              && Error(id) && Error(peer) && Words(peer) == IShowContext.NoPeerContexts,
            $"mcp target: where no desk is kept, a target is refused unrun, and a shared id and a peer's context are errors ({Words(id)})");
    }
}
