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
    public static IReadOnlyList<string> Kinds(JsonObject step)
    {
        var found = step.Select(pair => pair.Key).Where(Steps.ContainsKey).ToList();
        var named = found.Where(kind => found.All(other => other == kind || Steps[kind].Keys.Contains(other))).ToList();
        return named.Count == 1 ? named : found;
    }

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
