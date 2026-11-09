using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Snail.MCP.Plans.Application;
using Snail.MCP.Plans.Core;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Tools;

/// <summary>The MCP tools an agent keeps its plan with.</summary>
/// <remarks>
/// None of them removes anything — a step is skipped, never deleted — so the ones that write say they are not destructive.
/// Each takes the call's token, which the MCP SDK fills in and leaves out of the schema: a client that cancels a call
/// stops the work behind it. Every one answers in the fleet's envelope: <c>{"ok":true,"data":{…,"text":…}}</c>, the
/// printed plan in <c>data.text</c> with its ids beside it, or <c>{"ok":false,"error":…,"hint":…,"type":…}</c> flagged
/// as an error, so a refusal is no longer read as a plan. The text alone (<see cref="AnswerShape.None"/>): no tool states
/// an output schema, and structured content beside the text would carry a plan of up to 20 000 characters twice.
/// </remarks>
[McpServerToolType]
public sealed class PlanTools
{
    /// <summary>Drafts the plan a task is about to be done by.</summary>
    [McpServerTool(Name = "create_plan", ReadOnly = false, Destructive = false), Description(ToolDescriptions.CreatePlan)]
    public async Task<CallToolResult> CreatePlanAsync(
        PlanDrafting drafting,
        [Description("What the work is meant to achieve. Immutable once the plan exists.")] string objective,
        [Description("The steps, in the order they are to be walked.")] string[] steps,
        [Description(ToolDescriptions.ProjectParam)] string projectName = "",
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await drafting.DraftAsync(objective, steps, projectName, cancellationToken), AnswerFields.Of, AnswerShape.None);

    /// <summary>Answers with the plan this agent is walking.</summary>
    [McpServerTool(Name = "get_current_plan", ReadOnly = true, Idempotent = true, OpenWorld = false), Description(ToolDescriptions.GetCurrentPlan)]
    public async Task<CallToolResult> GetCurrentPlanAsync(
        PlanProgress progress,
        [Description(ToolDescriptions.ProjectParam)] string projectName = "",
        [Description("Read this plan instead, by its id — works for any agent's plan, read-only.")] string planId = "",
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await progress.CurrentAsync(projectName, planId, cancellationToken), AnswerFields.Of, AnswerShape.None);

    /// <summary>Moves a step and answers with the progress and the next step.</summary>
    [McpServerTool(Name = "update_step", ReadOnly = false, Destructive = false), Description(ToolDescriptions.UpdateStep)]
    public async Task<CallToolResult> UpdateStepAsync(
        PlanProgress progress,
        [Description("Id of the step, as the plan printed it.")] string stepId,
        [Description("Where the step now stands.")] StepStatus status,
        [Description("What came out of the step, or why it failed. Required when skipping.")] string note = "",
        [Description(ToolDescriptions.ProjectParam)] string projectName = "",
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await progress.AdvanceAsync(stepId, status, note, projectName, cancellationToken), AnswerFields.Of, AnswerShape.None);

    /// <summary>Applies changes to the step list of a plan.</summary>
    /// <remarks>
    /// The changes are optional but typed as never null: declared <c>StepChange[]?</c>, the published schema let every
    /// change and every field of one be null as well, which the tool would only fail on inside.
    /// </remarks>
    [McpServerTool(Name = "revise_plan", ReadOnly = false, Destructive = false), Description(ToolDescriptions.RevisePlan)]
    public async Task<CallToolResult> RevisePlanAsync(
        PlanRevision revision,
        [Description("Id of the plan to change.")] string planId,
        [Description("Version of the plan you last read; a stale version writes nothing.")] int version,
        [Description("The changes, applied in the order given. Empty — none, to settle the plan by its outcome alone.")] StepChange[] changes = null!,
        [Description("Where the work stands now. Empty — left as it was.")] string summary = "",
        [Description("Settle the plan: Active, Completed, Failed or Abandoned. Empty — decided by the steps.")] string outcome = "",
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await revision.ReviseAsync(planId, version, changes ?? [], summary, outcome, cancellationToken), AnswerFields.Of, AnswerShape.None);

    /// <summary>Answers with every active plan and how far along it is.</summary>
    [McpServerTool(Name = "list_active_plans", ReadOnly = true, Idempotent = true, OpenWorld = false), Description(ToolDescriptions.ListActivePlans)]
    public async Task<CallToolResult> ListActivePlansAsync(
        ActivePlans active,
        [Description("Narrow to one project. Empty — every project.")] string projectName = "",
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(await active.RenderAsync(projectName, cancellationToken), AnswerFields.Of, AnswerShape.None);
}
