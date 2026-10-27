using System.Text;

namespace Stagewatch.Core;

/// <summary>
/// A word of a line as Patterns' and PlayMap's line wires read it: <see cref="Text"/> is the word (a quoted phrase's text, each
/// backslash in it taking the mark after it as it stands), <see cref="Raw"/> the word as the line spells it.
/// </summary>
public readonly record struct LineWord(string Text, string Raw)
{
    /// <summary>A word that opens with "{" or "[": JSON, which runs to the end of the line.</summary>
    public bool Json => Raw.Length > 0 && Raw[0] is '{' or '[';

    /// <summary>A quoted phrase.</summary>
    public bool Quoted => Raw.Length > 0 && Raw[0] == '"';
}

/// <summary>
/// One command a peer's context lists (Show Bus §6 "commands"): its form in §6's notation, its risk (low, review or high), what it
/// does in words, and the word its secret starts at (§6's optional <c>"secret": n</c>, the line's first word counted as word 0, as
/// PlayMap's SecretFrom counts), or null.
/// </summary>
public readonly record struct PeerCommand(string Verb, string Risk, string Words = "", int? SecretFrom = null);

/// <summary>
/// How a line rates against a node's table (Show Bus §11): its <see cref="Risk"/>; the <see cref="Form"/> that decided it and that
/// row's <see cref="Words"/>, when a form of the table takes the line (<see cref="Listed"/>); and whether the line fills a secret
/// placeholder with anything but <see cref="VerbSyntax.SecretMarker"/> (<see cref="Secret"/>). A line no form takes is high.
/// </summary>
public sealed record RiskVerdict(string Risk, string? Form, string Words, bool Listed, bool Secret)
{
    /// <summary>A line no verified form takes: high, and not listed.</summary>
    public static readonly RiskVerdict Unlisted = new("high", null, "", false, false);
}

/// <summary>
/// Show Bus §6's command notation, read as the line wires read a line. A literal word matches whatever its case; <c>A|B</c> is one
/// word, either of those; <c>[…]</c> is optional and nests; a bare <c>…</c> is any further words. A top-level <c> | </c> separates whole
/// forms, and a form that does not start with the verb's first word carries on from the first form's leading literal words, all but
/// the last. A <c>&lt;…&gt;</c> value is exactly one word, but a form's last element and a value named <c>words</c>, <c>text</c> or
/// <c>line</c>, or ending in <c> words</c> or <c> line</c>, each take one word or more. A row whose brackets do not close has no form,
/// so no line rates by it. Pure.
/// </summary>
public static class VerbSyntax
{
    /// <summary>A line of more words than this rates high: no form is tried.</summary>
    public const int MaxWords = 64;

    /// <summary>Lines carried inside lines are read this deep; one carried deeper rates high.</summary>
    public const int MaxDepth = 4;

    /// <summary>What a proposer sends in place of a secret it keeps (§6, §11): the only filling a secret placeholder may have.</summary>
    public const string SecretMarker = "‹secret›";

    /// <summary>The whole names, in any case, of a value that is a secret placeholder (§6). <c>pin</c> is not one: it is PlayMap's calibration pin.</summary>
    public static readonly IReadOnlyList<string> SecretNames = ["token", "key", "password", "passcode", "secret"];

