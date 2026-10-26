namespace AutoTestRunner.Building;

/// <summary>A check as the builder offers it: its key, what it is called on the menu, and the group it is under.</summary>
public sealed record CheckKind(string Key, string Title, string Group);
