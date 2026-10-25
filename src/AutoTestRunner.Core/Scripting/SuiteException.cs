namespace AutoTestRunner.Scripting;

/// <summary>A suite that can't be run as it is, with every problem found.</summary>
public sealed class SuiteException(IReadOnlyList<string> problems) : Exception(string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
