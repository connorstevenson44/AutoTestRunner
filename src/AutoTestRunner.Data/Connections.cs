using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Data;

/// <summary>
/// The suite's databases and message queues, each reached with its vendor's own .NET driver: SqlClient for SQL Server,
/// Npgsql, Oracle's managed driver, MySqlConnector, Microsoft.Data.Sqlite, RabbitMQ.Client and IBM's MQ client. MSMQ,
/// which .NET has no client for, goes through Windows PowerShell's System.Messaging.
/// </summary>
public sealed class Connections(IMachine machine) : IConnections
{
    public QueryResult Query(DatabaseSettings database, string sql, IReadOnlyDictionary<string, string> parameters, TimeSpan timeout) =>
        Databases.Query(database, sql, parameters, timeout);

    public void Send(QueueSettings queue, string body) => Broker(queue).Send(queue, body);

    public string? Receive(QueueSettings queue, TimeSpan within) => Broker(queue).Receive(queue, within);

    public int Purge(QueueSettings queue) => Broker(queue).Purge(queue);

    public IReadOnlyList<string> Peek(QueueSettings queue, int most) => Broker(queue).Peek(queue, most);

    public int Count(QueueSettings queue) => Broker(queue).Count(queue);

    private IBroker Broker(QueueSettings queue) => queue.Kind switch
    {
        "rabbitMq" => RabbitBroker.Instance,
        "ibmMq" => IbmBroker.Instance,
        "msmq" => new MsmqBroker(machine),
        _ => throw new ScriptException($"There is no queue kind {queue.Kind}."),
    };
}