    /// <summary>
    /// A line's words as the line wires read them: a quoted phrase is one word, a backslash in it taking the mark after it as it
    /// stands; a word that opens with "{" or "[" runs to the end of the line (JSON); any other word runs to the next white space.
    /// Null when a quote does not close: no wire reads such a line.
    /// </summary>
    public static IReadOnlyList<LineWord>? Words(string line)
    {
        var words = new List<LineWord>();
        var i = 0;
        while (true)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) return words;
            var start = i;
            if (line[i] == '"')
            {
                var text = new StringBuilder();
                var closed = false;
                for (i++; i < line.Length; i++)
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        text.Append(line[++i]);
                        continue;
                    }
                    if (line[i] == '"')
                    {
                        closed = true;
                        i++;
                        break;
                    }
                    text.Append(line[i]);
                }
                if (!closed) return null;
                words.Add(new LineWord(text.ToString(), line[start..i]));
            }
            else if (line[i] is '{' or '[')
            {
                words.Add(new LineWord(line[i..], line[i..]));
                return words;
            }
            else
            {
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                words.Add(new LineWord(line[start..i], line[start..i]));
            }
        }
    }

    /// <summary>
    /// A row's whole forms: its verb split at each top-level <c> | </c> (outside <c>&lt;…&gt;</c> and <c>[…]</c>), a form that does
    /// not start with the first form's first word carrying on from the first form's leading literal words, all but the last
    /// ("SEND ENABLE &lt;send&gt; ON|OFF" stays one form; "start | pause" is two). None when an opened bracket never closes.
    /// </summary>
    public static IReadOnlyList<string> Forms(string verb)
    {
        if (!Closes(verb)) return [];
        var parts = SplitForms(verb).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return [];
        var first = Tokens(parts[0]);
        var lead = first.TakeWhile(IsLiteral).ToList();
        var forms = new List<string>(parts.Count) { parts[0] };
        foreach (var part in parts.Skip(1))
        {
            var tokens = Tokens(part);
            var own = tokens.Count > 0 && first.Count > 0 && string.Equals(tokens[0], first[0], StringComparison.OrdinalIgnoreCase);
            forms.Add(own || lead.Count < 2 ? part : string.Join(' ', lead.Take(lead.Count - 1).Concat(tokens)));
        }
        return forms;
    }

    /// <summary>
    /// A line a form takes, for a person to read its shape: its literal words (an alternative's first in ordinal order), each value
    /// <c>x1</c> — but a value that holds a line <paramref name="line"/> when one is given — each optional part taken when
    /// <paramref name="optional"/> is true and left out otherwise, a bare <c>…</c> as nothing.
    /// </summary>
    public static string Example(string form, bool optional, string? line = null)
    {
        var words = new List<string>();
        void Walk(IEnumerable<FormNode> nodes)
        {
            foreach (var node in nodes)
                switch (node)
                {
                    case LiteralNode lit: words.Add(lit.Words.Order(StringComparer.Ordinal).First()); break;
                    case ValueNode value: words.Add(value.Slot == Slot.Line && line is not null ? line : "x1"); break;
                    case OptionNode opt when optional: Walk(opt.Body); break;
                }
        }
        Walk(Compile(form));
        return string.Join(' ', words);
    }

    /// <summary>A form's nodes, in order.</summary>
    internal static FormNode[] Compile(string form) => CompileTokens(Tokens(form), tail: true);

    /// <summary>The names of a token's values: the text inside each top-level <c>&lt;…&gt;</c>, trimmed and in lower case.</summary>
    internal static IReadOnlyList<string> Names(string token)
    {
        var names = new List<string>();
        var depth = 0;
        StringBuilder? name = null;
        foreach (var c in token)
        {
            if (c == '<' && depth++ == 0)
            {
                name = new StringBuilder();
                continue;
            }
            if (c == '>' && depth > 0 && --depth == 0)
            {
                names.Add(name!.ToString().Trim().ToLowerInvariant());
                name = null;
                continue;
            }
            name?.Append(c);
        }
        return names;
    }

    private static FormNode[] CompileTokens(IReadOnlyList<string> tokens, bool tail)
    {
        var nodes = new FormNode[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            var last = tail && i == tokens.Count - 1;
            if (t is "…" or "...") nodes[i] = AnyNode.Words;
            else if (t[0] == '[' && Close(t) == t.Length - 1) nodes[i] = new OptionNode(CompileTokens(Tokens(t[1..^1]), last));
            else if (t.Contains('<') || t.Contains('"') || t.Split('|').Any(a => a.Length == 0)) nodes[i] = Value(t, last);
            else nodes[i] = new LiteralNode(t.Split('|').Select(a => a.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal));
        }
        return nodes;
    }

    private static ValueNode Value(string token, bool last)
    {
        var names = Names(token);
        var slot = names.Any(n => SecretNames.Contains(n, StringComparer.Ordinal)) ? Slot.Secret
            : names.Any(n => n == "line" || n.EndsWith(" line", StringComparison.Ordinal)) ? Slot.Line
            : names.Any(n => n is "words" or "text" || n.EndsWith(" words", StringComparison.Ordinal)) ? Slot.Words
            : Slot.Plain;
        return new ValueNode(slot, last || slot is Slot.Line or Slot.Words);
    }

    /// <summary>A form's tokens: split at white space outside <c>&lt;…&gt;</c> and <c>[…]</c>.</summary>
    private static List<string> Tokens(string form)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        int angle = 0, square = 0;
        foreach (var c in form)
        {
            Track(c, ref angle, ref square);
            if (char.IsWhiteSpace(c) && angle == 0 && square == 0)
            {
                if (token.Length > 0) tokens.Add(token.ToString());
                token.Clear();
                continue;
            }
            token.Append(c);
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }

    /// <summary>The verb split at each " | " outside <c>&lt;…&gt;</c> and <c>[…]</c>.</summary>
    private static List<string> SplitForms(string verb)
    {
        var parts = new List<string>();
        int angle = 0, square = 0, start = 0;
        for (var i = 0; i < verb.Length; i++)
        {
            Track(verb[i], ref angle, ref square);
            if (verb[i] == '|' && angle == 0 && square == 0 && i > 0 && verb[i - 1] == ' ' && i + 1 < verb.Length && verb[i + 1] == ' ')
            {
                parts.Add(verb[start..i]);
                start = i + 1;
            }
        }
        parts.Add(verb[start..]);
        return parts;
    }

    /// <summary>
    /// Depth inside <c>&lt;…&gt;</c>, and inside <c>[…]</c> outside one: a bracket inside a value belongs to the value ("&lt;host[:port]&gt;").
    /// A closing mark with nothing open is a plain mark.
    /// </summary>
    private static void Track(char c, ref int angle, ref int square)
    {
        if (c == '<') angle++;
        else if (c == '>' && angle > 0) angle--;
        else if (angle == 0 && c == '[') square++;
        else if (angle == 0 && c == ']' && square > 0) square--;
    }

    /// <summary>Whether every bracket the verb opens it closes.</summary>
    private static bool Closes(string verb)
    {
        int angle = 0, square = 0;
        foreach (var c in verb) Track(c, ref angle, ref square);
        return angle == 0 && square == 0;
    }

    /// <summary>Where the "[" a token opens with closes, or -1.</summary>
    private static int Close(string token)
    {
        int angle = 0, square = 0;
        for (var i = 0; i < token.Length; i++)
        {
            Track(token[i], ref angle, ref square);
            if (square == 0 && angle == 0 && i > 0) return i;
        }
        return -1;
    }

    /// <summary>A literal word of a form's lead: not a value, an option, alternatives or "…".</summary>
    private static bool IsLiteral(string token)
        => !(token.Contains('<') || token.Contains('[') || token.Contains('|') || token.Contains('"') || token is "…" or "...");
}

