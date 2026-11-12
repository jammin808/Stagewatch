using System.Text.RegularExpressions;

namespace Stagewatch.Core;

/// <summary>
/// 13.12.6 at an acceptor whose commands come in on many threads (the web server's sockets, Companion TCP's clients): per connector, the
/// receipts of its last <see cref="PeerLink.ReceiptsKept"/> ids for <see cref="PeerLink.ReceiptsKeptMs"/>, kept by one
/// <see cref="PeerReceipts"/> each under one lock, so an id two sockets of one connector send at once runs once. A connector is the
/// verified id its link proved (<c>t:id</c>) where it has one, else what its door can name of it. It keeps the receipts of at most
/// <see cref="Connectors"/> connectors, the one heard from longest ago forgotten first. Only an id of 13.12.1's shape is kept — a prefix
/// of letters, a counter and its connector's inst — so a sender whose ids restart with its process is never answered from an older
/// process's record: its commands run as they always did.
/// </summary>
public sealed class ReceiptRecord
{
    /// <summary>The most connectors whose receipts are kept at once.</summary>
    public const int Connectors = 64;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, (PeerReceipts Book, long Heard)> _books = new(StringComparer.Ordinal);

    /// <summary>13.12.1's shape: 1–4 letters, a counter from 1, a dash and the connector's inst, within <c>request.id.chars</c>.</summary>
    private static readonly Regex Shape = new("^[A-Za-z]{1,4}[1-9][0-9]*-[A-Za-z0-9]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>Whether <paramref name="id"/> is one this record keeps: 13.12.1's shape, at most <c>request.id.chars</c> long.</summary>
    public static bool Keeps(string? id) => id is { Length: > 0 and <= PeerLink.RequestIdChars } && Shape.IsMatch(id);

    /// <summary>
    /// Before running <paramref name="id"/> from <paramref name="connector"/>: the record's answer when it holds the id — the receipt's
    /// fields it kept with <c>"repeat":true</c>, or accepted while it still runs — and then the command does not run; or null, and the
    /// id is held as running until <see cref="Keep"/>. Null too for an id this record does not keep (<see cref="Keeps"/>).
    /// </summary>
    public string? Begin(string connector, string id, long now)
    {
        if (!Keeps(id)) return null;
        lock (_gate) return Book(connector, now).Begin(connector, id, now);
    }

    /// <summary>The receipt that settled <paramref name="id"/> (<see cref="CommandReceipt.RecordJson"/>), kept for its repeats and RECEIPTS.</summary>
    public void Keep(string connector, string id, string fields, long now)
    {
        if (!Keeps(id)) return;
        lock (_gate) Book(connector, now).Keep(connector, id, fields, now);
    }

    /// <summary>
    /// A RECEIPTS answer for <paramref name="connector"/>'s own ids (at most <see cref="PeerLink.ReceiptsAskMax"/>), with this acceptor's
    /// <paramref name="inst"/>: <c>{"inst":…,"receipts":{…}}</c>, each id its kept receipt, accepted while it runs, or "unknown".
    /// </summary>
    public string Answer(string connector, IEnumerable<string> ids, string inst, long now)
    {
        lock (_gate) return Book(connector, now).Answer(connector, ids, inst, now);
    }

    /// <summary>The ids an ask names, each once, at most <see cref="PeerLink.ReceiptsAskMax"/>; null when it names none or more.</summary>
    public static IReadOnlyList<string>? AskOf(IEnumerable<string> ids)
    {
        var list = ids.Distinct(StringComparer.Ordinal).ToList();
        return list.Count is > 0 and <= PeerLink.ReceiptsAskMax && list.All(i => CommandReceipt.CleanId(i) is not null) ? list : null;
    }

    /// <summary>
    /// The words of an ask this record does not answer: an ask that names no id, more than <see cref="PeerLink.ReceiptsAskMax"/>, or an id
    /// no request id can be.
    /// </summary>
    public static readonly string AskWords =
        $"RECEIPTS names 1 to {PeerLink.ReceiptsAskMax.ToString(System.Globalization.CultureInfo.InvariantCulture)} request ids, each 1-32 letters, digits, - or _";

    /// <summary>
    /// A command's receipt and, after an <c>accepted</c>, its one follow-up, each handed to <paramref name="send"/> in order; the receipt
    /// that settles it — the follow-up where there is one — handed to <paramref name="settled"/> (a record's keep) before it is sent, whether
    /// or not the sender still takes it; and, when the follow-up says the show thread had still not run it, what it came to once it ran
    /// (<see cref="CommandReceipt.Later"/>) handed to <paramref name="settled"/> alone. A receipt that never comes (its task faulted: the
    /// show thread went away) is <see cref="Unanswered"/>, so no id is held as running past its run. <paramref name="send"/> swallows what a
    /// gone sender throws.
    /// </summary>
    public static async Task Deliver(string id, Task<CommandReceipt> receipt, Func<CommandReceipt, Task> send, Action<CommandReceipt>? settled)
    {
        var first = await Answered(receipt, id).ConfigureAwait(false);
        if (first.Final is null) settled?.Invoke(first);
        await send(first).ConfigureAwait(false);
        if (first.Final is not { } pending) return;
        var last = await Answered(pending, id).ConfigureAwait(false);
        settled?.Invoke(last);
        await send(last).ConfigureAwait(false);
        if (settled is null || last.Later is not { } later) return;
        try
        {
            if (await later.ConfigureAwait(false) is { } ran) settled(ran);
        }
        catch (Exception)
        {
            // The show thread went away before it ran: the record keeps the unconfirmed follow-up it has, which says it may have run.
        }

        static async Task<CommandReceipt> Answered(Task<CommandReceipt> task, string id)
        {
            try { return await task.ConfigureAwait(false); }
            catch (Exception) { return Unanswered(id); }
        }
    }

    /// <summary>
    /// The follow-up of a command whose receipt never came (the show thread went away, or threw, before it said): accepted, unconfirmed,
    /// final — it may have run.
    /// </summary>
    public static CommandReceipt Unanswered(string id)
        => new(id, true, "accepted", "unconfirmed — the show thread did not say what came of it; it may have run — look at its state", null) { IsFinal = true };

    private PeerReceipts Book(string connector, long now)
    {
        if (_books.TryGetValue(connector, out var held))
        {
            _books[connector] = (held.Book, now);
            return held.Book;
        }
        if (_books.Count >= Connectors)
            _books.Remove(_books.MinBy(kv => kv.Value.Heard).Key);
        var book = new PeerReceipts();
        _books[connector] = (book, now);
        return book;
    }
}
