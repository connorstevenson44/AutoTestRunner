using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// The forms the builder shows for a step's details and for a new check: a field for each property, in words, with
/// what it holds, so that a file can be browsed to and a control picked in the app. A field reads its property from
/// the step's JSON as text, and writes the text back as the JSON the scripts take.
/// </summary>
public static class Forms
{

    /// <summary>The checks the builder offers, in the order its menu lists them, under their groups.</summary>
    public static readonly IReadOnlyList<CheckKind> CheckKinds =
    [
        new("shows", "It shows", "In the app"),
        new("says", "It says", "In the app"),
        new("enabled", "It can be used", "In the app"),
        new("disabled", "It's greyed out", "In the app"),
        new("ticked", "It's ticked", "In the app"),
        new("notTicked", "It isn't ticked", "In the app"),
        new("selected", "It's selected", "In the app"),
        new("focused", "It has the keyboard", "In the app"),
        new("rows", "Number of rows", "In the app"),
        new("fileExists", "A file is there", "Files"),
        new("fileMissing", "A file isn't there", "Files"),
        new("fileContains", "A file has a text", "Files"),
        new("fileMatches", "A file matches", "Files"),
        new("fileCount", "Number of files", "Files"),
        new("fileVersion", "A program's version", "Files"),
        new("jsonValue", "A JSON setting", "Files"),
        new("appSetting", "An app setting", "Files"),
        new("connectionString", "A connection string", "Files"),
        new("logged", "A log has a text", "Files"),
        new("lastLine", "A log's last line", "Files"),
        new("service", "A service", "Windows"),
        new("scheduledTask", "A scheduled task", "Windows"),
        new("registry", "A registry value", "Windows"),
        new("process", "A program is running", "Windows"),
        new("eventLog", "An event log entry", "Windows"),
        new("web", "A web address answers", "Windows"),
        new("output", "A program's output", "Windows"),
        new("clipboard", "The clipboard", "Windows"),
        new("nothingLeft", "Nothing left over", "Windows"),
        new("equals", "A value is", "Values"),
        new("contains", "A value has", "Values"),
        new("lacks", "A value doesn't have", "Values"),
        new("matches", "A value matches", "Values"),
        new("atMost", "A number is at most", "Values"),
        new("atLeast", "A number is at least", "Values"),
        new("dbValue", "A database value", "Data"),
        new("queue", "A queue", "Data"),
    ];

