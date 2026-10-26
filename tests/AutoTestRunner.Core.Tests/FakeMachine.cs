using AutoTestRunner.Running;

namespace AutoTestRunner.Core.Tests;

internal sealed class FakeMachine : IMachine
{
    public Dictionary<string, string> Services { get; } = [];

    public string? ServiceState(string name) => Services.GetValueOrDefault(name);

    public bool TaskExists(string path) => false;

    /// <summary>What a PowerShell script prints and its exit code, by its first line.</summary>
    public Dictionary<string, ProcessOutput> Scripts { get; } = [];

    // A program prints its command line.
    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null) =>
        Printed(new(0, $"{program} {string.Join(" ", arguments)}"), output);

    public ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null, Action<string, bool>? output = null) =>
        Printed(Scripts.GetValueOrDefault(script.Split('\n')[0].Trim(), new(0, "")), output);

    // Says each line as it is printed, the last as an error when the exit code isn't 0.
    private static ProcessOutput Printed(ProcessOutput result, Action<string, bool>? output)
    {
        var lines = result.Output.Length == 0 ? [] : result.Output.Split('\n');
        for (var index = 0; index < lines.Length; index++)
            output?.Invoke(lines[index], result.ExitCode != 0 && index == lines.Length - 1);
        return result;
    }

    public void Open(string what, string path, IReadOnlyList<string>? arguments = null) { }
}
