using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.Internal;
using Microsoft.EntityFrameworkCore;

namespace Inandu.Grid.Extensions.EntityFrameworkCore;

/// <summary>
/// Async <c>ToInanduGridAsync()</c> — runs an <c>&lt;inandu-grid serverSide&gt;</c> request against
/// EF Core with <c>CountAsync</c> + <c>ToListAsync</c>, so the database only returns one page.
/// </summary>
public static class InanduGridEntityFrameworkExtensions
{
    // ── rows ─────────────────────────────────────────────────────────────

    /// <summary>Applies the request to <paramref name="source"/> and returns one page, asynchronously.</summary>
    public static async Task<InanduGridResult<T>> ToInanduGridAsync<T>(
        this IQueryable<T> source, InanduGridOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = source.ApplyInanduGridQuery(options);
        var total = await query.FilteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await query.PagedQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        return new InanduGridResult<T>(page, total, query.Page, query.PageSize);
    }

    /// <summary>Async + an already-bound request.</summary>
    public static Task<InanduGridResult<T>> ToInanduGridAsync<T>(
        this IQueryable<T> source, InanduGridRequest request, Action<InanduGridOptions>? configure = null, CancellationToken cancellationToken = default)
        => source.ToInanduGridAsync(InanduGridOptions.For(request, configure), cancellationToken);

    /// <summary>Async + a request bound from a query string.</summary>
    public static Task<InanduGridResult<T>> ToInanduGridAsync<T>(
        this IQueryable<T> source, string? queryString, Action<InanduGridOptions>? configure = null, CancellationToken cancellationToken = default)
        => source.ToInanduGridAsync(InanduGridOptions.FromQueryString(queryString, configure), cancellationToken);

    // ── rows, projected to a DTO ─────────────────────────────────────────

    /// <summary>
    /// Applies the request, sorting/filtering on <typeparamref name="TSource"/>, then projects the
    /// page with <paramref name="selector"/> — asynchronously.
    /// </summary>
    public static async Task<InanduGridResult<TResult>> ToInanduGridAsync<TSource, TResult>(
        this IQueryable<TSource> source,
        Expression<Func<TSource, TResult>> selector,
        InanduGridOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var query = source.ApplyInanduGridQuery(selector, options);
        var total = await query.FilteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await query.PagedQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        return new InanduGridResult<TResult>(page, total, query.Page, query.PageSize);
    }

    /// <summary>Async projection + an already-bound request.</summary>
    public static Task<InanduGridResult<TResult>> ToInanduGridAsync<TSource, TResult>(
        this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, InanduGridRequest request, Action<InanduGridOptions>? configure = null, CancellationToken cancellationToken = default)
        => source.ToInanduGridAsync(selector, InanduGridOptions.For(request, configure), cancellationToken);

    /// <summary>Async projection + a request bound from a query string.</summary>
    public static Task<InanduGridResult<TResult>> ToInanduGridAsync<TSource, TResult>(
        this IQueryable<TSource> source, Expression<Func<TSource, TResult>> selector, string? queryString, Action<InanduGridOptions>? configure = null, CancellationToken cancellationToken = default)
        => source.ToInanduGridAsync(selector, InanduGridOptions.FromQueryString(queryString, configure), cancellationToken);

    // ── grouping ─────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side grouping with lazy drill-down, asynchronously — the group-level aggregation is a
    /// single <c>GROUP BY</c> query, the leaf level a normal paged query.
    /// </summary>
    public static async Task<InanduGridGroupedResult<T>> ToInanduGridGroupedAsync<T>(
        this IQueryable<T> source, InanduGridOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();
        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);

        var filtered = InanduGridQueryableExtensions.ApplyFilters(source, options);

        var drill = new List<FilterCondition>();
        for (var i = 0; i < request.GroupKeys.Count && i < request.GroupBy.Count; i++)
        {
            drill.Add(new FilterCondition(request.GroupBy[i], FilterOperator.Equal, request.GroupKeys[i]));
        }

        if (drill.Count > 0)
        {
            var drillPredicate = ExpressionBuilder.BuildPredicate<T>(drill, null, options);
            if (drillPredicate is not null)
            {
                filtered = filtered.Where(drillPredicate);
            }
        }

