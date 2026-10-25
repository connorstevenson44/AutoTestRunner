using System.IO;
using System.Windows;
using Walkthrough.Driving;
using Walkthrough.FlaUI;
using Walkthrough.Running;
using Walkthrough.Scripting;

namespace Walkthrough.Cli;

/// <summary>
/// A run you watch: the panel beside the app shows what is happening and what to look at, Space pauses and carries on,
/// Enter goes a step at a time and Esc stops. The run goes on a worker thread; the panel keeps the UI thread. Opened
/// from the launcher, the panel's Run more goes back to it to pick the next cases.
/// </summary>
internal static class Watched
{
    /// <summary>Runs these cases, or with none given, shows the launcher to pick them. Returns the last run's exit code.</summary>
    public static int Run(Suite suite, IReadOnlyList<Case>? cases, Options options)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var code = ExitCode.Passed;
        void Next(Options picked, IReadOnlyList<Case> run, bool fromLauncher)
        {
            var panel = Start(suite, run, picked, fromLauncher, result => code = result);
            panel.Closed += (_, _) =>
            {
                if (panel.RunAgain)
                    ShowLauncher(picked);
                else
                    app.Shutdown();
            };
        }
        void ShowLauncher(Options last)
        {
            var launcher = new Launcher(suite, last);
            launcher.Closed += (_, _) =>
            {
                if (launcher.Picked is { } picked)
                    Next(picked.Options, picked.Cases, fromLauncher: true);
                else
                    app.Shutdown();
            };
            launcher.Show();
        }
        app.Startup += (_, _) =>
        {
            if (cases is null)
                ShowLauncher(options);
            else
                Next(options, cases, fromLauncher: false);
        };
        app.Run();
        return (int)code;
    }

    // Shows the panel, and starts the run once it is on the screen.
    private static Panel Start(Suite suite, IReadOnlyList<Case> cases, Options options, bool canRunAgain, Action<ExitCode> ended)
    {
        var control = new RunControl(unattended: false, options.Speed)
        {
            StopAtCheckpoints = options.Mode == "checks",
            StepByStep = options.Mode == "steps",
        };
        var folder = Results.NewFolder(options.Results ?? Path.Combine(suite.Folder, "results"));
        var results = new Results(folder, Console.Out);
        var panel = new Panel(suite, cases, control, folder, canRunAgain);

        panel.Loaded += (_, _) =>
        {
            var worker = new Thread(() =>
            {
                using var driver = new FlaUiDriver(control, () => panel.Bounds, panel.KeepClear, note => results.Log("NOTE", note));
                var runner = new Runner(suite, driver, new WindowsMachine(), panel, control, results, options.Variables);
                panel.Attach(runner, driver);
                results.Log("START", $"{suite.Name}: {runner.Exe}");
                var code = ExitCode.Passed;
                try
                {
                    if (runner.Missing() is [_, ..] missing)
                    {
                        results.Log("ERROR", "Missing: " + string.Join(", ", missing));
                        panel.Tell($"These can't be found, so the tests can't start:\n\n{string.Join("\n", missing)}");
                        code = ExitCode.Missing;
                        return;
                    }
                    runner.Prepare(cases);
                    code = runner.Run(cases) > 0 ? ExitCode.Failed : ExitCode.Passed;
                }
                catch (Exception ex)
                {
                    results.Log("ERROR", ex.ToString());
                    runner.Capture("unhandled");
                    panel.Tell("Something went wrong in the runner itself. It's in run.log:\n\n" + ex.Message);
                    code = ExitCode.ScriptError;
                }
                finally
                {
                    ended(code);
                    panel.RunEnded();
                }
            });
            worker.SetApartmentState(ApartmentState.MTA);
            worker.IsBackground = true;
            worker.Start();
        };
        panel.Show();
        return panel;
    }
}
