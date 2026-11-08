using Microsoft.Extensions.Hosting;
using Snail.MCP.Plans.Infrastructure.Configuration;

namespace Snail.MCP.Plans.Host.Extensions;

/// <summary>Composition root of the MCP server.</summary>
public static class HostBuilderExtensions
{
    extension(HostApplicationBuilder builder)
    {
        /// <summary>Wires the plans of the settings the host read (<see cref="PlanOptions.From"/>) and the MCP server over stdio.</summary>
        public HostApplicationBuilder ConfigureMcpServer()
        {
            builder.Services
                .AddPlans(PlanOptions.From(builder.Configuration))
                .AddMcpTransport();

            return builder;
        }
    }
}
