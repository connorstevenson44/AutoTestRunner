using System.Text.Json.Nodes;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// What checks look at besides files: the machine's services, programs and logs, its databases and queues, what the
/// suite's leftovers script finds, the app's controls by their target, and when the case started, for logs written since.
/// </summary>
public sealed record CheckContext(IMachine Machine, Func<IReadOnlyList<string>> Leftovers, Func<JsonNode, ControlProbe>? Controls = null, DateTime? Since = null)
{
    /// <summary>The databases and queues, reached through their drivers.</summary>
    public IConnections? Connections { get; init; }

    /// <summary>A database by its name in suite.json, with its variables filled in.</summary>
    public Func<string, DatabaseSettings>? Database { get; init; }

    /// <summary>A queue by its name in suite.json, with its variables filled in.</summary>
    public Func<string, QueueSettings>? Queue { get; init; }
}
