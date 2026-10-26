namespace AutoTestRunner.Scripting;

/// <summary>
/// Scripts that put the machine back as it was before the suite ran, and list what is left over: each a .ps1 or a .cs
/// file in the suite.
/// </summary>
public sealed record ResetSettings(string? Script, string? Leftovers);
