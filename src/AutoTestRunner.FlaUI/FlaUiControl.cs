using AutoTestRunner.Driving;
using FlaUI.Core.AutomationElements;

namespace AutoTestRunner.FlaUI;

/// <summary>A control FlaUI found. What it says is read each time it is asked.</summary>
internal sealed class FlaUiControl(AutomationElement element) : IControl
{
    public AutomationElement Element { get; } = element;

    public string Name => Element.Properties.Name.ValueOrDefault ?? "";

    public string? Value => Element.Patterns.Value.IsSupported ? Element.Patterns.Value.Pattern.Value.ValueOrDefault : null;

    public string? HelpText => Element.Properties.HelpText.ValueOrDefault;

    public string? AutomationId => Element.Properties.AutomationId.ValueOrDefault;

    public string? Selection
    {
        get
        {
            if (Element.Patterns.Selection.IsSupported && Element.Patterns.Selection.Pattern.Selection.ValueOrDefault is [var picked, ..])
                return picked.Properties.Name.ValueOrDefault;
            return Value;
        }
    }

    public bool IsEnabled => Element.Properties.IsEnabled.ValueOrDefault;
}
