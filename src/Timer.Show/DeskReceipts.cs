using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// The Show Bus receipt of a line a person sent to another app (§3, §11): what that app made of it, not only that it was queued.
/// Within the sender's deadline it is the proposal's end — applied in the app's words when it ran it, refused when it said no, was
/// not delivered or was rejected — and with none by then, accepted, saying where the line stands: sent and not collected yet, or
/// collected and not answered. An accepted receipt carries its one follow-up (<see cref="CommandReceipt.Final"/>) within
/// <c>receipt.final.ms</c> of the send: the proposal's end once it comes, else accepted still, in words that say what is known.
/// Every receipt names the proposal in <c>refs.sharedProposal</c>, by its whole id. Pure but for the waits, on the clock given.
/// </summary>
public static class DeskReceipts
{
    /// <summary>The name a receipt's <c>refs</c> gives the shared proposal a command made or decided: its whole id, a string.</summary>
    public const string Ref = "sharedProposal";

    /// <summary>The longest a follow-up waits from the send: §14 <c>receipt.final.ms</c>.</summary>
    public static readonly TimeSpan FinalWithin = TimeSpan.FromMilliseconds(PeerLink.ReceiptFinalMs);

    /// <summary>
    /// The receipt for the line sent as <paramref name="proposal"/>, waiting at most <paramref name="remaining"/> on
    /// <paramref name="time"/> for its end; its follow-up, when it says accepted, by <see cref="FinalWithin"/> after
    /// <paramref name="sentAt"/> (a timestamp of <paramref name="time"/>).
    /// </summary>
    public static async Task<CommandReceipt> Within(string id, DeskService desk, string proposal, TimeSpan remaining, TimeProvider time, long sentAt)
    {
        var settled = desk.Settled(proposal);
        if (await Wait(settled, remaining, time).ConfigureAwait(false) is { } ended) return Of(id, ended);
        var now = desk.ResultOf(proposal) ?? await settled.ConfigureAwait(false);
        return now.Settled ? Of(id, now) : Of(id, now) with { Final = FinalOf(id, desk, proposal, settled, time, sentAt) };
    }

    /// <summary>
    /// Pure: the receipt a line's standing makes — applied when its app ran it; refused (not ok) when its app said no, it was not
    /// delivered or it was rejected; accepted while nothing has settled it.
    /// </summary>
    public static CommandReceipt Of(string id, DeskResult result, bool final = false)
    {
        var refs = new Dictionary<string, string>(StringComparer.Ordinal) { [Ref] = result.Id };
        var receipt = result.Ok switch
        {
            true => new CommandReceipt(id, true, "applied", result.Words, null, null, refs),
            false => new CommandReceipt(id, false, "applied", "", result.Words, null, refs),
            null => new CommandReceipt(id, true, "accepted", result.Words, null, null, refs),
        };
        return receipt with { IsFinal = final };
    }

    /// <summary>
    /// Pure: the follow-up of a line nothing settled within <see cref="FinalWithin"/> of the send: accepted and final. One its app
    /// collected and never answered is unconfirmed — it may have run; one never collected has not run, and still waits to be.
    /// </summary>
    public static CommandReceipt Unsettled(string id, DeskResult result)
    {
        var s = FinalWithin.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        var words = result.Stage == "collected"
            ? $"unconfirmed — {result.Words}; none came in {s} s — it may have run; its result is on the desk as {DeskService.Short(result.Id)}"
            : $"{result.Words} ({s} s on); it is not delivered unless it is collected";
        return Of(id, result with { Words = words }, final: true);
    }

    private static async Task<CommandReceipt> FinalOf(string id, DeskService desk, string proposal, Task<DeskResult> settled, TimeProvider time, long sentAt)
    {
        if (await Wait(settled, FinalWithin - time.GetElapsedTime(sentAt), time).ConfigureAwait(false) is { } ended) return Of(id, ended, final: true);
        var now = desk.ResultOf(proposal) ?? await settled.ConfigureAwait(false);
        return now.Settled ? Of(id, now, final: true) : Unsettled(id, now);
    }

    /// <summary><paramref name="settled"/>'s result when it comes within <paramref name="wait"/> on <paramref name="time"/>, else null.</summary>
    private static async Task<DeskResult?> Wait(Task<DeskResult> settled, TimeSpan wait, TimeProvider time)
    {
        if (settled.IsCompleted) return await settled.ConfigureAwait(false);
        if (wait <= TimeSpan.Zero) return null;
        try
        {
            return await settled.WaitAsync(wait, time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
