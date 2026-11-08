using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Application;

/// <summary>Reading the plan in hand and reporting a step against it.</summary>
/// <remarks>
/// Neither operation takes a plan identity: it lived in a conversation that may since have been
/// summarised away, and an agent that cannot name its plan drafts a second one instead of continuing
/// the first.
/// </remarks>
public sealed class PlanProgress(IPlanStore plans, IAgentIdentity identity, TimeProvider clock)
{
    /// <summary>The plan this agent is working on, or any plan by identity.</summary>
    /// <remarks>
    /// A plan named by identity is readable from anywhere, including another agent's: the active list
    /// shows only a summary, and without this a session in another folder had no way to see the steps —
    /// one tried <c>create_plan</c> to look and left a duplicate plan behind.
    /// </remarks>
    public async Task<Answer<PrintedPlan>> CurrentAsync(
        string projectName = "",
        string planId = "",
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        if (planId.Length > 0)
        {
            if (!Guid.TryParse(planId, out var named))
                return PlanNotFound;

            var found = await plans.FindAsync(named, cancellationToken);

            if (found.Count is 0)
                return PlanNotFound;

            var borrowed = PlanView.Render(found[0], now, PlanView.Prologue(Borrowed(found[0]), PlanView.Unread(found)));

            return Answer.Ok(new PrintedPlan(found[0], borrowed));
        }

        var project = Projects.Resolve(projectName, identity);
        var current = await plans.CurrentAsync(identity.AgentId, project, cancellationToken);

        if (current.Count is 0)
            return NoPlan(current);

        var text = PlanView.Render(current[0], now, PlanView.Unread(current));

        return Answer.Ok(new PrintedPlan(current[0], text));
    }

    private static AnswerFailure PlanNotFound => new(Messages.PlanNotFound, Messages.PlanIdFrom, FailureTypes.PlanNotFound);

    private static AnswerFailure StepNotFound => new(Messages.StepNotFound, Messages.StepIdFrom, FailureTypes.StepNotFound);

    /// <remarks>"No plan" and "a plan that cannot be read" call for different actions, and only the count of unread rows tells them apart.</remarks>
    private static AnswerFailure NoPlan(PlanListing read) =>
        read.UnreadablePlans > 0
            ? new(Messages.CurrentPlanUnreadable, Messages.FixTheRow, FailureTypes.Unreadable)
            : new(Messages.NoCurrentPlan, Messages.DraftOne, FailureTypes.NoPlan);

    private string Borrowed(Plan plan) =>
        plan.AgentId.Equals(identity.AgentId, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $"{Messages.SomeoneElsesPlan} It belongs to agent '{plan.AgentId}'.";

    /// <summary>Moves a step to a new status and answers with the progress and the step to do next.</summary>
    public async Task<Answer<PrintedStep>> AdvanceAsync(
        string stepId,
        StepStatus status,
        string note = "",
        string projectName = "",
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(stepId, out var wanted))
            return StepNotFound;

        if (status is StepStatus.Skipped && string.IsNullOrWhiteSpace(note))
            return Answer.Failed(Messages.SkipReasonRequired, $"{Messages.SayWhy} in 'note'", FailureTypes.Arguments);

        var project = Projects.Resolve(projectName, identity);
        var now = clock.GetUtcNow();

        var read = await plans.CurrentAsync(identity.AgentId, project, cancellationToken);

        if (read.Count is 0)
            return NoPlan(read);

        var plan = read[0];

        if (plan.Steps.FirstOrDefault(step => step.Id == wanted) is not { } current)
            return StepNotFound;

        if (!Transitions.CanMove(current.Status, status))
            return Refused(current, status);

        if (Occupied(plan, current, status) is { } busy)
            return AlreadyBusy(busy);

        var reported = Moved(current, status, note, now);

        var updated = Settlement.Reconcile(
            plan with
            {
                Steps = [.. plan.Steps.Select(step => step.Id == wanted ? reported : step)],
                UpdatedAt = now,
                Version = plan.Version + 1
            },
            now);

        if (!await plans.TrySaveAsync(updated, plan.Version, cancellationToken))
            return Answer.Failed(Messages.VersionConflict, Messages.ReadAgain, FailureTypes.VersionConflict);

        var next = updated.Steps
            .OrderBy(step => step.OrderIndex)
            .FirstOrDefault(step => step.Status is StepStatus.Pending);

        var hint = PlanView.Prologue(PlanView.Unread(read), status is StepStatus.Failed ? Messages.AfterFailure : "");

        var text = PlanView.Advanced(updated, reported, next, hint);

        return Answer.Ok(new PrintedStep(updated, reported, next, text));
    }

    /// <remarks>Going back to <see cref="StepStatus.Pending"/> keeps the attempt that was spent: the step was taken up, and hiding that would understate what the plan cost.</remarks>
    private static Step Moved(Step step, StepStatus status, string note, DateTimeOffset now) => step with
    {
        Status = status,
        Note = string.IsNullOrWhiteSpace(note) ? step.Note : note.Trim(),
        Attempts = status is StepStatus.InProgress ? step.Attempts + 1 : step.Attempts,
        StartedAt = status is StepStatus.InProgress ? now : step.StartedAt,
        CompletedAt = status is StepStatus.InProgress or StepStatus.Pending ? null : now
    };

    private static Step? Occupied(Plan plan, Step moving, StepStatus status) =>
        status is StepStatus.InProgress
            ? plan.Steps.FirstOrDefault(step => step.Status is StepStatus.InProgress && step.Id != moving.Id)
            : null;

    private static AnswerFailure AlreadyBusy(Step busy) => new(
        $"A plan runs one step at a time, and '{busy.Title}' ({busy.Id}) is already {StepStatus.InProgress}.",
        Messages.ReportThatOneFirst,
        FailureTypes.StepInProgress);

    private static AnswerFailure Refused(Step step, StepStatus wanted)
    {
        var allowed = Transitions.AllowedFrom(step.Status);

        if (allowed.Count is 0)
            return new($"Step '{step.Title}' is {step.Status}, which is final: it cannot move to {wanted}.", Type: FailureTypes.Settled);

        return new(
            $"Step '{step.Title}' is {step.Status}, so it cannot move to {wanted}.",
            $"allowed from here: {string.Join(", ", allowed)}",
            FailureTypes.Transition);
    }
}
