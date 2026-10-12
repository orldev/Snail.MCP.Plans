namespace Snail.MCP.Plans.Core;

/// <summary>Keeps the status of a plan in step with the statuses of its steps.</summary>
/// <remarks>
/// A plan needs no closing act: it is finished exactly when nothing is left open, and a failed step keeps
/// it active on purpose, so a failure cannot be left behind quietly. Adding work to a finished plan
/// reopens it, which is what stops the automatic close from being a trap.
/// </remarks>
public static class Settlement
{
    /// <summary>The plan with its status reconciled against its steps.</summary>
    public static Plan Reconcile(Plan plan, DateTimeOffset now)
    {
        if (plan.Status is PlanStatus.Failed or PlanStatus.Abandoned)
            return plan;

        var isFinished = plan.Steps.Count > 0 && plan.Steps.All(step => Transitions.IsTerminal(step.Status));

        if (isFinished && plan.Status is PlanStatus.Active)
            return plan with { Status = PlanStatus.Completed, CompletedAt = now };

        if (!isFinished && plan.Status is PlanStatus.Completed)
            return plan with { Status = PlanStatus.Active, CompletedAt = null };

        return plan;
    }
}
