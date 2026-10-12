using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Application;

/// <summary>What every agent is working on right now.</summary>
/// <remarks>
/// The one read that ignores identity: this is the question a second session asks about the first, and
/// scoping it to the caller would answer about the caller instead.
/// </remarks>
public sealed class ActivePlans(IPlanStore plans, TimeProvider clock)
{
    /// <summary>Every active plan with its counts, current step and how long since it last moved.</summary>
    public async Task<string> RenderAsync(string projectName = "", CancellationToken cancellationToken = default)
    {
        var active = await plans.ActiveAsync(projectName.Trim(), cancellationToken);

        return PlanView.Active(active, clock.GetUtcNow());
    }
}
