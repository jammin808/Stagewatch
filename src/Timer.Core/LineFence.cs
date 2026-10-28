using System.Globalization;
using System.Text;

namespace Stagewatch.Core;

/// <summary>
/// What no line on the approval desk holds (Show Bus §11): a line is at most <see cref="MaxChars"/> characters, refused — never cut,
/// since a cut line is another command — when longer, and refused when it holds a control character, a key of 13.5.1's shape or a
/// window's address. This desk also refuses what makes the line a person reads differ from the line that runs, or from the words an AI
/// reads: an invisible format character (<see cref="Hidden"/>) and half of a surrogate pair. A secret placeholder filled with anything
/// but <see cref="VerbSyntax.SecretMarker"/> is the rating's to find (<see cref="RiskVerdict.Secret"/>) and refused in
/// <see cref="SecretWords"/>. A proposer that keeps a secret itself sends the marker in its place and runs its own record. Pure; every
/// check reads the line once, left to right.
/// </summary>
public static class LineFence
{
    /// <summary>The longest line, in characters (§14 <c>proposal.line.chars</c>).</summary>
    public const int MaxChars = 2048;

    /// <summary>A key's symbols (13.5.1): Stagewatch's <c>swk_</c>, Patterns' <c>ptk_</c>, PlayMap's <c>pmk_</c> and every app's alike.</summary>
    public const string KeyAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";

    /// <summary>The symbols after a key's prefix (13.5.1).</summary>
    public const int KeySymbols = 32;

    /// <summary>The fewest and most lowercase letters of a key's prefix before its <c>_</c>.</summary>
    public const int KeyPrefixMin = 2, KeyPrefixMax = 8;

    /// <summary>A window's address on any node's web door: this, then its token.</summary>
    public const string WindowPath = "/w/";

    public static readonly string TooLong = $"a line is at most {MaxChars} characters — it is refused, never cut";
    public const string Control = "a line holds no control characters";
    public const string Format = "a line holds no invisible format characters (a direction override, a zero-width space) — what a person reads is what runs";
    public const string Surrogate = "a line holds no half of a surrogate pair";
    public const string Key = "a key rides in no line";
    public const string Window = "a window's address rides in no line";

    /// <summary>Why a line that fills a secret placeholder is refused: the secret is typed on the node that keeps it.</summary>
    public static string SecretWords(string node) => $"a secret rides in no line — type it on {node} itself";

    /// <summary>
    /// Why <paramref name="line"/> may not stand on the desk, or null: longer than <see cref="MaxChars"/>; a control character, an
    /// invisible format character (<see cref="Hidden"/>) or half of a surrogate pair anywhere, the first of them named; a word of a
    /// key's shape (<see cref="HoldsKey"/>); or a window's address — <see cref="WindowPath"/> and a token's length of window symbols,
    /// or any of <paramref name="windowTokens"/>, this desk's windows now.
    /// </summary>
    public static string? Check(string line, IEnumerable<string>? windowTokens = null)
    {
        if (line.Length > MaxChars) return TooLong;
        for (var i = 0; i < line.Length; i++)
        {
            if (char.IsSurrogatePair(line, i))
            {
                if (Hidden(Rune.GetUnicodeCategory(new Rune(line[i], line[i + 1])), line[i])) return Format;
                i++;
                continue;
            }
            if (char.IsSurrogate(line[i])) return Surrogate;
            if (char.IsControl(line[i])) return Control;
            if (Hidden(CharUnicodeInfo.GetUnicodeCategory(line[i]), line[i])) return Format;
        }
        if (HoldsKey(line)) return Key;
        if (HoldsWindow(line) || (windowTokens?.Any(t => t.Length > 0 && line.Contains(t, StringComparison.Ordinal)) ?? false)) return Window;
        return null;
    }

    /// <summary>
    /// Whether a character of <paramref name="category"/> is one a person cannot see that changes what the line says: every format
    /// character — a direction override, embedding or isolate that reorders the words drawn, a zero-width space, a word joiner, an
    /// invisible operator, a tag character that spells words only a machine reads, a byte-order mark, a soft hyphen — but the
    /// zero-width non-joiner and joiner, which several scripts and emoji need, and which shape a glyph without moving, hiding or
    /// spelling any other character.
    /// </summary>
    public static bool Hidden(UnicodeCategory category, char first) => category == UnicodeCategory.Format && first is not ('\u200C' or '\u200D');

