using System.Diagnostics;
using System.ServiceProcess;
using System.Text;

namespace AutoTestRunner.Running;

public sealed class WindowsMachine : IMachine
{
    private static readonly string Temp = Path.Combine(Path.GetTempPath(), "AutoTestRunner");

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

    public ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null)
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
        var printed = new StringBuilder();
        void Printed(string? line, bool error)
        {
            if (line is null)
                return;
            lock (printed)
            {
                printed.AppendLine(line);
                output?.Invoke(line, error);
            }
        }
        process.OutputDataReceived += (_, e) => Printed(e.Data, error: false);
        process.ErrorDataReceived += (_, e) => Printed(e.Data, error: true);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(timeout ?? TimeSpan.FromMinutes(10)))
        {
            process.Kill(entireProcessTree: true);
            // So what it printed before it stopped is all in, ahead of the line saying so.
            process.WaitForExit();
            var stopped = $"{Path.GetFileName(program)} was still running after {(timeout ?? TimeSpan.FromMinutes(10)).TotalMinutes:0} minutes, so it was stopped.";
            Printed(stopped, error: true);
            lock (printed)
                return new ProcessOutput(-1, printed.ToString().TrimEnd());
        }
        process.WaitForExit();
        lock (printed)
            return new ProcessOutput(process.ExitCode, printed.ToString().TrimEnd());
    }

    public ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null, Action<string, bool>? output = null)
    {
        Directory.CreateDirectory(Temp);
        var file = Path.Combine(Temp, $"script-{Guid.NewGuid():N}.ps1");
        // Progress bars have nowhere to go, and would come back as XML among what the script prints.
        File.WriteAllText(file, "$ErrorActionPreference = 'Stop'\r\n$ProgressPreference = 'SilentlyContinue'\r\n" + script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return Run("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", file, .. arguments], timeout: timeout, output: output);
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
