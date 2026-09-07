using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class SortingTests
{
    private static InanduGridResult<Product> Run(params InanduGridSort[] sort)
    {
        var request = new InanduGridRequest { PageSize = 100 };
        foreach (var s in sort)
        {
            request.Sort.Add(s);
        }

        return Sample.Products().ToInanduGrid(InanduGridOptions.For(request));
    }

    [Fact]
    public void Sorts_ascending_by_default()
    {
        var result = Run(new InanduGridSort("price"));

        var prices = result.Data.Select(p => p.Price).ToList();
        Assert.Equal(prices.OrderBy(p => p).ToList(), prices);
    }

    [Fact]
    public void Sorts_descending()
    {
        var result = Run(new InanduGridSort("price", SortDirection.Descending));

        var prices = result.Data.Select(p => p.Price).ToList();
        Assert.Equal(prices.OrderByDescending(p => p).ToList(), prices);
    }

    [Fact]
    public void Multi_column_sort_respects_priority_order()
    {
        var result = Run(
            new InanduGridSort("discontinued", SortDirection.Ascending),
            new InanduGridSort("price", SortDirection.Descending));

        var expected = Sample.Products()
            .OrderBy(p => p.Discontinued)
            .ThenByDescending(p => p.Price)
            .Select(p => p.Id)
            .ToList();

        Assert.Equal(expected, result.Data.Select(p => p.Id).ToList());
    }

    [Fact]
    public void Sorts_by_a_nested_path()
    {
        var result = Run(new InanduGridSort("category.name"));

        // rows with a null Category sort first (null name), then by name asc
        var names = result.Data.Select(p => p.Category?.Name ?? string.Empty).ToList();
        Assert.Equal(names.OrderBy(n => n, System.StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public void Unknown_sort_field_is_ignored_by_default()
    {
        var result = Run(new InanduGridSort("nope"));

        Assert.Equal(Sample.Products().Select(p => p.Id), result.Data.Select(p => p.Id));
    }

    [Fact]
    public void Unknown_sort_field_throws_when_configured()
    {
        var request = new InanduGridRequest();
        request.Sort.Add(new InanduGridSort("nope"));

        Assert.Throws<System.ArgumentException>(() =>
            Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.ThrowOnUnknownField = true)));
    }

    [Fact]
    public void FieldMap_redirects_a_grid_field_to_a_property_path()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Sort.Add(new InanduGridSort("categoryName", SortDirection.Descending));

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o =>
            o.FieldMap = new System.Collections.Generic.Dictionary<string, string> { ["categoryName"] = "category.name" }));

        var names = result.Data.Select(p => p.Category?.Name ?? string.Empty).ToList();
        Assert.Equal(names.OrderByDescending(n => n, System.StringComparer.Ordinal).ToList(), names);
    }
}
