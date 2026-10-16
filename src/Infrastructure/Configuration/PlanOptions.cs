using System.Collections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Infrastructure.Configuration;

/// <summary>Server configuration: flat keys from the settings file or <c>SNAIL_MCP_PLANS_*</c> variables; <see cref="Load()"/> is the single reader.</summary>
public sealed class PlanOptions
{
    /// <summary>The server's short name, which names its settings file and its variables.</summary>
    public const string ServerName = "plans";

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

    /// <summary>Reads options from the settings file chain of <see cref="ServerSettings"/> and the variables, which win.</summary>
    public static PlanOptions Load() => Load(ServerSettings.FileOf(ServerName), Environment.GetEnvironmentVariables());

    /// <summary>Reads options from <paramref name="file"/>, when there is one, and the server's variables among <paramref name="variables"/>.</summary>
    internal static PlanOptions Load(string? file, IDictionary variables) =>
        new ConfigurationBuilder()
            .AddServerSettings(ServerName, file, variables)
            .Build()
            .Get<PlanOptions>() ?? new PlanOptions();

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
