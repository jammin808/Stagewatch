using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

// Show Bus receipts: "attempts are not facts". A command that comes with a request id runs on the show thread and the
// sender hears what happened — applied (with the status line it produced), refused (and why), or only accepted when
// the show thread didn't get to it in time. Whatever the command made (a message's signal id, a shared proposal's id) rides
// along. A command handed to a playout link's peer is completed in that peer's own words (PlayoutReceipts), within the
// same deadline. A receipted command that runs another inside itself (an approval of a line aimed at this desk) keeps its
// own notes: the inner one's refusal, refs and peer are the inner one's alone (ReceiptStack).
public sealed partial class MainViewModel
{
    // The receipts in flight on the show thread, innermost first, and whether a command from a wire is running — so only the
    // operator's own actions are offered to Companion's recorder.
    private readonly ReceiptStack<PlayoutStep> _receipts = new();

    /// <summary>IShowCommands: run a command and say what happened. Thread-safe; never waits on itself.</summary>
    public async Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
    {
        var time = TimeProvider.System;
        var sentAt = time.GetTimestamp();
        (CommandReceipt Receipt, PlayoutStep? Peer) ran;
        if (_ui.CheckAccess()) ran = RunWithReceipt(command, id);
        else
        {
            var op = _ui.InvokeAsync(() => RunWithReceipt(command, id));
            var done = await Task.WhenAny(op.Task, Task.Delay(timeout)).ConfigureAwait(false);
            if (done != op.Task) return CommandReceipt.Accepted(id);
            ran = await op.Task.ConfigureAwait(false);
        }
        // A peer's command: its answer within what is left of the deadline, and its one follow-up when that says accepted.
        if (ran.Peer is { } peer) return await PlayoutReceipts.Within(id, peer, timeout - time.GetElapsedTime(sentAt), time, sentAt).ConfigureAwait(false);
        return ran.Receipt;
    }

    /// <summary>
    /// Show thread: run a command with notes of its own, and put back the notes (and the remote mark) it found when it ends — it may
    /// be running inside another receipted command, whose receipt is still to come.
    /// </summary>
    private (CommandReceipt Receipt, PlayoutStep? Peer) RunWithReceipt(ShowCommand c, string id)
    {
        var before = StatusLine;
        var notes = _receipts.Enter();
        try { ExecuteNow(c); }
        catch (Exception ex) { return (CommandReceipt.Refused(id, ex.Message), null); }
        finally { _receipts.Leave(notes); }
        if (notes.Error is null && notes.Peer is { } peer) return (CommandReceipt.Accepted(id), peer);
        return (notes.Receipt(id, StatusLine != before ? StatusLine : ""), null);
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

    /// <summary>The command being receipted made something a sender follows by an id that is no number (a shared proposal's id).</summary>
    private void ReceiptRefId(string name, string id) => _receipts.RefId(name, id);
}
