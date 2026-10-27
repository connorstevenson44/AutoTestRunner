using System.Text;
using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// The rows of a data file, for a repeat that runs its steps once for each: a CSV file whose first line names the
/// columns, or a JSON list of objects. Each row's text is its first column, and each column is a part of it, as
/// <c>${row.Customer}</c>.
/// </summary>
public static class DataRows
{
    public static List<Value> Read(string file)
    {
        if (!File.Exists(file))
            throw new ScriptException($"There is no data file {file}.");
        return Path.GetExtension(file).Equals(".json", StringComparison.OrdinalIgnoreCase) ? Json(file) : Csv(file);
    }

    private static List<Value> Json(string file)
    {
        var rows = ScriptJson.ReadFile(file) as JsonArray
            ?? throw new ScriptException($"{Path.GetFileName(file)} must be a list of objects.");
        return [.. rows.Select(row => row as JsonObject ?? throw new ScriptException($"{Path.GetFileName(file)} must be a list of objects."))
            .Select(row => Row([.. row.Select(pair => (pair.Key, pair.Value is JsonValue value && value.TryGetValue(out string? text) ? text : pair.Value?.ToJsonString() ?? ""))]))];
    }

    private static List<Value> Csv(string file)
    {
        var lines = Lines(File.ReadAllText(file)).Where(line => line.Count > 1 || line[0].Length > 0).ToList();
        if (lines.Count == 0)
            return [];
        var headers = lines[0];
        return [.. lines.Skip(1).Select(line => Row([.. headers.Select((header, index) => (header.Trim(), index < line.Count ? line[index] : ""))]))];
    }

    private static Value Row(List<(string Column, string Text)> cells) =>
        Value.Of(cells.Count > 0 ? cells[0].Text : "", cells.ToDictionary(cell => cell.Column, cell => cell.Text, StringComparer.Ordinal));

    // Fields split by commas, a field in double quotes keeping its commas, line breaks and doubled quotes.
    private static IEnumerable<List<string>> Lines(string text)
    {
        var line = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (quoted)
            {
                if (c == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    line.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    line.Add(field.ToString());
                    field.Clear();
                    yield return line;
                    line = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        if (field.Length > 0 || line.Count > 0)
        {
            line.Add(field.ToString());
            yield return line;
        }
    }
}
