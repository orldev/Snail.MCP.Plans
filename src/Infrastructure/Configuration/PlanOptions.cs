using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Snail.MCP.Plans.Infrastructure.Configuration;

/// <summary>Server configuration: flat keys from a JSON config file or <c>SNAIL_MCP_PLANS_*</c> variables; <see cref="Load"/> is the single reader.</summary>
public sealed class PlanOptions
{
    /// <summary>Database file name inside the data directory.</summary>
    public const string DefaultDatabaseFileName = "plans.db";

    /// <summary>Seconds a write waits for the other session's write to finish before giving up.</summary>
    /// <remarks>Two processes share the file, so a lock is expected rather than exceptional; SQLite retries for this long instead of failing at once.</remarks>
    public const int DefaultBusyTimeoutSeconds = 10;

    /// <summary>Identity of the agent. Empty — the working directory of the process.</summary>
    public string? AgentId { get; set; }

    /// <summary>Data directory. Empty — <see cref="ServerPaths.DefaultDataDirectory"/>.</summary>
    public string? Directory { get; set; }

    /// <summary>Database file or connection string. Empty — <c>{Directory}/plans.db</c>.</summary>
    public string? DbPath { get; set; }

    /// <summary>Seconds a blocked write keeps retrying. Empty — <see cref="DefaultBusyTimeoutSeconds"/>.</summary>
    public int? BusyTimeoutSeconds { get; set; }

    /// <summary>Reads options from the config file and the environment; the environment wins.</summary>
    public static PlanOptions Load()
    {
        var builder = new ConfigurationBuilder();

        if (ServerPaths.FindConfigFile() is { } configFile)
            builder.AddJsonFile(Path.GetFullPath(configFile), optional: true, reloadOnChange: false);

        return builder.AddEnvironment().Build().Get<PlanOptions>() ?? new PlanOptions();
    }

    /// <summary>Configured directory or the default one, with a leading <c>~</c> expanded.</summary>
    public string ResolveDirectory() => ServerPaths.ExpandHome(Directory);

    /// <summary>Connection string with the busy timeout applied.</summary>
    public string ResolveConnectionString()
    {
        var configured = (DbPath ?? "").Trim();

        if (configured.Contains('=', StringComparison.Ordinal))
            return configured;

        var dataSource = configured switch
        {
            "" => Path.Combine(ResolveDirectory(), DefaultDatabaseFileName),
            ":memory:" => configured,
            _ => ServerPaths.ExpandHome(configured)
        };

        return new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            DefaultTimeout = ResolveBusyTimeout()
        }.ToString();
    }

    /// <summary>Seconds a blocked write keeps retrying.</summary>
    public int ResolveBusyTimeout() =>
        BusyTimeoutSeconds is > 0 ? BusyTimeoutSeconds.Value : DefaultBusyTimeoutSeconds;
}
