using Stagewatch.Core;

namespace Stagewatch.App.ViewModels;

// Show Bus receipts: "attempts are not facts". A command that comes with a request id runs on the show thread and the
// sender hears what happened — applied (with the status line it produced), refused (and why), or only accepted when
// the show thread didn't get to it in time. Whatever the command made (a message's signal id) rides along.
public sealed partial class MainViewModel
{
    private sealed class ReceiptNotes
    {
        public string? Error;
        public Dictionary<string, long>? Refs;
        /// <summary>Handed on to another app that confirms later (Patterns): "accepted", not "applied".</summary>
        public bool HandedOn;
    }

    private ReceiptNotes? _receipt;

    /// <summary>IShowCommands: run a command and say what happened. Thread-safe; never waits on itself.</summary>
    public async Task<CommandReceipt> ExecuteWithReceiptAsync(ShowCommand command, string id, TimeSpan timeout)
    {
        if (_ui.CheckAccess()) return RunWithReceipt(command, id);
        var op = _ui.InvokeAsync(() => RunWithReceipt(command, id));
        var done = await Task.WhenAny(op.Task, Task.Delay(timeout)).ConfigureAwait(false);
        return done == op.Task ? await op.Task.ConfigureAwait(false) : CommandReceipt.Accepted(id);
    }

    private CommandReceipt RunWithReceipt(ShowCommand c, string id)
    {
        var before = StatusLine;
        var notes = _receipt = new ReceiptNotes();
        _executingRemote = true;
        try { ExecuteNow(c); }
        catch (Exception ex) { return CommandReceipt.Refused(id, ex.Message); }
        finally { _executingRemote = false; _receipt = null; }
        if (notes.Error is { } error) return CommandReceipt.Refused(id, error);
        return new CommandReceipt(id, true, notes.HandedOn ? "accepted" : "applied", StatusLine != before ? StatusLine : "", null, notes.Refs);
    }

    /// <summary>The action didn't happen: say why on the status line and in the sender's receipt.</summary>
    private void Refuse(string why)
    {
        StatusLine = why;
        ReceiptRefused(why);
    }

    /// <summary>The command was passed to another app that answers for itself later (its own receipt).</summary>
    private void ReceiptHandedOn()
    {
        if (_receipt is { } r) r.HandedOn = true;
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
