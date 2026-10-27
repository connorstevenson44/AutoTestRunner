using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

// Reads suite.json, elements.json, the macros and the cases.
public sealed partial class Suite
{
    /// <summary>
    /// Reads a suite folder and checks every step against the vocabulary, so a misspelt step, element or macro is
    /// reported before anything runs. Throws SuiteException with every problem found.
    /// </summary>
    public static Suite Load(string folder)
    {
        folder = Path.GetFullPath(folder);
        var problems = new List<string>();
        var suitePath = Path.Combine(folder, "suite.json");
        if (!File.Exists(suitePath))
            throw new SuiteException([$"There is no suite.json in {folder}."]);
        var root = Read(suitePath, problems) ?? new JsonObject();

        var app = root["app"] as JsonObject ?? new JsonObject();
        if (app["exe"] is null)
            problems.Add("suite.json: app.exe is missing: the program to start.");
        if (app["window"] is null)
            problems.Add("suite.json: app.window is missing: the title of the app's main window.");

        var helpers = new Dictionary<string, Helper>();
        foreach (var (name, helper) in root["helpers"] as JsonObject ?? [])
        {
            if (string.Equals(name, VariableScope.ReadFile, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"suite.json: there can't be a helper called {name}: {VariableScope.ReadFile} is built in, and gives a file's text.");
                continue;
            }
            try
            {
                helpers[name] = Helper.From(name, helper);
            }
            catch (ArgumentException ex)
            {
                problems.Add($"suite.json: helper {name}: {ex.Message}");
            }
        }

        var elements = new Dictionary<string, JsonNode>();
        var elementsPath = Path.Combine(folder, "elements.json");
        if (File.Exists(elementsPath) && Read(elementsPath, problems) is { } elementsRoot)
            foreach (var (name, element) in elementsRoot)
                if (name != "$schema" && element is not null)
                    elements[name] = element;

        var macros = new Dictionary<string, Macro>();
        foreach (var file in Files(folder, "macros"))
        {
            if (Read(file, problems) is not { } macro)
                continue;
            var name = macro["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(file);
            macros[name] = Macro.From(name, file, macro);
        }

        var cases = new List<Case>();
        foreach (var file in Files(folder, "cases"))
        {
            if (Read(file, problems) is not { } item)
                continue;
            var id = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"^\d+[-_ ]*", "");
            cases.Add(new Case(
                cases.Count + 1,
                id,
                item["name"]?.GetValue<string>() ?? id,
                file,
                (item["needs"] as JsonArray ?? []).Select(need => need!.GetValue<string>()).ToList(),
                item["needsNote"]?.GetValue<string>(),
                item["covers"]?.GetValue<string>(),
                item["steps"] as JsonArray ?? [])
            {
                Review = item["review"]?.GetValue<string>(),
                Tags = (item["tags"] as JsonArray ?? []).Select(tag => tag!.GetValue<string>()).ToList(),
                Reset = item["reset"] as JsonArray ?? [],
                AfterReset = item["afterReset"]?.GetValue<string>(),
            });
        }

        var screen = root["screen"] is JsonObject size
            ? ((int)size["width"]!, (int)size["height"]!)
            : ((int, int)?)null;

        var builder = root["builder"] as JsonObject ?? [];
        var watch = new List<WatchHook>();
        foreach (var node in builder["watch"] as JsonArray ?? [])
        {
            if (node is not JsonObject hook || hook["element"]?.GetValue<string>() is not { } element)
            {
                problems.Add("suite.json: each of builder.watch needs element, the control to watch.");
                continue;
            }
            watch.Add(new WatchHook(element, hook["call"]?.GetValue<string>(), hook["check"]?.GetValue<bool>() ?? false));
        }
        var afterClick = new List<ClickHook>();
        foreach (var node in builder["afterClick"] as JsonArray ?? [])
        {
            if (node is not JsonObject hook || hook["element"]?.GetValue<string>() is not { } element || hook["call"]?.GetValue<string>() is not { } call)
            {
                problems.Add("suite.json: each of builder.afterClick needs element, the control clicked, and call, the macro to run after it.");
                continue;
            }
            var delay = hook["delay"] is { } seconds ? double.Parse(seconds.ToString(), CultureInfo.InvariantCulture) : 0;
            afterClick.Add(new ClickHook(element, call, hook["with"]?.DeepClone() as JsonObject, delay));
        }

        var (databases, queues, software) = (ReadDatabases(root, problems), ReadQueues(root, problems), ReadSoftware(root, problems));

        var suite = new Suite
        {
            Folder = folder,
            Name = root["name"]?.GetValue<string>() ?? Path.GetFileName(folder),
            App = new AppSettings(
                app["exe"]?.GetValue<string>() ?? "",
                app["window"]?.GetValue<string>() ?? "",
                app["runAsAdmin"]?.GetValue<bool>() ?? false,
                app["status"]?.GetValue<string>(),
                app["arguments"]?.GetValue<string>(),
                app["startWindow"]?.GetValue<string>()),
            // script was called powershell before it could be C# as well.
            Reset = new ResetSettings((root["reset"]?["script"] ?? root["reset"]?["powershell"])?.GetValue<string>(), root["reset"]?["leftovers"]?.GetValue<string>()),
            Variables = root["variables"] as JsonObject ?? [],
            Requires = (root["requires"] as JsonArray ?? []).Select(path => path!.GetValue<string>()).ToList(),
            Helpers = helpers,
            Elements = elements,
            Macros = macros,
            Cases = cases,
            Screen = screen,
            Watch = watch,
            AfterClick = afterClick,
            Databases = databases,
            Queues = queues,
            Software = software,
        };
        suite.Check(problems);
        if (problems.Count > 0)
            throw new SuiteException(problems);
        return suite;
    }

