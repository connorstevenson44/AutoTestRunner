using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Gives a step a run is stuck on the control picked for it instead: in elements.json when the step names an element,
/// as every step that uses it then finds it the new way, else in the case or macro file the step is written in. The
/// run tries the step again as it then is.
/// </summary>
public static class TargetFixer
{
    /// <summary>Where the stuck step's control is written, or null when it is inside a repeat or an if, or uses no control.</summary>
    public static TargetFix? Where(Suite suite, StuckStep stuck)
    {
        if (stuck.Target is JsonValue value && value.TryGetValue(out string? name) && suite.Elements.ContainsKey(name))
            return new TargetFix(Path.Combine(suite.Folder, "elements.json"), name, null);
        if (stuck.Target is null)
            return null;
        var files = new[] { stuck.Case?.File }.Concat(suite.Macros.Values.Select(macro => macro.File)).OfType<string>();
        foreach (var file in files)
        {
            var steps = ScriptJson.ReadFile(file)?["steps"] as JsonArray ?? [];
            for (var index = 0; index < steps.Count; index++)
                if (JsonNode.DeepEquals(steps[index], stuck.Step))
                    return new TargetFix(file, null, index);
        }
        return null;
    }

    /// <summary>Writes the picked control where the fix says, and has the run use it from now on.</summary>
    public static void Save(Suite suite, StuckStep stuck, TargetFix fix, JsonNode target)
    {
        var text = File.ReadAllText(fix.File);
        var newline = ScriptJson.NewLineOf(text);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (fix.Element is { } element)
        {
            var (start, end) = ElementSpan(bytes, element) ?? throw new InvalidOperationException($"elements.json has no {element}.");
            File.WriteAllText(fix.File, JsonLine.Splice(bytes, start, end, JsonLine.Write(target)));
            suite.Redefine(element, target);
            return;
        }
        var (from, to) = StepSpan(bytes, fix.Step ?? -1) ?? throw new InvalidOperationException($"{Path.GetFileName(fix.File)} has no step {fix.Step + 1}.");
        var changed = stuck.Step.DeepClone().AsObject();
        changed[stuck.Kind] = target.DeepClone();
        File.WriteAllText(fix.File, JsonLine.Splice(bytes, from, to, JsonLine.Step(changed, JsonLine.IndentAt(bytes, from), newline)));
        stuck.Step[stuck.Kind] = target.DeepClone();
    }

    // Where an element's value is in elements.json.
    private static (int Start, int End)? ElementSpan(byte[] bytes, string element)
    {
        var reader = new Utf8JsonReader(bytes, ScriptJson.Reader);
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1 || reader.GetString() != element)
                continue;
            reader.Read();
            var start = (int)reader.TokenStartIndex;
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
            {
                reader.Skip();
                return (start, (int)reader.BytesConsumed);
            }
            return (start, start + reader.ValueSpan.Length + (reader.TokenType == JsonTokenType.String ? 2 : 0));
        }
        return null;
    }

    // Where a top-level step is in a case or macro file, by its place among the steps.
    private static (int Start, int End)? StepSpan(byte[] bytes, int index)
    {
        var reader = new Utf8JsonReader(bytes, ScriptJson.Reader);
        var inSteps = false;
        var count = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
                inSteps = reader.GetString() == "steps";
            else if (inSteps && reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 2)
            {
                var start = (int)reader.TokenStartIndex;
                reader.Skip();
                if (count++ == index)
                    return (start, (int)reader.BytesConsumed);
            }
        }
        return null;
    }
}
