using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Application;

/// <summary>Which project a tool call belongs to.</summary>
public static class Projects
{
    /// <summary>The name given by the caller, or the one the process is running in.</summary>
    public static string Resolve(string requested, IAgentIdentity identity) =>
        string.IsNullOrWhiteSpace(requested) ? identity.ProjectName : requested.Trim();
}
