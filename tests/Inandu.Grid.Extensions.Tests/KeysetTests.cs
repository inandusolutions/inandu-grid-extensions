using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class KeysetTests
{
    private static List<Product> Many => Sample.Many(100);

    [Fact]
    public void First_page_with_EnableKeyset_emits_a_cursor_and_HasMore()
    {
        var result = Many.ToInanduGrid("?pageSize=10&sort=id", o => o.EnableKeyset = true);

        Assert.Equal(10, result.Data.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, result.Data.Select(p => p.Id));
        Assert.True(result.HasMore);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public void Following_the_cursor_returns_the_next_slice_with_no_overlap()
    {
        var page1 = Many.ToInanduGrid("?pageSize=10&sort=id", o => o.EnableKeyset = true);
        var page2 = Many.ToInanduGrid($"?pageSize=10&sort=id&after={page1.NextCursor}");

        Assert.Equal(Enumerable.Range(11, 10), page2.Data.Select(p => p.Id));
        Assert.True(page2.HasMore);
    }

    [Fact]
    public void Descending_sort_seeks_the_other_way()
    {
        var page1 = Many.ToInanduGrid("?pageSize=5&sort=-id", o => o.EnableKeyset = true);
        var page2 = Many.ToInanduGrid($"?pageSize=5&sort=-id&after={page1.NextCursor}");

        Assert.Equal(new[] { 100, 99, 98, 97, 96 }, page1.Data.Select(p => p.Id));
        Assert.Equal(new[] { 95, 94, 93, 92, 91 }, page2.Data.Select(p => p.Id));
    }

    [Fact]
    public void Multi_column_sort_uses_a_compound_seek()
    {
        // sort by status asc, then id asc — 90 rows: 30 Draft(0), 30 Active(1), 30 Archived(2)
        var many = Sample.Many(90);
        var all = many.ToInanduGrid("?pageSize=1000&sort=status,id").Data.Select(p => p.Id).ToList();

        var acc = new List<int>();
        string? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var qs = cursor is null ? "?pageSize=13&sort=status,id" : $"?pageSize=13&sort=status,id&after={cursor}";
            var page = many.ToInanduGrid(qs, o => o.EnableKeyset = true);
            acc.AddRange(page.Data.Select(p => p.Id));
            cursor = page.NextCursor;
            if (page.HasMore != true)
            {
                break;
            }
        }

        Assert.Equal(all, acc);
    }

    [Fact]
    public void IncludeTotal_false_skips_the_count()
    {
        var result = Many.ToInanduGrid("?pageSize=10&sort=id", o => { o.EnableKeyset = true; o.IncludeTotal = false; });
        Assert.Equal(-1, result.Total);
        Assert.Equal(0, result.PageCount);
    }

    [Fact]
    public void No_sort_means_no_keyset()
    {
        var result = Many.ToInanduGrid("?pageSize=10", o => o.EnableKeyset = true);
        Assert.Null(result.NextCursor);
        Assert.Null(result.HasMore);
    }

    [Fact]
    public void A_garbage_cursor_falls_back_to_offset()
    {
        var result = Many.ToInanduGrid("?pageSize=10&sort=id&after=not-a-cursor");
        Assert.Equal(Enumerable.Range(1, 10), result.Data.Select(p => p.Id));
    }
}

/// <summary>A null sort key at a page boundary can't be sought past; the cursor must still advance.</summary>
public class KeysetNullBoundaryTests
{
    // 60 rows; the first 12 have no Sku (they sort first), the rest have unique ones.
    private static List<Product> Rows => Enumerable.Range(1, 60)
        .Select(i => new Product { Id = i, Name = $"P{i}", Sku = i <= 12 ? null : $"SKU-{i:D3}" })
        .ToList();

    private static List<int> Walk(string sort, int pageSize)
    {
        var seen = new List<int>();
        string? cursor = null;

        for (var guard = 0; guard < 100; guard++)
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

    [Fact]
    public void Following_cursors_across_null_sort_keys_terminates_and_visits_each_row_once()
    {
        var seen = Walk("sku,id", 5);

        Assert.Equal(60, seen.Count);
        Assert.Equal(60, seen.Distinct().Count());
    }

    [Fact]
    public void Descending_sort_with_null_keys_also_terminates()
    {
        var seen = Walk("-sku,id", 7);

        Assert.Equal(60, seen.Distinct().Count());
    }

    [Fact]
    public void An_old_array_form_cursor_is_still_accepted()
    {
        var first = Sample.Many(50).ToInanduGrid("?pageSize=10&sort=id", o => o.EnableKeyset = true);
        var legacy = Inandu.Grid.Extensions.Internal.KeysetCursor.Encode(new object?[] { 10 });

        var next = Sample.Many(50).ToInanduGrid($"?pageSize=10&sort=id&after={legacy}");

        Assert.NotNull(first.NextCursor);
        Assert.Equal(Enumerable.Range(11, 10), next.Data.Select(p => p.Id));
    }
}
