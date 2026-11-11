using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>What a send or a decision did: whether it happened, the words for the sender, and the proposal as it stands after (null when none was named or made).</summary>
public sealed record DeskOutcome(bool Ok, string Words, SharedProposal? Proposal = null)
{
    /// <summary>
    /// The words for a reader that is not the sender (a status line, a log): <see cref="Words"/>, but for a line its app does not
    /// list, which they never quote — such a line cannot be told from one that holds a secret.
    /// </summary>
    public string Shown { get; init; } = Words;
}

/// <summary>
/// What an approved line aimed at this desk did when it ran here: applied or refused in words, or — handed to a playout link's peer —
/// <see cref="Later"/>, that peer's final outcome, which finishes the proposal when it comes.
/// </summary>
public sealed record DeskRun(bool Ok, string Words, Task<(bool Done, string Words)>? Later = null);

/// <summary>
/// A row for the desk's journal, in the order the desk made them. <see cref="Words"/> open with the desk's own word (Proposed, Sent,
/// Approved…) and name a role alone — they reach every reader of the journal (the context's recent lines, the report, a model).
/// <see cref="Who"/> — a key's name, an address — is kept apart, for the journal file alone.
/// </summary>
public sealed record DeskRow(DateTime AtUtc, string Words, string Who = "")
{
    /// <summary>
    /// The row as every host journals it: the desk's own kind, which nothing else writes, so no note and no peer's name passes for a
    /// desk row; who and from where in the entry's <see cref="JournalEntry.Via"/>, never in its text.
    /// </summary>
    public JournalEntry Entry(int item = -1) => new(AtUtc, JournalKind.Desk, item, Words) { Via = Who.Length > 0 ? Who : null };
}

/// <summary>
/// Where a line stands for whoever sent or approved it: <c>pending</c> (it waits for a person), <c>sent</c> (approved, not collected
/// yet), <c>collected</c> (its app has it and has not answered), <c>done</c>, <c>refused</c> (its app ran it and said no, or did not
/// confirm), <c>undelivered</c> or <c>rejected</c>. <see cref="Ok"/> is null until it is settled; <see cref="Settled"/> when nothing
/// more will come.
/// </summary>
public sealed record DeskResult(string Id, string Stage, bool? Ok, string Words, bool Settled);

/// <summary>A verb a person fills by typing only its last words: the form's leading words, its value's name, its risk and the row's words.</summary>
public sealed record QuickVerb(string Head, string Value, string Risk, string Words);

/// <summary>What the desk service reads from its host: every delegate may be called on any thread.</summary>
public sealed class DeskHost
{
    /// <summary>This desk's Show Bus node id.</summary>
    public required Func<string> Id { get; init; }

    /// <summary>This desk's name, as a line born here names its from (the room).</summary>
    public required Func<string> Name { get; init; }

    /// <summary>The bindings every node route shares: which key speaks for which node.</summary>
    public required NodeBindings Nodes { get; init; }

    /// <summary>The contexts other apps posted, with what each proves.</summary>
    public required PeerContexts Contexts { get; init; }

    /// <summary>The show network as last joined: the apps heard by their hello, the contexts held and the proven links.</summary>
    public Func<NetworkView> Network { get; init; } = () => NetworkView.Empty;

    /// <summary>The context this Stagewatch read over its own link to a node, or null: it stands first while it proves something.</summary>
    public Func<NodeRef, PeerContexts.Entry?> Linked { get; init; } = _ => null;

    /// <summary>The tokens of this desk's windows now, so no line names one's address.</summary>
    public Func<IEnumerable<string>>? WindowTokens { get; init; }

    /// <summary>The clock (UTC).</summary>
    public Func<DateTime> Now { get; init; } = () => DateTime.UtcNow;

    /// <summary>How long a target's last collect keeps it collecting (§14 <c>proposal.collect.ms</c>, unless a rig sets its own).</summary>
    public TimeSpan Fresh { get; init; } = ProposalDesk.CollectFresh;

    /// <summary>The desk's run tag, for a test; drawn when null.</summary>
    public string? Run { get; init; }

    /// <summary>
    /// Runs an approved line aimed at this desk through the host's own command path, with a receipt, on the thread that called
    /// <see cref="DeskService.Decide"/> (the show thread); the proposal's whole id is the receipt's. Null on a host that runs none.
    /// </summary>
    public Func<ShowCommand, string, DeskRun>? RunHere { get; init; }
}

/// <summary>
/// The approval desk as one pure service (Show Bus §11): the shared queue, how a line rates for its target, which show app a word
/// names, a person's line to another app (born approved), a person's decision on a proposal, what a sent line came to, and the desk's
/// journal rows — the same rules for every host of the desk, which only mirrors it. A line is sent only to an app whose
/// verbs are verified and list it, and only while that app collects; a proposal is approved only by a person, a high one only with
/// its id and the word (never as <c>next</c>), and an unticked one not at all; an approved line aimed at this desk runs here through
/// the host and the decision returns what that run did. A refusal repeated is one journal row and a count. Thread-safe: the queue
/// locks itself, and every event is raised on the caller's thread.
/// </summary>
public sealed class DeskService : IShowApprovals
{
    /// <summary>The kinds that are Stagewatch itself (a desk, its Slides agent): never an app a line is sent to.</summary>
    public static readonly IReadOnlyList<string> OwnKinds = ["main", "agent"];

