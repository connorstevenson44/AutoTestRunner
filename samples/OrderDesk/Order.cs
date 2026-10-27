namespace OrderDesk;

/// <summary>An order, as it is kept in the Orders table.</summary>
public sealed class Order
{
    public int Number { get; set; }
    public string Customer { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public bool GiftWrap { get; set; }
    public string Delivery { get; set; } = OrderDesk.Delivery.Standard;

    /// <summary>The day it should arrive by, as 2026-11-03, or empty for whenever.</summary>
    public string DeliverBy { get; set; } = "";

    public string Notes { get; set; } = "";

    /// <summary>The total with two decimals, such as 26.00, kept as text so it is exact.</summary>
    public string Total { get; set; } = "";

    /// <summary>New, Shipped, then Picked once the warehouse says it has picked it.</summary>
    public string Status { get; set; } = "New";

    public string Title => $"{Number} {Customer}";

    public string Summary =>
        $"{Quantity} × {Product}{(GiftWrap ? ", gift wrapped" : "")}{(Delivery == OrderDesk.Delivery.Standard ? "" : ", " + Delivery.ToLowerInvariant())}"
        + (DeliverBy.Length > 0 ? $", by {DeliverBy}" : "");

    public string Tip => $"Order {Number}";
}
