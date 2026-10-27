using AutoTestRunner.Running;
using AutoTestRunner.Scripting;

namespace AutoTestRunner.Data.Tests;

/// <summary>
/// Each kind of queue against a real broker: emptied, sent to, looked at without taking, and received from in order.
/// They run where the broker is in the environment, as CI sets it: ATR_RABBITMQ, ATR_IBMMQ, and ATR_MSMQ on Windows.
/// </summary>
public sealed class QueueTests
{
    public static TheoryData<string> Kinds() => ["rabbitMq", "ibmMq", "msmq"];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Messages_go_round_a_queue_in_order(string kind)
    {
        var queue = Queue(kind, "autotest.round");
        var connections = new Connections(new WindowsMachine());
        var text = $"{{\"id\":1001,\"note\":\"Ümlaut & <angle> brackets\"}}\nline two {Guid.NewGuid():N}";

        connections.Purge(queue);
        Assert.Equal(0, connections.Count(queue));
        connections.Send(queue, text);
        connections.Send(queue, "second");
        Assert.Equal(2, connections.Count(queue));
        Assert.Equal([text, "second"], connections.Peek(queue, 10));
        // Looking leaves them there.
        Assert.Equal(2, connections.Count(queue));

        Assert.Equal(text, connections.Receive(queue, TimeSpan.FromSeconds(5)));
        Assert.Equal("second", connections.Receive(queue, TimeSpan.FromSeconds(5)));
        Assert.Null(connections.Receive(queue, TimeSpan.FromSeconds(1)));

        connections.Send(queue, "left over");
        Assert.Equal(1, connections.Purge(queue));
        Assert.Equal(0, connections.Count(queue));
    }

    // A queue from the environment, or a skip when the broker isn't there. Tests that run at once use different queues.
    internal static QueueSettings Queue(string kind, string name)
    {
        var variable = kind switch { "rabbitMq" => "ATR_RABBITMQ", "ibmMq" => "ATR_IBMMQ", _ => "ATR_MSMQ" };
        var setting = Environment.GetEnvironmentVariable(variable);
        Assert.SkipWhen(string.IsNullOrEmpty(setting), $"{variable} isn't set, so there is no {QueueSettings.Kinds[kind]} to try.");
        return kind switch
        {
            // amqp://guest:guest@localhost:5672/ and a queue the test declares.
            "rabbitMq" => new QueueSettings("orders", kind, Rabbit.Declare(setting!, name)) { Connection = setting },
            // host(port), with the developer image's queue manager, channel, queue and app user.
            "ibmMq" => new QueueSettings("orders", kind, "DEV.QUEUE.1")
            {
                Connection = setting, Channel = "DEV.APP.SVRCONN", QueueManager = "QM1", User = "app",
                Password = Environment.GetEnvironmentVariable("ATR_IBMMQ_PASSWORD") ?? "passw0rd",
            },
            // A private queue's path, made if need be.
            _ => new QueueSettings("orders", kind, Msmq.Make(setting!.TrimEnd('\\') + "." + name)),
        };
    }
}
