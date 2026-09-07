using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace InanduGrid.ServerSide.Internal;

/// <summary>
/// Turns <see cref="InanduGridSort"/> / <see cref="FilterCondition"/> into LINQ expression trees.
/// Everything here composes over <see cref="IQueryable{T}"/> so the same code path serves both an
/// in-memory <see cref="IEnumerable{T}"/> (via <see cref="Queryable.AsQueryable(IQueryable)"/>) and
/// EF Core (translated to SQL). String operators are emitted as the plain
/// <c>Contains</c>/<c>StartsWith</c>/<c>==</c> forms EF can translate; case-insensitivity is done by
/// lowering both operands, which also translates.
/// </summary>
internal static class ExpressionBuilder
{
    private static readonly MethodInfo StringContains =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;

    private static readonly MethodInfo StringStartsWith =
        typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;

    private static readonly MethodInfo StringEndsWith =
        typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

    private static readonly MethodInfo StringToLower =
        typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;

    private static readonly MethodInfo StringCompare =
        typeof(string).GetMethod(nameof(string.Compare), new[] { typeof(string), typeof(string) })!;

    // ── sorting ────────────────────────────────────────────────────────────

    public static IQueryable<T> ApplySort<T>(IQueryable<T> source, IEnumerable<InanduGridSort> sorts, InanduGridOptions options)
    {
        var applied = false;

        foreach (var sort in sorts)
        {
            if (sort is null || string.IsNullOrWhiteSpace(sort.Field))
            {
                continue;
            }

            var param = Expression.Parameter(typeof(T), "x");
            var resolved = PropertyResolver.Resolve(param, typeof(T), options.MapField(sort.Field));
            if (resolved is null)
            {
                if (options.ThrowOnUnknownField)
                {
                    throw new ArgumentException($"Unknown sort field '{sort.Field}' on {typeof(T).Name}.", nameof(sorts));
                }

                continue;
            }

            var (access, keyType) = resolved.Value;
            var keySelector = Expression.Lambda(access, param);
            var method = (applied, sort.IsDescending) switch
            {
                (false, false) => nameof(Queryable.OrderBy),
                (false, true) => nameof(Queryable.OrderByDescending),
                (true, false) => nameof(Queryable.ThenBy),
                (true, true) => nameof(Queryable.ThenByDescending),
            };

            var call = Expression.Call(
                typeof(Queryable),
                method,
                new[] { typeof(T), keyType },
                source.Expression,
                Expression.Quote(keySelector));

            source = source.Provider.CreateQuery<T>(call);
            applied = true;
        }

        return source;
    }

    // ── filtering ──────────────────────────────────────────────────────────

    public static Expression<Func<T, bool>>? BuildPredicate<T>(
        IEnumerable<FilterCondition> conditions,
        string? freeText,
        InanduGridOptions options)
    {
        var param = Expression.Parameter(typeof(T), "x");
        Expression? body = null;

        foreach (var condition in conditions)
        {
            var expr = BuildCondition(param, typeof(T), condition, options);
            if (expr is not null)
            {
                body = body is null ? expr : Expression.AndAlso(body, expr);
            }
        }

        var search = BuildFreeText(param, typeof(T), freeText, options);
        if (search is not null)
        {
            body = body is null ? search : Expression.AndAlso(body, search);
        }

        return body is null ? null : Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static Expression? BuildFreeText(ParameterExpression param, Type rootType, string? freeText, InanduGridOptions options)
    {
        if (string.IsNullOrWhiteSpace(freeText))
        {
            return null;
        }

        var fields = options.SearchableFields ?? DefaultSearchableFields(rootType);
        if (fields.Count == 0)
        {
            return null;
        }

        var ignoreCase = IsIgnoreCase(options.StringComparison);
        var needle = ignoreCase ? freeText.ToLower() : freeText;
        Expression? body = null;

        foreach (var field in fields)
        {
            var resolved = PropertyResolver.Resolve(param, rootType, options.MapField(field));
            if (resolved is null)
            {
                continue;
            }

            var (access, type) = resolved.Value;
            if ((Nullable.GetUnderlyingType(type) ?? type) != typeof(string))
            {
                continue;
            }

            var contains = StringOp(access, needle, StringContains, ignoreCase);
            body = body is null ? contains : Expression.OrElse(body, contains);
        }

        return body;
    }

    private static Expression? BuildCondition(ParameterExpression param, Type rootType, FilterCondition condition, InanduGridOptions options)
    {
        var resolved = PropertyResolver.Resolve(param, rootType, options.MapField(condition.Field));
        if (resolved is null)
        {
            if (options.ThrowOnUnknownField)
            {
                throw new ArgumentException($"Unknown filter field '{condition.Field}' on {rootType.Name}.", nameof(condition));
            }

            return null;
        }

        var (member, memberType) = resolved.Value;
        var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
        var ignoreCase = IsIgnoreCase(options.StringComparison);

        switch (condition.Operator)
        {
            case FilterOperator.Contains:
            case FilterOperator.StartsWith:
            case FilterOperator.EndsWith:
            {
                if (underlying != typeof(string))
                {
                    return null;
                }

                var text = Convert.ToString(condition.Value, options.Culture);
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }

                var method = condition.Operator switch
                {
                    FilterOperator.StartsWith => StringStartsWith,
                    FilterOperator.EndsWith => StringEndsWith,
                    _ => StringContains,
                };

                return StringOp(member, ignoreCase ? text!.ToLower() : text!, method, ignoreCase);
            }

            case FilterOperator.In:
            {
                var items = condition.Value switch
                {
                    null => Enumerable.Empty<object?>(),
                    string s => s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Cast<object?>(),
                    IEnumerable e => e.Cast<object?>(),
                    var v => new object?[] { v },
                };
                Expression? body = null;

                foreach (var item in items)
                {
                    if (!ValueCoercion.TryCoerce(item, memberType, options.Culture, out var coerced))
                    {
                        continue;
                    }

                    var eq = BuildEquals(member, memberType, underlying, coerced, ignoreCase);
                    body = body is null ? eq : Expression.OrElse(body, eq);
                }

                return body;
            }

            case FilterOperator.Equal:
            case FilterOperator.NotEqual:
            {
                if (!ValueCoercion.TryCoerce(condition.Value, memberType, options.Culture, out var coerced))
                {
                    return null;
                }

                var eq = BuildEquals(member, memberType, underlying, coerced, ignoreCase);
                return condition.Operator == FilterOperator.NotEqual ? Expression.Not(eq) : eq;
            }

            case FilterOperator.GreaterThan:
            case FilterOperator.GreaterThanOrEqual:
            case FilterOperator.LessThan:
            case FilterOperator.LessThanOrEqual:
            {
                if (!ValueCoercion.TryCoerce(condition.Value, memberType, options.Culture, out var coerced))
                {
                    return null;
                }

                return BuildComparison(member, memberType, underlying, coerced, condition.Operator, ignoreCase);
            }

            default:
                return null;
        }
    }

