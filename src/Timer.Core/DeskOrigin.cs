namespace Stagewatch.Core;

/// <summary>
/// Who sends a line to another app, or decides a proposal: a role word (<see cref="Role"/>) and, for the desk's journal file alone, who
/// and from where (<see cref="Who"/>: a key's name, an address — never a secret), which no row's words and no proposal carry. A person's
/// roles are §11's (<see cref="ProposalDesk.Roles"/>); the rest name what is never a person: an agent on MCP, the advisor, a copilot
/// step, a proposal.
/// </summary>
public sealed record DeskOrigin(string Role, string Who = "")
{
    /// <summary>The operator at this desk: a person proved by being here (<see cref="Proved"/>).</summary>
    public static readonly DeskOrigin Operator = new("operator") { Proved = true };

    /// <summary>Why a send from what is not a person is refused.</summary>
    public const string PersonSends = "a person sends a line to another app — not the copilot, an agent or a proposal";

    /// <summary>Why a decision from what is not a person is refused.</summary>
    public const string PersonDecides = "a person approves — not a trigger, the copilot or an agent";

    /// <summary>An AI agent on MCP, the advisor, a copilot step and a proposal's own line: none is a person, so none sends or decides.</summary>
    public static readonly DeskOrigin Mcp = new("mcp"), Advisor = new("advisor"), Copilot = new("copilot"), Proposal = new("proposal");

    /// <summary>A command whose door named no sender (<see cref="ShowCommand.By"/> unset): nobody, so it sends and decides nothing.</summary>
    public static readonly DeskOrigin Nobody = new("nobody");

    /// <summary>
    /// A person on HTTP or a WebSocket (<paramref name="who"/>: the key's name, the address): <paramref name="proved"/> when the request gave
    /// the PIN or a control key, not when it came in on its address alone (no PIN set, this PC or a listed address).
    /// </summary>
    public static DeskOrigin Wire(string who = "", bool proved = false) => new("wire", who) { Proved = proved };

    /// <summary>
    /// A person is proved behind this sender: the operator at this desk, or a wire request that gave the PIN or a control key. A deck and an
    /// address alone prove nobody, so neither approves a window grant (Show Bus §12.4: a grant is a person's act).
    /// </summary>
    public bool Proved { get; init; }

    /// <summary>
    /// A person at a deck (Companion TCP, OSC; <paramref name="door"/>, its name): a door that proves no person, so a window grant it
    /// passes is asked of the operator (<see cref="Asks"/>), never made.
    /// </summary>
    public static DeskOrigin Deck(string who = "", string door = "") => new("deck", who) { Door = door };

    /// <summary>The door a deck's line came in by, as a proposal it asks names its from ("Companion TCP", "OSC"): never an address; "" for every other role.</summary>
    public string Door { get; init; } = "";

    /// <summary>
    /// A window grant from this sender is asked of the operator, never made (Show Bus §12.4): a deck proves no person, so the host queues
    /// the grant on the desk for a person to approve.
    /// </summary>
    public bool Asks => Role == "deck";

    /// <summary>A command trigger the operator set on this PC: it may send a line, never decide a proposal.</summary>
    public static DeskOrigin Trigger(string who = "") => new("trigger", who);

    /// <summary>A person may send a line to another app as this role.</summary>
    public bool Sends => ProposalDesk.Roles.Contains(Role, StringComparer.Ordinal);

    /// <summary>A person may approve or reject as this role: every sending role but a trigger, which no person is behind when it fires.</summary>
    public bool Decides => Sends && Role != "trigger";

    /// <summary>Who and from where as plain words ("key 'FOH tablet', 10.0.0.7"), or "": what a row keeps apart from its words (<see cref="DeskRow.Who"/>).</summary>
    public string Named => LineFence.Shown(Who, 120);
}
