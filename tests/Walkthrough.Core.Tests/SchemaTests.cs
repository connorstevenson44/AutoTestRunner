using System.Text.Json.Nodes;
using Walkthrough.Scripting;

namespace Walkthrough.Core.Tests;

/// <summary>The schemas give editors completion and errors; these keep them saying what the runner accepts.</summary>
public class SchemaTests
{
    private static readonly JsonObject Definitions = JsonNode.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "schema", "walkthrough.case.schema.json")))!["definitions"]!.AsObject();

    private static HashSet<string> Properties(string definition) =>
        Definitions[definition]!["properties"]!.AsObject().Select(pair => pair.Key).ToHashSet();

    [Fact]
    public void Every_step_in_the_vocabulary_is_in_the_schema_with_the_same_properties()
    {
        foreach (var (kind, step) in Vocabulary.Steps)
            Assert.Equal(new[] { kind }.Concat(step.Keys).Concat(Vocabulary.CommonStepKeys).Order(StringComparer.Ordinal), Properties(kind + "Step").Order(StringComparer.Ordinal));
        Assert.Equal(Vocabulary.Steps.Count + 1, Definitions["step"]!["anyOf"]!.AsArray().Count);
    }

    [Fact]
    public void Every_check_and_tool_is_in_the_schema_with_the_same_properties()
    {
        foreach (var (kind, keys) in Vocabulary.Checks)
            Assert.Equal(new[] { kind }.Concat(keys).Concat(Vocabulary.CommonCheckKeys).Order(StringComparer.Ordinal), Properties(kind + "Check").Order(StringComparer.Ordinal));
        foreach (var (kind, keys) in Vocabulary.Tools)
            Assert.Equal(new[] { kind, "label" }.Concat(keys).Order(StringComparer.Ordinal), Properties(kind + "Tool").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Selectors_take_the_same_properties_in_the_schema_and_the_runner() =>
        Assert.Equal(Targets.SelectorKeys.Order(StringComparer.Ordinal), Properties("selector").Order(StringComparer.Ordinal));
}
