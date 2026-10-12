using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Tests.TestDoubles;

namespace Snail.MCP.Plans.Tests.Application;

/// Revising the plan as the work turns out different from the draft.
public class PlanRevisionTests
{
    /// Two sessions share one database file. Without the version check the later revision would swallow
    /// the earlier one and no one would learn of it.
    [Fact]
    public async Task Revision_BuiltOnAVersionThatHasMoved_WritesNothing()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [Add("wire the tools")]);
        var late = await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [Add("write the readme")]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(Messages.VersionConflict, late);
        Assert.Equal(["write the store", "wire the tools"], reread!.Steps.Select(step => step.Title));
    }

    [Fact]
    public async Task Revision_AddingAStep_PutsItAtTheEnd()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [Add("write the readme")]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(["write the store", "wire the tools", "write the readme"], reread!.Steps.Select(step => step.Title));
    }

    /// The insertion the first specification could not do at all: a step discovered to belong in the
    /// middle of work already planned.
    [Fact]
    public async Task Revision_AddingAStepAfterAnother_PutsItBetweenThem()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [Add("write the schema") with { After = plan.Steps[0].Id.ToString() }]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(["write the store", "write the schema", "wire the tools"], reread!.Steps.Select(step => step.Title));
    }

    [Fact]
    public async Task Revision_Reordering_MovesTheStepWithoutLosingAny()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools", "write the readme");

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [new StepChange(ChangeKind.Reorder, StepId: plan.Steps[2].Id.ToString())]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(["write the readme", "write the store", "wire the tools"], reread!.Steps.Select(step => step.Title));
    }

    /// A step taken out of the work stays on the record with its reason: progress another session reads
    /// must not be improvable by dropping the inconvenient steps.
    [Fact]
    public async Task Revision_SkippingAStep_KeepsItInThePlanWithItsReason()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [new StepChange(ChangeKind.Skip, StepId: plan.Steps[1].Id.ToString(), Reason: "the tools moved to another server")]);

        var reread = await harness.Plans.FindAsync(plan.Id);
        var skipped = reread!.Steps.Single(step => step.Status is StepStatus.Skipped);

        Assert.Equal(2, reread.Steps.Count);
        Assert.Equal("the tools moved to another server", skipped.Note);
    }

    [Fact]
    public async Task Revision_SkippingWithoutSayingWhy_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [new StepChange(ChangeKind.Skip, StepId: plan.Steps[0].Id.ToString())]);

        Assert.Equal(Messages.SkipReasonRequired, answer);
    }

    [Fact]
    public async Task Revision_RewordingAFinishedStep_IsRefused()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(step, StepStatus.Done, "stored");

        var current = await harness.Plans.FindAsync(plan.Id);

        var answer = await harness.Revision.ReviseAsync(
            plan.Id.ToString(), current!.Version,
            [new StepChange(ChangeKind.Edit, Title: "write the store differently", StepId: step)]);

        Assert.Contains("record of what happened", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revision_OfAnotherAgentsPlan_IsRefused()
    {
        using var harness = new PlanHarness("agent-one", "project-one");
        var plan = await Drafted(harness, "write the store");

        var answer = await harness
            .RevisionOf("agent-two", "project-one")
            .ReviseAsync(plan.Id.ToString(), plan.Version, [Add("wire the tools")]);

        Assert.Contains("agent-one", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revision_DeclaringAnOutcome_SettlesThePlanEvenWithStepsLeft()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version, [],
            summary: "the approach does not work",
            outcome: nameof(PlanStatus.Failed));

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(PlanStatus.Failed, reread!.Status);
        Assert.Equal("the approach does not work", reread.Summary);
        Assert.Empty(await harness.Plans.ActiveAsync(""));
    }

    /// An automatic completion has to be undoable, or the agent that remembers one more step is stuck
    /// with a plan it cannot touch.
    [Fact]
    public async Task Revision_AddingAStepToACompletedPlan_ReopensIt()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(step, StepStatus.Done, "stored");

        var completed = await harness.Plans.FindAsync(plan.Id);

        await harness.Revision.ReviseAsync(plan.Id.ToString(), completed!.Version, [Add("write the readme")]);

        var reopened = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(PlanStatus.Active, reopened!.Status);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Revision_OfAPlanThatIsNotThere_SaysSo()
    {
        using var harness = new PlanHarness();

        Assert.Equal(
            Messages.PlanNotFound,
            await harness.Revision.ReviseAsync(Guid.CreateVersion7().ToString(), 1, [Add("write the store")]));
    }

    [Fact]
    public async Task Revision_WithAnUnknownOutcome_NamesTheAllowedOnes()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");

        var answer = await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [], outcome: "finished");

        foreach (var status in Enum.GetNames<PlanStatus>())
            Assert.Contains(status, answer, StringComparison.Ordinal);
    }

    /// Found in use: two reorders in one call answered with a new version while the order stayed as it
    /// was. Both move a step after another, which is the branch that read the order from the indexes —
    /// and inside one revision those still describe the state the call started from.
    [Fact]
    public async Task Revision_WithTwoReordersAfterAStep_AppliesBoth()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "alpha", "beta", "gamma", "delta");

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [
                new StepChange(ChangeKind.Reorder, StepId: plan.Steps[0].Id.ToString(), After: plan.Steps[2].Id.ToString()),
                new StepChange(ChangeKind.Reorder, StepId: plan.Steps[3].Id.ToString(), After: plan.Steps[1].Id.ToString())
            ]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(["beta", "delta", "gamma", "alpha"], reread!.Steps.Select(step => step.Title));
    }

    [Fact]
    public async Task Revision_AddingAfterAStepThenReordering_KeepsBothChanges()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "alpha", "beta", "gamma");

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [
                new StepChange(ChangeKind.Add, Title: "inserted", After: plan.Steps[0].Id.ToString()),
                new StepChange(ChangeKind.Reorder, StepId: plan.Steps[2].Id.ToString(), After: plan.Steps[0].Id.ToString())
            ]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(["alpha", "gamma", "inserted", "beta"], reread!.Steps.Select(step => step.Title));
    }

    /// Found in use: three steps added in one call with the same 'after' did not end up next to each
    /// other, because each landed directly behind the anchor and so in front of the previous arrival.
    [Fact]
    public async Task Revision_AddingSeveralStepsAfterTheSameOne_KeepsTheOrderTheyWereAskedIn()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "alpha", "omega");
        var anchor = plan.Steps[0].Id.ToString();

        await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version,
            [
                new StepChange(ChangeKind.Add, Title: "first", After: anchor),
                new StepChange(ChangeKind.Add, Title: "second", After: anchor),
                new StepChange(ChangeKind.Add, Title: "third", After: anchor)
            ]);

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(["alpha", "first", "second", "third", "omega"], reread!.Steps.Select(step => step.Title));
    }

    /// The ids of what was just added are what the next update_step call needs, and on a long plan the
    /// printed step list runs past the answer limit — so the answer names them up front.
    [Fact]
    public async Task Revision_ThatAddedSteps_NamesTheirIdsBeforeThePlan()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "alpha");

        var answer = await harness.Revision.ReviseAsync(
            plan.Id.ToString(), plan.Version, [Add("beta"), Add("gamma")]);

        var reread = await harness.Plans.FindAsync(plan.Id);
        var added = reread!.Steps.Where(step => step.Title is "beta" or "gamma").ToList();

        Assert.Contains("added 2:", answer, StringComparison.Ordinal);
        Assert.All(added, step => Assert.Contains(step.Id.ToString(), answer, StringComparison.Ordinal));
    }

    private static StepChange Add(string title) => new(ChangeKind.Add, Title: title);

    private static async Task<Plan> Drafted(PlanHarness harness, params string[] steps)
    {
        await harness.Drafting.DraftAsync("ship the server", steps);

        return (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName))!;
    }
}
