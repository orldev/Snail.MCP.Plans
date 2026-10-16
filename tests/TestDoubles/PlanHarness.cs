using Microsoft.Data.Sqlite;
using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.MCP.Plans.Infrastructure.Persistence;

namespace Snail.MCP.Plans.Tests.TestDoubles;

/// <summary>The server's own storage on a throwaway database file, wired to a stopped clock.</summary>
/// <remarks>
/// Deliberately the real SQLite store rather than an in-memory double: the invariants under test are
/// partial unique indexes and a concurrency token, and neither exists anywhere but in the database.
/// </remarks>
public sealed class PlanHarness : IDisposable
{
    private readonly string _file;
    private readonly string _database;
    private readonly SqlitePlanStore _store;

    /// <summary>Opens a fresh database for one test.</summary>
    public PlanHarness(string agentId = "agent-one", string projectName = "project-one")
    {
        _file = Path.Combine(TestFolder.Create(), "plans.db");

        _database = $"Data Source={_file}";
        _store = new SqlitePlanStore(_database);

        Clock = new StoppedClock(new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));
        Identity = new FixedIdentity(agentId, projectName);
        Plans = _store;

        Drafting = new PlanDrafting(Plans, Identity, Clock);
        Progress = new PlanProgress(Plans, Identity, Clock);
        Revision = new PlanRevision(Plans, Identity, Clock);
        Active = new ActivePlans(Plans, Clock);
    }

    /// <summary>Path of the throwaway database, for assertions about the file itself.</summary>
    public string DatabaseFile => _file;

    /// <summary>The clock every process in this harness reads.</summary>
    public StoppedClock Clock { get; }

    /// <summary>Who the harness acts as.</summary>
    public IAgentIdentity Identity { get; }

    /// <summary>The store under the processes.</summary>
    public IPlanStore Plans { get; }

    /// <summary>Drafting process.</summary>
    public PlanDrafting Drafting { get; }

    /// <summary>Progress process.</summary>
    public PlanProgress Progress { get; }

    /// <summary>Revision process.</summary>
    public PlanRevision Revision { get; }

    /// <summary>Active plans read.</summary>
    public ActivePlans Active { get; }

    /// <summary>A second agent sharing the same database file, as a second session would.</summary>
    public PlanProgress ProgressOf(string agentId, string projectName) =>
        new(Plans, new FixedIdentity(agentId, projectName), Clock);

    /// <summary>A second agent's drafting against the same database file.</summary>
    public PlanDrafting DraftingOf(string agentId, string projectName) =>
        new(Plans, new FixedIdentity(agentId, projectName), Clock);

    /// <summary>A second agent's revision against the same database file.</summary>
    public PlanRevision RevisionOf(string agentId, string projectName) =>
        new(Plans, new FixedIdentity(agentId, projectName), Clock);

    /// <summary>Closes this harness's pooled connections, so the file can go, and deletes it.</summary>
    /// <remarks>
    /// Only this file's pool: clearing every pool of the process reached into the tests running beside this one, and a
    /// connection another test was opening at that moment was disposed under it.
    /// </remarks>
    public void Dispose()
    {
        _store.Dispose();

        using (var connection = new SqliteConnection(_database))
            SqliteConnection.ClearPool(connection);

        foreach (var path in new[] { _file, $"{_file}-wal", $"{_file}-shm" })
            File.Delete(path);
    }
}
