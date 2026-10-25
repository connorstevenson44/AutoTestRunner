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
