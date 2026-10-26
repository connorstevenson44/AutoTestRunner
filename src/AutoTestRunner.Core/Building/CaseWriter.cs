using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// Saves a case that was built: a new file in cases/, numbered after the last, or a case opened to change it, with
/// only the steps that changed written afresh; and the controls that were named, added to elements.json.
/// </summary>
public static partial class CaseWriter
{
    private const string SchemaFolder = "https://raw.githubusercontent.com/connorstevenson44/AutoTestRunner/main/schema/";
    private static readonly JsonReaderOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Saves the draft, and returns the case file it went in; with the controls it named, the hooks and macros it
    /// added, and the review lines for the cases that carry on from it.
    /// </summary>
    public static string Save(Suite suite, CaseDraft draft)
    {
        AddElements(suite, draft.NewElements);
        AddHooks(suite, draft.NewWatch, draft.NewAfterClick);
        foreach (var (name, macro) in draft.NewMacros)
            MacroMaker.Save(suite, name, macro);
        foreach (var (file, why) in draft.Review)
            File.WriteAllText(file, Flag(File.ReadAllText(file), why));
        if (draft.Editing is { } existing)
        {
            var text = File.ReadAllText(existing.File);
            File.WriteAllText(existing.File, Unflag(Rewrite(text, draft)));
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
    /// A case's text with its steps as the draft has them, and the draft's checks and things to look at put into its
    /// checkpoint. The steps the draft keeps as they were, from the first, stay as written, comments and all; the rest
    /// are written afresh, those unchanged as they were written. The checkpoint, and everything else, stays as it was.
    /// </summary>
    public static string Rewrite(string text, CaseDraft draft)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var layout = Read(bytes);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<(int At, int Length, string Text)>();
        var checkpoint = layout.Steps.Count > 0 && layout.Steps[^1].IsCheckpoint ? layout.Steps[^1] : null;
        var written = checkpoint is null ? layout.Steps : layout.Steps[..^1];
        string? Written(DraftStep step) =>
            step.Source is { } source && source < written.Count && Encoding.UTF8.GetString(bytes[written[source].Start..written[source].End]) is var original
            && JsonNode.DeepEquals(JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }), step.Json)
                ? original
                : null;

        // The steps kept as they were, from the first, and those after them.
        var steps = draft.Steps.ToList();
        var kept = 0;
        while (kept < steps.Count && kept < written.Count && steps[kept].Source == kept && Written(steps[kept]) is not null)
            kept++;
        var indent = layout.Steps.Count > 0 ? IndentAt(bytes, layout.Steps[0].Start) : "    ";
        var lines = steps.Skip(kept).Select(step => Written(step) ?? JsonLine.Step(step.Json, indent, newline)).ToList();
        if (checkpoint is null && draft.Checkpoint() is { } made)
            lines.Add(JsonLine.Step(made, indent, newline));
        if (kept < written.Count || lines.Count > 0)
        {
            var between = "," + newline + indent;
            var from = kept > 0 ? written[kept - 1].End : layout.Steps.Count > 0 ? layout.Steps[0].Start : layout.StepsEnd;
            var to = checkpoint?.Start ?? (written.Count > 0 ? written[^1].End : layout.StepsEnd);
            var join = string.Join(between, lines);
            var replace = (kept > 0, checkpoint is not null, layout.Steps.Count > 0) switch
            {
                (true, true, _) => between + (lines.Count > 0 ? join + between : ""),
                (true, false, _) => lines.Count > 0 ? between + join : "",
                (false, true, _) => lines.Count > 0 ? join + between : "",
                (false, false, true) => join,
                (false, false, false) => lines.Count > 0 ? newline + indent + join + newline + "  " : "",
            };
            edits.Add((from, to - from, replace));
        }
        if (checkpoint is not null)
        {
            var itemIndent = IndentAt(bytes, checkpoint.Start) + "    ";
            AddItems(edits, bytes, checkpoint, "checks", draft.Checks.Select(check => (JsonNode)check), itemIndent, newline);
            AddItems(edits, bytes, checkpoint, "look", draft.Look.Select(line => (JsonNode)JsonValue.Create(line)), itemIndent, newline);
        }