    private static readonly Dictionary<string, FormField[]> CheckFields = new(StringComparer.Ordinal)
    {
        ["shows"] = [Control()],
        ["enabled"] = [Control()],
        ["disabled"] = [Control()],
        ["ticked"] = [Control("Tick box")],
        ["notTicked"] = [Control("Tick box")],
        ["selected"] = [Control()],
        ["focused"] = [Control()],
        ["says"] = [Control(), new("equals", "Says exactly", FieldType.Text), new("startsWith", "Starts with", FieldType.Text), new("contains", "Has in it", FieldType.Text), new("matches", "Matches the regex", FieldType.Text)],
        ["rows"] = [Control("List, grid or tree"), new("equals", "Rows", FieldType.Number)],
        ["fileExists"] = [new("fileExists", "File", FieldType.File)],
        ["fileMissing"] = [new("fileMissing", "File", FieldType.File)],
        ["fileContains"] = [new("fileContains", "File", FieldType.File), new("text", "Has in it", FieldType.Text)],
        ["fileMatches"] = [new("fileMatches", "File", FieldType.File), new("regex", "Regex", FieldType.Text)],
        ["fileCount"] = [new("fileCount", "Folder", FieldType.Folder), new("pattern", "Files like", FieldType.Text), new("equals", "How many", FieldType.Number), new("folders", "Count folders", FieldType.YesNo), new("recursive", "And in folders inside", FieldType.YesNo)],
        ["fileVersion"] = [new("fileVersion", "Program", FieldType.File), new("startsWith", "Version starts with", FieldType.Text)],
        ["jsonValue"] = [new("jsonValue", "JSON file", FieldType.File), new("path", "Setting, such as Orders:Region", FieldType.Text), new("equals", "Is", FieldType.Text)],
        ["appSetting"] = [new("appSetting", ".config file", FieldType.File), new("key", "Key", FieldType.Text), new("equals", "Is", FieldType.Text)],
        ["connectionString"] = [new("connectionString", ".config file", FieldType.File), new("name", "Name", FieldType.Text)],
        ["logged"] = [new("logged", "Log file", FieldType.File), new("text", "Has in it", FieldType.Text)],
        ["lastLine"] = [new("lastLine", "Log file", FieldType.File), new("contains", "Last line has", FieldType.Text)],
        ["service"] = [new("service", "Service name", FieldType.Text), new("state", "State", FieldType.Choice, ["Running", "Stopped", ""])],
        ["scheduledTask"] = [new("scheduledTask", @"Task, such as \Contoso\Export", FieldType.Text), new("exists", "Is there", FieldType.YesNo)],
        ["registry"] = [new("registry", @"Key, such as HKLM\SOFTWARE\Contoso", FieldType.Text), new("value", "Value name", FieldType.Text), new("equals", "Is", FieldType.Text)],
        ["process"] = [new("process", "Program name", FieldType.Text), new("running", "Running", FieldType.YesNo)],
        ["eventLog"] = [new("eventLog", "Log, such as Application", FieldType.Text), new("source", "Source", FieldType.Text), new("contains", "Has in it", FieldType.Text)],
        ["web"] = [new("web", "Address", FieldType.Text), new("status", "Status", FieldType.Number), new("contains", "Page has", FieldType.Text)],
        ["output"] = [new("output", "Program", FieldType.File), new("args", "Arguments, one to a line", FieldType.Lines), new("contains", "Output has", FieldType.Text)],
        ["clipboard"] = [new("clipboard", "Clipboard has", FieldType.Text)],
        ["nothingLeft"] = [],
        ["equals"] = [new("equals", "Value", FieldType.Text), new("to", "Is", FieldType.Text)],
        ["contains"] = [new("contains", "Value", FieldType.Text), new("text", "Has in it", FieldType.Text)],
        ["lacks"] = [new("lacks", "Value", FieldType.Text), new("text", "Doesn't have", FieldType.Text)],
        ["matches"] = [new("matches", "Value", FieldType.Text), new("regex", "Regex", FieldType.Text)],
        ["atMost"] = [new("atMost", "Number", FieldType.Text), new("to", "At most", FieldType.Number)],
        ["atLeast"] = [new("atLeast", "Number", FieldType.Text), new("to", "At least", FieldType.Number)],
        ["dbValue"] = [new("dbValue", "Database", FieldType.Choice), new("sql", "SQL that reads the value", FieldType.LongText), new("params", "Parameters", FieldType.Json),
            new("equals", "Is", FieldType.Text), new("contains", "Has in it", FieldType.Text), new("matches", "Matches the regex", FieldType.Text), new("rows", "Rows", FieldType.Number)],
        ["queue"] = [new("queue", "Queue", FieldType.Choice), new("count", "Messages waiting", FieldType.Number), new("has", "A message has", FieldType.Text)],
    };

    private static readonly FormField[] CommonCheck = [new("label", "Say instead", FieldType.Text), new("within", "Seconds to come true", FieldType.Number)];

