using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>A test case: the steps a person takes, ending at a checkpoint.</summary>
public sealed record Case(
    int Number, string Id, string Name, string File, IReadOnlyList<string> Needs, string? NeedsNote, string? Covers, JsonArray Steps)
{
    /// <summary>Why the case needs looking at again, such as a change to a case it carries on from; null when it doesn't.</summary>
    public string? Review { get; init; }

    /// <summary>Words to pick cases by, such as smoke.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
