using System;
using System.Collections.Generic;
using System.Linq;

namespace InanduGrid.ServerSide.Tests;

public enum Status
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

public sealed class Category
{
    public string Name { get; set; } = string.Empty;
}

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Sku { get; set; }

    public decimal Price { get; set; }

    public int? Stock { get; set; }

    public bool Discontinued { get; set; }

    public Status Status { get; set; }

    public DateTime CreatedOn { get; set; }

    public Category? Category { get; set; }
}

public static class Sample
{
    public static List<Product> Products() => new()
    {
        new Product { Id = 1, Name = "Alpha Keyboard", Sku = "KB-01", Price = 45.00m, Stock = 120, Discontinued = false, Status = Status.Active, CreatedOn = new DateTime(2026, 1, 5), Category = new Category { Name = "Peripherals" } },
        new Product { Id = 2, Name = "Beta Mouse", Sku = "MO-02", Price = 19.50m, Stock = 300, Discontinued = false, Status = Status.Active, CreatedOn = new DateTime(2026, 1, 8), Category = new Category { Name = "Peripherals" } },
        new Product { Id = 3, Name = "Gamma Monitor", Sku = "MN-03", Price = 289.00m, Stock = 25, Discontinued = false, Status = Status.Active, CreatedOn = new DateTime(2026, 2, 1), Category = new Category { Name = "Displays" } },
        new Product { Id = 4, Name = "Delta Dock", Sku = "DK-04", Price = 129.00m, Stock = 0, Discontinued = true, Status = Status.Archived, CreatedOn = new DateTime(2025, 11, 20), Category = null },
        new Product { Id = 5, Name = "Epsilon Cable", Sku = "CB-05", Price = 8.99m, Stock = 999, Discontinued = false, Status = Status.Draft, CreatedOn = new DateTime(2026, 3, 3), Category = new Category { Name = "Cables" } },
        new Product { Id = 6, Name = "alpha stand", Sku = null, Price = 24.00m, Stock = 40, Discontinued = false, Status = Status.Active, CreatedOn = new DateTime(2026, 2, 14), Category = new Category { Name = "Peripherals" } },
        new Product { Id = 7, Name = "Zeta Webcam", Sku = "WC-07", Price = 59.00m, Stock = null, Discontinued = false, Status = Status.Active, CreatedOn = new DateTime(2026, 1, 30), Category = new Category { Name = "Peripherals" } },
        new Product { Id = 8, Name = "Eta Hub", Sku = "HB-08", Price = 34.50m, Stock = 75, Discontinued = true, Status = Status.Archived, CreatedOn = new DateTime(2025, 12, 12), Category = new Category { Name = "Peripherals" } },
    };

    public static List<Product> Many(int count)
        => Enumerable.Range(1, count)
            .Select(i => new Product
            {
                Id = i,
                Name = $"Product {i:D5}",
                Sku = $"SKU-{i:D5}",
                Price = 10m + (i % 90),
                Stock = i % 500,
                Discontinued = i % 7 == 0,
                Status = (Status)(i % 3),
                CreatedOn = new DateTime(2026, 1, 1).AddMinutes(i),
                Category = new Category { Name = $"Cat {i % 5}" },
            })
            .ToList();
}
