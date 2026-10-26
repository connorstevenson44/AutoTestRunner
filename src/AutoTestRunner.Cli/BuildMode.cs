namespace AutoTestRunner.Cli;

/// <summary>What the builder makes of the person's clicks in the app.</summary>
internal enum BuildMode
{
    /// <summary>Nothing: the builder is getting the app ready or playing a step, or recording is off.</summary>
    Paused,

    /// <summary>Each click, pick, tick and bit of typing becomes a step. Ctrl+click picks instead.</summary>
    Recording,

    /// <summary>The mouse only points: a click picks the control under it, and doesn't reach the app.</summary>
    Picking,
}
