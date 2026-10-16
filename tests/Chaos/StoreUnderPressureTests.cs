using Microsoft.Data.Sqlite;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Tests.TestDoubles;

namespace Snail.MCP.Plans.Tests.Chaos;

/// What the server does when two sessions of one agent hit the same file at the same moment, when the
/// file holds something it did not write, and when the caller gives up mid-call. The invariants are the
/// database's, so these tests ask what a refusal from the database looks like to the agent.
public class StoreUnderPressureTests
{
    /// <summary>
    /// Two sessions in one folder share an identity on purpose, and MCP delivers at least once, so several
    /// create_plan calls can be in flight together. Each checks for an active plan before inserting, and
    /// between that check and the insert stands the unique index — the only thing holding the invariant.
    /// Twenty callers, because two rarely interleave on one machine and the window is microseconds wide.
    /// </summary>
    [Fact]
    public async Task Drafting_RacedByTwentySessionsOfOneAgent_AnswersEveryCallerWithoutARawDatabaseError()
    {
        using var harness = new PlanHarness();

        var drafting = Enumerable
            .Range(0, 20)
            .Select(_ => harness.DraftingOf(harness.Identity.AgentId, harness.Identity.ProjectName))
            .ToList();

        var answers = await Task.WhenAll(drafting.Select(one =>
            Task.Run(() => one.DraftAsync("ship the server", ["write the store"]))));

        Assert.All(answers, answer => Assert.False(string.IsNullOrWhiteSpace(answer)));
        Assert.Single(await harness.Plans.ActiveAsync(""));
    }

    /// <summary>
    /// The same race on a step: both sessions read the plan, both decide the slot is free, both write. Here
    /// the plan's version stands in front of the index, so the loser should be told the plan moved.
    /// </summary>
    [Fact]
    public async Task Advancing_RacedByTwoSessionsOfOneAgent_LeavesOneStepInProgressAndTellsTheLoser()
    {
        using var harness = new PlanHarness();
        await harness.Drafting.DraftAsync("ship the server", ["first", "second"]);
        var plan = (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName))!;
        var second = harness.ProgressOf(harness.Identity.AgentId, harness.Identity.ProjectName);

        var answers = await Task.WhenAll(
            Task.Run(() => harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress)),
            Task.Run(() => second.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress)));

        var reread = await harness.Plans.FindAsync(plan.Id);

        Assert.Equal(1, reread!.Steps.Count(step => step.Status is StepStatus.InProgress));
        Assert.Contains(answers, answer => answer.Contains(Messages.VersionConflict, StringComparison.Ordinal)
            || answer.Contains("one step at a time", StringComparison.Ordinal));
    }

    /// <summary>
    /// The owner reads this database with sqlite3 and has written to it by hand. A row whose id is not a
    /// Guid — a hand edit, a half-written file, a future column — reaches Guid.Parse on a read path.
    /// </summary>
    [Fact]
    public async Task Reading_APlanWhoseStoredIdIsNotAGuid_DoesNotCrashTheTool()
    {
        using var harness = new PlanHarness();
        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        await using (var connection = new SqliteConnection($"Data Source={harness.DatabaseFile}"))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO "Plans" ("Id", "AgentId", "ProjectName", "Objective", "Summary", "Status", "Version", "Created", "Updated", "Completed")
                VALUES ('not-a-guid', 'agent-two', 'project-two', 'edited by hand', '', 'Active', 1, 0, 0, NULL);
                """;
            await insert.ExecuteNonQueryAsync();
        }

        var answer = await Record.ExceptionAsync(() => harness.Active.RenderAsync());

        Assert.Null(answer);
    }

    /// <summary>
    /// The same hand edit on a step. Passing the row over on read is not enough: a revision writes the plan
    /// back whole, and a writer that removes every row it did not read would delete the step it could not parse.
    /// </summary>
    [Fact]
    public async Task Revising_APlanHoldingAStepWhoseStoredIdIsNotAGuid_KeepsThatRowAndAnswers()
    {
        using var harness = new PlanHarness();
        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        var plan = (await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName))!;

        await ExecuteAsync(harness, $"""
            INSERT INTO "Steps" ("Id", "PlanId", "OrderIndex", "Title", "Status", "Note", "Attempts", "Started", "Completed")
            VALUES ('not-a-guid', '{plan.Id.ToString().ToUpperInvariant()}', 999, 'edited by hand', 'Pending', '', 0, NULL, NULL);
            """);

        var answer = await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [new StepChange(ChangeKind.Add, Title: "wire the tools")]);

        Assert.Contains("wire the tools", answer, StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync(harness, """SELECT COUNT(*) FROM "Steps" WHERE "Id" = 'not-a-guid';"""));
    }

    private static async Task ExecuteAsync(PlanHarness harness, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={harness.DatabaseFile}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(PlanHarness harness, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={harness.DatabaseFile}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>A caller that gave up must stop the work, not wait for it: the token reaches the store.</summary>
    [Fact]
    public async Task Reading_WithATokenAlreadyCancelled_StopsInsteadOfAnswering()
    {
        using var harness = new PlanHarness();
        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Plans.ActiveAsync("", cancelled.Token));
    }
}
