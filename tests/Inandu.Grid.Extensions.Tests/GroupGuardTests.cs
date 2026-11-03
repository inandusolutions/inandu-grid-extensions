using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// Distinct values and <c>groupBy</c> expose a column's values, so they honour the column
/// allow-list like filtering does: a <c>.NotFilterable()</c> column can't be grouped or enumerated,
/// and an unknown field is dropped (or rejected in strict mode) instead of throwing an
/// <see cref="System.ArgumentException"/> that surfaces as HTTP 500.
/// </summary>
public class GroupGuardTests
{
    [Fact]
    public void Distinct_on_a_NotFilterable_column_returns_nothing()
    {
        var result = Sample.Products().ToInanduGridDistinct("sku",
            InanduGridOptions.For(new InanduGridRequest(), o => o.Column("sku").NotFilterable()));

        Assert.Empty(result.Values);
        Assert.Equal(0, result.Total);
        Assert.Equal("sku", result.Field);
    }

    [Fact]
    public void Distinct_on_a_NotFilterable_column_is_refused_in_strict_mode()
    {
        var options = InanduGridOptions.For(new InanduGridRequest(), o =>
        {
            o.Column("sku").NotFilterable();
            o.ThrowOnUnknownField = true;
        });

        var ex = Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGridDistinct("sku", options));
        Assert.True(ex.Errors.ContainsKey("distinct.sku"));
    }

    [Fact]
    public void Distinct_on_an_unknown_field_returns_nothing()
    {
        var result = Sample.Products().ToInanduGridDistinct("doesNotExist");

        Assert.Empty(result.Values);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Distinct_on_an_unknown_field_raises_a_request_error_in_strict_mode()
    {
        var options = InanduGridOptions.For(new InanduGridRequest(), o => o.ThrowOnUnknownField = true);

        var ex = Record.Exception(() => Sample.Products().ToInanduGridDistinct("doesNotExist", options));

        Assert.IsType<InanduGridRequestException>(ex);
    }

    [Fact]
    public void Distinct_on_an_allowed_column_still_works()
    {
        var result = Sample.Products().ToInanduGridDistinct("status",
            InanduGridOptions.For(new InanduGridRequest { PageSize = 100 }, o => o.Column("sku").NotFilterable()));

        Assert.NotEmpty(result.Values);
        Assert.Equal(Sample.Products().Count, result.Values.Sum(v => v.Count));
    }

    [Fact]
    public void GroupBy_on_a_NotFilterable_column_is_dropped()
    {
        var request = new InanduGridRequest();
        request.GroupBy.Add("sku");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o => o.Column("sku").NotFilterable()));

        Assert.True(result.IsLeaf);
        Assert.True(result.Groups is null || result.Groups.Count == 0);
    }

    [Fact]
    public void GroupBy_on_a_NotFilterable_column_is_refused_in_strict_mode()
    {
        var request = new InanduGridRequest();
        request.GroupBy.Add("sku");

        var ex = Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGridGrouped(
            InanduGridOptions.For(request, o => { o.Column("sku").NotFilterable(); o.ThrowOnUnknownField = true; })));
        Assert.True(ex.Errors.ContainsKey("groupBy"));
    }

    [Fact]
    public void Dropping_a_groupBy_level_keeps_the_remaining_levels_and_keys_aligned()
    {
        // groupBy=sku,status with a key for the first level: sku is dropped together with its key,
        // so the request becomes a top-level groupBy=status (not a drill into status=<sku key>).
        var request = new InanduGridRequest();
        request.GroupBy.Add("sku");
        request.GroupBy.Add("status");
        request.GroupKeys.Add("SKU-1");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o => o.Column("sku").NotFilterable()));

        Assert.False(result.IsLeaf);
        Assert.Equal(0, result.Level);
        Assert.Equal(Sample.Products().Count, result.Groups!.Sum(g => g.Count));
    }

    [Fact]
    public void GroupBy_on_an_unknown_field_is_dropped_instead_of_throwing()
    {
        var request = new InanduGridRequest();
        request.GroupBy.Add("doesNotExist");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request));

        Assert.True(result.IsLeaf);
    }

    [Fact]
    public void Aggregate_on_a_NotFilterable_column_is_dropped()
    {
        var result = Sample.Products().ToInanduGrid("?aggregate=max:price,count:*",
            o => o.Column("price").NotFilterable());

        Assert.NotNull(result.Aggregations);
        Assert.False(result.Aggregations!.ContainsKey("max:price"));
        Assert.Equal(Sample.Products().Count, result.Aggregations["count:*"]);
    }

    [Fact]
    public void Aggregate_on_a_NotFilterable_column_is_refused_in_strict_mode()
    {
        var ex = Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid("?aggregate=max:price",
            o => { o.Column("price").NotFilterable(); o.ThrowOnUnknownField = true; }));
        Assert.True(ex.Errors.ContainsKey("aggregate.price"));
    }
}
