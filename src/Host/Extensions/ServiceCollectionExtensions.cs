using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.MCP.Plans.Host.Prompts;
using Snail.MCP.Plans.Infrastructure.Configuration;
using Snail.MCP.Plans.Infrastructure.Identity;
using Snail.MCP.Plans.Infrastructure.Persistence;

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
        /// <remarks>The directory is created eagerly: EF Core does not create a missing parent of the database file.</remarks>
        private IServiceCollection AddPlanStorage(PlanOptions options)
        {
            var connectionString = options.ResolveConnectionString();

            EnsureDatabaseDirectory(connectionString);

            services.AddPooledDbContextFactory<PlansDbContext>(builder => builder.UseSqlite(connectionString));
            services.AddSingleton<PlansDb>();
            services.AddSingleton<IPlanStore, EfPlanStore>();

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
                .AddMcpServer(server => server.ServerInstructions = ServerGuidance.Instructions)
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            return services;
        }
    }

    private static void EnsureDatabaseDirectory(string connectionString)
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

        if (dataSource == ":memory:")
            return;

        if (Path.GetDirectoryName(Path.GetFullPath(dataSource)) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
    }
}
