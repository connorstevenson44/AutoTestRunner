using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// The built-in checks a checkpoint can make, written the way the AutoHotkey runner's were: each label says what
/// should be true, and a failure says what was found instead.
/// </summary>
public static class Checks
{
    public const string Absent = "<absent>";

    /// <summary>Reads a check from a checkpoint's "checks" list.</summary>
    /// <param name="node">The check as the script writes it.</param>
    /// <param name="scope">The variables to expand in its values.</param>
    /// <param name="context">The machine and the app it looks at.</param>
    public static Check Build(JsonObject node, VariableScope scope, CheckContext context)
    {
        var kind = Kinds.Of(node, ScriptVocabulary.Checks) ?? throw new ScriptException($"{node.ToJsonString()} isn't a check.");
        string Text(string key) => scope.Expand(node[key]?.ToString() ?? "");
        string? Optional(string key) => node[key] is null ? null : Text(key);
        var machine = context.Machine;
        // A check of a control's subject is a target, which the app's probe resolves itself.
        var subject = ScriptVocabulary.ControlChecks.Contains(kind) ? "" : Text(kind);
        var name = Path.GetFileName(subject);
        ControlProbe Control() => (context.Controls ?? throw new ScriptException($"The {kind} check needs the app."))(node[kind]!);

        (string Label, Func<string?> Test, int Seconds) check = kind switch
        {
            "fileExists" => ($"{subject} is there", () => Exists(subject) ? null : "it isn't there", 0),
            "fileMissing" => ($"{subject} isn't there", () => Exists(subject) ? "it is there" : null, 0),
            "service" => Service(subject, Optional("state"), machine),
            "scheduledTask" => Task(subject, node["exists"]?.GetValue<bool>() ?? true, machine),
            "jsonValue" => Setting(name, Text("path"), Text("equals"), () => JsonValue(subject, Text("path"))),
            "appSetting" => Setting(name, Text("key"), Text("equals"), () => XmlValue(subject, "appSettings", "key", Text("key"), "value")),
            "connectionString" => ($"{name} has a connection string called {Text("name")}",
                () => XmlValue(subject, "connectionStrings", "name", Text("name"), "connectionString") != Absent ? null : "it isn't there", 0),
            "fileContains" => ($"{name} has {Text("text")} in it", () => FileHas(subject, Text("text")), 0),
            "logged" => ($"The log says \"{Text("text")}\"", () => FileHas(subject, Text("text")), 0),
            "fileCount" => Count(subject, Optional("pattern") ?? "*", int.Parse(Text("equals"), CultureInfo.InvariantCulture), node["folders"]?.GetValue<bool>() ?? false,
                node["recursive"]?.GetValue<bool>() ?? false),
            "fileVersion" => ($"{name} is version {Text("startsWith")}", () => Version(subject, Text("startsWith")), 0),
            "output" => Output(subject, (node["args"] as JsonArray ?? []).Select(arg => scope.Expand(arg!.ToString())).ToList(), Text("contains"), machine),
            "lastLine" => ($"The latest line of {name} says {Text("contains")}", () => LastLine(subject, Text("contains")), 30),
            "equals" => ($"It is {Text("to")}", () => subject == Text("to") ? null : $"it is {subject}", 0),
            "matches" => ($"It matches {Text("regex")}", () => Regex.IsMatch(subject, Text("regex")) ? null : $"it is {subject}", 0),
            "contains" => ($"It has {Text("text")} in it", () => subject.Contains(Text("text"), StringComparison.Ordinal) ? null : $"it is {subject}", 0),
            "lacks" => ($"It doesn't have {Text("text")} in it", () => subject.Contains(Text("text"), StringComparison.Ordinal) ? "it does" : null, 0),
            "shows" => Shows(Control()),
            "enabled" or "disabled" or "ticked" or "notTicked" or "selected" or "focused" => State(kind, Control()),
            "says" => Says(Control(), Optional("equals"), Optional("startsWith"), Optional("contains"), Optional("matches")),
            "rows" => Rows(Control(), int.Parse(Text("equals"), CultureInfo.InvariantCulture)),
            "nothingLeft" => ("Nothing from the tests is left on this computer",
                () => context.Leftovers() is [_, ..] found ? "still there: " + string.Join(", ", found) : null, 0),
            "fileMatches" => ($"{name} matches {Text("regex")}", () => !File.Exists(subject) ? "the file isn't there" : Regex.IsMatch(ReadShared(subject).ReplaceLineEndings("\n"), Text("regex"), RegexOptions.Multiline) ? null : "it doesn't", 0),
            "registry" => Setting($"the registry's {subject}", Text("value"), Optional("equals") ?? "", () => machine.RegistryValue(subject, Text("value")) ?? Absent),
            "process" => Running(subject, node["running"]?.GetValue<bool>() ?? true, machine),
            "eventLog" => ($"The {subject} event log says \"{Text("contains")}\"{(Optional("source") is { } source ? $" from {source}" : "")}",
                () => machine.EventLogged(subject, Optional("source"), Text("contains"), context.Since ?? DateTime.Now.AddMinutes(-10)) ? null : "it doesn't", 0),
            "web" => Web(subject, Optional("status") is { } status ? int.Parse(status, CultureInfo.InvariantCulture) : 200, Optional("contains"), machine),
            "clipboard" => ($"The clipboard has \"{subject}\" in it", () => machine.Clipboard() is var text && text.Contains(subject, StringComparison.Ordinal) ? null : $"it has: {text.Trim()}", 0),
            "atMost" => Compare(subject, Text("to"), atMost: true),
            "atLeast" => Compare(subject, Text("to"), atMost: false),
            _ => throw new ScriptException($"There is no {kind} check."),
        };

        var within = node["within"] is { } seconds ? TimeSpan.FromSeconds(double.Parse(scope.Expand(seconds.ToString()), CultureInfo.InvariantCulture)) : TimeSpan.FromSeconds(check.Seconds);
        return new Check(Optional("label") ?? check.Label, within, check.Test);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    // A service can take a few seconds to start or go, so this waits a little for it.
    private static (string, Func<string?>, int) Service(string name, string? state, IMachine machine)
    {
        var absent = state is null or "" or "absent";
        var label = absent ? $"There is no service called {name}" : $"The service {name} is {state!.ToLowerInvariant()}";
        return (label, () =>
        {
            var found = machine.ServiceState(name);
            if (absent ? found is null : string.Equals(found, state, StringComparison.OrdinalIgnoreCase))
                return null;
            return found is null ? "it is not there" : $"it is {found.ToLowerInvariant()}";
        }, 20);
    }

    private static (string, Func<string?>, int) Task(string path, bool exists, IMachine machine) =>
        (exists ? $"The scheduled task {path} is there" : $"There is no scheduled task {path}",
         () => machine.TaskExists(path) == exists ? null : exists ? "it isn't there" : "it is still there", 0);

    private static (string, Func<string?>, int) Setting(string file, string key, string expected, Func<string> read) =>
        (expected == Absent ? $"{file} has no {key} setting" : $"In {file}, {key} is {expected}",
         () =>
         {
             var found = read();
             return found == expected ? null : found == Absent ? "it isn't in the file" : $"it is {found}";
         }, 0);

    /// <summary>A JSON setting by its path, such as Orders:Region; an array as its items with spaces between.</summary>
    public static string JsonValue(string file, string path)
    {
        JsonNode? node = JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        foreach (var key in path.Split(':'))
        {
            if (node is not JsonObject item)
                return Absent;
            node = item.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return node switch
        {
            null => Absent,
            JsonArray list => string.Join(" ", list.Select(entry => entry?.ToString())),
            JsonValue value when value.TryGetValue(out string? text) => text,
            _ => node.ToJsonString(),
        };
    }

    /// <summary>A value from a .config file's section, such as an appSettings value by its key.</summary>
    public static string XmlValue(string file, string section, string keyAttribute, string key, string valueAttribute)
    {
        var add = XDocument.Load(file).Root?.Element(section)?.Elements("add")
            .FirstOrDefault(element => (string?)element.Attribute(keyAttribute) == key);
        return (string?)add?.Attribute(valueAttribute) ?? Absent;
    }

    private static string? FileHas(string file, string text) =>
        !File.Exists(file) ? "the file isn't there" : ReadShared(file).Contains(text, StringComparison.Ordinal) ? null : "it doesn't";

    // recursive counts the folders inside it too.
    private static (string, Func<string?>, int) Count(string folder, string pattern, int expected, bool folders, bool recursive) =>
        ($"{folder} has {expected} {(folders ? "folder" : pattern + " file")}{(expected == 1 ? "" : "s")} in it",
         () =>
         {
             var depth = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
             var found = !Directory.Exists(folder) ? 0 : folders ? Directory.GetDirectories(folder, pattern, depth).Length : Directory.GetFiles(folder, pattern, depth).Length;
             return found == expected ? null : $"it has {found}";
         }, 0);

    private static (string, Func<string?>, int) Shows(ControlProbe control) =>
        ($"It shows {control.What}", () => control.Find() is null ? "it isn't there" : null, 0);

    // What a control is like now: whether it can be used, is ticked, picked or has the keyboard.
    private static (string, Func<string?>, int) State(string kind, ControlProbe control)
    {
        var what = Capital(control.What);
        var (label, holds, otherwise) = kind switch
        {
            "enabled" => ($"{what} can be used", (Func<IControl, bool>)(found => found.IsEnabled), "it is greyed out"),
            "disabled" => ($"{what} is greyed out", found => !found.IsEnabled, "it can be used"),
            "ticked" => ($"{what} is ticked", found => found.IsChecked == true, "it isn't ticked"),
            "notTicked" => ($"{what} isn't ticked", found => found.IsChecked == false, "it is ticked"),
            "selected" => ($"{what} is picked", found => found.IsSelected, "it isn't picked"),
            _ => ($"{what} has the keyboard", found => found.HasFocus, "the keyboard is elsewhere"),
        };
        return (label, () => control.Find() is not { } found ? "it isn't there" : holds(found) ? null : otherwise, 0);
    }

    private static (string, Func<string?>, int) Says(ControlProbe control, string? equals, string? startsWith, string? contains, string? matches)
    {
        var wanted = equals ?? startsWith ?? contains ?? matches ?? "";
        var how = equals is not null ? "says" : startsWith is not null ? "starts with" : contains is not null ? "has" : "matches";
        return ($"{Capital(control.What)} {how} \"{wanted}\"", () =>
        {
            if (control.Find() is not { } found)
                return "it isn't there";
            var text = found.Value ?? found.Name;
            var holds = (equals is null || text == equals) && (startsWith is null || text.StartsWith(startsWith, StringComparison.Ordinal))
                && (contains is null || text.Contains(contains, StringComparison.Ordinal)) && (matches is null || Regex.IsMatch(text, matches));
            return holds ? null : $"it says: {text}";
        }, 0);
    }

    private static (string, Func<string?>, int) Rows(ControlProbe control, int expected) =>
        ($"{Capital(control.What)} has {expected} row{(expected == 1 ? "" : "s")}",
         () => control.Find() is not { } found ? "it isn't there" : control.Rows(found) is var rows && rows == expected ? null : $"it has {rows}", 0);

    private static (string, Func<string?>, int) Running(string name, bool running, IMachine machine) =>
        (running ? $"{name} is running" : $"{name} isn't running",
         () => machine.ProcessRunning(name) == running ? null : running ? "it isn't" : "it still is", 0);

    private static (string, Func<string?>, int) Web(string url, int status, string? text, IMachine machine) =>
        ($"{url} answers {status}{(text is null ? "" : $" with \"{text}\"")}",
         () =>
         {
             var (found, body) = machine.Get(url);
             if (found == 0)
                 return $"it couldn't be reached: {body}";
             if (found != status)
                 return $"it answered {found}";
             return text is null || body.Contains(text, StringComparison.Ordinal) ? null : "the page doesn't have it";
         }, 0);

