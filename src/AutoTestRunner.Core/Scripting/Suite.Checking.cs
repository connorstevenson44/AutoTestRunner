using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

// Checks every step, check and control the suite's files name against the vocabulary, as it is loaded.
public sealed partial class Suite
{
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
        foreach (var item in Cases)
        {
            foreach (var need in item.Needs)
            {
                // Cases run in file order, so the case it carries on from must come before it.
                if (WithId(need) is not { } needed)
                    problems.Add($"{Path.GetFileName(item.File)}: needs {need}, and there is no case with that id.");
                else if (needed.Number >= item.Number)
                    problems.Add($"{Path.GetFileName(item.File)}: needs {need}, which runs after it. Cases run in file order, so give it a lower number.");
            }
            if (item.AfterReset is { } setup)
            {
                var file = Path.GetFileName(item.File);
                if (WithId(setup) is not { } from)
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