    private static readonly Dictionary<string, FormField[]> StepFields = new(StringComparer.Ordinal)
    {
        ["startApp"] = [],
        ["closeApp"] = [],
        ["click"] = [Control("Control", "click"), new("how", "How", FieldType.Choice, ["", "mouse", "right", "double"]), new("whileEnabled", "Again while it can be used", FieldType.YesNo)],
        ["drag"] = [Control("Control", "drag"), Control("Drop on", "to")],
        ["menu"] = [new("menu", "Menu items, one to a line", FieldType.Lines), Control("Right-click first", "on")],
        ["type"] = [Control("Box", "type"), new("text", "Text", FieldType.Text), new("secret", "Keep it out of the log", FieldType.YesNo)],
        ["choose"] = [Control("Drop-down", "choose"), new("option", "Option", FieldType.Text)],
        ["tick"] = [Control("Tick box", "tick"), new("checked", "Ticked", FieldType.YesNo)],
        ["setValue"] = [Control("Control", "setValue"), new("to", "Value", FieldType.Text)],
        ["expand"] = [new("expand", "Tree items, one to a line", FieldType.Lines), Control("Tree", "in")],
        ["hover"] = [Control("Control", "hover")],
        ["tab"] = [new("tab", "Tab", FieldType.Text)],
        ["find"] = [Control("Control", "find")],
        ["waitGone"] = [Control("Control", "waitGone")],
        ["waitEnabled"] = [Control("Control", "waitEnabled")],
        ["useWindow"] = [new("useWindow", "Window title", FieldType.Text)],
        ["waitText"] = [Control("Control", "waitText"), new("startsWith", "Starts with", FieldType.Text), new("contains", "Has in it", FieldType.Text), new("equals", "Says exactly", FieldType.Text), new("matches", "Matches the regex", FieldType.Text), new("changes", "Any change", FieldType.YesNo), new("new", "A new text", FieldType.YesNo)],
        ["replaceText"] = [Control("Box", "replaceText"), new("regex", "Regex", FieldType.Text), new("replacement", "Replace with", FieldType.Text)],
        ["key"] = [new("key", "Keys, such as Ctrl+S", FieldType.Keys)],
        ["answerDialog"] = [new("answerDialog", "Message box title", FieldType.Text), new("button", "Button", FieldType.Choice, ["OK", "Yes", "No", "Cancel"]), new("expect", "Message has", FieldType.Text)],
        ["pickFile"] = [new("pickFile", "Picker title", FieldType.Text), new("path", "File", FieldType.File)],
        ["pickFolder"] = [new("pickFolder", "Picker title", FieldType.Text), new("path", "Folder", FieldType.Folder)],
        ["call"] = [new("call", "Macro", FieldType.Text), new("with", "Parameters", FieldType.Json)],
        ["repeat"] = [new("repeat", "For each of", FieldType.Json), new("item", "Call each", FieldType.Text)],
        ["whileShows"] = [Control("Control", "whileShows"), Control("Until this", "untilText"), new("startsWith", "Starts with", FieldType.Text), new("contains", "Has in it", FieldType.Text), new("equals", "Says exactly", FieldType.Text), new("matches", "Matches the regex", FieldType.Text), new("new", "A new text", FieldType.YesNo)],
        ["if"] = [new("if", "Check", FieldType.Json)],
        ["set"] = [new("set", "Variables", FieldType.Json)],
        ["run"] = [new("run", "Program", FieldType.File), new("args", "Arguments, one to a line", FieldType.Lines), new("env", "Environment", FieldType.Json)],
        ["powershell"] = [new("powershell", "Script", FieldType.LongText), new("args", "Arguments, one to a line", FieldType.Lines)],
        ["script"] = [new("script", "Script", FieldType.File), new("args", "Arguments, one to a line", FieldType.Lines)],
        ["wait"] = [new("wait", "Seconds", FieldType.Number)],
        ["copy"] = [new("copy", "File or folder", FieldType.File), new("to", "To", FieldType.Folder)],
        ["delete"] = [new("delete", "File or folder", FieldType.File)],
        ["writeFile"] = [new("writeFile", "File", FieldType.File), new("text", "Text", FieldType.LongText), new("append", "Add to the end", FieldType.YesNo)],
        ["unzip"] = [new("unzip", ".zip file", FieldType.File), new("to", "Into", FieldType.Folder)],
        ["startService"] = [new("startService", "Service name", FieldType.Text)],
        ["stopService"] = [new("stopService", "Service name", FieldType.Text)],
        ["setJsonValue"] = [new("setJsonValue", "JSON file", FieldType.File), new("path", "Setting, such as Orders:Region", FieldType.Text), new("to", "Value", FieldType.Text)],
        ["setAppSetting"] = [new("setAppSetting", ".config file", FieldType.File), new("key", "Key", FieldType.Text), new("to", "Value", FieldType.Text)],
        ["query"] = [new("query", "Database", FieldType.Choice), new("sql", "SQL", FieldType.LongText), new("params", "Parameters, such as { \"id\": \"${order}\" }", FieldType.Json)],
        ["send"] = [new("send", "Queue", FieldType.Choice), new("message", "Message", FieldType.LongText), new("file", "Or a file with it in", FieldType.File)],
        ["receive"] = [new("receive", "Queue", FieldType.Choice), new("contains", "Message has", FieldType.Text), new("equals", "Message is", FieldType.Text), new("matches", "Matches the regex", FieldType.Text)],
        ["purge"] = [new("purge", "Queue", FieldType.Choice)],
        ["checkpoint"] = [new("checkpoint", "Title", FieldType.Text), new("checks", "Checks", FieldType.Json), new("look", "To look at, one to a line", FieldType.Lines)],
        ["say"] = [],
    };

