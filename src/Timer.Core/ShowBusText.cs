namespace Stagewatch.Core;

/// <summary>
/// How a Show Bus writer shortens text (§2, §6, §14), one rule for every writer in this tree: a word — an id, a kind, a node's name — is
/// clipped with no mark; words — a label, a state, a headline, a recent line — are cut and end with "…". Neither ever ends inside a
/// surrogate pair, and neither throws for no room. A node's name is said the same in its hello, its context's <c>node</c> and every peer
/// row: no control characters and at most <see cref="NameChars"/>, so a peer hears one name for it wherever it reads it.
/// </summary>
public static class ShowBusText
{
    /// <summary>§14 <c>hello.name.chars</c>: the most a node's name says, in its hello and in every context.</summary>
    public const int NameChars = 64;

    /// <summary>A node's name (or a host) as the Show Bus says it: no control characters, clipped at <see cref="NameChars"/>.</summary>
    public static string Name(string? text) => Clip(new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()), NameChars);

    /// <summary>At most <paramref name="max"/> UTF-16 units of <paramref name="text"/> with no mark ("" for no room).</summary>
    public static string Clip(string text, int max)
        => max <= 0 ? "" : text.Length <= max ? text : char.IsHighSurrogate(text[max - 1]) ? text[..(max - 1)] : text[..max];

    /// <summary>
    /// <paramref name="text"/> within <paramref name="max"/> UTF-16 units: whole when it fits, else cut and ended with "…" ("" for no room).
    /// </summary>
    public static string Cut(string text, int max)
        => max <= 0 ? "" : text.Length <= max ? text : Clip(text, max - 1) + "…";
}
