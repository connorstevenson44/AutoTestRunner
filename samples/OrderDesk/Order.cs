using System.Text.Json.Serialization;

namespace OrderDesk;

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
