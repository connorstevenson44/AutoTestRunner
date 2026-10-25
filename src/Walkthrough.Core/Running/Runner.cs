using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Walkthrough.Driving;
using Walkthrough.Scripting;

namespace Walkthrough.Running;

/// <summary>How a run ended, as the process's exit code. CI relies on these, as it did on the AutoHotkey runner's.</summary>
public enum ExitCode { Passed = 0, Failed = 1, Missing = 2, ScriptError = 3, NotAdministrator = 4 }

/// <summary>
/// Runs a suite's cases: each step in turn at a pace a person can follow, holding when the run is paused or step by
/// step, ending each case at its checkpoint. When something can't be found, a watched run asks the person what to do,
/// and an unattended run stops with a screenshot and a UI Automation dump.
/// </summary>
public sealed class Runner
{
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GoneTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(60);

    private readonly Suite _suite;
    private readonly IDriver _driver;
    private readonly IMachine _machine;
    private readonly IRunHost _host;
    private readonly Results _results;
    private readonly Scope _globals;
    private CaseResult? _current;

    // The status text before the last click, so a wait for a new status doesn't take the old one.
    private string? _marked;

    public Runner(Suite suite, IDriver driver, IMachine machine, IRunHost host, RunControl control, Results results, IReadOnlyDictionary<string, string>? overrides = null)
    {
        (_suite, _driver, _host, Control, _results) = (suite, driver, host, control, results);
        _machine = new ShownMachine(machine, host, results);
        _globals = Variables(suite, results.Folder, overrides ?? new Dictionary<string, string>());
    }

    public RunControl Control { get; }

    public Scope Globals => _globals;

    /// <summary>The exe to start, with its variables filled in and relative to the suite folder.</summary>
    public string Exe => SuitePath(_globals.Expand(_suite.App.Exe));

