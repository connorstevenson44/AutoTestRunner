namespace OrderDesk;

/// <summary>A product in the catalogue: 500 of them, so the list is longer than the window.</summary>
public sealed record CatalogueItem(string Name, string Description, string Price)
{
    private static readonly string[] Kinds = ["Espresso blend", "Breakfast tea", "Travel mug", "Cast iron teapot", "Filter papers", "Oolong", "Cold brew kit"];

    public static IReadOnlyList<CatalogueItem> All() =>
        Enumerable.Range(1, 500)
            .Select(number => new CatalogueItem($"Product {number:000}", Kinds[number % Kinds.Length],
                Pricing.Money(3m + number % 40 + (number % 100) / 100m)))
            .ToList();
}
