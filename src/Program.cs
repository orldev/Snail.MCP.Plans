using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Host.Extensions;
using Snail.Sdk.Mcp.Hosting;

ServerFolders.MoveFromSnailNames("plans");

await McpHost.CreateBuilder(args)
    .ConfigureMcpServer()
    .Build()
    .RunAsync();
