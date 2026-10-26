namespace AutoTestRunner.Running;

/// <summary>A check at a checkpoint, and what the runner found: what it looked at, what it wanted and what it saw.</summary>
public sealed record CheckResult(string Label, Subject Subject, string How, string Expected, string Actual, bool Passed)
{
    /// <summary>All of what it saw, when Actual is a part of it.</summary>
    public string? Full { get; init; }

    /// <summary>What it found when it failed, for the log; null when it passed.</summary>
    public string? Detail => Passed ? null : How.Length == 0 ? Actual : $"found {Actual}";

    /// <summary>A check that couldn't be made, and why, such as the app not being ready.</summary>
    public static CheckResult Unmade(string why) => new("The check", new Subject(SubjectKind.Value, "The check"), "", "", why, false);
}
