using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Inandu.Grid.Extensions.Internal;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

public class RequestParsingEdgeCases
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("&&&")]
    [InlineData("?=&=&")]
    public void Blank_or_degenerate_query_strings_yield_an_empty_request(string? qs)
    {
        var r = InanduGridRequest.Parse(qs);
        Assert.Null(r.Page);
        Assert.Empty(r.Sort);
        Assert.Empty(r.Conditions);
        Assert.Null(r.Query);
    }

    [Fact]
    public void Non_numeric_page_is_ignored()
    {
        var r = InanduGridRequest.Parse("?page=abc&pageSize=-x");
        Assert.Null(r.Page);
        Assert.Null(r.PageSize);
    }

    [Fact]
    public void Leading_plus_in_a_sort_token_means_ascending()
    {
        var r = InanduGridRequest.Parse("?sort=%2Bname,-age");
        Assert.Equal(SortDirection.Ascending, r.Sort[0].Direction);
        Assert.Equal("name", r.Sort[0].Field);
        Assert.Equal(SortDirection.Descending, r.Sort[1].Direction);
    }

    [Fact]
    public void Bare_dash_or_plus_sort_token_is_dropped()
    {
        var r = InanduGridRequest.Parse("?sort=-,%2B,name");
        Assert.Single(r.Sort);
        Assert.Equal("name", r.Sort[0].Field);
    }

    [Fact]
    public void Operator_aliases_ne_ge_le_are_accepted()
    {
        var r = InanduGridRequest.Parse("?a_ne=1&b_ge=2&c_le=3");
        Assert.Contains(r.Conditions, c => c.Field == "a" && c.Operator == FilterOperator.NotEqual);
        Assert.Contains(r.Conditions, c => c.Field == "b" && c.Operator == FilterOperator.GreaterThanOrEqual);
        Assert.Contains(r.Conditions, c => c.Field == "c" && c.Operator == FilterOperator.LessThanOrEqual);
    }

    [Fact]
    public void Field_names_with_underscores_split_on_the_last_one()
    {
        var r = InanduGridRequest.Parse("?created_on_gte=2026-01-01");
        var c = Assert.Single(r.Conditions);
        Assert.Equal("created_on", c.Field);
        Assert.Equal(FilterOperator.GreaterThanOrEqual, c.Operator);
    }

    [Fact]
    public void Empty_in_list_produces_no_condition()
    {
        var r = InanduGridRequest.Parse("?status_in=");
        Assert.Empty(r.Conditions);
    }

    [Fact]
    public void Repeated_q_keeps_the_last()
    {
        var r = InanduGridRequest.Parse("?q=first&q=second");
        Assert.Equal("second", r.Query);
    }

    [Fact]
    public void Offset_zero_maps_to_page_one()
    {
        var (page, size) = InanduGridRequest.Parse("?offset=0&limit=25").ResolvePaging(25, 500);
        Assert.Equal(1, page);
        Assert.Equal(25, size);
    }

    [Fact]
    public void Limit_zero_falls_back_to_page_pagesize()
    {
        var (page, size) = new InanduGridRequest { Offset = 100, Limit = 0, Page = 4, PageSize = 10 }.ResolvePaging(25, 500);
        Assert.Equal(4, page);
        Assert.Equal(10, size);
    }
}

public class ValueCoercionMoreTests
{
    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void Whitespace_is_trimmed_before_coercion()
    {
        Assert.True(ValueCoercion.TryCoerce("  42  ", typeof(int), Inv, out var v));
        Assert.Equal(42, v);
    }

    [Fact]
    public void Enum_by_numeric_string()
    {
        Assert.True(ValueCoercion.TryCoerce("2", typeof(Status), Inv, out var v));
        Assert.Equal(Status.Archived, v);
    }

    [Fact]
    public void Nullable_enum_null_ok()
    {
        Assert.True(ValueCoercion.TryCoerce(null, typeof(Status?), Inv, out var v));
        Assert.Null(v);
    }

    [Fact]
    public void Overflow_is_a_soft_failure()
    {
        Assert.False(ValueCoercion.TryCoerce("99999999999999999999", typeof(int), Inv, out _));
    }

    [Fact]
    public void Char_needs_exactly_one_character()
    {
        Assert.True(ValueCoercion.TryCoerce("x", typeof(char), Inv, out var ok));
        Assert.Equal('x', ok);
        Assert.False(ValueCoercion.TryCoerce("xy", typeof(char), Inv, out _));
    }

    [Fact]
    public void Boxed_value_of_the_target_type_passes_through()
    {
        Assert.True(ValueCoercion.TryCoerce(7, typeof(int?), Inv, out var v));
        Assert.Equal(7, v);
    }
}

public class PropertyResolverTests
{
    private sealed class Node
    {
        public string? Name { get; set; }
        public Node? Parent { get; set; }
        public int Value { get; set; }
    }

