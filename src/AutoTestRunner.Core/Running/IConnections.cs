using AutoTestRunner.Scripting;

namespace AutoTestRunner.Running;

/// <summary>
/// The databases and message queues a suite names, reached through their own drivers. Steps and checks go through
/// this, with every ${variable} already filled in, so the engine's tests can stand in for the servers.
/// </summary>
public interface IConnections
{
    /// <summary>Runs SQL with named parameters, such as @id, and gives the rows it read, or how many it changed.</summary>
    QueryResult Query(DatabaseSettings database, string sql, IReadOnlyDictionary<string, string> parameters, TimeSpan timeout);

    /// <summary>Puts a message on the queue.</summary>
    void Send(QueueSettings queue, string body);

    /// <summary>Takes the next message off the queue, waiting up to within for one; null when none came.</summary>
    string? Receive(QueueSettings queue, TimeSpan within);

    /// <summary>Takes every message off the queue, and gives how many there were.</summary>
    int Purge(QueueSettings queue);

    /// <summary>The bodies of the messages waiting, up to most, leaving them on the queue.</summary>
    IReadOnlyList<string> Peek(QueueSettings queue, int most);

    /// <summary>How many messages are waiting.</summary>
    int Count(QueueSettings queue);
}
