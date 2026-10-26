namespace AutoTestRunner.Building;

/// <summary>
/// A control in the app that has no AutomationId: whether it has a name to find it by instead, which is less sturdy,
/// or nothing, and the XAML that gives it an id.
/// </summary>
public sealed record IdGap(ControlFacts Control, bool Named, string Xaml);
