using System.Text.Json.Nodes;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// A step of a case being built: the JSON it saves as, what kind of thing it does, and the control it uses with how
/// it is found, so the panel can show how sturdy it is and outline the control again. A step of a case opened to
/// change it also knows its place in the file, so it is saved as it was written while it is unchanged.
/// </summary>
public sealed class DraftStep(JsonObject json, ActivityKind kind, TargetChoice? target = null, ControlFacts? control = null)
{
    public JsonObject Json { get; } = json;

    public ActivityKind Kind { get; } = kind;

    public TargetChoice? Target { get; set; } = target;

    public ControlFacts? Control { get; } = control;

    /// <summary>Its place among the steps in the case file, for a step of a case opened to change it; else null.</summary>
    public int? Source { get; init; }

    /// <summary>
    /// Whether the case's reset starts at it: it and the steps after it take the app back to where the case leaves
    /// it, for the cases that start after its reset.
    /// </summary>
    public bool Reset { get; set; }

    /// <summary>Whether it was done in the app. Set by the builder's thread, read by the panel.</summary>
    public DraftStepState State { get; set; }

    /// <summary>Why it didn't work, when it was played and didn't.</summary>
    public string? Problem { get; set; }

    /// <summary>The .ps1 or .cs file a script step runs, relative to the suite folder; else null.</summary>
    public string? Script => Verb == "script" ? Json["script"]?.GetValue<string>() : null;

    /// <summary>The step's key, such as click or waitText.</summary>
    public string Verb => ScriptVocabulary.KindOf(Json);

    /// <summary>Whether the step waits for the app rather than doing something.</summary>
    public bool Waits => Verb is "call" or "find" or "waitText" or "waitGone";

    /// <summary>What the panel says for it: its say, or what it does.</summary>
    public string Text
    {
        get => Json["say"]?.GetValue<string>() ?? Describe();
        set => Json["say"] = value;
    }

    private string Describe() => Verb switch
    {
        "call" when Json["with"]?["startsWith"] is { } words => $"Wait for the bar at the bottom to say \"{words}\"",
        "call" => $"Run {Json["call"]}",
        "waitText" when Json["startsWith"] is { } words => $"Wait for {Shown} to say \"{words}\"",
        "waitText" => $"Wait for {Shown} to change",
        "tab" => $"Open the {Json["tab"]} tab",
        "startApp" => "Start the app",
        "closeApp" => "Close the app",
        "click" => $"Click {Shown}",
        "type" => $"Type {Json["text"]} in {Shown}",
        "choose" => $"Pick {Json["option"]} from {Shown}",
        "tick" => Json["checked"]?.GetValue<bool>() == false ? $"Clear {Shown}" : $"Tick {Shown}",
        "hover" => $"Point at {Shown}",
        "find" => $"Wait for {Shown}",
        "waitGone" => $"Wait for {Shown} to go",
        "key" => $"Press {Json["key"]}",
        "answerDialog" => $"Click {Json["button"]} in {Json["answerDialog"]}",
        "pickFile" or "pickFolder" => $"Pick {Json["path"]}",
        "script" => $"Run {System.IO.Path.GetFileName(Script)}",
        "powershell" => "Run a PowerShell script",
        "run" => $"Run {System.IO.Path.GetFileName(Json["run"]?.ToString())}",
        "wait" => $"Wait {Json["wait"]} seconds",
        "repeat" => $"Repeat for {JsonLine.Write(Json["repeat"])}",
        "if" => "If " + JsonLine.Write(Json["if"]),
        "set" => "Set " + string.Join(", ", (Json["set"] as JsonObject ?? []).Select(pair => pair.Key)),
        _ => Verb,
    };

    private string Shown => Target?.Shown ?? "it";
}
