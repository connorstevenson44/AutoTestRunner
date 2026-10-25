using System.Globalization;

namespace OrderDesk;

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
