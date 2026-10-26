using AutoTestRunner.Building;

namespace AutoTestRunner.Cli;

/// <summary>
/// Something the person did, passed from the UI thread, where the hook and the panel are, to the builder's thread,
/// where the app is read and driven.
/// </summary>
/// <param name="Kind">What it was.</param>
/// <param name="X">Where a click was, in pixels.</param>
/// <param name="Y">Where a click was, in pixels.</param>
/// <param name="Keys">The key pressed, as a key step writes it.</param>
/// <param name="PassOn">Whether the click or key was kept from the app until the builder had read it, and is passed on now.</param>
/// <param name="Step">The step to play or outline.</param>
/// <param name="Add">Whether a step played goes in the case.</param>
internal sealed record BuildInput(BuildInputKind Kind, int X = 0, int Y = 0, string? Keys = null, bool PassOn = false, DraftStep? Step = null, bool Add = false);
