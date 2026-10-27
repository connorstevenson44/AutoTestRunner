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
    /// <summary>A step of a case opened to change it, at this place among its steps, or null for a reset step; not played yet.</summary>
    public static DraftStep Written(JsonObject json, int? source)
    {
        var verb = ScriptVocabulary.KindOf(json);
        var target = ScriptVocabulary.Steps.TryGetValue(verb, out var kind) && kind.Takes == Takes.Target && json[verb] is { } node
            ? TargetSuggester.Describe(node)
            : null;
        return new DraftStep(json, KindOf(verb), target) { Source = source, State = DraftStepState.NotPlayed };
    }

    /// <summary>A step made in the panel from its JSON, such as a macro's call or a repeat, with how it finds its control.</summary>
    public static DraftStep FromJson(JsonObject json, DraftStepState state)
    {
        var verb = ScriptVocabulary.KindOf(json);
        var target = ScriptVocabulary.Steps.TryGetValue(verb, out var kind) && kind.Takes == Takes.Target && json[verb] is { } node
            ? TargetSuggester.Describe(node)
            : null;
        return new DraftStep(json, KindOf(verb), target) { State = state };
    }

    /// <summary>Runs a .ps1 or .cs file in the suite, given relative to its folder.</summary>
    public static DraftStep Script(string path)
    {
        path = path.Replace('\\', '/');
        return new(new JsonObject { ["script"] = path, ["say"] = $"Run {path[(path.LastIndexOf('/') + 1)..]}" }, ActivityKind.Command);
    }

    /// <summary>Waits a number of seconds, for an app that gives nothing to wait for.</summary>
    public static DraftStep Wait(double seconds) => new(new JsonObject { ["wait"] = seconds }, ActivityKind.Say);

    /// <summary>Says something, with nothing done.</summary>
    public static DraftStep Say(string text) => new(new JsonObject { ["say"] = text }, ActivityKind.Say);

    /// <summary>What a step's icon shows, by its key.</summary>
    public static ActivityKind KindOf(string verb) => verb switch
    {
        "startApp" or "closeApp" => ActivityKind.App,
        "click" or "tab" => ActivityKind.Click,
        "type" or "replaceText" => ActivityKind.Type,
        "choose" or "tick" => ActivityKind.Choose,
        "hover" or "find" or "waitGone" or "waitEnabled" or "waitText" or "whileShows" => ActivityKind.Point,
        "key" => ActivityKind.Key,
        "answerDialog" => ActivityKind.Dialog,
        "pickFile" or "pickFolder" => ActivityKind.File,
        "run" or "powershell" or "script" => ActivityKind.Command,
        "checkpoint" => ActivityKind.Check,
        _ => ActivityKind.Say,
    };

    /// <summary>Drives another of the app's windows from now on, as after a sign-in window opens the main one.</summary>
    public static DraftStep UseWindow(string title) =>
        new(new JsonObject { ["useWindow"] = title, ["say"] = $"Use the {title} window" }, ActivityKind.Say);

    public static DraftStep StartApp(Suite suite) =>
        new(new JsonObject { ["startApp"] = new JsonObject(), ["say"] = $"Start {suite.App.Window}" }, ActivityKind.App);

    /// <summary>
    /// A right-click on a control and the items of its menu picked in turn; with no items, only the right-click.
    /// </summary>
    public static DraftStep ContextMenu(TargetChoice target, ControlFacts control, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return Make("click", target, control, $"Right-click {control.Label}", ActivityKind.Click, ("how", "right"));
        var json = new JsonObject
        {
            ["menu"] = new JsonArray([.. items.Select(item => (JsonNode?)JsonValue.Create(item))]),
            ["on"] = target.Json.DeepClone(),
            ["say"] = $"Pick {string.Join(" › ", items)} from {control.Label}'s menu",
        };
        return new DraftStep(json, ActivityKind.Click, control: control);
    }

    public static DraftStep Click(TargetChoice target, ControlFacts control) =>
        Make("click", target, control, $"Click {control.Label}", ActivityKind.Click);

    public static DraftStep Type(TargetChoice target, ControlFacts control, string text)
    {
        if (control.IsPassword)
        {
            return Make("type", target, control, $"Type the password in {control.Label}", ActivityKind.Type,
                ("text", "${password}"), ("secret", true), ("note", "A password box can't be read: give the password with autotest run --var password=…"));
        }
        var say = text.Length is > 0 and <= 40 && !text.Contains('\n') ? $"Type {text} in {control.Label}" : $"Type in {control.Label}";
        return Make("type", target, control, say, ActivityKind.Type, ("text", text));
    }

    public static DraftStep Choose(TargetChoice target, ControlFacts control, string option)
    {
        return Make("choose", target, control, $"Pick {option} from {control.Label}", ActivityKind.Choose, ("option", option));
    }

    public static DraftStep Tick(TargetChoice target, ControlFacts control, bool isChecked)
    {
        var say = $"{(isChecked ? "Tick" : "Clear")} {control.Label}";
        return isChecked
            ? Make("tick", target, control, say, ActivityKind.Choose)
            : Make("tick", target, control, say, ActivityKind.Choose, ("checked", false));
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

    public static DraftStep WaitEnabled(TargetChoice target, ControlFacts control) =>
        Make("waitEnabled", target, control, $"{Capital(control.Label)} can be used", ActivityKind.Point);

    public static DraftStep WaitText(TargetChoice target, ControlFacts control, string text) =>
        new(new JsonObject { ["waitText"] = target.Json.DeepClone(), ["startsWith"] = StartOf(text) }, ActivityKind.Point, target, control);

    /// <summary>Keeps what a control says in a variable, for a check at the checkpoint.</summary>
    public static DraftStep Keep(TargetChoice target, ControlFacts control, string name)
    {
        return Make("find", target, control, $"Keep what it says as {name}: ${{{name}}}", ActivityKind.Point, ("as", name));
    }

    /// <summary>
    /// The steps that wait for a watched control to say what it says now: the hook's macro, given startsWith when it
    /// takes it, else a waitText; the app's status text waits for text new since the last click. With keep, they keep
    /// what it says in a variable of that name, with a find after a macro that hands nothing back.
    /// </summary>
    public static IReadOnlyList<DraftStep> WatchWait(Suite suite, WatchHook hook, string text, string? keep)
    {
        var start = StartOf(text);
        if (start.Length == 0)
            return [];
        var target = new TargetChoice(JsonValue.Create(hook.Element), hook.Element, TargetQuality.Sturdy, $"Watched by the builder, {hook.Element} in elements.json.");
        if (hook.Call is { } call && suite.Macros.TryGetValue(call, out var macro))
        {
            var json = new JsonObject { ["call"] = call };
            if (macro.Parameters.ContainsKey("startsWith"))
                json["with"] = new JsonObject { ["startsWith"] = start };
            if (keep is null)
                return [new DraftStep(json, ActivityKind.Say)];
            if (macro.Returns is not null)
            {
                json["as"] = keep;
                return [new DraftStep(json, ActivityKind.Say)];
            }
            return [new DraftStep(json, ActivityKind.Say), new DraftStep(new JsonObject { ["find"] = hook.Element, ["as"] = keep }, ActivityKind.Point, target)];
        }
        var wait = new JsonObject { ["waitText"] = hook.Element, ["startsWith"] = start };
        if (hook.Element == suite.App.Status)
            wait["new"] = true;
        if (keep is not null)
            wait["as"] = keep;
        return [new DraftStep(wait, ActivityKind.Say, target)];
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

    // A step as a person writes one: what it does to which control, what goes with that, and last, what it says.
    private static DraftStep Make(string verb, TargetChoice target, ControlFacts control, string say, ActivityKind kind, params (string Key, JsonNode? Value)[] with)
    {
        var json = new JsonObject { [verb] = target.Json.DeepClone() };
        foreach (var (key, value) in with)
            json[key] = value;
        json["say"] = say;
        return new(json, kind, target, control);
    }

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
