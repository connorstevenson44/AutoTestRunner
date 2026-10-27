using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

public sealed partial class Runner
{
    // Steps that run programs and scripts, change files, and start and stop services. False when the step is another kind.
    private bool MachineStep(StepAt at)
    {
        var (step, scope, kind) = (at.Step, at.Scope, at.Kind);
        switch (kind)
        {
            case "run":
            {
                var program = SuitePath(at.Text("run")!);
                var arguments = at.Args();
                var environment = (step["env"] as JsonObject ?? []).ToDictionary(pair => pair.Key, pair => scope.Expand(pair.Value?.ToString() ?? ""));
                var line = CommandReportingMachine.CommandLine(program, arguments, environment);
                ShowStep(at.Line(ActivityKind.Command, Part.Verb("Run"), Part.File(program)) with { Command = line });
                // What it prints goes in the log as it prints it, as the case may need to show it.
                var output = _machine.Run(program, arguments, environment);
                KeepOutput(step, scope, output);
                break;
            }

            case "powershell":
            {
                // The script isn't expanded: PowerShell has ${...} of its own. It gets the step's args as $args.
                var script = step["powershell"] is JsonArray lines ? string.Join("\r\n", lines.Select(line => line!.ToString())) : step["powershell"]!.ToString();
                var arguments = at.Args();
                ShowStep(at.Line(ActivityKind.Command, Part.Verb("Run"), Part.Word("PowerShell")) with { Command = CommandReportingMachine.CommandLine("powershell", arguments, null) + "\n" + script });
                var output = _machine.PowerShell(script, arguments);
                if (output.ExitCode != 0)
                    FailStep(new StepFailure($"A PowerShell step failed with exit code {output.ExitCode}: {output.Output}", "exit 0", $"exit {output.ExitCode}"));
                KeepOutput(step, scope, output);
                break;
            }

            case "script":
            {
                // A .ps1 or .cs file in the suite, run as the reset script is, so it can be opened and changed on its own.
                var file = Path.GetFullPath(Path.Combine(_suite.Folder, at.Text("script")!));
                if (!File.Exists(file))
                    throw new ScriptException($"There is no script {file}.");
                var arguments = at.Args();
                var (program, line) = ScriptFile.Command(file, arguments);
                ShowStep(at.Line(ActivityKind.Command, Part.Verb("Run"), Part.File(file)) with { Command = CommandReportingMachine.CommandLine(program, line, null) });
                var output = ScriptFile.Run(_machine, file, arguments, folder: _suite.Folder);
                if (output.ExitCode != 0)
                    FailStep(new StepFailure(ScriptFile.Failed(file, output), "exit 0", $"exit {output.ExitCode}"));
                KeepOutput(step, scope, output);
                break;
            }

            case "copy":
            {
                var (from, to) = (SuitePath(at.Required("copy")), SuitePath(at.Required("to")));
                ShowStep(at.Line(ActivityKind.File, Part.Verb("Copy"), Part.File(from), Part.Word("to"), Part.File(to)));
                Files(() => Copy(from, to));
                break;
            }

            case "delete":
            {
                var path = SuitePath(at.Required("delete"));
                ShowStep(at.Line(ActivityKind.File, Part.Verb("Delete"), Part.File(path)));
                Files(() =>
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, recursive: true);
                    else if (File.Exists(path))
                        File.Delete(path);
                });
                break;
            }

