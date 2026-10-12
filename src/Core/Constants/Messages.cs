namespace Snail.MCP.Plans.Core.Constants;

/// <summary>Text of the answers a tool sends back.</summary>
/// <remarks>Read by the agent as prose, so these are free to reword; the contracts it obeys live in <c>ToolDescriptions</c>.</remarks>
public static class Messages
{
    /// <summary>No plan is being worked on.</summary>
    public const string NoCurrentPlan =
        "No active plan for this agent in this project. Draft one with create_plan before starting work.";

    /// <summary>The agent already has a plan where it asked to draft one.</summary>
    /// <remarks>Names the call that settles a plan: knowing a plan must be closed first is useless without knowing how, and the way to do it is a parameter of a tool named after revising.</remarks>
    public const string PlanAlreadyActive =
        "This agent already has an active plan in this project, shown below. Carry on with it, revise it with revise_plan, or close it first — revise_plan with its planId, its version and outcome Completed (or Abandoned if the work was dropped) — and then draft the new one.";

    /// <summary>Told to an agent that drafted a second plan in a project someone else is already working in.</summary>
    /// <remarks>
    /// Said because the two plans are legitimate but easy to confuse: a session in another folder,
    /// wanting to look at the first plan, called create_plan and left a duplicate nobody meant to make.
    /// </remarks>
    public const string AnotherAgentIsHere =
        "Note: this project already has an active plan, walked by another agent. Yours is separate and both now count as active. To read theirs instead, call get_current_plan with its planId; if you drafted yours only to have a look, settle it with revise_plan and outcome Abandoned.";

    /// <summary>Nothing is being worked on anywhere.</summary>
    public const string NoActivePlans = "No active plans. Nothing is being worked on right now.";

    /// <summary>A plan of another agent was read by its identity.</summary>
    /// <remarks>Said out loud because the steps look exactly like one's own, and acting on another agent's plan is the mistake this read makes possible.</remarks>
    public const string SomeoneElsesPlan =
        "Reading only: this plan is walked by another agent, so do not report its steps or revise it.";

    /// <summary>A plan was asked for by an identity nothing matches.</summary>
    public const string PlanNotFound = "No plan with this id.";

    /// <summary>A step was asked for by an identity the plan does not hold.</summary>
    public const string StepNotFound = "This plan has no step with that id.";

    /// <summary>A revision or an update was built on a version that has since moved.</summary>
    public const string VersionConflict =
        "The plan changed since you read it, so nothing was written. Read it again with get_current_plan and redo this change on the current version.";

    /// <summary>A plan cannot be drafted without steps.</summary>
    public const string StepsRequired = "A plan needs at least one step.";

    /// <summary>A plan cannot be drafted without a goal.</summary>
    public const string ObjectiveRequired = "A plan needs an objective: what the work is meant to achieve.";

    /// <summary>Work was reported on a plan that is no longer active.</summary>
    public const string PlanSettled = "This plan is no longer active, so its steps cannot change.";

    /// <summary>No step is left to pick up, but the one in hand is still open.</summary>
    /// <remarks>Distinct from <see cref="PlanFinished"/>: announcing the plan settled while a step is in progress told the agent the work was over one step early.</remarks>
    public const string LastOpenStep =
        "This is the last open step: nothing else is waiting. Report it when it ends.";

    /// <summary>Nothing is waiting, and what holds the plan open is a failure.</summary>
    public const string OnlyFailureLeft =
        "Nothing else is waiting: this plan stays open until the failed step is retried or skipped with a reason.";

    /// <summary>Every step reached a terminal status.</summary>
    public const string PlanFinished =
        "Every step is settled. Close the plan by reporting its outcome, or add what is left with revise_plan.";

    /// <summary>A step failed and the agent has to choose what happens next.</summary>
    /// <remarks>Statuses are spelled exactly as the tool schema accepts them: a status named in prose is a value the agent will send straight back.</remarks>
    public const string AfterFailure =
        "This step failed. Either attempt it again (update_step to InProgress, which counts another attempt), skip it with a reason, or insert the step that unblocks it with revise_plan.";

    /// <summary>A skip was asked for without saying why.</summary>
    public const string SkipReasonRequired =
        "A skipped step keeps its reason on the record — say why it is being skipped.";
}
