using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Core.Interfaces;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Application;

/// <summary>Changing a plan while the work is under way.</summary>
/// <remarks>
/// The caller states the version it read and the write is refused if the plan has moved since: two
/// sessions share one database, and without the check the later revision would silently swallow the earlier.
/// </remarks>
public sealed class PlanRevision(IPlanStore plans, IAgentIdentity identity, TimeProvider clock)
{
    /// <summary>Applies the changes in order and answers with the plan they produced.</summary>
    public async Task<Answer<PrintedPlan>> ReviseAsync(
        string planId,
        int version,
        IReadOnlyList<StepChange> changes,
        string summary = "",
        string outcome = "",
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(planId, out var wanted))
            return PlanNotFound;

        if (outcome.Length > 0 && !Enum.TryParse<PlanStatus>(outcome, ignoreCase: true, out _))
            return UnknownOutcome(outcome);

        var read = await plans.FindAsync(wanted, cancellationToken);

        if (read.Count is 0)
            return PlanNotFound;

        var plan = read[0];

        if (!plan.AgentId.Equals(identity.AgentId, StringComparison.OrdinalIgnoreCase))
            return NotYours(plan);

        if (plan.Status is PlanStatus.Failed or PlanStatus.Abandoned)
            return Answer.Failed(Messages.PlanSettled, Messages.DraftANewOne, FailureTypes.Settled);

        if (plan.Version != version)
            return VersionConflict;

        var now = clock.GetUtcNow();
        var rework = new Rework(plan.Steps);

        foreach (var change in changes)
        {
            if (rework.Apply(change, now) is { } refusal)
                return refusal;
        }

        var updated = Reconciled(plan, rework.Steps, summary, outcome, now);

        if (!await plans.TrySaveAsync(updated, plan.Version, cancellationToken))
            return VersionConflict;

        var text = PlanView.Render(updated, now, PlanView.Prologue(PlanView.Added(rework.AddedSteps), PlanView.Unread(read)));

