using Snail.MCP.Plans.Core;

namespace Snail.MCP.Plans.Tests.Core;

/// A plan has no closing tool: its status follows its steps. These are the rules that makes it so.
public class SettlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Plan_WithEveryStepDone_Completes()
    {
        var settled = Settlement.Reconcile(PlanOf(StepStatus.Done, StepStatus.Done), Now);

        Assert.Equal(PlanStatus.Completed, settled.Status);
        Assert.Equal(Now, settled.CompletedAt);
    }

    [Fact]
    public void Plan_WithASkippedStepAmongDoneOnes_Completes() =>
        Assert.Equal(PlanStatus.Completed, Settlement.Reconcile(PlanOf(StepStatus.Done, StepStatus.Skipped), Now).Status);

    /// The point of keeping Failed out of the terminal set: the plan stays active until the agent retries
    /// the step or skips it with a reason, so a failure cannot be walked away from quietly.
    [Fact]
    public void Plan_HoldingAFailedStep_StaysActive() =>
        Assert.Equal(PlanStatus.Active, Settlement.Reconcile(PlanOf(StepStatus.Done, StepStatus.Failed), Now).Status);

    [Fact]
    public void Plan_WithWorkLeft_StaysActive() =>
        Assert.Equal(PlanStatus.Active, Settlement.Reconcile(PlanOf(StepStatus.Done, StepStatus.Pending), Now).Status);

    /// Automatic completion would be a trap if it could not be undone: adding a step to a finished plan
    /// reopens it instead of refusing the revision.
    [Fact]
    public void Plan_GainingAStepAfterCompletion_Reopens()
    {
        var completed = Settlement.Reconcile(PlanOf(StepStatus.Done), Now);

        var reopened = Settlement.Reconcile(
            completed with { Steps = [.. completed.Steps, StepOf(StepStatus.Pending, 200)] },
            Now);

        Assert.Equal(PlanStatus.Active, reopened.Status);
        Assert.Null(reopened.CompletedAt);
    }

    /// An outcome the agent declared outranks the steps: it said the work failed, and finishing the
    /// remaining steps is not what it asked for.
    [Theory]
    [InlineData(PlanStatus.Failed)]
    [InlineData(PlanStatus.Abandoned)]
    public void Plan_SettledByTheAgent_IsLeftAlone(PlanStatus declared)
    {
        var declaredPlan = PlanOf(StepStatus.Done) with { Status = declared };

        Assert.Equal(declared, Settlement.Reconcile(declaredPlan, Now).Status);
    }

    private static Plan PlanOf(params StepStatus[] statuses) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentId = "agent-one",
        ProjectName = "project-one",
        Objective = "ship it",
        CreatedAt = Now,
        UpdatedAt = Now,
        Steps = [.. statuses.Select((status, position) => StepOf(status, (position + 1) * Ordering.Gap))]
    };

    private static Step StepOf(StepStatus status, int order) => new()
    {
        Id = Guid.CreateVersion7(),
        OrderIndex = order,
        Title = $"step at {order}",
        Status = status
    };
}
