using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Application;

/// <summary>What every agent is working on right now.</summary>
/// <remarks>
/// The one read that ignores identity: this is the question a second session asks about the first, and
/// scoping it to the caller would answer about the caller instead.
/// </remarks>
public sealed class ActivePlans(IPlanStore plans, TimeProvider clock)
{
    /// <summary>Every active plan with its counts, current step and how long since it last moved.</summary>
    /// <remarks>Never a failure: nothing active is an answer about the plans, and the text says which kind of empty it is.</remarks>
    public async Task<Answer<PrintedListing>> RenderAsync(string projectName = "", CancellationToken cancellationToken = default)
    {
        var active = await plans.ActiveAsync(projectName.Trim(), cancellationToken);
        var text = PlanView.Active(active, clock.GetUtcNow());

        return Answer.Ok(new PrintedListing(active, text));
    }
}
