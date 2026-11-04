using System;
using System.Collections.Generic;

namespace Inandu.Grid.Extensions;

/// <summary>What to do when a request exceeds a <see cref="InanduGridLimits"/> cap.</summary>
public enum InanduGridLimitMode
{
    /// <summary>Silently trim the request down to the limit (the default — a stale/abusive client can't 500 the endpoint).</summary>
    Trim = 0,

    /// <summary>Throw <see cref="InanduGridRequestException"/>.</summary>
    Reject = 1,
}

/// <summary>Caps on how large / deep an incoming request may be — a guard against abusive or runaway queries.</summary>
public sealed class InanduGridLimits
{
    /// <summary>Maximum number of flat filter conditions (column filters + <c>{field}_{op}</c>). Default 50.</summary>
    public int MaxConditions { get; set; } = 50;

    /// <summary>Maximum number of sort columns. Default 5.</summary>
    public int MaxSortColumns { get; set; } = 5;

    /// <summary>Maximum number of nodes (groups + conditions) in the advanced-filter tree. Default 200.</summary>
    public int MaxAdvancedFilterNodes { get; set; } = 200;

    /// <summary>Maximum nesting depth of the advanced-filter tree. Default 10.</summary>
    public int MaxAdvancedFilterDepth { get; set; } = 10;

    /// <summary>Maximum number of items in a single <c>in</c> list. Default 500.</summary>
    public int MaxInListItems { get; set; } = 500;

    /// <summary>Maximum number of <c>groupBy</c> levels. Default 5.</summary>
    public int MaxGroupByLevels { get; set; } = 5;

    /// <summary>Maximum number of <c>aggregate</c> functions. Default 20.</summary>
    public int MaxAggregations { get; set; } = 20;

    /// <summary>Maximum length of the free-text <c>q</c> search. Over EF Core it becomes a <c>LIKE '%…%'</c> on every searchable column, so long text is expensive. Default 200.</summary>
    public int MaxQueryTextLength { get; set; } = 200;

    /// <summary>Maximum length of the advanced <c>filter</c> JSON. Default 20000.</summary>
    public int MaxFilterLength { get; set; } = 20_000;

    /// <summary>
    /// Maximum length of the keyset cursor (<c>after</c>), checked before it is decoded. A cursor holds the sort-key
    /// values of one row, so it is short; over the limit it is treated as invalid (trim) or rejected. Default 4096.
    /// </summary>
    public int MaxCursorLength { get; set; } = 4096;

    /// <summary>
    /// Maximum length of one filter value: a condition value, an <c>in</c> item, a column filter's text / bounds / set
    /// values. A <c>contains</c> over EF Core becomes a <c>LIKE '%…%'</c>, so a megabyte of text per condition is expensive.
    /// Over the limit the condition is dropped (trim) or the request rejected. Default 1000.
    /// </summary>
    public int MaxValueLength { get; set; } = 1000;

    /// <summary>Maximum length of a field name in a sort, filter or group-by. Default 128.</summary>
    public int MaxFieldNameLength { get; set; } = 128;

    /// <summary>Maximum length of a raw query string given to <see cref="InanduGridOptions.FromQueryString"/>, checked before it is split and parsed. Default 32768.</summary>
    public int MaxQueryStringLength { get; set; } = 32_768;
}

/// <summary>
/// Thrown when a request is rejected — an unknown / forbidden field with
/// <see cref="InanduGridOptions.ThrowOnUnknownField"/>, or a <see cref="InanduGridLimits"/> cap
/// exceeded with <see cref="InanduGridLimitMode.Reject"/>. <see cref="Errors"/> maps a request
/// location (<c>"sort"</c>, <c>"filter.price"</c>, …) to the problems found there — shaped for a
/// <c>ValidationProblemDetails</c> response.
/// </summary>
public sealed class InanduGridRequestException : Exception
{
    /// <summary>Creates the exception.</summary>
    public InanduGridRequestException(string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    /// <summary>Per-location problem messages.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    internal static InanduGridRequestException Single(string key, string message)
        => new(message, new Dictionary<string, string[]> { [key] = new[] { message } });
}
