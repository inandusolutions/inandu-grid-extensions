using System;

namespace Inandu.Grid.Extensions;

/// <summary>
/// One normalized <c>field · operator · value</c> predicate. A column filter
/// (<see cref="InanduGridColumnFilter"/>) expands to zero or more of these; the REST
/// <c>{field}_{op}={value}</c> params bind straight to one.
/// </summary>
public sealed class FilterCondition
{
    /// <summary>Creates a condition.</summary>
    public FilterCondition(string field, FilterOperator @operator, object? value)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Operator = @operator;
        Value = value;
    }

    /// <summary>The grid column's <c>field</c> (may be a dotted path).</summary>
    public string Field { get; }

    /// <summary>The comparison to apply.</summary>
    public FilterOperator Operator { get; }

    /// <summary>
    /// The raw operand. For <see cref="FilterOperator.In"/> this is an <see cref="System.Collections.IEnumerable"/>
    /// (typically <c>string[]</c>); otherwise a single value, usually a <see cref="string"/> that is
    /// coerced to the target property type at build time.
    /// </summary>
    public object? Value { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Field} {Operator.ToToken()} {Value}";
}