    private static System.Linq.Expressions.ParameterExpression P => System.Linq.Expressions.Expression.Parameter(typeof(Node), "x");

    [Fact]
    public void Resolves_case_insensitively()
    {
        Assert.True(PropertyResolver.Exists(typeof(Node), "name"));
        Assert.True(PropertyResolver.Exists(typeof(Node), "NAME"));
        Assert.True(PropertyResolver.Exists(typeof(Node), "vAlUe"));
    }

    [Fact]
    public void Resolves_a_nested_path()
    {
        Assert.True(PropertyResolver.Exists(typeof(Node), "parent.parent.name"));
    }

    [Fact]
    public void Unknown_or_malformed_paths_do_not_resolve()
    {
        Assert.False(PropertyResolver.Exists(typeof(Node), "nope"));
        Assert.False(PropertyResolver.Exists(typeof(Node), "parent..name"));
        Assert.False(PropertyResolver.Exists(typeof(Node), ""));
        Assert.False(PropertyResolver.Exists(typeof(Node), "name.nope"));
    }

    [Fact]
    public void Follows_valid_nested_members_including_bcl_types()
    {
        // x.Name.Length is a real path
        Assert.True(PropertyResolver.Exists(typeof(Node), "name.length"));
    }

    [Fact]
    public void Nested_null_path_compiles_to_a_null_safe_read()
    {
        var chain = new List<Node>
        {
            new() { Name = "root", Value = 1 },
            new() { Name = "child", Value = 2, Parent = new Node { Name = "p" } },
        };

        // filter by parent.name — the row with no Parent must not throw, just not match
        var request = new InanduGridRequest();
        request.Conditions.Add(new FilterCondition("parent.name", FilterOperator.Equal, "p"));

        var result = chain.ToInanduGrid(InanduGridOptions.For(request));
        Assert.Single(result.Data);
        Assert.Equal("child", result.Data[0].Name);
    }
}

public class ResultAndOptionsTests
{
    [Theory]
    [InlineData(0, 25, 1)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(100, 25, 4)]
    [InlineData(101, 25, 5)]
    [InlineData(10, 0, 1)]
    public void PageCount_matrix(int total, int pageSize, int expected)
    {
        Assert.Equal(expected, new InanduGridResult<int> { Total = total, PageSize = pageSize }.PageCount);
    }

    [Fact]
    public void DefaultPageSize_is_used_and_clamped_by_MaxPageSize()
    {
        var (_, size) = new InanduGridRequest().ResolvePaging(defaultPageSize: 1000, maxPageSize: 100);
        Assert.Equal(100, size);
    }

    [Fact]
    public void FieldMap_is_case_insensitive()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Sort.Add(new InanduGridSort("CatName", SortDirection.Ascending));

        var result = Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o =>
            o.FieldMap = new Dictionary<string, string> { ["catname"] = "category.name" }));

        Assert.Equal(8, result.Data.Count);
    }

    [Fact]
    public void Null_source_throws_for_every_entry_point()
    {
        IEnumerable<Product>? nil = null;
        Assert.Throws<ArgumentNullException>(() => nil!.ToInanduGrid());
        Assert.Throws<ArgumentNullException>(() => nil!.ToInanduGridGrouped());
        Assert.Throws<ArgumentNullException>(() => nil!.ToInanduGrid(p => p.Id));
    }
}

public class StringOperatorMatrixTests
{
    private static InanduGridResult<Product> Run(FilterOperator op, string value, StringComparison? cmp = null)
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.Conditions.Add(new FilterCondition("name", op, value));
        return Sample.Products().ToInanduGrid(InanduGridOptions.For(request, o =>
        {
            if (cmp is { } c) o.StringComparison = c;
        }));
    }

    [Fact]
    public void Contains_ordinal_is_case_sensitive()
    {
        Assert.Empty(Run(FilterOperator.Contains, "ALPHA", StringComparison.Ordinal).Data);
        Assert.NotEmpty(Run(FilterOperator.Contains, "Alpha", StringComparison.Ordinal).Data);
    }

    [Fact]
    public void Eq_on_string_is_exact()
    {
        Assert.Single(Run(FilterOperator.Equal, "Gamma Monitor").Data);
        Assert.Empty(Run(FilterOperator.Equal, "gamma").Data);
    }

    [Fact]
    public void Comparison_on_string_uses_lexical_order()
    {
        // names > "M" — case-insensitive by default
        var result = Run(FilterOperator.GreaterThan, "m");
        Assert.All(result.Data, p => Assert.True(string.Compare(p.Name.ToLower(), "m", StringComparison.Ordinal) > 0));
    }

    [Fact]
    public void Empty_operand_is_a_no_op()
    {
        Assert.Equal(8, Run(FilterOperator.Contains, "").Data.Count);
    }
}
