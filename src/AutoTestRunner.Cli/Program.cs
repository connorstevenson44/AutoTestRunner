using System.IO;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest: runs a suite while you watch, or unattended for CI, and builds cases by using the app.
///
///   autotest                          the launcher, on the suites found near the runner
///   autotest launch [&lt;folder&gt;]        the launcher, on a suite or the suites found in a folder
///   autotest run &lt;suite folder&gt; [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
///            [--mode auto|pause-at-checkpoints|step-by-step] [--results &lt;folder&gt;] [--var name=value]...
///   autotest record &lt;suite folder&gt; [--name &lt;name&gt;] [--after &lt;case&gt;] [--add-to &lt;case&gt;]   builds a case
///   autotest validate &lt;suite folder&gt;   reads every script and reports what is wrong with them
///   autotest list &lt;suite folder&gt;    lists the cases
///   autotest preview-ui [--shots &lt;folder&gt;]   plays a made-up run in the panel, and saves pictures of it
/// </summary>
public static class Program
{
    private const string Usage = """
        autotest [launch [<suite folder, or a folder of suites>]] [the options of run, to start the launcher with]
        autotest run <suite folder> [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
                 [--mode auto|pause-at-checkpoints|step-by-step] [--results <folder>] [--var name=value]...
        autotest record <suite folder> [--name <name>] [--after <case>] [--add-to <case>]
        autotest validate <suite folder>
        autotest list <suite folder>
        autotest preview-ui [--shots <folder>]

        With no folder, the launcher offers the suites near autotest and the folder it was started from.

        --mode auto                  carries on by itself, showing each checkpoint for a few seconds (Space holds it there)
        --mode pause-at-checkpoints  carries on by itself, waiting for you at each checkpoint
        --mode step-by-step          waits for Enter before each step
        --name                       record: what the new case is called
        --after                      record: the new case carries on from where this case leaves off, by number or id
        --add-to                     record: adds steps to this case, before its checkpoint, by number or id
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["preview-ui", ..])
            return PreviewUi.Run(args[1..]);
        if (args is [] || args[0].StartsWith("--", StringComparison.Ordinal))
            return Launch(null, args, ["launch", .. args]);
        if (args is ["launch", ..])
            return args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? Launch(args[1], args[2..], args) : Launch(null, args[1..], args);
        if (args.Length < 2 || args[0] is not ("run" or "record" or "validate" or "list"))
            return Problem(null);

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
        if (args[0] == "record" && options.Unattended)
            options = options with { Problem = "The builder is used by a person; autotest run --unattended runs without one." };
        if (options.Problem is { } problem)
            return Problem(problem);
        if (args[0] == "record")
            return Record(suite, options, args);

        IReadOnlyList<Case> cases;
        try
        {
            cases = suite.Select(options.Cases);
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }

        if (NeedsAdministrator(suite) && options.Unattended)
        {
            Console.Error.WriteLine($"{suite.Name} needs to run as administrator.");
            return (int)ExitCode.NotAdministrator;
        }
        if (NeedsAdministrator(suite))
            return Elevate(args);
        return options.Unattended
            ? RunUnattended(suite, cases, options, Results.NewFolder(options.Results ?? Path.Combine(suite.Folder, "results")))
            : WatchedRun.Run(suite, cases, options);
    }

    // The launcher, on the suites found near autotest and the folder given or started from. A suite given, or the
    // only one found, that needs administrator asks for it now; with several, the launcher asks when one starts.
    private static int Launch(string? folder, string[] rest, string[] args)
    {
        var options = Options.Read(rest);
        if (options.Unattended)
            options = options with { Problem = "The launcher is for a run you watch; autotest run --unattended runs without it." };
        if (options.Problem is { } problem)
            return Problem(problem);
        if (folder is not null && !Directory.Exists(folder))
            return Problem($"There is no folder {folder}.");

        var asked = folder is not null && SuiteFinder.IsSuite(folder) ? Path.GetFullPath(folder) : null;
        var places = folder is null || asked is not null ? SuiteFinder.Places(AppContext.BaseDirectory, Environment.CurrentDirectory, asked) : [folder];
        var found = SuiteFinder.Find(places).Select(FoundSuite.Load).ToList();
        if (asked is not null && !found.Any(each => SamePath(each.Folder, asked)))
            found.Insert(0, FoundSuite.Load(asked));
        if (found.Count == 0)
        {
            Console.Error.WriteLine($"There are no suites in {(folder is null ? string.Join(", ", places.Distinct()) : folder)}, or the folders below. A suite is a folder with a suite.json.");
            return (int)ExitCode.ScriptError;
        }
        var chosen = asked is null ? found[0] : found.First(each => SamePath(each.Folder, asked));

        if (options.Cases is not null && chosen.Suite is { } suite)
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
        }
        if ((asked is not null || found.Count == 1 || options.Start) && NeedsAdministrator(chosen.Suite))
            return Elevate(args);
        return WatchedRun.Launch(found, chosen, options);
    }

    // The builder, then the launcher on the suites beside it.
    private static int Record(Suite suite, Options options, string[] args)
    {
        Case? Named(string? which, string flag) => which is null ? null : suite.Find(which) ?? throw new SuiteException([$"{flag}: there is no case {which}."]);
        BuildRequest request;
        try
        {
            if (options.AddTo is not null && options.After is not null)
                throw new SuiteException(["--add-to adds to a case, and --after starts a new one: give one of them."]);
            request = new BuildRequest(suite, options.Name ?? "New case", Named(options.After, "--after"), Named(options.AddTo, "--add-to"));
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }
        if (NeedsAdministrator(suite))
            return Elevate(args);
        var found = SuiteFinder.Find(SuiteFinder.Places(AppContext.BaseDirectory, Environment.CurrentDirectory, suite.Folder)).Select(FoundSuite.Load).ToList();
        if (!found.Any(each => SamePath(each.Folder, suite.Folder)))
            found.Insert(0, FoundSuite.Load(suite.Folder));
        return WatchedRun.Launch(found, found.First(each => SamePath(each.Folder, suite.Folder)), options with { AddTo = null, After = null, Name = null }, request);
    }

    private static int Problem(string? problem)
    {
        if (problem is not null)
            Console.Error.WriteLine(problem);
        Console.Error.WriteLine(Usage);
        return (int)ExitCode.ScriptError;
    }

    // A suite that installs services needs administrator, and so does driving an app that runs as administrator.
    private static bool NeedsAdministrator(Suite? suite) => suite?.App.RunAsAdmin == true && !Elevation.IsAdministrator;

    private static int Elevate(string[] args)
    {
        if (Elevation.Restart(args))
            return (int)ExitCode.Passed;
        Console.Error.WriteLine("The tests need to run as administrator, and that was declined.");
        return (int)ExitCode.NotAdministrator;
    }

    private static bool SamePath(string one, string other) =>
        string.Equals(Path.GetFullPath(one).TrimEnd('\\', '/'), Path.GetFullPath(other).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

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
}
