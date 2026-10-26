namespace AutoTestRunner.Building;

/// <summary>
/// Where the control of a step a run is stuck on is written: an element in elements.json, or the step itself, by its
/// place among the top-level steps of a case or macro file.
/// </summary>
public sealed record TargetFix(string File, string? Element, int? Step)
{
    /// <summary>Where it goes, in a few words: "elements.json, as statusText" or "step 3 of 02-delete-it.json".</summary>
    public string Shown => Element is { } name ? $"elements.json, as {name}" : $"step {Step + 1} of {Path.GetFileName(File)}";
}
