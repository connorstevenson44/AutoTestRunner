using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace AutoTestRunner.Cli;

/// <summary>
/// Running as administrator, which a suite that installs services needs, and so does driving an app that runs as
/// administrator. autotest restarts itself elevated with the same arguments, in the same folder.
/// </summary>
internal static class Elevation
{
    public static bool IsAdministrator =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts autotest again as administrator with these arguments. False when that was declined.</summary>
    public static bool Restart(IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.CurrentDirectory };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        try
        {
            Process.Start(start);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
