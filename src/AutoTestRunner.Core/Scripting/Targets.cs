using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// Turns a step's target into selectors. A target is the name of an element from elements.json, a selector object,
/// or a list of either to try in order. A selector object can use a shorthand for type and name together:
/// <c>{ "button": "Add" }</c> is <c>{ "type": "Button", "name": "Add" }</c>.
/// </summary>
public static class Targets
{
    public static readonly IReadOnlyDictionary<string, string> Shorthands = new Dictionary<string, string>
    {
        ["button"] = "Button",
        ["text"] = "Text",
        ["edit"] = "Edit",
        ["tabItem"] = "TabItem",
        ["comboBox"] = "ComboBox",
        ["checkBox"] = "CheckBox",
        ["radioButton"] = "RadioButton",
        ["listItem"] = "ListItem",
        ["list"] = "List",
        ["window"] = "Window",
    };

    public static readonly IReadOnlySet<string> SelectorKeys = new HashSet<string>(
        ["element", "with", "id", "name", "type", "helpText", "className", "match", "within", "sameRowAs", "scrollToFind", .. Shorthands.Keys]);

    public static Target Resolve(JsonNode? node, VariableScope scope, IReadOnlyDictionary<string, JsonNode> elements, string? what = null)
    {
        var selectors = new List<Selector>();
        string? described = null;
        Add(node, scope, elements, selectors, ref described, 0);
        if (selectors.Count == 0)
            throw new ScriptException("The step doesn't say which control.");
        return new Target(selectors, what ?? described ?? selectors[0].Describe());
    }

    private static void Add(JsonNode? node, VariableScope scope, IReadOnlyDictionary<string, JsonNode> elements, List<Selector> selectors, ref string? described, int depth)
    {
        if (depth > 10)
            throw new ScriptException("Elements refer to each other in a loop.");
        switch (node)
        {
            case JsonArray list:
                foreach (var item in list)
                    Add(item, scope, elements, selectors, ref described, depth + 1);
                break;
            case JsonValue value when value.TryGetValue(out string? name):
                AddElement(scope.Expand(name), null, scope, elements, selectors, ref described, depth);
                break;
            case JsonObject selector when selector["element"] is JsonValue element:
                AddElement(scope.Expand(element.GetValue<string>()), selector["with"] as JsonObject, scope, elements, selectors, ref described, depth);
                break;
            case JsonObject selector:
                selectors.Add(Parse(selector, scope, elements));
                break;
            default:
                throw new ScriptException($"A target is an element's name, a selector or a list of them, not {node?.ToJsonString() ?? "nothing"}.");
        }
    }

    private static void AddElement(string name, JsonObject? with, VariableScope scope, IReadOnlyDictionary<string, JsonNode> elements, List<Selector> selectors, ref string? described, int depth)
    {
        if (!elements.TryGetValue(name, out var definition))
            throw new ScriptException($"There is no element called {name} in elements.json.");
        var inner = scope;
        if (with is not null)
        {
            inner = scope.Child();
            foreach (var (key, value) in with)
                inner.Set(key, scope.Expand(value?.ToString() ?? ""));
        }
        described ??= $"the {Words(name)}";
        Add(definition, inner, elements, selectors, ref described, depth + 1);
    }

    private static Selector Parse(JsonObject node, VariableScope scope, IReadOnlyDictionary<string, JsonNode> elements)
    {
        foreach (var (key, _) in node)
            if (!SelectorKeys.Contains(key))
                throw new ScriptException($"A selector has no \"{key}\". It can have: {string.Join(", ", SelectorKeys)}.");

        string? Text(string key) => node[key] is JsonNode value ? scope.Expand(value.GetValue<string>()) : null;

        var type = Text("type");
        var name = Text("name");
        foreach (var (shorthand, controlType) in Shorthands)
            if (Text(shorthand) is { } shortName)
                (type, name) = (controlType, shortName);

        var match = Text("match") switch
        {
            null or "exact" => NameMatch.Exact,
            "startsWith" => NameMatch.StartsWith,
            "contains" => NameMatch.Contains,
            var other => throw new ScriptException($"match is exact, startsWith or contains, not {other}."),
        };
        return new Selector(
            Id: Text("id"),
            Name: name,
            Type: type,
            HelpText: Text("helpText"),
            ClassName: Text("className"),
            Match: match,
            Within: node["within"] is { } within ? Resolve(within, scope, elements) : null,
            SameRowAs: node["sameRowAs"] is { } row ? Resolve(row, scope, elements) : null,
            ScrollToFind: node["scrollToFind"]?.GetValue<bool>() ?? false);
    }

    // "newInstallName" as "new install name".
    private static string Words(string name) =>
        string.Concat(name.Select((c, i) => char.IsUpper(c) && i > 0 ? " " + char.ToLowerInvariant(c) : c.ToString()));
}
