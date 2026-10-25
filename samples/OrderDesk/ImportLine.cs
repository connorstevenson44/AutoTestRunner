using System.IO;

namespace OrderDesk;

/// <summary>A line of a file to import: customer,product,quantity, with an optional note such as draft.</summary>
public sealed record ImportLine(string Customer, string Product, int Quantity, string Note)
{
    public string Summary => $"{Customer}: {Quantity} × {Product}{(Note.Length > 0 ? $" ({Note})" : "")}";

    public static IReadOnlyList<ImportLine> Read(string file) =>
        File.ReadAllLines(file)
            .Skip(1)
            .Where(line => line.Trim().Length > 0)
            .Select(line => line.Split(',').Select(part => part.Trim()).ToArray())
            .Select(parts => new ImportLine(parts[0], parts.ElementAtOrDefault(1) ?? "",
                int.TryParse(parts.ElementAtOrDefault(2), out var quantity) ? quantity : 0, parts.ElementAtOrDefault(3) ?? ""))
            .ToList();
}
