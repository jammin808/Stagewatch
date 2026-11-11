namespace Stagewatch.Core;

/// <summary>How much a line needs a person's eye before it runs (Show Bus §11's three words: low, review, high).</summary>
public enum LineRisk
{
    /// <summary>Changes nothing anyone sees (a note, finding a screen, an extra timer).</summary>
    Low,
    /// <summary>Changes timing or sends words to someone: read before running.</summary>
    Review,
    /// <summary>Changes what the speaker, crew or audience sees right now, or what another node's air shows: a person chooses it.</summary>
    High,
}

/// <summary>
/// What one of this Stagewatch's own commands would do to the room: the one table every reader of a command's risk takes — the desk
/// rating a proposal aimed at itself, the copilot ticking a step, an agent's proposal. Decided here by the command, never by a model
/// and never by the words around it. Pure.
/// </summary>
public static class CommandRisk
{
    /// <summary>
    /// High when the command changes what the speaker, crew or audience sees right now (covering a screen, even briefly, included) or
    /// drives another node's air; low when it changes nothing anyone sees; everything else (timing, words to someone) is review.
    /// </summary>
    public static LineRisk Of(ShowCommand c)
    {
        var text = c.Text ?? "";
        return c.Kind switch
        {
            ShowCommandKind.Note => LineRisk.Low,
            ShowCommandKind.Undo or ShowCommandKind.Loop or ShowCommandKind.Patterns => LineRisk.High,
            // Every playout action is high: what it does is on another node's air, which nothing here can see (a PlayMap cue shows
            // its first frame at once, a Patterns cue moves that desk's own standby).
            ShowCommandKind.Playout => LineRisk.High,
            // Trusting a certificate decides who may answer as a peer from then on: a person's, and refused from every line anyway.
            ShowCommandKind.PlayoutTrust => LineRisk.High,
            ShowCommandKind.Screen when text.StartsWith("save", StringComparison.Ordinal) => LineRisk.Low,
            ShowCommandKind.Screen when text.StartsWith("name", StringComparison.Ordinal) => LineRisk.Low,
            // A window's name, its link-loss choice and its fade change nothing on screen now; everything else does.
            ShowCommandKind.Window => c.Get<WindowArgs>().Action is "new" or "rename" or "loss" or "fade" ? LineRisk.Low : LineRisk.High,
            // An extra timer is invisible until it's on stage; putting it on (or taking it off) changes the speaker's screen.
            ShowCommandKind.Aux when text.StartsWith("stage", StringComparison.Ordinal) || text.StartsWith("unstage", StringComparison.Ordinal) => LineRisk.High,
            ShowCommandKind.Aux => LineRisk.Review,
            ShowCommandKind.Start or ShowCommandKind.Pause or ShowCommandKind.Toggle or ShowCommandKind.Reset
                or ShowCommandKind.Next or ShowCommandKind.Previous or ShowCommandKind.GoTo or ShowCommandKind.Preset
                or ShowCommandKind.Blackout or ShowCommandKind.Mode or ShowCommandKind.Screen or ShowCommandKind.Style
                or ShowCommandKind.Holding or ShowCommandKind.Ndi or ShowCommandKind.Output or ShowCommandKind.Ltc
                or ShowCommandKind.Preview or ShowCommandKind.Roll or ShowCommandKind.Rundown
                or ShowCommandKind.SignalGoAll => LineRisk.High,
            _ => LineRisk.Review,
        };
    }

    /// <summary>A risk as §11 writes it: <c>low</c>, <c>review</c> or <c>high</c>.</summary>
    public static string Word(LineRisk risk) => risk switch { LineRisk.Low => "low", LineRisk.Review => "review", _ => "high" };

    /// <summary>§11's word as a risk; a word that is none of the three is high (unknown is never lower).</summary>
    public static LineRisk Parse(string? word) => VerbTable.Normal(word) switch { "low" => LineRisk.Low, "review" => LineRisk.Review, _ => LineRisk.High };

    /// <summary>
    /// How a line in this Stagewatch's own grammar rates: its command's risk, listed by no table (the desk rates its own lines by its
    /// own rules); a line its parser does not take is high and unlisted.
    /// </summary>
    public static RiskVerdict OfLine(string line)
        => ShowCommandParser.TryParseLine(line, out var cmd, out _) ? new RiskVerdict(Word(Of(cmd)), null, "", true, false) : RiskVerdict.Unlisted;
}
