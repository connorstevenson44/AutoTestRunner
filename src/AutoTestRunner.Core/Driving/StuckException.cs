namespace AutoTestRunner.Driving;

/// <summary>Something the run needs isn't there, or can't be used: what it was trying to do, and what it found instead.</summary>
public sealed class StuckException(string what, string? detail = null) : Exception(detail is null ? $"I couldn't {what}." : $"I couldn't {what}. {detail}")
{
    public string What { get; } = what;
    public string? Detail { get; } = detail;
}
