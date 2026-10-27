using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>A watched run's view that answers questions as the test says, and remembers what was shown.</summary>
internal sealed class FakeRunView(StuckAnswer answer = StuckAnswer.Stop) : IRunView
{
    /// <summary>Each step's line as words, and the case each started.</summary>
    public List<string> Shown { get; } = [];
    public List<Activity> Steps { get; } = [];
    public List<ActivityKind> Kinds { get; } = [];
    public List<string> Waits { get; } = [];

    /// <summary>The wait steps that are over, as words, with what they saw.</summary>
    public List<Activity> Waited { get; } = [];
    public List<CheckResult> Checks { get; } = [];
    public List<CaseResult> Ended { get; } = [];

    /// <summary>Each command: "start line", then "out line" or "err line" for what it printed, then "exit code".</summary>
    public List<string> Commands { get; } = [];

    /// <summary>The failures of steps the case carried on after.</summary>
    public List<string> StepFailures { get; } = [];

    public int StuckCount { get; private set; }

    public void CaseStarted(int index, int count, Case item) => Shown.Add($"case {item.Name}");
    public void CaseFinished(CaseResult result) => Ended.Add(result);

    public void ShowStep(Activity step)
    {
        Shown.Add(step.Text);
        Steps.Add(step);
        Kinds.Add(step.Kind);
    }

    public void ShowWait(Activity what) => Waits.Add(what.Text);

    /// <summary>What a manual run asked the person to do, as words.</summary>
    public List<string> Turns { get; } = [];

    public void YourTurn(Activity? what)
    {
        if (what is not null)
            Turns.Add(what.Text);
    }

    /// <summary>Each place a manual run outlined, or null when it took the outline away.</summary>
    public List<ScreenRect?> Outlines { get; } = [];

    public void Outline(ScreenRect? box)
    {
        lock (Outlines)
            Outlines.Add(box);
    }

    void IRunView.Waited(Activity what, TimeSpan took) => Waited.Add(what);
    public void CommandStarted(string line, string? script) => Commands.Add("start " + line);
    public void CommandOutput(string line, bool error) => Commands.Add((error ? "err " : "out ") + line);
    public void CommandFinished(int exitCode, TimeSpan took) => Commands.Add($"exit {exitCode}");
    public void StepFailed(StepFailure failure) => StepFailures.Add(failure.Message);
    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) => Checks.AddRange(checks);
    public void ShowCarryingOn(TimeSpan after) { }

    /// <summary>Called as the run asks what to do when it is stuck.</summary>
    public Action? OnStuck { get; init; }

    public StuckAnswer AskStuck(string message, string screenshot)
    {
        StuckCount++;
        OnStuck?.Invoke();
        return answer;
    }

    public bool AskScriptError(string message) => false;
    public void KeepClear(ScreenRect rect) { }
    public void Finished(string summary, string resultsFolder, bool allPassed) { }
}
