using System.IO;
using System.Xml.Linq;

namespace OrderDesk;

/// <summary>
/// What OrderDesk.dll.config beside the exe says: the connection string for the orders database, and the warehouse's
/// broker and queues. It is read as XML, as .NET doesn't read a .config by itself any more.
/// </summary>
public sealed class Connections
{
    private readonly XElement? _root;

    public Connections(string file)
    {
        File = file;
        if (System.IO.File.Exists(file))
            _root = XDocument.Load(file).Root;
    }

    public string File { get; }

    public string Orders => ConnectionString("Orders") ?? "Data Source={DataFolder}\\orderdesk.db;Pooling=False";

    public string? Warehouse => Setting("WarehouseBroker");

    public string OrdersQueue => Setting("WarehouseOrdersQueue") ?? "warehouse.orders";

    public string PicksQueue => Setting("WarehousePicksQueue") ?? "orderdesk.picks";

    public string ShopName => Setting("ShopName") ?? "Order Desk";

    private string? ConnectionString(string name) => Add("connectionStrings", "name", name)?.Attribute("connectionString")?.Value;

    private string? Setting(string key) => Add("appSettings", "key", key)?.Attribute("value")?.Value;

    private XElement? Add(string section, string attribute, string name) =>
        _root?.Element(section)?.Elements("add").FirstOrDefault(add => (string?)add.Attribute(attribute) == name);

    public static Connections Beside(string exeFolder) => new(Path.Combine(exeFolder, "OrderDesk.dll.config"));
}
