using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stagewatch.Core;
using Stagewatch.Integrations;

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
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, "Copilot asked: " + Trunc(request, 200));
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
        var item = _rundownActive ? _currentItem : -1;
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
        p.State = ProposalState.Discarded;
        _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, $"Copilot proposal discarded ({p.Source}): " + Trunc(p.Request, 160));
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
    /// Returns null when the agent may act directly. Thread-safe (called from the web server).
    /// </summary>
    public string? ProposeFromAgent(string line, string reason)
    {
        if (!Settings.Integrations.AgentsNeedApproval) return null;
        if (Copilot.Check(line) is not { } cmd) return "Rejected: not a valid Stagewatch command.";
        var step = new CopilotStep(line.Trim(), reason.Length > 300 ? reason[..300] : reason, Copilot.Classify(cmd));
        var proposal = new CopilotProposal { Source = "AI agent (MCP)", Request = step.Command };
        _ui.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            Fill(proposal, new CopilotPlan(reason.Length > 0 ? reason : "An AI agent wants to run this.", new[] { step }, Array.Empty<string>(), null));
            proposal.Consequence = PlanConsequence(new[] { step.Command });
            Proposals.Insert(0, proposal);
            _journal.Add(DateTime.UtcNow, JournalKind.Note, _rundownActive ? _currentItem : -1, "AI agent proposed: " + Trunc(line, 200));
            UpdatePendingCount();
            StatusLine = "An AI agent proposed a change — see Copilot";
        });
        return $"Queued for the operator's approval (proposal #{proposal.Id}). It runs only if they approve it in Stagewatch → Copilot.";
    }

    /// <summary>Everything the copilot may reason about: the operator frame, presets and messages, screens, timers.</summary>
    private string CopilotContext()
    {
        var frame = SnapshotJson.Serialize(_hub.Latest, FrameAccess.Operator);
        var config = SnapshotJson.SerializeConfig(GetRemoteConfig());
        var screens = ListScreens().Select(s => new { id = s.Id, name = s.Name, page = s.Page, device = s.Device, control = s.Control });
        var setups = Settings.Remote.ScreenPresets.Select(p => new { name = p.Name, page = p.Path });
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("now", ShowClock.ToShow(DateTime.UtcNow).ToString("yyyy-MM-dd HH:mm:ss"));
            w.WritePropertyName("show");
            w.WriteRawValue(frame);
            w.WritePropertyName("config");
            w.WriteRawValue(config);
            w.WritePropertyName("screens");
            JsonSerializer.Serialize(w, screens);
            w.WritePropertyName("screen_setups");
            JsonSerializer.Serialize(w, setups);
            // How each link is doing, so "why isn't OBS switching?" has an answer.
            w.WritePropertyName("integrations");
            JsonSerializer.Serialize(w, IntegrationStatus().Select(x => new { name = x.Name, health = x.Health.ToString(), status = x.Status }));
            // This operator's history: how much each item is likely to run over, learned from their past shows.
            w.WritePropertyName("history");
            JsonSerializer.Serialize(w, new
            {
                items_learned_from = _history.Records.Count,
                items = Settings.Rundown.Items.Select((it, i) =>
                {
                    var (mu, sigma) = _model.For(it.Kind, it.Speaker);
                    return new
                    {
                        n = i + 1, title = it.Title, kind = it.Kind.ToString(), speaker = it.Speaker ?? "",
                        planned_min = Math.Round(it.DurationSeconds / 60.0, 1),
                        expected_overrun_pct = Math.Round((Math.Exp(mu) - 1) * 100),
                        p90_overrun_pct = Math.Round((Math.Exp(mu + 1.2816 * sigma) - 1) * 100),
                    };
                }),
            });
            // The latest journal lines: refused commands, link drops, what just happened.
            w.WritePropertyName("recent");
            JsonSerializer.Serialize(w, _journal.Entries.TakeLast(30).Select(e => $"{ShowClock.ToShow(e.Utc):HH:mm:ss} {e.Kind}: {e.Text}"));
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Draft the narrative for the show report from the journal (the operator edits it before it's kept).</summary>
    public async Task<string> DraftShowSummaryAsync()
    {
        if (CopilotClient() is not { } copilot) return "";
        var entries = _journal.Entries.TakeLast(1500).Select(e => new
        {
            t = ShowClock.ToShow(e.Utc).ToString("HH:mm:ss"), k = e.Kind.ToString(),
            item = e.Item >= 0 && e.Item < Settings.Rundown.Items.Count ? $"{e.Item + 1}. {Settings.Rundown.Items[e.Item].Title}" : "",
            x = e.Text, v = e.Value
        });
        var plan = Settings.Rundown.Items.Select((it, i) => new { n = i + 1, title = it.Title, planned_min = it.DurationSeconds / 60.0, kind = it.Kind.ToString() });
        var json = JsonSerializer.Serialize(new { running_order = plan, journal = entries });
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
