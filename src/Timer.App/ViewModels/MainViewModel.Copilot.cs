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

/// <summary>
/// One step of a proposal: the command, why, how risky — and whether the operator ticked it. A step of a copilot plan for another app
/// names that app (<see cref="Target"/>) and, once sent, the desk's proposal it went as (<see cref="SharedId"/>), whose outcome it follows.
/// </summary>
public sealed partial class CopilotStepItem : ObservableObject
{
    public CopilotStepItem(CopilotStep step)
    {
        Command = step.Command;
        Why = step.Why;
        _risk = step.Risk;
        Target = step.Target;
        _selected = step.Risk != CopilotRisk.High;   // anything that changes the room right now is opt-in
    }

    public string Command { get; }
    public string Why { get; }

    /// <summary>
    /// How risky the step is as the operator was shown it; for another app's step, rated again by its app's table when it is refused
    /// for rating higher now, and unticked then, so it goes only once ticked at that rating.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RiskLabel))] private CopilotRisk _risk;
    public string RiskLabel => Risk switch { CopilotRisk.High => "CHANGES THE ROOM", CopilotRisk.Review => "REVIEW", _ => "SAFE" };

    /// <summary>The app a copilot step is for, by the desk's id and name; null for this Stagewatch's own command.</summary>
    public NodeRef? Target { get; }

    /// <summary>The app's Show Bus id, or null for this Stagewatch.</summary>
    public string? TargetId => Target?.Id;

    /// <summary>"→ PlayMap Hall" above a step for another app; "" for this Stagewatch's own.</summary>
    public string TargetLabel => Target is { } t ? "→ " + t.Name : "";

    /// <summary>The desk's proposal a step for another app went as, once sent: its result follows that proposal.</summary>
    [ObservableProperty] private string? _sharedId;
    [ObservableProperty] private bool _selected;
    [ObservableProperty] private string _result = "";

    /// <summary>The run has said what came of this step: an earlier stage the desk reports after that never replaces its words.</summary>
    internal bool Answered { get; set; }

    /// <summary>The run stopped waiting for this step: its app's outcome, when it comes, is added to the run's words, never put over them.</summary>
    internal bool WaitedOut { get; set; }

    /// <summary>Its app's outcome has been shown: nothing the desk reports of it after that changes its words.</summary>
    internal bool Settled { get; set; }
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

