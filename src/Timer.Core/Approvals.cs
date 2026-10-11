using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stagewatch.Core;

/// <summary>A node as a proposal names it (Show Bus §11).</summary>
public sealed record NodeRef(string Kind, string Id, string Name);

public enum SharedProposalState { Pending, Approved, Rejected, Done, Failed, Undelivered }

/// <summary>One AI proposal on the shared queue: who proposes, which node is to act, the line in that node's grammar.</summary>
public sealed record SharedProposal(
    string Id, NodeRef From, NodeRef Target, string Line, string Reason, string Risk, bool TargetIsDesk,
    SharedProposalState State, DateTime CreatedUtc, DateTime? DecidedUtc = null, string DecidedBy = "",
    DateTime? ClaimedUtc = null, string Result = "")
{
    public string StateName => State.ToString().ToLowerInvariant();

    public object ToJson() => new
    {
        id = Id,
        from = new { t = From.Kind, id = From.Id, name = From.Name },
        target = new { t = Target.Kind, id = Target.Id, name = Target.Name },
        line = Line, reason = Reason, risk = Risk, state = StateName,
        created = new DateTimeOffset(CreatedUtc).ToUnixTimeMilliseconds(),
        decided = DecidedUtc is { } d ? new DateTimeOffset(d).ToUnixTimeMilliseconds() : (long?)null,
        decidedBy = DecidedBy, result = Result,
        // The outcome in the shape a proposer checks first: ok (null until it ran) and the target's words.
        ok = State == SharedProposalState.Done ? true : State is SharedProposalState.Failed or SharedProposalState.Undelivered ? false : (bool?)null,
        words = Result,
    };
}

/// <summary>
/// The shared approval queue (Show Bus §11), held by the desk: every app's AI proposes here, one person approves, and each
/// target collects what was approved for it, runs it through its own command path and reports the result. The desk never
/// holds another app's credentials. Pure and thread-safe; the app's UI mirrors it and journals the decisions.
/// </summary>
public sealed class ProposalDesk
{
    public const int MaxOpen = 200, MaxLine = 400, MaxReason = 400;
    /// <summary>An approved proposal the target hasn't collected in this long is undelivered.</summary>
    public static readonly TimeSpan CollectWithin = TimeSpan.FromSeconds(60);
    /// <summary>A collected one with no result in this long is undelivered too (the target went away).</summary>
    public static readonly TimeSpan ResultWithin = TimeSpan.FromMinutes(5);

    private readonly object _lock = new();
    private readonly Dictionary<string, SharedProposal> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _keyNode = new(StringComparer.Ordinal);
    private readonly Func<string> _deskId;
    private int _next;

    public ProposalDesk(Func<string> deskId) => _deskId = deskId;

    /// <summary>Raised (on the caller's thread) whenever a proposal is added or changes.</summary>
    public event Action<SharedProposal>? Changed;

    /// <summary>
    /// A node key belongs to the first node id it spoke for, and a node id to the first key that spoke for it: neither can
    /// then act as another (one app's key can't collect another app's approvals). False when that would break the pairing.
    /// </summary>
    public bool BindKey(string keyId, string nodeId)
    {
        lock (_lock)
        {
            if (_keyNode.TryGetValue(keyId, out var bound)) return bound == nodeId;
            if (_keyNode.Any(kv => kv.Value == nodeId)) return false;
            _keyNode[keyId] = nodeId;
            return true;
        }
    }

    public string? NodeOfKey(string keyId) { lock (_lock) return _keyNode.TryGetValue(keyId, out var n) ? n : null; }

