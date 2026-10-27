using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Makes a macro of steps picked in the builder: the values that change from one use to the next, such as what is
/// typed, become its parameters, and the steps are replaced by a call that gives it the values they had.
/// </summary>
public static partial class MacroMaker
{
    // The properties of each step that hold a value someone would give a macro, rather than how the app works.
    private static readonly Dictionary<string, string[]> ValueKeys = new(StringComparer.Ordinal)
    {
        ["type"] = ["text"],
        ["choose"] = ["option"],
        ["setValue"] = ["to"],
        ["waitText"] = ["startsWith", "contains", "equals", "matches"],
        ["answerDialog"] = ["expect"],
        ["pickFile"] = ["path"],
        ["pickFolder"] = ["path"],
        ["replaceText"] = ["replacement"],
        ["writeFile"] = ["text"],
        ["setJsonValue"] = ["to"],
        ["setAppSetting"] = ["to"],
        ["tab"] = ["tab"],
    };

    /// <summary>The values in these steps that could be parameters, each named after its control or what it is.</summary>
    public static IReadOnlyList<MacroValue> Values(IReadOnlyList<JsonObject> steps)
    {
        var found = new List<MacroValue>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var verb = ScriptVocabulary.KindOf(step);
            if (!ValueKeys.TryGetValue(verb, out var keys) || step["secret"]?.GetValue<bool>() == true)
                continue;
            foreach (var key in keys)
            {
                if (step[key] is not JsonValue value || !value.TryGetValue(out string? text) || text.Length == 0)
                    continue;
                var name = Unique(NameFor(step, verb, key), taken);
                found.Add(new MacroValue(index, key, text, name));
            }
        }
        return found;
    }

    /// <summary>
    /// The macro's file contents, and the call step that replaces the steps: each value made a parameter is written
    /// as ${name} in the macro, and given its old value in the call.
    /// </summary>
    public static (JsonObject Macro, JsonObject Call) Make(string name, string description, IReadOnlyList<JsonObject> steps, IReadOnlyList<MacroValue> parameters)
    {
        var copies = steps.Select(step => step.DeepClone().AsObject()).ToList();
        var given = new JsonObject();
        var declared = new JsonObject();
        foreach (var parameter in parameters)
        {
            copies[parameter.Step][parameter.Key] = $"${{{parameter.Name}}}";
            given[parameter.Name] = parameter.Text;
            declared[parameter.Name] = new JsonObject();
        }
        var macro = new JsonObject { ["description"] = description };
        if (declared.Count > 0)
            macro["params"] = declared;
        macro["steps"] = new JsonArray([.. copies]);
        var call = new JsonObject { ["call"] = name };
        if (given.Count > 0)
            call["with"] = given;
        call["say"] = description;
        return (macro, call);
    }

    /// <summary>Writes a new macro to macros/name.json, as the suite's other files are written, and returns the file.</summary>
    public static string Save(Suite suite, string name, JsonObject macro)
    {
        if (!ScriptVocabulary.IsName(name))
            throw new ArgumentException("A macro's name is letters and digits, such as addOrder.", nameof(name));
        if (suite.Macros.ContainsKey(name))
            throw new ArgumentException($"The suite already has a macro called {name}.", nameof(name));
        var folder = Path.Combine(suite.Folder, "macros");
        var path = Path.Combine(folder, name + ".json");
        if (File.Exists(path))
            throw new ArgumentException($"macros/{name}.json is already there.", nameof(name));
        Directory.CreateDirectory(folder);
        File.WriteAllText(path, Text(macro, CaseWriter.Schema(suite, "macro"), CaseWriter.NewLine(suite)));
        return path;
    }

    // A macro's text: its schema, then a property to a line and a step to a line.
    private static string Text(JsonObject macro, string schema, string newline)
    {
        var text = new StringBuilder("{").Append(newline);
        text.Append(CultureInfo.InvariantCulture, $"  \"$schema\": {JsonLine.Quote(schema)}");
        foreach (var (key, value) in macro)
        {
            text.Append(',').Append(newline);
            if (key == "steps" && value is JsonArray steps)
            {
                text.Append("  \"steps\": [").Append(newline);
                text.Append(string.Join("," + newline, steps.OfType<JsonObject>().Select(step => "    " + JsonLine.Step(step, "    ", newline))));
                text.Append(newline).Append("  ]");
            }
            else
                text.Append(CultureInfo.InvariantCulture, $"  {JsonLine.Quote(key)}: {JsonLine.Write(value)}");
        }
        return text.Append(newline).Append('}').Append(newline).ToString();
    }

    // A parameter's name: the control's in elements.json, else a word or two of how the step finds it, else the key.
    private static string NameFor(JsonObject step, string verb, string key)
    {
        if (verb == "tab")
            return "tab";
        var target = step[verb];
        var words = target switch
        {
            JsonValue value when value.TryGetValue(out string? element) => element,
            JsonObject selector => selector["element"]?.ToString() ?? selector["name"]?.ToString() ?? selector["id"]?.ToString()
                ?? selector.Select(pair => pair.Value).OfType<JsonValue>().Select(value => value.ToString()).FirstOrDefault(),
            _ => null,
        };
        return words is null ? key : Camel(words);
    }

    private static string Camel(string text)
    {
        var words = Word().Matches(text).Select(match => match.Value).Take(3).ToList();
        if (words.Count == 0)
            return "value";
        var name = string.Concat(words.Select((word, index) => index == 0 ? char.ToLowerInvariant(word[0]) + word[1..] : char.ToUpperInvariant(word[0]) + word[1..]));
        return char.IsDigit(name[0]) ? "value" + name : name;
    }

    private static string Unique(string name, HashSet<string> taken)
    {
        var unique = name;
        for (var copy = 2; !taken.Add(unique); copy++)
            unique = $"{name}{copy}";
        return unique;
    }


    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Word();
}
