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
    private readonly IShowControl _control;
    private readonly Func<byte[]> _detailFrame;

    public McpEndpoint(IShowControl control, Func<byte[]> detailFrame)
    {
        _control = control;
        _detailFrame = detailFrame;
    }

    /// <summary>Handle one JSON-RPC message (or batch). Returns the reply, or null for notifications.</summary>
    public byte[]? Handle(byte[] body)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(body); }
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
        var method = msg["method"]?.GetValue<string>() ?? "";
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
                return Result(id, Call(msg["params"] as JsonObject));
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
            new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() })
    };

    private static JsonObject Tool(string name, string description, JsonObject schema)
        => new() { ["name"] = name, ["description"] = description, ["inputSchema"] = schema };

    private JsonObject Call(JsonObject? p)
    {
        var name = p?["name"]?.GetValue<string>() ?? "";
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
                return Text(_control.Report("csv") ?? "No running order yet.");
            case "run_command":
            {
                var line = args?["command"]?.GetValue<string>() ?? "";
                var tokens = Tokenize(line);
                if (tokens.Count == 0) return Text("Empty command.", isError: true);
                if (!ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out var error))
                    return Text("Rejected: " + error + ". Grammar: " + ShowCommandParser.Help, isError: true);
                // Human in the loop: by default an agent proposes and the operator decides.
                var reason = args?["reason"]?.GetValue<string>() ?? "";
                if (_control.ProposeFromAgent(line, reason) is { } queued) return Text(queued);
                _control.Execute(cmd);
                return Text("OK: " + line);
            }
            default:
                return Text("Unknown tool: " + name, isError: true);
        }
    }

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
