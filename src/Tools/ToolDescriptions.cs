namespace Snail.MCP.Plans.Tools;

/// <summary>The text the model reads to decide what to call and what to pass.</summary>
/// <remarks>
/// These are contracts, not prose: they decide the agent's behaviour, and the guard tests fail when a
/// status, a change kind or a stated limit here stops matching the code.
/// </remarks>
public static class ToolDescriptions
{
    /// <summary>Contract of <c>create_plan</c>.</summary>
    public const string CreatePlan = """
        Draft the plan BEFORE starting a task of more than one step: the objective, then the steps in the
        order they are to be walked. Answers with the plan and the id of every step — those ids are what
        update_step takes.
        One active plan per project: called while one is already active, it answers with that plan instead
        of drafting a second, so a retried call never leaves two behind.
        """;

    /// <summary>Contract of <c>get_current_plan</c>.</summary>
    public const string GetCurrentPlan = """
        The plan this agent is working on: the objective, where it stands, and every step with its status
        and id. Needs no arguments — it answers by the identity of this server process.
        Call it at the start of a session and after losing context, before drafting anything: the plan you
        cannot remember is still here, and drafting a second one for the same work loses the first.
        Pass 'planId' to read any plan, including one another agent walks — that is how you look at a plan
        from a different folder, which list_active_plans only summarises. Never call create_plan just to
        look: it leaves a second plan behind.
        """;

    /// <summary>Contract of <c>update_step</c>.</summary>
    public const string UpdateStep = """
        Report a step and receive the next one. Call it when you start a step, and again the moment it
        ends — the answer carries the new progress and the next Pending step, so this replaces asking what
        to do next.
        Status is one of: Pending (not started), InProgress (taken up — counts an attempt), Done
        (finished), Failed (attempted, did not work — may be attempted again), Skipped (deliberately not
        done, and then a note saying why is required).
        A plan runs one step at a time: report the step in progress before taking up another. Took one up
        by mistake? Report it Pending again — the attempt it already spent is kept, no failure is
        invented. A step that is Done or Skipped is settled and does not move again.
        """;

    /// <summary>Contract of <c>revise_plan</c>.</summary>
    public const string RevisePlan = """
        Change the plan when the work turns out different from the draft — a step missing, a step worded
        wrongly, an order that no longer holds. Revise it rather than quietly walking away from it.
        Pass the version you last read; if the plan has changed since, nothing is written and you are told
        to read it again. Each change is one of: Add (a new step, at the end or after the step named in
        'after'), Edit (reword a step that has not settled), Skip (take a step out of the work, 'reason'
        required — it stays on the record), Reorder (move a step after another, or to the front).
        Steps are never deleted, only Skipped. Optionally pass 'summary' to say where the work stands, and
        'outcome' to settle the plan: Active (reopen it), Completed, Failed, Abandoned. Left alone, a plan
        completes itself once no step is open.
        """;

    /// <summary>Contract of <c>list_active_plans</c>.</summary>
    public const string ListActivePlans = """
        What every agent is working on right now: per plan the objective, step counts by status, the step
        in progress, and how long since it last moved — that age is what shows an agent stuck or gone.
        This is the one tool that answers about other agents, not the caller. Answers are capped at 20000
        characters and say so when cut.
        """;

    /// <summary>Contract of the project parameter shared by four tools.</summary>
    public const string ProjectParam =
        "Project the plan belongs to. Empty — the folder this server runs in, which is the usual answer.";
}
