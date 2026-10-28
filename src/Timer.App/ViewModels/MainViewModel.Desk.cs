using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

// The shared approval queue (Show Bus §11): this Stagewatch is the desk. Other apps' AI proposals land in the same
// Copilot list as Stagewatch's own; the operator approves or discards each once; the target app collects what was
// approved for it, runs it through its own command path and reports back. Network calls (any thread) go to the
// thread-safe ProposalDesk; the list and the journal follow on the show thread. The desk refuses a line rather than cut it,
// names no window's address in one (its windows' tokens are read as each line comes), and approves a line for another app
// only while that app collects.
public sealed partial class MainViewModel
{
    private ProposalDesk? _deskStore;
    private ProposalDesk Desk => LazyInitializer.EnsureInitialized(ref _deskStore, () =>
    {
        // Network threads may race to the first proposal: exactly one desk is published, and only it is ever used.
        var d = new ProposalDesk(() => _store.Snapshot.NodeId, _nodes) { WindowTokens = () => ControlledWindows.Select(w => w.Token) };
        d.Changed += p => _ui.BeginInvoke(() => OnSharedProposal(p));
        return d;
    });

    private static string Json(object o) => JsonSerializer.Serialize(o);

    /// <summary>
    /// IShowApprovals: an app proposes (network thread) with what its request proved (the server's credential: a node
    /// key, the operator, or nothing). 403 when the credential may not speak for its <c>from</c>.
    /// </summary>
    public (int Status, string Json) SubmitProposal(string body, NodeCredential credential)
    {
        var (p, error, forbidden) = Desk.Submit(body, credential, RateFor, DateTime.UtcNow, NeverProposed);
        // §11's answer first; this desk appends the form that rated the line and that row's words (none for its own lines).
        return p is null ? (forbidden ? 403 : 400, Json(new { ok = false, error }))
            : (201, Json(new { ok = true, id = p.Id, state = p.StateName, risk = p.Risk, form = p.Form, formWords = p.FormWords }));
    }

    /// <summary>One proposal by its id (or p&lt;n&gt; of this run); an id of an earlier run is 404 in words that say the desk restarted.</summary>
    public (int Status, string Json) GetProposal(string id)
        => Desk.Get(id) is { } p ? (200, Json(p.ToJson())) : (404, Json(new { ok = false, error = Desk.Unknown(id) }));

    public (int Status, string Json) ProposalsFrom(string nodeId)
        => (200, Json(new { proposals = (nodeId.Length > 0 ? Desk.From(nodeId) : Desk.All()).Select(p => p.ToJson()) }));

    public (int Status, string Json) CollectApproved(string targetId, NodeCredential credential)
    {
        var (items, error) = Desk.Collect(targetId, credential, DateTime.UtcNow);
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
        return Desk.Result(id, ok, words, credential, DateTime.UtcNow) is { } error ? (409, Json(new { ok = false, error })) : (200, Json(new { ok = true }));
    }

    /// <summary>
    /// IShowContext: another app's context (network thread), taken only when the request's credential may speak for its
    /// node, and held with that proof (none is kept 30 s, shown, and never sets a risk).
    /// </summary>
    public (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
        => _peerContexts.Accept(json, address, DateTime.UtcNow, _store.Snapshot.NodeId, credential);

    /// <summary>
    /// How a line rates for its target: by the desk's own rules for itself (a line it parses, with no form of a table), by the most
    /// specific whole form of the target's verified table otherwise, of the target's kind — the verdict that says whether the line fills
    /// a secret placeholder, which the desk refuses. Unknown is high, and so is a target whose context proves nothing now. A playout
    /// link's peer's context, pulled over this desk's own link to the address the operator set, comes first while it proves something —
    /// while no key speaks for that id — as on the Show network's row (<see cref="PeerContexts.Chosen"/>).
    /// </summary>
    private RiskVerdict RateFor(NodeRef target, string line)
    {
        if (target.Id == _store.Snapshot.NodeId)
        {
            var tokens = McpTokens(line);
            if (tokens.Count == 0 || !ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out _)) return RiskVerdict.Unlisted;
            var risk = Copilot.Classify(cmd) switch { CopilotRisk.Low => "low", CopilotRisk.Review => "review", _ => "high" };
            return new RiskVerdict(risk, null, "", true, false);
        }
        return _peerContexts.VerdictFor(target, line, DateTime.UtcNow, LinkedContext(target));
    }