    private static readonly FormField[] CommonStep =
    [
        new("say", "Panel says", FieldType.Text), new("as", "Keep as", FieldType.Text), new("within", "Seconds before it's stuck", FieldType.Number),
        new("what", "Call the control", FieldType.Text), new("note", "Note", FieldType.Text),
    ];

    /// <summary>
    /// The fields of a check of this kind: its own key first, then the rest, then label and within. A database or queue
    /// is picked from the suite's.
    /// </summary>
    public static IReadOnlyList<FormField> ForCheck(string kind, Scripting.Suite? suite = null) =>
        CheckFields.TryGetValue(kind, out var fields) ? [.. fields.Select(field => Named(field.Key.Length == 0 ? field with { Key = kind } : field, kind, suite)), .. CommonCheck] : throw new ArgumentException($"There is no check called {kind}.", nameof(kind));

    /// <summary>The fields of a step of this kind: its own key first, then the rest, then what any step can have.</summary>
    public static IReadOnlyList<FormField> ForStep(string kind, Scripting.Suite? suite = null) =>
        StepFields.TryGetValue(kind, out var fields) ? [.. fields.Select(field => Named(field, kind, suite)), .. CommonStep] : throw new ArgumentException($"There is no step called {kind}.", nameof(kind));

    // The database or queue a step or check names, to choose from those in suite.json.
    private static FormField Named(FormField field, string kind, Scripting.Suite? suite) => field.Key != kind || suite is null ? field : kind switch
    {
        "query" or "dbValue" => field with { Choices = [.. suite.Databases.Keys] },
        "send" or "receive" or "purge" or "queue" => field with { Choices = [.. suite.Queues.Keys] },
        _ => field,
    };

    /// <summary>A new check of this kind, with its key there, empty, so it reads as that kind.</summary>
    public static JsonObject NewCheck(string kind) => new() { [kind] = kind == "nothingLeft" ? true : "" };

    /// <summary>What a field shows: its property as text, or empty when it isn't there.</summary>
    public static string Read(JsonObject json, FormField field)
    {
        var node = json[field.Key];
        return node switch
        {
            null => "",
            JsonValue value when value.TryGetValue(out string? text) => text,
            JsonArray list when field.Type == FieldType.Lines && list.All(item => item is JsonValue) => string.Join("\n", list.Select(item => item?.ToString())),
            JsonValue value when field.Type == FieldType.YesNo && value.TryGetValue(out bool yes) => yes ? "true" : "false",
            JsonValue value => value.ToJsonString(),
            _ => JsonLine.Write(node),
        };
    }

    /// <summary>
    /// Writes what a field holds back into the JSON: as a number or yes and no where it is one, a list a line to an
    /// item, a control or anything else as JSON when it is written as JSON. An empty field takes the property out,
    /// except the step's or check's own key. Throws a FormatException, saying why, for JSON that doesn't read.
    /// </summary>
    public static void Write(JsonObject json, FormField field, string text, bool isKey = false)
    {
        if (text.Trim().Length == 0 && !isKey)
        {
            json.Remove(field.Key);
            return;
        }
        json[field.Key] = field.Type switch
        {
            FieldType.Number when double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) =>
                number == Math.Floor(number) && Math.Abs(number) < int.MaxValue ? JsonValue.Create((int)number) : JsonValue.Create(number),
            FieldType.YesNo => JsonValue.Create(text.Trim() == "true"),
            FieldType.Lines => new JsonArray([.. text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0).Select(line => (JsonNode?)JsonValue.Create(line))]),
            FieldType.Target when text.TrimStart() is ['{' or '[', ..] => Parse(text, field),
            FieldType.Json => Parse(text, field),
            FieldType.Target or FieldType.Choice => JsonValue.Create(text.Trim()),
            _ => JsonValue.Create(text),
        };
    }

    // A field written as JSON, or a FormatException saying where it doesn't read.
    private static JsonNode? Parse(string text, FormField field)
    {
        try
        {
            return ScriptJson.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{field.Label} isn't JSON that reads: {ex.Message}", ex);
        }
    }

    // A control check's control is under the check's own key, filled in by ForCheck.
    private static FormField Control(string label = "Control", string key = "") => new(key, label, FieldType.Target);
}
