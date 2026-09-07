using System;
using System.Collections.Generic;
using System.Globalization;

namespace Inandu.Grid.Extensions;

/// <summary>
/// Everything <see cref="InanduGridQueryableExtensions.ToInanduGrid{T}(System.Collections.Generic.IEnumerable{T}, InanduGridOptions?)"/>
/// needs: the incoming <see cref="Request"/> (sort + filter + paging) and the knobs that control how
/// it's applied. Build it from a query string with <see cref="FromQueryString"/>, from a already-bound
/// <see cref="InanduGridRequest"/> with <see cref="For"/>, or by hand.
/// </summary>
public sealed class InanduGridOptions
{
    /// <summary>The request to apply. Never <c>null</c> — defaults to an empty request (page 1).</summary>
    public InanduGridRequest Request { get; set; } = new();

    /// <summary>Page size used when the request doesn't specify one. Default <c>25</c>.</summary>
    public int DefaultPageSize { get; set; } = 25;

    /// <summary>Upper bound on the page size a caller can ask for. Default <c>500</c>.</summary>
    public int MaxPageSize { get; set; } = 500;

    /// <summary>
    /// The fields the free-text <see cref="InanduGridRequest.Query"/> searches. <c>null</c> (the
    /// default) searches every public <see cref="string"/> property of the row type. An empty list
    /// disables free-text search.
    /// </summary>
    public List<string>? SearchableFields { get; set; }

    /// <summary>String comparison for <c>contains</c> / <c>startsWith</c> / <c>endsWith</c> / <c>eq</c>
    /// on strings and for the free-text search. Default <see cref="StringComparison.OrdinalIgnoreCase"/>.
    /// <para>
    /// Note: over EF Core / <see cref="System.Linq.IQueryable{T}"/>, string comparison is decided by
    /// the database collation; this setting only affects in-memory (<see cref="System.Collections.Generic.IEnumerable{T}"/>) evaluation.
    /// </para>
    /// </summary>
    public StringComparison StringComparison { get; set; } = StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Optional map from a grid <c>field</c> to a CLR property path (e.g.
    /// <c>{ ["customerName"] = "Customer.Name" }</c>). Fields not in the map are used as-is.
    /// </summary>
    public IDictionary<string, string>? FieldMap { get; set; }

    /// <summary>
    /// When <c>true</c>, an unknown sort/filter field throws <see cref="ArgumentException"/>. When
    /// <c>false</c> (the default) it's ignored, so a stale client can't break the endpoint.
    /// </summary>
    public bool ThrowOnUnknownField { get; set; }

    /// <summary>Culture used to coerce filter operand strings to numbers / dates. Default <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>Resolves <paramref name="field"/> through <see cref="FieldMap"/> (case-insensitive), else returns it unchanged.</summary>
    public string MapField(string field)
    {
        if (FieldMap is not null)
        {
            foreach (var pair in FieldMap)
            {
                if (string.Equals(pair.Key, field, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return field;
    }

    /// <summary>Options wrapping an already-bound <paramref name="request"/>, with optional extra configuration.</summary>
    public static InanduGridOptions For(InanduGridRequest request, Action<InanduGridOptions>? configure = null)
    {
        var options = new InanduGridOptions { Request = request ?? new InanduGridRequest() };
        configure?.Invoke(options);
        return options;
    }

    /// <summary>Options with <see cref="Request"/> parsed from <paramref name="queryString"/>.</summary>
    public static InanduGridOptions FromQueryString(string? queryString, Action<InanduGridOptions>? configure = null)
        => For(InanduGridRequest.Parse(queryString), configure);

    /// <summary>Options with <see cref="Request"/> parsed from a key/value collection.</summary>
    public static InanduGridOptions FromQuery(IEnumerable<KeyValuePair<string, string?>> pairs, Action<InanduGridOptions>? configure = null)
        => For(InanduGridRequest.Parse(pairs), configure);
}
