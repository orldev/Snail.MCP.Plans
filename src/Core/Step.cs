namespace Snail.MCP.Plans.Core;

/// <summary>Where a single step stands.</summary>
public enum StepStatus
{
    /// <summary>Not started.</summary>
    Pending,

    /// <summary>Being worked on; at most one step of a plan may be here.</summary>
    InProgress,

    /// <summary>Finished successfully.</summary>
    Done,

    /// <summary>Attempted and did not work; may be attempted again.</summary>
    Failed,

    /// <summary>Deliberately not done, with the reason in <see cref="Step.Note"/>.</summary>
    Skipped
}

/// <summary>One step of a plan.</summary>
/// <remarks>
/// A step is never deleted, only <see cref="StepStatus.Skipped"/> with a reason: progress that another
/// session reads must not be improvable by dropping the inconvenient steps.
/// </remarks>
public sealed record Step
{
    /// <summary>Identity of the step.</summary>
    public required Guid Id { get; init; }

    /// <summary>Position in the plan, sparse so a step can be inserted between two others without renumbering.</summary>
    public required int OrderIndex { get; init; }

    /// <summary>What is to be done.</summary>
    public required string Title { get; init; }

    /// <summary>Status of the step.</summary>
    public StepStatus Status { get; init; } = StepStatus.Pending;

    /// <summary>Latest note: what came out of the step, or why it did not.</summary>
    public string Note { get; init; } = "";

    /// <summary>How many times the step entered <see cref="StepStatus.InProgress"/>.</summary>
    public int Attempts { get; init; }

    /// <summary>When work on the step started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the step reached a terminal status.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Whether the step still expects work.</summary>
    public bool IsOpen => Status is StepStatus.Pending or StepStatus.InProgress;
}
