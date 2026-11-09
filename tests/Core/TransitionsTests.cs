using Snail.MCP.Plans.Core;

namespace Snail.MCP.Plans.Tests.Core;

/// The transition table decides what update_step accepts, so every rule it states is asserted here
/// rather than inferred from the code that reads it.
public class TransitionsTests
{
    [Theory]
    [InlineData(StepStatus.Pending, StepStatus.InProgress)]
    [InlineData(StepStatus.Pending, StepStatus.Skipped)]
    [InlineData(StepStatus.Pending, StepStatus.Done)]
    [InlineData(StepStatus.Pending, StepStatus.Failed)]
    [InlineData(StepStatus.InProgress, StepStatus.Done)]
    [InlineData(StepStatus.InProgress, StepStatus.Failed)]
    [InlineData(StepStatus.InProgress, StepStatus.Skipped)]
    [InlineData(StepStatus.Failed, StepStatus.InProgress)]
    [InlineData(StepStatus.Failed, StepStatus.Skipped)]
    [InlineData(StepStatus.InProgress, StepStatus.Pending)]
    public void Step_MovingAlongTheTable_IsAllowed(StepStatus from, StepStatus to) =>
        Assert.True(Transitions.CanMove(from, to));

    [Theory]
    [InlineData(StepStatus.Pending, StepStatus.Pending)]
    [InlineData(StepStatus.Failed, StepStatus.Done)]
    [InlineData(StepStatus.Done, StepStatus.InProgress)]
    [InlineData(StepStatus.Done, StepStatus.Failed)]
    [InlineData(StepStatus.Skipped, StepStatus.InProgress)]
    [InlineData(StepStatus.Skipped, StepStatus.Pending)]
    public void Step_MovingOffTheTable_IsRefused(StepStatus from, StepStatus to) =>
        Assert.False(Transitions.CanMove(from, to));

    /// A step not taken up may be finished in one call, which counts as taking it up; a failed one is taken up again
    /// first, so a retry stays visible as its own attempt.
    [Fact]
    public void Step_FinishedInOneCall_StartsFromPendingOnly() =>
        Assert.True(Transitions.TakesUp(StepStatus.Pending, StepStatus.Done) && !Transitions.TakesUp(StepStatus.InProgress, StepStatus.Done));

    /// A step taken up by mistake has to have a way back, or the only escape from the plan's single
    /// slot is reporting a failure that never happened.
    [Fact]
    public void Step_TakenUpByMistake_CanBePutBack() =>
        Assert.True(Transitions.CanMove(StepStatus.InProgress, StepStatus.Pending));

    [Fact]
    public void Step_ThatFailed_CanBeAttemptedAgain() =>
        Assert.Contains(StepStatus.InProgress, Transitions.AllowedFrom(StepStatus.Failed));

    [Theory]
    [InlineData(StepStatus.Done)]
    [InlineData(StepStatus.Skipped)]
    public void Step_ThatSettled_MovesNowhere(StepStatus status) =>
        Assert.Empty(Transitions.AllowedFrom(status));

    /// Failed is deliberately not terminal: a plan holding a failed step stays active, which is what
    /// stops a failure from being left behind unreported.
    [Fact]
    public void Failure_IsNotTerminal() =>
        Assert.False(Transitions.IsTerminal(StepStatus.Failed));

    [Fact]
    public void EveryStatus_IsCoveredByTheTable()
    {
        foreach (var status in Enum.GetValues<StepStatus>())
            Assert.True(Transitions.AllowedFrom(status).Count > 0 || Transitions.IsTerminal(status),
                $"{status} allows no transition and is not terminal, so a step reaching it would be stuck.");
    }
}
