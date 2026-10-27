using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Core.Tests;

/// <summary>
/// Databases that answer each SQL with the rows the test gives, and queues that are lists of messages. It remembers
/// each query, with its database's connection and parameters as the runner filled them in.
/// </summary>
internal sealed class FakeConnections : IConnections
{
    public Dictionary<string, QueryResult> Answers { get; } = new(StringComparer.Ordinal);
    public List<(DatabaseSettings Database, string Sql, IReadOnlyDictionary<string, string> Parameters)> Queries { get; } = [];
    public Dictionary<string, List<string>> Queues { get; } = new(StringComparer.Ordinal);

    public static QueryResult Rows(params string?[][] rows) => new(["value"], rows, rows.Length);

    public QueryResult Query(DatabaseSettings database, string sql, IReadOnlyDictionary<string, string> parameters, TimeSpan timeout)
    {
        Queries.Add((database, sql, parameters));
        return Answers.TryGetValue(sql, out var answer) ? answer : throw new InvalidOperationException($"relation in \"{sql}\" does not exist");
    }

    public void Send(QueueSettings queue, string body) => Of(queue).Add(body);

    public string? Receive(QueueSettings queue, TimeSpan within)
    {
        var waiting = Of(queue);
        if (waiting.Count == 0)
            return null;
        var body = waiting[0];
        waiting.RemoveAt(0);
        return body;
    }

    public int Purge(QueueSettings queue)
    {
        var count = Of(queue).Count;
        Of(queue).Clear();
        return count;
    }

    public IReadOnlyList<string> Peek(QueueSettings queue, int most) => [.. Of(queue).Take(most)];

    public int Count(QueueSettings queue) => Of(queue).Count;

    private List<string> Of(QueueSettings queue) => Queues.TryGetValue(queue.Queue, out var list) ? list : Queues[queue.Queue] = [];
}