    /// <summary>
    /// A proposal from the network. <paramref name="nodeKeyId"/> is the node key that sent it (null for the PIN, a control
    /// or an MCP key). <paramref name="risk"/> says how risky a line is for its target (the target's own word). Returns the
    /// proposal, or why not.
    /// </summary>
    public (SharedProposal? Proposal, string? Error) Submit(string json, string? nodeKeyId, Func<NodeRef, string, string> risk, DateTime nowUtc)
    {
        JsonObject root;
        try { root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { return (null, "a proposal is a JSON object"); }
        var from = Ref(root["from"]);
        var target = Ref(root["target"]);
        var line = Str(root["line"], MaxLine);
        if (from is null || from.Id.Length == 0) return (null, "from.t and from.id are required");
        if (target is null) return (null, "target.t is required");
        if (line.Length == 0) return (null, "line is required");
        if (nodeKeyId is not null && !BindKey(nodeKeyId, from.Id)) return (null, "this key speaks for another node (or that node has its own key)");
        var deskId = _deskId();
        var toDesk = target.Kind == "main" && (target.Id.Length == 0 || target.Id == deskId);
        if (toDesk) target = target with { Id = deskId };
        else if (target.Id.Length == 0) return (null, "target.id is required for another node");
        lock (_lock)
        {
            Expire(nowUtc);
            if (_byId.Values.Count(p => p.State == SharedProposalState.Pending) >= MaxOpen) return (null, "too many proposals waiting");
            var id = "p" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var p = new SharedProposal(id, from, target, line, Str(root["reason"], MaxReason), Risk(risk(target, line)), toDesk,
                SharedProposalState.Pending, nowUtc);
            _byId[id] = p;
            Raise(p);
            return (p, null);
        }
    }

    public SharedProposal? Get(string id) { lock (_lock) return _byId.TryGetValue(id, out var p) ? p : null; }

    public IReadOnlyList<SharedProposal> From(string nodeId) { lock (_lock) return _byId.Values.Where(p => p.From.Id == nodeId).OrderBy(p => p.CreatedUtc).ToList(); }

    public IReadOnlyList<SharedProposal> All() { lock (_lock) return _byId.Values.OrderBy(p => p.CreatedUtc).ToList(); }

    public SharedProposal? Approve(string id, string by, DateTime nowUtc) => Decide(id, SharedProposalState.Approved, by, nowUtc);
    public SharedProposal? Reject(string id, string by, DateTime nowUtc) => Decide(id, SharedProposalState.Rejected, by, nowUtc);

    private SharedProposal? Decide(string id, SharedProposalState to, string by, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var p) || p.State != SharedProposalState.Pending) return null;
            p = p with { State = to, DecidedUtc = nowUtc, DecidedBy = by };
            _byId[id] = p;
            Raise(p);
            return p;
        }
    }

    /// <summary>
    /// A target collects what was approved for it (and marks it collected). Null + error when the key may not collect
    /// for that node.
    /// </summary>
    public (IReadOnlyList<SharedProposal>? Items, string? Error) Collect(string targetId, string? nodeKeyId, DateTime nowUtc)
    {
        if (nodeKeyId is not null && !BindKey(nodeKeyId, targetId)) return (null, "this key speaks for another node (or that node has its own key)");
        lock (_lock)
        {
            Expire(nowUtc);
            var due = _byId.Values.Where(p => p.Target.Id == targetId && !p.TargetIsDesk && p.State == SharedProposalState.Approved && p.ClaimedUtc is null)
                .OrderBy(p => p.DecidedUtc).ToList();
            foreach (var p in due) _byId[p.Id] = p with { ClaimedUtc = nowUtc };
            return (due, null);
        }
    }

    /// <summary>The target ran it (or refused): done / failed with its words. Null = taken, else why not.</summary>
    public string? Result(string id, bool ok, string words, string? nodeKeyId, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var p)) return "no such proposal";
            if (nodeKeyId is not null && NodeOfKey(nodeKeyId) is { } bound && bound != p.Target.Id) return "this key speaks for another node";
            if (p.State != SharedProposalState.Approved && !(p.State == SharedProposalState.Undelivered && p.ClaimedUtc is not null))
                return $"proposal {id} is {p.StateName}";
            p = p with { State = ok ? SharedProposalState.Done : SharedProposalState.Failed, Result = Str(JsonValue.Create(words), 300) };
            _byId[id] = p;
            Raise(p);
            return null;
        }
    }

    /// <summary>Approved ones aimed at the desk itself: the desk runs them and records the result here.</summary>
    public void Finished(string id, bool ok, string words)
    {
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var p) || p.State != SharedProposalState.Approved) return;
            p = p with { State = ok ? SharedProposalState.Done : SharedProposalState.Failed, Result = words.Length > 300 ? words[..300] : words };
            _byId[id] = p;
            Raise(p);
        }
    }

    /// <summary>Approved proposals nobody collected (or answered) in time become undelivered. Returns them.</summary>
    public IReadOnlyList<SharedProposal> Expire(DateTime nowUtc)
    {
        var changed = new List<SharedProposal>();
        lock (_lock)
        {
            foreach (var p in _byId.Values.ToList())
            {
                if (p.State != SharedProposalState.Approved || p.TargetIsDesk) continue;
                var late = p.ClaimedUtc is null ? nowUtc - p.DecidedUtc > CollectWithin : nowUtc - p.ClaimedUtc > ResultWithin;
                if (!late) continue;
                var u = p with { State = SharedProposalState.Undelivered, Result = p.ClaimedUtc is null ? $"{p.Target.Name} didn't collect it" : $"{p.Target.Name} didn't report back" };
                _byId[p.Id] = u;
                changed.Add(u);
            }
            // Keep the queue bounded: the oldest settled ones go first.
            foreach (var old in _byId.Values.Where(p => p.State is not (SharedProposalState.Pending or SharedProposalState.Approved))
                         .OrderBy(p => p.CreatedUtc).Take(Math.Max(0, _byId.Count - 500)).ToList())
                _byId.Remove(old.Id);
        }
        foreach (var c in changed) Raise(c);
        return changed;
    }

    private void Raise(SharedProposal p) => Changed?.Invoke(p);

    private static string Risk(string r) => r is "low" or "review" or "high" ? r : "high";

    private static NodeRef? Ref(JsonNode? n)
    {
        if (n is not JsonObject o) return null;
        var kind = Str(o["t"], 16);
        if (kind.Length == 0 || !kind.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        var id = Str(o["id"], 40);
        if (id.Length > 0 && !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        return new NodeRef(kind, id, Str(o["name"], 60));
    }

    private static string Str(JsonNode? n, int max)
    {
        if (n is null || n.GetValueKind() != JsonValueKind.String) return "";
        var s = new string(n.GetValue<string>().Where(c => !char.IsControl(c)).ToArray()).Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>
    /// How risky a line is for a node, from the verbs its context lists ("signal go all" → high). Matches the verb's words
    /// up to its first placeholder; anything not listed is high.
    /// </summary>
    public static string RiskFromCommands(IReadOnlyList<(string Verb, string Risk)> commands, string line)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var best = (Len: 0, Risk: "high");
        foreach (var (verb, risk) in commands)
        {
            var fixedWords = verb.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeWhile(w => !w.StartsWith('<') && !w.Contains('/')).ToArray();
            if (fixedWords.Length == 0 || fixedWords.Length > words.Length) continue;
            if (!fixedWords.Select((w, i) => w.Equals(words[i], StringComparison.OrdinalIgnoreCase)).All(x => x)) continue;
            if (fixedWords.Length > best.Len) best = (fixedWords.Length, risk);
        }
        return best.Risk is "low" or "review" or "high" ? best.Risk : "high";
    }
}

/// <summary>
/// Pinned certificates of other nodes (Show Bus §10): trust is the fingerprint seen on the first authenticated success,
/// never a name or an authority. A different fingerprint later is refused until the operator accepts it. Thread-safe.
/// </summary>
public sealed class TlsPins
{
    private readonly Dictionary<string, string> _pins = new(StringComparer.Ordinal);

    public TlsPins(IReadOnlyDictionary<string, string>? saved = null)
    {
        if (saved is not null) foreach (var (k, v) in saved) if (IsFingerprint(v)) _pins[k] = v;
    }

    /// <summary>"sha256:" + lowercase hex of the certificate's DER bytes.</summary>
    public static string Fingerprint(byte[] der) => "sha256:" + Convert.ToHexString(SHA256.HashData(der)).ToLowerInvariant();

    public static bool IsFingerprint(string? s) => s is { Length: 71 } && s.StartsWith("sha256:", StringComparison.Ordinal) && s[7..].All(char.IsAsciiHexDigitLower);

    public enum Verdict { Trusted, FirstUse, Changed }

    /// <summary>Is this the certificate we know for the node? (A first one is accepted — and pinned by <see cref="Pin"/>.)</summary>
    public Verdict Check(string nodeId, string fingerprint)
    {
        lock (_pins)
            return !_pins.TryGetValue(nodeId, out var known) ? Verdict.FirstUse : known == fingerprint ? Verdict.Trusted : Verdict.Changed;
    }

    /// <summary>Pin after the first authenticated success, or when the operator accepts a changed certificate.</summary>
    public void Pin(string nodeId, string fingerprint)
    {
        if (!IsFingerprint(fingerprint)) return;
        lock (_pins) _pins[nodeId] = fingerprint;
    }

    public IReadOnlyDictionary<string, string> Snapshot() { lock (_pins) return new Dictionary<string, string>(_pins); }
}
