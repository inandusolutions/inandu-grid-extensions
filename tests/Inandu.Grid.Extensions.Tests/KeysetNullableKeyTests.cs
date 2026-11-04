using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// A nullable sort key can hold rows that sort after a page boundary (nulls last when descending), and the seek
/// can't compare against null. Every row must still be visited exactly once, whichever way nulls sort in the
/// database, so keyset mode orders them explicitly: first when ascending, last when descending.
/// </summary>
public class KeysetNullableKeyTests
{
    // 60 rows; every 5th has no Stock, every 4th has no Sku.
    private static List<Product> Rows => Enumerable.Range(1, 60)
        .Select(i => new Product
        {
            Id = i,
            Name = $"P{i}",
            Stock = i % 5 == 0 ? null : i % 9,
            Sku = i % 4 == 0 ? null : $"SKU-{i % 7}",
        })
        .ToList();

    private static List<int> Walk(string sort, int pageSize)
    {
        var seen = new List<int>();
        string? cursor = null;

        for (var guard = 0; guard < 200; guard++)
        {
            var qs = $"?pageSize={pageSize}&sort={sort}" + (cursor is null ? "" : $"&after={cursor}");
            var page = Rows.ToInanduGrid(qs, o => o.EnableKeyset = true);
            seen.AddRange(page.Data.Select(p => p.Id));

            if (page.HasMore != true)
            {
                return seen;
            }

            cursor = page.NextCursor;
            Assert.NotNull(cursor);
        }

        Assert.Fail("The keyset loop did not terminate.");
        return seen;
    }

    [Theory]
    [InlineData("stock,id")]
    [InlineData("-stock,id")]
    [InlineData("sku,id")]
    [InlineData("-sku,id")]
    [InlineData("-stock,sku,id")]
    [InlineData("stock,-sku,id")]
    [InlineData("-sku,-stock,-id")]
    public void Every_row_is_visited_exactly_once(string sort)
    {
        foreach (var pageSize in new[] { 1, 4, 7, 13 })
        {
            var seen = Walk(sort, pageSize);

            Assert.Equal(60, seen.Count);
            Assert.Equal(60, seen.Distinct().Count());
        }
    }

    [Theory]
    [InlineData("stock,id")]
    [InlineData("-stock,id")]
    [InlineData("-stock,sku,id")]
    public void The_keyset_walk_follows_the_same_order_as_one_big_page(string sort)
    {
        var all = Rows.ToInanduGrid($"?pageSize=200&sort={sort}", o => { o.EnableKeyset = true; o.MaxPageSize = 200; });

        Assert.Equal(all.Data.Select(p => p.Id), Walk(sort, 7));
    }

    [Fact]
    public void Nulls_sort_first_ascending_and_last_descending()
    {
        var asc = Rows.ToInanduGrid("?pageSize=60&sort=stock,id", o => { o.EnableKeyset = true; o.MaxPageSize = 60; }).Data;
        var desc = Rows.ToInanduGrid("?pageSize=60&sort=-stock,id", o => { o.EnableKeyset = true; o.MaxPageSize = 60; }).Data;

        Assert.All(asc.Take(12), p => Assert.Null(p.Stock));
        Assert.All(desc.Skip(48), p => Assert.Null(p.Stock));
    }

    [Fact]
    public void Offset_paging_without_keyset_keeps_the_providers_own_null_order()
    {
        // No cursor and EnableKeyset off: ORDER BY is unchanged, so an index on the column can still serve it.
        var plain = Rows.AsQueryable();
        var sorted = Internal.ExpressionBuilder.ApplySort(
            plain, new[] { new InanduGridSort { Field = "stock" } }, new InanduGridOptions());

        Assert.DoesNotContain("== null", sorted.Expression.ToString());
    }
}
