using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class GroupingTests
{
    // Many(90): Status = (Status)(i % 3) -> 30 Draft, 30 Active, 30 Archived; Category "Cat 0".."Cat 4".
    private static InanduGridGroupedResult<Product> Run(string queryString)
        => Sample.Many(90).ToInanduGridGrouped(InanduGridOptions.FromQueryString(queryString));

    [Fact]
    public void Top_level_groups_with_counts()
    {
        var result = Run("?groupBy=status&pageSize=50");

        Assert.False(result.IsLeaf);
        Assert.Equal(0, result.Level);
        Assert.NotNull(result.Groups);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Groups!.Count);
        Assert.All(result.Groups, g => Assert.Equal(30, g.Count));
        Assert.All(result.Groups, g => Assert.Equal("status", g.Field));
    }

    [Fact]
    public void Groups_can_be_ordered_by_count_descending()
    {
        // filter so the group sizes differ: price >= 50 keeps ~half, unevenly across statuses
        var result = Run("?groupBy=status&sort=-count&price_gte=50&pageSize=50");

        var counts = result.Groups!.Select(g => g.Count).ToList();
        Assert.Equal(counts.OrderByDescending(c => c).ToList(), counts);
    }

    [Fact]
    public void Drill_into_a_group_returns_the_next_level()
    {
        var result = Run("?groupBy=status,category.name&groupKeys=Active&pageSize=50");

        Assert.False(result.IsLeaf);
        Assert.Equal(1, result.Level);
        Assert.NotNull(result.Groups);
        Assert.All(result.Groups!, g => Assert.Equal("category.name", g.Field));
        // 30 Active rows spread over the 5 categories
        Assert.Equal(30, result.Groups!.Sum(g => g.Count));
    }

    [Fact]
    public void Drill_past_the_last_level_returns_rows()
    {
        var result = Run("?groupBy=status&groupKeys=Active&pageSize=1000");

        Assert.True(result.IsLeaf);
        Assert.Equal(1, result.Level);
        Assert.NotNull(result.Rows);
        Assert.Equal(30, result.Total);
        Assert.All(result.Rows!, p => Assert.Equal(Status.Active, p.Status));
    }

    [Fact]
    public void Filters_apply_to_the_group_counts()
    {
        var all = Run("?groupBy=status&pageSize=50");
        var filtered = Run("?groupBy=status&discontinued_eq=false&pageSize=50");

        Assert.Equal(90, all.Groups!.Sum(g => g.Count));
        Assert.True(filtered.Groups!.Sum(g => g.Count) < 90); // some are discontinued
        Assert.All(filtered.Groups!, g => Assert.True(g.Count <= 30));
    }

    [Fact]
    public void No_groupBy_behaves_as_a_leaf()
    {
        var result = Run("?pageSize=10");

        Assert.True(result.IsLeaf);
        Assert.NotNull(result.Rows);
        Assert.Equal(10, result.Rows!.Count);
        Assert.Equal(90, result.Total);
    }
}