    private static Expression BuildEquals(Expression member, Type memberType, Type underlying, object? value, bool ignoreCase)
    {
        if (underlying == typeof(string) && ignoreCase)
        {
            var left = Expression.Call(NullToEmpty(member), StringToLower);
            var right = Expression.Constant(((string?)value ?? string.Empty).ToLower());
            return Expression.Equal(left, right, liftToNull: false, method: null);
        }

        return Expression.Equal(member, TypedConstant(value, memberType, underlying), liftToNull: false, method: null);
    }

    /// <summary>A constant of <paramref name="memberType"/>, going through the underlying type so
    /// <see cref="Expression.Constant(object, Type)"/> doesn't reject a boxed non-nullable value for a
    /// <see cref="Nullable{T}"/> member.</summary>
    private static Expression TypedConstant(object? value, Type memberType, Type underlying)
    {
        if (value is null)
        {
            return Expression.Constant(null, memberType);
        }

        Expression constant = Expression.Constant(value, underlying);
        return memberType == underlying ? constant : Expression.Convert(constant, memberType);
    }

    private static Expression? BuildComparison(Expression member, Type memberType, Type underlying, object? value, FilterOperator op, bool ignoreCase)
    {
        // Ordered comparison of enum members isn't emitted (rare, and ambiguous); use eq / in / numeric fields instead.
        if (underlying.IsEnum || underlying == typeof(bool) || underlying == typeof(Guid))
        {
            return null;
        }

        Expression left;
        Expression right;

        if (underlying == typeof(string))
        {
            var l = ignoreCase ? Expression.Call(NullToEmpty(member), StringToLower) : NullToEmpty(member);
            var r = Expression.Constant(ignoreCase ? ((string?)value ?? string.Empty).ToLower() : (string?)value ?? string.Empty);
            left = Expression.Call(StringCompare, l, r);
            right = Expression.Constant(0);
        }
        else
        {
            left = member;
            right = TypedConstant(value, memberType, underlying);
        }

        return op switch
        {
            FilterOperator.GreaterThan => Expression.GreaterThan(left, right, liftToNull: false, method: null),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(left, right, liftToNull: false, method: null),
            FilterOperator.LessThan => Expression.LessThan(left, right, liftToNull: false, method: null),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(left, right, liftToNull: false, method: null),
            _ => throw new InvalidOperationException(),
        };
    }

    private static Expression StringOp(Expression member, string needle, MethodInfo method, bool ignoreCase)
    {
        var safe = NullToEmpty(member);
        var target = ignoreCase ? Expression.Call(safe, StringToLower) : safe;
        return Expression.Call(target, method, Expression.Constant(needle));
    }

    /// <summary><c>member ?? ""</c> so string ops never NRE on a null column.</summary>
    private static Expression NullToEmpty(Expression member)
        => member.Type == typeof(string)
            ? Expression.Coalesce(member, Expression.Constant(string.Empty))
            : member;

    private static IList<string> DefaultSearchableFields(Type rootType)
        => rootType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
            .Select(p => p.Name)
            .ToList();

    private static bool IsIgnoreCase(StringComparison comparison)
        => comparison is StringComparison.OrdinalIgnoreCase
            or StringComparison.InvariantCultureIgnoreCase
            or StringComparison.CurrentCultureIgnoreCase;
}
