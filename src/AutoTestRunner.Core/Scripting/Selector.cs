namespace AutoTestRunner.Scripting;

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
