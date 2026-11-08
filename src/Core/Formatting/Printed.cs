namespace Snail.MCP.Plans.Core.Formatting;

/// <summary>What a tool prints for the model to read, beside the facts the next call needs.</summary>
/// <remarks>
/// The text stays the answer's body because it is what the model reads, held to <see cref="OutputBudget"/>; the
/// records behind it are kept beside it so the envelope can name the plan's id and version as fields, which a model
/// or a script used to fish out of the text with a pattern.
/// </remarks>
public abstract record Printed(string Text);

/// <summary>A whole plan as printed: what <c>create_plan</c>, <c>get_current_plan</c> and <c>revise_plan</c> answer.</summary>
/// <param name="Plan">The plan printed.</param>
/// <param name="Text">The plan as the model reads it.</param>
/// <param name="Drafted">Whether this call drafted the plan, or found one already active; null where nothing was drafted.</param>
public sealed record PrintedPlan(Plan Plan, string Text, bool? Drafted = null) : Printed(Text);

/// <summary>A step reported: what <c>update_step</c> answers.</summary>
/// <param name="Plan">The plan after the report.</param>
/// <param name="Step">The step as reported.</param>
/// <param name="Next">The step to take up next, when one is waiting.</param>
/// <param name="Text">The progress and the next step as the model reads them.</param>
public sealed record PrintedStep(Plan Plan, Step Step, Step? Next, string Text) : Printed(Text);

/// <summary>The active plans: what <c>list_active_plans</c> answers.</summary>
/// <param name="Plans">The plans read, with the rows the read passed over.</param>
/// <param name="Text">The plans as the model reads them.</param>
public sealed record PrintedListing(PlanListing Plans, string Text) : Printed(Text);
