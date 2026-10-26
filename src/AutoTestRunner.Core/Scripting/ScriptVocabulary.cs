namespace AutoTestRunner.Scripting;

/// <summary>
/// The fixed vocabulary of steps, checks and tools, and the properties each one takes. The loader rejects anything
/// else, and the JSON schemas in schema/ are checked against this list by a test, so the two can't drift apart.
/// </summary>
public static class ScriptVocabulary
{
    /// <summary>Properties any step can have.</summary>
    public static readonly IReadOnlySet<string> CommonStepKeys = new HashSet<string>(["say", "note", "as", "what", "within", "disabled"]);

    /// <summary>Each step kind, by its key, and the other properties it takes.</summary>
    public static readonly IReadOnlyDictionary<string, StepKind> Steps = new Dictionary<string, StepKind>
    {
        ["startApp"] = new(Takes.Nothing),
        ["closeApp"] = new(Takes.Nothing),
        ["click"] = new(Takes.Target, "how", "whileEnabled"),
        ["type"] = new(Takes.Target, "text", "secret"),
        ["choose"] = new(Takes.Target, "option"),
        ["tick"] = new(Takes.Target, "checked"),
        ["hover"] = new(Takes.Target),
        ["tab"] = new(Takes.Text),
        ["find"] = new(Takes.Target),
        ["waitGone"] = new(Takes.Target),
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
        ["checkpoint"] = new(Takes.Text, "checks", "look", "tools"),
    };

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
        ["nothingLeft"] = [],
    };

    public static readonly IReadOnlySet<string> CommonCheckKeys = new HashSet<string>(["label", "within"]);

    /// <summary>The buttons a checkpoint can show, by their key, and the other properties each takes besides label.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Tools = new Dictionary<string, string[]>
    {
        ["openFolder"] = [],
        ["openFile"] = [],
        ["console"] = ["args"],
        ["app"] = ["args"],
    };
}
