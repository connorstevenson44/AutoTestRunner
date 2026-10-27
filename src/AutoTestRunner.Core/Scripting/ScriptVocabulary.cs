using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>
/// The fixed vocabulary of steps, checks and tools, and the properties each one takes. The loader rejects anything
/// else, and the JSON schemas in schema/ are checked against this list by a test, so the two can't drift apart.
/// </summary>
public static partial class ScriptVocabulary
{
    /// <summary>Properties any step can have.</summary>
    public static readonly IReadOnlySet<string> CommonStepKeys = new HashSet<string>(["say", "note", "as", "what", "within", "disabled"]);

    /// <summary>Each step kind, by its key, and the other properties it takes.</summary>
    public static readonly IReadOnlyDictionary<string, StepKind> Steps = new Dictionary<string, StepKind>
    {
        ["startApp"] = new(Takes.Nothing),
        ["closeApp"] = new(Takes.Nothing),
        ["click"] = new(Takes.Target, "how", "whileEnabled"),
        ["drag"] = new(Takes.Target, "to"),
        ["menu"] = new(Takes.List, "on"),
        ["type"] = new(Takes.Target, "text", "secret"),
        ["choose"] = new(Takes.Target, "option"),
        ["tick"] = new(Takes.Target, "checked"),
        ["setValue"] = new(Takes.Target, "to"),
        ["expand"] = new(Takes.List, "in"),
        ["hover"] = new(Takes.Target),
        ["tab"] = new(Takes.Text),
        ["find"] = new(Takes.Target),
        ["waitGone"] = new(Takes.Target),
        ["waitEnabled"] = new(Takes.Target),
        ["useWindow"] = new(Takes.Text),
        ["waitText"] = new(Takes.Target, "startsWith", "contains", "equals", "matches", "changes", "new"),
        ["replaceText"] = new(Takes.Target, "regex", "replacement"),
        ["key"] = new(Takes.Text),
        ["answerDialog"] = new(Takes.Text, "button", "expect"),
        ["pickFile"] = new(Takes.Text, "path"),
        ["pickFolder"] = new(Takes.Text, "path"),
        ["call"] = new(Takes.Text, "with"),
        ["repeat"] = new(Takes.List, "steps", "item"),
        ["whileShows"] = new(Takes.Target, "steps", "untilText", "startsWith", "contains", "equals", "matches", "new"),
        ["if"] = new(Takes.Object, "steps", "else"),
        ["set"] = new(Takes.Object),
        ["run"] = new(Takes.Text, "args", "env"),
        ["powershell"] = new(Takes.Text, "args"),
        ["script"] = new(Takes.Text, "args"),
        ["wait"] = new(Takes.Text),
        ["copy"] = new(Takes.Text, "to"),
        ["delete"] = new(Takes.Text),
        ["writeFile"] = new(Takes.Text, "text", "append"),
        ["unzip"] = new(Takes.Text, "to"),
        ["startService"] = new(Takes.Text),
        ["stopService"] = new(Takes.Text),
        ["setJsonValue"] = new(Takes.Text, "path", "to"),
        ["setAppSetting"] = new(Takes.Text, "key", "to"),
        ["query"] = new(Takes.Text, "sql", "params"),
        ["send"] = new(Takes.Text, "message", "file"),
        ["receive"] = new(Takes.Text, "equals", "contains", "matches"),
        ["purge"] = new(Takes.Text),
        ["checkpoint"] = new(Takes.Text, "checks", "look", "tools"),
    };

    /// <summary>
    /// The step a step object is, by its keys: "say" when it has none of the step keys. Some steps take properties
    /// named like other steps, such as setAppSetting's key, so a key only names the step when every other step-like
    /// key is one of its properties.
    /// </summary>
    public static string KindOf(JsonObject step) => Kinds(step) is [var kind] ? kind : step.Select(pair => pair.Key).FirstOrDefault(Steps.ContainsKey) ?? "say";

    /// <summary>The step keys that could name what a step is: one for a well-formed step, none for narration.</summary>
    public static IReadOnlyList<string> Kinds(JsonObject step) => Scripting.Kinds.Named(step, Steps.ContainsKey, kind => Steps[kind].Keys);

