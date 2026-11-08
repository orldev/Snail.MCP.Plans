using Snail.MCP.Plans.Core.Formatting;
using Snail.Sdk.Mcp.Hosting.Answers;

namespace Snail.MCP.Plans.Tests.TestDoubles;

/// <summary>What an answer of the application says, read the way a model reads its envelope.</summary>
internal static class AnswerReading
{
    /// <summary>The printed text of a success, or the error and the hint of a refusal: the words the model reads either way.</summary>
    public static string Words<T>(this Answer<T> answer) where T : Printed =>
        answer.TryGet(out var printed) ? printed.Text : $"{answer.Error} {answer.Hint}";

    /// <summary>Holds that the answer is a refusal of the kind and in the words given.</summary>
    public static void IsRefused<T>(this Answer<T> answer, string error, string type)
    {
        Assert.False(answer.IsSuccess, "the answer succeeded where a refusal was expected");
        Assert.Equal(type, answer.Type);
        Assert.Equal(error, answer.Error);
    }
}
