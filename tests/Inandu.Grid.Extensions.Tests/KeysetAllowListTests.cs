using System.Collections.Generic;
using System.Linq;
using Inandu.Grid.Extensions.Internal;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// Keyset paging builds its seek predicate and its next cursor from the sort list. That list must
/// be the one the options allow, so a field hidden by deny-by-default or <c>Sortable(false)</c>
/// never reaches a cursor or a seek.
/// </summary>
public class KeysetAllowListTests
{
    private static List<Product> Many => Sample.Many(100);

    private static void Locked(InanduGridOptions o)
    {
        o.AllowUnconfiguredFields = false;
        o.EnableKeyset = true;
        o.Column("id");
    }

    [Fact]
    public void A_sort_on_a_hidden_field_does_not_put_that_field_in_the_cursor()
    {
        var result = Many.ToInanduGrid("?pageSize=10&sort=id,sku", Locked);
        var idOnly = Many.ToInanduGrid("?pageSize=10&sort=id", Locked);

        Assert.NotNull(result.NextCursor);
        Assert.Equal(idOnly.NextCursor, result.NextCursor);
        Assert.Equal(1, KeysetCursor.Decode(result.NextCursor)!.Count);
    }

    [Fact]
    public void Sorting_only_by_a_hidden_field_yields_no_cursor()
    {
        var result = Many.ToInanduGrid("?pageSize=10&sort=sku", Locked);

        Assert.Equal(10, result.Data.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public void A_cursor_cannot_be_used_to_filter_on_a_hidden_field()
    {
        var cursor = KeysetCursor.Encode(new object?[] { "SKU-00050" });

        var result = Many.ToInanduGrid($"?pageSize=10&sort=sku&after={cursor}", Locked);

        // The hidden sort is dropped, so the cursor is ignored: no seek, rows start from the top.
        Assert.Equal(Enumerable.Range(1, 10), result.Data.Select(p => p.Id));
    }

    [Fact]
    public void A_column_marked_not_sortable_stays_out_of_the_cursor()
    {
        void Configure(InanduGridOptions o)
        {
            o.EnableKeyset = true;
            o.Column("sku").Sortable(false);
        }

        var result = Many.ToInanduGrid("?pageSize=10&sort=id,sku", Configure);
        var idOnly = Many.ToInanduGrid("?pageSize=10&sort=id", Configure);

        Assert.Equal(idOnly.NextCursor, result.NextCursor);
        Assert.Equal(1, KeysetCursor.Decode(result.NextCursor)!.Count);
    }

    [Fact]
    public void An_unknown_sort_field_stays_out_of_the_cursor()
    {
        var result = Many.ToInanduGrid("?pageSize=10&sort=id,doesNotExist", o => o.EnableKeyset = true);
        var idOnly = Many.ToInanduGrid("?pageSize=10&sort=id", o => o.EnableKeyset = true);

        Assert.Equal(idOnly.NextCursor, result.NextCursor);
    }

    [Fact]
    public void Allowed_sorts_still_page_through_every_row_with_no_overlap()
    {
        var seen = new List<int>();
        var qs = "?pageSize=20&sort=id,sku";
        string? cursor = null;

        do
        {
            var page = Many.ToInanduGrid(cursor is null ? qs : $"{qs}&after={cursor}", o =>
            {
                o.EnableKeyset = true;
                o.Column("id");
            });
            seen.AddRange(page.Data.Select(p => p.Id));
            cursor = page.HasMore == true ? page.NextCursor : null;
        }
        while (cursor is not null);

        Assert.Equal(Enumerable.Range(1, 100), seen);
    }
}

/// <summary>The advanced filter honours <c>Filterable(operators)</c> the same way the flat parameters do.</summary>
public class AdvancedFilterOperatorRestrictionTests
{
    private static string Filter(string op, string value = "\"Alpha\"")
        => "?pageSize=100&filter=" + System.Uri.EscapeDataString(
            "{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[{\"kind\":\"condition\",\"field\":\"name\",\"operator\":\"" + op + "\",\"value\":" + value + "}]}");

    private static void OnlyEqual(InanduGridOptions o) => o.Column("name").Filterable(FilterOperator.Equal);

    [Fact]
    public void An_operator_the_column_does_not_allow_is_ignored()
    {
        var restricted = Sample.Products().ToInanduGrid(Filter("contains"), OnlyEqual);
        var unfiltered = Sample.Products().ToInanduGrid("?pageSize=100");

        Assert.Equal(unfiltered.Total, restricted.Total);
    }

    [Fact]
    public void An_operator_the_column_does_not_allow_throws_when_ThrowOnUnknownField_is_on()
    {
        Assert.Throws<InanduGridRequestException>(() =>
            Sample.Products().ToInanduGrid(Filter("startsWith"), o => { OnlyEqual(o); o.ThrowOnUnknownField = true; }));
    }

    [Fact]
    public void An_allowed_operator_still_filters()
    {
        var result = Sample.Products().ToInanduGrid(Filter("eq", "\"Beta Mouse\""), OnlyEqual);

        Assert.Single(result.Data);
        Assert.Equal("Beta Mouse", result.Data[0].Name);
    }

    [Fact]
    public void Between_needs_both_bounds_allowed()
    {
        var json = "?pageSize=100&filter=" + System.Uri.EscapeDataString(
            "{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[{\"kind\":\"condition\",\"field\":\"price\",\"operator\":\"between\",\"value\":10,\"value2\":50}]}");

        var onlyGte = Sample.Products().ToInanduGrid(json, o => o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual));
        var both = Sample.Products().ToInanduGrid(json, o => o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual, FilterOperator.LessThanOrEqual));

        Assert.Equal(Sample.Products().Count, onlyGte.Total);
        Assert.True(both.Total < Sample.Products().Count);
    }
}