/// <summary>What a value of a form holds: one word or more of plain text, a secret, words, or a line of the same node.</summary>
internal enum Slot { Plain, Secret, Words, Line }

internal abstract class FormNode;

/// <summary>A literal word: one of <see cref="Words"/>, in upper case.</summary>
internal sealed class LiteralNode(HashSet<string> words) : FormNode
{
    public HashSet<string> Words { get; } = words;
}

/// <summary>A value: one word, or one word or more when <see cref="Many"/>.</summary>
internal sealed class ValueNode(Slot slot, bool many) : FormNode
{
    public Slot Slot { get; } = slot;
    public bool Many { get; } = many;
}

/// <summary>A bare <c>…</c>: any further words, none included.</summary>
internal sealed class AnyNode : FormNode
{
    public static readonly AnyNode Words = new();
}

/// <summary>An optional part: <see cref="Body"/> or nothing.</summary>
internal sealed class OptionNode(FormNode[] body) : FormNode
{
    public FormNode[] Body { get; } = body;
}

/// <summary>
/// A node's commands compiled (Show Bus §6, §11): at most <see cref="PeerContexts.MaxCommands"/> rows, each verb whole within
/// <see cref="PeerContexts.MaxVerbChars"/> and with a form, or dropped and counted, as a context is kept. A line rates by the most
/// specific whole form that takes it — the most literal words, then the most words that are one literal rather than alternatives —
/// and an exact tie rates by the higher risk. A line no form takes, one no wire reads, or one of more than
/// <see cref="VerbSyntax.MaxWords"/> words is high. A value that holds a line of the same node raises the risk to that line's, read
/// <see cref="VerbSyntax.MaxDepth"/> lines deep and high deeper; a carried line no form takes is high, as it is on its own, since the
/// node may run it by rules no table states (another name for a verb, a verb named by its first words alone). A line whose carried
/// lines number more than <see cref="MaxCarried"/>, at every depth together, is high, as PlayMap reads it; and a row that carries lines
/// rates a line high when JSON fills one of its values, which the desk does not read. Every rating is bounded: a form is matched as a
/// flat sequence of steps, each place in it against each word once, and a rating that takes more than <see cref="MaxSteps"/> steps is
/// high and unlisted. Immutable and thread-safe.
/// </summary>
public sealed class VerbTable
{
    /// <summary>The most carried lines one rating reads, at every depth together (PlayMap's own count); one more and the line is high.</summary>
    public const int MaxCarried = 256;

