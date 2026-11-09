using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Host.Extensions;
using Snail.Sdk.Mcp.Hosting;

try
{
    await McpHost.CreateBuilder(args)
        .ConfigureMcpServer()
        .Build()
        .RunAsync();

    return 0;
}
catch (StartRefusedException refused)
{
    await Console.Error.WriteLineAsync(refused.Message);

    return 1;
}
