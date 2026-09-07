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

    /// <summary>The number of rows that matched the filter, across every page.</summary>
    public int Total { get; set; }

    /// <summary>The 1-based page number this result represents.</summary>
    public int Page { get; set; }

    /// <summary>The page size that was applied (after clamping to <see cref="InanduGridOptions.MaxPageSize"/>).</summary>
    public int PageSize { get; set; }

    /// <summary>Total number of pages for the current filter and page size (at least 1).</summary>
    public int PageCount => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
