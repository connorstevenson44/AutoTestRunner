using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// The built-in checks a checkpoint can make. Each says what it looks at, what it expects and what it found, so the
/// panel can set them side by side; what it found shows when it passes too.
/// </summary>
public static class Checks
{
    public const string Absent = "<absent>";

    // How a check of a state compares, which the panel leaves out: Contoso.Orders-main | Running.
    private const string Is = "is";

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
        ControlProbe Control() => (context.Controls ?? throw new ScriptException($"The {kind} check needs the app."))(node[kind]!);
        // A value is named by the variable it came from, such as deploy.code.
        Subject Kept() => new(SubjectKind.Value, Named(node[kind]?.ToString() ?? ""));

        var check = kind switch
        {
            "fileExists" => Exists(subject, wanted: true),
            "fileMissing" => Exists(subject, wanted: false),
            "service" => Service(subject, Optional("state"), machine),
            "scheduledTask" => Task(subject, node["exists"]?.GetValue<bool>() ?? true, machine),
            "jsonValue" => Setting(File(subject, SubjectKind.Setting, Text("path")), Text("equals"), () => JsonValue(subject, Text("path"))),
            "appSetting" => Setting(File(subject, SubjectKind.Setting, Text("key")), Text("equals"), () => XmlValue(subject, "appSettings", "key", Text("key"), "value")),
            "connectionString" => new Shape(File(subject, SubjectKind.Setting, Text("name")), Is, "there", () =>
                XmlValue(subject, "connectionStrings", "name", Text("name"), "connectionString") == Absent ? new Seen(false, "absent") : new Seen(true, "there")),
            "fileContains" => new Shape(File(subject, SubjectKind.File), "contains", Text("text"), () => FileHas(subject, Text("text"))),
            "logged" => new Shape(File(subject, SubjectKind.Log), "contains", Text("text"), () => FileHas(subject, Text("text"))),
            "fileCount" => Count(subject, Optional("pattern") ?? "*", int.Parse(Text("equals"), CultureInfo.InvariantCulture), node["folders"]?.GetValue<bool>() ?? false,
                node["recursive"]?.GetValue<bool>() ?? false),
            "fileVersion" => new Shape(File(subject, SubjectKind.File, "version"), "starts with", Text("startsWith"), () => Version(subject, Text("startsWith"))),
            "output" => Output(subject, [.. (node["args"] as JsonArray ?? []).Select(arg => scope.Expand(arg!.ToString()))], Text("contains"), machine),
            "lastLine" => new Shape(File(subject, SubjectKind.Log, "last line"), "contains", Text("contains"), () => LastLine(subject, Text("contains")), 30),
            "equals" => new Shape(Kept(), "=", Text("to"), () => Whole(subject, subject == Text("to"))),
            "matches" => new Shape(Kept(), "matches", Text("regex"), () => Whole(subject, Regex.IsMatch(subject, Text("regex")))),
            "contains" => new Shape(Kept(), "contains", Text("text"), () => Has(subject, Text("text"))),
            "lacks" => new Shape(Kept(), "lacks", Text("text"), () => Lacks(subject, Text("text"))),
            "shows" => Shows(Control()),
            "enabled" or "disabled" or "ticked" or "notTicked" or "selected" or "focused" => State(kind, Control()),
            "says" => Says(Control(), Optional("equals"), Optional("startsWith"), Optional("contains"), Optional("matches")),
            "rows" => Rows(Control(), int.Parse(Text("equals"), CultureInfo.InvariantCulture)),
            "nothingLeft" => new Shape(new Subject(SubjectKind.Machine, "Leftovers", Tip: "What the suite's leftovers script finds on this computer"), "=", "none",
                () => context.Leftovers() is [_, ..] found ? new Seen(false, string.Join(", ", found)) : new Seen(true, "none")),
            "fileMatches" => new Shape(File(subject, SubjectKind.File), "matches", Text("regex"), () => FileMatches(subject, Text("regex"))),
            "registry" => Setting(new Subject(SubjectKind.Registry, Leaf(subject), Text("value"), subject), Optional("equals") ?? "",
                () => machine.RegistryValue(subject, Text("value")) ?? Absent),
            "process" => Running(subject, node["running"]?.GetValue<bool>() ?? true, machine),
            "eventLog" => new Shape(new Subject(SubjectKind.EventLog, subject, Optional("source")), "contains", Text("contains"), () =>
                machine.EventLogged(subject, Optional("source"), Text("contains"), context.Since ?? DateTime.Now.AddMinutes(-10)) ? new Seen(true, "logged") : new Seen(false, "not logged")),
            "web" => Web(subject, Optional("status") is { } status ? int.Parse(status, CultureInfo.InvariantCulture) : 200, Optional("contains"), machine),
            "clipboard" => new Shape(new Subject(SubjectKind.Clipboard, "Clipboard"), "contains", subject, () => Has(machine.Clipboard(), subject)),
            "atMost" => Compare(Kept(), subject, Text("to"), atMost: true),
            "atLeast" => Compare(Kept(), subject, Text("to"), atMost: false),
            "dbValue" => DbValue((context.Database ?? throw new ScriptException("The dbValue check needs the suite's databases."))(subject), Text("sql"),
                Parameters(node["params"], scope), Optional("equals"), Optional("contains"), Optional("matches"), Optional("rows"), Connections(context)),
            "queue" => Queue((context.Queue ?? throw new ScriptException("The queue check needs the suite's queues."))(subject), Optional("count"), Optional("has"), Connections(context)),
            _ => throw new ScriptException($"There is no {kind} check."),
        };

