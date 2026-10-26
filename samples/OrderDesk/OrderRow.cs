using System.ComponentModel;

namespace OrderDesk;

/// <summary>An order's row in the list, which changes in place when the order is shipped.</summary>
public sealed class OrderRow(Order order) : INotifyPropertyChanged
{
    public Order Order { get; } = order;
    public int Number => Order.Number;
    public string Customer => Order.Customer;
    public string Summary => Order.Summary;
    public string Title => Order.Title;
    public string Tip => Order.Tip;
    public bool IsShipped => Order.IsShipped;

    // The row shows what the order costs as the order form showed it.
    public string Money => Product.Named(Order.Product) is { } product
        ? Pricing.Money(Pricing.Total(product, Order.Quantity, Order.GiftWrap, Order.Express))
        : "£" + Order.Total;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
