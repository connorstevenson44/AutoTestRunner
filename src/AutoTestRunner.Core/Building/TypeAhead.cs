using AutoTestRunner.Scripting;

namespace AutoTestRunner.Building;

/// <summary>
/// The list that drops down under a box while an expression is typed in it, as after "${": the kept values and suite
/// variables, and the calls an expression can make, readFile and the suite's helpers. This works out what is being
/// typed and what picking one writes; the box and its list are the window's.
/// </summary>
public static class TypeAhead
{
    /// <summary>readFile and the suite's helpers, each with what it does.</summary>
    public static IReadOnlyList<Suggestion> Calls(IReadOnlyDictionary<string, Helper> helpers) =>
    [
        new(VariableScope.ReadFile, "a file's text, from the suite folder", IsCall: true),
        .. helpers.Values.OrderBy(helper => helper.Name, StringComparer.OrdinalIgnoreCase).Select(helper => new Suggestion(helper.Name, What(helper), IsCall: true)),
    ];

    /// <summary>
    /// Where the name being typed at the caret starts, and what of it is typed, when the caret is in a ${ not yet
    /// closed: just after the ${, or just after the ( of a call. Null anywhere else, such as after a dot or in plain text.
    /// </summary>
    public static (int Start, string Typed)? Typing(string text, int caret)
    {
        if (caret < 2)
            return null;
        var open = text.LastIndexOf("${", caret - 1, StringComparison.Ordinal);
        // "$${" writes a "${" rather than starting an expression.
        if (open < 0 || open > 0 && text[open - 1] == '$' || text.IndexOf('}', open, caret - open) >= 0)
            return null;
        var start = caret;
        while (start > open + 2 && IsNamePart(text[start - 1]))
            start--;
        if (start > open + 2 && text[start - 1] != '(')
            return null;
        return (start, text[start..caret]);
    }

    /// <summary>The suggestions with what is typed in their names, those that start with it first.</summary>
    public static IReadOnlyList<Suggestion> Matching(IEnumerable<Suggestion> all, string typed) =>
    [
        .. all.Where(suggestion => suggestion.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(suggestion => suggestion.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1),
    ];

    /// <summary>
    /// The text with the name the caret is in, from start, written as the suggestion, and where the caret goes: after
    /// the } that closes a value, or between the brackets of a call so what it is given can be typed next. A } or a
    /// bracket already there is used rather than doubled.
    /// </summary>
    public static (string Text, int Caret) Put(string text, int start, int caret, Suggestion suggestion)
    {
        var end = caret;
        while (end < text.Length && IsNamePart(text[end]))
            end++;
        // Inside a call, as in readFile(appConfig), the call's own } closes it.
        var inCall = start > 0 && text[start - 1] == '(';
        var before = text[..start] + suggestion.Name;
        var after = text[end..];
        if (suggestion.IsCall)
        {
            before += "(";
            after = after.StartsWith('(') ? after[1..] : ")" + (inCall || Closed(after) ? "" : "}") + after;
        }
        else if (!inCall && after.StartsWith('}'))
            (before, after) = (before + "}", after[1..]);
        else if (!inCall && !Closed(after))
            before += "}";
        return (before + after, before.Length);
    }

    private static bool IsNamePart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

    // Whether the text after the name has the } that ends its expression, before any other expression starts.
    private static bool Closed(string after)
    {
        var close = after.IndexOf('}');
        var next = after.IndexOf("${", StringComparison.Ordinal);
        return close >= 0 && (next < 0 || close < next);
    }

    private static string What(Helper helper) => helper switch
    {
        RegexHelper regex => $"regex {regex.Pattern}",
        XPathHelper xPath => $"xPath {xPath.Path}",
        JsonPathHelper jsonPath => $"jsonPath {jsonPath.Path}",
        _ => "one of the suite's helpers",
    };
}
