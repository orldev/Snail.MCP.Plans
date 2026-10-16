using System.Collections;

namespace Snail.MCP.Plans.Core;

/// <summary>The active plans as read, with the rows the read had to pass over.</summary>
/// <remarks>
/// A row whose id is not a Guid is left out so that it cannot take every other plan down with it, and the count
/// travels with the list so that an empty listing can tell "nothing is active" from "nothing could be read".
/// </remarks>
public sealed record PlanListing(IReadOnlyList<Plan> Plans, int UnreadablePlans, int UnreadableSteps) : IReadOnlyList<Plan>
{
    /// <summary>Rows of either kind the read passed over.</summary>
    public int Unreadable => UnreadablePlans + UnreadableSteps;

    /// <inheritdoc/>
    public int Count => Plans.Count;

    /// <inheritdoc/>
    public Plan this[int index] => Plans[index];

    /// <inheritdoc/>
    public IEnumerator<Plan> GetEnumerator() => Plans.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
