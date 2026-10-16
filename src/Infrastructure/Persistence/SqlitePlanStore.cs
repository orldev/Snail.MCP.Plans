using Microsoft.Data.Sqlite;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Infrastructure.Persistence;

/// <summary>Plans and their steps as rows of one SQLite file that every session of the machine shares.</summary>
/// <remarks>
/// <para>
/// Written against Microsoft.Data.Sqlite directly. Entity Framework kept the same rows, but a native build cannot
/// run it — its model is built at run time — and five statements were all it was used for. The tables, the indexes
/// and the way values are written are the ones Entity Framework made, so a file it created reads and writes the same:
/// ids as upper-case text, moments as Unix milliseconds, statuses by name.
/// </para>
/// <para>
/// The invariants live in the file, not here: one active plan per agent and project, one step in progress per plan,
/// and a version that must not have moved under a writer. Two sessions write the same file, and a check in the code
/// would be a race between them; a refusal comes back as an answer — TryAddAsync and TrySaveAsync return false.
/// </para>
/// </remarks>
public sealed class SqlitePlanStore(string database) : IPlanStore, IDisposable
{
    private const string Schema =
        """
        CREATE TABLE IF NOT EXISTS "Plans" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Plans" PRIMARY KEY,
            "AgentId" TEXT NOT NULL,
            "ProjectName" TEXT NOT NULL,
            "Objective" TEXT NOT NULL,
            "Summary" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "Version" INTEGER NOT NULL,
            "Created" INTEGER NOT NULL,
            "Updated" INTEGER NOT NULL,
            "Completed" INTEGER NULL
        );
        CREATE TABLE IF NOT EXISTS "Steps" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_Steps" PRIMARY KEY,
            "PlanId" TEXT NOT NULL,
            "OrderIndex" INTEGER NOT NULL,
            "Title" TEXT NOT NULL,
            "Status" TEXT NOT NULL,
            "Note" TEXT NOT NULL,
            "Attempts" INTEGER NOT NULL,
            "Started" INTEGER NULL,
            "Completed" INTEGER NULL,
            CONSTRAINT "FK_Steps_Plans_PlanId" FOREIGN KEY ("PlanId") REFERENCES "Plans" ("Id") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS "IX_Plans_AgentId_ProjectName_Status" ON "Plans" ("AgentId", "ProjectName", "Status");
        CREATE INDEX IF NOT EXISTS "IX_Plans_Status" ON "Plans" ("Status");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_Plans_OneActivePerAgentAndProject" ON "Plans" ("AgentId", "ProjectName") WHERE "Status" = 'Active';
        CREATE INDEX IF NOT EXISTS "IX_Steps_PlanId_OrderIndex" ON "Steps" ("PlanId", "OrderIndex");
        CREATE UNIQUE INDEX IF NOT EXISTS "UX_Steps_OneInProgressPerPlan" ON "Steps" ("PlanId") WHERE "Status" = 'InProgress';
        PRAGMA journal_mode = WAL;
        """;

    private const string PlanColumns = """ "Id", "AgentId", "ProjectName", "Objective", "Summary", "Status", "Version", "Created", "Updated", "Completed" """;

    private const string StepColumns = """ "Id", "PlanId", "OrderIndex", "Title", "Status", "Note", "Attempts", "Started", "Completed" """;

    private const string Active = nameof(PlanStatus.Active);

    private const string SelectCurrentPlan =
        $"""SELECT {PlanColumns} FROM "Plans" WHERE "AgentId" = $agent AND "ProjectName" = $project AND "Status" = $active LIMIT 1;""";

    private const string SelectPlan = $"""SELECT {PlanColumns} FROM "Plans" WHERE "Id" = $id;""";

    private const string SelectActivePlans = $"""SELECT {PlanColumns} FROM "Plans" WHERE "Status" = $active ORDER BY "Updated" DESC;""";

    private const string SelectActivePlansOfProject =
        $"""SELECT {PlanColumns} FROM "Plans" WHERE "Status" = $active AND "ProjectName" = $project ORDER BY "Updated" DESC;""";

    private const string SelectSteps = $"""SELECT {StepColumns} FROM "Steps" WHERE "PlanId" = $plan ORDER BY "OrderIndex";""";

    private const string InsertPlan =
        $"""INSERT INTO "Plans" ({PlanColumns}) VALUES ($id, $agent, $project, $objective, $summary, $status, $version, $created, $updated, $completed);""";

    private const string UpdatePlan = """
        UPDATE "Plans" SET "Objective" = $objective, "Summary" = $summary, "Status" = $status, "Version" = $version,
            "Updated" = $updated, "Completed" = $completed
        WHERE "Id" = $id AND "Version" = $expected;
        """;

    private const string UpsertStep = $"""
        INSERT INTO "Steps" ({StepColumns}) VALUES ($id, $plan, $order, $title, $status, $note, $attempts, $started, $completed)
        ON CONFLICT ("Id") DO UPDATE SET "OrderIndex" = excluded."OrderIndex", "Title" = excluded."Title", "Status" = excluded."Status",
            "Note" = excluded."Note", "Attempts" = excluded."Attempts", "Started" = excluded."Started", "Completed" = excluded."Completed";
        """;


