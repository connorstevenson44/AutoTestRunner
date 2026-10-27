using System.Windows;

namespace OrderDesk;

/// <summary>An order's row: Ship and Delete when the mouse is over it, its menu, and its details on a double-click.</summary>
public partial class MainWindow
{
    private static OrderRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as OrderRow;

    private async void OnShip(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;
        _store.SetStatus(row.Order, "Shipped");
        row.Changed();
        if (_warehouse is null)
        {
            Status($"Order {row.Number} shipped.");
            return;
        }
        await _warehouse.SendAsync(WarehouseMessages.Shipped(row.Order));
        _store.Log($"Sent order {row.Number} to the warehouse");
        Status($"Order {row.Number} shipped, and sent to the warehouse.");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;
        var answer = MessageBox.Show(this, $"Delete order {row.Number} for {row.Customer}? This can't be undone.", "Delete order",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            Status($"Kept order {row.Number}.");
            return;
        }
        _store.Delete(row.Order);
        _rows.Remove(row);
        Status($"Deleted order {row.Number}.");
    }

    // A double-click on the row, or Open order… on its menu.
    private void OnOpenOrder(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;
        e.Handled = true;
        var window = new OrderWindow(row.Order) { Owner = this };
        if (window.ShowDialog() != true)
        {
            Status($"Left order {row.Number} as it was.");
            return;
        }
        _store.Change(row.Order, window.Quantity, window.Notes);
        row.Changed();
        Status($"Saved order {row.Number}: {row.Summary}, {row.Money}.");
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender)?.Order is not { } order || Product.Named(order.Product) is not { } product)
            return;
        var copy = _store.Add(order.Customer, product, order.Quantity, order.GiftWrap, order.Delivery, "", order.Notes);
        _rows.Add(new OrderRow(copy));
        Status($"Duplicated order {order.Number} as {copy.Number}.");
    }

    private void OnCopyNumber(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row)
            return;
        Clipboard.SetText(row.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Status($"Copied {row.Number}.");
    }
}
