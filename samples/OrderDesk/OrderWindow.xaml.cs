using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace OrderDesk;

/// <summary>An order's details, to change its quantity and note: the total follows the quantity as it is typed.</summary>
public partial class OrderWindow : Window
{
    private readonly Order _order;

    public OrderWindow(Order order)
    {
        InitializeComponent();
        _order = order;
        Title = $"Order {order.Number}";
        CustomerText.Text = order.Customer;
        ProductText.Text = $"{order.Sku} {order.Product}, {order.Delivery.ToLowerInvariant()} delivery";
        DetailQuantityBox.Text = order.Quantity.ToString(CultureInfo.InvariantCulture);
        DetailNotesBox.Text = order.Notes;
    }

    public int Quantity { get; private set; }

    public string Notes => DetailNotesBox.Text;

    private void OnChanged(object sender, TextChangedEventArgs e)
    {
        var good = int.TryParse(DetailQuantityBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) && quantity is >= 1 and <= 99;
        DetailProblem.Text = good ? "" : "The quantity must be a whole number from 1 to 99.";
        SaveButton.IsEnabled = good;
        if (good && Product.Named(_order.Product) is { } product)
        {
            Quantity = quantity;
            DetailTotal.Text = $"Total: {Pricing.Money(Pricing.Total(product, quantity, _order.GiftWrap, _order.Delivery))}";
        }
        else
            DetailTotal.Text = "";
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;
}
