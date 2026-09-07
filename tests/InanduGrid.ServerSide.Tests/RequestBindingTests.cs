using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InanduGrid.ServerSide.Tests;

public class RequestBindingTests
{
    [Fact]
    public void Parses_page_pageSize_and_sort_tokens()
    {
        var request = InanduGridRequest.Parse("?page=2&pageSize=15&sort=-createdOn,name");

        Assert.Equal(2, request.Page);
        Assert.Equal(15, request.PageSize);
        Assert.Equal(2, request.Sort.Count);
        Assert.Equal("createdOn", request.Sort[0].Field);
        Assert.Equal(SortDirection.Descending, request.Sort[0].Direction);
        Assert.Equal("name", request.Sort[1].Field);
        Assert.Equal(SortDirection.Ascending, request.Sort[1].Direction);
    }

    [Fact]
    public void Parses_repeated_sort_params()
    {
        var request = InanduGridRequest.Parse("sort=name&sort=-price");

        Assert.Equal(new[] { "name", "price" }, request.Sort.Select(s => s.Field));
        Assert.Equal(SortDirection.Descending, request.Sort[1].Direction);
    }

    [Fact]
    public void Parses_offset_and_limit()
    {
        var request = InanduGridRequest.Parse("offset=200&limit=50");
        Assert.Equal(200, request.Offset);
        Assert.Equal(50, request.Limit);
    }

    [Fact]
    public void Parses_field_operator_conditions()
    {
        var request = InanduGridRequest.Parse("?price_gte=20&price_lte=100&name_contains=key&status_in=Active,Draft&q=cable");

        Assert.Equal("cable", request.Query);

        var conditions = request.Conditions;
        Assert.Contains(conditions, c => c.Field == "price" && c.Operator == FilterOperator.GreaterThanOrEqual && (string)c.Value! == "20");
        Assert.Contains(conditions, c => c.Field == "price" && c.Operator == FilterOperator.LessThanOrEqual);
        Assert.Contains(conditions, c => c.Field == "name" && c.Operator == FilterOperator.Contains);

        var inCondition = Assert.Single(conditions.Where(c => c.Operator == FilterOperator.In));
        Assert.Equal("status", inCondition.Field);
        Assert.Equal(new[] { "Active", "Draft" }, ((IEnumerable<object?>)inCondition.Value!).Select(v => (string)v!));
    }

    [Fact]
    public void Field_with_underscore_and_unknown_suffix_is_not_a_condition()
    {
        var request = InanduGridRequest.Parse("created_on=2026-01-01&weird_thing=x");
        Assert.Empty(request.Conditions);
    }

    [Fact]
    public void Url_encoded_values_are_decoded()
    {
        var request = InanduGridRequest.Parse("q=hello%20world&name_contains=a%2Bb");
        Assert.Equal("hello world", request.Query);
        Assert.Contains(request.Conditions, c => (string)c.Value! == "a+b");
    }

    [Fact]
    public void Plus_is_treated_as_space_in_values()
    {
        var request = InanduGridRequest.Parse("q=hello+world");
        Assert.Equal("hello world", request.Query);
    }

    [Fact]
    public void End_to_end_from_query_string()
    {
        var result = Sample.Products().ToInanduGrid("?sort=-price&status_eq=Active&pageSize=2");

        Assert.Equal(2, result.PageSize);
        Assert.Equal(5, result.Total); // 5 Active
        Assert.Equal(new[] { 3, 7 }, result.Data.Select(p => p.Id)); // 289, 59 are the two priciest Active
    }

    [Fact]
    public void Parse_from_key_value_pairs()
    {
        var pairs = new[]
        {
            new KeyValuePair<string, string?>("page", "3"),
            new KeyValuePair<string, string?>("sort", "name"),
            new KeyValuePair<string, string?>("price_gt", "10"),
        };

        var request = InanduGridRequest.Parse(pairs);

        Assert.Equal(3, request.Page);
        Assert.Single(request.Sort);
        Assert.Single(request.Conditions);
    }
}
