using System.Globalization;

namespace OrderDesk;

/// <summary>What an order costs, with the fees in settings.json, and money as the window shows it and the database keeps it.</summary>
public static class Pricing
{
    public static ShopSettings Settings { get; set; } = new();

    public static decimal Fee(string delivery) => delivery switch
    {
        Delivery.Express => Settings.ExpressFee,
        Delivery.NextDay => Settings.NextDayFee,
        _ => 0,
    };

    /// <summary>What the order costs: the products, then gift wrap and delivery.</summary>
    public static decimal Total(Product product, int quantity, bool giftWrap, string delivery) =>
        product.Price * quantity + (giftWrap ? Settings.GiftWrapFee : 0) + Fee(delivery);

    // A deliberate bug, for the failure examples to catch, and which the passing suite knows about: what is saved
    // leaves out express delivery, so an express order's saved total is the express fee less than the window shows.
    public static decimal SavedTotal(Product product, int quantity, bool giftWrap, string delivery) =>
        product.Price * quantity + (giftWrap ? Settings.GiftWrapFee : 0) + (delivery == Delivery.Express ? 0 : Fee(delivery));

    public static string Money(decimal amount) => Settings.Currency + Plain(amount);

    public static string Plain(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
