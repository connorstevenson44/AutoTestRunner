using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OrderDesk;

/// <summary>
/// The order form on the left, the orders and the catalogue on the right, and the bar at the bottom saying what the
/// app is doing, which is what the tests wait on.
/// </summary>
public partial class MainWindow : Window
{
    private readonly OrderStore _store;
    private readonly ObservableCollection<OrderRow> _rows = [];
    private DispatcherTimer? _connecting;

    public MainWindow()
    {
        InitializeComponent();
        _store = new OrderStore(DataFolder(Environment.GetCommandLineArgs()));
        foreach (var product in Product.All)
            ProductBox.Items.Add(product.Name);
        foreach (var order in _store.Orders)
            _rows.Add(new OrderRow(order));
        OrdersList.ItemsSource = _rows;
        _rows.CollectionChanged += (_, _) => ShowEmpty();
        ShowEmpty();
        CatalogueList.ItemsSource = CatalogueItem.All();
        OnFormChanged(this, new RoutedEventArgs());
        Status($"Ready. {Count(_rows.Count)} in {_store.Folder}.");
    }

    // --data <folder>, or Order Desk in the local app data folder, which the demo suites look in.
    private static string DataFolder(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        if (index >= 0 && index + 1 < args.Length)
            return Path.GetFullPath(args[index + 1]);
        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } given
            ? given
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "OrderDesk");
    }

    private void Status(string text) => StatusText.Text = text;

    private void ShowEmpty() => EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string Count(int orders) => orders == 1 ? "1 order" : $"{orders} orders";

    // ===== The order form =====

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
        return null;
    }

    private void OnFormChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || AddButton is null)
            return;
        var problem = Problem(out var product, out var quantity);
        PriceText.Text = product is null ? "" : $"{Pricing.Money(product.Price)} each";
        ProblemText.Text = problem ?? "";
        TotalText.Text = problem is null
            ? $"Total: {Pricing.Money(Pricing.Total(product!, quantity, GiftWrapBox.IsChecked == true, ExpressBox.IsChecked == true))}"
            : "";
        AddButton.IsEnabled = problem is null;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (Problem(out var product, out var quantity) is { } problem)
        {
            Status(problem);
            return;
        }
        var giftWrap = GiftWrapBox.IsChecked == true;
        var express = ExpressBox.IsChecked == true;
        var order = _store.Add(CustomerBox.Text.Trim(), product!, quantity, giftWrap, express, NotesBox.Text);
        _rows.Add(new OrderRow(order));
        Status($"Added order {order.Number} for {order.Customer}: {Pricing.Money(Pricing.Total(product!, quantity, giftWrap, express))}.");
        ClearForm();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        ClearForm();
        Status("Cleared the form.");
    }

    private void ClearForm()
    {
        CustomerBox.Text = "";
        QuantityBox.Text = "1";
        GiftWrapBox.IsChecked = false;
        ExpressBox.IsChecked = false;
        NotesBox.Text = "";
        CustomerBox.Focus();
    }

    // ===== The buttons on an order's row =====

    private void OnShip(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OrderRow row)
            return;
        _store.Ship(row.Order);
        row.Changed();
        Status($"Order {row.Number} shipped.");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OrderRow row)
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

    // ===== The bar at the top =====

    // Writes a line at a time, slowly, as a real export to a slow disk or server would, saying how far it has got.
    private async void OnExport(object sender, RoutedEventArgs e)
    {
        var orders = _store.Orders.ToList();
        if (orders.Count == 0)
        {
            Status("There are no orders to export.");
            return;
        }
        ExportButton.IsEnabled = false;
        try
        {
            await using var writer = new StreamWriter(_store.ExportFile, append: false);
            await writer.WriteLineAsync(OrderStore.CsvHeader);
            for (var index = 0; index < orders.Count; index++)
            {
                Status($"Exporting order {index + 1} of {orders.Count}…");
                await Task.Delay(600);
                await writer.WriteLineAsync(OrderStore.CsvLine(orders[index]));
            }
        }
        finally
        {
            ExportButton.IsEnabled = CheckedBox.IsChecked == true;
        }
        _store.Log($"Exported {Count(orders.Count)} to {_store.ExportFile}");
        Status($"Exported {Count(orders.Count)}. File: {_store.ExportFile}");
    }

    // Asks about each order in the file in a window of its own, as some imports do: Import or Skip, one at a time.
    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Title = "Import orders", Filter = "Order files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != true)
        {
            Status("Import cancelled.");
            return;
        }
        var lines = ImportLine.Read(picker.FileName);
        int imported = 0, skipped = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            Status($"Importing order {index + 1} of {lines.Count}…");
            // A moment between windows, as there is when an app does some work for each.
            await Task.Delay(400);
            var line = lines[index];
            var window = new ImportWindow(line, index + 1, lines.Count) { Owner = this };
            if (window.ShowDialog() == true && Product.Named(line.Product) is { } product && line.Quantity > 0)
            {
                _rows.Add(new OrderRow(_store.Add(line.Customer, product, line.Quantity, false, false, line.Note)));
                imported++;
            }
            else
                skipped++;
        }
        _store.Log($"Imported {imported}, skipped {skipped} from {picker.FileName}");
        Status($"Import finished: {imported} imported, {skipped} skipped.");
    }

    // The warehouse never answers: a demo of a test that waits for something that doesn't happen.
    private void OnConnect(object sender, RoutedEventArgs e)
    {
        _connecting?.Stop();
        var tries = 1;
        Status("Connecting to the warehouse…");
        _connecting = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _connecting.Tick += (_, _) => Status($"Connecting to the warehouse… (try {++tries}) It isn't answering.");
        _connecting.Start();
    }
}

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

/// <summary>A product in the catalogue: 500 of them, so the list is longer than the window.</summary>
public sealed record CatalogueItem(string Name, string Description, string Price)
{
    private static readonly string[] Kinds = ["Espresso blend", "Breakfast tea", "Travel mug", "Cast iron teapot", "Filter papers", "Oolong", "Cold brew kit"];

    public static IReadOnlyList<CatalogueItem> All() =>
        Enumerable.Range(1, 500)
            .Select(number => new CatalogueItem($"Product {number:000}", Kinds[number % Kinds.Length],
                Pricing.Money(3m + number % 40 + (number % 100) / 100m)))
            .ToList();
}
