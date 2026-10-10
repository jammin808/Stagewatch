using System.Globalization;
using System.Text;

namespace Stagewatch.Core;

public enum PaceState { Unknown, Ahead, OnPace, Behind }

/// <summary>Slide-level "ghost run": live position compared with the speaker's own rehearsal.</summary>
public readonly record struct GhostResult(
    /// <summary>Positive = behind the rehearsal by this many seconds.</summary>
    long DeltaSeconds,
    /// <summary>Projected total talk length at the current pace.</summary>
    long ProjectedTotalSeconds,
    PaceState State);

/// <summary>
/// The rehearsal is a ghost car on the track: at every slide we know where the speaker was in
/// rehearsal, so we can say "2:10 behind your rehearsal, on course to finish 1:40 over" long
/// before the countdown reaches zero.
/// </summary>
public static class GhostRun
{
    /// <param name="budgets">Seconds per slide from the rehearsal (index 0 = slide 1).</param>
    /// <param name="slide">Current slide, 1-based.</param>
    /// <param name="onSlideSeconds">Time spent on the current slide so far.</param>
    /// <param name="elapsedSeconds">Time since the talk started.</param>
    public static GhostResult Evaluate(IReadOnlyList<double> budgets, int slide, double onSlideSeconds, double elapsedSeconds, double toleranceSeconds = 15)
    {
        if (budgets.Count == 0 || slide < 1) return new GhostResult(0, 0, PaceState.Unknown);
        slide = Math.Min(slide, budgets.Count);
        double before = 0;
        for (int i = 0; i < slide - 1; i++) before += budgets[i];
        var thisBudget = budgets[slide - 1];
        double after = 0;
        for (int i = slide; i < budgets.Count; i++) after += budgets[i];

        // Where the ghost would be now: start of this slide plus as much of it as we've used (capped).
        var ghostElapsed = before + Math.Min(onSlideSeconds, thisBudget);
        // Overstaying this slide counts in full (the ghost has moved on).
        var delta = elapsedSeconds - ghostElapsed;

        // Pace ratio from the slides already delivered (needs some history to mean anything).
        var reached = elapsedSeconds - onSlideSeconds;
        var pace = before >= 30 && reached > 0 ? Math.Clamp(reached / before, 0.5, 2.0) : 1.0;
        var projected = elapsedSeconds + Math.Max(0, thisBudget - onSlideSeconds) * pace + after * pace;

        var state = Math.Abs(delta) <= toleranceSeconds ? PaceState.OnPace : delta > 0 ? PaceState.Behind : PaceState.Ahead;
        // Quantised to 5 s so the frame doesn't change every tick.
        return new GhostResult((long)Math.Round(delta / 5) * 5, (long)Math.Round(projected / 5) * 5, state);
    }
}

/// <summary>A snapshot of where the speaker is in their script.</summary>
public readonly record struct ScriptPosition(int Word, int TotalWords, double Percent, double WordsPerMinute, long? ProjectedRemainingSeconds);

/// <summary>
/// Follows a speaker through their script from recognised speech, the way a voice-following
/// teleprompter does — but for timing: percent delivered, live words per minute and a projected
/// finish. Robust to ad-libs (it searches a window ahead and only commits to strong matches) and
/// to recogniser errors (fuzzy word matching).
/// </summary>
public sealed class ScriptTracker
{
    private readonly string[] _script;
    private readonly Queue<(DateTime Utc, int Words)> _recent = new();
    private int _pos;
    private readonly List<string> _heard = new();

    public ScriptTracker(string script)
    {
        _script = Tokenize(script);
    }

    public int TotalWords => _script.Length;
    public int Position => _pos;

    /// <summary>Feed a recognised phrase. Returns the updated position.</summary>
    public ScriptPosition Feed(string phrase, DateTime utc)
    {
        var words = Tokenize(phrase);
        if (words.Length == 0) return Current(utc);
        _recent.Enqueue((utc, words.Length));
        _heard.AddRange(words);
        if (_heard.Count > 24) _heard.RemoveRange(0, _heard.Count - 24);
        Align();
        return Current(utc);
    }

    public ScriptPosition Current(DateTime utc)
    {
        while (_recent.Count > 0 && utc - _recent.Peek().Utc > TimeSpan.FromSeconds(90)) _recent.Dequeue();
        double wpm = 0;
        if (_recent.Count > 0)
        {
            var span = Math.Max(20, (utc - _recent.Peek().Utc).TotalSeconds);
            wpm = _recent.Sum(r => r.Words) * 60.0 / span;
        }
        var pct = _script.Length == 0 ? 0 : (double)_pos / _script.Length * 100;
        long? remaining = wpm >= 40 ? (long)Math.Round((_script.Length - _pos) / wpm * 60 / 5) * 5 : null;
        return new ScriptPosition(_pos, _script.Length, Math.Round(pct, 0), Math.Round(wpm / 5) * 5, remaining);
    }

    /// <summary>Find where the last few heard words best match the script, searching a window ahead.</summary>
    private void Align()
    {
        if (_script.Length == 0) return;
        var n = Math.Min(_heard.Count, 6);
        if (n < 3) return;
        var tail = _heard.Skip(_heard.Count - n).ToArray();
        var from = Math.Max(0, _pos - 10);
        var to = Math.Min(_script.Length - n, _pos + 250);
        var bestScore = 0;
        var bestEnd = -1;
        for (int start = from; start <= to; start++)
        {
            var score = 0;
            for (int k = 0; k < n; k++) if (Similar(_script[start + k], tail[k])) score++;
            // Prefer the earliest strong match (nearest to where we are).
            if (score > bestScore) { bestScore = score; bestEnd = start + n; }
        }
        // Commit only to a clear match: at least two-thirds of the window.
        if (bestEnd >= 0 && bestScore * 3 >= n * 2 && bestEnd > _pos - 10) _pos = Math.Max(_pos, bestEnd);
    }

    private static bool Similar(string a, string b)
    {
        if (a == b) return true;
        if (a.Length < 4 || b.Length < 4) return false;
        return Levenshtein(a, b) <= (a.Length >= 8 ? 2 : 1);
    }

    private static int Levenshtein(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2) return 3;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public static string[] Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) || ch == '\'' ? char.ToLowerInvariant(ch) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('\'')).Where(w => w.Length > 0).ToArray();
    }
}
