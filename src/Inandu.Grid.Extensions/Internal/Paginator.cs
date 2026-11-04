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

        /// <summary>Rows that precede this page, when known (null for an old-form cursor).</summary>
        public int? StartSkip { get; init; }
        public IReadOnlyList<InanduGridSort> Sorts { get; init; } = Array.Empty<InanduGridSort>();
    }

    public static PagePlan<T> Plan<T>(IQueryable<T> sorted, InanduGridRequest request, InanduGridOptions options)
    {
        var (page, pageSize, skip) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);
        // Only the sorts ApplySort actually applied: a field the options hide must not reach the seek or the cursor.
        var sorts = ExpressionBuilder.EffectiveSorts<T>(request.Sort, options);

        var wantsKeyset = sorts.Count > 0 && (request.After is not null || options.EnableKeyset);
        if (!wantsKeyset)
        {
            return new PagePlan<T> { Query = InanduGridQueryableExtensions.Paginate(sorted, skip, pageSize), Page = page, PageSize = pageSize };
        }

        var decoded = request.After is null ? null : KeysetCursor.Decode(request.After);
        var cursorSkip = request.After is null ? null : KeysetCursor.DecodeSkip(request.After);

        var seek = decoded is { Count: > 0 } values
            ? ExpressionBuilder.BuildSeekPredicate<T>(sorts, values, options)
            : null;

        // seek unusable (first page, unparseable cursor, null key, non-orderable type) -> offset,
        // but still keyset-shaped so the caller keeps a cursor to follow. An offset-only cursor
        // ({"s":N}, issued when a boundary key was null) resumes at its own row count instead of
        // restarting at page 1. Over-fetch one row either way so Finish can report HasMore.
        int? startSkip;
        IQueryable<T> query;
        if (seek is not null)
        {
            startSkip = cursorSkip;
            query = sorted.Where(seek).Take(pageSize + 1);
        }
        else if (cursorSkip is int resume)
        {
            startSkip = resume;
            page = (resume / pageSize) + 1;
            query = OffsetOverfetch(sorted, resume, pageSize);
        }
        else
        {
            startSkip = request.After is null ? skip : null;
            query = OffsetOverfetch(sorted, skip, pageSize);
        }

        return new PagePlan<T> { Query = query, Page = page, PageSize = pageSize, Keyset = true, Sorts = sorts, StartSkip = startSkip };
    }

    private static IQueryable<T> OffsetOverfetch<T>(IQueryable<T> sorted, int skip, int pageSize)
        => sorted.Skip(Math.Max(0, skip)).Take(pageSize + 1);

    /// <summary>Trims a keyset over-fetch to the page and builds the next cursor / HasMore.</summary>
    public static (List<T> Rows, bool? HasMore, string? NextCursor) Finish<T>(
        List<T> fetched, PagePlan<T> plan, InanduGridOptions options)
    {
        if (!plan.Keyset)
        {
            return (fetched, null, null);
        }

        // both the seek path and the offset fallback over-fetch one row, which is how HasMore is known
        var hasMore = fetched.Count > plan.PageSize;
        var rows = hasMore ? fetched.Take(plan.PageSize).ToList() : fetched;

        string? nextCursor = null;
        if (rows.Count > 0)
        {
            var reader = ExpressionBuilder.BuildKeyReader<T>(plan.Sorts, options);
            int? nextSkip = plan.StartSkip is int start ? start + rows.Count : null;
            if (reader is not null)
            {
                var keys = reader(rows[^1]);
                if (Array.IndexOf(keys, null) < 0)
                {
                    nextCursor = KeysetCursor.Encode(keys, nextSkip);
                }
                else if (nextSkip is int resume && hasMore)
                {
                    // A null boundary key can't be sought past: carry on by offset instead of
                    // handing back a cursor that would restart the sequence.
                    nextCursor = KeysetCursor.EncodeSkip(resume);
                }
            }
        }

        return (rows, hasMore, nextCursor);
    }
}