    /// <summary>
    /// Why approving this would be refused now — another app's line whose app is not asking this desk for its approved lines — in
    /// the desk's own words; "" while it can be approved. Run ticked steps is greyed while it is set, the words beside it.
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanApprove))] private string _approveBlocked = "";
    public bool CanApprove => ApproveBlocked.Length == 0;

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
            // The apps a step may be for, as the desk knows them now; the whole verbs of those the request names go in the first pass,
            // and a second pass (at most one) is built here, on the show thread, with the verbs the plan asked for.
            var apps = CopilotApp.From(Desk.Apps(), Desk.Risk);
            var named = Copilot.Named(request, apps);
            var tokens = ScreenWindows.Tokens(ControlledWindows).ToList();
            var ct = _copilotCts.Token;
            var plan = await Task.Run(() => copilot.PlanAsync(request, full => _ui.Invoke(() => CopilotContext(full)), apps, named, mode, tokens, ct));
            Fill(proposal, plan);
            proposal.Consequence = PlanConsequence(plan.Steps.Where(s => s.Target is null).Select(s => s.Command));
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

    private void Fill(CopilotProposal p, CopilotPlan plan)
    {
        p.Reply = plan.Reply;
        p.Question = plan.Question ?? "";
        // Each dropped line carries its own reason in brackets where it has one (too many steps, a person's choice on the desk); the
        // rest are not Stagewatch commands at all. Steps for other apps are listed apart, each naming its app and why, never its line.
        var dropped = new List<string>();
        if (plan.Dropped.Count > 0) dropped.Add("Not proposed (not a valid Stagewatch command, or not one a plan may hold): " + string.Join(" · ", plan.Dropped));
        if (plan.AppDropped.Count > 0) dropped.Add("Not proposed for other apps: " + string.Join(" · ", plan.AppDropped));
        p.Dropped = string.Join("\n", dropped);
        foreach (var s in plan.Steps) p.Steps.Add(new CopilotStepItem(s));
        p.State = ProposalState.Pending;
        p.RaisePending();
        // Greyed at once when a ticked step's app would not take its line now — not a second later.
        RefreshApproveBlocks();
    }

    /// <summary>A plan's steps as the runner and the desk read them: each with its app, its tick and the risk the operator was shown.</summary>
    private static List<PlanStep> PlanSteps(IEnumerable<CopilotStepItem> items)
        => items.Select(s => new PlanStep(s.Command, s.Target, s.Selected, Copilot.Line(s.Risk))).ToList();

    /// <summary>
    /// Run the ticked steps, in order (<see cref="PlanRunner"/>): this Stagewatch's through the same path as every other command,
    /// re-checked first, each run of them one undo batch; another app's as a person's line from this desk, decided by the operator who
    /// pressed — the desk's <see cref="DeskService.Send"/>, with its fence, its app's verified table and its collect rule — and waited
    /// for, at most <see cref="PlanRunner.AppWait"/>. A step that does not report done stops the run, and the steps after it say so.
    /// Before any step runs, every ticked step for another app is asked of the desk (<see cref="PlanRunner.Blocked"/>): one it would
    /// refuse now — its app gone, not collecting, not listing the line, or rating it above what the operator was shown — refuses the
    /// whole run, the plan still waiting, so a plan never stops halfway on a refusal the press could have known.
    /// Another app's proposal on the shared queue is the desk's to decide, by the same tick: unticked, it is not approved.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ApproveProposal(CopilotProposal? p)
    {
        if (p is null || p.State != ProposalState.Pending) return;
        if (p.SharedId is not null) { ApproveShared(p); return; }
        var items = p.Steps.ToList();
        var steps = PlanSteps(items);
        if (!steps.Exists(s => s.Ticked))
        {
            foreach (var s in items) s.Result = PlanRunner.Skipped;
            p.State = ProposalState.Done;
            CopilotStatus = DeskService.NothingTicked + ".";
            UpdatePendingCount();
            return;
        }
        if (PlanRunner.Blocked(Desk, steps) is { } stop)
        {
            var (at, why) = stop;
            var blocked = items[at];
            // Rated higher by its app now: shown at that rating and unticked, so it goes only once the operator ticks it again.
            if (why.Risen is { } risen) { blocked.Risk = Copilot.Of(risen); blocked.Selected = false; }
            blocked.Result = "not sent: " + why.Shown;
            CopilotStatus = $"Nothing ran — step {at + 1}: {why.Shown}";
            RefreshApproveBlocks();
            return;
        }
        // Decided once pressed: nothing in it can be approved twice while an app's step is waited for.
        p.State = ProposalState.Done;
        UpdatePendingCount();
        CopilotStatus = "Running the ticked steps…";
        var batch = $"Copilot: {Trunc(p.Request, 60)}";
        var send = PlanRunner.ThroughDesk(Desk, PlanRunner.Origin(p.Source));
        var runner = new PlanRunner
        {
            Here = s => RunPlannedHere(p, s),
            // By the step itself, never by its value: two steps may say the same line.
            App = s => SentPlanned(items[steps.FindIndex(x => ReferenceEquals(x, s))], send(s)),
            Open = () => BeginEditBatch(batch),
            Close = EndEditBatch,
            Reported = (i, r) => ReportedPlanned(items[i], steps[i], r),
        };
        var results = await runner.RunAsync(steps);
        var ran = results.Count(r => r.Done);
        var stoppedAt = Enumerable.Range(0, steps.Count).FirstOrDefault(i => steps[i].Ticked && !results[i].Done, -1);
        CopilotStatus = stoppedAt >= 0
            ? $"Ran {ran} step(s); stopped at step {stoppedAt + 1}: {results[stoppedAt].Words}"
            : $"Ran {ran} step(s). Undo (Ctrl+Z) reverses each run of Stagewatch's own steps together; another app's steps are that app's to undo.";
    }

    /// <summary>A plan's step for this Stagewatch, on the show thread: re-checked, run, journalled.</summary>
    private PlanStepResult RunPlannedHere(CopilotProposal p, PlanStep step)
    {
        if (Copilot.Check(step.Command) is not { } cmd) return new("refused", "refused: not a valid command");
        Execute(cmd);
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _show.RundownActive ? _show.CurrentItem : -1, $"Copilot step approved ({p.Source}): {step.Command}");
        return new(PlanRunner.DoneStage, "done");
    }

    /// <summary>
    /// A plan's step for another app as the desk took it (<see cref="PlanRunner.ThroughDesk"/>), on the show thread: sent, its proposal is
    /// remembered on the step — and kept apart from the list (<see cref="KeepPlanned"/>) — so its outcome is followed there, never as a
    /// list item of its own; refused for rating higher now, the step shows that rating and is unticked.
    /// </summary>
    private PlanSend SentPlanned(CopilotStepItem step, PlanSend sent)
    {
        if (sent.Risen is { } risen) { step.Risk = Copilot.Of(risen); step.Selected = false; }
        if (sent.Proposal is not { } proposal) return sent;
        step.SharedId = proposal.Id;
        KeepPlanned(proposal.Id, step);
        OnSharedProposal(proposal);
        return sent;
    }

    /// <summary>A step's result as the run reports it; for another app's ticked step, the run's word on it, which later stages respect.</summary>
    private static void ReportedPlanned(CopilotStepItem item, PlanStep step, PlanStepResult result)
    {
        item.Result = result.Words;
        if (step is not { Ticked: true, Target: not null } || item.Settled) return;
        item.Answered = true;
        item.WaitedOut = result.Stage == PlanRunner.NoResultStage;
    }

    /// <summary>The most plan steps sent to other apps whose outcomes are followed after their plan has left the list.</summary>
    private const int PlannedKept = 64;

    private readonly Dictionary<string, CopilotStepItem> _planned = new(StringComparer.Ordinal);
    private readonly Queue<string> _plannedOrder = new();

    /// <summary>A plan's step sent as <paramref name="id"/>, followed by its id whether or not its plan is still listed; the oldest go past <see cref="PlannedKept"/>.</summary>
    private void KeepPlanned(string id, CopilotStepItem step)
    {
        if (_planned.TryAdd(id, step)) _plannedOrder.Enqueue(id);
        while (_plannedOrder.Count > PlannedKept) _planned.Remove(_plannedOrder.Dequeue());
    }

    [RelayCommand]
    private void DiscardProposal(CopilotProposal? p)
    {
        if (p is null || p.State is ProposalState.Done or ProposalState.Discarded) return;
        DiscardShared(p);
        p.State = ProposalState.Discarded;
        // A shared proposal's rejection is the desk's own journal row (who rejected it, and its id); Stagewatch's own plans are journalled here.
        if (p.SharedId is null)
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

    /// <summary>
    /// Everything the copilot may reason about, gathered here (show thread); the JSON is <see cref="AiContext"/>'s. The other apps carry
    /// whether each collects and, for a verified one, its verb heads — and its whole verbs only when it is in <paramref name="full"/>.
    /// </summary>
    private string CopilotContext(IReadOnlyCollection<string>? full = null) => AiContext.Copilot(new CopilotFacts(
        DateTime.UtcNow, ShowClock,
        SnapshotJson.Serialize(_hub.Latest, FrameAccess.Operator),
        SnapshotJson.SerializeConfig(GetRemoteConfig()),
        ListScreens().Select(x => new ScreenFact(x.Id, x.Name, x.Page, x.Device, x.Control, x.Claim)).ToList(),
        Settings.Remote.ScreenPresets,
        IntegrationStatus().Select(x => new IntegrationFact(x.Name, x.Health.ToString(), x.Status)).ToList(),
        _history.Records.Count, _model, Settings.Rundown.Items, _journal.Entries,
        AiContext.Apps(PeerFacts(), Desk.Apps(), Desk.VerifiedTable, full ?? []), WindowFacts(), PlayoutFactsForAi(), _preflight));

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
