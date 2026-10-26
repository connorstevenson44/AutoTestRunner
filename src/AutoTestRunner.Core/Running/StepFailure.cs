namespace AutoTestRunner.Running;

/// <summary>
/// Why a step failed while the case carries on: in words for the log, and when the step wanted something, what it
/// wanted and what it found, for the panel to set side by side.
/// </summary>
public sealed record StepFailure(string Message, string? Expected = null, string? Actual = null)
{
    public override string ToString() => Message;
}
