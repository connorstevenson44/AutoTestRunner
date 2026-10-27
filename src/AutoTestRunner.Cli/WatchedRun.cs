using System.Globalization;
using System.IO;
using System.Windows;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// A run you watch: the panel beside the app shows what is happening and what to look at, Space pauses and carries on,
/// Enter goes a step at a time and Esc stops. The run goes on a worker thread; the panel keeps the UI thread. Opened
/// from the launcher, the panel's Run more goes back to it to pick the next cases, as closing the builder does.
/// </summary>
internal static class WatchedRun
{
    /// <summary>Runs these cases while you watch, from autotest run. Returns the run's exit code.</summary>
    public static int Run(Suite suite, IReadOnlyList<Case> cases, Options options)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Crashes.Watch(app);
        var code = ExitCode.Passed;
        app.Startup += (_, _) => Watch(suite, cases, options, canRunAgain: false, result => code = result, _ => app.Shutdown());
        app.Run();
        return (int)code;
    }

    /// <summary>
    /// The launcher, with the suites found near the runner, open on the one chosen. What is picked there runs, or a
    /// case is built, and the launcher comes back when that is over. With --start, the chosen suite's cases run first,
    /// as after autotest restarted as administrator to run them; with build, that case is built first. Returns the
    /// last run's exit code.
    /// </summary>
    public static int Launch(IReadOnlyList<FoundSuite> suites, FoundSuite chosen, Options options, BuildRequest? build = null)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Crashes.Watch(app);
        var code = ExitCode.Passed;
        var found = suites;

        // The launcher on a suite, loaded again, as a run or the builder may have changed it; tick is a case file to
        // tick, such as the one just built.
        void ShowLauncher(string folder, Options last, string? tick = null)
        {
            found = LauncherWindow.Reload(found, folder);
            var open = found.FirstOrDefault(each => SamePath(each.Folder, folder)) ?? found[0];
            if (tick is not null && open.Suite?.Cases.FirstOrDefault(item => SamePath(item.File, tick)) is { } built)
                last = last with { Cases = built.Number.ToString(CultureInfo.InvariantCulture) };
            var launcher = new LauncherWindow(found, open, last);
            launcher.Closed += (_, _) =>
            {
                if (launcher.Picked is { } picked)
                    RunPicked(picked.Suite, picked.Cases, picked.Options);
                else if (launcher.Opened is { } made)
                {
                    found = [.. found, FoundSuite.Load(made)];
                    ShowLauncher(made, last with { Cases = null, Tags = [] });
                }
                else if (launcher.Build is { } request)
                    Build(request, last);
                else
                    app.Shutdown();
            };
            launcher.Show();
        }

        void RunPicked(Suite suite, IReadOnlyList<Case> cases, Options picked) =>
            Watch(suite, cases, picked, canRunAgain: true, result => code = result, again =>
            {
                if (again)
                    ShowLauncher(suite.Folder, picked);
                else
                    app.Shutdown();
            });

        void Build(BuildRequest request, Options last)
        {
            var ended = new TaskCompletionSource();
            var panel = BuildSession.Open(request, last, () => ended.TrySetResult());
            panel.Closed += (_, _) => AfterEnded(panel, ended.Task, () => ShowLauncher(request.Suite.Folder, last with { Cases = null }, panel.SavedAs));
        }

        app.Startup += (_, _) =>
        {
            if (build is not null)
                Build(build, options);
            else if (options.Start && chosen.Suite is { } suite)
                RunPicked(suite, options.Pick(suite), options with { Start = false });
            else
                ShowLauncher(chosen.Folder, options);
        };
        app.Run();
        return (int)code;
    }

    // Shows a run's panel; closed is called with whether Run more was clicked, once the run has ended.
    private static void Watch(Suite suite, IReadOnlyList<Case> cases, Options options, bool canRunAgain, Action<ExitCode> result, Action<bool> closed)
    {
        var ended = new TaskCompletionSource();
        var panel = Start(suite, cases, options, canRunAgain, code =>
        {
            result(code);
            ended.TrySetResult();
        });
        panel.Closed += (_, _) => AfterEnded(panel, ended.Task, () => closed(panel.RunAgain));
    }

    // A panel closed after Stop goes before the run has written its results, and the builder's before it has closed
    // the app: wait for it to end, but not for ever if the app it drives won't let go.
    private static void AfterEnded(Window panel, Task ended, Action then) =>
        Task.WhenAny(ended, Task.Delay(TimeSpan.FromSeconds(30))).ContinueWith(_ => panel.Dispatcher.BeginInvoke(then), TaskScheduler.Default);

    private static bool SamePath(string one, string other) =>
        string.Equals(Path.GetFullPath(one).TrimEnd('\\', '/'), Path.GetFullPath(other).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // Shows the panel, and starts the run once it is on the screen.
    private static RunPanel Start(Suite suite, IReadOnlyList<Case> cases, Options options, bool canRunAgain, Action<ExitCode> ended)
    {
        var control = new RunControl(unattended: false, options.Speed)
        {
            StopAtCheckpoints = options.Mode == "pause-at-checkpoints",
            StepByStep = options.Mode == "step-by-step",
        };
        var folder = Results.NewFolder(options.Results ?? Path.Combine(suite.Folder, "results"));
        var results = new Results(folder, Console.Out);
        var panel = new RunPanel(suite, cases, control, folder, canRunAgain);
        panel.Closed += (_, _) => control.RequestStop();

        panel.Loaded += (_, _) =>
        {
            var worker = new Thread(() =>
            {
                using var driver = Runs.Driver(control, results, () => panel.Bounds, panel.KeepClear);
                var runner = Runs.Runner(suite, driver, panel, control, results, options);
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
                    Crashes.Log(ex, $"Running {suite.Name}. Its run.log is in {folder}.");
                    runner.Capture("unhandled");
                    panel.Tell($"Something went wrong in the runner itself. It's in errors.log in {Crashes.Folder}:\n\n" + ex.Message);
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
