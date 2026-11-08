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

        Assert.All(answers, answer => Assert.False(string.IsNullOrWhiteSpace(answer.Words())));
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
        var plan = ((await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName)).FirstOrDefault())!;
        var second = harness.ProgressOf(harness.Identity.AgentId, harness.Identity.ProjectName);

        var answers = await Task.WhenAll(
            Task.Run(() => harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress)),
            Task.Run(() => second.AdvanceAsync(plan.Steps[1].Id.ToString(), StepStatus.InProgress)));

        var reread = (await harness.Plans.FindAsync(plan.Id)).FirstOrDefault();

        Assert.Equal(1, reread!.Steps.Count(step => step.Status is StepStatus.InProgress));
        Assert.Contains(answers, answer => answer.Type is FailureTypes.VersionConflict or FailureTypes.StepInProgress);
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
        var plan = ((await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName)).FirstOrDefault())!;

        await ExecuteAsync(harness, $"""
            INSERT INTO "Steps" ("Id", "PlanId", "OrderIndex", "Title", "Status", "Note", "Attempts", "Started", "Completed")
            VALUES ('not-a-guid', '{plan.Id.ToString().ToUpperInvariant()}', 999, 'edited by hand', 'Pending', '', 0, NULL, NULL);
            """);

        var answer = await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [new StepChange(ChangeKind.Add, Title: "wire the tools")]);

        Assert.Contains("wire the tools", answer.Words(), StringComparison.Ordinal);
        Assert.Equal(1L, await ScalarAsync(harness, """SELECT COUNT(*) FROM "Steps" WHERE "Id" = 'not-a-guid';"""));
    }

    /// <summary>
    /// Passing an unreadable row over keeps the other plans alive, but an answer that then says nothing is running
    /// lies when the file holds a plan it could not read: "nothing matched" and "nothing readable" differ.
    /// </summary>
    [Fact]
    public async Task Listing_AFileWhoseOnlyActivePlanIsUnreadable_SaysARowWasNotReadRatherThanNothingRuns()
    {
        using var harness = new PlanHarness();
        await harness.Plans.ActiveAsync("");
        await ExecuteAsync(harness, UnreadablePlan("agent-two", "project-two"));

        var answer = await harness.Active.RenderAsync();

        Assert.NotEqual(Messages.NoActivePlans, answer.Words());
        Assert.Contains("could not be read", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listing_AReadableAndAnUnreadablePlan_ShowsTheOneAndCountsTheOther()
    {
        using var harness = new PlanHarness();
        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        await ExecuteAsync(harness, UnreadablePlan("agent-two", "project-two"));

        var answer = await harness.Active.RenderAsync();

        Assert.Contains("ship the server", answer.Words(), StringComparison.Ordinal);
        Assert.Contains("1 row", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Listing_APlanWithAnUnreadableStep_ShowsThePlanAndCountsTheStep()
    {
        using var harness = new PlanHarness();
        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        var plan = ((await harness.Plans.CurrentAsync(harness.Identity.AgentId, harness.Identity.ProjectName)).FirstOrDefault())!;

        await ExecuteAsync(harness, $"""
            INSERT INTO "Steps" ("Id", "PlanId", "OrderIndex", "Title", "Status", "Note", "Attempts", "Started", "Completed")
            VALUES ('not-a-guid', '{plan.Id.ToString().ToUpperInvariant()}', 999, 'edited by hand', 'Pending', '', 0, NULL, NULL);
            """);

        var answer = await harness.Active.RenderAsync();

        Assert.Contains("ship the server", answer.Words(), StringComparison.Ordinal);
        Assert.Contains($"1 row {Messages.UnreadableRows}", answer.Words(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An unreadable active plan of this very agent and project blocks a new one through the unique index, while
    /// every read passes it over. Without naming it, the refusal read as a race lost to a plan already settled, and
    /// "draft again" sent the agent into the same refusal.
    /// </summary>
    [Fact]
    public async Task Drafting_BlockedByAnUnreadablePlanOfThisAgent_NamesWhatBlocksIt()
    {
        using var harness = new PlanHarness("agent-one", "project-one");
        await harness.Plans.ActiveAsync("");
        await ExecuteAsync(harness, UnreadablePlan("agent-one", "project-one"));

        var answer = await harness.Drafting.DraftAsync("ship the server", ["write the store"]);

        answer.IsRefused(Messages.DraftBlockedByUnreadable, FailureTypes.Unreadable);
    }

    /// <summary>
    /// The plan an agent walks is printed whole by get_current_plan, update_step and revise_plan. A step passed over on
    /// the read left each of them short by one step with nothing said, so the agent counted progress over a list it
    /// did not have.
    /// </summary>
    [Fact]
    public async Task Current_OfAPlanWithAnUnreadableStep_SaysARowWasNotRead()
    {
        using var harness = new PlanHarness();
        var plan = await PlanWithAnUnreadableStepAsync(harness);

        var answer = await harness.Progress.CurrentAsync();

        Assert.Contains("write the store", answer.Words(), StringComparison.Ordinal);
        Assert.Contains($"1 row {Messages.UnreadableRows}", answer.Words(), StringComparison.Ordinal);
        Assert.Contains(plan.Id.ToString(), answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Advancing_APlanWithAnUnreadableStep_SaysARowWasNotRead()
    {
        using var harness = new PlanHarness();
        var plan = await PlanWithAnUnreadableStepAsync(harness);

        var answer = await harness.Progress.AdvanceAsync(plan.Steps[0].Id.ToString(), StepStatus.InProgress);

        Assert.Contains($"1 row {Messages.UnreadableRows}", answer.Words(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revising_APlanWithAnUnreadableStep_SaysARowWasNotRead()
    {
        using var harness = new PlanHarness();
        var plan = await PlanWithAnUnreadableStepAsync(harness);

        var answer = await harness.Revision.ReviseAsync(plan.Id.ToString(), plan.Version, [new StepChange(ChangeKind.Add, Title: "wire the tools")]);

        Assert.Contains($"1 row {Messages.UnreadableRows}", answer.Words(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The agent's own active plan unreadable: get_current_plan said there was none, which invited a create_plan the
    /// unique index then refused — the plan is there, it only cannot be read.
    /// </summary>
    [Fact]
    public async Task Current_WhenThisAgentsActivePlanIsUnreadable_SaysSoRatherThanThatThereIsNone()
    {
        using var harness = new PlanHarness("agent-one", "project-one");
        await harness.Plans.ActiveAsync("");
        await ExecuteAsync(harness, UnreadablePlan("agent-one", "project-one"));

        var answer = await harness.Progress.CurrentAsync();

        answer.IsRefused(Messages.CurrentPlanUnreadable, FailureTypes.Unreadable);
    }

    private static async Task<Plan> PlanWithAnUnreadableStepAsync(PlanHarness harness)
    {
        await harness.Drafting.DraftAsync("ship the server", ["write the store"]);
        var plan = (await harness.Plans.ActiveAsync(harness.Identity.ProjectName))[0];

        await ExecuteAsync(harness, $"""
            INSERT INTO "Steps" ("Id", "PlanId", "OrderIndex", "Title", "Status", "Note", "Attempts", "Started", "Completed")
            VALUES ('not-a-guid', '{plan.Id.ToString().ToUpperInvariant()}', 999, 'edited by hand', 'Pending', '', 0, NULL, NULL);
            """);

        return plan;
    }

    private static string UnreadablePlan(string agent, string project) => $"""
        INSERT INTO "Plans" ("Id", "AgentId", "ProjectName", "Objective", "Summary", "Status", "Version", "Created", "Updated", "Completed")
        VALUES ('not-a-guid-{agent}', '{agent}', '{project}', 'edited by hand', '', 'Active', 1, 0, 0, NULL);
        """;

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