    private static Dictionary<string, DatabaseSettings> ReadDatabases(JsonObject root, List<string> problems)
    {
        var databases = new Dictionary<string, DatabaseSettings>(StringComparer.Ordinal);
        foreach (var (name, node) in root["databases"] as JsonObject ?? [])
        {
            var (kind, connection) = (Words(node?["kind"]), Words(node?["connection"]));
            // A kind with ${...} in it, such as one read from the app's .config file, is checked when it is used.
            if (kind is null || !DatabaseSettings.Kinds.ContainsKey(kind) && !kind.Contains("${", StringComparison.Ordinal))
                problems.Add($"suite.json: database {name} needs kind, one of: {string.Join(", ", DatabaseSettings.Kinds.Keys)}.");
            else if (connection is null)
                problems.Add($"suite.json: database {name} needs connection, its connection string.");
            else
                databases[name] = new DatabaseSettings(name, kind, connection);
        }
        return databases;
    }

    private static Dictionary<string, QueueSettings> ReadQueues(JsonObject root, List<string> problems)
    {
        var queues = new Dictionary<string, QueueSettings>(StringComparer.Ordinal);
        foreach (var (name, node) in root["queues"] as JsonObject ?? [])
        {
            var (kind, queue) = (Words(node?["kind"]), Words(node?["queue"]));
            if (kind is null || !QueueSettings.Kinds.ContainsKey(kind))
            {
                problems.Add($"suite.json: queue {name} needs kind, one of: {string.Join(", ", QueueSettings.Kinds.Keys)}.");
                continue;
            }
            var settings = new QueueSettings(name, kind, queue ?? "")
            {
                Connection = Words(node?["connection"]),
                Channel = Words(node?["channel"]),
                QueueManager = Words(node?["queueManager"]),
                User = Words(node?["user"]),
                Password = Words(node?["password"]),
                Format = Words(node?["format"]),
            };
            var missing = new List<string>();
            if (queue is null)
                missing.Add("queue, the queue's name" + (kind == "msmq" ? @" or path, such as .\private$\orders" : ""));
            if (kind != "msmq" && settings.Connection is null)
                missing.Add(kind == "rabbitMq" ? "connection, such as amqp://guest:guest@localhost:5672/" : "connection, such as localhost(1414)");
            if (kind == "ibmMq" && settings.Channel is null)
                missing.Add("channel, such as DEV.APP.SVRCONN");
            if (kind == "ibmMq" && settings.QueueManager is null)
                missing.Add("queueManager, such as QM1");
            foreach (var what in missing)
                problems.Add($"suite.json: queue {name} needs {what}.");
            if (settings.Format is { } format && format is not ("xml" or "text"))
                problems.Add($"suite.json: queue {name}'s format is xml or text, not {format}.");
            if (missing.Count == 0)
                queues[name] = settings;
        }
        return queues;
    }

    // The software suite.json says the suite needs, with what is wrong with it.
    private static Dictionary<string, SoftwareSettings> ReadSoftware(JsonObject root, List<string> problems)
    {
        var software = new Dictionary<string, SoftwareSettings>(StringComparer.Ordinal);
        foreach (var (name, node) in root["software"] as JsonObject ?? [])
        {
            string? Text(string key) => node?[key] is JsonValue value && value.TryGetValue(out string? text) ? text : node?[key] is JsonValue number ? number.ToString() : null;
            var item = new SoftwareSettings(name, Text("title") ?? name)
            {
                Winget = Text("winget"),
                Version = Text("version"),
                WindowsFeature = Text("windowsFeature"),
                Script = Text("script"),
                Args = (node?["args"] as JsonArray ?? []).Select(arg => arg?.ToString() ?? "").ToList(),
                Present = node?["present"] as JsonObject,
                Administrator = node?["administrator"]?.GetValue<bool>() ?? false,
            };
            var ways = new[] { item.Winget, item.WindowsFeature, item.Script }.Count(way => way is not null);
            if (ways != 1)
                problems.Add($"suite.json: software {name} needs one of winget, a package id; windowsFeature, such as MSMQ-Server; or script, a .ps1 or .cs file that installs it.");
            else if (item.Script is not null && item.Present is null)
                problems.Add($"suite.json: software {name} is installed by a script, so it needs present, a check that passes when it is there, such as {{ \"service\": \"RabbitMQ\", \"state\": \"Running\" }}.");
            else if (item.WindowsFeature is not null && item.Version is not null)
                problems.Add($"suite.json: software {name} is a Windows feature, which has no version.");
            else
                software[name] = item;
        }
        return software;
    }

    // A property's text, or null when it isn't there or isn't text.
    private static string? Words(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static IEnumerable<string> Files(string folder, string sub)
    {
        var path = Path.Combine(folder, sub);
        return Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Order(StringComparer.Ordinal) : [];
    }

    private static JsonObject? Read(string path, List<string> problems)
    {
        try
        {
            return ScriptJson.ReadFile(path) as JsonObject
                ?? throw new JsonException("It must be a JSON object.");
        }
        catch (JsonException ex)
        {
            problems.Add($"{Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }
}
