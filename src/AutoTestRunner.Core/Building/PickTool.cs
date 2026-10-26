using System.Text.Json.Nodes;

namespace AutoTestRunner.Building;

/// <summary>
/// A pick tool in the builder's toolbar: chosen first, it is used on the control clicked next in the app. Title names
/// it, Doing finishes "Click a control to …", and Tip says what it adds. A Check tool opens the form of the check
/// whose key is Check, with the control in it.
/// </summary>
public sealed record PickTool(PickAction Action, string Title, string Doing, string Tip, string? Check = null)
{
    // The controls a count of rows is read from, and those a tick is read from.
    private static readonly HashSet<string> Lists = new(["DataGrid", "List", "Tree", "Table"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Toggles = new(["CheckBox", "Button"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Why the tool can't be used on this control, or null when it can.</summary>
    public string? Refusal(ControlFacts control, TargetChoice choice) => Action switch
    {
        PickAction.WaitText or PickAction.Keep or PickAction.CheckText when TextOf(control).Length == 0 => "it shows no text.",
        PickAction.Check when Check == "says" && TextOf(control).Length == 0 => "it shows no text.",
        PickAction.Check when Check is "ticked" or "notTicked" && !Toggles.Contains(control.Type) => "it isn't a tick box.",
        PickAction.Check when Check == "rows" && !Lists.Contains(control.Type) => "it isn't a list, a grid or a tree.",
        PickAction.Name when choice.Json is not JsonObject selector || selector.ContainsKey("element") => "it already has a name in elements.json.",
        PickAction.Watch or PickAction.AfterClick when choice.Json is JsonArray => "it is found more than one way: use a control found one way.",
        _ => null,
    };

    /// <summary>What a control says, as a wait or a check of its text reads it: what a box holds, else its words.</summary>
    public static string TextOf(ControlFacts control) => control.Value is { Length: > 0 } value ? value : control.Name;
}
