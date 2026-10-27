using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// A helper that gives what a JSONPath finds first in JSON, such as appsettings.json read with readFile: a text as it
/// is, anything else as JSON.
/// </summary>
public sealed record JsonPathHelper(string Name, JsonPath Path) : Helper(Name)
{
    public override string Apply(string text)
    {
        JsonNode? root;
        try
        {
            root = ScriptJson.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ScriptException($"{Name} reads JSON, and what it was given isn't: {ex.Message}");
        }
        return Path.Find(root).FirstOrDefault() switch
        {
            null => "",
            JsonValue value when value.TryGetValue(out string? words) => words,
            var node => node.ToJsonString(),
        };
    }
}
