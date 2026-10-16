namespace Snail.MCP.Plans.Core.Interfaces;

/// <summary>The stored plans.</summary>
/// <remarks>
/// Two sessions read the same database at once, so a write states the version it was built on and
/// <see cref="TrySaveAsync"/> refuses when that version has moved: the alternative is a revision
/// silently overwriting another one.
/// </remarks>
public interface IPlanStore
{
    /// <summary>The agent's active plan in a project — none or one — with the rows that could not be read.</summary>
    /// <remarks>A listing rather than a plan or null: "no active plan" and "an active plan that cannot be read" call for different answers, and only the count tells them apart.</remarks>
    Task<PlanListing> CurrentAsync(string agentId, string projectName, CancellationToken cancellationToken = default);

    /// <summary>A plan by identity, whatever its status — none or one — with the step rows that could not be read.</summary>
    Task<PlanListing> FindAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>Every active plan, newest activity first, with the rows that could not be read; an empty project name spans all of them.</summary>
    Task<PlanListing> ActiveAsync(string projectName, CancellationToken cancellationToken = default);

    /// <summary>Stores a freshly drafted plan; <c>false</c> means the agent already has an active plan in that project.</summary>
    /// <remarks>
    /// A refusal is an answer, not a failure: two calls of one agent can be in flight together — MCP delivers at least
    /// once, and two sessions in one folder share an identity on purpose — and only the file can tell which came first.
    /// </remarks>
    Task<bool> TryAddAsync(Plan plan, CancellationToken cancellationToken = default);

    /// <summary>Writes a plan built on <paramref name="expectedVersion"/>; <c>false</c> means someone else wrote first.</summary>
    Task<bool> TrySaveAsync(Plan plan, int expectedVersion, CancellationToken cancellationToken = default);
}
