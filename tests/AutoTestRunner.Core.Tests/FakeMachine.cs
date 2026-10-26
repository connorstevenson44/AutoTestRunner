using AutoTestRunner.Running;

namespace AutoTestRunner.Core.Tests;

internal sealed class FakeMachine : IMachine
{
    public Dictionary<string, string> Services { get; } = [];

    public string? ServiceState(string name) => Services.GetValueOrDefault(name);

    public bool TaskExists(string path) => false;

    public void SetServiceRunning(string name, bool running) => Services[name] = running ? "Running" : "Stopped";

    /// <summary>Registry values by key and name, as "HKLM\SOFTWARE\Contoso|Region".</summary>
    public Dictionary<string, string> Registry { get; } = [];

    public string? RegistryValue(string key, string name) => Registry.GetValueOrDefault($"{key}|{name}");

    public HashSet<string> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool ProcessRunning(string name) => Processes.Contains(name.Replace(".exe", "", StringComparison.OrdinalIgnoreCase));

    public List<string> Events { get; } = [];

    public bool EventLogged(string log, string? source, string text, DateTime since) => Events.Any(entry => entry.Contains(text, StringComparison.Ordinal));

    public string ClipboardText { get; set; } = "";

    public string Clipboard() => ClipboardText;

    /// <summary>What each web address answers.</summary>
    public Dictionary<string, (int Status, string Body)> Pages { get; } = [];

    public (int Status, string Body) Get(string url) => Pages.GetValueOrDefault(url, (404, ""));

    /// <summary>What a PowerShell script prints and its exit code, by its first line.</summary>
    public Dictionary<string, ProcessOutput> Scripts { get; } = [];

    /// <summary>What a program prints and its exit code, by its name, in place of its command line.</summary>
    public Dictionary<string, ProcessOutput> Programs { get; } = [];

    /// <summary>Programs that aren't installed, so don't start.</summary>
    public HashSet<string> Missing { get; } = [];

    // A program prints its command line.
    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null) =>
        Missing.Contains(program) ? throw new System.ComponentModel.Win32Exception(2, $"{program} isn't installed.")
        : Printed(Programs.GetValueOrDefault(program, new(0, $"{program} {string.Join(" ", arguments)}")), output);

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