    /// <summary>The most steps one rating takes, its carried lines included; a line that needs more is high and unlisted.</summary>
    public const int MaxSteps = 200_000;

    private enum Op : byte { Literal, Value, Any, Option }

    /// <summary>
    /// One place of a form laid flat: a literal (one of <see cref="Words"/>), a value (<see cref="Many"/> words or one), a bare "…", or
    /// an optional part, whose body follows it and whose <see cref="Skip"/> is the place after that body.
    /// </summary>
    private readonly record struct Step(Op Op, HashSet<string>? Words, Slot Slot, bool Many, int Skip);

    private sealed record CompiledForm(string Text, Step[] Steps, bool Json);

    private sealed record Row(PeerCommand Command, CompiledForm[] Forms, bool CarriesLines);

    private readonly Row[] _rows;

    private VerbTable(Row[] rows, int dropped)
    {
        _rows = rows;
        Dropped = dropped;
    }

    /// <summary>A table with no rows: every line is high.</summary>
    public static readonly VerbTable Empty = new([], 0);

    /// <summary>
    /// The rows not kept: a verb that is empty, over <see cref="PeerContexts.MaxVerbChars"/> or has no form (a bracket it opens never
    /// closes), or one past the <see cref="PeerContexts.MaxCommands"/>th kept.
    /// </summary>
    public int Dropped { get; }

    /// <summary>The rows kept, in the order sent, each risk read as low, review or high (anything else high).</summary>
    public IReadOnlyList<PeerCommand> Commands => _rows.Select(r => r.Command).ToList();

    /// <summary>The table of <paramref name="commands"/>, in their order.</summary>
    public static VerbTable Compile(IEnumerable<PeerCommand> commands)
    {
        var rows = new List<Row>();
        var dropped = 0;
        foreach (var c in commands)
        {
            var forms = string.IsNullOrEmpty(c.Verb) || c.Verb.Length > PeerContexts.MaxVerbChars || rows.Count >= PeerContexts.MaxCommands
                ? [] : VerbSyntax.Forms(c.Verb);
            if (forms.Count == 0)
            {
                dropped++;
                continue;
            }
            var compiled = forms.Select(f => new CompiledForm(f, Flatten(VerbSyntax.Compile(f)), VerbSyntax.Names(f).Contains("json", StringComparer.Ordinal))).ToArray();
            rows.Add(new Row(c with { Risk = Normal(c.Risk), Words = c.Words ?? "" }, compiled, compiled.Any(f => f.Steps.Any(s => s is { Op: Op.Value, Slot: Slot.Line }))));
        }
        return new VerbTable([.. rows], dropped);
    }

    /// <summary>The table of verbs and risks alone.</summary>
    public static VerbTable Compile(IEnumerable<(string Verb, string Risk)> commands) => Compile(commands.Select(c => new PeerCommand(c.Verb, c.Risk)));

    /// <summary>How <paramref name="line"/> rates by this table.</summary>
    public RiskVerdict Rate(string line) => Measure(line).Verdict;

    /// <summary>
    /// How <paramref name="line"/> rates by this table, with the steps the rating took — never more than <see cref="MaxSteps"/> and the
    /// last step's own few, a rating that would take more being high and unlisted — and the carried lines it read at every depth
    /// together, counted once more than <see cref="MaxCarried"/> at most.
    /// </summary>
    public (RiskVerdict Verdict, int Steps, int Carried) Measure(string line)
    {
        var run = new Run();
        var (verdict, carried) = Rate(line, 0, run);
        if (run.Out) return (RiskVerdict.Unlisted, run.Steps, carried);
        return (carried > MaxCarried ? verdict with { Risk = "high" } : verdict, run.Steps, carried);
    }

    /// <summary>"low", "review" or "high": anything else, none at all included, is high, as an unknown line is (§11).</summary>
    public static string Normal(string? risk) => (risk ?? "").Trim().ToLowerInvariant() switch
    {
        "low" => "low",
        "review" => "review",
        _ => "high",
    };

    /// <summary>0 for low, 1 for review, 2 for high (and anything else).</summary>
    public static int Rank(string risk) => Normal(risk) switch { "low" => 0, "review" => 1, _ => 2 };

