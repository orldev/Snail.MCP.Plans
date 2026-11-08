using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.MCP.Plans.Host.Prompts;
using Snail.MCP.Plans.Infrastructure.Configuration;
using Snail.MCP.Plans.Infrastructure.Identity;
using Snail.MCP.Plans.Infrastructure.Persistence;
using Snail.MCP.Plans.Tools;
using Snail.Sdk.Mcp.Hosting;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Host.Extensions;

/// <summary>Registration of everything behind the tools.</summary>
public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the identity, the SQLite store and the processes the tools delegate to.</summary>
        public IServiceCollection AddPlans(PlanOptions options)
        {
            services.AddSingleton(options);
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<IAgentIdentity>(new ProcessIdentity(options));

            return services
                .AddPlanStorage(options)
                .AddPlanProcesses();
        }

        /// <summary>One SQLite file, shared with whatever other session has it open.</summary>
        /// <remarks>
        /// The directory is created eagerly: SQLite does not create a missing parent of the database file. The store is
        /// registered as a factory, never as a ready instance: the container disposes only what it builds itself.
        /// </remarks>
        private IServiceCollection AddPlanStorage(PlanOptions options)
        {
            var database = options.ResolveConnectionString();

            EnsureDatabaseDirectory(database);

            services.AddSingleton<IPlanStore>(_ => new SqlitePlanStore(database));

            return services;
        }

        /// <summary>The processes standing behind the five tools.</summary>
        private IServiceCollection AddPlanProcesses()
        {
            services.AddSingleton<PlanDrafting>();
            services.AddSingleton<PlanProgress>();
            services.AddSingleton<PlanRevision>();
            services.AddSingleton<ActivePlans>();

            return services;
        }

        /// <summary>Driving adapter: the tools reach the model over stdio, with the protocol in the handshake.</summary>
        public IServiceCollection AddMcpTransport()
        {
            services
                .AddMcpServer(server =>
                {
                    server.ServerInfo = ServerIdentity.Of(typeof(ServiceCollectionExtensions).Assembly);
                    server.ServerInstructions = ServerGuidance.Instructions;
                })
                .ExplainFailures()
                .WithStdioServerTransport()
                .WithTools<PlanTools>(ToolJson.Options(PlansJson.Default));

            return services;
        }
    }

    private static void EnsureDatabaseDirectory(string database)
    {
        var dataSource = new SqliteConnectionStringBuilder(database).DataSource;

        if (dataSource == ":memory:")
            return;

        if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
    }
}
