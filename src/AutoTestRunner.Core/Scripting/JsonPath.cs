using System.Globalization;
using System.Text.Json.Nodes;

namespace AutoTestRunner.Scripting;

/// <summary>
/// The part of JSONPath that reading a setting out of a file needs: $.a.b, $['a b'], $.list[0], $.list[-1] for the last,
/// * for every property or item, and $..name for a name at any depth. A name that isn't there exactly is looked for
/// ignoring case, as .NET reads appsettings.json. Filters and slices are left out on purpose.
/// </summary>
public sealed class JsonPath
{
    private readonly IReadOnlyList<Part> _parts;

    private JsonPath(string text, IReadOnlyList<Part> parts) => (Text, _parts) = (text, parts);

    public string Text { get; }

    /// <summary>Reads a path. Throws an ArgumentException saying where it doesn't read.</summary>
    public static JsonPath Parse(string text)
    {
        text = text.Trim();
        if (!text.StartsWith('$'))
            throw new ArgumentException("its jsonPath starts with $, such as $.ConnectionStrings.Orders.");
        var parts = new List<Part>();
        var at = 1;
        while (at < text.Length)
        {
            var deep = false;
            if (text[at] == '.')
            {
                deep = at + 1 < text.Length && text[at + 1] == '.';
                at += deep ? 2 : 1;
                if (at >= text.Length)
                    throw new ArgumentException("its jsonPath has a . with no name after it.");
                if (text[at] != '[')
                {
                    var start = at;
                    while (at < text.Length && text[at] is not ('.' or '['))
                        at++;
                    parts.Add(Named(text[start..at], deep));
                    continue;
                }
            }
            if (at >= text.Length || text[at] != '[')
                throw Unreadable(text, at);
            at++;
            if (at < text.Length && text[at] is '\'' or '"')
            {
                var end = text.IndexOf(text[at], at + 1);
                if (end < 0 || end + 1 >= text.Length || text[end + 1] != ']')
                    throw Unreadable(text, at);
                parts.Add(new Part(text[(at + 1)..end], null, Deep: deep));
                at = end + 2;
                continue;
            }
            var close = text.IndexOf(']', at);
            if (close < 0)
                throw Unreadable(text, at);
            var inside = text[at..close].Trim();
            parts.Add(inside == "*" ? new Part(null, null, Deep: deep)
                : int.TryParse(inside, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index) ? new Part(null, index, Deep: deep)
                : throw Unreadable(text, at));
            at = close + 1;
        }
        return new JsonPath(text, parts);
    }

    /// <summary>What the path finds in the JSON, in the order the JSON has them.</summary>
    public IEnumerable<JsonNode?> Find(JsonNode? root)
    {
        IEnumerable<JsonNode?> found = [root];
        foreach (var part in _parts)
        {
            var from = found;
            found = part.Deep ? from.SelectMany(AndBelow).SelectMany(part.In) : from.SelectMany(part.In);
        }
        return found;
    }

    public override string ToString() => Text;

    private static Part Named(string name, bool deep) =>
        name.Length == 0 ? throw new ArgumentException("its jsonPath has a . with no name after it.") : new Part(name == "*" ? null : name, null, deep);

    private static ArgumentException Unreadable(string text, int at) => new($"its jsonPath doesn't read from {text[Math.Min(at, text.Length - 1)..]}.");

    private static IEnumerable<JsonNode?> AndBelow(JsonNode? node)
    {
        yield return node;
        var children = node switch
        {
            JsonObject properties => properties.Select(pair => pair.Value),
            JsonArray items => items,
            _ => [],
        };
        foreach (var child in children)
            foreach (var below in AndBelow(child))
                yield return below;
    }

    // One step of a path: a property by its name, an item by its place, or, with neither, every property or item.
    private sealed record Part(string? Name, int? Index, bool Deep)
    {
        public IEnumerable<JsonNode?> In(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject properties when Name is not null:
                    if (properties.TryGetPropertyValue(Name, out var exact))
                        return [exact];
                    return properties.Where(pair => string.Equals(pair.Key, Name, StringComparison.OrdinalIgnoreCase)).Take(1).Select(pair => pair.Value);
                case JsonArray items when Index is { } index:
                    var place = index < 0 ? items.Count + index : index;
                    return place >= 0 && place < items.Count ? [items[place]] : [];
                case JsonObject properties when Index is null:
                    return properties.Select(pair => pair.Value);
                case JsonArray items when Name is null:
                    return items;
                default:
                    return [];
            }
        }
    }
}
