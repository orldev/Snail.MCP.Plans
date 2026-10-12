using Snail.MCP.Plans.Core.Interfaces;
using Snail.MCP.Plans.Infrastructure.Configuration;

namespace Snail.MCP.Plans.Infrastructure.Identity;

/// <summary>Identity taken from the process the client started.</summary>
/// <remarks>
/// Falls back to the working directory rather than demanding configuration: a client starts a stdio
/// server in the folder of the project, so the default is right without a variable being set per project,
/// and two sessions opened in the same folder deliberately share a plan — the second continues the first
/// instead of drafting its own.
/// </remarks>
public sealed class ProcessIdentity(PlanOptions options) : IAgentIdentity
{
    private const string Unnamed = "unnamed";

    /// <inheritdoc />
    public string AgentId { get; } = string.IsNullOrWhiteSpace(options.AgentId)
        ? WorkingDirectoryName()
        : options.AgentId.Trim();

    /// <inheritdoc />
    public string ProjectName { get; } = WorkingDirectoryName();

    private static string WorkingDirectoryName()
    {
        var current = System.IO.Directory.GetCurrentDirectory()
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var name = Path.GetFileName(current);

        return name.Length is 0 ? Unnamed : name;
    }
}
