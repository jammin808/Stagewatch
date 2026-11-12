using System.Globalization;
using Stagewatch.Core;

namespace Stagewatch.Show;

/// <summary>
/// One step of an approved plan as it is run: its line, the app it is for (null for this Stagewatch) — by the id the desk listed it
/// under, with the name the plan showed — whether the operator ticked it, and the risk the operator was shown for it, which a step
/// for another app may not exceed when it goes. An unticked step never runs.
/// </summary>
public sealed record PlanStep(string Command, NodeRef? Target, bool Ticked, LineRisk Risk = LineRisk.Low);

/// <summary>What came of one step: <see cref="Done"/>, and the words the Copilot list shows beside it.</summary>
public sealed record PlanStepResult(string Stage, string Words)
{
    /// <summary>The step ran: this Stagewatch ran it, or its app said it did.</summary>
    public bool Done => Stage == PlanRunner.DoneStage;
}

/// <summary>
/// What a step for another app came to as it was handed to this Stagewatch's desk: refused there (<see cref="Ok"/> false, in the desk's
/// words, nothing queued — <see cref="Risen"/> the app's rating now when it was refused for rating the line above what the operator was
/// shown), or sent as <see cref="Proposal"/> under <see cref="Id"/>, whose outcome <see cref="Settled"/> brings.
/// </summary>
public sealed record PlanSend(bool Ok, string Words, string? Id = null, Task<DeskResult>? Settled = null)
{
    /// <summary>The proposal the step went as; null when it was refused.</summary>
    public SharedProposal? Proposal { get; init; }

    /// <summary>The line's rating now, when it was refused for rating above what the operator was shown; null otherwise.</summary>
    public LineRisk? Risen { get; init; }
}

/// <summary>
/// Runs an approved copilot plan, pure but for its two delegates and the clock it waits on. Steps run in the plan's order. A step for
/// this Stagewatch runs through <see cref="Here"/> on the caller's thread; a run of them, up to the next step for another app, is one
/// undo batch (<see cref="Open"/>, <see cref="Close"/>). A step for another app goes to this Stagewatch's desk through
/// <see cref="App"/> — as a person's line, which only the operator's approval of the plan sends — and the run waits for that app's
/// result for at most <see cref="Wait"/>. A step that does not report done — refused here, refused by the desk or by its app, or with
/// no result in time — stops the run: every ticked step after it is not run, in words that say so. Unticked steps are skipped.
/// </summary>
public sealed class PlanRunner
{
    /// <summary>The longest a step for another app is waited for before the run stops.</summary>
    public static readonly TimeSpan AppWait = TimeSpan.FromSeconds(10);

    /// <summary>The stage of a step that ran.</summary>
    public const string DoneStage = "done";

    /// <summary>The stage of a step for another app that gave no result within <see cref="Wait"/>: the run stopped waiting for it.</summary>
    public const string NoResultStage = "no result";

    /// <summary>What an unticked step says.</summary>
    public const string Skipped = "skipped";

    /// <summary>What a ticked step after one that did not report done says.</summary>
    public const string NotRun = "not run — the step before it did not report done";

    /// <summary>What a step for another app tells that app of where it comes from: fixed words, so no model's words leave this machine.</summary>
    public const string Reason = "a step of Stagewatch's copilot plan, approved by its operator";

    /// <summary>
    /// Who a plan's step for another app is sent as: the operator who approved the plan, a person proved by being at this desk — the
    /// plan is named only beside the desk's journal row (<see cref="DeskOrigin.Who"/>), never in the proposal another app reads.
    /// </summary>
    public static DeskOrigin Origin(string source) => DeskOrigin.Operator with { Who = "the copilot's plan (" + source + ")" };

