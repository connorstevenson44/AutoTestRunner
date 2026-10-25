using Walkthrough.Driving;
using Walkthrough.Running;
using Walkthrough.Scripting;

namespace Walkthrough.Core.Tests;

/// <summary>A control in the fake app.</summary>
internal sealed class FakeControl(string type, string name, string? id = null, string? helpText = null) : IControl
{
    public string Type { get; } = type;
    public string Name { get; set; } = name;
    public string? Value { get; set; }
    public string? HelpText { get; } = helpText;
    public string? AutomationId { get; } = id;
    public string? Selection { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// The app under test, as a list of controls. Clicking a button runs what OnClick says, so a test can script the app:
/// Save sets the status bar, for example. A control that isn't there is stuck straight away, without waiting.
/// </summary>
internal sealed class FakeDriver : IDriver
{
    public List<FakeControl> Controls { get; } = [];
    public Dictionary<string, Action> OnClick { get; } = [];
    public List<string> Done { get; } = [];
    public bool Started { get; private set; }

    public FakeControl Add(string type, string name, string? id = null, string? helpText = null)
    {
        var control = new FakeControl(type, name, id, helpText);
        Controls.Add(control);
        return control;
    }

    public void Start(AppSettings app, string exe)
    {
        Started = true;
        Done.Add($"start {exe}");
    }

    public void Close() => Done.Add("close");

    public void CloseAll(string exe) => Done.Add("close all");

    public IControl Find(Target target, TimeSpan timeout) => TryFind(target) ?? throw new StuckException($"find {target.What}");

    public IControl? TryFind(Target target) =>
        target.Selectors.Select(selector => Controls.FirstOrDefault(control => selector.Matches(control.AutomationId, control.Name, control.Type, control.HelpText, null)))
            .FirstOrDefault(control => control is not null);

    public void WaitGone(Target target, TimeSpan timeout)
    {
        if (TryFind(target) is not null)
            throw new StuckException($"see {target.What} go");
    }

    public void Point(IControl control, string what) => Done.Add($"point {control.Name}");

    public void Click(IControl control, bool useMouse)
    {
        Done.Add($"click {control.Name}");
        if (OnClick.TryGetValue(control.Name, out var reaction))
            reaction();
    }

    public void Type(IControl control, string text, bool secret)
    {
        ((FakeControl)control).Value = text;
        Done.Add($"type {(secret ? "****" : text)}");
    }

    public void Choose(IControl control, string option) => ((FakeControl)control).Selection = option;

    public void SetChecked(IControl control, bool isChecked) => Done.Add($"tick {control.Name} {isChecked}");

    public bool ReplaceText(IControl control, string pattern, string replacement)
    {
        var fake = (FakeControl)control;
        var regex = new System.Text.RegularExpressions.Regex(pattern);
        if (!regex.IsMatch(fake.Value ?? ""))
            return false;
        fake.Value = regex.Replace(fake.Value!, replacement, 1);
        return true;
    }

    public void Key(string keys) => Done.Add($"key {keys}");

    public string WaitDialog(string title, TimeSpan timeout) => throw new StuckException($"see the \"{title}\" message");

    public void PressDialogButton(string title, DialogButton button) { }

    public string? OtherWindow() => null;

    public void PickFile(string title, string path, bool folder) => Done.Add($"pick {path}");

    public void Screenshot(string path) => File.WriteAllText(path, "png");

    public string DumpTree() => string.Join("\n", Controls.Select(control => $"{control.Type} \"{control.Name}\""));

    public void Dispose() { }
}

internal sealed class FakeMachine : IMachine
{
    public Dictionary<string, string> Services { get; } = [];

    public string? ServiceState(string name) => Services.GetValueOrDefault(name);

    public bool TaskExists(string path) => false;

    /// <summary>What a PowerShell script prints and its exit code, by its first line.</summary>
    public Dictionary<string, ProcessOutput> Scripts { get; } = [];

    // A program prints its command line.
    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null) =>
        Printed(new(0, $"{program} {string.Join(" ", arguments)}"), output);

    public ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null, Action<string, bool>? output = null) =>
        Printed(Scripts.GetValueOrDefault(script.Split('\n')[0].Trim(), new(0, "")), output);

    // Says each line as it is printed, the last as an error when the exit code isn't 0.
    private static ProcessOutput Printed(ProcessOutput result, Action<string, bool>? output)
    {
        var lines = result.Output.Length == 0 ? [] : result.Output.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            output?.Invoke(lines[index], result.ExitCode != 0 && index == lines.Length - 1);
        return result;
    }

    public void Open(string what, string path, IReadOnlyList<string>? arguments = null) { }
}

/// <summary>A watched run's host that answers questions as the test says, and remembers what was shown.</summary>
internal sealed class FakeHost(StuckAnswer answer = StuckAnswer.Stop) : IRunHost
{
    public List<string> Shown { get; } = [];
    public List<Doing> Kinds { get; } = [];
    public List<string> Waits { get; } = [];
    public List<CheckResult> Checks { get; } = [];
    public List<CaseResult> Ended { get; } = [];

    /// <summary>Each command: "start line", then "out line" or "err line" for what it printed, then "exit code".</summary>
    public List<string> Commands { get; } = [];

    public int StuckCount { get; private set; }

    public void CaseStarted(int index, int count, Case item) => Shown.Add($"case {item.Name}");
    public void CaseFinished(CaseResult result) => Ended.Add(result);

    public void ShowStep(string text, Doing kind = Doing.Say, string? command = null)
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
