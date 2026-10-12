using Microsoft.EntityFrameworkCore;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Infrastructure.Persistence;

/// <summary>The plans, in SQLite through EF Core.</summary>
/// <remarks>
/// A plan is read and written whole: it is small, it is always used whole, and one write per revision
/// keeps the version check meaningful — <see cref="PlanRow.Version"/> is a concurrency token, so a write
/// built on a stale read updates nothing and says so instead of overwriting.
/// </remarks>
public sealed class EfPlanStore(PlansDb db) : IPlanStore
{
    /// <inheritdoc />
    public async Task<Plan?> CurrentAsync(string agentId, string projectName, CancellationToken cancellationToken = default)
    {
        await using var context = await db.CreateAsync(cancellationToken);

        var active = nameof(PlanStatus.Active);

        var row = await context.Plans
            .Include(plan => plan.Steps)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                plan => plan.AgentId == agentId && plan.ProjectName == projectName && plan.Status == active,
                cancellationToken);

        return row is null ? null : Restored(row);
    }

    /// <inheritdoc />
    public async Task<Plan?> FindAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        await using var context = await db.CreateAsync(cancellationToken);

        var row = await context.Plans
            .Include(plan => plan.Steps)
            .AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.Id == planId, cancellationToken);

        return row is null ? null : Restored(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Plan>> ActiveAsync(string projectName, CancellationToken cancellationToken = default)
    {
        await using var context = await db.CreateAsync(cancellationToken);

        var active = nameof(PlanStatus.Active);

        var query = context.Plans
            .Include(plan => plan.Steps)
            .AsNoTracking()
            .Where(plan => plan.Status == active);

        if (projectName.Length > 0)
            query = query.Where(plan => plan.ProjectName == projectName);

        var rows = await query
            .OrderByDescending(plan => plan.Updated)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(Restored)];
    }

    /// <inheritdoc />
    public async Task AddAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        await using var context = await db.CreateAsync(cancellationToken);

        context.Plans.Add(Stored(plan));

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TrySaveAsync(Plan plan, int expectedVersion, CancellationToken cancellationToken = default)
    {
        await using var context = await db.CreateAsync(cancellationToken);

        var row = await context.Plans
            .Include(stored => stored.Steps)
            .FirstOrDefaultAsync(stored => stored.Id == plan.Id, cancellationToken);

        if (row is null || row.Version != expectedVersion)
            return false;

        Overwrite(row, plan);

        try
        {
            await context.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private static void Overwrite(PlanRow row, Plan plan)
    {
        row.Objective = plan.Objective;
        row.Summary = plan.Summary;
        row.Status = plan.Status.ToString();
        row.Version = plan.Version;
        row.Updated = Milliseconds(plan.UpdatedAt);
        row.Completed = plan.CompletedAt is { } completed ? Milliseconds(completed) : null;

        var wanted = plan.Steps.ToDictionary(step => step.Id);

        foreach (var existing in row.Steps.ToList())
        {
            if (wanted.Remove(existing.Id, out var step))
            {
                Overwrite(existing, step);

                continue;
            }

            row.Steps.Remove(existing);
        }

        foreach (var added in wanted.Values)
            row.Steps.Add(Stored(plan.Id, added));
    }

    private static void Overwrite(StepRow row, Step step)
    {
        row.OrderIndex = step.OrderIndex;
        row.Title = step.Title;
        row.Status = step.Status.ToString();
        row.Note = step.Note;
        row.Attempts = step.Attempts;
        row.Started = step.StartedAt is { } started ? Milliseconds(started) : null;
        row.Completed = step.CompletedAt is { } completed ? Milliseconds(completed) : null;
    }

    private static PlanRow Stored(Plan plan) => new()
    {
        Id = plan.Id,
        AgentId = plan.AgentId,
        ProjectName = plan.ProjectName,
        Objective = plan.Objective,
        Summary = plan.Summary,
        Status = plan.Status.ToString(),
        Version = plan.Version,
        Created = Milliseconds(plan.CreatedAt),
        Updated = Milliseconds(plan.UpdatedAt),
        Completed = plan.CompletedAt is { } completed ? Milliseconds(completed) : null,
        Steps = [.. plan.Steps.Select(step => Stored(plan.Id, step))]
    };

    private static StepRow Stored(Guid planId, Step step) => new()
    {
        Id = step.Id,
        PlanId = planId,
        OrderIndex = step.OrderIndex,
        Title = step.Title,
        Status = step.Status.ToString(),
        Note = step.Note,
        Attempts = step.Attempts,
        Started = step.StartedAt is { } started ? Milliseconds(started) : null,
        Completed = step.CompletedAt is { } completed ? Milliseconds(completed) : null
    };

    private static Plan Restored(PlanRow row) => new()
    {
        Id = row.Id,
        AgentId = row.AgentId,
        ProjectName = row.ProjectName,
        Objective = row.Objective,
        Summary = row.Summary,
        Status = Enum.TryParse<PlanStatus>(row.Status, out var status) ? status : PlanStatus.Active,
        Version = row.Version,
        CreatedAt = Moment(row.Created),
        UpdatedAt = Moment(row.Updated),
        CompletedAt = row.Completed is { } completed ? Moment(completed) : null,
        Steps = [.. row.Steps.OrderBy(step => step.OrderIndex).Select(Restored)]
    };

    private static Step Restored(StepRow row) => new()
    {
        Id = row.Id,
        OrderIndex = row.OrderIndex,
        Title = row.Title,
        Status = Enum.TryParse<StepStatus>(row.Status, out var status) ? status : StepStatus.Pending,
        Note = row.Note,
        Attempts = row.Attempts,
        StartedAt = row.Started is { } started ? Moment(started) : null,
        CompletedAt = row.Completed is { } completed ? Moment(completed) : null
    };

    private static long Milliseconds(DateTimeOffset moment) => moment.ToUnixTimeMilliseconds();

    private static DateTimeOffset Moment(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
}
