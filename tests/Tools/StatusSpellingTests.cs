using System.Reflection;
using Snail.MCP.Plans.Core;
using Snail.MCP.Plans.Core.Constants;
using Snail.MCP.Plans.Host.Prompts;
using Snail.MCP.Plans.Tools;

namespace Snail.MCP.Plans.Tests.Tools;

/// Every status the agent reads in a text is a value it sends straight back, so a status spelled any
/// other way than the schema accepts is a trap. Found live: the hint after a failure advised
/// "update_step to in_progress" while the schema only accepts "InProgress".
public class StatusSpellingTests
{
    private static readonly IReadOnlyList<Type> TextStores =
        [typeof(Messages), typeof(ToolDescriptions), typeof(ServerGuidance)];

    private static readonly IReadOnlyList<string> StatusNames =
    [
        .. Enum.GetNames<StepStatus>(),
        .. Enum.GetNames<PlanStatus>(),
        .. Enum.GetNames<ChangeKind>()
    ];

    [Theory]
    [MemberData(nameof(Texts))]
    public void Text_NamingAStatus_SpellsItTheWayTheSchemaAcceptsIt(string origin, string text)
    {
        foreach (var misspelling in StatusNames.SelectMany(Misspellings))
            Assert.False(text.Contains(misspelling, StringComparison.OrdinalIgnoreCase),
                $"{origin} says '{misspelling}', which the tool schema rejects. Spell the status as the enum does.");
    }

    public static TheoryData<string, string> Texts()
    {
        var texts = new TheoryData<string, string>();

        foreach (var store in TextStores)
        {
            foreach (var constant in store
                         .GetFields(BindingFlags.Public | BindingFlags.Static)
                         .Where(field => field is { IsLiteral: true, FieldType: { } type } && type == typeof(string)))
            {
                texts.Add($"{store.Name}.{constant.Name}", (string)constant.GetRawConstantValue()!);
            }
        }

        return texts;
    }

    /// Only names of more than one word can be misspelled this way: a single word is already identical
    /// to every casing convention a model might reach for. Separated by a space it is not checked —
    /// "the step in progress" is ordinary English prose, not an attempt at the value.
    private static IEnumerable<string> Misspellings(string status)
    {
        var words = SplitWords(status);

        if (words.Count < 2)
            return [];

        return [string.Join('_', words), string.Join('-', words)];
    }

    private static IReadOnlyList<string> SplitWords(string name)
    {
        List<string> words = [];
        var start = 0;

        for (var position = 1; position <= name.Length; position++)
        {
            if (position != name.Length && !char.IsUpper(name[position]))
                continue;

            words.Add(name[start..position].ToLowerInvariant());
            start = position;
        }

        return words;
    }
}
