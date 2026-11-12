using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

// Show Bus receipts: "attempts are not facts". A command that comes with a request id runs on the show thread and the
// sender hears what happened — applied (with the status line it produced), refused (and why), or only accepted when
// the show thread didn't get to it in time. Whatever the command made (a message's signal id, a shared proposal's id) rides
// along. A command handed to a playout link's peer is completed in that peer's own words (PlayoutReceipts), within the
// same deadline; a line a person sent to another app, in what that app made of it (DeskReceipts). A receipted command that
// runs another inside itself (an approval of a line aimed at this desk) keeps its own notes: the inner one's refusal, refs
// and peer are the inner one's alone (ReceiptStack).
public sealed partial class MainViewModel
{
    // The receipts in flight on the show thread, innermost first, and whether a command from a wire is running — so only the
    // operator's own actions are offered to Companion's recorder.
    private readonly ReceiptStack<PlayoutStep> _receipts = new();

    /// <summary>
    /// 13.12.6's record of what each verified connector's ids came to on /ws: one for this process, as its inst is, handed to every web
    /// server Remote settings rebuild, so a repeat never runs twice and RECEIPTS never forgets an id this inst ran.
    /// </summary>
    private readonly ReceiptRecord _linkReceipts = new();

    /// <summary>
    /// IShowCommands: run a command and say what happened. Thread-safe; never waits on itself. A command the show thread has not run within
    /// the deadline is answered accepted, and its follow-up says what came of it once it ran (or that it had still not run) within
    /// receipt.final.ms of its read (ShowReceipts); one it ran more than receipt.late.ms after its read says how late, in its receipt and in
    /// a journal row.
    /// </summary>
    public async Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
    {
        var time = TimeProvider.System;
        var readAt = time.GetTimestamp();
        var ran = _ui.CheckAccess()
            ? Task.FromResult(RunWithReceipt(command, id, time, readAt))
            : _ui.InvokeAsync(() => RunWithReceipt(command, id, time, readAt)).Task;
        return await ShowReceipts.Of(id, ran, Complete, timeout, time, readAt).ConfigureAwait(false);

        // What the run made of it within what is left of the deadline: a peer's command its peer's answer, and its one follow-up when that
        // says accepted; a line sent to another app what that app made of it, and its one follow-up after.
        async Task<CommandReceipt> Complete((CommandReceipt Receipt, PlayoutStep? Peer, string? Awaited, long? LateMs) run, TimeSpan left)
        {
            var receipt = run.Peer is { } peer ? await PlayoutReceipts.Within(id, peer, left, time, readAt).ConfigureAwait(false)
                : run.Awaited is { } proposal ? await DeskReceipts.Within(id, Desk, proposal, left, time, readAt).ConfigureAwait(false)
                : run.Receipt;
            return ShowReceipts.Noted(receipt, run.LateMs);
        }
    }

    /// <summary>
    /// Show thread: run a command with notes of its own, and put back the notes (and the remote mark) it found when it ends — it may
    /// be running inside another receipted command, whose receipt is still to come. A command begun more than receipt.late.ms after its
    /// read says how late, and the journal keeps a row of it.
    /// </summary>
    private (CommandReceipt Receipt, PlayoutStep? Peer, string? Awaited, long? LateMs) RunWithReceipt(ShowCommand c, string id, TimeProvider time, long readAt)
    {
        var late = ShowReceipts.Late(time.GetElapsedTime(readAt));
        if (late is { } ms) _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, ShowReceipts.LateLine(c.Kind, id, ms));
        var before = StatusLine;
        var notes = _receipts.Enter();
        try { ExecuteNow(c); }
        catch (Exception ex) { return (CommandReceipt.Refused(id, ex.Message), null, null, late); }
        finally { _receipts.Leave(notes); }
        if (notes.Error is null && notes.Peer is { } peer) return (CommandReceipt.Accepted(id), peer, null, late);
        return (notes.Receipt(id, StatusLine != before ? StatusLine : ""), null, notes.Error is null ? notes.Awaited : null, late);
    }

    /// <summary>The action didn't happen: say why on the status line and in the sender's receipt.</summary>
    private void Refuse(string why)
    {
        StatusLine = why;
        ReceiptRefused(why);
    }

    /// <summary>The command was handed to a playout link's peer, which answers for itself: its receipt is that answer.</summary>
    private void ReceiptPeer(PlayoutStep step) => _receipts.Peer(step);

    /// <summary>The command being receipted was refused (a licence gate, a missing running order…).</summary>
    private void ReceiptRefused(string why) => _receipts.Refused(why);

    /// <summary>The command being receipted made something a sender can follow (a message's signal id).</summary>
    private void ReceiptRef(string name, long value) => _receipts.Ref(name, value);

    /// <summary>The command being receipted sent a line to another app: its receipt waits for what that app makes of it.</summary>
    private void ReceiptAwait(string proposalId) => _receipts.Await(proposalId);

    /// <summary>The command being receipted made something a sender follows by an id that is no number (a shared proposal's id).</summary>
    private void ReceiptRefId(string name, string id) => _receipts.RefId(name, id);
}
