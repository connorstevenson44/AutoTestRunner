using System.Diagnostics;
using System.Globalization;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

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

    public void SetServiceRunning(string name, bool running)
    {
        using var service = new ServiceController(name);
        var wanted = running ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped;
        if (service.Status == wanted)
            return;
        if (running)
            service.Start();
        else
            service.Stop();
        service.WaitForStatus(wanted, TimeSpan.FromMinutes(2));
    }

    public string? RegistryValue(string key, string name)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var (hive, path) = key.Split('\\', 2) switch
        {
            [var first, var rest] => (first.ToUpperInvariant(), rest),
            [var first] => (first.ToUpperInvariant(), ""),
            _ => ("", ""),
        };
        var root = hive switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKU" or "HKEY_USERS" => Registry.Users,
            _ => throw new ArgumentException($"A registry key starts with HKLM, HKCU, HKCR or HKU, not {hive}."),
        };
        using var opened = root.OpenSubKey(path);
        return opened?.GetValue(name) switch
        {
            null => null,
            string[] lines => string.Join(" ", lines),
            byte[] bytes => Convert.ToHexString(bytes),
            var value => Convert.ToString(value, CultureInfo.InvariantCulture),
        };
    }

    public bool ProcessRunning(string name) =>
        Process.GetProcessesByName(name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name).Length > 0;

    // Get-WinEvent reads every log, including those under Applications and Services Logs.
    public bool EventLogged(string log, string? source, string text, DateTime since)
    {
        var output = PowerShell("""
            $log, $source, $text, $since = $args
            $filter = @{ LogName = $log; StartTime = [datetime]::Parse($since, [cultureinfo]::InvariantCulture) }
            if ($source) { $filter.ProviderName = $source }
            @(Get-WinEvent -FilterHashtable $filter -ErrorAction SilentlyContinue | Where-Object { $_.Message -like "*$text*" }).Count
            """, [log, source ?? "", text, since.ToString("o", CultureInfo.InvariantCulture)]);
        return int.TryParse(output.Output.Trim(), out var count) && count > 0;
    }

    public string Clipboard() => PowerShell("Get-Clipboard -Raw", []).Output;

    public (int Status, string Body) Get(string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = client.GetAsync(new Uri(url)).GetAwaiter().GetResult();
            return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return (0, ex.Message);
        }
    }

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
