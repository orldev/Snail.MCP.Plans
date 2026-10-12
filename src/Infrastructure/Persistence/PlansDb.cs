using Microsoft.EntityFrameworkCore;

namespace Snail.MCP.Plans.Infrastructure.Persistence;

/// <summary>Hands out <see cref="PlansDbContext"/> instances with the schema guaranteed to exist.</summary>
/// <remarks>
/// Write-ahead logging is switched on at first use and is what makes a second session's read work while
/// the first is writing; creation failing because another process got there first counts as success,
/// which is why the schema is probed rather than the exception trusted.
/// </remarks>
public sealed class PlansDb(IDbContextFactory<PlansDbContext> factory) : IDisposable
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _initialized;

    /// <summary>Creates a context; the first call prepares the database.</summary>
    public async Task<PlansDbContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
            await InitializeAsync(cancellationToken);

        return await factory.CreateDbContextAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _initLock.Dispose();

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
                return;

            await using var context = await factory.CreateDbContextAsync(cancellationToken);

            await CreateSchemaAsync(context, cancellationToken);

            await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static async Task CreateSchemaAsync(PlansDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (Exception)
        {
            await context.Plans.AnyAsync(cancellationToken);
        }
    }
}
