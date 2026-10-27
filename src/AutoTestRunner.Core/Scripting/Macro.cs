using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>A reusable list of steps with parameters, called with { "call": name, "with": { ... } }.</summary>
public sealed record Macro(string Name, string File, IReadOnlyDictionary<string, string?> Parameters, JsonArray Steps, string? Returns)
{
    /// <summary>The macro a macro file's JSON describes.</summary>
    public static Macro From(string name, string file, JsonObject macro) =>
        new(name, file, (macro["params"] as JsonObject ?? []).ToDictionary(pair => pair.Key, pair => pair.Value?["default"]?.ToString()),
            macro["steps"] as JsonArray ?? [], macro["returns"]?.GetValue<string>());
}
