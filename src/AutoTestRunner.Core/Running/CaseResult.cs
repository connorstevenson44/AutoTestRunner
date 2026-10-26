namespace AutoTestRunner.Running;

/// <summary>One case's result: what failed and what the person noted.</summary>
public sealed class CaseResult(int number, string name)
{
    public int Number { get; } = number;
    public string Name { get; } = name;
    public CaseStatus Status { get; set; } = CaseStatus.Passed;
    public List<string> Failures { get; } = [];
    public List<string> Notes { get; } = [];
    public TimeSpan Duration { get; set; }
}
