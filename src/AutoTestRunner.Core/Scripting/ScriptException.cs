namespace AutoTestRunner.Scripting;

/// <summary>A mistake in a script, such as an unknown variable, element or macro.</summary>
public class ScriptException(string message) : Exception(message);
