using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OrderDesk;

/// <summary>
/// The orders, kept in orders.json in the data folder, with every change written to activity.log there too, so a
/// test can check both.
/// </summary>
public sealed class OrderStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly OrderFile _file = new();

    public OrderStore(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(folder);
        if (File.Exists(OrdersFile))
            _file = JsonSerializer.Deserialize<OrderFile>(File.ReadAllText(OrdersFile), Json) ?? new OrderFile();
    }

    public string Folder { get; }

    public string OrdersFile => Path.Combine(Folder, "orders.json");

    public string LogFile => Path.Combine(Folder, "activity.log");

    public string ExportFile => Path.Combine(Folder, "orders.csv");

    public IEnumerable<Order> Orders => _file.Orders.Values.OrderBy(order => order.Number);

    public Order Add(string customer, Product product, int quantity, bool giftWrap, bool express, string notes)
    {
        var order = new Order
        {
            Number = _file.NextNumber++,
            Customer = customer,
            Product = product.Name,
            Quantity = quantity,
            GiftWrap = giftWrap,
            Express = express,
            Notes = notes,
            Total = Pricing.Plain(Pricing.SavedTotal(product, quantity, giftWrap, express)),
        };
        _file.Orders[order.Number.ToString(CultureInfo.InvariantCulture)] = order;
        Save();
        Log($"Added order {order.Number}: {customer}, {quantity} x {product.Name}, {order.Total}");
        return order;
    }

    public void Ship(Order order)
    {
        order.Status = "Shipped";
        Save();
        Log($"Shipped order {order.Number}");
    }

    public void Delete(Order order)
    {
        _file.Orders.Remove(order.Number.ToString(CultureInfo.InvariantCulture));
        Save();
        Log($"Deleted order {order.Number}");
    }

    /// <summary>Writes one line of the export, so it can go a line at a time while the window shows progress.</summary>
    public static string CsvHeader => "number,customer,product,quantity,giftWrap,express,total,status";

    public static string CsvLine(Order order) =>
        string.Join(",", order.Number, Quote(order.Customer), Quote(order.Product), order.Quantity, order.GiftWrap, order.Express, order.Total, order.Status);

    private static string Quote(string text) => text.Contains(',') || text.Contains('"') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;

    public void Log(string line) =>
        File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}", Encoding.UTF8);

    private void Save() => File.WriteAllText(OrdersFile, JsonSerializer.Serialize(_file, Json), Encoding.UTF8);

    private sealed class OrderFile
    {
        public int NextNumber { get; set; } = 1001;
        public SortedDictionary<string, Order> Orders { get; set; } = [];
    }
}
