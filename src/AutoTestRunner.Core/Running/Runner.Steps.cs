using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
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
        var at = new StepAt(this, step, scope);
        _ = AppStep(at) || WaitStep(at) || MachineStep(at) || DataStep(at) || FlowStep(at);
    }

    // Steps that run other steps, set variables, wait, or check. False when the step is another kind.
    private bool FlowStep(StepAt at)
    {
        var (step, scope) = (at.Step, at.Scope);
        switch (at.Kind)
        {
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
                var name = at.Text("item") ?? "item";
                // The steps share the case's variables, so what they set is still there after the repeat.
                foreach (var item in items)
                {
                    scope.Set(name, item);
                    Steps(step["steps"] as JsonArray ?? [], scope, "repeat");
                }
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

            case "wait":
            {
                var seconds = double.Parse(at.Text("wait")!, CultureInfo.InvariantCulture);
                var line = at.Line(ActivityKind.Wait, Part.Verb("Wait"), Part.Value($"{seconds.ToString(CultureInfo.InvariantCulture)} s"));
                ShowStep(line);
                _view.ShowWait(line);
                Control.Wait((int)(seconds * 1000));
                break;
            }

            case "checkpoint":
                Checkpoint(step, scope);
                break;

            default:
                return false;
        }
        return true;
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

    /// <summary>A step as it runs: its kind, and its properties with the scope's ${...} filled in.</summary>
    private sealed class StepAt
    {
        private readonly Runner _runner;
        private readonly string? _what;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public StepAt(Runner runner, JsonObject step, VariableScope scope)
        {
            (_runner, Step, Scope, Kind) = (runner, step, scope, ScriptVocabulary.KindOf(step));
            // The panel shows what a step does, with what it acts on; the script's own words for it show when pointing
            // at it. A wait's are read once it is over, so they can use what it found.
            var waits = Kind is "find" or "waitGone" or "waitText" or "whileShows" or "waitEnabled" or "useWindow";
            Say = Kind == "say" || waits ? null : Text("say");
            _what = Text("what");
        }

        public JsonObject Step { get; }

        public VariableScope Scope { get; }

        public string Kind { get; }

        public string? Say { get; }

        public string? Text(string key) => Step[key] is { } node ? Scope.Expand(node.ToString()) : null;

        public string Required(string key) => Text(key) ?? throw new ScriptException($"{Kind} needs {key}.");

        public List<string> Args() => [.. (Step["args"] as JsonArray ?? []).Select(arg => Scope.Expand(arg!.ToString()))];

        public Activity Line(ActivityKind activity, params Part[] parts) => new(activity, parts) { Say = Say };

        public TimeSpan Within(TimeSpan normal) =>
            Step["within"] is { } seconds ? TimeSpan.FromSeconds(double.Parse(Scope.Expand(seconds.ToString()), CultureInfo.InvariantCulture)) : normal;

        public Target TargetOf(string key) => Targets.Resolve(Step[key], Scope, _runner._suite.Elements, key == Kind ? _what : null);

        public List<string> ListOf(string key) => Step[key] switch
        {
            JsonArray list => [.. list.Select(item => Scope.Expand(item!.ToString()))],
            { } one => [.. Scope.Expand(one.ToString()).Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            null => throw new ScriptException($"{Kind} needs {key}."),
        };

        public IControl Find(Target target)
        {
            _runner._view.ShowWait(Activity.Of(ActivityKind.Wait, Part.Verb("Look for"), Part.Control(target)));
            return _runner._driver.Find(target, _runner.Control.Scale(Within(FindTimeout)));
        }

        // A wait step, once it is over: what it waited for, what it saw and how long it took.
        public void Waited(Activity line, string? saw = null)
        {
            var done = line with { Say = Text("say"), Detail = string.IsNullOrWhiteSpace(saw) ? null : saw };
            _runner._results.Log("WAITED", $"{done.Text}{(done.Detail is null ? "" : $": {done.Detail}")} ({_clock.Elapsed.TotalSeconds:0.0} s)");
            _runner._view.Waited(done, _clock.Elapsed);
        }
    }
}
