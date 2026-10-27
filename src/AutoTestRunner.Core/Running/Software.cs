using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// Finds whether a suite's software is on the computer, and installs it: winget packages with winget, Windows features
/// with Enable-WindowsOptionalFeature, and the rest with the suite's own script. Detecting changes nothing; installing
/// is only done once a person has said which.
/// </summary>
public static class Software
{
    private static readonly TimeSpan LookTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(30);

    /// <summary>What is there of each of the suite's software, in the order suite.json gives it.</summary>
    public static IReadOnlyList<SoftwareState> Detect(Suite suite, IMachine machine, VariableScope scope) =>
        [.. suite.Software.Values.Select(item => Detect(item, machine, scope))];

    /// <summary>
    /// Whether it is there: its present check when it has one, else winget's or Windows' list of what is installed.
    /// With a version wanted, winget's version there has to start with it.
    /// </summary>
    public static SoftwareState Detect(SoftwareSettings item, IMachine machine, VariableScope scope)
    {
        try
        {
            bool? present = item.Present is { } check ? Checks.Build(check, scope, new CheckContext(machine, () => [])).Try().Passed : null;
            if (item.Winget is { } id)
            {
                var found = WingetVersion(machine, id, out var problem);
                if (problem is not null && present is null)
                    return new SoftwareState(item, SoftwareStatus.Unknown, Detail: problem);
                if (found is null && present != true)
                    return new SoftwareState(item, SoftwareStatus.Missing);
                if (found is not null && item.Version is { } wanted && !Matches(found, wanted))
                    return new SoftwareState(item, SoftwareStatus.OtherVersion, found);
                return new SoftwareState(item, SoftwareStatus.Installed, found);
            }
            if (item.WindowsFeature is { } feature && present is null)
            {
                var state = machine.PowerShell($"(Get-CimInstance Win32_OptionalFeature -Filter \"Name='{feature.Replace("'", "''")}'\").InstallState", [], LookTimeout).Output.Trim();
                return state switch
                {
                    "1" => new SoftwareState(item, SoftwareStatus.Installed),
                    "" => new SoftwareState(item, SoftwareStatus.Unknown, Detail: $"Windows has no feature called {feature}"),
                    _ => new SoftwareState(item, SoftwareStatus.Missing),
                };
            }
            return new SoftwareState(item, present == true ? SoftwareStatus.Installed : SoftwareStatus.Missing);
        }
        catch (Exception ex) when (ex is ScriptException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return new SoftwareState(item, SoftwareStatus.Unknown, Detail: ex.Message);
        }
    }

    /// <summary>Installs it, with what it prints passed on line by line, and says whether it ended well.</summary>
    public static ProcessOutput Install(SoftwareSettings item, IMachine machine, string suiteFolder, VariableScope scope, Action<string, bool>? output = null)
    {
        if (item.Winget is { } id)
        {
            List<string> args = ["install", "--id", id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"];
            if (item.Version is { } version)
                args.AddRange(["--version", version]);
            return machine.Run("winget", args, timeout: InstallTimeout, output: output);
        }
        if (item.WindowsFeature is { } feature)
        {
            var name = feature.Replace("'", "''");
            return machine.PowerShell($"$ProgressPreference = 'SilentlyContinue'; $result = Enable-WindowsOptionalFeature -Online -FeatureName '{name}' -All -NoRestart; "
                + $"'{name} is on' + $(if ($result.RestartNeeded) {{ ', after a restart' }} else {{ '.' }})", [], InstallTimeout, output);
        }
        var path = Path.GetFullPath(Path.Combine(suiteFolder, scope.Expand(item.Script!)));
        var local = scope.Child();
        local.Set("version", item.Version ?? "");
        var given = item.Args.Select(local.Expand).ToList();
        return ScriptFile.Run(machine, path, given, InstallTimeout, output, suiteFolder);
    }

    /// <summary>The version winget says is installed, or null when it isn't; problem says why winget couldn't be asked.</summary>
    public static string? WingetVersion(IMachine machine, string id, out string? problem)
    {
        problem = null;
        ProcessOutput listed;
        try
        {
            listed = machine.Run("winget", ["list", "--id", id, "--exact", "--accept-source-agreements", "--disable-interactivity"], timeout: LookTimeout);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            problem = "winget isn't on this computer. It comes with App Installer, from the Microsoft Store.";
            return null;
        }
        return Listed(listed.Output, id);
    }

    /// <summary>The version on winget list's line for the package, the word after its id; null when it isn't listed.</summary>
    public static string? Listed(string output, string id)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var at = Array.FindIndex(words, word => string.Equals(word, id, StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < words.Length)
                return words[at + 1];
        }
        return null;
    }

    // 3.13 will do for 3.13.7, and 16.0 for 16.0.1000.6, but not 3.1 for 3.13.
    private static bool Matches(string found, string wanted) =>
        found.Equals(wanted, StringComparison.OrdinalIgnoreCase)
        || found.StartsWith(wanted + ".", StringComparison.OrdinalIgnoreCase)
        || Version.TryParse(found, out var have) && Version.TryParse(wanted, out var want) && have == want;

    /// <summary>The software to install from a list such as sqlExpress,rabbitMq; missing for each that isn't there, all for every one.</summary>
    public static IReadOnlyList<SoftwareState> Picked(IReadOnlyList<SoftwareState> states, string which)
    {
        if (which.Equals("missing", StringComparison.OrdinalIgnoreCase))
            return [.. states.Where(state => state.Wanted)];
        if (which.Equals("all", StringComparison.OrdinalIgnoreCase))
            return states;
        var names = which.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var unknown = names.Where(name => !states.Any(state => state.Item.Name == name)).ToList();
        if (unknown.Count > 0)
            throw new SuiteException([$"There is no software called {string.Join(", ", unknown)} in suite.json. It is one of: {string.Join(", ", states.Select(state => state.Item.Name))}."]);
        return [.. states.Where(state => names.Contains(state.Item.Name, StringComparer.Ordinal))];
    }
}
