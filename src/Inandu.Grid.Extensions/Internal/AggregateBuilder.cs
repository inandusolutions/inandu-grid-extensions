using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>One planned aggregate — enough for the sync path here and the async path in the EF package.</summary>
internal readonly struct AggregatePlan
{
    public AggregatePlan(InanduGridAggregate spec, LambdaExpression? selector, Type? selectorType)
    {
        Spec = spec;
        Selector = selector;
        SelectorType = selectorType;
    }

    public InanduGridAggregate Spec { get; }

    /// <summary>Selector lambda: <c>Func&lt;T, TNum&gt;</c> for sum/avg (native member type, or promoted),
    /// <c>Func&lt;T, TMember&gt;</c> for min/max, <c>Func&lt;T, bool&gt;</c> for a scoped count. <c>null</c> for <c>count:*</c>.</summary>
    public LambdaExpression? Selector { get; }

    /// <summary>The selector's return type.</summary>
    public Type? SelectorType { get; }
}

internal static class AggregateBuilder
{
    public static List<AggregatePlan> Plan<T>(IEnumerable<InanduGridAggregate> aggregates, InanduGridOptions options)
    {
        var plans = new List<AggregatePlan>();

        foreach (var agg in aggregates)
        {
            if (agg.Function == AggregateFunction.Count && agg.Field == "*")
            {
                plans.Add(new AggregatePlan(agg, null, null));
                continue;
            }

            var param = Expression.Parameter(typeof(T), "x");
            var resolved = PropertyResolver.Resolve(param, typeof(T), options.MapField(agg.Field));
            if (resolved is null)
            {
                if (options.ThrowOnUnknownField)
                {
                    throw InanduGridRequestException.Single($"aggregate.{agg.Field}", $"Unknown aggregate field '{agg.Field}' on {typeof(T).Name}.");
                }

                continue;
            }

            var (member, memberType) = resolved.Value;
            var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
            var nullable = memberType != underlying || !memberType.IsValueType;

            switch (agg.Function)
            {
                case AggregateFunction.Count:
                {
                    Expression body = memberType.IsValueType && Nullable.GetUnderlyingType(memberType) is null
                        ? Expression.Constant(true)
                        : Expression.NotEqual(member, Expression.Constant(null, memberType));
                    plans.Add(new AggregatePlan(agg, Expression.Lambda(body, param), typeof(bool)));
                    break;
                }

                case AggregateFunction.Sum:
                case AggregateFunction.Average:
                {
                    var numeric = SumSelectorType(underlying);
                    if (numeric is null)
                    {
                        continue; // not summable
                    }

                    var selectorType = nullable && numeric.IsValueType ? typeof(Nullable<>).MakeGenericType(numeric) : numeric;
                    Expression body = selectorType == memberType ? member : Expression.Convert(member, selectorType);
                    plans.Add(new AggregatePlan(agg, Expression.Lambda(body, param), selectorType));
                    break;
                }

                case AggregateFunction.Min:
                case AggregateFunction.Max:
                {
                    plans.Add(new AggregatePlan(agg, Expression.Lambda(member, param), memberType));
                    break;
                }
            }
        }

        return plans;
    }

    public static Dictionary<string, object?> Compute<T>(IQueryable<T> filtered, List<AggregatePlan> plans)
    {
        var result = new Dictionary<string, object?>(plans.Count);
        foreach (var plan in plans)
        {
            result[plan.Spec.Key] = Execute(filtered, plan);
        }

        return result;
    }

    private static object? Execute<T>(IQueryable<T> filtered, AggregatePlan plan)
    {
        switch (plan.Spec.Function)
        {
            case AggregateFunction.Count when plan.Selector is null:
                return filtered.Count();

            case AggregateFunction.Count:
                return filtered.Count((Expression<Func<T, bool>>)plan.Selector!);

            case AggregateFunction.Sum:
                return InvokeSelector<T>(nameof(Queryable.Sum), plan);

            case AggregateFunction.Average:
                return filtered.Any() ? InvokeSelector<T>(nameof(Queryable.Average), plan) : null;

            case AggregateFunction.Min:
            case AggregateFunction.Max:
                return InvokeSelector<T>(plan.Spec.Function == AggregateFunction.Min ? nameof(Queryable.Min) : nameof(Queryable.Max), plan);

            default:
                return null;

            object? InvokeSelector<TSource>(string name, AggregatePlan p)
            {
                var isMinMax = name is nameof(Queryable.Min) or nameof(Queryable.Max);
                var method = typeof(Queryable).GetMethods()
                    .First(m => m.Name == name
                                && m.IsGenericMethodDefinition
                                && m.GetParameters() is { Length: 2 } ps
                                && ps[1].ParameterType.IsGenericType
                                && ps[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)
                                && (isMinMax
                                    ? m.GetGenericArguments().Length == 2
                                    : m.GetGenericArguments().Length == 1
                                      && ps[1].ParameterType.GetGenericArguments()[0].GetGenericArguments()[1] == p.SelectorType));

                var generic = isMinMax ? method.MakeGenericMethod(typeof(TSource), p.SelectorType!) : method.MakeGenericMethod(typeof(TSource));
                return generic.Invoke(null, new object?[] { filtered, p.Selector });
            }
        }
    }

    private static Type? SumSelectorType(Type underlying)
    {
        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal)
            || underlying == typeof(double) || underlying == typeof(float))
        {
            return underlying;
        }

        if (underlying == typeof(short) || underlying == typeof(byte) || underlying == typeof(sbyte) || underlying == typeof(uint))
        {
            return typeof(long); // promote small / unsigned to a supported Sum type
        }

        if (underlying == typeof(ulong) || underlying == typeof(ushort))
        {
            return typeof(double);
        }

        return null;
    }
}
