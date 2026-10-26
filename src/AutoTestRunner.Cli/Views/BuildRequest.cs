using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A case to build: a new one with its name, starting from a fresh start of the app or from where another case leaves
/// off; or steps to add to an existing case.
/// </summary>
internal sealed record BuildRequest(Suite Suite, string Name, Case? After = null, Case? AddTo = null);
