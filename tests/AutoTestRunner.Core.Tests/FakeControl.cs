using AutoTestRunner.Driving;

namespace AutoTestRunner.Core.Tests;

/// <summary>A control in the fake app.</summary>
internal sealed class FakeControl(string type, string name, string? id = null, string? helpText = null) : IControl
{
    public string Type { get; } = type;
    public string Name { get; set; } = name;
    public string? Value { get; set; }
    public string? HelpText { get; } = helpText;
    public string? AutomationId { get; } = id;
    public string? Selection { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool? IsChecked { get; set; }
    public bool IsSelected { get; set; }
    public bool HasFocus { get; set; }

    /// <summary>How many rows it has, as a list or a grid.</summary>
    public int Rows { get; set; }
}
