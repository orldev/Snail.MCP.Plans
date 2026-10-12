using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Formatting;
using Snail.MCP.Plans.Host.Prompts;
using Snail.MCP.Plans.Tools;

namespace Snail.MCP.Plans.Tests.Tools;

/// Guard tests over the text the model reads. A hardcoded description drifts away from the code it
/// describes the moment either changes, and the drift is invisible at runtime: adding a status or
/// moving a limit has to fail a build instead.
public class ToolDescriptionsTests
{
    private static readonly IReadOnlyList<MethodInfo> Tools =
    [
        .. typeof(PlanTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
    ];

    [Fact]
    public void EveryTool_CarriesADescription()
    {
        foreach (var tool in Tools)
            Assert.False(string.IsNullOrWhiteSpace(tool.GetCustomAttribute<DescriptionAttribute>()?.Description),
                $"{tool.Name} is exposed to the model with no description.");
    }

    [Fact]
    public void EveryToolParameter_CarriesADescription()
    {
        foreach (var parameter in Tools.SelectMany(tool => tool.GetParameters()).Where(IsModelSupplied))
            Assert.False(string.IsNullOrWhiteSpace(parameter.GetCustomAttribute<DescriptionAttribute>()?.Description),
                $"{parameter.Member.Name}({parameter.Name}) is filled in by the model with nothing to go on.");
    }

    /// The plan the owner approved names five tools; a sixth would be a contract the specification
    /// does not describe, and four would leave one of its four requirements unanswered.
    [Fact]
    public void Server_Exposes_TheFiveToolsOfTheSpecification() =>
        Assert.Equal(
            ["CreatePlan", "GetCurrentPlan", "ListActivePlans", "RevisePlan", "UpdateStep"],
            Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));

    [Fact]
    public void UpdateStep_Names_EveryStepStatus()
    {
        foreach (var status in Enum.GetNames<StepStatus>())
            Assert.Contains(status, ToolDescriptions.UpdateStep, StringComparison.Ordinal);
    }

    [Fact]
    public void RevisePlan_Names_EveryChangeKind()
    {
        foreach (var kind in Enum.GetNames<ChangeKind>())
            Assert.Contains(kind, ToolDescriptions.RevisePlan, StringComparison.Ordinal);
    }

    [Fact]
    public void RevisePlan_Names_EveryPlanStatus()
    {
        foreach (var status in Enum.GetNames<PlanStatus>())
            Assert.Contains(status, ToolDescriptions.RevisePlan, StringComparison.Ordinal);
    }

    /// A limit stated in prose and a limit in the code are one fact written twice.
    [Fact]
    public void ListActivePlans_States_TheRealCharacterLimit() =>
        Assert.Contains(OutputBudget.CharacterLimit.ToString(), ToolDescriptions.ListActivePlans, StringComparison.Ordinal);

    [Fact]
    public void TruncationMarker_States_TheRealCharacterLimit() =>
        Assert.Contains(OutputBudget.CharacterLimit.ToString(), OutputBudget.TruncationMarker, StringComparison.Ordinal);

    /// The instruction is the only thing that makes the tools get called at all, so it has to mention
    /// each of them by the name the client will list.
    [Fact]
    public void ServerInstructions_Name_EveryTool()
    {
        foreach (var tool in Tools.Select(tool => SnakeCase(tool.Name)))
            Assert.Contains(tool, ServerGuidance.Instructions, StringComparison.Ordinal);
    }

    private static bool IsModelSupplied(ParameterInfo parameter) =>
        parameter.ParameterType.Namespace?.StartsWith("Snail.MCP.Plans.Application", StringComparison.Ordinal) is not true;

    private static string SnakeCase(string name) =>
        string.Concat(name.Select((letter, position) =>
            char.IsUpper(letter) && position > 0 ? $"_{char.ToLowerInvariant(letter)}" : $"{char.ToLowerInvariant(letter)}"));
}
