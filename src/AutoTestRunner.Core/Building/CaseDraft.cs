using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// A case being built: its name, the case it carries on from, its steps, and the checkpoint it ends at. A case opened
/// to change it has its own steps first, as written, and its checkpoint stays: the checks and things to look at
/// go into it.
/// </summary>
public sealed class CaseDraft
{
    public string Name { get; set; } = "";

    /// <summary>The ids of the cases it carries on from.</summary>
    public List<string> Needs { get; } = [];

    /// <summary>The id of the case it starts after the reset of, or null.</summary>
    public string? AfterReset { get; set; }

    /// <summary>
    /// The steps, in order: the case's own, then its reset, from the first step marked as where it starts. The builder records on one thread and the panel edits on another, so the list is changed
    /// under a lock on it.
    /// </summary>
    public List<DraftStep> Steps { get; } = [];

    public string CheckpointTitle { get; set; } = "";

    /// <summary>The checks to add to the checkpoint. The builder adds them for watched controls, so it is changed under a lock on it.</summary>
    public List<JsonObject> Checks { get; } = [];

    public List<string> Look { get; } = [];

    /// <summary>Controls named while building, to add to elements.json. Changed under a lock on it, as Steps is.</summary>
    public Dictionary<string, JsonNode> NewElements { get; } = new(StringComparer.Ordinal);

    /// <summary>The case opened to change it, or null for a new one.</summary>
    public Case? Editing { get; init; }

    /// <summary>The case files to mark as needing looking at again when it is saved, with why.</summary>
    public Dictionary<string, string> Review { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The controls to watch and clicks to follow, from the panel, to add to suite.json's builder.</summary>
    public List<WatchHook> NewWatch { get; } = [];

    /// <inheritdoc cref="NewWatch"/>
    public List<ClickHook> NewAfterClick { get; } = [];

    /// <summary>Macros made of steps while building, by name, to write to macros/ when the case is saved.</summary>
    public Dictionary<string, JsonObject> NewMacros { get; } = new(StringComparer.Ordinal);

    /// <summary>A case opened to change it: its steps up to its checkpoint, none of them played yet.</summary>
    public static CaseDraft Load(Case item)
    {
        var draft = new CaseDraft { Editing = item, Name = item.Name, AfterReset = item.AfterReset };
        draft.Needs.AddRange(item.Needs);
        for (var index = 0; index < item.Steps.Count; index++)
            if (item.Steps[index] is JsonObject step && (index < item.Steps.Count - 1 || step["checkpoint"] is null))
                draft.Steps.Add(StepMaker.Written(step.DeepClone().AsObject(), index));
        for (var index = 0; index < item.Reset.Count; index++)
        {
            if (item.Reset[index] is not JsonObject step)
                continue;
            var written = StepMaker.Written(step.DeepClone().AsObject(), null);
            written.Reset = index == 0;
            draft.Steps.Add(written);
        }
        return draft;
    }

    /// <summary>The case's own steps, and its reset steps: those from the first marked as where the reset starts.</summary>
    public (List<DraftStep> Own, List<DraftStep> Reset) Split()
    {
        lock (Steps)
        {
            var at = Steps.FindIndex(step => step.Reset);
            return at < 0 ? ([.. Steps], []) : ([.. Steps.Take(at)], [.. Steps.Skip(at)]);
        }
    }

    /// <summary>The checkpoint step it ends at, or null when nothing was said about one.</summary>
    public JsonObject? Checkpoint()
    {
        List<JsonObject> checks;
        lock (Checks)
            checks = [.. Checks];
        if (CheckpointTitle.Trim().Length == 0 && checks.Count == 0 && Look.Count == 0)
            return null;
        var step = new JsonObject { ["checkpoint"] = CheckpointTitle.Trim().Length > 0 ? CheckpointTitle.Trim() : Name };
        if (checks.Count > 0)
            step["checks"] = new JsonArray([.. checks.Select(check => check.DeepClone())]);
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
        lock (Checks)
            foreach (var check in Checks.Where(check => JsonNode.DeepEquals(check["shows"], selector)))
                check["shows"] = JsonValue.Create(name);
        return named;
    }
}
