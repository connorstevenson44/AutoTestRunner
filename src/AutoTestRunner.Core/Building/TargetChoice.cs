using System.Text.Json.Nodes;

namespace AutoTestRunner.Building;

/// <summary>
/// How a recorded step finds its control: the target as it goes in the script, a short form of it for the panel,
/// how sturdy it is and why.
/// </summary>
public sealed record TargetChoice(JsonNode Json, string Shown, TargetQuality Quality, string Why);
