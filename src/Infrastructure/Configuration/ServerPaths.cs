using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Infrastructure.Configuration;

/// <summary>Paths where the server looks for its configuration and keeps its data.</summary>
public static class ServerPaths
{
    /// <summary>Config file name looked up in the working directory and in the profile.</summary>
    public const string ConfigFileName = "s3l-mcp-plans.json";

    /// <summary>The config file's name before the s3l names, still read after <see cref="ConfigFileName"/>.</summary>
    public const string FormerConfigFileName = "snail-mcp-plans.json";

    /// <summary>User profile directory, resolved without touching the disk.</summary>
    public static string Home => Environment.GetFolderPath(
        Environment.SpecialFolder.UserProfile,
        Environment.SpecialFolderOption.DoNotVerify);

    /// <summary><c>~/.s3l-mcp-plans</c>, moved there from <c>~/.snail-mcp-plans</c> when only that one exists.</summary>
    public static string DefaultDataDirectory => ServerFolders.DataDirectory("plans");

    /// <summary>Config lookup chain, highest priority first; <c>SNAIL_MCP_PLANS_CONFIG</c> overrides everything.</summary>
    public static IEnumerable<string> ConfigCandidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentSettings.ConfigPathVariable);

        if (!string.IsNullOrWhiteSpace(fromEnv))
            yield return fromEnv;

        var workingDirectory = Directory.GetCurrentDirectory();

        yield return Path.Combine(workingDirectory, $".{ConfigFileName}");

        yield return Path.Combine(workingDirectory, $".{FormerConfigFileName}");

        yield return Path.Combine(workingDirectory, ConfigFileName);

        yield return Path.Combine(workingDirectory, FormerConfigFileName);

        yield return Path.Combine(ServerFolders.ConfigDirectory("plans"), "config.json");

        yield return Path.Combine(Home, $".{ConfigFileName}");

        yield return Path.Combine(Home, $".{FormerConfigFileName}");
    }

    /// <summary>First existing file from <see cref="ConfigCandidates"/>, or <c>null</c>.</summary>
    public static string? FindConfigFile() => ConfigCandidates().FirstOrDefault(File.Exists);

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
