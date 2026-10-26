namespace AutoTestRunner.Scripting;

/// <summary>How to start the app, and the control whose text says what it is doing, such as a status bar.</summary>
public sealed record AppSettings(string Exe, string Window, bool RunAsAdmin, string? Status, string? Arguments);
