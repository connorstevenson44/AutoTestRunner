using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>A test case: the steps a person takes, ending at a checkpoint.</summary>
public sealed record Case(
    int Number, string Id, string Name, string File, IReadOnlyList<string> Needs, string? NeedsNote, string? Covers, JsonArray Steps);
