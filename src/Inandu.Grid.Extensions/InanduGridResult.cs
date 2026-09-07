using System;
using System.Collections.Generic;

namespace Inandu.Grid.Extensions;

/// <summary>
/// The payload to send back to an <c>&lt;inandu-grid serverSide&gt;</c>: one page of rows plus the
/// grand total. Bind <see cref="Data"/> to <c>[data]</c> and <see cref="Total"/> to
/// <c>[totalItems]</c>. Serializes to camelCase by default with
/// <c>System.Text.Json</c> (<c>data</c>, <c>total</c>, …).
/// </summary>
/// <typeparam name="T">The row type.</typeparam>
public sealed class InanduGridResult<T>
{
    /// <summary>Creates an empty result.</summary>
    public InanduGridResult()
    {
        Data = Array.Empty<T>();
    }

    /// <summary>Creates a result.</summary>
    public InanduGridResult(IReadOnlyList<T> data, int total, int page, int pageSize)
    {
        Data = data ?? Array.Empty<T>();
        Total = total;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>The rows for the requested page (after sorting + filtering).</summary>
    public IReadOnlyList<T> Data { get; set; }

    /// <summary>
    /// The number of rows that matched the filter, across every page. <c>-1</c> when it wasn't
    /// computed (keyset paging with <see cref="InanduGridOptions.IncludeTotal"/> off).
    /// </summary>
    public int Total { get; set; }

    /// <summary>The 1-based page number this result represents.</summary>
    public int Page { get; set; }

    /// <summary>The page size that was applied (after clamping to <see cref="InanduGridOptions.MaxPageSize"/>).</summary>
    public int PageSize { get; set; }

    /// <summary>Total number of pages for the current filter and page size (at least 1). <c>0</c> when <see cref="Total"/> is unknown.</summary>
    public int PageCount => Total < 0 ? 0 : PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    /// <summary>
    /// Totals requested via <c>aggregate=</c>, keyed <c>"sum:amount"</c> etc. <c>null</c> when none
    /// were asked for.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Aggregations { get; set; }

    /// <summary>
    /// Keyset cursor for the row after this page — pass it as the next request's <c>after</c>.
    /// <c>null</c> unless keyset paging was used, or when this is the last page.
    /// </summary>
    public string? NextCursor { get; set; }

    /// <summary>Whether more rows follow this page (keyset paging). <c>null</c> for offset paging (use <see cref="PageCount"/>).</summary>
    public bool? HasMore { get; set; }
}
