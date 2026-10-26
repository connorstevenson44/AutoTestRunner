namespace AutoTestRunner.Building;

/// <summary>How well a recorded step will find its control again, shown as a coloured dot beside it.</summary>
public enum TargetQuality
{
    /// <summary>By a name in elements.json or an AutomationId: it keeps working when the wording changes.</summary>
    Sturdy,

    /// <summary>By its words or tooltip: it breaks when they change.</summary>
    Wording,

    /// <summary>More than one control matches, and the first is used.</summary>
    Ambiguous,
}
