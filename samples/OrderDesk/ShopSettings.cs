using System.IO;
using System.Text.Json;

namespace OrderDesk;

/// <summary>
/// The shop's prices that aren't a product's, from settings.json in the data folder, written with these when it isn't
/// there: the currency sign, and what gift wrap and each kind of delivery add to an order.
/// </summary>
public sealed class ShopSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string Currency { get; set; } = "£";
    public decimal GiftWrapFee { get; set; } = 2.00m;
    public decimal ExpressFee { get; set; } = 5.00m;
    public decimal NextDayFee { get; set; } = 9.00m;

    public static ShopSettings Load(string file)
    {
        if (File.Exists(file))
            return JsonSerializer.Deserialize<ShopSettings>(File.ReadAllText(file), Json) ?? new ShopSettings();
        var settings = new ShopSettings();
        File.WriteAllText(file, JsonSerializer.Serialize(settings, Json));
        return settings;
    }
}
