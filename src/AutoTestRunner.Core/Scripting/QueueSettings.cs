namespace AutoTestRunner.Scripting;

/// <summary>
/// A message queue the suite sends to and receives from, from queues in suite.json. Connection is the broker: an
/// amqp:// address for RabbitMQ, host(port) for IBM MQ, and none for MSMQ, whose queue is a path such as
/// .\private$\orders. Every part can have ${variables}.
/// </summary>
public sealed record QueueSettings(string Name, string Kind, string Queue)
{
    public string? Connection { get; init; }

    /// <summary>IBM MQ's server connection channel, such as DEV.APP.SVRCONN.</summary>
    public string? Channel { get; init; }

    /// <summary>IBM MQ's queue manager, such as QM1.</summary>
    public string? QueueManager { get; init; }

    /// <summary>IBM MQ's user and password, when the channel asks for them.</summary>
    public string? User { get; init; }

    public string? Password { get; init; }

    /// <summary>How an MSMQ message's body is written: xml, as .NET's XmlMessageFormatter does by default, or text.</summary>
    public string? Format { get; init; }

    /// <summary>The kinds of queue, by the word suite.json uses, and what a person calls each.</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["rabbitMq"] = "RabbitMQ",
        ["ibmMq"] = "IBM MQ",
        ["msmq"] = "MSMQ",
    };
}