    /// <summary>
    /// Each place in a step that names a control: its own target, a second one such as a drag's "to", the control an
    /// if or a checkpoint checks, and the same in the steps inside it. Each is the object it is in, its key, and its
    /// path from the step's own, such as steps/2/steps/0/click.
    /// </summary>
    public static IEnumerable<(JsonObject Owner, string Key, string Path)> Targets(JsonObject step, string path)
    {
        var kind = KindOf(step);
        if (Steps.TryGetValue(kind, out var known) && known.Takes == Takes.Target && step[kind] is not null)
            yield return (step, kind, $"{path}/{kind}");
        if (SecondTargets.GetValueOrDefault(kind) is { } second && step[second] is not null)
            yield return (step, second, $"{path}/{second}");
        if (kind == "if" && step["if"] is JsonObject condition && ControlCheckOf(condition) is { } subject)
            yield return (condition, subject, $"{path}/if/{subject}");
        if (kind == "checkpoint" && step["checks"] is JsonArray checks)
            for (var index = 0; index < checks.Count; index++)
                if (checks[index] is JsonObject check && ControlCheckOf(check) is { } each)
                    yield return (check, each, $"{path}/checks/{index}/{each}");
        foreach (var key in new[] { "steps", "else" })
            if (step[key] is JsonArray inner)
                for (var index = 0; index < inner.Count; index++)
                    if (inner[index] is JsonObject child)
                        foreach (var target in Targets(child, $"{path}/{key}/{index}"))
                            yield return target;
    }

    /// <summary>The kind of a check of a control in the app, whose key holds its target, or null for any other check.</summary>
    public static string? ControlCheckOf(JsonObject check) =>
        Scripting.Kinds.Of(check, Checks) is { } kind && ControlChecks.Contains(kind) && check[kind] is not null ? kind : null;

    /// <summary>Each check kind, by its key, and the other properties it takes. Every check can have a label and within.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Checks = new Dictionary<string, string[]>
    {
        ["fileExists"] = [],
        ["fileMissing"] = [],
        ["service"] = ["state"],
        ["scheduledTask"] = ["exists"],
        ["jsonValue"] = ["path", "equals"],
        ["appSetting"] = ["key", "equals"],
        ["connectionString"] = ["name"],
        ["fileContains"] = ["text"],
        ["logged"] = ["text"],
        ["fileCount"] = ["pattern", "equals", "folders", "recursive"],
        ["fileVersion"] = ["startsWith"],
        ["output"] = ["args", "contains"],
        ["lastLine"] = ["contains"],
        ["equals"] = ["to"],
        ["matches"] = ["regex"],
        ["contains"] = ["text"],
        ["lacks"] = ["text"],
        ["shows"] = [],
        ["enabled"] = [],
        ["disabled"] = [],
        ["ticked"] = [],
        ["notTicked"] = [],
        ["selected"] = [],
        ["focused"] = [],
        ["says"] = ["equals", "startsWith", "contains", "matches"],
        ["rows"] = ["equals"],
        ["nothingLeft"] = [],
        ["fileMatches"] = ["regex"],
        ["registry"] = ["value", "equals"],
        ["process"] = ["running"],
        ["eventLog"] = ["source", "contains"],
        ["web"] = ["status", "contains"],
        ["clipboard"] = [],
        ["atMost"] = ["to"],
        ["atLeast"] = ["to"],
        ["dbValue"] = ["sql", "params", "equals", "contains", "matches", "rows"],
        ["queue"] = ["count", "has"],
    };

    /// <summary>The steps with a second control besides their own key's, by the property it is in.</summary>
    public static readonly IReadOnlyDictionary<string, string> SecondTargets = new Dictionary<string, string>
    {
        ["drag"] = "to",
        ["menu"] = "on",
        ["expand"] = "in",
        ["whileShows"] = "untilText",
    };

    /// <summary>The checks whose subject is a control in the app, written as a target.</summary>
    public static readonly IReadOnlySet<string> ControlChecks = new HashSet<string>(
        ["shows", "enabled", "disabled", "ticked", "notTicked", "selected", "focused", "says", "rows"]);

    public static readonly IReadOnlySet<string> CommonCheckKeys = new HashSet<string>(["label", "within"]);

    /// <summary>The buttons a checkpoint can show, by their key, and the other properties each takes besides label.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Tools = new Dictionary<string, string[]>
    {
        ["openFolder"] = [],
        ["openFile"] = [],
        ["console"] = ["args"],
        ["app"] = ["args"],
    };

    /// <summary>Whether the text can name a variable, macro or parameter: a letter or _, then letters, digits and _.</summary>
    public static bool IsName(string text) => Name().IsMatch(text);

    [GeneratedRegex(@"^[A-Za-z_]\w*$")]
    private static partial Regex Name();
}
