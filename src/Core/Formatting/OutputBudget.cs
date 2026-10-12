using System.Text;

namespace Snail.MCP.Plans.Core.Formatting;

/// <summary>The size limit every tool answer is held to.</summary>
/// <remarks>
/// An unbounded answer is a hole a long-running plan eventually falls into, and silent truncation is
/// worse than none: the agent cannot tell a short plan from a clipped one, so the cut says so in words.
/// </remarks>
public static class OutputBudget
{
    /// <summary>Characters a tool answer may occupy.</summary>
    public const int CharacterLimit = 20_000;

    /// <summary>Line appended in place of what did not fit.</summary>
    public const string TruncationMarker = "[truncated: the answer hit the 20000 character limit]";

    /// <summary>Characters a settled step's note keeps when the plan is printed.</summary>
    /// <remarks>
    /// Measured in use: on plans of 46 and 79 steps the notes of finished steps filled the answer and the
    /// ids of the later steps fell past the limit, so they had to be read out of the database by hand.
    /// A finished step's note is a record to look up, not something needed on every read; an open step's
    /// note is what the agent is working from, so it is never cut.
    /// </remarks>
    public const int SettledNoteLength = 120;

    /// <summary>Shortens a settled step's note, leaving open steps untouched.</summary>
    public static string Note(string note, bool isSettled)
    {
        if (!isSettled || note.Length <= SettledNoteLength)
            return note;

        return $"{note[..SettledNoteLength].TrimEnd()}…";
    }

    /// <summary>Trims an answer to <see cref="CharacterLimit"/>, marking the cut when one happens.</summary>
    public static string Apply(string answer)
    {
        if (answer.Length <= CharacterLimit)
            return answer;

        var room = CharacterLimit - TruncationMarker.Length - 1;
        var cut = answer.LastIndexOf('\n', Math.Min(room, answer.Length - 1));

        var kept = cut > 0 ? answer[..cut] : answer[..room];

        return new StringBuilder(kept).Append('\n').Append(TruncationMarker).ToString();
    }
}
