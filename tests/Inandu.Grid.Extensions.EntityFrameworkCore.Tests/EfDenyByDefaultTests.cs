using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inandu.Grid.Extensions.EntityFrameworkCore.Tests;

/// <summary><c>AllowUnconfiguredFields = false</c> over EF Core / SQLite.</summary>
public class EfDenyByDefaultTests : IClassFixture<ShopFixture>
{
    private readonly ShopFixture _fx;

    public EfDenyByDefaultTests(ShopFixture fx) => _fx = fx;

    private static void Locked(InanduGridOptions o)
    {
        o.AllowUnconfiguredFields = false;
        o.Column("category");
        o.Column("name").Searchable();
    }

    [Fact]
    public async Task Unconfigured_filter_is_not_part_of_the_sql()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync("?stock_gt=100&pageSize=5", Locked, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(240, result.Total);
    }

    [Fact]
    public async Task Configured_filter_still_runs_as_sql()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync("?category_eq=Cat 1&pageSize=5", Locked, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(40, result.Total);
    }

    [Fact]
    public async Task Unconfigured_distinct_groupBy_and_aggregate_reveal_nothing()
    {
        await using var db = _fx.NewContext();

        var distinct = await db.Widgets.AsNoTracking().ToInanduGridDistinctAsync("stock", "?pageSize=50", Locked, cancellationToken: TestContext.Current.CancellationToken);
        var grouped = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=stock&pageSize=5", Locked, cancellationToken: TestContext.Current.CancellationToken);
        var aggregated = await db.Widgets.AsNoTracking().ToInanduGridAsync("?aggregate=max:stock,count:*&pageSize=1", Locked, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(distinct.Values);
        Assert.True(grouped.IsLeaf);
        Assert.False(aggregated.Aggregations!.ContainsKey("max:stock"));
        Assert.Equal(240, aggregated.Aggregations["count:*"]);
    }

    [Fact]
    public async Task Free_text_search_only_covers_searchable_columns()
    {
        await using var db = _fx.NewContext();

        // "Cat 1" lives in Category, which is configured but not marked Searchable.
        var byCategory = await db.Widgets.AsNoTracking().ToInanduGridAsync("?q=Cat 1&pageSize=5", Locked, cancellationToken: TestContext.Current.CancellationToken);
        var byName = await db.Widgets.AsNoTracking().ToInanduGridAsync("?q=Widget 0001&pageSize=5", Locked, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, byCategory.Total);
        Assert.Equal(1, byName.Total);
    }

    [Fact]
    public async Task Strict_mode_rejects_an_unconfigured_sort()
    {
        await using var db = _fx.NewContext();

        await Assert.ThrowsAsync<InanduGridRequestException>(() => db.Widgets.AsNoTracking().ToInanduGridAsync(
            "?sort=-stock", o => { Locked(o); o.ThrowOnUnknownField = true; }, cancellationToken: TestContext.Current.CancellationToken));
    }
}
