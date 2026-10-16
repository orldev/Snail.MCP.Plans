using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Infrastructure.Configuration;

namespace Snail.MCP.Plans.Tests.Host;

/// The contract of the real server process: stdout carries JSON-RPC and nothing else, the composition
/// root boots, the instruction reaches the client at the handshake, every tool reaches the model, and a
/// whole plan can be walked over the wire.
public partial class StdioContractTests
{
    private const string ProtocolVersion = "2025-06-18";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly string Prefix = Snail.Sdk.Mcp.Hosting.ServerSettings.PrefixOf(PlanOptions.ServerName);

    private static readonly string ConfigVariable = Snail.Sdk.Mcp.Hosting.ServerSettings.FileVariableOf(PlanOptions.ServerName);

    [Fact]
    public async Task Server_AtTheHandshake_NamesItselfAndTellsTheClientHowToWork()
    {
        using var session = await ServerSession.StartAsync();

        var initialize = await session.CallAsync(1, "initialize", Handshake);

        var result = initialize.GetProperty("result");

        Assert.Equal("s3l-mcp-plans", result.GetProperty("serverInfo").GetProperty("name").GetString());

        var instructions = result.GetProperty("instructions").GetString()!;

        Assert.Contains("get_current_plan", instructions, StringComparison.Ordinal);
        Assert.Contains("create_plan", instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_ListsTheFiveToolsUnderTheirWireNames()
    {
        using var session = await ServerSession.StartAsync();

        await session.CallAsync(1, "initialize", Handshake);
        await session.NotifyAsync("notifications/initialized");

        var listed = await session.CallAsync(2, "tools/list", new { });

        var names = listed.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
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
        var database = Path.Combine(Snail.Sdk.Mcp.Testing.ServerProgram.Home, "plans.db");
        var environment = new Dictionary<string, string?> { [Prefix + "DBPATH"] = database, [Prefix + "AGENTID"] = "guard-agent" };

        await using var client = await Snail.Sdk.Mcp.Testing.ServerProgram.StartAsync("Snail.MCP.Plans", "SNAIL_PLANS_SERVER", environment);

        var listed = await client.ListToolsAsync();

        Assert.Empty(Snail.Sdk.Mcp.Testing.ToolGuards.Problems(listed.Select(tool => tool.ProtocolTool)));
    }

    /// The whole point of the server, walked over the wire the way an agent would: draft, come back
    /// without knowing the plan id, report a step, and be handed the next one.
    [Fact]
    public async Task Agent_OverTheWire_DraftsAPlanThenFindsItAgainWithoutKnowingItsId()
    {
        using var session = await ServerSession.StartAsync();

        await session.CallAsync(1, "initialize", Handshake);
        await session.NotifyAsync("notifications/initialized");

        await session.CallToolAsync(2, "create_plan", new
        {
            objective = "ship the plans server",
            steps = new[] { "write the store", "wire the tools" }
        });

        var current = await session.CallToolAsync(3, "get_current_plan", new { });

        Assert.Contains("ship the plans server", current, StringComparison.Ordinal);

        var stepId = StepIdOf(current, "write the store");

        await session.CallToolAsync(4, "update_step", new { stepId, status = "InProgress" });

        var reported = await session.CallToolAsync(5, "update_step", new
        {
            stepId,
            status = "Done",
            note = "stored in sqlite"
        });

        Assert.Contains("wire the tools", reported, StringComparison.Ordinal);

        var active = await session.CallToolAsync(6, "list_active_plans", new { });

        Assert.Contains("ship the plans server", active, StringComparison.Ordinal);
        Assert.Contains($"1 {nameof(StepStatus.Done)}", active, StringComparison.Ordinal);

        var plan = await session.CallToolAsync(7, "get_current_plan", new { });
        var planId = PlanHeading().Match(plan).Groups[1].Value;
        var version = int.Parse(PlanVersion().Match(plan).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        var revised = await session.CallToolAsync(8, "revise_plan", new
        {
            planId,
            version,
            changes = new object[]
            {
                new { kind = "add", title = "publish it" },
                new { kind = "skip", step_id = StepIdOf(plan, "wire the tools"), reason = "done by hand" }
            }
        });

        Assert.Contains("publish it", revised, StringComparison.Ordinal);

        var exitCode = await session.ShutdownAsync();

        Assert.Equal(0, exitCode);
        Assert.All(session.StdoutLines, line => session.ParseOrFail(line));
        Assert.True(session.HasDatabaseFile, $"the configured database was never created. stderr:\n{session.StderrText}");
    }

    [GeneratedRegex("# Plan ([0-9a-f-]{36})")]
    private static partial Regex PlanHeading();

    [GeneratedRegex(@"version (\d+)")]
    private static partial Regex PlanVersion();

    private static object Handshake => new
    {
        protocolVersion = ProtocolVersion,
        capabilities = new { },
        clientInfo = new { name = "stdio-contract", version = "1.0" }
    };

    private static string StepIdOf(string plan, string title)
    {
        var line = plan
            .Split('\n')
            .Single(candidate => candidate.Contains(title, StringComparison.Ordinal) && candidate.StartsWith("  [", StringComparison.Ordinal));

        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
    }

    private sealed class ServerSession : IDisposable
    {
        private readonly Process _process;
        private readonly string _databaseFile;
        private readonly List<string> _stderr = [];

        private ServerSession(Process process, string databaseFile)
        {
            _process = process;
            _databaseFile = databaseFile;
        }

        public List<string> StdoutLines { get; } = [];

        public bool HasDatabaseFile => File.Exists(_databaseFile);

        public string StderrText => string.Join('\n', _stderr);

        public static Task<ServerSession> StartAsync()
        {
            var databaseFile = Path.Combine(TestFolder.Create(), "plans.db");

            var (command, arguments) = Snail.Sdk.Mcp.Testing.ServerProgram.Command("Snail.MCP.Plans", "SNAIL_PLANS_SERVER");
            var start = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            start.Environment[Prefix + "DBPATH"] = databaseFile;
            start.Environment[Prefix + "AGENTID"] = "stdio-contract-agent";
            start.Environment[ConfigVariable] = Snail.Sdk.Mcp.Testing.ServerProgram.NoSettingsFile;

            var session = new ServerSession(Process.Start(start)!, databaseFile);
            session.DrainStderr();

            return Task.FromResult(session);
        }

        public async Task<JsonElement> CallAsync(int id, string method, object parameters)
        {
            await NotifyAsync(method, parameters, id);

            return await ReadAsync();
        }

        public async Task<string> CallToolAsync(int id, string name, object arguments)
        {
            var answer = await CallAsync(id, "tools/call", new { name, arguments });

            Assert.False(answer.TryGetProperty("error", out var failure),
                $"{name} answered with an error: {failure}\nstderr:\n{StderrText}");

            return answer.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        }

        public async Task NotifyAsync(string method, object? parameters = null, int? id = null)
        {
            var envelope = new Dictionary<string, object> { ["jsonrpc"] = "2.0", ["method"] = method };

            if (id is { } value) envelope["id"] = value;
            if (parameters is not null) envelope["params"] = parameters;

            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(envelope));
            await _process.StandardInput.FlushAsync();
        }

        public async Task<int> ShutdownAsync()
        {
            _process.StandardInput.Close();

            using var deadline = new CancellationTokenSource(Timeout);

            while (await _process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length > 0) StdoutLines.Add(line);
            }

            await _process.WaitForExitAsync(deadline.Token);

            return _process.ExitCode;
        }

        /// <summary>stdio carries JSON-RPC only: a single stray write breaks every MCP client, so the line itself is reported.</summary>
        public JsonElement ParseOrFail(string line)
        {
            try
            {
                return JsonDocument.Parse(line).RootElement;
            }
            catch (JsonException)
            {
                Assert.Fail($"stdout carries a line that is not JSON-RPC: {line}\nstderr:\n{StderrText}");

                throw;
            }
        }

        public void Dispose()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);

            _process.Dispose();

            foreach (var path in new[] { _databaseFile, $"{_databaseFile}-wal", $"{_databaseFile}-shm" })
                File.Delete(path);
        }

        private async Task<JsonElement> ReadAsync()
        {
            using var deadline = new CancellationTokenSource(Timeout);

            while (await _process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
            {
                if (line.Length is 0)
                    continue;

                StdoutLines.Add(line);

                var message = ParseOrFail(line);

                if (message.TryGetProperty("id", out _))
                    return message;
            }

            Assert.Fail($"the server closed stdout without answering. stderr:\n{StderrText}");

            throw new InvalidOperationException();
        }

        private void DrainStderr() =>
            _ = Task.Run(async () =>
            {
                while (await _process.StandardError.ReadLineAsync() is { } line)
                    _stderr.Add(line);
            });
    }
}
