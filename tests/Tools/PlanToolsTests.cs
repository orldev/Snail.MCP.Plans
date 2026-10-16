using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Tests.TestDoubles;
using Snail.MCP.Plans.Tools;

namespace Snail.MCP.Plans.Tests.Tools;

/// A client that cancels a call expects the work to stop. The processes behind the tools took a token from the
/// start, but the tools never passed one, so every call ran with none: the cancellation stopped at the tool.
public class PlanToolsTests
{
    private readonly PlanTools _tools = new();

    [Fact]
    public async Task ListActivePlans_CalledWithACancelledToken_Stops()
    {
        using var harness = new PlanHarness();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tools.ListActivePlans(harness.Active, "", Cancelled()));
    }

    [Fact]
    public async Task GetCurrentPlan_CalledWithACancelledToken_Stops()
    {
        using var harness = new PlanHarness();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tools.GetCurrentPlan(harness.Progress, "", "", Cancelled()));
    }

    [Fact]
    public async Task CreatePlan_CalledWithACancelledToken_StoresNothing()
    {
        using var harness = new PlanHarness();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tools.CreatePlan(harness.Drafting, "ship the server", ["write the store"], "", Cancelled()));

        Assert.Empty(await harness.Plans.ActiveAsync(""));
    }

    [Fact]
    public async Task UpdateStep_CalledWithACancelledToken_Stops()
    {
        using var harness = new PlanHarness();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tools.UpdateStep(harness.Progress, Guid.CreateVersion7().ToString(), StepStatus.InProgress, "", "", Cancelled()));
    }

    [Fact]
    public async Task RevisePlan_CalledWithACancelledToken_Stops()
    {
        using var harness = new PlanHarness();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _tools.RevisePlan(harness.Revision, Guid.CreateVersion7().ToString(), 1, [], "", "", Cancelled()));
    }

    private static CancellationToken Cancelled() => new(canceled: true);
}
