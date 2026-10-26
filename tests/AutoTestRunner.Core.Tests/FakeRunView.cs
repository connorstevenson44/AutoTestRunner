using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>A watched run's view that answers questions as the test says, and remembers what was shown.</summary>
internal sealed class FakeRunView(StuckAnswer answer = StuckAnswer.Stop) : IRunView
{
    public List<string> Shown { get; } = [];
    public List<ActivityKind> Kinds { get; } = [];
    public List<string> Waits { get; } = [];
    public List<CheckResult> Checks { get; } = [];
    public List<CaseResult> Ended { get; } = [];

    /// <summary>Each command: "start line", then "out line" or "err line" for what it printed, then "exit code".</summary>
    public List<string> Commands { get; } = [];

    public int StuckCount { get; private set; }

    public void CaseStarted(int index, int count, Case item) => Shown.Add($"case {item.Name}");
    public void CaseFinished(CaseResult result) => Ended.Add(result);

    public void ShowStep(string text, ActivityKind kind = ActivityKind.Say, string? command = null)
    {
        Shown.Add(text);
        Kinds.Add(kind);
    }

    public void ShowWait(string what) => Waits.Add(what);
    public void CommandStarted(string line, string? script) => Commands.Add("start " + line);
    public void CommandOutput(string line, bool error) => Commands.Add((error ? "err " : "out ") + line);
    public void CommandFinished(int exitCode, TimeSpan took) => Commands.Add($"exit {exitCode}");
    public void ShowCheckpoint(string title, IReadOnlyList<CheckResult> checks, IReadOnlyList<string> look, IReadOnlyList<ToolButton> tools) => Checks.AddRange(checks);
    public void ShowCarryingOn(TimeSpan after) { }

    public StuckAnswer AskStuck(string message, string screenshot)
    {
        StuckCount++;
        return answer;
    }

    public bool AskScriptError(string message) => false;
    public void KeepClear(ScreenRect rect) { }
    public void Finished(string summary, string resultsFolder, bool allPassed) { }
}
