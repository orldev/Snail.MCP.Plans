using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Application;

/// <summary>Drafting a plan before the work starts.</summary>
/// <remarks>
/// A second draft for the same agent and project answers with the plan that already exists instead of
/// creating one: MCP delivers at least once, so a call that timed out on the way back is retried, and a
/// plain insert would leave two plans behind. The check before the insert covers the retry; two calls in
/// flight together both pass it, and then the file's unique index decides and the loser is answered with
/// the winner's plan. That answer is a success with <see cref="PrintedPlan.Drafted"/> false: the retried call got
/// the plan it asked for.
/// </remarks>
public sealed class PlanDrafting(IPlanStore plans, IAgentIdentity identity, TimeProvider clock)
{
    /// <summary>Stores a new plan and answers with it, or with the active one that blocks it.</summary>
    public async Task<Answer<PrintedPlan>> DraftAsync(
        string objective,
        IReadOnlyList<string> steps,
        string projectName = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objective))
            return Answer.Failed(Messages.ObjectiveRequired, type: FailureTypes.Arguments);

        IReadOnlyList<string> titles =
        [
            .. steps
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .Select(title => title.Trim())
        ];

        if (titles.Count is 0)
            return Answer.Failed(Messages.StepsRequired, Messages.PassTheSteps, FailureTypes.Arguments);

        var project = Projects.Resolve(projectName, identity);
        var now = clock.GetUtcNow();

        var current = await plans.CurrentAsync(identity.AgentId, project, cancellationToken);

        if (current.Count > 0 || current.UnreadablePlans > 0)
            return Blocked(current, now);

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

        if (!await plans.TryAddAsync(plan, cancellationToken))
            return await OutracedAsync(project, now, cancellationToken);

        var text = PlanView.Render(plan, now, await NeighboursAsync(project, cancellationToken));

        return Answer.Ok(new PrintedPlan(plan, text, Drafted: true));
    }

    /// <remarks>
    /// The index refused the insert because an active plan of this agent and project exists. When the read that
    /// follows finds none, readable or not, it was settled a moment ago.
    /// </remarks>
    private async Task<Answer<PrintedPlan>> OutracedAsync(string project, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = await plans.CurrentAsync(identity.AgentId, project, cancellationToken);

        if (current.Count > 0 || current.UnreadablePlans > 0)
            return Blocked(current, now);

        return Answer.Failed(Messages.DraftOutraced, Messages.DraftAgain, FailureTypes.Outraced);
    }

    /// <remarks>An active plan of this agent blocks a new one whether it can be read or not; only the readable one can be shown.</remarks>
    private static Answer<PrintedPlan> Blocked(PlanListing current, DateTimeOffset now)
    {
        if (current.Count is 0)
            return Answer.Failed(Messages.DraftBlockedByUnreadable, Messages.FixTheRowThenDraft, FailureTypes.Unreadable);

        var text = PlanView.Render(current[0], now, Messages.PlanAlreadyActive);

        return Answer.Ok(new PrintedPlan(current[0], text, Drafted: false));
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
