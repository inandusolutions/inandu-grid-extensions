using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>
/// Turns <see cref="InanduGridSort"/> / <see cref="FilterCondition"/> / an
/// <see cref="AdvancedFilterGroup"/> into LINQ expression trees. Everything here composes over
/// <see cref="IQueryable{T}"/> so the same code path serves both an in-memory
/// <see cref="IEnumerable{T}"/> (via <c>Queryable.AsQueryable</c>) and EF Core (translated to SQL).
/// String operators are emitted as the plain <c>Contains</c>/<c>StartsWith</c>/<c>==</c> forms EF can
/// translate; case-insensitivity is done by lowering both operands, which also translates.
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

    // ── grouping ─────────────────────────────────────────────────────────

    /// <summary>
    /// Groups <paramref name="source"/> by <paramref name="field"/> and returns one page of
    /// <c>{ key, count }</c> groups plus the total group count. <paramref name="groupSort"/> — the
    /// request's first sort — orders by <c>count</c> when its field is <c>"count"</c>, otherwise by
    /// the group key; ascending unless the criterion is descending.
    /// </summary>
    public static (List<InanduGridGroup> Groups, int Total) GroupLevel<T>(
        IQueryable<T> source, string field, InanduGridOptions options, int page, int pageSize, InanduGridSort? groupSort)
    {
        var param = Expression.Parameter(typeof(T), "x");
        var resolved = PropertyResolver.Resolve(param, typeof(T), options.MapField(field))
            ?? throw new ArgumentException($"Unknown group field '{field}' on {typeof(T).Name}.", nameof(field));

        var (access, keyType) = resolved;
        var keySelector = Expression.Lambda(access, param);

        var core = typeof(ExpressionBuilder)
            .GetMethod(nameof(GroupLevelCore), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(T), keyType);

        return ((List<InanduGridGroup> Groups, int Total))core.Invoke(
            null,
            new object?[] { source, keySelector, field, page, pageSize, groupSort })!;
    }

    private static (List<InanduGridGroup> Groups, int Total) GroupLevelCore<T, TKey>(
        IQueryable<T> source, Expression<Func<T, TKey>> keySelector, string field, int page, int pageSize, InanduGridSort? groupSort)
    {
        var grouped = source.GroupBy(keySelector).Select(g => new GroupCount<TKey> { Key = g.Key, Count = g.Count() });

        var total = grouped.Count();

        var byCount = groupSort is not null && string.Equals(groupSort.Field, "count", StringComparison.OrdinalIgnoreCase);
        var desc = groupSort?.IsDescending == true;

        var ordered = (byCount, desc) switch
        {
            (true, true) => grouped.OrderByDescending(x => x.Count),
            (true, false) => grouped.OrderBy(x => x.Count),
            (false, true) => grouped.OrderByDescending(x => x.Key),
            _ => grouped.OrderBy(x => x.Key),
        };

        var skip = Math.Max(0, (page - 1)) * (long)pageSize;
        var pageRows = ordered.Skip((int)Math.Min(skip, int.MaxValue)).Take(pageSize).ToList();

        var groups = pageRows
            .Select(x => new InanduGridGroup { Field = field, Key = x.Key, Count = x.Count })
            .ToList();

        return (groups, total);
    }

    // ── flat filtering (FilterCondition list + free text) ─────────────────

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

    // ── advanced filter tree (nested AND / OR) ────────────────────────────

    public static Expression<Func<T, bool>>? BuildAdvancedPredicate<T>(AdvancedFilterGroup? root, InanduGridOptions options)
    {
        if (root is null || root.Children.Count == 0)
        {
            return null;
        }

        var param = Expression.Parameter(typeof(T), "x");
        var body = BuildAdvancedNode(root, param, typeof(T), options);
        return body is null ? null : Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static Expression? BuildAdvancedNode(AdvancedFilterNode node, ParameterExpression param, Type rootType, InanduGridOptions options)
    {
        if (node is AdvancedFilterGroup group)
        {
            Expression? body = null;
            var or = group.Combinator == AdvancedFilterCombinator.Or;
            foreach (var child in group.Children)
            {
                var childExpr = BuildAdvancedNode(child, param, rootType, options);
                if (childExpr is null)
                {
                    continue;
                }

                body = body is null
                    ? childExpr
                    : (or ? Expression.OrElse(body, childExpr) : Expression.AndAlso(body, childExpr));
            }

            return body;
        }

        var condition = (AdvancedFilterCondition)node;
        var resolved = PropertyResolver.Resolve(param, rootType, options.MapField(condition.Field));
        if (resolved is null)
        {
            if (options.ThrowOnUnknownField)
            {
                throw new ArgumentException($"Unknown filter field '{condition.Field}' on {rootType.Name}.", nameof(node));
            }

            return null;
        }

        var (member, memberType) = resolved.Value;
        var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
        var ignoreCase = IsIgnoreCase(options.StringComparison);
        var culture = options.Culture;

        switch (condition.Operator)
        {
            case AdvancedFilterOperator.NotContains:
            {
                var inner = BuildScalarOperator(member, memberType, underlying, FilterOperator.Contains, condition.Value, ignoreCase, culture);
                return inner is null ? null : Expression.Not(inner);
            }

            case AdvancedFilterOperator.Between:
            {
                var lo = BuildScalarOperator(member, memberType, underlying, FilterOperator.GreaterThanOrEqual, condition.Value, ignoreCase, culture);
                var hi = BuildScalarOperator(member, memberType, underlying, FilterOperator.LessThanOrEqual, condition.Value2, ignoreCase, culture);
                if (lo is null || hi is null)
                {
                    return lo ?? hi;
                }

                return Expression.AndAlso(lo, hi);
            }

            case AdvancedFilterOperator.IsTrue:
                return BuildBoolEquals(member, memberType, underlying, true);

            case AdvancedFilterOperator.IsFalse:
                return BuildBoolEquals(member, memberType, underlying, false);

            case AdvancedFilterOperator.IsEmpty:
                return BuildIsEmpty(member, memberType, underlying, negate: false);

            case AdvancedFilterOperator.IsNotEmpty:
                return BuildIsEmpty(member, memberType, underlying, negate: true);

            default:
                return BuildScalarOperator(member, memberType, underlying, ToFilterOperator(condition.Operator), condition.Value, ignoreCase, culture);
        }
    }

    // ── shared building blocks ───────────────────────────────────────────

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

        if (condition.Operator == FilterOperator.In)
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

        return BuildScalarOperator(member, memberType, underlying, condition.Operator, condition.Value, ignoreCase, options.Culture);
    }

    /// <summary>
    /// The per-operator expression for a single scalar comparison — shared by the flat
    /// <see cref="FilterCondition"/> path and the advanced-filter tree. Does not handle
    /// <see cref="FilterOperator.In"/> (list) — the caller does that.
    /// </summary>
    private static Expression? BuildScalarOperator(
        Expression member, Type memberType, Type underlying,
        FilterOperator op, object? rawValue, bool ignoreCase, IFormatProvider culture)
    {
        switch (op)
        {
            case FilterOperator.Contains:
            case FilterOperator.StartsWith:
            case FilterOperator.EndsWith:
            {
                if (underlying != typeof(string))
                {
                    return null;
                }

                var text = Convert.ToString(rawValue, culture);
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }

                var method = op switch
                {
                    FilterOperator.StartsWith => StringStartsWith,
                    FilterOperator.EndsWith => StringEndsWith,
                    _ => StringContains,
                };

                return StringOp(member, ignoreCase ? text!.ToLower() : text!, method, ignoreCase);
            }

            case FilterOperator.Equal:
            case FilterOperator.NotEqual:
            {
                if (!ValueCoercion.TryCoerce(rawValue, memberType, culture, out var coerced))
                {
                    return null;
                }

                var eq = BuildEquals(member, memberType, underlying, coerced, ignoreCase);
                return op == FilterOperator.NotEqual ? Expression.Not(eq) : eq;
            }

            case FilterOperator.GreaterThan:
            case FilterOperator.GreaterThanOrEqual:
            case FilterOperator.LessThan:
            case FilterOperator.LessThanOrEqual:
            {
                if (!ValueCoercion.TryCoerce(rawValue, memberType, culture, out var coerced))
                {
                    return null;
                }

                return BuildComparison(member, memberType, underlying, coerced, op, ignoreCase);
            }

            default:
                return null;
        }
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

    private static Expression? BuildBoolEquals(Expression member, Type memberType, Type underlying, bool value)
        => underlying != typeof(bool)
            ? null
            : Expression.Equal(member, TypedConstant(value, memberType, typeof(bool)), liftToNull: false, method: null);

    private static Expression BuildIsEmpty(Expression member, Type memberType, Type underlying, bool negate)
    {
        Expression empty;

        if (underlying == typeof(string))
        {
            var isNull = Expression.Equal(member, Expression.Constant(null, typeof(string)));
            var isBlank = Expression.Equal(NullToEmpty(member), Expression.Constant(string.Empty));
            empty = Expression.OrElse(isNull, isBlank);
        }
        else if (!memberType.IsValueType || Nullable.GetUnderlyingType(memberType) is not null)
        {
            empty = Expression.Equal(member, Expression.Constant(null, memberType), liftToNull: false, method: null);
        }
        else
        {
            empty = Expression.Constant(false);
        }

        return negate ? Expression.Not(empty) : empty;
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
        // Ordered comparison of enum / bool / Guid isn't emitted (ambiguous); use eq / in / numeric fields.
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

    private static FilterOperator ToFilterOperator(AdvancedFilterOperator op) => op switch
    {
        AdvancedFilterOperator.Eq => FilterOperator.Equal,
        AdvancedFilterOperator.Neq => FilterOperator.NotEqual,
        AdvancedFilterOperator.Contains => FilterOperator.Contains,
        AdvancedFilterOperator.StartsWith => FilterOperator.StartsWith,
        AdvancedFilterOperator.EndsWith => FilterOperator.EndsWith,
        AdvancedFilterOperator.Gt => FilterOperator.GreaterThan,
        AdvancedFilterOperator.Gte => FilterOperator.GreaterThanOrEqual,
        AdvancedFilterOperator.Lt => FilterOperator.LessThan,
        AdvancedFilterOperator.Lte => FilterOperator.LessThanOrEqual,
        _ => FilterOperator.Equal,
    };
}
