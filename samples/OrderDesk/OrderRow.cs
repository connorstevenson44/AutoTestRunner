using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace OrderDesk;

/// <summary>An order's row in the list, which changes in place when the order is shipped, picked or changed.</summary>
public sealed class OrderRow(Order order) : INotifyPropertyChanged
{
    private static readonly Brush ShippedBack = Frozen("#E6F4EA"), ShippedInk = Frozen("#1E7B34"), PickedBack = Frozen("#E8F0F8"), PickedInk = Frozen("#1F4E79");

    public Order Order { get; } = order;
    public int Number => Order.Number;
    public string Customer => Order.Customer;
    public string Summary => Order.Summary;
    public string Title => Order.Title;
    public string Tip => Order.Tip;

    // Shipped, or Picked once the warehouse has picked it: a label on the row, with the order's number in its tip.
    public string Label => Order.Status;
    public string LabelTip => $"Order {Number} {Order.Status.ToLowerInvariant()}";
    public Visibility LabelShown => Order.Status == "New" ? Visibility.Collapsed : Visibility.Visible;
    public Brush LabelBack => Order.Status == "Picked" ? PickedBack : ShippedBack;
    public Brush LabelInk => Order.Status == "Picked" ? PickedInk : ShippedInk;

    // The row shows what the order costs as the order form showed it.
    public string Money => Product.Named(Order.Product) is { } product
        ? Pricing.Money(Pricing.Total(product, Order.Quantity, Order.GiftWrap, Order.Delivery))
        : Pricing.Settings.Currency + Order.Total;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private static SolidColorBrush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
