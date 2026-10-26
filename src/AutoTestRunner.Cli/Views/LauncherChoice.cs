using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>The suite and cases picked in the launcher, and how to run them.</summary>
internal sealed record LauncherChoice(Suite Suite, IReadOnlyList<Case> Cases, Options Options);
