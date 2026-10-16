using System.Text;
using Snail.MCP.Plans.Core.Constants;

namespace Snail.MCP.Plans.Core.Formatting;

/// <summary>Renders plans as the text a tool answers with.</summary>
/// <remarks>
/// Step statuses are printed under the very names the agent passes back to <c>update_step</c>, so the
/// answer doubles as the vocabulary of the next call and nothing has to be translated in either direction.
/// </remarks>
public static class PlanView
{
    /// <summary>A whole plan: goal, status, counts and every step with its identity.</summary>
    public static string Render(Plan plan, DateTimeOffset now, string prologue = "")
    {
        var text = new StringBuilder();

        if (prologue.Length > 0)
            text.AppendLine(prologue).AppendLine();

        text.AppendLine($"# Plan {plan.Id} · {plan.ProjectName}");
        text.AppendLine($"objective: {plan.Objective}");

        if (plan.Summary.Length > 0)
            text.AppendLine($"summary: {plan.Summary}");

        text.AppendLine($"status: {plan.Status} · version {plan.Version} · updated {Age.Since(plan.UpdatedAt, now)}");
        text.AppendLine($"progress: {Counted(Progress.Of(plan))}");
        text.AppendLine();
        text.AppendLine("steps:");

        foreach (var step in plan.Steps.OrderBy(step => step.OrderIndex))
            text.AppendLine(Line(step));

        return OutputBudget.Apply(text.ToString());
    }

    /// <summary>Names the steps a revision created, so their ids need not be hunted for in the plan below.</summary>
    /// <remarks>On a long plan the printed steps run past the answer limit, and the ids of what was just added were the part that got cut.</remarks>
    public static string Added(IReadOnlyList<Step> added)
    {
        if (added.Count is 0)
            return "";

        var lines = added.Select(step => $"  {step.Id} {step.Title}");

        return string.Join('\n', [$"added {added.Count}:", .. lines]);
    }

    /// <summary>The line naming the rows a read passed over, or nothing when it passed over none.</summary>
    /// <remarks>Every answer that prints a plan carries it: a plan printed without a step it could not read looks complete, and the agent counts its progress over that list.</remarks>
    public static string Unread(PlanListing read) =>
        read.Unreadable > 0 ? $"note: {Rows(read.Unreadable)} {Messages.UnreadableRows}" : "";

    /// <summary>Joins the two lines an answer opens with, leaving out an empty one.</summary>
    /// <remarks>Two lines and no array: a params array and a filtered join cost update_step 0.14 KB a call, measured, for lines that are almost always empty.</remarks>
    public static string Prologue(string first, string second)
    {
        if (first.Length is 0)
            return second;

        return second.Length is 0 ? first : $"{first}\n{second}";
    }

    /// <summary>What <c>update_step</c> answers: the new counts, then what to do next.</summary>
    public static string Advanced(Plan plan, Step reported, Step? next, string hint = "")
    {
        var text = new StringBuilder();

        text.AppendLine($"{reported.Status}: {reported.Title}");

        if (reported.Note.Length > 0)
            text.AppendLine($"note: {reported.Note}");

        text.AppendLine($"progress: {Counted(Progress.Of(plan))} · version {plan.Version}");
        text.AppendLine();

        if (hint.Length > 0)
            text.AppendLine(hint);

        text.AppendLine(Next(plan, next));

        return OutputBudget.Apply(text.ToString());
    }

    /// <summary>What <c>list_active_plans</c> answers: one block per plan, the stalest last.</summary>
    public static string Active(PlanListing plans, DateTimeOffset now)
    {
        if (plans.Count is 0 && plans.Unreadable is 0)
            return Messages.NoActivePlans;

        var text = new StringBuilder();

        text.AppendLine($"# Active plans: {plans.Count}");

        if (plans.Unreadable > 0)
            text.AppendLine(Unread(plans));

        foreach (var plan in plans)
        {
            var current = plan.Steps
                .OrderBy(step => step.OrderIndex)
                .FirstOrDefault(step => step.Status is StepStatus.InProgress);

            text.AppendLine();
            text.AppendLine($"## {Heading(plan)}");
            text.AppendLine($"objective: {plan.Objective}");

            if (plan.Summary.Length > 0)
                text.AppendLine($"summary: {plan.Summary}");

            text.AppendLine($"progress: {Counted(Progress.Of(plan))}");
            text.AppendLine($"current step: {(current is null ? "none in progress" : current.Title)}");
            text.AppendLine($"updated: {Age.Since(plan.UpdatedAt, now)}");
            text.AppendLine($"plan: {plan.Id}");
        }

        return OutputBudget.Apply(text.ToString());
    }

    /// <remarks>
    /// Having no next step is not the same as being finished: with the last step in progress there is
    /// nothing to hand over yet, and saying the plan was settled ended the work one step early.
    /// </remarks>
    private static string Next(Plan plan, Step? next)
    {
        if (next is not null)
            return $"next step — {next.Id}: {next.Title}";

        var progress = Progress.Of(plan);

        if (progress.IsSettled)
            return Messages.PlanFinished;

        return progress.InProgress > 0 ? Messages.LastOpenStep : Messages.OnlyFailureLeft;
    }

    /// <remarks>By default the agent is named after the folder it runs in, which is also the project — printing both would repeat one word on every line.</remarks>
    private static string Heading(Plan plan) =>
        plan.AgentId.Equals(plan.ProjectName, StringComparison.OrdinalIgnoreCase)
            ? plan.ProjectName
            : $"{plan.ProjectName} · {plan.AgentId}";

    private static string Rows(int count) => count is 1 ? "1 row" : $"{count} rows";

    private static string Counted(Progress progress)
    {
        var settled = $"{progress.Done} {nameof(StepStatus.Done)}, {progress.Failed} {nameof(StepStatus.Failed)}, {progress.Skipped} {nameof(StepStatus.Skipped)}";
        var open = $"{progress.InProgress} {nameof(StepStatus.InProgress)}, {progress.Pending} {nameof(StepStatus.Pending)}";

        return $"{progress.Total} steps — {settled}, {open} · {progress.Attempts} attempts";
    }

    private static string Line(Step step)
    {
        var attempts = step.Attempts > 1 ? $" ({step.Attempts} attempts)" : "";
        var shortened = OutputBudget.Note(step.Note, Transitions.IsTerminal(step.Status));
        var note = shortened.Length > 0 ? $" — {shortened}" : "";

        return $"  [{step.Status}] {step.Id} {step.Title}{attempts}{note}";
    }
}
