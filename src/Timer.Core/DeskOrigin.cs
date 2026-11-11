namespace Stagewatch.Core;

/// <summary>
/// Who sends a line to another app, or decides a proposal: a role word (<see cref="Role"/>) and, for the desk's journal file alone, who
/// and from where (<see cref="Who"/>: a key's name, an address — never a secret), which no row's words and no proposal carry. A person's
/// roles are §11's (<see cref="ProposalDesk.Roles"/>); the rest name what is never a person: an agent on MCP, the advisor, a copilot
/// step, a proposal.
/// </summary>
public sealed record DeskOrigin(string Role, string Who = "")
{
    /// <summary>The operator at this desk.</summary>
    public static readonly DeskOrigin Operator = new("operator");

    /// <summary>Why a send from what is not a person is refused.</summary>
    public const string PersonSends = "a person sends a line to another app — not the copilot, an agent or a proposal";

    /// <summary>Why a decision from what is not a person is refused.</summary>
    public const string PersonDecides = "a person approves — not a trigger, the copilot or an agent";

    /// <summary>An AI agent on MCP, the advisor, a copilot step and a proposal's own line: none is a person, so none sends or decides.</summary>
    public static readonly DeskOrigin Mcp = new("mcp"), Advisor = new("advisor"), Copilot = new("copilot"), Proposal = new("proposal");

    /// <summary>A command whose door named no sender (<see cref="ShowCommand.By"/> unset): nobody, so it sends and decides nothing.</summary>
    public static readonly DeskOrigin Nobody = new("nobody");

    /// <summary>A person on HTTP or a WebSocket, with the PIN or a control key (<paramref name="who"/>: the key's name, the address).</summary>
    public static DeskOrigin Wire(string who = "") => new("wire", who);

    /// <summary>A person at a deck (Companion, OSC).</summary>
    public static DeskOrigin Deck(string who = "") => new("deck", who);

    /// <summary>A command trigger the operator set on this PC: it may send a line, never decide a proposal.</summary>
    public static DeskOrigin Trigger(string who = "") => new("trigger", who);

    /// <summary>A person may send a line to another app as this role.</summary>
    public bool Sends => ProposalDesk.Roles.Contains(Role, StringComparer.Ordinal);

    /// <summary>A person may approve or reject as this role: every sending role but a trigger, which no person is behind when it fires.</summary>
    public bool Decides => Sends && Role != "trigger";

    /// <summary>Who and from where as plain words ("key 'FOH tablet', 10.0.0.7"), or "": what a row keeps apart from its words (<see cref="DeskRow.Who"/>).</summary>
    public string Named => LineFence.Shown(Who, 120);
}
