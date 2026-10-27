using AutoTestRunner.Data;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>What a run of the real app is made of: FlaUI driving it, this machine, and the databases and queues.</summary>
internal static class Runs
{
    /// <summary>The app's driver, which notes the workarounds it uses in the run's log.</summary>
    public static FlaUiDriver Driver(RunControl control, Results results, Func<ScreenRect?>? panel = null, Action<ScreenRect>? keepClear = null) =>
        new(control, panel ?? (() => null), keepClear ?? (_ => { }), note => results.Log("NOTE", note));

    public static Runner Runner(Suite suite, IDriver driver, IRunView view, RunControl control, Results results, Options options) =>
        new(suite, driver, new WindowsMachine(), view, control, results, options.Variables) { Connections = new Connections(new WindowsMachine()) };

    /// <summary>
    /// Plays the cases, and says how it went. What keeps them from starting, or goes wrong in the runner itself, goes in
    /// the run's log and to tell, for whoever is watching.
    /// </summary>
    public static ExitCode Play(Suite suite, Runner runner, IReadOnlyList<Case> cases, Results results, Action<string> tell)
    {
        results.Log("START", $"{suite.Name}: {runner.Exe}");
        try
        {
            if (runner.Missing() is [_, ..] missing)
            {
                results.Log("ERROR", "Missing: " + string.Join(", ", missing));
                tell($"These can't be found, so the tests can't start:\n\n{string.Join("\n", missing)}");
                return ExitCode.Missing;
            }
            runner.Prepare(cases);
            return runner.Run(cases) > 0 ? ExitCode.Failed : ExitCode.Passed;
        }
        catch (Exception ex)
        {
            results.Log("ERROR", ex.ToString());
            Crashes.Log(ex, $"Running {suite.Name}. Its run.log is in {results.Folder}.");
            runner.Capture("unhandled");
            tell($"Something went wrong in the runner itself. It's in errors.log in {Crashes.Folder}:\n\n" + ex.Message);
            return ExitCode.ScriptError;
        }
    }
}
