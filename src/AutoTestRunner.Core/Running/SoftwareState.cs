using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>Whether a suite's software is on the computer.</summary>
public enum SoftwareStatus
{
    /// <summary>There, at the version wanted.</summary>
    Installed,

    /// <summary>Not there.</summary>
    Missing,

    /// <summary>There, at another version than the one wanted.</summary>
    OtherVersion,

    /// <summary>It couldn't be told, such as with no winget on the computer: Detail says why.</summary>
    Unknown,
}

/// <summary>What was found of a suite's software: its status, the version there when known, and why it couldn't be told.</summary>
public sealed record SoftwareState(SoftwareSettings Item, SoftwareStatus Status, string? Found = null, string? Detail = null)
{
    /// <summary>Whether installing would change something: it is missing, or at another version.</summary>
    public bool Wanted => Status is SoftwareStatus.Missing or SoftwareStatus.OtherVersion;

    /// <summary>One line on it, for the console and a tooltip: SQL Server Express: installed, 16.0.1000.6.</summary>
    public string Line => Item.Title + ": " + Status switch
    {
        SoftwareStatus.Installed => Found is null ? "installed" : $"installed, {Found}",
        SoftwareStatus.Missing => Item.Version is null ? "not installed" : $"not installed, wants {Item.Version}",
        SoftwareStatus.OtherVersion => $"{Found} installed, wants {Item.Version}",
        _ => $"can't tell: {Detail}",
    };
}
