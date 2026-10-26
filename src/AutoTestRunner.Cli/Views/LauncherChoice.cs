using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>The cases picked in the launcher, and how to run them.</summary>
internal sealed record LauncherChoice(IReadOnlyList<Case> Cases, Options Options);
