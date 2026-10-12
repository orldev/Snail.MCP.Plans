using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Application;

/// <summary>Drafting a plan before the work starts.</summary>
/// <remarks>
/// A second draft for the same agent and project answers with the plan that already exists instead of
/// creating one: MCP delivers at least once, so a call that timed out on the way back is retried, and a
/// plain insert would leave two plans behind.
/// </remarks>
public sealed class PlanDrafting(IPlanStore plans, IAgentIdentity identity, TimeProvider clock)
{
    /// <summary>Stores a new plan and answers with it, or with the active one that blocks it.</summary>
    public async Task<string> DraftAsync(
        string objective,
        IReadOnlyList<string> steps,
        string projectName = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
            return Messages.ObjectiveRequired;

        IReadOnlyList<string> titles =
        [
            .. steps
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title.Trim())
        ];

        if (titles.Count is 0)
            return Messages.StepsRequired;

        var project = Projects.Resolve(projectName, identity);
        var now = clock.GetUtcNow();

        if (await plans.CurrentAsync(identity.AgentId, project, cancellationToken) is { } active)
            return PlanView.Render(active, now, Messages.PlanAlreadyActive);

        var plan = new Plan
        {
            Id = Guid.CreateVersion7(),
            AgentId = identity.AgentId,
            ProjectName = project,
            Objective = objective.Trim(),
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            Steps = [.. titles.Select((title, position) => new Step
            {
                Id = Guid.CreateVersion7(),
                OrderIndex = (position + 1) * Ordering.Gap,
                Title = title
            })]
        };

        await plans.AddAsync(plan, cancellationToken);

        return PlanView.Render(plan, now, await NeighboursAsync(project, cancellationToken));
    }

    /// <remarks>Named rather than hidden: both plans are legitimate, and the one thing the agent must not do is report steps of the plan it does not own.</remarks>
    private async Task<string> NeighboursAsync(string project, CancellationToken cancellationToken)
    {
        var strangers = (await plans.ActiveAsync(project, cancellationToken))
            .Where(plan => !plan.AgentId.Equals(identity.AgentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (strangers.Count is 0)
            return "";

        var listed = strangers.Select(plan => $"  {plan.Id} · agent '{plan.AgentId}' · {plan.Objective}");

        return string.Join('\n', [Messages.AnotherAgentIsHere, .. listed]);
    }
}