            case "writeFile":
            {
                var path = SuitePath(at.Required("writeFile"));
                var text = step["text"] is JsonArray lines ? string.Join("\r\n", lines.Select(line => scope.Expand(line!.ToString()))) + "\r\n" : at.Text("text") ?? "";
                var append = step["append"]?.GetValue<bool>() ?? false;
                ShowStep(at.Line(ActivityKind.File, Part.Verb(append ? "Add to" : "Write"), Part.File(path)));
                Files(() =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    if (append)
                        File.AppendAllText(path, text);
                    else
                        File.WriteAllText(path, text);
                });
                break;
            }

            case "unzip":
            {
                var (zip, to) = (SuitePath(at.Required("unzip")), SuitePath(at.Required("to")));
                ShowStep(at.Line(ActivityKind.File, Part.Verb("Unzip"), Part.File(zip), Part.Word("to"), Part.File(to)));
                Files(() =>
                {
                    if (Directory.Exists(to))
                        Directory.Delete(to, recursive: true);
                    ZipFile.ExtractToDirectory(zip, to);
                });
                break;
            }

            case "startService":
            case "stopService":
            {
                var name = at.Required(kind);
                var start = kind == "startService";
                ShowStep(at.Line(ActivityKind.Command, Part.Verb(start ? "Start" : "Stop"), Part.Service(name)));
                try
                {
                    _machine.SetServiceRunning(name, start);
                }
                catch (Exception ex) when (ex is not StopRunException)
                {
                    FailStep(new StepFailure($"The service {name} didn't {(start ? "start" : "stop")}: {ex.Message}", start ? "Running" : "Stopped", ex.Message));
                }
                break;
            }

            case "setJsonValue":
            {
                var (file, path, value) = (SuitePath(at.Required("setJsonValue")), at.Required("path"), at.Required("to"));
                ShowStep(at.Line(ActivityKind.File, Part.Verb("Set"), Part.File(file), Part.Word(path), Part.Word("="), Part.Value(value)));
                Files(() => SetJsonValue(file, path, value));
                break;
            }

            case "setAppSetting":
            {
                var (file, key, value) = (SuitePath(at.Required("setAppSetting")), at.Required("key"), at.Required("to"));
                ShowStep(at.Line(ActivityKind.File, Part.Verb("Set"), Part.File(file), Part.Word(key), Part.Word("="), Part.Value(value)));
                Files(() => SetAppSetting(file, key, value));
                break;
            }

            default:
                return false;
        }
        return true;
    }

    // A step that changes files: one that can't, such as a file in use, fails the step and the case carries on.
    private void Files(Action change)
    {
        try
        {
            change();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or System.Xml.XmlException)
        {
            FailStep(new StepFailure(ex.Message));
        }
    }

    // A file into a folder or onto a file, or a folder in place of another.
    private static void Copy(string from, string to)
    {
        if (Directory.Exists(from))
        {
            if (Directory.Exists(to))
                Directory.Delete(to, recursive: true);
            foreach (var folder in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
            return;
        }
        if (!File.Exists(from))
            throw new FileNotFoundException($"There is no {from} to copy.");
        var target = Directory.Exists(to) ? Path.Combine(to, Path.GetFileName(from)) : to;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(from, target, overwrite: true);
    }

    /// <summary>
    /// Sets a JSON setting by its path, such as Orders:Region, making the objects on the way when they aren't there. A
    /// number or true and false stay a number or a true and false when the old value was one.
    /// </summary>
    public static void SetJsonValue(string file, string path, string value)
    {
        var root = ScriptJson.ReadFile(file) as JsonObject
            ?? throw new JsonException($"{Path.GetFileName(file)} isn't a JSON object.");
        var keys = path.Split(':');
        var node = root;
        foreach (var key in keys[..^1])
        {
            var name = node.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Key ?? key;
            if (node[name] is not JsonObject inner)
                node[name] = inner = new JsonObject();
            node = inner;
        }
        var last = node.FirstOrDefault(pair => string.Equals(pair.Key, keys[^1], StringComparison.OrdinalIgnoreCase)).Key ?? keys[^1];
        node[last] = node[last] switch
        {
            JsonValue old when old.GetValueKind() == JsonValueKind.Number && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            JsonValue old when old.GetValueKind() is JsonValueKind.True or JsonValueKind.False && bool.TryParse(value, out var flag) => flag,
            _ => value,
        };
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Sets an appSettings value in a .config file, adding it when it isn't there, and keeping the file's layout.</summary>
    public static void SetAppSetting(string file, string key, string value)
    {
        var document = XDocument.Load(file, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new System.Xml.XmlException($"{Path.GetFileName(file)} is empty.");
        var settings = root.Element("appSettings");
        if (settings is null)
            root.Add(settings = new XElement("appSettings"));
        var add = settings.Elements("add").FirstOrDefault(element => (string?)element.Attribute("key") == key);
        if (add is null)
            settings.Add(add = new XElement("add", new XAttribute("key", key)));
        add.SetAttributeValue("value", value);
        document.Save(file, SaveOptions.DisableFormatting);
    }
}
