using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// A part of a step's line. The panel draws each kind in its own way: a control as a badge, a value as data, a key as
/// a key. Tip is what pointing at it shows, such as how a control was found or a file's whole path.
/// </summary>
public sealed record Part(PartKind Kind, string Text, string? Tip = null)
{
    public static Part Verb(string text) => new(PartKind.Verb, text);

    public static Part Word(string text) => new(PartKind.Word, text);

    public static Part Value(string text) => new(PartKind.Value, text);

    public static Part Key(string text) => new(PartKind.Key, text);

    public static Part Window(string title) => new(PartKind.Window, title);

    public static Part Service(string name) => new(PartKind.Service, name);

    /// <summary>A file or folder by its name, with its whole path to point at.</summary>
    public static Part File(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return new(PartKind.File, name.Length > 0 ? name : path, path);
    }

    /// <summary>A control, by the name a person knows it by, with how it was found and what it is to point at.</summary>
    public static Part Control(Target target, IControl? found = null) => new(PartKind.Control, target.Label, TipOf(target, found));

    private static string TipOf(Target target, IControl? found)
    {
        var lines = new List<string>();
        if (found is not null)
        {
            lines.Add(found.Name.Length is > 0 and <= 80 ? $"{found.Type} \"{found.Name}\"" : found.Type);
            if (!string.IsNullOrEmpty(found.AutomationId))
                lines.Add($"AutomationId  {found.AutomationId}");
            if (!string.IsNullOrEmpty(found.HelpText))
                lines.Add($"Tooltip  {found.HelpText}");
        }
        if (target.Element is { } element)
            lines.Add($"elements.json  {element}");
        lines.Add($"Found by  {string.Join("  or  ", target.Selectors.Select(selector => selector.Shown()))}");
        return string.Join("\n", lines);
    }
}
