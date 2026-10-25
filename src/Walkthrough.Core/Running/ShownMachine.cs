using System.Diagnostics;

namespace Walkthrough.Running;

/// <summary>
/// The machine as a run uses it: every program and script it starts is shown as it runs, with its command line, what
/// it prints as it prints it, and how it ended, and goes in the log. Looking at services and scheduled tasks isn't
/// shown, as checks do that often.
/// </summary>
public sealed class ShownMachine(IMachine machine, IRunHost host, Results results) : IMachine
{
    public string? ServiceState(string name) => machine.ServiceState(name);

    public bool TaskExists(string path) => machine.TaskExists(path);

    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null) =>
        Shown(CommandLine(program, arguments, environment), null, output, shown => machine.Run(program, arguments, environment, timeout, shown));

    public ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null, Action<string, bool>? output = null) =>
        Shown(CommandLine("powershell", arguments, null), script, output, shown => machine.PowerShell(script, arguments, timeout, shown));

    public void Open(string what, string path, IReadOnlyList<string>? arguments = null) => machine.Open(what, path, arguments);

    /// <summary>
    /// A command line as a person would type it: the program's file name, then its arguments, quoted where they have
    /// spaces. Environment variables go first, with their values hidden, as they often hold secrets.
    /// </summary>
    public static string CommandLine(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment)
    {
        var parts = (environment ?? new Dictionary<string, string>()).Keys.Select(name => $"{name}=•••").ToList();
        parts.Add(Quote(Path.GetFileName(program)));
        parts.AddRange(arguments.Select(Quote));
        return string.Join(" ", parts);
    }

    private static string Quote(string text) => text.Length == 0 || text.Any(char.IsWhiteSpace) ? $"\"{text}\"" : text;

    private ProcessOutput Shown(string line, string? script, Action<string, bool>? output, Func<Action<string, bool>, ProcessOutput> start)
    {
        results.Log("RUN", line);
        host.CommandStarted(line, script);
        var clock = Stopwatch.StartNew();
        ProcessOutput? ended = null;
        try
        {
            ended = start((text, error) =>
            {
                if (text.Trim().Length > 0)
                    results.Log("OUTPUT", text.TrimEnd());
                host.CommandOutput(text, error);
                output?.Invoke(text, error);
            });
            return ended;
        }
        finally
        {
            // A program that couldn't start ends as -1, as one that was stopped does.
            var code = ended?.ExitCode ?? -1;
            results.Log("EXIT", $"{code}, after {clock.Elapsed.TotalSeconds:0.0} s");
            host.CommandFinished(code, clock.Elapsed);
        }
    }
}
