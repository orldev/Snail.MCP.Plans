using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Interfaces;

namespace Snail.MCP.Plans.Benchmarks;

/// <summary>Plans of the size a long task reaches, built the same on every run.</summary>
public static class Sample
{
    /// <summary>The moment every benchmark reads as now.</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The agent the plans belong to.</summary>
    public const string Agent = "bench-agent";

    /// <summary>The project the plans belong to.</summary>
    public const string Project = "bench-project";

    /// <summary>A plan of <paramref name="steps"/> steps: the first third settled with long notes, one in progress, the rest pending.</summary>
    public static Plan Of(int number, int steps)
    {
        var settled = steps / 3;
        var list = new List<Step>(steps);

        for (var index = 0; index < steps; index++)
        {
            var status = index < settled ? (index % 5 == 4 ? StepStatus.Skipped : StepStatus.Done) : index == settled ? StepStatus.InProgress : StepStatus.Pending;

            list.Add(new Step
            {
                Id = Id(number, index + 1),
                OrderIndex = (index + 1) * Ordering.Gap,
                Title = $"step {index + 1} of the work: read the module, change it and run its tests",
                Status = status,
                Note = status is StepStatus.Pending ? "" : new string('n', 40 + index % 200),
                Attempts = status is StepStatus.Pending ? 0 : 1 + index % 3,
                StartedAt = status is StepStatus.Pending ? null : Now.AddMinutes(-steps + index),
                CompletedAt = index < settled ? Now.AddMinutes(-steps + index + 1) : null
            });
        }

        return new Plan
        {
            Id = Id(number, 0),
            AgentId = number == 0 ? Agent : $"agent-{number}",
            ProjectName = number == 0 ? Project : $"project-{number % 4}",
            Objective = "carry the server over to the shared standard without changing what it does",
            Summary = "the store and the tools are done; the answers are next",
            Version = 7,
            CreatedAt = Now.AddHours(-3),
            UpdatedAt = Now.AddMinutes(-number * 7),
            Steps = list
        };
    }

    /// <summary>The same id for the same plan and step on every run.</summary>
    public static Guid Id(int plan, int step) => new(plan, (short)step, 0, [1, 2, 3, 4, 5, 6, 7, 8]);

    /// <summary>A store holding one plan that never changes: every call starts from the same state.</summary>
    public sealed class FixedStore(Plan plan) : IPlanStore
    {
        public Task<Plan?> CurrentAsync(string agentId, string projectName, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(plan);

        public Task<Plan?> FindAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult<Plan?>(plan);

        public Task<IReadOnlyList<Plan>> ActiveAsync(string projectName, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Plan>>([plan]);

        public Task AddAsync(Plan plan, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TrySaveAsync(Plan plan, int expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    /// <summary>The agent of the sample plan.</summary>
    public sealed class Identity : IAgentIdentity
    {
        public string AgentId => Agent;

        public string ProjectName => Project;
    }

    /// <summary>A clock stopped at <see cref="Now"/>.</summary>
    public sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
