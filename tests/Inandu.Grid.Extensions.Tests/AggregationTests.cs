using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class AggregationTests
{
    private static InanduGridResult<Product> Run(string qs) => Sample.Products().ToInanduGrid(qs);

    [Fact]
    public void Parse_aggregate_tokens()
    {
        var r = InanduGridRequest.Parse("?aggregate=sum:price,avg:stock,count:*,min:createdOn,max:price");
        Assert.Equal(5, r.Aggregations.Count);
        Assert.Equal("sum:price", r.Aggregations[0].Key);
        Assert.Equal(AggregateFunction.Count, r.Aggregations[2].Function);
        Assert.Equal("*", r.Aggregations[2].Field);
    }

    [Fact]
    public void Sum_and_average_over_the_filtered_set()
    {
        var result = Run("?pageSize=2&status_eq=Active&aggregate=sum:price,avg:price,count:*");

        var active = Sample.Products().Where(p => p.Status == Status.Active).ToList();
        Assert.NotNull(result.Aggregations);
        Assert.Equal(active.Sum(p => p.Price), (decimal?)result.Aggregations!["sum:price"]);
        Assert.Equal(active.Average(p => p.Price), (decimal?)result.Aggregations["avg:price"]);
        Assert.Equal(active.Count, result.Aggregations["count:*"]);
        Assert.Equal(2, result.Data.Count); // page is still just 2 rows
    }

    [Fact]
    public void Min_max_keep_the_member_type()
    {
        var result = Run("?aggregate=min:createdOn,max:price,min:stock");

        Assert.IsType<System.DateTime>(result.Aggregations!["min:createdOn"]);
        Assert.Equal(Sample.Products().Max(p => p.Price), (decimal)result.Aggregations["max:price"]!);
        Assert.Equal(Sample.Products().Where(p => p.Stock != null).Min(p => p.Stock), (int?)result.Aggregations["min:stock"]);
    }

    [Fact]
    public void Count_of_a_field_counts_non_null()
    {
        var result = Run("?aggregate=count:stock,count:sku");
        Assert.Equal(Sample.Products().Count(p => p.Stock != null), result.Aggregations!["count:stock"]);
        Assert.Equal(Sample.Products().Count(p => p.Sku != null), result.Aggregations["count:sku"]);
    }

    [Fact]
    public void No_aggregate_param_means_null_aggregations()
    {
        Assert.Null(Run("?pageSize=3").Aggregations);
    }

    [Fact]
    public void Non_numeric_field_for_sum_is_skipped()
    {
        var result = Run("?aggregate=sum:name,count:*");
        Assert.False(result.Aggregations!.ContainsKey("sum:name"));
        Assert.True(result.Aggregations.ContainsKey("count:*"));
    }
}
