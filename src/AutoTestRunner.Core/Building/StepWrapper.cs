using System.Text.Json.Nodes;

namespace AutoTestRunner.Building;

/// <summary>Puts steps picked in the builder inside a repeat, or an if that runs them only when a check passes.</summary>
public static class StepWrapper
{
    /// <summary>
    /// A repeat of the steps for each item of a list, or each row of a data file when rows is given; each is
    /// ${item}, or ${name} when item names it.
    /// </summary>
    public static JsonObject Repeat(IReadOnlyList<JsonObject> steps, IReadOnlyList<string> items, string? rows, string? item)
    {
        var step = new JsonObject
        {
            ["repeat"] = rows is { Length: > 0 } file ? new JsonObject { ["rows"] = file } : new JsonArray([.. items.Select(each => (JsonNode?)JsonValue.Create(each))]),
        };
        if (item is { Length: > 0 } name && name != "item")
            step["item"] = name;
        step["steps"] = Copies(steps);
        return step;
    }

    /// <summary>An if that runs the steps when the check passes.</summary>
    public static JsonObject OnlyIf(IReadOnlyList<JsonObject> steps, JsonObject check) =>
        new() { ["if"] = check.DeepClone(), ["steps"] = Copies(steps) };

    /// <summary>The steps a repeat or an if holds, to take them out of it again.</summary>
    public static IReadOnlyList<JsonObject> Inside(JsonObject step) => [.. (step["steps"] as JsonArray ?? []).OfType<JsonObject>()];

    private static JsonArray Copies(IReadOnlyList<JsonObject> steps) => new([.. steps.Select(step => step.DeepClone())]);
}
