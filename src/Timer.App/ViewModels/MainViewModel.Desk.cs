using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

// The shared approval queue (Show Bus §11): this Stagewatch is the desk, and this file only mirrors it. The rules — how a line rates,
// who may send or decide, what is refused and in which words, the journal's rows, which of a verified peer's own lines are heard
// and journalled — are DeskService's (Timer.Show), pure, so any host of the desk keeps the same ones. Other apps' AI proposals land in the same Copilot list as Stagewatch's own; the operator approves or discards each
// once; the target app collects what was approved for it, runs it through its own command path and reports back. Network calls (any
// thread) go to the thread-safe service; the list, the status line and the journal follow on the show thread.
public sealed partial class MainViewModel
{
    private DeskService? _deskService;
    private DeskService Desk => LazyInitializer.EnsureInitialized(ref _deskService, () =>
    {
        // Network threads may race to the first proposal: exactly one desk is published, and only it is ever used.
        var d = new DeskService(new DeskHost
        {
            Id = () => _store.Snapshot.NodeId,
            Name = () => _store.Snapshot.Integrations.RoomName is { Length: > 0 } room ? room : Environment.MachineName,
            Nodes = _nodes,
            Contexts = _peerContexts,
            Network = () => _network,
            Linked = LinkedContext,
            WindowTokens = () => ScreenWindows.Tokens(ControlledWindows),
            KeyName = ApiKeyName,
            ShowTime = utc => ShowClock.ToShow(utc).ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            RunHere = RunApprovedHere,
        });
        d.Changed += p => _ui.BeginInvoke(() => OnSharedProposal(p));
        d.Journalled += row =>
        {
            // A row made on the show thread is journalled at once, so it stands before the rows of what it set off (an approval before
            // the approved line's own rows); a network thread's is posted. Who and from where go in the entry's via, never its text.
            if (_ui.CheckAccess()) JournalDesk(row);
            else _ui.BeginInvoke(() => JournalDesk(row));
        };
        // What verified peers' own journals told the desk, heard on the show thread once a second: the service's rows, as they are.
        d.Heard += line =>
        {
            if (_ui.CheckAccess()) JournalHeard(line);
            else _ui.BeginInvoke(() => JournalHeard(line));
        };
        return d;
    });

    private void JournalDesk(DeskRow row) => _journal.Add(row.Entry(_show.RundownActive ? _show.CurrentItem : -1));

    private void JournalHeard(HeardLine line) => _journal.Add(line.Entry(_show.RundownActive ? _show.CurrentItem : -1));

    /// <summary>
    /// Show thread: the desk's readers follow it, by the desk's own rule (<see cref="DeskService.Refresh"/>), asked when a proposal
    /// changes and once a second — an app starts or stops collecting, comes or goes with no proposal moving — never per frame, so
    /// building a frame asks the desk nothing: it carries <see cref="DeskService.Said"/>. The operator frame's desk block is
    /// published when it reads differently; the config's peers (a deck's and a page's list of apps, <see cref="DeskService.PeersSaid"/>)
    /// are sent again when an app comes, goes or is renamed, no oftener than the desk's hold.
    /// </summary>
    private void RefreshDeskReaders()
    {
        RefreshApproveBlocks();
        var (frame, config) = Desk.Refresh();
        if (frame) MarkDirty();
        if (config) BumpConfig();   // rebuilds the config from the desk's peers and publishes
        else Publish();
    }

    /// <summary>
    /// IShowApprovals: an app proposes (network thread) with what its request proved (the server's credential: a node
    /// key, the operator, or nothing). 403 when the credential may not speak for its <c>from</c>.
    /// </summary>
    public (int Status, string Json) SubmitProposal(string body, NodeCredential credential) => Desk.SubmitProposal(body, credential);

    /// <summary>One proposal by its id (or p&lt;n&gt; of this run); an id of an earlier run is 404 in words that say the desk restarted.</summary>
    public (int Status, string Json) GetProposal(string id) => Desk.GetProposal(id);

