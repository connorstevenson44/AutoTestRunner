using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// The step a watched run is stuck on, for the panel to offer to pick its control again: the step itself, which the
/// runner tries again as it then is, its kind, and the case it is in, or null when steps are played outside a case.
/// </summary>
public sealed record StuckStep(JsonObject Step, string Kind, Case? Case)
{
    /// <summary>The control the step uses, when it is a step that uses one.</summary>
    public JsonNode? Target => ScriptVocabulary.Steps.TryGetValue(Kind, out var kind) && kind.Takes == Takes.Target ? Step[Kind] : null;
}
