namespace AutoTestRunner.Scripting;

/// <summary>
/// How to start the app, and the control whose text says what it is doing, such as a status bar. StartWindow is the
/// title of the window it opens first, such as a sign-in window, when that isn't its main window.
/// </summary>
public sealed record AppSettings(string Exe, string Window, bool RunAsAdmin, string? Status, string? Arguments, string? StartWindow = null);
