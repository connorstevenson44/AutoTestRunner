namespace AutoTestRunner.Building;

/// <summary>
/// A name the type-ahead offers in an expression, with what it holds now or does. A call, such as readFile or one of
/// the suite's helpers, does something with what it is given, where any other name is a value.
/// </summary>
public sealed record Suggestion(string Name, string Detail, bool IsCall = false);
