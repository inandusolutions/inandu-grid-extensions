using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>Edge cases for <c>AllowUnconfiguredFields = false</c>.</summary>
public class DenyByDefaultEdgeCaseTests
{
    private sealed record ProductDto(int Id, string Name);

    private static void Locked(InanduGridOptions o)
    {
        o.AllowUnconfiguredFields = false;
        o.Column("name").Searchable();
        o.Column("status");
        o.Column("cat").Path("category.name");
    }

    private static void Strict(InanduGridOptions o)
    {
        Locked(o);
        o.ThrowOnUnknownField = true;
    }

    // ── a Path-mapped column doesn't open its underlying path ──────────────

    [Fact]
    public void Configured_alias_works_but_the_raw_path_behind_it_stays_closed()
    {
        var viaAlias = Sample.Products().ToInanduGrid("?cat_eq=Cables&pageSize=100", Locked);
        var viaRawPath = Sample.Products().ToInanduGrid("?category.name_eq=Cables&pageSize=100", Locked);

        Assert.Equal(1, viaAlias.Total);
        Assert.Equal(Sample.Products().Count, viaRawPath.Total);
    }

    [Fact]
    public void Distinct_and_groupBy_on_the_raw_path_stay_closed()
    {
        var distinct = Sample.Products().ToInanduGridDistinct("category.name", InanduGridOptions.FromQueryString("?", Locked));
        var grouped = Sample.Products().ToInanduGridGrouped(InanduGridOptions.FromQueryString("?groupBy=category.name", Locked));
        var viaAlias = Sample.Products().ToInanduGridDistinct("cat", InanduGridOptions.FromQueryString("?pageSize=100", Locked));

        Assert.Empty(distinct.Values);
        Assert.True(grouped.IsLeaf);
        Assert.Equal(Sample.Products().Count, viaAlias.Values.Sum(v => v.Count));
    }

    // ── what counts as "configured" ─────────────────────────────────────────

    [Fact]
    public void A_FieldMap_entry_alone_does_not_count_as_configured()
    {
        var result = Sample.Products().ToInanduGrid("?customer_eq=Cables&pageSize=100", o =>
        {
            o.AllowUnconfiguredFields = false;
            o.FieldMap = new Dictionary<string, string> { ["customer"] = "category.name" };
        });

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Configured_names_are_matched_case_insensitively()
    {
        var result = Sample.Products().ToInanduGrid("?STATUS_eq=Draft&pageSize=100", Locked);

        Assert.Equal(Sample.Products().Count(p => p.Status == Status.Draft), result.Total);
    }

    [Fact]
    public void A_configured_but_NotFilterable_column_stays_closed()
    {
        var result = Sample.Products().ToInanduGrid("?status_eq=Draft&pageSize=100", o =>
        {
            Locked(o);
            o.Column("status").NotFilterable();
        });

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Operator_restrictions_still_apply_to_configured_columns()
    {
        void Opts(InanduGridOptions o)
        {
            o.AllowUnconfiguredFields = false;
            o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual);
        }

        var allowed = Sample.Products().ToInanduGrid("?price_gte=100&pageSize=100", Opts);
        var blocked = Sample.Products().ToInanduGrid("?price_lt=100&pageSize=100", Opts);

        Assert.Equal(Sample.Products().Count(p => p.Price >= 100), allowed.Total);
        Assert.Equal(Sample.Products().Count, blocked.Total);
    }

    // ── every surface rejects in strict mode ────────────────────────────────

    [Theory]
    [InlineData("?sku_eq=KB-01")]
    [InlineData("?sort=sku")]
    [InlineData("?aggregate=max:stock")]
    [InlineData("?category.name_eq=Cables")]
    public void Strict_mode_rejects_every_unconfigured_surface(string qs)
    {
        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(qs, Strict));
    }

    [Fact]
    public void Strict_mode_rejects_an_unconfigured_advanced_filter_field()
    {
        var qs = "?filter=" + System.Uri.EscapeDataString(
            "{\"kind\":\"group\",\"combinator\":\"and\",\"children\":[{\"kind\":\"condition\",\"field\":\"sku\",\"operator\":\"eq\",\"value\":\"KB-01\"}]}");

        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(qs, Strict));
    }

    [Fact]
    public void Strict_mode_rejects_unconfigured_groupBy_and_distinct()
    {
        Assert.Throws<InanduGridRequestException>(() =>
            Sample.Products().ToInanduGridGrouped(InanduGridOptions.FromQueryString("?groupBy=sku", Strict)));
        Assert.Throws<InanduGridRequestException>(() =>
            Sample.Products().ToInanduGridDistinct("sku", InanduGridOptions.FromQueryString("?", Strict)));
    }

    [Fact]
    public void Strict_mode_accepts_a_fully_configured_request()
    {
        var result = Sample.Products().ToInanduGrid("?status_eq=Active&sort=-name&q=alpha&aggregate=count:*&pageSize=100", Strict);

        Assert.True(result.Total > 0);
        Assert.Equal(result.Total, result.Aggregations!["count:*"]);
    }

    // ── grouping, search, aggregates ────────────────────────────────────────

    [Fact]
    public void Drill_down_on_a_configured_column_works()
    {
        var result = Sample.Products().ToInanduGridGrouped(
            InanduGridOptions.FromQueryString("?groupBy=status&groupKeys=Active&pageSize=100", Locked));

        Assert.True(result.IsLeaf);
        Assert.All(result.Rows!, p => Assert.Equal(Status.Active, p.Status));
    }

    [Fact]
    public void Explicit_SearchableFields_are_still_honoured()
    {
        var result = Sample.Products().ToInanduGrid("?q=KB-01&pageSize=100", o =>
        {
            o.AllowUnconfiguredFields = false;
            o.SearchableFields = new List<string> { "sku" };
        });

        Assert.Equal(1, result.Total);
    }

    [Fact]
    public void Searchable_false_still_removes_a_column_from_search()
    {
        var result = Sample.Products().ToInanduGrid("?q=keyboard&pageSize=100", o =>
        {
            o.AllowUnconfiguredFields = false;
            o.SearchableFields = new List<string> { "name" };
            o.Column("name").Searchable(false);
        });

        Assert.Equal(Sample.Products().Count, result.Total); // no searchable field left => q ignored
    }

    [Fact]
    public void Count_star_is_always_allowed()
    {
        var result = Sample.Products().ToInanduGrid("?aggregate=count:*", Strict);

        Assert.Equal(Sample.Products().Count, result.Aggregations!["count:*"]);
    }

    // ── the DTO projection doesn't widen or narrow anything ────────────────

    [Fact]
    public void Projection_overload_denies_entity_fields_the_dto_leaves_out()
    {
        var result = Sample.Products().ToInanduGrid(
            p => new ProductDto(p.Id, p.Name), "?sku_startsWith=KB&pageSize=100", Locked);

        Assert.Equal(Sample.Products().Count, result.Total);
    }

    [Fact]
    public void Projection_overload_is_permissive_by_default()
    {
        // Documents the risk the option exists for: the DTO has no Sku, yet the filter applies.
        var result = Sample.Products().ToInanduGrid(
            p => new ProductDto(p.Id, p.Name), "?sku_startsWith=KB&pageSize=100");

        Assert.Equal(1, result.Total);
    }
}
