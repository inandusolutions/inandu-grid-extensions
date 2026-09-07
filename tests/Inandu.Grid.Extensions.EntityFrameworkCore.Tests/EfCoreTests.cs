using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inandu.Grid.Extensions.EntityFrameworkCore.Tests;

public enum ShopStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? Stock { get; set; }
    public bool Discontinued { get; set; }
    public ShopStatus Status { get; set; }
    public DateTime CreatedOn { get; set; }
}

public sealed class ShopContext : DbContext
{
    public ShopContext(DbContextOptions<ShopContext> options) : base(options) { }

    public DbSet<Widget> Widgets => Set<Widget>();
}

/// <summary>One shared in-memory SQLite database, seeded once.</summary>
public sealed class ShopFixture : IDisposable
{
    private readonly DbConnection _connection;

    public ShopFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ShopContext>().UseSqlite(_connection).Options;
        using var db = new ShopContext(options);
        db.Database.EnsureCreated();

        db.Widgets.AddRange(Enumerable.Range(1, 240).Select(i => new Widget
        {
            Id = i,
            Name = $"Widget {i:D4}",
            Category = $"Cat {i % 6}",
            Price = 5m + (i % 100),
            Stock = i % 13 == 0 ? null : i % 400,
            Discontinued = i % 9 == 0,
            Status = (ShopStatus)(i % 3),
            CreatedOn = new DateTime(2026, 1, 1).AddHours(i),
        }));
        db.SaveChanges();

        OptionsBuilder = options;
    }

    public DbContextOptions<ShopContext> OptionsBuilder { get; }

    public ShopContext NewContext() => new(OptionsBuilder);

    public void Dispose() => _connection.Dispose();
}

public class EfCoreTests : IClassFixture<ShopFixture>
{
    private readonly ShopFixture _fx;

    public EfCoreTests(ShopFixture fx) => _fx = fx;

    [Fact]
    public async Task ToInanduGridAsync_pages_sorts_filters_via_sql()
    {
        await using var db = _fx.NewContext();

        // NB: SQLite can't ORDER BY decimal, so sort on a non-decimal column here.
        var result = await db.Widgets.AsNoTracking()
            .ToInanduGridAsync("?page=2&pageSize=10&sort=-id&price_gte=50&category_eq=Cat 1");

        Assert.Equal(2, result.Page);
        Assert.InRange(result.Data.Count, 1, 10);
        Assert.All(result.Data, w => Assert.True(w.Price >= 50m && w.Category == "Cat 1"));
        var ids = result.Data.Select(w => w.Id).ToList();
        Assert.Equal(ids.OrderByDescending(i => i).ToList(), ids);
        Assert.True(result.Total > 10, "the filter should match more than one page");
    }

    [Fact]
    public async Task ToInanduGridAsync_total_reflects_the_filter()
    {
        await using var db = _fx.NewContext();

        var request = new InanduGridRequest { PageSize = 5 };
        request.ColumnFilters["status"] = new InanduGridColumnFilter { Values = new() { "Active" } };

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync(request);

        var expected = await db.Widgets.CountAsync(w => w.Status == ShopStatus.Active);
        Assert.Equal(expected, result.Total);
        Assert.Equal(5, result.Data.Count);
    }

    [Fact]
    public async Task ToInanduGridAsync_projection_only_reads_the_dto_columns()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync(
            w => new { w.Id, w.Name },
            "?pageSize=5&sort=id");

        Assert.Equal(5, result.Data.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.Data.Select(x => x.Id));
    }

    [Fact]
    public async Task ToInanduGridAsync_advanced_filter_tree()
    {
        await using var db = _fx.NewContext();

        var json = """
            {"kind":"group","combinator":"and","children":[
              {"kind":"condition","field":"status","operator":"eq","value":"Active"},
              {"kind":"group","combinator":"or","children":[
                {"kind":"condition","field":"price","operator":"gte","value":90},
                {"kind":"condition","field":"stock","operator":"isEmpty"}
              ]}
            ]}
            """;

        var result = await db.Widgets.AsNoTracking()
            .ToInanduGridAsync("?pageSize=200&filter=" + Uri.EscapeDataString(json));

        Assert.All(result.Data, w =>
        {
            Assert.Equal(ShopStatus.Active, w.Status);
            Assert.True(w.Price >= 90m || w.Stock is null);
        });
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public async Task ToInanduGridGroupedAsync_top_level_then_drill()
    {
        await using var db = _fx.NewContext();

        var top = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=category&pageSize=50");
        Assert.False(top.IsLeaf);
        Assert.Equal(6, top.Total); // Cat 0..5
        Assert.Equal(240, top.Groups!.Sum(g => g.Count));

        var drill = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=category&groupKeys=Cat 1&pageSize=1000");
        Assert.True(drill.IsLeaf);
        Assert.All(drill.Rows!, w => Assert.Equal("Cat 1", w.Category));
        Assert.Equal(40, drill.Total);
    }

    [Fact]
    public async Task ToInanduGridGroupedAsync_orders_groups_by_count_desc()
    {
        await using var db = _fx.NewContext();

        var res = await db.Widgets.AsNoTracking()
            .ToInanduGridGroupedAsync("?groupBy=category&sort=-count&discontinued_eq=false&pageSize=50");

        var counts = res.Groups!.Select(g => g.Count).ToList();
        Assert.Equal(counts.OrderByDescending(c => c).ToList(), counts);
    }
}
