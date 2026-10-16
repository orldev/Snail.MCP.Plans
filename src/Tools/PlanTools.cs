using System.ComponentModel;
using ModelContextProtocol.Server;
using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core;

namespace Snail.MCP.Plans.Tools;

/// <summary>The MCP tools an agent keeps its plan with.</summary>
/// <remarks>
/// None of them removes anything — a step is skipped, never deleted — so the ones that write say they are not destructive.
/// Each takes the call's token, which the MCP SDK fills in and leaves out of the schema: a client that cancels a call
/// stops the work behind it.
/// </remarks>
[McpServerToolType]
public sealed class PlanTools
{
    /// <summary>Drafts the plan a task is about to be done by.</summary>
    [McpServerTool(ReadOnly = false, Destructive = false), Description(ToolDescriptions.CreatePlan)]
    public Task<string> CreatePlan(
        PlanDrafting drafting,
        [Description("What the work is meant to achieve. Immutable once the plan exists.")] string objective,
        [Description("The steps, in the order they are to be walked.")] string[] steps,
        [Description(ToolDescriptions.ProjectParam)] string projectName = "",
        CancellationToken cancellationToken = default) =>
        drafting.DraftAsync(objective, steps, projectName, cancellationToken);

    /// <summary>Answers with the plan this agent is walking.</summary>
    [McpServerTool(ReadOnly = true), Description(ToolDescriptions.GetCurrentPlan)]
    public Task<string> GetCurrentPlan(
        PlanProgress progress,
        [Description(ToolDescriptions.ProjectParam)] string projectName = "",
        [Description("Read this plan instead, by its id — works for any agent's plan, read-only.")] string planId = "",
        CancellationToken cancellationToken = default) =>
        progress.CurrentAsync(projectName, planId, cancellationToken);

    /// <summary>Moves a step and answers with the progress and the next step.</summary>
    [McpServerTool(ReadOnly = false, Destructive = false), Description(ToolDescriptions.UpdateStep)]
    public Task<string> UpdateStep(
        PlanProgress progress,
        [Description("Id of the step, as the plan printed it.")] string stepId,
        [Description("Where the step now stands.")] StepStatus status,
        [Description("What came out of the step, or why it failed. Required when skipping.")] string note = "",
        [Description(ToolDescriptions.ProjectParam)] string projectName = "",
        CancellationToken cancellationToken = default) =>
        progress.AdvanceAsync(stepId, status, note, projectName, cancellationToken);

    /// <summary>Applies changes to the step list of a plan.</summary>
    [McpServerTool(ReadOnly = false, Destructive = false), Description(ToolDescriptions.RevisePlan)]
    public Task<string> RevisePlan(
        PlanRevision revision,
        [Description("Id of the plan to change.")] string planId,
        [Description("Version of the plan you last read; a stale version writes nothing.")] int version,
        [Description("The changes, applied in the order given.")] StepChange[] changes,
        [Description("Where the work stands now. Empty — left as it was.")] string summary = "",
        [Description("Settle the plan: Active, Completed, Failed or Abandoned. Empty — decided by the steps.")] string outcome = "",
        CancellationToken cancellationToken = default) =>
        revision.ReviseAsync(planId, version, changes, summary, outcome, cancellationToken);

    /// <summary>Answers with every active plan and how far along it is.</summary>
    [McpServerTool(ReadOnly = true), Description(ToolDescriptions.ListActivePlans)]
    public Task<string> ListActivePlans(
        ActivePlans active,
        [Description("Narrow to one project. Empty — every project.")] string projectName = "",
        CancellationToken cancellationToken = default) =>
        active.RenderAsync(projectName, cancellationToken);
}