    public (int Status, string Json) ProposalsFrom(string nodeId) => Desk.ProposalsFrom(nodeId);

    public (int Status, string Json) CollectApproved(string targetId, NodeCredential credential) => Desk.CollectApproved(targetId, credential);

    public (int Status, string Json) ProposalResult(string id, string body, NodeCredential credential) => Desk.ProposalResult(id, body, credential);

    /// <summary>
    /// IShowContext: another app's context (network thread), taken only when the request's credential may speak for its
    /// node, and held with that proof (none is kept 30 s, shown, and never sets a risk).
    /// </summary>
    public (int Status, string? Error, NodeProof Proof) AcceptPeerContext(string json, string address, NodeCredential credential)
        => _peerContexts.Accept(json, address, DateTime.UtcNow, _store.Snapshot.NodeId, credential);

    /// <summary>
    /// Show thread: mirror a shared proposal into the Copilot list (new, decided, done…). A proposal joins the list when first seen
    /// pending, and a person's line from this desk (§11, born approved by a role) when first seen approved — as sent, waiting for its
    /// app — so its done, failed or undelivered reaches the list and the status line as any approved line's does. Whoever decided it —
    /// this window, a deck or the wire — the list shows the same. The journal's rows are the service's.
    /// </summary>
    private void OnSharedProposal(SharedProposal p)
    {
        RefreshDeskReaders();
        MirrorShared(p);
        // The item just listed, or just decided, says at once whether it can be approved — not a second later.
        RefreshApproveBlocks();
    }

    private void MirrorShared(SharedProposal p)
    {
        var item = Proposals.FirstOrDefault(x => x.SharedId == p.Id);
        // Both ends by the names this desk knows their ids by, as its journal rows and the frame say them — never the proposal's own.
        var target = Desk.Actor(p);
        if (item is null)
        {
            var sent = p.State == SharedProposalState.Approved && !p.TargetIsDesk && p.From.Id == _store.Snapshot.NodeId;
            if (p.State != SharedProposalState.Pending && !sent) return;
            item = new CopilotProposal
            {
                Source = sent ? $"{p.DecidedBy} → {target}" : $"{Desk.Proposer(p)} → {target}",
                Request = p.Reason.Length > 0 ? p.Reason : p.Line,
                SharedId = p.Id,
            };
            var first = new CopilotStepItem(new CopilotStep(p.Line, p.Reason, Copilot.Of(CommandRisk.Parse(p.Risk))));
            item.Steps.Add(first);
            if (sent)
            {
                first.Result = "approved — waiting for " + target;
                item.Reply = $"{target} runs this itself and reports back here.";
                item.State = ProposalState.Done;
                Proposals.Insert(0, item);
                StatusLine = $"{item.Source}: \"{p.Line}\" sent — {target} runs it and reports back";
                UpdatePendingCount();
                return;
            }
            item.Reply = p.TargetIsDesk ? "" : $"{target} runs this itself once you approve, and reports back here.";
            item.State = ProposalState.Pending;
            item.RaisePending();
            Proposals.Insert(0, item);
            StatusLine = $"{item.Source} proposes \"{p.Line}\" — see Copilot";
            UpdatePendingCount();
            return;
        }
        var step = item.Steps.FirstOrDefault();
        switch (p.State)
        {
            // Approved, by whoever decided it: it is decided, and waits — for its app, or, aimed at this desk and handed to a playout
            // link's peer, for that peer's answer. One this desk ran itself is done or failed by the time the list hears of it.
            case SharedProposalState.Approved when item.State == ProposalState.Pending:
                if (step is not null) step.Result = p.TargetIsDesk ? "approved — waiting for its answer" : "approved — waiting for " + target;
                item.State = ProposalState.Done;
                break;
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
                if (!p.TargetIsDesk) StatusLine = $"{target}: \"{p.Line}\" {p.StateName}" + (p.Result.Length > 0 ? $" — {p.Result}" : "");
                break;
            case SharedProposalState.Rejected:
                item.State = ProposalState.Discarded;
                break;
        }
        UpdatePendingCount();
    }

