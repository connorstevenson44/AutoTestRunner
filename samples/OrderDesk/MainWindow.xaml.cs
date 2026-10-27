using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrderDesk;

/// <summary>
/// The order form on the left; the orders, customers, catalogue and activity on the right; and the bar at the bottom
/// saying what the app is doing, which is what the tests wait on. Each part is in a file of its own: MainWindow.Form,
/// .Orders, .Files, .Catalogue, .Activity and .Warehouse.
/// </summary>
public partial class MainWindow : Window
{
    private readonly OrderStore _store;
    private readonly Connections _connections;
    private readonly ObservableCollection<OrderRow> _rows = [];

    public MainWindow(OrderStore store, Connections connections, string signedIn)
    {
        InitializeComponent();
        (_store, _connections) = (store, connections);
        SignedInText.Text = $"{connections.ShopName} · {signedIn}";
        foreach (var product in Product.OnSale)
            ProductBox.Items.Add(product.Name);
        GiftWrapBox.Content = $"Gift wrap (+{Pricing.Money(Pricing.Settings.GiftWrapFee)})";
        ExpressBox.Content = $"Express (+{Pricing.Money(Pricing.Settings.ExpressFee)})";
        NextDayBox.Content = $"Next day (+{Pricing.Money(Pricing.Settings.NextDayFee)})";
        OrdersList.ItemsSource = _rows;
        _rows.CollectionChanged += (_, _) => ShowEmpty();
        Reload();
        ShowCategories();
        OnFormChanged(this, new RoutedEventArgs());
        Closed += async (_, _) => await Disconnect();
        Status($"Ready. Signed in as {signedIn}. {Count(_rows.Count)} in {_store.Folder}.");
    }

    private void Status(string text) => StatusText.Text = text;

    private void ShowEmpty() => EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string Count(int orders) => orders == 1 ? "1 order" : $"{orders} orders";

    // Reads the orders and customers again, for a change made by someone else, such as a restore or the database itself.
    private void Reload()
    {
        _rows.Clear();
        foreach (var order in _store.Orders())
            _rows.Add(new OrderRow(order));
        CustomersGrid.ItemsSource = _store.Customers();
    }

    // Ctrl+N starts a new order, and F5 reads everything again.
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ClearForm();
            Status("New order.");
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Reload();
            ShowActivity();
            Status($"Refreshed: {Count(_rows.Count)}, {CustomersGrid.Items.Count} customers.");
            e.Handled = true;
        }
    }

    private void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs)
            return;
        if (Tabs.SelectedIndex == 1)
            CustomersGrid.ItemsSource = _store.Customers();
        else if (Tabs.SelectedIndex == 3)
        {
            _page = 0;
            ShowActivity();
        }
    }

    private void OnAbout(object sender, RoutedEventArgs e) => new AboutWindow(_connections.ShopName) { Owner = this }.ShowDialog();

    private void OnExit(object sender, RoutedEventArgs e) => Close();
}
