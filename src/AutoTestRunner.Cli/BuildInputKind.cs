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

    /// <summary>A step from the panel to add without playing it, such as a new script to write first.</summary>
    Add,

    /// <summary>Plays the steps not played yet, up to and including this one.</summary>
    RunTo,

    /// <summary>Starts the app again from where the case starts, and plays the steps up to and including this one.</summary>
    RollBack,

    /// <summary>
    /// The steps after this one were taken out: gets the app to just after it, starting again first when the steps
    /// taken out had been played, and records from there.
    /// </summary>
    EditFrom,

    /// <summary>A check from a form's Try now, to run against the app as it is.</summary>
    Try,

    /// <summary>A macro made of steps in the panel, for the runner to play when the steps are played again.</summary>
    Use,
}
