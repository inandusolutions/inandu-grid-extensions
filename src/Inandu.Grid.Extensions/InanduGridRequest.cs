using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace Inandu.Grid.Extensions;

/// <summary>
/// The server-side request an <c>&lt;inandu-grid serverSide&gt;</c> makes: the multi-column sort,
/// the free-text box, the per-column filters, and the page (or block) to return. Bind it from JSON
/// (the body <c>@inandu-solutions/grid-pro</c> POSTs) or from a query string / key-value collection
/// with <see cref="Parse(string?)"/> / <see cref="Parse(IEnumerable{KeyValuePair{string, string?}})"/>,
/// which follow the same param contract as <c>createInanduGridDataSource</c>.
/// </summary>
/// <remarks>See <c>docs/request-contract.md</c> for the exact parameter names.</remarks>
public sealed class InanduGridRequest
{
    /// <summary>Query-string / form keys that are never treated as a column filter.</summary>
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "pagesize", "offset", "limit", "skip", "take",
        "sort", "orderby", "order",
        "q", "query", "search", "term",
        "filter", "advancedfilter",
        "groupby", "groupkeys",
        "_", "t", "_t",
    };

    /// <summary>1-based page number. Ignored when <see cref="Offset"/> / <see cref="Limit"/> are set.</summary>
    public int? Page { get; set; }

    /// <summary>Rows per page. Ignored when <see cref="Offset"/> / <see cref="Limit"/> are set.</summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Row offset for the block/windowed model (<c>createInanduGridServerRowModel</c>). When both
    /// <see cref="Offset"/> and <see cref="Limit"/> are set they win over <see cref="Page"/> /
    /// <see cref="PageSize"/>.
    /// </summary>
    public int? Offset { get; set; }

    /// <summary>Row count for the block/windowed model. See <see cref="Offset"/>.</summary>
    public int? Limit { get; set; }

    /// <summary>Multi-column sort, in priority order.</summary>
    public IList<InanduGridSort> Sort { get; set; } = new List<InanduGridSort>();

    /// <summary>The grid's free-text search box.</summary>
    public string? Query { get; set; }

    /// <summary>Per-column filter controls, keyed by the column's <c>field</c>.</summary>
    public IDictionary<string, InanduGridColumnFilter> ColumnFilters { get; set; }
        = new Dictionary<string, InanduGridColumnFilter>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extra flat conditions, e.g. from REST <c>{field}_{op}=value</c> params. These are applied in
    /// addition to <see cref="ColumnFilters"/> and combined with <c>AND</c>.
    /// </summary>
    public IList<FilterCondition> Conditions { get; set; } = new List<FilterCondition>();

    private string? _filterJson;
    private AdvancedFilterGroup? _advancedFilter;

    /// <summary>
    /// The raw JSON of the advanced filter tree — the <c>filter</c> param produced by
    /// <c>@inandu-solutions/grid-pro</c>'s <c>advancedQueryToRestParams</c>. Setting it (re)parses
    /// <see cref="AdvancedFilter"/>.
    /// </summary>
    [JsonPropertyName("filter")]
    public string? Filter
    {
        get => _filterJson;
        set
        {
            _filterJson = value;
            _advancedFilter = AdvancedFilterJson.Parse(value);
        }
    }

    /// <summary>
    /// The parsed advanced filter tree (nested AND / OR), from <see cref="Filter"/> or set directly.
    /// Applied in addition to <see cref="ColumnFilters"/> / <see cref="Conditions"/>, combined with <c>AND</c>.
    /// </summary>
    [JsonIgnore]
    public AdvancedFilterGroup? AdvancedFilter
    {
        get => _advancedFilter;
        set => _advancedFilter = value;
    }

    /// <summary>
    /// Fields to group by, outermost first — the <c>groupBy</c> param (<c>groupBy=region,category</c>).
    /// Consumed by <c>ToInanduGridGrouped</c>.
    /// </summary>
    public IList<string> GroupBy { get; set; } = new List<string>();

    /// <summary>
    /// The already-expanded group path for a drill-down request — the <c>groupKeys</c> param
    /// (<c>groupKeys=EMEA</c> or <c>groupKeys=EMEA,2026</c>). Each value is matched with <c>eq</c>
    /// against the corresponding <see cref="GroupBy"/> field.
    /// </summary>
    public IList<string> GroupKeys { get; set; } = new List<string>();

    /// <summary>
    /// Every filter, flattened: <see cref="ColumnFilters"/> expanded via
    /// <see cref="InanduGridColumnFilter.ToConditions"/> plus <see cref="Conditions"/>.
    /// </summary>
    public IEnumerable<FilterCondition> AllConditions()
    {
        foreach (var pair in ColumnFilters)
        {
            foreach (var condition in pair.Value.ToConditions(pair.Key))
            {
                yield return condition;
            }
        }

        foreach (var condition in Conditions)
        {
            yield return condition;
        }
    }

    /// <summary>The effective (1-based) page and page size, resolving <see cref="Offset"/> / <see cref="Limit"/>.</summary>
    internal (int Page, int PageSize) ResolvePaging(int defaultPageSize, int maxPageSize)
    {
        int size;
        int page;

        if (Offset is int offset && Limit is int limit && limit > 0)
        {
            size = Clamp(limit, 1, maxPageSize);
            page = offset <= 0 ? 1 : (offset / size) + 1;
            return (page, size);
        }

        size = PageSize is int ps && ps > 0 ? Clamp(ps, 1, maxPageSize) : Clamp(defaultPageSize, 1, maxPageSize);
        page = Page is int p && p > 0 ? p : 1;
        return (page, size);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

    // ── parsing ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a raw query string (with or without a leading <c>'?'</c>). Repeated keys and
    /// comma-separated <c>sort</c> tokens are both supported.
    /// </summary>
    public static InanduGridRequest Parse(string? queryString)
        => Parse(SplitQueryString(queryString));

    /// <summary>
    /// Parses a collection of key/value pairs — e.g.
    /// <c>Request.Query.SelectMany(kv =&gt; kv.Value, (kv, v) =&gt; new KeyValuePair&lt;string, string?&gt;(kv.Key, v))</c>
    /// in ASP.NET Core. A key may appear more than once.
    /// </summary>
    public static InanduGridRequest Parse(IEnumerable<KeyValuePair<string, string?>> pairs)
    {
        if (pairs is null)
        {
            throw new ArgumentNullException(nameof(pairs));
        }

        var request = new InanduGridRequest();

        foreach (var (key, rawValue) in pairs.Select(p => (p.Key, p.Value)))
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            var value = rawValue ?? string.Empty;

            if (KeyIs(key, "page"))
            {
                request.Page = ParseInt(value);
            }
            else if (KeyIs(key, "pageSize") || KeyIs(key, "take"))
            {
                request.PageSize = ParseInt(value);
            }
            else if (KeyIs(key, "offset") || KeyIs(key, "skip"))
            {
                request.Offset = ParseInt(value);
            }
            else if (KeyIs(key, "limit"))
            {
                request.Limit = ParseInt(value);
            }
            else if (KeyIs(key, "sort") || KeyIs(key, "orderby") || KeyIs(key, "order"))
            {
                foreach (var token in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var criterion = InanduGridSort.ParseToken(token);
                    if (criterion is not null)
                    {
                        request.Sort.Add(criterion);
                    }
                }
            }
            else if (KeyIs(key, "q") || KeyIs(key, "query") || KeyIs(key, "search") || KeyIs(key, "term"))
            {
                if (!string.IsNullOrEmpty(value))
                {
                    request.Query = value;
                }
            }
            else if (KeyIs(key, "filter") || KeyIs(key, "advancedFilter"))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    request.Filter = value;
                }
            }
            else if (KeyIs(key, "groupBy"))
            {
                foreach (var g in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = g.Trim();
                    if (name.Length > 0)
                    {
                        request.GroupBy.Add(name);
                    }
                }
            }
            else if (KeyIs(key, "groupKeys"))
            {
                foreach (var gk in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    request.GroupKeys.Add(gk.Trim());
                }
            }
            else if (!ReservedKeys.Contains(key))
            {
                var condition = ParseConditionKey(key, value);
                if (condition is not null)
                {
                    request.Conditions.Add(condition);
                }
            }
        }

        return request;

        static bool KeyIs(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses a <c>{field}_{op}</c> key (e.g. <c>price_gte</c>, <c>name_contains</c>, <c>status_in</c>).
    /// The operator is the segment after the last <c>'_'</c>; if it isn't a known operator token the
    /// key is ignored. <c>in</c> splits the value on commas.
    /// </summary>
    private static FilterCondition? ParseConditionKey(string key, string value)
    {
        var underscore = key.LastIndexOf('_');
        if (underscore <= 0 || underscore == key.Length - 1)
        {
            return null;
        }

        var op = FilterOperators.TryParse(key.Substring(underscore + 1));
        if (op is null)
        {
            return null;
        }

        var field = key.Substring(0, underscore);

        if (op == FilterOperator.In)
        {
            var items = value
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0)
                .ToList();
            return items.Count == 0 ? null : new FilterCondition(field, FilterOperator.In, items);
        }

        return new FilterCondition(field, op.Value, value);
    }

    private static int? ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static IEnumerable<KeyValuePair<string, string?>> SplitQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString))
        {
            yield break;
        }

        var start = queryString[0] == '?' ? 1 : 0;
        var span = queryString.Substring(start);

        foreach (var part in span.Split('&'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                yield return new KeyValuePair<string, string?>(Uri.UnescapeDataString(part), string.Empty);
            }
            else
            {
                var name = Uri.UnescapeDataString(part.Substring(0, eq));
                var value = Uri.UnescapeDataString(part.Substring(eq + 1).Replace('+', ' '));
                yield return new KeyValuePair<string, string?>(name, value);
            }
        }
    }
}
