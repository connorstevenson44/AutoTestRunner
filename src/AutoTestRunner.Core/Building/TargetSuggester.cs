using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Picks how a recorded step finds its control: the most lasting way that finds only that control. An element of
/// elements.json comes first, then the AutomationId, then the control's words or tooltip, then either of those inside
/// the nearest control around it that can be named. Whether a way finds only one control is asked of the app itself,
/// through count, which says how many controls on screen a target finds.
/// </summary>
public static class TargetSuggester
{
    private const string GiveItAnId = "Giving the control an x:Name or AutomationId in the app keeps the test working when its words change.";

    public static TargetChoice Suggest(ControlFacts control, IReadOnlyDictionary<string, JsonNode> elements, VariableScope globals, Func<JsonNode, int> count)
    {
        var named = ElementMatcher.Matches(control, elements, globals);
        foreach (var match in named)
            if (count(match.Json) == 1)
                return new TargetChoice(match.Json, match.Shown, TargetQuality.Sturdy, $"Found by its name in elements.json, {match.Name}.");

        // An element that is only the way found here is that element, even when the app answered differently when
        // asked about it a moment before, as a drop-down closing can make it.
        var own = Own(control).ToList();
        foreach (var choice in own)
        {
            if (count(choice.Json) != 1)
                continue;
            var same = named.FirstOrDefault(match => match.With.Count == 0 && elements.TryGetValue(match.Name, out var definition) && JsonNode.DeepEquals(definition, choice.Json));
            return same is null ? choice : new TargetChoice(same.Json, same.Shown, TargetQuality.Sturdy, $"Found by its name in elements.json, {same.Name}.");
        }

        // Inside the nearest control around it that can be named, such as a list's row or another window.
        var inner = own.Count > 0 ? own : [new TargetChoice(new JsonObject { ["type"] = control.Type }, control.Type.ToLowerInvariant(), TargetQuality.Wording, "")];
        for (var index = 0; index < Math.Min(control.Ancestors.Count, 8); index++)
        {
            if (Scope(control.Up(index), elements, globals, count) is not { } scope)
                continue;
            foreach (var choice in inner)
            {
                var json = (JsonObject)choice.Json.DeepClone();
                json["within"] = scope.Json.DeepClone();
                if (count(json) != 1)
                    continue;
                var sturdy = choice.Quality == TargetQuality.Sturdy && scope.Quality == TargetQuality.Sturdy;
                return new TargetChoice(json, $"{choice.Shown} in {scope.Shown}", sturdy ? TargetQuality.Sturdy : TargetQuality.Wording,
                    sturdy ? $"Found by its id inside {scope.Shown}." : $"Found by its words inside {scope.Shown}. {GiveItAnId}");
            }
        }

        var first = named.Count > 0
            ? new TargetChoice(named[0].Json, named[0].Shown, TargetQuality.Ambiguous, "")
            : inner[0];
        var many = count(first.Json);
        return first with
        {
            Quality = TargetQuality.Ambiguous,
            Why = (many > 1 ? $"{many} controls match this, and the first is used. " : "This doesn't find the control again by itself. ") + GiveItAnId,
        };
    }

    /// <summary>
    /// How sturdy a target written in a case is, as the panel shows it for a step of a case opened to change it: by a
    /// name in elements.json or an AutomationId is sturdy, by words or a tooltip breaks when they change.
    /// </summary>
    public static TargetChoice Describe(JsonNode target)
    {
        switch (target)
        {
            case JsonValue value when value.TryGetValue(out string? name):
                return new TargetChoice(target, name, TargetQuality.Sturdy, $"Found by its name in elements.json, {name}.");
            case JsonArray { Count: > 0 } list:
                return Describe(list[0]!) with { Json = target };
            case JsonObject selector when selector["element"]?.GetValue<string>() is { } element:
            {
                var with = (selector["with"] as JsonObject ?? []).Select(pair => pair.Value?.ToString());
                return new TargetChoice(target, string.Join(' ', [element, .. with]), TargetQuality.Sturdy, $"Found by its name in elements.json, {element}.");
            }
            case JsonObject selector:
            {
                var inside = selector["within"] is { } within ? Describe(within) : null;
                var id = selector["id"]?.ToString();
                var shorthand = Targets.Shorthands.Keys.FirstOrDefault(selector.ContainsKey);
                var words = shorthand is not null ? $"{shorthand} \"{selector[shorthand]}\""
                    : selector["name"] is { } name ? $"{selector["type"]?.ToString() ?? "control"} \"{name}\""
                    : selector["helpText"] is { } tip ? $"{selector["type"]?.ToString() ?? "control"} with tip \"{tip}\""
                    : selector["type"]?.ToString() ?? "a control";
                var shown = (id is not null ? $"id {id}" : words) + (inside is null ? "" : $" in {inside.Shown}");
                var sturdy = id is not null && inside is not { Quality: not TargetQuality.Sturdy };
                return new TargetChoice(target, shown, sturdy ? TargetQuality.Sturdy : TargetQuality.Wording,
                    sturdy ? "Found by its AutomationId." : $"Found by its words. {GiveItAnId}");
            }
            default:
                return new TargetChoice(target, JsonLine.Write(target), TargetQuality.Ambiguous, "");
        }
    }

    /// <summary>The ways to find a control by what it says itself: its AutomationId, its words, its tooltip.</summary>
    private static IEnumerable<TargetChoice> Own(ControlFacts control)
    {
        if (!string.IsNullOrEmpty(control.Id))
            yield return new TargetChoice(new JsonObject { ["id"] = control.Id }, $"id {control.Id}", TargetQuality.Sturdy, $"Found by its AutomationId, {control.Id}.");
        var noun = Noun(control.Type);
        if (control.Name.Length > 0)
        {
            var json = Shorthand(control.Type) is { } shorthand
                ? new JsonObject { [shorthand] = control.Name }
                : new JsonObject { ["type"] = control.Type, ["name"] = control.Name };
            yield return new TargetChoice(json, $"{noun} \"{control.Name}\"", TargetQuality.Wording, $"Found by its words. {GiveItAnId}");
        }
        if (!string.IsNullOrEmpty(control.HelpText))
            yield return new TargetChoice(new JsonObject { ["type"] = control.Type, ["helpText"] = control.HelpText }, $"{noun} with tip \"{control.HelpText}\"",
                TargetQuality.Wording, $"Found by its tooltip. {GiveItAnId}");
    }

    // A control around another that finds only itself: another window by its title, or a row or panel by what names it.
    private static TargetChoice? Scope(ControlFacts around, IReadOnlyDictionary<string, JsonNode> elements, VariableScope globals, Func<JsonNode, int> count)
    {
        if (around.Type == "Window")
            return around.Name.Length > 0
                ? new TargetChoice(new JsonObject { ["window"] = around.Name }, $"the {around.Name} window", TargetQuality.Sturdy, "")
                : null;
        foreach (var match in ElementMatcher.Matches(around, elements, globals))
            if (count(match.Json) == 1)
                return new TargetChoice(match.Json, match.Shown, TargetQuality.Sturdy, "");
        foreach (var choice in Own(around))
            if (count(choice.Json) == 1)
                return choice;
        return null;
    }

    private static string? Shorthand(string type) =>
        Targets.Shorthands.FirstOrDefault(pair => string.Equals(pair.Value, type, StringComparison.OrdinalIgnoreCase)).Key;

    private static string Noun(string type) => Shorthand(type) ?? type;
}
