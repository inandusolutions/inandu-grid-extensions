using System;
using System.Collections.Generic;
using System.Linq;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>
/// Turns a filtered+sorted query into "the query to materialise" plus enough metadata to finish the
/// result — shared by the sync and EF-async entry points. Handles offset paging and keyset (seek)
/// paging, and building the next cursor.
/// </summary>
internal static class Paginator
{
    internal sealed class PagePlan<T>
    {
        public required IQueryable<T> Query { get; init; }
        public required int Page { get; init; }
        public required int PageSize { get; init; }
        public bool Keyset { get; init; }
        public IReadOnlyList<InanduGridSort> Sorts { get; init; } = Array.Empty<InanduGridSort>();
    }

    public static PagePlan<T> Plan<T>(IQueryable<T> sorted, InanduGridRequest request, InanduGridOptions options)
    {
        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);
        var sorts = request.Sort as IReadOnlyList<InanduGridSort> ?? request.Sort.ToList();

        var wantsKeyset = sorts.Count > 0 && (request.After is not null || options.EnableKeyset);
        if (!wantsKeyset)
        {
            return new PagePlan<T> { Query = InanduGridQueryableExtensions.Paginate(sorted, page, pageSize), Page = page, PageSize = pageSize };
        }

        var seek = request.After is null
            ? null
            : (KeysetCursor.Decode(request.After) is { Count: > 0 } values
                ? ExpressionBuilder.BuildSeekPredicate<T>(sorts, values, options)
                : null);

        // seek unusable (first page, unparseable cursor, null key, non-orderable type) -> offset,
        // but still keyset-shaped so the caller keeps a cursor to follow. Over-fetch one row either
        // way so Finish can report HasMore.
        var query = seek is null
            ? OffsetOverfetch(sorted, page, pageSize)
            : sorted.Where(seek).Take(pageSize + 1);

        return new PagePlan<T> { Query = query, Page = page, PageSize = pageSize, Keyset = true, Sorts = sorts };
    }

    private static IQueryable<T> OffsetOverfetch<T>(IQueryable<T> sorted, int page, int pageSize)
    {
        var skip = (long)(page - 1) * pageSize;
        if (skip < 0)
        {
            skip = 0;
        }

        return sorted.Skip((int)Math.Min(skip, int.MaxValue)).Take(pageSize + 1);
    }

    /// <summary>Trims a keyset over-fetch to the page and builds the next cursor / HasMore.</summary>
    public static (List<T> Rows, bool? HasMore, string? NextCursor) Finish<T>(
        List<T> fetched, PagePlan<T> plan, InanduGridOptions options)
    {
        if (!plan.Keyset)
        {
            return (fetched, null, null);
        }

        // over-fetch only happens on the seek path; the offset fallback returns exactly pageSize
        var hasMore = fetched.Count > plan.PageSize;
        var rows = hasMore ? fetched.Take(plan.PageSize).ToList() : fetched;

        string? nextCursor = null;
        if (rows.Count > 0)
        {
            var reader = ExpressionBuilder.BuildKeyReader<T>(plan.Sorts, options);
            if (reader is not null)
            {
                nextCursor = KeysetCursor.Encode(reader(rows[^1]));
            }
        }

        return (rows, hasMore, nextCursor);
    }
}
