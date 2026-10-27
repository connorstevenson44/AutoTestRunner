using System.Text.RegularExpressions;

namespace AutoTestRunner.Scripting;

/// <summary>A helper that gives a regex's first group in the text, or the whole match when it has no group.</summary>
public sealed record RegexHelper(string Name, Regex Pattern) : Helper(Name)
{
    public override string Apply(string text)
    {
        var match = Pattern.Match(text);
        return match.Success ? (match.Groups.Count > 1 ? match.Groups[1].Value : match.Value) : "";
    }
}
