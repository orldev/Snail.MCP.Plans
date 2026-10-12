using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Infrastructure.Configuration;

namespace Snail.MCP.Plans.Host.Extensions;

/// <summary>Composition root of the MCP server.</summary>
public static class HostBuilderExtensions
{
    extension(HostApplicationBuilder builder)
    {
        /// <summary>Wires logging to stderr, the plans of <see cref="PlanOptions.Load"/> and the MCP server over stdio.</summary>
        public HostApplicationBuilder ConfigureMcpServer()
        {
            builder.Services
                .AddPlans(PlanOptions.Load())
                .AddMcpTransport();

            return builder;
        }
    }
}
