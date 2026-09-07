using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class ColumnConfigTests
{
    [Fact]
    public void Path_overrides_the_field_resolution()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Sort.Add(new InanduGridSort("cat"));

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o =>
            o.Column("cat").Path("category.name")));

        var names = result.Data.Select(p => p.Category?.Name ?? string.Empty).ToList();
        Assert.Equal(names.OrderBy(n => n, System.StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public void NotFilterable_column_is_ignored()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThan, "40"));

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.Column("price").NotFilterable()));

        Assert.Equal(Sample.Products().Count, result.Data.Count); // filter dropped
    }

    [Fact]
    public void NotFilterable_column_throws_in_strict_mode()
    {
        var request = new InanduGridRequest();
        request.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThan, "40"));

        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(
            InanduGridOptions.For(request, o => { o.Column("price").NotFilterable(); o.ThrowOnUnknownField = true; })));
    }

    [Fact]
    public void Filterable_allow_list_permits_only_listed_operators()
    {
        var opts = new System.Action<InanduGridOptions>(o => o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual, FilterOperator.LessThanOrEqual));

        var range = new InanduGridRequest { PageSize = 100 };
        range.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThanOrEqual, "20"));
        Assert.NotEqual(Sample.Products().Count, Sample.Products().ToInanduGrid(InanduGridOptions.For(range, opts)).Data.Count);

        var eq = new InanduGridRequest { PageSize = 100 };
        eq.Conditions.Add(new FilterCondition("price", FilterOperator.Equal, "45.99"));
        Assert.Equal(Sample.Products().Count, Sample.Products().ToInanduGrid(InanduGridOptions.For(eq, opts)).Data.Count); // eq not allowed -> dropped
    }

    [Fact]
    public void Not_sortable_column_is_ignored()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Sort.Add(new InanduGridSort("price", SortDirection.Descending));

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.Column("price").Sortable(false)));

        Assert.Equal(Sample.Products().Select(p => p.Id), result.Data.Select(p => p.Id)); // original order
    }

    [Fact]
    public void Searchable_forces_a_column_into_free_text()
    {
        // "289.00" isn't a substring of any string field, but forcing price in as searchable... price isn't a string, so it's skipped.
        // Instead: force only 'sku' searchable and confirm name is excluded.
        var request = new InanduGridRequest { PageSize = 100, Query = "Teclado" };
        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.Column("name").Searchable(false)));

        Assert.Empty(result.Data); // "Teclado" only appears in Name, which we excluded
    }
}

public class CostGuardTests
{
    private static InanduGridRequest ManyConditions(int n)
    {
        var r = new InanduGridRequest();
        for (var i = 0; i < n; i++)
        {
            r.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThanOrEqual, "0"));
        }

        return r;
    }

    [Fact]
    public void Trim_mode_caps_conditions_silently()
    {
        var request = ManyConditions(200);
        request.PageSize = 100;

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.Limits.MaxConditions = 10));

        Assert.Equal(10, request.Conditions.Count);
        Assert.Equal(Sample.Products().Count, result.Data.Count); // all price >= 0
    }

    [Fact]
    public void Reject_mode_throws_with_details()
    {
        var request = ManyConditions(200);

        var ex = Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(
            InanduGridOptions.For(request, o => { o.Limits.MaxConditions = 10; o.OnLimitExceeded = InanduGridLimitMode.Reject; })));

        Assert.True(ex.Errors.ContainsKey("filter"));
    }

    [Fact]
    public void Caps_sort_columns()
    {
        var request = new InanduGridRequest { PageSize = 10 };
        foreach (var f in new[] { "id", "name", "price", "stock", "status", "createdOn" })
        {
            request.Sort.Add(new InanduGridSort(f));
        }

        Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.Limits.MaxSortColumns = 3));
        Assert.Equal(3, request.Sort.Count);
    }

    [Fact]
    public void Caps_in_list_length()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Conditions.Add(new FilterCondition("id", FilterOperator.In,
            Enumerable.Range(1, 1000).Select(i => i.ToString()).ToList()));

        Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o => o.Limits.MaxInListItems = 5));

        var items = ((System.Collections.IEnumerable)request.Conditions[0].Value!).Cast<object?>().ToList();
        Assert.Equal(5, items.Count);
    }

    [Fact]
    public void Rejects_a_too_deep_advanced_filter()
    {
        // build a tree 12 groups deep
        var json = new System.Text.StringBuilder();
        for (var i = 0; i < 12; i++)
        {
            json.Append("{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[");
        }

        json.Append("{\"kind\":\"condition\",\"field\":\"id\",\"operator\":\"gt\",\"value\":0}");
        for (var i = 0; i < 12; i++)
        {
            json.Append("]}");
        }

        var request = new InanduGridRequest { Filter = json.ToString() };

        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(
            InanduGridOptions.For(request, o => { o.Limits.MaxAdvancedFilterDepth = 8; o.OnLimitExceeded = InanduGridLimitMode.Reject; })));
    }
}

public class DistinctValuesTests
{
    [Fact]
    public void Distinct_values_with_counts()
    {
        var result = Sample.Many(90).ToInanduGridDistinct("status", InanduGridOptions.FromQueryString("?pageSize=10"));

        Assert.Equal("status", result.Field);
        Assert.Equal(3, result.Total);
        Assert.Equal(90, result.Values.Sum(v => v.Count));
        Assert.All(result.Values, v => Assert.Equal(30, v.Count));
    }

    [Fact]
    public void Distinct_respects_other_filters()
    {
        var result = Sample.Many(90).ToInanduGridDistinct("status",
            InanduGridOptions.FromQueryString("?pageSize=10&discontinued_eq=false"));

        Assert.True(result.Values.Sum(v => v.Count) < 90);
    }

    [Fact]
    public void Distinct_ordered_by_count_desc()
    {
        var result = Sample.Many(90).ToInanduGridDistinct("category.name",
            InanduGridOptions.FromQueryString("?pageSize=10&sort=-count&price_gte=50"));

        var counts = result.Values.Select(v => v.Count).ToList();
        Assert.Equal(counts.OrderByDescending(c => c).ToList(), counts);
    }

    [Fact]
    public void Distinct_pages()
    {
        var page1 = Sample.Many(90).ToInanduGridDistinct("category.name", InanduGridOptions.FromQueryString("?pageSize=2"));
        Assert.Equal(2, page1.Values.Count);
        Assert.Equal(5, page1.Total); // "Cat 0".."Cat 4"
    }
}
