using System.IO;
using AutoTestRunner.Cli.Views;
using AutoTestRunner.Driving;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest: runs a suite while you watch, or unattended for CI, and builds cases by using the app. The commands and
/// their options are in <see cref="CommandLine"/>; autotest --help lists them.
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
            case "setup":
                return Setup(suite, options, args);
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

        if (Elevation.Needed(suite) && options.Unattended)
        {
            Console.Error.WriteLine($"{suite.Name} needs to run as administrator.");
            return (int)ExitCode.NotAdministrator;
        }
        if (Elevation.Needed(suite))
            return Elevate(args);
        return options.Unattended
            ? RunUnattended(suite, cases, options, Results.NewFolder(options.ResultsFolder(suite)))
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
        if ((asked is null ? found.FirstOrDefault() : Among(found, asked)) is not { } chosen)
        {
            Console.Error.WriteLine($"There are no suites in {(folder is null ? string.Join(", ", places.Distinct()) : folder)}, or the folders below. A suite is a folder with a suite.json.");
            return (int)ExitCode.ScriptError;
        }

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
        if ((asked is not null || found.Count == 1 || options.Start) && Elevation.Needed(chosen.Suite))
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
            if (new[] { options.Edit, options.After, options.AfterReset }.Count(given => given is not null) > 1)
                throw new SuiteException(["--edit changes a case, and --after and --after-reset start a new one: give one of them."]);
            request = options.AfterReset is { } setup
                ? new BuildRequest(suite, options.Name ?? "New case", Named(setup, "--after-reset"), Reset: true)
                : new BuildRequest(suite, options.Name ?? "New case", Named(options.After, "--after"), Named(options.Edit, "--edit"));
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }
        if (Elevation.Needed(suite))
            return Elevate(args);
        var found = SuiteFinder.Find(SuiteFinder.Places(AppContext.BaseDirectory, Environment.CurrentDirectory, suite.Folder)).Select(FoundSuite.Load).ToList();
        return WatchedRun.Launch(found, Among(found, suite.Folder), options with { Edit = null, After = null, AfterReset = null, Name = null }, request);
    }

    // The suite in this folder among those found, put first when it wasn't found, as when it is further away.
    private static FoundSuite Among(List<FoundSuite> found, string folder)
    {
        if (found.FirstOrDefault(each => SuiteFinder.SamePath(each.Folder, folder)) is { } there)
            return there;
        found.Insert(0, FoundSuite.Load(folder));
        return found[0];
    }

    // Says what of the suite's software is there, then installs what was asked for, or what is missing once the person
    // says yes. A Windows feature needs administrator, which it asks for after the yes, so it isn't asked twice.
    private static int Setup(Suite suite, Options options, string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var log = options.Log is { } file ? new StreamWriter(file, append: true) { AutoFlush = true } : null;
        void Say(string line)
        {
            Console.WriteLine(line);
            log?.WriteLine(line);
        }
        if (suite.Software.Count == 0)
        {
            Say($"{suite.Name} needs no software: its suite.json has none.");
            return (int)ExitCode.Passed;
        }
        var machine = new WindowsMachine();
        var scope = Runner.Variables(suite, Path.GetTempPath(), options.Variables);
        static string Mark(SoftwareState state) => state.Status switch { SoftwareStatus.Installed => "✓", SoftwareStatus.Unknown => "?", _ => "↓" };
        var states = Software.Detect(suite, machine, scope);
        foreach (var state in states)
            Say($"  {Mark(state)} {state.Line}");
        IReadOnlyList<SoftwareState> picked;
        try
        {
            picked = Software.Picked(states, options.Install ?? "missing");
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }
        if (picked.Count == 0)
        {
            Say("Nothing to install.");
            return states.Any(state => state.Wanted) ? (int)ExitCode.Missing : (int)ExitCode.Passed;
        }
        if (!options.Yes)
        {
            Console.Write($"Install {string.Join(", ", picked.Select(state => state.Item.Title))}? [y/N] ");
            if (Console.ReadLine()?.Trim().StartsWith('y') != true)
                return (int)ExitCode.Missing;
        }
        if (!Elevation.IsAdministrator && picked.Any(state => SoftwareWindow.NeedsAdministrator(state.Item)))
        {
            Say("Installing needs administrator: asking for it.");
            return Elevate([.. args.Where(arg => arg != "--yes"), "--yes"]);
        }
        var done = true;
        foreach (var state in picked)
        {
            Say($"Installing {state.Item.Title} with {state.Item.How}");
            var output = Software.Install(state.Item, machine, suite.Folder, scope, (line, _) => Say("    " + line));
            var now = Software.Detect(state.Item, machine, scope);
            done &= now.Status == SoftwareStatus.Installed;
            Say($"  {(now.Status == SoftwareStatus.Installed ? "✓" : "✗")} {now.Line}{(output.ExitCode == 0 ? "" : $" (exit code {output.ExitCode})")}");
        }
        return done ? (int)ExitCode.Passed : (int)ExitCode.Missing;
    }

    // A suite that installs services needs administrator, and so does driving an app that runs as administrator.

    private static int Elevate(string[] args)
    {
        if (Elevation.Restart(args))
            return (int)ExitCode.Passed;
        Console.Error.WriteLine("The tests need to run as administrator, and that was declined.");
        return (int)ExitCode.NotAdministrator;
    }

    private static int RunUnattended(Suite suite, IReadOnlyList<Case> cases, Options options, string resultsFolder)
    {
        var control = new RunControl(unattended: true);
        var results = new Results(resultsFolder, Console.Out);
        var code = ExitCode.Passed;
        // UI Automation is used from a thread of its own, as in a watched run.
        var worker = new Thread(() =>
        {
            using var driver = Runs.Driver(control, results);
            var runner = Runs.Runner(suite, driver, new ConsoleRunView(), control, results, options);
            code = Runs.Play(suite, runner, cases, results, tell: _ => { });
            // The app is closed for the next job on the machine, unless it was never started.
            if (code != ExitCode.Missing)
                driver.CloseAll(runner.Exe);
        });
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();
        worker.Join();
        return (int)code;
    }
}
