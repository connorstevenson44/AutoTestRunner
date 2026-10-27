using System.Globalization;
using System.Windows;

namespace OrderDesk;

/// <summary>The order form: what it asks for, what is wrong with it, its total, and Add order.</summary>
public partial class MainWindow
{
    private string DeliveryPicked => ExpressBox.IsChecked == true ? Delivery.Express : NextDayBox.IsChecked == true ? Delivery.NextDay : Delivery.Standard;

    // What is wrong with the form, or null when it can be added.
    private string? Problem(out Product? product, out int quantity)
    {
        product = Product.Named(ProductBox.SelectedItem as string);
        quantity = 0;
        if (CustomerBox.Text.Trim().Length == 0)
            return "Type the customer's name.";
        if (product is null)
            return "Pick a product.";
        if (!int.TryParse(QuantityBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out quantity) || quantity is < 1 or > 99)
            return "The quantity must be a whole number from 1 to 99.";
        if (DeliverByBox.SelectedDate is { } date && date.Date < DateTime.Today)
            return "Deliver by can't be before today.";
        return null;
    }

    private void OnFormChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || AddButton is null)
            return;
        var problem = Problem(out var product, out var quantity);
        PriceText.Text = Product.Named(ProductBox.SelectedItem as string) is { } picked ? $"{picked.Sku} · {Pricing.Money(picked.Price)} each" : "";
        ProblemText.Text = problem ?? "";
        TotalText.Text = problem is null ? $"Total: {Pricing.Money(Pricing.Total(product!, quantity, GiftWrapBox.IsChecked == true, DeliveryPicked))}" : "";
        AddButton.IsEnabled = problem is null;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (Problem(out var product, out var quantity) is { } problem)
        {
            Status(problem);
            return;
        }
        var (giftWrap, delivery) = (GiftWrapBox.IsChecked == true, DeliveryPicked);
        var deliverBy = DeliverByBox.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        var order = _store.Add(CustomerBox.Text.Trim(), product!, quantity, giftWrap, delivery, deliverBy, NotesBox.Text);
        _rows.Add(new OrderRow(order));
        Status($"Added order {order.Number} for {order.Customer}: {Pricing.Money(Pricing.Total(product!, quantity, giftWrap, delivery))}.");
        ClearForm();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        ClearForm();
        Status("Cleared the form.");
    }

    // Leaves the product picked, as the next order is often for the same, unless it is a new order.
    private void ClearForm(bool newOrder = false)
    {
        if (newOrder)
            ProductBox.SelectedIndex = -1;
        CustomerBox.Text = "";
        QuantityBox.Text = "1";
        GiftWrapBox.IsChecked = false;
        StandardBox.IsChecked = true;
        DeliverByBox.SelectedDate = null;
        NotesBox.Text = "";
        CustomerBox.Focus();
    }
}
