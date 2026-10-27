using AutoTestRunner.Building;

namespace AutoTestRunner.Cli;

/// <summary>
/// Something the person did, passed from the hook's thread or the panel's UI thread to the builder's thread, where the
/// app is read and driven.
/// </summary>
/// <param name="Kind">What it was.</param>
/// <param name="X">Where a click was, in pixels.</param>
/// <param name="Y">Where a click was, in pixels.</param>
/// <param name="Keys">The key pressed, as a key step writes it.</param>
/// <param name="PassOn">Whether the click or key was kept from the app until the builder had read it, and is passed on now.</param>
/// <param name="Step">The step to play or outline.</param>
/// <param name="Add">Whether a step played goes in the case.</param>
/// <param name="Rewind">For EditFrom: whether the app has to start again, as it went past the step.</param>
/// <param name="Check">For Try: the check to run against the app as it is.</param>
/// <param name="Answer">For Try: called on the builder's thread with how the check went.</param>
/// <param name="Macro">For Use: a macro made while building, for the runner to play.</param>
/// <param name="Exact">For Pick: Shift was held, so the control picked is the one under the mouse, even one no step can find.</param>
internal sealed record BuildInput(
    BuildInputKind Kind, int X = 0, int Y = 0, string? Keys = null, bool PassOn = false, DraftStep? Step = null, bool Add = false, bool Rewind = false,
    System.Text.Json.Nodes.JsonObject? Check = null, Action<Running.CheckResult>? Answer = null, Scripting.Macro? Macro = null, bool Exact = false);