    /// <summary>
    /// The operator approved a shared proposal (show thread): the service decides, with the step's tick — an unticked one is not
    /// approved, and a high one, never ticked for the operator, is approved only once they tick it. One the desk refuses (its target
    /// app has not asked for approved lines lately, so it would not collect it) stays pending, and the Copilot and the status line
    /// say why.
    /// </summary>
    private void ApproveShared(CopilotProposal p)
    {
        if (p.SharedId is not { } id) return;
        var ticked = p.Steps.FirstOrDefault()?.Selected ?? false;
        var outcome = Desk.Decide(id, approve: true, DeskOrigin.Operator, high: ticked, ticked: ticked);
        // The list follows at once, and again (the same) when the service's own change arrives on this thread.
        if (outcome.Proposal is { } now) OnSharedProposal(now);
        if (!ticked)
        {
            CopilotStatus = DeskService.NothingTicked + ".";
            return;
        }
        if (!outcome.Ok && outcome.Proposal is not { State: SharedProposalState.Failed })
        {
            CopilotStatus = "Not approved: " + outcome.Words;
            StatusLine = $"{p.Source}: not approved — {outcome.Words}";
            return;
        }
        if (outcome.Proposal is { TargetIsDesk: true, State: SharedProposalState.Approved })
        {
            // Handed to a playout link's peer: approved, and not run until that peer says so — never "it ran here".
            if (p.Steps.FirstOrDefault() is { } step) step.Result = outcome.Words;
            CopilotStatus = char.ToUpperInvariant(outcome.Words[0]) + outcome.Words[1..] + ".";
            return;
        }
        CopilotStatus = outcome.Proposal is { TargetIsDesk: false } sp ? $"Approved. {Desk.Actor(sp)} runs it and reports back."
            : outcome.Ok ? "Approved — it ran here." : "Approved — it was refused here: " + outcome.Words;
    }

    /// <summary>
    /// DeskHost.RunHere (show thread): an approved line aimed at this Stagewatch runs here with a receipt of its own, under the
    /// proposal's id. One that went to a playout link's peer is finished by that command's final receipt (Show Bus §3), within
    /// receipt.final.ms of the send: done only once the peer said it applied it; a refusal or a command never sent failed in its
    /// words; one the peer only took, or never answered, failed as unconfirmed — it may have run
    /// (<see cref="PlayoutReceipts.ProposalOutcome"/>). Never a "done" the peer did not say.
    /// </summary>
    private DeskRun RunApprovedHere(ShowCommand cmd, string id)
    {
        var sentAt = TimeProvider.System.GetTimestamp();
        var (receipt, peer, _) = RunWithReceipt(cmd, id);
        if (peer is null) return new DeskRun(receipt.Ok, receipt.Ok ? receipt.Effect : receipt.Error ?? "refused");
        return new DeskRun(true, $"handed to {peer.Peer} — waiting for its answer", FinishedByPeerAsync(id, peer, sentAt));
    }

    private static async Task<(bool Done, string Words)> FinishedByPeerAsync(string id, PlayoutStep peer, long sentAt)
        => PlayoutReceipts.ProposalOutcome(await PlayoutReceipts.Settled(id, peer, TimeProvider.System, sentAt).ConfigureAwait(false));

    /// <summary>
    /// Show thread: a <c>peer</c> or <c>desk</c> line, as its door took it (<see cref="ShowCommand.By"/>): the service sends or
    /// decides, and this only says what came of it. The proposal sent or decided rides in the receipt's refs by its whole id, a
    /// refusal included; a line sent to another app is then waited for (<see cref="DeskReceipts"/>). The status line never quotes a
    /// line its app does not list; the sender's receipt says which.
    /// </summary>
    private void DeskCommand(ShowCommand c)
    {
        var outcome = Desk.Run(c);
        if (outcome.Proposal is { } p)
        {
            ReceiptRefId(DeskReceipts.Ref, p.Id);
            // The list follows at once, and again (the same) when the service's own change arrives on this thread.
            OnSharedProposal(p);
        }
        if (!outcome.Ok)
        {
            StatusLine = outcome.Shown;
            ReceiptRefused(outcome.Words);
            return;
        }
        // The service's words as they are: the receipt's effect is the status line, the same on every host of the desk.
        StatusLine = outcome.Words;
        if (c.Kind == ShowCommandKind.Peer && outcome.Proposal is { } sent) ReceiptAwait(sent.Id);
    }

