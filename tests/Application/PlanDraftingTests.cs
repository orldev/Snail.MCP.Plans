using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.MCP.Plans.Tests.TestDoubles;

namespace Snail.MCP.Plans.Tests.Application;

/// Drafting is the first thing an agent does, and the one call that can be delivered twice.
public class PlanDraftingTests
{
    [Fact]
    public async Task Drafting_APlan_StoresItWithItsStepsInOrder()
    {
        using var harness = new PlanHarness();

        await harness.Drafting.DraftAsync("ship the server", ["write the store", "wire the tools"]);

        var stored = (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName)).FirstOrDefault();

        Assert.NotNull(stored);
        Assert.Equal("ship the server", stored.Objective);
        Assert.Equal(["write the store", "wire the tools"], stored.Steps.Select(step => step.Title));
        Assert.All(stored.Steps, step => Assert.Equal(StepStatus.Pending, step.Status));
    }

    /// MCP delivers at least once: a call whose answer was lost on the way back is retried, and a plain
    /// insert would leave the agent with two plans for one task.
    [Fact]
    public async Task Drafting_CalledTwice_LeavesOnePlanAndAnswersWithIt()
    {
        using var harness = new PlanHarness();

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        var second = await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        var active = await harness.Plans.ActiveAsync("");

        Assert.Single(active);
        Assert.True(second.TryGet(out var printed), second.Words());
        Assert.False(printed.Drafted);
        Assert.Contains(Messages.PlanAlreadyActive, printed.Text, StringComparison.Ordinal);
    }

    /// Knowing a plan must be closed first is useless without knowing how: settling is a parameter of
    /// a tool named after revising, which is not where anyone looks for it.
    [Fact]
    public async Task Drafting_WhileAPlanIsActive_NamesTheCallThatClosesIt()
    {
        using var harness = new PlanHarness();

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        var answer = await harness.Drafting.DraftAsync("ship something else", ["write something"]);

        Assert.Contains("revise_plan", answer.Words(), StringComparison.Ordinal);
        Assert.Contains(nameof(PlanStatus.Completed), answer.Words(), StringComparison.Ordinal);
        Assert.Contains("version", answer.Words(), StringComparison.Ordinal);
    }

    /// The loser of a race whose winner was settled before it could be read: there is no plan to answer with,
    /// and saying "already active" would send the agent looking for a plan that is gone.
    [Fact]
    public async Task Drafting_OutracedByAPlanSettledBeforeItCouldBeRead_SaysToDraftAgain()
    {
        var drafting = new PlanDrafting(new RefusingStore(), new FixedIdentity("agent-one", "project-one"), TimeProvider.System);

        var answer = await drafting.DraftAsync("ship the server", ["write the store"]);

        answer.IsRefused(Messages.DraftOutraced, FailureTypes.Outraced);
    }

    [Fact]
    public async Task Drafting_WithoutAnObjective_IsRefused()
    {
        using var harness = new PlanHarness();

        (await harness.Drafting.DraftAsync("  ", ["write the store"])).IsRefused(Messages.ObjectiveRequired, FailureTypes.Arguments);
    }

    [Fact]
    public async Task Drafting_WithoutSteps_IsRefused()
    {
        using var harness = new PlanHarness();

        (await harness.Drafting.DraftAsync("ship the server", ["", "   "])).IsRefused(Messages.StepsRequired, FailureTypes.Arguments);
    }

    /// Found in use: a session in another folder called create_plan to look at an existing plan and
    /// silently got a duplicate. Both plans are legitimate, so the answer names the neighbour and says
    /// how to read it and how to abandon this one.
    [Fact]
    public async Task Drafting_InAProjectAnotherAgentWorksIn_NamesTheirPlanAndHowToUndoThisOne()
    {
        using var harness = new PlanHarness("agent-one", "project-one");

        await harness.DraftingOf("agent-two", "project-one").DraftAsync("review the docs", ["read the spec"]);
        var answer = await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        var theirs = (await harness.Plans.ActiveAsync("project-one")).Single(plan => plan.AgentId == "agent-two");

        Assert.Contains(Messages.AnotherAgentIsHere, answer.Words(), StringComparison.Ordinal);
        Assert.Contains(theirs.Id.ToString(), answer.Words(), StringComparison.Ordinal);
        Assert.Contains("review the docs", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drafting_InAProjectNobodyElseWorksIn_SaysNothingAboutNeighbours()
    {
        using var harness = new PlanHarness();

        var answer = await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        Assert.DoesNotContain(Messages.AnotherAgentIsHere, answer.Words(), StringComparison.Ordinal);
    }

    /// Two agents in one project are two plans: the identity comes from the process, so neither sees
    /// the other's draft as its own.
    [Fact]
    public async Task Drafting_ByASecondAgentInTheSameProject_IsItsOwnPlan()
    {
        using var harness = new PlanHarness("agent-one", "project-one");

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        await harness.DraftingOf("agent-two", "project-one").DraftAsync("review the server", ["read the store"]);

        var active = await harness.Plans.ActiveAsync("project-one");

        Assert.Equal(2, active.Count);
        Assert.Equal(["agent-one", "agent-two"], active.Select(plan => plan.AgentId).OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>A store whose unique index has just refused the draft and whose winner is already gone.</summary>
    private sealed class RefusingStore : IPlanStore
    {
        public Task<PlanListing> CurrentAsync(string agentId, string projectName, CancellationToken cancellationToken = default) => Task.FromResult(new PlanListing([], 0, 0));

        public Task<PlanListing> FindAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(new PlanListing([], 0, 0));

        public Task<PlanListing> ActiveAsync(string projectName, CancellationToken cancellationToken = default) => Task.FromResult(new PlanListing([], 0, 0));

        public Task<bool> TryAddAsync(Plan plan, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> TrySaveAsync(Plan plan, int expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
