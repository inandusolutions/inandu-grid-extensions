using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// The set filter (<c>{field}_in</c>) splits on commas, so a value like "Smith, John" can't be sent that
/// way. <c>inEncoding=repeat</c> takes one value per occurrence; <c>{field}_none</c> is the empty set.
/// </summary>
public class SetFilterEncodingTests
{
    private static List<Product> Rows => new[] { "Smith, John", "Ann", "Bob", "", " Padded " }
        .Select((name, i) => new Product { Id = i + 1, Name = name })
        .ToList();

    [Fact]
    public void Without_the_marker_a_comma_splits_the_value()
    {
        var result = Rows.ToInanduGrid("?name_in=Smith, John");

        Assert.Empty(result.Data); // "Smith" and "John" match nothing
    }

    [Fact]
    public void With_the_marker_a_value_containing_a_comma_is_one_value()
    {
        var result = Rows.ToInanduGrid("?name_in=Smith, John&name_in=Ann&inEncoding=repeat&sort=id");

        Assert.Equal(new[] { 1, 2 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void With_the_marker_an_empty_value_and_padding_survive()
    {
        var result = Rows.ToInanduGrid("?name_in=&name_in=%20Padded%20&inEncoding=repeat&sort=id", o => o.StringComparison = System.StringComparison.Ordinal);

        Assert.Equal(new[] { 4, 5 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void The_marker_is_not_read_as_a_filter()
    {
        var result = Rows.ToInanduGrid("?inEncoding=repeat&pageSize=100");

        Assert.Equal(5, result.Total);
    }

    [Fact]
    public void An_explicit_none_matches_no_rows()
    {
        var result = Rows.ToInanduGrid("?name_none=1");

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Data);
    }

    [Fact]
    public void An_empty_list_built_by_hand_matches_no_rows()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Conditions.Add(new FilterCondition("name", FilterOperator.In, new List<string>()));

        var result = Rows.ToInanduGrid(InanduGridOptions.For(request));

        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void An_in_with_no_usable_text_is_still_no_constraint()
    {
        var result = Rows.ToInanduGrid("?name_in=&pageSize=100");

        Assert.Equal(5, result.Total);
    }
}
