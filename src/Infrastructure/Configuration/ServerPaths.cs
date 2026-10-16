using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Infrastructure.Configuration;

/// <summary>Paths where the server keeps its data; its settings file is found by <see cref="ServerSettings"/>.</summary>
public static class ServerPaths
{
    /// <summary>User profile directory, resolved without touching the disk.</summary>
    public static string Home => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>~/.s3l-mcp-plans</c>, moved there from <c>~/.snail-mcp-plans</c> when only that one exists.</summary>
    public static string DefaultDataDirectory => ServerFolders.DataDirectory(PlanOptions.ServerName);

    /// <summary>Expands a leading <c>~</c> and makes the path absolute; empty — the default data directory.</summary>
    public static string ExpandHome(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return DefaultDataDirectory;

        path = path.Trim();

        if (path == "~")
            return Home;

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
            return Path.Combine(Home, path[2..]);

        return Path.GetFullPath(path);
    }
}
