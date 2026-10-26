namespace AutoTestRunner.Cli;

/// <summary>What the builder makes of the person's clicks in the app.</summary>
internal enum BuildMode
{
    /// <summary>Nothing: the builder is getting the app ready or playing a step, or recording is off.</summary>
    Paused,

    /// <summary>Each click, pick, tick and bit of typing becomes a step. Ctrl+click uses the last pick tool instead.</summary>
    Recording,

    /// <summary>
    /// A pick tool, or a form, waits for a control: the mouse only points, and a click picks the control under it
    /// without reaching the app.
    /// </summary>
    Picking,
}
