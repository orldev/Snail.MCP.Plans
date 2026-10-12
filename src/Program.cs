using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Host.Extensions;
using Snail.Toolkit.Mcp.Hosting;

await McpHost.CreateBuilder(args)
    .ConfigureMcpServer()
    .Build()
    .RunAsync();