        foreach (var (at, length, insert) in edits.OrderByDescending(edit => edit.At))
            bytes = [.. bytes[..at], .. Encoding.UTF8.GetBytes(insert), .. bytes[(at + length)..]];
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// A case's text marked as needing looking at again, with why, as a review line after its name: the launcher
    /// shows a warning on it until it is changed and saved, or the warning is cleared.
    /// </summary>
    public static string Flag(string text, string why)
    {
        text = Unflag(text);
        var name = NameLine().Match(text);
        if (!name.Success)
            return text;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var line = $"\"review\": {JsonLine.Quote(why)}";
        var after = name.Index + name.Length;
        return name.Value.EndsWith(',')
            ? text[..after] + newline + name.Groups[1].Value + line + "," + text[after..]
            : text[..after] + "," + newline + name.Groups[1].Value + line + text[after..];
    }

    /// <summary>A case's text without its review line.</summary>
    public static string Unflag(string text)
    {
        var line = ReviewLine().Match(text);
        if (line.Success)
            return text.Remove(line.Index, line.Length);
        var last = ReviewLast().Match(text);
        return last.Success ? text.Remove(last.Index, last.Length) : text;
    }

    /// <summary>Clears a case's review in its file.</summary>
    public static void ClearReview(Case item) => File.WriteAllText(item.File, Unflag(File.ReadAllText(item.File)));

    /// <summary>
    /// Adds controls to watch and clicks to follow to suite.json's builder, keeping the rest of it as it was written:
    /// on the end of builder.watch and builder.afterClick, made when they aren't there.
    /// </summary>
    public static void AddHooks(Suite suite, IReadOnlyList<WatchHook> watch, IReadOnlyList<ClickHook> afterClick)
    {
        if (watch.Count == 0 && afterClick.Count == 0)
            return;
        var path = Path.Combine(suite.Folder, "suite.json");
        var text = File.ReadAllText(path);
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var items = new Dictionary<string, List<string>>
        {
            ["watch"] = [.. watch.Select(hook => JsonLine.Write(Hook(hook)))],
            ["afterClick"] = [.. afterClick.Select(hook => JsonLine.Write(Hook(hook)))],
        };
        var bytes = Encoding.UTF8.GetBytes(text);
        var reader = new Utf8JsonReader(bytes, ReadOptions);
        string? property = null;
        int? builderStart = null, rootEnd = null, lastRootValueEnd = null;
        var arrays = new Dictionary<string, (int Start, int? LastItemEnd)>();
        string? inArray = null;
        while (reader.Read())
        {
            var depth = reader.CurrentDepth;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    property = reader.GetString();
                    continue;
                case JsonTokenType.StartObject when depth == 1 && property == "builder":
                    builderStart = (int)reader.TokenStartIndex;
                    break;
                case JsonTokenType.StartArray when depth == 2 && builderStart is not null && property is "watch" or "afterClick":
                    inArray = property;
                    arrays[property] = ((int)reader.TokenStartIndex, null);
                    break;
                case JsonTokenType.EndArray when depth == 2 && inArray is not null:
                    inArray = null;
                    break;
                case JsonTokenType.EndObject when depth == 3 && inArray is not null:
                    arrays[inArray] = (arrays[inArray].Start, (int)reader.BytesConsumed);
                    break;
                case JsonTokenType.EndObject when depth == 0:
                    rootEnd = (int)reader.TokenStartIndex;
                    break;
            }
            if (depth == 1 && reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
                lastRootValueEnd = (int)reader.BytesConsumed;
            property = null;
        }
        if (rootEnd is null)
            throw new JsonException("suite.json isn't a JSON object.");
        var edits = new List<(int At, string Text)>();
        var indent = "      ";
        var missing = new List<string>();
        foreach (var (key, lines) in items.Where(pair => pair.Value.Count > 0))
        {
            if (arrays.TryGetValue(key, out var array))
                edits.Add(array.LastItemEnd is { } end
                    ? (end, "," + newline + indent + string.Join("," + newline + indent, lines))
                    : (array.Start + 1, newline + indent + string.Join("," + newline + indent, lines) + newline + "    "));
            else
                missing.Add($"{JsonLine.Quote(key)}: [{newline}{indent}{string.Join("," + newline + indent, lines)}{newline}    ]");
        }
        if (missing.Count > 0 && builderStart is { } start)
            edits.Add((start + 1, newline + "    " + string.Join("," + newline + "    ", missing) + ","));
        else if (missing.Count > 0)
            edits.Add((LastValueEnd(bytes, rootEnd.Value), "," + newline + "  \"builder\": {" + newline + "    " + string.Join("," + newline + "    ", missing) + newline + "  }"));
        foreach (var (at, insert) in edits.OrderByDescending(edit => edit.At))
            bytes = [.. bytes[..at], .. Encoding.UTF8.GetBytes(insert), .. bytes[at..]];
        File.WriteAllText(path, Encoding.UTF8.GetString(bytes));
    }

