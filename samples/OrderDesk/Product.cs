namespace OrderDesk;

/// <summary>
/// Something the shop sells: its stock code, its name, where it sits in the catalogue, such as Tea/Green, and its price
/// for one. Spare parts are in the catalogue to look up, and aren't taken on the order form.
/// </summary>
public sealed record Product(string Sku, string Name, string Category, decimal Price)
{
    public static readonly IReadOnlyList<Product> All =
    [
        new("CB-001", "Coffee beans", "Coffee/Whole bean", 12.00m),
        new("CB-002", "Ethiopian Yirgacheffe 250g", "Coffee/Whole bean", 9.50m),
        new("CB-003", "Colombian Supremo 1kg", "Coffee/Whole bean", 28.00m),
        new("CG-001", "House blend ground 250g", "Coffee/Ground", 7.25m),
        new("CG-002", "Decaf ground 250g", "Coffee/Ground", 7.75m),
        new("TG-001", "Green tea", "Tea/Green", 8.00m),
        new("TG-002", "Sencha 100g", "Tea/Green", 6.40m),
        new("TB-001", "English breakfast 80 bags", "Tea/Black", 4.95m),
        new("TB-002", "Earl Grey loose 125g", "Tea/Black", 5.60m),
        new("TH-001", "Peppermint 20 bags", "Tea/Herbal", 2.95m),
        new("BK-001", "Teapot", "Brewing kit", 24.00m),
        new("BK-002", "Hand grinder", "Brewing kit", 35.00m),
        new("BK-003", "Cafetière 8 cup", "Brewing kit", 22.50m),
        new("BK-004", "Pour-over kettle", "Brewing kit", 42.00m),
        new("TW-001", "Mug", "Tableware", 6.00m),
        new("TW-002", "Espresso cups, set of 4", "Tableware", 14.00m),
        new("TW-003", "Milk jug", "Tableware", 9.00m),
        .. SpareParts(),
    ];

    /// <summary>What the order form offers: everything but the spare parts.</summary>
    public static IEnumerable<Product> OnSale => All.Where(product => !product.IsSparePart);

    public bool IsSparePart => Category.StartsWith("Spare parts", StringComparison.Ordinal);

    public static Product? Named(string? name) => All.FirstOrDefault(product => product.Name == name);

    // 420 spare parts for the grinders and espresso machines the shop services: more than a window shows, as a real
    // parts list is, so the list only builds the rows in view.
    private static IEnumerable<Product> SpareParts()
    {
        string[] grinder = ["Burr set", "Hopper lid", "Adjustment collar", "Drive belt", "Dosing chute"];
        string[] machine = ["Group head gasket 58mm", "Shower screen", "Steam wand tip", "Portafilter basket", "Pump seal", "Drip tray grid"];
        for (var number = 1; number <= 420; number++)
        {
            var (category, kinds) = number <= 120 ? ("Spare parts/Grinders", grinder) : ("Spare parts/Espresso machines", machine);
            yield return new Product($"SP-{number:0000}", $"SP-{number:0000} {kinds[number % kinds.Length]}", category,
                3m + number % 37 + number % 100 / 100m);
        }
    }
}
