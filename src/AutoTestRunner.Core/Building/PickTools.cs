namespace AutoTestRunner.Building;

/// <summary>The builder's pick tools: each of what can be done with a control in the app, chosen before the control is.</summary>
public static class PickTools
{
    public static readonly PickTool Click = new(PickAction.Click, "Click", "click it", "Click it now, and add the step.");

    public static readonly PickTool Hover = new(PickAction.Hover, "Point at", "point at it", "Move the mouse onto it, for buttons that only show while the mouse is over them.");

    public static readonly PickTool WaitFor = new(PickAction.WaitFor, "Wait for it to show", "wait for it to show", "Wait until it appears.");

    public static readonly PickTool WaitGone = new(PickAction.WaitGone, "Wait for it to go", "wait for it to go", "Wait until it disappears, such as a progress bar.");

    public static readonly PickTool WaitText = new(PickAction.WaitText, "Wait for its text", "wait for its text", "Wait until it says what it says now.");

    public static readonly PickTool WaitEnabled = new(PickAction.WaitEnabled, "Wait until it can be used", "wait until it can be used", "Wait until it is there and not greyed out.");

    public static readonly PickTool Keep = new(PickAction.Keep, "Keep its text", "keep its text", "Save what it says in a variable, for a check or a later step.");

    public static readonly PickTool CheckShows = new(PickAction.CheckShows, "Check it shows", "check it shows", "Check at the checkpoint that it is there.");

    public static readonly PickTool CheckText = new(PickAction.CheckText, "Check its text", "check its text", "Check at the checkpoint that it says what it says now.");

    public static readonly PickTool SetValue = new(PickAction.SetValue, "Set its value…", "set its value", "Set a slider's, a number box's or a date picker's value in one go.");

    public static readonly PickTool Name = new(PickAction.Name, "Name it", "name it", "Give it a name in elements.json, which the steps then use.");

    public static readonly PickTool Watch = new(PickAction.Watch, "Watch it", "watch it",
        "When a step changes its text, add a wait for the new text. Saved in suite.json, for this case and new ones.");

    public static readonly PickTool AfterClick = new(PickAction.AfterClick, "After clicking it…", "add a macro after each click on it",
        "Choose a macro to add as a step after each click on it while recording. Saved in suite.json.");

    /// <summary>
    /// The checks of a control that have a form of their own, such as that it can be used or how many rows it has: a
    /// tool each, which opens the form with the control in it. That it shows is CheckShows, which needs no form.
    /// </summary>
    public static IReadOnlyList<PickTool> Checks { get; } =
    [
        .. Forms.CheckKinds.Where(kind => Scripting.ScriptVocabulary.ControlChecks.Contains(kind.Key) && kind.Key != "shows").Select(kind =>
        {
            // "It can be used" reads as "Check it can be used", "Number of rows" as "Check its number of rows".
            var words = char.ToLowerInvariant(kind.Title[0]) + kind.Title[1..];
            var title = "Check " + (words.StartsWith("it ", StringComparison.Ordinal) || words.StartsWith("it's ", StringComparison.Ordinal) ? words : "its " + words);
            return new PickTool(PickAction.Check, title + "…", char.ToLowerInvariant(title[0]) + title[1..], "Check at the checkpoint, from a form with the control filled in.", kind.Key);
        }),
    ];
}
