namespace Snail.MCP.Plans.Core.Constants;

/// <summary>The <c>type</c> of a refusal: the word a program branches on, where the error is prose for the model.</summary>
/// <remarks>
/// A contract like a status name: a hook or a script that tells "no plan yet" from "the plan moved" reads this word,
/// never the error's text, which stays free to reword. Spelled in camel case, as the fleet's failure types are.
/// </remarks>
public static class FailureTypes
{
    /// <summary>A value the tool cannot work with: no objective, no steps, a skip without a reason, an unknown outcome.</summary>
    /// <remarks>The same word the SDK answers for an argument that did not bind, since to the caller both are the same mistake.</remarks>
    public const string Arguments = "arguments";

    /// <summary>This agent has no active plan in the project.</summary>
    public const string NoPlan = "noPlan";

    /// <summary>No plan has the id given.</summary>
    public const string PlanNotFound = "planNotFound";

    /// <summary>The plan holds no step of the id given.</summary>
    public const string StepNotFound = "stepNotFound";

    /// <summary>The plan moved since the version the caller read; nothing was written.</summary>
    public const string VersionConflict = "versionConflict";

    /// <summary>A step cannot move to the status asked for from the one it is in.</summary>
    public const string Transition = "transition";

    /// <summary>Another step of the plan is already in progress.</summary>
    public const string StepInProgress = "stepInProgress";

    /// <summary>The plan or the step is settled and does not change any more.</summary>
    public const string Settled = "settled";

    /// <summary>The plan is walked by another agent.</summary>
    public const string NotYours = "notYours";

    /// <summary>The plans file holds an active plan that cannot be read.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>Another session of this agent drafted at the same moment and already settled what it drafted.</summary>
    public const string Outraced = "outraced";
}
