using System.Linq;
using Xunit;

namespace InanduGrid.ServerSide.Tests;

public class PagingTests
{
    [Fact]
    public void Default_page_and_size_when_request_is_empty()
    {
        var result = Sample.Many(100).ToInanduGrid(new InanduGridOptions());

        Assert.Equal(1, result.Page);
        Assert.Equal(25, result.PageSize);
        Assert.Equal(100, result.Total);
        Assert.Equal(25, result.Data.Count);
        Assert.Equal(1, result.Data[0].Id);
        Assert.Equal(4, result.PageCount);
    }

    [Fact]
    public void Page_and_pageSize_select_the_right_slice()
    {
        var result = Sample.Many(100).ToInanduGrid(InanduGridOptions.For(new InanduGridRequest { Page = 3, PageSize = 10 }));

        Assert.Equal(3, result.Page);
        Assert.Equal(10, result.Data.Count);
        Assert.Equal(21, result.Data.First().Id);
        Assert.Equal(30, result.Data.Last().Id);
        Assert.Equal(100, result.Total);
    }

    [Fact]
    public void Offset_and_limit_win_over_page_and_pageSize()
    {
        var request = new InanduGridRequest { Page = 99, PageSize = 5, Offset = 40, Limit = 20 };

        var result = Sample.Many(100).ToInanduGrid(InanduGridOptions.For(request));

        Assert.Equal(20, result.Data.Count);
        Assert.Equal(41, result.Data.First().Id);
        Assert.Equal(60, result.Data.Last().Id);
        Assert.Equal(3, result.Page); // offset 40 / size 20 -> page 3
        Assert.Equal(20, result.PageSize);
    }

    [Fact]
    public void PageSize_is_clamped_to_MaxPageSize()
    {
        var result = Sample.Many(100).ToInanduGrid(
            InanduGridOptions.For(new InanduGridRequest { PageSize = 10_000 }, o => o.MaxPageSize = 50));

        Assert.Equal(50, result.PageSize);
        Assert.Equal(50, result.Data.Count);
    }

    [Fact]
    public void Page_past_the_end_returns_no_rows_but_the_real_total()
    {
        var result = Sample.Many(30).ToInanduGrid(InanduGridOptions.For(new InanduGridRequest { Page = 10, PageSize = 10 }));

        Assert.Empty(result.Data);
        Assert.Equal(30, result.Total);
        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void Total_reflects_the_filter_not_the_full_list()
    {
        var request = new InanduGridRequest { PageSize = 5 };
        request.ColumnFilters["status"] = new InanduGridColumnFilter { Values = new() { "Active" } };

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request));

        Assert.Equal(5, result.Total); // 5 Active products in the sample
        Assert.All(result.Data, p => Assert.Equal(Status.Active, p.Status));
    }
}
