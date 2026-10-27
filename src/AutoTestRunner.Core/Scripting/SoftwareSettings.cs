using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// Software the suite needs on the computer, such as SQL Server Express or RabbitMQ, from software in suite.json: a
/// winget package, at a version or the latest; a Windows feature, such as MSMQ-Server; or a script that installs it,
/// given args. Present is a check that passes when it is there, for software winget doesn't know about. Nothing is
/// installed without asking.
/// </summary>
public sealed record SoftwareSettings(string Name, string Title)
{
    /// <summary>The winget package's id, such as Microsoft.SQLServer.2022.Express.</summary>
    public string? Winget { get; init; }

    /// <summary>The version wanted, such as 3.13: an installed version that starts with it will do. Null for any.</summary>
    public string? Version { get; init; }

    /// <summary>The Windows feature's name, as Enable-WindowsOptionalFeature takes it, such as MSMQ-Server.</summary>
    public string? WindowsFeature { get; init; }

    /// <summary>A .ps1 or .cs file that installs it, relative to the suite folder, and what it is given; ${version} is the version.</summary>
    public string? Script { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>A check, as a checkpoint has, that passes when it is installed, such as { "service": "RabbitMQ", "state": "Running" }.</summary>
    public JsonObject? Present { get; init; }

    /// <summary>Whether installing it needs administrator: a Windows feature does, and a script that says so.</summary>
    public bool Administrator { get; init; }

    /// <summary>How it is installed, in words.</summary>
    public string How => Winget is not null ? "winget" : WindowsFeature is not null ? "Windows feature" : "script";
}
