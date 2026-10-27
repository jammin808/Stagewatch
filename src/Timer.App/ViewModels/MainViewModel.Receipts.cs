using Stagewatch.Core;
using Stagewatch.Integrations;

namespace Stagewatch.App.ViewModels;

// Show Bus receipts: "attempts are not facts". A command that comes with a request id runs on the show thread and the
// sender hears what happened — applied (with the status line it produced), refused (and why), or only accepted when
// the show thread didn't get to it in time. Whatever the command made (a message's signal id) rides along. A command
// handed to a playout link's peer is completed in that peer's own words (PlayoutReceipts), within the same deadline.
public sealed partial class MainViewModel
{
    private sealed class ReceiptNotes
    {
        public string? Error;
        public Dictionary<string, long>? Refs;
        /// <summary>Handed to a playout link's peer, which answers for itself: the receipt is its answer, not the show thread's.</summary>
        public PlayoutStep? Peer;
    }

    private ReceiptNotes? _receipt;

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

    private (CommandReceipt Receipt, PlayoutStep? Peer) RunWithReceipt(ShowCommand c, string id)
    {
        var before = StatusLine;
        var notes = _receipt = new ReceiptNotes();
        _executingRemote = true;
        try { ExecuteNow(c); }
        catch (Exception ex) { return (CommandReceipt.Refused(id, ex.Message), null); }
        finally { _executingRemote = false; _receipt = null; }
        if (notes.Error is { } error) return (CommandReceipt.Refused(id, error), null);
        if (notes.Peer is { } peer) return (CommandReceipt.Accepted(id), peer);
        return (new CommandReceipt(id, true, "applied", StatusLine != before ? StatusLine : "", null, notes.Refs), null);
    }

    /// <summary>The action didn't happen: say why on the status line and in the sender's receipt.</summary>
    private void Refuse(string why)
    {
        StatusLine = why;
        ReceiptRefused(why);
    }

    /// <summary>The command was handed to a playout link's peer, which answers for itself: its receipt is that answer.</summary>
    private void ReceiptPeer(PlayoutStep step)
    {
        if (_receipt is { } r) r.Peer = step;
    }

    /// <summary>The command being receipted was refused (a licence gate, a missing running order…).</summary>
    private void ReceiptRefused(string why)
    {
        if (_receipt is { Error: null } r) r.Error = why;
    }

    /// <summary>The command being receipted made something a sender can follow (a message's signal id).</summary>
    private void ReceiptRef(string name, long value)
    {
        if (_receipt is { } r) (r.Refs ??= new())[name] = value;
    }
}
