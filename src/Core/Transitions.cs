namespace Snail.MCP.Plans.Core;

/// <summary>The transition table of a step: which status changes are allowed, and what to answer when one is not.</summary>
/// <remarks>
/// A step that failed may be attempted again, which is why <see cref="StepStatus.Failed"/> is not terminal.
/// <see cref="StepStatus.Done"/> is: undoing a finished step would make progress reversible and the
/// active list untrustworthy.
/// <para>
/// A step taken up by mistake goes back to <see cref="StepStatus.Pending"/>. Without that way out the
/// only escape was to report it <see cref="StepStatus.Failed"/>, which put a failure that never happened
/// on the record.
/// </para>
/// </remarks>
public static class Transitions
{
    /// <summary>Statuses a step may move to from the one it is in.</summary>
    public static IReadOnlyList<StepStatus> AllowedFrom(StepStatus status) => status switch
    {
        StepStatus.Pending => [StepStatus.InProgress, StepStatus.Skipped],
        StepStatus.InProgress => [StepStatus.Done, StepStatus.Failed, StepStatus.Skipped, StepStatus.Pending],
        StepStatus.Failed => [StepStatus.InProgress, StepStatus.Skipped],
        _ => []
    };

    /// <summary>Whether a step may move between these two statuses.</summary>
    public static bool CanMove(StepStatus from, StepStatus to) => AllowedFrom(from).Contains(to);

    /// <summary>Whether a status expects no further work.</summary>
    public static bool IsTerminal(StepStatus status) => status is StepStatus.Done or StepStatus.Skipped;
}
