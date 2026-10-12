using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Tests.TestDoubles;

namespace Snail.MCP.Plans.Tests.Core;

/// What a long plan costs to read. Measured in use: on plans of 46 and 79 steps the notes of finished
/// steps filled the answer and the ids of later steps fell past the limit.
public class OutputBudgetTests
{
    [Fact]
    public void Note_OfASettledStep_IsShortened()
    {
        var note = new string('x', OutputBudget.SettledNoteLength + 50);

        Assert.Equal(OutputBudget.SettledNoteLength + 1, OutputBudget.Note(note, isSettled: true).Length);
    }

    /// An open step's note is what the agent is working from, not a record to look up later.
    [Fact]
    public void Note_OfAnOpenStep_IsKeptWhole()
    {
        var note = new string('x', OutputBudget.SettledNoteLength + 50);

        Assert.Equal(note, OutputBudget.Note(note, isSettled: false));
    }

    [Fact]
    public void Note_ShorterThanTheLimit_IsLeftAlone() =>
        Assert.Equal("done quickly", OutputBudget.Note("done quickly", isSettled: true));

    [Fact]
    public async Task Plan_WithLongNotesOnFinishedSteps_StillPrintsEveryStepId()
    {
        using var harness = new PlanHarness();

        await harness.Drafting.DraftAsync("ship it", [.. Enumerable.Range(1, 60).Select(number => $"step {number}")]);

        var plan = (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName))!;
        var essay = new string('n', 400);

        foreach (var step in plan.Steps.Take(55))
        {
            await harness.Progress.AdvanceAsync(step.Id.ToString(), StepStatus.InProgress);
            await harness.Progress.AdvanceAsync(step.Id.ToString(), StepStatus.Done, essay);
        }

        var answer = await harness.Progress.CurrentAsync();
        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.DoesNotContain(OutputBudget.TruncationMarker, answer, StringComparison.Ordinal);
        Assert.All(reread!.Steps, step => Assert.Contains(step.Id.ToString(), answer, StringComparison.Ordinal));
    }
}
