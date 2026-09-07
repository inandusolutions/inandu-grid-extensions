namespace Inandu.Grid.Extensions;

/// <summary>
/// The comparison operators the grid's column filters and REST <c>{field}_{op}={value}</c> params
/// map to. The names match <c>@inandu-solutions/grid-pro</c>'s <c>FilterOp</c>.
/// </summary>
public enum FilterOperator
{
    /// <summary><c>eq</c> — equals.</summary>
    Equal = 0,

    /// <summary><c>neq</c> — not equals.</summary>
    NotEqual = 1,

    /// <summary><c>contains</c> — substring match (strings).</summary>
    Contains = 2,

    /// <summary><c>startsWith</c> — prefix match (strings).</summary>
    StartsWith = 3,

    /// <summary><c>endsWith</c> — suffix match (strings).</summary>
    EndsWith = 4,

    /// <summary><c>gt</c> — greater than.</summary>
    GreaterThan = 5,

    /// <summary><c>gte</c> — greater than or equal.</summary>
    GreaterThanOrEqual = 6,

    /// <summary><c>lt</c> — less than.</summary>
    LessThan = 7,

    /// <summary><c>lte</c> — less than or equal.</summary>
    LessThanOrEqual = 8,

    /// <summary><c>in</c> — value is one of a comma-separated list.</summary>
    In = 9,
}

/// <summary>Parsing / formatting helpers for <see cref="FilterOperator"/>.</summary>
public static class FilterOperators
{
    /// <summary>The wire token for an operator (<c>gte</c>, <c>contains</c>, …).</summary>
    public static string ToToken(this FilterOperator op) => op switch
    {
        FilterOperator.Equal => "eq",
        FilterOperator.NotEqual => "neq",
        FilterOperator.Contains => "contains",
        FilterOperator.StartsWith => "startsWith",
        FilterOperator.EndsWith => "endsWith",
        FilterOperator.GreaterThan => "gt",
        FilterOperator.GreaterThanOrEqual => "gte",
        FilterOperator.LessThan => "lt",
        FilterOperator.LessThanOrEqual => "lte",
        FilterOperator.In => "in",
        _ => op.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Parses an operator token (case-insensitive; also accepts a few aliases like <c>ne</c>,
    /// <c>ge</c>, <c>le</c>). Returns <c>null</c> when the token is not a known operator.
    /// </summary>
    public static FilterOperator? TryParse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return token.Trim().ToLowerInvariant() switch
        {
            "eq" or "equals" or "==" => FilterOperator.Equal,
            "neq" or "ne" or "not" or "!=" => FilterOperator.NotEqual,
            "contains" or "like" => FilterOperator.Contains,
            "startswith" or "starts" => FilterOperator.StartsWith,
            "endswith" or "ends" => FilterOperator.EndsWith,
            "gt" => FilterOperator.GreaterThan,
            "gte" or "ge" => FilterOperator.GreaterThanOrEqual,
            "lt" => FilterOperator.LessThan,
            "lte" or "le" => FilterOperator.LessThanOrEqual,
            "in" => FilterOperator.In,
            _ => null,
        };
    }
}
