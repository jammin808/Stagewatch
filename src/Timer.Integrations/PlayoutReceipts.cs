using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Integrations;

/// <summary>
/// A playout command's Show Bus receipt (§3) from its ticket: completed in the peer's own words, never a "done" the peer did not say
/// (G07). Within the sender's deadline it is the peer's first answer — applied, refused, accepted (taken, not run yet) or unconfirmed
/// — and with none by then, accepted "sent to … — waiting for its answer". An accepted receipt that waits on the peer carries its one
/// follow-up (<see cref="CommandReceipt.Final"/>), the same id with <c>final</c>, within <c>receipt.final.ms</c> of the send: the
/// peer's final answer — applied, refused, unconfirmed or not-sent, never an accepted one — once it comes, and when none has come by
/// then, unconfirmed in Stagewatch's own words: it may have run. Pure but for the waits, which run on the clock it is given.
/// </summary>
public static class PlayoutReceipts
{
    /// <summary>The longest a follow-up waits from the send: §14 <c>receipt.final.ms</c>.</summary>
    public static readonly TimeSpan FinalWithin = TimeSpan.FromMilliseconds(PeerLink.ReceiptFinalMs);

    /// <summary>The word an unconfirmed outcome's words start with: written, and no final answer came; it may have run.</summary>
    public const string Unconfirmed = "unconfirmed";

    /// <summary>
    /// The receipt for <paramref name="step"/>'s command, waiting at most <paramref name="remaining"/> for the peer's first answer on
    /// <paramref name="time"/>; its follow-up, when it says accepted, is <see cref="Settled"/>'s, by <see cref="FinalWithin"/> after
    /// <paramref name="sentAt"/> (a timestamp of <paramref name="time"/>).
    /// </summary>
    public static async Task<CommandReceipt> Within(string id, PlayoutStep step, TimeSpan remaining, TimeProvider time, long sentAt)
    {
        if (step.Error is { } refused) return CommandReceipt.Refused(id, refused);
        var ticket = step.Ticket!;
        var answered = await Answer(ticket.First, remaining, time).ConfigureAwait(false);
        // No answer yet: whatever comes first later — accepted under load among it — is not final; the follow-up waits for the final one.
        if (answered is not { } first) return Waiting(id, step.Peer, ticket) with { Final = FinalOf(id, step.Peer, ticket, time, sentAt) };
        var receipt = Of(id, first, step.Peer, ticket);
        return first.Outcome == PeerOutcome.Accepted ? receipt with { Final = FinalOf(id, step.Peer, ticket, time, sentAt) } : receipt;
    }

    /// <summary>
    /// The command's final receipt, marked <c>final</c>: refused when nothing was sent; else the peer's final answer within
    /// <see cref="FinalWithin"/> of <paramref name="sentAt"/> — applied, refused, unconfirmed or not-sent, never accepted — and when none
    /// came by then, unconfirmed in Stagewatch's own words (<see cref="Unanswered"/>). What an approved proposal ends on.
    /// </summary>
    public static async Task<CommandReceipt> Settled(string id, PlayoutStep step, TimeProvider time, long sentAt)
    {
        if (step.Error is { } refused) return CommandReceipt.Refused(id, refused) with { IsFinal = true };
        return await FinalOf(id, step.Peer, step.Ticket!, time, sentAt).ConfigureAwait(false);
    }

    /// <summary>The receipt while the peer has not answered: accepted, sent and waiting, its request named in <c>refs.peerRequest</c>.</summary>
    public static CommandReceipt Waiting(string id, string peer, PlayoutTicket ticket)
        => new(id, true, "accepted", $"sent to {peer} — waiting for its answer", null, Refs(ticket));

