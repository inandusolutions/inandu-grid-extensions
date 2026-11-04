using System;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>Raw input sizes are capped before they cost anything: the search text, the advanced-filter JSON and the query string.</summary>
public class RequestSizeLimitTests
{
    [Fact]
    public void Long_search_text_is_trimmed_by_default()
    {
        var result = Sample.Products().ToInanduGrid("?q=" + new string('a', 500), o => o.Limits.MaxQueryTextLength = 10);

        Assert.Equal(0, result.Total); // matched on the 10-char prefix, no product name contains it
    }

    [Fact]
    public void Long_search_text_is_rejected_in_reject_mode()
    {
        var ex = Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(
            "?q=" + new string('a', 500),
            o =>
            {
                o.Limits.MaxQueryTextLength = 10;
                o.OnLimitExceeded = InanduGridLimitMode.Reject;
            }));

        Assert.Contains("q", ex.Errors.Keys);
    }

    [Fact]
    public void Search_text_within_the_limit_still_works()
    {
        var result = Sample.Products().ToInanduGrid("?q=Mouse");

        Assert.Single(result.Data);
    }

    private static string BigFilter(int children)
        => "{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[" +
           string.Join(",", Enumerable.Range(0, children).Select(_ => "{\"kind\":\"condition\",\"field\":\"price\",\"operator\":\"gt\",\"value\":1}")) +
           "]}";

    [Fact]
    public void An_oversized_advanced_filter_is_dropped_in_trim_mode()
    {
        var qs = "?pageSize=100&filter=" + Uri.EscapeDataString(BigFilter(5));

        var limited = Sample.Products().ToInanduGrid(qs, o => o.Limits.MaxFilterLength = 50);
        var all = Sample.Products().ToInanduGrid("?pageSize=100");

        Assert.Equal(all.Total, limited.Total);
    }

    [Fact]
    public void An_oversized_advanced_filter_is_rejected_in_reject_mode()
    {
        var qs = "?pageSize=100&filter=" + Uri.EscapeDataString(BigFilter(5));

        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(qs, o =>
        {
            o.Limits.MaxFilterLength = 50;
            o.OnLimitExceeded = InanduGridLimitMode.Reject;
        }));
    }

    [Fact]
    public void An_enormous_filter_is_not_parsed_at_all()
    {
        var request = new InanduGridRequest { Filter = BigFilter(10_000) };

        Assert.Null(request.AdvancedFilter);
        Assert.NotNull(request.Filter);
    }

    [Fact]
    public void An_oversized_query_string_is_cut_at_a_parameter_boundary_in_trim_mode()
    {
        var qs = "?pageSize=5&sort=id&filler=" + new string('x', 200) + "&q=Mouse";

        var options = InanduGridOptions.FromQueryString(qs, o => o.Limits.MaxQueryStringLength = 100);

        Assert.Equal(5, options.Request.PageSize);
        Assert.Null(options.Request.Query); // the parameter past the cap is gone, nothing is half-read
    }

    [Fact]
    public void An_oversized_query_string_is_rejected_in_reject_mode()
    {
        var qs = "?q=" + new string('x', 500);

        Assert.Throws<InanduGridRequestException>(() => InanduGridOptions.FromQueryString(qs, o =>
        {
            o.Limits.MaxQueryStringLength = 100;
            o.OnLimitExceeded = InanduGridLimitMode.Reject;
        }));
    }

    [Fact]
    public void A_plus_in_a_parameter_name_decodes_to_a_space_like_it_does_in_a_value()
    {
        var request = InanduGridRequest.Parse("?pageSize=7&some+key=1");

        Assert.Equal(7, request.PageSize);
    }

    [Fact]
    public void A_comma_is_not_a_thousands_separator_in_a_numeric_filter()
    {
        // "1,5" used to parse as 15 under the invariant culture and silently match the wrong rows.
        var result = Sample.Products().ToInanduGrid("?pageSize=100&price_gte=1,5");
        var all = Sample.Products().ToInanduGrid("?pageSize=100");

        Assert.Equal(all.Total, result.Total); // the unparseable operand is a no-op, not "price >= 15"
    }
}
