using System.Collections;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace AutoTestRunner.Scripting;

/// <summary>
/// A helper that gives what an XPath finds first in XML, such as a .config file read with readFile. Namespaces are left
/// out, so //connectionString finds it whatever namespace the file puts it in.
/// </summary>
public sealed record XPathHelper(string Name, string Path) : Helper(Name)
{
    public override string Apply(string text)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (XmlException ex)
        {
            throw new ScriptException($"{Name} reads XML, and what it was given isn't: {ex.Message}");
        }
        foreach (var element in document.Descendants())
        {
            element.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Remove();
            element.Name = element.Name.LocalName;
        }
        return document.XPathEvaluate(Path) switch
        {
            string words => words,
            bool yes => yes ? "true" : "false",
            double number => number.ToString(CultureInfo.InvariantCulture),
            IEnumerable found => found.Cast<object>().Select(Words).FirstOrDefault() ?? "",
            var other => other?.ToString() ?? "",
        };
    }

    private static string Words(object node) => node switch
    {
        XElement element => element.Value,
        XAttribute attribute => attribute.Value,
        XText words => words.Value,
        XComment comment => comment.Value,
        _ => node.ToString() ?? "",
    };
}
