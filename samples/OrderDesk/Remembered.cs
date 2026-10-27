using Microsoft.Win32;

namespace OrderDesk;

/// <summary>What Order Desk remembers for the person using it, under HKEY_CURRENT_USER\Software\OrderDesk.</summary>
public static class Remembered
{
    private const string Key = @"Software\OrderDesk";

    /// <summary>Who signed in last, to fill in the sign-in window.</summary>
    public static string? LastUser
    {
        get => Read("LastUser");
        set => Write("LastUser", value);
    }

    /// <summary>The folder Export orders writes to, once one has been picked.</summary>
    public static string? ExportFolder
    {
        get => Read("ExportFolder");
        set => Write("ExportFolder", value);
    }

    private static string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(name) as string;
    }

    private static void Write(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (value is null)
            key.DeleteValue(name, throwOnMissingValue: false);
        else
            key.SetValue(name, value);
    }
}
