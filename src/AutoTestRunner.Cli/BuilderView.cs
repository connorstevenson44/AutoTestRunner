using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// Where the runner shows the steps the builder plays: the panel says which step is playing, and a step that gets
/// stuck or fails is noted, for the panel to mark, instead of asking what to do.
/// </summary>
internal sealed class BuilderView(BuilderPanel panel) : IRunView
{
    /// <summary>Why the last step played didn't work, or null. Read and cleared on the builder's thread.</summary>
    public string? Problem { get; set; }

    public void ShowStep(Activity step) => panel.Busy(step.Text);

    public void StepFailed(StepFailure failure) => Problem = failure.Message;

    public StuckAnswer AskStuck(string message, string screenshot)
    {
        Problem = message;
        return StuckAnswer.SkipCase;
    }

    public bool AskScriptError(string message)
    {
        Problem = message;
        return false;
    }

    public void CaseStarted(int index, int count, Case item) { }

    public void CaseFinished(CaseResult result) { }

    public void ShowWait(Activity what) { }

    public void YourTurn(Activity? what) { }

    public void Waited(Activity what, TimeSpan took) { }

    public void CommandStarted(string line, string? script) { }

    public void CommandOutput(string line, bool error) { }

    public void CommandFinished(int exitCode, TimeSpan took) { }

    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }

    public void ShowCarryingOn(TimeSpan after) { }

    public void KeepClear(ScreenRect rect) { }

    public void Finished(string summary, string resultsFolder, bool allPassed) { }
}
