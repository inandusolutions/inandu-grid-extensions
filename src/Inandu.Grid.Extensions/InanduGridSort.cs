using System;
using System.Text.Json.Serialization;
using Inandu.Grid.Extensions.Json;

namespace Inandu.Grid.Extensions;

/// <summary>Sort order for a single field. Mirrors the core grid's <c>InanduGridSortCriterion.direction</c>.</summary>
public enum SortDirection
{
    /// <summary>Ascending (<c>"asc"</c>).</summary>
    Ascending = 0,

    /// <summary>Descending (<c>"desc"</c>).</summary>
    Descending = 1,
}

/// <summary>
/// One entry of the grid's multi-column sort, in priority order (index 0 sorts first). Corresponds
/// to a single <c>{ field, direction }</c> of the payload emitted by <c>(sortChange)</c>.
/// </summary>
[JsonConverter(typeof(InanduGridSortJsonConverter))]
public sealed class InanduGridSort
{
    /// <summary>Creates an empty criterion. Prefer the parameterized constructor.</summary>
    public InanduGridSort()
    {
        Field = string.Empty;
    }

    /// <summary>Creates a criterion for <paramref name="field"/> in the given <paramref name="direction"/>.</summary>
    public InanduGridSort(string field, SortDirection direction = SortDirection.Ascending)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Direction = direction;
    }

    /// <summary>The grid column's <c>field</c>. May be a dotted path (<c>"customer.name"</c>).</summary>
    public string Field { get; set; }

    /// <summary>Ascending or descending.</summary>
    public SortDirection Direction { get; set; }

    /// <summary><c>true</c> when <see cref="Direction"/> is <see cref="SortDirection.Descending"/>.</summary>
    public bool IsDescending => Direction == SortDirection.Descending;

    /// <summary>
    /// Parses one token of a REST <c>sort</c> parameter: a leading <c>'-'</c> means descending
    /// (e.g. <c>"-createdOn"</c>), an optional leading <c>'+'</c> is ignored, everything else is
    /// ascending. Returns <c>null</c> for a blank token.
    /// </summary>
    public static InanduGridSort? ParseToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var trimmed = token.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var direction = SortDirection.Ascending;
        if (trimmed[0] == '-')
        {
            direction = SortDirection.Descending;
            trimmed = trimmed.Substring(1);
        }
        else if (trimmed[0] == '+')
        {
            trimmed = trimmed.Substring(1);
        }

        return trimmed.Length == 0 ? null : new InanduGridSort(trimmed, direction);
    }

    /// <inheritdoc />
    public override string ToString() => IsDescending ? "-" + Field : Field;
}