    /// <summary>
    /// One rating's bounds: the steps it has taken, and each line it rated at each depth with the carried lines that rating read, so a
    /// line carried twice is rated once and counted each time.
    /// </summary>
    private sealed class Run
    {
        public int Steps { get; private set; }

        public bool Out => Steps > MaxSteps;

        public readonly Dictionary<(string Line, int Depth), (RiskVerdict Verdict, int Carried)> Rated = [];

        /// <summary>Takes <paramref name="n"/> steps: false once the rating is past its bound.</summary>
        public bool Spend(int n)
        {
            Steps += n;
            return Steps <= MaxSteps;
        }
    }

    private (RiskVerdict Verdict, int Carried) Rate(string line, int depth, Run run)
    {
        if (run.Rated.TryGetValue((line, depth), out var known)) return known;
        var rated = RateOnce(line, depth, run);
        run.Rated[(line, depth)] = rated;
        return rated;
    }

    /// <summary>A line's verdict, and how many carried lines its rating read at every depth below it (past <see cref="MaxCarried"/>, one more).</summary>
    private (RiskVerdict Verdict, int Carried) RateOnce(string line, int depth, Run run)
    {
        if (!run.Spend(1 + line.Length / 16) || VerbSyntax.Words(line) is not { Count: > 0 and <= VerbSyntax.MaxWords } words) return (RiskVerdict.Unlisted, 0);
        var upper = words.Select(w => w.Text.ToUpperInvariant()).ToArray();
        var matcher = new Matcher(words, upper, run);
        Found? best = null;
        var tied = new List<(Row Row, CompiledForm Form, Found Found)>();
        foreach (var row in _rows)
            foreach (var form in row.Forms)
            {
                var found = matcher.Match(form.Steps);
                if (run.Out) return (RiskVerdict.Unlisted, 0);
                if (found is not { Lits: > 0 }) continue;
                var order = best is null ? 1 : found.CompareTo(best);
                if (order > 0)
                {
                    best = found;
                    tied.Clear();
                }
                if (order >= 0) tied.Add((row, form, found));
            }
        if (tied.Count == 0) return (RiskVerdict.Unlisted, 0);

        var json = words.Any(w => w.Json);
        (string Risk, string Form, string Words)? pick = null;
        var secret = false;
        var carried = 0;
        foreach (var (row, form, found) in tied)
        {
            var risk = row.Command.Risk;
            secret |= found.Secret || SecretFrom(row.Command, words);
            if (row.CarriesLines && (json || form.Json)) risk = "high";
            foreach (var (from, to) in found.Carried)
            {
                carried = Math.Min(carried + 1, MaxCarried + 1);
                if (carried > MaxCarried || depth >= VerbSyntax.MaxDepth || !run.Spend(to - from))
                {
                    risk = "high";
                    break;
                }
                var (inner, below) = Rate(Line(words, from, to), depth + 1, run);
                if (run.Out) return (RiskVerdict.Unlisted, 0);
                carried = Math.Min(carried + below, MaxCarried + 1);
                secret |= inner.Secret;
                // A carried line no form takes is high, as on its own: the node may run it by a rule its table does not state.
                risk = !inner.Listed || Rank(inner.Risk) > Rank(risk) ? inner.Risk : risk;
            }
            if (pick is null || Rank(risk) > Rank(pick.Value.Risk)) pick = (risk, form.Text, row.Command.Words);
        }
        return (new RiskVerdict(pick!.Value.Risk, pick.Value.Form, pick.Value.Words, true, secret), carried);
    }

    /// <summary>The line a line value holds: a quoted phrase's text, else its words as the line spells them.</summary>
    private static string Line(IReadOnlyList<LineWord> words, int from, int to)
        => to - from == 1 && words[from].Quoted ? words[from].Text : string.Join(' ', words.Skip(from).Take(to - from).Select(w => w.Raw));

    /// <summary>Whether a row's <c>"secret": n</c> finds a word from the n-th on that is not the marker.</summary>
    private static bool SecretFrom(PeerCommand command, IReadOnlyList<LineWord> words)
        => command.SecretFrom is int from && words.Skip(Math.Max(0, from)).Any(w => w.Text != VerbSyntax.SecretMarker);

