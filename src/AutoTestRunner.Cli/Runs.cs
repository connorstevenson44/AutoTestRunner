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
}