    /// <summary>
    /// The suite's variables, with the built-in suiteDir, resultsDir and temp. A variable is a text, or
    /// { "firstExisting": [paths] }: the first that exists, relative to the suite folder, else the last.
    /// </summary>
    public static Scope Variables(Suite suite, string resultsFolder, IReadOnlyDictionary<string, string> overrides)
    {
        var scope = new Scope(helpers: suite.Helpers);
        scope.Set("suiteDir", suite.Folder);
        scope.Set("resultsDir", resultsFolder);
        scope.Set("temp", Path.Combine(Path.GetTempPath(), "Walkthrough"));
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
    public static IReadOnlyList<string> Missing(Suite suite, Scope globals) =>
        suite.Requires.Select(path => SuitePath(suite, globals.Expand(path))).Where(path => !File.Exists(path) && !Directory.Exists(path)).ToList();

    /// <summary>What earlier runs left on the machine, from the suite's leftovers script: one line for each.</summary>
    public IReadOnlyList<string> Leftovers() => Leftovers(_suite, _machine);

    public static IReadOnlyList<string> Leftovers(Suite suite, IMachine machine)
    {
        if (suite.Reset.Leftovers is not { } script)
            return [];
        var output = machine.PowerShell(File.ReadAllText(SuitePath(suite, script)), []);
        return output.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Closes the app and runs the suite's reset script.</summary>
    public void ResetMachine()
    {
        if (_suite.Reset.PowerShell is not { } script)
            return;
        _driver.CloseAll(Exe);
        // What it prints goes in the log, as OUTPUT.
        _machine.Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", SuitePath(script)], timeout: TimeSpan.FromMinutes(5));
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
            _host.ShowStep("Clean up what an earlier run left behind: " + string.Join(", ", leftovers), Doing.Command);
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
            _results.Cases.Add(_current);
            _host.CaseStarted(index + 1, cases.Count, item);
            _results.Log("CASE", $"{item.Number}. {item.Name}");
            var clock = Stopwatch.StartNew();
            var stop = false;
            try
            {
                Steps(item.Steps, _globals.Child(), Path.GetFileName(item.File));
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
                stop = Control.Unattended || !_host.AskScriptError($"Something went wrong in the test script itself:\n\n{ex.Message}\n\nIt's saved with the results.");
            }
            _current.Duration = clock.Elapsed;
            _results.Log("RESULT", $"{item.Name}: {_current.Status.ToString().ToLowerInvariant()}");
            _host.CaseFinished(_current);
            if (stop)
                break;
        }
        var summary = _results.Finish(_suite.Name);
        _results.Log("DONE", summary.Replace("\r\n", " | "));
        var failed = _results.Cases.Count(result => result.Status != CaseStatus.Passed);
        _host.Finished(summary, _results.Folder, failed == 0);
        return failed;
    }

    // ===== Steps =====

    private void Steps(JsonArray steps, Scope scope, string where)
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
                    Step(step, scope);
                    break;
                }
                catch (StuckException ex)
                {
                    Stuck(ex);
                }
                catch (ScriptException ex)
                {
                    throw new ScriptException($"{where}, step {index + 1}: {ex.Message}");
                }
            }
        }
    }

    private void Step(JsonObject step, Scope scope)
    {
        var kind = step.Select(pair => pair.Key).FirstOrDefault(Vocabulary.Steps.ContainsKey) ?? "say";
        string? Text(string key) => step[key] is { } node ? scope.Expand(node.ToString()) : null;
        // A wait's say comes after it, so it can tell what the step found: it is filled in then.
        var after = kind is "find" or "waitGone" or "waitText" or "whileShows";
        var say = after ? null : Text("say");
        void SayAfter()
        {
            if (Text("say") is { } text)
                Say(text);
        }
        var what = Text("what");
        TimeSpan Within(TimeSpan normal) => step["within"] is { } seconds ? TimeSpan.FromSeconds(double.Parse(scope.Expand(seconds.ToString()))) : normal;
        Target TargetOf(string key) => Targets.Resolve(step[key], scope, _suite.Elements, what);
        IControl Find(Target target)
        {
            _host.ShowWait($"Look for {target.What}");
            return _driver.Find(target, Control.Scale(Within(FindTimeout)));
        }

        switch (kind)
        {
            case "say":
                Say(say ?? "");
                break;

            case "startApp":
                ShowStep(say ?? $"Start {_suite.App.Window}", Doing.App);
                _driver.Start(_suite.App, Exe);
                break;

            case "closeApp":
                ShowStep(say ?? $"Close {_suite.App.Window}", Doing.App);
                _driver.Close();
                break;

            case "click" when step["whileEnabled"]?.GetValue<bool>() == true:
                ClickWhileEnabled(TargetOf("click"), say, Text("how") == "mouse");
                break;

            case "click":
            case "tab":
            {
                var target = kind == "tab" ? Target.Of(new Selector(Type: "TabItem", Name: Text("tab"))) : TargetOf("click");
                var control = Find(target);
                var text = say ?? (kind == "tab" ? $"Open the {Text("tab")} tab" : $"Click {control.Name}");
                ShowStep(text, Doing.Click);
                Mark();
                _driver.Point(control, text);
                if (!control.IsEnabled)
                    throw new StuckException(Lower(text), "It's greyed out, so it can't be used yet.");
                _driver.Click(control, Text("how") == "mouse");
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "type":
            {
                var control = Find(TargetOf("type"));
                var value = Text("text") ?? "";
                var secret = step["secret"]?.GetValue<bool>() ?? false;
                var text = say ?? (secret ? "Type the secret" : $"Type {value}");
                ShowStep(text, Doing.Type);
                _driver.Point(control, text);
                _driver.Type(control, value, secret);
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "choose":
            {
                var control = Find(TargetOf("choose"));
                var option = Text("option") ?? throw new ScriptException("choose needs the option to pick.");
                var text = say ?? $"Pick {option}";
                ShowStep(text, Doing.Choose);
                _driver.Point(control, text);
                _driver.Choose(control, option);
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "tick":
            {
                var control = Find(TargetOf("tick"));
                var isChecked = step["checked"]?.GetValue<bool>() ?? true;
                var text = say ?? $"{(isChecked ? "Tick" : "Clear")} {control.Name}";
                ShowStep(text, Doing.Choose);
                _driver.Point(control, text);
                _driver.SetChecked(control, isChecked);
                Control.Wait(Control.Pace / 2);
                Keep(step, scope, control);
                break;
            }

            case "hover":
            {
                var control = Find(TargetOf("hover"));
                var text = say ?? $"Point at {control.Name}";
                ShowStep(text, Doing.Point);
                _driver.Point(control, text);
                Control.Wait(300);
                Keep(step, scope, control);
                break;
            }

            case "find":
            {
                var control = Find(TargetOf("find"));
                Keep(step, scope, control);
                SayAfter();
                break;
            }

            case "waitGone":
            {
                var target = TargetOf("waitGone");
                _host.ShowWait($"Wait for {target.What} to go");
                _driver.WaitGone(target, Control.Scale(Within(GoneTimeout)));
                SayAfter();
                break;
            }

            case "waitText":
                WaitText(step, scope, TargetOf("waitText"), Within(FindTimeout));
                SayAfter();
                break;

            case "replaceText":
            {
                var control = Find(TargetOf("replaceText"));
                var pattern = Text("regex") ?? throw new ScriptException("replaceText needs a regex.");
                var text = say ?? $"Change {pattern}";
                ShowStep(text, Doing.Type);
                _driver.Point(control, text);
                if (!_driver.ReplaceText(control, pattern, Text("replacement") ?? ""))
                    Fail($"The text has nothing matching {pattern}, so I couldn't {Lower(text)}");
                Control.Wait(Control.Pace);
                break;
            }

            case "key":
                ShowStep(say ?? $"Press {Text("key")}", Doing.Key);
                _driver.Key(Text("key")!);
                Control.Wait(Control.Pace / 2);
                break;

            case "answerDialog":
            {
                var title = Text("answerDialog")!;
                var button = Enum.Parse<DialogButton>(Text("button") ?? "OK", ignoreCase: true);
                _host.ShowWait($"Wait for the \"{title}\" message");
                var message = _driver.WaitDialog(title, Control.Scale(Within(DialogTimeout)));
                ShowStep($"{say ?? $"Click {button}"}  ({title}: \"{message}\")", Doing.Dialog);
                if (Text("expect") is { } expect && !message.Contains(expect, StringComparison.Ordinal))
                    Fail($"The \"{title}\" message was \"{message}\", expected \"{expect}\"");
                Mark();
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
                ShowStep(say ?? $"Pick {Path.GetFileName(path)}", Doing.File);
                Mark();
                _driver.PickFile(Text(kind)!, path, kind == "pickFolder");
                Control.Wait(Control.Pace / 2);
                break;
            }

            case "call":
                Call(step, scope);
                break;

            case "repeat":
            {
                var items = step["repeat"] is JsonArray list
                    ? list.Select(item => scope.Expand(item!.ToString())).ToList()
                    : scope.Expand(step["repeat"]!.ToString()).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
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
                WhileShows(step, scope, TargetOf("whileShows"), Within(TimeSpan.FromMinutes(5)));
                SayAfter();
                break;

            case "if":
            {
                var check = Checks.Build(step["if"]!.AsObject(), scope, _machine, Leftovers, Shows(scope));
                string? detail;
                try
                {
                    detail = check.Test();
                }
                catch (Exception ex) when (ex is not StopRunException)
                {
                    detail = "Error: " + ex.Message;
                }
                _results.Log("IF", $"{check.Label}: {(detail is null ? "yes" : "no, " + detail)}");
                Steps((detail is null ? step["steps"] : step["else"]) as JsonArray ?? [], scope, detail is null ? "if" : "else");
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
                var line = ShownMachine.CommandLine(program, arguments, environment);
                ShowStep(say ?? $"Run {Path.GetFileName(program)}", Doing.Command, line);
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
                ShowStep(say ?? "Run a PowerShell script", Doing.Command, ShownMachine.CommandLine("powershell", arguments, null) + "\n" + script);
                var output = _machine.PowerShell(script, arguments);
                if (output.ExitCode != 0)
                    Fail($"A PowerShell step failed with exit code {output.ExitCode}: {output.Output}");
                KeepOutput(step, scope, output);
                break;
            }

            case "checkpoint":
                Checkpoint(step, scope);
                break;
        }
    }

    private void Call(JsonObject step, Scope scope)
    {
        var name = scope.Expand(step["call"]!.GetValue<string>());
        if (!_suite.Macros.TryGetValue(name, out var macro))
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

    private void WaitText(JsonObject step, Scope scope, Target target, TimeSpan within)
    {
        var wanted = WantedText(step, scope, _driver.TryFind(target) is { } before ? TextOf(before) : null);
        _host.ShowWait(WaitingFor(target, wanted));
        var end = Environment.TickCount64 + (long)Control.Scale(within).TotalMilliseconds;
        while (true)
        {
            Control.Gate();
            var control = _driver.TryFind(target);
            var found = control is null ? null : TextOf(control);
            if (found is not null && wanted.Test(found))
            {
                _results.Log("TEXT", found);
                Keep(step, scope, control!);
                return;
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
    private void WhileShows(JsonObject step, Scope scope, Target each, TimeSpan within)
    {
        var until = Targets.Resolve(step["untilText"] ?? throw new ScriptException("whileShows needs untilText: the control that says when it is done."),
            scope, _suite.Elements);
        var wanted = WantedText(step, scope, first: null);
        _host.ShowWait(WaitingFor(until, wanted));
        var end = Environment.TickCount64 + (long)Control.Scale(within).TotalMilliseconds;
        for (var rounds = 0; ; )
        {
            Control.Gate();
            var found = _driver.TryFind(until) is { } control ? TextOf(control) : null;
            if (found is not null && wanted.Test(found))
            {
                _results.Log("TEXT", found);
                if (step["as"] is { } name)
                    scope.Set(name.GetValue<string>(), found);
                return;
            }
            if (_driver.TryFind(each) is not null)
            {
                // Steps that never make it go away would go round for ever.
                if (++rounds > MaxRounds)
                    throw new StuckException($"see \"{wanted.Expected}\" in {until.What}", $"{each.What} was still showing after {MaxRounds} rounds.");
                Steps(step["steps"] as JsonArray ?? [], scope, "whileShows");
                _host.ShowWait(WaitingFor(until, wanted));
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
    private Wanted WantedText(JsonObject step, Scope scope, string? first)
    {
        string? Text(string key) => step[key] is { } node ? scope.Expand(node.ToString()) : null;
        var startsWith = Text("startsWith");
        var contains = Text("contains");
        var equals = Text("equals");
        var pattern = Text("matches") is { } given ? new Regex(given) : null;
        var changes = step["changes"]?.GetValue<bool>() ?? false;
        var isNew = step["new"]?.GetValue<bool>() ?? false;
        var marked = _marked;
        return new Wanted(startsWith ?? contains ?? equals ?? pattern?.ToString() ?? "new text", found =>
            (startsWith is null || found.StartsWith(startsWith, StringComparison.Ordinal))
            && (contains is null || found.Contains(contains, StringComparison.Ordinal))
            && (equals is null || found == equals)
            && (pattern is null || pattern.IsMatch(found))
            && (!changes || found != first)
            && (!isNew || found != marked));
    }

    private sealed record Wanted(string Expected, Func<string, bool> Test);

    private static string WaitingFor(Target target, Wanted wanted) =>
        wanted.Expected == "new text" ? $"Wait for {target.What} to change" : $"Wait for {target.What} to say \"{wanted.Expected}\"";

    // Clicks a control again and again while it is there and can be used, such as Up until the top folder. Not being
    // there at all is fine: there is nothing to do.
    private void ClickWhileEnabled(Target target, string? say, bool useMouse)
    {
        for (var clicks = 0; _driver.TryFind(target) is { IsEnabled: true } control; clicks++)
        {
            if (clicks == 50)
                throw new StuckException($"get past {target.What}", "It was still there after 50 clicks.");
            var text = say ?? $"Click {control.Name}";
            ShowStep(text, Doing.Click);
            Mark();
            _driver.Point(control, text);
            _driver.Click(control, useMouse);
            Control.Wait(Math.Max(Control.Pace / 2, 300));
        }
    }

    // A control's text: what is typed in a text box or editor, or the name of anything else, such as a label.
    private static string TextOf(IControl control) => control.Value ?? control.Name;

    // The shows check: whether a target is in the app now.
    private Func<JsonNode, (string What, Func<bool> Shown)> Shows(Scope scope) => node =>
    {
        var target = Targets.Resolve(node, scope, _suite.Elements);
        return (target.What, () => _driver.TryFind(target) is not null);
    };

    private void Checkpoint(JsonObject step, Scope scope)
    {
        var title = scope.Expand(step["checkpoint"]!.ToString());
        _results.Log("STEP", "Check: " + title);
        _host.ShowStep("Check: " + title, Doing.Check);
        Control.Gate();

        var results = new List<CheckResult>();
        foreach (var node in step["checks"] as JsonArray ?? [])
        {
            var check = Checks.Build(node!.AsObject(), scope, _machine, Leftovers, Shows(scope));
            var detail = Evaluate(check);
            results.Add(new CheckResult(check.Label, detail is null, detail));
            if (detail is null)
                _results.Log("PASS", check.Label);
            else
                Fail($"{check.Label}: {detail}");
        }
        var look = (step["look"] as JsonArray ?? []).Select(item => scope.Expand(item!.ToString())).ToList();
        var tools = (step["tools"] as JsonArray ?? []).Select(item => Tool(item!.AsObject(), scope)).ToList();
        _host.ShowCheckpoint(title, results, look, tools);
        if (Control.Unattended)
            return;
        if (Control.StopAtCheckpoints || Control.StepByStep)
        {
            Control.Hold(Waiting.Checkpoint);
            Control.Gate();
            return;
        }
        var pause = Math.Max(Control.Pace * 5, 4000);
        _host.ShowCarryingOn(TimeSpan.FromMilliseconds(pause));
        Control.Wait(pause);
    }

    // Waits for a check to pass, for things that take a moment, such as a service's next heartbeat.
    private string? Evaluate(Check check)
    {
        var end = Environment.TickCount64 + (long)Control.Scale(check.Within).TotalMilliseconds;
        while (true)
        {
            string? detail;
            try
            {
                detail = check.Test();
            }
            catch (Exception ex) when (ex is not StopRunException)
            {
                detail = "Error: " + ex.Message;
            }
            if (detail is null || Environment.TickCount64 >= end)
                return detail;
            Control.Wait(500);
        }
    }

    private ToolButton Tool(JsonObject node, Scope scope)
    {
        var kind = Kinds.Of(node, Vocabulary.Tools) ?? throw new ScriptException($"{node.ToJsonString()} isn't a tool.");
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
        var folder = Path.Combine(Path.GetTempPath(), "Walkthrough");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, Regex.Replace(title, @"\W+", "-") + ".ps1");
        File.WriteAllText(file, $"$Host.UI.RawUI.WindowTitle = '{title.Replace("'", "''")}'\r\n{script}", new System.Text.UTF8Encoding(true));
        _machine.Open("app", "powershell.exe", ["-NoExit", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file, .. arguments]);
    }

    // ===== Showing the person what is happening =====

    /// <summary>Shows what is about to happen. Step by step, it then waits for Enter; paused, until the run carries on.</summary>
    private void ShowStep(string text, Doing kind = Doing.Say, string? command = null)
    {
        _results.Log("STEP", text);
        _host.ShowStep(text, kind, command);
        if (Control.StepByStep && !Control.Unattended)
            Control.Hold(Waiting.Step);
        Control.Gate();
    }

    /// <summary>Explains what is on screen, and gives time to read it.</summary>
    private void Say(string text)
    {
        ShowStep(text);
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

    private static void Keep(JsonObject step, Scope scope, IControl control)
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

    private static void KeepOutput(JsonObject step, Scope scope, ProcessOutput output)
    {
        if (step["as"] is not { } name)
            return;
        scope.Set(name.GetValue<string>(), Value.Of(output.Output, new Dictionary<string, string>
        {
            ["output"] = output.Output,
            ["code"] = output.ExitCode.ToString(),
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
        Control.Hold(Waiting.Stuck);
        var answer = _host.AskStuck(message, shot);
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

    private static string SuitePath(Suite suite, string path) =>
        Path.IsPathRooted(path) || !(path.Contains('/') || path.Contains('\\')) ? path : Path.GetFullPath(Path.Combine(suite.Folder, path));

    // "Click Add" as "click Add", to go on the end of a sentence.
    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
