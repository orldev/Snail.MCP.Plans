using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Client;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Infrastructure.Configuration;
using Snail.Sdk.Mcp.Hosting;

namespace Snail.MCP.Plans.Tests.Host;

/// The contract of the real server process, started the way a client starts it — the build beside the tests, or the
/// one SNAIL_PLANS_SERVER names: the composition root boots, the instruction reaches the client at the handshake,
/// every tool reaches the model, a whole plan can be walked over the wire, and stdout carries JSON-RPC and nothing else.
public partial class StdioContractTests
{
    private const string Assembly = "Snail.MCP.Plans";

    private const string Variable = "SNAIL_PLANS_SERVER";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly string Prefix = ServerSettings.PrefixOf(PlanOptions.ServerName);

    [Fact]
    public async Task Server_AtTheHandshake_NamesItselfAndTellsTheClientHowToWork()
    {
        await using var client = await StartAsync("handshake-agent");

        Assert.Equal("s3l-mcp-plans", client.ServerInfo?.Name);
        Assert.Contains("get_current_plan", client.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("create_plan", client.ServerInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_ListsTheFiveToolsUnderTheirWireNames()
    {
        await using var client = await StartAsync("listing-agent");

        var listed = await client.ListToolsAsync();

        var names = listed
            .Select(tool => tool.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(
            ["create_plan", "get_current_plan", "list_active_plans", "revise_plan", "update_step"],
            names);
    }

    /// <summary>
    /// The guard rules of every Snail server, on the list a model receives: names in snake case and listed once, the tool
    /// and every parameter described, read-only or destructive stated, every allowed value named, English.
    /// </summary>
    [Fact]
    public async Task Tools_AsListed_KeepTheGuardRules()
    {
        await using var client = await StartAsync("guard-agent");

        var listed = await client.ListToolsAsync();

        Assert.Empty(ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// The whole point of the server, walked over the wire the way an agent would: draft, come back
    /// without knowing the plan id, report a step, and be handed the next one.
    [Fact]
    public async Task Agent_OverTheWire_DraftsAPlanThenFindsItAgainWithoutKnowingItsId()
    {
        await using var client = await StartAsync("stdio-contract-agent");

        await CallAsync(client, "create_plan", new()
        {
            ["objective"] = "ship the plans server",
            ["steps"] = new[] { "write the store", "wire the tools" }
        });

        var current = await CallAsync(client, "get_current_plan", []);

        Assert.Contains("ship the plans server", current, StringComparison.Ordinal);

        var stepId = StepIdOf(current, "write the store");

        await CallAsync(client, "update_step", new() { ["stepId"] = stepId, ["status"] = "InProgress" });

        var reported = await CallAsync(client, "update_step", new()
        {
            ["stepId"] = stepId,
            ["status"] = "Done",
            ["note"] = "stored in sqlite"
        });

        Assert.Contains("wire the tools", reported, StringComparison.Ordinal);

        var active = await CallAsync(client, "list_active_plans", []);

        Assert.Contains("ship the plans server", active, StringComparison.Ordinal);
        Assert.Contains($"1 {nameof(StepStatus.Done)}", active, StringComparison.Ordinal);

        var plan = await CallAsync(client, "get_current_plan", []);
        var planId = PlanHeading().Match(plan).Groups[1].Value;
        var version = int.Parse(PlanVersion().Match(plan).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        var revised = await CallAsync(client, "revise_plan", new()
        {
            ["planId"] = planId,
            ["version"] = version,
            ["changes"] = new object[]
            {
                new Dictionary<string, object?> { ["kind"] = "add", ["title"] = "publish it" },
                new Dictionary<string, object?> { ["kind"] = "skip", ["step_id"] = StepIdOf(plan, "wire the tools"), ["reason"] = "done by hand" }
            }
        });

        Assert.Contains("publish it", revised, StringComparison.Ordinal);
    }

    /// A status the schema does not name never reaches the tool: the answer says which parameter was wrong and what it
    /// takes, flagged as an error, instead of the MCP SDK's bare "An error occurred invoking".
    [Fact]
    public async Task UpdateStep_WithAStatusTheSchemaLacks_IsAnsweredAsAnArgumentsFailure()
    {
        await using var client = await StartAsync("arguments-agent");

        var answer = await client.CallToolAsync(
            "update_step",
            new Dictionary<string, object?> { ["stepId"] = Guid.CreateVersion7().ToString(), ["status"] = "Finished" });

        Assert.True(answer.IsError, answer.Text());
        Assert.False(answer.IsOk(), answer.Text());
        Assert.Equal("arguments", answer.Type());
    }

    /// stdio carries JSON-RPC only: a single stray write breaks every MCP client. The client of <see cref="ServerProgram"/>
    /// would pass over such a line, so the process is read here line by line, and it ends cleanly once its stdin closes,
    /// having created the database it was told to use.
    [Fact]
    public async Task Process_UntilItsStdinCloses_WritesOnlyJsonRpcAndExitsCleanly()
    {
        var database = Path.Combine(TestFolder.Create(), "plans.db");
        var (command, arguments) = ServerProgram.Command(Assembly, Variable);
        var start = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        start.Environment["HOME"] = ServerProgram.Home;
        start.Environment["USERPROFILE"] = ServerProgram.Home;
        start.Environment[ServerSettings.FileVariableOf(PlanOptions.ServerName)] = ServerProgram.NoSettingsFile;
        start.Environment[Prefix + "DBPATH"] = database;
        start.Environment[Prefix + "AGENTID"] = "stdout-agent";

        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.WriteLineAsync(Message(1, "initialize", Handshake));
            await process.StandardInput.WriteLineAsync(Message(null, "notifications/initialized", null));
            await process.StandardInput.WriteLineAsync(Message(2, "tools/call", new
            {
                name = "create_plan",
                arguments = new { objective = "say nothing on stdout", steps = new[] { "answer" } }
            }));
            await process.StandardInput.FlushAsync();

            using var deadline = new CancellationTokenSource(Timeout);
            var answered = 0;

            while (answered < 2 && await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length is 0)
                    continue;

                using var message = ParseOrFail(line, process);

                if (message.RootElement.TryGetProperty("id", out _))
                    answered++;
            }

            process.StandardInput.Close();

            while (await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length > 0)
                    ParseOrFail(line, process).Dispose();
            }

            await process.WaitForExitAsync(deadline.Token);

            Assert.Equal(2, answered);
            Assert.Equal(0, process.ExitCode);
            Assert.True(File.Exists(database), $"the configured database was never created. stderr:\n{await stderr}");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [GeneratedRegex("# Plan ([0-9a-f-]{36})")]
    private static partial Regex PlanHeading();

    [GeneratedRegex(@"version (\d+)")]
    private static partial Regex PlanVersion();

    private static object Handshake => new
    {
        protocolVersion = "2025-06-18",
        capabilities = new { },
        clientInfo = new { name = "stdio-contract", version = "1.0" }
    };

    /// <summary>The server started with a database of its own in a test folder and an agent named for the test.</summary>
    private static Task<McpClient> StartAsync(string agent) =>
        ServerProgram.StartAsync(Assembly, Variable, new Dictionary<string, string?>
        {
            [Prefix + "DBPATH"] = Path.Combine(TestFolder.Create(), "plans.db"),
            [Prefix + "AGENTID"] = agent
        });

    /// <summary>The text a tool answered, failing the test when the call was flagged an error.</summary>
    private static async Task<string> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments)
    {
        var answer = await client.CallToolAsync(tool, arguments);
        var text = answer.Text();

        Assert.False(answer.IsError is true, $"{tool} answered with an error: {text}");

        return text;
    }

    private static string StepIdOf(string plan, string title)
    {
        var line = plan
            .Split('\n')
            .Single(candidate => candidate.Contains(title, StringComparison.Ordinal) && candidate.StartsWith("  [", StringComparison.Ordinal));

        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
    }

    private static string Message(int? id, string method, object? parameters)
    {
        var message = new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["method"] = method };

        if (id is { } value)
            message["id"] = value;

        if (parameters is not null)
            message["params"] = parameters;

        return JsonSerializer.Serialize(message);
    }

    /// <summary>The line as JSON, or the test failed with the line itself.</summary>
    private static JsonDocument ParseOrFail(string line, Process process)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);

            Assert.Fail($"stdout carries a line that is not JSON-RPC: {line}");

            throw;
        }
    }
}
