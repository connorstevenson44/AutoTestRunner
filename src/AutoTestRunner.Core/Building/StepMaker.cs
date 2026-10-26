using System.Text.Json.Nodes;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Writes the steps a recording makes, each saying what it does the way the cases written by hand do: "Click Add
/// order", "Type Ada Lovelace in Customer", "Pick Coffee beans from Product".
/// </summary>
public static class StepMaker
{
    public static DraftStep StartApp(Suite suite) =>
        new(new JsonObject { ["startApp"] = new JsonObject(), ["say"] = $"Start {suite.App.Window}" }, ActivityKind.App);

    public static DraftStep Click(TargetChoice target, ControlFacts control) =>
        Make("click", target, control, $"Click {control.Label}", ActivityKind.Click);

    public static DraftStep Type(TargetChoice target, ControlFacts control, string text)
    {
        if (control.IsPassword)
        {
            var secret = Make("type", target, control, $"Type the password in {control.Label}", ActivityKind.Type);
            secret.Json["text"] = "${password}";
            secret.Json["secret"] = true;
            secret.Json["note"] = "A password box can't be read: give the password with autotest run --var password=…";
            return secret;
        }
        var step = Make("type", target, control, text.Length is > 0 and <= 40 && !text.Contains('\n') ? $"Type {text} in {control.Label}" : $"Type in {control.Label}", ActivityKind.Type);
        step.Json["text"] = text;
        return step;
    }

    public static DraftStep Choose(TargetChoice target, ControlFacts control, string option)
    {
        var step = Make("choose", target, control, $"Pick {option} from {control.Label}", ActivityKind.Choose);
        step.Json["option"] = option;
        return step;
    }

    public static DraftStep Tick(TargetChoice target, ControlFacts control, bool isChecked)
    {
        var step = Make("tick", target, control, $"{(isChecked ? "Tick" : "Clear")} {control.Label}", ActivityKind.Choose);
        if (!isChecked)
            step.Json["checked"] = false;
        return step;
    }

    public static DraftStep Tab(string name) => new(new JsonObject { ["tab"] = name, ["say"] = $"Open the {name} tab" }, ActivityKind.Click);

    public static DraftStep Key(string keys) => new(new JsonObject { ["key"] = keys, ["say"] = $"Press {keys}" }, ActivityKind.Key);

    public static DraftStep AnswerDialog(DialogFacts dialog, string button)
    {
        var json = new JsonObject { ["answerDialog"] = dialog.Title, ["button"] = button };
        if (FirstSentence(dialog.Message) is { Length: > 0 } expect)
            json["expect"] = expect;
        json["say"] = $"Click {button}";
        return new DraftStep(json, ActivityKind.Dialog);
    }

    public static DraftStep PickFile(DialogFacts dialog) =>
        new(new JsonObject
        {
            [dialog.PicksFolder ? "pickFolder" : "pickFile"] = dialog.Title,
            ["path"] = dialog.Path,
            ["say"] = $"Pick {System.IO.Path.GetFileName(dialog.Path?.TrimEnd('\\', '/')) ?? dialog.Path}",
        }, ActivityKind.File);

    public static DraftStep Hover(TargetChoice target, ControlFacts control) =>
        Make("hover", target, control, $"Point at {control.Label}", ActivityKind.Point);

    public static DraftStep Find(TargetChoice target, ControlFacts control) =>
        Make("find", target, control, $"{Capital(control.Label)} is there", ActivityKind.Point);

    public static DraftStep WaitGone(TargetChoice target, ControlFacts control) =>
        Make("waitGone", target, control, $"{Capital(control.Label)} has gone", ActivityKind.Point);

    public static DraftStep WaitText(TargetChoice target, ControlFacts control, string text) =>
        new(new JsonObject { ["waitText"] = target.Json.DeepClone(), ["startsWith"] = StartOf(text) }, ActivityKind.Point, target, control);

    /// <summary>Keeps what a control says in a variable, for a check at the checkpoint.</summary>
    public static DraftStep Keep(TargetChoice target, ControlFacts control, string name)
    {
        var step = Make("find", target, control, $"Keep what it says as {name}: ${{{name}}}", ActivityKind.Point);
        step.Json["as"] = name;
        return step;
    }

    /// <summary>
    /// Waits for the app's status text to say what it said after the last click, through the suite's waitForStatusBar
    /// macro when it has one, so the case waits for the app rather than for a time.
    /// </summary>
    public static DraftStep? StatusWait(Suite suite, string text)
    {
        var start = StartOf(text);
        if (start.Length == 0)
            return null;
        if (suite.Macros.TryGetValue("waitForStatusBar", out var macro) && macro.Parameters.ContainsKey("startsWith"))
            return new DraftStep(new JsonObject { ["call"] = "waitForStatusBar", ["with"] = new JsonObject { ["startsWith"] = start } }, ActivityKind.Say);
        if (suite.App.Status is not { } status)
            return null;
        var target = new TargetChoice(JsonValue.Create(status), status, TargetQuality.Sturdy, $"The app's status text, {status} in elements.json.");
        return new DraftStep(new JsonObject { ["waitText"] = status, ["startsWith"] = start, ["new"] = true }, ActivityKind.Say, target);
    }

    /// <summary>The start of what a control says, as a wait for it puts it: up to its first full stop, colon or comma, six words at most.</summary>
    public static string StartOf(string text)
    {
        text = text.Trim().ReplaceLineEndings(" ");
        foreach (var stop in new[] { ". ", ": ", ", " })
            if (text.IndexOf(stop, StringComparison.Ordinal) is > 0 and var at)
                text = text[..(stop == ". " ? at + 1 : at)];
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Take(6));
    }

    // What a message box's expect checks: its first sentence, or its first 60 characters or so.
    private static string FirstSentence(string message)
    {
        message = message.Trim().ReplaceLineEndings(" ");
        var end = message.IndexOfAny(['.', '?', '!']);
        var sentence = end >= 0 ? message[..(end + 1)] : message;
        if (sentence.Length > 60)
            sentence = sentence[..(sentence.LastIndexOf(' ', 60) is > 0 and var space ? space : 60)];
        return sentence;
    }

    private static DraftStep Make(string verb, TargetChoice target, ControlFacts control, string say, ActivityKind kind) =>
        new(new JsonObject { [verb] = target.Json.DeepClone(), ["say"] = say }, kind, target, control);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
