using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>A test case: the steps a person takes, ending at a checkpoint.</summary>
public sealed record Case(
    int Number, string Id, string Name, string File, IReadOnlyList<string> Needs, string? NeedsNote, string? Covers, JsonArray Steps);

/// <summary>A reusable list of steps with parameters, called with { "call": name, "with": { ... } }.</summary>
public sealed record Macro(string Name, string File, IReadOnlyDictionary<string, string?> Parameters, JsonArray Steps, string? Returns);

/// <summary>How to start the app, and the control whose text says what it is doing, such as a status bar.</summary>
public sealed record AppSettings(string Exe, string Window, bool RunAsAdmin, string? Status, string? Arguments);

/// <summary>Scripts that put the machine back as it was before the suite ran, and list what is left over.</summary>
public sealed record ResetSettings(string? PowerShell, string? Leftovers);

/// <summary>
/// A suite: one app's tests, in a folder. suite.json says how to start the app, its variables and how to reset the
/// machine; elements.json names its controls; macros/ holds reusable steps and cases/ the cases, run in file order.
/// </summary>
public sealed class Suite
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

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
            var parameters = (macro["params"] as JsonObject ?? []).ToDictionary(
                pair => pair.Key, pair => pair.Value?["default"]?.ToString());
            macros[name] = new Macro(name, file, parameters, macro["steps"] as JsonArray ?? [], macro["returns"]?.GetValue<string>());
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
                item["steps"] as JsonArray ?? []));
        }

        var screen = root["screen"] is JsonObject size
            ? ((int)size["width"]!, (int)size["height"]!)
            : ((int, int)?)null;

        var suite = new Suite
        {
            Folder = folder,
            Name = root["name"]?.GetValue<string>() ?? Path.GetFileName(folder),
            App = new AppSettings(
                app["exe"]?.GetValue<string>() ?? "",
                app["window"]?.GetValue<string>() ?? "",
                app["runAsAdmin"]?.GetValue<bool>() ?? false,
                app["status"]?.GetValue<string>(),
                app["arguments"]?.GetValue<string>()),
            Reset = new ResetSettings(root["reset"]?["powershell"]?.GetValue<string>(), root["reset"]?["leftovers"]?.GetValue<string>()),
            Variables = root["variables"] as JsonObject ?? [],
            Requires = (root["requires"] as JsonArray ?? []).Select(path => path!.GetValue<string>()).ToList(),
            Helpers = helpers,
            Elements = elements,
            Macros = macros,
            Cases = cases,
            Screen = screen,
        };
        suite.Check(problems);
        if (problems.Count > 0)
            throw new SuiteException(problems);
        return suite;
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

    private static IEnumerable<string> Files(string folder, string sub)
    {
        var path = Path.Combine(folder, sub);
        return Directory.Exists(path) ? Directory.GetFiles(path, "*.json").Order(StringComparer.Ordinal) : [];
    }

    private static JsonObject? Read(string path, List<string> problems)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject
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
        if (App.Status is { } status && !Elements.ContainsKey(status))
            problems.Add($"suite.json: app.status names {status}, which isn't in elements.json.");
        var ids = Cases.Select(item => item.Id).ToHashSet();
        foreach (var item in Cases)
        {
            foreach (var need in item.Needs.Where(need => !ids.Contains(need)))
                problems.Add($"{Path.GetFileName(item.File)}: needs {need}, and there is no case with that id.");
            CheckSteps(item.Steps, Path.GetFileName(item.File), problems);
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
            var kinds = step.Select(pair => pair.Key).Where(ScriptVocabulary.Steps.ContainsKey).ToList();
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
            switch (kind)
            {
                case "call" when step["call"]?.GetValue<string>() is { } name:
                    if (!Macros.TryGetValue(name, out var macro))
                        problems.Add($"{at}: there is no macro called {name}.");
                    else
                        foreach (var (key, _) in step["with"] as JsonObject ?? [])
                            if (!macro.Parameters.ContainsKey(key))
                                problems.Add($"{at}: {name} has no parameter {key}. It takes: {string.Join(", ", macro.Parameters.Keys)}.");
                    break;
                case "repeat":
                    CheckSteps(step["steps"] as JsonArray ?? [], at, problems);
                    break;
                case "whileShows":
                    if (step["untilText"] is { } until)
                        CheckTarget(until, at, problems);
                    else
                        problems.Add($"{at}: whileShows needs untilText, the control that says when it is done.");
                    CheckSteps(step["steps"] as JsonArray ?? [], at, problems);
                    break;
                case "if":
                    CheckChecks(new JsonArray(step["if"]?.DeepClone()), at, problems);
                    CheckSteps(step["steps"] as JsonArray ?? [], at, problems);
                    CheckSteps(step["else"] as JsonArray ?? [], at + ", else", problems);
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
            if (check is JsonObject { } item && item["shows"] is { } target)
                CheckTarget(target, at, problems);
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

/// <summary>A suite that can't be run as it is, with every problem found.</summary>
public sealed class SuiteException(IReadOnlyList<string> problems) : Exception(string.Join(Environment.NewLine, problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
