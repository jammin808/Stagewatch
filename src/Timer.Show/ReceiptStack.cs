using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// What a command being receipted notes as it runs (Show Bus §3): why it was refused, what it made — by number
/// (<see cref="Refs"/>) and by id (<see cref="RefIds"/>) — and the peer it was handed to, which then answers for itself.
/// </summary>
public sealed class ReceiptNotes<TPeer> where TPeer : class
{
    internal ReceiptNotes(ReceiptNotes<TPeer>? outer, bool outerRemote)
    {
        Outer = outer;
        OuterRemote = outerRemote;
    }

    internal ReceiptNotes<TPeer>? Outer { get; }
    internal bool OuterRemote { get; }

    /// <summary>Why the command did not happen; the first reason given stands.</summary>
    public string? Error { get; internal set; }
    public Dictionary<string, long>? Refs { get; internal set; }
    public Dictionary<string, string>? RefIds { get; internal set; }
    /// <summary>Handed to a playout link's peer, which answers for itself: the receipt is its answer, not the show thread's.</summary>
    public TPeer? Peer { get; internal set; }

    /// <summary>
    /// The receipt these notes come to: refused in the first reason's words, or applied with <paramref name="effect"/>; either way
    /// it names what the command made before it ended. A command handed to a peer is receipted by that peer, not here.
    /// </summary>
    public CommandReceipt Receipt(string id, string effect)
        => Error is { } error
            ? CommandReceipt.Refused(id, error) with { Refs = Refs, RefIds = RefIds }
            : new CommandReceipt(id, true, "applied", effect, null, Refs, RefIds);
}

/// <summary>
/// The show thread's receipts in flight, innermost first: a command that runs another command inside itself — a person's approval of
/// a line aimed at this desk — opens notes of its own for the inner one and gets its own back when that ends, so the inner command's
/// refusal, refs and peer never land on the outer receipt, and the outer's are still there to take what it notes afterwards.
/// <see cref="Remote"/> — a command from a wire is running, so nothing it does is offered to a deck's recorder as the operator's
/// own — is kept the same way: every run that sets it, receipted (<see cref="Enter"/>) or not (<see cref="EnterRemote"/>), ends by
/// putting back what it found, never by clearing it. One thread only.
/// </summary>
public sealed class ReceiptStack<TPeer> where TPeer : class
{
    /// <summary>The notes of the command running now, or null when none is being receipted.</summary>
    public ReceiptNotes<TPeer>? Current { get; private set; }

    /// <summary>A command that came from a wire is running.</summary>
    public bool Remote { get; private set; }

    /// <summary>A wire's command with no receipt starts: it is marked, and what was found is returned for <see cref="LeaveRemote"/>.</summary>
    public bool EnterRemote()
    {
        var found = Remote;
        Remote = true;
        return found;
    }

    /// <summary>That command ended: the mark is again what <see cref="EnterRemote"/> found — it may have run inside another wire's command.</summary>
    public void LeaveRemote(bool found) => Remote = found;

    /// <summary>How many receipts are in flight (0 when none).</summary>
    public int Depth
    {
        get
        {
            var depth = 0;
            for (var n = Current; n is not null; n = n.Outer) depth++;
            return depth;
        }
    }

    /// <summary>A receipted command starts: notes of its own, the ones before it kept under them.</summary>
    public ReceiptNotes<TPeer> Enter()
    {
        var notes = new ReceiptNotes<TPeer>(Current, Remote);
        Current = notes;
        Remote = true;
        return notes;
    }

    /// <summary>The command <paramref name="notes"/> were opened for ended: the notes and <see cref="Remote"/> are again what it found.</summary>
    public void Leave(ReceiptNotes<TPeer> notes)
    {
        Current = notes.Outer;
        Remote = notes.OuterRemote;
    }

    /// <summary>The command running now was refused (a licence gate, a missing running order…): the first reason stands.</summary>
    public void Refused(string why)
    {
        if (Current is { Error: null } notes) notes.Error = why;
    }

    /// <summary>The command running now made something a sender can follow by number (a message's signal id).</summary>
    public void Ref(string name, long value)
    {
        if (Current is { } notes) (notes.Refs ??= new())[name] = value;
    }

    /// <summary>The command running now made something a sender can follow by an id that is no number (a shared proposal's).</summary>
    public void RefId(string name, string id)
    {
        if (Current is { } notes) (notes.RefIds ??= new(StringComparer.Ordinal))[name] = id;
    }

    /// <summary>The command running now was handed to a playout link's peer, which answers for itself.</summary>
    public void Peer(TPeer step)
    {
        if (Current is { } notes) notes.Peer = step;
    }
}