    /// <summary>The words that name this desk, never an app.</summary>
    public static readonly IReadOnlyList<string> OwnWords = ["stagewatch", "main", "desk"];

    /// <summary>The most quick verbs a row's menu offers.</summary>
    public const int MaxQuickVerbs = 6;

    /// <summary>Why a send from what is not a person is refused.</summary>
    public const string PersonSends = DeskOrigin.PersonSends;

    /// <summary>Why a decision from what is not a person is refused.</summary>
    public const string PersonDecides = DeskOrigin.PersonDecides;

    /// <summary>What approving with nothing ticked says: nothing was approved, and the proposal still waits.</summary>
    public const string NothingTicked = "Nothing was ticked — nothing ran";

    /// <summary>How long a refusal's row stands for its repeats: the same attempt refused for the same reason within it is counted, not written again.</summary>
    public static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(60);

    /// <summary>The most refusals held for their repeats at once; past it the oldest gives way, its count written.</summary>
    public const int MaxRepeats = 16;

    // The words a decision's row opens with.
    private const string Approved = "Approved", NotApproved = "Not approved", Rejected = "Rejected", NotRejected = "Not rejected";

    private readonly DeskHost _host;

    public DeskService(DeskHost host)
    {
        _host = host;
        Queue = new ProposalDesk(host.Id, host.Nodes, host.Run) { WindowTokens = host.WindowTokens, Fresh = host.Fresh, Names = Known };
        Queue.Changed += OnChanged;
    }

    /// <summary>The shared queue itself: the service is its only writer on a host.</summary>
    public ProposalDesk Queue { get; }

    /// <summary>A proposal was added or changed (the caller's thread).</summary>
    public event Action<SharedProposal>? Changed;

    /// <summary>A row for the desk's journal (the caller's thread): who proposed, who sent or decided and from where, and what came of it.</summary>
    public event Action<DeskRow>? Journalled;

    private DateTime Now => _host.Now();

    // ------------------------------------------------------------------ the node routes (IShowApprovals)

    /// <summary>
    /// An app proposes, with what its request proved. §11's answer first; this desk appends the form that rated the line and that
    /// row's words (none for its own lines). 403 when the credential may not speak for its <c>from</c>.
    /// </summary>
    public (int Status, string Json) SubmitProposal(string body, NodeCredential credential)
    {
        var (p, error, forbidden) = Queue.Submit(body, credential, Risk, Now, NeverProposed);
        return p is null ? (forbidden ? 403 : 400, Json(new { ok = false, error }))
            : (201, Json(new { ok = true, id = p.Id, state = p.StateName, risk = p.Risk, form = p.Form, formWords = p.FormWords }));
    }

    /// <summary>One proposal by its id (or p&lt;n&gt; of this run); an id of an earlier run is 404 in words that say the desk restarted.</summary>
    public (int Status, string Json) GetProposal(string id)
        => Queue.Get(id) is { } p ? (200, Json(p.ToJson())) : (404, Json(new { ok = false, error = Queue.Unknown(id) }));

    public (int Status, string Json) ProposalsFrom(string nodeId)
        => (200, Json(new { proposals = (nodeId.Length > 0 ? Queue.From(nodeId) : Queue.All()).Select(p => p.ToJson()) }));

    public (int Status, string Json) CollectApproved(string targetId, NodeCredential credential)
    {
        var (items, error) = Queue.Collect(targetId, credential, Now);
        return items is null ? (403, Json(new { ok = false, error })) : (200, Json(new { proposals = items.Select(p => p.ToJson()) }));
    }

