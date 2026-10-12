using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Tests.TestDoubles;

/// <summary>An agent identity stated by the test instead of read from the process.</summary>
public sealed class FixedIdentity(string agentId, string projectName) : IAgentIdentity
{
    /// <inheritdoc />
    public string AgentId { get; } = agentId;

    /// <inheritdoc />
    public string ProjectName { get; } = projectName;
}
