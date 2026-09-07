using System;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class ToInanduGridTests
{
    [Fact]
    public void Null_options_uses_defaults()
    {
        var result = Sample.Products().ToInanduGrid();
        Assert.Equal(1, result.Page);
        Assert.Equal(8, result.Total);
        Assert.Equal(8, result.Data.Count);
    }

    [Fact]
    public void Null_source_throws()
    {
        Assert.Throws<ArgumentNullException>(() => ((System.Collections.Generic.IEnumerable<Product>)null!).ToInanduGrid());
    }

    [Fact]
    public void IQueryable_path_matches_the_IEnumerable_path()
    {
        var request = new InanduGridRequest { Page = 2, PageSize = 10, Query = "product" };
        request.Sort.Add(new InanduGridSort("price", SortDirection.Descending));
        request.Conditions.Add(new FilterCondition("stock", FilterOperator.GreaterThan, "100"));

        var data = Sample.Many(200);

        var fromEnumerable = data.ToInanduGrid(InanduGridOptions.For(Clone(request)));
        var fromQueryable = data.AsQueryable().ToInanduGrid(InanduGridOptions.For(Clone(request)));

        Assert.Equal(fromEnumerable.Total, fromQueryable.Total);
        Assert.Equal(
            fromEnumerable.Data.Select(p => p.Id),
            fromQueryable.Data.Select(p => p.Id));
    }

    [Fact]
    public void ApplyInanduGridQuery_exposes_filtered_and_paged_queries()
    {
        var request = new InanduGridRequest { Page = 2, PageSize = 20 };
        request.Conditions.Add(new FilterCondition("discontinued", FilterOperator.Equal, "false"));

        var query = Sample.Many(300).AsQueryable().ApplyInanduGridQuery(InanduGridOptions.For(request));

        var total = query.FilteredQuery.Count();
        var page = query.PagedQuery.ToList();

        Assert.Equal(2, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.All(query.FilteredQuery, p => Assert.False(p.Discontinued));
        Assert.Equal(20, page.Count);

        var result = query.ToResult(page, total);
        Assert.Equal(total, result.Total);
        Assert.Equal(page.Count, result.Data.Count);
    }

    [Fact]
    public void PageCount_math()
    {
        Assert.Equal(4, new InanduGridResult<Product> { Total = 100, PageSize = 25 }.PageCount);
        Assert.Equal(5, new InanduGridResult<Product> { Total = 101, PageSize = 25 }.PageCount);
        Assert.Equal(1, new InanduGridResult<Product> { Total = 0, PageSize = 25 }.PageCount);
        Assert.Equal(1, new InanduGridResult<Product> { Total = 10, PageSize = 0 }.PageCount);
    }

    [Fact]
    public void ResolvePaging_from_offset_limit()
    {
        var (page, size) = new InanduGridRequest { Offset = 60, Limit = 20 }.ResolvePaging(25, 500);
        Assert.Equal(4, page);
        Assert.Equal(20, size);
    }

    private static InanduGridRequest Clone(InanduGridRequest r)
    {
        var copy = new InanduGridRequest
        {
            Page = r.Page,
            PageSize = r.PageSize,
            Offset = r.Offset,
            Limit = r.Limit,
            Query = r.Query,
        };
        foreach (var s in r.Sort)
        {
            copy.Sort.Add(new InanduGridSort(s.Field, s.Direction));
        }

        foreach (var c in r.Conditions)
        {
            copy.Conditions.Add(new FilterCondition(c.Field, c.Operator, c.Value));
        }

        return copy;
    }
}
