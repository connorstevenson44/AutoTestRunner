using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>
/// A suite: one app's tests, in a folder. suite.json says how to start the app, its variables and how to reset the
/// machine; elements.json names its controls; macros/ holds reusable steps and cases/ the cases, run in file order.
/// </summary>
public sealed class Suite
{

    public required string Folder { get; init; }
    public required string Name { get; init; }
    public required AppSettings App { get; init; }
    public required ResetSettings Reset { get; init; }
    public required JsonObject Variables { get; init; }
    public required IReadOnlyList<string> Requires { get; init; }
    public required IReadOnlyDictionary<string, Helper> Helpers { get; init; }
    public required IReadOnlyDictionary<string, JsonNode> Elements { get; init; }
    public required IReadOnlyDictionary<string, Macro> Macros { get; init; }
    public required IReadOnlyList<Case> Cases { get; init; }
    public (int Width, int Height)? Screen { get; init; }

    /// <summary>The databases steps and checks read and change, by their name in suite.json.</summary>
    public IReadOnlyDictionary<string, DatabaseSettings> Databases { get; init; } = new Dictionary<string, DatabaseSettings>();

    /// <summary>The message queues steps and checks send to and read, by their name in suite.json.</summary>
    public IReadOnlyDictionary<string, QueueSettings> Queues { get; init; } = new Dictionary<string, QueueSettings>();

    /// <summary>The software the suite needs on the computer, by its name in suite.json, which the launcher offers to install.</summary>
    public IReadOnlyDictionary<string, SoftwareSettings> Software { get; init; } = new Dictionary<string, SoftwareSettings>();

    /// <summary>The controls the builder watches while recording, from builder.watch in suite.json.</summary>
    public IReadOnlyList<WatchHook> Watch { get; init; } = [];

    /// <summary>The steps the builder adds after a control is clicked, from builder.afterClick in suite.json.</summary>
    public IReadOnlyList<ClickHook> AfterClick { get; init; } = [];

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
            try
            {
                helpers[name] = new Helper(name, new Regex(helper!["regex"]!.GetValue<string>(), RegexOptions.Multiline));
            }
            catch (Exception ex) when (ex is ArgumentException or NullReferenceException or InvalidOperationException)
            {
                problems.Add($"suite.json: helper {name} needs a regex: {ex.Message}");
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
            if (kind is null || !DatabaseSettings.Kinds.ContainsKey(kind))
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

    /// <summary>
    /// Finds an element another way from now on, as when a run stuck on it has it picked again; elements.json is
    /// written separately.
    /// </summary>
    public void Redefine(string element, JsonNode target)
    {
        if (Elements is not IDictionary<string, JsonNode> elements || !elements.ContainsKey(element))
            throw new ArgumentException($"There is no element called {element} to change.", nameof(element));
        elements[element] = target.DeepClone();
    }

    /// <summary>The case with this id, such as "open-package", or this number.</summary>
    public Case? Find(string idOrNumber) =>
        int.TryParse(idOrNumber, out var number)
            ? Cases.FirstOrDefault(item => item.Number == number)
            : Cases.FirstOrDefault(item => item.Id == idOrNumber);

    /// <summary>Picks cases by number or id, such as "1-5,8" or "open-package". Empty picks them all.</summary>
    public IReadOnlyList<Case> Select(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return Cases;
        var picked = new List<Case>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-', 2);
            if (range.Length == 2 && int.TryParse(range[0], out var from) && int.TryParse(range[1], out var to))
            {
                for (var number = from; number <= to; number++)
                    picked.Add(Find(number.ToString(CultureInfo.InvariantCulture)) ?? throw new SuiteException([$"There is no case {number}."]));
            }
            else
                picked.Add(Find(part) ?? throw new SuiteException([$"There is no case {part}."]));
        }
        return picked;
    }

    /// <summary>The cases with any of these tags, in order; all of them when there are none.</summary>
    public static IReadOnlyList<Case> Tagged(IReadOnlyList<Case> cases, IReadOnlyCollection<string> tags) =>
        tags.Count == 0 ? cases : [.. cases.Where(item => item.Tags.Any(tag => tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))];

    /// <summary>Every tag the cases have, in the order they first appear.</summary>
    public IReadOnlyList<string> Tags => [.. Cases.SelectMany(item => item.Tags).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>A case and the cases it carries on from, in the order they run.</summary>
    public IReadOnlyList<Case> Leading(Case last)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var waiting = new Stack<Case>([last]);
        while (waiting.TryPop(out var item))
        {
            if (!ids.Add(item.Id))
                continue;
            foreach (var need in item.Needs.Append(item.AfterReset).OfType<string>())
                if (Find(need) is { } needed)
                    waiting.Push(needed);
        }
        return [.. Cases.Where(item => ids.Contains(item.Id))];
    }

