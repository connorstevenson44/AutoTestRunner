namespace AutoTestRunner.Cli;

/// <summary>What happened, for the builder to make a step of.</summary>
internal enum BuildInputKind
{
    /// <summary>A click in the app while recording.</summary>
    Click,

    /// <summary>A click that picks the control under it, while picking or with Ctrl held.</summary>
    Pick,

    /// <summary>A key a step can press, such as Enter or Ctrl+S, while recording.</summary>
    Key,

    /// <summary>A step from the panel to play in the app: a picked control's, or one to try again.</summary>
    Play,

    /// <summary>The mouse went over a step in the panel, to outline its control in the app; or left it.</summary>
    Locate,
}
