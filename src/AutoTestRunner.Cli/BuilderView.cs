using AutoTestRunner.Cli.Views;
using AutoTestRunner.Running;

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
}
