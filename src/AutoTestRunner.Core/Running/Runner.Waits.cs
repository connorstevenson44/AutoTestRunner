using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
    // Steps that wait for the app: for a control, its text, a window. False when the step is another kind.
    private bool WaitStep(StepAt at)
    {
        var (step, scope) = (at.Step, at.Scope);
        switch (at.Kind)
        {
            case "find":
            {
                var target = at.TargetOf("find");
                var control = at.Find(target);
                Keep(step, scope, control);
                // What it found is shown when the case keeps it.
                at.Waited(at.Line(ActivityKind.Wait, Part.Verb("Find"), Part.Control(target, control)), step["as"] is null ? null : TextOf(control));
                break;
            }

            case "waitGone":
            {
                var target = at.TargetOf("waitGone");
                var line = at.Line(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target), Part.Word("gone"));
                _view.ShowWait(line);
                _driver.WaitGone(target, Control.Scale(at.Within(GoneTimeout)));
                at.Waited(line);
                break;
            }

            case "waitText":
            {
                var target = at.TargetOf("waitText");
                var (line, found) = WaitText(step, scope, target, at.Within(FindTimeout));
                at.Waited(line, found);
                break;
            }

            case "waitEnabled":
            {
                var target = at.TargetOf("waitEnabled");
                var control = WaitEnabled(target, at.Within(FindTimeout));
                Keep(step, scope, control);
                at.Waited(at.Line(ActivityKind.Wait, Part.Verb("Wait"), Part.Control(target, control), Part.Word("usable")));
                break;
            }

            case "useWindow":
            {
                var title = at.Required("useWindow");
                var line = at.Line(ActivityKind.Wait, Part.Verb("Switch to"), Part.Window(title));
                _view.ShowWait(line);
                _driver.UseWindow(title, Control.Scale(at.Within(TimeSpan.FromSeconds(60))));
                at.Waited(line);
                break;
            }

            case "whileShows":
            {
                var (line, found) = WhileShows(step, scope, at.TargetOf("whileShows"), at.Within(TimeSpan.FromMinutes(5)));
                at.Waited(line, found);
                break;
            }

            default:
                return false;
        }
        return true;
    }

    // Waits for a control's text, and gives the wait's line and the text it saw.
    private (Activity Line, string Found) WaitText(JsonObject step, VariableScope scope, Target target, TimeSpan within)
    {
        var wanted = WantedText(step, scope, _driver.TryFind(target) is { } before ? TextOf(before) : null);
        var line = WaitingFor(target, wanted);
        _view.ShowWait(line);
        var end = Control.Deadline(within);
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
            WaitMore(end, $"see \"{wanted.Expected}\" in {target.What}", found is null ? null : $"It says: {found}");
        }
    }

    // Runs steps each time a control shows, such as a window that asks about each step of a removal, until another
    // control's text says it is done. Another window showing up instead, or nothing happening for within, is stuck.
    // Gives the wait's line and the text that said it was done.
    private (Activity Line, string Found) WhileShows(JsonObject step, VariableScope scope, Target each, TimeSpan within)
    {
        var until = Targets.Resolve(step["untilText"] ?? throw new ScriptException("whileShows needs untilText: the control that says when it is done."),
            scope, _suite.Elements);
        var wanted = WantedText(step, scope, first: null);
        var line = WaitingFor(until, wanted);
        _view.ShowWait(line);
        var end = Control.Deadline(within);
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
                end = Control.Deadline(within);
                continue;
            }
            WaitMore(end, $"see \"{wanted.Expected}\" in {until.What}", found is null ? null : $"It says: {found}");
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
        var end = Control.Deadline(within);
        while (true)
        {
            Control.Gate();
            var control = _driver.TryFind(target);
            if (control is { IsEnabled: true })
                return control;
            WaitMore(end, $"see {target.What} become usable", control is null ? null : "It's still greyed out.");
        }
    }

    // Waits a moment more for what a step is waiting to do, unless the app shows another window or the time is up.
    private void WaitMore(long end, string doing, string? seen)
    {
        if (_driver.OtherWindow() is { } other)
            throw new StuckException(doing, $"The app is showing another window instead: {other}");
        if (Environment.TickCount64 > end)
            throw new StuckException(doing, seen);
        Control.Wait(300);
    }

    // A control's text: what is typed in a text box or editor, or the name of anything else, such as a label.
    private static string TextOf(IControl control) => control.Value ?? control.Name;
}
