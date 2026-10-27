using System.IO;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli;

/// <summary>What was asked for on the command line after the suite folder, as <see cref="CommandLine"/> reads it.</summary>
public sealed record Options
{
    public bool Unattended { get; init; }
    public string? Cases { get; init; }
    public Speed Speed { get; init; } = Speed.Modest;
    public string Mode { get; init; } = "auto";
    public string? Results { get; init; }
    public Dictionary<string, string> Variables { get; } = [];

    /// <summary>Only the cases with one of these tags; all of them when empty.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>launch: start the cases given straight away, as after restarting as administrator to run them.</summary>
    public bool Start { get; init; }

    /// <summary>record: the case to open and change, by number or id.</summary>
    public string? Edit { get; init; }

    /// <summary>record: the case whose end the new case starts from, by number or id.</summary>
    public string? After { get; init; }

    /// <summary>record: the case whose reset the new case starts after, by number or id.</summary>
    public string? AfterReset { get; init; }

    /// <summary>setup: the software to install, by name, or missing or all; null to offer what is missing.</summary>
    public string? Install { get; init; }

    /// <summary>setup: install without asking.</summary>
    public bool Yes { get; init; }

    /// <summary>setup: a file to add what it prints to, which the launcher shows as it is written.</summary>
    public string? Log { get; init; }

    /// <summary>record: the new case's name.</summary>
    public string? Name { get; init; }

    /// <summary>The options as arguments again, to pass on when autotest restarts itself.</summary>
    public IEnumerable<string> Arguments()
    {
        foreach (var (flag, value) in new[] { ("--cases", Cases), ("--mode", Mode), ("--speed", Speed.ToString()), ("--results", Results), ("--edit", Edit), ("--after", After), ("--after-reset", AfterReset), ("--name", Name) })
        {
            if (value is null)
                continue;
            yield return flag;
            yield return value;
        }
        foreach (var (name, value) in Variables)
        {
            yield return "--var";
            yield return $"{name}={value}";
        }
        foreach (var tag in Tags)
        {
            yield return "--tags";
            yield return tag;
        }
        if (Start)
            yield return "--start";
    }

    /// <summary>The suite's cases that --cases and --tags pick, in order.</summary>
    public IReadOnlyList<Case> Pick(Suite suite) => Suite.Tagged(suite.Select(Cases), Tags);

    /// <summary>The folder each run's results go in a folder of their own in: --results, or the suite's results folder.</summary>
    public string ResultsFolder(Suite suite) => Results ?? Path.Combine(suite.Folder, "results");
}
