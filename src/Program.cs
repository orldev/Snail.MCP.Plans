using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Host.Extensions;
using Snail.MCP.Plans.Infrastructure.Configuration;
using Snail.Sdk.Mcp.Hosting;

ServerFolders.MoveFromSnailNames(PlanOptions.ServerName);

await McpHost.CreateBuilder(args)
    .ConfigureMcpServer()
    .Build()
    .RunAsync();
