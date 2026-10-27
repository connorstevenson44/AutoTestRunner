using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.XPath;

namespace AutoTestRunner.Scripting;

/// <summary>
/// A helper a suite registers for expressions, such as logFile(status): what it picks out of the text it is given, by a
/// regex, an XPath or a JSONPath.
/// </summary>
public abstract record Helper(string Name)
{
    /// <summary>The ways a helper can be written in suite.json.</summary>
    public static readonly IReadOnlyList<string> Ways = ["regex", "xPath", "jsonPath"];

    /// <summary>What it picks out of the text; empty when it finds nothing there.</summary>
    public abstract string Apply(string text);

    /// <summary>A helper as suite.json writes it. Throws an ArgumentException saying what is wrong with it.</summary>
    public static Helper From(string name, JsonNode? node)
    {
        var given = Ways.Where(way => node?[way] is not null).ToList();
        if (given.Count != 1)
            throw new ArgumentException("it needs one of regex, xPath or jsonPath.");
        var pattern = node![given[0]] is JsonValue value && value.TryGetValue(out string? text)
            ? text
            : throw new ArgumentException($"its {given[0]} must be text.");
        try
        {
            return given[0] switch
            {
                "regex" => new RegexHelper(name, new Regex(pattern, RegexOptions.Multiline)),
                "xPath" => new XPathHelper(name, XPathExpression.Compile(pattern).Expression),
                _ => new JsonPathHelper(name, JsonPath.Parse(pattern)),
            };
        }
        catch (XPathException ex)
        {
            throw new ArgumentException($"its xPath doesn't read: {ex.Message}", ex);
        }
    }
}
