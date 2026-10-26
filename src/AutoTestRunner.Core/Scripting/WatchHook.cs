namespace AutoTestRunner.Scripting;

/// <summary>
/// A control the builder watches while recording, from suite.json's builder.watch: when its text changes after a
/// step, the builder adds a wait for the new text, through the macro given or a waitText of its own; with check, it
/// keeps the text too and checks it at the checkpoint.
/// </summary>
/// <param name="Element">The control's name in elements.json.</param>
/// <param name="Call">The macro that waits for it, given startsWith when it takes it; null waits with waitText.</param>
/// <param name="Check">Whether the checkpoint checks what it said.</param>
public sealed record WatchHook(string Element, string? Call = null, bool Check = false);
