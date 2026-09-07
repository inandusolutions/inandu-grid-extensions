using System;
using System.Collections.Generic;
using System.Linq;
using Inandu.Grid.Extensions.Internal;

namespace Inandu.Grid.Extensions;

/// <summary>One group at the current grouping level.</summary>
public sealed class InanduGridGroup
{
    /// <summary>The field this group is keyed on.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>The group's key value (the distinct value of <see cref="Field"/>).</summary>
    public object? Key { get; set; }

    /// <summary>How many rows (across every deeper level) fall under this group, after filters.</summary>
    public int Count { get; set; }
}

/// <summary>
/// The result of a grouped request — either a page of <see cref="Groups"/> (a grouping level) or a
/// page of <see cref="Rows"/> (drilled past the last <c>groupBy</c> level). Check <see cref="IsLeaf"/>.
/// </summary>
/// <typeparam name="T">The row type.</typeparam>
public sealed class InanduGridGroupedResult<T>
{
    /// <summary>The groups for this level — <c>null</c> when <see cref="IsLeaf"/>.</summary>
    public IReadOnlyList<InanduGridGroup>? Groups { get; set; }

    /// <summary>The rows for a fully drilled-down group — <c>null</c> until <see cref="IsLeaf"/>.</summary>
    public IReadOnlyList<T>? Rows { get; set; }

    /// <summary>Group count (or, for a leaf, row count) matching the filter, across every page.</summary>
    public int Total { get; set; }

    /// <summary>1-based page number.</summary>
    public int Page { get; set; }

    /// <summary>Page size applied.</summary>
    public int PageSize { get; set; }

    /// <summary>How many <c>groupBy</c> levels are already expanded (== <c>groupKeys</c> length).</summary>
    public int Level { get; set; }

    /// <summary><c>true</c> when <see cref="Rows"/> is populated (no more grouping levels below).</summary>
    public bool IsLeaf { get; set; }
}

/// <summary>
/// <c>ToInanduGridGrouped()</c> — server-side grouping with lazy drill-down for
/// <c>@inandu-solutions/grid-pro</c>'s <c>createInanduGridServerRowModel</c> grouped mode.
/// </summary>
public static class InanduGridGroupedExtensions
{
    /// <inheritdoc cref="ToInanduGridGrouped{T}(IQueryable{T}, InanduGridOptions?)"/>
    public static InanduGridGroupedResult<T> ToInanduGridGrouped<T>(this IEnumerable<T> source, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return source.AsQueryable().ToInanduGridGrouped(options);
    }

    /// <summary>
    /// Groups <paramref name="source"/> by the request's <c>groupBy</c> fields. With no
    /// <c>groupKeys</c> it returns the top-level groups (<c>{ key, count }</c>) + count; with
    /// <c>groupKeys</c> it drills in — the next level's groups, or (past the last <c>groupBy</c>) the
    /// rows of that group. Filters, free-text and the advanced filter all apply throughout.
    /// </summary>
    public static InanduGridGroupedResult<T> ToInanduGridGrouped<T>(this IQueryable<T> source, InanduGridOptions? options = null)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        options ??= new InanduGridOptions();
        var request = options.Request ?? new InanduGridRequest();
        var (page, pageSize) = request.ResolvePaging(options.DefaultPageSize, options.MaxPageSize);

        var filtered = InanduGridQueryableExtensions.ApplyFilters(source, options);

        // Drill-down: an `eq` on each already-expanded group key.
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
            var rowTotal = sorted.Count();
            var rows = InanduGridQueryableExtensions.Paginate(sorted, page, pageSize).ToList();

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
        var (groups, groupTotal) = ExpressionBuilder.GroupLevel(filtered, request.GroupBy[level], options, page, pageSize, groupSort);

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
}
