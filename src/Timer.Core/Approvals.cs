using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Stagewatch.Core;

/// <summary>A node as a proposal names it (Show Bus §11).</summary>
public sealed record NodeRef(string Kind, string Id, string Name);

public enum SharedProposalState { Pending, Approved, Rejected, Done, Failed, Undelivered }

/// <summary>
/// One proposal on the shared queue: who proposes, which node is to act, the line in that node's grammar. <see cref="Form"/> is the form
/// of the target's verified table that rated the line, and <see cref="FormWords"/> that row's words, when one did; null and empty
/// otherwise (an unlisted line, or one for this desk, which rates by its own rules).
/// </summary>
public sealed record SharedProposal(
    string Id, NodeRef From, NodeRef Target, string Line, string Reason, string Risk, bool TargetIsDesk,
    SharedProposalState State, DateTime CreatedUtc, DateTime? DecidedUtc = null, string DecidedBy = "",
    DateTime? ClaimedUtc = null, string Result = "", string? Form = null, string FormWords = "")
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
        // §11's fields end at words; this desk records collects, so it appends when the target collected this one (null before), so a
        // target that lost a collect's answer runs only the ones it collected, and then the form that rated the line.
        collected = ClaimedUtc is { } c ? new DateTimeOffset(c).ToUnixTimeMilliseconds() : (long?)null,
        form = Form,
    };
}

/// <summary>
/// The shared approval queue (Show Bus §11), held by the desk: every app's AI proposes here, one person approves, and each
/// target collects what was approved for it, runs it through its own command path and reports the result. The desk never
/// holds another app's credentials. Every request that names a node (a proposal's from, a collect, a result) is spoken
/// for through <see cref="NodeBindings"/> before anything is queued or claimed. A line is refused, never cut
/// (<see cref="LineFence"/>); ids carry the desk's run, so no id names a proposal of an earlier run; and the desk records each
/// target's last collect, neither approving nor sending a line for a target that has not asked within <see cref="Fresh"/>.
/// Pure and thread-safe; the app's UI mirrors it and journals the decisions.
/// </summary>
public sealed class ProposalDesk
{
    public const int MaxOpen = 200, MaxReason = 400;

    /// <summary>The longest line (§14 <c>proposal.line.chars</c>): a longer one is refused, never cut, as every rule of <see cref="LineFence"/>.</summary>
    public const int MaxLine = LineFence.MaxChars;

    /// <summary>An approved proposal the target hasn't collected in this long is undelivered.</summary>
    public static readonly TimeSpan CollectWithin = TimeSpan.FromSeconds(60);
    /// <summary>A collected one with no result in this long is undelivered too (the target went away).</summary>
    public static readonly TimeSpan ResultWithin = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A target that has not asked for its approved lines in this long is not collecting (§14 <c>proposal.collect.ms</c>): the desk
    /// neither approves nor sends a line for it, and says why.
    /// </summary>
    public static readonly TimeSpan CollectFresh = TimeSpan.FromSeconds(15);

    /// <summary>A run's tag: this many symbols of <see cref="RunAlphabet"/>, base 32, drawn once as a desk starts.</summary>
    public const int RunChars = 4;

    /// <summary>The run's symbols: a key's and a window's base 32, with no 0, 1, l or o.</summary>
    public const string RunAlphabet = LineFence.KeyAlphabet;

    /// <summary>The targets whose last collect is kept; past it the longest silent is forgotten, and asks again within its next poll.</summary>
    public const int MaxCollectors = 256;

    /// <summary>The kinds a proposal's from may not name: this desk's own (§11). An AI on its MCP endpoint proposes through /mcp, as the desk.</summary>
    public static readonly IReadOnlyList<string> DeskKinds = ["main", "mcp"];

    /// <summary>The first words of a line aimed at this desk that a person sends from it, never a proposal.</summary>
    public static readonly IReadOnlyList<string> PersonVerbs = ["peer", "desk"];

