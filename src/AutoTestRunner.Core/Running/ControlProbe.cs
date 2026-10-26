using AutoTestRunner.Driving;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>A control a check looks at: how to find it, the control if it is on screen now, and how many rows one has.</summary>
public sealed record ControlProbe(Target Target, Func<IControl?> Find, Func<IControl, int> Rows);
