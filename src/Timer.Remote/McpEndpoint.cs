using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;

namespace Stagewatch.Remote;

/// <summary>
/// Model Context Protocol (JSON-RPC 2.0 over HTTP POST /mcp, stateless): lets AI assistants and other
/// tools in the Patterns ecosystem read the show and drive it through the same single command
/// grammar as every other remote. Tools: get_state, get_running_order, run_command, get_report.
/// </summary>
public sealed class McpEndpoint
{
    public const string ProtocolVersion = "2025-06-18";
    private readonly IShowCommands _commands;
    private readonly IShowReports _reports;
    private readonly IShowAgents _agents;
    private readonly IShowContext? _context;
    private readonly Func<byte[]> _detailFrame;

    public McpEndpoint(IShowControl control, Func<byte[]> detailFrame) : this(control, control, control, detailFrame, control) { }

    /// <summary>MCP needs only these: run commands, read the report and the show's context, and route agents' commands to the operator.</summary>
    public McpEndpoint(IShowCommands commands, IShowReports reports, IShowAgents agents, Func<byte[]> detailFrame, IShowContext? context = null)
    {
        _commands = commands;
        _reports = reports;
        _agents = agents;
        _detailFrame = detailFrame;
        _context = context;
    }

    /// <summary>Handle one JSON-RPC message (or batch). Returns the reply, or null for notifications.</summary>
    public byte[]? Handle(byte[] body)
    {
        JsonNode? root;
        try { root = BusJson.Parse(body); }
        catch (JsonException) { return Encode(Error(null, -32700, "parse error")); }

        if (root is JsonArray batch)
        {
            var replies = new JsonArray();
            foreach (var m in batch) if (HandleOne(m as JsonObject) is { } r) replies.Add(r);
            return replies.Count == 0 ? null : Encode(replies);
        }
        return HandleOne(root as JsonObject) is { } one ? Encode(one) : null;
    }

