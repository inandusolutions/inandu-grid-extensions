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
