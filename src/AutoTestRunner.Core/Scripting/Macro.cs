using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>A reusable list of steps with parameters, called with { "call": name, "with": { ... } }.</summary>
public sealed record Macro(string Name, string File, IReadOnlyDictionary<string, string?> Parameters, JsonArray Steps, string? Returns);
