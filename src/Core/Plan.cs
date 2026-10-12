namespace Snail.MCP.Plans.Core;

/// <summary>Where a plan as a whole stands.</summary>
public enum PlanStatus
{
    /// <summary>Being worked on; the only status <c>list_active_plans</c> reports.</summary>
    Active,

    /// <summary>Every step reached a terminal status and the agent closed the plan.</summary>
    Completed,

    /// <summary>Abandoned because the work could not be finished.</summary>
    Failed,

    /// <summary>Left behind without a verdict — a session ended and never came back.</summary>
    Abandoned
}

/// <summary>A plan an agent drafted before starting work, and walks step by step.</summary>
/// <remarks>
/// <see cref="Objective"/> never changes while <see cref="Summary"/> does: a single rewritable field
/// let the goal be overwritten by progress notes, and the agent drifted away from what it was asked for.
/// </remarks>
public sealed record Plan
{
    /// <summary>Identity of the plan.</summary>
    public required Guid Id { get; init; }

    /// <summary>Agent that owns the plan, taken from the server process, never from a tool argument.</summary>
    public required string AgentId { get; init; }

    /// <summary>Project the work belongs to; groups plans in the active list.</summary>
    public required string ProjectName { get; init; }

    /// <summary>What the agent was asked to achieve. Immutable.</summary>
    public required string Objective { get; init; }

    /// <summary>Where the work stands right now, in the agent's own words.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Status of the plan.</summary>
    public PlanStatus Status { get; init; } = PlanStatus.Active;

    /// <summary>Increments on every write; a writer states the version it read to detect a lost update.</summary>
    public int Version { get; init; }

    /// <summary>When the plan was drafted.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last write of any kind; its age is what tells a watching session that an agent is stuck.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>When the plan left <see cref="PlanStatus.Active"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The steps, in the order they are meant to be walked.</summary>
    public IReadOnlyList<Step> Steps { get; init; } = [];
}
