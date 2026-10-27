using System.CommandLine;
using AutoTestRunner.Driving;

namespace AutoTestRunner.Cli;

/// <summary>
/// autotest's commands and options, read with System.CommandLine: it writes --help for each command, checks each
/// option's value, and says what is wrong with a command line.
/// </summary>
internal static class CommandLine
{
    private static readonly Option<string?> Cases = new("--cases") { Description = "The cases to run, by number or id, such as 1-5,8 or open-package. All of them when left out." };
    private static readonly Option<string[]> Tags = new("--tags") { Description = "Only the cases with one of these tags, such as smoke or smoke,nightly.", AllowMultipleArgumentsPerToken = true };
    private static readonly Option<Speed> Pace = new("--speed") { Description = "How fast the mouse moves and the steps go; or Manual, where you click and type, and each step waits for you.", DefaultValueFactory = _ => Speed.Modest };
    private static readonly Option<string> Mode = new("--mode")
    {
        Description = "auto carries on by itself, showing each checkpoint for a few seconds; pause-at-checkpoints waits for you at each checkpoint; step-by-step waits for Enter before each step.",
        DefaultValueFactory = _ => "auto",
    };
    private static readonly Option<string?> Results = new("--results") { Description = "The folder to put the results in. The suite's results folder when left out." };
    private static readonly Option<string[]> Variables = new("--var") { Description = "Sets one of the suite's variables, as name=value. Give it once for each." };
    private static readonly Option<bool> Unattended = new("--unattended") { Description = "Runs without the panel and without pausing, as CI does." };
    private static readonly Option<bool> Start = new("--start") { Description = "Starts the cases given straight away.", Hidden = true };
    private static readonly Option<string?> Name = new("--name") { Description = "What the new case is called." };
    private static readonly Option<string?> After = new("--after") { Description = "The new case carries on from where this case leaves off, by number or id." };
    private static readonly Option<string?> AfterReset = new("--after-reset")
    {
        Description = "The new case starts after this case has run, from its reset steps, by number or id: the cases after its reset don't start the app again.",
    };
    private static readonly Option<string?> Edit = new("--edit", "--add-to")
    {
        Description = "Opens this case to change it, by number or id: the app is taken to where it starts, and its steps can be played to any of them, and recorded again from there.",
    };
    private static readonly Option<string?> Install = new("--install")
    {
        Description = "The software to install, by its name in suite.json, such as sqlExpress,rabbitMq; missing for what isn't there, or all. Without it, autotest setup offers what is missing.",
    };
    private static readonly Option<bool> Yes = new("--yes") { Description = "Installs without asking first." };
    private static readonly Option<string?> Log = new("--log") { Description = "A file to add what autotest setup prints to, as it prints it.", Hidden = true };
    private static readonly Option<string?> Shots = new("--shots") { Description = "Saves a picture of each window in this folder." };

    // The options of a run, which the launcher and the builder pass on to the runs they start.
    private static readonly Option[] RunOptions = [Cases, Tags, Pace, Mode, Results, Variables];

    static CommandLine()
    {
        Mode.AcceptOnlyFromAmong("auto", "pause-at-checkpoints", "step-by-step");
        Variables.Validators.Add(result =>
        {
            foreach (var pair in result.GetValueOrDefault<string[]>() ?? [])
                if (!pair.Contains('=', StringComparison.Ordinal))
                    result.AddError($"--var is name=value, not {pair}.");
        });
    }

    /// <summary>The commands, each doing what it says with what was read.</summary>
    /// <param name="launch">The launcher: on a folder or none, with the options.</param>
    /// <param name="suite">run, record, validate or list: the command's name, the suite folder, and the options.</param>
    /// <param name="preview">preview-ui: the folder to save pictures in, or null.</param>
    public static RootCommand Commands(Func<string?, Options, int> launch, Func<string, string, Options, int> suite, Func<string?, int> preview)
    {
        var root = new RootCommand("Runs a WPF app's tests while you watch, or unattended in CI, and builds them by using the app. "
            + "With no command it opens the launcher on the suites near autotest and in the folder it was started from.");
        Add(root, [.. RunOptions, Start]);
        root.SetAction(result => launch(null, Read(result)));

        var folder = new Argument<string?>("folder") { Description = "A suite's folder, or a folder of suites.", Arity = ArgumentArity.ZeroOrOne };
        var launcher = new Command("launch", "Opens the launcher, to pick the cases to run, build new ones and change them.") { folder };
        Add(launcher, [.. RunOptions, Start]);
        launcher.SetAction(result => launch(result.GetValue(folder), Read(result)));
        root.Subcommands.Add(launcher);

        root.Subcommands.Add(SuiteCommand("run", "Runs a suite's cases while you watch, or unattended.", [.. RunOptions, Unattended], suite));
        root.Subcommands.Add(SuiteCommand("record", "Builds a new case by using the app, or changes one.", [.. RunOptions, Name, After, AfterReset, Edit], suite));
        root.Subcommands.Add(SuiteCommand("setup", "Looks for the software a suite needs, such as SQL Server Express or RabbitMQ, and installs what you say.",
            [Variables, Install, Yes, Log], suite));
        root.Subcommands.Add(SuiteCommand("validate", "Reads every script in a suite and says what is wrong with them, without running anything.", [], suite));
        root.Subcommands.Add(SuiteCommand("list", "Lists a suite's cases in order.", [], suite));

        var previewUi = new Command("preview-ui", "Plays a made-up run in the launcher and the panel, to see them without an app.") { Shots };
        previewUi.SetAction(result => preview(result.GetValue(Shots)));
        root.Subcommands.Add(previewUi);
        return root;
    }

    private static Command SuiteCommand(string name, string description, Option[] options, Func<string, string, Options, int> action)
    {
        var folder = new Argument<string>("suite") { Description = "The suite's folder: the one with suite.json in it." };
        var command = new Command(name, description) { folder };
        Add(command, options);
        command.SetAction(result => action(name, result.GetValue(folder)!, Read(result)));
        return command;
    }

    private static void Add(Command command, IEnumerable<Option> options)
    {
        foreach (var option in options)
            command.Options.Add(option);
    }

    // What the command line asked for, from the options the command takes.
    private static Options Read(ParseResult result)
    {
        var taken = result.CommandResult.Command.Options;
        T? Get<T>(Option<T> option) => taken.Contains(option) ? result.GetValue(option) : default;
        var options = new Options
        {
            Cases = Get(Cases),
            Tags = [.. (Get(Tags) ?? []).SelectMany(tags => tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))],
            Speed = taken.Contains(Pace) ? result.GetValue(Pace) : Speed.Modest,
            Mode = Get(Mode) ?? "auto",
            Results = Get(Results),
            Unattended = Get(Unattended),
            Start = Get(Start),
            Name = Get(Name),
            After = Get(After),
            AfterReset = Get(AfterReset),
            Install = Get(Install),
            Yes = Get(Yes),
            Log = Get(Log),
            Edit = Get(Edit),
        };
        foreach (var pair in Get(Variables) ?? [])
        {
            var (key, value) = pair.Split('=', 2) is [var left, var right] ? (left, right) : (pair, "");
            options.Variables[key] = value;
        }
        return options;
    }
}
