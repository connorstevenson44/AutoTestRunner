using AutoTestRunner.Scripting;

namespace AutoTestRunner.Cli.Views;

/// <summary>
/// A case to build: a new one with its name, starting from a fresh start of the app, from where another case leaves
/// off, or, with Reset, after another case has run and its reset steps have taken the app back; or an existing case
/// to change.
/// </summary>
internal sealed record BuildRequest(Suite Suite, string Name, Case? After = null, Case? Edit = null, bool Reset = false);
