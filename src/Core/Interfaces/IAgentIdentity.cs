namespace Snail.MCP.Plans.Core.Interfaces;

/// <summary>Who this server instance is working for.</summary>
/// <remarks>
/// Identity comes from the process, never from a tool argument: an argument is written by the model, so
/// any agent could read another one's plan simply by naming its identity.
/// </remarks>
public interface IAgentIdentity
{
    /// <summary>The agent owning the plans of this process.</summary>
    string AgentId { get; }

    /// <summary>Project the session is working in, used when a tool is called without one.</summary>
    string ProjectName { get; }
}
