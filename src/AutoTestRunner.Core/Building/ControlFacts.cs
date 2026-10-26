using AutoTestRunner.Driving;

namespace AutoTestRunner.Building;

/// <summary>
/// What UI Automation says about a control the person used or pointed at: enough to write a selector that finds it
/// again and a step that uses it. Type is a UI Automation control type such as Button or Edit. Ancestors run from its
/// parent outwards, each with no ancestors of its own; a control in another of the app's windows, such as a dialog,
/// has that window last.
/// </summary>
public sealed record ControlFacts(string Type, string Name = "", string? Id = null, string? HelpText = null, string? ClassName = null)
{
    // Pointed at, the words or picture on one of these mean it.
    private static readonly HashSet<string> Wrappers = new(["Button", "CheckBox", "RadioButton", "TabItem", "MenuItem", "Hyperlink", "ComboBox", "SplitButton"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ControlFacts> Ancestors { get; init; } = [];

    /// <summary>What a text box holds, or a drop-down shows.</summary>
    public string? Value { get; init; }

    /// <summary>Whether a tick box is ticked, read just after it was clicked.</summary>
    public bool? Checked { get; init; }

    public bool IsPassword { get; init; }

    public ScreenRect Bounds { get; init; }

    /// <summary>Set when the control is in a Windows message box or file picker rather than one of the app's own windows.</summary>
    public DialogFacts? Dialog { get; init; }

    /// <summary>The ancestor at this place, with the ancestors beyond it as its own.</summary>
    public ControlFacts Up(int index) => Ancestors[index] with { Ancestors = Ancestors.Skip(index + 1).ToList() };

    /// <summary>The nearest ancestor of this type, within a few levels, or null.</summary>
    public ControlFacts? Nearest(string type, int levels = 4)
    {
        for (var index = 0; index < Math.Min(levels, Ancestors.Count); index++)
            if (string.Equals(Ancestors[index].Type, type, StringComparison.OrdinalIgnoreCase))
                return Up(index);
        return null;
    }

    /// <summary>The control a person means by pointing here: the button, tab or tick box around its words or picture, else this.</summary>
    public ControlFacts Meant()
    {
        if (Type is "Text" or "Image")
            for (var index = 0; index < Math.Min(2, Ancestors.Count); index++)
                if (Wrappers.Contains(Ancestors[index].Type))
                    return Up(index);
        return this;
    }

    /// <summary>What a person would call it in a sentence: its words, or its tooltip, else what it is.</summary>
    public string Label
    {
        get
        {
            var name = Name.Trim();
            if (name.Length is > 0 and <= 40)
                return name;
            if (HelpText?.Trim() is { Length: > 0 and <= 40 } tip)
                return tip;
            return name.Length > 0 ? name[..37].TrimEnd() + "…" : "the " + Type.ToLowerInvariant();
        }
    }
}