    /// <summary>
    /// What a line born approved names as its decider (§11's four role words): a role, never a key's name or an address, which stay in
    /// the desk's journal.
    /// </summary>
    public static readonly IReadOnlyList<string> Roles = ["operator", "deck", "wire", "trigger"];

    private readonly object _lock = new();
    private readonly Dictionary<string, SharedProposal> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _lastCollect = new(StringComparer.Ordinal);
    private readonly Func<string> _deskId;
    private readonly NodeBindings _nodes;
    private int _next;

    /// <summary>
    /// A desk whose node routes speak through <paramref name="nodes"/>, the bindings every node route shares. Its run is
    /// <paramref name="run"/> when given (a test's), else drawn from the platform's random numbers.
    /// </summary>
    public ProposalDesk(Func<string> deskId, NodeBindings nodes, string? run = null)
    {
        if (run is not null && !ValidRun(run)) throw new ArgumentException($"a run is {RunChars} of {RunAlphabet}", nameof(run));
        _deskId = deskId;
        _nodes = nodes;
        Run = run ?? NewRun();
    }

    /// <summary>This desk's run: every id it makes ends with it (<c>p17-k3m9</c>), and <c>p17</c> alone names this run's.</summary>
    public string Run { get; }

    /// <summary>How long a target's last collect keeps it collecting: <see cref="CollectFresh"/>, unless a rig sets its own.</summary>
    public TimeSpan Fresh { get; init; } = CollectFresh;

    /// <summary>The tokens of this desk's windows now (any thread), so no line names one's address; none when null.</summary>
    public Func<IEnumerable<string>>? WindowTokens { get; init; }

