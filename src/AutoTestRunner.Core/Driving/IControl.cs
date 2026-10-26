namespace AutoTestRunner.Driving;

/// <summary>A control that was found. What it says is read when asked, so it is up to date.</summary>
public interface IControl
{
    /// <summary>The UI Automation control type, such as Button or Edit.</summary>
    string Type { get; }

    string Name { get; }
    string? Value { get; }
    string? HelpText { get; }
    string? AutomationId { get; }

    /// <summary>The picked item of a drop-down or list, or null.</summary>
    string? Selection { get; }

    bool IsEnabled { get; }

    /// <summary>Whether a tick box is ticked; null when it isn't one.</summary>
    bool? IsChecked { get; }

    /// <summary>Whether an item of a list, a tab or an option is the one picked.</summary>
    bool IsSelected { get; }

    /// <summary>Whether it has the keyboard.</summary>
    bool HasFocus { get; }

    /// <summary>Where it is on the screen; null when it can't be told or takes no room.</summary>
    ScreenRect? Bounds { get; }
}