    /// <summary>A form's nodes laid flat, in order: an optional part is a step that either enters its body or skips past it.</summary>
    private static Step[] Flatten(FormNode[] nodes)
    {
        var steps = new List<Step>();
        Lay(nodes);
        return [.. steps];

        void Lay(FormNode[] sequence)
        {
            foreach (var node in sequence)
                switch (node)
                {
                    case LiteralNode lit: steps.Add(new Step(Op.Literal, lit.Words, Slot.Plain, false, 0)); break;
                    case ValueNode value: steps.Add(new Step(Op.Value, null, value.Slot, value.Many, 0)); break;
                    case AnyNode: steps.Add(new Step(Op.Any, null, Slot.Plain, true, 0)); break;
                    case OptionNode opt:
                        var at = steps.Count;
                        steps.Add(default);
                        Lay(opt.Body);
                        steps[at] = new Step(Op.Option, null, Slot.Plain, false, steps.Count);
                        break;
                }
        }
    }

    /// <summary>
    /// One way a form takes a line's words: its literal words, those that are one literal, whether it fills a secret with anything
    /// but the marker, and the spans of the line its line values hold. Two ways that tie keep both's secret and spans, so a tie never
    /// hides either.
    /// </summary>
    private sealed class Found(int lits, int singles, bool secret, (int From, int To)[] carried)
    {
        public static readonly Found End = new(0, 0, false, []);

        public int Lits { get; } = lits;
        public int Singles { get; } = singles;
        public bool Secret { get; } = secret;
        public (int From, int To)[] Carried { get; } = carried;

        public int CompareTo(Found other) => Lits != other.Lits ? Lits.CompareTo(other.Lits) : Singles.CompareTo(other.Singles);

        public Found Literal(bool single) => new(Lits + 1, Singles + (single ? 1 : 0), Secret, Carried);

        public Found Filled(bool secret, (int, int)? line)
            => !secret && line is null ? this : new(Lits, Singles, Secret || secret, line is { } span ? [span, .. Carried] : Carried);
    }

    /// <summary>
    /// The forms of a table against one line, one form at a time: every place of the form against every word once (a form of n steps
    /// and a line of w words is at most n × (w + 1) places, each trying at most w + 1 ends), the best way kept for each; every step taken
    /// counted against the rating's bound.
    /// </summary>
    private sealed class Matcher(IReadOnlyList<LineWord> words, string[] upper, Run run)
    {
        private Step[] _steps = [];
        private readonly Dictionary<int, Found?> _memo = [];

        public Found? Match(Step[] steps)
        {
            if (!run.Spend(1) || steps.Length == 0) return null;
            // A form whose first word is a literal the line's first word is not takes the line nowhere.
            if (steps[0] is { Op: Op.Literal, Words: { } first } && !first.Contains(upper[0])) return null;
            _steps = steps;
            _memo.Clear();
            return Go(0, 0);
        }

        private Found? Go(int at, int word)
        {
            if (at == _steps.Length) return word == words.Count ? Found.End : null;
            var place = at * (words.Count + 1) + word;
            if (_memo.TryGetValue(place, out var known)) return known;
            Found? best = null;
            if (run.Spend(1))
            {
                var step = _steps[at];
                switch (step.Op)
                {
                    case Op.Literal:
                        if (word < words.Count && step.Words!.Contains(upper[word])) best = Go(at + 1, word + 1)?.Literal(step.Words.Count == 1);
                        break;
                    case Op.Value:
                        var most = step.Many ? words.Count : Math.Min(word + 1, words.Count);
                        for (var end = word + 1; end <= most && run.Spend(1); end++)
                            if (Go(at + 1, end) is { } rest)
                                best = Better(best, rest.Filled(step.Slot == Slot.Secret && !(end == word + 1 && words[word].Text == VerbSyntax.SecretMarker),
                                    step.Slot == Slot.Line && run.Spend(rest.Carried.Length) ? (word, end) : null));
                        break;
                    case Op.Any:
                        for (var end = word; end <= words.Count && run.Spend(1); end++) best = Better(best, Go(at + 1, end));
                        break;
                    case Op.Option:
                        best = Better(Go(at + 1, word), Go(step.Skip, word));
                        break;
                }
            }
            _memo[place] = best;
            return best;
        }

        /// <summary>The better of two ways, or both merged when they tie.</summary>
        private Found? Better(Found? best, Found? found)
        {
            if (found is null) return best;
            if (best is null) return found;
            var order = found.CompareTo(best);
            if (order != 0) return order > 0 ? found : best;
            if (!run.Spend(best.Carried.Length + found.Carried.Length)) return best;
            return new Found(best.Lits, best.Singles, best.Secret || found.Secret, [.. best.Carried.Union(found.Carried)]);
        }
    }
}