    /// <summary>Whether <paramref name="run"/> is a run's shape.</summary>
    public static bool ValidRun(string? run) => run is { Length: RunChars } && run.All(c => RunAlphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>A new run, from the platform's random numbers.</summary>
    public static string NewRun()
        => string.Concat(Enumerable.Range(0, RunChars).Select(_ => RunAlphabet[RandomNumberGenerator.GetInt32(RunAlphabet.Length)]));

    /// <summary>Raised (on the caller's thread) whenever a proposal is added or changes.</summary>
    public event Action<SharedProposal>? Changed;

    /// <summary>
    /// A proposal rated by a word alone: <see cref="Submit(string, NodeCredential, Func{NodeRef, string, RiskVerdict}, DateTime, Func{NodeRef, string, string?}?)"/>
    /// with <paramref name="risk"/>'s word as the verdict, which names no form.
    /// </summary>
    public (SharedProposal? Proposal, string? Error, bool Forbidden) Submit(string json, NodeCredential credential, Func<NodeRef, string, string> risk, DateTime nowUtc,
        Func<NodeRef, string, string?>? never = null)
        => Submit(json, credential, (target, line) => new RiskVerdict(risk(target, line), null, "", false, false), nowUtc, never);

    /// <summary>
    /// A proposal from the network, sent with <paramref name="credential"/>, which must speak for the proposal's <c>from</c> (Show Bus
    /// §11). Refused with words, before anything is queued or any key is spoken for: a body that is no proposal; a <c>from</c> that is
    /// this desk's own — its id, or kind <c>main</c> or <c>mcp</c>; a line that breaks §11's line rules (<see cref="LineFence"/>), a
    /// person's verb aimed at this desk, or a line <paramref name="never"/> says is never a proposal for its target (one only a person on
    /// that desk runs, such as trusting a certificate); a full desk; and a line that fills a secret placeholder, by
    /// <paramref name="rate"/>'s verdict. <paramref name="rate"/> rates the line for its target before any key speaks and before the
    /// desk's lock is taken. A target proposing for itself under the key bound to its id may raise the risk it states (<c>risk</c>),
    /// never lower it. A target of kind <c>main</c> with no id, or with this desk's own id under any kind, is this desk. Returns the
    /// proposal, or why not; <c>Forbidden</c> when the credential may not speak for that node or the from is this desk's own (an HTTP 403).
    /// </summary>
    public (SharedProposal? Proposal, string? Error, bool Forbidden) Submit(string json, NodeCredential credential, Func<NodeRef, string, RiskVerdict> rate, DateTime nowUtc,
        Func<NodeRef, string, string?>? never = null)
    {
        if (Root(json) is not { } root) return (null, NotAProposal, false);
        var from = Ref(root["from"]);
        var target = Ref(root["target"]);
        var raw = root["line"] is { } l && l.GetValueKind() == JsonValueKind.String ? Text(l) : "";
        if (from is null || from.Id.Length == 0) return (null, NoFrom, false);
        if (raw is null) return (null, LineFence.Surrogate, false);
        if (target is null) return (null, "target.t is required", false);
        if (raw.Trim().Length == 0) return (null, "line is required", false);
        var deskId = _deskId();
        if (from.Id == deskId) return (null, "that's this Stagewatch — its own proposals are made at its desk", true);
        if (DeskKinds.Contains(from.Kind, StringComparer.OrdinalIgnoreCase)) return (null, DeskOwn(from.Kind), true);
        // §11: kind main with no id, or the desk's own id under any kind, names this desk, which names itself as it is.
        var toDesk = target.Id == deskId || (target.Kind == "main" && target.Id.Length == 0);
        if (toDesk) target = target with { Kind = "main", Id = deskId };
        else if (target.Id.Length == 0) return (null, "target.id is required for another node", false);
        if (LineFence.Check(raw, WindowTokens?.Invoke()) is { } fenced) return (null, fenced, false);
        var line = raw.Trim();
        if (toDesk && PersonVerb(line) is { } verb) return (null, $"{verb} is a person's at this desk — never a proposal", false);
        if (never?.Invoke(target, line) is { } notProposed) return (null, notProposed, false);
        // Spoken for only once the proposal is whole and there is room for it, so a malformed proposal, a line that fills a secret or a
        // full desk binds no key. The room is checked again when it is queued: two proposals racing for the last place can leave the
        // loser's key bound to the node it named, which is that key's own first proven use.
        lock (_lock)
        {
            Expire(nowUtc);
            if (PendingCount() >= MaxOpen) return (null, "too many proposals waiting", false);
        }
        // Rated before the desk's lock is taken: a rating reads the target's table on the caller's thread, and no approval, rejection
        // or collect waits on it.
        var verdict = rate(target, line);
        if (verdict.Secret) return (null, LineFence.SecretWords(NameOf(target)), false);
        var said = _nodes.Speak(credential, from.Kind, from.Id, nowUtc);
        if (!said.Ok) return (null, said.Error, true);
        var risk = Risk(verdict.Risk);
        if (from.Id == target.Id && said.Proof == NodeProof.Key && Stated(root["risk"]) is { } stated && VerbTable.Rank(stated) > VerbTable.Rank(risk)) risk = stated;
        lock (_lock)
        {
            Expire(nowUtc);
            if (PendingCount() >= MaxOpen) return (null, "too many proposals waiting", false);
            var p = new SharedProposal(NextId(), from, target, line, Str(root["reason"], MaxReason), risk, toDesk, SharedProposalState.Pending, nowUtc,
                Form: verdict.Listed ? verdict.Form : null, FormWords: verdict.Listed ? verdict.Words : "");
            _byId[p.Id] = p;
            Raise(p);
            return (p, null, false);
        }
    }

    /// <summary>
    /// A line a person sends to another app from this desk (Show Bus §11, a person's line): born approved, from this desk itself, decided
    /// by a role (<see cref="Roles"/>). Refused with words, nothing queued: a from that is not this desk; a target that is this desk; a
    /// decider that is no role; a line that breaks §11's line rules or fills a secret placeholder; a line no form of the target's verified
    /// table takes (<paramref name="verdict"/>, rated for that target); a target that has not asked for approved lines within
    /// <see cref="Fresh"/>; or a desk full of lines waiting for their apps. Raises one <see cref="Changed"/>, approved.
    /// </summary>
    public (SharedProposal? Proposal, string? Error) ProposeApproved(NodeRef from, NodeRef target, string line, string reason, RiskVerdict verdict, string by, DateTime nowUtc)
    {
        var deskId = _deskId();
        if (from.Kind != "main" || from.Id != deskId) return (null, "a person's line comes from this desk itself");
        if (target.Kind == "main" || target.Id.Length == 0 || target.Id == deskId) return (null, "this desk runs its own lines — a person's line goes to another app");
        if (!Roles.Contains(by, StringComparer.Ordinal)) return (null, "a person's line is decided by a role: " + string.Join(", ", Roles));
        if (line.Trim().Length == 0) return (null, "line is required");
        if (LineFence.Check(line, WindowTokens?.Invoke()) is { } fenced) return (null, fenced);
        line = line.Trim();
        if (verdict.Secret) return (null, LineFence.SecretWords(NameOf(target)));
        if (!verdict.Listed) return (null, $"{NameOf(target)} does not list '{Shown(line)}' — see its verbs");
        lock (_lock)
        {
            Expire(nowUtc);
            if (Quiet(target, nowUtc) is { } quiet) return (null, quiet);
            if (_byId.Values.Count(p => p.State == SharedProposalState.Approved) >= MaxOpen) return (null, "too many lines waiting for their apps");
            var p = new SharedProposal(NextId(), from, target, line, Clean(reason, MaxReason), Risk(verdict.Risk), false, SharedProposalState.Approved, nowUtc,
                nowUtc, by, Form: verdict.Form, FormWords: verdict.Words);
            _byId[p.Id] = p;
            Raise(p);
            return (p, null);
        }
    }

    /// <summary>Why <see cref="Submit(string, NodeCredential, Func{NodeRef, string, RiskVerdict}, DateTime, Func{NodeRef, string, string?}?)"/> refuses a body it can't read as a proposal, and one that names no node it speaks for.</summary>
    public const string NotAProposal = "a proposal is a JSON object that names each key once", NoFrom = "from.t and from.id are required";

    /// <summary>Why an MCP key's proposal is refused at the door (403): §11's words.</summary>
    public const string McpPostsNone = "an mcp key posts no proposal here — an AI on this desk proposes through /mcp, as the desk's own";

    /// <summary>Why a from of <paramref name="kind"/> is refused: it is this desk's own (§11).</summary>
    public static string DeskOwn(string kind) => $"a from of kind {kind} is this desk's own — another app proposes as itself, and an AI here proposes through /mcp";

    /// <summary>
    /// The node a proposal speaks for (its <c>from</c>), read exactly as Submit reads it, or Submit's own words for why it names none:
    /// what a server checks against the bindings before the desk is called, and refuses (400) for the desk when there is no node to check.
    /// </summary>
    public static (NodeRef? From, string? Error) FromOf(string json)
    {
        if (Root(json) is not { } root) return (null, NotAProposal);
        return Ref(root["from"]) is { Id.Length: > 0 } from ? (from, null) : (null, NoFrom);
    }

    /// <summary>A proposal's body as an object, or null when it isn't one (not JSON, not an object, or a key repeated).</summary>
    private static JsonObject? Root(string json)
    {
        try { return BusJson.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private int PendingCount() => _byId.Values.Count(p => p.State == SharedProposalState.Pending);   // under _lock

    private string NextId() => "p" + (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Run;   // under _lock

    /// <summary>The proposal an id names: its whole id, or <c>p&lt;n&gt;</c> of this run. Under _lock.</summary>
    private SharedProposal? Find(string id)
        => _byId.TryGetValue(id, out var p) ? p
            : IsShort(id) && _byId.TryGetValue(id + "-" + Run, out var mine) ? mine : null;

    private static bool IsShort(string id) => id.Length is > 1 and <= 11 && id[0] == 'p' && id.Skip(1).All(char.IsAsciiDigit);

    /// <summary>The whole id an id names (<c>p17</c> → <c>p17-k3m9</c>), or null when it names no proposal held.</summary>
    public string? Resolve(string id) { lock (_lock) return Find(id)?.Id; }

    /// <summary>
    /// Why an id names no proposal: "no such proposal", and for an id of another run, that this desk has restarted since it was made —
    /// a target never takes it for one of this run's.
    /// </summary>
    public string Unknown(string id)
    {
        var dash = id.LastIndexOf('-');
        return dash > 0 && IsShort(id[..dash]) && ValidRun(id[(dash + 1)..]) && id[(dash + 1)..] != Run
            ? $"no such proposal — this desk has restarted since {id} was made (it runs as {Run} now)"
            : "no such proposal";
    }

    public SharedProposal? Get(string id) { lock (_lock) return Find(id); }

    /// <summary>The oldest proposal still pending, the first made when two were made at one instant, or null: what a person's "next" names.</summary>
    public SharedProposal? NextPending()
    {
        lock (_lock)
            return _byId.Values.Where(p => p.State == SharedProposalState.Pending).OrderBy(p => p.CreatedUtc).ThenBy(p => Seq(p.Id)).FirstOrDefault();
    }

    /// <summary>The number of an id this desk made (<c>p17-k3m9</c> → 17), or 0 for one it did not.</summary>
    private static long Seq(string id)
        => id.Length > 1 && id[0] == 'p' && long.TryParse(id.AsSpan(1, (id.IndexOf('-') is var dash and > 0 ? dash : id.Length) - 1),
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;

    public IReadOnlyList<SharedProposal> From(string nodeId) { lock (_lock) return _byId.Values.Where(p => p.From.Id == nodeId).OrderBy(p => p.CreatedUtc).ToList(); }

    public IReadOnlyList<SharedProposal> All() { lock (_lock) return _byId.Values.OrderBy(p => p.CreatedUtc).ToList(); }

    /// <summary>
    /// A person approves a pending proposal, named by its id or by <c>p&lt;n&gt;</c> of this run. Refused with words, the proposal left
    /// as it is: no such proposal, one no longer pending, or one whose target — another node — has not asked for its approved lines
    /// within <see cref="Fresh"/>, since it would not collect it (§11).
    /// </summary>
    public (SharedProposal? Proposal, string? Error) Approve(string id, string by, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (Find(id) is not { } p) return (null, Unknown(id));
            if (p.State != SharedProposalState.Pending) return (null, $"{p.Id} is {p.StateName}");
            if (!p.TargetIsDesk && Quiet(p.Target, nowUtc) is { } quiet) return (null, quiet);
            p = p with { State = SharedProposalState.Approved, DecidedUtc = nowUtc, DecidedBy = by };
            _byId[p.Id] = p;
            Raise(p);
            return (p, null);
        }
    }

    /// <summary>A person rejects a pending proposal, named as for <see cref="Approve"/>; null when there is none pending by that id.</summary>
    public SharedProposal? Reject(string id, string by, DateTime nowUtc)
    {
        lock (_lock)
        {
            if (Find(id) is not { State: SharedProposalState.Pending } p) return null;
            p = p with { State = SharedProposalState.Rejected, DecidedUtc = nowUtc, DecidedBy = by };
            _byId[p.Id] = p;
            Raise(p);
            return p;
        }
    }

    /// <summary>When <paramref name="targetId"/> last asked for its approved lines, or null when it has not since this desk started.</summary>
    public DateTime? LastCollect(string targetId) { lock (_lock) return _lastCollect.TryGetValue(targetId, out var at) ? at : null; }

    /// <summary>
    /// Why a line for <paramref name="target"/> would not be collected now — it has not asked for its approved lines within
    /// <see cref="Fresh"/>, or not since this desk started — or null while it collects.
    /// </summary>
    public string? NotCollecting(NodeRef target, DateTime nowUtc) { lock (_lock) return Quiet(target, nowUtc); }

    private string? Quiet(NodeRef target, DateTime nowUtc)   // under _lock
        => !_lastCollect.TryGetValue(target.Id, out var at)
            ? $"{NameOf(target)} has not asked this desk for approved lines since this desk started — it would not collect this"
            : nowUtc - at > Fresh
                ? $"{NameOf(target)} has not asked this desk for approved lines in {NodeBindings.SecondsSince(at, nowUtc)} s — it would not collect this"
                : null;

    /// <summary>
    /// A target collects what was approved for it (and marks it collected), and its ask is recorded as its last collect. Null + error
    /// when the credential may not speak for that node (an HTTP 403); a refused collect records nothing.
    /// </summary>
    public (IReadOnlyList<SharedProposal>? Items, string? Error) Collect(string targetId, NodeCredential credential, DateTime nowUtc)
    {
        if (targetId == _deskId()) return (null, "that's this Stagewatch — it runs what was approved for it");
        // A collect speaks for the id alone: a proposal's target kind is the proposer's word, never the target's, so it
        // neither refuses the target's own key nor names the kind a first collect binds.
        var said = _nodes.Speak(credential, "", targetId, nowUtc);
        if (!said.Ok) return (null, said.Error);
        lock (_lock)
        {
            Expire(nowUtc);
            _lastCollect[targetId] = nowUtc;
            if (_lastCollect.Count > MaxCollectors) _lastCollect.Remove(_lastCollect.MinBy(kv => kv.Value).Key);
            var due = _byId.Values.Where(p => p.Target.Id == targetId && !p.TargetIsDesk && p.State == SharedProposalState.Approved && p.ClaimedUtc is null)
                .OrderBy(p => p.DecidedUtc).ToList();
            foreach (var p in due) _byId[p.Id] = p with { ClaimedUtc = nowUtc };
            return (due.Select(p => p with { ClaimedUtc = nowUtc }).ToList(), null);
        }
    }

    /// <summary>
    /// The target ran it (or refused): done / failed with its words, from a credential that speaks for the target. Null =
    /// taken, else why not.
    /// </summary>
    public string? Result(string id, bool ok, string words, NodeCredential credential, DateTime nowUtc)
    {
        static bool Open(SharedProposal p) => p.State == SharedProposalState.Approved || (p.State == SharedProposalState.Undelivered && p.ClaimedUtc is not null);
        NodeRef target;
        string whole;
        lock (_lock)
        {
            if (Find(id) is not { } asked) return Unknown(id);
            if (asked.TargetIsDesk) return "this Stagewatch runs its own proposals and records what they did";
            if (!Open(asked)) return $"proposal {asked.Id} is {asked.StateName}";
            target = asked.Target;
            whole = asked.Id;
        }
        // Spoken for only when the result could be taken, so a result refused for its state binds no key; for the id alone,
        // as a collect is (the target's kind in a proposal is the proposer's word).
        var said = _nodes.Speak(credential, "", target.Id, nowUtc);
        if (!said.Ok) return said.Error;
        lock (_lock)
        {
            if (!_byId.TryGetValue(whole, out var p)) return Unknown(id);
            if (!Open(p)) return $"proposal {p.Id} is {p.StateName}";
            p = p with { State = ok ? SharedProposalState.Done : SharedProposalState.Failed, Result = Str(JsonValue.Create(words), 300) };
            _byId[whole] = p;
            Raise(p);
            return null;
        }
    }

    /// <summary>Approved ones aimed at the desk itself: the desk runs them and records the result here.</summary>
    public void Finished(string id, bool ok, string words)
    {
        lock (_lock)
        {
            if (Find(id) is not { State: SharedProposalState.Approved } p) return;
            p = p with { State = ok ? SharedProposalState.Done : SharedProposalState.Failed, Result = words.Length > 300 ? words[..300] : words };
            _byId[p.Id] = p;
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

    /// <summary>The risk a proposal states (<c>risk</c>): low, review or high in any case, else null — a word that is none of them states nothing.</summary>
    private static string? Stated(JsonNode? n)
    {
        var r = Str(n, 16).ToLowerInvariant();
        return r is "low" or "review" or "high" ? r : null;
    }

    /// <summary>The line's first word when it is a person's verb at this desk (<see cref="PersonVerbs"/>), else null.</summary>
    private static string? PersonVerb(string line)
    {
        var first = VerbSyntax.Words(line) is [var w, ..] ? w.Text : line.Split(' ', 2)[0];
        return PersonVerbs.FirstOrDefault(v => string.Equals(v, first.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>How words name a node: its name, else its kind and id.</summary>
    private static string NameOf(NodeRef node) => node.Name.Length > 0 ? node.Name : NodeBindings.Describe(node.Kind, node.Id);

    /// <summary>A line as refusal words quote it: its first 60 characters.</summary>
    private static string Shown(string line) => line.Length > 60 ? line[..60] + "…" : line;

    /// <summary>
    /// A node as a proposal names it: its kind and id as sent, trimmed — a character in them that is not an ASCII letter, a digit,
    /// <c>-</c> or <c>_</c> names no node, never one left out to name another — and its name as free words (<see cref="Clean"/>).
    /// </summary>
    private static NodeRef? Ref(JsonNode? n)
    {
        if (n is not JsonObject o) return null;
        var kind = Word(o["t"], 16);
        if (kind.Length == 0 || !kind.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        var id = Word(o["id"], 40);
        if (id.Length > 0 && !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        return new NodeRef(kind, id, Str(o["name"], 60));
    }

    private static string Str(JsonNode? n, int max)
        => n is null || n.GetValueKind() != JsonValueKind.String ? "" : Clean(Text(n) ?? "", max);

    /// <summary>A string value as sent, trimmed and clipped to <paramref name="max"/>; nothing in it left out.</summary>
    private static string Word(JsonNode? n, int max)
        => n is null || n.GetValueKind() != JsonValueKind.String ? "" : ShowBusText.Clip((Text(n) ?? "").Trim(), max);

    /// <summary>
    /// A JSON string's text, or null when it is no text — an escape of half a surrogate pair, which the reader refuses to turn into a
    /// string: a line of it is refused in <see cref="LineFence.Surrogate"/>'s words, a kind or id names no node, free words are left out.
    /// </summary>
    private static string? Text(JsonNode n)
    {
        try { return n.GetValue<string>(); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Free words shown beside a line (a reason, a name): what the line's fence refuses left out, trimmed, kept to <paramref name="max"/>.</summary>
    private static string Clean(string text, int max) => LineFence.Shown(text, max);

    /// <summary>
    /// How risky a line is for a node, from the verbs its context lists ("signal go all" → high): the most specific whole form
    /// that takes the line (<see cref="VerbTable.Rate"/>); anything no form takes is high.
    /// </summary>
    public static string RiskFromCommands(IReadOnlyList<(string Verb, string Risk)> commands, string line) => VerbTable.Compile(commands).Rate(line).Risk;
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

    /// <summary>
    /// A fingerprint's first <paramref name="hex"/> hex in groups of four, as a person compares it ("03e9 b152"), its
    /// <c>sha256:</c> left off: one grouping for the journal, the desk's Settings → Remote and a presenter PC's row. A
    /// fingerprint is public. Fewer are shown only when it has fewer.
    /// </summary>
    public static string Shown(string fingerprint, int hex = 8)
    {
        var all = fingerprint.StartsWith("sha256:", StringComparison.Ordinal) ? fingerprint[7..] : fingerprint;
        var head = all[..Math.Clamp(hex, 0, all.Length)];
        return string.Join(' ', Enumerable.Range(0, (head.Length + 3) / 4).Select(i => head.Substring(i * 4, Math.Min(4, head.Length - i * 4))));
    }

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
