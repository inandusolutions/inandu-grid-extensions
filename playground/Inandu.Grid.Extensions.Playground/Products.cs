namespace Inandu.Grid.Extensions.Playground;

public enum ProductStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

/// <summary>Slim projection returned by <c>/api/products/summary</c>.</summary>
public sealed record ProductSummary(int Id, string Name, decimal Price, ProductStatus Status);

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public int Stock { get; set; }

    public bool Discontinued { get; set; }

    public ProductStatus Status { get; set; }

    public DateTime CreatedOn { get; set; }
}

/// <summary>An in-memory catalogue so the playground has something big to page through.</summary>
public static class ProductStore
{
    private static readonly string[] Adjectives =
        { "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa" };

    private static readonly string[] Nouns =
        { "Keyboard", "Mouse", "Monitor", "Dock", "Cable", "Webcam", "Hub", "Stand", "Adapter", "Charger" };

    private static readonly string[] Categories =
        { "Peripherals", "Displays", "Cables", "Audio", "Networking", "Power" };

    public static IReadOnlyList<Product> All { get; } = Build(10_000);

    private static List<Product> Build(int count)
    {
        var random = new Random(20260907);
        var start = new DateTime(2025, 1, 1);
        var list = new List<Product>(count);

        for (var i = 1; i <= count; i++)
        {
            var name = $"{Adjectives[i % Adjectives.Length]} {Nouns[(i / 3) % Nouns.Length]} {i}";
            list.Add(new Product
            {
                Id = i,
                Name = name,
                Sku = $"SKU-{i:D5}",
                Category = Categories[i % Categories.Length],
                Price = Math.Round(5m + (decimal)(random.NextDouble() * 495), 2),
                Stock = random.Next(0, 1000),
                Discontinued = i % 11 == 0,
                Status = (ProductStatus)(i % 3),
                CreatedOn = start.AddHours(random.Next(0, 24 * 600)),
            });
        }

        return list;
    }
}
