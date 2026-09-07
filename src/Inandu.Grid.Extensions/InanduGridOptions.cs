using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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
    /// When <c>true</c>, an unknown or forbidden sort/filter field throws
    /// <see cref="InanduGridRequestException"/>. When <c>false</c> (the default) it's ignored, so a
    /// stale client can't break the endpoint.
    /// </summary>
    public bool ThrowOnUnknownField { get; set; }

    /// <summary>Culture used to coerce filter operand strings to numbers / dates. Default <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    /// <summary>Add the grand total to <see cref="InanduGridResult{T}.Total"/>. Default <c>true</c>; keyset callers can turn it off to skip the <c>Count</c>.</summary>
    public bool IncludeTotal { get; set; } = true;

    /// <summary>
    /// Emit a <see cref="InanduGridResult{T}.NextCursor"/> + <see cref="InanduGridResult{T}.HasMore"/>
    /// on every sorted result so a client can page by cursor from the start. Keyset also activates
    /// automatically whenever the request carries an <c>after</c> cursor. Default <c>false</c>.
    /// </summary>
    public bool EnableKeyset { get; set; }

    /// <summary>Caps on request size / depth — a guard against abusive queries. See <see cref="OnLimitExceeded"/>.</summary>
    public InanduGridLimits Limits { get; set; } = new();

    /// <summary>What to do when a <see cref="Limits"/> cap is exceeded. Default <see cref="InanduGridLimitMode.Trim"/>.</summary>
    public InanduGridLimitMode OnLimitExceeded { get; set; } = InanduGridLimitMode.Trim;

    /// <summary>Per-column configuration, keyed by grid <c>field</c> (case-insensitive). Populated by <see cref="Column"/>.</summary>
    public IDictionary<string, InanduGridColumnConfig> Columns { get; }
        = new Dictionary<string, InanduGridColumnConfig>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Get-or-create the fluent config for <paramref name="field"/>.</summary>
    public InanduGridColumnBuilder Column(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("Field must not be blank.", nameof(field));
        }

        if (!Columns.TryGetValue(field, out var config))
        {
            config = new InanduGridColumnConfig(field);
            Columns[field] = config;
        }

        return new InanduGridColumnBuilder(config);
    }

    internal InanduGridColumnConfig? FindColumn(string field)
        => Columns.TryGetValue(field, out var c) ? c : null;

    /// <summary>Resolves <paramref name="field"/> to its CLR path — <see cref="InanduGridColumnConfig.Path"/> first, then <see cref="FieldMap"/>, else unchanged.</summary>
    public string MapField(string field)
    {
        var column = FindColumn(field);
        if (column?.Path is { } path)
        {
            return path;
        }

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

    /// <summary><c>true</c> when <paramref name="op"/> is allowed on <paramref name="field"/> (no column config ⇒ any operator).</summary>
    internal bool IsFilterAllowed(string field, FilterOperator op)
        => FindColumn(field) is not { } column || column.Allows(op);

    /// <summary><c>true</c> when sorting on <paramref name="field"/> is allowed.</summary>
    internal bool IsSortAllowed(string field)
        => FindColumn(field) is not { } column || column.CanSort;

    /// <summary>The fields free-text search runs against, honouring per-column <c>Searchable(...)</c> overrides.</summary>
    internal List<string>? EffectiveSearchableFields(Func<List<string>> stringPropertyFallback)
    {
        var forcedIn = Columns.Values.Where(c => c.Searchable == true).Select(c => c.Field).ToList();
        var forcedOut = new HashSet<string>(
            Columns.Values.Where(c => c.Searchable == false).Select(c => c.Field),
            StringComparer.OrdinalIgnoreCase);

        var baseline = SearchableFields ?? (forcedIn.Count > 0 ? new List<string>() : stringPropertyFallback());
        var result = baseline.Concat(forcedIn).Where(f => !forcedOut.Contains(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return result;
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
