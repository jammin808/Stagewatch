namespace Stagewatch.Core;

/// <summary>
/// What a load left out of a file or changed in it, in words a person reads once (a status line, the show log, Diagnostics): never a
/// token or a key, and a file's own text in them only as <see cref="Shown"/> makes it. At most <see cref="Max"/> notes; past that, one
/// more line says how many more there were.
/// </summary>
public sealed class LoadNotes
{
    public const int Max = 16;
    /// <summary>The most of a file's own text (a name, a host, a title) a note quotes.</summary>
    public const int QuoteChars = 40;

    private readonly List<string> _words = new();
    private int _more;

    public void Add(string words)
    {
        if (_words.Count < Max) _words.Add(words);
        else _more++;
    }

    /// <summary>Forget every note (a load that starts again from nothing says only what that load did).</summary>
    public void Clear()
    {
        _words.Clear();
        _more = 0;
    }

    public int Count => _words.Count + _more;

    /// <summary>The notes in the order they were made, and "and N more" when there were more than <see cref="Max"/>.</summary>
    public IReadOnlyList<string> Words
        => _more == 0 ? _words.ToList() : [.. _words, $"and {_more.ToString(System.Globalization.CultureInfo.InvariantCulture)} more"];

    /// <summary>A file's own text as a note quotes it: no control characters, cut at <see cref="QuoteChars"/>.</summary>
    public static string Shown(string? text) => ShowBusText.Cut(new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()), QuoteChars);
}