    private static List<string> McpTokens(string line) => Stagewatch.Remote.McpEndpoint.Tokenize(line);

    /// <summary>Why a line is never a proposal for this desk (a desk-only line: trusting a certificate), or null; another node's lines are its own.</summary>
    private string? NeverProposed(NodeRef target, string line)
        => target.Id == _store.Snapshot.NodeId ? PlayoutGate.NeverProposed(McpTokens(line)) : null;

    /// <summary>
    /// Show thread: mirror a shared proposal into the Copilot list (new, decided, done…). A proposal joins the list when first seen
    /// pending, and a person's line from this desk (§11, born approved by a role) when first seen approved — as sent, waiting for its
    /// app — so its done, failed or undelivered reaches the list, the status line and the journal as any approved line's does.
    /// </summary>
    private void OnSharedProposal(SharedProposal p)
    {
        var item = Proposals.FirstOrDefault(x => x.SharedId == p.Id);
        if (item is null)
        {
            var sent = p.State == SharedProposalState.Approved && !p.TargetIsDesk && p.From.Id == _store.Snapshot.NodeId;
            if (p.State != SharedProposalState.Pending && !sent) return;
            var target = p.TargetIsDesk ? "this Stagewatch" : (p.Target.Name.Length > 0 ? p.Target.Name : p.Target.Kind);
            item = new CopilotProposal
            {
                Source = sent ? $"{p.DecidedBy} → {target}" : $"{(p.From.Name.Length > 0 ? p.From.Name : p.From.Kind)} → {target}",
                Request = p.Reason.Length > 0 ? p.Reason : p.Line,
                SharedId = p.Id,
            };
            var risk = p.Risk switch { "low" => CopilotRisk.Low, "review" => CopilotRisk.Review, _ => CopilotRisk.High };
            var first = new CopilotStepItem(new CopilotStep(p.Line, p.Reason, risk));
            item.Steps.Add(first);
            var row = _show.RundownActive ? _show.CurrentItem : -1;
            if (sent)
            {
                first.Result = "approved — waiting for " + target;
                item.Reply = $"{target} runs this itself and reports back here.";
                item.State = ProposalState.Done;
                Proposals.Insert(0, item);
                _journal.Add(DateTime.UtcNow, JournalKind.Note, row, $"Sent ({item.Source}): {p.Line}");
                StatusLine = $"{item.Source}: \"{p.Line}\" sent — {target} runs it and reports back";
                UpdatePendingCount();
                return;
            }
            item.Reply = p.TargetIsDesk ? "" : $"{target} runs this itself once you approve, and reports back here.";
            item.State = ProposalState.Pending;
            item.RaisePending();
            Proposals.Insert(0, item);
            _journal.Add(DateTime.UtcNow, JournalKind.Note, row, $"{item.Source} proposed: {p.Line}");
            StatusLine = $"{item.Source} proposes \"{p.Line}\" — see Copilot";
            UpdatePendingCount();
            return;
        }
        var step = item.Steps.FirstOrDefault();
        switch (p.State)
        {
            case SharedProposalState.Done or SharedProposalState.Failed or SharedProposalState.Undelivered:
                if (step is not null) step.Result = p.State switch
                {
                    SharedProposalState.Done => "done" + (p.Result.Length > 0 ? ": " + p.Result : ""),
                    // An unconfirmed line failed only for want of a "done": it may have run, and its words say so.
                    SharedProposalState.Failed when p.Result.StartsWith(PlayoutReceipts.Unconfirmed, StringComparison.Ordinal) => p.Result,
                    SharedProposalState.Failed => "refused: " + p.Result,
                    _ => "not delivered: " + p.Result,
                };
                item.State = ProposalState.Done;
                _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, $"{item.Source}: {p.Line} — {p.StateName}{(p.Result.Length > 0 ? " (" + p.Result + ")" : "")}");
                if (!p.TargetIsDesk) StatusLine = $"{p.Target.Name}: \"{p.Line}\" {p.StateName}" + (p.Result.Length > 0 ? $" — {p.Result}" : "");
                break;
            case SharedProposalState.Rejected:
                item.State = ProposalState.Discarded;
                break;
        }
        UpdatePendingCount();
    }

    /// <summary>
    /// The operator approved a shared proposal (show thread). One the desk refuses — its target app has not asked for approved lines
    /// lately, so it would not collect it — stays pending: the Copilot and the status line say why, and the journal keeps the refusal.
    /// </summary>
    private void ApproveShared(CopilotProposal p)
    {
        if (p.SharedId is not { } id) return;
        var (sp, refused) = Desk.Approve(id, "operator", DateTime.UtcNow);
        if (sp is null)
        {
            CopilotStatus = "Not approved: " + refused;
            StatusLine = $"{p.Source}: not approved — {refused}";
            _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1,
                $"Not approved ({p.Source}): {Desk.Get(id)?.Line ?? id} — {refused}");
            return;
        }
        var step = p.Steps.FirstOrDefault();
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, $"Approved ({p.Source}): {sp.Line}");
        if (!sp.TargetIsDesk)
        {
            if (step is not null) step.Result = "approved — waiting for " + sp.Target.Name;
            p.State = ProposalState.Done;
            CopilotStatus = $"Approved. {sp.Target.Name} runs it and reports back.";
            UpdatePendingCount();
            return;
        }
        // Aimed at this Stagewatch: run it here, with a receipt, and record what happened.
        var tokens = McpTokens(sp.Line);
        ShowCommand cmd = default;
        string? error = "an empty line";
        if (tokens.Count == 0 || !ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out cmd, out error))
        {
            Desk.Finished(id, false, "not a valid Stagewatch command: " + error);
            return;
        }
        var sentAt = TimeProvider.System.GetTimestamp();
        var (receipt, peer) = RunWithReceipt(cmd, id);
        if (peer is null)
        {
            Desk.Finished(id, receipt.Ok, receipt.Ok ? receipt.Effect : receipt.Error ?? "refused");
            return;
        }
        _ = FinishedByPeerAsync(id, peer, sentAt);
    }

    /// <summary>
    /// An approved line that went to a playout link's peer is finished by that command's final receipt (Show Bus §3), within
    /// receipt.final.ms of the send, on the show thread: done only once the peer said it applied it; a refusal or a command never sent
    /// failed in its words; one the peer only took, or never answered, failed as unconfirmed — it may have run
    /// (<see cref="PlayoutReceipts.ProposalOutcome"/>). Never a "done" the peer did not say.
    /// </summary>
    private async Task FinishedByPeerAsync(string id, PlayoutStep peer, long sentAt)
    {
        var final = await PlayoutReceipts.Settled(id, peer, TimeProvider.System, sentAt).ConfigureAwait(false);
        var (done, words) = PlayoutReceipts.ProposalOutcome(final);
        _ = _ui.BeginInvoke(() => Desk.Finished(id, done, words));
    }

    private void DiscardShared(CopilotProposal p)
    {
        if (p.SharedId is { } id) Desk.Reject(id, "operator", DateTime.UtcNow);
    }

    /// <summary>Once a second: approved proposals a target never collected become undelivered.</summary>
    private void ExpireShared() => _deskStore?.Expire(DateTime.UtcNow);
}
