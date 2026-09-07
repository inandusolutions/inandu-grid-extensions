using System;
using System.Collections.Generic;
using System.Linq;
using Inandu.Grid.Extensions.Internal;

namespace Inandu.Grid.Extensions;

/// <summary>
/// <c>ToInanduGrid()</c> — apply an <c>&lt;inandu-grid serverSide&gt;</c> request (multi-column sort,
/// free-text search, per-column filters and paging) to a sequence and get back just the current
/// page plus the total match count, ready to serialize as the grid's <c>[data]</c> / <c>[totalItems]</c>.
/// </summary>
public static class InanduGridQueryableExtensions
{
    /// <summary>
    /// Applies <paramref name="options"/> (<see cref="InanduGridOptions.Request"/> + configuration) to
    /// an in-memory sequence and returns one page.
    /// </summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="source">The full sequence.</param>
    /// <param name="options">The request and configuration. When <c>null</c>, an empty request (page 1, default size) is used.</param>
    public static InanduGridResult<T> ToInanduGrid<T>(this IEnumerable<T> source, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return ToInanduGrid(source.AsQueryable(), options);
    }

    /// <summary>
    /// Applies <paramref name="options"/> to an <see cref="IQueryable{T}"/> (EF Core, etc.) and
    /// returns one page. Executes synchronously with <c>Count()</c> + <c>ToList()</c>; for
    /// <c>async</c> use <see cref="ApplyInanduGridQuery{T}(IQueryable{T}, InanduGridOptions?)"/>.
    /// </summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IQueryable<T> source, InanduGridOptions? options = null)
    {
        var query = ApplyInanduGridQuery(source, options);
        var total = query.FilteredQuery.Count();
        var page = query.PagedQuery.ToList();
        return new InanduGridResult<T>(page, total, query.Page, query.PageSize);
    }

    /// <summary>Convenience overload: apply an already-bound <paramref name="request"/> with optional extra configuration.</summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IEnumerable<T> source, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.For(request, configure));

    /// <summary>Convenience overload: apply an already-bound <paramref name="request"/> with optional extra configuration.</summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IQueryable<T> source, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.For(request, configure));

    /// <summary>Convenience overload: bind the request from a raw query string.</summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IEnumerable<T> source, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.FromQueryString(queryString, configure));

    /// <summary>Convenience overload: bind the request from a raw query string.</summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IQueryable<T> source, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.FromQueryString(queryString, configure));

    /// <summary>
    /// Composes sorting + filtering + paging onto <paramref name="source"/> <b>without executing</b>.
    /// Returns the <see cref="InanduGridQuery{T}"/> holding <c>FilteredQuery</c> (for your own
    /// <c>CountAsync</c>) and <c>PagedQuery</c> (for your own <c>ToListAsync</c>).
    /// </summary>
    public static InanduGridQuery<T> ApplyInanduGridQuery<T>(this IQueryable<T> source, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();

        var filtered = source;

        var predicate = ExpressionBuilder.BuildPredicate<T>(request.AllConditions(), request.Query, options);
        if (predicate is not null)
        {
            filtered = filtered.Where(predicate);
        }

        var ordered = ExpressionBuilder.ApplySort(filtered, request.Sort, options);

        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);
        var skip = (page - 1) * (long)pageSize;
        skip = skip < 0 ? 0 : skip;

        var paged = ordered.Skip((int)Math.Min(skip, int.MaxValue)).Take(pageSize);

        return new InanduGridQuery<T>(filtered, paged, page, pageSize);
    }
}