        var level = Math.Min(request.GroupKeys.Count, request.GroupBy.Count);

        if (request.GroupBy.Count == 0 || level >= request.GroupBy.Count)
        {
            var sorted = ExpressionBuilder.ApplySort(filtered, request.Sort, options);
            var rowTotal = await sorted.CountAsync(cancellationToken).ConfigureAwait(false);
            var rows = await InanduGridQueryableExtensions.Paginate(sorted, page, pageSize)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new InanduGridGroupedResult<T>
            {
                Rows = rows,
                Total = rowTotal,
                Page = page,
                PageSize = pageSize,
                Level = level,
                IsLeaf = true,
            };
        }

        var groupSort = request.Sort.Count > 0 ? request.Sort[0] : null;
        var (groups, groupTotal) = await GroupLevelAsync(filtered, request.GroupBy[level], options, page, pageSize, groupSort, cancellationToken)
            .ConfigureAwait(false);

        return new InanduGridGroupedResult<T>
        {
            Groups = groups,
            Total = groupTotal,
            Page = page,
            PageSize = pageSize,
            Level = level,
            IsLeaf = false,
        };
    }

    /// <summary>Async grouped + an already-bound request.</summary>
    public static Task<InanduGridGroupedResult<T>> ToInanduGridGroupedAsync<T>(
        this IQueryable<T> source, InanduGridRequest request, Action<InanduGridOptions>? configure = null, CancellationToken cancellationToken = default)
        => source.ToInanduGridGroupedAsync(InanduGridOptions.For(request, configure), cancellationToken);

    /// <summary>Async grouped + a request bound from a query string.</summary>
    public static Task<InanduGridGroupedResult<T>> ToInanduGridGroupedAsync<T>(
        this IQueryable<T> source, string? queryString, Action<InanduGridOptions>? configure = null, CancellationToken cancellationToken = default)
        => source.ToInanduGridGroupedAsync(InanduGridOptions.FromQueryString(queryString, configure), cancellationToken);

    private static Task<(List<InanduGridGroup> Groups, int Total)> GroupLevelAsync<T>(
        IQueryable<T> source, string field, InanduGridOptions options, int page, int pageSize, InanduGridSort? groupSort, CancellationToken ct)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var resolved = PropertyResolver.Resolve(param, typeof(T), options.MapField(field))
            ?? throw new ArgumentException($"Unknown group field '{field}' on {typeof(T).Name}.", nameof(field));

        var (access, keyType) = resolved;
        var keySelector = Expression.Lambda(access, param);

        var core = typeof(InanduGridEntityFrameworkExtensions)
            .GetMethod(nameof(GroupLevelAsyncCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T), keyType);

        return (Task<(List<InanduGridGroup>, int)>)core.Invoke(
            null,
            new object?[] { source, keySelector, field, page, pageSize, groupSort, ct })!;
    }

    private static async Task<(List<InanduGridGroup> Groups, int Total)> GroupLevelAsyncCore<T, TKey>(
        IQueryable<T> source, Expression<Func<T, TKey>> keySelector, string field, int page, int pageSize, InanduGridSort? groupSort, CancellationToken ct)
    {
        var grouped = source.GroupBy(keySelector).Select(g => new GroupCount<TKey> { Key = g.Key, Count = g.Count() });

        var total = await grouped.CountAsync(ct).ConfigureAwait(false);

        var byCount = groupSort is not null && string.Equals(groupSort.Field, "count", StringComparison.OrdinalIgnoreCase);
        var desc = groupSort?.IsDescending == true;

        var ordered = (byCount, desc) switch
        {
            (true, true) => grouped.OrderByDescending(x => x.Count),
            (true, false) => grouped.OrderBy(x => x.Count),
            (false, true) => grouped.OrderByDescending(x => x.Key),
            _ => grouped.OrderBy(x => x.Key),
        };

        var skip = Math.Max(0, page - 1) * (long)pageSize;
        var pageRows = await ordered.Skip((int)Math.Min(skip, int.MaxValue)).Take(pageSize)
            .ToListAsync(ct).ConfigureAwait(false);

        var groups = pageRows
            .Select(x => new InanduGridGroup { Field = field, Key = x.Key, Count = x.Count })
            .ToList();

        return (groups, total);
    }
}
