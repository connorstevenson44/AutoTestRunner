using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OrderDesk;

/// <summary>Something on sale, and its price for one.</summary>
public sealed record Product(string Name, decimal Price)
{
    public static readonly IReadOnlyList<Product> All =
    [
        new("Coffee beans", 12.00m),
        new("Green tea", 8.00m),
        new("Mug", 6.00m),
        new("Teapot", 24.00m),
    ];

    public static Product? Named(string? name) => All.FirstOrDefault(product => product.Name == name);
}

/// <summary>An order, as it is kept in orders.json.</summary>
public sealed class Order
{
    public int Number { get; set; }
    public string Customer { get; set; } = "";
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public bool GiftWrap { get; set; }
    public bool Express { get; set; }
    public string Notes { get; set; } = "";

    /// <summary>The total with two decimals, such as 26.00, so a test can compare it as text.</summary>
    public string Total { get; set; } = "";

    public string Status { get; set; } = "New";

    [JsonIgnore]
    public string Title => $"{Number} {Customer}";

    [JsonIgnore]
    public string Summary => $"{Quantity} × {Product}{(GiftWrap ? ", gift wrapped" : "")}{(Express ? ", express" : "")}";

    [JsonIgnore]
    public string Tip => $"Order {Number}";

    [JsonIgnore]
    public bool IsShipped => Status == "Shipped";
}

public static class Pricing
{
    public const decimal GiftWrapFee = 2.00m;
    public const decimal ExpressFee = 5.00m;

    /// <summary>What the order costs: the products, then gift wrap and express delivery.</summary>
    public static decimal Total(Product product, int quantity, bool giftWrap, bool express) =>
        product.Price * quantity + (giftWrap ? GiftWrapFee : 0) + (express ? ExpressFee : 0);

    // A deliberate bug, for the failure demo to catch: what is saved leaves out express delivery, so an express
    // order's saved total is 5.00 less than the total the window shows.
    public static decimal SavedTotal(Product product, int quantity, bool giftWrap, bool express) =>
        product.Price * quantity + (giftWrap ? GiftWrapFee : 0);

    public static string Money(decimal amount) => "£" + amount.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Plain(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}

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

    private OrderFile _file = new();

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

/// <summary>A line of a file to import: customer,product,quantity, with an optional note such as draft.</summary>
public sealed record ImportLine(string Customer, string Product, int Quantity, string Note)
{
    public string Summary => $"{Customer}: {Quantity} × {Product}{(Note.Length > 0 ? $" ({Note})" : "")}";

    public static IReadOnlyList<ImportLine> Read(string file) =>
        File.ReadAllLines(file)
            .Skip(1)
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Split(',').Select(part => part.Trim()).ToArray())
            .Select(parts => new ImportLine(parts[0], parts.ElementAtOrDefault(1) ?? "",
                int.TryParse(parts.ElementAtOrDefault(2), out var quantity) ? quantity : 0, parts.ElementAtOrDefault(3) ?? ""))
            .ToList();
}