    /// <summary>The cases that carry on from a case, directly or through others, in order: a change to it changes where they start.</summary>
    public IReadOnlyList<Case> CarryingOn(Case first)
    {
        var after = new HashSet<string>(StringComparer.Ordinal) { first.Id };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var item in Cases)
                if (!after.Contains(item.Id) && (item.Needs.Any(after.Contains) || item.AfterReset is { } setup && after.Contains(setup)))
                    grew = after.Add(item.Id) || grew;
        }
        return [.. Cases.Where(item => item != first && after.Contains(item.Id))];
    }

    /// <summary>
    /// The controls the builder watches: those in builder.watch, those with a macro named waitFor and their name, such
    /// as waitForSavedText for savedText, and the app's status text, waited for with waitForStatusBar when there is one.
    /// </summary>
    public IReadOnlyList<WatchHook> Watched()
    {
        var watched = Watch.ToList();
        bool Has(string element) => watched.Any(hook => hook.Element == element);
        foreach (var element in Elements.Keys)
            if (!Has(element) && Macros.ContainsKey(WaitMacro(element)))
                watched.Add(new WatchHook(element, WaitMacro(element)));
        if (App.Status is { } status && !Has(status))
            watched.Add(new WatchHook(status, Macros.ContainsKey("waitForStatusBar") ? "waitForStatusBar" : null));
        return watched;
    }

    /// <summary>The macro that waits for an element by its name alone, such as waitForSavedText for savedText.</summary>
    public static string WaitMacro(string element) => "waitFor" + char.ToUpperInvariant(element[0]) + element[1..];

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

    // ===== Checking the scripts =====

    private void Check(List<string> problems)
    {
        foreach (var item in Software.Values.Where(item => item.Present is not null))
            CheckChecks(new JsonArray(item.Present!.DeepClone()), $"suite.json: software {item.Name}'s present", problems);
        if (App.Status is { } status && !Elements.ContainsKey(status))
            problems.Add($"suite.json: app.status names {status}, which isn't in elements.json.");
        foreach (var (element, call, where) in Watch.Select(hook => (hook.Element, hook.Call, "builder.watch"))
            .Concat(AfterClick.Select(hook => (hook.Element, (string?)hook.Call, "builder.afterClick"))))
        {
            if (!Elements.ContainsKey(element))
                problems.Add($"suite.json: {where} names {element}, which isn't in elements.json.");
            if (call is not null && !Macros.ContainsKey(call))
                problems.Add($"suite.json: {where} calls {call}, and there is no macro called that.");
        }
        foreach (var (name, definition) in Elements)
            CheckTarget(definition, $"elements.json, {name}", problems);
        var ids = Cases.Select(item => item.Id).ToHashSet();
        foreach (var item in Cases)
        {
            foreach (var need in item.Needs)
            {
                // Cases run in file order, so the case it carries on from must come before it.
                if (!ids.Contains(need))
                    problems.Add($"{Path.GetFileName(item.File)}: needs {need}, and there is no case with that id.");
                else if (Find(need) is { } needed && needed.Number >= item.Number)
                    problems.Add($"{Path.GetFileName(item.File)}: needs {need}, which runs after it. Cases run in file order, so give it a lower number.");
            }
            if (item.AfterReset is { } setup)
            {
                var file = Path.GetFileName(item.File);
                if (Find(setup) is not { } from || int.TryParse(setup, out _))
                    problems.Add($"{file}: afterReset is {setup}, and there is no case with that id.");
                else if (from.Number >= item.Number)
                    problems.Add($"{file}: afterReset is {setup}, which runs after it. Cases run in file order, so give it a lower number.");
                else if (from.Reset.Count == 0)
                    problems.Add($"{file}: afterReset is {setup}, which has no reset steps to take the app back.");
                if (item.Needs.Count > 0)
                    problems.Add($"{file}: has needs and afterReset. A case starts at the end of others, or after a reset, not both.");
            }
            CheckSteps(item.Steps, Path.GetFileName(item.File), problems);
            CheckSteps(item.Reset, Path.GetFileName(item.File) + ", reset", problems);
            if (item.Reset.Any(step => step?["checkpoint"] is not null))
                problems.Add($"{Path.GetFileName(item.File)}: a reset step can't be a checkpoint. The case's checkpoint goes at the end of its steps.");
        }
        foreach (var macro in Macros.Values)
            CheckSteps(macro.Steps, Path.GetFileName(macro.File), problems);
    }

    private void CheckSteps(JsonArray steps, string where, List<string> problems)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            var at = $"{where}, step {index + 1}";
            if (steps[index] is not JsonObject step)
            {
                problems.Add($"{at}: a step must be an object.");
                continue;
            }
            var kinds = ScriptVocabulary.Kinds(step).ToList();
            if (kinds.Count > 1)
            {
                problems.Add($"{at}: has {string.Join(" and ", kinds)}; a step does one thing.");
                continue;
            }
            if (kinds.Count == 0)
            {
                if (step["say"] is null)
                    problems.Add($"{at}: doesn't say what to do. A step is one of: {string.Join(", ", ScriptVocabulary.Steps.Keys)}, or just say.");
                foreach (var (key, _) in step.Where(pair => !ScriptVocabulary.CommonStepKeys.Contains(pair.Key)))
                    problems.Add($"{at}: has \"{key}\", which isn't a step this runner knows.");
                continue;
            }
            var kind = kinds[0];
            var definition = ScriptVocabulary.Steps[kind];
            foreach (var (key, _) in step)
                if (key != kind && !ScriptVocabulary.CommonStepKeys.Contains(key) && !definition.Keys.Contains(key))
                    problems.Add($"{at}: {kind} doesn't take \"{key}\". It takes: {string.Join(", ", definition.Keys.Concat(ScriptVocabulary.CommonStepKeys))}.");

            if (definition.Takes == Takes.Target)
                CheckTarget(step[kind], at, problems);
            if (ScriptVocabulary.SecondTargets.GetValueOrDefault(kind) is { } other && step[other] is { } control)
                CheckTarget(control, at, problems);
            switch (kind)
            {
                case "call" when step["call"]?.GetValue<string>() is { } name:
                    if (!Macros.TryGetValue(name, out var macro))
                        problems.Add($"{at}: there is no macro called {name}.");
                    else
                    {
                        var given = step["with"] as JsonObject ?? [];
                        foreach (var (key, _) in given)
                            if (!macro.Parameters.ContainsKey(key))
                                problems.Add($"{at}: {name} has no parameter {key}. It takes: {string.Join(", ", macro.Parameters.Keys)}.");
                        foreach (var (parameter, fallback) in macro.Parameters)
                            if (fallback is null && !given.ContainsKey(parameter))
                                problems.Add($"{at}: {name} needs {parameter}, in with.");
                    }
                    break;
                case "repeat":
                    if (step["repeat"] is JsonObject source && Written(source["rows"]) is { } rows && !File.Exists(Path.Combine(Folder, rows)))
                        problems.Add($"{at}: there is no data file {rows} in the suite's folder.");
                    CheckSteps(step["steps"] as JsonArray ?? [], at, problems);
                    break;
                case "click" when Written(step["how"]) is { } how && how is not ("mouse" or "right" or "double"):
                    problems.Add($"{at}: how is mouse, right or double, not {how}.");
                    break;
                case "script" when step["script"]?.GetValue<string>() is { } script && !script.Contains("${", StringComparison.Ordinal):
                    if (!script.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && !script.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                        problems.Add($"{at}: a script is a .ps1 or a .cs file, not {script}.");
                    else if (!File.Exists(Path.Combine(Folder, script)))
                        problems.Add($"{at}: there is no script {script} in the suite's folder.");
                    break;
                case "whileShows":
                    if (step["untilText"] is null)
                        problems.Add($"{at}: whileShows needs untilText, the control that says when it is done.");
                    CheckSteps(step["steps"] as JsonArray ?? [], at, problems);
                    break;
                case "if":
                    CheckChecks(new JsonArray(step["if"]?.DeepClone()), at, problems);
                    CheckSteps(step["steps"] as JsonArray ?? [], at, problems);
                    CheckSteps(step["else"] as JsonArray ?? [], at + ", else", problems);
                    break;
                case "query":
                    CheckSource(step["query"], Databases, "database", at, problems);
                    if (step["sql"] is null)
                        problems.Add($"{at}: query needs sql, what to run.");
                    if (step["params"] is { } parameters && parameters is not JsonObject)
                        problems.Add($"{at}: params is an object of names and values, such as {{ \"id\": \"1001\" }}.");
                    break;
                case "send" or "receive" or "purge":
                    CheckSource(step[kind], Queues, "queue", at, problems);
                    if (kind == "send" && (step["message"] is null) == (step["file"] is null))
                        problems.Add($"{at}: send needs message, the text to send, or file, a file with it in.");
                    break;
                case "checkpoint":
                    CheckChecks(step["checks"] as JsonArray, at, problems);
                    CheckItems(step["tools"] as JsonArray, ScriptVocabulary.Tools, new HashSet<string>(["label"]), "tool", at, problems);
                    break;
            }
        }
    }

    private void CheckChecks(JsonArray? checks, string at, List<string> problems)
    {
        CheckItems(checks, ScriptVocabulary.Checks, ScriptVocabulary.CommonCheckKeys, "check", at, problems);
        foreach (var check in checks ?? [])
        {
            if (check is not JsonObject { } item || Kinds.Of(item, ScriptVocabulary.Checks) is not { } kind)
                continue;
            if (ScriptVocabulary.ControlChecks.Contains(kind))
                CheckTarget(item[kind], at, problems);
            else if (kind == "dbValue")
            {
                CheckSource(item[kind], Databases, "database", at, problems);
                if (item["sql"] is null)
                    problems.Add($"{at}: dbValue needs sql, the query that reads the value.");
            }
            else if (kind == "queue")
            {
                CheckSource(item[kind], Queues, "queue", at, problems);
                if ((item["count"] is null) == (item["has"] is null))
                    problems.Add($"{at}: the queue check needs count, how many messages are waiting, or has, text one of them has.");
            }
        }
    }

    // A database or queue named in suite.json, unless it is a ${variable} filled in when it runs.
    private static void CheckSource<T>(JsonNode? node, IReadOnlyDictionary<string, T> named, string noun, string at, List<string> problems)
    {
        if (Written(node) is not { } name)
            return;
        if (!named.ContainsKey(name))
            problems.Add(named.Count == 0
                ? $"{at}: there is no {noun} called {name}. Name it in suite.json's {noun}s."
                : $"{at}: there is no {noun} called {name} in suite.json. It is one of: {string.Join(", ", named.Keys)}.");
    }

    private void CheckTarget(JsonNode? target, string at, List<string> problems)
    {
        switch (target)
        {
            case JsonValue value when value.TryGetValue(out string? name):
                if (!name.Contains("${", StringComparison.Ordinal) && !Elements.ContainsKey(name))
                    problems.Add($"{at}: there is no element called {name} in elements.json.");
                break;
            case JsonArray list:
                foreach (var item in list)
                    CheckTarget(item, at, problems);
                break;
            case JsonObject selector:
                foreach (var (key, _) in selector.Where(pair => !Targets.SelectorKeys.Contains(pair.Key)))
                    problems.Add($"{at}: a selector has no \"{key}\".");
                if (Written(selector["type"]) is { } type && !Targets.ControlTypes.Contains(type))
                    problems.Add($"{at}: there is no control type {type}. It is one of: {string.Join(", ", Targets.ControlTypes)}.");
                if (Written(selector["match"]) is { } match && match is not ("exact" or "startsWith" or "contains"))
                    problems.Add($"{at}: match is exact, startsWith or contains, not {match}.");
                if (selector["element"] is JsonValue element)
                    CheckTarget(element, at, problems);
                if (selector["within"] is { } within)
                    CheckTarget(within, at, problems);
                if (selector["sameRowAs"] is { } row)
                    CheckTarget(row, at, problems);
                break;
            default:
                problems.Add($"{at}: the step doesn't say which control.");
                break;
        }
    }

    // A property's text as written, when it has no ${...} to fill in first.
    private static string? Written(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) && !text.Contains("${", StringComparison.Ordinal) ? text : null;

    private static void CheckItems(JsonArray? items, IReadOnlyDictionary<string, string[]> vocabulary, IReadOnlySet<string> common, string noun, string at, List<string> problems)
    {
        foreach (var node in items ?? [])
        {
            if (node is not JsonObject item)
            {
                problems.Add($"{at}: a {noun} must be an object.");
                continue;
            }
            if (Kinds.Of(item, vocabulary) is not { } kind)
            {
                problems.Add($"{at}: a {noun} is one of: {string.Join(", ", vocabulary.Keys)}.");
                continue;
            }
            foreach (var (key, _) in item)
                if (key != kind && !common.Contains(key) && !vocabulary[kind].Contains(key))
                    problems.Add($"{at}: the {kind} {noun} doesn't take \"{key}\".");
        }
    }
}
