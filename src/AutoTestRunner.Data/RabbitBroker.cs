using System.Text;
using RabbitMQ.Client;

namespace AutoTestRunner.Data;

/// <summary>
/// RabbitMQ, through RabbitMQ.Client: a connection to the broker for each step, as steps are few and far between, and
/// the queue asked about first, so a misspelt one is said rather than the message going nowhere.
/// </summary>
internal sealed class RabbitBroker : IBroker
{
    public static readonly RabbitBroker Instance = new();

    public void Send(Scripting.QueueSettings queue, string body) => With(queue, async channel =>
    {
        var properties = new BasicProperties { Persistent = true, ContentType = "text/plain", ContentEncoding = "utf-8" };
        await channel.BasicPublishAsync("", queue.Queue, mandatory: true, properties, Encoding.UTF8.GetBytes(body));
        return 0;
    });

    public string? Receive(Scripting.QueueSettings queue, TimeSpan within) => With(queue, async channel =>
    {
        var end = DateTime.UtcNow + within;
        while (true)
        {
            if (await channel.BasicGetAsync(queue.Queue, autoAck: true) is { } message)
                return Encoding.UTF8.GetString(message.Body.Span);
            if (DateTime.UtcNow > end)
                return null;
            await Task.Delay(250);
        }
    });

    public int Purge(Scripting.QueueSettings queue) => With(queue, async channel => (int)await channel.QueuePurgeAsync(queue.Queue));

    // Got without acknowledging, and put back when the channel closes.
    public IReadOnlyList<string> Peek(Scripting.QueueSettings queue, int most) => With(queue, async channel =>
    {
        var bodies = new List<string>();
        while (bodies.Count < most && await channel.BasicGetAsync(queue.Queue, autoAck: false) is { } message)
            bodies.Add(Encoding.UTF8.GetString(message.Body.Span));
        return (IReadOnlyList<string>)bodies;
    });

    public int Count(Scripting.QueueSettings queue) => With(queue, async channel => (int)(await channel.QueueDeclarePassiveAsync(queue.Queue)).MessageCount);

    private static T With<T>(Scripting.QueueSettings queue, Func<IChannel, Task<T>> work) => Task.Run(async () =>
    {
        var factory = new ConnectionFactory { Uri = new Uri(queue.Connection!), ClientProvidedName = "autotest" };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclarePassiveAsync(queue.Queue);
        return await work(channel);
    }).GetAwaiter().GetResult();
}
