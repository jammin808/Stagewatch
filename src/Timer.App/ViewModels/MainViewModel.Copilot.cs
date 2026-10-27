using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;
using Stagewatch.Remote;
using Stagewatch.Show;

namespace Stagewatch.App.ViewModels;

public enum ProposalState { Thinking, Pending, Done, Discarded }

/// <summary>One step of a proposal: the command, why, how risky — and whether the operator ticked it.</summary>
public sealed partial class CopilotStepItem : ObservableObject
{
    public CopilotStepItem(CopilotStep step)
    {
        Command = step.Command;
        Why = step.Why;
        Risk = step.Risk;
        _selected = step.Risk != CopilotRisk.High;   // anything that changes the room right now is opt-in
    }

    public string Command { get; }
    public string Why { get; }
    public CopilotRisk Risk { get; }
    public string RiskLabel => Risk switch { CopilotRisk.High => "CHANGES THE ROOM", CopilotRisk.Review => "REVIEW", _ => "SAFE" };
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string _result = "";
}

/// <summary>A proposal from the copilot, the assistant or an AI agent (MCP). Nothing in it runs until approved.</summary>
public sealed partial class CopilotProposal : ObservableObject
{
    private static int _next;
    public int Id { get; } = Interlocked.Increment(ref _next);
    public required string Source { get; init; }
    public required string Request { get; init; }
    /// <summary>Another app's proposal on the shared queue (Show Bus §11), by its desk id; null for Stagewatch's own.</summary>
    public string? SharedId { get; init; }
    public DateTime Created { get; } = DateTime.Now;
    public string CreatedText => Created.ToString("HH:mm");
    public ObservableCollection<CopilotStepItem> Steps { get; } = new();

    [ObservableProperty] private string _reply = "";
    /// <summary>What the plan's timing steps would do to the show (simulated before anything runs).</summary>
    [ObservableProperty] private string _consequence = "";
    [ObservableProperty] private string _question = "";
    [ObservableProperty] private string _dropped = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsThinking), nameof(StateText))] private ProposalState _state;

    public bool IsPending => State == ProposalState.Pending && Steps.Count > 0;
    public bool IsThinking => State == ProposalState.Thinking;
    public string StateText => State switch
    {
        ProposalState.Thinking => "thinking…",
        ProposalState.Done => "approved",
        ProposalState.Discarded => "discarded",
        _ => Steps.Count == 0 ? "" : "waiting for you",
    };

    internal void RaisePending() => OnPropertyChanged(nameof(IsPending));
}

/// <summary>
/// The AI copilot, human in the loop: plain English → a plan in Stagewatch's own commands → the operator ticks what
/// runs. AI agents on MCP land in the same queue (unless the operator lets them act directly). Every request,
/// approval and refusal goes in the show journal, so the report shows what the AI suggested and who decided.
/// </summary>
public sealed partial class MainViewModel
{
    private Copilot? _copilot;
    private string _copilotKeyHash = "";
    private CancellationTokenSource? _copilotCts;

    public ObservableCollection<CopilotProposal> Proposals { get; } = new();

