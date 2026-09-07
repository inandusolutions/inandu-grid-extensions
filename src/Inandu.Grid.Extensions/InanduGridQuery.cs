using System.Linq;

namespace Inandu.Grid.Extensions;

/// <summary>
/// The composed (but not yet executed) queryables produced by
/// <see cref="InanduGridQueryableExtensions.ApplyInanduGridQuery{T}(System.Linq.IQueryable{T}, InanduGridOptions?)"/>.
/// Use this when you need <c>await</c> — call your own <c>CountAsync()</c> on
/// <see cref="FilteredQuery"/> and <c>ToListAsync()</c> on <see cref="PagedQuery"/> (EF Core), then
/// assemble an <see cref="InanduGridResult{T}"/> yourself.
/// </summary>
/// <typeparam name="T">The row type.</typeparam>
public sealed class InanduGridQuery<T>
{
    internal InanduGridQuery(IQueryable<T> filteredQuery, IQueryable<T> pagedQuery, int page, int pageSize)
    {
        FilteredQuery = filteredQuery;
        PagedQuery = pagedQuery;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>The source with sorting + filtering applied, but no paging — count this for <see cref="InanduGridResult{T}.Total"/>.</summary>
    public IQueryable<T> FilteredQuery { get; }

    /// <summary>The source with sorting + filtering + <c>Skip</c>/<c>Take</c> applied — materialize this for the page.</summary>
    public IQueryable<T> PagedQuery { get; }

    /// <summary>The resolved 1-based page number.</summary>
    public int Page { get; }

    /// <summary>The resolved page size (after clamping).</summary>
    public int PageSize { get; }

    /// <summary>Builds the response DTO from an already-materialized page and a total you counted yourself.</summary>
    public InanduGridResult<T> ToResult(System.Collections.Generic.IReadOnlyList<T> pageRows, int total)
        => new(pageRows, total, Page, PageSize);
}
