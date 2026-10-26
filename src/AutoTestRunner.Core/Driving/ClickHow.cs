namespace AutoTestRunner.Driving;

/// <summary>
/// How a click step clicks: through UI Automation, which works whatever covers the control, or with the mouse, once,
/// with the right button or twice.
/// </summary>
public enum ClickHow { Invoke, Mouse, Right, Double }
