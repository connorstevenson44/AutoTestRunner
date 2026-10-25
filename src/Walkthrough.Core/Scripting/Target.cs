using System.Text.Json.Nodes;

namespace Walkthrough.Scripting;

/// <summary>How a selector's name and help text are compared with a control's.</summary>
public enum NameMatch { Exact, StartsWith, Contains }

/// <summary>
/// One way to find a control, with every ${...} already filled in. Id is the AutomationId; Type is a UI Automation
/// control type such as Button or Edit. Within limits the search to inside another control, and SameRowAs to controls
/// on the same row as another, such as the buttons beside a file's name. ScrollToFind scrolls the window's long lists
/// until the control is built, for rows of a list that only builds the rows in view.
/// </summary>
public sealed record Selector(
    string? Id = null,
    string? Name = null,
    string? Type = null,
    string? HelpText = null,
    string? ClassName = null,
    NameMatch Match = NameMatch.Exact,
    Target? Within = null,
    Target? SameRowAs = null,
    bool ScrollToFind = false)
{
    public bool Matches(string? id, string? name, string? type, string? helpText, string? className) =>
        (Id is null || Id == id)
        && (Type is null || string.Equals(Type, type, StringComparison.OrdinalIgnoreCase))
        && (ClassName is null || ClassName == className)
        && TextMatches(name ?? "", helpText ?? "");

    public bool NameMatches(string name) => Compare(name, Name!);

    /// <summary>Whether a control's name and help text are what the selector asks for, compared as match says.</summary>
    public bool TextMatches(string name, string helpText) =>
        (Name is null || Compare(name, Name)) && (HelpText is null || Compare(helpText, HelpText));

    private bool Compare(string found, string wanted) => Match switch
    {
        NameMatch.StartsWith => found.StartsWith(wanted, StringComparison.Ordinal),
        NameMatch.Contains => found.Contains(wanted, StringComparison.Ordinal),
        _ => found == wanted,
    };

    /// <summary>What a person would call it, as in "the Add button".</summary>
    public string Describe()
    {
        var noun = Type switch
        {
            null => "control",
            "Button" => "button",
            "Edit" => "box",
            "Text" => "text",
            "TabItem" => "tab",
            "ComboBox" => "drop-down",
            "CheckBox" => "tick box",
            "RadioButton" => "option",
            "ListItem" => "item",
            "Window" => "window",
            _ => Type,
        };
        if (Name is not null)
            return Type == "Text" ? $"\"{Name.TrimEnd()}\"{(Match == NameMatch.Exact ? "" : "…")}" : $"the {Name} {noun}";
        if (HelpText is not null)
            return $"the {noun} with the tip \"{HelpText}\"";
        if (Id is not null)
            return $"the {Id} {noun}";
        return $"the {noun}";
    }
}

/// <summary>A control to find: the selectors to try in order, and what to call it in a message.</summary>
public sealed record Target(IReadOnlyList<Selector> Selectors, string What)
{
    public static Target Of(Selector selector) => new([selector], selector.Describe());
}

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

    public static Target Resolve(JsonNode? node, Scope scope, IReadOnlyDictionary<string, JsonNode> elements, string? what = null)
    {
        var selectors = new List<Selector>();
        string? described = null;
        Add(node, scope, elements, selectors, ref described, 0);
        if (selectors.Count == 0)
            throw new ScriptException("The step doesn't say which control.");
        return new Target(selectors, what ?? described ?? selectors[0].Describe());
    }

    private static void Add(JsonNode? node, Scope scope, IReadOnlyDictionary<string, JsonNode> elements, List<Selector> selectors, ref string? described, int depth)
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

    private static void AddElement(string name, JsonObject? with, Scope scope, IReadOnlyDictionary<string, JsonNode> elements, List<Selector> selectors, ref string? described, int depth)
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

    private static Selector Parse(JsonObject node, Scope scope, IReadOnlyDictionary<string, JsonNode> elements)
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
