using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snail.MCP.Plans.Core;

/// <summary>What a revision does to a plan.</summary>
public enum ChangeKind
{
    /// <summary>Appends a step, or inserts it after an existing one.</summary>
    Add,

    /// <summary>Rewords a step that has not finished.</summary>
    Edit,

    /// <summary>Takes a step out of the work with a reason, leaving it on the record.</summary>
    Skip,

    /// <summary>Moves a step to another position.</summary>
    Reorder
}

/// <summary>One edit to the step list of a plan.</summary>
/// <remarks>
/// Identities travel as strings rather than <see cref="Guid"/>: the value is written by a model, and an
/// empty string is a meaning the schema can express while a malformed Guid is only an error.
/// </remarks>
public sealed record StepChange(
    [property: JsonPropertyName("kind"), Description("What to do: add, edit, skip or reorder.")]
    ChangeKind Kind,
    [property: JsonPropertyName("title"), Description("Step text — required by add and edit, ignored otherwise.")]
    string Title = "",
    [property: JsonPropertyName("step_id"), Description("Step this change applies to — required by edit, skip and reorder.")]
    string StepId = "",
    [property: JsonPropertyName("after"), Description("Put the step right after this one. Empty — at the end for add, at the front for reorder.")]
    string After = "",
    [property: JsonPropertyName("reason"), Description("Why the step is skipped — required by skip, it stays on the record in its place.")]
    string Reason = "");
