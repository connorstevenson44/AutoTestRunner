namespace AutoTestRunner.Driving;

/// <summary>Thrown to end the whole run: the person stopped it, or an unattended run got stuck.</summary>
public sealed class StopRunException(string message) : Exception(message);
