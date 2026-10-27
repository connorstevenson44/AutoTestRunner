using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderDesk;

/// <summary>
/// The link to the warehouse, over its RabbitMQ broker: shipped orders go on the orders queue, and the warehouse says
/// what it has picked on the picks queue. Both are declared on connecting, so either side can start first.
/// </summary>
public sealed class WarehouseLink : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _ordersQueue;

    private WarehouseLink(IConnection connection, IChannel channel, string ordersQueue) =>
        (_connection, _channel, _ordersQueue) = (connection, channel, ordersQueue);

    public string Host => _connection.Endpoint.HostName;

    /// <summary>Connects, or throws when the broker doesn't answer. Each message on the picks queue goes to received.</summary>
    public static async Task<WarehouseLink> ConnectAsync(string address, string ordersQueue, string picksQueue, Func<string, Task> received)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(address),
            ClientProvidedName = "Order Desk",
            RequestedConnectionTimeout = TimeSpan.FromSeconds(2),
        };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(ordersQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueDeclareAsync(picksQueue, durable: true, exclusive: false, autoDelete: false);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, message) =>
        {
            await received(Encoding.UTF8.GetString(message.Body.Span));
            await channel.BasicAckAsync(message.DeliveryTag, multiple: false);
        };
        await channel.BasicConsumeAsync(picksQueue, autoAck: false, consumer);
        return new WarehouseLink(connection, channel, ordersQueue);
    }

    public async Task SendAsync(string body)
    {
        var properties = new BasicProperties { Persistent = true, ContentType = "application/json", ContentEncoding = "utf-8" };
        await _channel.BasicPublishAsync("", _ordersQueue, mandatory: true, properties, Encoding.UTF8.GetBytes(body));
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
