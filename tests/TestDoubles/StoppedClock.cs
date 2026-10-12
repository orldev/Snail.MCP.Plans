namespace Snail.MCP.Plans.Tests.TestDoubles;

/// <summary>A clock that only moves when a test moves it.</summary>
/// <remarks>Ages are part of what the tools answer, so the tests need to place two writes hours apart without waiting.</remarks>
public sealed class StoppedClock(DateTimeOffset start) : TimeProvider
{
    /// <summary>The instant every call sees.</summary>
    public DateTimeOffset Now { get; set; } = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => Now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan elapsed) => Now += elapsed;
}
