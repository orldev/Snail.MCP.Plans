namespace Snail.MCP.Plans.Core;

/// <summary>Positions of steps inside a plan.</summary>
/// <remarks>
/// Indexes are spaced by <see cref="Gap"/> rather than consecutive, and a revision re-spaces the whole
/// list: a plan is stored as a whole, so renumbering costs nothing, and steps are cited by identity
/// rather than by position, so nothing outside depends on the numbers holding still.
/// </remarks>
public static class Ordering
{
    /// <summary>Distance between two neighbouring steps.</summary>
    public const int Gap = 100;

    /// <summary>Position after the last step.</summary>
    public static int Last(IReadOnlyList<Step> steps) =>
        steps.Count is 0 ? Gap : steps.Max(step => step.OrderIndex) + Gap;

    /// <summary>Re-spaces the steps by <see cref="Gap"/>, keeping the order they arrive in.</summary>
    public static IReadOnlyList<Step> Respaced(IEnumerable<Step> steps) =>
    [
        .. steps.Select((step, position) => step with { OrderIndex = (position + 1) * Gap })
    ];
}
