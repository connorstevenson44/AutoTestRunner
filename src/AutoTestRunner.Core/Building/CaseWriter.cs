using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Saves a case that was built: a new file in cases/, numbered after the last, or new steps and checks put into an
/// existing case without touching the rest of its text; and the controls that were named, added to elements.json.
/// </summary>
public static partial class CaseWriter
{
    private const string SchemaFolder = "https://raw.githubusercontent.com/connorstevenson44/AutoTestRunner/main/schema/";
    private static readonly JsonReaderOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Saves the draft, and returns the case file it went in.</summary>
    public static string Save(Suite suite, CaseDraft draft)
    {
        AddElements(suite, draft.NewElements);
        if (draft.AddingTo is { } existing)
        {
            var text = File.ReadAllText(existing.File);
            File.WriteAllText(existing.File, Insert(text, draft));
            return existing.File;
        }
        var folder = Path.Combine(suite.Folder, "cases");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName(suite, draft.Name));
        File.WriteAllText(path, NewCase(draft, Schema(suite, "case"), NewLine(suite)));
        return path;
    }

    /// <summary>The file name for a new case: the next number, then its name in a few words, such as 09-take-an-order.json.</summary>
    public static string FileName(Suite suite, string name)
    {
        var numbers = suite.Cases.Select(item => Leading().Match(Path.GetFileName(item.File))).Where(match => match.Success).ToList();
        var next = numbers.Count == 0 ? 1 : numbers.Max(match => int.Parse(match.Value, CultureInfo.InvariantCulture)) + 1;
        var width = Math.Max(2, numbers.Count == 0 ? 2 : numbers.Max(match => match.Value.Length));
        var words = Words().Matches(name.ToLowerInvariant()).Select(match => match.Value).Take(6).ToList();
        var id = words.Count == 0 ? "new-case" : string.Join('-', words);
        var unique = id;
        for (var copy = 2; suite.Cases.Any(item => item.Id == unique); copy++)
            unique = $"{id}-{copy}";
        return $"{next.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}-{unique}.json";
    }

    /// <summary>A new case file's text.</summary>
    public static string NewCase(CaseDraft draft, string schema, string newline)
    {
        var text = new StringBuilder("{").Append(newline);
        text.Append(CultureInfo.InvariantCulture, $"  \"$schema\": {JsonLine.Quote(schema)},").Append(newline);
        text.Append(CultureInfo.InvariantCulture, $"  \"name\": {JsonLine.Quote(draft.Name)},").Append(newline);
        if (draft.Needs.Count > 0)
            text.Append(CultureInfo.InvariantCulture, $"  \"needs\": {JsonLine.Write(new JsonArray([.. draft.Needs.Select(need => (JsonNode?)JsonValue.Create(need))]))},").Append(newline);
        var steps = Steps(draft).Select(step => "    " + JsonLine.Step(step, "    ", newline)).ToList();
        text.Append("  \"steps\": [");
        if (steps.Count > 0)
            text.Append(newline).Append(string.Join("," + newline, steps)).Append(newline).Append("  ");
        return text.Append(']').Append(newline).Append('}').Append(newline).ToString();
    }

    /// <summary>
    /// An existing case's text with the draft's steps put in before its checkpoint (or at the end, when it has none),
    /// and the draft's checks and things to look at put into that checkpoint. Everything else stays as it was written.
    /// </summary>
    public static string Insert(string text, CaseDraft draft)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var layout = Read(bytes);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<(int At, string Text)>();
        var steps = draft.Steps.Select(step => step.Json).ToList();
        var last = layout.Steps.Count > 0 ? layout.Steps[^1] : null;
        var checkpoint = last is { IsCheckpoint: true } ? last : null;
        var addCheckpoint = checkpoint is null && draft.Checkpoint() is { } made ? made : null;
        if (addCheckpoint is not null)
            steps.Add(addCheckpoint);

        if (steps.Count > 0)
        {
            var indent = last is null ? "    " : IndentAt(bytes, last.Start);
            var lines = steps.Select(step => JsonLine.Step(step, indent, newline)).ToList();
            if (checkpoint is not null)
                edits.Add((checkpoint.Start, string.Join("," + newline + indent, lines) + "," + newline + indent));
            else if (last is not null)
                edits.Add((last.End, "," + newline + indent + string.Join("," + newline + indent, lines)));
            else
                edits.Add((layout.StepsEnd, newline + indent + string.Join("," + newline + indent, lines) + newline + "  "));
        }
        if (checkpoint is not null)
        {
            var itemIndent = IndentAt(bytes, checkpoint.Start) + "    ";
            AddItems(edits, bytes, checkpoint, "checks", draft.Checks.Select(check => (JsonNode)check), itemIndent, newline);
            AddItems(edits, bytes, checkpoint, "look", draft.Look.Select(line => (JsonNode)JsonValue.Create(line)), itemIndent, newline);
        }

        foreach (var (at, insert) in edits.OrderByDescending(edit => edit.At))
            bytes = [.. bytes[..at], .. Encoding.UTF8.GetBytes(insert), .. bytes[at..]];
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Adds named controls to the end of elements.json, keeping the rest of it as it was written.</summary>
    public static void AddElements(Suite suite, IReadOnlyDictionary<string, JsonNode> elements)
    {
        if (elements.Count == 0)
            return;
        var path = Path.Combine(suite.Folder, "elements.json");
        var newline = NewLine(suite);
        var text = File.Exists(path) ? File.ReadAllText(path) : $"{{{newline}  \"$schema\": {JsonLine.Quote(Schema(suite, "elements"))}{newline}}}{newline}";
        var lines = string.Join("," + newline, elements.Select(pair => $"  {JsonLine.Quote(pair.Key)}: {JsonLine.Write(pair.Value)}"));
        var close = text.LastIndexOf('}');
        var before = close - 1;
        while (before >= 0 && char.IsWhiteSpace(text[before]))
            before--;
        text = before >= 0 && text[before] == '{'
            ? text[..(before + 1)] + newline + lines + newline + text[close..]
            : text[..(before + 1)] + "," + newline + lines + text[(before + 1)..];
        File.WriteAllText(path, text);
    }

    private static IEnumerable<JsonObject> Steps(CaseDraft draft)
    {
        foreach (var step in draft.Steps)
            yield return step.Json;
        if (draft.Checkpoint() is { } checkpoint)
            yield return checkpoint;
    }

    // Items go on the end of a checkpoint's array, or in a new array at its start when it has none.
    private static void AddItems(List<(int At, string Text)> edits, byte[] bytes, StepLayout checkpoint, string key, IEnumerable<JsonNode> items, string indent, string newline)
    {
        var lines = items.Select(JsonLine.Write).ToList();
        if (lines.Count == 0)
            return;
        if (!checkpoint.Arrays.TryGetValue(key, out var array))
        {
            edits.Add((checkpoint.Start + 1, $" {JsonLine.Quote(key)}: [ {string.Join(", ", lines)} ],"));
            return;
        }
        var multiline = Encoding.UTF8.GetString(bytes[array.Start..array.End]).Contains('\n', StringComparison.Ordinal);
        var separator = multiline ? "," + newline + indent : ", ";
        edits.Add(array.LastItemEnd is { } end
            ? (end, separator + string.Join(separator, lines))
            : (array.Start + 1, (multiline ? newline + indent : " ") + string.Join(separator, lines) + " "));
    }

    // Where a case file's steps are, from its text: each step's start and end, and a checkpoint's arrays.
    private static CaseLayout Read(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, ReadOptions);
        var layout = new CaseLayout();
        string? property = null;
        StepLayout? step = null;
        ArrayLayout? array = null;
        var inSteps = false;
        while (reader.Read())
        {
            var depth = reader.CurrentDepth;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    if (depth == 3 && step is not null && property == "checkpoint")
                        step.IsCheckpoint = true;
                    continue;
                case JsonTokenType.StartArray when depth == 1 && property == "steps":
                    inSteps = true;
                    break;
                case JsonTokenType.EndArray when depth == 1 && inSteps:
                    inSteps = false;
                    layout.StepsEnd = (int)reader.TokenStartIndex;
                    break;
                case JsonTokenType.StartObject when depth == 2 && inSteps:
                    step = new StepLayout { Start = (int)reader.TokenStartIndex };
                    break;
                case JsonTokenType.EndObject when depth == 2 && step is not null:
                    step.End = (int)reader.BytesConsumed;
                    layout.Steps.Add(step);
                    step = null;
                    break;
                case JsonTokenType.StartArray when depth == 3 && step is not null && property is "checks" or "look":
                    array = new ArrayLayout { Start = (int)reader.TokenStartIndex };
                    step.Arrays[property] = array;
                    break;
                case JsonTokenType.EndArray when depth == 3 && array is not null:
                    array.End = (int)reader.BytesConsumed;
                    array = null;
                    break;
                case JsonTokenType.EndObject or JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null
                    when depth == 4 && array is not null:
                    array.LastItemEnd = (int)reader.BytesConsumed;
                    break;
            }
            // A property's name is only needed by the token after it, its value.
            property = null;
        }
        return layout;
    }

    // The spaces before the line's first character, at a place in the text.
    private static string IndentAt(byte[] bytes, int at)
    {
        var start = at;
        while (start > 0 && bytes[start - 1] != (byte)'\n')
            start--;
        var end = start;
        while (end < bytes.Length && bytes[end] is (byte)' ' or (byte)'\t')
            end++;
        return Encoding.UTF8.GetString(bytes[start..end]);
    }

    // The $schema a new file points at: as the suite's other files do, or the one on GitHub.
    private static string Schema(Suite suite, string kind)
    {
        foreach (var item in suite.Cases)
        {
            if (JsonNode.Parse(File.ReadAllText(item.File), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["$schema"]?.GetValue<string>() is not { } schema)
                continue;
            schema = schema.Replace("case.schema.json", $"{kind}.schema.json", StringComparison.Ordinal);
            // The suite's files are a folder above its cases, so a relative path goes up one fewer.
            return kind != "case" && !schema.Contains("://", StringComparison.Ordinal) && schema.StartsWith("../", StringComparison.Ordinal) ? schema[3..] : schema;
        }
        return SchemaFolder + kind + ".schema.json";
    }

    private static string NewLine(Suite suite) =>
        suite.Cases is [var item, ..] && File.ReadAllText(item.File).Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : suite.Cases.Count > 0 ? "\n" : Environment.NewLine;

    [GeneratedRegex(@"^\d+")]
    private static partial Regex Leading();

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex Words();

    private sealed class CaseLayout
    {
        public List<StepLayout> Steps { get; } = [];
        public int StepsEnd { get; set; }
    }

    private sealed class StepLayout
    {
        public int Start { get; set; }
        public int End { get; set; }
        public bool IsCheckpoint { get; set; }
        public Dictionary<string, ArrayLayout> Arrays { get; } = [];
    }

    private sealed class ArrayLayout
    {
        public int Start { get; set; }
        public int End { get; set; }
        public int? LastItemEnd { get; set; }
    }
}
