namespace AutoTestRunner.Cli;

/// <summary>What to do with a picked control, from the buttons under it in the builder panel.</summary>
internal enum PickAction { Click, Hover, WaitFor, WaitGone, WaitText, Keep, CheckShows, CheckText, Name }
