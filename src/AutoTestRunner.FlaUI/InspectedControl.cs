using AutoTestRunner.Building;
using FlaUI.Core.AutomationElements;

namespace AutoTestRunner.FlaUI;

/// <summary>A control of the app's the person pointed at or used, and what UI Automation said about it then.</summary>
public sealed class InspectedControl
{
    internal InspectedControl(AutomationElement element, ControlFacts facts) => (Element, Facts) = (element, facts);

    internal AutomationElement Element { get; }

    public ControlFacts Facts { get; }

    /// <summary>Whether this is the same control as another, by UI Automation's runtime id.</summary>
    public bool Is(InspectedControl? other)
    {
        try
        {
            return other is not null && Element.Equals(other.Element);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}
