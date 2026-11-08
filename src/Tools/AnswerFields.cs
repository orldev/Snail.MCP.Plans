using System.Text.Json.Nodes;
using Snail.MCP.Plans.Core.Formatting;

namespace Snail.MCP.Plans.Tools;

/// <summary>The fields of <c>data</c> a tool answers, one overload per printed record.</summary>
/// <remarks>
/// The text stays the field the model reads, under one name on every tool; beside it go the ids and the version the
/// next call takes, so neither a model nor a script has to find them in the text. Statuses are spelled as the
/// schema takes them — <c>InProgress</c>, never another casing — because a status read here is sent straight back.
/// </remarks>
public static class AnswerFields
{
    /// <summary>A plan: its id, version and status, whether this call drafted it, and the plan as printed.</summary>
    public static JsonNode Of(PrintedPlan printed)
    {
        var fields = new JsonObject
        {
            ["planId"] = printed.Plan.Id.ToString(),
            ["version"] = printed.Plan.Version,
            ["status"] = printed.Plan.Status.ToString()
        };

        if (printed.Drafted is { } drafted)
            fields["drafted"] = drafted;

        fields["text"] = printed.Text;

        return fields;
    }

    /// <summary>A step reported: the plan's id, version and status, the step as it now stands, the next step, and the progress as printed.</summary>
    public static JsonNode Of(PrintedStep printed)
    {
        var fields = new JsonObject
        {
            ["planId"] = printed.Plan.Id.ToString(),
            ["version"] = printed.Plan.Version,
            ["planStatus"] = printed.Plan.Status.ToString(),
            ["stepId"] = printed.Step.Id.ToString(),
            ["status"] = printed.Step.Status.ToString()
        };

        if (printed.Next is { } next)
            fields["nextStepId"] = next.Id.ToString();

        fields["text"] = printed.Text;

        return fields;
    }

    /// <summary>The active plans: how many were read, how many rows could not be, and the plans as printed.</summary>
    public static JsonNode Of(PrintedListing printed)
    {
        var fields = new JsonObject { ["count"] = printed.Plans.Count };

        if (printed.Plans.Unreadable > 0)
            fields["unreadableRows"] = printed.Plans.Unreadable;

        fields["text"] = printed.Text;

        return fields;
    }
}