    /// <summary>
    /// Pure: the receipt an outcome makes, in the peer's words where it gave some and Stagewatch's where it gave none ("PlayMap Stage
    /// left took PLAYLIST GO show intro"): applied ok; refused not ok, with the peer's words; accepted ok, taken and not run yet;
    /// unconfirmed ok and accepted, saying it may have run; not-sent not ok, nothing of it reached the peer.
    /// </summary>
    public static CommandReceipt Of(string id, PlayoutAnswer a, string peer, PlayoutTicket ticket, bool final = false)
    {
        var words = a.Words;
        var refs = Refs(ticket);
        var receipt = a.Outcome switch
        {
            PeerOutcome.Applied => new CommandReceipt(id, true, "applied", $"{peer} took {a.Line}" + (words.Length > 0 ? $" ({words})" : ""), null, refs),
            PeerOutcome.Refused => new CommandReceipt(id, false, "applied", "", $"{peer} refused {a.Line}: {(words.Length > 0 ? words : "refused")}", refs),
            PeerOutcome.Accepted => new CommandReceipt(id, true, "accepted", words.Length > 0 ? $"{peer}: {words}" : $"{peer} took {a.Line} — it has not run yet", null, refs),
            PeerOutcome.Unconfirmed => new CommandReceipt(id, true, "accepted", Unconfirmed + " — " + (words.Length > 0 ? words : "it may have run"), null, refs),
            _ => new CommandReceipt(id, false, "applied", "", "not sent — " + (words.Length > 0 ? words : "nothing of it reached " + peer), refs),
        };
        return receipt with { IsFinal = final };
    }

    /// <summary>
    /// Pure: the final receipt of a command whose peer gave no final answer within <see cref="FinalWithin"/> of the send (13.12.3's
    /// "no answer in 30 s — it may have run"): accepted and unconfirmed, marked final — <paramref name="took"/> when the peer had said
    /// it took it, so the words say it never said it ran.
    /// </summary>
    public static CommandReceipt Unanswered(string id, string peer, PlayoutTicket ticket, bool took)
    {
        var s = FinalWithin.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        var words = took ? $"{peer} took it and gave no final answer in {s} s" : $"no answer from {peer} in {s} s";
        return new CommandReceipt(id, true, "accepted", $"{Unconfirmed} — {words} — it may have run; look at its state", null, Refs(ticket)) { IsFinal = true };
    }

    /// <summary>
    /// Pure: how an approved proposal whose line went to a playout link's peer ends (§11: done or failed, with the words), from that
    /// command's final receipt (<see cref="Settled"/>): done only when the peer said it applied it; failed when it refused or nothing was
    /// sent; and an unconfirmed one — taken and never said to have run, or never answered — is never done: failed, its words saying it
    /// may have run, for §11 has no state between done and failed.
    /// </summary>
    public static (bool Done, string Words) ProposalOutcome(CommandReceipt final)
    {
        if (!final.Ok) return (false, final.Error is { Length: > 0 } e ? e : "refused");
        if (final.Stage == "applied") return (true, final.Effect);
        return (false, final.Effect.StartsWith(Unconfirmed, StringComparison.Ordinal) ? final.Effect : $"{Unconfirmed} — {final.Effect} — it may have run");
    }

    private static Dictionary<string, long> Refs(PlayoutTicket ticket) => new() { ["peerRequest"] = ticket.Number };

    /// <summary>
    /// The ticket's final answer as a final receipt, waited for until <see cref="FinalWithin"/> after the send; with none by then,
    /// <see cref="Unanswered"/> — taken when the peer's first answer said accepted.
    /// </summary>
    private static async Task<CommandReceipt> FinalOf(string id, string peer, PlayoutTicket ticket, TimeProvider time, long sentAt)
    {
        var left = FinalWithin - time.GetElapsedTime(sentAt);
        if (await Answer(ticket.Final, left, time).ConfigureAwait(false) is { } a) return Of(id, a, peer, ticket, final: true);
        var took = ticket.First is { IsCompletedSuccessfully: true } first && (await first.ConfigureAwait(false)).Outcome == PeerOutcome.Accepted;
        return Unanswered(id, peer, ticket, took);
    }

    /// <summary><paramref name="answer"/> when it comes within <paramref name="wait"/> on <paramref name="time"/>, else null.</summary>
    private static async Task<PlayoutAnswer?> Answer(Task<PlayoutAnswer> answer, TimeSpan wait, TimeProvider time)
    {
        if (answer.IsCompleted) return await answer.ConfigureAwait(false);
        if (wait <= TimeSpan.Zero) return null;
        try
        {
            return await answer.WaitAsync(wait, time).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
