using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>An unattended run: everything is in the log, which goes to the console.</summary>
public sealed class ConsoleRunView : IRunView
{
    public void CaseStarted(int index, int count, Case item) { }
    public void CaseFinished(CaseResult result) { }
    public void ShowStep(Activity step) { }
    public void ShowWait(Activity what) { }
    public void YourTurn(Activity? what) { }
    public void Waited(Activity what, TimeSpan took) { }
    public void CommandStarted(string line, string? script) { }
    public void CommandOutput(string line, bool error) { }
    public void CommandFinished(int exitCode, TimeSpan took) { }
    public void StepFailed(StepFailure failure) { }
    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }
    public void ShowCarryingOn(TimeSpan after) { }
    public StuckAnswer AskStuck(string message, string screenshot) => StuckAnswer.Stop;
    public bool AskScriptError(string message) => false;
    public void KeepClear(ScreenRect rect) { }
    public void Finished(string summary, string resultsFolder, bool allPassed) { }
}
