using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// The counts were capped; the length of each string was not. A cursor, a field name or a filter value of a
/// megabyte reached the decoder, the property lookup or a LIKE '%...%' unchecked.
/// </summary>
public class StringSizeLimitTests
{
    private static string Big(int n = 5000) => new('a', n);

    private static void Reject(InanduGridOptions o) => o.OnLimitExceeded = InanduGridLimitMode.Reject;

    // -- cursor ---------------------------------------------------------

    [Fact]
    public void An_oversized_cursor_is_treated_as_invalid_and_never_decoded()
    {
        var result = Sample.Many(100).ToInanduGrid("?pageSize=10&sort=id&after=" + Big(), o => o.EnableKeyset = true);

        Assert.Equal(Enumerable.Range(1, 10), result.Data.Select(p => p.Id)); // the first page, like any cursor that does not decode
    }

    [Fact]
    public void An_oversized_cursor_is_rejected_in_reject_mode()
    {
        var ex = Assert.Throws<InanduGridRequestException>(() =>
            Sample.Many(100).ToInanduGrid("?pageSize=10&sort=id&after=" + Big(), o => { o.EnableKeyset = true; Reject(o); }));

        Assert.Contains("after", ex.Errors.Keys);
    }

    [Fact]
    public void A_real_cursor_is_far_below_the_limit()
    {
        var first = Sample.Many(100).ToInanduGrid("?pageSize=10&sort=id", o => o.EnableKeyset = true);
        var second = Sample.Many(100).ToInanduGrid($"?pageSize=10&sort=id&after={first.NextCursor}", o => o.EnableKeyset = true);

        Assert.True(first.NextCursor!.Length < new InanduGridLimits().MaxCursorLength);
        Assert.Equal(Enumerable.Range(11, 10), second.Data.Select(p => p.Id));
    }

    [Fact]
    public void The_cursor_limit_is_configurable()
    {
        var first = Sample.Many(100).ToInanduGrid("?pageSize=10&sort=id", o => o.EnableKeyset = true);

        var second = Sample.Many(100).ToInanduGrid($"?pageSize=10&sort=id&after={first.NextCursor}", o => { o.EnableKeyset = true; o.Limits.MaxCursorLength = 4; });

        Assert.Equal(Enumerable.Range(1, 10), second.Data.Select(p => p.Id)); // too long for 4: dropped
    }

    // -- values ---------------------------------------------------------

    [Fact]
    public void A_condition_with_a_huge_value_is_dropped_in_trim_mode()
    {
        var all = Sample.Many(20).ToInanduGrid("?pageSize=50");
        var limited = Sample.Many(20).ToInanduGrid("?pageSize=50&name_contains=" + Big());

        Assert.Equal(all.Total, limited.Total);
    }

    [Fact]
    public void A_condition_with_a_huge_value_is_rejected_in_reject_mode()
    {
        var ex = Assert.Throws<InanduGridRequestException>(() =>
            Sample.Many(20).ToInanduGrid("?pageSize=50&name_contains=" + Big(), Reject));

        Assert.Contains("filter.name", ex.Errors.Keys);
    }

    [Fact]
    public void Oversized_in_items_are_dropped_and_the_rest_kept()
    {
        var result = Sample.Many(20).ToInanduGrid("?pageSize=50&id_in=1,2," + Big() + ",3");

        Assert.Equal(new[] { 1, 2, 3 }, result.Data.Select(p => p.Id).OrderBy(i => i));
    }

    [Fact]
    public void A_column_filter_with_a_huge_value_is_dropped()
    {
        var request = new InanduGridRequest { PageSize = 50 };
        request.ColumnFilters["name"] = new InanduGridColumnFilter { Text = Big() };

        var result = Sample.Many(20).ToInanduGrid(InanduGridOptions.For(request));

        Assert.Equal(20, result.Total);
    }

    [Fact]
    public void A_set_filter_with_a_huge_value_is_dropped()
    {
        var request = new InanduGridRequest { PageSize = 50 };
        request.ColumnFilters["name"] = new InanduGridColumnFilter { Values = new List<string> { "P1", Big() } };

        var result = Sample.Many(20).ToInanduGrid(InanduGridOptions.For(request));

        Assert.Equal(20, result.Total);
    }

    [Fact]
    public void Values_within_the_limit_filter_as_before()
    {
        var result = Sample.Many(20).ToInanduGrid("?pageSize=50&id_in=1,2,3");

        Assert.Equal(3, result.Total);
    }

    [Fact]
    public void The_value_limit_is_configurable()
    {
        var result = Sample.Many(20).ToInanduGrid("?pageSize=50&name_contains=abc", o => o.Limits.MaxValueLength = 0);

        Assert.Equal(20, result.Total); // "abc" is too long for 0: the condition is dropped
    }

    [Fact]
    public void An_in_list_left_empty_by_the_limit_matches_nothing()
    {
        var result = Sample.Many(20).ToInanduGrid("?pageSize=50&id_in=1,2,3", o => o.Limits.MaxValueLength = 0);

        Assert.Equal(0, result.Total); // narrowing, like the existing in-list cap: the safe direction
    }

    // -- field names ----------------------------------------------------

    [Fact]
    public void A_huge_sort_field_name_is_dropped_and_never_looked_up()
    {
        var result = Sample.Many(20).ToInanduGrid("?pageSize=5&sort=" + Big() + ",id");

        Assert.Equal(Enumerable.Range(1, 5), result.Data.Select(p => p.Id));
    }

    [Fact]
    public void A_huge_filter_field_name_is_dropped()
    {
        var result = Sample.Many(20).ToInanduGrid("?pageSize=50&" + Big() + "_eq=1");

        Assert.Equal(20, result.Total);
    }

    [Fact]
    public void A_huge_field_name_is_rejected_in_reject_mode()
    {
        var ex = Assert.Throws<InanduGridRequestException>(() =>
            Sample.Many(20).ToInanduGrid("?pageSize=5&sort=" + Big(), Reject));

        Assert.Contains("sort", ex.Errors.Keys);
    }

    [Fact]
    public void Defaults_are_what_the_docs_say()
    {
        var limits = new InanduGridLimits();

        Assert.Equal(4096, limits.MaxCursorLength);
        Assert.Equal(1000, limits.MaxValueLength);
        Assert.Equal(128, limits.MaxFieldNameLength);
    }
}
