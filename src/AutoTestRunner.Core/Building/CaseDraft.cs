using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// A case being built: its name, the case it carries on from, its steps, and the checkpoint it ends at. Adding to an
/// existing case, the steps go in before its checkpoint, and the checks and things to look at go into it.
/// </summary>
public sealed class CaseDraft
{
    public string Name { get; set; } = "";

    /// <summary>The ids of the cases it carries on from.</summary>
    public List<string> Needs { get; } = [];

    /// <summary>
    /// The steps, in order. The builder records on one thread and the panel edits on another, so the list is changed
    /// under a lock on it.
    /// </summary>
    public List<DraftStep> Steps { get; } = [];

    public string CheckpointTitle { get; set; } = "";

    public List<JsonObject> Checks { get; } = [];

    public List<string> Look { get; } = [];

    /// <summary>Controls named while building, to add to elements.json. Changed under a lock on it, as Steps is.</summary>
    public Dictionary<string, JsonNode> NewElements { get; } = new(StringComparer.Ordinal);

    /// <summary>The case being added to, or null for a new one.</summary>
    public Case? AddingTo { get; init; }

    /// <summary>The checkpoint step it ends at, or null when nothing was said about one.</summary>
    public JsonObject? Checkpoint()
    {
        if (CheckpointTitle.Trim().Length == 0 && Checks.Count == 0 && Look.Count == 0)
            return null;
        var step = new JsonObject { ["checkpoint"] = CheckpointTitle.Trim().Length > 0 ? CheckpointTitle.Trim() : Name };
        if (Checks.Count > 0)
            step["checks"] = new JsonArray([.. Checks.Select(check => check.DeepClone())]);
        if (Look.Count > 0)
            step["look"] = new JsonArray([.. Look.Select(line => (JsonNode?)JsonValue.Create(line))]);
        return step;
    }

    /// <summary>The suite's elements and those named while building, as the steps can use them.</summary>
    public Dictionary<string, JsonNode> ElementsWith(IReadOnlyDictionary<string, JsonNode> elements)
    {
        lock (NewElements)
            return new Dictionary<string, JsonNode>(elements.Concat(NewElements), StringComparer.Ordinal);
    }

    /// <summary>
    /// Names a control in elements.json, and has every step and check that found it the same way use the name. The
    /// name has to be new, and a target that is already an element keeps its name.
    /// </summary>
    public TargetChoice NameControl(TargetChoice target, string name, IReadOnlyDictionary<string, JsonNode> elements)
    {
        if (target.Json is not JsonObject selector || selector.ContainsKey("element"))
            throw new ArgumentException("That control already has a name in elements.json.", nameof(target));
        if (elements.ContainsKey(name) || NewElements.ContainsKey(name))
            throw new ArgumentException($"elements.json already has {name}.", nameof(name));
        lock (NewElements)
            NewElements[name] = selector.DeepClone();
        var named = new TargetChoice(JsonValue.Create(name), name, TargetQuality.Sturdy, $"Found by its name in elements.json, {name}.");
        lock (Steps)
        {
            foreach (var step in Steps.Where(step => step.Target is { } used && JsonNode.DeepEquals(used.Json, selector)))
            {
                step.Json[step.Verb] = JsonValue.Create(name);
                step.Target = named;
            }
        }
        foreach (var check in Checks.Where(check => JsonNode.DeepEquals(check["shows"], selector)))
            check["shows"] = JsonValue.Create(name);
        return named;
    }
}