    /// <summary>SQLITE_CONSTRAINT_UNIQUE, the extended code a unique index refuses a row with.</summary>
    /// <remarks>The extended code rather than the primary 19, which a foreign key or a NOT NULL column returns as well.</remarks>
    private const int UniqueViolation = 2067;

    /// <summary>Taken around every open: connections of one file are opened one at a time.</summary>
    /// <remarks>
    /// Microsoft.Data.Sqlite 10.0.11 and 10.0.12 mark a pooled connection active before naming its owner, and an
    /// open that finds the pool empty reclaims every active connection without an owner: one being opened at that
    /// moment goes back to the pool and two callers share its handle (dotnet/efcore#39008, fixed after 10.0.12). The
    /// pool belongs to the process, not to a store, so the lock does too. An open costs microseconds; tool calls come
    /// seconds apart.
    /// </remarks>
    private static readonly Lock Opening = new();

    /// <summary>The connection string with the foreign keys switched on, which the open then does by itself.</summary>
    private readonly string _database = new SqliteConnectionStringBuilder(database) { ForeignKeys = true }.ToString();

    private readonly SemaphoreSlim _preparing = new(1, 1);

    private volatile bool _isPrepared;

    /// <inheritdoc/>
    public async Task<PlanListing> CurrentAsync(string agentId, string projectName, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var select = new SqliteCommand(SelectCurrentPlan, connection);

        Bind(select, ("$agent", agentId), ("$project", projectName), ("$active", Active));

        return await PlansAsync(connection, select, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PlanListing> FindAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var select = new SqliteCommand(SelectPlan, connection);

        Bind(select, ("$id", Text(planId)));

        return await PlansAsync(connection, select, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PlanListing> ActiveAsync(string projectName, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var select = projectName.Length > 0
            ? new SqliteCommand(SelectActivePlansOfProject, connection)
            : new SqliteCommand(SelectActivePlans, connection);

        Bind(select, ("$active", Active));

        if (projectName.Length > 0)
            Bind(select, ("$project", projectName));

        return await PlansAsync(connection, select, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAddAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var insert = new SqliteCommand(InsertPlan, connection, transaction);

            Bind(insert, plan);
            insert.Parameters.AddWithValue("$agent", plan.AgentId);
            insert.Parameters.AddWithValue("$project", plan.ProjectName);
            insert.Parameters.AddWithValue("$created", Milliseconds(plan.CreatedAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException refused) when (refused.SqliteExtendedErrorCode == UniqueViolation)
        {
            return false;
        }

        await WriteStepsAsync(connection, transaction, plan, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Steps are written, never removed. The domain deletes no step — a step leaves the work as Skipped and stays on
    /// the record — and a writer that removed every row it had not read deleted the rows a read passes over.
    /// </remarks>
    public async Task<bool> TrySaveAsync(Plan plan, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var update = new SqliteCommand(UpdatePlan, connection, transaction))
        {
            Bind(update, plan);
            update.Parameters.AddWithValue("$expected", expectedVersion);

            if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                return false;
        }

        await WriteStepsAsync(connection, transaction, plan, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc/>
    public void Dispose() => _preparing.Dispose();

    /// <summary>Writes every step of a plan, the ones leaving work before the one taking it up.</summary>
    /// <remarks>
    /// The index allowing one step in progress per plan is checked statement by statement: a step that starts while
    /// another finishes would collide with it if written first.
    /// </remarks>
    private static async Task WriteStepsAsync(SqliteConnection connection, SqliteTransaction transaction, Plan plan, CancellationToken cancellationToken)
    {
        await using var upsert = new SqliteCommand(UpsertStep, connection, transaction);

        foreach (var step in plan.Steps.OrderBy(step => step.Status == StepStatus.InProgress))
        {
            upsert.Parameters.Clear();
            upsert.Parameters.AddWithValue("$id", Text(step.Id));
            upsert.Parameters.AddWithValue("$plan", Text(plan.Id));
            upsert.Parameters.AddWithValue("$order", step.OrderIndex);
            upsert.Parameters.AddWithValue("$title", step.Title);
            upsert.Parameters.AddWithValue("$status", step.Status.ToString());
            upsert.Parameters.AddWithValue("$note", step.Note);
            upsert.Parameters.AddWithValue("$attempts", step.Attempts);
            upsert.Parameters.AddWithValue("$started", step.StartedAt is { } started ? Milliseconds(started) : DBNull.Value);
            upsert.Parameters.AddWithValue("$completed", step.CompletedAt is { } completed ? Milliseconds(completed) : DBNull.Value);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>The plans a select reads, each with its steps in order, and the rows passed over.</summary>
    /// <remarks>
    /// A row whose id is not a Guid is passed over, a plan and a step alike: no tool can name it, and a single row
    /// edited by hand must not take every other plan down with it, which is what parsing it did. The rows passed over
    /// are counted, so that an answer can say so instead of reading as if they were not there.
    /// </remarks>
    private static async Task<PlanListing> PlansAsync(SqliteConnection connection, SqliteCommand select, CancellationToken cancellationToken)
    {
        var rows = new List<Plan>();
        var unreadablePlans = 0;
        var unreadableSteps = 0;

        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Guid.TryParse(reader.GetString(0), out var id))
                {
                    unreadablePlans++;

                    continue;
                }

                rows.Add(new Plan
                {
                    Id = id,
                    AgentId = reader.GetString(1),
                    ProjectName = reader.GetString(2),
                    Objective = reader.GetString(3),
                    Summary = reader.GetString(4),
                    Status = Enum.TryParse<PlanStatus>(reader.GetString(5), out var status) ? status : PlanStatus.Active,
                    Version = reader.GetInt32(6),
                    CreatedAt = Moment(reader.GetInt64(7)),
                    UpdatedAt = Moment(reader.GetInt64(8)),
                    CompletedAt = await reader.IsDBNullAsync(9, cancellationToken) ? null : Moment(reader.GetInt64(9)),
                    Steps = []
                });
            }
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var (steps, unreadable) = await StepsAsync(connection, rows[index].Id, cancellationToken);

            rows[index] = rows[index] with { Steps = steps };
            unreadableSteps += unreadable;
        }

        return new PlanListing(rows, unreadablePlans, unreadableSteps);
    }

    /// <summary>The steps of one plan, in order.</summary>
    /// <remarks>
    /// One statement per plan, on purpose. Reading the steps of every selected plan in one statement, the condition
    /// as a subquery, was measured on 400 plans of 25 steps, three runs each: medians of 13.0–14.5 ms against
    /// 13.7–15.5 ms, inside the noise. In-process SQLite charges next to nothing per statement; the cost is the rows,
    /// and a single statement sorts all of them at once where this one walks the (PlanId, OrderIndex) index.
    /// </remarks>
    private static async Task<(IReadOnlyList<Step> Steps, int Unreadable)> StepsAsync(SqliteConnection connection, Guid planId, CancellationToken cancellationToken)
    {
        await using var select = new SqliteCommand(SelectSteps, connection);
        select.Parameters.AddWithValue("$plan", Text(planId));

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        var steps = new List<Step>();
        var unreadable = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Guid.TryParse(reader.GetString(0), out var id))
            {
                unreadable++;

                continue;
            }

            steps.Add(new Step
            {
                Id = id,
                OrderIndex = reader.GetInt32(2),
                Title = reader.GetString(3),
                Status = Enum.TryParse<StepStatus>(reader.GetString(4), out var status) ? status : StepStatus.Pending,
                Note = reader.GetString(5),
                Attempts = reader.GetInt32(6),
                StartedAt = await reader.IsDBNullAsync(7, cancellationToken) ? null : Moment(reader.GetInt64(7)),
                CompletedAt = await reader.IsDBNullAsync(8, cancellationToken) ? null : Moment(reader.GetInt64(8))
            });
        }

        return (steps, unreadable);
    }

    private static void Bind(SqliteCommand command, Plan plan)
    {
        command.Parameters.AddWithValue("$id", Text(plan.Id));
        command.Parameters.AddWithValue("$objective", plan.Objective);
        command.Parameters.AddWithValue("$summary", plan.Summary);
        command.Parameters.AddWithValue("$status", plan.Status.ToString());
        command.Parameters.AddWithValue("$version", plan.Version);
        command.Parameters.AddWithValue("$updated", Milliseconds(plan.UpdatedAt));
        command.Parameters.AddWithValue("$completed", plan.CompletedAt is { } completed ? Milliseconds(completed) : DBNull.Value);
    }

    private static void Bind(SqliteCommand command, params (string Name, object Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
    }

    /// <remarks>
    /// Nothing runs between opening a connection and handing it over, so no failure can leave an open handle with
    /// nobody holding it: the foreign keys are switched on by the open itself, through the connection string, and the
    /// schema is prepared beforehand on a connection of its own.
    /// </remarks>
    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (!_isPrepared)
            await PrepareAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return Opened();
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await _preparing.WaitAsync(cancellationToken);

        try
        {
            if (_isPrepared)
                return;

            await using var connection = Opened();
            await using var schema = new SqliteCommand(Schema, connection);
            await schema.ExecuteNonQueryAsync(cancellationToken);
            _isPrepared = true;
        }
        finally
        {
            _preparing.Release();
        }
    }

    /// <summary>A new open connection of the file.</summary>
    /// <remarks>Opened synchronously because it is opened under <see cref="Opening"/>, and a lock cannot be held across an await.</remarks>
    private SqliteConnection Opened()
    {
        var connection = new SqliteConnection(_database);

        lock (Opening)
            connection.Open();

        return connection;
    }

    /// <summary>An id as Entity Framework wrote it: upper-case text, which every older row of the file uses.</summary>
    private static string Text(Guid id) => id.ToString().ToUpperInvariant();

    private static long Milliseconds(DateTimeOffset moment) => moment.ToUnixTimeMilliseconds();

    private static DateTimeOffset Moment(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
