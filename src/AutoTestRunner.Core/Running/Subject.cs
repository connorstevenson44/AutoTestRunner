namespace AutoTestRunner.Running;

/// <summary>
/// What a check looks at: what kind of thing, for its icon, its name, and a detail such as the setting in a file.
/// Tip is what pointing at it shows, such as the file's whole path.
/// </summary>
public sealed record Subject(SubjectKind Kind, string Name, string? Detail = null, string? Tip = null);
