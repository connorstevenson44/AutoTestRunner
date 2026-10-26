using AutoTestRunner.Driving;

namespace AutoTestRunner.Running;

/// <summary>A control a check looks at: what to call it, the control if it is on screen now, and how many rows one has.</summary>
public sealed record ControlProbe(string What, Func<IControl?> Find, Func<IControl, int> Rows);
