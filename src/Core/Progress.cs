namespace Snail.MCP.Plans.Core;

/// <summary>How far a plan has gone, counted by step status.</summary>
/// <remarks>
/// Deliberately not a percentage of done over total: on a plan the agent may revise, that number improves
/// when inconvenient steps are dropped. Counts plus the age of the last write are the honest answer, and
/// the age is what exposes a stalled agent.
/// </remarks>
public sealed record Progress
{
    /// <summary>How many steps the plan holds.</summary>
    public required int Total { get; init; }

    /// <summary>Steps not started.</summary>
    public required int Pending { get; init; }

    /// <summary>Steps being worked on.</summary>
    public required int InProgress { get; init; }

    /// <summary>Steps finished successfully.</summary>
    public required int Done { get; init; }

    /// <summary>Steps attempted without success.</summary>
    public required int Failed { get; init; }

    /// <summary>Steps deliberately not done.</summary>
    public required int Skipped { get; init; }

    /// <summary>Sum of attempts across every step.</summary>
    public required int Attempts { get; init; }

    /// <summary>Whether no step expects work any more.</summary>
    /// <remarks>A failed step counts as expecting work, the same way <see cref="Settlement"/> keeps a plan holding one active: two different answers to "is this settled" would let the plan be called finished while the failure still stood.</remarks>
    public bool IsSettled => Pending is 0 && InProgress is 0 && Failed is 0;

    /// <summary>Counts the steps of a plan.</summary>
    public static Progress Of(Plan plan) => new()
    {
        Total = plan.Steps.Count,
        Pending = plan.Steps.Count(step => step.Status is StepStatus.Pending),
        InProgress = plan.Steps.Count(step => step.Status is StepStatus.InProgress),
        Done = plan.Steps.Count(step => step.Status is StepStatus.Done),
        Failed = plan.Steps.Count(step => step.Status is StepStatus.Failed),
        Skipped = plan.Steps.Count(step => step.Status is StepStatus.Skipped),
        Attempts = plan.Steps.Sum(step => step.Attempts)
    };
}
