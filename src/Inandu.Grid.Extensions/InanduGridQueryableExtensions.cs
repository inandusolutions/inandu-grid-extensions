using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Inandu.Grid.Extensions.Internal;

namespace Inandu.Grid.Extensions;

/// <summary>
/// <c>ToInanduGrid()</c> — apply an <c>&lt;inandu-grid serverSide&gt;</c> request (multi-column sort,
/// free-text search, per-column filters and paging) to a sequence and get back just the current
/// page plus the total match count, ready to serialize as the grid's <c>[data]</c> / <c>[totalItems]</c>.
/// </summary>
public static class InanduGridQueryableExtensions
{
    // ── ToInanduGrid ───────────────────────────────────────────────────────

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
    /// <c>async</c> use <c>Inandu.Grid.Extensions.EntityFrameworkCore</c>'s <c>ToInanduGridAsync</c>
    /// or <see cref="ApplyInanduGridQuery{T}(IQueryable{T}, InanduGridOptions?)"/>.
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

    // ── ToInanduGrid with projection — filter/sort on the entity, return a DTO ──

    /// <summary>
    /// Applies the request to <paramref name="source"/>, then projects the page with
    /// <paramref name="selector"/>. Sorting and filtering still resolve against
    /// <typeparamref name="TSource"/> (the entity), so the DTO never has to carry the filterable
    /// columns. Over EF Core the projection is part of the SQL, so only the DTO's columns are read.
    /// </summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IEnumerable<TSource> source,
        Expression<Func<TSource, TResult>> selector,
        InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return source.AsQueryable().ToInanduGrid(selector, options);
    }

    /// <inheritdoc cref="ToInanduGrid{TSource, TResult}(IEnumerable{TSource}, Expression{Func{TSource, TResult}}, InanduGridOptions?)"/>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TResult>> selector,
        InanduGridOptions? options = null)
    {
        var query = ApplyInanduGridQuery(source, selector, options);
        var total = query.FilteredQuery.Count();
        var page = query.PagedQuery.ToList();
        return new InanduGridResult<TResult>(page, total, query.Page, query.PageSize);
    }

    /// <summary>Projection + an already-bound request.</summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(selector, InanduGridOptions.For(request, configure));

    /// <summary>Projection + a request bound from a query string.</summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(selector, InanduGridOptions.FromQueryString(queryString, configure));

    // ── ApplyInanduGridQuery — compose, don't execute ────────────────────

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
        var filtered = ApplyFilterAndSort(source, options);
        var (page, pageSize) = (options.Request ?? new InanduGridRequest())
            .ResolvePaging(options.DefaultPageSize, options.MaxPageSize);

        return new InanduGridQuery<T>(filtered, Paginate(filtered, page, pageSize), page, pageSize);
    }

    /// <summary>
    /// Composes filtering + sorting on <typeparamref name="TSource"/>, then <c>Select</c>s with
    /// <paramref name="selector"/>, then pages — without executing. <c>FilteredQuery</c> and
    /// <c>PagedQuery</c> are the projected <see cref="IQueryable{T}"/> of <typeparamref name="TResult"/>.
    /// </summary>
    public static InanduGridQuery<TResult> ApplyInanduGridQuery<TSource, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TResult>> selector,
        InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        options ??= new InanduGridOptions();
        var projected = ApplyFilterAndSort(source, options).Select(selector);
        var (page, pageSize) = (options.Request ?? new InanduGridRequest())
            .ResolvePaging(options.DefaultPageSize, options.MaxPageSize);

        return new InanduGridQuery<TResult>(projected, Paginate(projected, page, pageSize), page, pageSize);
    }

    // ── internals shared with the grouping / EF Core paths ────────────────

    /// <summary>Applies the flat conditions, free-text search and advanced-filter tree — no sorting.</summary>
    internal static IQueryable<T> ApplyFilters<T>(IQueryable<T> source, InanduGridOptions options)
    {
        var request = options.Request ?? new InanduGridRequest();
        var filtered = source;

        var predicate = ExpressionBuilder.BuildPredicate<T>(request.AllConditions(), request.Query, options);
        if (predicate is not null)
        {
            filtered = filtered.Where(predicate);
        }

        var advanced = ExpressionBuilder.BuildAdvancedPredicate<T>(request.AdvancedFilter, options);
        if (advanced is not null)
        {
            filtered = filtered.Where(advanced);
        }

        return filtered;
    }

    internal static IQueryable<T> ApplyFilterAndSort<T>(IQueryable<T> source, InanduGridOptions options)
        => ExpressionBuilder.ApplySort(ApplyFilters(source, options), (options.Request ?? new InanduGridRequest()).Sort, options);

    internal static IQueryable<T> Paginate<T>(IQueryable<T> source, int page, int pageSize)
    {
        var skip = (page - 1) * (long)pageSize;
        if (skip < 0)
        {
            skip = 0;
        }

        return source.Skip((int)Math.Min(skip, int.MaxValue)).Take(pageSize);
    }
}
