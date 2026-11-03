using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inandu.Grid.Extensions.EntityFrameworkCore.Tests;

/// <summary>The column allow-list on the EF Core (SQL) grouping, distinct and aggregate paths.</summary>
public class EfAllowListTests : IClassFixture<ShopFixture>
{
    private readonly ShopFixture _fx;

    public EfAllowListTests(ShopFixture fx) => _fx = fx;

    [Fact]
    public async Task Distinct_on_a_locked_column_is_refused_in_strict_mode()
    {
        await using var db = _fx.NewContext();

        await Assert.ThrowsAsync<InanduGridRequestException>(() => db.Widgets.AsNoTracking().ToInanduGridDistinctAsync(
            "category", "?pageSize=50", o => { o.Column("category").NotFilterable(); o.ThrowOnUnknownField = true; }));
    }

    [Fact]
    public async Task Distinct_on_an_unknown_field_returns_nothing()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridDistinctAsync("doesNotExist", "?pageSize=50");

        Assert.Empty(result.Values);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task Grouped_on_a_locked_column_is_refused_in_strict_mode()
    {
        await using var db = _fx.NewContext();

        await Assert.ThrowsAsync<InanduGridRequestException>(() => db.Widgets.AsNoTracking().ToInanduGridGroupedAsync(
            "?groupBy=category", o => { o.Column("category").NotFilterable(); o.ThrowOnUnknownField = true; }));
    }

    [Fact]
    public async Task Dropping_a_locked_level_keeps_levels_and_keys_aligned()
    {
        await using var db = _fx.NewContext();

        // category is locked: groupBy=category,status + groupKeys=Cat 1 becomes a top-level groupBy=status.
        var result = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync(
            "?groupBy=category,status&groupKeys=Cat 1&pageSize=50", o => o.Column("category").NotFilterable());

        Assert.False(result.IsLeaf);
        Assert.Equal(0, result.Level);
        Assert.Equal(3, result.Total); // Draft / Active / Archived
        Assert.Equal(240, result.Groups!.Sum(g => g.Count));
    }

    [Fact]
    public async Task Allowed_groupBy_still_translates_to_sql()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync(
            "?groupBy=category&pageSize=50", o => o.Column("price").NotFilterable());

        Assert.Equal(6, result.Total);
        Assert.Equal(240, result.Groups!.Sum(g => g.Count));
    }

    [Fact]
    public async Task Aggregate_on_a_locked_column_is_dropped()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync(
            "?aggregate=max:stock,count:*&pageSize=5", o => o.Column("stock").NotFilterable());

        Assert.False(result.Aggregations!.ContainsKey("max:stock"));
        Assert.Equal(240, result.Aggregations["count:*"]);
    }

    [Fact]
    public async Task Aggregate_on_a_locked_column_is_refused_in_strict_mode()
    {
        await using var db = _fx.NewContext();

        await Assert.ThrowsAsync<InanduGridRequestException>(() => db.Widgets.AsNoTracking().ToInanduGridAsync(
            "?aggregate=max:stock", o => { o.Column("stock").NotFilterable(); o.ThrowOnUnknownField = true; }));
    }
}
