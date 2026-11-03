using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>Edge cases around the allow-list on grouping, distinct values and aggregates.</summary>
public class GroupGuardEdgeCaseTests
{
    private static int ActiveCount => Sample.Products().Count(p => p.Status == Status.Active);

    [Fact]
    public void A_disallowed_middle_level_is_removed_with_its_key_and_the_drill_continues_below()
    {
        // groupBy=status,sku,category.name + groupKeys=[Active, KB-01], sku locked
        //   => groupBy=status,category.name + groupKeys=[Active] => level 1 (categories inside Active).
        var request = new InanduGridRequest { PageSize = 100 };
        request.GroupBy.Add("status");
        request.GroupBy.Add("sku");
        request.GroupBy.Add("category.name");
        request.GroupKeys.Add("Active");
        request.GroupKeys.Add("KB-01");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o => o.Column("sku").NotFilterable()));

        Assert.False(result.IsLeaf);
        Assert.Equal(1, result.Level);
        Assert.Equal(ActiveCount, result.Groups!.Sum(g => g.Count));
        Assert.Equal(new[] { "category.name" }, new[] { request.GroupBy[1] });
        Assert.Single(request.GroupKeys);
    }

    [Fact]
    public void Several_disallowed_levels_are_all_removed()
    {
        var request = new InanduGridRequest();
        request.GroupBy.Add("sku");
        request.GroupBy.Add("price");
        request.GroupBy.Add("status");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o =>
        {
            o.Column("sku").NotFilterable();
            o.Column("price").NotFilterable();
        }));

        Assert.Equal(new[] { "status" }, request.GroupBy.ToArray());
        Assert.False(result.IsLeaf);
        Assert.Equal(Sample.Products().Count, result.Groups!.Sum(g => g.Count));
    }

    [Fact]
    public void Drill_down_on_an_allowed_column_still_returns_the_rows()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.GroupBy.Add("status");
        request.GroupKeys.Add("Active");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o => o.Column("sku").NotFilterable()));

        Assert.True(result.IsLeaf);
        Assert.Equal(ActiveCount, result.Total);
        Assert.All(result.Rows!, p => Assert.Equal(Status.Active, p.Status));
    }

    [Fact]
    public void A_blank_groupBy_entry_is_dropped()
    {
        var request = new InanduGridRequest();
        request.GroupBy.Add("  ");
        request.GroupBy.Add("status");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request));

        Assert.Equal(new[] { "status" }, request.GroupBy.ToArray());
        Assert.False(result.IsLeaf);
    }

    [Fact]
    public void Column_config_lookup_is_case_insensitive()
    {
        var result = Sample.Products().ToInanduGridDistinct("sku",
            InanduGridOptions.For(new InanduGridRequest(), o => o.Column("SKU").NotFilterable()));

        Assert.Empty(result.Values);
    }

    [Fact]
    public void A_locked_column_with_a_Path_is_locked_under_its_grid_name()
    {
        var result = Sample.Products().ToInanduGridDistinct("cat",
            InanduGridOptions.For(new InanduGridRequest(), o => o.Column("cat").Path("category.name").NotFilterable()));

        Assert.Empty(result.Values);
    }

    [Fact]
    public void An_allowed_column_with_a_Path_lists_the_nested_values()
    {
        var result = Sample.Products().ToInanduGridDistinct("cat",
            InanduGridOptions.For(new InanduGridRequest { PageSize = 100 }, o => o.Column("cat").Path("category.name")));

        Assert.Equal(Sample.Products().Count, result.Values.Sum(v => v.Count));
        Assert.Contains(result.Values, v => (string?)v.Value == "Peripherals");
    }

    [Fact]
    public void Distinct_and_grouping_follow_CanFilter_not_CanSort()
    {
        var options = InanduGridOptions.For(new InanduGridRequest { PageSize = 100 }, o => o.Column("status").Sortable(false));

        var distinct = Sample.Products().ToInanduGridDistinct("status", options);

        Assert.NotEmpty(distinct.Values);
    }

    [Fact]
    public void A_column_restricted_to_some_operators_can_still_be_listed()
    {
        var options = InanduGridOptions.For(new InanduGridRequest { PageSize = 100 },
            o => o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual, FilterOperator.LessThanOrEqual));

        var distinct = Sample.Products().ToInanduGridDistinct("price", options);

        Assert.Equal(Sample.Products().Count, distinct.Values.Sum(v => v.Count));
    }

    [Fact]
    public void Limits_trim_first_then_the_allow_list_applies()
    {
        var request = new InanduGridRequest();
        request.GroupBy.Add("status");
        request.GroupBy.Add("sku");

        var result = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o =>
        {
            o.Limits.MaxGroupByLevels = 1;
            o.Column("sku").NotFilterable();
        }));

        Assert.Equal(new[] { "status" }, request.GroupBy.ToArray());
        Assert.Equal(Sample.Products().Count, result.Groups!.Sum(g => g.Count));
    }

    [Fact]
    public void Strict_mode_still_allows_a_permitted_groupBy_and_distinct()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.GroupBy.Add("status");

        var grouped = Sample.Products().ToInanduGridGrouped(InanduGridOptions.For(request, o =>
        {
            o.Column("sku").NotFilterable();
            o.ThrowOnUnknownField = true;
        }));
        var distinct = Sample.Products().ToInanduGridDistinct("status", InanduGridOptions.For(new InanduGridRequest(), o =>
        {
            o.Column("sku").NotFilterable();
            o.ThrowOnUnknownField = true;
        }));

        Assert.False(grouped.IsLeaf);
        Assert.NotEmpty(distinct.Values);
    }

    [Fact]
    public void Count_on_a_locked_column_is_dropped_while_other_aggregates_are_kept()
    {
        var result = Sample.Products().ToInanduGrid("?aggregate=count:sku,sum:price,count:*",
            o => o.Column("sku").NotFilterable());

        Assert.False(result.Aggregations!.ContainsKey("count:sku"));
        Assert.Equal(Sample.Products().Sum(p => p.Price), (decimal?)result.Aggregations["sum:price"]);
        Assert.Equal(Sample.Products().Count, result.Aggregations["count:*"]);
    }

    [Fact]
    public void Advanced_filter_on_a_locked_column_is_still_dropped()
    {
        var qs = "?pageSize=100&filter=" + System.Uri.EscapeDataString(
            "{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[{\"kind\":\"condition\",\"field\":\"sku\",\"operator\":\"startsWith\",\"value\":\"KB\"}]}");

        var result = Sample.Products().ToInanduGrid(qs, o => o.Column("sku").NotFilterable());

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Distinct_honours_the_other_filters_of_the_request()
    {
        var result = Sample.Products().ToInanduGridDistinct("status",
            InanduGridOptions.FromQueryString("?discontinued_eq=true&pageSize=100", o => o.Column("sku").NotFilterable()));

        Assert.Equal(Sample.Products().Count(p => p.Discontinued), result.Values.Sum(v => v.Count));
    }
}
