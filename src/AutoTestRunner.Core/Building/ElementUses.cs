using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Where a suite uses each name in elements.json: as the target of a step or a control check, inside another target's
/// within or sameRowAs, in another element, and in suite.json as the status text or a control the builder watches.
/// A name can be changed everywhere it is used, with the files kept as they were written apart from the name.
/// </summary>
public static class ElementUses
{

    /// <summary>Each element's uses, as the file each is in relative to the suite folder, once for each use.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Find(Suite suite)
    {
        var uses = suite.Elements.Keys.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var file in Files(suite))
        {
            var relative = Path.GetRelativePath(suite.Folder, file).Replace('\\', '/');
            foreach (var (_, name) in References(suite, file))
                if (uses.TryGetValue(name, out var list))
                    list.Add(relative);
        }
        return uses.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Gives an element a new name in elements.json and everywhere it is used. Returns the files changed.</summary>
    public static IReadOnlyList<string> Rename(Suite suite, string from, string to)
    {
        if (!suite.Elements.ContainsKey(from))
            throw new ArgumentException($"elements.json has no {from}.", nameof(from));
        if (suite.Elements.ContainsKey(to))
            throw new ArgumentException($"elements.json already has {to}.", nameof(to));
        var changed = new List<string>();
        foreach (var file in Files(suite))
        {
            var at = References(suite, file).Where(reference => reference.Name == from).Select(reference => reference.Path).ToHashSet(StringComparer.Ordinal);
            var isElements = Path.GetFileName(file).Equals("elements.json", StringComparison.OrdinalIgnoreCase);
            var text = File.ReadAllText(file);
            var edited = Edit(text, at, from, to, renameKey: isElements);
            if (edited != text)
            {
                File.WriteAllText(file, edited);
                changed.Add(file);
            }
        }
        return changed;
    }

    // suite.json, elements.json, and every case and macro file.
    private static IEnumerable<string> Files(Suite suite)
    {
        foreach (var name in new[] { "suite.json", "elements.json" })
            if (File.Exists(Path.Combine(suite.Folder, name)))
                yield return Path.Combine(suite.Folder, name);
        foreach (var item in suite.Cases)
            yield return item.File;
        foreach (var macro in suite.Macros.Values)
            yield return macro.File;
    }

    // The places in a file that name an element, each as its path from the root, such as steps/2/click, with the name.
    private static List<(string Path, string Name)> References(Suite suite, string file)
    {
        var found = new List<(string Path, string Name)>();
        if (ScriptJson.ReadFile(file) is not JsonObject root)
            return found;
        switch (Path.GetFileName(file).ToLowerInvariant())
        {
            case "suite.json":
                if (root["app"]?["status"] is JsonValue)
                    Target(root["app"]!["status"], "app/status", found);
                foreach (var key in new[] { "watch", "afterClick" })
                    if (root["builder"]?[key] is JsonArray hooks)
                        for (var index = 0; index < hooks.Count; index++)
                            Target(hooks[index]?["element"], $"builder/{key}/{index}/element", found);
                break;
            case "elements.json":
                foreach (var (name, element) in root)
                    if (name != "$schema")
                        Inside(element, Escape(name), found);
                break;
            default:
                Steps(root["steps"], "steps", found);
                break;
        }
        return found;
    }

    private static void Steps(JsonNode? steps, string path, List<(string, string)> found)
    {
        if (steps is not JsonArray list)
            return;
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] is not JsonObject step)
                continue;
            var at = $"{path}/{index}";
            var kind = ScriptVocabulary.KindOf(step);
            if (ScriptVocabulary.Steps.TryGetValue(kind, out var known) && known.Takes == Takes.Target)
                Target(step[kind], $"{at}/{Escape(kind)}", found);
            if (ScriptVocabulary.SecondTargets.GetValueOrDefault(kind) is { } key)
                Target(step[key], $"{at}/{key}", found);
            if (kind == "if" && step["if"] is JsonObject condition)
                Check(condition, $"{at}/if", found);
            if (kind == "checkpoint" && step["checks"] is JsonArray checks)
                for (var check = 0; check < checks.Count; check++)
                    if (checks[check] is JsonObject each)
                        Check(each, $"{at}/checks/{check}", found);
            Steps(step["steps"], $"{at}/steps", found);
            Steps(step["else"], $"{at}/else", found);
        }
    }

    private static void Check(JsonObject check, string path, List<(string, string)> found)
    {
        if (Kinds.Of(check, ScriptVocabulary.Checks) is { } kind && ScriptVocabulary.ControlChecks.Contains(kind))
            Target(check[kind], $"{path}/{Escape(kind)}", found);
    }

    // A target: a name, a selector whose element, within and sameRowAs can name one, or a list of either.
    private static void Target(JsonNode? target, string path, List<(string, string)> found)
    {
        switch (target)
        {
            case JsonValue value when value.TryGetValue(out string? name):
                found.Add((path, name));
                break;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                    Target(list[index], $"{path}/{index}", found);
                break;
            case JsonObject selector:
                Inside(selector, path, found);
                break;
        }
    }

    // What in a selector or an element can name another element.
    private static void Inside(JsonNode? node, string path, List<(string, string)> found)
    {
        switch (node)
        {
            case JsonArray list:
                Target(list, path, found);
                break;
            case JsonObject selector:
                if (selector["element"] is JsonValue element && element.TryGetValue(out string? name))
                    found.Add(($"{path}/element", name));
                foreach (var key in new[] { "within", "sameRowAs" })
                    Target(selector[key], $"{path}/{key}", found);
                break;
        }
    }

    // A name in a path, with its slashes written so that they don't split it.
    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    // The text with the strings at these paths that say from saying to instead, and in elements.json the key too.
    private static string Edit(string text, HashSet<string> at, string from, string to, bool renameKey)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, ScriptJson.Reader);
        var edits = new List<(int Start, int End)>();
        // Each open object or array: the property being read in it, or the index of the item being read.
        var frames = new List<(bool IsArray, int Index, string? Property)>();
        string Here() => string.Join("/", frames.Select(frame => frame.IsArray ? frame.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) : Escape(frame.Property ?? "")));
        while (reader.Read())
        {
            var token = reader.TokenType;
            if (token is not (JsonTokenType.PropertyName or JsonTokenType.EndObject or JsonTokenType.EndArray) && frames.Count > 0 && frames[^1].IsArray)
                frames[^1] = frames[^1] with { Index = frames[^1].Index + 1 };
            switch (token)
            {
                case JsonTokenType.PropertyName:
                    var property = reader.GetString();
                    frames[^1] = frames[^1] with { Property = property };
                    if (renameKey && frames.Count == 1 && property == from)
                        edits.Add(Quoted(ref reader));
                    break;
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    frames.Add((token == JsonTokenType.StartArray, -1, null));
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    frames.RemoveAt(frames.Count - 1);
                    break;
                case JsonTokenType.String when reader.GetString() == from && at.Contains(Here()):
                    edits.Add(Quoted(ref reader));
                    break;
            }
        }
        // Where a string is in the text, quotes and all: after a property's name the reader has read its colon too.
        static (int Start, int End) Quoted(ref Utf8JsonReader reader) => ((int)reader.TokenStartIndex, (int)reader.TokenStartIndex + reader.ValueSpan.Length + 2);
        var replacement = Encoding.UTF8.GetBytes(JsonLine.Quote(to));
        foreach (var (start, end) in edits.OrderByDescending(edit => edit.Start))
            bytes = [.. bytes[..start], .. replacement, .. bytes[end..]];
        return Encoding.UTF8.GetString(bytes);
    }
}
