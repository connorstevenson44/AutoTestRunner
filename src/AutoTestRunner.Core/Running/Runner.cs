using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// Runs a suite's cases: each step in turn at a pace a person can follow, holding when the run is paused or step by
/// step, ending each case at its checkpoint. When something can't be found, a watched run asks the person what to do,
/// and an unattended run stops with a screenshot and a UI Automation dump. In a manual run the person does the clicks
/// and keys, and the run waits for each: see Runner.ByHand.cs.
/// </summary>
public sealed partial class Runner
{
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GoneTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

    private readonly Suite _suite;
    private readonly IDriver _driver;
    private readonly IMachine _machine;
    // The same machine without showing: for checks, which can ask many times while they wait.
    private readonly IMachine _quiet;
    private readonly IRunView _view;
    private readonly Results _results;
    private readonly VariableScope _globals;
    private CaseResult? _current;

    // What steps played outside a case keep, for the next ones played.
    private VariableScope? _played;

    // The status text before the last click, so a wait for a new status doesn't take the old one.
    private string? _marked;

    // Macros made while building, which the suite as it was loaded doesn't have.
    private readonly Dictionary<string, Macro> _made = new(StringComparer.Ordinal);

    // The case running, for what the panel says about a step it is stuck on.
    private Case? _case;

    // When the running case started, for checks of logs written since.
    private DateTime _since = DateTime.Now;

    // The case whose reset the app is at now, ready for the cases that start after it; null when it is wherever the
    // last case left it.
    private string? _at;

    // The cases with reset steps that have run, whether they passed, and what they kept for the cases after their reset.
    private readonly Dictionary<string, (bool Passed, VariableScope Scope)> _setups = new(StringComparer.Ordinal);

    // While a reset plays before a case: a step it gets stuck on starts the app again instead of asking.
    private bool _resetting;

    public Runner(Suite suite, IDriver driver, IMachine machine, IRunView view, RunControl control, Results results, IReadOnlyDictionary<string, string>? overrides = null)
    {
        (_suite, _driver, _view, Control, _results) = (suite, driver, view, control, results);
        _quiet = machine;
        _machine = new CommandReportingMachine(machine, view, results);
        _globals = Variables(suite, results.Folder, overrides ?? new Dictionary<string, string>());
    }

    public RunControl Control { get; }

    /// <summary>The suite's databases and queues, through their drivers; without them, a step that uses one is a script error.</summary>
    public IConnections? Connections { get; init; }

    // The same, with every query and message shown as a command is.
    private IConnections Reported => _reported ??= new ReportingConnections(
        Connections ?? throw new ScriptException("autotest can't reach databases or queues here."), _view, _results);

    private IConnections? _reported;

    public VariableScope Globals => _globals;

    /// <summary>The exe to start, with its variables filled in and relative to the suite folder.</summary>
    public string Exe => SuitePath(_globals.Expand(_suite.App.Exe));

    /// <summary>
    /// The suite's variables, with the built-in suiteDir, resultsDir and temp. A variable is a text, or
    /// { "firstExisting": [paths] }: the first that exists, relative to the suite folder, else the last.
    /// </summary>
    public static VariableScope Variables(Suite suite, string resultsFolder, IReadOnlyDictionary<string, string> overrides)
    {
        var scope = new VariableScope(helpers: suite.Helpers);
        scope.Set("suiteDir", suite.Folder);
        scope.Set("resultsDir", resultsFolder);
        scope.Set("temp", Path.Combine(Path.GetTempPath(), "AutoTestRunner"));
        foreach (var (name, node) in suite.Variables)
        {
            if (overrides.TryGetValue(name, out var given))
                scope.Set(name, given);
            else if (node is JsonObject { } choice && choice["firstExisting"] is JsonArray paths)
            {
                var candidates = paths.Select(path => Path.GetFullPath(Path.Combine(suite.Folder, scope.Expand(path!.GetValue<string>())))).ToList();
                scope.Set(name, candidates.FirstOrDefault(path => File.Exists(path) || Directory.Exists(path)) ?? candidates.LastOrDefault() ?? "");
            }
            else
                scope.Set(name, scope.Expand(node?.ToString() ?? ""));
        }
        foreach (var (name, value) in overrides)
            if (!scope.TryGet(name, out _))
                scope.Set(name, value);
        return scope;
    }

