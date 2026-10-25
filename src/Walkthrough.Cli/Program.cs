using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using Walkthrough.Driving;
using Walkthrough.FlaUI;
using Walkthrough.Running;
using Walkthrough.Scripting;

namespace Walkthrough.Cli;

/// <summary>
/// walk: runs a suite while you watch, or unattended for CI.
///
///   walk run &lt;suite folder&gt; [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
///            [--mode itself|checks|steps] [--results &lt;folder&gt;] [--var name=value]...
///   walk check &lt;suite folder&gt;   reads every script and reports what is wrong with them
///   walk list &lt;suite folder&gt;    lists the cases
/// </summary>
public static class Program
{
    private const string Usage = """
        walk run <suite folder> [--cases 1-5,8] [--unattended] [--speed Slow|Modest|Fast]
                 [--mode itself|checks|steps] [--results <folder>] [--var name=value]...
        walk check <suite folder>
        walk list <suite folder>

        --mode itself   carries on by itself, showing each checkpoint for a few seconds (Space holds it there)
        --mode checks   carries on by itself, waiting for you at each checkpoint
        --mode steps    waits for Enter before each step
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("run" or "check" or "list"))
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
            case "check":
                Console.WriteLine($"{suite.Name}: {suite.Cases.Count} cases, {suite.Macros.Count} macros, {suite.Elements.Count} elements. No problems found.");
                return (int)ExitCode.Passed;
            case "list":
                foreach (var item in suite.Cases)
                    Console.WriteLine($"{item.Number,3}. {item.Name}  [{item.Id}]{(item.Needs.Count > 0 ? "  needs " + string.Join(", ", item.Needs) : "")}");
                return (int)ExitCode.Passed;
        }

        var options = Options.Read(args[2..]);
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
        try
        {
            cases = suite.Select(options.Cases);
        }
        catch (SuiteException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return (int)ExitCode.ScriptError;
        }

        var resultsFolder = Results.NewFolder(options.Results ?? Path.Combine(suite.Folder, "results"));
        return options.Unattended
            ? RunUnattended(suite, cases, options, resultsFolder)
            : Watched.Run(suite, cases, options, resultsFolder);
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
            var runner = new Runner(suite, driver, new WindowsMachine(), new ConsoleHost(), control, results, options.Variables);
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

/// <summary>What was asked for on the command line after the suite folder.</summary>
public sealed record Options
{
    public bool Unattended { get; init; }
    public string? Cases { get; init; }
    public Speed Speed { get; init; } = Speed.Modest;
    public string Mode { get; init; } = "itself";
    public string? Results { get; init; }
    public Dictionary<string, string> Variables { get; } = [];
    public string? Problem { get; init; }

    public static Options Read(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            string Next() => index + 1 < args.Length ? args[++index] : "";
            switch (args[index])
            {
                case "--unattended":
                    options = options with { Unattended = true };
                    break;
                case "--cases":
                    options = options with { Cases = Next() };
                    break;
                case "--speed":
                    var speed = Next();
                    options = Enum.TryParse<Speed>(speed, ignoreCase: true, out var parsed)
                        ? options with { Speed = parsed }
                        : options with { Problem = $"The speed is Slow, Modest or Fast, not {speed}." };
                    break;
                case "--mode":
                    var mode = Next();
                    options = mode is "itself" or "checks" or "steps" ? options with { Mode = mode } : options with { Problem = $"The mode is itself, checks or steps, not {mode}." };
                    break;
                case "--results":
                    options = options with { Results = Next() };
                    break;
                case "--var":
                    var pair = Next().Split('=', 2);
                    if (pair.Length == 2)
                        options.Variables[pair[0]] = pair[1];
                    else
                        options = options with { Problem = "--var is name=value." };
                    break;
                default:
                    options = options with { Problem = $"I don't know {args[index]}." };
                    break;
            }
        }
        return options;
    }
}
