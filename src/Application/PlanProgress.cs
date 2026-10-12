using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;

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
    public async Task<string> CurrentAsync(
        string projectName = "",
        string planId = "",
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        if (planId.Length > 0)
        {
            if (!Guid.TryParse(planId, out var named))
                return Messages.PlanNotFound;

            if (await plans.FindAsync(named, cancellationToken) is not { } found)
                return Messages.PlanNotFound;

            return PlanView.Render(found, now, Borrowed(found));
        }

        var project = Projects.Resolve(projectName, identity);

        var plan = await plans.CurrentAsync(identity.AgentId, project, cancellationToken);

        return plan is null
            ? Messages.NoCurrentPlan
            : PlanView.Render(plan, now);
    }

    private string Borrowed(Plan plan) =>
        plan.AgentId.Equals(identity.AgentId, StringComparison.OrdinalIgnoreCase)
            ? ""
            : $"{Messages.SomeoneElsesPlan} It belongs to agent '{plan.AgentId}'.";

    /// <summary>Moves a step to a new status and answers with the progress and the step to do next.</summary>
    public async Task<string> AdvanceAsync(
        string stepId,
        StepStatus status,
        string note = "",
        string projectName = "",
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(stepId, out var wanted))
            return Messages.StepNotFound;

        if (status is StepStatus.Skipped && string.IsNullOrWhiteSpace(note))
            return Messages.SkipReasonRequired;

        var project = Projects.Resolve(projectName, identity);
        var now = clock.GetUtcNow();

        if (await plans.CurrentAsync(identity.AgentId, project, cancellationToken) is not { } plan)
            return Messages.NoCurrentPlan;

        if (plan.Steps.FirstOrDefault(step => step.Id == wanted) is not { } current)
            return Messages.StepNotFound;

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
            return Messages.VersionConflict;

        var next = updated.Steps
            .OrderBy(step => step.OrderIndex)
            .FirstOrDefault(step => step.Status is StepStatus.Pending);

        var hint = status is StepStatus.Failed ? Messages.AfterFailure : "";

        return PlanView.Advanced(updated, reported, next, now, hint);
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

    private static string AlreadyBusy(Step busy) =>
        $"A plan runs one step at a time, and '{busy.Title}' ({busy.Id}) is already {StepStatus.InProgress}. Report that one first.";

    private static string Refused(Step step, StepStatus wanted)
    {
        var allowed = Transitions.AllowedFrom(step.Status);

        return allowed.Count is 0
            ? $"Step '{step.Title}' is {step.Status}, which is final: it cannot move to {wanted}."
            : $"Step '{step.Title}' is {step.Status}, so it cannot move to {wanted}. Allowed from here: {string.Join(", ", allowed)}.";
    }
}
