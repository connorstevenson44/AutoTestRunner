namespace AutoTestRunner.Scripting;

/// <summary>
/// A control to find: the selectors to try in order, and what to call it in a message. Element is the name it has in
/// elements.json, with the values it was given, such as orderRow 1001.
/// </summary>
public sealed record Target(IReadOnlyList<Selector> Selectors, string What)
{
    public string? Element { get; init; }

    /// <summary>
    /// The control's name on a badge: the words on it when the step names it by them, such as Add order, else its name
    /// in elements.json, else its tooltip or AutomationId.
    /// </summary>
    public string Label
    {
        get
        {
            var first = Selectors[0];
            if (first.Name is { } name && (Element is null || !string.Equals(first.Type, "Text", StringComparison.OrdinalIgnoreCase)))
                return name.Trim();
            return Element ?? first.HelpText ?? first.Id ?? (first.Cell is { } cell ? $"{cell.Column} of {cell.Row}" : What);
        }
    }

    public static Target Of(Selector selector) => new([selector], selector.Describe());
}
