using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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
        var output = ScriptFile.IsCSharp(path) ? ScriptFile.Run(machine, path, []) : machine.PowerShell(File.ReadAllText(path), []);
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
            ScriptFile.Run(_machine, SuitePath(script), [], TimeSpan.FromMinutes(5));
            return;
        }
        // What it prints goes in the log, as OUTPUT. -Command rather than -File, so progress bars stay out of it.
        var path = SuitePath(script).Replace("'", "''");
        _machine.Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $"$ProgressPreference = 'SilentlyContinue'; & '{path}'"], timeout: TimeSpan.FromMinutes(5));
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

    // ===== Resets =====

    /// <summary>
    /// Gets the app ready for a case that starts after another's reset, and gives the variables it starts with: none
    /// to do when the app is there already; that case's reset steps when it has run; and when it hasn't run, or its
    /// reset doesn't take the app back, that case and those it carries on from, from the start. When it failed, the
    /// case is skipped, since the app can't be got there.
    /// </summary>
    private VariableScope Ready(Case item)
    {
        if (item.AfterReset is not { } id || _suite.Cases.FirstOrDefault(other => other.Id == id) is not { } setup)
            return _globals.Child();
        var hasRun = _setups.TryGetValue(id, out var ran);
        if (hasRun)
        {
            if (!ran.Passed)
            {
                var reason = $"It starts after {setup.Number}. {setup.Name}, which didn't pass, so the app can't be got there.";
                _results.Log("SKIP", reason);
                _view.ShowStep(Activity.Words(reason));
                throw new SkipCaseException(reason);
            }
            if (_at == id)
                return ran.Scope.Child();
            if (TryReset(setup, ran.Scope))
            {
                _at = id;
                return ran.Scope.Child();
            }
        }
        return StartAgain(setup, !hasRun ? null : "Its reset didn't take the app back, so it starts again.");
    }

    /// <summary>A case's reset steps, played before a case that starts after them. False when they didn't get there.</summary>
    private bool TryReset(Case setup, VariableScope scope)
    {
        ShowStep(ResetLine(setup, "Reset"));
        var failures = _current!.Failures.Count;
        _resetting = true;
        try
        {
            Steps(setup.Reset, scope, Path.GetFileName(setup.File) + ", reset");
        }
        catch (ResetStuckException ex)
        {
            _results.Log("RESET", $"The reset got stuck: {ex.Message} ({Capture("reset")})");
            return false;
        }
        finally
        {
            _resetting = false;
        }
        if (_current.Failures.Count == failures)
            return true;
        _results.Log("RESET", "The reset didn't work: " + string.Join(" ", _current.Failures.Skip(failures)));
        _current.Failures.RemoveRange(failures, _current.Failures.Count - failures);
        return false;
    }

    /// <summary>
    /// Starts the app again and plays a case and those it carries on from, without their checkpoints, then its reset:
    /// the reset of each case one of them starts after, too. A failure here is the case's that is waiting.
    /// </summary>
    private VariableScope StartAgain(Case setup, string? why)
    {
        ShowStep(ResetLine(setup, why is null ? "First" : "Start again from") with { Detail = why });
        _driver.CloseAll(Exe);
        var leading = _suite.Leading(setup);
        var scope = _globals.Child();
        var failures = _current!.Failures.Count;
        foreach (var other in leading)
        {
            Steps(new JsonArray([.. other.Steps.Where(step => step?["checkpoint"] is null).Select(step => step!.DeepClone())]), scope, Path.GetFileName(other.File));
            if (other == setup || leading.Any(later => later.AfterReset == other.Id))
                Steps(other.Reset, scope, Path.GetFileName(other.File) + ", reset");
        }
        var passed = _current.Failures.Count == failures;
        _setups[setup.Id] = (passed, scope);
        _at = passed ? setup.Id : null;
        return scope.Child();
    }

    // Reset to after 1. Log in; First 1. Log in; Start again from 1. Log in.
    private static Activity ResetLine(Case setup, string verb) => verb == "Reset"
        ? Activity.Of(ActivityKind.Reset, Part.Verb(verb), Part.Word("to after"), Part.Value($"{setup.Number}. {setup.Name}"))
        : Activity.Of(ActivityKind.Reset, Part.Verb(verb), Part.Value($"{setup.Number}. {setup.Name}"));

    private sealed class ResetStuckException(string message) : Exception(message);

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

    // ===== Steps =====

    private void Steps(JsonArray steps, VariableScope scope, string where)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var step = (JsonObject)steps[index]!;
            if (step["disabled"]?.GetValue<bool>() == true)
                continue;
            while (true)
            {
                try
                {
                    var clock = Stopwatch.StartNew();
                    Step(step, scope);
                    KeepSeconds(step, scope, clock.Elapsed);
                    break;
                }
                catch (StuckException ex) when (_resetting)
                {
                    throw new ResetStuckException(ex.Message);
                }
                catch (StuckException ex)
                {
                    StuckAt = new StuckStep(step, ScriptVocabulary.KindOf(step), _case);
                    try
                    {
                        Stuck(ex);
                    }
                    finally
                    {
                        StuckAt = null;
                    }
                }
                catch (ScriptException ex)
                {
                    throw new ScriptException($"{where}, step {index + 1}: {ex.Message}");
                }
            }
        }
    }

    private void Step(JsonObject step, VariableScope scope)
    {
        var kind = ScriptVocabulary.KindOf(step);
        string? Text(string key) => step[key] is { } node ? scope.Expand(node.ToString()) : null;
        // The panel shows what a step does, with what it acts on; the script's own words for it show when pointing at it.
        // A wait's are read once it is over, so they can use what it found.
        var waits = kind is "find" or "waitGone" or "waitText" or "whileShows" or "waitEnabled" or "useWindow";
        var say = kind == "say" || waits ? null : Text("say");
        Activity Line(ActivityKind activity, params Part[] parts) => new(activity, parts) { Say = say };
        var clock = Stopwatch.StartNew();
        // A wait step, once it is over: what it waited for, what it saw and how long it took.
        void Waited(Activity line, string? saw = null)
        {
            var done = line with { Say = Text("say"), Detail = string.IsNullOrWhiteSpace(saw) ? null : saw };
            _results.Log("WAITED", $"{done.Text}{(done.Detail is null ? "" : $": {done.Detail}")} ({clock.Elapsed.TotalSeconds:0.0} s)");
            _view.Waited(done, clock.Elapsed);
        }
        var what = Text("what");
        TimeSpan Within(TimeSpan normal) => step["within"] is { } seconds ? TimeSpan.FromSeconds(double.Parse(scope.Expand(seconds.ToString()), CultureInfo.InvariantCulture)) : normal;
        Target TargetOf(string key) => Targets.Resolve(step[key], scope, _suite.Elements, key == kind ? what : null);
        List<string> ListOf(string key) => step[key] switch
        {
            JsonArray list => [.. list.Select(item => scope.Expand(item!.ToString()))],
            { } one => [.. scope.Expand(one.ToString()).Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            null => throw new ScriptException($"{kind} needs {key}."),
        };
        string Required(string key) => Text(key) ?? throw new ScriptException($"{kind} needs {key}.");
        IControl Find(Target target)
        {
            _view.ShowWait(Activity.Of(ActivityKind.Wait, Part.Verb("Look for"), Part.Control(target)));
            return _driver.Find(target, Control.Scale(Within(FindTimeout)));
        }

        switch (kind)
        {
            case "say":
                Say(Text("say") ?? "");
                break;

            case "startApp":
                ShowStep(Line(ActivityKind.App, Part.Verb("Start"), new Part(PartKind.Window, _suite.App.Window, Exe)));
                _driver.Start(_suite.App, Exe);
                break;

            case "closeApp":
                ShowStep(Line(ActivityKind.App, Part.Verb("Close"), Part.Window(_suite.App.Window)));
                _driver.Close();
                break;

            case "click" when step["whileEnabled"]?.GetValue<bool>() == true:
                ClickWhileEnabled(TargetOf("click"), say, How(Text("how")));
                break;

            case "click":
            case "tab":
            {
                var target = kind == "tab" ? Target.Of(new Selector(Type: "TabItem", Name: Text("tab"))) : TargetOf("click");
                var control = Find(target);
                var how = How(Text("how"));
                var verb = kind == "tab" ? "Open" : how switch { ClickHow.Right => "Right-click", ClickHow.Double => "Double-click", _ => "Click" };
                var line = Line(ActivityKind.Click, Part.Verb(verb), Part.Control(target, control));
                ShowStep(line);
                Mark();
                if (!ClickByHand(line, target, how, tab: kind == "tab"))
                {
                    _driver.Point(control, line.Text);
                    if (!control.IsEnabled)
                        throw new StuckException(Lower(line.Text), "It's greyed out, so it can't be used yet.");
                    _driver.Click(control, how);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "drag":
            {
                var (from, to) = (TargetOf("drag"), TargetOf("to"));
                var control = Find(from);
                var onto = Find(to);
                var line = Line(ActivityKind.Point, Part.Verb("Drag"), Part.Control(from, control), Part.Word("onto"), Part.Control(to, onto));
                ShowStep(line);
                Mark();
                if (!DragByHand(line, from, to))
                {
                    _driver.Point(control, line.Text);
                    _driver.Drag(control, onto);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "menu":
            {
                var path = ListOf("menu");
                var items = path.SelectMany((item, index) => index == 0 ? [Part.Value(item)] : new[] { Part.Word("›"), Part.Value(item) });
                var on = step["on"] is null ? null : TargetOf("on");
                var opener = on is null ? null : Find(on);
                var line = on is null
                    ? Line(ActivityKind.Click, [Part.Verb("Menu"), .. items])
                    : Line(ActivityKind.Click, [Part.Verb("Right-click"), Part.Control(on, opener), Part.Word("›"), .. items]);
                ShowStep(line);
                Mark();
                if (!MenuByHand(line, on, path))
                {
                    if (opener is not null)
                    {
                        _driver.Point(opener, line.Text);
                        _driver.Click(opener, ClickHow.Right);
                        Control.Wait(300);
                    }
                    _driver.Menu(path);
                }
                Control.Wait(Control.Pace / 2);
                break;
            }

            case "expand":
            {
                var path = ListOf("expand");
                var line = Line(ActivityKind.Choose, Part.Verb("Expand"), Part.Value(string.Join(" › ", path)));
                ShowStep(line);
                var parent = step["in"] is null ? null : TargetOf("in");
                if (ExpandByHand(line, parent, path) is { } expanded)
                {
                    Control.Wait(Control.Pace / 2);
                    Keep(step, scope, expanded);
                    break;
                }
                IControl? item = null;
                for (var index = 0; index < path.Count; index++)
                {
                    var target = Target.Of(new Selector(Type: "TreeItem", Name: path[index], Within: parent));
                    item = Find(target);
                    _driver.Point(item, path[index]);
                    if (index < path.Count - 1)
                        _driver.Expand(item);
                    else
                        _driver.Click(item, ClickHow.Invoke);
                    Control.Wait(Control.Pace / 2);
                    parent = target;
                }
                if (item is not null)
                    Keep(step, scope, item);
                break;
            }

            case "setValue":
            {
                var target = TargetOf("setValue");
                var control = Find(target);
                var value = Required("to");
                var line = Line(ActivityKind.Type, Part.Verb("Set"), Part.Control(target, control), Part.Word("to"), Part.Value(value));
                ShowStep(line);
                if (!SetValueByHand(line, target, value))
                {
                    _driver.Point(control, line.Text);
                    _driver.SetValue(control, value);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "type":
            {
                var target = TargetOf("type");
                var control = Find(target);
                var value = Text("text") ?? "";
                var secret = step["secret"]?.GetValue<bool>() ?? false;
                var line = Line(ActivityKind.Type, Part.Verb("Type"), Part.Value(secret ? "••••••" : value), Part.Word("in"), Part.Control(target, control));
                ShowStep(line);
                if (!TypeByHand(line, target, value, secret))
                {
                    _driver.Point(control, line.Text);
                    _driver.Type(control, value, secret);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "choose":
            {
                var target = TargetOf("choose");
                var control = Find(target);
                var option = Text("option") ?? throw new ScriptException("choose needs the option to pick.");
                var line = Line(ActivityKind.Choose, Part.Verb("Pick"), Part.Value(option), Part.Word("in"), Part.Control(target, control));
                ShowStep(line);
                if (!ChooseByHand(line, target, option))
                {
                    _driver.Point(control, line.Text);
                    _driver.Choose(control, option);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "tick":
            {
                var target = TargetOf("tick");
                var control = Find(target);
                var isChecked = step["checked"]?.GetValue<bool>() ?? true;
                var line = Line(ActivityKind.Choose, Part.Verb(isChecked ? "Tick" : "Clear"), Part.Control(target, control));
                ShowStep(line);
                if (!TickByHand(line, target, isChecked))
                {
                    _driver.Point(control, line.Text);
                    _driver.SetChecked(control, isChecked);
                }
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "hover":
            {
                var target = TargetOf("hover");
                var control = Find(target);
                var line = Line(ActivityKind.Point, Part.Verb("Point at"), Part.Control(target, control));
                ShowStep(line);
                if (!HoverByHand(line, target))
                    _driver.Point(control, line.Text);
                Control.Wait(300);
                Keep(step, scope, control);
                break;
            }

            case "find":
            {
                var target = TargetOf("find");
                var control = Find(target);
                Keep(step, scope, control);
                // What it found is shown when the case keeps it.
                Waited(Line(ActivityKind.Wait, Part.Verb("Find"), Part.Control(target, control)), step["as"] is null ? null : TextOf(control));
                break;
            }

            case "waitGone":
            {
                var target = TargetOf("waitGone");
                var line = Line(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target), Part.Word("gone"));
                _view.ShowWait(line);
                _driver.WaitGone(target, Control.Scale(Within(GoneTimeout)));
                Waited(line);
                break;
            }

            case "waitText":
            {
                var target = TargetOf("waitText");
                var (line, found) = WaitText(step, scope, target, Within(FindTimeout));
                Waited(line, found);
                break;
            }

            case "waitEnabled":
            {
                var target = TargetOf("waitEnabled");
                var control = WaitEnabled(target, Within(FindTimeout));
                Keep(step, scope, control);
                Waited(Line(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target, control), Part.Word("usable")));
                break;
            }

            case "useWindow":
            {
                var title = Required("useWindow");
                var line = Line(ActivityKind.Wait, Part.Verb("Switch to"), Part.Window(title));
                _view.ShowWait(line);
                _driver.UseWindow(title, Control.Scale(Within(TimeSpan.FromSeconds(60))));
                Waited(line);
                break;
            }

            case "replaceText":
            {
                var target = TargetOf("replaceText");
                var control = Find(target);
                var pattern = Text("regex") ?? throw new ScriptException("replaceText needs a regex.");
                var replacement = Text("replacement") ?? "";
                var line = Line(ActivityKind.Type, Part.Verb("Replace"), Part.Value(pattern), Part.Word("with"), Part.Value(replacement), Part.Word("in"), Part.Control(target, control));
                ShowStep(line);
                var replaced = ReplaceByHand(line, target, control, pattern, replacement);
                if (replaced is null)
                    _driver.Point(control, line.Text);
                if (!(replaced ?? _driver.ReplaceText(control, pattern, replacement)))
                    FailStep(new StepFailure($"The text has nothing matching {pattern}, so I couldn't {Lower(line.Text)}", $"matches {pattern}", "no match"));
                Control.Wait(Control.Pace);
                break;
            }

            case "key":
            {
                var line = Line(ActivityKind.Key, Part.Verb("Press"), Part.Key(Text("key")!));
                ShowStep(line);
                if (!KeyByHand(line, Text("key")!))
                    _driver.Key(Text("key")!);
                Control.Wait(Control.Pace / 2);
                break;
            }

            case "answerDialog":
            {
                var title = Text("answerDialog")!;
                var button = Enum.Parse<DialogButton>(Text("button") ?? "OK", ignoreCase: true);
                _view.ShowWait(Activity.Of(ActivityKind.Wait, Part.Verb("Wait for"), Part.Window(title)));
                var message = _driver.WaitDialog(title, Control.Scale(Within(DialogTimeout)));
                ShowStep(Line(ActivityKind.Dialog, Part.Verb("Answer"), Part.Window(title), Part.Word("with"), Part.Key(button.ToString())) with { Detail = message });
                if (Text("expect") is { } expect && !message.Contains(expect, StringComparison.Ordinal))
                    FailStep(new StepFailure($"The \"{title}\" message was \"{message}\", expected \"{expect}\"", $"contains \"{expect}\"", message));
                Mark();
                if (!AnswerByHand(title, button))
                    _driver.PressDialogButton(title, button);
                Control.Wait(Control.Pace / 2);
                if (step["as"] is { } name)
                    scope.Set(name.GetValue<string>(), message);
                break;
            }

            case "pickFile":
            case "pickFolder":
            {
                var path = Text("path") ?? throw new ScriptException($"{kind} needs the path to pick.");
                var line = Line(ActivityKind.File, Part.Verb("Pick"), Part.File(path), Part.Word("in"), Part.Window(Text(kind)!));
                ShowStep(line);
                Mark();
                if (!PickByHand(line, Text(kind)!, path, kind == "pickFolder"))
                    _driver.PickFile(Text(kind)!, path, kind == "pickFolder");
                Control.Wait(Control.Pace / 2);
                break;
            }

            case "call":
                Call(step, scope);
                break;

            case "repeat":
            {
                var items = step["repeat"] switch
                {
                    JsonArray list => list.Select(item => Value.Of(scope.Expand(item!.ToString()))).ToList(),
                    JsonObject { } source when source["rows"] is { } rows => DataRows.Read(Path.GetFullPath(Path.Combine(_suite.Folder, scope.Expand(rows.ToString())))),
                    var text => scope.Expand(text!.ToString()).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Value.Of).ToList(),
                };
                var name = Text("item") ?? "item";
                // The steps share the case's variables, so what they set is still there after the repeat.
                foreach (var item in items)
                {
                    scope.Set(name, item);
                    Steps(step["steps"] as JsonArray ?? [], scope, "repeat");
                }
                break;
            }

            case "whileShows":
            {
                var (line, found) = WhileShows(step, scope, TargetOf("whileShows"), Within(TimeSpan.FromMinutes(5)));
                Waited(line, found);
                break;
            }

            case "if":
            {
                var check = Checks.Build(step["if"]!.AsObject(), scope, Context(scope));
                var seen = check.Try();
                _results.Log("IF", $"{check.Label}: {(seen.Passed ? "yes" : "no")}, found {seen.Actual}");
                Steps((seen.Passed ? step["steps"] : step["else"]) as JsonArray ?? [], scope, seen.Passed ? "if" : "else");
                break;
            }

            case "set":
                foreach (var (name, value) in step["set"]!.AsObject())
                    scope.SetOuter(name, Value.Of(scope.Expand(value?.ToString() ?? "")));
                break;

            case "run":
            {
                var program = SuitePath(Text("run")!);
                var arguments = (step["args"] as JsonArray ?? []).Select(arg => scope.Expand(arg!.ToString())).ToList();
                var environment = (step["env"] as JsonObject ?? []).ToDictionary(pair => pair.Key, pair => scope.Expand(pair.Value?.ToString() ?? ""));
                var line = CommandReportingMachine.CommandLine(program, arguments, environment);
                ShowStep(Line(ActivityKind.Command, Part.Verb("Run"), Part.File(program)) with { Command = line });
                // What it prints goes in the log as it prints it, as the case may need to show it.
                var output = _machine.Run(program, arguments, environment);
                KeepOutput(step, scope, output);
                break;
            }

            case "powershell":
            {
                // The script isn't expanded: PowerShell has ${...} of its own. It gets the step's args as $args.
                var script = step["powershell"] is JsonArray lines ? string.Join("\r\n", lines.Select(line => line!.ToString())) : step["powershell"]!.ToString();
                var arguments = (step["args"] as JsonArray ?? []).Select(arg => scope.Expand(arg!.ToString())).ToList();
                ShowStep(Line(ActivityKind.Command, Part.Verb("Run"), Part.Word("PowerShell")) with { Command = CommandReportingMachine.CommandLine("powershell", arguments, null) + "\n" + script });
                var output = _machine.PowerShell(script, arguments);
                if (output.ExitCode != 0)
                    FailStep(new StepFailure($"A PowerShell step failed with exit code {output.ExitCode}: {output.Output}", "exit 0", $"exit {output.ExitCode}"));
                KeepOutput(step, scope, output);
                break;
            }

            case "script":
            {
                // A .ps1 or .cs file in the suite, run as the reset script is, so it can be opened and changed on its own.
                var file = Path.GetFullPath(Path.Combine(_suite.Folder, Text("script")!));
                if (!File.Exists(file))
                    throw new ScriptException($"There is no script {file}.");
                var arguments = (step["args"] as JsonArray ?? []).Select(arg => scope.Expand(arg!.ToString())).ToList();
                var (program, line) = ScriptFile.Command(file, arguments);
                ShowStep(Line(ActivityKind.Command, Part.Verb("Run"), Part.File(file)) with { Command = CommandReportingMachine.CommandLine(program, line, null) });
                var output = ScriptFile.Run(_machine, file, arguments);
                if (output.ExitCode != 0)
                    FailStep(new StepFailure(ScriptFile.Failed(file, output), "exit 0", $"exit {output.ExitCode}"));
                KeepOutput(step, scope, output);
                break;
            }

            case "wait":
            {
                var seconds = double.Parse(Text("wait")!, CultureInfo.InvariantCulture);
                var line = Line(ActivityKind.Wait, Part.Verb("Wait"), Part.Value($"{seconds.ToString(CultureInfo.InvariantCulture)} s"));
                ShowStep(line);
                _view.ShowWait(line);
                Control.Wait((int)(seconds * 1000));
                break;
            }

            case "copy":
            {
                var (from, to) = (SuitePath(Required("copy")), SuitePath(Required("to")));
                ShowStep(Line(ActivityKind.File, Part.Verb("Copy"), Part.File(from), Part.Word("to"), Part.File(to)));
                Files(() => Copy(from, to));
                break;
            }

            case "delete":
            {
                var path = SuitePath(Required("delete"));
                ShowStep(Line(ActivityKind.File, Part.Verb("Delete"), Part.File(path)));
                Files(() =>
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, recursive: true);
                    else if (File.Exists(path))
                        File.Delete(path);
                });
                break;
            }

            case "writeFile":
            {
                var path = SuitePath(Required("writeFile"));
                var text = step["text"] is JsonArray lines ? string.Join("\r\n", lines.Select(line => scope.Expand(line!.ToString()))) + "\r\n" : Text("text") ?? "";
                var append = step["append"]?.GetValue<bool>() ?? false;
                ShowStep(Line(ActivityKind.File, Part.Verb(append ? "Add to" : "Write"), Part.File(path)));
                Files(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (append)
                        File.AppendAllText(path, text);
                    else
                        File.WriteAllText(path, text);
                });
                break;
            }

            case "unzip":
            {
                var (zip, to) = (SuitePath(Required("unzip")), SuitePath(Required("to")));
                ShowStep(Line(ActivityKind.File, Part.Verb("Unzip"), Part.File(zip), Part.Word("to"), Part.File(to)));
                Files(() =>
                {
                    if (Directory.Exists(to))
                        Directory.Delete(to, recursive: true);
                    ZipFile.ExtractToDirectory(zip, to);
                });
                break;
            }

            case "startService":
            case "stopService":
            {
                var name = Required(kind);
                var start = kind == "startService";
                ShowStep(Line(ActivityKind.Command, Part.Verb(start ? "Start" : "Stop"), Part.Service(name)));
                try
                {
                    _machine.SetServiceRunning(name, start);
                }
                catch (Exception ex) when (ex is not StopRunException)
                {
                    FailStep(new StepFailure($"The service {name} didn't {(start ? "start" : "stop")}: {ex.Message}", start ? "Running" : "Stopped", ex.Message));
                }
                break;
            }

            case "setJsonValue":
            {
                var (file, path, value) = (SuitePath(Required("setJsonValue")), Required("path"), Required("to"));
                ShowStep(Line(ActivityKind.File, Part.Verb("Set"), Part.File(file), Part.Word(path), Part.Word("="), Part.Value(value)));
                Files(() => SetJsonValue(file, path, value));
                break;
            }

            case "setAppSetting":
            {
                var (file, key, value) = (SuitePath(Required("setAppSetting")), Required("key"), Required("to"));
                ShowStep(Line(ActivityKind.File, Part.Verb("Set"), Part.File(file), Part.Word(key), Part.Word("="), Part.Value(value)));
                Files(() => SetAppSetting(file, key, value));
                break;
            }

            case "query":
            {
                var database = Database(Required("query"), scope);
                var sql = Required("sql");
                ShowStep(Line(ActivityKind.Database, Part.Verb("Query"), Sources.Part(database), Part.Value(OneLine(sql))) with { Command = sql });
                QueryResult result;
                try
                {
                    result = Reported.Query(database, sql, Checks.Parameters(step["params"], scope), Within(TimeSpan.FromSeconds(30)));
                }
                catch (Exception ex) when (ex is not (StopRunException or ScriptException))
                {
                    FailStep(new StepFailure($"The query on {database.Name} failed: {ex.Message}", "rows", ex.Message));
                    break;
                }
                if (step["as"]?.GetValue<string>() is { } keep)
                {
                    scope.Set(keep, Value.Of(result.First, new Dictionary<string, string>
                    {
                        ["rows"] = result.Count.ToString(CultureInfo.InvariantCulture),
                        ["output"] = result.Table(int.MaxValue),
                    }));
                }
                break;
            }

            case "send":
            {
                var queue = Queue(Required("send"), scope);
                var body = step["file"] is { } file ? File.ReadAllText(SuitePath(scope.Expand(file.ToString()))) : Text("message")!;
                ShowStep(Line(ActivityKind.Queue, Part.Verb("Send"), Part.Value(OneLine(body)), Part.Word("to"), Sources.Part(queue)) with { Command = body });
                Queued(queue, "send", () => Reported.Send(queue, body));
                break;
            }

            case "receive":
            {
                var queue = Queue(Required("receive"), scope);
                var wanted = WantedText(step, scope, first: null);
                var line = Line(ActivityKind.Queue, Part.Verb("Receive"), Part.Word("from"), Sources.Part(queue));
                ShowStep(line with { Command = Sources.Line(queue) });
                _view.ShowWait(line);
                var within = Control.Scale(Within(TimeSpan.FromSeconds(10)));
                string? body = null;
                Queued(queue, "receive from", () => body = Reported.Receive(queue, within));
                if (body is null)
                    throw new StuckException($"receive a message from {queue.Name}", $"Nothing came in {within.TotalSeconds:0} s.");
                if (wanted.Expected.Length > 0 && !wanted.Test(body))
                    FailStep(new StepFailure($"The message from {queue.Name} isn't the one expected: wanted {wanted.Expected}, got {OneLine(body)}", wanted.Expected, OneLine(body)));
                if (step["as"]?.GetValue<string>() is { } keep)
                    scope.Set(keep, Value.Of(body));
                break;
            }

            case "purge":
            {
                var queue = Queue(Required("purge"), scope);
                ShowStep(Line(ActivityKind.Queue, Part.Verb("Empty"), Sources.Part(queue)));
                Queued(queue, "empty", () => Reported.Purge(queue));
                break;
            }

            case "checkpoint":
                Checkpoint(step, scope);
                break;
        }
    }

    // A message step: when the broker can't be reached or refuses, the step fails and the case carries on.
    private void Queued(QueueSettings queue, string what, Action work)
    {
        try
        {
            work();
        }
        catch (Exception ex) when (ex is not (StopRunException or ScriptException))
        {
            FailStep(new StepFailure($"Couldn't {what} {queue.Name}: {ex.Message}", "done", ex.Message));
        }
    }

    // SQL or a message on one line, short enough for a step's row: the whole of it shows with the command.
    private static string OneLine(string text)
    {
        var line = Regex.Replace(text.Trim(), @"\s+", " ");
        return line.Length <= 60 ? line : line[..59] + "…";
    }

    private void Call(JsonObject step, VariableScope scope)
    {
        var name = scope.Expand(step["call"]!.GetValue<string>());
        if (!_suite.Macros.TryGetValue(name, out var macro) && !_made.TryGetValue(name, out macro))
            throw new ScriptException($"There is no macro called {name}.");
        var inner = scope.Child();
        var given = step["with"] as JsonObject ?? [];
        foreach (var (parameter, fallback) in macro.Parameters)
        {
            if (given[parameter] is { } value)
                inner.Set(parameter, scope.Expand(value.ToString()));
            else if (fallback is not null)
                inner.Set(parameter, inner.Expand(fallback));
            else
                throw new ScriptException($"{name} needs {parameter}.");
        }
        Steps(macro.Steps, inner, Path.GetFileName(macro.File));
        if (step["as"] is { } keep && macro.Returns is { } returns)
            scope.Set(keep.GetValue<string>(), inner.Resolve(returns));
    }

    // Waits for a control's text, and gives the wait's line and the text it saw.
    private (Activity Line, string Found) WaitText(JsonObject step, VariableScope scope, Target target, TimeSpan within)
    {
        var wanted = WantedText(step, scope, _driver.TryFind(target) is { } before ? TextOf(before) : null);
        var line = WaitingFor(target, wanted);
        _view.ShowWait(line);
        var end = Environment.TickCount64 + (long)Control.Scale(within).TotalMilliseconds;
        while (true)
        {
            Control.Gate();
            var control = _driver.TryFind(target);
            var found = control is null ? null : TextOf(control);
            if (found is not null && wanted.Test(found))
            {
                Keep(step, scope, control!);
                return (line with { Parts = [.. line.Parts.Select(part => part.Kind == PartKind.Control ? Part.Control(target, control) : part)] }, found);
            }
            if (_driver.OtherWindow() is { } other)
                throw new StuckException($"see \"{wanted.Expected}\" in {target.What}", $"The app is showing another window instead: {other}");
            if (Environment.TickCount64 > end)
                throw new StuckException($"see \"{wanted.Expected}\" in {target.What}", found is null ? null : $"It says: {found}");
            Control.Wait(300);
        }
    }

    /// <summary>
    /// Runs steps each time a control shows, such as a window that asks about each step of a removal, until another
    /// control's text says it is done. Another window showing up instead, or nothing happening for within, is stuck.
    /// </summary>
    // Goes round, and gives the wait's line and the text that said it was done.
    private (Activity Line, string Found) WhileShows(JsonObject step, VariableScope scope, Target each, TimeSpan within)
    {
        var until = Targets.Resolve(step["untilText"] ?? throw new ScriptException("whileShows needs untilText: the control that says when it is done."),
            scope, _suite.Elements);
        var wanted = WantedText(step, scope, first: null);
        var line = WaitingFor(until, wanted);
        _view.ShowWait(line);
        var end = Environment.TickCount64 + (long)Control.Scale(within).TotalMilliseconds;
        for (var rounds = 0; ;)
        {
            Control.Gate();
            var found = _driver.TryFind(until) is { } control ? TextOf(control) : null;
            if (found is not null && wanted.Test(found))
            {
                if (step["as"] is { } name)
                    scope.Set(name.GetValue<string>(), found);
                return (line with { Parts = [Part.Verb("Repeat while"), Part.Control(each), Part.Word("shows, until"), .. line.Parts.Skip(1)] }, rounds == 0 ? found : $"{found}  ·  {rounds} ×");
            }
            if (_driver.TryFind(each) is not null)
            {
                // Steps that never make it go away would go round for ever.
                if (++rounds > MaxRounds)
                    throw new StuckException($"see \"{wanted.Expected}\" in {until.What}", $"{each.What} was still showing after {MaxRounds} rounds.");
                Steps(step["steps"] as JsonArray ?? [], scope, "whileShows");
                _view.ShowWait(line);
                end = Environment.TickCount64 + (long)Control.Scale(within).TotalMilliseconds;
                continue;
            }
            if (_driver.OtherWindow() is { } other)
                throw new StuckException($"see \"{wanted.Expected}\" in {until.What}", $"The app is showing another window instead: {other}");
            if (Environment.TickCount64 > end)
                throw new StuckException($"see \"{wanted.Expected}\" in {until.What}", found is null ? null : $"It says: {found}");
            Control.Wait(300);
        }
    }

    private const int MaxRounds = 100;

    // What waitText and whileShows wait for a control's text to be. first is its text when the wait started.
    private Wanted WantedText(JsonObject step, VariableScope scope, string? first)
    {
        string? Text(string key) => step[key] is { } node ? scope.Expand(node.ToString()) : null;
        var startsWith = Text("startsWith");
        var contains = Text("contains");
        var equals = Text("equals");
        var pattern = Text("matches") is { } given ? new Regex(given) : null;
        var changes = step["changes"]?.GetValue<bool>() ?? false;
        var isNew = step["new"]?.GetValue<bool>() ?? false;
        var marked = _marked;
        var how = startsWith is not null ? "starts with" : contains is not null ? "contains" : equals is not null ? "=" : pattern is not null ? "matches" : "changes";
        return new Wanted(how, startsWith ?? contains ?? equals ?? pattern?.ToString() ?? "", found =>
            (startsWith is null || found.StartsWith(startsWith, StringComparison.Ordinal))
            && (contains is null || found.Contains(contains, StringComparison.Ordinal))
            && (equals is null || found == equals)
            && (pattern is null || pattern.IsMatch(found))
            && (!changes || found != first)
            && (!isNew || found != marked));
    }

    // How a wait compares a control's text, such as starts with, and what with; changes has nothing to compare with.
    private sealed record Wanted(string How, string Expected, Func<string, bool> Test);

    private static Activity WaitingFor(Target target, Wanted wanted) => wanted.Expected.Length == 0
        ? Activity.Of(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target), Part.Word(wanted.How))
        : Activity.Of(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target), Part.Word(wanted.How), Part.Value(wanted.Expected));

    // Waits until a control is there and can be used, such as a button greyed out while the app works.
    private IControl WaitEnabled(Target target, TimeSpan within)
    {
        _view.ShowWait(Activity.Of(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target), Part.Word("usable")));
        var end = Environment.TickCount64 + (long)Control.Scale(within).TotalMilliseconds;
        while (true)
        {
            Control.Gate();
            var control = _driver.TryFind(target);
            if (control is { IsEnabled: true })
                return control;
            if (_driver.OtherWindow() is { } other)
                throw new StuckException($"see {target.What} become usable", $"The app is showing another window instead: {other}");
            if (Environment.TickCount64 > end)
                throw new StuckException($"see {target.What} become usable", control is null ? null : "It's still greyed out.");
            Control.Wait(300);
        }
    }

    private static ClickHow How(string? how) => how switch
    {
        null => ClickHow.Invoke,
        "mouse" => ClickHow.Mouse,
        "right" => ClickHow.Right,
        "double" => ClickHow.Double,
        _ => throw new ScriptException($"how is mouse, right or double, not {how}."),
    };

    // A step that changes files: one that can't, such as a file in use, fails the step and the case carries on.
    private void Files(Action change)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or System.Xml.XmlException)
        {
            FailStep(new StepFailure(ex.Message));
        }
    }

    // A file into a folder or onto a file, or a folder in place of another.
    private static void Copy(string from, string to)
    {
        if (Directory.Exists(from))
        {
            if (Directory.Exists(to))
                Directory.Delete(to, recursive: true);
            foreach (var folder in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
            return;
        }
        if (!File.Exists(from))
            throw new FileNotFoundException($"There is no {from} to copy.");
        var target = Directory.Exists(to) ? Path.Combine(to, Path.GetFileName(from)) : to;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(from, target, overwrite: true);
    }

    /// <summary>
    /// Sets a JSON setting by its path, such as Orders:Region, making the objects on the way when they aren't there. A
    /// number or true and false stay a number or a true and false when the old value was one.
    /// </summary>
    public static void SetJsonValue(string file, string path, string value)
    {
        var root = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
            ?? throw new JsonException($"{Path.GetFileName(file)} isn't a JSON object.");
        var keys = path.Split(':');
        var node = root;
        foreach (var key in keys[..^1])
        {
            var name = node.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Key ?? key;
            if (node[name] is not JsonObject inner)
                node[name] = inner = new JsonObject();
            node = inner;
        }
        var last = node.FirstOrDefault(pair => string.Equals(pair.Key, keys[^1], StringComparison.OrdinalIgnoreCase)).Key ?? keys[^1];
        node[last] = node[last] switch
        {
            JsonValue old when old.GetValueKind() == JsonValueKind.Number && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            JsonValue old when old.GetValueKind() is JsonValueKind.True or JsonValueKind.False && bool.TryParse(value, out var flag) => flag,
            _ => value,
        };
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Sets an appSettings value in a .config file, adding it when it isn't there, and keeping the file's layout.</summary>
    public static void SetAppSetting(string file, string key, string value)
    {
        var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new System.Xml.XmlException($"{Path.GetFileName(file)} is empty.");
        var settings = root.Element("appSettings");
        if (settings is null)
            root.Add(settings = new XElement("appSettings"));
        var add = settings.Elements("add").FirstOrDefault(element => (string?)element.Attribute("key") == key);
        if (add is null)
            settings.Add(add = new XElement("add", new XAttribute("key", key)));
        add.SetAttributeValue("value", value);
        document.Save(file, SaveOptions.DisableFormatting);
    }

    // Clicks a control again and again while it is there and can be used, such as Up until the top folder. Not being
    // there at all is fine: there is nothing to do.
    private void ClickWhileEnabled(Target target, string? say, ClickHow how)
    {
        for (var clicks = 0; _driver.TryFind(target) is { IsEnabled: true } control; clicks++)
        {
            if (clicks == 50)
                throw new StuckException($"get past {target.What}", "It was still there after 50 clicks.");
            var line = new Activity(ActivityKind.Click, [Part.Verb("Click"), Part.Control(target, control)]) { Say = say };
            ShowStep(line);
            Mark();
            if (!ClickByHand(line, target, how, tab: false))
            {
                _driver.Point(control, line.Text);
                _driver.Click(control, how);
            }
            Control.Wait(Math.Max(Control.Pace / 2, 300));
        }
    }

    // A control's text: what is typed in a text box or editor, or the name of anything else, such as a label.
    private static string TextOf(IControl control) => control.Value ?? control.Name;

    // What checks look at: the machine without showing, the suite's leftovers, the app's controls, and since when.
    private CheckContext Context(VariableScope scope) => new(_quiet, () => Leftovers(_suite, _quiet), node =>
    {
        var target = Targets.Resolve(node, scope, _suite.Elements);
        return new ControlProbe(target, () => _driver.TryFind(target), _driver.Rows);
    }, _since)
    {
        Connections = Connections,
        Database = name => Database(name, scope),
        Queue = name => Queue(name, scope),
    };

    /// <summary>A database by its name in suite.json, with its variables filled in.</summary>
    private DatabaseSettings Database(string name, VariableScope scope) =>
        _suite.Databases.TryGetValue(name, out var database)
            ? database with { Connection = scope.Expand(database.Connection) }
            : throw new ScriptException($"There is no database called {name} in suite.json.");

    /// <summary>A queue by its name in suite.json, with its variables filled in.</summary>
    private QueueSettings Queue(string name, VariableScope scope)
    {
        if (!_suite.Queues.TryGetValue(name, out var queue))
            throw new ScriptException($"There is no queue called {name} in suite.json.");
        string? Filled(string? text) => text is null ? null : scope.Expand(text);
        return queue with
        {
            Queue = scope.Expand(queue.Queue),
            Connection = Filled(queue.Connection),
            Channel = Filled(queue.Channel),
            QueueManager = Filled(queue.QueueManager),
            User = Filled(queue.User),
            Password = Filled(queue.Password),
        };
    }

    /// <summary>
    /// Makes a check now, as the builder does to try one before it is added: whether it passes and what it found. Its
    /// variables are those of the steps played so far.
    /// </summary>
    public CheckResult TryCheck(JsonObject node)
    {
        var scope = _played ?? _globals.Child();
        var check = Checks.Build(node, scope, Context(scope));
        return check.Result(check.Try());
    }

    private void Checkpoint(JsonObject step, VariableScope scope)
    {
        var title = scope.Expand(step["checkpoint"]!.ToString());
        var line = Activity.Of(ActivityKind.Check, Part.Verb("Check"), Part.Word(title));
        _results.Log("STEP", line.Text);
        _view.ShowStep(line);
        Control.Gate();

        var results = new List<CheckResult>();
        foreach (var node in step["checks"] as JsonArray ?? [])
        {
            var check = Checks.Build(node!.AsObject(), scope, Context(scope));
            var result = check.Result(Evaluate(check));
            results.Add(result);
            if (result.Passed)
                _results.Log("PASS", $"{check.Label} ({result.Actual})");
            else
                Fail($"{check.Label}: {result.Detail}");
        }
        var look = (step["look"] as JsonArray ?? []).Select(item => scope.Expand(item!.ToString())).ToList();
        var tools = (step["tools"] as JsonArray ?? []).Select(item => Tool(item!.AsObject(), scope)).ToList();
        _view.ShowCheckpoint(title, results, look, tools);
        if (Control.Unattended)
            return;
        if (Control.StopAtCheckpoints || Control.StepByStep)
        {
            Control.Hold(HoldReason.Checkpoint);
            Control.Gate();
            return;
        }
        var pause = Math.Max(Control.Pace * 5, 4000);
        _view.ShowCarryingOn(TimeSpan.FromMilliseconds(pause));
        Control.Wait(pause);
    }

    // Waits for a check to pass, for things that take a moment, such as a service's next heartbeat.
    private Seen Evaluate(Check check)
    {
        var end = Environment.TickCount64 + (long)Control.Scale(check.Within).TotalMilliseconds;
        while (true)
        {
            var seen = check.Try();
            if (seen.Passed || Environment.TickCount64 >= end)
                return seen;
            Control.Wait(500);
        }
    }

    private ToolButton Tool(JsonObject node, VariableScope scope)
    {
        var kind = Kinds.Of(node, ScriptVocabulary.Tools) ?? throw new ScriptException($"{node.ToJsonString()} isn't a tool.");
        var subject = scope.Expand(node[kind]!.ToString());
        var arguments = (node["args"] as JsonArray ?? []).Select(arg => scope.Expand(arg!.ToString())).ToList();
        var label = node["label"] is { } given ? scope.Expand(given.ToString()) : $"Open {Path.GetFileName(subject.TrimEnd('\\', '/'))}";
        return kind switch
        {
            "openFolder" => new ToolButton(label, () => _machine.Open("folder", subject), kind),
            "openFile" => new ToolButton(label, () => _machine.Open("file", subject), kind),
            "app" => new ToolButton(label, () => _machine.Open("app", subject, arguments), kind),
            _ => new ToolButton(label, () => OpenConsole(label, subject, arguments), kind),
        };
    }

    // A PowerShell window that runs a script and stays open, such as a list of the services.
    private void OpenConsole(string title, string script, IReadOnlyList<string> arguments)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AutoTestRunner");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Regex.Replace(title, @"\W+", "-") + ".ps1");
        File.WriteAllText(file, $"$Host.UI.RawUI.WindowTitle = '{title.Replace("'", "''")}'\r\n{script}", new System.Text.UTF8Encoding(true));
        _machine.Open("app", "powershell.exe", ["-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file, .. arguments]);
    }

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
