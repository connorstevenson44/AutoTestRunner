using System.Text.Json;
using System.Text.Json.Nodes;

namespace OrderDesk;

/// <summary>What Order Desk and the warehouse say to each other: an order that has shipped, and an order picked.</summary>
public static class WarehouseMessages
{
    public static string Shipped(Order order) => new JsonObject
    {
        ["type"] = "OrderShipped",
        ["number"] = order.Number,
        ["customer"] = order.Customer,
        ["lines"] = new JsonArray(new JsonObject { ["sku"] = order.Sku, ["product"] = order.Product, ["quantity"] = order.Quantity }),
        ["delivery"] = order.Delivery,
        ["deliverBy"] = order.DeliverBy.Length == 0 ? null : order.DeliverBy,
    }.ToJsonString();

    /// <summary>The order number of an OrderPicked message, or null when the message isn't one.</summary>
    public static int? Picked(string body)
    {
        try
        {
            return JsonNode.Parse(body) is JsonObject message && (string?)message["type"] == "OrderPicked" && message["number"] is JsonValue number
                && number.TryGetValue<int>(out var picked) ? picked : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
