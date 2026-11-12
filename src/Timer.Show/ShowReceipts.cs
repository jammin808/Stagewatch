using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// The Show Bus receipt of a command handed to the show thread (§3, 13.12.4–5), whatever the thread is (the desk's dispatcher, the rig's
/// owner thread). Within the sender's deadline it is what the run made of it (<c>complete</c>: applied, refused, or what a peer or another
/// app answered); with no run by then, accepted, and its one follow-up (<see cref="CommandReceipt.Final"/>) within <c>receipt.final.ms</c>
/// of the read: what the run made of it once it ran, marked final — or, when the thread has still not run it, accepted and unconfirmed,
/// for it may still run, with what it came to once it ran handed on for the record (<see cref="CommandReceipt.Later"/>). A command that runs more than <c>receipt.late.ms</c> after it was read says how late (<c>lateMs</c>). Pure but
/// for the waits, which run on the clock it is given.
/// </summary>
public static class ShowReceipts
{
    /// <summary>§14 <c>receipt.late.ms</c>: a command run later than this after it was read says how late.</summary>
    public static readonly TimeSpan LateAfter = TimeSpan.FromMilliseconds(1_000);

    /// <summary>The longest a follow-up waits from the read: §14 <c>receipt.final.ms</c>.</summary>
    public static readonly TimeSpan FinalWithin = TimeSpan.FromMilliseconds(PeerLink.ReceiptFinalMs);

    /// <summary>
    /// The receipt of the command whose run is <paramref name="ran"/>, read at <paramref name="readAt"/> (a timestamp of
    /// <paramref name="time"/>): <paramref name="complete"/>'s, given what is left of <paramref name="deadline"/>, when it ran within it;
    /// else accepted (its effect empty, as an accepted receipt's always was), with its follow-up.
    /// </summary>
    public static async Task<CommandReceipt> Of<T>(string id, Task<T> ran, Func<T, TimeSpan, Task<CommandReceipt>> complete, TimeSpan deadline,
        TimeProvider time, long readAt)
    {
        if (await Within(ran, deadline - time.GetElapsedTime(readAt), time).ConfigureAwait(false))
            return await complete(await ran.ConfigureAwait(false), deadline - time.GetElapsedTime(readAt)).ConfigureAwait(false);
        return CommandReceipt.Accepted(id) with { Final = FinalOf(id, ran, complete, time, readAt) };
    }

    /// <summary>
    /// How late a command ran, in ms, when the show thread began it more than <see cref="LateAfter"/> after it was read; else null.
    /// </summary>
    public static long? Late(TimeSpan sinceRead) => sinceRead > LateAfter ? (long)sinceRead.TotalMilliseconds : null;

    /// <summary><paramref name="receipt"/> saying how late its command ran, and its follow-up the same; unchanged when it ran in time.</summary>
    public static CommandReceipt Noted(CommandReceipt receipt, long? lateMs)
    {
        if (lateMs is not { } late) return receipt;
        var final = receipt.Final is { } f ? NotedFinal(f, late) : null;
        return receipt with { LateMs = late, Final = final };

        static async Task<CommandReceipt> NotedFinal(Task<CommandReceipt> f, long late) => (await f.ConfigureAwait(false)) with { LateMs = late };
    }

    /// <summary>The journal's words for a command run late (13.12.5): which, and how late after it was read.</summary>
    public static string LateLine(ShowCommandKind kind, string id, long lateMs)
        => $"{kind} ({id}) ran {Seconds(lateMs)} s after it was read — the show thread was busy; its receipt says lateMs {lateMs.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The follow-up of a command the show thread had not run within <see cref="FinalWithin"/> of its read: accepted, unconfirmed, final.
    /// </summary>
    public static CommandReceipt Unrun(string id)
        => new(id, true, "accepted", $"{PlayoutUnconfirmed} — the show thread had not run it {Seconds(PeerLink.ReceiptFinalMs)} s after it was read; it may still run — look at its state", null)
        {
            IsFinal = true,
        };

    /// <summary>The word an unconfirmed outcome's words start with (as a playout link's): it may have run, or may yet.</summary>
    private const string PlayoutUnconfirmed = "unconfirmed";

    /// <summary>How long a run that missed its follow-up is still watched for the record: §14 <c>receipts.kept.ms</c>, from the read.</summary>
    public static readonly TimeSpan KeptWithin = TimeSpan.FromMilliseconds(PeerLink.ReceiptsKeptMs);

    private static async Task<CommandReceipt> FinalOf<T>(string id, Task<T> ran, Func<T, TimeSpan, Task<CommandReceipt>> complete, TimeProvider time, long readAt)
    {
        if (!await Within(ran, FinalWithin - time.GetElapsedTime(readAt), time).ConfigureAwait(false))
            return Unrun(id) with { Later = LaterOf(ran, complete, time, readAt) };
        return await Ran(ran, complete, time, readAt).ConfigureAwait(false);
    }

    /// <summary>
    /// What a command whose follow-up said unrun came to once the show thread ran it, within <see cref="KeptWithin"/> of its read, for the
    /// record (<see cref="CommandReceipt.Later"/>); null when it had still not run by then.
    /// </summary>
    private static async Task<CommandReceipt?> LaterOf<T>(Task<T> ran, Func<T, TimeSpan, Task<CommandReceipt>> complete, TimeProvider time, long readAt)
        => await Within(ran, KeptWithin - time.GetElapsedTime(readAt), time).ConfigureAwait(false)
            ? await Ran(ran, complete, time, readAt).ConfigureAwait(false)
            : null;

    /// <summary>The settling receipt of a run that has ended: what waits on another node ends with that node's follow-up, already final; any other run ends here.</summary>
    private static async Task<CommandReceipt> Ran<T>(Task<T> ran, Func<T, TimeSpan, Task<CommandReceipt>> complete, TimeProvider time, long readAt)
    {
        var receipt = await complete(await ran.ConfigureAwait(false), FinalWithin - time.GetElapsedTime(readAt)).ConfigureAwait(false);
        return receipt.Final is { } after ? await after.ConfigureAwait(false) with { IsFinal = true } : receipt with { IsFinal = true };
    }

    /// <summary>Whether <paramref name="task"/> has ended, or ends within <paramref name="wait"/> on <paramref name="time"/>.</summary>
    private static async Task<bool> Within(Task task, TimeSpan wait, TimeProvider time)
    {
        if (task.IsCompleted) return true;
        if (wait <= TimeSpan.Zero) return false;
        try
        {
            await task.WaitAsync(wait, time).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static string Seconds(long ms) => (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);
}