        return Answer.Ok(new PrintedPlan(updated, text));
    }

    private static AnswerFailure PlanNotFound => new(Messages.PlanNotFound, Messages.PlanIdFrom, FailureTypes.PlanNotFound);

    private static AnswerFailure VersionConflict => new(Messages.VersionConflict, Messages.ReadAgain, FailureTypes.VersionConflict);

    private static AnswerFailure StepNotFound => new(Messages.StepNotFound, Messages.StepIdFrom, FailureTypes.StepNotFound);

    private static Plan Reconciled(Plan plan, IReadOnlyList<Step> steps, string summary, string outcome, DateTimeOffset now)
    {
        var revised = plan with
        {
            Steps = steps,
            Summary = string.IsNullOrWhiteSpace(summary) ? plan.Summary : summary.Trim(),
            UpdatedAt = now,
            Version = plan.Version + 1
        };

        if (outcome.Length is 0)
            return Settlement.Reconcile(revised, now);

        var declared = Enum.Parse<PlanStatus>(outcome, ignoreCase: true);

        return declared is PlanStatus.Active
            ? revised with { Status = PlanStatus.Active, CompletedAt = null }
            : revised with { Status = declared, CompletedAt = now };
    }

    private static AnswerFailure NotYours(Plan plan) =>
        new($"This plan belongs to agent '{plan.AgentId}'.", Messages.NotYours, FailureTypes.NotYours);

    private static AnswerFailure UnknownOutcome(string outcome) => new(
        $"Unknown outcome '{outcome}'.",
        $"allowed: {string.Join(", ", Enum.GetNames<PlanStatus>())}",
        FailureTypes.Arguments);

    /// <summary>The step list of one revision while the changes are being applied to it.</summary>
    /// <remarks>
    /// Holds the order in the list and re-spaces the indexes after every change, because inside a
    /// revision <see cref="Step.OrderIndex"/> still describes the state the call started from: reading
    /// the order from it undid whatever the previous change had just done.
    /// </remarks>
    private sealed class Rework(IReadOnlyList<Step> steps)
    {
        private readonly Dictionary<Guid, Guid> _tails = [];
        private readonly List<Step> _added = [];

        /// <summary>The steps as the applied changes left them.</summary>
        public IReadOnlyList<Step> Steps { get; private set; } = steps;

        /// <summary>Steps this revision created, in the order they were asked for.</summary>
        public IReadOnlyList<Step> AddedSteps => _added;

        /// <summary>Applies one change, answering with a refusal when it cannot be made.</summary>
        public AnswerFailure? Apply(StepChange change, DateTimeOffset now)
        {
            var refusal = change.Kind switch
            {
                ChangeKind.Add => Add(change),
                ChangeKind.Edit => Edit(change),
                ChangeKind.Skip => Skip(change, now),
                ChangeKind.Reorder => Reorder(change),
                _ => new AnswerFailure(
                    $"Unknown change '{change.Kind}'.",
                    $"allowed: {string.Join(", ", Enum.GetNames<ChangeKind>())}",
                    FailureTypes.Arguments)
            };

            if (refusal is null)
                Steps = Ordering.Respaced(Steps);

            return refusal;
        }

        private AnswerFailure? Add(StepChange change)
        {
            if (string.IsNullOrWhiteSpace(change.Title))
                return new($"{ChangeKind.Add} needs the text of the step to add.", "pass it as 'title'", FailureTypes.Arguments);

            var step = new Step
            {
                Id = Guid.CreateVersion7(),
                OrderIndex = Ordering.Last(Steps),
                Title = change.Title.Trim()
            };

            _added.Add(step);

            if (string.IsNullOrWhiteSpace(change.After))
            {
                Steps = [.. Steps, step];

                return null;
            }

            return Insert(step, change.After, chained: true);
        }

        private AnswerFailure? Edit(StepChange change)
        {
            if (Located(change.StepId) is not { } target)
                return StepNotFound;

            if (string.IsNullOrWhiteSpace(change.Title))
                return new($"{ChangeKind.Edit} needs the new text of the step.", "pass it as 'title'", FailureTypes.Arguments);

            if (Transitions.IsTerminal(target.Status))
                return new($"Step '{target.Title}' is {target.Status}: a settled step is a record of what happened and is not reworded.", Type: FailureTypes.Settled);

            Steps = [.. Steps.Select(step => step.Id == target.Id ? step with { Title = change.Title.Trim() } : step)];

            return null;
        }

        private AnswerFailure? Skip(StepChange change, DateTimeOffset now)
        {
            if (Located(change.StepId) is not { } target)
                return StepNotFound;

            if (string.IsNullOrWhiteSpace(change.Reason))
                return new(Messages.SkipReasonRequired, $"{Messages.SayWhy} in 'reason'", FailureTypes.Arguments);

            if (!Transitions.CanMove(target.Status, StepStatus.Skipped))
                return new($"Step '{target.Title}' is {target.Status} and cannot be skipped.", Type: FailureTypes.Settled);

            var skipped = target with
            {
                Status = StepStatus.Skipped,
                Note = change.Reason.Trim(),
                CompletedAt = now
            };

            Steps = [.. Steps.Select(step => step.Id == target.Id ? skipped : step)];

            return null;
        }

        private AnswerFailure? Reorder(StepChange change)
        {
            if (Located(change.StepId) is not { } target)
                return StepNotFound;

            Steps = [.. Steps.Where(step => step.Id != target.Id)];

            if (string.IsNullOrWhiteSpace(change.After))
            {
                Steps = [target, .. Steps];

                return null;
            }

            return Insert(target, change.After, chained: false);
        }

        /// <remarks>
        /// Additions chain: several steps added after the same one keep the order they were asked in,
        /// each landing behind the previous arrival rather than in front of it. A reorder does not —
        /// "after this step" there means exactly that position, not behind whatever was just added.
        /// </remarks>
        private AnswerFailure? Insert(Step moving, string afterId, bool chained)
        {
            if (Located(afterId) is not { } requested)
                return new($"No step with id '{afterId}' to place it after.", Messages.StepIdFrom, FailureTypes.StepNotFound);

            var anchorId = chained && _tails.TryGetValue(requested.Id, out var tail) ? tail : requested.Id;

            var ordered = Steps.ToList();
            ordered.Insert(ordered.FindIndex(step => step.Id == anchorId) + 1, moving);

            if (chained)
                _tails[requested.Id] = moving.Id;

            Steps = ordered;

            return null;
        }

        private Step? Located(string stepId) =>
            Guid.TryParse(stepId, out var id)
                ? Steps.FirstOrDefault(step => step.Id == id)
                : null;
    }
}
