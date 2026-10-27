using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
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
}
