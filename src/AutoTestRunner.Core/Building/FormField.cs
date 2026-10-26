namespace AutoTestRunner.Building;

/// <summary>
/// One field of a step's or check's form: the property it writes, what the form calls it, what it holds, and for a
/// choice, the words to choose from. The first field of a form is the step's or check's own key.
/// </summary>
public sealed record FormField(string Key, string Label, FieldType Type, IReadOnlyList<string>? Choices = null);
