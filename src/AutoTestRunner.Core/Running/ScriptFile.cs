using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// How a script file in a suite is run: a .ps1 with Windows PowerShell, and a .cs with dotnet run, which builds it
/// the first time and after it changes, so it needs the .NET 10 SDK. Both get the step's args.
/// </summary>
public static class ScriptFile
{
    /// <summary>What dotnet prints when it has a runtime but no SDK, so can't build a C# script.</summary>
    private const string NoSdk = "No .NET SDKs were found";

    /// <summary>The message for a C# script on a machine that can't build one.</summary>
    public const string NeedsSdk = "C# scripts run with dotnet run, which needs the .NET 10 SDK on this machine, from https://dot.net.";

    public static bool IsCSharp(string path) => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The program and command line that run a script file with its args. A C# script builds quietly, so what it
    /// prints is only its own output, or the build's errors when it has them.
    /// </summary>
    public static (string Program, IReadOnlyList<string> Arguments) Command(string path, IReadOnlyList<string> arguments) => IsCSharp(path)
        ? ("dotnet", ["run", "--file", path, "-v", "q", "--", .. arguments])
        : ("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path, .. arguments]);

    /// <summary>Why a script failed, with what it printed, and what to install when it was C# on a machine without the SDK.</summary>
    public static string Failed(string path, ProcessOutput output) =>
        $"{Path.GetFileName(path)} failed with exit code {output.ExitCode}: {output.Output}"
        + (IsCSharp(path) && output.Output.Contains(NoSdk, StringComparison.Ordinal) ? $" {NeedsSdk}" : "");

    /// <summary>Runs a script file, with a clear message when it is C# and dotnet isn't installed at all.</summary>
    public static ProcessOutput Run(IMachine machine, string path, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var (program, line) = Command(path, arguments);
        try
        {
            return machine.Run(program, line, timeout: timeout);
        }
        catch (System.ComponentModel.Win32Exception) when (IsCSharp(path))
        {
            throw new ScriptException(NeedsSdk);
        }
    }
}
