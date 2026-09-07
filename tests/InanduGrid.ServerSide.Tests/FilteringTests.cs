using System.Linq;
using Xunit;

namespace InanduGrid.ServerSide.Tests;

public class FilteringTests
{
    private static InanduGridResult<Product> Run(InanduGridRequest request, System.Action<InanduGridOptions>? configure = null)
    {
        request.PageSize = 100;
        return Sample.Products().ToInanduGrid(InanduGridOptions.For(request, configure));
    }

    private static InanduGridRequest WithCondition(string field, FilterOperator op, object? value)
    {
        var r = new InanduGridRequest();
        r.Conditions.Add(new FilterCondition(field, op, value));
        return r;
    }

    [Fact]
    public void Equals_on_int()
    {
        var result = Run(WithCondition("id", FilterOperator.Equal, "3"));
        Assert.Equal(new[] { 3 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void NotEquals_excludes()
    {
        var result = Run(WithCondition("status", FilterOperator.NotEqual, "Active"));
        Assert.DoesNotContain(result.Data, p => p.Status == Status.Active);
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public void Contains_is_case_insensitive_by_default()
    {
        var result = Run(WithCondition("name", FilterOperator.Contains, "ALPHA"));
        Assert.Equal(new[] { 1, 6 }, result.Data.Select(p => p.Id).OrderBy(x => x));
    }

    [Fact]
    public void Contains_can_be_made_case_sensitive()
    {
        var result = Run(WithCondition("name", FilterOperator.Contains, "alpha"),
            o => o.StringComparison = System.StringComparison.Ordinal);
        Assert.Equal(new[] { 6 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void StartsWith_and_EndsWith()
    {
        Assert.Equal(new[] { 3 }, Run(WithCondition("name", FilterOperator.StartsWith, "Gamma")).Data.Select(p => p.Id));
        Assert.Equal(new[] { 2 }, Run(WithCondition("name", FilterOperator.EndsWith, "Mouse")).Data.Select(p => p.Id));
    }

    [Fact]
    public void Numeric_range_gte_and_lte()
    {
        var r = new InanduGridRequest();
        r.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThanOrEqual, "20"));
        r.Conditions.Add(new FilterCondition("price", FilterOperator.LessThanOrEqual, "60"));

        var result = Run(r);

        Assert.All(result.Data, p => Assert.InRange(p.Price, 20m, 60m));
        Assert.Contains(result.Data, p => p.Id == 1);
        Assert.DoesNotContain(result.Data, p => p.Id == 3); // 289
    }

    [Fact]
    public void Date_comparison()
    {
        var result = Run(WithCondition("createdOn", FilterOperator.GreaterThan, "2026-02-01"));
        Assert.All(result.Data, p => Assert.True(p.CreatedOn > new System.DateTime(2026, 2, 1)));
    }

    [Fact]
    public void In_list_on_enum()
    {
        var result = Run(WithCondition("status", FilterOperator.In, new[] { "Draft", "Archived" }));
        Assert.All(result.Data, p => Assert.True(p.Status is Status.Draft or Status.Archived));
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public void Nullable_column_gte_treats_null_as_not_matching()
    {
        // Stock is int?; product 7 has null stock
        var result = Run(WithCondition("stock", FilterOperator.GreaterThanOrEqual, "1"));
        Assert.DoesNotContain(result.Data, p => p.Id == 7);
        Assert.DoesNotContain(result.Data, p => p.Id == 4); // stock 0
    }

    [Fact]
    public void Free_text_search_spans_string_fields()
    {
        var result = Run(new InanduGridRequest { Query = "webcam" });
        Assert.Equal(new[] { 7 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void Free_text_search_can_be_restricted_to_named_fields()
    {
        // "MO-02" only appears in Sku; restricting to Name should find nothing
        var result = Run(new InanduGridRequest { Query = "MO-02" }, o => o.SearchableFields = new() { "name" });
        Assert.Empty(result.Data);

        var bySku = Run(new InanduGridRequest { Query = "MO-02" }, o => o.SearchableFields = new() { "sku" });
        Assert.Equal(new[] { 2 }, bySku.Data.Select(p => p.Id));
    }

    [Fact]
    public void Unknown_filter_field_is_ignored_by_default()
    {
        var result = Run(WithCondition("nope", FilterOperator.Equal, "x"));
        Assert.Equal(Sample.Products().Count, result.Data.Count);
    }

    [Fact]
    public void Multiple_conditions_combine_with_AND()
    {
        var r = new InanduGridRequest();
        r.Conditions.Add(new FilterCondition("status", FilterOperator.Equal, "Active"));
        r.Conditions.Add(new FilterCondition("price", FilterOperator.LessThan, "30"));

        var result = Run(r);

        Assert.All(result.Data, p =>
        {
            Assert.Equal(Status.Active, p.Status);
            Assert.True(p.Price < 30m);
        });
        Assert.Equal(new[] { 2, 6 }, result.Data.Select(p => p.Id).OrderBy(x => x));
    }
}
