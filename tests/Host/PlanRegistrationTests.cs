using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.MCP.Plans.Host.Extensions;
using Snail.MCP.Plans.Infrastructure.Configuration;

namespace Snail.MCP.Plans.Tests.Host;

/// The container disposes only what it builds itself. A store handed to it ready-made outlived the server it
/// served, and nothing ever called its Dispose.
public class PlanRegistrationTests
{
    [Fact]
    public async Task Store_WhenTheServerShutsDown_IsDisposedWithIt()
    {
        var options = new PlanOptions { DbPath = Path.Combine(TestFolder.Create(), "plans.db") };
        IPlanStore store;

        await using (var provider = new ServiceCollection().AddPlans(options).BuildServiceProvider())
            store = provider.GetRequiredService<IPlanStore>();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.CurrentAsync("agent-one", "project-one"));
    }
}
