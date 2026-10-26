using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest: runs a suite while you watch, or unattended for CI.
///
///   autotest launch &lt;suite folder&gt;    the launcher: pick the cases, how they run and how fast, then watch them
///   autotest run &lt;suite folder&gt; [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
///            [--mode auto|pause-at-checkpoints|step-by-step] [--results &lt;folder&gt;] [--var name=value]...
///   autotest validate &lt;suite folder&gt;   reads every script and reports what is wrong with them
///   autotest list &lt;suite folder&gt;    lists the cases
///   autotest preview-ui [--shots &lt;folder&gt;]   plays a made-up run in the panel, and saves pictures of it
/// </summary>
public static class Program
{
    private const string Usage = """
        autotest launch <suite folder> [the options of run, to start the launcher with]
        autotest run <suite folder> [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
                 [--mode auto|pause-at-checkpoints|step-by-step] [--results <folder>] [--var name=value]...
        autotest validate <suite folder>
        autotest list <suite folder>
        autotest preview-ui [--shots <folder>]

        --mode auto                  carries on by itself, showing each checkpoint for a few seconds (Space holds it there)
        --mode pause-at-checkpoints  carries on by itself, waiting for you at each checkpoint
        --mode step-by-step          waits for Enter before each step
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["preview-ui", ..])
            return PreviewUi.Run(args[1..]);
        if (args.Length < 2 || args[0] is not ("launch" or "run" or "validate" or "list"))
        {
            Console.Error.WriteLine(Usage);
            return (int)ExitCode.ScriptError;
        }

        Suite suite;
        try
        {
            suite = Suite.Load(args[1]);
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine($"The suite in {args[1]} has problems:");
            foreach (var line in ex.Problems)
                Console.Error.WriteLine("  " + line);
            return (int)ExitCode.ScriptError;
        }

        switch (args[0])
        {
            case "validate":
                Console.WriteLine($"{suite.Name}: {suite.Cases.Count} cases, {suite.Macros.Count} macros, {suite.Elements.Count} elements. No problems found.");
                return (int)ExitCode.Passed;
            case "list":
                foreach (var item in suite.Cases)
                    Console.WriteLine($"{item.Number,3}. {item.Name}  [{item.Id}]{(item.Needs.Count > 0 ? "  needs " + string.Join(", ", item.Needs) : "")}");
                return (int)ExitCode.Passed;
        }

        var options = Options.Read(args[2..]);
        if (args[0] == "launch" && options.Unattended)
            options = options with { Problem = "The launcher is for a run you watch; autotest run --unattended runs without it." };
        if (options.Problem is { } problem)
        {
            Console.Error.WriteLine(problem);
            Console.Error.WriteLine(Usage);
            return (int)ExitCode.ScriptError;
        }

        // A suite that installs services needs administrator, and so does driving an app that runs as administrator.
        if (suite.App.RunAsAdmin && !IsAdministrator)
        {
            if (options.Unattended)
            {
                Console.Error.WriteLine($"{suite.Name} needs to run as administrator.");
                return (int)ExitCode.NotAdministrator;
            }
            return RestartElevated(args);
        }

        IReadOnlyList<Case> cases;
        if (args[0] == "launch")
        {
            try
            {
                suite.Select(options.Cases);
            }
            catch (SuiteException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return (int)ExitCode.ScriptError;
            }
            return WatchedRun.Run(suite, null, options);
        }
        try
        {
            cases = suite.Select(options.Cases);
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }

        return options.Unattended
            ? RunUnattended(suite, cases, options, Results.NewFolder(options.Results ?? Path.Combine(suite.Folder, "results")))
            : WatchedRun.Run(suite, cases, options);
    }

    private static int RunUnattended(Suite suite, IReadOnlyList<Case> cases, Options options, string resultsFolder)
    {
        var control = new RunControl(unattended: true);
        var results = new Results(resultsFolder, Console.Out);
        var code = ExitCode.Passed;
        // UI Automation is used from a thread of its own, as in a watched run.
        var worker = new Thread(() =>
        {
            using var driver = new FlaUiDriver(control, () => null, _ => { }, note => results.Log("NOTE", note));
            var runner = new Runner(suite, driver, new WindowsMachine(), new ConsoleRunView(), control, results, options.Variables);
            results.Log("START", $"{suite.Name}: {runner.Exe}");
            if (runner.Missing() is [_, ..] missing)
            {
                results.Log("ERROR", "Missing: " + string.Join(", ", missing));
                code = ExitCode.Missing;
                return;
            }
            try
            {
                runner.Prepare(cases);
                var failed = runner.Run(cases);
                code = failed > 0 ? ExitCode.Failed : ExitCode.Passed;
            }
            catch (Exception ex)
            {
                results.Log("ERROR", ex.ToString());
                runner.Capture("unhandled");
                code = ExitCode.ScriptError;
            }
            finally
            {
                driver.CloseAll(runner.Exe);
            }
        });
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        worker.Join();
        return (int)code;
    }

    private static bool IsAdministrator =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static int RestartElevated(string[] args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start);
            return (int)ExitCode.Passed;
        }
        catch (Win32Exception)
        {
            Console.Error.WriteLine("The tests need to run as administrator, and that was declined.");
            return (int)ExitCode.NotAdministrator;
        }
    }
}