    private static JsonObject Hook(WatchHook hook)
    {
        var json = new JsonObject { ["element"] = hook.Element };
        if (hook.Call is { } call)
            json["call"] = call;
        if (hook.Check)
            json["check"] = true;
        return json;
    }

    private static JsonObject Hook(ClickHook hook)
    {
        var json = new JsonObject { ["element"] = hook.Element, ["call"] = hook.Call };
        if (hook.With is { Count: > 0 } with)
            json["with"] = with.DeepClone();
        if (hook.Delay > 0)
            json["delay"] = hook.Delay;
        return json;
    }

    // Where the last thing before an object's closing brace ends, so a property can go after it.
    private static int LastValueEnd(byte[] bytes, int close)
    {
        var at = close;
        while (at > 0 && bytes[at - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            at--;
        return at;
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
    private static void AddItems(List<(int At, int Length, string Text)> edits, byte[] bytes, StepLayout checkpoint, string key, IEnumerable<JsonNode> items, string indent, string newline)
    {
        var lines = items.Select(JsonLine.Write).ToList();
        if (lines.Count == 0)
            return;
        if (!checkpoint.Arrays.TryGetValue(key, out var array))
        {
            edits.Add((checkpoint.Start + 1, 0, $" {JsonLine.Quote(key)}: [ {string.Join(", ", lines)} ],"));
            return;
        }
        var multiline = Encoding.UTF8.GetString(bytes[array.Start..array.End]).Contains('\n', StringComparison.Ordinal);
        var separator = multiline ? "," + newline + indent : ", ";
        edits.Add(array.LastItemEnd is { } end
            ? (end, 0, separator + string.Join(separator, lines))
            : (array.Start + 1, 0, (multiline ? newline + indent : " ") + string.Join(separator, lines) + " "));
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
    internal static string Schema(Suite suite, string kind)
    {
        foreach (var item in suite.Cases)
        {
            if (JsonNode.Parse(File.ReadAllText(item.File), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["$schema"]?.GetValue<string>() is not { } schema)
                continue;
            schema = schema.Replace("case.schema.json", $"{kind}.schema.json", StringComparison.Ordinal);
            // The suite's files are a folder above its cases and macros, so a relative path goes up one fewer.
            return kind is not ("case" or "macro") && !schema.Contains("://", StringComparison.Ordinal) && schema.StartsWith("../", StringComparison.Ordinal) ? schema[3..] : schema;
        }
        return SchemaFolder + kind + ".schema.json";
    }

    internal static string NewLine(Suite suite) =>
        suite.Cases is [var item, ..] && File.ReadAllText(item.File).Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
        : suite.Cases.Count > 0 ? "\n" : Environment.NewLine;

    // A case's name line, with its indent: the review line goes after it.
    [GeneratedRegex(@"(?m)^([ \t]*)""name""[ \t]*:[ \t]*""(?:[^""\\\r\n]|\\.)*""[ \t]*,?")]
    internal static partial Regex NameLine();

    // A review line, as Flag writes it, on a line of its own with a comma after it.
    [GeneratedRegex(@"(?m)^[ \t]*""review""[ \t]*:[ \t]*""(?:[^""\\\r\n]|\\.)*""[ \t]*,[ \t]*\r?\n")]
    private static partial Regex ReviewLine();

    // A review written last, with the comma before it.
    [GeneratedRegex(@",\s*""review""\s*:\s*""(?:[^""\\\r\n]|\\.)*""")]
    private static partial Regex ReviewLast();

    [GeneratedRegex(@"^\d+")]
    internal static partial Regex Leading();

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
