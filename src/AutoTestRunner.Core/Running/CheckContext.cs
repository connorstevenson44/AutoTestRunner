using System.Text.Json.Nodes;

namespace AutoTestRunner.Running;

/// <summary>
/// What checks look at besides files: the machine's services, programs and logs, what the suite's leftovers script
/// finds, the app's controls by their target, and when the case started, for logs written since.
/// </summary>
public sealed record CheckContext(IMachine Machine, Func<IReadOnlyList<string>> Leftovers, Func<JsonNode, ControlProbe>? Controls = null, DateTime? Since = null);
