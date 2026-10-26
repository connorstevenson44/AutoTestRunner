using System.Text.Json.Nodes;

namespace AutoTestRunner.Building;

/// <summary>An element of elements.json that describes a control, with the parameters that make it that one.</summary>
public sealed record ElementMatch(string Name, IReadOnlyDictionary<string, string> With, int Score)
{
    /// <summary>The target as a step writes it: the element's name, or with its parameters.</summary>
    public JsonNode Json => With.Count == 0
        ? JsonValue.Create(Name)
        : new JsonObject { ["element"] = Name, ["with"] = new JsonObject(With.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)JsonValue.Create(pair.Value)))) };

    public string Shown => With.Count == 0 ? Name : $"{Name} {string.Join(" ", With.Values)}";
}
