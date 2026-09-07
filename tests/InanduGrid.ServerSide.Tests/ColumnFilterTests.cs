using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InanduGrid.ServerSide.Tests;

public class ColumnFilterTests
{
    private static InanduGridResult<Product> Run(string field, InanduGridColumnFilter filter)
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.ColumnFilters[field] = filter;
        return Sample.Products().ToInanduGrid(InanduGridOptions.For(request));
    }

    [Fact]
    public void Text_maps_to_contains()
    {
        var result = Run("name", new InanduGridColumnFilter { Text = "monitor" });
        Assert.Equal(new[] { 3 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void Min_and_max_map_to_a_numeric_range()
    {
        var result = Run("price", new InanduGridColumnFilter { Min = "20", Max = "50" });
        Assert.All(result.Data, p => Assert.InRange(p.Price, 20m, 50m));
        Assert.Contains(result.Data, p => p.Id == 1);
    }

    [Fact]
    public void From_and_to_map_to_a_date_range()
    {
        var result = Run("createdOn", new InanduGridColumnFilter { From = "2026-01-01", To = "2026-01-31" });
        Assert.All(result.Data, p => Assert.InRange(p.CreatedOn, new System.DateTime(2026, 1, 1), new System.DateTime(2026, 1, 31)));
    }

    [Fact]
    public void Bool_true_and_false()
    {
        Assert.All(Run("discontinued", new InanduGridColumnFilter { Bool = "true" }).Data, p => Assert.True(p.Discontinued));
        Assert.All(Run("discontinued", new InanduGridColumnFilter { Bool = "false" }).Data, p => Assert.False(p.Discontinued));
    }

    [Fact]
    public void Values_map_to_an_in_list()
    {
        var result = Run("category.name", new InanduGridColumnFilter { Values = new List<string> { "Cables", "Displays" } });
        Assert.All(result.Data, p => Assert.Contains(p.Category!.Name, new[] { "Cables", "Displays" }));
        Assert.Equal(new[] { 3, 5 }, result.Data.Select(p => p.Id).OrderBy(x => x));
    }

    [Fact]
    public void Empty_values_list_is_no_constraint()
    {
        var result = Run("category.name", new InanduGridColumnFilter { Values = new List<string>() });
        Assert.Equal(Sample.Products().Count, result.Data.Count);
    }

    [Fact]
    public void IsEmpty_is_true_only_when_nothing_is_set()
    {
        Assert.True(new InanduGridColumnFilter().IsEmpty);
        Assert.True(new InanduGridColumnFilter { Bool = "maybe" }.IsEmpty);
        Assert.False(new InanduGridColumnFilter { Text = "x" }.IsEmpty);
        Assert.False(new InanduGridColumnFilter { Values = new() { "a" } }.IsEmpty);
    }

    [Fact]
    public void ColumnFilters_and_free_flat_conditions_both_apply()
    {
        var request = new InanduGridRequest { PageSize = 100, Query = null };
        request.ColumnFilters["status"] = new InanduGridColumnFilter { Values = new() { "Active" } };
        request.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThan, "40"));

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request));

        Assert.All(result.Data, p =>
        {
            Assert.Equal(Status.Active, p.Status);
            Assert.True(p.Price > 40m);
        });
        Assert.Equal(new[] { 1, 3, 7 }, result.Data.Select(p => p.Id).OrderBy(x => x));
    }
}