    [ObservableProperty] private string _copilotInput = "";
    [ObservableProperty] private bool _copilotBusy;
    [ObservableProperty] private string _copilotStatus = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPendingProposals))] private int _pendingProposals;
    public bool HasPendingProposals => PendingProposals > 0;

    /// <summary>Why the copilot can't run right now, or null when it can.</summary>
    public string? CopilotUnavailable
    {
        get
        {
            if (!FeatureGate.AllowsAssistant(_license.Tier)) return FeatureGate.Needs(FeatureTier.Production);
            var i = Settings.Integrations;
            if (!i.AssistantCloud || ProtectedKey.Unprotect(i.AssistantKeyProtected).Length == 0)
                return "Turn on Settings → Integrations → Producer assistant → Also ask Claude, and add an API key.";
            return null;
        }
    }

    private Copilot? CopilotClient()
    {
        if (CopilotUnavailable is not null) return null;
        var key = ProtectedKey.Unprotect(Settings.Integrations.AssistantKeyProtected);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        if (_copilot is null || hash != _copilotKeyHash) { _copilot = new Copilot(key); _copilotKeyHash = hash; }
        return _copilot;
    }

    [RelayCommand]
    private Task AskCopilot(string? text) => RunCopilot((text ?? CopilotInput).Trim(), CopilotMode.Ask, "You asked");

    /// <summary>Review the running order before the show, using this operator's own history of overruns.</summary>
    [RelayCommand]
    private Task ReviewPlan() => RunCopilot("Review the running order before the show: what is likely to go wrong, and what would you change?", CopilotMode.Review, "Plan review");

    /// <summary>Explain what's wrong with the setup (integrations, refused commands, screens) and how to fix it.</summary>
    [RelayCommand]
    private Task ExplainProblems() => RunCopilot("Something isn't working. What's wrong with the setup, and how do I fix it?", CopilotMode.Explain, "Diagnosis");

    private async Task RunCopilot(string request, CopilotMode mode, string source)
    {
        if (request.Length == 0) return;
        if (CopilotClient() is not { } copilot) { CopilotStatus = CopilotUnavailable ?? ""; return; }
        CopilotInput = "";
        var proposal = new CopilotProposal { Source = source, Request = request, State = ProposalState.Thinking };
        Proposals.Insert(0, proposal);
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, "Copilot asked: " + Trunc(request, 200));
        CopilotBusy = true;
        CopilotStatus = "Thinking…";
        _copilotCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var context = CopilotContext();
            var plan = await Task.Run(() => copilot.PlanAsync(request, context, mode, _copilotCts.Token));
            Fill(proposal, plan);
            proposal.Consequence = PlanConsequence(plan.Steps.Select(s => s.Command));
            CopilotStatus = plan.Steps.Count > 0 ? "Review the plan, tick what should run, then Run." : "";
        }
        finally
        {
            CopilotBusy = false;
            UpdatePendingCount();
        }
    }

    [RelayCommand]
    private void CancelCopilot() => _copilotCts?.Cancel();

    private static void Fill(CopilotProposal p, CopilotPlan plan)
    {
        p.Reply = plan.Reply;
        p.Question = plan.Question ?? "";
        p.Dropped = plan.Dropped.Count == 0 ? "" : "Not proposed (not a valid Stagewatch command): " + string.Join(" · ", plan.Dropped);
        foreach (var s in plan.Steps) p.Steps.Add(new CopilotStepItem(s));
        p.State = ProposalState.Pending;
        p.RaisePending();
    }

    /// <summary>Run the ticked steps, in order, through the same path as every other command. Re-checked first.</summary>
    [RelayCommand]
    private void ApproveProposal(CopilotProposal? p)
    {
        if (p is null || p.State != ProposalState.Pending) return;
        if (p.SharedId is not null) { ApproveShared(p); return; }
        var item = _show.RundownActive ? _show.CurrentItem : -1;
        var ran = 0;
        BeginEditBatch($"Copilot: {Trunc(p.Request, 60)}");
        foreach (var step in p.Steps)
        {
            if (!step.Selected) { step.Result = "skipped"; continue; }
            if (Copilot.Check(step.Command) is not { } cmd) { step.Result = "refused: not a valid command"; continue; }
            Execute(cmd);
            step.Result = "done";
            ran++;
            _journal.Add(DateTime.UtcNow, JournalKind.Note, item, $"Copilot step approved ({p.Source}): {step.Command}");
        }
        EndEditBatch();
        p.State = ProposalState.Done;
        CopilotStatus = ran == 0 ? "Nothing was ticked — nothing ran." : $"Ran {ran} step(s). Undo (Ctrl+Z) reverses the timing changes together.";
        UpdatePendingCount();
    }

    [RelayCommand]
    private void DiscardProposal(CopilotProposal? p)
    {
        if (p is null || p.State is ProposalState.Done or ProposalState.Discarded) return;
        DiscardShared(p);
        p.State = ProposalState.Discarded;
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, $"Copilot proposal discarded ({p.Source}): " + Trunc(p.Request, 160));
        UpdatePendingCount();
    }

    [RelayCommand]
    private void ClearProposals()
    {
        for (int i = Proposals.Count - 1; i >= 0; i--)
            if (Proposals[i].State is ProposalState.Done or ProposalState.Discarded) Proposals.RemoveAt(i);
    }

    private void UpdatePendingCount() => PendingProposals = Proposals.Count(p => p.State == ProposalState.Pending && p.Steps.Count > 0);

    /// <summary>
    /// MCP agents: with approval on (the default), their commands become proposals here and the agent is told so.
    /// Returns null when the agent may act directly, never for a line that drives another app's air (<paramref name="mustPropose"/>):
    /// that one is always a proposal, so a person decides what goes on a peer's air. Thread-safe (called from the web server).
    /// </summary>
    public AgentReply? ProposeFromAgent(string line, string reason, bool mustPropose)
    {
        if (!mustPropose && !Settings.Integrations.AgentsNeedApproval) return null;
        if (Copilot.Check(line) is not { } cmd) return AgentReply.Refused("Rejected: not a valid Stagewatch command.");
        var step = new CopilotStep(line.Trim(), reason.Length > 300 ? reason[..300] : reason, Copilot.Classify(cmd));
        var proposal = new CopilotProposal { Source = "AI agent (MCP)", Request = step.Command };
        _ui.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            Fill(proposal, new CopilotPlan(reason.Length > 0 ? reason : "An AI agent wants to run this.", new[] { step }, Array.Empty<string>(), null));
            proposal.Consequence = PlanConsequence(new[] { step.Command });
            Proposals.Insert(0, proposal);
            _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, "AI agent proposed: " + Trunc(line, 200));
            UpdatePendingCount();
            StatusLine = "An AI agent proposed a change — see Copilot";
        });
        var always = mustPropose && !Settings.Integrations.AgentsNeedApproval ? " A command that drives another app's air always waits for a person." : "";
        return AgentReply.Waiting($"Queued for the operator's approval (proposal #{proposal.Id}).{always} It runs only if they approve it in Stagewatch → Copilot.");
    }

    /// <summary>IShowAgents: a proposal's fate for the agent that made it (network thread; read on the owner thread).</summary>
    public string? ProposalJson(int id)
    {
        string? Read()
        {
            var p = Proposals.FirstOrDefault(x => x.Id == id);
            return p is null ? null : JsonSerializer.Serialize(new
            {
                id = p.Id, source = p.Source, request = p.Request,
                state = p.State switch { ProposalState.Pending => "waiting for the operator", ProposalState.Done => "approved", ProposalState.Discarded => "discarded", _ => "being prepared" },
                steps = p.Steps.Select(s => new { command = s.Command, ticked = s.Selected, result = s.Result }),
            });
        }
        if (_ui.CheckAccess()) return Read();
        var op = _ui.InvokeAsync(Read);
        return op.Task.Wait(TimeSpan.FromSeconds(2)) ? op.Result : null;
    }

    /// <summary>Everything the copilot may reason about, gathered here; the JSON is <see cref="AiContext"/>'s.</summary>
    private string CopilotContext() => AiContext.Copilot(new CopilotFacts(
        DateTime.UtcNow, ShowClock,
        SnapshotJson.Serialize(_hub.Latest, FrameAccess.Operator),
        SnapshotJson.SerializeConfig(GetRemoteConfig()),
        ListScreens().Select(x => new ScreenFact(x.Id, x.Name, x.Page, x.Device, x.Control, x.Claim)).ToList(),
        Settings.Remote.ScreenPresets,
        IntegrationStatus().Select(x => new IntegrationFact(x.Name, x.Health.ToString(), x.Status)).ToList(),
        _history.Records.Count, _model, Settings.Rundown.Items, _journal.Entries, PeerFacts(), WindowItems()));

    /// <summary>Draft the narrative for the show report from the journal (the operator edits it before it's kept).</summary>
    public async Task<string> DraftShowSummaryAsync()
    {
        if (CopilotClient() is not { } copilot) return "";
        var json = AiContext.SummaryInput(Settings, _journal.Entries, ShowClock);
        return await Task.Run(() => copilot.DraftSummaryAsync(json));
    }

    /// <summary>Keep an approved summary: it goes in the journal, so the show report includes it.</summary>
    public void KeepShowSummary(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        _journal.Add(DateTime.UtcNow, JournalKind.Note, -1, ShowReport.SummaryPrefix + text);
        StatusLine = "Show summary added to the report";
    }

    /// <summary>Draft running-order rows from agenda text (the editor shows them for review before anything is kept).</summary>
    public async Task<AgendaDraft> DraftAgendaAsync(string text)
    {
        if (CopilotClient() is not { } copilot) return new AgendaDraft(Array.Empty<AgendaDraftItem>(), new[] { CopilotUnavailable ?? "Copilot unavailable" });
        return await Task.Run(() => copilot.DraftAgendaAsync(text));
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
