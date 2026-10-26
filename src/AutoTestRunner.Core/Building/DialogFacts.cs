namespace AutoTestRunner.Building;

/// <summary>
/// A Windows message box or file picker that a control is in: its title, the message it shows, and for a picker
/// whether it picks a folder and the path it would open.
/// </summary>
public sealed record DialogFacts(string Title, string Message = "", bool IsPicker = false, bool PicksFolder = false, string? Path = null);