    private JsonObject? HandleOne(JsonObject? msg)
    {
        if (msg is null) return Error(null, -32600, "invalid request");
        var id = msg["id"]?.DeepClone();
        if (Str(msg["method"]) is not { } method) return Error(id, -32600, "invalid request: method is a string");
        if (id is null) return null; // notification (e.g. notifications/initialized)

        switch (method)
        {
            case "initialize":
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = ProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = "stagewatch", ["title"] = "Stagewatch stage timer", ["version"] = typeof(McpEndpoint).Assembly.GetName().Version?.ToString(3) ?? "2" },
                    ["instructions"] = "Stagewatch is the live show clock. Read get_state before acting. Change the show only with run_command, " +
                                       "using one command per call. Never black out outputs or reset the timer unless the operator asked for it."
                });
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = Tools() });
            case "tools/call":
                return Call(msg["params"] as JsonObject) is { } called
                    ? Result(id, called)
                    : Error(id, -32602, "invalid params: name, arguments.command and arguments.reason are strings");
            default:
                return Error(id, -32601, "method not found: " + method);
        }
    }

    private static JsonArray Tools() => new()
    {
        Tool("get_state", "The live show: timer, phase, current and next item, schedule offset, curfew, forecast, messages, cues and acknowledgements, slides, Q&A.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        Tool("get_running_order", "Every running-order item with planned, expected and actual times (show time zone), status, delay and curfews.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        Tool("run_command", "Propose one Stagewatch command, e.g. 'nudge 60', 'next', 'trim 7 -180', 'delay 300', 'catchup', " +
                            "'signal message speaker \"Please wrap up\"', 'signal go Lighting', 'q next', 'aux 1 start', 'screen identify'. " +
                            "Usually the operator must approve it in Stagewatch first (the result says so); give a short reason they will read. Grammar: " + ShowCommandParser.Help,
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["command"] = new JsonObject { ["type"] = "string", ["description"] = "One command line" },
                    ["reason"] = new JsonObject { ["type"] = "string", ["description"] = "One sentence for the operator: why this helps" }
                },
                ["required"] = new JsonArray("command")
            }),
        Tool("get_report", "The planned-vs-actual show report as CSV.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        Tool("get_proposal", "What became of a command you proposed: waiting for the operator, approved (with each step's result) or discarded. " +
                             "Pass the proposal number run_command gave you.",
            new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject { ["id"] = new JsonObject { ["type"] = "integer", ["description"] = "The proposal number" } },
                ["required"] = new JsonArray("id")
            }),
        Tool("get_context", "The whole room at a glance (Show Bus context): this Stagewatch's light and one-line headline, its parts " +
                            "(timer, departments, links to Patterns, presenter PCs, screens) with their lights, recent events, the verbs you may " +
                            "propose with their risk, and every other show app it can hear (Patterns, AGMessage…) with their lights and headlines. " +
                            "Lights are facts: grey means unknown — say so, never guess.",
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })
    };

    private static JsonObject Tool(string name, string description, JsonObject schema)
        => new() { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };

    /// <summary>A tool's result, or null when a field it reads is there with the wrong type.</summary>
    private JsonObject? Call(JsonObject? p)
    {
        if (Str(p?["name"]) is not { } name) return null;
        var args = p?["arguments"] as JsonObject;
        switch (name)
        {
            case "get_state":
                return Text(Encoding.UTF8.GetString(_detailFrame()));
            case "get_running_order":
            {
                var frame = JsonNode.Parse(_detailFrame()) as JsonObject;
                return Text(frame?["timings"]?.ToJsonString() ?? "[]");
            }
            case "get_report":
                return Text(_reports.Report("csv") ?? "No running order yet.");
            case "run_command":
            {
                if (Str(args?["command"]) is not { } line || Str(args?["reason"]) is not { } reason) return null;
                var tokens = Tokenize(line);
                if (tokens.Count == 0) return Text("Empty command.", isError: true);
                if (!ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out var error))
                    return Text("Rejected: " + error + ". Grammar: " + ShowCommandParser.Help, isError: true);
                // Human in the loop: by default an agent proposes and the operator decides.
                if (_agents.ProposeFromAgent(line, reason) is { } queued) return Text(queued);
                // Acting directly: say what actually happened (Show Bus receipt), not just that it was sent.
                var receipt = _commands.ExecuteWithReceiptAsync(cmd, "mcp", TimeSpan.FromSeconds(3));
                if (!receipt.Wait(TimeSpan.FromSeconds(4))) return Text("Sent: " + line + " (Stagewatch hasn't confirmed it yet)");
                var r = receipt.Result;
                return r.Ok
                    ? Text($"{(r.Stage == "applied" ? "Done" : "Accepted")}: {line}{(r.Effect.Length > 0 ? " — " + r.Effect : "")}")
                    : Text($"Refused: {line} — {r.Error}", isError: true);
            }
            case "get_proposal":
            {
                var id = args?["id"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
                return _agents.ProposalJson(id) is { } json ? Text(json) : Text($"No proposal {id}.", isError: true);
            }
            case "get_context":
                return Text(_context?.ContextJson() ?? "{}");
            default:
                return Text("Unknown tool: " + name, isError: true);
        }
    }

    /// <summary>A string field: its text, "" when absent or null, null when it is there with another type.</summary>
    private static string? Str(JsonNode? n) => n is null ? "" : n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : null;

    private static JsonObject Text(string text, bool isError = false)
        => new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError };

    private static JsonObject Result(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static byte[] Encode(JsonNode n) => Encoding.UTF8.GetBytes(n.ToJsonString());

    public static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (!quoted && char.IsWhiteSpace(ch)) { if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); } continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }
}

/// <summary>QR codes as SVG, for the audience "ask a question" link and phone/watch pairing.</summary>
public static class QrSvg
{
    public static string Render(string text)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        return new QRCoder.SvgQRCode(data).GetGraphic(8, "#000000", "#FFFFFF", drawQuietZones: true);
    }
}
