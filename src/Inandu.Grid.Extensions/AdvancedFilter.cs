using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Inandu.Grid.Extensions;

/// <summary>How a group's children combine.</summary>
public enum AdvancedFilterCombinator
{
    /// <summary>All children must match.</summary>
    And = 0,

    /// <summary>At least one child must match.</summary>
    Or = 1,
}

/// <summary>
/// The operators the advanced filter tree supports — a superset of <see cref="FilterOperator"/>
/// (adds <c>notContains</c>, <c>between</c>, <c>isTrue/isFalse</c>, <c>isEmpty/isNotEmpty</c>).
/// Names match <c>@inandu-solutions/grid-pro</c>'s <c>AdvancedFilterOperator</c>.
/// </summary>
public enum AdvancedFilterOperator
{
    /// <summary><c>eq</c></summary>
    Eq = 0,

    /// <summary><c>neq</c></summary>
    Neq = 1,

    /// <summary><c>contains</c> (strings)</summary>
    Contains = 2,

    /// <summary><c>notContains</c> (strings)</summary>
    NotContains = 3,

    /// <summary><c>startsWith</c> (strings)</summary>
    StartsWith = 4,

    /// <summary><c>endsWith</c> (strings)</summary>
    EndsWith = 5,

    /// <summary><c>gt</c></summary>
    Gt = 6,

    /// <summary><c>gte</c></summary>
    Gte = 7,

    /// <summary><c>lt</c></summary>
    Lt = 8,

    /// <summary><c>lte</c></summary>
    Lte = 9,

    /// <summary><c>between</c> — <c>value</c> ≤ x ≤ <c>value2</c>.</summary>
    Between = 10,

    /// <summary><c>isTrue</c> (boolean columns)</summary>
    IsTrue = 11,

    /// <summary><c>isFalse</c> (boolean columns)</summary>
    IsFalse = 12,

    /// <summary><c>isEmpty</c> — null, or empty string.</summary>
    IsEmpty = 13,

    /// <summary><c>isNotEmpty</c></summary>
    IsNotEmpty = 14,
}

/// <summary>A node of the advanced filter tree — either an <see cref="AdvancedFilterGroup"/> or an <see cref="AdvancedFilterCondition"/>.</summary>
public abstract class AdvancedFilterNode
{
}

/// <summary>A group of child nodes combined with AND or OR.</summary>
public sealed class AdvancedFilterGroup : AdvancedFilterNode
{
    /// <summary>How <see cref="Children"/> combine.</summary>
    public AdvancedFilterCombinator Combinator { get; set; } = AdvancedFilterCombinator.And;

    /// <summary>Nested groups and/or leaf conditions.</summary>
    public IList<AdvancedFilterNode> Children { get; set; } = new List<AdvancedFilterNode>();
}

/// <summary>A leaf <c>field · operator · value</c> comparison.</summary>
public sealed class AdvancedFilterCondition : AdvancedFilterNode
{
    /// <summary>The grid column's <c>field</c> (may be a dotted path).</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>The comparison to apply.</summary>
    public AdvancedFilterOperator Operator { get; set; }

    /// <summary>Primary operand (usually a <see cref="string"/>; coerced to the column's CLR type). Unused by the unary operators.</summary>
    public object? Value { get; set; }

    /// <summary>Upper bound for <see cref="AdvancedFilterOperator.Between"/>.</summary>
    public object? Value2 { get; set; }
}

/// <summary>Parses the JSON tree <c>@inandu-solutions/grid-pro</c>'s <c>advancedQueryToRestParams</c> emits.</summary>
public static class AdvancedFilterJson
{
    /// <summary>
    /// Parses <paramref name="json"/> — the value of the <c>filter</c> query param, shaped
    /// <c>{ "kind": "group", "combinator": "and", "children": [ { "kind": "condition", "field": …,
    /// "operator": …, "value": …, "value2": … } | { "kind": "group", … } ] }</c>. Returns <c>null</c>
    /// for blank / malformed input (so a bad param is a no-op, not a 400).
    /// </summary>
    public static AdvancedFilterGroup? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var node = ParseNode(doc.RootElement);
            return node as AdvancedFilterGroup
                   ?? (node is AdvancedFilterCondition c ? new AdvancedFilterGroup { Children = { c } } : null);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AdvancedFilterNode? ParseNode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var kind = element.TryGetProperty("kind", out var k) ? k.GetString() : null;

        if (string.Equals(kind, "condition", StringComparison.OrdinalIgnoreCase))
        {
            var field = element.TryGetProperty("field", out var f) ? f.GetString() : null;
            if (string.IsNullOrWhiteSpace(field))
            {
                return null;
            }

            var op = ParseOperator(element.TryGetProperty("operator", out var o) ? o.GetString() : null);
            if (op is null)
            {
                return null;
            }

            return new AdvancedFilterCondition
            {
                Field = field!,
                Operator = op.Value,
                Value = element.TryGetProperty("value", out var v) ? ScalarValue(v) : null,
                Value2 = element.TryGetProperty("value2", out var v2) ? ScalarValue(v2) : null,
            };
        }

        // default: treat as a group (grid-pro always tags "group", but be lenient)
        var group = new AdvancedFilterGroup
        {
            Combinator = string.Equals(
                element.TryGetProperty("combinator", out var comb) ? comb.GetString() : "and",
                "or",
                StringComparison.OrdinalIgnoreCase)
                ? AdvancedFilterCombinator.Or
                : AdvancedFilterCombinator.And,
        };

        if (element.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                var parsed = ParseNode(child);
                if (parsed is not null)
                {
                    group.Children.Add(parsed);
                }
            }
        }

        return group;
    }

    private static AdvancedFilterOperator? ParseOperator(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "eq" => AdvancedFilterOperator.Eq,
        "neq" or "ne" => AdvancedFilterOperator.Neq,
        "contains" => AdvancedFilterOperator.Contains,
        "notcontains" => AdvancedFilterOperator.NotContains,
        "startswith" => AdvancedFilterOperator.StartsWith,
        "endswith" => AdvancedFilterOperator.EndsWith,
        "gt" => AdvancedFilterOperator.Gt,
        "gte" or "ge" => AdvancedFilterOperator.Gte,
        "lt" => AdvancedFilterOperator.Lt,
        "lte" or "le" => AdvancedFilterOperator.Lte,
        "between" => AdvancedFilterOperator.Between,
        "istrue" => AdvancedFilterOperator.IsTrue,
        "isfalse" => AdvancedFilterOperator.IsFalse,
        "isempty" => AdvancedFilterOperator.IsEmpty,
        "isnotempty" => AdvancedFilterOperator.IsNotEmpty,
        _ => null,
    };

    /// <summary>A JSON scalar as a string operand (numbers / bools become their invariant text); coercion happens later.</summary>
    private static object? ScalarValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => element.GetRawText(),
    };

    /// <summary>Formatting counterpart of <see cref="ScalarValue"/> — used by tests / diagnostics.</summary>
    internal static string FormatScalar(object? value) => value switch
    {
        null => "null",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
