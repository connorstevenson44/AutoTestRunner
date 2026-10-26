using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AutoTestRunner.Building;

/// <summary>
/// Writes JSON the way the suites are written by hand: a step on one line, <c>{ "click": "addOrder", "say": "Click
/// Add order" }</c>, and a step too long for a line, such as a checkpoint, with each property and each check on a line
/// of its own.
/// </summary>
public static class JsonLine
{
    private const int Width = 120;

    private static readonly JsonSerializerOptions Text = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The node on one line.</summary>
    public static string Write(JsonNode? node) => node switch
    {
        JsonObject { Count: 0 } => "{}",
        JsonObject item => "{ " + string.Join(", ", item.Select(pair => $"{Quote(pair.Key)}: {Write(pair.Value)}")) + " }",
        JsonArray { Count: 0 } => "[]",
        JsonArray list when list.All(entry => entry is JsonValue) => "[" + string.Join(", ", list.Select(Write)) + "]",
        JsonArray list => "[ " + string.Join(", ", list.Select(Write)) + " ]",
        null => "null",
        _ => node.ToJsonString(Text),
    };

    /// <summary>A step at this indent: on one line when it fits, else a property to a line and an array's items to a line each.</summary>
    public static string Step(JsonObject step, string indent, string newline)
    {
        var line = Write(step);
        if (indent.Length + line.Length <= Width)
            return line;
        var text = new StringBuilder("{ ");
        var inner = indent + "  ";
        var first = true;
        foreach (var (key, value) in step)
        {
            if (!first)
                text.Append(',').Append(newline).Append(inner);
            first = false;
            var property = $"{Quote(key)}: {Write(value)}";
            if (value is JsonArray { Count: > 0 } list && inner.Length + property.Length > Width)
            {
                text.Append(Quote(key)).Append(": [").Append(newline);
                text.Append(string.Join("," + newline, list.Select(item => inner + "  " + Write(item))));
                text.Append(newline).Append(inner).Append(']');
            }
            else
                text.Append(property);
        }
        return text.Append(" }").ToString();
    }

    public static string Quote(string text) => JsonSerializer.Serialize(text, Text);
}
