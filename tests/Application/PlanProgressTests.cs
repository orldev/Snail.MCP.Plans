using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Tests.TestDoubles;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Tests.Application;

/// Walking the plan: the read that needs no plan id, and the report that answers with what to do next.
public class PlanProgressTests
{
    /// The whole reason this read takes no arguments: the plan id lived in a conversation that may have
    /// been summarised away, and an agent that cannot name its plan drafts a second one.
    [Fact]
    public async Task Current_AskedWithoutAnyIdentifier_AnswersWithThisAgentsPlan()
    {
        using var harness = new PlanHarness();

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        var answer = await harness.Progress.CurrentAsync();

        Assert.Contains("ship the server", answer.Words(), StringComparison.Ordinal);
        Assert.Contains("write the store", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_ForAnAgentThatNeverDrafted_SaysThereIsNone()
    {
        using var harness = new PlanHarness();

        (await harness.Progress.CurrentAsync()).IsRefused(Messages.NoCurrentPlan, FailureTypes.NoPlan);
    }

    /// The largest saving in the whole server: reporting a step and learning the next one is one call.
    [Fact]
    public async Task Advance_FinishingAStep_AnswersWithTheNextPendingStep()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Done, "stored");

        Assert.Contains("wire the tools", answer.Words(), StringComparison.Ordinal);
        Assert.Contains(plan.Steps[1].Id.ToString(), answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_TakingUpAStepTwice_CountsBothAttempts()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(step, StepStatus.Failed, "the file was locked");
        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(2, reread!.Steps[0].Attempts);
    }

    /// A failure the agent has to answer for: the plan stays active and the answer says what the three
    /// ways out are.
    [Fact]
    public async Task Advance_ReportingAFailure_KeepsThePlanActiveAndOffersTheWaysOut()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(step, StepStatus.Failed, "the file was locked");

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(PlanStatus.Active, reread!.Status);
        Assert.Contains(Messages.AfterFailure, answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_WhileAnotherStepIsInProgress_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress);

        Assert.Contains("one step at a time", answer.Words(), StringComparison.Ordinal);
        Assert.Contains("write the store", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_OffTheTransitionTable_NamesWhatIsAllowedInstead()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(step, StepStatus.Failed, "the file was locked");
        var answer = await harness.Progress.AdvanceAsync(step, StepStatus.Done);

        answer.IsRefused($"Step 'write the store' is {StepStatus.Failed}, so it cannot move to {StepStatus.Done}.", FailureTypes.Transition);
        Assert.Contains(nameof(StepStatus.InProgress), answer.Words(), StringComparison.Ordinal);
        Assert.Contains(nameof(StepStatus.Skipped), answer.Words(), StringComparison.Ordinal);
    }

    /// Agents report a short step once, when it is over: 106 of 2031 update_step calls in two weeks went straight from
    /// Pending to Done and were refused, each costing a second call. The one call counts as taking the step up.
    [Theory]
    [InlineData(StepStatus.Done)]
    [InlineData(StepStatus.Failed)]
    public async Task Advance_StraightFromPending_CountsTheStepAsTakenUp(StepStatus status)
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), status, "in one go");

        Assert.True(answer.IsSuccess, answer.Words());

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(status, reread!.Steps[0].Status);
        Assert.Equal(1, reread.Steps[0].Attempts);
        Assert.NotNull(reread.Steps[0].StartedAt);
    }

    /// Finishing a step in one call still takes it up, so the plan's single slot holds: with another step in progress
    /// the call is refused rather than letting two steps run at once.
    [Fact]
    public async Task Advance_StraightFromPendingWhileAnotherStepIsInProgress_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.Done);

        Assert.Contains("one step at a time", answer.Words(), StringComparison.Ordinal);
        Assert.Contains("write the store", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_SkippingWithoutSayingWhy_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        (await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Skipped))
            .IsRefused(Messages.SkipReasonRequired, ToolFailures.Arguments);
    }

    [Fact]
    public async Task Advance_FinishingTheLastOpenStep_CompletesThePlanWithoutAClosingCall()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(step, StepStatus.Done, "stored");

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(PlanStatus.Completed, reread!.Status);
        Assert.Contains(Messages.PlanFinished, answer.Words(), StringComparison.Ordinal);
    }

    /// Identity comes from the process, so naming another agent's step is not a way into its plan.
    [Fact]
    public async Task Advance_OnAStepOfAnotherAgentsPlan_DoesNotReachIt()
    {
        using var harness = new PlanHarness("agent-one", "project-one");
        var plan = await Drafted(harness, "write the store");

        var answer = await harness
            .ProgressOf("agent-two", "project-one")
            .AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);

        answer.IsRefused(Messages.NoCurrentPlan, FailureTypes.NoPlan);
    }

    /// Found live: with the last step taken up there is no Pending step to hand over, and the answer
    /// announced the plan settled while the agent was still working on it.
    [Fact]
    public async Task Advance_TakingUpTheLastStep_DoesNotCallThePlanFinished()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);

        Assert.Contains(Messages.LastOpenStep, answer.Words(), StringComparison.Ordinal);
        Assert.DoesNotContain(Messages.PlanFinished, answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_LeavingAFailedStepAndNothingPending_DoesNotCallThePlanFinished()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(step, StepStatus.Failed, "the file was locked");

        Assert.DoesNotContain(Messages.PlanFinished, answer.Words(), StringComparison.Ordinal);
        Assert.Contains(Messages.OnlyFailureLeft, answer.Words(), StringComparison.Ordinal);
    }

    /// Found in use: a step taken up by mistake could not be put back, and the only way on was to report
    /// a failure that never happened just to free the plan's single slot.
    [Fact]
    public async Task Advance_PuttingBackAStepTakenUpByMistake_FreesThePlanWithoutInventingAFailure()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "wrong one", "right one");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Pending, "taken up by mistake");
        await harness.Progress.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress);

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(StepStatus.Pending, reread!.Steps[0].Status);
        Assert.Equal(StepStatus.InProgress, reread.Steps[1].Status);
        Assert.Equal(0, reread.Steps.Count(step => step.Status is StepStatus.Failed));
    }

    /// The attempt is kept: the step really was taken up, and dropping that would understate the cost.
    [Fact]
    public async Task Advance_PuttingBackAStep_KeepsTheAttemptItSpent()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "wrong one");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(step, StepStatus.Pending);

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(1, reread!.Steps[0].Attempts);
        Assert.Null(reread.Steps[0].CompletedAt);
    }

    [Fact]
    public async Task Advance_OnAStepThatIsNotInThePlan_SaysSo()
    {
        using var harness = new PlanHarness();
        await Drafted(harness, "write the store");

        (await harness.Progress.AdvanceAsync(Guid.CreateVersion7().ToString(), StepStatus.InProgress))
            .IsRefused(Messages.StepNotFound, FailureTypes.StepNotFound);
    }

    /// Found in use: a session in another folder could see only the summary in the active list, and
    /// calling create_plan "to have a look" left a duplicate plan that had to be abandoned by hand.
    [Fact]
    public async Task Current_AskedForAnotherAgentsPlanById_ShowsItsStepsAndSaysItIsNotYours()
    {
        using var harness = new PlanHarness("agent-one", "project-one");
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.ProgressOf("agent-two", "project-two").CurrentAsync(planId: plan.Id.ToString());

        Assert.Contains("write the store", answer.Words(), StringComparison.Ordinal);
        Assert.Contains(Messages.SomeoneElsesPlan, answer.Words(), StringComparison.Ordinal);
        Assert.Contains("agent-one", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_AskedForItsOwnPlanById_DoesNotCallItSomeoneElses()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Progress.CurrentAsync(planId: plan.Id.ToString());

        Assert.DoesNotContain(Messages.SomeoneElsesPlan, answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_AskedForAPlanThatIsNotThere_SaysSo()
    {
        using var harness = new PlanHarness();

        (await harness.Progress.CurrentAsync(planId: Guid.CreateVersion7().ToString())).IsRefused(Messages.PlanNotFound, FailureTypes.PlanNotFound);
    }

    private static async Task<Plan> Drafted(PlanHarness harness, params string[] steps)
    {
        await harness.Drafting.DraftAsync("ship the server", steps);

        return ((await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName)).FirstOrDefault())!;
    }
}
