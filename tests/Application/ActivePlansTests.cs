using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Tests.TestDoubles;

namespace Snail.MCP.Plans.Tests.Application;

/// The read a second session makes: what is running everywhere, and how far along it is.
public class ActivePlansTests
{
    [Fact]
    public async Task Active_AskedFromASecondSession_ShowsEveryAgentsPlan()
    {
        using var harness = new PlanHarness("agent-one", "project-one");

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        await harness.DraftingOf("agent-two", "project-two").DraftAsync("review the docs", ["read the spec"]);

        var answer = await harness.Active.RenderAsync();

        Assert.Contains("agent-one", answer, StringComparison.Ordinal);
        Assert.Contains("agent-two", answer, StringComparison.Ordinal);
        Assert.Contains("ship the server", answer, StringComparison.Ordinal);
        Assert.Contains("review the docs", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_NarrowedToAProject_LeavesTheOthersOut()
    {
        using var harness = new PlanHarness("agent-one", "project-one");

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        await harness.DraftingOf("agent-two", "project-two").DraftAsync("review the docs", ["read the spec"]);

        var answer = await harness.Active.RenderAsync("project-two");

        Assert.Contains("review the docs", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("ship the server", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_WithNothingRunning_SaysSo() =>
        Assert.Equal(Messages.NoActivePlans, await new PlanHarness().Active.RenderAsync());

    /// The owner's actual question — how far along is it — answered by counts rather than a percentage,
    /// because a percentage over a revisable step list improves when steps are dropped.
    [Fact]
    public async Task Active_ReportingProgress_CountsStatusesAndShowsNoPercentage()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools", "write the readme");

        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.Done, "stored");
        await harness.Progress.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress);

        var answer = await harness.Active.RenderAsync();

        Assert.Contains($"1 {nameof(StepStatus.Done)}", answer, StringComparison.Ordinal);
        Assert.Contains($"1 {nameof(StepStatus.InProgress)}", answer, StringComparison.Ordinal);
        Assert.Contains($"1 {nameof(StepStatus.Pending)}", answer, StringComparison.Ordinal);
        Assert.DoesNotContain("%", answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_ShowsTheStepBeingWorkedOn()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store", "wire the tools");

        await harness.Progress.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress);

        var answer = await harness.Active.RenderAsync();

        Assert.Contains("current step: wire the tools", answer, StringComparison.Ordinal);
    }

    /// The age of the last write is what exposes an agent that stopped, which is the one thing a
    /// timestamp in the answer could not tell the reader.
    [Fact]
    public async Task Active_ForAPlanUntouchedForHours_ReportsHowLongItHasBeenStill()
    {
        using var harness = new PlanHarness();
        await Drafted(harness, "write the store");

        harness.Clock.Advance(TimeSpan.FromHours(3));

        Assert.Contains("updated: 3 hours ago", await harness.Active.RenderAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_AfterAPlanIsFinished_LeavesItOut()
    {
        using var harness = new PlanHarness();
        var plan = await Drafted(harness, "write the store");
        var step = plan.Steps[0].Id.ToString();

        await harness.Progress.AdvanceAsync(step, StepStatus.InProgress);
        await harness.Progress.AdvanceAsync(step, StepStatus.Done, "stored");

        Assert.Equal(Messages.NoActivePlans, await harness.Active.RenderAsync());
    }

    /// The default identity is the folder, which is also the project name, so the heading would
    /// otherwise carry the same word twice on every line the owner reads.
    [Fact]
    public async Task Active_ForAnAgentNamedAfterItsProject_NamesItOnce()
    {
        using var harness = new PlanHarness("project-one", "project-one");

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        Assert.Contains("## project-one\n", await harness.Active.RenderAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_ForAnAgentNamedApart_NamesBoth()
    {
        using var harness = new PlanHarness("agent-one", "project-one");

        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        Assert.Contains("## project-one · agent-one", await harness.Active.RenderAsync(), StringComparison.Ordinal);
    }

    private static async Task<Plan> Drafted(PlanHarness harness, params string[] steps)
    {
        await harness.Drafting.DraftAsync("ship the server", steps);

        return (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName))!;
    }
}
