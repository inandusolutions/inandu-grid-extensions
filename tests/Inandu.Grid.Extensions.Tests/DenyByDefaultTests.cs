using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// <c>AllowUnconfiguredFields = false</c>: only fields declared with <c>o.Column(...)</c> are
/// honoured — everything else is dropped (or rejected in strict mode) on every query surface.
/// </summary>
public class DenyByDefaultTests
{
    private static void Locked(InanduGridOptions o)
    {
        o.AllowUnconfiguredFields = false;
        o.Column("name").Searchable();
        o.Column("price");
    }

    [Fact]
    public void Default_stays_permissive()
    {
        var result = Sample.Products().ToInanduGrid("?sku_startsWith=KB&pageSize=100");

        Assert.Single(result.Data);
    }

    [Fact]
    public void Filter_on_an_unconfigured_field_is_dropped()
    {
        var result = Sample.Products().ToInanduGrid("?sku_startsWith=KB&pageSize=100", Locked);

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Filter_on_an_unconfigured_navigation_path_is_dropped()
    {
        var result = Sample.Products().ToInanduGrid("?category.name_eq=Cables&pageSize=100", Locked);

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Filter_on_a_configured_field_still_applies()
    {
        var result = Sample.Products().ToInanduGrid("?price_gte=100&pageSize=100", Locked);

        Assert.Equal(Sample.Products().Count(p => p.Price >= 100), result.Total);
    }

    [Fact]
    public void Unconfigured_filter_is_rejected_in_strict_mode()
    {
        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid("?sku_startsWith=KB",
            o => { Locked(o); o.ThrowOnUnknownField = true; }));
    }

    [Fact]
    public void Sort_on_an_unconfigured_field_is_dropped()
    {
        var unsorted = Sample.Products().ToInanduGrid("?pageSize=100", Locked).Data.Select(p => p.Id).ToList();
        var result = Sample.Products().ToInanduGrid("?sort=-stock&pageSize=100", Locked).Data.Select(p => p.Id).ToList();

        Assert.Equal(unsorted, result);
    }

    [Fact]
    public void Advanced_filter_on_an_unconfigured_field_is_dropped()
    {
        var qs = "?pageSize=100&filter=" + System.Uri.EscapeDataString(
            "{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[{\"kind\":\"condition\",\"field\":\"sku\",\"operator\":\"startsWith\",\"value\":\"KB\"}]}");

        var result = Sample.Products().ToInanduGrid(qs, Locked);

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Distinct_groupBy_and_aggregate_on_an_unconfigured_field_reveal_nothing()
    {
        var distinct = Sample.Products().ToInanduGridDistinct("sku", InanduGridOptions.FromQueryString("?", Locked));
        Assert.Empty(distinct.Values);

        var grouped = Sample.Products().ToInanduGridGrouped(InanduGridOptions.FromQueryString("?groupBy=sku", Locked));
        Assert.True(grouped.IsLeaf);

        var aggregated = Sample.Products().ToInanduGrid("?aggregate=max:stock,max:price", Locked);
        Assert.False(aggregated.Aggregations!.ContainsKey("max:stock"));
        Assert.True(aggregated.Aggregations.ContainsKey("max:price"));
    }

    [Fact]
    public void Free_text_search_no_longer_falls_back_to_every_string_property()
    {
        // No column is marked .Searchable() and SearchableFields is unset: the permissive default
        // searches every string property, deny-by-default searches nothing (q is ignored).
        var permissive = Sample.Products().ToInanduGrid("?q=keyboard&pageSize=100", o => o.Column("price"));
        Assert.Equal(1, permissive.Total);

        var denied = Sample.Products().ToInanduGrid("?q=keyboard&pageSize=100",
            o => { o.AllowUnconfiguredFields = false; o.Column("price"); });
        Assert.Equal(Sample.Products().Count, denied.Total);
    }

    [Fact]
    public void Free_text_search_still_covers_explicitly_searchable_columns()
    {
        var result = Sample.Products().ToInanduGrid("?q=keyboard&pageSize=100", Locked);

        Assert.Equal(1, result.Total);
    }
}
