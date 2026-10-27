using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;
using Microsoft.Win32;

namespace AutoTestRunner.Cli;

/// <summary>
/// The .ps1 and .cs files that script steps run: a new one written from a short template in the suite's tools folder, and
/// any of them, or suite.json, opened to change, in Notepad++ when it is installed, else in Notepad.
/// </summary>
internal static partial class ScriptEditor
{
    private const string AppPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\notepad++.exe";

    /// <summary>Opens a script in Notepad++, or Notepad when there is no Notepad++. False when neither would start.</summary>
    public static bool Open(string path)
    {
        foreach (var editor in new[] { NotepadPlusPlus(), "notepad.exe" }.OfType<string>())
        {
            var start = new ProcessStartInfo(editor) { UseShellExecute = false };
            start.ArgumentList.Add(path);
            try
            {
                Process.Start(start)?.Dispose();
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not there, or not allowed to start: try the next one.
            }
        }
        return false;
    }

    /// <summary>Opens a folder in Explorer, saying so when it can't.</summary>
    public static void ShowFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            System.Windows.MessageBox.Show($"Couldn't open {folder}: {ex.Message}", "AutoTestRunner", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>Where Notepad++ is installed, from its App Paths entry or Program Files; null when it isn't.</summary>
    public static string? NotepadPlusPlus()
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(AppPath);
            if (key?.GetValue(null) is string found && File.Exists(found.Trim('"')))
                return found.Trim('"');
        }
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Notepad++", "notepad++.exe"))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// A new script in the suite's tools folder, named for what it does, for a case to run: Check-Orders.ps1, or
    /// CheckOrders.cs when it is C#. Returns its path relative to the suite folder. A script with that name already
    /// there is kept as it is.
    /// </summary>
    public static string Create(Suite suite, string name, string caseName, bool csharp = false)
    {
        var words = Words().Matches(name).Select(match => char.ToUpperInvariant(match.Value[0]) + match.Value[1..]).ToList();
        var file = csharp
            ? (words.Count == 0 ? "NewScript" : string.Concat(words)) + ".cs"
            : (words.Count == 0 ? "New-Script" : string.Join('-', words)) + ".ps1";
        var relative = "tools/" + file;
        var path = Path.Combine(suite.Folder, "tools", file);
        if (File.Exists(path))
            return relative;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // What it prints is the name, quoted as each language quotes a string.
        var said = name.Trim().Replace("\"", "'", StringComparison.Ordinal);
        if (csharp)
            said = said.Replace("\\", "\\\\", StringComparison.Ordinal);
        var text = csharp
            ? $"""
                // {name.Trim()}: run by a step of "{caseName}", as {"{"} "script": "{relative}" {"}"}.
                // It runs from the suite's folder with dotnet run, which needs the .NET 10 SDK, and gets the step's args as args.
                // What it prints goes in the run's log, and a step with "as" keeps it. An exit code other than 0, or an
                // exception, fails the step. A NuGet package is a line at the top, such as #:package Microsoft.Data.SqlClient@7.1.1

                Console.WriteLine("{said}");
                return 0;

                """
            : $"""
                # {name.Trim()}: run by a step of "{caseName}", as {"{"} "script": "{relative}" {"}"}.
                # It runs from the suite's folder, and gets the step's args as $args. What it prints goes in the run's log,
                # and a step with "as" keeps it. An exit code other than 0 fails the step.
                $ErrorActionPreference = 'Stop'

                Write-Output "{said}"

                """;
        // Windows PowerShell reads a file without a byte order mark as the machine's code page.
        File.WriteAllText(path, text.ReplaceLineEndings("\r\n"), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return relative;
    }

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Words();
}
