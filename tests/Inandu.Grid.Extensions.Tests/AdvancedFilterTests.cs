using System;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class AdvancedFilterTests
{
    private static InanduGridResult<Product> Run(string filterJson)
    {
        var request = new InanduGridRequest { PageSize = 100, Filter = filterJson };
        return Sample.Products().ToInanduGrid(InanduGridOptions.For(request));
    }

    [Fact]
    public void Parse_returns_null_for_blank_or_garbage()
    {
        Assert.Null(AdvancedFilterJson.Parse(null));
        Assert.Null(AdvancedFilterJson.Parse("   "));
        Assert.Null(AdvancedFilterJson.Parse("not json"));
        Assert.Null(AdvancedFilterJson.Parse("[1,2,3]"));
    }

    [Fact]
    public void Parses_a_grid_pro_shaped_tree()
    {
        var tree = AdvancedFilterJson.Parse(
            """{"kind":"group","combinator":"or","children":[{"kind":"condition","field":"a","operator":"eq","value":1}]}""");

        Assert.NotNull(tree);
        Assert.Equal(AdvancedFilterCombinator.Or, tree!.Combinator);
        var cond = Assert.IsType<AdvancedFilterCondition>(Assert.Single(tree.Children));
        Assert.Equal("a", cond.Field);
        Assert.Equal(AdvancedFilterOperator.Eq, cond.Operator);
    }

    [Fact]
    public void Nested_and_or()
    {
        // status = Active AND (price > 60 OR stock <= 30)
        var result = Run("""
            {"kind":"group","combinator":"and","children":[
              {"kind":"condition","field":"status","operator":"eq","value":"Active"},
              {"kind":"group","combinator":"or","children":[
                {"kind":"condition","field":"price","operator":"gt","value":60},
                {"kind":"condition","field":"stock","operator":"lte","value":30}
              ]}
            ]}
            """);

        Assert.All(result.Data, p =>
        {
            Assert.Equal(Status.Active, p.Status);
            Assert.True(p.Price > 60m || (p.Stock is int s && s <= 30));
        });
        // Active: 1(45,120) 2(19.5,300) 3(289,25) 6(24,40) 7(59,null)
        //   price>60: 3 ;  stock<=30: 3  ->  {3}
        Assert.Equal(new[] { 3 }, result.Data.Select(p => p.Id));
    }

    [Fact]
    public void Between()
    {
        var result = Run("""{"kind":"group","combinator":"and","children":[{"kind":"condition","field":"price","operator":"between","value":20,"value2":50}]}""");
        Assert.All(result.Data, p => Assert.InRange(p.Price, 20m, 50m));
        Assert.Equal(new[] { 1, 6, 8 }, result.Data.Select(p => p.Id).OrderBy(x => x));
    }

    [Fact]
    public void NotContains()
    {
        var result = Run("""{"kind":"group","combinator":"and","children":[{"kind":"condition","field":"name","operator":"notContains","value":"o"}]}""");
        Assert.DoesNotContain(result.Data, p => p.Name.Contains('o', StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public void IsEmpty_on_a_nullable_column()
    {
        var result = Run("""{"kind":"group","combinator":"and","children":[{"kind":"condition","field":"stock","operator":"isEmpty"}]}""");
        Assert.Equal(new[] { 7 }, result.Data.Select(p => p.Id)); // only product 7 has null stock
    }

    [Fact]
    public void IsNotEmpty_on_a_nullable_string()
    {
        var result = Run("""{"kind":"group","combinator":"and","children":[{"kind":"condition","field":"sku","operator":"isNotEmpty"}]}""");
        Assert.DoesNotContain(result.Data, p => p.Id == 6); // product 6 has null Sku
        Assert.Contains(result.Data, p => p.Id == 1);
    }

    [Fact]
    public void End_to_end_from_a_query_string()
    {
        var json = """{"kind":"group","combinator":"and","children":[{"kind":"condition","field":"status","operator":"eq","value":"Archived"}]}""";
        var qs = "?pageSize=100&filter=" + Uri.EscapeDataString(json);

        var result = Sample.Products().ToInanduGrid(qs);

        Assert.All(result.Data, p => Assert.Equal(Status.Archived, p.Status));
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public void Combines_with_flat_filters_via_AND()
    {
        var request = new InanduGridRequest
        {
            PageSize = 100,
            Filter = """{"kind":"group","combinator":"and","children":[{"kind":"condition","field":"price","operator":"gt","value":30}]}""",
        };
        request.ColumnFilters["status"] = new InanduGridColumnFilter { Values = new() { "Active" } };

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request));

        Assert.All(result.Data, p =>
        {
            Assert.Equal(Status.Active, p.Status);
            Assert.True(p.Price > 30m);
        });
        Assert.Equal(new[] { 1, 3, 7 }, result.Data.Select(p => p.Id).OrderBy(x => x));
    }
}