    /// <summary>What the Show network row's … button opens, in words: the same entries as the row's right-click.</summary>
    public const string DoorOpens = "Send it a line, see its verbs, copy its headline — the same entries as right-click";

    /// <summary>
    /// The desk's door for a Show network row, read now (<see cref="DeskService.Door"/>): what its right-click menu and its … button
    /// offer, and why an entry is greyed. Show thread.
    /// </summary>
    public DeskDoor DoorFor(PeerRow row) => Desk.Door(row.NodeId);

    /// <summary>Show thread, once a second: the row follows its door — collecting, verified, the quick verbs, the last line sent it.</summary>
    private void ReadDoor(PeerRow row)
    {
        var door = DoorFor(row);
        row.Collects = door.Collects;
        row.Verified = door.Verified;
        row.SendBlocked = door.NoSend ?? "";
        row.LastSent = door.LastSent;
        if (!row.QuickVerbs.SequenceEqual(door.Quick)) row.QuickVerbs = door.Quick;
        row.DoorTip = door.NoSend is { } why ? $"{DoorOpens}. No line now: {why}" : DoorOpens;
    }

    /// <summary>The forms of an app's verified verbs, for "Its verbs…" (<see cref="DeskService.Verbs"/>).</summary>
    public IReadOnlyList<DeskVerb> PeerVerbs(string appId) => Desk.Verbs(appId);

    /// <summary>What sending a line to an app would say now, with nothing sent or journalled (<see cref="DeskService.Preview"/>): the send dialog's words as a line is typed.</summary>
    public LinePreview PreviewPeerLine(string appId, string line) => Desk.Preview(appId, line);

    /// <summary>
    /// The send dialog's press (show thread): the operator's line goes to the service as typed — never through the command box's
    /// path, so no deck's recorder takes it — born approved by the operator, and the Copilot list and the status line follow as for
    /// any sent line. The status line never quotes a line its app does not list; the dialog, the sender's own, does.
    /// </summary>
    public DeskOutcome SendPeerLine(string appId, string line)
    {
        var outcome = Desk.Send(appId, line, DeskOrigin.Operator);
        if (outcome.Proposal is { } p) OnSharedProposal(p);
        StatusLine = outcome.Ok ? outcome.Words : outcome.Shown;
        return outcome;
    }

    /// <summary>What a sent line came to, once nothing more will come (<see cref="DeskService.Settled"/>): the send dialog waits on it.</summary>
    public Task<DeskResult> PeerLineSettled(string id) => Desk.Settled(id);

    /// <summary>
    /// Show thread, when a proposal changes and once a second: each waiting shared proposal in the Copilot list says why approving
    /// it would be refused now (<see cref="DeskService.WhyNotApprove"/>: its app is not asking for approved lines), so Run ticked
    /// steps is greyed with the reason before it is pressed; "" once its app collects, and for Stagewatch's own plans.
    /// </summary>
    private void RefreshApproveBlocks()
    {
        foreach (var p in Proposals)
            p.ApproveBlocked = p is { SharedId: { } id, State: ProposalState.Pending } ? Desk.WhyNotApprove(id) ?? "" : "";
    }

    private void DiscardShared(CopilotProposal p)
    {
        if (p.SharedId is { } id) Desk.Decide(id, approve: false, DeskOrigin.Operator);
    }

    /// <summary>
    /// Once a second, show thread, after the show network of this second is joined and published: approved proposals a target never
    /// collected become undelivered, and the lines verified peers' own journals told since the last second are heard from that join.
    /// </summary>
    private void ExpireShared() => Desk.Expire();
}
