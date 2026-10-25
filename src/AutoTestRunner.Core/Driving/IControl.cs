namespace AutoTestRunner.Driving;

/// <summary>A control that was found. What it says is read when asked, so it is up to date.</summary>
public interface IControl
{
    string Name { get; }
    string? Value { get; }
    string? HelpText { get; }
    string? AutomationId { get; }

    /// <summary>The picked item of a drop-down or list, or null.</summary>
    string? Selection { get; }

    bool IsEnabled { get; }
}
