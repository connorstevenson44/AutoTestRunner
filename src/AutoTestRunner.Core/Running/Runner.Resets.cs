using System.Text.Json.Nodes;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
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
}
