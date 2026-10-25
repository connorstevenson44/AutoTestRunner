using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>A check ready to run: its label, how long it may take to pass, and a test that returns null when it passes.</summary>
public sealed record Check(string Label, TimeSpan Within, Func<string?> Test);

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
    /// <param name="machine">The machine whose services, tasks and files it checks.</param>
    /// <param name="leftovers">Lists what the case left behind, for the leftovers check.</param>
    /// <param name="shows">Finds a target in the app now, for the shows check: what to call it, and whether it is there.</param>
    public static Check Build(JsonObject node, VariableScope scope, IMachine machine, Func<IReadOnlyList<string>> leftovers,
        Func<JsonNode, (string What, Func<bool> Shown)>? shows = null)
    {
        var kind = Kinds.Of(node, ScriptVocabulary.Checks) ?? throw new ScriptException($"{node.ToJsonString()} isn't a check.");
        string Text(string key) => scope.Expand(node[key]?.ToString() ?? "");
        string? Optional(string key) => node[key] is null ? null : Text(key);
        // A shows check's subject is a target, which shows resolves itself.
        var subject = kind == "shows" ? "" : Text(kind);
        var name = Path.GetFileName(subject);

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
            "shows" => Shows(node["shows"]!, shows ?? throw new ScriptException("The shows check needs the app.")),
            "nothingLeft" => ("Nothing from the tests is left on this computer",
                () => leftovers() is [_, ..] found ? "still there: " + string.Join(", ", found) : null, 0),
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

    private static (string, Func<string?>, int) Shows(JsonNode target, Func<JsonNode, (string What, Func<bool> Shown)> shows)
    {
        var (what, shown) = shows(target);
        return ($"It shows {what}", () => shown() ? null : "it isn't there", 0);
    }

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
