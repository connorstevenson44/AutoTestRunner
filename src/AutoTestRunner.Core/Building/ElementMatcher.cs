using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Finds the elements in elements.json that describe a control, filling in their ${parameters} from what the control
/// says: a button whose tooltip is "Order 1001" is orderRow with number 1001. An element has to say more about the
/// control than its type to count, so one made only of parameters doesn't match everything of that type.
/// </summary>
public static partial class ElementMatcher
{
    /// <summary>The elements that describe the control, the most specific first.</summary>
    public static IReadOnlyList<ElementMatch> Matches(ControlFacts control, IReadOnlyDictionary<string, JsonNode> elements, VariableScope globals)
    {
        var found = new List<ElementMatch>();
        foreach (var (name, definition) in elements)
        {
            foreach (var alternative in Alternatives(definition))
            {
                var with = new Dictionary<string, string>(StringComparer.Ordinal);
                var score = Match(alternative, control, with, elements, globals, 0);
                if (score <= 0)
                    continue;
                found.Add(new ElementMatch(name, with, score));
                break;
            }
        }
        return [.. found.OrderByDescending(match => match.Score).ThenBy(match => match.With.Count).ThenBy(match => match.Name, StringComparer.Ordinal)];
    }

    private static IEnumerable<JsonObject> Alternatives(JsonNode? node) => node switch
    {
        JsonObject one => [one],
        JsonArray list => list.OfType<JsonObject>(),
        _ => [],
    };

    // How many literal characters of the selector the control matched; 0 when it doesn't match.
    private static int Match(JsonObject selector, ControlFacts control, Dictionary<string, string> with, IReadOnlyDictionary<string, JsonNode> elements, VariableScope globals, int depth)
    {
        if (depth > 5 || selector["element"] is not null || selector["sameRowAs"] is not null)
            return 0;
        string? Text(string key) => selector[key] is JsonValue value && value.TryGetValue(out string? text) ? text : null;

        var type = Text("type");
        var name = Text("name");
        foreach (var (shorthand, controlType) in Targets.Shorthands)
            if (Text(shorthand) is { } shortName)
                (type, name) = (controlType, shortName);
        if (type is not null && !string.Equals(type, control.Type, StringComparison.OrdinalIgnoreCase))
            return 0;
        var mode = Text("match") switch
        {
            "startsWith" => NameMatch.StartsWith,
            "contains" => NameMatch.Contains,
            _ => NameMatch.Exact,
        };

        var score = 0;
        foreach (var (template, actual, how) in new[]
        {
            (Text("id"), control.Id, NameMatch.Exact),
            (name, control.Name, mode),
            (Text("helpText"), control.HelpText, mode),
            (Text("className"), control.ClassName, NameMatch.Exact),
        })
        {
            if (template is null)
                continue;
            var literal = Fill(template, actual ?? "", how, with, globals);
            if (literal < 0)
                return 0;
            score += literal;
        }
        if (selector["within"] is { } within)
        {
            var around = Within(within, control, with, elements, globals, depth + 1);
            if (around <= 0)
                return 0;
            score += around;
        }
        return score;
    }

    // Whether one of the control's ancestors is what within names, binding its parameters; how well, or 0.
    private static int Within(JsonNode within, ControlFacts control, Dictionary<string, string> with, IReadOnlyDictionary<string, JsonNode> elements, VariableScope globals, int depth)
    {
        switch (within)
        {
            case JsonArray list:
                foreach (var item in list.OfType<JsonNode>())
                    if (Within(item, control, with, elements, globals, depth) is > 0 and var score)
                        return score;
                return 0;
            case JsonValue value when value.TryGetValue(out string? name) && elements.TryGetValue(name, out var definition):
                return Ancestor(control, Alternatives(definition), with, inner => true, elements, globals, depth);
            case JsonObject { } reference when reference["element"] is JsonValue element && elements.TryGetValue(element.GetValue<string>(), out var definition):
            {
                var map = reference["with"] as JsonObject ?? [];
                // The referenced element's parameters, filled from the ancestor, must fit the templates that pass them.
                return Ancestor(control, Alternatives(definition), with, inner => map.All(pair =>
                    inner.TryGetValue(pair.Key, out var given) && Fill(pair.Value?.ToString() ?? "", given, NameMatch.Exact, with, globals) >= 0), elements, globals, depth);
            }
            case JsonObject selector:
                return Ancestor(control, [selector], with, inner =>
                {
                    foreach (var (key, value) in inner)
                        with[key] = value;
                    return true;
                }, elements, globals, depth);
            default:
                return 0;
        }
    }

    private static int Ancestor(ControlFacts control, IEnumerable<JsonObject> selectors, Dictionary<string, string> with, Func<Dictionary<string, string>, bool> fits,
        IReadOnlyDictionary<string, JsonNode> elements, VariableScope globals, int depth)
    {
        foreach (var selector in selectors)
            for (var index = 0; index < control.Ancestors.Count; index++)
            {
                var inner = new Dictionary<string, string>(StringComparer.Ordinal);
                var score = Match(selector, control.Up(index), inner, elements, globals, depth);
                if (score <= 0)
                    continue;
                var before = new Dictionary<string, string>(with, StringComparer.Ordinal);
                if (fits(inner))
                    return score + 1;
                with.Clear();
                foreach (var (key, value) in before)
                    with[key] = value;
            }
        return 0;
    }

    /// <summary>
    /// Matches a text with ${...} in it against what a control says, binding each parameter it fills; the suite's own
    /// variables are filled in first. Returns how many literal characters matched, or -1 when it doesn't match.
    /// </summary>
    public static int Fill(string template, string actual, NameMatch match, Dictionary<string, string> with, VariableScope globals)
    {
        var pattern = new StringBuilder(match == NameMatch.Contains ? "" : "^");
        var groups = new List<string>();
        var literal = 0;
        var index = 0;
        foreach (Match part in Parameter().Matches(template))
        {
            literal += Literal(template[index..part.Index], pattern);
            var expression = part.Groups["expression"].Value.Trim();
            if (ScriptVocabulary.IsName(expression) && with.TryGetValue(expression, out var bound))
                literal += Literal(bound, pattern);
            else if (ScriptVocabulary.IsName(expression) && !globals.TryGet(expression, out _))
            {
                groups.Add(expression);
                pattern.Append(CultureInfo.InvariantCulture, $"(?<p{groups.Count - 1}>.*?)");
            }
            else if (Known(globals, part.Value) is { } known)
                literal += Literal(known, pattern);
            else
                pattern.Append(".*?");
            index = part.Index + part.Length;
        }
        literal += Literal(template[index..], pattern);
        if (match == NameMatch.Exact)
            pattern.Append('$');
        var found = Regex.Match(actual, pattern.ToString(), RegexOptions.Singleline, TimeSpan.FromSeconds(1));
        if (!found.Success)
            return -1;
        for (var group = 0; group < groups.Count; group++)
        {
            var value = found.Groups[$"p{group}"].Value;
            // A parameter left empty means the control doesn't have what the element finds it by.
            if (value.Length == 0 || with.TryGetValue(groups[group], out var earlier) && earlier != value)
                return -1;
            with[groups[group]] = value;
        }
        return literal;
    }

    private static int Literal(string text, StringBuilder pattern)
    {
        pattern.Append(Regex.Escape(text));
        return text.Length;
    }

    private static string? Known(VariableScope globals, string expression)
    {
        try
        {
            return globals.Expand(expression);
        }
        catch (ScriptException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<!\$)\$\{(?<expression>[^}]*)\}")]
    private static partial Regex Parameter();

}
