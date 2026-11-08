using System.Collections;
using Microsoft.Data.Sqlite;
using Snail.MCP.Plans.Infrastructure.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Tests.Infrastructure;

/// The settings file is where a machine's settings live and a variable overrides it for one start; both are what
/// a person writes, so the way they reach the options is pinned here rather than left to the binder.
public sealed class PlanOptionsTests : IDisposable
{
    private readonly string _directory = TestFolder.Create();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Load_WithSettingsFile_ReachesTheOptions()
    {
        var file = WriteSettings("""{ "AgentId": "from-file", "BusyTimeoutSeconds": 30 }""");

        var options = PlanOptions.Load(file, new Hashtable());

        Assert.Equal("from-file", options.AgentId);
        Assert.Equal(30, options.ResolveBusyTimeout());
    }

    /// The variable wins for the setting it names, and the rest of the file still applies.
    [Fact]
    public void Load_WithVariableOverTheFile_TakesTheVariable()
    {
        var file = WriteSettings("""{ "AgentId": "from-file", "BusyTimeoutSeconds": 30 }""");

        var options = PlanOptions.Load(file, new Hashtable { ["SNAIL_MCP_PLANS_AGENTID"] = "from-variable" });

        Assert.Equal("from-variable", options.AgentId);
        Assert.Equal(30, options.ResolveBusyTimeout());
    }

    /// A bare name or another server's variable would collide with whatever else the client process exports.
    [Fact]
    public void Load_WithVariablesNotOfThisServer_IgnoresThem()
    {
        var options = PlanOptions.Load(null, new Hashtable
        {
            ["AGENTID"] = "bare",
            ["SNAIL_MCP_MEMORY_AGENTID"] = "memory",
            ["SNAIL_MCP_PLANS_CONFIG"] = "/tmp/plans.json",
        });

        Assert.Null(options.AgentId);
        Assert.Equal(PlanOptions.DefaultBusyTimeoutSeconds, options.ResolveBusyTimeout());
    }

    /// No directory configured is the server's data folder, never the working directory.
    [Fact]
    public void ResolveDirectory_WithNoneConfigured_IsTheDataFolder()
    {
        var directory = new PlanOptions { Directory = "  " }.ResolveDirectory();

        Assert.Equal(ServerFolders.DataDirectory(PlanOptions.ServerName), directory);
    }

    /// A person writes ~/plans in a settings file, and no shell expands it on the way to the server.
    [Fact]
    public void ResolveDirectory_WithALeadingTilde_IsUnderTheHome()
    {
        var directory = new PlanOptions { Directory = "~/plans" }.ResolveDirectory();

        Assert.Equal(Path.Combine(ServerFolders.Home, "plans"), directory);
    }

    /// A relative database path is the working directory's, made absolute, with the busy timeout beside it.
    [Fact]
    public void ResolveConnectionString_WithARelativeFile_MakesItAbsolute()
    {
        var connection = new SqliteConnectionStringBuilder(new PlanOptions { DbPath = "plans.db" }.ResolveConnectionString());

        Assert.Equal(Path.GetFullPath("plans.db"), connection.DataSource);
        Assert.Equal(PlanOptions.DefaultBusyTimeoutSeconds, connection.DefaultTimeout);
    }

    private string WriteSettings(string json)
    {
        var file = Path.Combine(_directory, "config.json");

        File.WriteAllText(file, json);

        return file;
    }
}
