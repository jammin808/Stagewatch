using System.Text.Json;
using System.Text.Json.Nodes;
using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

// The shared approval queue (Show Bus §11): this Stagewatch is the desk. Other apps' AI proposals land in the same
// Copilot list as Stagewatch's own; the operator approves or discards each once; the target app collects what was
// approved for it, runs it through its own command path and reports back. Network calls (any thread) go to the
// thread-safe ProposalDesk; the list and the journal follow on the show thread.
public sealed partial class MainViewModel
{
    private ProposalDesk? _deskStore;
    private ProposalDesk Desk
    {
        get
        {
            if (_deskStore is not null) return _deskStore;
            var d = new ProposalDesk(() => _store.Snapshot.NodeId);
            d.Changed += p => _ui.BeginInvoke(() => OnSharedProposal(p));
            return _deskStore = d;
        }
    }

    private static string Json(object o) => JsonSerializer.Serialize(o);

    /// <summary>IShowApprovals: an app proposes (network thread).</summary>
    public (int Status, string Json) SubmitProposal(string body, string? nodeKeyId)
    {
        var (p, error) = Desk.Submit(body, nodeKeyId, RiskFor, DateTime.UtcNow);
        return p is null ? (400, Json(new { ok = false, error })) : (201, Json(new { ok = true, id = p.Id, state = p.StateName, risk = p.Risk }));
    }

    public (int Status, string Json) GetProposal(string id)
        => Desk.Get(id) is { } p ? (200, Json(p.ToJson())) : (404, Json(new { ok = false, error = "no such proposal" }));

    public (int Status, string Json) ProposalsFrom(string nodeId)
        => (200, Json(new { proposals = (nodeId.Length > 0 ? Desk.From(nodeId) : Desk.All()).Select(p => p.ToJson()) }));

    public (int Status, string Json) CollectApproved(string targetId, string? nodeKeyId)
    {
        var (items, error) = Desk.Collect(targetId, nodeKeyId, DateTime.UtcNow);
        return items is null ? (403, Json(new { ok = false, error })) : (200, Json(new { proposals = items.Select(p => p.ToJson()) }));
    }

    public (int Status, string Json) ProposalResult(string id, string body, string? nodeKeyId)
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
        return Desk.Result(id, ok, words, nodeKeyId, DateTime.UtcNow) is { } error ? (409, Json(new { ok = false, error })) : (200, Json(new { ok = true }));
    }

    /// <summary>IShowContext with a node key: the key is bound to the node it speaks for (it can only collect that node's approvals).</summary>
    public string? AcceptPeerContext(string json, string address, string? nodeKeyId)
    {
        var refused = AcceptPeerContext(json, address);
        if (refused is null && nodeKeyId is not null && PeerContexts.Parse(json, address, DateTime.UtcNow) is { } e) Desk.BindKey(nodeKeyId, e.Id);
        return refused;
    }

    /// <summary>How risky a line is for its target: the desk's own rules for itself, the target's listed verbs otherwise; unknown is high.</summary>
    private string RiskFor(NodeRef target, string line)
    {
        if (target.Id == _store.Snapshot.NodeId)
        {
            var tokens = McpTokens(line);
            if (tokens.Count == 0 || !ShowCommandParser.TryParse(tokens[0], tokens.Skip(1).ToList(), out var cmd, out _)) return "high";
            return Copilot.Classify(cmd) switch { CopilotRisk.Low => "low", CopilotRisk.Review => "review", _ => "high" };
        }
        var entry = _peerContexts.Find(target.Id, DateTime.UtcNow);
        if (entry is null && _patterns?.Context is { } pc && PeerContexts.Parse(pc, "", DateTime.UtcNow) is { } pe && pe.Id == target.Id) entry = pe;
        return entry is null ? "high" : ProposalDesk.RiskFromCommands(entry.Commands, line);
    }

    private static List<string> McpTokens(string line) => Stagewatch.Remote.McpEndpoint.Tokenize(line);

    /// <summary>Show thread: mirror a shared proposal into the Copilot list (new, decided, done…).</summary>
    private void OnSharedProposal(SharedProposal p)
    {
        var item = Proposals.FirstOrDefault(x => x.SharedId == p.Id);
        if (item is null)
        {
            if (p.State != SharedProposalState.Pending) return;
            var target = p.TargetIsDesk ? "this Stagewatch" : (p.Target.Name.Length > 0 ? p.Target.Name : p.Target.Kind);
            item = new CopilotProposal
            {
                Source = $"{(p.From.Name.Length > 0 ? p.From.Name : p.From.Kind)} → {target}",
                Request = p.Reason.Length > 0 ? p.Reason : p.Line,
                SharedId = p.Id,
            };
            var risk = p.Risk switch { "low" => CopilotRisk.Low, "review" => CopilotRisk.Review, _ => CopilotRisk.High };
            item.Steps.Add(new CopilotStepItem(new CopilotStep(p.Line, p.Reason, risk)));
            item.Reply = p.TargetIsDesk ? "" : $"{target} runs this itself once you approve, and reports back here.";
            item.State = ProposalState.Pending;
            item.RaisePending();
            Proposals.Insert(0, item);
            _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, $"{item.Source} proposed: {p.Line}");
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

    /// <summary>The operator approved a shared proposal (show thread).</summary>
    private void ApproveShared(CopilotProposal p)
    {
        if (p.SharedId is not { } id || Desk.Approve(id, "operator", DateTime.UtcNow) is not { } sp) return;
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
        var receipt = RunWithReceipt(cmd, id);
        Desk.Finished(id, receipt.Ok, receipt.Ok ? receipt.Effect : receipt.Error ?? "refused");
    }

    private void DiscardShared(CopilotProposal p)
    {
        if (p.SharedId is { } id) Desk.Reject(id, "operator", DateTime.UtcNow);
    }

    /// <summary>Once a second: approved proposals a target never collected become undelivered.</summary>
    private void ExpireShared() => _deskStore?.Expire(DateTime.UtcNow);
}
