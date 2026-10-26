namespace AutoTestRunner.Building;

/// <summary>
/// A value in the steps a macro is made from that can become one of its parameters: the step it is in, by its place
/// among them, the property, what it is now, and the name the parameter would have.
/// </summary>
public sealed record MacroValue(int Step, string Key, string Text, string Name);
