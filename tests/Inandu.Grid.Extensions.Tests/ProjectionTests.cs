using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class ProjectionTests
{
    private sealed record ProductDto(int Id, string Name, decimal Price);

    [Fact]
    public void Projects_the_page_to_a_dto()
    {
        var request = new InanduGridRequest { PageSize = 3 };
        request.Sort.Add(new InanduGridSort("price", SortDirection.Descending));

        var result = Sample.Products().ToInanduGrid(
            p => new ProductDto(p.Id, p.Name, p.Price),
            InanduGridOptions.For(request));

        Assert.Equal(8, result.Total);
        Assert.Equal(3, result.Data.Count);
        Assert.IsType<ProductDto>(result.Data[0]);
        Assert.Equal(new[] { 289.00m, 129.00m, 59.00m }, result.Data.Select(d => d.Price));
    }

    [Fact]
    public void Filter_and_sort_resolve_against_the_entity_not_the_dto()
    {
        // category.name is on the entity's nested Category, not on ProductDto
        var request = new InanduGridRequest { PageSize = 100 };
        request.Sort.Add(new InanduGridSort("category.name"));
        request.ColumnFilters["category.name"] = new InanduGridColumnFilter { Values = new() { "Peripherals" } };

        var result = Sample.Products().ToInanduGrid(
            p => new ProductDto(p.Id, p.Name, p.Price),
            InanduGridOptions.For(request));

        Assert.Equal(5, result.Total); // 5 Peripherals in the sample
        Assert.All(result.Data, d => Assert.IsType<ProductDto>(d));
        Assert.Equal(new[] { 1, 2, 6, 7, 8 }, result.Data.Select(d => d.Id).OrderBy(x => x));
    }

    [Fact]
    public void ApplyInanduGridQuery_projected_exposes_projected_queries()
    {
        var request = new InanduGridRequest { Page = 2, PageSize = 20 };
        request.Conditions.Add(new FilterCondition("discontinued", FilterOperator.Equal, "false"));

        var q = Sample.Many(300).AsQueryable().ApplyInanduGridQuery(
            p => new { p.Id, p.Name },
            InanduGridOptions.For(request));

        Assert.Equal(2, q.Page);
        var total = q.FilteredQuery.Count();
        var page = q.PagedQuery.ToList();
        Assert.Equal(20, page.Count);
        Assert.True(total < 300); // some are discontinued
    }
}
