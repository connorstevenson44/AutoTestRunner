using System.Text.Json.Nodes;
using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// A step of a case being built: the JSON it saves as, what kind of thing it does, and the control it uses with how
/// it is found, so the panel can show how sturdy it is and outline the control again.
/// </summary>
public sealed class DraftStep(JsonObject json, ActivityKind kind, TargetChoice? target = null, ControlFacts? control = null)
{
    public JsonObject Json { get; } = json;

    public ActivityKind Kind { get; } = kind;

    public TargetChoice? Target { get; set; } = target;

    public ControlFacts? Control { get; } = control;

    /// <summary>The step's key, such as click or waitText.</summary>
    public string Verb => Json.Select(pair => pair.Key).FirstOrDefault(ScriptVocabulary.Steps.ContainsKey) ?? "say";

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
        "waitText" => $"Wait for {Target?.Shown ?? "it"} to say \"{Json["startsWith"]}\"",
        "tab" => $"Open the {Json["tab"]} tab",
        "startApp" => "Start the app",
        _ => Verb,
    };
}