        var within = node["within"] is { } seconds ? TimeSpan.FromSeconds(double.Parse(scope.Expand(seconds.ToString()), CultureInfo.InvariantCulture)) : TimeSpan.FromSeconds(check.Seconds);
        return new Check(Optional("label") ?? Label(check.Subject, check.How, check.Expected), check.Subject, check.How, check.Expected, within, check.Test);
    }

    /// <summary>A query's or check's params, such as { "id": "${order}" }, with their variables filled in.</summary>
    public static IReadOnlyDictionary<string, string> Parameters(JsonNode? node, VariableScope scope) =>
        (node as JsonObject ?? []).ToDictionary(pair => pair.Key.TrimStart('@', ':'), pair => scope.Expand(pair.Value?.ToString() ?? ""), StringComparer.Ordinal);

    private static IConnections Connections(CheckContext context) =>
        context.Connections ?? throw new ScriptException("autotest can't reach databases or queues here.");

    // The first value a query reads, against what is wanted; with none of them, that it reads a row at all.
    private static Shape DbValue(DatabaseSettings database, string sql, IReadOnlyDictionary<string, string> parameters, string? equals, string? contains, string? matches, string? rows,
        IConnections connections)
    {
        var subject = Sources.Subject(database) with { Detail = Short(sql.Trim()) };
        QueryResult Read() => connections.Query(database, sql, parameters, TimeSpan.FromSeconds(30));
        if (rows is not null)
        {
            return new(subject with { Detail = "rows" }, "=", rows, () =>
            {
                var found = Read().Count.ToString(CultureInfo.InvariantCulture);
                return new Seen(found == rows, found);
            });
        }
        Seen Value(Func<string, bool> test)
        {
            var result = Read();
            var full = result.Rows.Count > 1 ? result.Table() : null;
            return new Seen(result.Rows.Count > 0 && test(result.First), result.Rows.Count == 0 ? "no rows" : Short(result.First), full);
        }
        if (equals is not null)
            return new(subject, "=", equals, () => Value(found => found == equals));
        if (contains is not null)
            return new(subject, "contains", contains, () => Value(found => found.Contains(contains, StringComparison.Ordinal)));
        if (matches is not null)
            return new(subject, "matches", matches, () => Value(found => Regex.IsMatch(found, matches)));
        return new(subject, Is, "there", () => Value(_ => true));
    }

    // How many messages are waiting, or that one of them has the text, without taking them off the queue.
    private static Shape Queue(QueueSettings queue, string? count, string? has, IConnections connections)
    {
        if (count is not null)
        {
            return new(Sources.Subject(queue, "messages"), "=", count, () =>
            {
                var found = connections.Count(queue).ToString(CultureInfo.InvariantCulture);
                return new Seen(found == count, found);
            });
        }
        var wanted = has ?? "";
        return new(Sources.Subject(queue, "a message"), "contains", wanted, () =>
        {
            var waiting = connections.Peek(queue, 500);
            if (waiting.FirstOrDefault(body => body.Contains(wanted, StringComparison.Ordinal)) is { } found)
                return new Seen(true, Short(found), Long(found));
            return new Seen(false, waiting.Count == 0 ? "no messages" : $"{waiting.Count} message{(waiting.Count == 1 ? "" : "s")}, none with it",
                waiting.Count == 0 ? null : string.Join("\n\n", waiting.Take(20)));
        });
    }

    /// <summary>A check in words, for the log when the script gives it no label: orders.json orders:1001:total = 26.00.</summary>
    public static string Label(Subject subject, string how, string expected) =>
        $"{subject.Name}{(subject.Detail is null ? "" : " " + subject.Detail)} {how} {expected}";

    /// <summary>Whether how is one the panel leaves out before what is expected, as it reads as a state: Running, there.</summary>
    public static bool IsState(string how) => how is Is or "=";

    // What a check looks at, how it compares, what it wants, its test, and the seconds it may take to come true.
    private sealed record Shape(Subject Subject, string How, string Expected, Func<Seen> Test, int Seconds = 0);

    private static Subject File(string path, SubjectKind kind, string? detail = null) => new(kind, Leaf(path), detail, path);

    private static string Leaf(string path) => Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path;

    // "${deploy.code}" as deploy.code, the variable a value came from; anything else as it is written, cut short.
    private static string Named(string raw) =>
        Regex.Match(raw, @"^\$\{([^}]+)\}$") is { Success: true } match ? match.Groups[1].Value : raw.Length <= 32 ? raw : raw[..31] + "…";

    private static Shape Exists(string path, bool wanted) =>
        new(File(path, Directory.Exists(path) || Path.GetExtension(path).Length == 0 ? SubjectKind.Folder : SubjectKind.File), Is, wanted ? "there" : "missing", () =>
        {
            var there = System.IO.File.Exists(path) || Directory.Exists(path);
            return new Seen(there == wanted, there ? "there" : "missing");
        });

    // A service can take a few seconds to start or go, so this waits a little for it.
    private static Shape Service(string name, string? state, IMachine machine)
    {
        var absent = state is null or "" or "absent";
        return new(new Subject(SubjectKind.Service, name), Is, absent ? "absent" : state!, () =>
        {
            var found = machine.ServiceState(name);
            var passed = absent ? found is null : string.Equals(found, state, StringComparison.OrdinalIgnoreCase);
            return new Seen(passed, found ?? "absent");
        }, 20);
    }

    private static Shape Task(string path, bool exists, IMachine machine) =>
        new(new Subject(SubjectKind.Task, Leaf(path), Tip: path), Is, exists ? "there" : "absent", () =>
        {
            var found = machine.TaskExists(path);
            return new Seen(found == exists, found ? "there" : "absent");
        });

    private static Shape Setting(Subject subject, string expected, Func<string> read) =>
        new(subject, expected == Absent ? Is : "=", expected == Absent ? "absent" : expected, () =>
        {
            var found = read();
            return new Seen(found == expected, found == Absent ? "absent" : found);
        });

    /// <summary>A JSON setting by its path, such as Orders:Region; an array as its items with spaces between.</summary>
    public static string JsonValue(string file, string path)
    {
        JsonNode? node = ScriptJson.ReadFile(file);
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

    // The line of the file that has the text, as what was found.
    private static Seen FileHas(string file, string text) => !System.IO.File.Exists(file) ? new Seen(false, "no file") : Has(ReadShared(file), text);

    private static Seen FileMatches(string file, string pattern)
    {
        if (!System.IO.File.Exists(file))
            return new Seen(false, "no file");
        var text = ReadShared(file).ReplaceLineEndings("\n");
        var match = Regex.Match(text, pattern, RegexOptions.Multiline);
        return match.Success ? new Seen(true, Short(LineAt(text, match.Index))) : new Seen(false, "no match", Long(text));
    }

    // recursive counts the folders inside it too.
    private static Shape Count(string folder, string pattern, int expected, bool folders, bool recursive) =>
        new(File(folder, SubjectKind.Folder, folders ? "folders" : pattern == "*" ? "files" : pattern), "=", $"{expected}", () =>
        {
            var depth = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var found = !Directory.Exists(folder) ? 0 : folders ? Directory.GetDirectories(folder, pattern, depth).Length : Directory.GetFiles(folder, pattern, depth).Length;
            return new Seen(found == expected, $"{found}");
        });

    private static Shape Shows(ControlProbe control) =>
        new(Badge(control), Is, "shown", () => control.Find() is null ? new Seen(false, "missing") : new Seen(true, "shown"));

    // What a control is like now: whether it can be used, is ticked, picked or has the keyboard.
    private static Shape State(string kind, ControlProbe control)
    {
        var (wanted, read) = kind switch
        {
            "enabled" => ("enabled", (Func<IControl, string>)(found => found.IsEnabled ? "enabled" : "greyed out")),
            "disabled" => ("greyed out", found => found.IsEnabled ? "enabled" : "greyed out"),
            "ticked" => ("ticked", found => found.IsChecked == true ? "ticked" : "not ticked"),
            "notTicked" => ("not ticked", found => found.IsChecked == true ? "ticked" : "not ticked"),
            "selected" => ("selected", found => found.IsSelected ? "selected" : "not selected"),
            _ => ("focused", found => found.HasFocus ? "focused" : "not focused"),
        };
        return new(Badge(control), Is, wanted, () =>
        {
            if (control.Find() is not { } found)
                return new Seen(false, "missing");
            var state = read(found);
            return new Seen(state == wanted, state);
        });
    }

    private static Shape Says(ControlProbe control, string? equals, string? startsWith, string? contains, string? matches)
    {
        var wanted = equals ?? startsWith ?? contains ?? matches ?? "";
        var how = equals is not null ? "=" : startsWith is not null ? "starts with" : contains is not null ? "contains" : "matches";
        return new(Badge(control), how, wanted, () =>
        {
            if (control.Find() is not { } found)
                return new Seen(false, "missing");
            var text = found.Value ?? found.Name;
            var holds = (equals is null || text == equals) && (startsWith is null || text.StartsWith(startsWith, StringComparison.Ordinal))
                && (contains is null || text.Contains(contains, StringComparison.Ordinal)) && (matches is null || Regex.IsMatch(text, matches));
            return Whole(text, holds);
        });
    }

    private static Shape Rows(ControlProbe control, int expected) =>
        new(Badge(control) with { Detail = "rows" }, "=", $"{expected}", () =>
        {
            if (control.Find() is not { } found)
                return new Seen(false, "missing");
            var rows = control.Rows(found);
            return new Seen(rows == expected, $"{rows}");
        });

    private static Subject Badge(ControlProbe control) => new(SubjectKind.Control, control.Target.Label, Tip: Part.Control(control.Target).Tip);

    private static Shape Running(string name, bool running, IMachine machine) =>
        new(new Subject(SubjectKind.Process, name), Is, running ? "running" : "not running", () =>
        {
            var found = machine.ProcessRunning(name);
            return new Seen(found == running, found ? "running" : "not running");
        });

    private static Shape Web(string url, int status, string? text, IMachine machine) =>
        new(new Subject(SubjectKind.Web, url), "answers", text is null ? $"{status}" : $"{status} with \"{text}\"", () =>
        {
            var (found, body) = machine.Get(url);
            if (found == 0)
                return new Seen(false, $"no answer: {body}");
            if (found != status)
                return new Seen(false, $"{found}", Long(body));
            return text is null || body.Contains(text, StringComparison.Ordinal) ? new Seen(true, $"{found}", Long(body)) : new Seen(false, $"{found} without it", Long(body));
        });

    // Numbers, such as how long a step took: at most or at least another.
    private static Shape Compare(Subject subject, string value, string limit, bool atMost) =>
        new(subject, atMost ? "≤" : "≥", limit, () =>
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return new Seen(false, $"{Short(value)} (not a number)");
            var bound = double.Parse(limit, CultureInfo.InvariantCulture);
            return new Seen(atMost ? number <= bound : number >= bound, value);
        });

    private static Seen Version(string exe, string expected)
    {
        if (!System.IO.File.Exists(exe))
            return new Seen(false, "no file");
        var version = FileVersionInfo.GetVersionInfo(exe).FileVersion ?? "";
        return new Seen(version.StartsWith(expected, StringComparison.Ordinal), version);
    }

    private static Shape Output(string program, IReadOnlyList<string> arguments, string text, IMachine machine) =>
        new(new Subject(SubjectKind.Program, $"{Path.GetFileName(program)} {string.Join(" ", arguments)}".Trim(), Tip: program), "contains", text,
            () => Has(machine.Run(program, arguments).Output, text));

    // The service writes a heartbeat every 10 seconds, so this waits for the next one.
    private static Seen LastLine(string file, string text)
    {
        var line = System.IO.File.Exists(file) ? ReadShared(file).TrimEnd('\r', '\n').Split('\n')[^1].TrimEnd('\r') : "";
        return new Seen(line.Contains(text, StringComparison.Ordinal), line.Length == 0 ? "nothing" : Short(line));
    }

    // Whether a text has another in it: found, the line it is on; not, the text's last line, with all of it to point at.
    private static Seen Has(string text, string wanted)
    {
        var at = text.IndexOf(wanted, StringComparison.Ordinal);
        if (at >= 0)
            return new Seen(true, Short(LineAt(text, at)), Long(text));
        return new Seen(false, text.Trim().Length == 0 ? "nothing" : Short(LastLineOf(text)), Long(text));
    }

    private static Seen Lacks(string text, string unwanted)
    {
        var found = Has(text, unwanted);
        return found.Passed ? found with { Passed = false } : new Seen(true, "not in it", found.Full);
    }

    // A value as it is, short, with all of it to point at when it is long.
    private static Seen Whole(string text, bool passed) => new(passed, Short(text.Trim()), Long(text));

    private static string LineAt(string text, int at)
    {
        var start = text.LastIndexOf('\n', Math.Max(at - 1, 0)) + 1;
        var end = text.IndexOf('\n', at);
        return text[start..(end < 0 ? text.Length : end)].Trim();
    }

    private static string LastLineOf(string text) => text.Trim().Split('\n')[^1].Trim();

    // A table cell holds a line, and not a long one.
    private static string Short(string text)
    {
        var line = text.Contains('\n') ? text.Trim().Split('\n')[0].Trim() + " …" : text;
        return line.Length <= 120 ? line : line[..119] + "…";
    }

    private static string? Long(string text) => text.Contains('\n') || text.Length > 120 ? text.Trim() : null;

    // A file a service is writing, such as its log, is read without stopping it.
    private static string ReadShared(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