    /// <summary>
    /// A plan's step for another app handed to <paramref name="desk"/> as <paramref name="by"/>'s line (<see cref="DeskService.SendPlanned"/>:
    /// the app by its id alone, rated again against what the operator was shown), with <see cref="Reason"/> for its reason; sent, it is
    /// waited for through <see cref="DeskService.Settled"/>. A refusal is in the desk's shown words, which never quote a line its app
    /// does not list. Runs on the caller's thread.
    /// </summary>
    public static Func<PlanStep, PlanSend> ThroughDesk(DeskService desk, DeskOrigin by) => step =>
    {
        if (step.Target is not { } target) return new PlanSend(false, "not a step for another app");
        var outcome = desk.SendPlanned(target, step.Command, step.Risk, by, Reason);
        return outcome.Proposal is { } sent
            ? new PlanSend(true, outcome.Words, sent.Id, desk.Settled(sent.Id)) { Proposal = sent }
            : new PlanSend(false, outcome.Shown) { Risen = outcome.Risen };
    };

    /// <summary>
    /// The first ticked step for another app that the desk would refuse now (<see cref="DeskService.WhyNotPlanned"/>), by its index, or
    /// null: a Run press asks this before any step runs, so a plan never stops halfway on a refusal it could have known at the press.
    /// </summary>
    public static (int Index, DeskOutcome Why)? Blocked(DeskService desk, IReadOnlyList<PlanStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] is { Ticked: true, Target: { } target } step && desk.WhyNotPlanned(target, step.Command, step.Risk) is { } why)
                return (i, why);
        return null;
    }

    /// <summary>A step for this Stagewatch, run on the caller's thread: done, or refused in words.</summary>
    public required Func<PlanStep, PlanStepResult> Here { get; init; }

    /// <summary>A step for another app, handed to the desk on the caller's thread.</summary>
    public required Func<PlanStep, PlanSend> App { get; init; }

    /// <summary>Opens an undo batch before the first step of a run of this Stagewatch's steps.</summary>
    public Action Open { get; init; } = () => { };

    /// <summary>Closes that batch once the run of this Stagewatch's steps ends.</summary>
    public Action Close { get; init; } = () => { };

    /// <summary>Told each step's result as it lands, by its index, so a list follows the run live.</summary>
    public Action<int, PlanStepResult> Reported { get; init; } = (_, _) => { };

    /// <summary>The clock the wait for another app runs on.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>The longest a step for another app is waited for.</summary>
    public TimeSpan Wait { get; init; } = AppWait;

    /// <summary>
    /// Runs <paramref name="steps"/> in order, each step's result in the same order. The awaits keep the caller's context, so
    /// <see cref="Here"/>, <see cref="App"/> and <see cref="Reported"/> run on the thread that called this (the show thread).
    /// </summary>
    public async Task<IReadOnlyList<PlanStepResult>> RunAsync(IReadOnlyList<PlanStep> steps)
    {
        var results = new PlanStepResult[steps.Count];
        var stopped = false;
        var batch = false;
        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                PlanStepResult result;
                if (!step.Ticked) result = new(Skipped, Skipped);
                else if (stopped) result = new("not run", NotRun);
                else if (step.Target is null)
                {
                    if (!batch) { Open(); batch = true; }
                    result = Here(step);
                }
                else
                {
                    if (batch) { Close(); batch = false; }
                    result = await ForApp(step);
                }
                if (step.Ticked && !stopped && !result.Done) stopped = true;
                results[i] = result;
                Reported(i, result);
            }
        }
        finally
        {
            if (batch) Close();
        }
        return results;
    }

    /// <summary>A step for another app: refused by the desk, or sent and waited for — its app's answer, or none in time.</summary>
    private async Task<PlanStepResult> ForApp(PlanStep step)
    {
        var sent = App(step);
        if (!sent.Ok || sent.Settled is null) return new("refused", sent.Words);
        DeskResult? result;
        try
        {
            result = sent.Settled.IsCompleted ? await sent.Settled : await sent.Settled.WaitAsync(Wait, Time);
        }
        catch (TimeoutException)
        {
            result = null;
        }
        if (result is null)
            return new(NoResultStage, string.Create(CultureInfo.InvariantCulture,
                $"no result in {Wait.TotalSeconds:0.#} s — {sent.Words}; its outcome still reaches the desk"));
        return result.Ok == true ? new(DoneStage, result.Words) : new(result.Stage, result.Words);
    }
}
