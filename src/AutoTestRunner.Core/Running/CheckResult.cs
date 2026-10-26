namespace AutoTestRunner.Running;

/// <summary>A check at a checkpoint, and what the runner found.</summary>
public sealed record CheckResult(string Label, bool Passed, string? Detail);
