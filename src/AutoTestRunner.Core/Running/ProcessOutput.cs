namespace AutoTestRunner.Running;

/// <summary>What a program printed, and how it ended.</summary>
public sealed record ProcessOutput(int ExitCode, string Output)
{
    public string FirstLine => Output.Split('\n', 2)[0].Trim();
}
