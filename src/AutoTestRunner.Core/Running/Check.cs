namespace AutoTestRunner.Running;

/// <summary>A check ready to run: its label, how long it may take to pass, and a test that returns null when it passes.</summary>
public sealed record Check(string Label, TimeSpan Within, Func<string?> Test);
