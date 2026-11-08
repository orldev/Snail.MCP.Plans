namespace Snail.MCP.Plans.Host.Prompts;

/// <summary>The instruction the client receives at <c>initialize</c>.</summary>
/// <remarks>
/// Measured on a sibling server: without an instruction the tools were called in 6% of the sessions they
/// applied to. It travels with the server, so it works in any client and cannot drift out of step with
/// the tool set the way a per-project note does.
/// </remarks>
public static class ServerGuidance
{
    /// <summary>What this server expects an agent to do, in the order it expects it.</summary>
    public const string Instructions = """
        This server holds your plan. Use it for any task of more than one step.

        At the start of a session, and whenever you have lost context, call get_current_plan before
        anything else. It takes no arguments and answers by this process's identity. A plan you no longer
        remember is still here, and drafting a second one for the same work loses the first.

        Before starting a multi-step task, call create_plan with the objective and the steps.

        Then walk the plan in order. Call update_step when you take a step up (InProgress) and again the
        moment it ends (Done, or Failed with a note). Its answer already carries the progress and the next
        step, so never ask separately what comes next.

        When the work turns out different from the draft, call revise_plan instead of quietly departing
        from the plan: add the step you found missing, reword one, skip one with a reason, reorder them.
        Steps are never deleted, and a failed step keeps the plan active until you retry it or skip it.
        Pass the version you last read; a stale version writes nothing.

        list_active_plans answers about every agent rather than about you — it is what another session
        calls to see what is running and how far it has gone.

        Every answer is JSON with an ok flag. On success data.text is the plan as you read it, with
        data.planId, data.version and the step ids the next call takes beside it. When ok is false, read
        error and hint and change the call instead of repeating it; type names the refusal (noPlan,
        versionConflict, stepNotFound, transition, …).
        """;
}
