using System.Diagnostics;
using System.ServiceProcess;
using System.Text;

namespace Walkthrough.Running;

/// <summary>What a program printed, and how it ended.</summary>
public sealed record ProcessOutput(int ExitCode, string Output)
{
    public string FirstLine => Output.Split('\n', 2)[0].Trim();
}

/// <summary>
/// The machine the app runs on: its services and scheduled tasks, and programs and scripts to run. Checks and steps go
/// through this, so the engine's tests can stand in for Windows.
/// </summary>
public interface IMachine
{
    /// <summary>"Running", "Stopped" and so on, or null when there is no such service.</summary>
    string? ServiceState(string name);

    /// <summary>Whether a scheduled task exists, by its full path, such as \Contoso\Orders Export-main.</summary>
    bool TaskExists(string path);

    ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null);

    /// <summary>Runs a PowerShell script, passing arguments as $args, with errors stopping it.</summary>
    ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null);

    /// <summary>Opens a folder in File Explorer, a file in Notepad, or starts a program, for a person to look at.</summary>
    void Open(string what, string path, IReadOnlyList<string>? arguments = null);
}

public sealed class WindowsMachine : IMachine
{
    private static readonly string Temp = Path.Combine(Path.GetTempPath(), "Walkthrough");

    public string? ServiceState(string name)
    {
        var service = ServiceController.GetServices().FirstOrDefault(item => string.Equals(item.ServiceName, name, StringComparison.OrdinalIgnoreCase));
        if (service is null)
            return null;
        // As Windows' own tools word it: Running, Stopped, Start Pending.
        var state = service.Status.ToString();
        return string.Concat(state.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }

    public bool TaskExists(string path) => Run("schtasks.exe", ["/query", "/tn", path]).ExitCode == 0;

    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (var (key, value) in environment ?? new Dictionary<string, string>())
            start.Environment[key] = value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start.");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout ?? TimeSpan.FromMinutes(10)))
        {
            process.Kill(entireProcessTree: true);
            lock (output)
                return new ProcessOutput(-1, output + $"{Path.GetFileName(program)} was still running after {(timeout ?? TimeSpan.FromMinutes(10)).TotalMinutes:0} minutes, so it was stopped.");
        }
        process.WaitForExit();
        lock (output)
            return new ProcessOutput(process.ExitCode, output.ToString().TrimEnd());
    }

    public ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        Directory.CreateDirectory(Temp);
        var file = Path.Combine(Temp, $"script-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(file, "$ErrorActionPreference = 'Stop'\r\n" + script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file, .. arguments], timeout: timeout);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    public void Open(string what, string path, IReadOnlyList<string>? arguments = null)
    {
        var start = what switch
        {
            "folder" => new ProcessStartInfo("explorer.exe", $"\"{path}\""),
            "file" => new ProcessStartInfo("notepad.exe", $"\"{path}\""),
            _ => new ProcessStartInfo(path) { UseShellExecute = true },
        };
        foreach (var argument in arguments ?? [])
            start.ArgumentList.Add(argument);
        Process.Start(start);
    }
}
