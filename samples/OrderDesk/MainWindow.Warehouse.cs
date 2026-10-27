using RabbitMQ.Client.Exceptions;

namespace OrderDesk;

/// <summary>
/// The warehouse: Connect to warehouse tries its broker every few seconds until it answers; once connected, a shipped
/// order is sent to it, and each order it picks is marked Picked.
/// </summary>
public partial class MainWindow
{
    private WarehouseLink? _warehouse;
    private bool _connecting;

    private async void OnConnect(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_warehouse is not null)
        {
            Status($"Already connected to the warehouse at {_warehouse.Host}.");
            return;
        }
        if (_connecting || _connections.Warehouse is not { Length: > 0 } address)
            return;
        _connecting = true;
        Status("Connecting to the warehouse…");
        for (var tries = 1; _warehouse is null; tries++)
        {
            try
            {
                _warehouse = await WarehouseLink.ConnectAsync(address, _connections.OrdersQueue, _connections.PicksQueue,
                    body => Dispatcher.InvokeAsync(() => Picked(body)).Task);
            }
            catch (BrokerUnreachableException)
            {
                await Task.Delay(3000);
                if (!IsLoaded)
                    return;
                Status($"Connecting to the warehouse… (try {tries + 1}) It isn't answering.");
            }
        }
        _connecting = false;
        _store.Log($"Connected to the warehouse at {_warehouse.Host}");
        Status($"Connected to the warehouse at {_warehouse.Host}.");
    }

    private void Picked(string body)
    {
        if (WarehouseMessages.Picked(body) is not { } number)
        {
            _store.Log($"Ignored a message from the warehouse: {(body.Length > 80 ? body[..80] : body)}");
            Status("Ignored a message from the warehouse: it isn't a pick.");
            return;
        }
        if (_rows.FirstOrDefault(row => row.Number == number) is not { } picked)
        {
            _store.Log($"The warehouse picked order {number}, which isn't here");
            Status($"The warehouse picked order {number}, which isn't here.");
            return;
        }
        _store.SetStatus(picked.Order, "Picked");
        picked.Changed();
        Status($"The warehouse picked order {number}.");
    }

    private async Task Disconnect()
    {
        if (_warehouse is { } link)
        {
            _warehouse = null;
            await link.DisposeAsync();
        }
    }
}
