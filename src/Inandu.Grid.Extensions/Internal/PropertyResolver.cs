using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>
/// Resolves a grid <c>field</c> (optionally a dotted path like <c>"customer.name"</c>) to a
/// member-access <see cref="Expression"/> off a parameter, case-insensitively. Intermediate nulls in
/// a nested path short-circuit to <c>default</c> so both in-memory and EF Core evaluation are safe.
/// </summary>
internal static class PropertyResolver
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo[]?> Cache = new();

    /// <summary>
    /// Builds a null-safe member access for <paramref name="path"/> off <paramref name="parameter"/>.
    /// Returns <c>null</c> when the path can't be resolved on <paramref name="rootType"/>.
    /// </summary>
    public static (Expression Access, Type Type)? Resolve(ParameterExpression parameter, Type rootType, string path)
    {
        var chain = ResolveChain(rootType, path);
        if (chain is null || chain.Length == 0)
        {
            return null;
        }

        Expression access = parameter;
        Expression? guard = null;

        for (var i = 0; i < chain.Length; i++)
        {
            var member = chain[i];

            if (i > 0 && IsNullable(access.Type))
            {
                var isNotNull = Expression.NotEqual(access, Expression.Constant(null, access.Type));
                guard = guard is null ? isNotNull : Expression.AndAlso(guard, isNotNull);
            }

            access = Expression.Property(access, member);
        }

        if (guard is not null)
        {
            var fallback = Expression.Default(access.Type);
            access = Expression.Condition(guard, access, fallback);
        }

        return (access, access.Type);
    }

    /// <summary>Whether <paramref name="rootType"/> exposes <paramref name="path"/> (used by <see cref="InanduGridOptions.ThrowOnUnknownField"/>).</summary>
    public static bool Exists(Type rootType, string path) => ResolveChain(rootType, path) is not null;

    private static PropertyInfo[]? ResolveChain(Type rootType, string path)
        => Cache.GetOrAdd((rootType, path), static key =>
        {
            var (type, raw) = key;
            var segments = raw.Split('.');
            var chain = new PropertyInfo[segments.Length];
            var current = type;

            for (var i = 0; i < segments.Length; i++)
            {
                var name = segments[i].Trim();
                if (name.Length == 0)
                {
                    return null;
                }

                var prop = current.GetProperty(
                    name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase | BindingFlags.FlattenHierarchy);

                if (prop is null)
                {
                    return null;
                }

                chain[i] = prop;
                current = prop.PropertyType;
            }

            return chain;
        });

    private static bool IsNullable(Type type)
        => !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
}