    /// <summary>
    /// Free words shown beside a line (a reason, a node's name): what <see cref="Check"/> refuses in a line — control characters,
    /// invisible format characters, half of a surrogate pair — left out rather than refused, since these words never run; then
    /// trimmed and clipped to <paramref name="max"/> characters with no mark, never ending inside a surrogate pair.
    /// </summary>
    public static string Shown(string text, int max)
    {
        var kept = new StringBuilder(Math.Min(text.Length, max + 1));
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogatePair(text, i))
            {
                if (!Hidden(Rune.GetUnicodeCategory(new Rune(text[i], text[i + 1])), text[i])) kept.Append(text, i, 2);
                i++;
                continue;
            }
            if (char.IsSurrogate(text[i]) || char.IsControl(text[i]) || Hidden(CharUnicodeInfo.GetUnicodeCategory(text[i]), text[i])) continue;
            kept.Append(text[i]);
        }
        return ShowBusText.Clip(kept.ToString().Trim(), max);
    }

    /// <summary>
    /// The prefixes of the show apps' own credentials: Stagewatch's keys, Patterns' and PlayMap's app keys, and AGMessage's context
    /// token (<c>agn_</c> and base64url, not 13.5.1's shape). A word that opens with one and goes on is a credential whatever its length,
    /// so a cut or mistyped one is refused as a whole one is.
    /// </summary>
    public static readonly IReadOnlyList<string> KeyPrefixes = ["swk_", "ptk_", "pmk_", "agn_"];

    /// <summary>
    /// Whether the line holds a key: 13.5.1's shape — <see cref="KeyPrefixMin"/> or more lowercase letters, <c>_</c>, then at least
    /// <see cref="KeySymbols"/> of <see cref="KeyAlphabet"/> — wherever it stands, quoted, inside a word or with more after it; or a
    /// word that opens with one of <see cref="KeyPrefixes"/> and goes on with a letter, a digit, <c>-</c> or <c>_</c>. A key is
    /// refused however it is wrapped.
    /// </summary>
    public static bool HoldsKey(string line)
    {
        for (var i = KeyPrefixMin; i < line.Length; i++)
        {
            if (line[i] != '_') continue;
            var letters = 0;
            while (letters < KeyPrefixMax && i - letters - 1 >= 0 && line[i - letters - 1] is >= 'a' and <= 'z') letters++;
            if (letters < KeyPrefixMin) continue;
            var symbols = 0;
            while (symbols < KeySymbols && i + symbols + 1 < line.Length && KeyAlphabet.Contains(line[i + symbols + 1], StringComparison.Ordinal)) symbols++;
            if (symbols == KeySymbols) return true;
        }
        foreach (var prefix in KeyPrefixes)
            for (var at = line.IndexOf(prefix, StringComparison.Ordinal); at >= 0; at = line.IndexOf(prefix, at + 1, StringComparison.Ordinal))
            {
                var next = at + prefix.Length;
                if ((at == 0 || !char.IsAsciiLetterOrDigit(line[at - 1])) && next < line.Length && (char.IsAsciiLetterOrDigit(line[next]) || line[next] is '-' or '_'))
                    return true;
            }
        return false;
    }

    /// <summary>
    /// Whether the line holds a window's address: <see cref="WindowPath"/> followed by a token of a window's shape
    /// (<see cref="ScreenWindows.ValidToken"/>), whatever follows it.
    /// </summary>
    public static bool HoldsWindow(string line)
    {
        for (var at = line.IndexOf(WindowPath, StringComparison.Ordinal); at >= 0; at = line.IndexOf(WindowPath, at + 1, StringComparison.Ordinal))
        {
            var start = at + WindowPath.Length;
            if (start + ScreenWindows.TokenLength <= line.Length && ScreenWindows.ValidToken(line.Substring(start, ScreenWindows.TokenLength))) return true;
        }
        return false;
    }
}
