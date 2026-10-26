namespace AutoTestRunner.Building;

/// <summary>What a pick tool in the builder's toolbar does with the control clicked after it.</summary>
public enum PickAction
{
    Click,
    Hover,
    WaitFor,
    WaitGone,
    WaitText,
    WaitEnabled,
    Keep,
    CheckShows,
    CheckText,
    Check,
    SetValue,
    Name,
    Watch,
    AfterClick,
}
