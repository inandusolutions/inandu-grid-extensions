using System;
using System.Text.Json.Serialization;
using Inandu.Grid.Extensions.Json;

namespace Inandu.Grid.Extensions;

/// <summary>An aggregate function for the totals block.</summary>
public enum AggregateFunction
{
    /// <summary><c>sum</c></summary>
    Sum = 0,

    /// <summary><c>avg</c> / <c>average</c></summary>
    Average = 1,

    /// <summary><c>min</c></summary>
    Min = 2,

    /// <summary><c>max</c></summary>
    Max = 3,

    /// <summary><c>count</c> — of rows where the field is non-null, or all rows for <c>count:*</c>.</summary>
    Count = 4,
}

/// <summary>One entry of the <c>aggregate</c> param — e.g. <c>sum:amount</c>, <c>count:*</c>.</summary>
[JsonConverter(typeof(InanduGridAggregateJsonConverter))]
public sealed class InanduGridAggregate
{
    /// <summary>Creates an aggregate spec.</summary>
    public InanduGridAggregate(AggregateFunction function, string field)
    {
        Function = function;
        Field = field ?? throw new ArgumentNullException(nameof(field));
    }

    /// <summary>The function to apply.</summary>
    public AggregateFunction Function { get; }

    /// <summary>The field to aggregate. <c>"*"</c> is only meaningful with <see cref="AggregateFunction.Count"/>.</summary>
    public string Field { get; }

    /// <summary>The key this aggregate appears under in <see cref="InanduGridResult{T}.Aggregations"/> — <c>"sum:amount"</c>.</summary>
    public string Key => $"{Token(Function)}:{Field}";

    /// <summary>Parses a token like <c>"sum:amount"</c> / <c>"count:*"</c>. Returns <c>null</c> for an unknown function or a blank field.</summary>
    public static InanduGridAggregate? Parse(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var colon = token.IndexOf(':');
        if (colon <= 0 || colon == token.Length - 1)
        {
            return null;
        }

        var fn = ParseFunction(token[..colon]);
        if (fn is null)
        {
            return null;
        }

        var field = token[(colon + 1)..].Trim();
        return field.Length == 0 ? null : new InanduGridAggregate(fn.Value, field);
    }

    private static AggregateFunction? ParseFunction(string token) => token.Trim().ToLowerInvariant() switch
    {
        "sum" => AggregateFunction.Sum,
        "avg" or "average" or "mean" => AggregateFunction.Average,
        "min" => AggregateFunction.Min,
        "max" => AggregateFunction.Max,
        "count" => AggregateFunction.Count,
        _ => null,
    };

    private static string Token(AggregateFunction fn) => fn switch
    {
        AggregateFunction.Sum => "sum",
        AggregateFunction.Average => "avg",
        AggregateFunction.Min => "min",
        AggregateFunction.Max => "max",
        AggregateFunction.Count => "count",
        _ => fn.ToString().ToLowerInvariant(),
    };
}
