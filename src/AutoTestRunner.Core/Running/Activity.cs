namespace AutoTestRunner.Running;

/// <summary>
/// A step as the panel and the log show it: a verb and what it acts on, with no sentence around them, such as Click
/// [Add order] or Type "Ada" in [customer]. Say is the script's own words for the step, which the panel shows when
/// pointing at it; Detail is what the step saw, such as a message box's text; Command is the command line a run,
/// PowerShell or script step is about to start.
/// </summary>
public sealed record Activity(ActivityKind Kind, IReadOnlyList<Part> Parts)
{
    public string? Say { get; init; }

    public string? Detail { get; init; }

    public string? Command { get; init; }

    /// <summary>The line as words, for run.log: Click Add order, Type "Ada" in customer.</summary>
    public string Text => string.Join(" ", Parts.Select(part => part.Kind == PartKind.Value ? $"\"{part.Text}\"" : part.Text));

    public static Activity Of(ActivityKind kind, params Part[] parts) => new(kind, parts);

    /// <summary>A line that is only words: narration, or a note.</summary>
    public static Activity Words(string text, ActivityKind kind = ActivityKind.Say) => new(kind, [Part.Word(text)]);

    public override string ToString() => Text;
}
