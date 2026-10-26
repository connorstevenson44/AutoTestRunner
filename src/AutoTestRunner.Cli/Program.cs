using System.IO;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.FlaUI;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest: runs a suite while you watch, or unattended for CI, and builds cases by using the app. The commands and
/// their options are in <see cref="CommandLine"/>; autotest --help lists them.
///
///   autotest                          the launcher, on the suites found near the runner
///   autotest launch [&lt;folder&gt;]        the launcher, on a suite or the suites found in a folder
///   autotest run &lt;suite folder&gt; [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
///            [--mode auto|pause-at-checkpoints|step-by-step] [--results &lt;folder&gt;] [--var name=value]...
///   autotest record &lt;suite folder&gt; [--name &lt;name&gt;] [--after &lt;case&gt;] [--edit &lt;case&gt;]   builds or changes a case
///   autotest validate &lt;suite folder&gt;   reads every script and reports what is wrong with them
///   autotest list &lt;suite folder&gt;    lists the cases
///   autotest preview-ui [--shots &lt;folder&gt;]   plays a made-up run in the panel, and saves pictures of it
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Crashes.Watch();
        var commands = CommandLine.Commands(
            (folder, options) => Launch(folder, options, args is ["launch", ..] ? args : ["launch", .. args]),
            (command, folder, options) => SuiteCommand(command, folder, options, args),
            PreviewUi.Run);
        var parsed = commands.Parse(args);
        if (parsed.Errors is [_, ..] errors)
        {
            foreach (var error in errors)
                Console.Error.WriteLine(error.Message);
            var command = parsed.CommandResult.Command;
            Console.Error.WriteLine(command == commands ? "autotest --help says what it takes." : $"autotest {command.Name} --help says what it takes.");
            return (int)ExitCode.ScriptError;
        }
        return parsed.Invoke();
    }

    // run, record, validate and list, on a suite.
    private static int SuiteCommand(string command, string folder, Options options, string[] args)
    {
        Suite suite;
        try
        {
            suite = Suite.Load(folder);
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine($"The suite in {folder} has problems:");
            foreach (var line in ex.Problems)
                Console.Error.WriteLine("  " + line);
            return (int)ExitCode.ScriptError;
        }

        switch (command)
        {
            case "validate":
                Console.WriteLine($"{suite.Name}: {suite.Cases.Count} cases, {suite.Macros.Count} macros, {suite.Elements.Count} elements. No problems found.");
                return (int)ExitCode.Passed;
            case "list":
                foreach (var item in suite.Cases)
                    Console.WriteLine($"{item.Number,3}. {item.Name}  [{item.Id}]{(item.Needs.Count > 0 ? "  needs " + string.Join(", ", item.Needs) : "")}{(item.Tags.Count > 0 ? "  #" + string.Join(" #", item.Tags) : "")}");
                return (int)ExitCode.Passed;
            case "record":
                return Record(suite, options, args);
        }

        IReadOnlyList<Case> cases;
        try
        {
            cases = options.Pick(suite);
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"No case has the tags {string.Join(", ", options.Tags)}.");
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
    private static int Launch(string? folder, Options options, string[] args)
    {
        if (folder is not null && !Directory.Exists(folder))
        {
            Console.Error.WriteLine($"There is no folder {folder}.");
            return (int)ExitCode.ScriptError;
        }

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
                options.Pick(suite);
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
            if (options.Edit is not null && options.After is not null)
                throw new SuiteException(["--edit changes a case, and --after starts a new one: give one of them."]);
            request = new BuildRequest(suite, options.Name ?? "New case", Named(options.After, "--after"), Named(options.Edit, "--edit"));
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
        return WatchedRun.Launch(found, found.First(each => SamePath(each.Folder, suite.Folder)), options with { Edit = null, After = null, Name = null }, request);
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
                Crashes.Log(ex, $"Running {suite.Name} unattended. Its run.log is in {resultsFolder}.");
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