    /// <summary>What suite.json's requires lists and isn't there, such as the exe or a package.</summary>
    public IReadOnlyList<string> Missing() => Missing(_suite, _globals);

    /// <summary>What a suite's requires lists and isn't there, with its variables, before there is a run.</summary>
    public static IReadOnlyList<string> Missing(Suite suite, VariableScope globals) =>
        suite.Requires.Select(path => SuitePath(suite, globals.Expand(path))).Where(path => !File.Exists(path) && !Directory.Exists(path)).ToList();

    /// <summary>What earlier runs left on the machine, from the suite's leftovers script: one line for each.</summary>
    public IReadOnlyList<string> Leftovers() => Leftovers(_suite, _machine);

    public static IReadOnlyList<string> Leftovers(Suite suite, IMachine machine)
    {
        if (suite.Reset.Leftovers is not { } script)
            return [];
        var path = SuitePath(suite, script);
        var output = ScriptFile.IsCSharp(path) ? ScriptFile.Run(machine, path, [], folder: suite.Folder) : machine.PowerShell(File.ReadAllText(path), []);
        return output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Closes the app and runs the suite's reset script.</summary>
    public void ResetMachine()
    {
        if (_suite.Reset.Script is not { } script)
            return;
        _driver.CloseAll(Exe);
        if (ScriptFile.IsCSharp(script))
        {
            ScriptFile.Run(_machine, SuitePath(script), [], TimeSpan.FromMinutes(5), folder: _suite.Folder);
            return;
        }
        // What it prints goes in the log, as OUTPUT. -Command rather than -File, so progress bars stay out of it.
        var path = SuitePath(script).Replace("'", "''");
        _machine.Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $"$ProgressPreference = 'SilentlyContinue'; & '{path}'"], timeout: TimeSpan.FromMinutes(5), folder: _suite.Folder);
    }

    /// <summary>
    /// Cleans up what an earlier run left first, when it would change what the run sees: always unattended, and
    /// when a watched run starts from the first case.
    /// </summary>
    public void Prepare(IReadOnlyList<Case> cases)
    {
        if (_suite.Reset.Leftovers is null || cases.Count == 0)
            return;
        if (!Control.Unattended && cases[0] != _suite.Cases[0])
            return;
        if (Leftovers() is [_, ..] leftovers)
        {
            _results.Log("RESET", "Cleaning up first: " + string.Join(", ", leftovers));
            _view.ShowStep(Activity.Of(ActivityKind.Command, [Part.Verb("Clean up"), .. leftovers.Select(Part.Value)]));
            ResetMachine();
        }
    }

    /// <summary>Runs the cases in order and writes the results. Returns how many cases didn't pass.</summary>
    public int Run(IReadOnlyList<Case> cases)
    {
        for (var index = 0; index < cases.Count; index++)
        {
            var item = cases[index];
            _current = new CaseResult(item.Number, item.Name);
            (_case, _since) = (item, DateTime.Now);
            _results.Cases.Add(_current);
            _view.CaseStarted(index + 1, cases.Count, item);
            _results.Log("CASE", $"{item.Number}. {item.Name}");
            var clock = Stopwatch.StartNew();
            var stop = false;
            VariableScope? scope = null;
            try
            {
                scope = Ready(item);
                _at = null;
                Steps(item.Steps, scope, Path.GetFileName(item.File));
                if (item.Reset.Count > 0 && _current.Failures.Count == 0)
                {
                    ShowStep(ResetLine(item, "Reset"));
                    Steps(item.Reset, scope, Path.GetFileName(item.File) + ", reset");
                    _at = _current.Failures.Count == 0 ? item.Id : null;
                }
                if (_current.Failures.Count > 0)
                    _current.Status = CaseStatus.Failed;
            }
            catch (SkipCaseException)
            {
                _current.Status = CaseStatus.Skipped;
            }
            catch (StopRunException ex)
            {
                _current.Status = Control.StopRequested ? CaseStatus.Stopped : CaseStatus.Failed;
                _results.Log("STOP", ex.Message);
                stop = true;
            }
            catch (Exception ex) when (ex is ScriptException or InvalidOperationException or ArgumentException or IOException or FormatException or KeyNotFoundException)
            {
                Capture("error");
                Fail($"Script error: {ex.Message}");
                _current.Status = CaseStatus.Failed;
                stop = Control.Unattended || !_view.AskScriptError($"Something went wrong in the test script itself:\n\n{ex.Message}\n\nIt's saved with the results.");
            }
            if (item.Reset.Count > 0)
                _setups[item.Id] = (_current.Status == CaseStatus.Passed && _at == item.Id, scope ?? _globals.Child());
            _current.Duration = clock.Elapsed;
            _results.Log("RESULT", $"{item.Name}: {_current.Status.ToString().ToLowerInvariant()}");
            _view.CaseFinished(_current);
            if (stop)
                break;
        }
        var summary = _results.Finish(_suite.Name);
        _results.Log("DONE", summary.Replace("\r\n", " | "));
        var failed = _results.Cases.Count(result => result.Status != CaseStatus.Passed);
        _view.Finished(summary, _results.Folder, failed == 0);
        return failed;
    }

    /// <summary>
    /// Plays steps outside a case, as the builder does to get the app ready and to try a step, without their
    /// checkpoints. What they keep stays for the next steps played. A step that gets stuck asks the view, as in a case.
    /// </summary>
    public void Play(JsonArray steps, string where)
    {
        _played ??= _globals.Child();
        Steps(new JsonArray([.. steps.Where(step => step?["checkpoint"] is null).Select(step => step!.DeepClone())]), _played, where);
    }

    /// <summary>Forgets what steps played outside a case kept, as the builder does when it starts the case again.</summary>
    public void Forget() => _played = null;

    /// <summary>The variables the steps played so far can use, with what each holds now: the suite's and those kept.</summary>
    public IReadOnlyList<(string Name, string Text)> Values() => (_played ?? _globals).All();

    /// <summary>
    /// The step the run is stuck on while it asks what to do, or null. The panel can change the step's control, or the
    /// suite's element it uses, before it answers Try again.
    /// </summary>
    public StuckStep? StuckAt { get; private set; }

    /// <summary>A macro made while building, for the steps that call it to play before the suite is loaded again.</summary>
    public void Use(Macro macro) => _made[macro.Name] = macro;

    // ===== Showing the person what is happening =====

    /// <summary>Shows what is about to happen. Step by step, it then waits for Enter; paused, until the run carries on.</summary>
    private void ShowStep(Activity step)
    {
        _results.Log("STEP", step.Text);
        _view.ShowStep(step);
        if (Control.StepByStep && !Control.Unattended)
            Control.Hold(HoldReason.Step);
        Control.Gate();
    }

    /// <summary>Explains what is on screen, and gives time to read it.</summary>
    private void Say(string text)
    {
        ShowStep(Activity.Words(text));
        if (!Control.Unattended && !Control.StepByStep)
            Control.Wait(Math.Max(Control.Pace * 2, text.Length * 40));
    }

    private void Mark()
    {
        if (_suite.App.Status is not { } status)
            return;
        try
        {
            _marked = _driver.TryFind(Targets.Resolve(status, _globals, _suite.Elements)) is { } control ? TextOf(control) : null;
        }
        catch (StuckException)
        {
            _marked = null;
        }
    }

    private static void Keep(JsonObject step, VariableScope scope, IControl control)
    {
        if (step["as"] is not { } name)
            return;
        var parts = new Dictionary<string, string>
        {
            ["name"] = control.Name,
            ["value"] = control.Value ?? "",
            ["helpText"] = control.HelpText ?? "",
            ["id"] = control.AutomationId ?? "",
            ["selection"] = control.Selection ?? "",
        };
        scope.Set(name.GetValue<string>(), Value.Of(control.Name, parts));
    }

    // How long a step took, as a part of what it kept: ${name.seconds}.
    private static void KeepSeconds(JsonObject step, VariableScope scope, TimeSpan took)
    {
        if (step["as"]?.GetValue<string>() is not { } name || !scope.TryGet(name, out var value))
            return;
        var parts = new Dictionary<string, string>(value.Parts) { ["seconds"] = took.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) };
        scope.SetOuter(name, value with { Parts = parts });
    }

    private static void KeepOutput(JsonObject step, VariableScope scope, ProcessOutput output)
    {
        if (step["as"] is not { } name)
            return;
        scope.Set(name.GetValue<string>(), Value.Of(output.Output, new Dictionary<string, string>
        {
            ["output"] = output.Output,
            ["code"] = output.ExitCode.ToString(CultureInfo.InvariantCulture),
            ["first"] = output.FirstLine,
        }));
    }

    // ===== Failures =====

    /// <summary>Records a failure against the running case without stopping it.</summary>
    private void Fail(string text)
    {
        _results.Log("FAIL", text);
        _current?.Failures.Add(text);
    }

    /// <summary>Records a failure of the step just shown, which the panel marks on it. The case carries on.</summary>
    private void FailStep(StepFailure failure)
    {
        Fail(failure.Message);
        _view.StepFailed(failure);
    }

    /// <summary>
    /// Something the run needs isn't there. Watched, the person can put it right and try again, skip the case or stop;
    /// unattended, the run ends.
    /// </summary>
    private void Stuck(StuckException stuck)
    {
        var shot = Capture("stuck");
        var message = stuck.Message;
        _results.Log("STUCK", $"{message} ({shot})");
        if (Control.Unattended)
        {
            Fail(message);
            throw new StopRunException(message);
        }
        Control.Hold(HoldReason.Stuck);
        var answer = _view.AskStuck(message, shot);
        Control.CarryOn();
        if (answer == StuckAnswer.TryAgain)
            return;
        Fail(message + (answer == StuckAnswer.SkipCase ? " (skipped the rest of the case)" : " (stopped the test)"));
        if (answer == StuckAnswer.SkipCase)
            throw new SkipCaseException(stuck.What);
        throw new StopRunException(stuck.What);
    }

    /// <summary>Saves a screenshot and the app's UI Automation tree, for working out what went wrong.</summary>
    public string Capture(string name)
    {
        var path = _results.NextCapture(name);
        try
        {
            _driver.Screenshot(path + ".png");
        }
        catch (Exception ex) when (ex is not StopRunException)
        {
            _results.Log("NOTE", $"The screenshot failed: {ex.Message}");
        }
        try
        {
            File.WriteAllText(path + "-uia.txt", _driver.DumpTree());
        }
        catch (Exception ex) when (ex is not StopRunException)
        {
            _results.Log("NOTE", $"The UI Automation dump failed: {ex.Message}");
        }
        return path + ".png";
    }

    /// <summary>Saves a note against the running case, with a screenshot.</summary>
    public void Note(string text)
    {
        var shot = Capture("note");
        _current?.Notes.Add($"{text} ({shot})");
        _results.Log("NOTE", $"{text} ({shot})");
    }

    private string SuitePath(string path) => SuitePath(_suite, path);

    /// <summary>A path in a script, relative to the suite folder when it is one, as the runner reads it.</summary>
    public static string SuitePath(Suite suite, string path) =>
        Path.IsPathRooted(path) || !(path.Contains('/') || path.Contains('\\')) ? path : Path.GetFullPath(Path.Combine(suite.Folder, path));

    // "Click Add" as "click Add", to go on the end of a sentence.
    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