    // Numbers, such as how long a step took: at most or at least another.
    private static (string, Func<string?>, int) Compare(string value, string limit, bool atMost) =>
        ($"It is {(atMost ? "at most" : "at least")} {limit}",
         () =>
         {
             if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                 return $"{value} isn't a number";
             var bound = double.Parse(limit, CultureInfo.InvariantCulture);
             return (atMost ? number <= bound : number >= bound) ? null : $"it is {value}";
         }, 0);

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string? Version(string exe, string expected)
    {
        if (!File.Exists(exe))
            return "it isn't there";
        var version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
        return version.StartsWith(expected, StringComparison.Ordinal) ? null : $"it is version {version}";
    }

    private static (string, Func<string?>, int) Output(string program, IReadOnlyList<string> arguments, string text, IMachine machine) =>
        ($"Running \"{Path.GetFileName(program)} {string.Join(" ", arguments)}\" says {text}",
         () =>
         {
             var output = machine.Run(program, arguments).Output;
             return output.Contains(text, StringComparison.Ordinal) ? null : $"it printed: {output.Trim()}";
         }, 0);

    // The service writes a heartbeat every 10 seconds, so this waits for the next one.
    private static string? LastLine(string file, string text)
    {
        var line = File.Exists(file) ? ReadShared(file).TrimEnd('\r', '\n').Split('\n')[^1].TrimEnd('\r') : "";
        return line.Contains(text, StringComparison.Ordinal) ? null : $"the latest one says: {line}";
    }

    // A file a service is writing, such as its log, is read without stopping it.
    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
