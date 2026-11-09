using System.Collections;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Infrastructure.Configuration;

/// <summary>Server configuration: flat keys from the settings file or <c>SNAIL_MCP_PLANS_*</c> variables, bound by <see cref="From"/> from the settings the host read once.</summary>
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

    /// <summary>Data directory. Empty — <c>~/.s3l-mcp-plans</c> (<see cref="ServerFolders.DataDirectory(string)"/>).</summary>
    public string? Directory { get; set; }

    /// <summary>Database file or connection string. Empty — <c>{Directory}/plans.db</c>.</summary>
    public string? DbPath { get; set; }

    /// <summary>Seconds a blocked write keeps retrying. Empty — <see cref="DefaultBusyTimeoutSeconds"/>.</summary>
    public int? BusyTimeoutSeconds { get; set; }

    /// <summary>Binds the options from the server's settings: its settings file and its variables, which win (<see cref="ServerSettings"/>).</summary>
    /// <remarks>
    /// <para>The host has read them already (<see cref="McpHost.CreateBuilder(string[])"/>); reading them a second time here could only disagree with it.</para>
    /// <para>
    /// Bound while the services are registered, before the host is built, so a value its property's type cannot hold —
    /// <c>SNAIL_MCP_PLANS_BUSY_TIMEOUT_SECONDS=abc</c> — is refused through
    /// <see cref="ServerSettings.Bind(string, IConfiguration, Action{IConfiguration})"/> as a <see cref="StartRefusedException"/>
    /// naming the variable, where the binder's bare exception crashed the program with a trace. The call to <c>Get</c> stays
    /// written here, where the native build's binding source generator replaces it.
    /// </para>
    /// </remarks>
    /// <exception cref="StartRefusedException">A setting holds a value its property's type cannot hold.</exception>
    public static PlanOptions From(IConfiguration settings)
    {
        PlanOptions? bound = null;

        ServerSettings.Bind(ServerName, settings, read => bound = read.Get<PlanOptions>());

        return bound ?? new PlanOptions();
    }

    /// <summary>Reads options from <paramref name="file"/>, when there is one, and the server's variables among <paramref name="variables"/>.</summary>
    internal static PlanOptions Load(string? file, IDictionary variables) =>
        From(new ConfigurationBuilder()
            .AddServerSettings(ServerName, file, variables)
            .Build());

    /// <summary>Configured directory or the default one, absolute, with a leading <c>~</c> made the user's home.</summary>
    public string ResolveDirectory() => ServerFolders.FullPath(Directory, ServerFolders.DataDirectory(ServerName));

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
            _ => ServerFolders.FullPath(configured)
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
