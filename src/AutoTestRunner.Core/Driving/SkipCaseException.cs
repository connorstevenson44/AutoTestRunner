namespace AutoTestRunner.Driving;

/// <summary>Thrown to give up on the rest of a case, after the person chose to skip it.</summary>
public sealed class SkipCaseException(string message) : Exception(message);
