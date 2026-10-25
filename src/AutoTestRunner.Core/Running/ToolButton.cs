namespace AutoTestRunner.Running;

/// <summary>
/// A button on a checkpoint that opens something to look at, such as a folder or a file. Kind is the tool's name in the
/// script: openFolder, openFile, app or console.
/// </summary>
public sealed record ToolButton(string Label, Action Open, string Kind = "app");
