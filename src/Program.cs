using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Host.Extensions;

await Host.CreateApplicationBuilder(args)
    .ConfigureMcpServer()
    .Build()
    .RunAsync();
