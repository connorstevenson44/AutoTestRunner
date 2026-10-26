using System.Text.RegularExpressions;

namespace AutoTestRunner.Building;

/// <summary>
/// The controls in the app a test would use that have no AutomationId, with the line of XAML that gives each one,
/// as Microsoft recommends for UI Automation: a step then finds the control by its id however its words change.
/// </summary>
public static partial class IdReport
{
    // The controls a person uses, which a step finds; text and pictures are found through what they are on.
    private static readonly HashSet<string> Used = new(
        ["Button", "Edit", "ComboBox", "CheckBox", "RadioButton", "TabItem", "MenuItem", "Slider", "Spinner", "Hyperlink", "DataGrid", "List", "Tree", "Document", "SplitButton", "Table"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>The controls without an id, those with nothing to find them by first, each once.</summary>
    public static IReadOnlyList<IdGap> Gaps(IEnumerable<ControlFacts> controls) =>
        [.. controls
            .Where(control => Used.Contains(control.Type) && string.IsNullOrEmpty(control.Id))
            .DistinctBy(control => (control.Type, control.Name, control.Bounds))
            .Select(control => new IdGap(control, control.Name.Trim().Length > 0, Xaml(control)))
            .OrderBy(gap => gap.Named)];

    /// <summary>The attribute that gives a control an id, named after its words and what it is, such as AddOrderButton.</summary>
    public static string Xaml(ControlFacts control)
    {
        var words = Word().Matches(control.Name).Select(match => match.Value).Take(4).Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
        var name = string.Concat(words);
        if (name.Length > 0 && char.IsDigit(name[0]))
            name = "_" + name;
        return $"AutomationProperties.AutomationId=\"{name}{control.Type}\"";
    }

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Word();
}
