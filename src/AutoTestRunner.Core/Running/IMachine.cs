namespace AutoTestRunner.Running;

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

    /// <summary>Runs a program and waits for it to exit.</summary>
    /// <param name="program">The program to run, by path or by name on the PATH.</param>
    /// <param name="arguments">Its arguments, each passed as one argument.</param>
    /// <param name="environment">Environment variables to set for it, on top of this process's own.</param>
    /// <param name="timeout">How long it may run before it is stopped; null for no limit.</param>
    /// <param name="output">Called with each line the program prints as it prints it, and whether it is an error.</param>
    ProcessOutput Run(string program, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null,
        Action<string, bool>? output = null);

    /// <summary>Runs a PowerShell script, passing arguments as $args, with errors stopping it.</summary>
    ProcessOutput PowerShell(string script, IReadOnlyList<string> arguments, TimeSpan? timeout = null, Action<string, bool>? output = null);

    /// <summary>Opens a folder in File Explorer, a file in Notepad, or starts a program, for a person to look at.</summary>
    void Open(string what, string path, IReadOnlyList<string>? arguments = null);
}
