using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// Optional producer assistant backed by Claude. Strictly opt-in (it sends the running order and
/// timings to Anthropic) and strictly advisory: suggestions come back as structured JSON, every
/// proposed command is re-validated through the show's own command grammar and an allow-list, and
/// nothing runs until the operator clicks it. Runs off the show thread, at most once every two
/// minutes, and any failure just leaves the rule-based suggestions in place.
/// </summary>
public sealed class CloudAdvisor : IIntegration
{
    public const string ModelId = "claude-opus-5-5";
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(2);

    /// <summary>Verbs the model may propose. Nothing that blacks out, resets or changes what the audience sees.</summary>
    private static readonly HashSet<string> AllowedVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "trim", "delay", "nudge", "signal", "catchup", "q", "note" };

    private readonly AnthropicClient _client;
    private DateTime _lastAsk = DateTime.MinValue;
    private int _busy;
    private IReadOnlyList<Advice> _latest = Array.Empty<Advice>();

    public CloudAdvisor(string apiKey)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
    }

    public string Name => "Assistant (Claude)";
    public LinkHealth Health { get; private set; } = LinkHealth.Starting;
    public string Status { get; private set; } = "waiting for the show to start";

    /// <summary>The newest validated suggestions (latest-wins).</summary>
    public IReadOnlyList<Advice> Latest => Volatile.Read(ref _latest);

    /// <summary>Owner thread: hand over a summary; the request runs on the thread pool if it's time.</summary>
    public void MaybeAsk(string showSummaryJson, DateTime nowUtc)
    {
        if (nowUtc - _lastAsk < MinInterval) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _lastAsk = nowUtc;
        _ = Task.Run(async () =>
        {
            try { await AskAsync(showSummaryJson); }
            finally { Volatile.Write(ref _busy, 0); }
        });
    }

    private const string SystemPrompt =
        "You assist the show caller at a live corporate event. You get the running order with planned, expected and actual " +
        "times, curfews (hard-outs), the live item and recent cues. Propose at most 4 short, practical suggestions that help the " +
        "show finish on time without rushing speakers needlessly (rushing hurts interpretation and the audience). Each suggestion " +
        "is one sentence. If a suggestion maps to a Stagewatch command, give it in 'command', using only these forms: " +
        "'trim <item number> <negative seconds>', 'delay <seconds>', 'nudge <±seconds>', 'catchup', " +
        "'signal message <speaker|moderator> \"<text>\"', 'signal call backstage \"<text>\"', 'q next', 'note \"<text>\"'. " +
        "Otherwise give an empty command. Never invent items that aren't in the data. If the show is on track, say so in one suggestion.";

    private async Task AskAsync(string summary)
    {
        try
        {
            var schema = new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
                ["required"] = JsonSerializer.SerializeToElement(new[] { "suggestions" }),
                ["properties"] = JsonSerializer.SerializeToElement(new
                {
                    suggestions = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            required = new[] { "text", "command" },
                            properties = new { text = new { type = "string" }, command = new { type = "string" } }
                        }
                    }
                })
            };

            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = 16000,
                // Refusal fallback (on by default): a declined request is re-run on Opus 4.8 inside the same call.
                Betas = ["server-side-fallback-2026-06-01"],
                Fallbacks = new List<BetaFallbackParam> { new() { Model = "claude-opus-4-8" } },
                OutputConfig = new BetaOutputConfig
                {
                    Effort = Effort.Low,
                    Format = new BetaJsonOutputFormat { Schema = schema },
                },
                System = SystemPrompt,
                Messages = [new() { Role = Role.User, Content = summary }],
            });

            if (response.StopReason == "refusal")
            {
                Health = LinkHealth.Error;
                Status = "the assistant declined this request — rule-based suggestions only";
                return;
            }

            var text = new StringBuilder();
            foreach (var block in response.Content)
                if (block.TryPickText(out var t)) text.Append(t.Text);

            var parsed = Parse(text.ToString());
            Volatile.Write(ref _latest, parsed);
            Health = LinkHealth.Ok;
            Status = $"{parsed.Count} suggestion(s) at {DateTime.Now:HH:mm}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Offline, bad key, rate limited… the show goes on with the rule-based assistant.
            Health = LinkHealth.Error;
            Status = "unavailable: " + ex.Message;
        }
    }

    /// <summary>
    /// Pure: model JSON → advice. Commands are untrusted: kept only when they parse in the shared
    /// grammar and use an allowed verb; otherwise the suggestion is shown as text only.
    /// </summary>
    public static IReadOnlyList<Advice> Parse(string json)
    {
        var list = new List<Advice>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("suggestions", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            var n = 0;
            foreach (var s in arr.EnumerateArray().Take(4))
            {
                var text = s.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString() ?? "" : "";
                var cmd = s.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                if (text.Length == 0) continue;
                if (text.Length > 240) text = text[..240];
                list.Add(new Advice($"ai-{n++}", AdviceLevel.Suggest, "Assistant: " + text, Validate(cmd), Validate(cmd) is null ? null : "Apply"));
            }
        }
        catch (JsonException) { }
        return list;
    }

    public static string? Validate(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 300) return null;
        var tokens = Tokenize(command.Trim());
        if (tokens.Count == 0 || !AllowedVerbs.Contains(tokens[0])) return null;
        return ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out _, out _) ? command.Trim() : null;
    }

    /// <summary>Space-separated tokens with "quoted strings".</summary>
    public static List<string> Tokenize(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        foreach (var ch in line)
        {
            if (ch == '"') { quoted = !quoted; continue; }
            if (!quoted && char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    public void Dispose() { }
}
