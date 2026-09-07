using System;
using System.Collections.Generic;
using System.Linq;
using Inandu.Grid.Extensions.Internal;

namespace Inandu.Grid.Extensions;

/// <summary>One distinct value of a column, with how many (filtered) rows carry it.</summary>
public sealed class InanduGridDistinctValue
{
    /// <summary>The value.</summary>
    public object? Value { get; set; }

    /// <summary>Row count for this value, after the request's filters.</summary>
    public int Count { get; set; }
}

/// <summary>A page of a column's distinct values — feeds a set-filter (Excel-style) checklist.</summary>
public sealed class InanduGridDistinctResult
{
    /// <summary>The field these values belong to.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>This page of distinct values.</summary>
    public IReadOnlyList<InanduGridDistinctValue> Values { get; set; } = Array.Empty<InanduGridDistinctValue>();

    /// <summary>Total number of distinct values (across every page).</summary>
    public int Total { get; set; }

    /// <summary>1-based page number.</summary>
    public int Page { get; set; }

    /// <summary>Page size applied.</summary>
    public int PageSize { get; set; }
}

/// <summary><c>ToInanduGridDistinct()</c> — a column's distinct values + counts, honouring the request's other filters.</summary>
public static class InanduGridDistinctExtensions
{
    /// <inheritdoc cref="ToInanduGridDistinct{T}(System.Linq.IQueryable{T}, string, InanduGridOptions?)"/>
    public static InanduGridDistinctResult ToInanduGridDistinct<T>(this IEnumerable<T> source, string field, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return source.AsQueryable().ToInanduGridDistinct(field, options);
    }

    /// <summary>
    /// The distinct values of <paramref name="field"/> with row counts, after applying the request's
    /// filters / free-text / advanced filter (so the checklist reflects what's actually selectable).
    /// The first <c>sort</c> criterion orders the values — <c>count</c> orders by count, anything else
    /// by the value; paged like any other result.
    /// </summary>
    public static InanduGridDistinctResult ToInanduGridDistinct<T>(this IQueryable<T> source, string field, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field must not be blank.", nameof(field));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();
        RequestGuard.Enforce(request, options);

        var filtered = InanduGridQueryableExtensions.ApplyFilters(source, options);
        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);
        var order = request.Sort.Count > 0 ? request.Sort[0] : null;

        var (groups, total) = ExpressionBuilder.GroupLevel(filtered, field, options, page, pageSize, order);

        return new InanduGridDistinctResult
        {
            Field = field,
            Values = groups.Select(g => new InanduGridDistinctValue { Value = g.Key, Count = g.Count }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }
}
