using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Tests.TestDoubles;

namespace Snail.MCP.Plans.Tests.Infrastructure;

/// The invariants that live in the database rather than in the code, because two sessions write the
/// same file and a check in the application would be a race between them.
public class PlanStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Store_AskedForASecondActivePlanOfOneAgentAndProject_RefusesInTheDatabase()
    {
        using var harness = new PlanHarness();

        await harness.Plans.AddAsync(PlanOf("agent-one", "project-one"));

        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => harness.Plans.AddAsync(PlanOf("agent-one", "project-one")));
    }

    [Fact]
    public async Task Store_AskedForTwoPlansOfOneAgentInDifferentProjects_AcceptsBoth()
    {
        using var harness = new PlanHarness();

        await harness.Plans.AddAsync(PlanOf("agent-one", "project-one"));
        await harness.Plans.AddAsync(PlanOf("agent-one", "project-two"));

        Assert.Equal(2, (await harness.Plans.ActiveAsync("")).Count);
    }

    /// A plan runs one step at a time. The application refuses it with a readable answer, and the index
    /// is what makes the refusal true even when two processes ask at once.
    [Fact]
    public async Task Store_AskedForTwoStepsInProgressInOnePlan_RefusesInTheDatabase()
    {
        using var harness = new PlanHarness();
        var plan = PlanOf("agent-one", "project-one", StepStatus.Pending, StepStatus.Pending);

        await harness.Plans.AddAsync(plan);

        var busy = plan with
        {
            Version = plan.Version + 1,
            Steps = [.. plan.Steps.Select(step => step with { Status = StepStatus.InProgress })]
        };

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => harness.Plans.TrySaveAsync(busy, plan.Version));
    }

    [Fact]
    public async Task Store_AskedForOneStepInProgressInEachOfTwoPlans_AcceptsBoth()
    {
        using var harness = new PlanHarness();

        foreach (var project in new[] { "project-one", "project-two" })
        {
            var plan = PlanOf("agent-one", project, StepStatus.Pending);

            await harness.Plans.AddAsync(plan);

            var started = plan with
            {
                Version = plan.Version + 1,
                Steps = [plan.Steps[0] with { Status = StepStatus.InProgress }]
            };

            Assert.True(await harness.Plans.TrySaveAsync(started, plan.Version));
        }
    }

    [Fact]
    public async Task Store_WritingOnAVersionThatHasMoved_WritesNothing()
    {
        using var harness = new PlanHarness();
        var plan = PlanOf("agent-one", "project-one", StepStatus.Pending);

        await harness.Plans.AddAsync(plan);

        Assert.True(await harness.Plans.TrySaveAsync(plan with { Version = 2, Summary = "first" }, 1));
        Assert.False(await harness.Plans.TrySaveAsync(plan with { Version = 2, Summary = "second" }, 1));

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal("first", reread!.Summary);
    }

    [Fact]
    public async Task Store_ListingActivePlans_PutsTheMostRecentlyTouchedFirst()
    {
        using var harness = new PlanHarness();
        var older = PlanOf("agent-one", "project-one") with { UpdatedAt = Now.AddHours(-2) };
        var newer = PlanOf("agent-two", "project-one") with { UpdatedAt = Now };

        await harness.Plans.AddAsync(older);
        await harness.Plans.AddAsync(newer);

        var listed = await harness.Plans.ActiveAsync("project-one");

        Assert.Equal(["agent-two", "agent-one"], listed.Select(plan => plan.AgentId));
    }

    /// Write-ahead logging is what lets the second session read the file while the first one writes it,
    /// so the mode is asserted on the real file rather than assumed from the pragma being issued.
    [Fact]
    public async Task Store_OnceOpened_LeavesTheFileInWriteAheadLoggingMode()
    {
        using var harness = new PlanHarness();

        await harness.Plans.AddAsync(PlanOf("agent-one", "project-one"));

        await using var connection = new SqliteConnection($"Data Source={harness.DatabaseFile}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        Assert.Equal("wal", (string?)await command.ExecuteScalarAsync());
    }

    private static Plan PlanOf(string agentId, string projectName, params StepStatus[] statuses) => new()
    {
        Id = Guid.CreateVersion7(),
        AgentId = agentId,
        ProjectName = projectName,
        Objective = "ship the server",
        Version = 1,
        CreatedAt = Now,
        UpdatedAt = Now,
        Steps = [.. statuses.Select((status, position) => new Step
        {
            Id = Guid.CreateVersion7(),
            OrderIndex = (position + 1) * Ordering.Gap,
            Title = $"step {position + 1}",
            Status = status
        })]
    };
}
