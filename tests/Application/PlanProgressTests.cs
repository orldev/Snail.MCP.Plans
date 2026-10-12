using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Tests.TestDoubles;

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

        Assert.Contains("ship the server", answer, StringComparison.Ordinal);
        Assert.Contains("write the store", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_ForAnAgentThatNeverDrafted_SaysThereIsNone()
    {
        using var harness = new PlanHarness();

        Assert.Equal(Messages.NoCurrentPlan, await harness.Progress.CurrentAsync());
    }

    /// The largest saving in the whole server: reporting a step and learning the next one is one call.
    [Fact]
    public async Task Advance_FinishingAStep_AnswersWithTheNextPendingStep()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Done, "stored");

        Assert.Contains("wire the tools", answer, StringComparison.Ordinal);
        Assert.Contains(plan.Steps[1].Id.ToString(), answer, StringComparison.Ordinal);
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

        var reread = await harness.Plans.FindAsync(plan.Id);

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

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(PlanStatus.Active, reread!.Status);
        Assert.Contains(Messages.AfterFailure, answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_WhileAnotherStepIsInProgress_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress);

        Assert.Contains("one step at a time", answer, StringComparison.Ordinal);
        Assert.Contains("write the store", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_OffTheTransitionTable_NamesWhatIsAllowedInstead()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Done);

        Assert.Contains(nameof(StepStatus.InProgress), answer, StringComparison.Ordinal);
        Assert.Contains(nameof(StepStatus.Skipped), answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_SkippingWithoutSayingWhy_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        Assert.Equal(
            Messages.SkipReasonRequired,
            await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Skipped));
    }

    [Fact]
    public async Task Advance_FinishingTheLastOpenStep_CompletesThePlanWithoutAClosingCall()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(step, StepStatus.Done, "stored");

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(PlanStatus.Completed, reread!.Status);
        Assert.Contains(Messages.PlanFinished, answer, StringComparison.Ordinal);
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

        Assert.Equal(Messages.NoCurrentPlan, answer);
    }

    /// Found live: with the last step taken up there is no Pending step to hand over, and the answer
    /// announced the plan settled while the agent was still working on it.
    [Fact]
    public async Task Advance_TakingUpTheLastStep_DoesNotCallThePlanFinished()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);

        Assert.Contains(Messages.LastOpenStep, answer, StringComparison.Ordinal);
        Assert.DoesNotContain(Messages.PlanFinished, answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advance_LeavingAFailedStepAndNothingPending_DoesNotCallThePlanFinished()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        var answer = await harness.Progress.AdvanceAsync(step, StepStatus.Failed, "the file was locked");

        Assert.DoesNotContain(Messages.PlanFinished, answer, StringComparison.Ordinal);
        Assert.Contains(Messages.OnlyFailureLeft, answer, StringComparison.Ordinal);
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

        var reread = await harness.Plans.FindAsync(plan.Id);

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

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(1, reread!.Steps[0].Attempts);
        Assert.Null(reread.Steps[0].CompletedAt);
    }

    [Fact]
    public async Task Advance_OnAStepThatIsNotInThePlan_SaysSo()
    {
        using var harness = new PlanHarness();
        await Drafted(harness, "write the store");

        Assert.Equal(
            Messages.StepNotFound,
            await harness.Progress.AdvanceAsync(Guid.CreateVersion7().ToString(), StepStatus.InProgress));
    }

    /// Found in use: a session in another folder could see only the summary in the active list, and
    /// calling create_plan "to have a look" left a duplicate plan that had to be abandoned by hand.
    [Fact]
    public async Task Current_AskedForAnotherAgentsPlanById_ShowsItsStepsAndSaysItIsNotYours()
    {
        using var harness = new PlanHarness("agent-one", "project-one");
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.ProgressOf("agent-two", "project-two").CurrentAsync(planId: plan.Id.ToString());

        Assert.Contains("write the store", answer, StringComparison.Ordinal);
        Assert.Contains(Messages.SomeoneElsesPlan, answer, StringComparison.Ordinal);
        Assert.Contains("agent-one", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_AskedForItsOwnPlanById_DoesNotCallItSomeoneElses()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Progress.CurrentAsync(planId: plan.Id.ToString());

        Assert.DoesNotContain(Messages.SomeoneElsesPlan, answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Current_AskedForAPlanThatIsNotThere_SaysSo()
    {
        using var harness = new PlanHarness();

        Assert.Equal(Messages.PlanNotFound, await harness.Progress.CurrentAsync(planId: Guid.CreateVersion7().ToString()));
    }

    private static async Task<Plan> Drafted(PlanHarness harness, params string[] steps)
    {
        await harness.Drafting.DraftAsync("ship the server", steps);

        return (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName))!;
    }
}
