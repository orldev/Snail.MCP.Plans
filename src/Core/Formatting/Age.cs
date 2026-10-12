namespace Snail.MCP.Plans.Core.Formatting;

/// <summary>Renders how long ago something happened.</summary>
/// <remarks>An absolute timestamp tells a reading session nothing about a stalled agent; an age does.</remarks>
public static class Age
{
    /// <summary>A span as a coarse phrase — "just now", "6 minutes ago", "3 hours ago", "2 days ago".</summary>
    public static string Since(DateTimeOffset moment, DateTimeOffset now)
    {
        var elapsed = now - moment;

        if (elapsed < TimeSpan.FromMinutes(1))
            return "just now";

        if (elapsed < TimeSpan.FromHours(1))
            return Plural((int)elapsed.TotalMinutes, "minute");

        if (elapsed < TimeSpan.FromDays(1))
            return Plural((int)elapsed.TotalHours, "hour");

        return Plural((int)elapsed.TotalDays, "day");
    }

    private static string Plural(int count, string unit) =>
        count is 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}
