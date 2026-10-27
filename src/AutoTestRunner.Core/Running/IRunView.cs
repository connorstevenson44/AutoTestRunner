using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// Where a run shows itself: the panel beside the app for a watched run, nothing for an unattended one, whose log
/// goes to the console. Called on the run's thread; a panel moves the calls to its own. Each call does nothing, and
/// a question stops, unless the view says otherwise.
/// </summary>
public interface IRunView
{
    void CaseStarted(int index, int count, Case item) { }

    /// <summary>A case is over: passed, failed, skipped or stopped, and why.</summary>
    void CaseFinished(CaseResult result) { }

    /// <summary>What is happening now, or with step by step, what is up next: an action, or narration.</summary>
    void ShowStep(Activity step) { }

    /// <summary>What the run is waiting to see in the app, such as a status bar's text. It lasts until the next call.</summary>
    void ShowWait(Activity what) { }

    /// <summary>
    /// A manual run is waiting for the person to do a step in the app: what to do, with how in its Detail. Null once it
    /// is done, or when the run does it after all, as when the speed is changed from manual.
    /// </summary>
    void YourTurn(Activity? what) { }

    /// <summary>
    /// Keeps an outline around a place on the screen, in a manual run, for the person to find what to use: until it is
    /// moved, or taken away with null. The mouse goes straight through it to the app.
    /// </summary>
    void Outline(ScreenRect? box) { }

    /// <summary>A wait step is over: what it waited for, what it saw, and how long it took.</summary>
    void Waited(Activity what, TimeSpan took) { }

    /// <summary>
    /// The run started a program or script: its command line, and a script's text. What it prints follows as
    /// CommandOutput, as it prints it, then CommandFinished. Output can come on any thread.
    /// </summary>
    void CommandStarted(string line, string? script) { }

    void CommandOutput(string line, bool error) { }

    void CommandFinished(int exitCode, TimeSpan took) { }

    /// <summary>
    /// The step just shown failed, and the case carries on: a message box said something else, say. A checkpoint's own
    /// checks show with it instead.
    /// </summary>
    void StepFailed(StepFailure failure) { }

    void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }

    /// <summary>Shown at a checkpoint in a run that carries on by itself, which it does after the time given.</summary>
    void ShowCarryingOn(TimeSpan after) { }

    /// <summary>Asks a watched run's person what to do. message says what couldn't be done; screenshot is where it was saved.</summary>
    StuckAnswer AskStuck(string message, string screenshot) => StuckAnswer.Stop;

    /// <summary>A mistake in the script itself. True goes on to the next case.</summary>
    bool AskScriptError(string message) => false;

    /// <summary>Moves the panel off a control that is about to be used.</summary>
    void KeepClear(ScreenRect rect) { }

    void Finished(string summary, string resultsFolder, bool allPassed) { }
}
