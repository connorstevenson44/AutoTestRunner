using Walkthrough.Driving;
using Walkthrough.Scripting;

namespace Walkthrough.Running;

/// <summary>A check at a checkpoint, and what the runner found.</summary>
public sealed record CheckResult(string Label, bool Passed, string? Detail);

/// <summary>A button on a checkpoint that opens something to look at, such as a folder or a file.</summary>
public sealed record ToolButton(string Label, Action Open);

/// <summary>What the person picked when the run got stuck.</summary>
public enum StuckAnswer { TryAgain, SkipCase, Stop }

/// <summary>
/// Where a run shows itself: the panel beside the app for a watched run, nothing for an unattended one, whose log
/// goes to the console. Called on the run's thread; a panel moves the calls to its own.
/// </summary>
public interface IRunHost
{
    void CaseStarted(int index, int count, Case item);

    /// <summary>What is happening now, or with step by step, what is up next.</summary>
    void ShowStep(string text);

    void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools);

    /// <summary>Shown for a few seconds at a checkpoint in a run that carries on by itself.</summary>
    void ShowCarryingOn();

    /// <summary>Asks a watched run's person what to do. message says what couldn't be done; screenshot is where it was saved.</summary>
    StuckAnswer AskStuck(string message, string screenshot);

    /// <summary>A mistake in the script itself. True goes on to the next case.</summary>
    bool AskScriptError(string message);

    /// <summary>Moves the panel off a control that is about to be used.</summary>
    void KeepClear(ScreenRect rect);

    void Finished(string summary, string resultsFolder, bool allPassed);
}

/// <summary>An unattended run: everything is in the log, which goes to the console.</summary>
public sealed class ConsoleHost : IRunHost
{
    public void CaseStarted(int index, int count, Case item) { }
    public void ShowStep(string text) { }
    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) { }
    public void ShowCarryingOn() { }
    public StuckAnswer AskStuck(string message, string screenshot) => StuckAnswer.Stop;
    public bool AskScriptError(string message) => false;
    public void KeepClear(ScreenRect rect) { }
    public void Finished(string summary, string resultsFolder, bool allPassed) { }
}
