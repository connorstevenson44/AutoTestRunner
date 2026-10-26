namespace AutoTestRunner.Scripting;

/// <summary>Scripts that put the machine back as it was before the suite ran, and list what is left over.</summary>
public sealed record ResetSettings(string? PowerShell, string? Leftovers);
