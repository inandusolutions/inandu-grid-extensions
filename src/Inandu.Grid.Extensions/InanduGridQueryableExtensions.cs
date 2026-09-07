using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Inandu.Grid.Extensions.Internal;

namespace Inandu.Grid.Extensions;

/// <summary>
/// <c>ToInanduGrid()</c> — apply an <c>&lt;inandu-grid serverSide&gt;</c> request (multi-column sort,
/// free-text search, per-column filters, an advanced-filter tree, paging or a keyset cursor, and
/// optional aggregate totals) to a sequence and get back just the current page plus the total match
/// count, ready to serialize as the grid's <c>[data]</c> / <c>[totalItems]</c>.
/// </summary>
public static class InanduGridQueryableExtensions
{
    // ── ToInanduGrid ───────────────────────────────────────────────────────

    /// <summary>Applies <paramref name="options"/> to an in-memory sequence and returns one page.</summary>
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
    /// returns one page. Executes synchronously; for <c>async</c> use
    /// <c>Inandu.Grid.Extensions.EntityFrameworkCore</c>'s <c>ToInanduGridAsync</c>.
    /// </summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IQueryable<T> source, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();
        RequestGuard.Enforce(request, options);

        var filtered = ApplyFilters(source, options);
        var sorted = ExpressionBuilder.ApplySort(filtered, request.Sort, options);

        var total = options.IncludeTotal ? filtered.Count() : -1;

        var plan = Paginator.Plan(sorted, request, options);
        var (rows, hasMore, nextCursor) = Paginator.Finish(plan.Query.ToList(), plan, options);

        return new InanduGridResult<T>(rows, total, plan.Page, plan.PageSize)
        {
            HasMore = hasMore,
            NextCursor = nextCursor,
            Aggregations = ComputeAggregations(filtered, request, options),
        };
    }

    /// <summary>Convenience overload: apply an already-bound <paramref name="request"/> with optional extra configuration.</summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IEnumerable<T> source, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.For(request, configure));

    /// <inheritdoc cref="ToInanduGrid{T}(IEnumerable{T}, InanduGridRequest, Action{InanduGridOptions}?)"/>
    public static InanduGridResult<T> ToInanduGrid<T>(this IQueryable<T> source, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.For(request, configure));

    /// <summary>Convenience overload: bind the request from a raw query string.</summary>
    public static InanduGridResult<T> ToInanduGrid<T>(this IEnumerable<T> source, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.FromQueryString(queryString, configure));

    /// <inheritdoc cref="ToInanduGrid{T}(IEnumerable{T}, string?, Action{InanduGridOptions}?)"/>
    public static InanduGridResult<T> ToInanduGrid<T>(this IQueryable<T> source, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(InanduGridOptions.FromQueryString(queryString, configure));

    // ── ToInanduGrid with projection ─────────────────────────────────────

    /// <summary>
    /// Applies the request to <paramref name="source"/>, then projects the page with
    /// <paramref name="selector"/>. Sorting and filtering resolve against <typeparamref name="TSource"/>
    /// (the entity). With offset paging the projection is part of the SQL; with keyset paging the
    /// entity page is materialised first, then projected in memory (the sort keys must survive).
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
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (selector is null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();
        RequestGuard.Enforce(request, options);

        var filtered = ApplyFilters(source, options);
        var sorted = ExpressionBuilder.ApplySort(filtered, request.Sort, options);
        var total = options.IncludeTotal ? filtered.Count() : -1;
        var aggregations = ComputeAggregations(filtered, request, options);

        var plan = Paginator.Plan(sorted, request, options);

        List<TResult> rows;
        bool? hasMore;
        string? nextCursor;

        if (plan.Keyset)
        {
            // materialise the entity page (sort keys intact), then project in memory
            var entityRows = plan.Query.ToList();
            var finished = Paginator.Finish(entityRows, plan, options);
            var compiled = selector.Compile();
            rows = finished.Rows.Select(compiled).ToList();
            hasMore = finished.HasMore;
            nextCursor = finished.NextCursor;
        }
        else
        {
            rows = plan.Query.Select(selector).ToList();
            hasMore = null;
            nextCursor = null;
        }

        return new InanduGridResult<TResult>(rows, total, plan.Page, plan.PageSize)
        {
            HasMore = hasMore,
            NextCursor = nextCursor,
            Aggregations = aggregations,
        };
    }

    /// <summary>Projection + an already-bound request.</summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(selector, InanduGridOptions.For(request, configure));

    /// <summary>Projection + a request bound from a query string.</summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(selector, InanduGridOptions.FromQueryString(queryString, configure));

    /// <summary>Projection (in-memory) + an already-bound request.</summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IEnumerable<TSource> source, Expression<Func<TSource, TResult>> selector, InanduGridRequest request, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(selector, InanduGridOptions.For(request, configure));

    /// <summary>Projection (in-memory) + a request bound from a query string.</summary>
    public static InanduGridResult<TResult> ToInanduGrid<TSource, TResult>(
        this IEnumerable<TSource> source, Expression<Func<TSource, TResult>> selector, string? queryString, Action<InanduGridOptions>? configure = null)
        => source.ToInanduGrid(selector, InanduGridOptions.FromQueryString(queryString, configure));

    // ── ApplyInanduGridQuery — compose, don't execute (offset paging only) ─

    /// <summary>
    /// Composes sorting + filtering + <b>offset</b> paging onto <paramref name="source"/> without
    /// executing. Returns <c>FilteredQuery</c> (for your own <c>CountAsync</c>) and <c>PagedQuery</c>
    /// (for your own <c>ToListAsync</c>). Keyset paging / aggregations are not applied here — use
    /// <see cref="ToInanduGrid{T}(IQueryable{T}, InanduGridOptions?)"/> or the EF Core package.
    /// </summary>
    public static InanduGridQuery<T> ApplyInanduGridQuery<T>(this IQueryable<T> source, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();
        RequestGuard.Enforce(request, options);

        var filtered = ApplyFilterAndSort(source, options);
        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);

        return new InanduGridQuery<T>(filtered, Paginate(filtered, page, pageSize), page, pageSize);
    }

    /// <summary>Projected variant of <see cref="ApplyInanduGridQuery{T}(IQueryable{T}, InanduGridOptions?)"/>.</summary>
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
        var request = options.Request ?? new InanduGridRequest();
        RequestGuard.Enforce(request, options);

        var projected = ApplyFilterAndSort(source, options).Select(selector);
        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);

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

    internal static IReadOnlyDictionary<string, object?>? ComputeAggregations<T>(IQueryable<T> filtered, InanduGridRequest request, InanduGridOptions options)
    {
        if (request.Aggregations.Count == 0)
        {
            return null;
        }

        var plans = AggregateBuilder.Plan<T>(request.Aggregations, options);
        return plans.Count == 0 ? null : AggregateBuilder.Compute(filtered, plans);
    }
}