    public (int Status, string Json) ProposalResult(string id, string body, NodeCredential credential)
    {
        bool ok;
        string words;
        try
        {
            var root = BusJson.Parse(body) as JsonObject ?? throw new JsonException();
            ok = root["ok"]?.GetValue<bool>() ?? false;
            words = root["words"]?.GetValueKind() == JsonValueKind.String ? root["words"]!.GetValue<string>() : "";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (400, Json(new { ok = false, error = "a result is {\"ok\":true|false,\"words\":\"…\"}" }));
        }
        return Queue.Result(id, ok, words, credential, Now) is { } error ? (409, Json(new { ok = false, error })) : (200, Json(new { ok = true }));
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    // ------------------------------------------------------------------ risk

    /// <summary>
    /// How a line rates for its target: by this desk's own rules for itself (<see cref="CommandRisk.OfLine"/>: a line its parser takes,
    /// with no form of a table), and otherwise by the most specific whole form of the target's verified table, of the target's kind —
    /// the verdict that also says whether the line fills a secret placeholder, which the desk refuses. Unknown is high, and so is a
    /// target whose context proves nothing now. A context read over this Stagewatch's own link stands first while it proves something
    /// (<see cref="PeerContexts.Chosen"/>), as on the Show network's row.
    /// </summary>
    public RiskVerdict Risk(NodeRef target, string line)
        => target.Id == _host.Id() ? CommandRisk.OfLine(line) : _host.Contexts.VerdictFor(target, line, Now, _host.Linked(target));

    /// <summary>Why a line is never a proposal for this desk (a desk-only line: trusting a certificate), or null; another node's lines are its own.</summary>
    private string? NeverProposed(NodeRef target, string line)
        => target.Id == _host.Id() ? PlayoutGate.NeverProposed(ShowCommandParser.Tokens(line)) : null;

    // ------------------------------------------------------------------ apps

    /// <summary>
    /// The show apps a line could go to, as the show network lists them now: every row that is not Stagewatch itself
    /// (<see cref="OwnKinds"/>, this desk's own id) and is still heard, each with whether it collects and whether its verbs are
    /// verified. Liveness and name are the network's; an app not heard for the network's "gone" age is left out.
    /// </summary>
    public IReadOnlyList<DeskApp> Apps()
    {
        var now = Now;
        var own = _host.Id();
        return _host.Network().Rows
            .Where(r => r.Peer.Id != own && r.Peer.Id.Length > 0 && !OwnKinds.Contains(r.Peer.Kind, StringComparer.Ordinal) && r.Peer.Liveness != "gone")
            .Select(r => App(new NodeRef(r.Peer.Kind, r.Peer.Id, r.Peer.Name), now))
            .ToList();
    }

    private DeskApp App(NodeRef node, DateTime now)
        => new(node.Id, NameOf(node), node.Kind, Queue.Collects(node.Id, now), Verified(node, now));

    /// <summary>Whether a table of the node's verbs, of its kind, proves something now: posted under the key bound to its id, or read over this Stagewatch's own link.</summary>
    private bool Verified(NodeRef node, DateTime now)
        => _host.Contexts.Chosen(node.Id, _host.Linked(node) is { } linked ? new HeldContext(linked, NodeProof.None, Pulled: true) : null, now, node.Kind)
            is { Proof: not NodeProof.None };

    /// <summary>
    /// The app a word names, or why it names none: a node id; else a name only one app has, whatever its case; else a kind word —
    /// the kind as Show Bus writes it (<c>playmap</c>, <c>patterns</c>, <c>messenger</c>) or the app's name for it (<c>agmessage</c>)
    /// — when exactly one app of that kind is heard. A word that is one app's name and another app's kind names neither. Never this
    /// desk, by any word. A word that holds a key or a window's address names nothing and is never quoted back.
    /// </summary>
    public (DeskApp? App, string? Error) Resolve(string word)
    {
        // Its refusals quote the word, to the sender and to whoever reads a status line or a log: one that holds a key is never quoted.
        if (Withheld(word ?? "") is { } withheld) return (null, withheld);
        word = LineFence.Shown(word ?? "", 80);
        if (word.Length == 0) return (null, "name the app: its node id, its name, or playmap, patterns or agmessage");
        if (word == _host.Id() || OwnWords.Contains(word, StringComparer.OrdinalIgnoreCase))
            return (null, $"'{word}' is this Stagewatch — a line goes to another show app");
        var apps = Apps();
        if (apps.FirstOrDefault(a => a.Id == word) is { } byId) return (byId, null);
        var named = apps.Where(a => string.Equals(a.Name, word, StringComparison.OrdinalIgnoreCase)).ToList();
        var ofKind = apps.Where(a => string.Equals(a.Kind, word, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(ShowBusKinds.Label(a.Kind), word, StringComparison.OrdinalIgnoreCase)).ToList();
        if (named.Count > 1) return (null, $"{Count(named.Count)} apps answer '{word}' — name one by its id: {string.Join(", ", named.Select(a => $"{a.Name} ({a.Id})"))}");
        if (named.Count == 1)
        {
            // A name that is also the kind word of another app is two answers, never a guess between them.
            var others = ofKind.Where(a => a.Id != named[0].Id).ToList();
            if (others.Count == 0) return (named[0], null);
            return (null, $"'{word}' is {named[0].Name}'s name and the kind of {string.Join(", ", others.Select(a => a.Name))} — name one by its id: "
                          + string.Join(", ", others.Prepend(named[0]).Select(a => $"{a.Name} ({a.Id})")));
        }
        if (ofKind.Count == 1) return (ofKind[0], null);
        if (ofKind.Count > 1) return (null, $"{Count(ofKind.Count)} apps answer '{word}' — name one: {string.Join(", ", ofKind.Select(a => a.Name))}");
        return (null, apps.Count == 0
            ? $"no app called '{word}' on the show network — none is heard"
            : $"no app called '{word}' on the show network — heard: {string.Join(", ", apps.Select(a => $"{a.Name} ({a.Kind})"))}");
    }

    private static string Count(int n) => n == 2 ? "two" : n.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Why the free words of a person's line — the app's word, a rejection's reason — are neither quoted nor journalled, or null:
    /// they hold a key or a window's address (<see cref="LineFence.Key"/>, <see cref="LineFence.Window"/>), as written or once the
    /// characters nobody sees are left out, which is how such words are shown. The line's own rule, kept for the words beside it.
    /// </summary>
    private string? Withheld(string words)
    {
        if (words.Length == 0) return null;
        var tokens = _host.WindowTokens?.Invoke()?.Where(t => t.Length > 0).ToList() ?? [];
        foreach (var read in new[] { words, LineFence.Shown(words, words.Length) })
        {
            if (LineFence.HoldsKey(read)) return LineFence.Key;
            if (LineFence.HoldsWindow(read) || tokens.Exists(t => read.Contains(t, StringComparison.Ordinal))) return LineFence.Window;
        }
        return null;
    }

    /// <summary>
    /// Up to <see cref="MaxQuickVerbs"/> verbs of an app's verified table that a person fills by typing only their last words
    /// (<see cref="VerbSyntax.Quick"/>), low and review alone, in the table's order; none while its table proves nothing.
    /// </summary>
    public IReadOnlyList<QuickVerb> QuickVerbs(string appId)
    {
        var now = Now;
        if (Apps().FirstOrDefault(a => a.Id == appId) is not { Verified: true } app) return [];
        var node = new NodeRef(app.Kind, app.Id, app.Name);
        var chosen = _host.Contexts.Chosen(app.Id, _host.Linked(node) is { } linked ? new HeldContext(linked, NodeProof.None, Pulled: true) : null, now, app.Kind);
        if (chosen is not { Proof: not NodeProof.None }) return [];
        var quick = new List<QuickVerb>();
        foreach (var row in chosen.Entry.Verbs.Commands)
        {
            if (VerbTable.Rank(row.Risk) > VerbTable.Rank("review")) continue;
            foreach (var form in VerbSyntax.Forms(row.Verb))
            {
                if (VerbSyntax.Quick(form) is not { } shape) continue;
                quick.Add(new QuickVerb(shape.Head, shape.Value, VerbTable.Normal(row.Risk), row.Words));
                if (quick.Count == MaxQuickVerbs) return quick;
            }
        }
        return quick;
    }

    // ------------------------------------------------------------------ a person's line

    /// <summary>
    /// A person sends one line to another show app (§11, a person's line): born approved, from this desk, decided by the sender's
    /// role. Refused with words, nothing queued: a sender that is not a person; a word that names no app; a line that breaks §11's
    /// line rules or fills a secret placeholder; an app whose verbs are not verified, or do not list the line; an app that has not
    /// asked for its approved lines lately. The journal file keeps who sent it and from where, apart from the row's words; a refusal's
    /// row never quotes the line (a line no form lists cannot be told from one that holds a secret), and a refusal repeated is one row.
    /// </summary>
    public DeskOutcome Send(string app, string line, DeskOrigin by, string reason = "")
    {
        if (!by.Sends) return new(false, PersonSends);
        var (found, unknown) = Resolve(app);
        if (found is null) return new(false, unknown!);
        var now = Now;
        var target = new NodeRef(found.Kind, found.Id, found.Name);
        string? refused = null;
        // What the journal says of a refusal, and what makes two refusals the same one: the sender's words, but for a line no form
        // lists (never quoted) and an app that does not collect (its words count the seconds).
        string? journalled = null;
        SharedProposal? sent = null;
        if (line.Trim().Length == 0) refused = "line is required";
        else if (LineFence.Check(line, _host.WindowTokens?.Invoke()) is { } fenced) refused = fenced;
        else
        {
            var verdict = Risk(target, line.Trim());
            // A secret is refused in the desk's own words first; an unverified table may refuse a line, never let one through.
            if (!verdict.Secret && !Verified(target, now)) refused = Unverified(target, now);
            else
            {
                (sent, refused) = Queue.ProposeApproved(new NodeRef("main", _host.Id(), _host.Name()), target, line, reason, verdict, by.Role, now);
                if (sent is null && !verdict.Secret && !verdict.Listed) journalled = $"{found.Name} does not list that line — see its verbs";
            }
        }
        if (sent is null)
        {
            var head = $"Refused to send to {found.Name} ({by.Role})";
            var same = journalled ?? (Queue.NotCollecting(target, now) is { } quiet && quiet == refused ? "not collecting" : refused);
            Refusal(now, $"send|{found.Id}|{by.Role}|{by.Named}|{same}", head, $"{head}: {journalled ?? refused}", by.Named);
            return new(false, refused!) { Shown = journalled ?? refused! };
        }
        Journal(now, $"Sent {Short(sent.Id)} to {found.Name} ({by.Role}): {sent.Line}", by.Named);
        return new(true, $"sent to {found.Name} as {Short(sent.Id)} — it collects it and reports back", sent);
    }

    /// <summary>Why an app's verbs set no line: it told this desk none, or what it posted proves nothing.</summary>
    private string Unverified(NodeRef target, DateTime now)
    {
        var held = _host.Contexts.Find(target.Id, now) is not null || _host.Linked(target) is not null;
        return held
            ? $"{NameOf(target)}'s verbs are not verified (posted without its key) — Stagewatch sends only lines it can read"
            : $"{NameOf(target)} has told this desk no verbs — Stagewatch sends only lines it can read";
    }

    // ------------------------------------------------------------------ a person's decision

    /// <summary>
    /// A person approves or rejects a proposal, named by its id (whole, or p&lt;n&gt; of this run) or <c>next</c>, the oldest waiting.
    /// Refused with words, the proposal left as it is: a decider that is not a person; no such proposal, or one no longer waiting;
    /// an approval with nothing ticked; an approval of a high proposal without its id and <paramref name="high"/> — <c>next</c> never
    /// approves one, whatever comes with it, and its words name the id to say; and an approval for an app that has not asked for its
    /// approved lines lately; and a rejection whose reason holds a key or a window's address, which no journal row keeps. An approved
    /// line aimed at this desk runs here at once through the host, and the outcome is that run's: its refusal is this decision's. A
    /// refusal repeated is one journal row and a count.
    /// </summary>
    public DeskOutcome Decide(string idOrNext, bool approve, DeskOrigin by, bool high = false, string why = "", bool ticked = true)
    {
        var now = Now;
        var next = string.Equals(idOrNext, "next", StringComparison.OrdinalIgnoreCase);
        var p = next ? Queue.NextPending() : Queue.Get(idOrNext);
        // Who may decide is settled before anything about the proposal is said: what is not a person learns only that.
        if (!by.Decides)
        {
            if (p is { State: SharedProposalState.Pending }) Refused(now, p, approve ? NotApproved : NotRejected, by, "a person decides", PersonDecides);
            return new(false, PersonDecides);
        }
        if (p is null) return new(false, next ? "nothing is waiting for a decision" : Queue.Unknown(idOrNext));
        if (p.State != SharedProposalState.Pending) return new(false, $"{Short(p.Id)} is {p.StateName}", p);
        if (!approve)
        {
            // The reason is journalled: one that holds a key or a window's address rejects nothing, and its row leaves the reason out.
            if (Withheld(why) is { } withheld)
            {
                Refused(now, p, NotRejected, by, withheld, withheld);
                return new(false, withheld, p);
            }
            if (Queue.Reject(p.Id, by.Role, now) is not { } rejected) return new(false, $"{Short(p.Id)} is no longer waiting", Queue.Get(p.Id));
            var because = LineFence.Shown(why, ProposalDesk.MaxReason);
            Journal(now, Decision(Rejected, p, by) + (because.Length > 0 ? " — " + because : ""), by.Named);
            return new(true, $"rejected {Short(p.Id)} ({Title(p)})", rejected);
        }
        if (!ticked) return new(false, NothingTicked, p);
        // A high proposal is chosen by name: its id and the word. As next — the oldest, whichever that is — it is never approved.
        if (p.Risk == "high" && (!high || next))
        {
            Refused(now, p, NotApproved, by, "high", "it is high, and takes its id and the word high");
            return new(false, $"{Short(p.Id)} is high — approve it by its id: desk approve {p.Id} high", p);
        }
        var (approved, refused) = Queue.Approve(p.Id, by.Role, now);
        if (approved is null)
        {
            Refused(now, p, NotApproved, by, !p.TargetIsDesk && Queue.NotCollecting(p.Target, now) == refused ? "not collecting" : refused!, refused!);
            return new(false, refused!, Queue.Get(p.Id));
        }
        Journal(now, Decision(Approved, p, by), by.Named);
        if (!approved.TargetIsDesk)
            return new(true, $"approved {Short(p.Id)} ({Title(p)}) — {Actor(p)} runs it and reports back", approved);
        return RunHere(approved);
    }

    /// <summary>An approved line aimed at this desk: parsed, run through the host, and finished with what the run did — or with why it could not be run.</summary>
    private DeskOutcome RunHere(SharedProposal approved)
    {
        var id = approved.Id;
        if (!ShowCommandParser.TryParseLine(approved.Line, out var cmd, out var error))
            return Finish(id, false, "not a valid Stagewatch command: " + error);
        if (_host.RunHere is not { } run) return Finish(id, false, "this desk runs no lines of its own here");
        DeskRun ran;
        // A host's run that throws still finishes the proposal: approved, it is nobody's to collect, and would wait for ever.
        try { ran = run(cmd, id); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Finish(id, false, "it could not be run here: " + ex.Message);
        }
        if (ran.Later is null) return Finish(id, ran.Ok, ran.Words);
        _ = FinishLaterAsync(id, ran.Later);
        return new(true, $"approved {Short(id)} — {ran.Words}", Queue.Get(id));
    }

    private DeskOutcome Finish(string id, bool ok, string words)
    {
        Queue.Finished(id, ok, words);
        return new(ok, ok ? $"approved {Short(id)} — ran here" + (words.Length > 0 ? ": " + words : "") : words, Queue.Get(id));
    }

    /// <summary>A line handed to a playout link's peer is finished by that peer's final outcome, never a "done" it did not say.</summary>
    private async Task FinishLaterAsync(string id, Task<(bool Done, string Words)> later)
    {
        bool done;
        string words;
        try { (done, words) = await later.ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            (done, words) = (false, "its outcome could not be read: " + ex.Message);
        }
        Queue.Finished(id, done, words);
    }

    // ------------------------------------------------------------------ the two verbs

    /// <summary>
    /// A <c>peer</c> or <c>desk</c> command as a door took it: the line sent (<see cref="Send"/>) or the proposal decided
    /// (<see cref="Decide"/>), by whoever the door named (<see cref="ShowCommand.By"/>). A command that names nobody is nobody's:
    /// refused as what is not a person is. The one executor every host of the desk runs these two kinds through.
    /// </summary>
    public DeskOutcome Run(ShowCommand c)
    {
        var by = c.By ?? DeskOrigin.Nobody;
        switch (c.Kind)
        {
            case ShowCommandKind.Peer:
                var peer = c.Get<PeerArgs>();
                return Send(peer.App, peer.Line, by);
            case ShowCommandKind.Desk:
                var desk = c.Get<DeskArgs>();
                return Decide(desk.Id, desk.Action == "approve", by, desk.High, desk.Why);
            default:
                return new(false, "not a line for the desk");
        }
    }

    // ------------------------------------------------------------------ what came of it

    /// <summary>
    /// What a proposal comes to, once nothing more will come (<see cref="DeskResult.Settled"/>): done, refused, undelivered or
    /// rejected. Completed already for one that is settled; an id that names none is settled as unknown. One waiter a proposal,
    /// held only while it is open, so no more than the queue holds.
    /// </summary>
    public Task<DeskResult> Settled(string id)
    {
        if (ResultOf(id) is not { } now) return Task.FromResult(new DeskResult(id, "unknown", false, Queue.Unknown(id), true));
        if (now.Settled) return Task.FromResult(now);
        TaskCompletionSource<DeskResult>? waiter;
        lock (_waiters)
        {
            if (!_waiters.TryGetValue(now.Id, out waiter))
                _waiters[now.Id] = waiter = new TaskCompletionSource<DeskResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        // A result that landed between the read above and the waiter's place in the table found no waiter: read once more. The
        // queue's lock is never taken while the table's is held (a change is raised under the queue's, and takes the table's).
        if (ResultOf(now.Id) is { Settled: true } landed) Complete(landed);
        return waiter.Task;
    }

    private readonly Dictionary<string, TaskCompletionSource<DeskResult>> _waiters = new(StringComparer.Ordinal);

    private void Complete(DeskResult result)
    {
        if (!result.Settled) return;
        TaskCompletionSource<DeskResult>? waiter;
        lock (_waiters)
        {
            if (!_waiters.Remove(result.Id, out waiter)) return;
        }
        waiter.TrySetResult(result);
    }

    /// <summary>Where a proposal stands, for whoever sent or approved it, in words that name its app; null for an id that names none.</summary>
    public DeskResult? ResultOf(string id)
    {
        if (Queue.Get(id) is not { } p) return null;
        var who = Actor(p);
        var tag = Short(p.Id);
        var line = Quoted(p.Line);
        var said = p.Result.Length > 0 ? ": " + p.Result : "";
        return p.State switch
        {
            SharedProposalState.Pending => new(p.Id, "pending", null, $"{tag} waits for a person's approval", false),
            SharedProposalState.Approved when p.ClaimedUtc is null && !p.TargetIsDesk
                => new(p.Id, "sent", null, $"sent to {who} as {tag} — it has not collected it yet", false),
            SharedProposalState.Approved => new(p.Id, "collected", null, $"{who} has {tag} — waiting for its answer", false),
            SharedProposalState.Done => new(p.Id, "done", true, $"{who} ran {line}{said} ({tag})", true),
            SharedProposalState.Failed => new(p.Id, "refused", false, $"{who} refused {line}{said} ({tag})", true),
            SharedProposalState.Undelivered => new(p.Id, "undelivered", false, $"{tag} was not delivered{said}", true),
            _ => new(p.Id, "rejected", false, $"{tag} was rejected", true),
        };
    }

    /// <summary>
    /// The desk now, for every reader that shows it: how many proposals wait, the oldest, and the apps a line could go to. The oldest
    /// names its two ends as this desk knows their ids (<see cref="Known"/>), each id beside its name, and says when its line is
    /// longer than the part it carries.
    /// </summary>
    public DeskSummary Summary()
    {
        var now = Now;
        var pending = Queue.All().Count(p => p.State == SharedProposalState.Pending);
        DeskNext? next = null;
        if (Queue.NextPending() is { } p)
            next = new DeskNext(p.Id, Proposer(p), Actor(p), ShowBusText.Clip(p.Line, DeskSummary.NextLineChars), p.Risk,
                p.TargetIsDesk || Queue.Collects(p.Target.Id, now), p.Line.Length > DeskSummary.NextLineChars, p.From.Id, p.Target.Id);
        return new DeskSummary(pending, next, Apps().ToEquatableArray());
    }

    // ------------------------------------------------------------------ what the readers are told

    /// <summary>
    /// The least time between two changes of the apps a config lists (<see cref="PeersSaid"/>). A hello is not authenticated: an app
    /// that renames itself every second would otherwise send every page and deck the config every second. Within it the list waits,
    /// and then says the apps as they are.
    /// </summary>
    public static readonly TimeSpan PeersHold = TimeSpan.FromSeconds(5);

    private volatile DeskSummary _said = DeskSummary.Empty;
    private volatile IReadOnlyList<(string Id, string Name, string Kind)> _peersSaid = [];
    private DateTime _peersSaidUtc = DateTime.MinValue;   // Refresh's thread

    /// <summary>The desk as its readers were last told (<see cref="Refresh"/>): what an operator's frame carries. Any thread.</summary>
    public DeskSummary Said => _said;

    /// <summary>The apps as a config lists them, as last told (<see cref="Refresh"/>): id, name and kind. Any thread.</summary>
    public IReadOnlyList<(string Id, string Name, string Kind)> PeersSaid => _peersSaid;

    /// <summary>
    /// The host's one thread, when a proposal changes and once a second: reads the desk and says which of its readers are to be told
    /// again. <c>Frame</c>: the summary reads differently from the one last said (<see cref="Said"/> is now the new one). <c>Config</c>:
    /// the apps differ from the list last said and that list is <see cref="PeersHold"/> old or more (<see cref="PeersSaid"/> is now
    /// the apps as they are); a change inside the hold is said at its end, by the call that comes then.
    /// </summary>
    public (bool Frame, bool Config) Refresh()
    {
        var now = Now;
        var summary = Summary();
        var frame = summary != _said;
        if (frame) _said = summary;
        var peers = summary.Peers();
        // A clock set back never holds the list longer than the hold: a time before the last change counts as past it.
        var config = !peers.SequenceEqual(_peersSaid) && (now < _peersSaidUtc || now - _peersSaidUtc >= PeersHold);
        if (config)
        {
            _peersSaid = peers;
            _peersSaidUtc = now;
        }
        return (frame, config);
    }

    /// <summary>
    /// The show network's rows as the context says them, each with whether its node collects from this desk now
    /// (<see cref="PeerSummary.Collects"/>): it asked for its approved lines within the freshness window. Any node's row is
    /// answered, an app's or another Stagewatch's (which never asks, and so never collects).
    /// </summary>
    public IReadOnlyList<PeerSummary> Collecting(IEnumerable<PeerSummary> peers)
    {
        var now = Now;
        return peers.Select(p => p with { Collects = p.Id.Length > 0 && Queue.Collects(p.Id, now) }).ToList();
    }

    /// <summary>
    /// Once a second: approved proposals a target never collected, or never answered, become undelivered, and a refusal whose repeats
    /// were counted for <see cref="RepeatWindow"/> gets its row of how many.
    /// </summary>
    public void Expire()
    {
        var now = Now;
        Queue.Expire(now);
        Write(now, null);
    }

    // ------------------------------------------------------------------ the journal

    private void OnChanged(SharedProposal p)
    {
        if (ResultOf(p.Id) is { } result) Complete(result);
        Changed?.Invoke(p);
        // Each row opens with the desk's own word and the proposal's number, never with a name a peer chose.
        switch (p.State)
        {
            case SharedProposalState.Pending:
                Journal(p.CreatedUtc, $"Proposed {Short(p.Id)}: {Title(p)}");
                break;
            case SharedProposalState.Done or SharedProposalState.Failed or SharedProposalState.Undelivered:
                Journal(Now, $"Outcome {Short(p.Id)}: {Actor(p)}: {p.Line} — {p.StateName}{(p.Result.Length > 0 ? $" ({p.Result})" : "")}");
                break;
        }
    }

    /// <summary>A decision's row: the word, the proposal's number, the role that decided, and what it was — <c>Approved p18 (deck): AI agent → PlayMap Hall: UNDO</c>.</summary>
    private string Decision(string word, SharedProposal p, DeskOrigin by) => $"{word} {Short(p.Id)} ({by.Role}): {Title(p)}";

    /// <summary>A decision refused: its row, said once for this proposal, decider and reason (<paramref name="same"/>) while it repeats.</summary>
    private void Refused(DateTime now, SharedProposal p, string word, DeskOrigin by, string same, string why)
        => Refusal(now, $"decide|{p.Id}|{word}|{by.Role}|{by.Named}|{same}", $"{word} {Short(p.Id)} ({by.Role})", $"{Decision(word, p, by)} — {why}", by.Named);

    private void Journal(DateTime atUtc, string words, string who = "") => Write(Now, new DeskRow(atUtc, words, who));

    /// <summary>
    /// A refusal's row, bounded: the first of its kind (<paramref name="key"/>: what was tried, by whom, and why) is written at once;
    /// the same again within <see cref="RepeatWindow"/> is counted, and the count is one more row (<paramref name="head"/> and how
    /// many) when the window ends — so a held button or a trigger firing at a silent app is two rows a minute, not one a press.
    /// </summary>
    private void Refusal(DateTime now, string key, string head, string words, string who)
    {
        List<DeskRow> rows = [];
        lock (_repeats)
        {
            Settle(now, rows);
            if (_repeats.Find(r => r.Key == key) is { } seen) seen.More++;
            else
            {
                if (_repeats.Count == MaxRepeats)
                {
                    if (_repeats[0].Counted(now) is { } counted) rows.Add(counted);
                    _repeats.RemoveAt(0);
                }
                _repeats.Add(new Repeat(key, now, head, who));
                rows.Add(new DeskRow(now, words, who));
            }
        }
        Raise(rows);
    }

    /// <summary>A row, after the count of every refusal whose window ended before it — the journal stays in time's order.</summary>
    private void Write(DateTime now, DeskRow? row)
    {
        List<DeskRow> rows = [];
        lock (_repeats) Settle(now, rows);
        if (row is not null) rows.Add(row);
        Raise(rows);
    }

    private void Settle(DateTime now, List<DeskRow> rows)   // under _repeats
    {
        for (var i = 0; i < _repeats.Count;)
        {
            var r = _repeats[i];
            // A clock set back ends a window too: nothing is counted against a time that has not come.
            if (now >= r.At && now - r.At < RepeatWindow) { i++; continue; }
            if (r.Counted(now < r.At ? now : r.At + RepeatWindow) is { } counted) rows.Add(counted);
            _repeats.RemoveAt(i);
        }
    }

    private void Raise(List<DeskRow> rows)
    {
        foreach (var row in rows) Journalled?.Invoke(row);
    }

    /// <summary>A refusal said once and how often it came again since: the oldest first.</summary>
    private readonly List<Repeat> _repeats = [];

    private sealed class Repeat(string key, DateTime at, string head, string who)
    {
        public string Key => key;
        public DateTime At => at;
        public int More;

        /// <summary>The row of its repeats, or null when it came once.</summary>
        public DeskRow? Counted(DateTime atUtc) => More == 0 ? null
            : new DeskRow(atUtc, string.Create(CultureInfo.InvariantCulture, $"{head}: refused the same way {More} more time{(More == 1 ? "" : "s")}"), who);
    }

    /// <summary>A proposal as a row names it: who proposes, which node is to act, and the line.</summary>
    private string Title(SharedProposal p) => $"{Proposer(p)} → {Actor(p)}: {p.Line}";

    /// <summary>
    /// How this desk names a node a proposal names: by the name the show network's row carries for its id — the one the Show network
    /// and <see cref="Apps"/> say — else by its kind and id. Never by the name the proposal gave it: those are the proposer's words,
    /// and a proposal for PlayMap's id that calls it something else is still PlayMap's to run.
    /// </summary>
    public string Known(NodeRef node)
        => _host.Network().Rows.FirstOrDefault(r => r.Peer.Id == node.Id && r.Peer.Name.Length > 0) is { } row ? row.Peer.Name : NodeBindings.Describe(node.Kind, node.Id);

    /// <summary>Who proposes, as this desk names it: itself by the name it gave a line born here (only this desk writes its own id), another node as <see cref="Known"/>.</summary>
    public string Proposer(SharedProposal p)
        => p.From.Id == _host.Id() ? (p.From.Name.Length > 0 ? p.From.Name : "this Stagewatch") : Known(p.From);

    /// <summary>Which node is to act, as this desk names it: this Stagewatch, or another node as <see cref="Known"/>.</summary>
    public string Actor(SharedProposal p) => p.TargetIsDesk ? "this Stagewatch" : Known(p.Target);

    /// <summary>How words name a node the show network listed: its name there, else its kind and id.</summary>
    private static string NameOf(NodeRef node) => node.Name.Length > 0 ? node.Name : NodeBindings.Describe(node.Kind, node.Id);

    /// <summary>An id as people say it: p17 of p17-k3m9.</summary>
    public static string Short(string id) => id.IndexOf('-') is var dash and > 0 ? id[..dash] : id;

    /// <summary>A line as words quote it: its first 60 characters.</summary>
    private static string Quoted(string line) => line.Length > 60 ? ShowBusText.Clip(line, 60) + "…" : line;
}
