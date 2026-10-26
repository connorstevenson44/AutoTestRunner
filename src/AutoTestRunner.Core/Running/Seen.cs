namespace AutoTestRunner.Running;

/// <summary>
/// What a check saw: whether it passed, and what it found, short enough for a table, such as a service's state or the
/// line of a log that has the words. Full is all of it when that is longer, such as everything a command printed.
/// </summary>
public sealed record Seen(bool Passed, string Actual, string? Full = null);
